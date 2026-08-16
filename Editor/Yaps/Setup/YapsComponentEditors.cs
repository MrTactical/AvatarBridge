// Inspectors and scene gizmos for YapsSocket and YapsPlug, built with
// UI Toolkit on the same elements as the windows. Gizmos are drawn at a
// fixed world size, about 5 cm.
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

        // The family stylesheet and skin.
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

        // A bound control for one field, with its help line under it.
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

            // A chip per system to the control's right.
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

        // The four systems, by colour.
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
            b.AddToClassList("ab-btn");
            return b;
        }
    }

    // --- preview -----------------------------------------------------------

    // Turns a socket's preview on and off for the inspector and the window.
    // Spawns a test plug when the scene has no baked plug. Repaints the
    // scene view while a preview is on or a plug is selected.
    static class YapsPreview
    {
        public const string PlugName = "YAPS Preview Plug";
        static bool _animating;

        public static void Set(YapsSocket socket, bool on)
        {
            if (socket == null) return;
            socket.preview = on;
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
            // Stop once nothing needs it.
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

        // A test plug in front of the socket, engaged but not swallowed.
        static void Spawn(YapsSocket socket)
        {
            if (GameObject.Find(PlugName) != null) return;
            // Keep the socket selected; its inspector holds the stop button.
            var go = YapsNativeBuilder.BuildTestPlug(null, select: false);
            if (go == null) return;
            go.name = PlugName;
            var t = socket.transform;
            // Base 0.3 m out along the socket's forward, pointing back at it.
            go.transform.position = t.position + t.forward * 0.3f;
            go.transform.rotation = Quaternion.LookRotation(-t.forward, t.up);
            EditorGUIUtility.PingObject(go);

            // Say when it did not bake; the console alone was missed.
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

        // The preview plug's mesh, material and bake go with it. The shader copy stays.
        static void Remove()
        {
            var existing = GameObject.Find(PlugName);
            if (existing == null) return;
            var doomed = new List<string>();
            var mf = existing.GetComponent<MeshFilter>();
            var mr = existing.GetComponent<MeshRenderer>();
            if (mf != null && mf.sharedMesh != null) doomed.Add(AssetDatabase.GetAssetPath(mf.sharedMesh));
            if (mr != null)
            {
                foreach (var m in mr.sharedMaterials)
                {
                    if (m == null) continue;
                    if (m.HasProperty("_YAPS_Bake") && m.GetTexture("_YAPS_Bake") != null)
                        doomed.Add(AssetDatabase.GetAssetPath(m.GetTexture("_YAPS_Bake")));
                    doomed.Add(AssetDatabase.GetAssetPath(m));
                }
            }
            Object.DestroyImmediate(existing);
            foreach (var path in doomed)
            {
                if (string.IsNullOrEmpty(path) || !path.StartsWith(YapsNativeBuilder.OutputRoot)) continue;
                AssetDatabase.DeleteAsset(path);
            }
        }
    }

    // --- socket ------------------------------------------------------------

    [CustomEditor(typeof(YapsSocket))]
    public class YapsSocketEditor : Editor
    {
        VisualElement _root;

        // Ticks the preview on every scene repaint while selected.
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

        // Callbacks ask for a rebuild from inside the elements about to go.
        void RebuildLater() => _root?.schedule.Execute(Rebuild);

        // Rebuilt on structural change and bound. Sliders need nothing.
        void Rebuild()
        {
            if (_root == null) return;
            _root.Clear();
            serializedObject.Update();
            var socket = (YapsSocket) target;
            var so = serializedObject;
            var kindProp = so.FindProperty("kind");
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
                    if (i == kindProp.enumValueIndex) return;
                    // One undo step for the kind and the markers it rebuilds:
                    // the hierarchy is recorded before the enum changes.
                    Undo.IncrementCurrentGroup();
                    int group = Undo.GetCurrentGroup();
                    Undo.RegisterFullObjectHierarchyUndo(socket.gameObject, "YAPS socket kind");
                    kindProp.enumValueIndex = i;
                    so.ApplyModifiedProperties();
                    YapsSocketBuilder.ApplyKind(socket);
                    Undo.CollapseUndoOperations(group);
                    RebuildLater();
                }));
            what.Body.Add(BridgeElements.Hint(hole
                ? "A hole closes around the plug and stops it — a mouth, a pussy, an anus."
                : "A ring lets the plug pass straight through — a hand, thighs, a foot."));
            body.Add(what);

            // Shapes.
            var opens = new BridgeElements.Card("Opens as a plug goes in");
            opens.Body.Add(BridgeElements.Hint(
                "For a socket with a mesh of its own, origin at the entrance: pick that mesh, then up " +
                "to four of its shapes. The entry opens as the plug arrives; each later one starts " +
                "deeper. Depths are fractions of the plug's length, and they stack. Baked by " +
                "\"Bake every plug and verify\". A body mesh cannot open this way; its reactions " +
                "stay with the animator."));

            var renderers = avatarRoot != null
                ? avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0).ToList()
                : new List<SkinnedMeshRenderer>();
            // The user's choice, never a guess: a guess written on every
            // draw made "None" impossible and dirtied a socket on selection.
            var current = rendererProp.objectReferenceValue as SkinnedMeshRenderer;
            if (current != null && !renderers.Contains(current)) renderers.Insert(0, current);
            var rNames = new List<string> { "None — bend plugs, play no shape" };
            rNames.AddRange(renderers.Select(r => $"{r.name}   ·   {r.sharedMesh.blendShapeCount} shapes"));
            int rIndex = current != null ? renderers.IndexOf(current) : -1;
            var meshPopup = new PopupField<string>("Mesh", rNames, rIndex + 1);
            meshPopup.AddToClassList("ab-field");
            meshPopup.RegisterValueChangedCallback(e =>
            {
                int picked = meshPopup.index - 1;
                rendererProp.objectReferenceValue = picked >= 0 && picked < renderers.Count ? renderers[picked] : null;
                so.ApplyModifiedProperties();
                RebuildLater();
            });
            if (current != null && !YapsNativeBuilder.MeshIsTheSocket(current, socket.transform))
            {
                opens.Body.Add(new HelpBox(
                    $"\"{current.name}\" does not sit at this socket, so its shapes cannot be baked here. " +
                    "The shader deform needs a mesh whose origin is the socket.", HelpBoxMessageType.Warning));
            }
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
                        int picked = shapePopup.index - 1;
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

                    // Depth as one range bar.
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

            // The socket-side knobs live on the material. Drawn here too once baked.
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

        // The material with this socket's baked deform.
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

        // Built means a plug can find it: a root light or a root pointer beneath.
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
            // Fixed world size, scaled with the avatar.
            float scale = Mathf.Max(0.05f, (t.lossyScale.x + t.lossyScale.y + t.lossyScale.z) / 3f);
            float r = 0.05f * scale;
            float thick = selected ? 4f : 3f;

            Handles.color = colour;
            Handles.DrawWireDisc(c, f, r, thick);
            // Entry arrow, from where a plug comes.
            Handles.DrawLine(c + f * (r * 2.0f), c + f * (r * 0.6f), thick);
            Handles.ConeHandleCap(0, c + f * (r * 0.6f), Quaternion.LookRotation(-f), r * 0.5f, EventType.Repaint);
            if (hole)
            {
                // A fainter ring behind, so a hole reads as a tube.
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

        // Fields grouped into three cards by their [Header].
        static readonly string[] MeshHeaders = { "Mesh", "Skinned mesh", "Measurement" };
        static readonly string[] MoveHeaders =
        {
            "Shape at rest", "Inside a socket", "Out of a socket", "Motion inside a socket", "The bend toward a socket",
        };

        // Filter and fold state, kept for the session.
        static readonly List<string> Systems = new List<string> { "All systems", "DPS", "TPS", "SPS", "YAPS" };
        static string _filter = "All systems";
        static readonly Dictionary<string, bool> _open = new Dictionary<string, bool>();

        static bool Passes(string from)
        {
            if (_filter == "All systems") return true;
            if (string.IsNullOrEmpty(from)) return false;
            return from.Split(new[] { " · " }, System.StringSplitOptions.RemoveEmptyEntries).Contains(_filter);
        }

        // The fold names its systems; an emptied fold is hidden.
        static void FinishFold(BridgeElements.Card fold, HashSet<string> systems)
        {
            if (fold == null) return;
            var ordered = new[] { "DPS", "TPS", "SPS", "YAPS" }.Where(systems.Contains).ToList();
            fold.SetSummary(ordered.Count > 0 ? "from " + string.Join(" · ", ordered) : null);
            if (fold.Body.childCount == 0) fold.style.display = DisplayStyle.None;
        }

        // A selected plug animates in the scene view.
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

            // Every knob is tagged with its system, and the filter shows one system.
            var filter = new PopupField<string>("Show", Systems.ToList(), Systems.IndexOf(_filter) < 0 ? 0 : Systems.IndexOf(_filter));
            filter.AddToClassList("ab-field");
            filter.RegisterValueChangedCallback(e => { _filter = e.newValue; RebuildLater(); });
            move.Body.Add(filter);
            move.Body.Add(BridgeElements.Hint(
                "Every knob is tagged with the system it comes from. Know a feature from DPS, TPS or SPS? " +
                "Pick that system and only its knobs stay. YAPS is what none of them had. Applies to Sockets below too."));

            // Fields in declaration order; a [Header] opens a section.
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

            // Knobs write through to the material. The material panel writes back.
            body.TrackSerializedObjectValue(serializedObject, so =>
            {
                var mats = BakedMaterials(plug.Target);
                if (plug.Target != renderer)
                {
                    // A new renderer that is already baked owns the knobs. Read them in.
                    if (mats.Count > 0)
                    {
                        Undo.RecordObject(plug, "YAPS plug knobs");
                        YapsNativeBuilder.ReadKnobs(plug, mats[0]);
                        EditorUtility.SetDirty(plug);
                    }
                    RebuildLater();
                    return;
                }
                // No undo record on the material: the component owns the
                // values and its own undo replays here, so recording the
                // material again would only discard the redo history.
                foreach (var m in mats)
                {
                    YapsNativeBuilder.WriteKnobs(plug, m);
                    if (plug.lengthOverride > 0) m.SetFloat("_YAPS_Length", plug.lengthOverride);
                    EditorUtility.SetDirty(m);
                }
                if ((mats.Count > 0) != isBaked) RebuildLater();
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

            // The markers object is the frame when built.
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
            // True length along the axis, a ring at the base, an arrow at the tip.
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
