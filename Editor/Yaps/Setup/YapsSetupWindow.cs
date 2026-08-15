// Tools ▸ YAPS ▸ Setup. The YAPS toolkit's window, for any ChilloutVR
// avatar or prop, no VRChat history needed.
//
// It is built on AvatarBridge's own skeleton — the same banner, the same
// mode tabs, the same numbered cards, the same gradient action button —
// because a new user should not have to learn two layouts, and because
// the two are one family: AvatarBridge converts a VRChat avatar and
// carries its penetration across as YAPS; this is for an avatar already
// here. Each points at the other.
//
// Three steps, like the converter's three:
//   1  Pick the avatar or prop.
//   2  What it has — the scan, one row per plug or socket, with what
//      reads it and what it lacks; and what to add — hole, ring, plug.
//   3  Build — bake every plug, verify.
//
// TODAY (2026-08-15): scan, add sockets, make/bake plugs on static meshes,
// a test plug, preview. Upgrade-in-place, the avatar channel and skinned
// bone chains are marked coming rather than pretended.
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
    public class YapsSetupWindow : EditorWindow
    {
        [MenuItem("Tools/YAPS/Setup")]
        public static void Open()
        {
            var w = GetWindow<YapsSetupWindow>();
            w.titleContent = new GUIContent("YAPS");
            w.minSize = new Vector2(440, 520);
        }

        enum Mode { Setup, Test }

        Mode _mode = Mode.Setup;
        GameObject _target;
        YapsScanner.Result _scan;

        VisualElement _pages;
        VisualElement _foundBody;
        Label _summary;
        BridgeElements.PrimaryButton _build;
        ObjectField _picker;

        void CreateGUI()
        {
            var root = rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("AvatarBridge");
            if (sheet != null) root.styleSheets.Add(sheet);
            root.AddToClassList(EditorGUIUtility.isProSkin ? "dark" : "light");

            root.Add(BridgeElements.Banner("YAPS", "Yet Another Penetration System  ·  for ChilloutVR",
                BridgeDefines.Version));

            var tabs = new VisualElement();
            tabs.Add(BridgeElements.Tabs(
                new[] { "Set up an avatar or prop", "Test props" },
                new[] { "d_Avatar Icon", "d_PlayButton" },
                (int) _mode, i => { _mode = (Mode) i; ShowPage(); }));
            root.Add(tabs);

            _pages = new ScrollView();
            _pages.AddToClassList("ab-body");
            root.Add(_pages);
            ShowPage();
        }

        void ShowPage()
        {
            _pages.Clear();
            if (_mode == Mode.Setup) BuildSetupPage(); else BuildTestPage();
        }

        // --- the setup page --------------------------------------------------

        void BuildSetupPage()
        {
            // 1 — Pick.
            var pick = new BridgeElements.Card("Pick your avatar or prop", null, null, 1, 0f);
            _picker = new ObjectField("Avatar or prop") { objectType = typeof(GameObject), allowSceneObjects = true, value = _target };
            _picker.RegisterValueChangedCallback(e => { _target = e.newValue as GameObject; Rescan(); });
            pick.Body.Add(_picker);
            pick.Body.Add(BridgeElements.Hint(
                "Any object in the scene. Scanning touches nothing — it only says what is there: " +
                "DPS, TPS, SPS, or already YAPS."));
            _pages.Add(pick);

            // 2 — What it has, and what to add.
            var have = new BridgeElements.Card("What it has, and what to add", null, null, 2, 0.5f);
            _summary = new Label("Pick something above.");
            _summary.AddToClassList("ab-hint");
            have.Body.Add(_summary);
            _foundBody = new VisualElement();
            have.Body.Add(_foundBody);

            have.Body.Add(BridgeElements.SubHeading("Add"));
            have.Body.Add(BridgeElements.Row(
                Btn("Add a hole", () => AddSocket(YapsSocket.SocketKind.Hole)),
                Btn("Add a ring", () => AddSocket(YapsSocket.SocketKind.Ring)),
                Btn("Make selected mesh a plug", MakePlug)));
            have.Body.Add(BridgeElements.Hint(
                "A hole closes around a plug; a ring lets it through. Both land under the object " +
                "selected in the Hierarchy — pick the bone they should follow first. Point their +Z the " +
                "way a plug enters. Every socket added here is readable by DPS, TPS and SPS plugs as " +
                "well as YAPS ones. Making a mesh a plug measures its shaft, bakes it, and patches its " +
                "own shader."));
            _pages.Add(have);

            // 3 — Build.
            var build = new BridgeElements.Card("Build", null, null, 3, 1f);
            _build = new BridgeElements.PrimaryButton("Bake every plug and verify", BuildAll);
            build.Body.Add(_build);
            build.Body.Add(BridgeElements.Hint(
                "Bakes each YAPS Plug under the picked object — measuring the mesh, patching its own " +
                "shader, writing the knobs, announcing it to every socket family — and rebuilds each " +
                "YAPS Socket's markers. Then checks the lot. Safe to run again; it edits, not stacks."));
            _pages.Add(build);

            var coming = new BridgeElements.Card("Coming", null, false, null, 0.5f);
            coming.Body.Add(BridgeElements.Hint(
                "Upgrade in place — read what a DPS/TPS/SPS setup's author tuned and carry it onto YAPS " +
                "on the same mesh. Skinned-mesh bone chains for plugs. The contact channel on an " +
                "avatar's own controller, so contact-only sockets move it too."));
            _pages.Add(coming);

            var cross = new BridgeElements.Card("Converting from VRChat?", null, false, null, 0f);
            cross.Body.Add(BridgeElements.Hint(
                "AvatarBridge does that, and carries a VRChat avatar's DPS, TPS or SPS across as YAPS " +
                "automatically — same shader, same wire format as this. Tools ▸ Avatar Bridge."));
            _pages.Add(cross);

            if (_target == null && Selection.activeGameObject != null) _picker.value = Selection.activeGameObject;
            else Rescan();
        }

        // --- the test page -----------------------------------------------------

        void BuildTestPage()
        {
            var what = new BridgeElements.Card("Test props", null, null, 1, 0f);
            what.Body.Add(BridgeElements.Hint(
                "Ready-made things to try YAPS with, here in the editor and in game. A test plug is a " +
                "capsule baked through the exact path your own mesh takes, so building it proves the " +
                "path; a test socket is the universal prefab. Drop them in, tick Preview on the socket, " +
                "and the plug bends toward it in the scene view."));
            _pages.Add(what);

            var make = new BridgeElements.Card("Make", null, null, 2, 0.5f);
            make.Body.Add(BridgeElements.Row(
                Btn("Test plug", () => YapsNativeBuilder.BuildTestPlug()),
                Btn("Test hole", () => AddSocket(YapsSocket.SocketKind.Hole, atCamera: true)),
                Btn("Test ring", () => AddSocket(YapsSocket.SocketKind.Ring, atCamera: true))));
            make.Body.Add(BridgeElements.Hint(
                "Each lands in front of the scene camera. The plug wears Standard, patched. Select the " +
                "hole or ring and tick Preview, then move it around the plug."));
            make.Body.Add(BridgeElements.SubHeading("Prefabs"));
            make.Body.Add(BridgeElements.Row(Btn("Create universal socket prefabs", YapsSocketBuilder.CreatePrefabs)));
            make.Body.Add(BridgeElements.Hint(
                "Writes YAPS Hole and YAPS Ring to Assets/YAPS/Prefabs — drag one under a bone on any " +
                "avatar and it works for every plug on the platform."));
            _pages.Add(make);

            var game = new BridgeElements.Card("In game", null, false, null, 1f);
            game.Body.Add(BridgeElements.Hint(
                "To try a test plug and socket in ChilloutVR, put each on its own CVR Spawnable and " +
                "upload them as props. The prop needs a collider to be grabbable, and Disallow Theft " +
                "on the plug so a socket switching on cannot pull it out of someone's hand. Full " +
                "prop-building — the channel, pickup rules, sync — is the toolkit's next piece; " +
                "until then the spike's Build YAPS test props does it, under Tools ▸ Avatar Bridge ▸ Spike."));
            _pages.Add(game);
        }

        // --- behaviour -----------------------------------------------------------

        static Button Btn(string text, System.Action act)
        {
            var b = new Button(act) { text = text };
            b.AddToClassList("ab-button");
            return b;
        }

        void Rescan()
        {
            if (_foundBody == null) return;
            _foundBody.Clear();
            if (_target == null)
            {
                _summary.text = "Pick something above.";
                _build?.SetActive(false);
                return;
            }
            _scan = YapsScanner.Scan(_target);
            _summary.text = _scan.Summary();
            _build?.SetActive(_scan.Total > 0);
            _build?.SetLabel(_scan.Total > 0
                ? $"Bake {_scan.Plugs.Count} plug{(_scan.Plugs.Count == 1 ? "" : "s")} and verify {_scan.Sockets.Count} socket{(_scan.Sockets.Count == 1 ? "" : "s")}"
                : "Nothing to build yet");

            bool alt = false;
            void Row(YapsScanner.Found f)
            {
                bool complete = f.Notes.Count == 0;
                var colour = f.IsYapsAlready && complete ? BridgeTheme.Good
                           : !complete ? BridgeTheme.Warn
                           : new Color(0.45f, 0.65f, 0.95f);
                string what = f.Kind == YapsScanner.Kind.Plug ? "Plug" : (f.IsHole ? "Hole" : "Ring");
                var detail = new List<string>();
                if (f.Kind == YapsScanner.Kind.Plug && f.StatedLength > 0) detail.Add($"{f.StatedLength:0.###} m");
                detail.Add((f.Kind == YapsScanner.Kind.Plug ? "seen by " : "readable by ") + f.ReadableList());
                if (f.Kind == YapsScanner.Kind.Socket && f.HasAxis) detail.Add("has an axis");
                if (f.Renderer != null && f.Kind == YapsScanner.Kind.Socket) detail.Add("shapes on " + f.Renderer.name);
                detail.AddRange(f.Notes);

                var row = BridgeElements.ReportRow(what, f.Name, string.Join("  ·  ", detail), colour, alt);
                var captured = f;
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    if (captured.Root != null) { Selection.activeTransform = captured.Root; EditorGUIUtility.PingObject(captured.Root); }
                });

                var comp = f.Root != null ? f.Root.GetComponent<YapsSocket>() : null;
                if (comp != null)
                {
                    var wrap = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                    row.style.flexGrow = 1;
                    var chip = BridgeElements.Chip(comp.preview ? "previewing" : "preview",
                        BridgeTheme.Good, comp.preview, () =>
                        {
                            Undo.RecordObject(comp, "YAPS preview");
                            comp.preview = !comp.preview;
                            EditorUtility.SetDirty(comp);
                            SceneView.RepaintAll();
                            Rescan();
                        }, comp.preview);
                    chip.style.marginLeft = 6; chip.style.marginRight = 8;
                    wrap.Add(row); wrap.Add(chip);
                    _foundBody.Add(wrap);
                }
                else _foundBody.Add(row);
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
                _summary.text = "Select the mesh that should bend in the Hierarchy — an object with a Mesh Renderer or Skinned Mesh Renderer — then press this.";
                return;
            }
            var plug = go.GetComponent<YapsPlug>();
            if (plug == null)
            {
                plug = Undo.AddComponent<YapsPlug>(go);
                plug.renderer = renderer;
            }
            var o = YapsNativeBuilder.Bake(plug);
            if (!o.Ok) Debug.LogError("[YAPS] " + o.Message);
            if (_target == null) _picker.value = YapsSocketEditor.AvatarRootOf(go.transform).gameObject;
            Rescan();
            _summary.text = o.Message + (o.Notes.Count > 0 ? "  " + string.Join(" ", o.Notes) : "");
        }

        void BuildAll()
        {
            if (_target == null) return;
            int plugsOk = 0, plugsTried = 0, socketsBuilt = 0;
            var lines = new List<string>();
            foreach (var p in _target.GetComponentsInChildren<YapsPlug>(true))
            {
                plugsTried++;
                var o = YapsNativeBuilder.Bake(p);
                if (o.Ok) plugsOk++;
                lines.Add((o.Ok ? "✓ " : "✗ ") + o.Message);
            }
            foreach (var s in _target.GetComponentsInChildren<YapsSocket>(true))
            {
                YapsSocketBuilder.Build(s);
                socketsBuilt++;
            }
            Rescan();
            _summary.text = $"Built: {plugsOk} of {plugsTried} plug{(plugsTried == 1 ? "" : "s")}, {socketsBuilt} socket{(socketsBuilt == 1 ? "" : "s")}.  " + string.Join("  ", lines);
        }

        void AddSocket(YapsSocket.SocketKind kind, bool atCamera = false)
        {
            var parent = atCamera ? null : (Selection.activeGameObject != null ? Selection.activeGameObject : _target);
            var go = new GameObject(kind == YapsSocket.SocketKind.Hole ? "YAPS Hole" : "YAPS Ring");
            if (parent != null) go.transform.SetParent(parent.transform, false);
            else
            {
                var cam = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
                if (cam != null)
                {
                    go.transform.position = cam.transform.position + cam.transform.forward * 0.6f;
                    go.transform.rotation = Quaternion.LookRotation(-cam.transform.forward);
                }
            }
            var socket = go.AddComponent<YapsSocket>();
            socket.kind = kind;
            YapsSocketBuilder.Build(socket);
            Undo.RegisterCreatedObjectUndo(go, "Add YAPS socket");
            Selection.activeGameObject = go;
            if (!atCamera) Rescan();
        }
    }
}
#endif
