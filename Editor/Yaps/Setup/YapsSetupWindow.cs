// Tools ▸ YAPS ▸ Setup. The toolkit's window for any ChilloutVR avatar
// or prop, on the converter's own elements. Pick, scan and add, build.
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

        enum Mode { Setup, Test, Toolkit }

        Mode _mode = Mode.Setup;
        GameObject _target;
        YapsScanner.Result _scan;

        VisualElement _pages;
        VisualElement _foundBody;
        Label _summary;
        HelpBox _next;
        Label _selection;
        Button _addHole, _addRing, _makePlug, _quiet, _makeProp, _verifyProp;
        BridgeElements.PrimaryButton _build;
        ObjectField _picker;

        void OnDisable()
        {
            Selection.selectionChanged -= RefreshSelection;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }

        // Rescan after any hierarchy change, once per edit.
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
            // Rebuilt with the mode so the active tab's tint follows.
            _tabs.Clear();
            _tabs.Add(BridgeElements.Tabs(
                new[] { "Set up an avatar or prop", "Test props", "Toolkit" },
                new[] { "d_Avatar Icon", "d_PlayButton", "d_SettingsIcon" },
                (int) _mode, i => { _mode = (Mode) i; ShowPage(); }));
            _pages.Clear();
            if (_mode == Mode.Setup) BuildSetupPage(); else if (_mode == Mode.Test) BuildTestPage(); else BuildToolkitPage();
            _pages.Add(Footer());
        }

        // The converter's footer: guide, bug report, Discord.
        static VisualElement Footer()
        {
            var footer = new VisualElement();
            footer.AddToClassList("ab-footer");
            footer.Add(BridgeElements.Link("Guide  ↗", () => Application.OpenURL(BridgeLinks.YapsHelp)));
            var report = BridgeElements.Link("Report an issue  ↗", BridgeLinks.OpenYapsBugReport);
            report.tooltip = "Opens a pre-filled GitHub issue with your versions and detected packages, marked as a YAPS tool report.";
            footer.Add(report);
            if (!string.IsNullOrEmpty(BridgeLinks.DiscordUser))
            {
                var discord = BridgeElements.Link(
                    BridgeLinks.HasDiscordLink ? $"Discord: {BridgeLinks.DiscordUser}" : $"Copy Discord: {BridgeLinks.DiscordUser}",
                    BridgeLinks.OpenDiscord);
                discord.tooltip = "Best for quick questions — please use GitHub issues for bugs so they don't get lost.";
                footer.Add(discord);
            }
            return footer;
        }

        // --- the setup page --------------------------------------------------

        void BuildSetupPage()
        {
            // 1. Pick.
            var pick = new BridgeElements.Card("Pick your avatar or prop", null, null, 1, 0f);
            _picker = new ObjectField("Avatar or prop") { objectType = typeof(GameObject), allowSceneObjects = true, value = _target };
            _picker.RegisterValueChangedCallback(e => { _target = e.newValue as GameObject; Rescan(); });
            pick.Body.Add(_picker);
            _pages.Add(pick);

            // 2. What it has, and what to add.
            var have = new BridgeElements.Card("What it has, and what to add", null, null, 2, 0.5f);
            _summary = new Label("Pick something above.");
            _summary.AddToClassList("ab-hint");
            have.Body.Add(_summary);
            _foundBody = new VisualElement();
            have.Body.Add(_foundBody);

            // What to do next, from the scan and the selection.
            _next = new HelpBox("", HelpBoxMessageType.Info);
            have.Body.Add(_next);

            have.Body.Add(BridgeElements.SubHeading("Add"));
            _addHole = Btn("Add a hole", () => AddSocket(YapsSocket.SocketKind.Hole));
            _addRing = Btn("Add a ring", () => AddSocket(YapsSocket.SocketKind.Ring));
            _makePlug = Btn("Make selected mesh a plug", MakePlug);
            have.Body.Add(BridgeElements.Row(_addHole, _addRing, _makePlug));
            _selection = BridgeElements.Hint("");
            have.Body.Add(_selection);

            // Props: a plug or socket on its own object becomes a spawnable
            // with a pickup, a collider and, for a baked plug, the channel.
            have.Body.Add(BridgeElements.SubHeading("Props"));
            _makeProp = Btn("Make selected object a prop", () => MakeProp(Selection.activeGameObject));
            _verifyProp = Btn("Verify prop", () =>
            {
                var o = YapsPropBuilder.Verify(Selection.activeGameObject);
                _summary.text = o.Message + (o.Notes.Count > 0 ? "  " + string.Join(" ", o.Notes) : "");
            });
            have.Body.Add(BridgeElements.Row(_makeProp, _verifyProp));
            have.Body.Add(BridgeElements.Hint(
                "Select the top object of a plug or socket meant to be spawned in ChilloutVR. It gets a CVR " +
                "Spawnable, a pickup with theft off, a collider to grab by and, for a baked plug, the synced " +
                "contact channel that reaches everyone. Verify before each upload: the CCK inspector can blank " +
                "a channel value's parameter name."));

            // One switch hides the CCK's icons while sockets are placed.
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

            // 3. Build.
            var build = new BridgeElements.Card("Build", null, null, 3, 1f);
            _build = new BridgeElements.PrimaryButton("Bake every plug and verify", BuildAll);
            build.Body.Add(_build);
            build.Body.Add(BridgeElements.Hint(
                "Bakes each YAPS Plug under the picked object — measuring the mesh, patching its own " +
                "shader, writing the knobs, announcing it to every socket family — and rebuilds each " +
                "YAPS Socket's markers. Then checks the lot. Safe to run again; it edits, not stacks."));
            _pages.Add(build);

            // Present, say where; absent, say where to get it.
            var cross = new BridgeElements.Card("Converting from VRChat?", null, false, null, 0f);
            if (BridgeLinks.HasAvatarBridge)
            {
                cross.Body.Add(BridgeElements.Hint(
                    "AvatarBridge does that, and carries a VRChat avatar's DPS, TPS or SPS across as YAPS " +
                    "automatically — same shader, same wire format as this. Tools ▸ Avatar Bridge ▸ " +
                    "VRChat to ChilloutVR Converter."));
                cross.Body.Add(BridgeElements.Row(Btn("Open AvatarBridge", () =>
                    EditorApplication.ExecuteMenuItem("Tools/Avatar Bridge/VRChat to ChilloutVR Converter"))));
            }
            else
            {
                cross.Body.Add(BridgeElements.Hint(
                    "AvatarBridge does that, and carries a VRChat avatar's DPS, TPS or SPS across as YAPS " +
                    "automatically — same shader, same wire format as this. It is not in this project."));
                cross.Body.Add(BridgeElements.Row(BridgeElements.Link("Get AvatarBridge (GitHub)  ↗",
                    () => Application.OpenURL(BridgeLinks.Repo))));
            }
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
            make.Body.Add(BridgeElements.Row(Btn("Make the selected test object a prop", () => MakeProp(Selection.activeGameObject))));
            make.Body.Add(BridgeElements.Hint("Select the test plug or socket at its top object first. Upload the result as a prop from the CCK."));
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

        // --- the toolkit page --------------------------------------------------

        // Standalone utilities from the converter that any ChilloutVR
        // avatar can use, YAPS or not. Each is a card: pick, run, read.
        void BuildToolkitPage()
        {
            var pick = new BridgeElements.Card("Pick your avatar or prop", null, null, 1, 0f);
            _picker = new ObjectField("Avatar or prop") { objectType = typeof(GameObject), allowSceneObjects = true, value = _target };
            _picker.RegisterValueChangedCallback(e => { _target = e.newValue as GameObject; ShowPage(); });
            pick.Body.Add(_picker);
            _pages.Add(pick);

            var stereo = new BridgeElements.Card("Stereo shaders", null, null, 2, 0.5f);
            stereo.Body.Add(BridgeElements.Hint(
                "ChilloutVR renders single-pass instanced; a shader that never opted in draws into one eye only " +
                "in VR. This copies such shaders into Assets/YAPS/Generated with the stereo macros added and " +
                "points this object's materials at the copies. Originals are never touched, a copy that fails " +
                "to compile is thrown away, and only plainly written vertex/fragment shaders can be patched. " +
                "Compilation is verified; how it looks needs VR eyes."));
            var stereoReport = new VisualElement();
            var patch = new BridgeElements.PrimaryButton("Patch shaders for VR stereo", () =>
            {
                if (_target == null) return;
                var report = new BridgeReport();
                Undo.RegisterFullObjectHierarchyUndo(_target, "Patch stereo shaders");
                var animator = _target.GetComponent<Animator>();
                var controller = animator != null ? animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController : null;
                ShaderSpiPatcher.Patch(_target, YapsNativeBuilder.OutputRoot + "/" + _target.name + "/RehomedAssets", controller, report);
                ShowReport(stereoReport, report, "Every shader on it already declares stereo support, or has no source to patch.");
            });
            patch.SetActive(_target != null);
            stereo.Body.Add(patch);
            if (_target == null) stereo.Body.Add(BridgeElements.Hint("Pick an object above first."));
            stereo.Body.Add(stereoReport);
            _pages.Add(stereo);

            var more = new BridgeElements.Card("Also in this package", null, false, null, 1f);
            more.Body.Add(BridgeElements.Hint(
                "CCK Animator Tester drives a ChilloutVR avatar the way the game does: gestures, stances, " +
                "visemes, emotes and the whole Advanced Settings menu, in Play mode. Tools ▸ Avatar Bridge ▸ " +
                "CCK Animator Tester."));
            more.Body.Add(BridgeElements.Row(BridgeElements.Link("Open the tester", () =>
                EditorApplication.ExecuteMenuItem("Tools/Avatar Bridge/CCK Animator Tester"))));
            _pages.Add(more);
        }

        static void ShowReport(VisualElement into, BridgeReport report, string whenEmpty)
        {
            into.Clear();
            if (report.Entries.Count == 0) { into.Add(BridgeElements.Hint(whenEmpty)); return; }
            bool alt = false;
            foreach (var e in report.Entries)
            {
                var colour = e.Status == ReportStatus.Error ? BridgeTheme.Bad
                           : e.Status == ReportStatus.Warning || e.Status == ReportStatus.Approximated ? BridgeTheme.Warn
                           : e.Status == ReportStatus.Converted ? BridgeTheme.Good : BridgeTheme.Muted;
                into.Add(BridgeElements.ReportRow(e.Status.ToString(), e.Subject, e.Detail, colour, alt));
                alt = !alt;
            }
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

        // The next-step line, from the scan and the selection.
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
                _next.text = "This has DPS, TPS or SPS on it that is not YAPS yet. Click \"upgrade to YAPS\" on a " +
                             "row, or Build, which does them all: sockets gain the markers they lack; a plug is " +
                             "baked with its author's values carried and the old deform switched off (DPS moves " +
                             "to YAPS Simple Lit, since its deform has no switch). Check the plug's Root Bone " +
                             "first on a skinned mesh.";
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

        // The buttons name what they will act on.
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
                ? $"Selected: bone \"{go.name}\" — a socket added now goes under it and follows it; Make a plug bakes the mesh this bone drives, from this bone down."
                : mesh ? $"Selected: mesh \"{go.name}\" — Make a plug will bake this one."
                : $"Selected: \"{go.name}\" — not a bone, so a socket goes in the YAPS folder; not a mesh, so no plug.";
            if (_addHole != null) _addHole.text = bone ? $"Add a hole under {go.name}" : "Add a hole";
            if (_addRing != null) _addRing.text = bone ? $"Add a ring under {go.name}" : "Add a ring";
            if (_makePlug != null)
            {
                _makePlug.text = mesh ? $"Make \"{go.name}\" a plug" : bone ? $"Make a plug from bone {go.name}" : "Make selected mesh a plug";
                _makePlug.SetEnabled(mesh || bone);
            }
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

                // Customise: select it and open its inspector.
                if (hasComp)
                {
                    var edit = BridgeElements.Chip("customise", new Color(0.45f, 0.65f, 0.95f), true, () =>
                    {
                        if (captured.Root == null) { Rescan(); return; }
                        Selection.activeTransform = captured.Root;
                        EditorGUIUtility.PingObject(captured.Root);
                        // Front the Inspector, opening one if there is none.
                        EditorApplication.ExecuteMenuItem("Window/General/Inspector");
                    });
                    edit.style.marginLeft = 6;
                    wrap.Add(edit);
                }
                if (socketComp != null)
                {
                    var chip = BridgeElements.Chip(socketComp.preview ? "previewing" : "preview",
                        BridgeTheme.Good, socketComp.preview, () =>
                        {
                            // A reconvert can leave the chip holding a dead component.
                            if (socketComp == null) { Rescan(); return; }
                            YapsPreview.Set(socketComp, !socketComp.preview);
                            Rescan();
                        }, socketComp.preview);
                    chip.style.marginLeft = 4; chip.style.marginRight = 8;
                    wrap.Add(chip);
                }
                else if (!hasComp && f.Root != null)
                {
                    // No component yet. YAPS output adopts; DPS, TPS or SPS
                    // upgrades in place: adopt, then build or bake.
                    bool legacy = !f.IsYapsAlready;
                    var chip = BridgeElements.Chip(legacy ? "upgrade to YAPS" : "make editable", BridgeTheme.Warn, false, () =>
                    {
                        if (captured.Root == null) { Rescan(); return; }
                        Undo.RegisterFullObjectHierarchyUndo(captured.Root.gameObject, "Adopt YAPS " + (captured.Kind == YapsScanner.Kind.Plug ? "plug" : "socket"));
                        Adopt(captured);
                        if (legacy) Upgrade(captured);
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

        // Puts the authoring component on a found plug or socket that has none.
        static void Adopt(YapsScanner.Found f)
        {
            if (f.Root == null) return;
            if (f.Kind == YapsScanner.Kind.Socket)
            {
                var shapes = new List<string>();
                if (f.Material != null && f.Material.HasProperty("_YAPS_ShapeCount"))
                {
                    // Shape names are not on the material; the rows stay for the user.
                }
                YapsNativeBuilder.AdoptSocket(f.Root, f.Renderer, f.Material, shapes);
            }
            else
            {
                YapsNativeBuilder.AdoptPlug(f.Root, f.Renderer, f.MaterialSlot, f.Material, null);
            }
        }

        // A legacy socket becomes YAPS by building the markers it lacks; a
        // legacy plug by baking, which carries its values and switches the
        // old deform off.
        void Upgrade(YapsScanner.Found f)
        {
            if (f.Root == null) return;
            if (f.Kind == YapsScanner.Kind.Socket)
            {
                var socket = f.Root.GetComponent<YapsSocket>();
                if (socket != null) YapsSocketBuilder.Build(socket);
            }
            else
            {
                var plug = f.Root.GetComponent<YapsPlug>();
                if (plug == null) return;
                var o = YapsNativeBuilder.Bake(plug);
                if (!o.Ok) Debug.LogError("[YAPS] " + o.Message);
                _summary.text = o.Message + (o.Notes.Count > 0 ? "  " + string.Join(" ", o.Notes) : "");
            }
        }

        int AdoptAll()
        {
            if (_scan == null) return 0;
            int n = 0;
            foreach (var f in _scan.Plugs.Concat(_scan.Sockets))
            {
                if (f.Root == null) continue;
                if (f.Root.GetComponent<YapsSocket>() != null || f.Root.GetComponent<YapsPlug>() != null) continue;
                Undo.RegisterFullObjectHierarchyUndo(f.Root.gameObject, "Adopt YAPS");
                Adopt(f);
                n++;
            }
            return n;
        }

        // A mesh selected: the plug is that mesh. A bone selected: the plug
        // is the skinned mesh that bone drives, from that bone down, and
        // the component sits on the bone so its markers follow it.
        void MakePlug()
        {
            var go = Selection.activeGameObject;
            var renderer = go != null ? go.GetComponent<Renderer>() : null;
            Transform rootBone = null;
            if (renderer == null && go != null)
            {
                var root = YapsSocketEditor.AvatarRootOf(go.transform);
                if (root != null && go.transform != root && IsBone(go.transform, root))
                {
                    renderer = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .FirstOrDefault(s => s.bones != null && System.Array.IndexOf(s.bones, go.transform) >= 0);
                    rootBone = go.transform;
                }
            }
            if (renderer == null)
            {
                _summary.text = "Select the mesh that should bend, or the bone the shaft grows from, in the Hierarchy, then press this.";
                return;
            }
            var plug = go.GetComponent<YapsPlug>();
            if (plug == null)
            {
                plug = Undo.AddComponent<YapsPlug>(go);
                plug.renderer = renderer;
                plug.rootBone = rootBone;
                var skin = renderer as SkinnedMeshRenderer;
                if (skin != null && rootBone != null) plug.materialSlot = YapsNativeBuilder.SlotWeightedTo(skin, rootBone);
            }
            var o = YapsNativeBuilder.Bake(plug);
            if (!o.Ok) Debug.LogError("[YAPS] " + o.Message);
            if (_target == null) _picker.value = YapsSocketEditor.AvatarRootOf(go.transform).gameObject;
            Rescan();
            _summary.text = o.Message + (o.Notes.Count > 0 ? "  " + string.Join(" ", o.Notes) : "");
        }

        void MakeProp(GameObject root)
        {
            var o = YapsPropBuilder.MakeProp(root);
            if (!o.Ok) Debug.LogError("[YAPS] " + o.Message);
            if (o.Ok && _target == null) _picker.value = root;
            Rescan();
            _summary.text = o.Message + (o.Notes.Count > 0 ? "  " + string.Join(" ", o.Notes) : "");
        }

        void BuildAll()
        {
            if (_target == null) return;
            // Adopt first, so bare converted sockets and plugs get components.
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

        // Under the selected bone, else in a YAPS folder on the avatar.
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
                    // Unique names in the folder.
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

        // A bone: bound by a skinned mesh, or under an Armature.
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
