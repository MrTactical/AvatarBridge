// The inspectors for YapsSocket and YapsPlug, and the scene gizmos that
// go with them. Same visual language as the shader panel and the window:
// a tinted header strip, sections with a coloured bar, one line of what
// each thing means, controls that read as controls. This is where "drag a
// prefab in and it just works" gets its second half — the moment a socket
// lands under a bone you SEE it, and the shape rows offer the avatar's own
// blendshapes from a dropdown instead of a name to type.
//
// The gizmos are drawn at a FIXED world size — about 5 cm, twice a real
// socket, so they hold their own beside the CCK's icons — with
// enough line weight to read and nothing extra. A first version scaled
// them with the camera distance so they stayed a fixed size on screen,
// and that swam disorientingly as you moved. Fixed, they sit still and
// read as things in the world. The plug axis is its true length — that IS
// the information — with a modest arrow at the tip.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    // --- shared drawing --------------------------------------------------

    static class YapsInspectorStyle
    {
        public static readonly Color HoleColour = new Color(0.25f, 0.85f, 0.95f);
        public static readonly Color RingColour = new Color(0.95f, 0.55f, 0.20f);
        public static readonly Color PlugColour = new Color(0.55f, 0.85f, 0.35f);
        public static readonly Color BadColour = new Color(0.95f, 0.30f, 0.30f);

        static GUIStyle _title, _sub, _section, _blurb, _tag;

        static void Ensure()
        {
            if (_title != null) return;
            _title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, normal = { textColor = Color.white } };
            _sub = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1, 1, 1, 0.82f) } };
            _section = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, padding = new RectOffset(8, 0, 0, 0), alignment = TextAnchor.MiddleLeft };
            _blurb = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, padding = new RectOffset(2, 2, 0, 6), normal = { textColor = new Color(0.65f, 0.65f, 0.65f) } };
            _tag = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(1, 1, 1, 0.9f) } };
        }

        // The header: the family gradient, like the window and the material
        // panel, bled to the inspector's edges. The socket's own colour is a
        // thin bar on the left — an accent that matches its gizmo, not a
        // wash over the whole thing. Kind and name in the title, state in
        // the subtitle, a tag on the right.
        public static void Header(string title, string subtitle, Color tint, string rightTag = null)
        {
            Ensure();
            var laid = GUILayoutUtility.GetRect(0, 42, GUILayout.ExpandWidth(true));
            var rect = new Rect(0, laid.y, EditorGUIUtility.currentViewWidth, laid.height);
            const int steps = 32;
            for (int i = 0; i < steps; i++)
            {
                var slice = new Rect(rect.x + rect.width * i / steps, rect.y, rect.width / steps + 1, rect.height);
                EditorGUI.DrawRect(slice, BridgeTheme.At((float) i / (steps - 1)));
            }
            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.18f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4, rect.height), tint);
            GUI.Label(new Rect(rect.x + 14, rect.y + 5, rect.width - 28, 20), title, _title);
            GUI.Label(new Rect(rect.x + 14, rect.y + 23, rect.width - 28, 16), subtitle, _sub);
            if (!string.IsNullOrEmpty(rightTag))
                GUI.Label(new Rect(rect.x, rect.y + 5, rect.width - 12, 16), rightTag, _tag);
            GUILayout.Space(8);
        }

        // A section: a hairline of colour, the title, a rule beneath. The
        // same header the material panel draws, so the two read as one
        // system. No slab.
        public static void Section(string title, Color tint, string blurb = null)
        {
            Ensure();
            GUILayout.Space(6);
            var laid = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            var rect = new Rect(0, laid.y, EditorGUIUtility.currentViewWidth, laid.height);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 4, 3, rect.height - 8), tint);
            GUI.Label(new Rect(rect.x + 14, rect.y, rect.width - 20, rect.height), title, _section);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1),
                new Color(0.5f, 0.5f, 0.5f, EditorGUIUtility.isProSkin ? 0.18f : 0.28f));
            GUILayout.Space(2);
            if (!string.IsNullOrEmpty(blurb)) GUILayout.Label(blurb, _blurb);
        }

        public static void Note(string text, MessageType type = MessageType.None)
        {
            Ensure();
            if (type == MessageType.None) GUILayout.Label(text, _blurb);
            else EditorGUILayout.HelpBox(text, type);
        }

    }

    // --- socket ------------------------------------------------------------

    [CustomEditor(typeof(YapsSocket))]
    [CanEditMultipleObjects]
    public class YapsSocketEditor : Editor
    {
        SerializedProperty _kind, _tag, _renderer, _shapes, _power, _lights, _preview;

        void OnEnable()
        {
            _kind = serializedObject.FindProperty("kind");
            _tag = serializedObject.FindProperty("tag");
            _renderer = serializedObject.FindProperty("renderer");
            _shapes = serializedObject.FindProperty("shapes");
            _power = serializedObject.FindProperty("shapePower");
            _lights = serializedObject.FindProperty("emitLights");
            _preview = serializedObject.FindProperty("preview");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var socket = (YapsSocket) target;
            var avatarRoot = AvatarRootOf(socket.transform);
            bool hole = _kind.enumValueIndex == (int) YapsSocket.SocketKind.Hole;
            bool built = IsBuilt(socket.transform);
            var tint = !built ? YapsInspectorStyle.BadColour : hole ? YapsInspectorStyle.HoleColour : YapsInspectorStyle.RingColour;

            YapsInspectorStyle.Header(
                (hole ? "Hole" : "Ring") + "  ·  " + socket.name,
                built ? "readable by DPS, TPS, SPS and YAPS plugs" : "not built — no plug can find it yet",
                tint, built ? "YAPS" : "!");

            // Kind + tag, as a row.
            YapsInspectorStyle.Section("What it is", tint);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(_kind, GUIContent.none, GUILayout.Width(120));
                GUILayout.Label(hole ? "closes around the plug and stops it" : "lets the plug pass straight through",
                    EditorStyles.miniLabel);
            }
            EditorGUILayout.PropertyField(_tag, new GUIContent("Tag", "Optional. Plugs can be told to answer only sockets with a tag, or never ones with another."));

            // Shapes.
            YapsInspectorStyle.Section("Opens as a plug goes in", tint,
                "Pick the mesh, then up to four of its shapes. The entry opens as the plug arrives; " +
                "each later one starts deeper. Depths are fractions of the plug's length, and they " +
                "stack — once a shape has opened it stays open as the plug goes deeper.");

            var renderers = avatarRoot != null
                ? avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0).ToList()
                : new List<SkinnedMeshRenderer>();
            var current = _renderer.objectReferenceValue as SkinnedMeshRenderer;
            if (current == null && renderers.Count > 0)
            {
                current = GuessRenderer(socket.transform, renderers);
                if (current != null) _renderer.objectReferenceValue = current;
            }
            int rIndex = current != null ? renderers.IndexOf(current) : -1;
            var rNames = new[] { "None — bend plugs, play no shape" }
                .Concat(renderers.Select(r => $"{r.name}   ·   {r.sharedMesh.blendShapeCount} shapes")).ToArray();
            int picked = EditorGUILayout.Popup(new GUIContent("Mesh"), rIndex + 1, rNames) - 1;
            if (picked != rIndex)
            {
                _renderer.objectReferenceValue = picked >= 0 ? renderers[picked] : null;
                current = picked >= 0 ? renderers[picked] : null;
            }

            if (current != null)
            {
                var mesh = current.sharedMesh;
                var shapeNames = Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToArray();
                var options = new[] { "— pick a shape —" }.Concat(shapeNames).ToArray();
                string[] stageNames = { "Entry", "Depth 1", "Depth 2", "Depth 3" };

                for (int i = 0; i < _shapes.arraySize && i < 4; i++)
                {
                    var row = _shapes.GetArrayElementAtIndex(i);
                    var name = row.FindPropertyRelative("blendshape");
                    var start = row.FindPropertyRelative("startsAt");
                    var fade = row.FindPropertyRelative("fadeOver");
                    int sIndex = System.Array.IndexOf(shapeNames, name.stringValue);

                    var box = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    // A tinted stripe per stage, deeper = more saturated.
                    var stripe = new Rect(box.x, box.y, 3, box.height);
                    EditorGUI.DrawRect(stripe, Color.Lerp(tint, Color.white, 0.6f - i * 0.2f));
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(stageNames[i], EditorStyles.boldLabel, GUILayout.Width(56));
                        int sPicked = EditorGUILayout.Popup(sIndex + 1, options) - 1;
                        if (sPicked != sIndex) name.stringValue = sPicked >= 0 ? shapeNames[sPicked] : "";
                        if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(58)))
                        {
                            _shapes.DeleteArrayElementAtIndex(i);
                            EditorGUILayout.EndVertical();
                            break;
                        }
                    }
                    // Depth as ONE range bar: from start to fully-open.
                    float s = start.floatValue, e = Mathf.Min(1f, start.floatValue + fade.floatValue);
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.MinMaxSlider(new GUIContent("Opens from → fully open by",
                        "as fractions of the plug's length"), ref s, ref e, 0f, 1f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        start.floatValue = s;
                        fade.floatValue = Mathf.Max(0.01f, e - s);
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(EditorGUIUtility.labelWidth);
                        GUILayout.Label($"{s:0.00}  →  {e:0.00}  of the plug's length", EditorStyles.miniLabel);
                    }
                    if (!string.IsNullOrEmpty(name.stringValue) && sIndex < 0)
                        EditorGUILayout.HelpBox($"\"{name.stringValue}\" is not on this mesh.", MessageType.Warning);
                    EditorGUILayout.EndVertical();
                }
                if (_shapes.arraySize < 4)
                {
                    if (GUILayout.Button(_shapes.arraySize == 0 ? "+ Add the entry shape" : "+ Add a deeper shape"))
                    {
                        _shapes.arraySize++;
                        var row = _shapes.GetArrayElementAtIndex(_shapes.arraySize - 1);
                        row.FindPropertyRelative("blendshape").stringValue = "";
                        row.FindPropertyRelative("startsAt").floatValue = 0.25f * (_shapes.arraySize - 1);
                        row.FindPropertyRelative("fadeOver").floatValue = 0.3f;
                    }
                }
                if (_shapes.arraySize > 0)
                    EditorGUILayout.Slider(_power, 0f, 1f, new GUIContent("Strength", "how far the shapes open — 1 is as authored"));
            }
            else if (renderers.Count == 0)
            {
                YapsInspectorStyle.Note(avatarRoot == null
                    ? "Put this socket under an avatar or prop and its meshes will appear here."
                    : "No skinned mesh with blendshapes under this avatar — the socket bends plugs and plays no shape.",
                    MessageType.Info);
            }

            // Preview.
            // Preview needs a PLUG to bend, and "there is nothing there" is
            // what a user sees when the scene has none. So Preview brings
            // its own: if no baked YAPS plug is in the scene, a test plug is
            // dropped a little way in front of the socket, aimed at it, and
            // removed again when preview goes off. With a real plug in the
            // scene it bends that one instead and spawns nothing.
            int bakedPlugs = CountBakedPlugs();
            YapsInspectorStyle.Section("See it work", tint,
                bakedPlugs > 0
                    ? $"Preview bends the {bakedPlugs} YAPS plug{(bakedPlugs == 1 ? "" : "s")} in the scene toward this socket, in the editor, so you can place it and watch before uploading. Writes nothing that ships."
                    : "There is no baked plug in the scene, so Preview drops a test plug in front of this socket and bends it. Move the socket and watch it follow. The test plug goes when preview stops.");
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                bool p = GUILayout.Toggle(_preview.boolValue,
                    _preview.boolValue ? "  Previewing — the plug bends toward this socket. Click to stop."
                                       : (bakedPlugs > 0 ? "  Preview" : "  Preview with a test plug"),
                    "Button", GUILayout.Height(24));
                if (EditorGUI.EndChangeCheck())
                {
                    _preview.boolValue = p;
                    serializedObject.ApplyModifiedProperties();
                    if (p && CountBakedPlugs() == 0) SpawnPreviewPlug(socket);
                    if (!p) RemovePreviewPlug(socket);
                    SceneView.RepaintAll();
                }
            }

            // The socket-side deform, once baked. These knobs live on the
            // renderer's material — the shader reads them there — but a
            // socket's customisation belongs in one place, so they are drawn
            // here too when the socket's mesh has been baked. Same values,
            // written straight through.
            var bakedMat = FindSocketMaterial(socket);
            if (bakedMat != null)
            {
                YapsInspectorStyle.Section("How it opens", tint,
                    "Read by the shader on this socket's mesh. Strength scales all the shapes; each stage " +
                    "opens from its start to start + fade, as fractions of the plug's length. Depth comes " +
                    "from the plug's tracker light, or from the contact channel where there is one.");
                EditorGUI.BeginChangeCheck();
                float power = EditorGUILayout.Slider(new GUIContent("Strength"), bakedMat.GetFloat("_YAPS_SocketPower"), 0f, 1f);
                Vector4 starts = bakedMat.GetVector("_YAPS_SocketShapeStart");
                Vector4 fades = bakedMat.GetVector("_YAPS_SocketShapeFade");
                string[] stageNames = { "Entry", "Depth 1", "Depth 2", "Depth 3" };
                for (int i = 0; i < 4; i++)
                {
                    float s = starts[i], e = Mathf.Min(1f, starts[i] + fades[i]);
                    EditorGUILayout.MinMaxSlider(new GUIContent(stageNames[i]), ref s, ref e, 0f, 1f);
                    starts[i] = s; fades[i] = Mathf.Max(0.01f, e - s);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(bakedMat, "YAPS socket shape");
                    bakedMat.SetFloat("_YAPS_SocketPower", power);
                    bakedMat.SetVector("_YAPS_SocketShapeStart", starts);
                    bakedMat.SetVector("_YAPS_SocketShapeFade", fades);
                    EditorUtility.SetDirty(bakedMat);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Open material", EditorStyles.miniButton, GUILayout.Width(100)))
                        Selection.activeObject = bakedMat;
                }
            }

            // Advanced.
            YapsInspectorStyle.Section("Advanced", tint);
            EditorGUILayout.PropertyField(_lights, new GUIContent("Emit marker lights",
                "What lets DPS plugs, and plugs with no sync budget, find this socket. Costs no sync. " +
                "An avatar with many sockets should wire these to menu toggles instead — the toolkit does that."));

            GUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(built ? "Rebuild markers" : "Build markers", GUILayout.Height(24)))
                {
                    Undo.RegisterFullObjectHierarchyUndo(socket.gameObject, "Rebuild YAPS socket");
                    YapsSocketBuilder.Build(socket);
                }
                if (GUILayout.Button("Open YAPS Setup", GUILayout.Height(24))) YapsSetupWindow.Open();
            }

            serializedObject.ApplyModifiedProperties();
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

        const string PreviewPlugName = "YAPS Preview Plug";

        static int CountBakedPlugs()
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
        static void SpawnPreviewPlug(YapsSocket socket)
        {
            var existing = GameObject.Find(PreviewPlugName);
            if (existing != null) return;
            var go = YapsNativeBuilder.BuildTestPlug(null);
            if (go == null) return;
            go.name = PreviewPlugName;
            var t = socket.transform;
            // A quarter metre is the test plug's length; sit its base 0.3 m
            // out along the socket's forward, pointing back at it.
            go.transform.position = t.position + t.forward * 0.3f;
            go.transform.rotation = Quaternion.LookRotation(-t.forward, t.up);
            EditorGUIUtility.PingObject(go);
        }

        static void RemovePreviewPlug(YapsSocket socket)
        {
            var existing = GameObject.Find(PreviewPlugName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);
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

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
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
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var plug = (YapsPlug) target;
            var renderer = plug.Target;
            var tint = YapsInspectorStyle.PlugColour;

            float len = 0f; string matName = null;
            if (renderer != null)
                foreach (var m in renderer.sharedMaterials)
                    if (m != null && m.HasProperty("_YAPS_Bake")) { len = m.GetFloat("_YAPS_Length"); matName = m.name; break; }
            bool baked = matName != null;

            YapsInspectorStyle.Header(
                "Plug  ·  " + plug.name,
                renderer == null ? "no renderer — pick the mesh that bends"
                : baked ? $"baked  ·  {len:0.###} m  ·  {matName}" : "not baked yet — set it up, then Bake",
                renderer == null ? YapsInspectorStyle.BadColour : tint, baked ? "YAPS" : "!");

            var it = serializedObject.GetIterator();
            it.NextVisible(true);   // m_Script
            string currentHeader = null;
            while (it.NextVisible(false))
            {
                // Group by the [Header] the component declares, drawn as
                // our sections. Unity would draw the header itself; we
                // draw a tinted one and let the field through.
                var field = typeof(YapsPlug).GetField(it.name);
                var header = field?.GetCustomAttributes(typeof(HeaderAttribute), false).FirstOrDefault() as HeaderAttribute;
                if (header != null && header.header != currentHeader)
                {
                    currentHeader = header.header;
                    YapsInspectorStyle.Section(currentHeader, tint);
                }
                else if (currentHeader == null)
                {
                    YapsInspectorStyle.Section("Mesh", tint);
                    currentHeader = "Mesh";
                }
                EditorGUILayout.PropertyField(it, true);
            }

            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(baked ? "Re-bake" : "Bake", GUILayout.Height(26)))
                {
                    var o = YapsNativeBuilder.Bake(plug);
                    if (o.Ok) Debug.Log("[YAPS] " + o.Message); else Debug.LogError("[YAPS] " + o.Message);
                }
                if (GUILayout.Button("Open YAPS Setup", GUILayout.Height(26))) YapsSetupWindow.Open();
            }
            serializedObject.ApplyModifiedProperties();
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
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
