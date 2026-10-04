// The material panel for anything wearing YAPS: the knobs grouped and
// named, with the shader's original panel underneath. The patcher injects
// this editor and records the original's class in a hidden property.
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
            public string Name, Label, From, Help, More;
            public RowKind Kind;
            public Knob(string name, string label, RowKind kind, string from = null, string help = null, string more = null)
            { Name = name; Label = label; Kind = kind; From = from; Help = help; More = more; }
        }

        // Each section's stripe is its place along the ChilloutVR span, worked out when drawn so
        // it follows the skin; Debug and Internals are grey.
        struct Section
        {
            public string Title, Blurb;
            public Knob[] Knobs;
            public bool StartOpen;
        }

        static readonly Section[] Sections =
        {
            new Section { Title = "Plug", StartOpen = true,
                Blurb = "The basics. Length is measured from the mesh by the bake.",
                Knobs = new[]
                {
                    new Knob("_YAPS_Enabled", "Deform on", RowKind.Toggle),
                    new Knob("_YAPS_Length", "Length", RowKind.Float, help: "from the bake: metres on a skinned mesh, the object's own units on a plain one"),
                    new Knob("_YAPS_Overrun", "Carry on through a ring", RowKind.Toggle, "SPS",
                        "On, the tip keeps going past a ring. Off, the shaft stops at every socket."),
                    new Knob("_YAPS_TaperStart", "Hole taper begins", RowKind.Slider, help: "how far past a hole the shaft starts to narrow, as a fraction of length"),
                    new Knob("_YAPS_TaperEnd", "Hole taper closes by", RowKind.Slider, help: "and where it has closed to a point"),
                }},
            new Section { Title = "Shape at rest",
                Blurb = "How the shaft sits when nothing is bending it.",
                Knobs = new[]
                {
                    new Knob("_YAPS_Curvature", "Curvature", RowKind.Slider, "DPS", "a resting bend along the whole shaft: positive bends up"),
                    new Knob("_YAPS_ReCurvature", "Recurvature", RowKind.Slider, "DPS", "a second bend at the tip, opposite in sign: sweep, then hook"),
                    new Knob("_YAPS_EntranceStiffness", "Entrance stiffness", RowKind.Slider, "DPS", "how much the base resists bending: 0 bends evenly from the root"),
                }},
            new Section { Title = "Inside a socket",
                Blurb = "A grip at the opening and a swell just short of it.",
                Knobs = new[]
                {
                    new Knob("_YAPS_Squeeze", "Squeeze", RowKind.Slider, "DPS · TPS", "how much the socket narrows the shaft where it grips"),
                    new Knob("_YAPS_SqueezeDistance", "Squeeze reach", RowKind.Slider, "DPS · TPS", "how far either side of the opening it grips, as a fraction of length"),
                    new Knob("_YAPS_Bulge", "Bulge", RowKind.Slider, "DPS · TPS", "the swell just short of the opening, as a fraction of radius"),
                    new Knob("_YAPS_BulgeDistance", "Bulge reach", RowKind.Slider, "DPS · TPS", "how far before the opening the swell begins"),
                    new Knob("_YAPS_BulgeFalloff", "Bulge falloff", RowKind.Slider, "TPS", "how far short of the opening it peaks; 0 peaks halfway"),
                }},
            new Section { Title = "Out of a socket",
                Blurb = "Shrink and wriggle while no socket has it.",
                Knobs = new[]
                {
                    new Knob("_YAPS_IdleLength", "Idle length", RowKind.Slider, "TPS", "how much of its length it keeps: 1 is no change"),
                    new Knob("_YAPS_IdleWidth", "Idle width", RowKind.Slider, "TPS", "how much of its width it keeps"),
                    new Knob("_YAPS_WriggleStrength", "Wriggle", RowKind.Slider, "DPS", "idle motion, tip-heavy"),
                    new Knob("_YAPS_WriggleSpeed", "Wriggle speed", RowKind.Slider, "DPS"),
                }},
            new Section { Title = "Motion inside a socket",
                Blurb = "A stroke along the shaft while a socket has it.",
                Knobs = new[]
                {
                    new Knob("_YAPS_PumpStrength", "Pumping", RowKind.Slider, "TPS", "a stroke along the shaft, only while engaged"),
                    new Knob("_YAPS_PumpSpeed", "Pumping speed", RowKind.Slider, "TPS"),
                    new Knob("_YAPS_PumpWidth", "Pumping width", RowKind.Slider, "TPS", "how much of the shaft pumps: 1 is all of it, small values only the tip"),
                }},
            new Section { Title = "The bend toward a socket",
                Blurb = "How the shaft arrives at a socket.",
                Knobs = new[]
                {
                    new Knob("_YAPS_BezierSmoothness", "Smoothness", RowKind.Slider, "TPS", "below 1 arrives more directly; above 1 sweeps a wider arc"),
                    new Knob("_YAPS_BezierStart", "Straight before bend", RowKind.Slider, "TPS", "a fraction of the shaft held straight before any bend"),
                    new Knob("_YAPS_SmoothStart", "Ease into bend", RowKind.Slider, "TPS", "eases the join between the straight part and the curve"),
                    new Knob("_YAPS_MinimumSocketDistance", "Minimum socket distance", RowKind.Slider, "TPS", "a socket nearer than this is held off, so the plug does not fold"),
                }},
            new Section { Title = "Which sockets it answers",
                Blurb = "Which sockets this plug will bend toward.",
                Knobs = new[]
                {
                    new Knob("_YAPS_SelfTag", "Own-avatar tag", RowKind.Float, help: "1 when the avatar wears sockets of its own, so it checks whose a socket is; -1 when there are none to check, a prop included"),
                    new Knob("_YAPS_SelfAllow", "Answer the wearer's own sockets", RowKind.Toggle, help: "off by default: an own socket is nearer than anyone else's and would take every bend"),
                    new Knob("_YAPS_UseAtlas", "Read the screen atlas", RowKind.Slider, help: "finds sockets through the screen, with no light slot. Off falls back to marker lights"),
                }},
            new Section { Title = "Socket",
                Blurb = "For a mesh that is a socket: how its shapes open as a plug goes in.",
                Knobs = new[]
                {
                    new Knob("_YAPS_SocketPower", "Shape strength", RowKind.Slider, "DPS", "0 is off"),
                    new Knob("_YAPS_SocketShapeStart", "Starts, shapes 0-3", RowKind.Vector, "DPS", "where each shape starts to open, as a fraction of the plug's length"),
                    new Knob("_YAPS_SocketShapeStart2", "Starts, shapes 4-7", RowKind.Vector, "DPS"),
                    new Knob("_YAPS_SocketShapeStart3", "Starts, shapes 8-11", RowKind.Vector, "DPS"),
                    new Knob("_YAPS_SocketShapeStart4", "Starts, shapes 12-15", RowKind.Vector, "DPS"),
                    new Knob("_YAPS_SocketShapeFade", "Fades, shapes 0-3", RowKind.Vector, "DPS", "how far past its start each shape is fully open"),
                    new Knob("_YAPS_SocketShapeFade2", "Fades, shapes 4-7", RowKind.Vector, "DPS"),
                    new Knob("_YAPS_SocketShapeFade3", "Fades, shapes 8-11", RowKind.Vector, "DPS"),
                    new Knob("_YAPS_SocketShapeFade4", "Fades, shapes 12-15", RowKind.Vector, "DPS"),
                    new Knob("_YAPS_SocketDepth", "Depth from channel", RowKind.Slider, help: "-1 reads plugs by their tracker light alone"),
                }},
            new Section { Title = "Debug",
                Blurb = "Views that say why nothing is happening.",
                Knobs = new[]
                {
                    new Knob("_YAPS_Debug", "View", RowKind.Enum,
                        help: "The plug goes straight and its length is the answer. Set it Off before upload.",
                        more: "Resolved by: a quarter nothing, half the preview, three quarters a marker light, full the atlas.\nGap to socket: distance as a fraction of the plug; a jump means a different socket.\nEngagement: a tenth at 0, full at 1.\nSocket facing: full same way, a little over half square across, a tenth facing back.\nAtlas taps: a tenth nothing, a third not this plug's, two thirds out of reach or own body, full found.\nAtlas target: a tenth target too small, four tenths wrong screen, seven tenths right screen and empty, full on."),
                }},
        };

        // Every other _YAPS_ property is an internal, found by prefix. A hand
        // list of them drifted from the patcher's block, and the fallback panel
        // hides every _YAPS_ name, so a missed one showed nowhere.
        static readonly HashSet<string> Claimed =
            new HashSet<string>(Sections.SelectMany(s => s.Knobs).Select(k => k.Name));

        // --- styles -----------------------------------------------------------

        static GUIStyle _header, _blurb, _from, _right, _help, _box;

        static void EnsureStyles()
        {
            if (_header == null)
            {
                // No box; the header is the structure.
                _box = new GUIStyle { padding = new RectOffset(14, 4, 4, 8) };
                _header = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 6, 0, 0) };
                _blurb = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, padding = new RectOffset(8, 8, 0, 4) };
                // Rich text, so each system wears its own tag colour.
                _from = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, richText = true };
                _right = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
                _help = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, padding = new RectOffset(4, 0, 0, 3) };
            }
            // Every draw, since the styles outlive a skin switch.
            _blurb.normal.textColor = _right.normal.textColor = _help.normal.textColor = BridgeTheme.Muted;
        }

        // The inspector's system tags, in the same series colours.
        static string FromText(string from) => string.Join(" · ",
            from.Split(new[] { " · " }, StringSplitOptions.RemoveEmptyEntries).Select(s =>
                $"<color=#{ColorUtility.ToHtmlStringRGB(BridgeTheme.TagColour(YapsInspectorStyle.SystemSeries(s)))}>{s}</color>"));

        // --- draw --------------------------------------------------------------

        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            EnsureStyles();
            // Not ToDictionary: a shader may declare the same property name
            // twice and Unity hands both back. Poiyomi does, and a duplicate
            // key threw here on every repaint, which made the material
            // uninspectable rather than merely noisy.
            var byName = new Dictionary<string, MaterialProperty>(properties.Length);
            foreach (var p in properties) byName[p.name] = p;
            var material = editor.target as Material;
            // A socket bake as the socket inspector reads one: power alone
            // cannot tell, since Strength 0 is a legal socket.
            bool hasSocket = YapsSocketEditor.IsSocketBake(material);
            bool hasPlug = byName.TryGetValue("_YAPS_Enabled", out var en) && en.floatValue > 0;
            string role = hasSocket ? "socket" : "plug";
            float length = byName.TryGetValue("_YAPS_Length", out var lp) ? lp.floatValue : 0f;

            Banner(role, length, material);

            // Write the knobs back to the YapsPlug that owns this material.
            EditorGUI.BeginChangeCheck();
            for (int i = 0; i < Sections.Length; i++)
            {
                var section = Sections[i];
                var present = section.Knobs.Where(k => byName.ContainsKey(k.Name)).ToArray();
                if (present.Length == 0) continue;
                if (section.Title == "Socket" && !hasSocket) continue;
                if (section.Title != "Socket" && section.Title != "Debug" && !hasPlug && hasSocket) continue;

                // Along the span, Debug last and grey.
                var tint = section.Title == "Debug" ? BridgeTheme.Fill(Tone.Muted)
                    : BridgeTheme.Span.Cvr.At(i / (float) (Sections.Length - 2));
                string key = material.shader.name + "/" + section.Title;
                if (!Open.TryGetValue(key, out bool open)) open = section.StartOpen;
                open = SectionHeader(section.Title, tint, open);
                Open[key] = open;
                if (!open) continue;

                using (new EditorGUILayout.VerticalScope(_box))
                {
                    if (!string.IsNullOrEmpty(section.Blurb)) GUILayout.Label(section.Blurb, _blurb);
                    foreach (var knob in present) DrawKnob(editor, byName[knob.Name], knob);
                }
                GUILayout.Space(2);
            }
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var t in editor.targets) YapsNativeBuilder.SyncPlugsFrom(t as Material);
            }

            // Internals, read only.
            {
                string key = material.shader.name + "/Internals";
                if (!Open.TryGetValue(key, out bool open)) open = false;
                open = SectionHeader("Internals", BridgeTheme.Fill(Tone.Muted), open, "written by the build: read-only");
                Open[key] = open;
                if (open)
                {
                    using (new EditorGUILayout.VerticalScope(_box))
                    using (new EditorGUI.DisabledScope(true))
                    {
                        foreach (var prop in properties)
                        {
                            if (!prop.name.StartsWith("_YAPS_", StringComparison.Ordinal) || Claimed.Contains(prop.name)) continue;
                            // A name declared twice is drawn once.
                            if (byName[prop.name] != prop) continue;
                            // The patcher's markers hold a class and a shader name, not a value.
                            if ((prop.flags & MaterialProperty.PropFlags.HideInInspector) != 0) continue;
                            editor.ShaderProperty(prop, prop.displayName.Replace("YAPS ", ""));
                        }
                    }
                }
            }

            GUILayout.Space(8);
            Rule();
            GUILayout.Space(4);

            // Everything else goes to the shader's own panel.
            var rest = properties.Where(p => !p.name.StartsWith("_YAPS_", StringComparison.Ordinal)).ToArray();
            var original = OriginalEditor(material);
            if (original != null)
            {
                // The FULL list, not the filtered one. A shader's own editor
                // builds its UI from the properties its shader declares and looks
                // them up by name and index; handing it a subset makes it
                // dereference something that is not there, and Poiyomi's threw on
                // every repaint. The YAPS ones appearing in its panel is cosmetic; taking
                // its editor down is not.
                original.OnGUI(editor, properties);
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

        // A plain mesh's length is held in that mesh's own units, so metres
        // need the renderer: the selected one, when it wears this material.
        static string LengthText(float raw, Material material)
        {
            var go = Selection.activeGameObject;
            var r = go != null ? go.GetComponent<Renderer>() : null;
            if (r != null && r.sharedMaterials.Contains(material))
                return $"{YapsNativeBuilder.WorldLength(r, material):0.###} m";
            bool skinned = material.HasProperty("_YAPS_FrameFromVertex") && material.GetFloat("_YAPS_FrameFromVertex") > 0.5f;
            return skinned ? $"{raw:0.###} m" : $"{raw:0.###} mesh units";
        }

        // The kit's banner at the inspector's 52px, bled to the panel's edges.
        static void Banner(string role, float length, Material material)
        {
            var laid = GUILayoutUtility.GetRect(0, 52, GUILayout.ExpandWidth(true));
            var rect = new Rect(0, laid.y, EditorGUIUtility.currentViewWidth, laid.height);
            string subText = length > 0 ? $"{role} · {LengthText(length, material)} · {material.shader.name.Replace("Hidden/YAPS/", "patched ")}" : role;
            BridgeTheme.DrawBanner(rect, "YAPS", subText, "v" + BridgeDefines.Version, BridgeTheme.Span.Cvr);
            GUILayout.Space(6);

            // A material can be running a shader older than the toolkit, and
            // nothing said so. The material panel is where somebody reads a
            // value and believes it, so the warning belongs here.
            if (YapsShaderPatcher.IsStale(material))
            {
                EditorGUILayout.HelpBox(
                    "This material runs an older shader than the toolkit. Bake again to refresh it; knobs and bake are kept.", MessageType.Warning);
                GUILayout.Space(4);
            }
        }

        // A section header: colour bar, arrow, title, rule.
        static bool SectionHeader(string title, Color tint, bool open, string right = null)
        {
            var rect = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            // Bled to the inspector's edges.
            var full = new Rect(0, rect.y, EditorGUIUtility.currentViewWidth, rect.height);

            bool hover = full.Contains(Event.current.mousePosition);
            if (hover)
            {
                EditorGUI.DrawRect(full, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0.06f));
            }
            EditorGUI.DrawRect(new Rect(full.x, full.y + 4, 3, full.height - 8), tint);
            // Two strokes, crisp at any skin.
            var arrowRect = new Rect(full.x + 12, full.y + 8, 10, 10);
            DrawArrow(arrowRect, open, _header.normal.textColor);
            GUI.Label(new Rect(full.x + 28, full.y, full.width - 40, full.height), title, _header);
            if (!string.IsNullOrEmpty(right))
            {
                GUI.Label(new Rect(full.x, full.y, full.width - 12, full.height), right, _right);
            }
            EditorGUI.DrawRect(new Rect(full.x, full.yMax - 1, full.width, 1),
                new Color(0.5f, 0.5f, 0.5f, EditorGUIUtility.isProSkin ? 0.18f : 0.28f));

            var e = Event.current;
            if (e.type == EventType.MouseDown && full.Contains(e.mousePosition))
            {
                // Not GUI.changed: a fold is no property, and the change check
                // around the sections syncs every plug in the scene on one.
                open = !open; e.Use();
            }
            if (e.type == EventType.MouseMove) EditorWindow.focusedWindow?.Repaint();
            return open;
        }

        static void DrawArrow(Rect r, bool open, Color colour)
        {
            Handles.BeginGUI();
            Handles.color = colour;
            if (open)
            {
                Handles.DrawAAPolyLine(2f, new Vector3(r.x, r.y + 3), new Vector3(r.center.x, r.yMax - 2), new Vector3(r.xMax, r.y + 3));
            }
            else
            {
                Handles.DrawAAPolyLine(2f, new Vector3(r.x + 3, r.y), new Vector3(r.xMax - 2, r.center.y), new Vector3(r.x + 3, r.yMax));
            }
            Handles.EndGUI();
        }

        static void DrawKnob(MaterialEditor editor, MaterialProperty prop, Knob knob)
        {
            var label = new GUIContent(knob.Label, knob.More != null ? knob.Help + "\n" + knob.More : knob.Help);
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
                        // Ranged in the shader, so a slider.
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
                    GUILayout.Label(FromText(knob.From), _from, GUILayout.Width(64));
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
            // A long explanation folds behind More, keyed per knob like the sections.
            if (!string.IsNullOrEmpty(knob.More))
            {
                string key = "more/" + knob.Name;
                Open.TryGetValue(key, out bool more);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUIUtility.labelWidth + 4);
                    // A fold is no property: left as a change, it would sync every plug in the scene.
                    bool changed = GUI.changed;
                    more = EditorGUILayout.Foldout(more, more ? "Less" : "More", true);
                    GUI.changed = changed;
                }
                Open[key] = more;
                if (more)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(EditorGUIUtility.labelWidth + 4);
                        GUILayout.Label(knob.More, _help);
                    }
                }
            }
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
