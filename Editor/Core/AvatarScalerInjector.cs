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
        // The template's other names (Output, One, StepSize...) are generic
        // enough for an avatar's own system to use, and sharing one drives
        // both at once. They are local scaffolding, so they get our own.
        const string Prefix = "#AvatarScaler/";
        const string OutputParam = Prefix + "Output";
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

            // "Height" is kept on purpose (see the note below), so an avatar
            // with its own would have it taken over: default forced to mid
            // range, and its own menu entry quietly driving the scaler.
            var menu = ctx.CvrAvatar != null && ctx.CvrAvatar.avatarSettings != null
                ? ctx.CvrAvatar.avatarSettings.settings : null;
            if (master.parameters.Any(p => p.name == HeightParam)
                || (menu != null && menu.Any(s => s != null && s.machineName == HeightParam)))
            {
                ctx.Report.Warning(Category, "Avatar scaler not added: the avatar already has its own \"Height\"",
                    "The scaler's slider is called \"Height\", and sharing the name would drive the avatar's " +
                    "own system and the scaler together. Rename the avatar's \"Height\" parameter to add the slider.");
                return;
            }

            float height = MeasureHeight(ctx);
            Vector3 baseScale = ctx.Target.transform.localScale;

            // ---- copy the smoothing layer (bundled) + generate the Size layer -----------
            var copier = new AnimatorDeepCopier();
            var layers = master.layers.ToList();
            var existing = new HashSet<string>(layers.Select(l => l.name));
            // The bundled template still says "Input", "Output" and so on internally;
            // the parameters are renamed, so every reference in the clone has to follow.
            var names = source.parameters.ToDictionary(p => p.name, p => Renamed(p.name));
            var clips = new Dictionary<AnimationClip, AnimationClip>();
            int added = 0;
            foreach (var srcLayer in source.layers)
            {
                if (srcLayer.name == SizeLayer)
                {
                    continue; // replaced by a generated, avatar-calibrated Size layer
                }
                var clone = copier.CloneLayer(srcLayer);
                clone.name = UniqueName(srcLayer.name, existing);
                clone.defaultWeight = srcLayer.defaultWeight <= 0f ? 1f : srcLayer.defaultWeight;
                RenameParameterReferences(clone.stateMachine, names, clips);
                layers.Add(clone);
                added++;
            }
            var sizeLayer = BuildSizeLayer(baseScale, height);
            sizeLayer.name = UniqueName(SizeLayer, existing);
            layers.Add(sizeLayer);
            added++;
            master.layers = layers.ToArray();

            // ---- copy the parameters, defaulting Height/Output to mid-slider -----------
            var parameters = master.parameters.ToList();
            var have = new HashSet<string>(parameters.Select(p => p.name));
            var collisions = new List<string>();
            foreach (var p in source.parameters)
            {
                string targetName = names[p.name];
                if (have.Add(targetName))
                {
                    var clone = AnimatorDeepCopier.CloneParameter(p);
                    clone.name = targetName;
                    if (targetName == HeightParam || targetName == OutputParam)
                    {
                        clone.defaultFloat = DefaultSlider; // mid-slider = exactly 1× by construction
                    }
                    parameters.Add(clone);
                }
                else
                {
                    collisions.Add(targetName);
                }
            }
            master.parameters = parameters.ToArray();

            // ---- add the "Height" slider, defaulted to dead centre (= 1×) ----------------
            AddHeightMenu(ctx);

            string note = $"Slider mapped geometrically: left end {height * ScaleAtZero:0.##} m (0.25×), " +
                          $"centre {height:0.##} m (this avatar's measured height: the default, so it spawns " +
                          $"its original size), right end {height * ScaleAtZero * ScaleRange:0.##} m (4×). " +
                          "Geometric so every doubling gets the same slider travel. Constant-speed smoothing " +
                          "(JustSleightly's ControllerTemplates) so size glides instead of snapping. This " +
                          "replaces the old \"Height (M)\" typed input, which ChilloutVR's quick menu renders " +
                          "as an unclamped keypad nobody could use. The parameter is named \"Height\" (not the " +
                          "template's \"Input\") because ChilloutVR restores saved profile values by parameter " +
                          "name: a re-upload keeping the old name inherited the metres-era value and spawned " +
                          "at 250%.";
            if (collisions.Count > 0)
            {
                note += $" NOTE: parameter name(s) already existed and were reused: {string.Join(", ", collisions)}.";
            }
            ctx.Report.Converted(Category,
                $"Avatar scaler injected: {added} layer(s), \"{HeightMenu}\" slider {height * ScaleAtZero:0.##}–{height * ScaleAtZero * ScaleRange:0.##} m",
                note);
        }

        internal static float MeasureHeight(BridgeContext ctx)
        {
            if (ctx.CvrAvatar != null)
            {
                // Already metres: the CCK stores the viewpoint scaled by the
                // root (see AvatarFeatureDetect.CckGizmoWorldPoint).
                float eye = ctx.CvrAvatar.viewPosition.y;
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
                blendParameter = OutputParam,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };

            const int knots = 9;   // 0, 1/8 … 1, one per ×√2
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

        // The menu parameter keeps its "Height"; everything else is scaffolding.
        static string Renamed(string template) => template == TemplateParam ? HeightParam : Prefix + template;

        static bool Rename(Dictionary<string, string> names, string from, out string to)
        {
            to = from;
            return from != null && names.TryGetValue(from, out to);
        }

        // `clips` is shared across calls so a clip used twice is cloned once.
        internal static void RenameParameterReferences(AnimatorStateMachine machine, Dictionary<string, string> names,
            Dictionary<AnimationClip, AnimationClip> clips)
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
                if (Rename(names, state.timeParameter, out string time)) state.timeParameter = time;
                if (Rename(names, state.speedParameter, out string speed)) state.speedParameter = speed;
                if (Rename(names, state.mirrorParameter, out string mirror)) state.mirrorParameter = mirror;
                if (Rename(names, state.cycleOffsetParameter, out string offset)) state.cycleOffsetParameter = offset;
                state.motion = RenameInMotion(state.motion, names, clips);
                foreach (var transition in state.transitions)
                {
                    RenameInConditions(transition, names);
                }
            }
            foreach (var transition in machine.anyStateTransitions)
            {
                RenameInConditions(transition, names);
            }
            foreach (var transition in machine.entryTransitions)
            {
                RenameInConditions(transition, names);
            }
            foreach (var child in machine.stateMachines)
            {
                RenameParameterReferences(child.stateMachine, names, clips);
            }
        }

        static Motion RenameInMotion(Motion motion, Dictionary<string, string> names,
            Dictionary<AnimationClip, AnimationClip> clips)
        {
            if (motion is AnimationClip clip)
            {
                return RenameInClip(clip, names, clips);
            }
            if (!(motion is BlendTree tree))
            {
                return motion;
            }
            if (Rename(names, tree.blendParameter, out string x)) tree.blendParameter = x;
            if (Rename(names, tree.blendParameterY, out string y)) tree.blendParameterY = y;
            // children is a value-type array copy: mutate it, recurse, write it back.
            var children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                if (Rename(names, children[i].directBlendParameter, out string direct))
                {
                    children[i].directBlendParameter = direct;
                }
                children[i].motion = RenameInMotion(children[i].motion, names, clips);
            }
            tree.children = children;
            return tree;
        }

        // The template's clips write Output and the delta as Animator curves,
        // which renaming the layer never reaches. Cloned, never edited: they
        // are the package's own assets.
        static AnimationClip RenameInClip(AnimationClip clip, Dictionary<string, string> names,
            Dictionary<AnimationClip, AnimationClip> clips)
        {
            if (clips.TryGetValue(clip, out var done))
            {
                return done;
            }
            var renames = AnimationUtility.GetCurveBindings(clip)
                .Where(b => b.type == typeof(Animator) && string.IsNullOrEmpty(b.path) && names.ContainsKey(b.propertyName))
                .ToArray();
            var result = clip;
            if (renames.Length > 0)
            {
                result = Object.Instantiate(clip);
                result.name = clip.name;
                foreach (var binding in renames)
                {
                    var curve = AnimationUtility.GetEditorCurve(result, binding);
                    AnimationUtility.SetEditorCurve(result, binding, null);
                    var renamed = binding;
                    renamed.propertyName = names[binding.propertyName];
                    AnimationUtility.SetEditorCurve(result, renamed, curve);
                }
            }
            clips[clip] = result;
            return result;
        }

        static void RenameInConditions(AnimatorTransitionBase transition, Dictionary<string, string> names)
        {
            if (transition == null)
            {
                return;
            }
            var conditions = transition.conditions;
            bool changed = false;
            for (int i = 0; i < conditions.Length; i++)
            {
                if (Rename(names, conditions[i].parameter, out string to))
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
