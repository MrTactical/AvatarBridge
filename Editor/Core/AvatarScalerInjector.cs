#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using ABI.CCK.Components;
using ABI.CCK.Scripts;

namespace AvatarBridge
{
    // Optional avatar scaler: the bundled smoothing layer plus a generated
    // Size layer. The menu control is a slider, 0..1, mapped geometrically
    // onto 0.25x to 4x of the measured height, so 1x sits at mid-slider. The
    // blend tree approximates the exponential with a knot each sqrt(2) step.
    public static class AvatarScalerInjector
    {
        const string Category = "Avatar scaler";
        const string ControllerGuid = "6d4ab2eb671c40f69f40f9d3f7e70cf2";
        const string HeightMenu = "Height";

        const string HeightParam = "Height";
        internal const string TemplateParam = "Input";
        internal const string SmoothingLayer = "Linear Smoothing Layer";
        const string SizeLayer = "Size";
        const float FallbackHeight = 1.3f;

        const float ScaleAtZero = 0.25f;
        const float ScaleRange = 16f;
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
                // The bundled template still says "Input" internally; the parameter is renamed
                // (see HeightParam), so every reference in the clone has to follow.
                RenameParameterReferences(clone.stateMachine, TemplateParam, HeightParam);
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
                // The template's menu parameter is called "Input"; ours is renamed; see the
                // HeightParam comment for the 250%-slider story that forced it.
                string targetName = p.name == TemplateParam ? HeightParam : p.name;
                if (have.Add(targetName))
                {
                    var clone = AnimatorDeepCopier.CloneParameter(p);
                    clone.name = targetName;
                    if (targetName == HeightParam || targetName == "Output")
                    {
                        clone.defaultFloat = DefaultSlider; // mid-slider = exactly 1× by construction
                    }
                    parameters.Add(clone);
                }
                else if (targetName == HeightParam || targetName == "Output")
                {
                    // Already present; retarget its default too.
                    var existingParam = parameters.First(x => x.name == targetName);
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
                          "as an unclamped keypad nobody could use. The parameter is named \"Height\" (not the " +
                          "template's \"Input\") because ChilloutVR restores saved profile values by parameter " +
                          "name — a re-upload keeping the old name inherited the metres-era value and spawned " +
                          "at 250%.";
            if (collisions.Count > 0)
            {
                note += $" NOTE: parameter name(s) already existed and were reused: {string.Join(", ", collisions)}.";
            }
            ctx.Report.Converted(Category,
                $"Avatar scaler injected — {added} layer(s), \"{HeightMenu}\" slider {height * ScaleAtZero:0.##}–{height * ScaleAtZero * ScaleRange:0.##} m",
                note);
        }

        internal static float MeasureHeight(BridgeContext ctx)
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

        internal static void RenameParameterReferences(AnimatorStateMachine machine, string from, string to)
        {
            if (machine == null)
            {
                return;
            }
            foreach (var child in machine.states)
            {
                var state = child.state;
                if (state == null)
                {
                    continue;
                }
                if (state.timeParameter == from) state.timeParameter = to;
                if (state.speedParameter == from) state.speedParameter = to;
                if (state.mirrorParameter == from) state.mirrorParameter = to;
                if (state.cycleOffsetParameter == from) state.cycleOffsetParameter = to;
                RenameInMotion(state.motion, from, to);
                foreach (var transition in state.transitions)
                {
                    RenameInConditions(transition, from, to);
                }
            }
            foreach (var transition in machine.anyStateTransitions)
            {
                RenameInConditions(transition, from, to);
            }
            foreach (var transition in machine.entryTransitions)
            {
                RenameInConditions(transition, from, to);
            }
            foreach (var child in machine.stateMachines)
            {
                RenameParameterReferences(child.stateMachine, from, to);
            }
        }

        static void RenameInMotion(Motion motion, string from, string to)
        {
            if (!(motion is BlendTree tree))
            {
                return;
            }
            if (tree.blendParameter == from) tree.blendParameter = to;
            if (tree.blendParameterY == from) tree.blendParameterY = to;
            // children is a value-type array copy: mutate it, recurse, write it back.
            var children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].directBlendParameter == from)
                {
                    children[i].directBlendParameter = to;
                }
                RenameInMotion(children[i].motion, from, to);
            }
            tree.children = children;
        }

        static void RenameInConditions(AnimatorTransitionBase transition, string from, string to)
        {
            if (transition == null)
            {
                return;
            }
            var conditions = transition.conditions;
            bool changed = false;
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i].parameter == from)
                {
                    conditions[i].parameter = to;
                    changed = true;
                }
            }
            if (changed)
            {
                transition.conditions = conditions;
            }
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
            // numeric keypad (the CCK type carries only a defaultValue; no min, max or step),
            // which in practice meant typing 9999 and watching nothing happen. Sliders drag.
            // First in the menu: it is the one everybody reaches for, and the
            // end of a long list is where nobody looks.
            settings.Insert(0, new CVRAdvancedSettingsEntry
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

        internal static AnimatorController LoadController()
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
