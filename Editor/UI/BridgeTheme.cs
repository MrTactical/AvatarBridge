using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge
{
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
    internal static class BridgeTheme
    {
        public static bool Dark => EditorGUIUtility.isProSkin;

        public static void ApplySkin(VisualElement root, bool? dark = null)
        {
            bool isDark = dark ?? Dark;
            root.EnableInClassList("dark", isDark);
            root.EnableInClassList("light", !isDark);
        }

        // The two platforms, as they colour themselves. Darkened slightly from the source values
        // so white text sits on them at a comfortable contrast.
        public static readonly Color BridgeFrom = Hex(0x1B5E9E);   // VRChat blue
        public static readonly Color BridgeTo = Hex(0xC4400C);     // ChilloutVR orange

        public static readonly Color VrcBlue = Hex(0x1778FF);
        public static readonly Color CvrOrange = Hex(0xEE4408);

        // VRChat's own status colours, reused so a warning here looks like a warning there.
        //
        // Darkened on the light skin. These are used as text on a pale chip, and #E3BE0C yellow
        // on a light grey background is close to unreadable; the dark-skin values are chosen to
        // glow against near-black, which is the opposite problem.
        public static Color Good => Dark ? Hex(0x2BCF5C) : Hex(0x1E7A34);
        public static Color Warn => Dark ? Hex(0xE3BE0C) : Hex(0x8A6D00);
        public static Color Bad => Dark ? Hex(0xF04747) : Hex(0xB02020);

        public static Color Muted => Dark ? new Color(1f, 1f, 1f, 0.50f) : new Color(0f, 0f, 0f, 0.55f);

        // Blue and orange are near-opposites, so interpolating straight between them drags the
        // middle through desaturated grey-brown; the ramp sagged visibly in the banner. Passing
        // through a plum keeps saturation up across the whole span, and it sits at the right
        // luminance to look like one continuous crossing rather than two colours meeting.
        static readonly Color BridgeMid = Hex(0x7A3B8C);

        public static Color At(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f
                ? Color.Lerp(BridgeFrom, BridgeMid, t * 2f)
                : Color.Lerp(BridgeMid, BridgeTo, (t - 0.5f) * 2f);
        }

        static Color Hex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f);

        // ------------------------------------------------------------- textures ----

        static Texture2D _bridge, _bridgeHover;

        public static Texture2D BridgeGradient(bool bright = false)
        {
            if (bright && _bridgeHover != null)
            {
                return _bridgeHover;
            }
            if (!bright && _bridge != null)
            {
                return _bridge;
            }
            const int width = 256;
            var tex = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "AvatarBridge Gradient",
            };
            for (int x = 0; x < width; x++)
            {
                var c = At(x / (float)(width - 1));
                tex.SetPixel(x, 0, bright ? Color.Lerp(c, Color.white, 0.14f) : c);
            }
            tex.Apply();
            if (bright) { _bridgeHover = tex; } else { _bridge = tex; }
            return tex;
        }

        public static Texture2D Solid(Color colour)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "AvatarBridge Solid",
            };
            tex.SetPixel(0, 0, colour);
            tex.Apply();
            return tex;
        }

        // ---------------------------------------------------------------- icons ----

        public static Texture GetIcon(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            try
            {
                var content = EditorGUIUtility.IconContent(Dark ? "d_" + name : name);
                if (content?.image != null)
                {
                    return content.image;
                }
                return EditorGUIUtility.IconContent(name)?.image;
            }
            catch
            {
                return null;
            }
        }
    }
}
