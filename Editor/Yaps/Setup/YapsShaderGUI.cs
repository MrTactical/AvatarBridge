// The material panel for anything wearing YAPS: the knobs grouped and
// named the way a user thinks, drawn like a product rather than a debug
// dump, with the shader's ORIGINAL panel underneath, untouched.
//
// Wrapping rather than replacing is the whole trick. The patcher injects
// `CustomEditor "AvatarBridge.YapsShaderGUI"` into every shader it
// patches and remembers the editor the shader HAD (Poiyomi's ThryEditor,
// Standard's, or none) in a hidden property's description. This panel
// draws the YAPS sections, then instantiates that original editor by
// name and hands it every property that is not ours — so a Poiyomi user
// keeps Poiyomi's panel entire, with YAPS sitting above it. Same panel
// whether the plug wears Poiyomi, Standard, or the test shader: the
// property names are the wire format and they do not change.
//
// Presentation, since that was the complaint: a banner in the family's
// gradient, coloured section headers that fold, a switch drawn as a
// switch, sliders labelled with what the number MEANS and which system a
// user knows the knob from, a one-line "what this is" under each section,
// and the channel's internals genuinely out of the way — visible on
// request, never editable.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public class YapsShaderGUI : ShaderGUI
    {
        public const string OriginalEditorProperty = "_YAPS_OriginalEditor";

        static readonly Dictionary<string, ShaderGUI> Originals = new Dictionary<string, ShaderGUI>();
        static readonly Dictionary<string, bool> Open = new Dictionary<string, bool>();

        // --- what a row is -------------------------------------------------

        enum RowKind { Slider, Toggle, Float, Vector, Enum, Texture }

        struct Knob
        {
            public string Name, Label, From, Help;
            public RowKind Kind;
            public Knob(string name, string label, RowKind kind, string from = null, string help = null)
            { Name = name; Label = label; Kind = kind; From = from; Help = help; }
        }

        struct Section
        {
            public string Title, Blurb;
            public Color Tint;
            public Knob[] Knobs;
            public bool StartOpen;
        }

        // Section tints: a walk along the family gradient, so the panel
        // reads as one thing rather than a list. Muted, they are headers.
        static readonly Color TintPlug   = new Color(0.32f, 0.55f, 0.85f);
        static readonly Color TintRest   = new Color(0.55f, 0.45f, 0.85f);
        static readonly Color TintIn     = new Color(0.85f, 0.40f, 0.60f);
        static readonly Color TintIdle   = new Color(0.85f, 0.55f, 0.35f);
        static readonly Color TintMotion = new Color(0.85f, 0.70f, 0.30f);
        static readonly Color TintCurve  = new Color(0.40f, 0.75f, 0.60f);
        static readonly Color TintWho    = new Color(0.45f, 0.65f, 0.75f);
        static readonly Color TintSocket = new Color(0.30f, 0.80f, 0.85f);
        static readonly Color TintDebug  = new Color(0.55f, 0.55f, 0.55f);

        static readonly Section[] Sections =
        {
            new Section { Title = "Plug", Tint = TintPlug, StartOpen = true,
                Blurb = "The basics. Length is measured from the mesh by the bake.",
                Knobs = new[]
                {
                    new Knob("_YAPS_Enabled", "Deform on", RowKind.Toggle),
                    new Knob("_YAPS_Length", "Length", RowKind.Float, help: "metres, from the bake"),
                    new Knob("_YAPS_Overrun", "Carry on through a ring", RowKind.Toggle, "SPS",
                        "On, the tip keeps going past a ring. Off, the shaft stops at every socket."),
                    new Knob("_YAPS_TaperStart", "Hole taper begins", RowKind.Slider, help: "how far past a hole before the shaft narrows, as a fraction of length"),
                    new Knob("_YAPS_TaperEnd", "Hole taper closes by", RowKind.Slider, help: "…and how far before it has closed to a point"),
                }},
            new Section { Title = "Shape at rest", Tint = TintRest,
                Blurb = "How the shaft sits when nothing is bending it.",
                Knobs = new[]
                {
                    new Knob("_YAPS_Curvature", "Curvature", RowKind.Slider, "DPS", "a resting bend along the whole shaft — positive bends up"),
                    new Knob("_YAPS_ReCurvature", "Recurvature", RowKind.Slider, "DPS", "a second bend gathered at the tip, opposite in sign: sweep, then hook"),
                    new Knob("_YAPS_EntranceStiffness", "Entrance stiffness", RowKind.Slider, "DPS", "how much the base resists bending toward a socket — 0 bends evenly from the root"),
                }},
            new Section { Title = "When it's in", Tint = TintIn,
                Blurb = "What a socket does to the shaft at the opening.",
                Knobs = new[]
                {
                    new Knob("_YAPS_Squeeze", "Squeeze", RowKind.Slider, "DPS · TPS", "how much the socket narrows the shaft where it grips"),
                    new Knob("_YAPS_SqueezeDistance", "Squeeze reach", RowKind.Slider, "DPS · TPS", "how far either side of the opening the grip reaches, as a fraction of length"),
                    new Knob("_YAPS_Bulge", "Bulge", RowKind.Slider, "DPS · TPS", "the swell just short of the opening, as a fraction of radius"),
                    new Knob("_YAPS_BulgeDistance", "Bulge reach", RowKind.Slider, "DPS · TPS", "how far before the opening the swell begins"),
                }},
            new Section { Title = "When it's not", Tint = TintIdle,
                Blurb = "What the shaft does with nothing using it.",
                Knobs = new[]
                {
                    new Knob("_YAPS_IdleLength", "Idle length", RowKind.Slider, "TPS", "how much of its length it keeps — 1 is no change"),
                    new Knob("_YAPS_IdleWidth", "Idle width", RowKind.Slider, "TPS", "how much of its width it keeps"),
                    new Knob("_YAPS_WriggleStrength", "Wriggle", RowKind.Slider, "DPS", "idle motion, tip-heavy"),
                    new Knob("_YAPS_WriggleSpeed", "Wriggle speed", RowKind.Slider, "DPS"),
                }},
            new Section { Title = "Motion", Tint = TintMotion,
                Blurb = "Motion while engaged.",
                Knobs = new[]
                {
                    new Knob("_YAPS_PumpStrength", "Pumping", RowKind.Slider, "TPS", "a stroke along the shaft, only while engaged"),
                    new Knob("_YAPS_PumpSpeed", "Pumping speed", RowKind.Slider, "TPS"),
                    new Knob("_YAPS_PumpWidth", "Pumping width", RowKind.Slider, "TPS", "how much of the shaft pumps — 1 is the whole length, small values move only the tip"),
                }},
            new Section { Title = "The curve", Tint = TintCurve,
                Blurb = "How the shaft arrives at a socket.",
                Knobs = new[]
                {
                    new Knob("_YAPS_BezierSmoothness", "Smoothness", RowKind.Slider, "TPS", "below 1 arrives more directly; above 1 sweeps a wider arc"),
                    new Knob("_YAPS_BezierStart", "Straight before bend", RowKind.Slider, "TPS", "a fraction of the shaft held perfectly straight before any bend"),
                    new Knob("_YAPS_SmoothStart", "Ease into bend", RowKind.Slider, "TPS", "ease the join between the straight part and the curve rather than kink"),
                    new Knob("_YAPS_MinimumSocketDistance", "Minimum socket distance", RowKind.Slider, "TPS", "a socket nearer than this is held off, so a plug pushed hard against one does not fold"),
                }},
            new Section { Title = "Who it answers", Tint = TintWho,
                Blurb = "Which sockets this plug will bend toward.",
                Knobs = new[]
                {
                    new Knob("_YAPS_TagInclude", "Only sockets tagged", RowKind.Float, "SPS", "a tag number the setup tool assigns — 0 answers all"),
                    new Knob("_YAPS_TagExclude", "Never sockets tagged", RowKind.Float, "SPS", "0 refuses none"),
                    new Knob("_YAPS_SelfTag", "Own-avatar tag", RowKind.Float, help: "which sockets are this plug's wearer's, so it ignores them — -1 on a prop"),
                }},
            new Section { Title = "Socket", Tint = TintSocket,
                Blurb = "For a mesh that is a socket: how its shapes open as a plug goes in.",
                Knobs = new[]
                {
                    new Knob("_YAPS_SocketPower", "Shape strength", RowKind.Slider, "DPS", "0 is off"),
                    new Knob("_YAPS_SocketShapeStart", "Stage starts", RowKind.Vector, "DPS", "entry, then depths 1–3, as fractions of the plug's length"),
                    new Knob("_YAPS_SocketShapeFade", "Stage fades", RowKind.Vector, "DPS", "how far past its start each stage takes to arrive"),
                    new Knob("_YAPS_SocketDepth", "Depth from channel", RowKind.Slider, help: "-1 reads plugs by their tracker light alone"),
                }},
            new Section { Title = "Debug", Tint = TintDebug,
                Blurb = "Views that say why nothing is happening.",
                Knobs = new[]
                {
                    new Knob("_YAPS_Debug", "View", RowKind.Enum, help: "Resolved by — black nobody, green the contact channel, yellow a marker light"),
                }},
        };

        static readonly string[] Internals =
        {
            "_YAPS_Bake", "_YAPS_VertexCount", "_YAPS_BakeScale", "_YAPS_FrameFromVertex",
            "_YAPS_ShapeCount", "_YAPS_ShapeWeights", "_YAPS_ShapeWeights2", "_YAPS_ShapeWeights3",
            "_YAPS_ShapeWeights4", "_YAPS_ChannelSpace", "_YAPS_ChannelExtents",
            "_YAPS_SocketPos", "_YAPS_SocketForward", "_YAPS_SocketUp", "_YAPS_SocketFlags",
            "_YAPS_SocketFront",
        };

        // --- styles -----------------------------------------------------------

        static GUIStyle _banner, _bannerTitle, _bannerSub, _header, _blurb, _from, _help;
        static Texture2D _white;

        static void EnsureStyles()
        {
            if (_banner != null) return;
            _white = Texture2D.whiteTexture;
            _banner = new GUIStyle { padding = new RectOffset(12, 12, 8, 8) };
            _bannerTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, normal = { textColor = Color.white } };
            _bannerSub = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1, 1, 1, 0.8f) } };
            _header = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 6, 0, 0) };
            _blurb = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, padding = new RectOffset(8, 8, 0, 4) };
            _from = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(0.6f, 0.6f, 0.6f) } };
            _help = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, padding = new RectOffset(4, 0, 0, 3), normal = { textColor = new Color(0.62f, 0.62f, 0.62f) } };
        }

        // --- draw --------------------------------------------------------------

        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            EnsureStyles();
            var byName = properties.ToDictionary(p => p.name, p => p);
            var material = editor.target as Material;
            bool hasSocket = byName.TryGetValue("_YAPS_SocketPower", out var sp) && sp.floatValue > 0;
            bool hasPlug = byName.TryGetValue("_YAPS_Enabled", out var en) && en.floatValue > 0;
            string role = hasPlug && hasSocket ? "plug + socket" : hasSocket ? "socket" : "plug";
            float length = byName.TryGetValue("_YAPS_Length", out var lp) ? lp.floatValue : 0f;

            Banner(role, length, material);

            foreach (var section in Sections)
            {
                var present = section.Knobs.Where(k => byName.ContainsKey(k.Name)).ToArray();
                if (present.Length == 0) continue;
                if (section.Title == "Socket" && !hasSocket) continue;
                if (section.Title != "Socket" && section.Title != "Debug" && !hasPlug && hasSocket) continue;

                string key = material.shader.name + "/" + section.Title;
                if (!Open.TryGetValue(key, out bool open)) open = section.StartOpen;
                open = SectionHeader(section.Title, section.Tint, open);
                Open[key] = open;
                if (!open) continue;

                using (new EditorGUILayout.VerticalScope(BoxStyle()))
                {
                    if (!string.IsNullOrEmpty(section.Blurb)) GUILayout.Label(section.Blurb, _blurb);
                    foreach (var knob in present) DrawKnob(editor, byName[knob.Name], knob);
                }
                GUILayout.Space(2);
            }

            // Internals: a single folded row, read-only inside.
            {
                string key = material.shader.name + "/Internals";
                if (!Open.TryGetValue(key, out bool open)) open = false;
                open = SectionHeader("Internals", new Color(0.4f, 0.4f, 0.4f), open, "written by the bake and the channel — read-only");
                Open[key] = open;
                if (open)
                {
                    using (new EditorGUILayout.VerticalScope(BoxStyle()))
                    using (new EditorGUI.DisabledScope(true))
                    {
                        foreach (string name in Internals)
                        {
                            if (byName.TryGetValue(name, out var prop))
                                editor.ShaderProperty(prop, prop.displayName.Replace("YAPS ", ""));
                        }
                    }
                }
            }

            GUILayout.Space(8);
            Rule();
            GUILayout.Space(4);

            // Everything that is not ours goes to the shader's own panel.
            var rest = properties.Where(p => !p.name.StartsWith("_YAPS_", StringComparison.Ordinal)).ToArray();
            var original = OriginalEditor(material);
            if (original != null)
            {
                original.OnGUI(editor, rest);
            }
            else
            {
                foreach (var p in rest)
                {
                    if ((p.flags & MaterialProperty.PropFlags.HideInInspector) != 0) continue;
                    editor.ShaderProperty(p, p.displayName);
                }
                editor.RenderQueueField();
                editor.EnableInstancingField();
                editor.DoubleSidedGIField();
            }
        }

        static void Banner(string role, float length, Material material)
        {
            var rect = GUILayoutUtility.GetRect(0, 44, GUILayout.ExpandWidth(true));
            // The family gradient, VRChat blue to ChilloutVR orange through
            // the bridge purple — the same walk the windows' banners take.
            const int steps = 32;
            for (int i = 0; i < steps; i++)
            {
                var slice = new Rect(rect.x + rect.width * i / steps, rect.y, rect.width / steps + 1, rect.height);
                EditorGUI.DrawRect(slice, BridgeTheme.At((float) i / (steps - 1)));
            }
            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.18f));
            var title = new Rect(rect.x + 12, rect.y + 6, rect.width - 24, 20);
            GUI.Label(title, "YAPS", _bannerTitle);
            var sub = new Rect(rect.x + 12, rect.y + 24, rect.width - 24, 16);
            string subText = length > 0 ? $"{role}  ·  {length:0.###} m  ·  {material.shader.name.Replace("Hidden/YAPS/", "patched ")}" : role;
            GUI.Label(sub, subText, _bannerSub);
            var ver = new Rect(rect.xMax - 60, rect.y + 6, 50, 16);
            GUI.Label(ver, BridgeDefines.Version, new GUIStyle(_bannerSub) { alignment = TextAnchor.MiddleRight });
            GUILayout.Space(6);
        }

        static bool SectionHeader(string title, Color tint, bool open, string right = null)
        {
            var rect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            var bg = tint; bg.a = EditorGUIUtility.isProSkin ? 0.16f : 0.22f;
            EditorGUI.DrawRect(rect, bg);
            var bar = new Rect(rect.x, rect.y, 3, rect.height);
            EditorGUI.DrawRect(bar, tint);
            var arrow = new Rect(rect.x + 8, rect.y + 3, 16, 16);
            EditorGUI.Foldout(arrow, open, GUIContent.none, true);
            var label = new Rect(rect.x + 22, rect.y, rect.width - 22, rect.height);
            GUI.Label(label, title, _header);
            if (!string.IsNullOrEmpty(right))
            {
                var r = new Rect(rect.x, rect.y, rect.width - 8, rect.height);
                GUI.Label(r, right, _from);
            }
            var e = Event.current;
            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                open = !open; e.Use();
            }
            return open;
        }

        static void DrawKnob(MaterialEditor editor, MaterialProperty prop, Knob knob)
        {
            var label = new GUIContent(knob.Label, knob.Help);
            using (new EditorGUILayout.HorizontalScope())
            {
                switch (knob.Kind)
                {
                    case RowKind.Toggle:
                    {
                        bool on = prop.floatValue > 0.5f;
                        EditorGUI.BeginChangeCheck();
                        bool now = EditorGUILayout.Toggle(label, on);
                        if (EditorGUI.EndChangeCheck()) prop.floatValue = now ? 1f : 0f;
                        break;
                    }
                    case RowKind.Slider:
                    {
                        // Ranged in the shader; Unity draws it as a slider.
                        editor.ShaderProperty(prop, label);
                        break;
                    }
                    case RowKind.Vector:
                    {
                        EditorGUI.BeginChangeCheck();
                        var v = EditorGUILayout.Vector4Field(label, prop.vectorValue);
                        if (EditorGUI.EndChangeCheck()) prop.vectorValue = v;
                        break;
                    }
                    case RowKind.Enum:
                    case RowKind.Float:
                    default:
                        editor.ShaderProperty(prop, label);
                        break;
                }
                if (!string.IsNullOrEmpty(knob.From))
                {
                    GUILayout.Label(knob.From, _from, GUILayout.Width(64));
                }
            }
            if (!string.IsNullOrEmpty(knob.Help))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUIUtility.labelWidth + 4);
                    GUILayout.Label(knob.Help, _help);
                }
            }
        }

        static GUIStyle BoxStyle()
        {
            var s = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(8, 8, 6, 6) };
            return s;
        }

        static void Rule()
        {
            var r = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.35f));
        }

        // --- the original editor ---------------------------------------------

        static ShaderGUI OriginalEditor(Material material)
        {
            if (material == null) return null;
            var shader = material.shader;
            int index = shader.FindPropertyIndex(OriginalEditorProperty);
            if (index < 0) return null;
            string typeName = shader.GetPropertyDescription(index);
            if (string.IsNullOrEmpty(typeName)) return null;
            if (Originals.TryGetValue(typeName, out var cached)) return cached;

            ShaderGUI made = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType(typeName, false); } catch { }
                if (t == null || !typeof(ShaderGUI).IsAssignableFrom(t)) continue;
                try { made = (ShaderGUI) Activator.CreateInstance(t); } catch { made = null; }
                break;
            }
            Originals[typeName] = made;
            return made;
        }
    }
}
#endif
