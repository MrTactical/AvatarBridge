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
    /// Optional avatar scaler. Injects a bundled two-layer rig — a **Linear Smoothing Layer**
    /// (constant-speed float smoothing so size changes glide instead of snapping, built on
    /// JustSleightly's ControllerTemplates blend-tree math) and a **Size** layer (maps the
    /// smoothed value onto the avatar root's localScale + CVR's #MotionScale) — plus a
    /// "Height (M)" Advanced Avatar Setting that writes the target height.
    ///
    /// The scale endpoints in the Size clips are calibrated to the source avatar, so the
    /// mechanism is portable but the meters↔size values are a per-avatar tweak (reported).
    /// Bundled under Assets/AvatarBridge/AvatarScaler; nothing is repathed (the scale clips
    /// target the root, path "").
    /// </summary>
    public static class AvatarScalerInjector
    {
        const string Category = "Avatar scaler";
        const string ControllerGuid = "6d4ab2eb671c40f69f40f9d3f7e70cf2";
        const string HeightMenu = "Height (M)";
        const string HeightParam = "Input";

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

            // ---- copy the two layers ---------------------------------------------------
            var copier = new AnimatorDeepCopier();
            var layers = master.layers.ToList();
            var existing = new HashSet<string>(layers.Select(l => l.name));
            int added = 0;
            foreach (var srcLayer in source.layers)
            {
                var clone = copier.CloneLayer(srcLayer);
                string name = srcLayer.name;
                int suffix = 2;
                while (!existing.Add(name))
                {
                    name = $"{srcLayer.name} {suffix++}";
                }
                clone.name = name;
                clone.defaultWeight = srcLayer.defaultWeight <= 0f ? 1f : srcLayer.defaultWeight;
                layers.Add(clone);
                added++;
            }
            master.layers = layers.ToArray();

            // ---- copy the parameters (Input/Output/InputOutputDelta/One/StepSize) ------
            var parameters = master.parameters.ToList();
            var have = new HashSet<string>(parameters.Select(p => p.name));
            var collisions = new List<string>();
            float inputDefault = 1.3f;
            foreach (var p in source.parameters)
            {
                if (p.name == HeightParam)
                {
                    inputDefault = p.defaultFloat;
                }
                if (have.Add(p.name))
                {
                    parameters.Add(AnimatorDeepCopier.CloneParameter(p));
                }
                else
                {
                    collisions.Add(p.name);
                }
            }
            master.parameters = parameters.ToArray();

            // ---- add the "Height (M)" Advanced Avatar Setting --------------------------
            AddHeightMenu(ctx, inputDefault);

            string note = "Constant-speed smoothing so size glides instead of snapping (JustSleightly's " +
                          "ControllerTemplates math). Scale endpoints are calibrated to the source avatar — " +
                          "tune the Anim_AvatarScale_Slider_Min/Max clips + Input default per avatar.";
            if (collisions.Count > 0)
            {
                note += $" NOTE: parameter name(s) already existed and were reused: {string.Join(", ", collisions)} " +
                        "— check for conflicts.";
            }
            ctx.Report.Converted(Category, $"Avatar scaler injected — {added} layer(s), \"{HeightMenu}\" menu", note);
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
