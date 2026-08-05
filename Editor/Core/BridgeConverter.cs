#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge
{
    /// <summary>
    /// Orchestrates a full VRChat -> ChilloutVR avatar conversion. Each pass reads shared
    /// state from the BridgeContext; the order matters:
    ///   1. Descriptor        (creates the CVRAvatar)
    ///   2. Parameters/menu   (fills preserve/impulse parameter sets)
    ///   3. PhysBones         (physics, before VRC components are deleted)
    ///   4. Contacts          (fills contact parameter set)
    ///   5. Animator merge    (uses all parameter sets for its rename pass)
    ///   6. Misc + constraints
    ///   7. VRC cleanup
    /// </summary>
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

            // Format numbers the same way for everybody, for the length of the conversion.
            //
            // The report is the main thing a bug arrives with, and it is read by someone other
            // than the person who generated it. On a comma-decimal locale it came out saying
            // 'Height (M)' = 1,24 m and StepSize 0,05 — correct for that user, ambiguous to
            // anyone else, and no good pasted into a field that wants a point. Set once here
            // rather than at each of the fifteen places that format a number, so anything added
            // later is covered without having to remember.
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

                // Always: ChilloutVR deletes them on load anyway, the CCK upload complains
                // about them, and an avatar still wearing its VRC descriptor reads as "not
                // converted" — it convinced even the maintainer once.
                MiscConverter.DeleteVrcComponents(ctx);

                // Deactivate the original whenever we worked on a separate object
                // (explicit clone or a VRCFury-baked copy).
                if (ctx.Target != descriptor.gameObject)
                {
                    descriptor.gameObject.SetActive(false);
                }

                // Belt and braces. AnimatorMerger already refuses to assign a controller that
                // would crash Unity, which is the only place that check can actually work — the
                // damage is done by the assignment itself. This stays as a net for anything else
                // that might put one back on an Animator during a later pass.
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

        /// <summary>
        /// The conversion's content passes, in the order they run. Declared as data so the ordering
        /// rules live in BridgePipeline where they are CHECKED rather than described, and so a test
        /// can validate this exact list without running a conversion.
        /// </summary>
        static BridgePass[] ContentPasses()
        {
            return new[]
                {
                    // Delete the VRChat-only systems first, so nothing downstream wastes effort
                    // converting content that's about to be thrown away — or worse, leaves parts
                    // of it behind (rescued SPS shaders that render pink, cloth whose bones vanish).
                    Pass("Strip VRChat-only systems", SystemStripper.RemoveStrippedObjects),
                    // Then rescue VRCFury-baked meshes out of its volatile temp before anything
                    // else reads them (otherwise they orphan to null → invisible avatar).
                    Pass("Rehome baked scene assets", SceneAssetRehomer.Run),

                    Pass("Avatar descriptor", DescriptorConverter.Run),
                    Pass("Face tracking", FaceTrackingConverter.Run),
                    Pass("Parameters and menu", ParameterMenuConverter.Run),
                    Pass("PhysBones", PhysBoneConverter.Run),
                    Pass("Contacts", ContactsConverter.Run),
                    Pass("Animator merge", AnimatorMerger.Run),
                    Pass("Misc components", MiscConverter.Run),
                    Pass("Constraints", ConstraintConverter.Run),
                    // After the constraints exist as Unity components and after
                    // AlignLocalSpaceRelays has finished moving transforms about — this pass reads
                    // live world poses, so everything it measures has to have stopped changing.
                    Pass("Constraint scale relays", ConstraintScaleRelay.Run),
                    Pass("Shader SPI patch", ShaderSpiPatcher.Run),

                    // Last content pass before anything edits a clip: the controller is final
                    // here, so every clip it references is pulled into the output folder — a
                    // conversion that works on this PC must also work on one without the source
                    // avatar's folders. Everything below now edits OUR copies.
                    Pass("Self-contain clips and masks", AnimationSelfContainer.Run,
                         PassTraits.MakesClipsOurs),

                    Pass("Strip dead material curves", AnimatorMerger.StripDeadMaterialCurves,
                         PassTraits.EditsClips),
                    // MOVED here from inside the Constraints pass, which is nine passes earlier:
                    // it rewrites curves, and back there the clips were still the author's own.
                    Pass("Repoint constraint curves", ConstraintConverter.RepointCurvesOnOurCopies,
                         PassTraits.EditsClips),
                    Pass("Repoint contact enable curves", ContactsConverter.RepointContactEnableCurves,
                         PassTraits.EditsClips),
                    // And the collider twin — clothing that switches its own collision.
                    Pass("Repoint collider enable curves", PhysBoneConverter.RepointColliderEnableCurves,
                         PassTraits.EditsClips),
                    // Reads the final clip list and writes to PARTICLE COMPONENTS, not to clips.
                    Pass("Enable animated particle emitters", MiscConverter.EnableAnimatedParticleEmitters),
                    // Animated PhysBone PARAMETERS (radius, gravity…) have no retarget on the
                    // Magica path — measured, not assumed — so they are named as lost and removed.
                    Pass("Report animated PhysBone properties",
                         PhysBoneConverter.ReportAnimatedPhysBoneProperties, PassTraits.EditsClips),
                    // And only then judge the saved file's references — auditing any earlier
                    // flags things the self-container is about to fix.
                    Pass("Audit serialized references", AnimatorMerger.AuditSerializedReferences),
                };
        }

        /// <summary>Lets the regression test validate the SHIPPING order, not a mock of it.</summary>
        internal static string ValidateLivePipelineForTest() => BridgePipeline.Validate(ContentPasses());
        /// <summary>Declares one content pass. Traits default to None — the common case.</summary>
        static BridgePass Pass(string name, Action<BridgeContext> run, PassTraits traits = PassTraits.None) =>
            new BridgePass { Name = name, Run = run, Traits = traits };

        /// <summary>
        /// Persists the converted avatar as a prefab in the output folder.
        ///
        /// The converted avatar otherwise lives only in the scene, and scenes are the one
        /// thing nobody saves right after converting: a tester's Unity died mid-shader-
        /// compilation during the CCK's bundle build (a native crash, no managed stack — the
        /// log just stops), the scene reloaded from its last save, and the entire conversion
        /// was gone. The generated ASSETS all survive on disk; the GameObject wiring them
        /// together was the only casualty, and it is exactly what this preserves. After any
        /// crash, drag the prefab back into the scene and continue.
        /// </summary>
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

        /// <summary>
        /// Re-binds every Animator against a freshly loaded copy of its controller, as the last
        /// thing the conversion does.
        ///
        /// A MITIGATION, not a cure, and it should be described that way. Keeping the controller's
        /// GUID stable means rewriting the asset file in place and force-reimporting it, and that
        /// reimport DESTROYS the old asset's sub-objects — every state machine and embedded clip —
        /// and builds new ones. Anything still holding the old ones is left dangling; an open
        /// Animator window says so out loud, filling the console with "The object of type
        /// 'AnimatorStateMachine' has been destroyed". Several passes run after the controller is
        /// saved and some of them save assets again, so the binding an Animator was given earlier
        /// may not be the one on disk by the time the conversion ends. Re-binding here means the
        /// last graph built is built from the final file.
        ///
        /// It does NOT make the conversion safe under Unity's "Enter Play Mode Options". With
        /// Reload Domain and Reload Scene both off, entering play mode restores a scene backup and
        /// re-awakes Animators without rebuilding anything, and that path reliably dies in
        /// GenerateGraph on a controller written this session. Turning the option off is the only
        /// thing that has been shown to prevent it — see WarnFastPlayMode.
        /// </summary>
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

        /// <summary>
        /// Warns when Unity's "Enter Play Mode Options" are on, because a freshly converted avatar
        /// is exactly the case they break.
        ///
        /// With **Reload Scene** disabled, pressing Play doesn't reload the scene — it restores a
        /// backup of it: RestoreSceneBackups → ResetOpenScenes → ActivateSceneAfterReset →
        /// Animator::AwakeFromLoad → SetAnimatorController → GenerateGraph. That rebinds every
        /// Animator against state carried over from edit mode, and the controller this tool just
        /// wrote is the newest thing in the project. With **Reload Domain** also disabled, nothing
        /// managed is rebuilt either, so a stale binding survives intact.
        ///
        /// Both symptoms it has produced here were reported as conversion bugs and were not:
        ///   - "Assertion failed on expression: 'MecanimDataWasBuilt()'" followed by SIGSEGV inside
        ///     mecanim::statemachine::EvaluateState — the whole stack sits under RestoreSceneBackups,
        ///     which does not run at all with the option off;
        ///   - an avatar rendering with the wrong materials in play mode while looking correct in
        ///     the scene, which is what a half-bound animator applying stale data looks like.
        ///
        /// Unity's own console says the same thing when it enters play mode this way. This is a
        /// warning, not a repair: it is the user's editor preference and not ours to change.
        /// </summary>
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

        /// <summary>
        /// Reports the real sync-bit usage so users don't misread the CCK inspector's
        /// two-number "(overrides, base+menu)" counter. AvatarBridge keeps everything in
        /// the base controller, so the first number is always 0 — the second is what syncs.
        /// </summary>
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

            // The output folder must stay inside the project's Assets folder — everything
            // here is created via AssetDatabase, and delete/overwrite operations must never
            // be able to point outside the project.
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

        /// <summary>
        /// An avatar carrying missing scripts was built with a package this project doesn't
        /// have — VRCFury and Modular Avatar are the usual suspects, and both do their real
        /// work at BUILD time (baking toggles, merging armatures, REWRITING ANIMATION PATHS).
        /// Converted without them, everything they would have baked is silently absent or
        /// broken while the report reads clean: a tester's tail-wag clip bound paths that only
        /// a build-time path rewrite could fix, and nothing said so. This cannot repair
        /// anything; it can only make the cause loud.
        /// </summary>
        static void WarnMissingScripts(BridgeContext ctx)
        {
            int missing = 0;
            var examples = new System.Collections.Generic.List<string>();
            var missingPrefabs = new System.Collections.Generic.List<string>();
            foreach (var t in ctx.Target.GetComponentsInChildren<Transform>(true))
            {
                // Unity renames a broken prefab instance to "<name> (Missing Prefab with
                // guid: …)" — an empty shell where a whole sub-hierarchy used to be. Worth
                // its own warning: the shell's name also carries guid-looking text that used
                // to fool the serialized-reference audit.
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

        /// <summary>Adopts a baked copy (from VRCFury or Modular Avatar) as the conversion target.</summary>
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

        /// <summary>
        /// Takes a crash-inducing controller off the scene Animator once every pass that needed to
        /// read it is done.
        ///
        /// A controller referencing assets that resolve to nothing makes Unity's Mecanim graph
        /// builder segfault, and 3.2.0 stopped AvatarBridge triggering that by leaving the Animator
        /// switched off. It wasn't enough. The graph is built by anything that awakens the
        /// component, and the next crash came from the INSPECTOR — clicking the converted avatar
        /// ran GameObjectInspector.DrawInspector -> ApplyModifiedProperties ->
        /// ActivateAwakeRecursively -> Animator::AwakeFromLoad -> GenerateGraph. Nothing in this
        /// tool was on the stack. Merely selecting the object was enough.
        ///
        /// So the reference is removed outright. Everything ChilloutVR actually needs still ships:
        /// CVRAvatar keeps baseController, baseOverrideController and overrides, which are plain
        /// asset references and build no graph — the client assigns them onto the Animator itself
        /// on load. Only the editor-side link that Unity eagerly instantiates goes.
        ///
        /// Runs last on purpose: ConstraintConverter and the clip audits read
        /// runtimeAnimatorController.animationClips, so the link has to exist for the whole
        /// conversion and only becomes a liability once the editor is left alone with it.
        /// </summary>
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
                // The web report renders the same entries with the numbers drawn — nice to have,
                // never allowed to take the conversion down. Written last so it can show
                // everything, including anything the diagnostics writer added.
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
