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

                DescriptorConverter.Run(ctx);
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

                if (settings.cloneAvatar)
                {
                    descriptor.gameObject.SetActive(false);
                }

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

        static void PrepareOutputFolder(BridgeContext ctx)
        {
            string safeName = ctx.SourceDescriptor.gameObject.name;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(c, '_');
            }
            ctx.OutputDir = ctx.Settings.outputFolder.TrimEnd('/') + "/" + safeName;

            string absolute = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", ctx.OutputDir));
            Directory.CreateDirectory(absolute);
            AssetDatabase.Refresh();
        }

        static void PrepareTarget(BridgeContext ctx)
        {
            var source = ctx.SourceDescriptor.gameObject;
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

        static void WriteReportFile(BridgeContext ctx)
        {
            string path = ctx.OutputDir + "/ConversionReport.md";
            string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            File.WriteAllText(absolute, ctx.Report.ToMarkdown(ctx.Target.name));
            AssetDatabase.ImportAsset(path);
            Debug.Log($"[AvatarBridge] Report written to {path}");
        }
    }
}
#endif
