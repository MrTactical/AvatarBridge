#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ABI.CCK.Components;
using ABI.CCK.Scripts;

namespace AvatarBridge
{
    /// <summary>
    /// Optional avatar scaler. Injects the bundled **Linear Smoothing Layer** (constant-speed
    /// float smoothing so size changes glide instead of snapping — JustSleightly's
    /// ControllerTemplates blend-tree math), then GENERATES a **Size** layer calibrated to this
    /// avatar: a 1D blend tree on the smoothed `Output` that drives the root's localScale so that
    /// `Height (M)` is the avatar's real eye height in metres (localScale = originalScale ×
    /// Output / measuredHeight). The "Height (M)" menu + the Input/Output defaults are set to the
    /// avatar's measured height, so at the default the avatar is exactly its pre-conversion size —
    /// height stays consistent before and after conversion.
    /// </summary>
    public static class AvatarScalerInjector
    {
        const string Category = "Avatar scaler";
        const string ControllerGuid = "6d4ab2eb671c40f69f40f9d3f7e70cf2";
        const string HeightMenu = "Height (M)";
        const string HeightParam = "Input";
        const string SmoothingLayer = "Linear Smoothing Layer";
        const string SizeLayer = "Size";
        const float MaxHeight = 10f;   // blend-tree upper threshold (metres)
        const float FallbackHeight = 1.3f;

        public static void Inject(AnimatorController master, BridgeContext ctx)
        {
            if (!ctx.Settings.addAvatarScaler)
            {
                return;
            }
            var source = LoadController();
            if (source == null)
            {
                ctx.Report.Warning(Category, "Avatar scaler selected, but its bundled assets weren't found",
                    "Reimport AvatarBridge so Assets/AvatarBridge/AvatarScaler is present, then convert again.");
                return;
            }

            float height = MeasureHeight(ctx);
            Vector3 baseScale = ctx.Target.transform.localScale;

            // ---- copy the smoothing layer (bundled) + generate the Size layer -----------
            var copier = new AnimatorDeepCopier();
            var layers = master.layers.ToList();
            var existing = new HashSet<string>(layers.Select(l => l.name));
            int added = 0;
            foreach (var srcLayer in source.layers)
            {
                if (srcLayer.name == SizeLayer)
                {
                    continue; // we generate our own, avatar-calibrated Size layer
                }
                var clone = copier.CloneLayer(srcLayer);
                clone.name = UniqueName(srcLayer.name, existing);
                clone.defaultWeight = srcLayer.defaultWeight <= 0f ? 1f : srcLayer.defaultWeight;
                layers.Add(clone);
                added++;
            }
            var sizeLayer = BuildSizeLayer(baseScale, height);
            sizeLayer.name = UniqueName(SizeLayer, existing);
            layers.Add(sizeLayer);
            added++;
            master.layers = layers.ToArray();

            // ---- copy the parameters, defaulting Input/Output to the measured height ----
            var parameters = master.parameters.ToList();
            var have = new HashSet<string>(parameters.Select(p => p.name));
            var collisions = new List<string>();
            foreach (var p in source.parameters)
            {
                if (have.Add(p.name))
                {
                    var clone = AnimatorDeepCopier.CloneParameter(p);
                    if (clone.name == HeightParam || clone.name == "Output")
                    {
                        clone.defaultFloat = height; // start settled at the avatar's real height
                    }
                    parameters.Add(clone);
                }
                else if (p.name == HeightParam || p.name == "Output")
                {
                    // Already present — retarget its default too.
                    var existingParam = parameters.First(x => x.name == p.name);
                    existingParam.defaultFloat = height;
                }
                else
                {
                    collisions.Add(p.name);
                }
            }
            master.parameters = parameters.ToArray();

            // ---- add the "Height (M)" Advanced Avatar Setting, defaulted to the height ---
            AddHeightMenu(ctx, height);

            string note = $"\"Height (M)\" defaults to this avatar's measured eye height ({height:0.##} m), so it's " +
                          "the same size before and after conversion; change the menu value to scale. Constant-speed " +
                          "smoothing (JustSleightly's ControllerTemplates) so size glides instead of snapping.";
            if (collisions.Count > 0)
            {
                note += $" NOTE: parameter name(s) already existed and were reused: {string.Join(", ", collisions)}.";
            }
            ctx.Report.Converted(Category, $"Avatar scaler injected — {added} layer(s), \"{HeightMenu}\" = {height:0.##} m", note);
        }

