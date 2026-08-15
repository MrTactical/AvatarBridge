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
        Button _addHole, _addRing, _makePlug, _quiet;
        BridgeElements.PrimaryButton _build;
        ObjectField _picker;

        void OnDisable()
        {
            Selection.selectionChanged -= RefreshSelection;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }

        // The found list describes objects, and objects come and go — a
        // reconvert destroys the avatar the list was built from and rebuilds
        // it. Rescan after any hierarchy change, once per edit.
        bool _rescanQueued;
        void OnHierarchyChanged()
        {
            if (_rescanQueued) return;
            _rescanQueued = true;
            EditorApplication.delayCall += () => { _rescanQueued = false; if (this != null) Rescan(); };
        }

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

            // The scene view over a converted avatar is a wall of the CCK's
            // pointer and trigger icons — ninety of them on Angela — plus
            // MagicaCloth's collider wires. None of it is ours and all of it
            // buries a socket. One switch hides those icons while you work
            // on sockets and puts them back after; it changes only what the
            // scene view DRAWS, nothing on the avatar.
            have.Body.Add(BridgeElements.SubHeading("Scene view"));
            _quiet = Btn(QuietLabel(), () => { SceneQuiet.Toggle(); _quiet.text = QuietLabel(); });
            _quiet.tooltip = "Hides the CCK component icons, the pointers' blue spheres, MagicaCloth's collider " +
                             "wires and Light icons in the scene view, so socket and plug gizmos can be seen. " +
                             "An editor preference only — nothing on the avatar changes — and it puts back " +
                             "exactly what it found.";
            have.Body.Add(BridgeElements.Row(_quiet));
            _pages.Add(have);
            Selection.selectionChanged -= RefreshSelection;
            Selection.selectionChanged += RefreshSelection;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;

            // 3 — Build.
            var build = new BridgeElements.Card("Build", null, null, 3, 1f);
            _build = new BridgeElements.PrimaryButton("Bake every plug and verify", BuildAll);
            build.Body.Add(_build);
            build.Body.Add(BridgeElements.Hint(
                "Bakes each YAPS Plug under the picked object — measuring the mesh, patching its own " +
                "shader, writing the knobs, announcing it to every socket family — and rebuilds each " +
                "YAPS Socket's markers. Then checks the lot. Safe to run again; it edits, not stacks."));
            _pages.Add(build);

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
                "Each lands in front of the scene camera. The plug wears YAPS Simple Lit — the plain " +
                "shader the toolkit falls back to when a mesh's own cannot be patched — baked and " +
                "announced like any plug. Select the hole or ring and click Preview, then move it around " +
                "the plug."));
            make.Body.Add(BridgeElements.SubHeading("Prefabs"));
            make.Body.Add(BridgeElements.Row(Btn("Create universal socket prefabs", YapsSocketBuilder.CreatePrefabs)));
            make.Body.Add(BridgeElements.Hint(
                "Writes YAPS Hole and YAPS Ring to Assets/YAPS/Prefabs — drag one under a bone on any " +
                "avatar and it works for every plug on the platform."));
            _pages.Add(make);

            var game = new BridgeElements.Card("In game", null, false, null, 1f);
            game.Body.Add(BridgeElements.Hint(
                "To try a test plug and socket in ChilloutVR, put each on its own CVR Spawnable and " +
                "upload them as props. A prop needs a collider to be grabbable, and Disallow Theft on " +
                "the plug so a socket switching on cannot pull it out of someone's hand. As props they " +
                "find each other by marker lights; the synced contact channel between props — the " +
                "part that reaches remote viewers — is the toolkit's next piece."));
            _pages.Add(game);
        }

        // --- behaviour -----------------------------------------------------------

        static string QuietLabel() => SceneQuiet.IsQuiet
            ? "Show the CCK's icons again"
            : "Quiet the scene view while I work";

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
                int bare = _scan.Plugs.Concat(_scan.Sockets).Count(f => f.Root != null
                    && f.Root.GetComponent<YapsSocket>() == null && f.Root.GetComponent<YapsPlug>() == null);
                if (bare > 0)
                {
                    _next.text = $"All YAPS, and {bare} of it came from a conversion with nothing on it you " +
                                 "can edit yet. Click \"make editable\" on a row — or Build, which does all " +
                                 "of them — and each socket and plug gets its component, filled from what " +
                                 "was built. Then retune anything you like and Build again.";
                    _next.messageType = HelpBoxMessageType.Info;
                }
                else
                {
                    _next.text = "All YAPS and editable. Click a row to select it and retune it in the " +
                                 "Inspector — kind, shapes, every knob — then Build to bake the changes. " +
                                 "Upload as normal.";
                    _next.messageType = HelpBoxMessageType.Info;
                }
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

                var wrap = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                row.style.flexGrow = 1;
                wrap.Add(row);

                var socketComp = f.Root != null ? f.Root.GetComponent<YapsSocket>() : null;
                var plugComp = f.Root != null ? f.Root.GetComponent<YapsPlug>() : null;
                bool hasComp = socketComp != null || plugComp != null;

                if (socketComp != null)
                {
                    var chip = BridgeElements.Chip(socketComp.preview ? "previewing" : "preview",
                        BridgeTheme.Good, socketComp.preview, () =>
                        {
                            // The list can outlive its avatar — a reconvert
                            // destroys and rebuilds it — and a chip holding a
                            // dead component threw on click. Rescan instead.
                            if (socketComp == null) { Rescan(); return; }
                            YapsPreview.Set(socketComp, !socketComp.preview);
                            Rescan();
                        }, socketComp.preview);
                    chip.style.marginLeft = 6; chip.style.marginRight = 8;
                    wrap.Add(chip);
                }
                else if (f.IsYapsAlready && !hasComp && f.Root != null)
                {
                    // Converter output with no authoring component: a bare
                    // object nobody can retune. Adopt puts the component on
                    // it, filled from what was built, and then it edits like
                    // one placed by hand.
                    var chip = BridgeElements.Chip("make editable", BridgeTheme.Warn, false, () =>
                    {
                        Undo.RegisterFullObjectHierarchyUndo(captured.Root.gameObject, "Adopt YAPS " + (captured.Kind == YapsScanner.Kind.Plug ? "plug" : "socket"));
                        Adopt(captured);
                        Rescan();
                    });
                    chip.style.marginLeft = 6; chip.style.marginRight = 8;
                    wrap.Add(chip);
                }
                _foundBody.Add(wrap);
                alt = !alt;
            }
            foreach (var f in _scan.Plugs) Row(f);
            foreach (var f in _scan.Sockets) Row(f);
        }

        // Put the authoring component on a found plug or socket that has
        // none, from what the scan saw. The converter does this itself for
        // anything it builds from now on; this is for what it built before,
        // and for anything else that is YAPS by material alone.
        static void Adopt(YapsScanner.Found f)
        {
            if (f.Root == null) return;
            if (f.Kind == YapsScanner.Kind.Socket)
            {
                var shapes = new List<string>();
                if (f.Material != null && f.Material.HasProperty("_YAPS_ShapeCount"))
                {
                    // The bake's shape names are not on the material; the
                    // component's rows are left for the user to fill from
                    // the dropdown. Kind, lights and strength still carry.
                }
                YapsNativeBuilder.AdoptSocket(f.Root, f.Renderer, f.Material, shapes);
            }
            else
            {
                YapsNativeBuilder.AdoptPlug(f.Root, f.Renderer, f.MaterialSlot, f.Material, null);
            }
        }

        int AdoptAll()
        {
            if (_scan == null) return 0;
            int n = 0;
            foreach (var f in _scan.Plugs.Concat(_scan.Sockets))
            {
                if (!f.IsYapsAlready || f.Root == null) continue;
                if (f.Root.GetComponent<YapsSocket>() != null || f.Root.GetComponent<YapsPlug>() != null) continue;
                Undo.RegisterFullObjectHierarchyUndo(f.Root.gameObject, "Adopt YAPS");
                Adopt(f);
                n++;
            }
            return n;
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
            // Adopt first, so a converted avatar's bare sockets and plug get
            // their components before the bake reads them.
            int adopted = AdoptAll();
            if (adopted > 0) _scan = YapsScanner.Scan(_target);
            int plugsOk = 0, plugsTried = 0, socketsBuilt = 0;
            var lines = new List<string>();
            if (adopted > 0) lines.Add($"made {adopted} editable");
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
