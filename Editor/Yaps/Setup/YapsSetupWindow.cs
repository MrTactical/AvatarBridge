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

            body.Add(BridgeElements.Row(
                Button("Scan", Rescan),
                Button("Add a hole", () => AddSocket(YapsSocket.SocketKind.Hole)),
                Button("Add a ring", () => AddSocket(YapsSocket.SocketKind.Ring)),
                Button("Create prefabs", YapsSocketBuilder.CreatePrefabs)));
            body.Add(BridgeElements.Row(
                Button("Make selected a plug", MakePlug),
                Button("Bake plugs", BakeAll),
                Button("Test plug", () => { YapsNativeBuilder.BuildTestPlug(); Rescan(); })));
            body.Add(BridgeElements.Hint(
                "Make selected a plug puts a YAPS Plug on the selected mesh and bakes it — its own " +
                "shader, patched. Test plug drops a ready-made one in front of the camera. Tick " +
                "Preview on any socket and every plug bends toward it, here, before you upload."));

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

            // One row per plug or socket, as an OBJECT: what it is, what
            // reads it, and what it lacks. Green when it is already YAPS
            // and complete, blue when it is another system's and whole,
            // amber when something is missing.
            bool alt = false;
            void Row(YapsScanner.Found f)
            {
                bool complete = f.Notes.Count == 0;
                var colour = f.IsYapsAlready && complete ? new Color(0.30f, 0.75f, 0.45f)
                           : !complete ? new Color(0.90f, 0.65f, 0.25f)
                           : new Color(0.45f, 0.65f, 0.95f);
                string what = f.Kind == YapsScanner.Kind.Plug ? "Plug" : (f.IsHole ? "Hole" : "Ring");
                string subject = $"{what}  ·  {f.Name}";
                var detail = new List<string>();
                if (f.Kind == YapsScanner.Kind.Plug && f.StatedLength > 0) detail.Add($"{f.StatedLength:0.###} m");
                detail.Add((f.Kind == YapsScanner.Kind.Plug ? "seen by " : "readable by ") + f.ReadableList());
                if (f.Kind == YapsScanner.Kind.Socket && f.HasAxis) detail.Add("has an axis");
                if (f.Lights.Count > 0 || f.Pointers.Count > 0)
                    detail.Add($"{f.Lights.Count} light{(f.Lights.Count == 1 ? "" : "s")}, {f.Pointers.Count} pointer{(f.Pointers.Count == 1 ? "" : "s")}");
                if (f.Renderer != null && f.Kind == YapsScanner.Kind.Socket) detail.Add("shapes on " + f.Renderer.name);
                detail.AddRange(f.Notes);
                string chip = f.IsYapsAlready ? "YAPS" : f.Origin.ToString();
                var row = BridgeElements.ReportRow(chip, subject, string.Join("  ·  ", detail), colour, alt);
                var captured = f;
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    if (captured.Root != null) { Selection.activeTransform = captured.Root; EditorGUIUtility.PingObject(captured.Root); }
                });

                // A socket authored here can PREVIEW: tick it and every plug
                // in the scene bends toward it, in the editor.
                var comp = f.Root != null ? f.Root.GetComponent<YapsSocket>() : null;
                if (comp != null)
                {
                    var wrap = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                    row.style.flexGrow = 1;
                    var toggle = new Toggle("Preview") { value = comp.preview };
                    toggle.style.marginLeft = 6; toggle.style.marginRight = 6;
                    toggle.RegisterValueChangedCallback(e =>
                    {
                        Undo.RecordObject(comp, "YAPS preview");
                        comp.preview = e.newValue;
                        EditorUtility.SetDirty(comp);
                        SceneView.RepaintAll();
                    });
                    wrap.Add(row);
                    wrap.Add(toggle);
                    _results.Add(wrap);
                }
                else
                {
                    _results.Add(row);
                }
                alt = !alt;
            }
            foreach (var f in _scan.Plugs) Row(f);
            foreach (var f in _scan.Sockets) Row(f);
        }

        void MakePlug()
        {
            var go = Selection.activeGameObject;
            var renderer = go != null ? go.GetComponent<Renderer>() : null;
            if (renderer == null)
            {
                _summary.text = "Select the mesh that should bend — an object with a MeshRenderer or SkinnedMeshRenderer.";
                return;
            }
            var plug = go.GetComponent<YapsPlug>();
            if (plug == null)
            {
                plug = Undo.AddComponent<YapsPlug>(go);
                plug.renderer = renderer;
            }
            var o = YapsNativeBuilder.Bake(plug);
            _summary.text = o.Message + (o.Notes.Count > 0 ? "  " + string.Join(" ", o.Notes) : "");
            if (!o.Ok) Debug.LogError("[YAPS] " + o.Message);
            Rescan();
        }

        void BakeAll()
        {
            if (_target == null) { _summary.text = "Pick an object first."; return; }
            var plugs = _target.GetComponentsInChildren<YapsPlug>(true);
            if (plugs.Length == 0) { _summary.text = "No YAPS Plug components under it. Make selected a plug first."; return; }
            int ok = 0; var lines = new List<string>();
            foreach (var p in plugs)
            {
                var o = YapsNativeBuilder.Bake(p);
                if (o.Ok) ok++;
                lines.Add((o.Ok ? "✓ " : "✗ ") + o.Message);
            }
            _summary.text = $"Baked {ok} of {plugs.Length}. " + string.Join("  ", lines);
            Rescan();
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
