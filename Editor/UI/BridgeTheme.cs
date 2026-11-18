using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge
{
    // What a status means, independent of skin. UI Toolkit reads it as a class suffix
    // (-info -good -warn -bad -muted); IMGUI and Handles read it through BridgeTheme.
    internal enum Tone { None, Info, Good, Warn, Bad, Muted }

    // The window's colours and generated textures. The ramp runs from VRChat's SDK blue (#1778FF)
    // to the CCK header orange (#EE4408 to #822A10); step badges sample along it.
    // USS has no gradients (2022.3 or Unity 6), so each is a generated Texture2D.
    // UI Toolkit colours are tokens in AvatarBridge.uss. The getters here are for IMGUI and
    // Handles only: a sampled colour in style.color keeps the old skin's value after a switch.
    internal static class BridgeTheme
    {
        public static bool Dark => EditorGUIUtility.isProSkin;

        public static void ApplySkin(VisualElement root, bool? dark = null)
        {
            bool isDark = dark ?? Dark;
            root.EnableInClassList("dark", isDark);
            root.EnableInClassList("light", !isDark);
            // Every skinned root is a kit root, so windows that predate Root() still match
            // the kit's .ab-root control rules.
            root.AddToClassList("ab-root");
        }

        // The two platforms, as they colour themselves. Darkened slightly from the source values
        // so white text sits on them at a comfortable contrast.
        public static readonly Color BridgeFrom = Hex(0x1B5E9E);   // VRChat blue
        public static readonly Color BridgeTo = Hex(0xC4400C);     // ChilloutVR orange

        public static Color Muted => Dark ? new Color(1f, 1f, 1f, 0.50f) : new Color(0f, 0f, 0f, 0.55f);

        // Here rather than in either window, so the converter's report and the Toolkit's agree.
        public static Tone ToneOf(ReportStatus status)
        {
            switch (status)
            {
                case ReportStatus.Error: return Tone.Bad;
                case ReportStatus.Warning: return Tone.Warn;
                case ReportStatus.Approximated: return Tone.Warn;
                case ReportStatus.Converted: return Tone.Good;
                default: return Tone.Muted;
            }
        }

        // The same values as the --ab-* tokens. Text reads on a card, a header or an inset.
        public static Color Text(Tone tone)
        {
            switch (tone)
            {
                case Tone.Good: return Dark ? Hex(0x5BE38A) : Hex(0x12561F);
                case Tone.Warn: return Dark ? Hex(0xEBCB5A) : Hex(0x5A4700);
                case Tone.Bad: return Dark ? Hex(0xFF9C94) : Hex(0x8E1515);
                case Tone.Info: return Dark ? Hex(0x8EC3FF) : Hex(0x0D428F);
                case Tone.Muted: return Dark ? Hex(0xBEBEBE) : Hex(0x333333);
                default: return Dark ? Hex(0xEEEEEE) : Hex(0x111111);
            }
        }

        // A solid stripe or selected chip; text on it is dark on the dark skin, white on light.
        public static Color Fill(Tone tone)
        {
            switch (tone)
            {
                case Tone.Good: return Dark ? Hex(0x2BCF5C) : Hex(0x1E7A34);
                case Tone.Warn: return Dark ? Hex(0xE3BE0C) : Hex(0x8A6D00);
                case Tone.Bad: return Dark ? Hex(0xF04747) : Hex(0xB02020);
                case Tone.Info: return Dark ? Hex(0x4A94FF) : Hex(0x1B5E9E);
                case Tone.Muted: return Dark ? Hex(0x9A9A9A) : Hex(0x5C5C5C);
                default: return Color.clear;
            }
        }

        // Scene-view labels sit on a black box whatever the editor skin, so always the dark fills.
        public static Color Overlay(Tone tone)
        {
            switch (tone)
            {
                case Tone.Good: return Hex(0x2BCF5C);
                case Tone.Warn: return Hex(0xE3BE0C);
                case Tone.Bad: return Hex(0xF04747);
                case Tone.Info: return Hex(0x4A94FF);
                case Tone.Muted: return Hex(0x9A9A9A);
                default: return Color.white;
            }
        }

        // Categorical series 1..4, the same values as --ab-tag-n. The kit never learns what they mean.
        public static Color TagColour(int series)
        {
            switch (series)
            {
                case 1: return Dark ? Hex(0xD7B5F7) : Hex(0x55257E);
                case 2: return Dark ? Hex(0x66DDD0) : Hex(0x07514A);
                case 3: return Dark ? Hex(0xFFB57A) : Hex(0x743200);
                case 4: return Dark ? Hex(0x8AE895) : Hex(0x16561F);
                default: return Text(Tone.Muted);
            }
        }

        // Blue straight to orange goes grey-brown in the middle; a plum stop keeps saturation up.
        static readonly Color BridgeMid = Hex(0x7A3B8C);

        // Plum to orange passes crimson, which read as an error; a rust stop avoids it.
        static readonly Color BridgeRust = Hex(0xA8481F);

        public static Color At(float t)
        {
            t = Mathf.Clamp01(t);
            if (t < 0.5f) return Color.Lerp(BridgeFrom, BridgeMid, t * 2f);
            return t < 0.75f
                ? Color.Lerp(BridgeMid, BridgeRust, (t - 0.5f) * 4f)
                : Color.Lerp(BridgeRust, BridgeTo, (t - 0.75f) * 4f);
        }

        static Color Hex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f);

        // Every surface runs the whole ramp, so a step or a tab means the same colour in every
        // window: the first on the blue end, the last on the orange.
        public static Color Step(int step, int of) => At(of <= 1 ? 0f : (step - 1) / (float)(of - 1));

        // ------------------------------------------------------------- textures ----

        static Texture2D _gradient;

        public static Texture2D Gradient()
        {
            if (_gradient == null)
            {
                _gradient = Ramp(At, "AvatarBridge Gradient");
            }
            return _gradient;
        }

        static Texture2D Ramp(System.Func<float, Color> colour, string name)
        {
            const int width = 256;
            var tex = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = name,
            };
            for (int x = 0; x < width; x++)
            {
                tex.SetPixel(x, 0, colour(x / (float)(width - 1)));
            }
            tex.Apply();
            return tex;
        }

        // ---------------------------------------------------------- IMGUI banner ----

        static GUIStyle _imTitle, _imSub, _imPill;

        // The UI Toolkit banner for IMGUI panels, at the inspector's 52px.
        public static void DrawBanner(Rect rect, string title, string subtitle, string pill)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            if (_imTitle == null)
            {
                _imTitle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 15, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0),
                    clipping = TextClipping.Clip, normal = { textColor = Color.white },
                };
                _imSub = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 11, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0),
                    clipping = TextClipping.Clip, normal = { textColor = new Color(1f, 1f, 1f, 0.90f) },
                };
                _imPill = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 10, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(8, 8, 2, 2),
                    normal = { textColor = new Color(1f, 1f, 1f, 0.95f) },
                };
            }

            GUI.DrawTexture(rect, Gradient(), ScaleMode.StretchToFill);

            const float left = 16f, right = 12f;
            float textRight = rect.xMax - right;
            if (!string.IsNullOrEmpty(pill))
            {
                var content = new GUIContent(pill);
                var size = _imPill.CalcSize(content);
                var box = new Rect(textRight - size.x, rect.center.y - size.y * 0.5f, size.x, size.y);
                GUI.DrawTexture(box, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                    new Color(0f, 0f, 0f, 0.26f), 0f, box.height * 0.5f);
                GUI.Label(box, content, _imPill);
                textRight = box.x - 8f;
            }

            float width = Mathf.Max(0f, textRight - rect.x - left);
            float titleHeight = _imTitle.CalcHeight(new GUIContent(title), width);
            bool hasSub = !string.IsNullOrEmpty(subtitle);
            float subHeight = hasSub ? _imSub.CalcHeight(new GUIContent(subtitle), width) : 0f;
            float top = rect.center.y - (titleHeight + (hasSub ? subHeight + 2f : 0f)) * 0.5f;
            GUI.Label(new Rect(rect.x + left, top, width, titleHeight), title, _imTitle);
            if (hasSub)
            {
                GUI.Label(new Rect(rect.x + left, top + titleHeight + 2f, width, subHeight), subtitle, _imSub);
            }
        }

        // ---------------------------------------------------------------- icons ----

        // tinted: the d_ glyph on both skins, for an element whose USS tint sets the colour. It is
        // light grey, so the tint lands near its own value; the light-skin glyph is dark grey, and
        // a tint only multiplies, so a white tint left it grey on a coloured tab.
        public static Texture GetIcon(string name, bool tinted = false)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            bool dark = Dark || tinted;
            // An already themed name would become "d_d_...", which logs a console error every
            // repaint instead of throwing.
            string bare = name.StartsWith("d_", System.StringComparison.Ordinal) ? name.Substring(2) : name;
            try
            {
                // FindTexture, not IconContent: only IconContent logs a miss.
                var icon = dark ? EditorGUIUtility.FindTexture("d_" + bare) : null;
                if (icon == null) icon = EditorGUIUtility.FindTexture(bare);
                // Type icons such as "Avatar Icon" live under Processed, where FindTexture never looks.
                if (icon == null) icon = EditorGUIUtility.Load($"Icons/Processed/UnityEngine/{(dark ? "d_" : "")}{bare}.asset") as Texture2D;
                return icon;
            }
            catch
            {
                return null;
            }
        }
    }
}
