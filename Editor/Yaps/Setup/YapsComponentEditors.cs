// The inspectors for YapsSocket and YapsPlug, and the scene gizmos that
// go with them.
//
// Built with UI Toolkit on AvatarBridge's own elements — the same
// banner, cards, subheadings, hints, toggles and stylesheet as the two
// windows — so a component's inspector and the window that placed it
// are one family. They were IMGUI once, with a hand-drawn header and
// section bars that resembled the family without being it, and read as a
// third look beside the other two.
//
// The gizmos are drawn at a FIXED world size — about 5 cm, twice a real
// socket, so they hold their own beside the CCK's icons — with enough
// line weight to read and nothing extra. A first version scaled them with
// the camera distance so they stayed a fixed size on screen, and that
// swam disorientingly as you moved. Fixed, they sit still and read as
// things in the world. The plug axis is its true length — that IS the
// information — with a modest arrow at the tip.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge
{
    // --- shared -----------------------------------------------------------

    static class YapsInspectorStyle
    {
        public static readonly Color HoleColour = new Color(0.25f, 0.85f, 0.95f);
        public static readonly Color RingColour = new Color(0.95f, 0.55f, 0.20f);
        public static readonly Color PlugColour = new Color(0.55f, 0.85f, 0.35f);
        public static readonly Color BadColour = new Color(0.95f, 0.30f, 0.30f);

        // The inspector's root: the family stylesheet and skin, and the
        // banner. Everything else is a card.
        public static VisualElement Root()
        {
            var root = new VisualElement();
            root.AddToClassList("ab-root");
            root.AddToClassList("ab-inspector");
            BridgeTheme.ApplySkin(root);
            var sheet = Resources.Load<StyleSheet>("AvatarBridge");
            if (sheet != null) root.styleSheets.Add(sheet);
            return root;
        }

        // A bound control for one serialized field, with the field's own
        // range and tooltip, and the tooltip as the hint under it — the
        // material panel's idiom, so nobody has to hover to learn a knob.
        public static VisualElement Field(SerializedProperty p, System.Reflection.FieldInfo field, string label = null,
            string from = null)
        {
            var tip = field?.GetCustomAttributes(typeof(TooltipAttribute), false).FirstOrDefault() as TooltipAttribute;
            var range = field?.GetCustomAttributes(typeof(RangeAttribute), false).FirstOrDefault() as RangeAttribute;
            label = label ?? p.displayName;
            string help = tip != null ? tip.tooltip : null;

            VisualElement control;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float when range != null:
                {
                    var s = new Slider(label, range.min, range.max) { showInputField = true };
                    s.BindProperty(p);
                    s.AddToClassList("ab-slider");
                    control = s;
                    break;
                }
                case SerializedPropertyType.Float:
                {
                    var f = new FloatField(label); f.BindProperty(p); control = f; break;
                }
                case SerializedPropertyType.Integer when range != null:
                {
                    var s = new SliderInt(label, (int) range.min, (int) range.max) { showInputField = true };
                    s.BindProperty(p);
                    s.AddToClassList("ab-slider");
                    control = s;
                    break;
                }
                case SerializedPropertyType.Integer:
                {
                    var f = new IntegerField(label); f.BindProperty(p); control = f; break;
                }
                case SerializedPropertyType.Boolean:
                {
                    var t = new Toggle(label); t.BindProperty(p);
                    t.AddToClassList("ab-toggle");
                    t.style.flexGrow = 1;
                    control = t;
                    break;
                }
                case SerializedPropertyType.String:
                {
                    var f = new TextField(label); f.BindProperty(p); control = f; break;
                }
                case SerializedPropertyType.ObjectReference:
                {
                    var f = new ObjectField(label)
                    {
                        objectType = field != null ? field.FieldType : typeof(Object),
                        allowSceneObjects = true,
                    };
                    f.BindProperty(p);
                    control = f;
                    break;
                }
                default:
                {
                    var f = new PropertyField(p, label); control = f; break;
                }
            }
            if (p.propertyType != SerializedPropertyType.Boolean) control.AddToClassList("ab-field");
            control.tooltip = help;

            // The control, and to its right the system it came from — one
            // small chip per system, coloured by system, the same everywhere.
            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;
            control.style.flexGrow = 1;
            control.style.flexShrink = 1;
            line.Add(control);
            if (!string.IsNullOrEmpty(from))
            {
                foreach (var s in from.Split(new[] { " · " }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    var chip = BridgeElements.Chip(s, SystemColour(s), true, null, false, false);
                    chip.AddToClassList("ab-from");
                    chip.tooltip = SystemName(s);
                    line.Add(chip);
                }
            }

            var box = new VisualElement();
            box.Add(line);
            if (help != null) box.Add(BridgeElements.Hint(help));
            return box;
        }

        // The four systems, told apart by colour wherever they are named.
        public static Color SystemColour(string system)
        {
            switch (system)
            {
                case "DPS": return new Color(0.70f, 0.50f, 0.95f);
                case "TPS": return new Color(0.30f, 0.78f, 0.72f);
                case "SPS": return new Color(0.95f, 0.62f, 0.28f);
                default: return PlugColour;
            }
        }

        public static string SystemName(string system)
        {
            switch (system)
            {
                case "DPS": return "From Raliv's Dynamic Penetration System";
                case "TPS": return "From Thry's Penetration System";
                case "SPS": return "From VRCFury's Super Plug Shader";
                default: return "YAPS's own — none of the others had it";
            }
        }

        public static Button Button(string text, System.Action act)
        {
            var b = new UnityEngine.UIElements.Button(act) { text = text };
            b.AddToClassList("ab-button");
            return b;
        }
    }

    // --- preview -----------------------------------------------------------

    // One place that turns a socket's preview on and off, whoever asks —
    // the socket's inspector, the window's row chip. Preview needs a PLUG
    // to bend, and "there is nothing there" is what a user sees when the
    // scene has none: so if no baked YAPS plug is in the scene, a test plug
    // is dropped a little way in front of the socket, aimed at it, and
    // removed again when preview goes off. With a real plug in the scene it
    // bends that one and spawns nothing. The socket is written into the
    // plugs the same instant, so the bend is there on the first repaint.
    //
    // While a preview is on, or a plug is selected, the scene view is
    // repainted continuously so the time-driven parts of the deform —
    // wriggle, pumping — actually move. A scene view otherwise repaints
    // only on input, and a wriggle at 0.5 looked exactly like none.
    static class YapsPreview
    {
        public const string PlugName = "YAPS Preview Plug";
        static bool _animating;

        public static void Set(YapsSocket socket, bool on)
        {
            if (socket == null) return;
            Undo.RecordObject(socket, "YAPS preview");
            socket.preview = on;
            EditorUtility.SetDirty(socket);
            if (on && CountBakedPlugs() == 0) Spawn(socket);
            if (!on) Remove();
            socket.PreviewTick();
            Animate(on);
            SceneView.RepaintAll();
        }

        public static void Animate(bool on)
        {
            if (on == _animating) return;
            _animating = on;
            EditorApplication.update -= Tick;
            if (on) EditorApplication.update += Tick;
        }

        static void Tick()
        {
            // Stop on our own once nothing needs it: no socket previewing
            // and no plug selected.
            bool previewing = Object.FindObjectsOfType<YapsSocket>().Any(s => s.preview);
            bool plugSelected = Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<YapsPlug>() != null;
            if (!previewing && !plugSelected) { Animate(false); return; }
            SceneView.RepaintAll();
        }

        public static int CountBakedPlugs()
        {
            int n = 0;
            foreach (var r in Object.FindObjectsOfType<Renderer>())
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.HasProperty("_YAPS_Bake") && m.HasProperty("_YAPS_Enabled") && m.GetFloat("_YAPS_Enabled") > 0) { n++; break; }
            return n;
        }

        // A test plug in front of the socket, aimed at it, at a distance
        // where the deform is clearly engaged but not fully swallowed — so
        // moving the socket a little either way shows the whole range.
        static void Spawn(YapsSocket socket)
        {
            if (GameObject.Find(PlugName) != null) return;
            // Keep the socket selected: the button that stops the preview
            // is on its inspector, and a spawn that stole the selection took
            // that button away.
            var go = YapsNativeBuilder.BuildTestPlug(null, select: false);
            if (go == null) return;
            go.name = PlugName;
            var t = socket.transform;
            // A quarter metre is the test plug's length; sit its base 0.3 m
            // out along the socket's forward, pointing back at it.
            go.transform.position = t.position + t.forward * 0.3f;
            go.transform.rotation = Quaternion.LookRotation(-t.forward, t.up);
            EditorGUIUtility.PingObject(go);

            // A test plug that did not bake sits there straight and does
            // nothing, and the only clue was a console line. Say it here.
            var plug = go.GetComponent<YapsPlug>();
            bool baked = plug != null && plug.Target != null
                         && plug.Target.sharedMaterials.Any(m => m != null && m.HasProperty("_YAPS_Bake"));
            if (!baked)
            {
                EditorUtility.DisplayDialog("YAPS preview",
                    "The test plug was placed but did not bake, so it will not bend. The Console has the " +
                    "reason on a [YAPS] line — usually the shader could not be patched.", "OK");
            }
        }

        static void Remove()
        {
            var existing = GameObject.Find(PlugName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);
        }
    }

    // --- socket ------------------------------------------------------------

    [CustomEditor(typeof(YapsSocket))]
    public class YapsSocketEditor : Editor
    {
        VisualElement _root;

        // Drive the preview from here while the socket is selected. Update
        // on the component runs in edit mode only when the scene changed,
        // and "changed" did not reliably include the test plug that had
        // just been baked — the plug sat straight until something else
        // moved. A scene repaint is every drag, every frame.
        void OnSceneGUI()
        {
            var socket = target as YapsSocket;
            if (socket != null && socket.preview) { socket.PreviewTick(); YapsPreview.Animate(true); }
        }

        public override VisualElement CreateInspectorGUI()
        {
            _root = YapsInspectorStyle.Root();
            Rebuild();
            return _root;
        }

        // Structural changes come from callbacks inside the very elements
        // about to be replaced; rebuilding on the next tick keeps the
        // dispatch that asked for it intact.
        void RebuildLater() => _root?.schedule.Execute(Rebuild);

        // The whole inspector is rebuilt on structural change — kind,
        // renderer, a shape added or removed, preview on or off, a bake —
        // and bound; slider drags on bound fields need nothing.
        void Rebuild()
        {
            if (_root == null) return;
            _root.Clear();
            serializedObject.Update();
            var socket = (YapsSocket) target;
            var so = serializedObject;
            var kindProp = so.FindProperty("kind");
            var tagProp = so.FindProperty("tag");
            var rendererProp = so.FindProperty("renderer");
            var shapesProp = so.FindProperty("shapes");
            var powerProp = so.FindProperty("shapePower");
            var lightsProp = so.FindProperty("emitLights");
            var type = typeof(YapsSocket);

            var avatarRoot = AvatarRootOf(socket.transform);
            bool hole = kindProp.enumValueIndex == (int) YapsSocket.SocketKind.Hole;
            bool built = IsBuilt(socket.transform);

            _root.Add(BridgeElements.Banner((hole ? "Hole" : "Ring") + "  ·  " + socket.name,
                built ? "readable by DPS, TPS, SPS and YAPS plugs" : "not built — no plug can find it yet",
                built ? "YAPS" : "not built"));

            var body = new VisualElement();
            body.AddToClassList("ab-scroll");
            _root.Add(body);

            // What it is.
            var what = new BridgeElements.Card("What it is");
            what.Body.Add(BridgeElements.Choice("Kind",
                "A hole closes around the plug and stops it. A ring lets it pass straight through.",
                new[] { "Hole", "Ring" }, kindProp.enumValueIndex, i =>
                {
                    kindProp.enumValueIndex = i;
                    so.ApplyModifiedProperties();
                    RebuildLater();
                }));
            what.Body.Add(BridgeElements.Hint(hole
                ? "A hole closes around the plug and stops it — a mouth, a pussy, an anus."
                : "A ring lets the plug pass straight through — a hand, thighs, a foot."));
            what.Body.Add(YapsInspectorStyle.Field(tagProp, type.GetField("tag"), "Tag"));
            body.Add(what);

            // Shapes.
            var opens = new BridgeElements.Card("Opens as a plug goes in");
            opens.Body.Add(BridgeElements.Hint(
                "Pick the mesh, then up to four of its shapes. The entry opens as the plug arrives; " +
                "each later one starts deeper. Depths are fractions of the plug's length, and they " +
                "stack — once a shape has opened it stays open as the plug goes deeper."));

            var renderers = avatarRoot != null
                ? avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0).ToList()
                : new List<SkinnedMeshRenderer>();
            var current = rendererProp.objectReferenceValue as SkinnedMeshRenderer;
            if (current == null && renderers.Count > 0)
            {
                current = GuessRenderer(socket.transform, renderers);
                if (current != null) { rendererProp.objectReferenceValue = current; so.ApplyModifiedProperties(); }
            }
            var rNames = new List<string> { "None — bend plugs, play no shape" };
            rNames.AddRange(renderers.Select(r => $"{r.name}   ·   {r.sharedMesh.blendShapeCount} shapes"));
            int rIndex = current != null ? renderers.IndexOf(current) : -1;
            var meshPopup = new PopupField<string>("Mesh", rNames, rIndex + 1);
            meshPopup.AddToClassList("ab-field");
            meshPopup.RegisterValueChangedCallback(e =>
            {
                int picked = rNames.IndexOf(e.newValue) - 1;
                rendererProp.objectReferenceValue = picked >= 0 ? renderers[picked] : null;
                so.ApplyModifiedProperties();
                RebuildLater();
            });
            opens.Body.Add(meshPopup);

            if (current != null)
            {
                var mesh = current.sharedMesh;
                var shapeNames = Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToList();
                var options = new List<string> { "— pick a shape —" };
                options.AddRange(shapeNames);
                string[] stageNames = { "Entry", "Depth 1", "Depth 2", "Depth 3" };
                var tint = hole ? YapsInspectorStyle.HoleColour : YapsInspectorStyle.RingColour;

                for (int i = 0; i < shapesProp.arraySize && i < 4; i++)
                {
                    int index = i;
                    var row = shapesProp.GetArrayElementAtIndex(i);
                    var name = row.FindPropertyRelative("blendshape");
                    var start = row.FindPropertyRelative("startsAt");
                    var fade = row.FindPropertyRelative("fadeOver");
                    int sIndex = shapeNames.IndexOf(name.stringValue);

                    var stage = new VisualElement();
                    stage.AddToClassList("ab-stage");
                    var stripe = new VisualElement();
                    stripe.AddToClassList("ab-report-stripe");
                    stripe.style.backgroundColor = Color.Lerp(tint, Color.white, 0.6f - i * 0.2f);
                    stage.Add(stripe);
                    var inner = new VisualElement();
                    inner.style.flexGrow = 1;
                    stage.Add(inner);

                    var head = new VisualElement();
                    head.AddToClassList("ab-row");
                    head.style.alignItems = Align.Center;
                    var shapePopup = new PopupField<string>(stageNames[i], options, sIndex + 1);
                    shapePopup.AddToClassList("ab-field");
                    shapePopup.style.flexGrow = 1;
                    shapePopup.RegisterValueChangedCallback(e =>
                    {
                        int picked = options.IndexOf(e.newValue) - 1;
                        name.stringValue = picked >= 0 ? shapeNames[picked] : "";
                        so.ApplyModifiedProperties();
                    });
                    head.Add(shapePopup);
                    var remove = YapsInspectorStyle.Button("Remove", () =>
                    {
                        shapesProp.DeleteArrayElementAtIndex(index);
                        so.ApplyModifiedProperties();
                        RebuildLater();
                    });
                    head.Add(remove);
                    inner.Add(head);

                    // Depth as ONE range bar: from start to fully open.
                    float s0 = start.floatValue, e0 = Mathf.Min(1f, start.floatValue + fade.floatValue);
                    var range = new MinMaxSlider("Opens from → fully open by", s0, e0, 0f, 1f);
                    range.AddToClassList("ab-field");
                    var readout = BridgeElements.Hint($"{s0:0.00}  →  {e0:0.00}  of the plug's length");
                    range.RegisterValueChangedCallback(e =>
                    {
                        start.floatValue = e.newValue.x;
                        fade.floatValue = Mathf.Max(0.01f, e.newValue.y - e.newValue.x);
                        so.ApplyModifiedProperties();
                        readout.text = $"{e.newValue.x:0.00}  →  {e.newValue.y:0.00}  of the plug's length";
                    });
                    inner.Add(range);
                    inner.Add(readout);
                    if (!string.IsNullOrEmpty(name.stringValue) && sIndex < 0)
                        inner.Add(new HelpBox($"\"{name.stringValue}\" is not on this mesh.", HelpBoxMessageType.Warning));
                    opens.Body.Add(stage);
                }
                if (shapesProp.arraySize < 4)
                {
                    opens.Body.Add(YapsInspectorStyle.Button(
                        shapesProp.arraySize == 0 ? "+ Add the entry shape" : "+ Add a deeper shape", () =>
                        {
                            shapesProp.arraySize++;
                            var row = shapesProp.GetArrayElementAtIndex(shapesProp.arraySize - 1);
                            row.FindPropertyRelative("blendshape").stringValue = "";
                            row.FindPropertyRelative("startsAt").floatValue = 0.25f * (shapesProp.arraySize - 1);
                            row.FindPropertyRelative("fadeOver").floatValue = 0.3f;
                            so.ApplyModifiedProperties();
                            RebuildLater();
                        }));
                }
                if (shapesProp.arraySize > 0)
                    opens.Body.Add(YapsInspectorStyle.Field(powerProp, type.GetField("shapePower"), "Strength"));
            }
            else if (renderers.Count == 0)
            {
                opens.Body.Add(new HelpBox(avatarRoot == null
                    ? "Put this socket under an avatar or prop and its meshes will appear here."
                    : "No skinned mesh with blendshapes under this avatar — the socket bends plugs and plays no shape.",
                    HelpBoxMessageType.Info));
            }
            body.Add(opens);

            // See it work.
            int bakedPlugs = YapsPreview.CountBakedPlugs();
            var see = new BridgeElements.Card("See it work");
            see.Body.Add(BridgeElements.Hint(bakedPlugs > 0
                ? $"Preview bends the {bakedPlugs} YAPS plug{(bakedPlugs == 1 ? "" : "s")} in the scene toward this socket, in the editor, so you can place it and watch before uploading. Writes nothing that ships."
                : "There is no baked plug in the scene, so Preview drops a test plug in front of this socket and bends it. Move the socket and watch it follow. The test plug goes when preview stops."));
            var previewButton = new BridgeElements.PrimaryButton(
                socket.preview ? "Previewing — click to stop" : (bakedPlugs > 0 ? "Preview" : "Preview with a test plug"),
                () => { YapsPreview.Set(socket, !socket.preview); RebuildLater(); });
            see.Body.Add(previewButton);
            body.Add(see);

            // The socket-side deform, once baked. These knobs live on the
            // renderer's material — the shader reads them there — but a
            // socket's customisation belongs in one place, so they are drawn
            // here too when the socket's mesh has been baked. Same values,
            // written straight through.
            var bakedMat = FindSocketMaterial(socket);
            if (bakedMat != null)
            {
                var opensHow = new BridgeElements.Card("How it opens");
                opensHow.Body.Add(BridgeElements.Hint(
                    "Read by the shader on this socket's mesh. Strength scales all the shapes; each stage " +
                    "opens from its start to start + fade, as fractions of the plug's length. Depth comes " +
                    "from the plug's tracker light, or from the contact channel where there is one."));
                var power = new Slider("Strength", 0f, 1f) { value = bakedMat.GetFloat("_YAPS_SocketPower"), showInputField = true };
                power.AddToClassList("ab-field"); power.AddToClassList("ab-slider");
                power.RegisterValueChangedCallback(e =>
                {
                    Undo.RecordObject(bakedMat, "YAPS socket shape");
                    bakedMat.SetFloat("_YAPS_SocketPower", e.newValue);
                    EditorUtility.SetDirty(bakedMat);
                });
                opensHow.Body.Add(power);
                string[] stageNames = { "Entry", "Depth 1", "Depth 2", "Depth 3" };
                for (int i = 0; i < 4; i++)
                {
                    int stage = i;
                    Vector4 starts = bakedMat.GetVector("_YAPS_SocketShapeStart");
                    Vector4 fades = bakedMat.GetVector("_YAPS_SocketShapeFade");
                    var mm = new MinMaxSlider(stageNames[i], starts[i], Mathf.Min(1f, starts[i] + fades[i]), 0f, 1f);
                    mm.AddToClassList("ab-field");
                    mm.RegisterValueChangedCallback(e =>
                    {
                        Undo.RecordObject(bakedMat, "YAPS socket shape");
                        Vector4 st = bakedMat.GetVector("_YAPS_SocketShapeStart");
                        Vector4 fd = bakedMat.GetVector("_YAPS_SocketShapeFade");
                        st[stage] = e.newValue.x; fd[stage] = Mathf.Max(0.01f, e.newValue.y - e.newValue.x);
                        bakedMat.SetVector("_YAPS_SocketShapeStart", st);
                        bakedMat.SetVector("_YAPS_SocketShapeFade", fd);
                        EditorUtility.SetDirty(bakedMat);
                    });
                    opensHow.Body.Add(mm);
                }
                opensHow.Body.Add(BridgeElements.Row(YapsInspectorStyle.Button("Open material", () => Selection.activeObject = bakedMat)));
                body.Add(opensHow);
            }

            // Advanced.
            var advanced = new BridgeElements.Card("Advanced");
            advanced.Body.Add(YapsInspectorStyle.Field(lightsProp, type.GetField("emitLights"), "Emit marker lights"));
            advanced.Body.Add(BridgeElements.Row(
                YapsInspectorStyle.Button(built ? "Rebuild markers" : "Build markers", () =>
                {
                    Undo.RegisterFullObjectHierarchyUndo(socket.gameObject, "Rebuild YAPS socket");
                    YapsSocketBuilder.Build(socket);
                    RebuildLater();
                }),
                YapsInspectorStyle.Button("Open YAPS Setup", YapsSetupWindow.Open)));
            body.Add(advanced);

            _root.Bind(so);
        }

        // The material carrying this socket's baked deform: on the renderer
        // the socket names, or failing that any YAPS-socket material on a
        // mesh this socket's bone drives.
        static Material FindSocketMaterial(YapsSocket socket)
        {
            IEnumerable<Renderer> candidates = socket.renderer != null
                ? new Renderer[] { socket.renderer }
                : AvatarRootOf(socket.transform).GetComponentsInChildren<Renderer>(true);
            foreach (var r in candidates)
            {
                if (r == null) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m != null && m.HasProperty("_YAPS_SocketPower") && m.HasProperty("_YAPS_Bake")
                        && m.GetTexture("_YAPS_Bake") != null && m.GetFloat("_YAPS_SocketPower") >= 0f
                        && (socket.renderer != null || m.GetFloat("_YAPS_SocketPower") > 0f))
                        return m;
                }
            }
            return null;
        }

        static SkinnedMeshRenderer GuessRenderer(Transform socket, List<SkinnedMeshRenderer> renderers)
        {
            for (var bone = socket.parent; bone != null; bone = bone.parent)
                foreach (var r in renderers)
                    if (r.bones != null && System.Array.IndexOf(r.bones, bone) >= 0) return r;
            return renderers.OrderByDescending(r => r.sharedMesh.blendShapeCount).FirstOrDefault();
        }

        // Built means a plug can FIND it: a root marker light or a root
        // pointer somewhere beneath. Not "has the folder the toolkit makes"
        // — a converted socket keeps VRCFury's WorldSpace/Lights and
        // WorldSpace/Senders and is every bit as built, and twelve of
        // Angela's read "not built" while working perfectly.
        internal static bool IsBuilt(Transform socket)
        {
            foreach (var l in socket.GetComponentsInChildren<Light>(true))
            {
                if (!YapsScanner.IsProtocolLight(l)) continue;
                int d = YapsScanner.LightDigit(l);
                if (d >= 1 && d <= 6) return true;
            }
            foreach (var p in socket.GetComponentsInChildren<ABI.CCK.Components.CVRPointer>(true))
            {
                if (p == null || string.IsNullOrEmpty(p.type)) continue;
                if (p.type.StartsWith("SPSLL_Socket_") || p.type.StartsWith("TPS_Orf_")) return true;
            }
            return false;
        }

        internal static Transform AvatarRootOf(Transform t)
        {
            Transform top = t;
            for (var at = t; at != null; at = at.parent)
            {
                if (at.GetComponent<Animator>() != null) return at;
                top = at;
            }
            return top;
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.InSelectionHierarchy | GizmoType.Pickable)]
        static void DrawSocket(YapsSocket socket, GizmoType type)
        {
            bool selected = (type & GizmoType.Selected) != 0;
            var t = socket.transform;
            bool built = IsBuilt(t);
            bool hole = socket.kind == YapsSocket.SocketKind.Hole;
            var colour = !built ? YapsInspectorStyle.BadColour : hole ? YapsInspectorStyle.HoleColour : YapsInspectorStyle.RingColour;
            colour.a = selected ? 1f : 0.7f;

            Vector3 c = t.position, f = t.forward, u = t.up;
            // FIXED world size — a socket about 5 cm across, drawn at 5 cm.
            // It used to scale with the camera distance and that swam as
            // you moved; fixed, it sits still and reads as a thing in the
            // world. Obvious enough by weight of line, quiet enough by
            // having one ring, one arrow, and nothing that competes with
            // the CCK's own icons on the markers beneath. Follows the
            // avatar's scale so a shrunk avatar's sockets shrink with it.
            float scale = Mathf.Max(0.05f, (t.lossyScale.x + t.lossyScale.y + t.lossyScale.z) / 3f);
            float r = 0.05f * scale;
            float thick = selected ? 4f : 3f;

            Handles.color = colour;
            Handles.DrawWireDisc(c, f, r, thick);
            // Entry arrow: from where a plug comes, into the socket. Short.
            Handles.DrawLine(c + f * (r * 2.0f), c + f * (r * 0.6f), thick);
            Handles.ConeHandleCap(0, c + f * (r * 0.6f), Quaternion.LookRotation(-f), r * 0.5f, EventType.Repaint);
            if (hole)
            {
                // Depth: a fainter ring set back, joined at four points, so
                // it reads as a short tube rather than a disc.
                var faint = colour; faint.a *= 0.4f;
                Handles.color = faint;
                Vector3 back = c - f * (r * 1.4f);
                Handles.DrawWireDisc(back, f, r * 0.7f, thick * 0.6f);
                for (int i = 0; i < 4; i++)
                {
                    var q = Quaternion.AngleAxis(i * 90f, f);
                    Handles.DrawLine(c + q * u * r, back + q * u * (r * 0.7f), thick * 0.5f);
                }
            }
            if (selected || !built)
            {
                var label = new GUIStyle(EditorStyles.whiteMiniLabel) { fontSize = 11 };
                Handles.Label(c + u * (r * 1.4f), (hole ? "hole" : "ring") + (built ? "" : "  — not built"), label);
            }
        }
    }

    // --- plug --------------------------------------------------------------

    [CustomEditor(typeof(YapsPlug))]
    public class YapsPlugEditor : Editor
    {
        VisualElement _root;

        // The component's fields, grouped into cards by the [Header] each
        // declares — the same names the material panel uses. Three cards:
        // the mesh, how it moves, and sockets; the sections are subheadings
        // within them, so the inspector reads like the window does.
        static readonly string[] MeshHeaders = { "Mesh", "Skinned mesh", "Measurement" };
        static readonly string[] MoveHeaders =
        {
            "Shape at rest", "Inside a socket", "Out of a socket", "Motion inside a socket", "The bend toward a socket",
        };

        // The filter and the folds' open state, kept for the session so
        // clicking between plugs does not reset the view.
        static readonly List<string> Systems = new List<string> { "All systems", "DPS", "TPS", "SPS", "YAPS" };
        static string _filter = "All systems";
        static readonly Dictionary<string, bool> _open = new Dictionary<string, bool>();

        static bool Passes(string from)
        {
            if (_filter == "All systems") return true;
            if (string.IsNullOrEmpty(from)) return false;
            return from.Split(new[] { " · " }, System.StringSplitOptions.RemoveEmptyEntries).Contains(_filter);
        }

        // A fold's summary names the systems its knobs came from; a fold
        // the filter emptied is hidden rather than left as a bare header.
        static void FinishFold(BridgeElements.Card fold, HashSet<string> systems)
        {
            if (fold == null) return;
            var ordered = new[] { "DPS", "TPS", "SPS", "YAPS" }.Where(systems.Contains).ToList();
            fold.SetSummary(ordered.Count > 0 ? "from " + string.Join(" · ", ordered) : null);
            if (fold.Body.childCount == 0) fold.style.display = DisplayStyle.None;
        }

        // A selected plug animates in the scene view — wriggle and pumping
        // are time-driven, and a scene view left to itself repaints only on
        // input. YapsPreview stops the repaint on its own once nothing
        // needs it.
        void OnEnable() => YapsPreview.Animate(true);

        public override VisualElement CreateInspectorGUI()
        {
            _root = YapsInspectorStyle.Root();
            Rebuild();
            return _root;
        }

        void RebuildLater() => _root?.schedule.Execute(Rebuild);

        void Rebuild()
        {
            if (_root == null) return;
            _root.Clear();
            serializedObject.Update();
            var plug = (YapsPlug) target;
            var renderer = plug.Target;
            var baked = BakedMaterials(renderer);
            bool isBaked = baked.Count > 0;
            float len = isBaked ? baked[0].GetFloat("_YAPS_Length") : 0f;

            _root.Add(BridgeElements.Banner("Plug  ·  " + plug.name,
                renderer == null ? "no renderer — pick the mesh that bends"
                : isBaked ? $"baked  ·  {len:0.###} m  ·  {baked[0].name}" : "not baked yet — set it up, then Bake",
                isBaked ? "YAPS" : "not baked"));

            var body = new VisualElement();
            body.AddToClassList("ab-scroll");
            _root.Add(body);

            var mesh = new BridgeElements.Card("Mesh");
            var move = new BridgeElements.Card("How it moves");
            var sockets = new BridgeElements.Card("Sockets");
            body.Add(mesh); body.Add(move); body.Add(sockets);

            // WHERE DO I LOOK. Every knob came from somewhere — DPS, TPS,
            // SPS, or YAPS itself — and a user who knows a feature from one
            // of those knows it by that system's name. So each knob wears a
            // tag saying which, each section is a fold that says what it
            // holds, and one filter shows only the knobs of one system.
            var filter = new PopupField<string>("Show", Systems.ToList(), Systems.IndexOf(_filter) < 0 ? 0 : Systems.IndexOf(_filter));
            filter.AddToClassList("ab-field");
            filter.RegisterValueChangedCallback(e => { _filter = e.newValue; RebuildLater(); });
            move.Body.Add(filter);
            move.Body.Add(BridgeElements.Hint(
                "Every knob is tagged with the system it comes from. Know a feature from DPS, TPS or SPS? " +
                "Pick that system and only its knobs stay. YAPS is what none of them had. Applies to Sockets below too."));

            // Walk the serialized fields in declaration order; a [Header]
            // opens a section in whichever card owns it — a subheading in
            // Mesh, a fold in the other two — and each knob's origin decides
            // whether the filter lets it through.
            var it = serializedObject.GetIterator();
            it.NextVisible(true);   // m_Script
            string header = "Mesh";
            VisualElement into = mesh.Body;
            BridgeElements.Card fold = null;
            var foldSystems = new HashSet<string>();
            bool first = true;
            while (it.NextVisible(false))
            {
                var field = typeof(YapsPlug).GetField(it.name);
                var h = field?.GetCustomAttributes(typeof(HeaderAttribute), false).FirstOrDefault() as HeaderAttribute;
                var from = (field?.GetCustomAttributes(typeof(YapsFromAttribute), false).FirstOrDefault() as YapsFromAttribute)?.System;
                if (h != null && h.header != header)
                {
                    FinishFold(fold, foldSystems);
                    header = h.header;
                    if (MeshHeaders.Contains(header))
                    {
                        into = mesh.Body; fold = null;
                        into.Add(BridgeElements.SubHeading(header));
                    }
                    else
                    {
                        var owner = MoveHeaders.Contains(header) ? move.Body : sockets.Body;
                        if (!_open.TryGetValue(header, out bool open)) open = true;
                        string title = header;
                        fold = new BridgeElements.Card(header, null, open, null, 0f, o => _open[title] = o);
                        fold.AddToClassList("ab-fold");
                        owner.Add(fold);
                        into = fold.Body;
                        foldSystems.Clear();
                    }
                }
                else if (first)
                {
                    into.Add(BridgeElements.SubHeading("Mesh"));
                }
                first = false;

                if (!string.IsNullOrEmpty(from))
                    foreach (var s in from.Split(new[] { " · " }, System.StringSplitOptions.RemoveEmptyEntries)) foldSystems.Add(s);
                if (!Passes(from)) continue;
                into.Add(YapsInspectorStyle.Field(it.Copy(), field, null, from));
            }
            FinishFold(fold, foldSystems);

            var bake = new BridgeElements.Card("Bake");
            bake.Body.Add(BridgeElements.Hint(isBaked
                ? "The knobs above write straight to the plug's material, and the material's YAPS panel writes back here — same values, two doors. Mesh, bone and measurement changes need a re-bake."
                : "Set the mesh up, then Bake: it measures the mesh, patches the material's own shader (or falls back to YAPS Simple Lit), writes the knobs and announces the plug to every socket family."));
            bake.Body.Add(new BridgeElements.PrimaryButton(isBaked ? "Re-bake" : "Bake", () =>
            {
                var o = YapsNativeBuilder.Bake(plug);
                if (o.Ok) Debug.Log("[YAPS] " + o.Message); else Debug.LogError("[YAPS] " + o.Message);
                RebuildLater();
            }));
            bake.Body.Add(BridgeElements.Row(YapsInspectorStyle.Button("Open YAPS Setup", YapsSetupWindow.Open)));
            body.Add(bake);

            _root.Bind(serializedObject);

            // A knob moved here moves the material the same instant. The
            // material's YAPS panel writes back the same way, so the two
            // agree; and a change of renderer rebuilds so the banner and
            // the bake state follow.
            body.TrackSerializedObjectValue(serializedObject, so =>
            {
                var mats = BakedMaterials(plug.Target);
                foreach (var m in mats)
                {
                    Undo.RecordObject(m, "YAPS plug knobs");
                    YapsNativeBuilder.WriteKnobs(plug, m);
                    if (plug.lengthOverride > 0) m.SetFloat("_YAPS_Length", plug.lengthOverride);
                    EditorUtility.SetDirty(m);
                }
                if ((mats.Count > 0) != isBaked || plug.Target != renderer) RebuildLater();
                SceneView.RepaintAll();
            });
        }

        static List<Material> BakedMaterials(Renderer renderer)
        {
            var list = new List<Material>();
            if (renderer == null) return list;
            foreach (var m in renderer.sharedMaterials)
                if (m != null && m.HasProperty("_YAPS_Bake") && m.HasProperty("_YAPS_Length")) list.Add(m);
            return list;
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.InSelectionHierarchy | GizmoType.Pickable)]
        static void DrawPlug(YapsPlug plug, GizmoType type)
        {
            bool selected = (type & GizmoType.Selected) != 0;
            var renderer = plug.Target;
            var colour = YapsInspectorStyle.PlugColour; colour.a = selected ? 1f : 0.7f;
            Handles.color = colour;

            float length = 0f;
            if (renderer != null)
                foreach (var m in renderer.sharedMaterials)
                    if (m != null && m.HasProperty("_YAPS_Length")) { length = m.GetFloat("_YAPS_Length"); break; }

            // The frame: the markers object if built (it sits on the measured
            // frame), else the plug object.
            var frame = plug.transform.Find("YAPS Markers") ?? plug.transform;
            Vector3 b = frame.position, f = frame.forward;
            float thick = selected ? 4f : 3f;
            float scale = Mathf.Max(0.05f, (frame.lossyScale.x + frame.lossyScale.y + frame.lossyScale.z) / 3f);

            if (length <= 0f)
            {
                Handles.DrawDottedLine(b, b + f * 0.3f * scale, 5f);
                var label = new GUIStyle(EditorStyles.whiteMiniLabel) { fontSize = 11 };
                Handles.Label(b + f * 0.3f * scale, "plug — not baked", label);
                return;
            }
            Vector3 tip = b + f * length * scale;
            // True length along the axis, a ring at the base at a typical
            // radius, a small arrow at the tip. Fixed world size.
            float r = 0.035f * scale;
            Handles.DrawLine(b, tip, thick);
            Handles.DrawWireDisc(b, f, r, thick);
            Handles.ConeHandleCap(0, tip, Quaternion.LookRotation(f), r * 1.2f, EventType.Repaint);
            if (selected)
            {
                var label = new GUIStyle(EditorStyles.whiteMiniLabel) { fontSize = 11 };
                Handles.Label(tip + f * (r * 1.5f), $"{length:0.###} m", label);
            }
        }
    }
}
#endif
