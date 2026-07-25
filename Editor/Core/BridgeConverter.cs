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

            try
            {
                PrepareOutputFolder(ctx);
                PrepareTarget(ctx);
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

                if (settings.deleteVrcComponents)
                {
                    MiscConverter.DeleteVrcComponents(ctx);
                }
                else
                {
                    report.Warning("Cleanup", "VRC components kept",
                        "The CCK upload will likely complain about them; enable cleanup or remove them manually.");
                }

                // Deactivate the original whenever we worked on a separate object
                // (explicit clone or a VRCFury-baked copy).
                if (ctx.Target != descriptor.gameObject)
                {
                    descriptor.gameObject.SetActive(false);
                }

                ReportSyncUsage(ctx);
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
            return report;
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
                    "Using the default \"Assets/AvatarBridge/Output\" instead.");
                folder = "Assets/AvatarBridge/Output";
            }
            ctx.OutputDir = folder + "/" + safeName;

            string absolute = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", ctx.OutputDir));
            Directory.CreateDirectory(absolute);
            AssetDatabase.Refresh();
        }

        static void PrepareTarget(BridgeContext ctx)
        {
            var source = ctx.SourceDescriptor.gameObject;

            // VRCFury avatars must be baked by VRCFury itself first, otherwise every
            // Fury-driven feature (toggles, linked clothing, full controllers) is lost.
            // Fury's bake also runs NDMF internally, so it covers avatars that use both
            // VRCFury and Modular Avatar.
            if (ctx.Settings.bakeVrcFury)
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
            if (ctx.Settings.bakeModularAvatar)
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
