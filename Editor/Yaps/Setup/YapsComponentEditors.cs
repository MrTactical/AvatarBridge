// The inspectors for YapsSocket and YapsPlug, and the scene gizmos that
// go with them. Same visual language as the shader panel and the window:
// a tinted header strip, sections with a coloured bar, one line of what
// each thing means, controls that read as controls. This is where "drag a
// prefab in and it just works" gets its second half — the moment a socket
// lands under a bone you SEE it, and the shape rows offer the avatar's own
// blendshapes from a dropdown instead of a name to type.
//
// The gizmos are drawn READABLE, not physically-sized. A real plug is a
// couple of centimetres across; drawn at that size a socket is a fleck at
// scene-view distance and loses to the CCK's own icons. So the socket
// gizmo scales with the distance to the scene camera — a fixed size on
// screen — and the plug axis is drawn at true length (that IS the
// information) but with fat, distance-scaled end caps.
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

        // The tinted header: kind, name, state — and a colour that matches
        // the gizmo, so what you see in the scene is what you see here.
        public static void Header(string title, string subtitle, Color tint, string rightTag = null)
        {
            Ensure();
            var rect = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
            var bg = tint; bg.a = 0.85f;
            EditorGUI.DrawRect(rect, Color.Lerp(bg, new Color(0.15f, 0.15f, 0.15f, 1f), 0.55f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4, rect.height), tint);
            GUI.Label(new Rect(rect.x + 12, rect.y + 4, rect.width - 24, 20), title, _title);
            GUI.Label(new Rect(rect.x + 12, rect.y + 22, rect.width - 24, 16), subtitle, _sub);
            if (!string.IsNullOrEmpty(rightTag))
                GUI.Label(new Rect(rect.x, rect.y + 4, rect.width - 10, 16), rightTag, _tag);
            GUILayout.Space(6);
        }

        public static void Section(string title, Color tint, string blurb = null)
        {
            Ensure();
            GUILayout.Space(4);
            var rect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            var bg = tint; bg.a = EditorGUIUtility.isProSkin ? 0.14f : 0.20f;
            EditorGUI.DrawRect(rect, bg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3, rect.height), tint);
            GUI.Label(rect, title, _section);
            if (!string.IsNullOrEmpty(blurb)) GUILayout.Label(blurb, _blurb);
        }

        public static void Note(string text, MessageType type = MessageType.None)
        {
            Ensure();
            if (type == MessageType.None) GUILayout.Label(text, _blurb);
            else EditorGUILayout.HelpBox(text, type);
        }

        // Screen-constant size: how many world units one pixel covers at
        // this point, so a gizmo can be drawn N pixels big wherever it is.
        public static float PixelSize(Vector3 at) => HandleUtility.GetHandleSize(at) * 0.05f;
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
            bool built = socket.transform.Find("YAPS Lights") != null || socket.transform.Find("YAPS Pointers") != null;
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
            YapsInspectorStyle.Section("See it work", tint,
                "Preview bends every YAPS plug in the scene toward this socket, in the editor, so you " +
                "can place it and watch before uploading. Writes nothing that ships.");
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                bool p = GUILayout.Toggle(_preview.boolValue, _preview.boolValue ? "  Previewing — plugs bend toward this socket" : "  Preview", "Button", GUILayout.Height(24));
                if (EditorGUI.EndChangeCheck()) { _preview.boolValue = p; SceneView.RepaintAll(); }
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

        static SkinnedMeshRenderer GuessRenderer(Transform socket, List<SkinnedMeshRenderer> renderers)
        {
            for (var bone = socket.parent; bone != null; bone = bone.parent)
                foreach (var r in renderers)
                    if (r.bones != null && System.Array.IndexOf(r.bones, bone) >= 0) return r;
            return renderers.OrderByDescending(r => r.sharedMesh.blendShapeCount).FirstOrDefault();
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
            bool built = t.Find("YAPS Lights") != null || t.Find("YAPS Pointers") != null;
            bool hole = socket.kind == YapsSocket.SocketKind.Hole;
            var colour = !built ? YapsInspectorStyle.BadColour : hole ? YapsInspectorStyle.HoleColour : YapsInspectorStyle.RingColour;
            colour.a = selected ? 1f : 0.7f;

            Vector3 c = t.position, f = t.forward, u = t.up;
            // Screen-constant: about 26 px radius, whatever the distance.
            float px = YapsInspectorStyle.PixelSize(c);
            float r = px * 26f;
            float thick = selected ? 4f : 2.5f;

            Handles.color = colour;
            Handles.DrawWireDisc(c, f, r, thick);
            Handles.DrawWireDisc(c, f, r * 0.45f, thick * 0.6f);
            // Entry arrow: from where a plug comes, into the socket.
            Handles.DrawLine(c + f * (r * 2.4f), c + f * (r * 0.5f), thick);
            Handles.ConeHandleCap(0, c + f * (r * 0.5f), Quaternion.LookRotation(-f), r * 0.55f, EventType.Repaint);
            if (hole)
            {
                var faint = colour; faint.a *= 0.45f;
                Handles.color = faint;
                Vector3 back = c - f * (r * 1.6f);
                Handles.DrawWireDisc(back, f, r * 0.7f, thick * 0.6f);
                for (int i = 0; i < 6; i++)
                {
                    var q = Quaternion.AngleAxis(i * 60f, f);
                    Handles.DrawLine(c + q * u * r, back + q * u * (r * 0.7f), thick * 0.5f);
                }
            }
            // A filled centre dot so it survives at any zoom.
            Handles.color = colour;
            Handles.DrawSolidDisc(c, -SceneView.currentDrawingSceneView.camera.transform.forward, r * 0.12f);
            if (selected || !built)
            {
                var label = new GUIStyle(EditorStyles.whiteBoldLabel) { fontSize = 12 };
                Handles.Label(c + u * (r * 1.5f), (hole ? "hole" : "ring") + (built ? "" : "  — not built"), label);
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
            float px = YapsInspectorStyle.PixelSize(b);
            float thick = selected ? 4f : 2.5f;

            if (length <= 0f)
            {
                Handles.DrawDottedLine(b, b + f * 0.4f, 6f);
                var label = new GUIStyle(EditorStyles.whiteBoldLabel) { fontSize = 12 };
                Handles.Label(b + f * 0.4f, "plug — not baked", label);
                return;
            }
            float scale = Mathf.Max(0.05f, (frame.lossyScale.x + frame.lossyScale.y + frame.lossyScale.z) / 3f);
            Vector3 tip = b + f * length * scale;
            // True length along the axis — that IS the information — with
            // screen-sized caps so it reads at any distance.
            Handles.DrawLine(b, tip, thick);
            Handles.DrawSolidDisc(b, -SceneView.currentDrawingSceneView.camera.transform.forward, px * 6f);
            Handles.DrawWireDisc(b, f, px * 14f, thick);
            Handles.ConeHandleCap(0, tip, Quaternion.LookRotation(f), px * 18f, EventType.Repaint);
            if (selected)
            {
                var label = new GUIStyle(EditorStyles.whiteBoldLabel) { fontSize = 12 };
                Handles.Label(tip + f * (px * 12f), $"{length:0.###} m", label);
            }
        }
    }
}
#endif
