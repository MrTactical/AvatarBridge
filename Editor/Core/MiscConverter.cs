#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using ABI.CCK.Components;

namespace AvatarBridge
{
    /// <summary>
    /// Small component conversions plus the final VRC-component cleanup:
    ///   VRCHeadChop           -> FPRExclusion (CVR first-person hiding/showing)
    ///   VRCSpatialAudioSource -> plain AudioSource spatial settings
    ///   leftover VRC.* components + PipelineManager -> deleted
    /// </summary>
    public static class MiscConverter
    {
        public static void Run(BridgeContext ctx)
        {
            if (ctx.Settings.convertHeadChop)
            {
                ConvertHeadChops(ctx);
            }
            if (ctx.Settings.convertSpatialAudio)
            {
                ConvertSpatialAudio(ctx);
            }
        }

        static void ConvertHeadChops(BridgeContext ctx)
        {
            const string category = "Head chop";
            foreach (var headChop in ctx.Target.GetComponentsInChildren<VRCHeadChop>(true))
            {
                foreach (var setting in headChop.targetBones)
                {
                    if (setting.transform == null)
                    {
                        continue;
                    }
                    float scaleFactor = setting.scaleFactor * headChop.globalScaleFactor;
                    bool isShown = Mathf.Approximately(scaleFactor, 1f);
                    bool isHidden = Mathf.Approximately(scaleFactor, 0f);
                    if (!isShown && !isHidden)
                    {
                        ctx.Report.Skipped(category, setting.transform.name,
                            $"Scale factor {scaleFactor:0.##} cannot be represented; FPRExclusion is show/hide only.");
                        continue;
                    }

                    var go = new GameObject("FPRExclusion_" + setting.transform.name);
                    go.transform.SetParent(ctx.Target.transform, false);
                    var exclusion = go.AddComponent<FPRExclusion>();
                    exclusion.isShown = isShown;
                    exclusion.shrinkToZero = true;
                    exclusion.target = setting.transform;
                    ctx.Report.Converted(category, setting.transform.name,
                        isShown ? "Force-shown in first person" : "Hidden in first person");
                }
                Object.DestroyImmediate(headChop);
            }
        }

        static void ConvertSpatialAudio(BridgeContext ctx)
        {
            const string category = "Audio";
            foreach (var spatial in ctx.Target.GetComponentsInChildren<VRCSpatialAudioSource>(true))
            {
                var audioSource = spatial.GetComponent<AudioSource>();
                if (audioSource != null)
                {
                    audioSource.spatialBlend = spatial.EnableSpatialization ? 1f : 0f;
                    if (!spatial.UseAudioSourceVolumeCurve)
                    {
                        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
                        audioSource.minDistance = spatial.Near;
                        audioSource.maxDistance = spatial.Far;
                    }
                    EditorUtility.SetDirty(audioSource);
                    ctx.Report.Approximated(category, spatial.gameObject.name,
                        "Spatial audio mapped to standard AudioSource settings (gain curve approximated).");
                }
                Object.DestroyImmediate(spatial);
            }
        }

        /// <summary>Removes every remaining VRC component. Run this last.</summary>
        public static void DeleteVrcComponents(BridgeContext ctx)
        {
            const string category = "Cleanup";

            var pipeline = ctx.Target.GetComponent(typeof(VRC.Core.PipelineManager));
            if (pipeline != null)
            {
                Object.DestroyImmediate(pipeline);
            }

            // Multiple passes: some VRC components depend on each other (RequireComponent).
            for (int pass = 0; pass < 4; pass++)
            {
                var vrcComponents = ctx.Target.GetComponentsInChildren(typeof(Component), true)
                    .Where(c => c != null && c.GetType().Name.StartsWith("VRC"))
                    // Respect the "keep PhysBones" option (e.g. converting them later by hand).
                    .Where(c => ctx.Settings.deleteConvertedPhysBones || !c.GetType().Name.Contains("PhysBone"))
                    .ToList();
                if (vrcComponents.Count == 0)
                {
                    break;
                }
                foreach (var component in vrcComponents)
                {
                    Object.DestroyImmediate(component);
                }
            }

            int remaining = ctx.Target.GetComponentsInChildren(typeof(Component), true)
                .Count(c => c != null && c.GetType().Name.StartsWith("VRC"));
            if (remaining > 0)
            {
                ctx.Report.Warning(category, $"{remaining} VRC component(s) could not be removed",
                    "Remove them manually before uploading.");
            }
            else
            {
                ctx.Report.Converted(category, "All VRC components removed");
            }
        }
    }
}
#endif
