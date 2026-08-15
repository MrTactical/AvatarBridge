// Tools ▸ YAPS ▸ Setup. The YAPS toolkit's window: for any ChilloutVR
// avatar or prop, no VRChat history needed.
//
// The other product. AvatarBridge converts a VRChat avatar and carries its
// penetration across as YAPS on the way; this window is for an avatar
// that is already here — scan it, upgrade what it has, add what it does
// not, tune, verify. Same shader, same wire format, same principles: read
// what is really there, say what was done, fail loudly rather than
// silently. It shares AvatarBridge's look on purpose, and each points at
// the other, because they are two doors into one system.
//
// TODAY (2026-08-15) it scans and adds sockets. Upgrade, Make-this-a-plug,
// the overlay and the shader GUI are the next sessions' work, and the
// buttons for them say so rather than pretending.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge
{
    public class YapsSetupWindow : EditorWindow
    {
        [MenuItem("Tools/YAPS/Setup")]
        public static void Open()
        {
            var w = GetWindow<YapsSetupWindow>();
            w.titleContent = new GUIContent("YAPS");
            w.minSize = new Vector2(420, 480);
        }

        GameObject _target;
        YapsScanner.Result _scan;
        VisualElement _results;
        Label _summary;

        void CreateGUI()
        {
            var root = rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("AvatarBridge");
            if (sheet != null) root.styleSheets.Add(sheet);
            root.AddToClassList(EditorGUIUtility.isProSkin ? "dark" : "light");

            root.Add(BridgeElements.Banner("YAPS", "Yet Another Penetration System — for ChilloutVR",
                BridgeDefines.Version));

            var body = new ScrollView();
            body.AddToClassList("ab-body");
            root.Add(body);

            body.Add(BridgeElements.SubHeading("What to work on"));
            var picker = new UnityEditor.UIElements.ObjectField("Avatar or prop")
            {
                objectType = typeof(GameObject), allowSceneObjects = true,
            };
            picker.RegisterValueChangedCallback(e => { _target = e.newValue as GameObject; Rescan(); });
            body.Add(picker);
            body.Add(BridgeElements.Hint("Any object in the scene. Scanning touches nothing — it " +
                "only says what is there: DPS, TPS, SPS, or already YAPS."));

            var buttons = BridgeElements.Row(
                Button("Scan", Rescan),
                Button("Add a hole", () => AddSocket(YapsSocket.SocketKind.Hole)),
                Button("Add a ring", () => AddSocket(YapsSocket.SocketKind.Ring)),
                Button("Create prefabs", YapsSocketBuilder.CreatePrefabs));
            body.Add(buttons);

            _summary = new Label("Pick an object and scan.");
            _summary.AddToClassList("ab-hint");
            body.Add(_summary);

            body.Add(BridgeElements.SubHeading("Found"));
            _results = new VisualElement();
            body.Add(_results);

            body.Add(BridgeElements.SubHeading("Coming"));
            body.Add(BridgeElements.Hint(
                "Upgrade in place — read what a DPS/TPS/SPS setup's author tuned and carry it onto " +
                "YAPS on the same mesh. Make this a plug — measure a mesh's shaft and bake it. A " +
                "scene overlay that shows a plug bending toward a socket before you upload. Shapes " +
                "that open as a plug goes in, picked from your mesh. A grouped shader panel instead " +
                "of forty sliders."));

            body.Add(BridgeElements.SubHeading("Converting from VRChat?"));
            body.Add(BridgeElements.Hint(
                "AvatarBridge does that, and carries a VRChat avatar's DPS, TPS or SPS across as " +
                "YAPS automatically — same shader, same wire format as this. Tools ▸ Avatar Bridge."));

            if (Selection.activeGameObject != null)
            {
                picker.value = Selection.activeGameObject;
            }
        }

        static Button Button(string text, System.Action act)
        {
            var b = new Button(act) { text = text };
            b.AddToClassList("ab-button");
            return b;
        }

        void Rescan()
        {
            _results.Clear();
            if (_target == null)
            {
                _summary.text = "Pick an object and scan.";
                return;
            }
            _scan = YapsScanner.Scan(_target);
            _summary.text = _scan.Summary();

            bool alt = false;
            foreach (var f in _scan.Plugs.Concat(_scan.Sockets))
            {
                var colour = f.Origin == YapsLegacyMap.Origin.YAPS ? new Color(0.30f, 0.75f, 0.45f)
                           : f.Notes.Count > 0 ? new Color(0.90f, 0.65f, 0.25f)
                           : new Color(0.45f, 0.65f, 0.95f);
                string subject = (f.Kind == YapsScanner.Kind.Plug ? "Plug" : (f.IsHole ? "Hole" : "Ring"))
                                 + (f.Root != null ? "  ·  " + f.Root.name : "");
                var detail = new List<string>();
                if (f.StatedLength > 0) detail.Add($"{f.StatedLength:0.###} m");
                if (f.Lights.Count > 0) detail.Add($"{f.Lights.Count} light(s)");
                if (f.Pointers.Count > 0) detail.Add(string.Join(", ", f.Pointers.Select(p => p.type).Distinct()));
                if (f.Material != null) detail.Add(f.Material.name);
                detail.AddRange(f.Notes);
                var row = BridgeElements.ReportRow(f.Origin.ToString(), subject,
                    string.Join("  ·  ", detail), colour, alt);
                var captured = f;
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    if (captured.Root != null) { Selection.activeTransform = captured.Root; EditorGUIUtility.PingObject(captured.Root); }
                });
                _results.Add(row);
                alt = !alt;
            }
        }

        void AddSocket(YapsSocket.SocketKind kind)
        {
            var parent = Selection.activeGameObject != null ? Selection.activeGameObject : _target;
            var go = new GameObject(kind == YapsSocket.SocketKind.Hole ? "YAPS Hole" : "YAPS Ring");
            if (parent != null) go.transform.SetParent(parent.transform, false);
            var socket = go.AddComponent<YapsSocket>();
            socket.kind = kind;
            YapsSocketBuilder.Build(socket);
            Undo.RegisterCreatedObjectUndo(go, "Add YAPS socket");
            Selection.activeGameObject = go;
            Rescan();
        }
    }
}
#endif
