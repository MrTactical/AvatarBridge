#if CVR_CCK_EXISTS
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
    /// avatar.
    ///
    /// The menu control is a **Slider**, 0..1, mapped GEOMETRICALLY onto 0.25×–4× of the
    /// avatar's measured height, so scale 1× sits at exactly mid-slider (0.25 × 16^0.5 = 1).
    ///
    /// It used to be an InputSingle reading in metres — a nicer unit, and unusable: the CCK's
    /// InputSingle carries only a defaultValue, no min/max/step, and ChilloutVR's quick menu
    /// renders it as a raw numeric keypad. A tester typing on it got 9999 and 0000, and the
    /// constant-speed smoothing then glided toward the garbage so slowly the avatar looked
    /// frozen. A slider drags normally on the quick menu.
    ///
    /// Geometric rather than linear because scale is multiplicative: on a linear 0.25×–4× the
    /// whole useful zone around 1× collapses into a sliver of travel, while geometric gives
    /// every doubling the same slider distance. The blend tree approximates the exponential
    /// with a knot at each √2 step (9 children), which keeps the error between knots under two
    /// percent — invisible next to the smoothing.
    /// </summary>
    public static class AvatarScalerInjector
    {
        const string Category = "Avatar scaler";
        const string ControllerGuid = "6d4ab2eb671c40f69f40f9d3f7e70cf2";
        const string HeightMenu = "Height";
        const string HeightParam = "Input";
        const string SmoothingLayer = "Linear Smoothing Layer";
        const string SizeLayer = "Size";
        const float FallbackHeight = 1.3f;

        /// <summary>Slider ends, as multiples of the measured height. 16 = 0.25 × 16^1.</summary>
        const float ScaleAtZero = 0.25f;
        const float ScaleRange = 16f;
        /// <summary>The slider position that is exactly 1× — dead centre, by construction.</summary>
        const float DefaultSlider = 0.5f;

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
                        clone.defaultFloat = DefaultSlider; // mid-slider = exactly 1× by construction
                    }
                    parameters.Add(clone);
                }
                else if (p.name == HeightParam || p.name == "Output")
                {
                    // Already present — retarget its default too.
                    var existingParam = parameters.First(x => x.name == p.name);
                    existingParam.defaultFloat = DefaultSlider;
                }
                else
                {
                    collisions.Add(p.name);
                }
            }
            master.parameters = parameters.ToArray();

            // ---- add the "Height" slider, defaulted to dead centre (= 1×) ----------------
            AddHeightMenu(ctx);

            string note = $"Slider mapped geometrically: left end {height * ScaleAtZero:0.##} m (0.25×), " +
                          $"centre {height:0.##} m (this avatar's measured height — the default, so it spawns " +
                          $"its original size), right end {height * ScaleAtZero * ScaleRange:0.##} m (4×). " +
                          "Geometric so every doubling gets the same slider travel. Constant-speed smoothing " +
                          "(JustSleightly's ControllerTemplates) so size glides instead of snapping. This " +
                          "replaces the old \"Height (M)\" typed input, which ChilloutVR's quick menu renders " +
                          "as an unclamped keypad nobody could use.";
            if (collisions.Count > 0)
            {
                note += $" NOTE: parameter name(s) already existed and were reused: {string.Join(", ", collisions)}.";
            }
            ctx.Report.Converted(Category,
                $"Avatar scaler injected — {added} layer(s), \"{HeightMenu}\" slider {height * ScaleAtZero:0.##}–{height * ScaleAtZero * ScaleRange:0.##} m",
                note);
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
        /// 1D blend tree on `Output` (the smoothed 0..1 slider) driving the root's localScale
        /// along scale(s) = ScaleAtZero × ScaleRange^s — the geometric curve. A 1D tree lerps
        /// linearly between neighbouring children, so the exponential is approximated with a
        /// knot every √2 step: nine children from 0.25× to 4×, worst-case error between knots
        /// about 1.5%, which the smoothing layer hides entirely.
        /// </summary>
        static AnimatorControllerLayer BuildSizeLayer(Vector3 baseScale, float height)
        {
            var tree = new BlendTree
            {
                name = "Size",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Output",
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };

            const int knots = 9;   // 0, 1/8 … 1 — one per ×√2
            for (int i = 0; i < knots; i++)
            {
                float slider = i / (float)(knots - 1);
                float factor = ScaleAtZero * Mathf.Pow(ScaleRange, slider);
                var clip = MakeScaleClip($"AvatarScale_{factor:0.###}x", baseScale * factor);
                tree.AddChild(clip, slider);
            }

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

        static void AddHeightMenu(BridgeContext ctx)
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
            // A Slider, not an InputSingle: the quick menu renders InputSingle as an unclamped
            // numeric keypad (the CCK type carries only a defaultValue — no min, max or step),
            // which in practice meant typing 9999 and watching nothing happen. Sliders drag.
            settings.Add(new CVRAdvancedSettingsEntry
            {
                name = HeightMenu,
                machineName = HeightParam,
                unlinkNameFromMachineName = true,
                type = CVRAdvancedSettingsEntry.SettingsType.Slider,
                setting = new CVRAdvancesAvatarSettingSlider
                {
                    defaultValue = DefaultSlider,
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
