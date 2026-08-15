// The inspectors for YapsSocket and YapsPlug, and the scene gizmos that
// go with them. This is where "drag a prefab in and it just works" gets
// its second half: the moment a socket lands under a bone you can SEE it
// — where it faces, how big it is, whether it is a hole or a ring — and
// the shape rows offer the avatar's own blendshapes from a dropdown
// instead of a text field to type a name into.
//
// Everything drawn here reads the avatar. The renderer list is every
// skinned mesh under the avatar root, defaulted to the one whose bones
// include the socket's parent (the body a hip socket sits on); the shape
// list is that mesh's actual blendshapes. Nothing is typed.
//
// The socket gizmo is a RING the size of a plug's typical radius (a real
// hole is not a point), an arrow along +Z showing which way a plug enters
// (the front pointer and the normal light both sit that way), and a
// second faint ring for a hole's depth. Cyan for a hole, orange for a
// ring, red when something is missing — no axis, not built.
//
// The plug gizmo is the measured axis: base marker, tip marker, a line
// between, and rings at both ends at the measured radius. Only once the
// plug has been baked, since the measurement is the bake's; before that a
// dashed line along the object's +Z says "unmeasured".
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    [CustomEditor(typeof(YapsSocket))]
    public class YapsSocketEditor : Editor
    {
        static readonly Color HoleColour = new Color(0.25f, 0.85f, 0.95f);
        static readonly Color RingColour = new Color(0.95f, 0.55f, 0.20f);
        static readonly Color BadColour = new Color(0.95f, 0.25f, 0.25f);

        // How big to DRAW. A socket has no radius of its own — the plug
        // decides — so the gizmo shows a typical one, and the user reads it
        // as "a plug about this size arrives here".
        const float DrawRadius = 0.028f;

        SerializedProperty _kind, _tag, _renderer, _shapes, _power, _lights;

        void OnEnable()
        {
            _kind = serializedObject.FindProperty("kind");
            _tag = serializedObject.FindProperty("tag");
            _renderer = serializedObject.FindProperty("renderer");
            _shapes = serializedObject.FindProperty("shapes");
            _power = serializedObject.FindProperty("shapePower");
            _lights = serializedObject.FindProperty("emitLights");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var socket = (YapsSocket) target;
            var avatarRoot = AvatarRootOf(socket.transform);

            EditorGUILayout.PropertyField(_kind);
            EditorGUILayout.PropertyField(_tag, new GUIContent("Tag (optional)"));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Shapes that open as a plug goes in", EditorStyles.boldLabel);

            // Renderer: a dropdown of the avatar's skinned meshes, defaulted
            // to the one this socket's bone belongs to.
            var renderers = avatarRoot != null
                ? avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0).ToList()
                : new List<SkinnedMeshRenderer>();
            var current = _renderer.objectReferenceValue as SkinnedMeshRenderer;
            if (current == null && renderers.Count > 0)
            {
                current = GuessRenderer(socket.transform, renderers);
                if (current != null) _renderer.objectReferenceValue = current;
            }
            int rIndex = current != null ? renderers.IndexOf(current) : -1;
            var rNames = new[] { "None — bend plugs only, play no shape" }
                .Concat(renderers.Select(r => $"{r.name}  ({r.sharedMesh.blendShapeCount} shapes)")).ToArray();
            int picked = EditorGUILayout.Popup("Mesh", rIndex + 1, rNames) - 1;
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

                EditorGUILayout.HelpBox("Up to four, staged by depth. Shape 0 opens first as the plug " +
                    "arrives; each later one starts deeper. Depths are fractions of the plug's length. " +
                    "They stack: once a shape has opened it stays open as the plug goes deeper.", MessageType.None);

                for (int i = 0; i < _shapes.arraySize; i++)
                {
                    var row = _shapes.GetArrayElementAtIndex(i);
                    var name = row.FindPropertyRelative("blendshape");
                    var start = row.FindPropertyRelative("startsAt");
                    var fade = row.FindPropertyRelative("fadeOver");

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label(i == 0 ? "Entry" : $"Depth {i}", EditorStyles.miniBoldLabel, GUILayout.Width(50));
                            int sIndex = System.Array.IndexOf(shapeNames, name.stringValue);
                            int sPicked = EditorGUILayout.Popup(sIndex + 1, options) - 1;
                            if (sPicked != sIndex) name.stringValue = sPicked >= 0 ? shapeNames[sPicked] : "";
                            if (GUILayout.Button("×", GUILayout.Width(22)))
                            {
                                _shapes.DeleteArrayElementAtIndex(i);
                                break;
                            }
                        }
                        EditorGUILayout.Slider(start, 0f, 1f, "Starts opening at");
                        EditorGUILayout.Slider(fade, 0.01f, 1f, "Fully open by (+)");
                        if (!string.IsNullOrEmpty(name.stringValue) && System.Array.IndexOf(shapeNames, name.stringValue) < 0)
                        {
                            EditorGUILayout.HelpBox($"\"{name.stringValue}\" is not on this mesh.", MessageType.Warning);
                        }
                    }
                }
                if (_shapes.arraySize < 4 && GUILayout.Button("+ Add a shape"))
                {
                    _shapes.arraySize++;
                    var row = _shapes.GetArrayElementAtIndex(_shapes.arraySize - 1);
                    row.FindPropertyRelative("blendshape").stringValue = "";
                    row.FindPropertyRelative("startsAt").floatValue = 0.25f * (_shapes.arraySize - 1);
                    row.FindPropertyRelative("fadeOver").floatValue = 0.3f;
                }
                EditorGUILayout.Slider(_power, 0f, 1f, "Shape strength");
            }
            else if (renderers.Count == 0)
            {
                EditorGUILayout.HelpBox(avatarRoot == null
                    ? "Put this socket under an avatar or prop and its meshes will appear here."
                    : "No skinned mesh with blendshapes under this avatar. The socket will bend plugs and play no shape.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Advanced", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_lights, new GUIContent("Emit marker lights",
                "What lets DPS plugs and plugs with no sync budget find this socket. Costs no sync. " +
                "An avatar with many sockets should wire these to menu toggles instead — the toolkit does that."));

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild markers"))
                {
                    Undo.RegisterFullObjectHierarchyUndo(socket.gameObject, "Rebuild YAPS socket");
                    YapsSocketBuilder.Build(socket);
                }
                if (GUILayout.Button("Open YAPS Setup"))
                {
                    YapsSetupWindow.Open();
                }
            }
            bool built = socket.transform.Find("YAPS Lights") != null || socket.transform.Find("YAPS Pointers") != null;
            if (!built)
            {
                EditorGUILayout.HelpBox("Not built yet — no lights or pointers beneath it, so no plug can find it. Rebuild markers.", MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }

        // The skinned mesh whose bones include this socket's parent — the
        // body a hip socket sits on. Falls back to the mesh with the most
        // shapes, which on an avatar is the body too.
        static SkinnedMeshRenderer GuessRenderer(Transform socket, List<SkinnedMeshRenderer> renderers)
        {
            for (var bone = socket.parent; bone != null; bone = bone.parent)
            {
                foreach (var r in renderers)
                {
                    if (r.bones != null && System.Array.IndexOf(r.bones, bone) >= 0) return r;
                }
            }
            return renderers.OrderByDescending(r => r.sharedMesh.blendShapeCount).FirstOrDefault();
        }

        internal static Transform AvatarRootOf(Transform t)
        {
            // The nearest ancestor with an Animator, or the topmost object.
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
            var colour = !built ? BadColour : hole ? HoleColour : RingColour;
            colour.a = selected ? 1f : 0.55f;

            float r = DrawRadius * HandleScale(t);
            Vector3 c = t.position, f = t.forward, u = t.up;

            Handles.color = colour;
            // The opening.
            Handles.DrawWireDisc(c, f, r);
            Handles.DrawWireDisc(c, f, r * 0.55f);
            // Which way a plug enters: an arrow along +Z, pointing INTO the
            // socket from where the plug would come. The front marker sits
            // 1 cm along forward; the plug arrives from behind that.
            Handles.DrawLine(c + f * (r * 2.2f), c);
            Handles.ConeHandleCap(0, c + f * (r * 0.4f), Quaternion.LookRotation(-f), r * 0.6f, EventType.Repaint);
            if (hole)
            {
                // A hole has depth: a fainter ring set back into it, and
                // lines joining, so it reads as a short tube rather than a
                // disc.
                var faint = colour; faint.a *= 0.4f;
                Handles.color = faint;
                Vector3 back = c - f * (r * 1.5f);
                Handles.DrawWireDisc(back, f, r * 0.7f);
                for (int i = 0; i < 4; i++)
                {
                    var q = Quaternion.AngleAxis(i * 90f, f);
                    Handles.DrawLine(c + q * u * r, back + q * u * (r * 0.7f));
                }
            }
            if (selected)
            {
                Handles.Label(c + u * (r * 1.4f), (hole ? "hole" : "ring") + (built ? "" : "  (not built)"),
                    EditorStyles.whiteMiniLabel);
            }
        }

        static float HandleScale(Transform t)
        {
            // Draw at a size that reads at the avatar's scale: a scaled-down
            // avatar's socket gizmo scales with it.
            var s = t.lossyScale;
            return Mathf.Max(0.05f, (s.x + s.y + s.z) / 3f);
        }
    }

    [CustomEditor(typeof(YapsPlug))]
    public class YapsPlugEditor : Editor
    {
        static readonly Color PlugColour = new Color(0.55f, 0.85f, 0.35f);

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var plug = (YapsPlug) target;
            var renderer = plug.Target;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (renderer == null)
                {
                    EditorGUILayout.HelpBox("No renderer. Put this on the mesh that bends, or pick one below.", MessageType.Warning);
                }
                else
                {
                    var mats = renderer.sharedMaterials;
                    int yapsSlot = -1;
                    for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].HasProperty("_YAPS_Bake")) { yapsSlot = i; break; }
                    if (yapsSlot >= 0)
                    {
                        float len = mats[yapsSlot].GetFloat("_YAPS_Length");
                        EditorGUILayout.LabelField("Baked", $"{len:0.###} m, material \"{mats[yapsSlot].name}\"");
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Not baked yet. Set it up below, then Bake in YAPS Setup.", MessageType.Info);
                    }
                }
            }

            DrawPropertiesExcluding(serializedObject, "m_Script");

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Open YAPS Setup")) YapsSetupWindow.Open();

            serializedObject.ApplyModifiedProperties();
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
        static void DrawPlug(YapsPlug plug, GizmoType type)
        {
            bool selected = (type & GizmoType.Selected) != 0;
            var renderer = plug.Target;
            var colour = PlugColour; colour.a = selected ? 1f : 0.5f;
            Handles.color = colour;

            // Baked: the measured axis lives on the material, in the plug's
            // frame — which for a static mesh IS the object's frame, and for
            // a skinned one is recovered per vertex; the object's forward is
            // the honest editor stand-in for both.
            float length = 0f, radius = 0.028f;
            if (renderer != null)
            {
                foreach (var m in renderer.sharedMaterials)
                {
                    if (m != null && m.HasProperty("_YAPS_Length")) { length = m.GetFloat("_YAPS_Length"); break; }
                }
            }
            var t = plug.transform;
            Vector3 b = t.position, f = t.forward;
            if (length <= 0f)
            {
                // Unmeasured: a dashed hint along +Z, half a metre.
                Handles.DrawDottedLine(b, b + f * 0.5f, 4f);
                if (selected) Handles.Label(b + f * 0.5f, "unmeasured — bake to measure", EditorStyles.whiteMiniLabel);
                return;
            }
            float scale = Mathf.Max(0.05f, (t.lossyScale.x + t.lossyScale.y + t.lossyScale.z) / 3f);
            Vector3 tip = b + f * length * scale;
            Handles.DrawLine(b, tip);
            Handles.DrawWireDisc(b, f, radius * scale);
            Handles.DrawWireDisc(tip, f, radius * 0.5f * scale);
            Handles.SphereHandleCap(0, b, Quaternion.identity, radius * 0.6f * scale, EventType.Repaint);
            if (selected) Handles.Label(tip, $"{length:0.###} m", EditorStyles.whiteMiniLabel);
        }
    }
}
#endif
