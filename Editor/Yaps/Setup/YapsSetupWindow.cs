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
        HelpBox _next;
        Label _selection;
        Button _addHole, _addRing, _makePlug;
        BridgeElements.PrimaryButton _build;
        ObjectField _picker;

        void OnDisable() => Selection.selectionChanged -= RefreshSelection;

        void CreateGUI()
        {
            var root = rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("AvatarBridge");
            if (sheet != null) root.styleSheets.Add(sheet);
            root.AddToClassList(EditorGUIUtility.isProSkin ? "dark" : "light");

            root.Add(BridgeElements.Banner("YAPS", "Yet Another Penetration System  ·  for ChilloutVR",
                BridgeDefines.Version));

            _tabs = new VisualElement();
            root.Add(_tabs);

            _pages = new ScrollView();
            _pages.AddToClassList("ab-body");
            root.Add(_pages);
            ShowPage();
        }

        VisualElement _tabs;

        void ShowPage()
        {
            // The strip is rebuilt with the mode, so the active tab's tint
            // follows the switch — built once, it highlighted whichever tab
            // was current when the window opened, forever.
            _tabs.Clear();
            _tabs.Add(BridgeElements.Tabs(
                new[] { "Set up an avatar or prop", "Test props" },
                new[] { "d_Avatar Icon", "d_PlayButton" },
                (int) _mode, i => { _mode = (Mode) i; ShowPage(); }));
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

            // WHAT TO DO NEXT. The one line a new user actually needs, and
            // it reads the scan and the Hierarchy selection to say it — so
            // the window teaches itself rather than needing a manual.
            _next = new HelpBox("", HelpBoxMessageType.Info);
            have.Body.Add(_next);

            have.Body.Add(BridgeElements.SubHeading("Add"));
            _addHole = Btn("Add a hole", () => AddSocket(YapsSocket.SocketKind.Hole));
            _addRing = Btn("Add a ring", () => AddSocket(YapsSocket.SocketKind.Ring));
            _makePlug = Btn("Make selected mesh a plug", MakePlug);
            have.Body.Add(BridgeElements.Row(_addHole, _addRing, _makePlug));
            _selection = BridgeElements.Hint("");
            have.Body.Add(_selection);
            have.Body.Add(BridgeElements.Hint(
                "A hole closes around a plug; a ring lets it through. Every socket added here is " +
                "readable by DPS, TPS and SPS plugs as well as YAPS ones. Making a mesh a plug " +
                "measures its shaft, bakes it, and patches its own shader."));
            _pages.Add(have);
            Selection.selectionChanged -= RefreshSelection;
            Selection.selectionChanged += RefreshSelection;

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

        // The one line that says what to do next, from what the scan found.
        // Four situations a user is actually in, and a sentence for each.
        void SayNext()
        {
            if (_next == null) return;
            if (_target == null)
            {
                _next.text = "Drag your avatar or prop into the box above. Nothing happens until you do.";
                _next.messageType = HelpBoxMessageType.Info;
                return;
            }
            int plugs = _scan.Plugs.Count, sockets = _scan.Sockets.Count;
            bool allYaps = _scan.Plugs.All(p => p.IsYapsAlready) && _scan.Sockets.All(s => s.IsYapsAlready);
            bool anyLegacy = _scan.Plugs.Any(p => !p.IsYapsAlready) || _scan.Sockets.Any(s => !s.IsYapsAlready);
            bool anyIssue = _scan.Plugs.Any(p => p.Notes.Count > 0) || _scan.Sockets.Any(s => s.Notes.Count > 0);

            if (plugs + sockets == 0)
            {
                _next.text = "Nothing on it yet. To add a socket: click the bone it should follow in the " +
                             "Hierarchy (Hips, say), then Add a hole or Add a ring. To make a plug: click " +
                             "the mesh that should bend, then Make selected mesh a plug. Then Build.";
                _next.messageType = HelpBoxMessageType.Info;
            }
            else if (anyLegacy)
            {
                _next.text = "This has DPS, TPS or SPS on it that is not YAPS yet. Upgrade-in-place is " +
                             "coming — for now, if it came from VRChat, AvatarBridge converts it (Tools ▸ " +
                             "Avatar Bridge) and the result arrives here already YAPS.";
                _next.messageType = HelpBoxMessageType.Warning;
            }
            else if (anyIssue)
            {
                _next.text = "Everything here is YAPS, but something is missing — the amber rows say what. " +
                             "Build fixes what it can (markers, bakes); a socket with no axis wants turning " +
                             "so its arrow points where a plug enters.";
                _next.messageType = HelpBoxMessageType.Warning;
            }
            else if (allYaps)
            {
                _next.text = "All YAPS and complete. Nothing to do here unless you want to add more or " +
                             "retune — click a row to select it and use its Inspector. Upload as normal.";
                _next.messageType = HelpBoxMessageType.Info;
            }
        }

        // The buttons say what they will act on, since that is the Hierarchy
        // selection and the window cannot otherwise show it.
        void RefreshSelection()
        {
            if (_selection == null) return;
            var go = Selection.activeGameObject;
            if (go == null)
            {
                _selection.text = "Nothing selected in the Hierarchy. Sockets will go in a YAPS folder on the avatar; a plug needs a mesh selected.";
                if (_addHole != null) _addHole.text = "Add a hole";
                if (_addRing != null) _addRing.text = "Add a ring";
                if (_makePlug != null) { _makePlug.text = "Make selected mesh a plug"; _makePlug.SetEnabled(false); }
                return;
            }
            var root = YapsSocketEditor.AvatarRootOf(go.transform);
            bool bone = root != null && go.transform != root && IsBone(go.transform, root);
            bool mesh = go.GetComponent<Renderer>() != null;
            _selection.text = bone
                ? $"Selected: bone \"{go.name}\" — a socket added now goes under it and follows it."
                : mesh ? $"Selected: mesh \"{go.name}\" — Make selected mesh a plug will bake this one."
                : $"Selected: \"{go.name}\" — not a bone, so a socket goes in the YAPS folder; not a mesh, so no plug.";
            if (_addHole != null) _addHole.text = bone ? $"Add a hole under {go.name}" : "Add a hole";
            if (_addRing != null) _addRing.text = bone ? $"Add a ring under {go.name}" : "Add a ring";
            if (_makePlug != null) { _makePlug.text = mesh ? $"Make \"{go.name}\" a plug" : "Make selected mesh a plug"; _makePlug.SetEnabled(mesh); }
        }

        void Rescan()
        {
            if (_foundBody == null) return;
            _foundBody.Clear();
            RefreshSelection();
            if (_target == null)
            {
                _summary.text = "Pick something above.";
                _build?.SetActive(false);
                SayNext();
                return;
            }
            _scan = YapsScanner.Scan(_target);
            _summary.text = _scan.Summary();
            _build?.SetActive(_scan.Total > 0);
            _build?.SetLabel(_scan.Total > 0
                ? $"Bake {_scan.Plugs.Count} plug{(_scan.Plugs.Count == 1 ? "" : "s")} and verify {_scan.Sockets.Count} socket{(_scan.Sockets.Count == 1 ? "" : "s")}"
                : "Nothing to build yet");
            SayNext();

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

        // Where a new socket goes follows the convention avatar authors
        // already use — Angela's author did both: sockets that must follow a
        // bone sit UNDER that bone (Armature/…/Hips/[VF] Pussy), and the
        // rest are organised in a folder (SPS/Handjob/Double, SPS/Feet/…).
        // So: if a BONE is selected, the socket goes under it and follows
        // it. Otherwise it goes in a "YAPS" folder at the avatar root, named
        // for the kind, where the user can move it. Never loose at the root
        // among the meshes and cloth roots — that is where the first version
        // dropped it and it looked like a mistake.
        void AddSocket(YapsSocket.SocketKind kind, bool atCamera = false)
        {
            string name = kind == YapsSocket.SocketKind.Hole ? "YAPS Hole" : "YAPS Ring";
            var go = new GameObject(name);

            if (atCamera)
            {
                var cam = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
                if (cam != null)
                {
                    go.transform.position = cam.transform.position + cam.transform.forward * 0.6f;
                    go.transform.rotation = Quaternion.LookRotation(-cam.transform.forward);
                }
            }
            else
            {
                var selected = Selection.activeGameObject != null ? Selection.activeGameObject.transform : null;
                var avatarRoot = _target != null ? _target.transform
                    : selected != null ? YapsSocketEditor.AvatarRootOf(selected) : null;
                bool onBone = selected != null && avatarRoot != null && selected != avatarRoot
                              && IsBone(selected, avatarRoot);
                Transform parent;
                if (onBone)
                {
                    parent = selected;
                }
                else if (avatarRoot != null)
                {
                    parent = avatarRoot.Find("YAPS");
                    if (parent == null)
                    {
                        var folder = new GameObject("YAPS");
                        folder.transform.SetParent(avatarRoot, false);
                        Undo.RegisterCreatedObjectUndo(folder, "YAPS folder");
                        parent = folder.transform;
                    }
                }
                else
                {
                    parent = selected;
                }
                if (parent != null) go.transform.SetParent(parent, false);
                if (!onBone && parent != null)
                {
                    // In the folder: unique names, so three holes are not
                    // three "YAPS Hole"s.
                    int n = 1;
                    foreach (Transform c in parent) if (c.name.StartsWith(name)) n++;
                    if (n > 1) go.name = $"{name} {n}";
                }
            }

            var socket = go.AddComponent<YapsSocket>();
            socket.kind = kind;
            YapsSocketBuilder.Build(socket);
            Undo.RegisterCreatedObjectUndo(go, "Add YAPS socket");
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            if (!atCamera) Rescan();
        }

        // A bone: any transform a skinned mesh under the avatar is bound
        // to, or anything under a transform named Armature.
        static bool IsBone(Transform t, Transform avatarRoot)
        {
            foreach (var smr in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.bones != null && System.Array.IndexOf(smr.bones, t) >= 0) return true;
            }
            for (var at = t; at != null && at != avatarRoot; at = at.parent)
            {
                if (at.name == "Armature") return true;
            }
            return false;
        }
    }
}
#endif
