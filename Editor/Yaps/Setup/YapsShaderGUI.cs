// The material panel for anything wearing YAPS: the knobs grouped and
// named the way a user thinks, with the internals folded away, and then
// the shader's ORIGINAL panel underneath, untouched.
//
// Forty flat sliders with the channel's internals mixed in is what a
// patched material showed before this. It was correct and unreadable.
//
// Wrapping rather than replacing is the whole trick. The patcher injects
// `CustomEditor "AvatarBridge.YapsShaderGUI"` into every shader it
// patches, and remembers the editor the shader HAD (Poiyomi's ThryEditor,
// Standard's StandardShaderGUI, or none) in a hidden property. This panel
// draws the YAPS groups, then instantiates that original editor by name
// and hands it every property that is not ours — so a Poiyomi user keeps
// Poiyomi's panel entire, with YAPS sitting above it. Same panel whether
// the plug wears Poiyomi, Standard, or the test shader: the property
// names are the wire format and they do not change.
//
// Groups mirror the systems' own vocabulary, so a DPS user finds
// "Entrance Stiffness" where they expect it, with the system named.
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
        // The property the patcher writes the original editor's class
        // name into, so this panel can find it again.
        public const string OriginalEditorProperty = "_YAPS_OriginalEditor";

        static readonly Dictionary<string, ShaderGUI> Originals = new Dictionary<string, ShaderGUI>();
        static readonly Dictionary<string, bool> Open = new Dictionary<string, bool>();

        // One row: property name, label, and where it came from.
        struct Knob
        {
            public string Name, Label, From;
            public Knob(string name, string label, string from = null) { Name = name; Label = label; From = from; }
        }

        struct Group
        {
            public string Title;
            public Knob[] Knobs;
            public bool StartOpen;
        }

        static readonly Group[] Groups =
        {
            new Group { Title = "Plug", StartOpen = true, Knobs = new[]
            {
                new Knob("_YAPS_Enabled", "Enabled"),
                new Knob("_YAPS_Length", "Length (m)"),
                new Knob("_YAPS_Overrun", "Carry on past a ring", "SPS"),
                new Knob("_YAPS_TaperStart", "Hole taper starts"),
                new Knob("_YAPS_TaperEnd", "Hole taper closes by"),
            }},
            new Group { Title = "Shape at rest", Knobs = new[]
            {
                new Knob("_YAPS_Curvature", "Curvature", "DPS"),
                new Knob("_YAPS_ReCurvature", "Recurvature", "DPS"),
                new Knob("_YAPS_EntranceStiffness", "Entrance stiffness", "DPS"),
            }},
            new Group { Title = "When it's in", Knobs = new[]
            {
                new Knob("_YAPS_Squeeze", "Squeeze", "DPS · TPS"),
                new Knob("_YAPS_SqueezeDistance", "Squeeze reach", "DPS · TPS"),
                new Knob("_YAPS_Bulge", "Bulge", "DPS · TPS"),
                new Knob("_YAPS_BulgeDistance", "Bulge reach", "DPS · TPS"),
            }},
            new Group { Title = "When it's not", Knobs = new[]
            {
                new Knob("_YAPS_IdleLength", "Idle length", "TPS"),
                new Knob("_YAPS_IdleWidth", "Idle width", "TPS"),
                new Knob("_YAPS_WriggleStrength", "Wriggle", "DPS"),
                new Knob("_YAPS_WriggleSpeed", "Wriggle speed", "DPS"),
            }},
            new Group { Title = "Motion", Knobs = new[]
            {
                new Knob("_YAPS_PumpStrength", "Pumping", "TPS"),
                new Knob("_YAPS_PumpSpeed", "Pumping speed", "TPS"),
                new Knob("_YAPS_PumpWidth", "Pumping width", "TPS"),
            }},
            new Group { Title = "The curve", Knobs = new[]
            {
                new Knob("_YAPS_BezierSmoothness", "Bezier smoothness", "TPS"),
                new Knob("_YAPS_BezierStart", "Straight before bend", "TPS"),
                new Knob("_YAPS_SmoothStart", "Ease into bend", "TPS"),
                new Knob("_YAPS_MinimumSocketDistance", "Minimum socket distance", "TPS"),
            }},
            new Group { Title = "Who it answers", Knobs = new[]
            {
                new Knob("_YAPS_TagInclude", "Only sockets tagged", "SPS"),
                new Knob("_YAPS_TagExclude", "Never sockets tagged", "SPS"),
                new Knob("_YAPS_SelfTag", "Self tag"),
            }},
            new Group { Title = "Socket", Knobs = new[]
            {
                new Knob("_YAPS_SocketPower", "Shape strength", "DPS"),
                new Knob("_YAPS_SocketShapeStart", "Stage starts (entry, 1, 2, 3)", "DPS"),
                new Knob("_YAPS_SocketShapeFade", "Stage fades", "DPS"),
                new Knob("_YAPS_SocketDepth", "Depth from channel (-1 = lights only)"),
            }},
            new Group { Title = "Debug", Knobs = new[]
            {
                new Knob("_YAPS_Debug", "Debug view"),
            }},
        };

        // Read-only, folded: written by the bake or the channel, never by
        // hand. Shown so a user can SEE the channel move, and so nothing is
        // hidden — just not offered as a thing to type into.
        static readonly string[] Internals =
        {
            "_YAPS_Bake", "_YAPS_VertexCount", "_YAPS_BakeScale", "_YAPS_FrameFromVertex",
            "_YAPS_ShapeCount", "_YAPS_ShapeWeights", "_YAPS_ShapeWeights2", "_YAPS_ShapeWeights3",
            "_YAPS_ShapeWeights4", "_YAPS_ChannelSpace", "_YAPS_ChannelExtents",
            "_YAPS_SocketPos", "_YAPS_SocketForward", "_YAPS_SocketUp", "_YAPS_SocketFlags",
            "_YAPS_SocketFront", OriginalEditorProperty,
        };

        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            var byName = properties.ToDictionary(p => p.name, p => p);
            var material = editor.target as Material;
            bool isSocket = byName.TryGetValue("_YAPS_SocketPower", out var sp) && sp.floatValue > 0
                            && byName.TryGetValue("_YAPS_Enabled", out var en) && en.floatValue <= 0;

            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("YAPS", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(isSocket ? "socket" : "plug", EditorStyles.miniLabel);
            }

            foreach (var group in Groups)
            {
                var present = group.Knobs.Where(k => byName.ContainsKey(k.Name)).ToArray();
                if (present.Length == 0) continue;
                // A plug's material hides the socket group and vice versa,
                // unless both are live (a body mesh carrying both ends).
                if (group.Title == "Socket" && !isSocket && !(sp != null && sp.floatValue > 0)) continue;

                string key = material.shader.name + "/" + group.Title;
                if (!Open.TryGetValue(key, out bool open)) open = group.StartOpen;
                open = EditorGUILayout.BeginFoldoutHeaderGroup(open, group.Title);
                Open[key] = open;
                if (open)
                {
                    EditorGUI.indentLevel++;
                    foreach (var knob in present)
                    {
                        var prop = byName[knob.Name];
                        string label = knob.From != null ? $"{knob.Label}   ({knob.From})" : knob.Label;
                        editor.ShaderProperty(prop, new GUIContent(label, Tip(knob.Name)));
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            // Internals, read-only, folded.
            {
                string key = material.shader.name + "/Internals";
                if (!Open.TryGetValue(key, out bool open)) open = false;
                open = EditorGUILayout.BeginFoldoutHeaderGroup(open, "Internals (written by the bake and the channel)");
                Open[key] = open;
                if (open)
                {
                    EditorGUI.indentLevel++;
                    using (new EditorGUI.DisabledScope(true))
                    {
                        foreach (string name in Internals)
                        {
                            if (byName.TryGetValue(name, out var prop) && name != OriginalEditorProperty)
                            {
                                editor.ShaderProperty(prop, prop.displayName.Replace("YAPS ", ""));
                            }
                        }
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            EditorGUILayout.Space(6);

            // Everything that is not ours goes to the shader's own panel.
            var rest = properties.Where(p => !p.name.StartsWith("_YAPS_", StringComparison.Ordinal)).ToArray();
            var original = OriginalEditor(material);
            if (original != null)
            {
                original.OnGUI(editor, rest);
            }
            else
            {
                // No custom editor to defer to: Unity's default rendering of
                // the remaining properties, exactly as it would have drawn
                // them without us.
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

        // The editor the shader had before the patch, by the class name the
        // patcher recorded. Cached per name; null when there was none or it
        // cannot be found (a package removed since the patch).
        static ShaderGUI OriginalEditor(Material material)
        {
            if (material == null || !material.HasProperty(OriginalEditorProperty)) return null;
            // The name rides in the property's DISPLAY name (a string a
            // shader property can carry); the float itself is unused.
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

        static string Tip(string name)
        {
            switch (name)
            {
                case "_YAPS_Overrun": return "On, the tip carries straight on through a ring. Off, the shaft stops at every socket.";
                case "_YAPS_TaperStart": return "How far past a hole the shaft may go before it starts narrowing, as a fraction of its length.";
                case "_YAPS_TaperEnd": return "…and how far before it has closed to a point.";
                case "_YAPS_Curvature": return "A resting bend along the whole shaft. Positive bends up.";
                case "_YAPS_ReCurvature": return "A second bend gathered at the tip, opposite in sign — sweep, then hook.";
                case "_YAPS_EntranceStiffness": return "How much the base resists bending toward a socket. 0 bends evenly from the root.";
                case "_YAPS_Squeeze": return "How much a socket narrows the shaft where it grips.";
                case "_YAPS_SqueezeDistance": return "How far either side of the opening the grip reaches, as a fraction of length.";
                case "_YAPS_Bulge": return "The swell just short of the opening, as a fraction of radius.";
                case "_YAPS_BulgeDistance": return "How far before the opening the swell begins, as a fraction of length.";
                case "_YAPS_IdleLength": return "How much of its length it keeps when nothing is using it.";
                case "_YAPS_IdleWidth": return "How much of its width it keeps when nothing is using it.";
                case "_YAPS_WriggleStrength": return "Idle motion, tip-heavy. Only while nothing is using it.";
                case "_YAPS_PumpStrength": return "A stroke along the shaft. Only while engaged.";
                case "_YAPS_PumpWidth": return "How much of the shaft pumps: 1 is the whole length, small values move only the tip.";
                case "_YAPS_BezierSmoothness": return "Below 1 arrives more directly; above 1 sweeps a wider arc.";
                case "_YAPS_BezierStart": return "A fraction of the shaft held perfectly straight before any bend.";
                case "_YAPS_SmoothStart": return "Ease the join between the straight part and the curve rather than kink at it.";
                case "_YAPS_MinimumSocketDistance": return "A socket nearer than this is held off, so a plug pushed hard against one does not fold.";
                case "_YAPS_TagInclude": return "Answer only sockets carrying this tag (a number the setup tool assigns). 0 answers all.";
                case "_YAPS_TagExclude": return "Never answer sockets carrying this tag. 0 refuses none.";
                case "_YAPS_SelfTag": return "Which sockets belong to this plug's own wearer, so it ignores them. -1 on a prop.";
                case "_YAPS_SocketPower": return "How strongly the socket's shapes open. 0 is off.";
                case "_YAPS_SocketShapeStart": return "Where each of the four stages begins, as fractions of the plug's length. x is the entry.";
                case "_YAPS_SocketShapeFade": return "How far past its start each stage takes to arrive fully.";
                case "_YAPS_Debug": return "Resolved by: black nobody, green the contact channel, yellow a marker light.";
                default: return null;
            }
        }
    }
}
#endif
