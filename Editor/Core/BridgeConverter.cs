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
                PrepareTarget(ctx);
                WarnMissingScripts(ctx);
                // Delete the VRChat-only systems first, so nothing downstream wastes effort
                // converting content that's about to be thrown away — or worse, leaves parts of
                // it behind (rescued SPS shaders that render pink, cloth whose bones then vanish).
                SystemStripper.RemoveStrippedObjects(ctx);
                // Then rescue VRCFury-baked meshes out of its volatile temp before anything else
                // reads them (otherwise they orphan to null → invisible avatar).
                SceneAssetRehomer.Run(ctx);

                DescriptorConverter.Run(ctx);
                FaceTrackingConverter.Run(ctx);
                ParameterMenuConverter.Run(ctx);
                PhysBoneConverter.Run(ctx);
                ContactsConverter.Run(ctx);
                AnimatorMerger.Run(ctx);
                MiscConverter.Run(ctx);
                ConstraintConverter.Run(ctx);
                ShaderSpiPatcher.Run(ctx);
                // Last content pass: the controller is final here, so every clip it ends up
                // referencing gets pulled into the output folder — a conversion that works on
                // this PC must also work on one without the source avatar's folders.
                AnimationSelfContainer.Run(ctx);
                // And only then judge the saved file's references — auditing any earlier
                // flags things the self-container is about to fix.
                AnimatorMerger.AuditSerializedReferences(ctx);

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

                ReportSyncUsage(ctx);
                SaveConvertedPrefab(ctx);
                // Last, so it validates and describes the avatar as it will actually ship.
                BridgeDiagnostics.Run(ctx, ctx.MergedController);
                ctx.Report.StoreDescription = AvatarDescription.Write(ctx);
                WriteReportFile(ctx);
                EditorUtility.SetDirty(ctx.CvrAvatar);
                AssetDatabase.SaveAssets();
                Selection.activeGameObject = ctx.Target;

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

        static void WriteReportFile(BridgeContext ctx)
        {
            string path = ctx.OutputDir + "/ConversionReport.md";
            string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            File.WriteAllText(absolute, ctx.Report.ToMarkdown(ctx.Target.name));
            AssetDatabase.ImportAsset(path);
            ctx.Report.SavedReportPath = path;
            Debug.Log($"[AvatarBridge] Report written to {path}");
        }
    }
}
#endif
