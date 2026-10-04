using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge
{
    // What a status means, independent of skin. UI Toolkit reads it as a class suffix
    // (-info -good -warn -bad -muted); IMGUI and Handles read it through BridgeTheme.
    internal enum Tone { None, Info, Good, Warn, Bad, Muted }

    // The window's colours and generated textures.
    //
    // The palette isn't invented. Both SDKs this tool sits between carry an identity colour, and
    // they're the two ends of what AvatarBridge does:
    //
    //   VRChat     ; its SDK panel banner is blue; the accent through its stylesheets is #1778FF.
    //   ChilloutVR ; the CCK's control-panel header is an orange gradient, #EE4408 to #822A10.
    //
    // So the banner runs left-to-right from one into the other, and the step badges sample points
    // along that same ramp: step 1 sits in VRChat's blue, step 3 in ChilloutVR's orange. The
    // window ends up being a picture of the crossing it performs.
    //
    // USS has no gradient support; not in 2022.3 and not in Unity 6; so every gradient here is
    // a Texture2D generated once and handed to style.backgroundImage.
    //
    // UI Toolkit colours live in AvatarBridge.uss as tokens. The getters below are for IMGUI and
    // Handles only: a colour sampled here and put into style.color keeps the old skin's value
    // after a skin switch, where a USS class follows it.
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

        // Blue and orange are near-opposites, so interpolating straight between them drags the
        // middle through desaturated grey-brown; the ramp sagged visibly in the banner. Passing
        // through a plum keeps saturation up across the whole span, and it sits at the right
        // luminance to look like one continuous crossing rather than two colours meeting.
        static readonly Color BridgeMid = Hex(0x7A3B8C);

        // Plum straight to orange lands on crimson halfway, close enough to the bad tone that a
        // middle step badge read as an error. A rust stop keeps the warm half brown-orange.
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

        // The stretch of the ramp a window lives on. Only the converter crosses platforms, so
        // only it starts in VRChat blue; every ChilloutVR-only surface runs plum to orange.
        public sealed class Span
        {
            public static readonly Span Bridge = new Span(0f, 1f);
            public static readonly Span Cvr = new Span(0.5f, 1f);

            readonly float _from, _to;
            Texture2D _gradient;

            Span(float from, float to) { _from = from; _to = to; }

            public Color At(float t) => BridgeTheme.At(Mathf.Lerp(_from, _to, Mathf.Clamp01(t)));

            internal Texture2D Gradient()
            {
                if (_gradient == null)
                {
                    _gradient = Ramp(At, "AvatarBridge Gradient");
                }
                return _gradient;
            }
        }

        // ------------------------------------------------------------- textures ----

        public static Texture2D Gradient(Span span) => span.Gradient();

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
        public static void DrawBanner(Rect rect, string title, string subtitle, string pill, Span span)
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

            GUI.DrawTexture(rect, Gradient(span), ScaleMode.StretchToFill);

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
            // A caller may hand over a name that is already themed. Prefixing
            // that again asks Unity for "d_d_Avatar Icon", which it answers
            // with a console ERROR rather than an exception, so the catch
            // below never saw it and every repaint logged one.
            string bare = name.StartsWith("d_", System.StringComparison.Ordinal) ? name.Substring(2) : name;
            try
            {
                // FindTexture, not IconContent: both resolve a built-in icon,
                // and only IconContent shouts about a miss. A missing icon is
                // a cosmetic matter and has no business filling a console.
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
