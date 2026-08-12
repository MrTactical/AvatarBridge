#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge
{
    // Orchestrates a full VRChat -> ChilloutVR avatar conversion. Each pass reads shared
    // state from the BridgeContext; the order matters:
    //   1. Descriptor        (creates the CVRAvatar)
    //   2. Parameters/menu   (fills preserve/impulse parameter sets)
    //   3. PhysBones         (physics, before VRC components are deleted)
    //   4. Contacts          (fills contact parameter set)
    //   5. Animator merge    (uses all parameter sets for its rename pass)
    //   6. Misc + constraints
    //   7. VRC cleanup
    public static class BridgeConverter
    {
        public static BridgeReport Convert(VRCAvatarDescriptor descriptor, BridgeSettings settings)
        {
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
                PrepareOutputFolder(ctx);
                // Before anything is built or written: if the project has a baker installed that
                // did not compile, every component it owns reads as absent and the conversion
                // comes out quietly gutted. Stop here rather than spend the run producing it.
                if (!BridgePreflight.Check(ctx))
                {
                    report.Error("Conversion", "Stopped before converting",
                        "The problem above would have made this conversion silently wrong rather " +
                        "than visibly broken, which is worse. Nothing was changed.");
                    return report;
                }
                PrepareTarget(ctx);
                WarnMissingScripts(ctx);

                BridgePipeline.Execute(ctx, ContentPasses());

                // Always. CVR deletes them on load, the CCK complains,
                // and a lingering VRC descriptor reads as unconverted.
                MiscConverter.DeleteVrcComponents(ctx);

                // Deactivate the original whenever the work happened on
                // a separate object (clone or baked copy).
                if (ctx.Target != descriptor.gameObject)
                {
                    descriptor.gameObject.SetActive(false);
                }

                // Belt and braces. AnimatorMerger refuses the crashing
                // assignment itself; this nets later passes.
                DetachCrashingController(ctx);
                WarnFastPlayMode(ctx);
                ReportSyncUsage(ctx);
                SaveConvertedPrefab(ctx);
                // Last, so it validates and describes the avatar as it will actually ship.
                BridgeDiagnostics.Run(ctx, ctx.MergedController);
                ctx.Report.StoreDescription = AvatarDescription.Write(ctx);
                WriteReportFile(ctx);
                EditorUtility.SetDirty(ctx.CvrAvatar);
                AssetDatabase.SaveAssets();
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
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture;
            }
            return report;
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
                    Pass("Misc components", MiscConverter.Run),
                    Pass("Constraints", ConstraintConverter.Run),
                    // After the constraints exist as Unity components
                    // and transforms have stopped moving; this pass
                    // reads live world poses.
                    Pass("Constraint scale relays", ConstraintScaleRelay.Run),
                    Pass("Shader SPI patch", ShaderSpiPatcher.Run),

                    // Last content pass before anything edits a clip.
                    // The controller is final; every referenced clip is
                    // pulled into the output folder. Everything below
                    // edits owned copies.
                    Pass("Self-contain clips and masks", AnimationSelfContainer.Run,
                         PassTraits.MakesClipsOurs),

                    // After self-containment; it renames clip assets,
                    // and only an owned copy may be renamed.
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
                    // Reads the final clip list, writes to particle components.
                    Pass("Enable animated particle emitters", MiscConverter.EnableAnimatedParticleEmitters),
                    // Animated PhysBone parameters have no retarget on
                    // the Magica path; named as lost and removed.
                    Pass("Report animated PhysBone properties",
                         PhysBoneConverter.ReportAnimatedPhysBoneProperties, PassTraits.EditsClips),
                    // Judge the saved file's references only now, after
                    // the self-container fixed what it was going to.
                    Pass("Audit serialized references", AnimatorMerger.AuditSerializedReferences),
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
                        $"{path} — a crash or an unsaved scene can no longer lose the conversion; " +
                        "drag the prefab back into the scene to continue where you left off.");
                }
                else
                {
                    ctx.Report.Warning("Conversion", "Could not save the converted avatar as a prefab",
                        "The scene object is still fine — save the scene to keep it. The usual cause " +
                        "is a component Unity refuses to persist; the console names it.");
                }
            }
            catch (Exception e)
            {
                ctx.Report.Warning("Conversion", "Could not save the converted avatar as a prefab",
                    $"{e.Message} — the scene object is still fine; save the scene to keep it.");
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

            ctx.Report.Warning("Conversion", "Unity's \"Enter Play Mode Options\" is on — turn it off before testing",
                $"Edit → Project Settings → Editor → Enter Play Mode Settings ({which}). It skips the scene " +
                "and/or domain reload, so pressing Play rebinds every Animator against state left over from " +
                "edit mode — and the controller this conversion just wrote is the newest thing in the project. " +
                "Two things that get blamed on conversion come from this and nothing else: Unity dying on Play " +
                "with \"Assertion failed on expression: 'MecanimDataWasBuilt()'\" and a SIGSEGV inside " +
                "GenerateGraph, and an avatar that looks right in the scene but renders with the wrong " +
                "materials the moment you press Play. That crash stack runs through RestoreSceneBackups, which " +
                "does not execute at all with the option off. Unity says the same in its own console every time " +
                "you enter play mode this way. Turn it off, reopen the scene, and test again before reporting " +
                "either symptom.");
        }

        static void ReportSyncUsage(BridgeContext ctx)
        {
            try
            {
                var usage = ctx.CvrAvatar.GetParameterSyncUsage();
                int used = usage.Item2; // base controller + menu entries = actual sync
                ctx.Report.Converted("Sync", $"{used} of 3200 sync bits used",
                    "In the CCK inspector this is the SECOND number of \"(0, N) of 3200\". The first is the " +
                    "override controller, which AvatarBridge doesn't use — so \"0\" there is expected, not a problem.");
            }
            catch (Exception e)
            {
                ctx.Report.Warning("Sync", "Could not read sync usage", e.Message);
            }
        }

        static void PrepareOutputFolder(BridgeContext ctx)
        {
            string safeName = ctx.SourceDescriptor.gameObject.name;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(c, '_');
            }
            // Names like ".", ".." or all-dots would escape (or collide with) the output
            // folder; so would an empty name. Fall back to a fixed name instead.
            safeName = safeName.Trim().Trim('.');
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "Avatar";
            }

            // The output folder must stay inside Assets. Delete and
            // overwrite operations must never point outside the project.
            string folder = (ctx.Settings.outputFolder ?? "").Trim().Replace('\\', '/').TrimEnd('/');
            if (folder != "Assets" && !folder.StartsWith("Assets/") || folder.Contains(".."))
            {
                ctx.Report.Warning("Conversion", $"Output folder \"{ctx.Settings.outputFolder}\" is not inside Assets",
                    "Using the default \"Assets/AvatarBridgeOutput\" instead.");
                folder = "Assets/AvatarBridgeOutput";
            }
            ctx.OutputDir = folder + "/" + safeName;

            string absolute = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", ctx.OutputDir));
            Directory.CreateDirectory(absolute);
            AssetDatabase.Refresh();
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
                    $"{string.Join("; ", missingPrefabs)} — the prefab asset these were instances of " +
                    "isn't in this project, so each is an empty shell where a feature used to be. The " +
                    "avatar converts fine without them; if the feature matters, import the package it " +
                    "came from and convert again.");
            }
            if (missing == 0)
            {
                return;
            }
            ctx.Report.Warning("Avatar",
                $"{missing} missing script(s) on the avatar — a package it was built with is not installed",
                $"On: {string.Join(", ", examples)}{(missing > examples.Count ? ", …" : "")}. If this " +
                "avatar uses VRCFury or Modular Avatar, INSTALL THEM BEFORE CONVERTING: both do their " +
                "real work at build time (toggles, armature merges, animation path rewriting), and " +
                "without them everything they would have baked is silently missing from the conversion " +
                "— features can look converted and still do nothing in game.");
        }

        static void PrepareTarget(BridgeContext ctx)
        {
            var source = ctx.SourceDescriptor.gameObject;

            // VRCFury avatars must be baked by VRCFury itself first, otherwise every
            // Fury-driven feature (toggles, linked clothing, full controllers) is lost.
            // Fury's bake also runs NDMF internally, so it covers avatars that use both
            // VRCFury and Modular Avatar.
            {
                var baked = VRCFuryBaker.TryBake(ctx.SourceDescriptor, ctx.Report);
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
                "Controller unlinked from the Animator — it CRASHES Unity",
                "This avatar's controller references assets that resolve to nothing, and Unity " +
                "builds a Mecanim playable graph from a controller whenever the Animator awakens — " +
                "which merely SELECTING the object in the Inspector is enough to do. That walks " +
                "into the missing references and takes the editor down with no error, losing " +
                "unsaved work. The reference has been removed so the editor can't do it. " +
                "ChilloutVR is unaffected by the removal itself: the CVRAvatar still carries the " +
                "base controller and the overrides, which is what the client reads on load. But " +
                "the broken references are still in that controller, so fix them and convert " +
                "again before uploading — see the unresolvable-asset error for where they came " +
                "from, usually a VRCFury or Modular Avatar bake that errored partway.");
        }

        static void WriteReportFile(BridgeContext ctx)
        {
            string path = ctx.OutputDir + "/ConversionReport.md";
            string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            File.WriteAllText(absolute, ctx.Report.ToMarkdown(ctx.Target.name));
            AssetDatabase.ImportAsset(path);
            ctx.Report.SavedReportPath = path;
            Debug.Log($"[AvatarBridge] Report written to {path}");

            // Never allowed to take the conversion down with it: the report is the deliverable,
            // diagnostics are a bonus, and a crash here would lose both.
            try
            {
                DiagnosticsWriter.Write(ctx);
                // The web report renders the same entries drawn.
                // Written last so it can show everything.
                HtmlReportWriter.Write(ctx);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarBridge] Diagnostics could not be written: {e}");
            }
        }
    }
}
#endif
