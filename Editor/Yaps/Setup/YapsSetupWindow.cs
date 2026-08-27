// Tools > YAPS > Setup. The toolkit's window for any ChilloutVR avatar
// or prop, on the converter's own elements. Pick, scan and add, build.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using ABI.CCK.Components;
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
        Label _pickNote;
        VisualElement _buildLog;
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
            _pages.AddToClassList("ab-scroll");
            root.Add(_pages);
            ShowPage();
        }

        VisualElement _tabs;

        void ShowPage()
        {
            // Rebuilt with the mode so the active tab's tint follows.
            _tabs.Clear();
            _tabs.Add(BridgeElements.Tabs(
                new[] { "Set up an avatar or prop", "Test it" },
                // Unthemed: GetIcon adds the dark-skin prefix itself.
                new[] { "Avatar Icon", "PlayButton" },
                (int) _mode, i => { _mode = (Mode) i; ShowPage(); }));
            _pages.Clear();
            if (_mode == Mode.Setup) BuildSetupPage(); else BuildTestPage();
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
            _pickNote = BridgeElements.Hint(
                "Drop the avatar here, or anything under it: a bone, a mesh, a socket. The toolkit takes the " +
                "avatar or prop above it and lists everything on the whole thing, so a doubled-up socket shows.");
            _picker.RegisterValueChangedCallback(e => Pick(e.newValue as GameObject));
            pick.Body.Add(_picker);
            pick.Body.Add(_pickNote);
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
            // The exact route is a choice, because it costs the prop's ownership.
            var channelOn = Btn("Add the synced channel", () =>
            {
                var o = YapsPropBuilder.AddChannel(Selection.activeGameObject);
                _summary.text = o.Message + (o.Notes.Count > 0 ? "  " + string.Join(" ", o.Notes) : "");
            });
            channelOn.tooltip = "Eight synced values a socket writes, for a plug prop that must reach a viewer with " +
                                "lights off or work among more than four lit sockets. It costs the prop's ownership: " +
                                "while a socket touches it, that socket's owner takes it over, which is what pulls a " +
                                "prop out of someone's hand.";
            var channelOff = Btn("Drop the contact channel", () =>
            {
                var o = YapsPropBuilder.DropChannel(Selection.activeGameObject);
                _summary.text = o.Message + (o.Notes.Count > 0 ? "  " + string.Join(" ", o.Notes) : "");
            });
            channelOff.tooltip = "Takes the channel off a prop that keeps changing hands. It then reads sockets by " +
                                 "their marker lights, which every client works out for itself.";
            have.Body.Add(BridgeElements.Row(channelOn, channelOff));
            have.Body.Add(BridgeElements.Hint(
                "Select the top object of a plug or socket meant to be spawned in ChilloutVR. It gets a CVR " +
                "Spawnable, a pickup anyone can take and a collider to grab by, and it finds sockets through " +
                "their marker lights — every client works those out for itself, so nobody owns the answer and " +
                "nobody takes the prop off anyone. The synced channel is the exact route and a separate choice; " +
                "it hands the prop to whoever's socket touches it. Verify before each upload: the CCK inspector " +
                "can blank a channel value's parameter name."));

            // One switch hides the CCK's icons while sockets are placed.
            have.Body.Add(BridgeElements.SubHeading("Scene view"));
            _quiet = Btn(QuietLabel(), () => { SceneQuiet.Toggle(); _quiet.text = QuietLabel(); });
            _quiet.tooltip = "Hides the CCK component icons, the pointers' blue spheres, the triggers' boxes, MagicaCloth's collider " +
                             "wires and Light icons in the scene view, so socket and plug gizmos can be seen. " +
                             "An editor preference only — nothing on the avatar changes — and it puts back " +
                             "exactly what it found.";
            have.Body.Add(BridgeElements.Row(_quiet));

            // What a socket or plug deleted by hand leaves behind.
            have.Body.Add(BridgeElements.SubHeading("Tidy"));
            var sweep = Btn("Clean up leftovers", () =>
            {
                if (_target == null) return;
                var done = YapsRemover.Sweep(_target.transform);
                _summary.text = done.Count == 0
                    ? "Nothing left behind: every YAPS layer, parameter, toggle and marker object belongs to a socket or plug that is still here."
                    : $"Cleaned up {done.Count} leftover(s): " + string.Join("; ", done) + ". One undo step.";
                foreach (var line in done) Debug.Log("[YAPS] Cleaned up " + line);
                Rescan();
            });
            sweep.tooltip = "Deleted a socket or plug by hand? This removes what it left: an animator layer with no " +
                            "socket, a depth parameter nothing reads, a menu toggle aiming at nothing, and the toolkit's " +
                            "marker objects with no component above them. Use the remove chip on a row instead to take " +
                            "one out cleanly in the first place.";
            have.Body.Add(BridgeElements.Row(sweep));
            have.Body.Add(BridgeElements.Hint(
                "Every row above has a remove chip that takes the plug or socket out entire, in one undo step. " +
                "Deleted one by hand instead? Clean up leftovers finds what it left behind."));
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

            // What it did, line by line, where the button is. A summary
            // label was too easy to miss for work this large.
            _buildLog = new VisualElement();
            build.Body.Add(_buildLog);
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

            var toolkit = new BridgeElements.Card("More tools", null, false, null, 0.5f);
            toolkit.Body.Add(BridgeElements.Hint(
                "The ChilloutVR Toolkit checks any avatar for what the game will break, patches shaders for VR " +
                "stereo, wires visemes and blink, clamps audio, fixes mesh bounds, adds a height slider, writes " +
                "the store description and merges animators. Tools ▸ Avatar Bridge ▸ ChilloutVR Toolkit."));
            toolkit.Body.Add(BridgeElements.Row(BridgeElements.Link("Open the Toolkit", ToolkitWindow.Open)));
            _pages.Add(toolkit);

            if (_target == null && Selection.activeGameObject != null) Pick(Selection.activeGameObject);
            else Rescan();
        }

        // --- the test page -----------------------------------------------------

        void BuildTestPage()
        {
            var what = new BridgeElements.Card("Test it here", null, null, 1, 0f);
            what.Body.Add(BridgeElements.Hint(
                "See a plug bend before you upload. Drop a test hole or ring in front of the scene " +
                "camera and every baked plug in the scene bends toward it, live, while you move it. " +
                "No plug yet? Drop a test plug too. Nothing here ships: the test objects are yours to " +
                "delete, and Preview writes nothing to the avatar."));
            _pages.Add(what);

            var make = new BridgeElements.Card("Drop a test socket", null, null, 2, 0.5f);
            make.Body.Add(BridgeElements.Row(
                Btn("Test hole (previews)", () => TestSocket(YapsSocket.SocketKind.Hole)),
                Btn("Test ring (previews)", () => TestSocket(YapsSocket.SocketKind.Ring)),
                Btn("Test plug", () => YapsNativeBuilder.BuildTestPlug())));
            make.Body.Add(BridgeElements.Hint(
                "The socket lands in front of the camera with Preview already on: select it and move " +
                "it around your baked plug. No plug in the scene yet? Test plug drops one: a capsule baked through the exact path your own " +
                "mesh takes, on YAPS Simple Lit. Stop Preview on the socket, or delete it, when done."));
            _pages.Add(make);

            var props = new BridgeElements.Card("Props and prefabs", null, false, null, 1f);
            props.Body.Add(BridgeElements.Row(Btn("Make the selected test object a prop", () => MakeProp(Selection.activeGameObject))));
            props.Body.Add(BridgeElements.Hint(
                "Select the test plug or socket at its top object first. It gets a CVR Spawnable, a " +
                "pickup with theft off, a collider and, for a plug, the synced contact channel; upload " +
                "it from the CCK and try it with a second person. As props they find each other by " +
                "marker lights and, plug to socket, by the channel."));
            props.Body.Add(BridgeElements.SubHeading("Prefabs"));
            props.Body.Add(BridgeElements.Row(
                Btn("Create universal socket prefabs", YapsSocketBuilder.CreatePrefabs),
                Btn("Create a ring-and-socket prop prefab", YapsSocketBuilder.CreateSocketPropPrefab),
                Btn("Create a plug prop prefab", YapsSocketBuilder.CreatePlugPropPrefab)));
            props.Body.Add(BridgeElements.Hint(
                "Writes YAPS Hole and YAPS Ring to Assets/YAPS/Prefabs; drag one under a bone on any " +
                "avatar and every plug on the platform reads it. The plug prop is a whole spawnable in " +
                "one click — built, baked on the current shader, pickup and contact channel wired — to " +
                "drop in the scene and upload. Make it again after updating: a prop already uploaded " +
                "keeps the bake and the shader copy it was built with, which is why an old one bends oddly."));
            _pages.Add(props);
        }

        // A test socket in front of the camera, previewing at once: every
        // baked plug in the scene bends toward it, and a test plug is
        // dropped when there is none.
        void TestSocket(YapsSocket.SocketKind kind)
        {
            var socket = AddSocket(kind, atCamera: true);
            if (socket == null) return;
            YapsPreview.Set(socket, true, spawnPlugIfNone: false);
        }

        // --- behaviour -----------------------------------------------------------

        // Whatever lands here, the avatar or prop above it is the target,
        // so the list always covers the whole thing.
        void Pick(GameObject picked)
        {
            var top = TopOf(picked);
            _target = top;
            if (_picker != null && _picker.value != top) _picker.SetValueWithoutNotify(top);
            if (_pickNote != null)
            {
                _pickNote.text = top == null
                    ? "Drop the avatar here, or anything under it: a bone, a mesh, a socket. The toolkit takes the " +
                      "avatar or prop above it and lists everything on the whole thing, so a doubled-up socket shows."
                    : top == picked
                        ? $"Everything on \"{top.name}\" is listed below, wherever it sits."
                        : $"You dropped \"{picked.name}\"; the {(top.GetComponent<CVRAvatar>() != null ? "avatar" : top.GetComponent<CVRSpawnable>() != null ? "prop" : "top object")} " +
                          $"above it, \"{top.name}\", is the target. Everything on it is listed below.";
            }
            Rescan();
        }

        // The avatar or prop an object belongs to: the CVRAvatar or
        // CVRSpawnable above it, else its top object.
        static GameObject TopOf(GameObject go)
        {
            if (go == null) return null;
            var avatar = go.GetComponentInParent<CVRAvatar>(true);
            if (avatar != null) return avatar.gameObject;
            var prop = go.GetComponentInParent<CVRSpawnable>(true);
            if (prop != null) return prop.gameObject;
            return go.transform.root.gameObject;
        }

        static string QuietLabel() => SceneQuiet.IsQuiet
            ? "Show the CCK's icons again"
            : "Quiet the scene view while I work";

        static Button Btn(string text, System.Action act)
        {
            var b = new Button(act) { text = text };
            b.AddToClassList("ab-btn");
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
                _next.text = "Nothing on it yet, and that is normal for an avatar that never had penetration: " +
                             "YAPS is what adds it. A plug: click the mesh that should bend (or the bone its " +
                             "shaft grows from) in the Hierarchy, then Make a plug. " +
                             "A socket: click the bone it should follow (Hips, say), then Add a hole or Add a " +
                             "ring. Then Build.";
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
        // Where a new socket or plug goes: the Hierarchy selection, and
        // only when it sits under the target.
        GameObject Candidate()
        {
            var go = Selection.activeGameObject;
            if (go == null) return null;
            if (_target != null && !go.transform.IsChildOf(_target.transform)) return null;
            return go;
        }

        void RefreshSelection()
        {
            if (_selection == null) return;
            var go = Candidate();
            if (go == null)
            {
                _selection.text = "Select a bone or a mesh in the Hierarchy: a socket goes under the selected bone (a YAPS folder on the avatar when nothing is selected); a plug is made from the selected mesh, or from the mesh a selected bone drives.";
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
            // Carried meshes are not plugs to bake: their carrier bakes them.
            // Counting them promises a number the build will not do.
            int bakeable = _scan.Plugs.Count(p => p.CarriedBy == null);
            _build?.SetLabel(_scan.Total > 0
                ? $"Bake {bakeable} plug{(bakeable == 1 ? "" : "s")} and verify {_scan.Sockets.Count} socket{(_scan.Sockets.Count == 1 ? "" : "s")}"
                : "Nothing to build yet");
            SayNext();

            bool alt = false;
            void Row(YapsScanner.Found f)
            {
                // Two sockets within three centimetres is one too many.
                // Said on both rows.
                if (f.Kind == YapsScanner.Kind.Socket && f.Root != null)
                {
                    var twins = _scan.Sockets.Where(o => o != f && o.Root != null
                        && Vector3.Distance(o.Root.position, f.Root.position) < 0.03f)
                        .Select(o => o.Name).ToList();
                    if (twins.Count > 0) f.Notes.Add("on the same spot as " + string.Join(", ", twins) + " — one too many?");
                }
                // A mesh another plug carries is not a plug. It gets a row —
                // it IS being changed, and a change nobody can see is the one
                // people add a second plug to fix — but a nested, quiet one
                // that says whose it is and offers none of the controls that
                // would make it a peer.
                if (f.CarriedBy != null)
                {
                    var carried = BridgeElements.ReportRow("part of",
                        f.Name,
                        $"carried by \"{YapsToggles.LabelFor(f.CarriedBy)}\" — its bones move this mesh, so it " +
                        "was baked with that plug's frame and length and bends as one piece with it. " +
                        "Nothing to set here: it wears that plug's settings. Give it its own plug only if " +
                        "you want it to bend separately.",
                        BridgeTheme.Dark ? new Color(0.45f, 0.47f, 0.52f) : new Color(0.55f, 0.57f, 0.62f), alt);
                    carried.style.marginLeft = 22;
                    carried.style.opacity = 0.75f;
                    var held = f;
                    carried.RegisterCallback<ClickEvent>(_ =>
                    {
                        if (held.Root != null) { Selection.activeTransform = held.Root; EditorGUIUtility.PingObject(held.Root); }
                    });
                    _foundBody.Add(carried);
                    alt = !alt;
                    return;
                }

                bool complete = f.Notes.Count == 0;
                // Nothing to fix but something to say gets a softer green
                // than a silent row: working as designed must never wear
                // the colour that means "you have a problem".
                var settled = BridgeTheme.Dark ? new Color(0.42f, 0.72f, 0.52f) : new Color(0.28f, 0.55f, 0.35f);
                var colour = f.IsYapsAlready && complete
                             ? (f.Expected.Count > 0 ? settled : BridgeTheme.Good)
                           : !complete ? BridgeTheme.Warn
                           : new Color(0.45f, 0.65f, 0.95f);
                string what = f.Kind == YapsScanner.Kind.Plug ? "Plug" : (f.IsHole ? "Hole" : "Ring");
                var detail = new List<string>();
                if (f.Kind == YapsScanner.Kind.Plug && f.StatedLength > 0) detail.Add($"{f.StatedLength:0.###} m");
                detail.Add((f.Kind == YapsScanner.Kind.Plug ? "seen by " : "readable by ") + f.ReadableList());
                if (f.Kind == YapsScanner.Kind.Socket && f.HasAxis) detail.Add("has an axis");
                if (f.Renderer != null && f.Kind == YapsScanner.Kind.Socket) detail.Add("shapes on " + f.Renderer.name);
                detail.AddRange(f.Notes);
                detail.AddRange(f.Expected);

                // By the bone it hangs from, so two rings are two rows a
                // reader can tell apart.
                var sc = f.Root != null ? f.Root.GetComponent<YapsSocket>() : null;
                var pc = f.Root != null ? f.Root.GetComponent<YapsPlug>() : null;
                string title = sc != null ? YapsToggles.LabelFor(sc)
                             : pc != null ? YapsToggles.LabelFor(pc)
                             : f.Name;
                var row = BridgeElements.ReportRow(what, title, string.Join("  ·  ", detail), colour, alt);
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
                    // Remove: out entire, after a dialog saying what goes.
                    var gone = BridgeElements.Chip("remove", BridgeTheme.Bad, false, () =>
                    {
                        if (captured.Root == null) { Rescan(); return; }
                        var s = captured.Root.GetComponent<YapsSocket>();
                        var p = captured.Root.GetComponent<YapsPlug>();
                        bool did = s != null ? YapsRemover.Ask(s) : YapsRemover.Ask(p);
                        if (did) Rescan();
                    });
                    gone.style.marginLeft = 4;
                    wrap.Add(gone);
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
                else if (plugComp != null)
                {
                    // The plug's half of the same idea. A socket previews by
                    // dropping a plug in front of it; a plug had no row chip
                    // at all, because there was nothing for it to bend toward
                    // until the test socket existed.
                    bool testing = YapsPreview.TestSocketInScene;
                    var chip = BridgeElements.Chip(testing ? "previewing" : "preview",
                        BridgeTheme.Good, testing, () =>
                        {
                            if (captured.Root == null) { Rescan(); return; }
                            if (YapsPreview.TestSocketInScene) YapsPreview.RemoveTestSocket();
                            else YapsPreview.DropTestSocket(plugComp);
                            Rescan();
                        }, testing);
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
            // Each plug, then the meshes it carries, so "part of" sits under
            // the thing it is part of.
            foreach (var f in _scan.Plugs.Where(p => p.CarriedBy == null))
            {
                Row(f);
                var mine = f.Root != null ? f.Root.GetComponent<YapsPlug>() : null;
                if (mine == null) continue;
                foreach (var c in _scan.Plugs.Where(p => p.CarriedBy == mine)) Row(c);
            }
            // Anything carried by a plug that is not itself listed, so a row
            // can never go missing.
            foreach (var f in _scan.Plugs.Where(p => p.CarriedBy != null
                && !_scan.Plugs.Any(o => o.CarriedBy == null && o.Root != null
                                         && o.Root.GetComponent<YapsPlug>() == p.CarriedBy)))
            {
                Row(f);
            }
            foreach (var f in _scan.Sockets) Row(f);
        }

        // Puts the authoring component on a found plug or socket that has none.
        static void Adopt(YapsScanner.Found f)
        {
            if (f.Root == null) return;
            // Never a mesh another plug carries. It wears a patched material,
            // so it reads as a plug, and adopting it hands it a component —
            // which is a claim on the mesh, which makes the carrier let go of
            // it, which leaves it bending on its own frame. That is Build
            // re-creating the exact component the user just deleted, every
            // time they press it.
            if (f.CarriedBy != null) return;
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
                // The same door the inspector's Bake goes through, menu and
                // channel included. Bake alone left the channel holding the
                // frames of a previous build and the menu animator unrefreshed,
                // so one plug came out differently depending on which button
                // was pressed. BuildAll below does those two once, for the
                // whole avatar, which is why it can call the bare Bake.
                var o = YapsNativeBuilder.BakeAndRefreshMenu(plug);
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
                if (f.Root == null || f.CarriedBy != null) continue;
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
            var go = Candidate();
            var renderer = go != null ? go.GetComponent<Renderer>() : null;
            Transform rootBone = null;
            if (renderer == null && go != null)
            {
                var root = YapsSocketEditor.AvatarRootOf(go.transform);
                if (root != null && go.transform != root && IsBone(go.transform, root))
                {
                    // Most vertices weighted to the chain, not the first
                    // mesh that names the bone: that is always the body.
                    int most = 0;
                    foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        int count = YapsBaker.CountVerticesUnder(skin, go.transform);
                        if (count > most) { most = count; renderer = skin; }
                    }
                    rootBone = go.transform;
                    if (renderer == null)
                    {
                        _summary.text = $"No skinned mesh has vertices weighted to \"{go.name}\" or the bones under it. " +
                                        "Select the plug mesh itself instead, or the bone its shaft is actually skinned to.";
                        return;
                    }
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
                // Left on auto. Pinning it to the best-weighted slot here
                // reads as helpful and is not: an explicit slot means "this
                // one only", so a plug whose vertices span several materials
                // silently bakes into one and tears along the seam. The bake
                // finds every slot the chain reaches; a number is the author
                // overriding that, not the tool guessing for them.
                plug.materialSlot = -1;
            }
            else if (rootBone != null && plug.renderer != renderer
                     && YapsBaker.CountVerticesUnder(plug.renderer, rootBone) == 0)
            {
                // A plug made earlier on the wrong mesh: take the right one.
                Undo.RecordObject(plug, "YAPS plug mesh");
                plug.renderer = renderer;
                plug.rootBone = rootBone;
                plug.materialSlot = -1;   // auto, for the reason above
            }
            var o = YapsNativeBuilder.BakeAndRefreshMenu(plug);
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
            int edits = YapsToggles.Edits;
            foreach (var p in _target.GetComponentsInChildren<YapsPlug>(true))
            {
                plugsTried++;
                var o = YapsNativeBuilder.Bake(p);
                if (o.Ok) plugsOk++;
                lines.Add((o.Ok ? "✓ " : "✗ ") + o.Message);
                // The toggle and wiring notes were only in the console before.
                lines.AddRange(o.Notes.Where(n => n.Contains("menu toggle") || n.Contains("Wired")));
            }
            // The plugs' toggles into the menu animator, once. Sockets do their own.
            string menu = YapsToggles.RefreshMenuAnimator(_target.GetComponentInChildren<CVRAvatar>(), edits);
            if (menu != null) lines.Add(menu);
            foreach (var s in _target.GetComponentsInChildren<YapsSocket>(true))
            {
                socketsBuilt++;
                lines.AddRange(YapsNativeBuilder.BuildSocket(s));
            }
            // Last, and once: the channel reads the frames the bakes just
            // measured, and it replaces its own wiring rather than stacking.
            lines.AddRange(YapsNativeChannel.Build(_target.GetComponentInChildren<CVRAvatar>()));
            Rescan();
            string headline = $"Built {plugsOk} of {plugsTried} plug{(plugsTried == 1 ? "" : "s")} and " +
                              $"{socketsBuilt} socket{(socketsBuilt == 1 ? "" : "s")}.";
            _summary.text = headline + "  The Build card below says what it did.";
            ShowBuildLog(headline, lines);
        }

        // The build's own report, under its button, and in the console for
        // a bug report to quote.
        void ShowBuildLog(string headline, List<string> lines)
        {
            if (_buildLog == null) return;
            _buildLog.Clear();
            _buildLog.Add(BridgeElements.SubHeading("What Build did"));
            _buildLog.Add(new HelpBox(headline + (lines.Count == 0
                ? " Nothing needed doing."
                : $" {lines.Count} note(s) below, and the same lines are in the Console."),
                lines.Any(l => l.StartsWith("✗")) ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info));

            bool alt = false;
            foreach (var line in lines)
            {
                var colour = line.StartsWith("✗") ? BridgeTheme.Bad
                           : line.StartsWith("⚠") || line.Contains("⚠") ? BridgeTheme.Warn
                           : BridgeTheme.Good;
                string text = line.TrimStart('✓', '✗', '⚠', ' ');
                int cut = text.IndexOf(':');
                string what = cut > 0 && cut < 40 ? text.Substring(0, cut) : "Build";
                string rest = cut > 0 && cut < 40 ? text.Substring(cut + 1).Trim() : text;
                _buildLog.Add(BridgeElements.ReportRow(colour == BridgeTheme.Bad ? "failed" : "done",
                    what, rest, colour, alt));
                alt = !alt;
                Debug.Log("[YAPS] " + line);
            }

            // Where the files went, and a way to get there.
            string dir = YapsNativeBuilder.OutputRoot + "/" + (_target != null ? _target.name : "");
            _buildLog.Add(BridgeElements.Hint("Generated materials, bakes and clips are in " + dir + "."));
            _buildLog.Add(BridgeElements.Row(Btn("Show me the files", () =>
            {
                var folder = AssetDatabase.LoadAssetAtPath<Object>(dir)
                             ?? AssetDatabase.LoadAssetAtPath<Object>(YapsNativeBuilder.OutputRoot);
                if (folder != null) { Selection.activeObject = folder; EditorGUIUtility.PingObject(folder); }
                else Debug.LogWarning("[YAPS] Nothing has been generated yet, so " + dir + " does not exist.");
            })));
        }

        // Under the selected bone, else in a YAPS folder on the avatar.
        YapsSocket AddSocket(YapsSocket.SocketKind kind, bool atCamera = false)
        {
            string name = kind == YapsSocket.SocketKind.Hole ? "YAPS Hole" : "YAPS Ring";
            var go = new GameObject(name);

            if (atCamera)
            {
                // Beside a baked plug when there is one: just past its tip,
                // a little above the axis, entrance facing the base, so the
                // plug bends into it at once. Else in front of the camera.
                if (YapsPreview.FirstBakedPlugFrame(out var origin, out var forward, out var up, out float length))
                {
                    go.transform.position = origin + forward * (length * 0.85f) + up * (length * 0.35f);
                    go.transform.rotation = Quaternion.LookRotation(-forward, up);
                }
                else
                {
                    var cam = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
                    if (cam != null)
                    {
                        go.transform.position = cam.transform.position + cam.transform.forward * 0.6f;
                        go.transform.rotation = Quaternion.LookRotation(-cam.transform.forward);
                    }
                }
            }
            else
            {
                // The selected bone; and the avatar's own root for the YAPS
                // folder when nothing is selected.
                var candidate = Candidate();
                var selected = candidate != null ? candidate.transform : null;
                var avatarRoot = _target != null ? YapsSocketEditor.AvatarRootOf(_target.transform)
                    : selected != null ? YapsSocketEditor.AvatarRootOf(selected) : null;
                if (avatarRoot == null && _target != null) avatarRoot = _target.transform;
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
            return socket;
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
