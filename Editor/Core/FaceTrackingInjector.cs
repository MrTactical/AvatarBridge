#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    /// <summary>
    /// DragonSkyRunner face-tracking mode: copies the package's "Face Tracking Layers"
    /// controller — its layers and parameters — into the generated CVR animator, exactly
    /// like the readme's "box-select the layer, Ctrl+C / Ctrl+V into your animator" plus
    /// adding the parameters. The animations drive Unified-Expressions blendshapes on the
    /// avatar's "Body" mesh directly (no binary encoding, no smoothing), so it's light and
    /// CVR-native. Any existing FT rig is stripped first (see AnimatorMerger).
    /// </summary>
    public static class FaceTrackingInjector
    {
        const string Category = "Face tracking";

        // Float parameters the readme says must not be left at 0.
        static readonly Dictionary<string, float> RequiredDefaults = new Dictionary<string, float>
        {
            { "#Direct", 1f },
            { "LeftEyeLidExpandedSqueeze", 0.8f },
            { "RightEyeLidExpandedSqueeze", 0.8f },
            { "EyesDilation", 0.5f }
        };

        public static void Inject(AnimatorController master, BridgeContext ctx)
        {
            if (ctx.Settings.faceTrackingMode != FaceTrackingMode.DragonSkyRunner)
            {
                return;
            }
            var source = FaceTrackingPackages.LoadController();
            if (source == null)
            {
                ctx.Report.Error(Category, "DragonSkyRunner FT selected, but its controller wasn't found",
                    $"Import \"{FaceTrackingPackages.DisplayName}\" and convert again.");
                return;
            }

            // Copy layers (deep-copied so the source asset is never mutated; the animation
            // clips stay referenced from the package, which is stable).
            var copier = new AnimatorDeepCopier();
            var layers = master.layers.ToList();
            var existingNames = new HashSet<string>(layers.Select(l => l.name));
            int addedLayers = 0;
            foreach (var srcLayer in source.layers)
            {
                var clone = copier.CloneLayer(srcLayer);
                string name = "[FT] " + srcLayer.name;
                int suffix = 2;
                while (!existingNames.Add(name))
                {
                    name = $"[FT] {srcLayer.name} {suffix++}";
                }
                clone.name = name;
                clone.defaultWeight = srcLayer.defaultWeight <= 0f ? 1f : srcLayer.defaultWeight;
                layers.Add(clone);
                addedLayers++;
            }
            master.layers = layers.ToArray();

            // Copy parameters (verbatim names — "#Direct" stays local, "v2/..." floats and
            // the EyeTracking/FaceTracking bools sync, matching the package's design).
            var parameters = master.parameters.ToList();
            var have = new HashSet<string>(parameters.Select(p => p.name));
            int addedParams = 0;
            foreach (var p in source.parameters)
            {
                if (have.Add(p.name))
                {
                    parameters.Add(AnimatorDeepCopier.CloneParameter(p));
                    addedParams++;
                }
            }
            // Apply the required non-zero defaults.
            foreach (var param in parameters)
            {
                if (RequiredDefaults.TryGetValue(param.name, out var value))
                {
                    param.defaultFloat = value;
                }
            }
            master.parameters = parameters.ToArray();

            ctx.Report.Converted(Category,
                $"Injected DragonSkyRunner FT — {addedLayers} layer(s), {addedParams} parameter(s)",
                "Copied from the package's \"Face Tracking Layers\" controller. Assumes Unified-Expressions " +
                "blendshapes on a mesh named \"Body\"; set up the Eye/Face Tracking toggles in Advanced Avatar " +
                "Settings and tune the eye-gaze animations per the package readme.");
        }
    }
}
#endif
