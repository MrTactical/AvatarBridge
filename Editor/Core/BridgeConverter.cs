#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge
{
    // Pass order matters; ContentPasses says why each sits where it does.
    public static class BridgeConverter
    {
        public static BridgeReport Convert(VRCAvatarDescriptor descriptor, BridgeSettings settings)
        {
            // A copy: the window's object is saved to per-user EditorPrefs, so changes below would reach every project.
            settings = JsonUtility.FromJson<BridgeSettings>(JsonUtility.ToJson(settings));
            // Converting implies stripping, also for old saved settings or scripts.
            if (settings.convertYapsSystems && !settings.stripSpsSystems)
            {
                settings.stripSpsSystems = true;
            }
#if !AVATARBRIDGE_YAPS
            // No add-on: settings saved where it was installed would strip the system and build no replacement.
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
            // Per conversion: names stay stable across runs so reconverting replaces the output,
            // but still collide within a run, where two controllers can each bring an "Angry".
            OutputAssetPaths.Reset();

            // The report is read on other machines, and comma decimals paste badly.
            var previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.InvariantCulture;

            try
            {
                // Before anything is written: a baker that did not compile makes its components
                // read as absent and the conversion comes out quietly gutted.
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

                // CVR deletes them on load, the CCK complains, and a VRC descriptor reads as unconverted.
                MiscConverter.DeleteVrcComponents(ctx);
#if AVATARBRIDGE_YAPS
                // On the finished avatar, as the YAPS window sees it when its Build is pressed.
                YapsConverter.UpgradeLegacy(ctx);
#endif

                // Work happened on a clone or baked copy.
                if (ctx.Target != descriptor.gameObject)
                {
                    // Recorded, or Undo removes the copy and leaves the original hidden.
                    Undo.RecordObject(descriptor.gameObject, "AvatarBridge conversion");
                    descriptor.gameObject.SetActive(false);
                }

                // AnimatorMerger refuses the crashing assignment itself; this nets later passes.
                DetachCrashingController(ctx);
                WarnFastPlayMode(ctx);
                ReportSyncUsage(ctx);
                KeepUploadId(ctx, descriptor.gameObject);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                SaveConvertedPrefab(ctx);
                long saved = clock.ElapsedMilliseconds;
                // Last, so it validates and describes the avatar as it will actually ship.
                BridgeFinish.Run(ctx, "ConversionReport.md", "Report");
                Debug.Log($"[AvatarBridge] After the passes (ms): save {saved}, report {clock.ElapsedMilliseconds - saved}");
                RebindAnimators(ctx);
                Selection.activeGameObject = ctx.Target;
                // For the report's "Show": Selection is whatever was clicked last by read time.
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
                // Cloth mesh caches: a run throwing before the helper-rig pass would keep them, and the avatar, alive.
                MagicaClothWriter.ReleaseCaches();
#endif
            }
            return report;
        }

        // A thrown run skips BridgeFinish but needs its report most. Markdown only: diagnostics
        // would read a half-converted avatar.
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
                    ctx.Report.ToMarkdown(name), DiagnosticsWriter.Marked);
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
                    // First, so nothing downstream converts content about to be removed.
                    Pass("Strip VRChat-only systems", SystemStripper.RemoveStrippedObjects),
                    // VRCFury-baked meshes out of its volatile temp before anything reads them.
                    Pass("Rehome baked scene assets", SceneAssetRehomer.Run),

                    // Before the descriptor, which places Voice Position on the jaw: unmapping a
                    // bogus jaw afterwards leaves the voice in the hair.
                    // Skeleton first: the jaw rebuild reads the rig this one leaves.
                    Pass("Humanoid rig skeleton", RigSkeletonSync.Run),
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
                    // After the constraints exist and transforms stop moving: reads live world poses.
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
                    // No socket channel pass: the shader does not read it (it latched plugs bent
                    // toward departed sockets), and it would cost up to nine synced floats a plug.
#endif

                    // Last before any clip edit: copies every clip into the output, so below edits owned copies.
                    Pass("Self-contain clips and masks", AnimationSelfContainer.Run,
                         PassTraits.MakesClipsOurs),
                    // The stereo patch's other half: swap curves in the
                    // clips, now that the clips are copies of this run's.
                    Pass("Repoint material swaps at stereo copies", ShaderSpiPatcher.RepointSwapClipsPass,
                         PassTraits.EditsClips),
                    // Source clips are only repointed on owned copies. First among the
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
                    // After ownership settles: editing a shared clip would reach its source package.
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
                    // Animated PhysBone parameters have no Magica retarget; reported lost and removed.
                    Pass("Report animated PhysBone properties",
                         PhysBoneConverter.ReportAnimatedPhysBoneProperties, PassTraits.EditsClips),
                    // The atlas objects went early; their curves go here,
                    // once the clips are the conversion's own. Before the rename so its
                    // dead-path sweep judges a clean set.
#if AVATARBRIDGE_YAPS
                    Pass("Strip screen-atlas curves", YapsConverter.StripAtlasCurves,
                         PassTraits.EditsClips),
                    // After every bake, before the rename: swaps need renderer paths as the clips still spell them.
                    Pass("Point material swaps at the baked material",
                         YapsConverter.RepointSwappedMaterials, PassTraits.EditsClips),
                    // After the bake's swaps point at the bake: the squash copies what the
                    // avatar ends up wearing and points those swaps on again.
                    Pass("Squash soft parts", YapsSquash.Run, PassTraits.EditsClips),
                    // Last clip pass: it renames objects and their paths, so a later path write would address nothing.
                    Pass("Rename YAPS objects", YapsRename.Run, PassTraits.EditsClips),
                    // Reads only, once every path is final.
                    Pass("Check YAPS curves bind", YapsConverter.CheckCurveBindings),
#endif
                    // Last animator edit: masks list transforms by name, or renamed objects' curves are silently dropped.
                    Pass("Refresh transform masks", AnimatorMerger.RefreshRigMasks),
                    // Same reason: the CCK builds a native toggle's clips from
                    // the path written at the merge, and objects have moved since.
                    Pass("Refresh native toggle paths", ToggleNativizer.RefreshTargetPaths),
#if AVATARBRIDGE_MAGICA
                    // Once the hierarchy is final: it records each cloth's transforms as they stand.
                    Pass("Pin cloth start scale", MagicaClothWriter.PinStartScale),
#endif
                    // After the self-container fixed what it could.
                    Pass("Audit serialized references", AnimatorMerger.AuditSerializedReferences),
                    // Last: any pass above can still add a renderer or change a texture.
                    Pass("Texture sizes", AvatarSlimmer.SlimOnConvert),
                };
        }

        internal static string ValidateLivePipelineForTest() => BridgePipeline.Validate(ContentPasses());
        static BridgePass Pass(string name, Action<BridgeContext> run, PassTraits traits = PassTraits.None) =>
            new BridgePass { Name = name, Run = run, Traits = traits };

        static string PrefabPath(BridgeContext ctx) =>
            $"{ctx.OutputDir}/{string.Concat(ctx.Target.name.Split(System.IO.Path.GetInvalidFileNameChars()))}.prefab";

        // The CCK uploads a copy without an ID as a new avatar. Taken from the source's output
        // prefab, or a same-named scene copy if no other avatar shares the name. Two IDs: user's choice.
        static void KeepUploadId(BridgeContext ctx, GameObject source)
        {
            var info = ctx.Target.GetComponent<CVRAssetInfo>();
            if (info == null || !string.IsNullOrEmpty(info.objectId)) return;
            var found = new List<CVRAssetInfo>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(ctx));
            if (prefab != null && prefab.TryGetComponent(out CVRAssetInfo kept)) found.Add(kept);
            var roots = new List<GameObject>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene.isLoaded) roots.AddRange(scene.GetRootGameObjects());
            }
            bool sharedName = roots.SelectMany(r => r.GetComponentsInChildren<VRCAvatarDescriptor>(true))
                .Any(d => d.gameObject != source && d.gameObject.name == source.name);
            if (!sharedName)
                found.AddRange(roots.SelectMany(r => r.GetComponentsInChildren<CVRAssetInfo>(true))
                    .Where(a => a.gameObject != ctx.Target && a.gameObject.name == ctx.Target.name));
            var ids = found.Where(a => a.type == CVRAssetInfo.AssetType.Avatar && Guid.TryParse(a.objectId, out _))
                .Select(a => a.objectId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (ids.Count == 1)
            {
                Undo.RecordObject(info, "AvatarBridge conversion");
                info.objectId = ids[0];
                EditorUtility.SetDirty(info);
                ctx.Report.Converted("Upload", "Upload ID kept from the last conversion",
                    $"Uploading updates the avatar already uploaded as {ids[0]} instead of adding a new one. " +
                    "To upload it as a new avatar, clear its GUID in the CCK first.");
            }
            else if (ids.Count > 1)
            {
                ctx.Report.Warning("Upload", "Earlier conversions were uploaded under different IDs",
                    $"{string.Join(", ", ids)}. Uploading adds a new avatar unless you attach the one to update " +
                    "in the avatar's Asset Info (Re-Attach GUID).");
            }
        }

        static void SaveConvertedPrefab(BridgeContext ctx)
        {
            try
            {
                string path = PrefabPath(ctx);
                bool linked = ctx.Settings.linkConvertedPrefab, success;
                var prefab = linked
                    ? PrefabUtility.SaveAsPrefabAssetAndConnect(ctx.Target, path, InteractionMode.AutomatedAction, out success)
                    : PrefabUtility.SaveAsPrefabAsset(ctx.Target, path, out success);
                if (success && prefab != null)
                {
                    ctx.Report.Converted("Conversion", "Converted avatar saved as a prefab",
                        $"{path}: a crash or an unsaved scene can no longer lose the conversion; " +
                        (linked
                            ? "the copy in the scene is linked to it, so edits made from here can be applied to the prefab."
                            : "drag the prefab back into the scene to continue where you left off."));
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
                // Not an Error: the CCK rounds bools to bytes and counts clip-driven floats the client
                // never syncs. Diagnostics counts the client's way.
                if (used > 3200)
                {
                    ctx.Report.Warning("Sync", $"{used} of 3200 sync bits used, over the limit",
                        "Parameters past the limit work for you and never reach anyone else. This count includes " +
                        "floats an animation drives, which never sync and cost nothing in game, so the sync budget " +
                        "entry in Diagnostics is the real count and names any that won't sync. " + cckNote);
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
            var examples = new List<string>();
            var missingPrefabs = new List<string>();
            foreach (var t in ctx.Target.GetComponentsInChildren<Transform>(true))
            {
                // A broken prefab instance is an empty shell named "(Missing Prefab with guid: ...)".
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
                    $"{BridgeElements.Count(missingPrefabs.Count, "missing prefab", "missing prefabs")} in the avatar's hierarchy",
                    $"{string.Join("; ", missingPrefabs)}: empty shells. Import their package and convert " +
                    "again if they matter.");
            }
            if (missing == 0)
            {
                return;
            }
            ctx.Report.Warning("Avatar",
                $"{BridgeElements.Count(missing, "missing script", "missing scripts")} on the avatar, a package it was built with is not installed",
                $"On: {string.Join(", ", examples)}{(missing > examples.Count ? ", …" : "")}. If this " +
                "avatar uses VRCFury or Modular Avatar, install them before converting, or what they build " +
                "is silently missing.");
        }

        static void PrepareTarget(BridgeContext ctx)
        {
            var source = ctx.SourceDescriptor.gameObject;

            // Unbaked, every Fury feature is lost. Fury's bake runs NDMF too, covering MA as well.
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

            // MA without VRCFury: NDMF's manual bake applies MA and returns a copy.
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
            // Convert the baked copy in place; the original stays untouched.
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
