#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge
{
    // Orchestrates a full VRChat -> ChilloutVR avatar conversion. Each pass reads shared
    // state from the BridgeContext. The order matters and lives in ContentPasses, where each
    // pass says why it sits where it does.
    public static class BridgeConverter
    {
        public static BridgeReport Convert(VRCAvatarDescriptor descriptor, BridgeSettings settings)
        {
            // Converting to YAPS implies stripping what it replaces. The
            // window says so; a caller with the pair the other way round
            // (older saved settings, a script) gets the same answer.
            if (settings.convertYapsSystems && !settings.stripSpsSystems)
            {
                settings.stripSpsSystems = true;
            }
#if !AVATARBRIDGE_YAPS
            // No add-on installed, so there is nothing to rebuild the
            // penetration into and the choice collapses to removing it.
            // Saved settings from a project that had the add-on say convert;
            // honouring that would strip the system and build no replacement.
            settings.convertYapsSystems = false;
            settings.stripSpsSystems = true;
#endif
            var report = new BridgeReport();
            var ctx = new BridgeContext
            {
                Settings = settings,
                Report = report,
                SourceDescriptor = descriptor
            };

            AnimatorMerger.ResetMaskCache();
            // Per conversion, not per session: names must be stable run-over-run so reconverting
            // replaces the previous output instead of parking a numbered copy beside it, while
            // still colliding within one run, where two source controllers really can each bring
            // their own "Angry".
            OutputAssetPaths.Reset();

            // Format numbers the same way for everybody. The report is
            // read on other machines, and comma decimals paste badly.
            // Set once here so later additions are covered.
            var previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.InvariantCulture;

            try
            {
                // Before anything is built or written, the output folder included: if the project
                // has a baker installed that did not compile, every component it owns reads as
                // absent and the conversion comes out quietly gutted. Stop here rather than spend
                // the run producing it.
                if (!BridgePreflight.Check(ctx))
                {
                    report.Error("Conversion", "Stopped before converting",
                        "The problem above would have made this conversion silently wrong rather " +
                        "than visibly broken, which is worse. Nothing was changed.");
                    return report;
                }
                // Setup's folder rules: the Toolkit finds a conversion's records only by landing
                // on the same folder.
                CvrSetup.PrepareOutputFolder(ctx, ctx.SourceDescriptor.gameObject, "Conversion");
                PrepareTarget(ctx);
                // Before any pass renames or deletes an object.
                ctx.SnapshotSourcePaths();
                WarnMissingScripts(ctx);

                BridgePipeline.Execute(ctx, ContentPasses());

                // Always. CVR deletes them on load, the CCK complains,
                // and a lingering VRC descriptor reads as unconverted.
                MiscConverter.DeleteVrcComponents(ctx);

                // Deactivate the original whenever the work happened on
                // a separate object (clone or baked copy).
                if (ctx.Target != descriptor.gameObject)
                {
                    // Recorded, or Undo removes the copy and leaves the original hidden.
                    Undo.RecordObject(descriptor.gameObject, "AvatarBridge conversion");
                    descriptor.gameObject.SetActive(false);
                }

                // Belt and braces. AnimatorMerger refuses the crashing
                // assignment itself; this nets later passes.
                DetachCrashingController(ctx);
                WarnFastPlayMode(ctx);
                ReportSyncUsage(ctx);
                SaveConvertedPrefab(ctx);
                // Last, so it validates and describes the avatar as it will actually ship.
                BridgeFinish.Run(ctx, "ConversionReport.md", "Report");
                RebindAnimators(ctx);
                Selection.activeGameObject = ctx.Target;
                // The window resolves report subjects against this to offer "Show". Selection
                // would have done at a pinch, but it is whatever the user clicked last by the
                // time they read the report.
                report.ConvertedRoot = ctx.Target;

                report.Converted("Conversion", "Finished",
                    $"\"{ctx.Target.name}\" is ready for the CCK upload checks.");
            }
            catch (Exception e)
            {
                report.Error("Conversion", "Unhandled exception", e.Message);
                Debug.LogException(e);
                WriteFailureReport(ctx, descriptor);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture;
#if AVATARBRIDGE_MAGICA
                // The cloth writer's mesh caches; a run that throws before the
                // helper-rig pass would otherwise keep them, and this avatar, alive.
                MagicaClothWriter.ReleaseCaches();
#endif
            }
            return report;
        }

        // A run that threw never reaches BridgeFinish, and it is the run whose report most needs
        // sending. The markdown alone: the diagnostics would read a half-converted avatar.
        static void WriteFailureReport(BridgeContext ctx, VRCAvatarDescriptor descriptor)
        {
            if (string.IsNullOrEmpty(ctx.OutputDir))
            {
                return;
            }
            try
            {
                string path = ctx.OutputDir + "/ConversionReport.md";
                string name = ctx.Target != null ? ctx.Target.name : descriptor.gameObject.name;
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", path)),
                    ctx.Report.ToMarkdown(name));
                AssetDatabase.ImportAsset(path);
                ctx.Report.SavedReportPath = path;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarBridge] The report could not be written either: {e.Message}");
            }
        }

        static BridgePass[] ContentPasses()
        {
            return new[]
                {
                    // Delete VRChat-only systems first, so nothing
                    // downstream converts content about to be removed.
                    Pass("Strip VRChat-only systems", SystemStripper.RemoveStrippedObjects),
                    // Then rescue VRCFury-baked meshes out of its
                    // volatile temp before anything else reads them.
                    Pass("Rehome baked scene assets", SceneAssetRehomer.Run),

                    // BEFORE the descriptor: the descriptor pass places Voice Position, and the
                    // jaw bone is what it places it on. Unmapping a bogus jaw afterwards would
                    // leave the voice already sitting in the avatar's hair.
                    Pass("Humanoid rig", JawUnmapper.Run),
                    Pass("Avatar descriptor", DescriptorConverter.Run),
                    Pass("Face tracking", FaceTrackingConverter.Run),
                    Pass("Parameters and menu", ParameterMenuConverter.Run),
                    Pass("PhysBones", PhysBoneConverter.Run),
                    Pass("Contacts", ContactsConverter.Run),
                    Pass("Animator merge", AnimatorMerger.Run),
                    // After the merge, so the curves it repoints are the
                    // ones the avatar will actually run.
                    Pass("Hold rewired contacts", ContactsConverter.HoldUnlatchedContacts),
                    Pass("Flatten VRCFury's second head", FuryHeadFlattener.Run),
                    Pass("Misc components", MiscConverter.Run),
                    Pass("Constraints", ConstraintConverter.Run),
                    // After the constraints exist as Unity components
                    // and transforms have stopped moving; this pass
                    // reads live world poses.
                    Pass("Constraint scale relays", ConstraintScaleRelay.Run),
                    // After the constraints, because the relays reading from a
                    // dead helper rig do not exist until now.
                    Pass("Helper rig cleanup", HelperRigCleanup.Run),
                    Pass("Shader SPI patch", ShaderSpiPatcher.Run),
#if !AVATARBRIDGE_YAPS
                    // Where the YAPS pass would have run.
                    Pass("Penetration add-on", SystemStripper.NotePenetrationAddOn),
#else
                    // After the SPI patch, so the deform lands on the stereo-fixed copy.
                    Pass("YAPS penetration system", YapsConverter.Run),
                    // No socket channel pass. The shader stopped reading the
                    // channel, which latched a plug bent toward a socket that
                    // had gone, and building it would spend up to nine synced
                    // floats a plug on nothing.
#endif

                    // Last content pass before anything edits a clip.
                    // The controller is final; every referenced clip is
                    // pulled into the output folder. Everything below
                    // edits owned copies.
                    Pass("Self-contain clips and masks", AnimationSelfContainer.Run,
                         PassTraits.MakesClipsOurs),
                    // The stereo patch's other half: swap curves in the
                    // clips, now that the clips are copies of this run's.
                    Pass("Repoint material swaps at stereo copies", ShaderSpiPatcher.RepointSwapClipsPass,
                         PassTraits.EditsClips),
                    // The source's own clips are only repointed on our copies. First among the
                    // path passes: the ones below recorded their paths after the head moved.
                    Pass("Repoint curves into the removed second head", FuryHeadFlattener.RepointOnOurCopies,
                         PassTraits.EditsClips),
                    // Before the constraint and collider repoints, so curves into a deleted rig
                    // are gone rather than listed as could not be carried.
                    Pass("Strip helper rig curves", HelperRigCleanup.StripCurves, PassTraits.EditsClips),

                    // Gives each grafted emote state a renamed copy of its clip and edits
                    // none: a rename in place would reach every other state using it.
                    Pass("Name grafted emote clips", AnimatorMerger.NameGraftedEmoteClips),
                    Pass("Strip dead material curves", AnimatorMerger.StripDeadMaterialCurves,
                         PassTraits.EditsClips),
                    // Rewrites curves; must run on owned copies.
                    Pass("Repoint constraint curves", ConstraintConverter.RepointCurvesOnOurCopies,
                         PassTraits.EditsClips),
                    Pass("Repoint contact enable curves", ContactsConverter.RepointContactEnableCurves,
                         PassTraits.EditsClips),
                    // The collider twin: clothing switching its own collision.
                    Pass("Repoint collider enable curves", PhysBoneConverter.RepointColliderEnableCurves,
                         PassTraits.EditsClips),
                    // After both repoints: a rewired zone something switches off
                    // needs a writer switching it back on, because CVR will not.
                    Pass("Balance rewired zone curves", ContactsConverter.BalanceRewiredZoneCurves,
                         PassTraits.EditsClips),
                    // After ownership settles: zones one slider grows get
                    // scale curves written beside that slider's own.
                    Pass("Scale zones with their sliders", ContactsConverter.ScaleZonesWithSliders,
                         PassTraits.EditsClips),
                    // After ownership settles: it writes into the clips that
                    // drive the plug's own blendshapes, and editing a shared
                    // clip would reach the package it came from.
#if AVATARBRIDGE_YAPS
                    Pass("Mirror YAPS blendshape curves", YapsConverter.MirrorShapeCurves,
                         PassTraits.EditsClips),
                    // Transition thresholds on the merged controller only;
                    // touches no clip.
                    Pass("Steady auto socket mode", YapsConverter.SteadyAutoMode),
#endif
                    // Reads the final clip list, enables emission on the systems a toggle
                    // switches, and strips their emission curves from the clips.
                    Pass("Enable animated particle emitters", MiscConverter.EnableAnimatedParticleEmitters,
                         PassTraits.EditsClips),
                    // Animated PhysBone parameters have no retarget on
                    // the Magica path; named as lost and removed.
                    Pass("Report animated PhysBone properties",
                         PhysBoneConverter.ReportAnimatedPhysBoneProperties, PassTraits.EditsClips),
                    // The atlas objects went early; their curves go here,
                    // once the clips are the conversion's own. Before the rename so its
                    // dead-path sweep judges a clean set.
#if AVATARBRIDGE_YAPS
                    Pass("Strip screen-atlas curves", YapsConverter.StripAtlasCurves,
                         PassTraits.EditsClips),
                    // Also before the rename, and after every bake: a swap
                    // recorded against a renderer needs that renderer's path
                    // as the clips still spell it.
                    Pass("Point material swaps at the baked material",
                         YapsConverter.RepointSwappedMaterials, PassTraits.EditsClips),
                    // DEAD LAST among the passes that touch a clip. It
                    // renames objects and rewrites every path naming them,
                    // so anything running after it that wrote a path would
                    // write the old one and address nothing.
                    Pass("Rename YAPS objects", YapsRename.Run, PassTraits.EditsClips),
                    // Reads only, once every path is final.
                    Pass("Check YAPS curves bind", YapsConverter.CheckCurveBindings),
#endif
                    // Last thing that touches the animator: the masks that
                    // list every transform by name must match the final
                    // hierarchy, or a renamed object's transform curves are
                    // silently dropped by every layer wearing one.
                    Pass("Refresh transform masks", AnimatorMerger.RefreshRigMasks),
                    // Same reason: the CCK builds a native toggle's clips from
                    // the path written at the merge, and objects have moved since.
                    Pass("Refresh native toggle paths", ToggleNativizer.RefreshTargetPaths),
                    // Judge the saved file's references only now, after
                    // the self-container fixed what it was going to.
                    Pass("Audit serialized references", AnimatorMerger.AuditSerializedReferences),
                    // Dead last. It reads the finished avatar's weight, and
                    // every pass above can still add a renderer or repoint a
                    // material at a different texture.
                    Pass("Texture sizes", AvatarSlimmer.SlimOnConvert),
                };
        }

        internal static string ValidateLivePipelineForTest() => BridgePipeline.Validate(ContentPasses());
        static BridgePass Pass(string name, Action<BridgeContext> run, PassTraits traits = PassTraits.None) =>
            new BridgePass { Name = name, Run = run, Traits = traits };

        static void SaveConvertedPrefab(BridgeContext ctx)
        {
            try
            {
                string safe = string.Concat(ctx.Target.name.Split(System.IO.Path.GetInvalidFileNameChars()));
                string path = $"{ctx.OutputDir}/{safe}.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(ctx.Target, path, out bool success);
                if (success && prefab != null)
                {
                    ctx.Report.Converted("Conversion", "Converted avatar saved as a prefab",
                        $"{path}: a crash or an unsaved scene can no longer lose the conversion; " +
                        "drag the prefab back into the scene to continue where you left off.");
                }
                else
                {
                    ctx.Report.Warning("Conversion", "Could not save the converted avatar as a prefab",
                        "The scene object is still fine, save the scene to keep it. The usual cause " +
                        "is a component Unity refuses to persist; the console names it.");
                }
            }
            catch (Exception e)
            {
                ctx.Report.Warning("Conversion", "Could not save the converted avatar as a prefab",
                    $"{e.Message}; the scene object is still fine; save the scene to keep it.");
            }
        }

        static void RebindAnimators(BridgeContext ctx)
        {
            foreach (var animator in ctx.Target.GetComponentsInChildren<Animator>(true))
            {
                var assigned = animator.runtimeAnimatorController;
                if (assigned == null)
                {
                    continue;
                }
                string path = AssetDatabase.GetAssetPath(assigned);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }
                var current = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
                if (current == null)
                {
                    continue;
                }
                // Only ever re-assigns what is already there, so the "would this crash Unity"
                // decision made before the first assignment still stands.
                animator.runtimeAnimatorController = current;
            }
        }

        static void WarnFastPlayMode(BridgeContext ctx)
        {
            if (!EditorSettings.enterPlayModeOptionsEnabled)
            {
                return;
            }
            var options = EditorSettings.enterPlayModeOptions;
            bool noScene = options.HasFlag(EnterPlayModeOptions.DisableSceneReload);
            bool noDomain = options.HasFlag(EnterPlayModeOptions.DisableDomainReload);
            string which = noScene && noDomain ? "Reload Domain and Reload Scene are both off"
                : noScene ? "Reload Scene is off"
                : noDomain ? "Reload Domain is off"
                : "it is on";

            ctx.Report.Warning("Conversion", "Unity's \"Enter Play Mode Options\" is on, turn it off before testing",
                $"Edit → Project Settings → Editor → Enter Play Mode Settings ({which}). It causes crashes on " +
                "Play (\"MecanimDataWasBuilt\") and wrong materials in Play mode. Turn it off and reopen the " +
                "scene before reporting either.");
        }

        static void ReportSyncUsage(BridgeContext ctx)
        {
            try
            {
                var usage = ctx.CvrAvatar.GetParameterSyncUsage();
                int used = usage.Item2; // base controller + menu entries = actual sync
                string cckNote = "In the CCK inspector this is the SECOND number of \"(0, N) of 3200\". The first is the " +
                    "override controller, which AvatarBridge doesn't use, so \"0\" there is expected, not a problem.";
                // Not an Error: the CCK rounds bools up to whole bytes and the client checks the
                // budget before each parameter, so a few bits over can still all sync. The
                // diagnostics entry counts the client's way and names any that won't.
                if (used > 3200)
                {
                    ctx.Report.Warning("Sync", $"{used} of 3200 sync bits used, over the limit",
                        "Parameters past the limit work for you and never reach anyone else. The sync budget " +
                        "entry in Diagnostics names them, if any. " + cckNote);
                }
                else
                {
                    ctx.Report.Converted("Sync", $"{used} of 3200 sync bits used", cckNote);
                }
            }
            catch (Exception e)
            {
                ctx.Report.Warning("Sync", "Could not read sync usage", e.Message);
            }
        }

        static void WarnMissingScripts(BridgeContext ctx)
        {
            int missing = 0;
            var examples = new System.Collections.Generic.List<string>();
            var missingPrefabs = new System.Collections.Generic.List<string>();
            foreach (var t in ctx.Target.GetComponentsInChildren<Transform>(true))
            {
                // Unity renames a broken prefab instance to
                // "<name> (Missing Prefab with guid: ...)", an empty
                // shell where a sub-hierarchy used to be. Worth its
                // own warning.
                if (missingPrefabs.Count < 4 && t.name.Contains("(Missing Prefab"))
                {
                    missingPrefabs.Add(t.name);
                }
                foreach (var component in t.GetComponents<Component>())
                {
                    if (component == null)
                    {
                        missing++;
                        if (examples.Count < 4 && !examples.Contains(t.name))
                        {
                            examples.Add(t.name);
                        }
                    }
                }
            }
            if (missingPrefabs.Count > 0)
            {
                ctx.Report.Warning("Avatar",
                    $"{missingPrefabs.Count} missing prefab(s) in the avatar's hierarchy",
                    $"{string.Join("; ", missingPrefabs)}: empty shells. Import their package and convert " +
                    "again if they matter.");
            }
            if (missing == 0)
            {
                return;
            }
            ctx.Report.Warning("Avatar",
                $"{missing} missing script(s) on the avatar, a package it was built with is not installed",
                $"On: {string.Join(", ", examples)}{(missing > examples.Count ? ", …" : "")}. If this " +
                "avatar uses VRCFury or Modular Avatar, install them before converting, or what they build " +
                "is silently missing.");
        }

        static void PrepareTarget(BridgeContext ctx)
        {
            var source = ctx.SourceDescriptor.gameObject;

            // VRCFury avatars must be baked by VRCFury itself first, otherwise every
            // Fury-driven feature (toggles, linked clothing, full controllers) is lost.
            // Fury's bake also runs NDMF internally, so it covers avatars that use both
            // VRCFury and Modular Avatar.
            {
#if AVATARBRIDGE_YAPS
                // Flips the plugs' SPS flag off for the bake, so the plain shader comes back.
                var yaps = YapsBakePrep.Begin(ctx, source);
#endif
                GameObject baked;
                try
                {
                    baked = VRCFuryBaker.TryBake(ctx.SourceDescriptor, ctx.Report);
                }
                finally
                {
#if AVATARBRIDGE_YAPS
                    yaps.Restore();
#endif
                }
                if (baked != null)
                {
                    AdoptBakedCopy(ctx, baked, source);
                    return;
                }
                if (VRCFuryBaker.HasFuryComponents(source))
                {
                    ctx.Report.Warning("VRCFury", "Converting WITHOUT a VRCFury bake",
                        "Fury-driven features will be missing from the result. " + VRCFuryBaker.ManualInstruction);
                }
            }

            // Modular Avatar / NDMF, for MA avatars that don't also use VRCFury (those are
            // already handled above). NDMF's manual bake applies MA and hands back a copy.
            {
                var baked = ModularAvatarBaker.TryBake(ctx.SourceDescriptor, ctx.Report);
                if (baked != null)
                {
                    AdoptBakedCopy(ctx, baked, source);
                    return;
                }
                if (ModularAvatarBaker.HasModularAvatarComponents(source))
                {
                    ctx.Report.Warning("Modular Avatar", "Converting WITHOUT a Modular Avatar bake",
                        "MA-driven features will be missing from the result. " + ModularAvatarBaker.ManualInstruction);
                }
            }

            if (ctx.Settings.cloneAvatar)
            {
                ctx.Target = UnityEngine.Object.Instantiate(source);
                ctx.Target.name = source.name + " (ChilloutVR)";
                ctx.Target.SetActive(true);
                Undo.RegisterCreatedObjectUndo(ctx.Target, "AvatarBridge conversion");
            }
            else
            {
                ctx.Target = source;
                Undo.RegisterFullObjectHierarchyUndo(ctx.Target, "AvatarBridge conversion");
            }
        }

        static void AdoptBakedCopy(BridgeContext ctx, GameObject baked, GameObject source)
        {
            var bakedDescriptor = baked.GetComponentInChildren<VRCAvatarDescriptor>(true);
            // Read everything (menus, params, layers) from the baked data and convert the
            // baked copy in place; the original stays untouched.
            ctx.SourceDescriptor = bakedDescriptor;
            ctx.Target = bakedDescriptor.gameObject;
            ctx.Target.name = source.name + " (ChilloutVR)";
            ctx.Target.SetActive(true);
            Undo.RegisterCreatedObjectUndo(ctx.Target, "AvatarBridge conversion");
        }

        static void DetachCrashingController(BridgeContext ctx)
        {
            var animator = ctx.TargetAnimator;
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return;
            }
            if (!AnimatorMerger.ControllerWouldCrashUnity(animator.runtimeAnimatorController))
            {
                return;
            }
            animator.runtimeAnimatorController = null;
            ctx.Report.Error("Animator",
                "Controller unlinked from the Animator, it CRASHES Unity",
                "It references missing assets, and selecting the object would crash Unity, so it is " +
                "unlinked. The CVRAvatar still carries it. Fix the references (see the unresolvable-asset " +
                "error) and convert again before uploading.");
        }
    }
}
#endif