        /// <summary>Avatar eye height in metres (the CVR/VRChat "height"), from the viewpoint.</summary>
        static float MeasureHeight(BridgeContext ctx)
        {
            if (ctx.CvrAvatar != null)
            {
                float scaleY = ctx.Target.transform.localScale.y;
                float eye = ctx.CvrAvatar.viewPosition.y * (Mathf.Approximately(scaleY, 0f) ? 1f : scaleY);
                if (eye > 0.2f && eye < 6f)
                {
                    return Mathf.Round(eye * 100f) / 100f; // clean 2-decimal metres
                }
            }
            return FallbackHeight;
        }

        /// <summary>
        /// 1D blend tree on `Output` (smoothed height, metres) driving the root's localScale so
        /// that localScale = baseScale × Output / height — i.e. Output metres of eye height. Two
        /// clips on that line (0 → zero scale, MaxHeight → the matching scale) give it exactly.
        /// </summary>
        static AnimatorControllerLayer BuildSizeLayer(Vector3 baseScale, float height)
        {
            Vector3 maxScale = baseScale * (MaxHeight / height);
            var minClip = MakeScaleClip("AvatarScale_0", Vector3.zero);
            var maxClip = MakeScaleClip("AvatarScale_Max", maxScale);

            var tree = new BlendTree
            {
                name = "Size",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Output",
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };
            tree.AddChild(minClip, 0f);
            tree.AddChild(maxClip, MaxHeight);

            var machine = new AnimatorStateMachine { name = "Size", hideFlags = HideFlags.HideInHierarchy };
            var state = machine.AddState("Blend Tree");
            state.writeDefaultValues = true;
            state.motion = tree;
            machine.defaultState = state;

            return new AnimatorControllerLayer { name = "Size", defaultWeight = 1f, stateMachine = machine };
        }

        static AnimationClip MakeScaleClip(string name, Vector3 scale)
        {
            var clip = new AnimationClip { name = name };
            clip.SetCurve("", typeof(Transform), "m_LocalScale.x", AnimationCurve.Constant(0f, 1f / 60f, scale.x));
            clip.SetCurve("", typeof(Transform), "m_LocalScale.y", AnimationCurve.Constant(0f, 1f / 60f, scale.y));
            clip.SetCurve("", typeof(Transform), "m_LocalScale.z", AnimationCurve.Constant(0f, 1f / 60f, scale.z));
            return clip;
        }

        static string UniqueName(string name, HashSet<string> taken)
        {
            string candidate = name;
            int suffix = 2;
            while (!taken.Add(candidate))
            {
                candidate = $"{name} {suffix++}";
            }
            return candidate;
        }

        static void AddHeightMenu(BridgeContext ctx, float defaultValue)
        {
            if (ctx.CvrAvatar == null || ctx.CvrAvatar.avatarSettings == null
                || ctx.CvrAvatar.avatarSettings.settings == null)
            {
                return;
            }
            var settings = ctx.CvrAvatar.avatarSettings.settings;
            if (settings.Any(s => s != null && s.machineName == HeightParam))
            {
                return; // already exposed
            }
            settings.Add(new CVRAdvancedSettingsEntry
            {
                name = HeightMenu,
                machineName = HeightParam,
                unlinkNameFromMachineName = true,
                type = CVRAdvancedSettingsEntry.SettingsType.InputSingle,
                setting = new CVRAdvancesAvatarSettingInputSingle
                {
                    defaultValue = defaultValue,
                    usedType = CVRAdvancesAvatarSettingBase.ParameterType.Float
                }
            });
        }

        static AnimatorController LoadController()
        {
            string path = AssetDatabase.GUIDToAssetPath(ControllerGuid);
            if (string.IsNullOrEmpty(path))
            {
                foreach (var guid in AssetDatabase.FindAssets("AvatarScaler t:AnimatorController"))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (p.EndsWith("AvatarScaler.controller"))
                    {
                        path = p;
                        break;
                    }
                }
            }
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        }
    }
}
#endif
