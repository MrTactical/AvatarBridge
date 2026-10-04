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

        VisualElement _tabs;
        VisualElement _pages;
        VisualElement _foundBody;
        Label _summary;
        Label _selection;
        Label _pickNote;
        Label _buildCounts;
        VisualElement _buildLog;
        VisualElement _legacy;
        // Each action reports under its own button. Results never go through the scan summary,
        // which every rescan rewrites.
        BridgeElements.NoticeBox _next, _addStatus, _propStatus, _tidyStatus;
        Button _addHole, _addRing, _makePlug;
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
            var root = BridgeElements.Root(rootVisualElement);
            root.Add(BridgeElements.Banner("YAPS", "Yet Another Penetration System · for ChilloutVR",
                "v" + BridgeDefines.Version, BridgeTheme.Span.Cvr));

            _tabs = new VisualElement();
            root.Add(_tabs);

            _pages = BridgeElements.Scroll();
            root.Add(_pages);
            ShowPage();
        }

        void ShowPage()
        {
            // Rebuilt with the mode so the active tab's tint follows.
            _tabs.Clear();
            _tabs.Add(BridgeElements.Tabs(
                new[] { "Set up an avatar or prop", "Test it" },
                // Unthemed: GetIcon adds the dark-skin prefix itself. Single-colour glyphs only:
                // the active tab's white tint cannot recolour a colour icon like "Avatar Icon".
                new[] { "Settings", "PlayButton" },
                (int) _mode, i => { _mode = (Mode) i; ShowPage(); }, BridgeTheme.Span.Cvr));
            _pages.Clear();
            if (_mode == Mode.Setup) BuildSetupPage(); else BuildTestPage();
            _pages.Add(Footer());
        }

        // Guide, bug report, Discord.
        static VisualElement Footer()
        {
            return BridgeElements.Footer(
                BridgeElements.ExternalLink("Guide", BridgeLinks.YapsHelp, "The YAPS chapter of the README."),
                BridgeElements.ExternalLink("Report an issue", BridgeLinks.OpenYapsBugReport,
                    "Opens a pre-filled GitHub issue with your versions and detected packages, marked as a YAPS tool report."),
                BridgeElements.ExternalLink("Discord", BridgeLinks.OpenDiscord,
                    $"{BridgeLinks.DiscordUser} on Discord. Best for quick questions; please use GitHub issues for " +
                    "bugs so they don't get lost."));
        }

        // --- the setup page --------------------------------------------------

        void BuildSetupPage()
        {
            // 1. Pick.
            var pick = new BridgeElements.Card("Pick your avatar or prop").Step(1, 3, BridgeTheme.Span.Cvr);
            _picker = BridgeElements.ObjectPicker<GameObject>("Avatar or prop", _target, Pick);
            _pickNote = BridgeElements.Hint(PickHint);
            pick.Body.Add(_picker);
            pick.Body.Add(_pickNote);
            _pages.Add(pick);

            // 2. What it has, and what to add.
            var have = new BridgeElements.Card("What it has, and what to add").Step(2, 3, BridgeTheme.Span.Cvr);
            _summary = BridgeElements.Hint("");
            have.Body.Add(_summary);
            _foundBody = new VisualElement();
            have.Body.Add(_foundBody);

            // What to do next, from the scan and the selection.
            _next = BridgeElements.Notice(Tone.Info, "");
            have.Body.Add(_next);

            have.Section("Add");
            _addHole = BridgeElements.Btn("Add a hole", () => AddSocket(YapsSocket.SocketKind.Hole));
            _addRing = BridgeElements.Btn("Add a ring", () => AddSocket(YapsSocket.SocketKind.Ring));
            // True enabled or not, so the disabled button still says what turns it on.
            _makePlug = BridgeElements.Btn("Make selected mesh a plug", MakePlug,
                "Select a mesh, or the bone that drives one, to make it a plug.");
            have.Body.Add(BridgeElements.ButtonRow(_addHole, _addRing, _makePlug));
            _selection = BridgeElements.Hint("");
            have.Body.Add(_selection);
            _addStatus = Status();
            have.Body.Add(_addStatus);
            _pages.Add(have);
            Selection.selectionChanged -= RefreshSelection;
            Selection.selectionChanged += RefreshSelection;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;

            // 3. Build. The label is fixed because the README and the inspectors quote it; the
            // counts go in the hint under it.
            var build = new BridgeElements.Card("Build").Step(3, 3, BridgeTheme.Span.Cvr);
            _build = new BridgeElements.PrimaryButton("Bake every plug and verify", BuildAll, BridgeTheme.BridgeTo);
            build.Body.Add(_build);
            _buildCounts = BridgeElements.Hint("Nothing to build yet.");
            build.Body.Add(_buildCounts);

            // What it did, line by line, where the button is. A summary
            // label was too easy to miss for work this large.
            _buildLog = new VisualElement();
            build.Body.Add(_buildLog);
            _pages.Add(build);

            BuildToolsCard();

            // Present, say where; absent, say where to get it.
            var also = new BridgeElements.Card("Also in this package", "the converter and the Toolkit", false).Remember("Yaps.Also");
            also.Body.Add(LinkRow(BridgeLinks.HasAvatarBridge
                    ? "AvatarBridge turns a VRChat avatar's DPS, TPS or SPS into YAPS."
                    : "AvatarBridge turns a VRChat avatar's DPS, TPS or SPS into YAPS. It is not in this project.",
                BridgeLinks.HasAvatarBridge
                    ? BridgeElements.Btn("Open AvatarBridge", () =>
                        EditorApplication.ExecuteMenuItem("Tools/Avatar Bridge/VRChat to ChilloutVR Converter"))
                    : BridgeElements.ExternalLink("Get AvatarBridge", BridgeLinks.Repo)));
            also.Body.Add(LinkRow(
                "The ChilloutVR Toolkit checks an avatar for what the game will break and fixes shaders, " +
                "visemes, audio and bounds.",
                BridgeElements.Btn("Open the Toolkit", ToolkitWindow.Open)));
            _pages.Add(also);

            Pick(_target != null ? _target : Selection.activeGameObject);
        }

        // Props, the legacy channel, the scene view and tidying: used now and then, so collapsed.
        void BuildToolsCard()
        {
            var tools = new BridgeElements.Card("Tools", "props, quiet the scene view, clean up", false).Remember("Yaps.Tools");

            // A plug or socket on its own object becomes a spawnable with a
            // pickup and a collider.
            tools.Section("Props");
            tools.Body.Add(BridgeElements.ButtonRow(
                BridgeElements.Btn("Make selected object a prop", () => MakeProp(Selection.activeGameObject, _propStatus)),
                BridgeElements.Btn("Verify prop", () =>
                {
                    var o = YapsPropBuilder.Verify(Selection.activeGameObject);
                    Say(_propStatus, o.Ok ? Tone.Good : Tone.Warn, o.Message, o.Notes);
                }, "Checks the selected prop's synced values and grab collider, and repairs what it can.")));
            tools.Body.Add(BridgeElements.Hint(
                "Select a plug or socket's top object to make it a spawnable with a pickup and collider. " +
                "Verify before uploading."));
            _propStatus = Status();
            tools.Body.Add(_propStatus);

            // No button adds the channel any more: the plug's shader no longer
            // reads it. Dropping one an older build added stays, shown only
            // while the selection carries one. Its result lands in the props
            // line above, since this section hides once the channel is gone.
            _legacy = new VisualElement();
            _legacy.Add(BridgeElements.SubHeading("Legacy"));
            _legacy.Add(BridgeElements.ButtonRow(BridgeElements.Btn("Drop the contact channel", () =>
            {
                var o = YapsPropBuilder.DropChannel(Selection.activeGameObject);
                Say(_propStatus, o.Ok ? Tone.Good : Tone.Warn, o.Message, o.Notes);
            }, "Takes an old contact channel off a prop. Plugs no longer read it; it only spends synced values.")));
            _legacy.Add(BridgeElements.Hint(
                "The selected prop carries a contact channel from an earlier build. Plugs no longer read it."));
            tools.Body.Add(_legacy);

            // One switch hides the CCK's icons while sockets are placed. Plum
            // while on, like any pressed button; the label stays put.
            tools.Section("Scene view");
            Button quiet = null;
            quiet = BridgeElements.Btn("Quiet the scene view while I work", () =>
            {
                SceneQuiet.Toggle();
                quiet.EnableInClassList("ab-on", SceneQuiet.IsQuiet);
            }, "Hides CCK icons, pointer spheres, trigger boxes, MagicaCloth wires and light icons so YAPS " +
               "gizmos show. Editor only; puts back what it found. Click again to show them.");
            quiet.EnableInClassList("ab-on", SceneQuiet.IsQuiet);
            tools.Body.Add(BridgeElements.ButtonRow(quiet));

            // What a socket or plug deleted by hand leaves behind.
            tools.Section("Tidy");
            tools.Body.Add(BridgeElements.ButtonRow(BridgeElements.Btn("Clean up leftovers", () =>
            {
                if (_target == null)
                {
                    _tidyStatus.Set("Pick an avatar or prop in step 1 first.", Tone.Warn);
                    return;
                }
                var done = YapsRemover.Sweep(_target.transform);
                _tidyStatus.Set(done.Count == 0
                    ? "Nothing left behind."
                    : $"Cleaned up {done.Count} leftover(s): " + string.Join("; ", done) + ". One undo step.",
                    Tone.Good);
                foreach (var line in done) Debug.Log("[YAPS] Cleaned up " + line);
                Rescan();
            }, "Removes what a socket or plug deleted by hand left behind: layers, parameters, " +
               "menu toggles and marker objects.")));
            tools.Body.Add(BridgeElements.Hint("A row's remove chip takes a plug or socket out in one undo step."));
            _tidyStatus = Status();
            tools.Body.Add(_tidyStatus);
            _pages.Add(tools);
        }

        // A sentence beside the one button or link it is about.
        static VisualElement LinkRow(string text, VisualElement link)
        {
            var hint = BridgeElements.Hint(text);
            hint.AddToClassList("ab-grow");
            return BridgeElements.Row(hint, link);
        }

        // A status line, hidden until an action reports.
        static BridgeElements.NoticeBox Status()
        {
            var box = BridgeElements.Notice(Tone.Info, "");
            box.Hide();
            return box;
        }

        static void Say(BridgeElements.NoticeBox box, Tone tone, string message, List<string> notes)
        {
            box?.Set(message + (notes.Count > 0 ? " " + string.Join(" ", notes) : ""), tone);
        }

        // --- the test page -----------------------------------------------------

        void BuildTestPage()
        {
            var make = new BridgeElements.Card("Drop a test socket or plug");
            make.Body.Add(BridgeElements.Hint(
                "Drop a test socket in front of the camera and every baked plug in the scene bends toward it. " +
                "Nothing here ships."));
            make.Body.Add(BridgeElements.ButtonRow(
                BridgeElements.Btn("Test hole (previews)", () => TestSocket(YapsSocket.SocketKind.Hole)),
                BridgeElements.Btn("Test ring (previews)", () => TestSocket(YapsSocket.SocketKind.Ring)),
                BridgeElements.Btn("Test plug", () => YapsNativeBuilder.BuildTestPlug())));
            make.Body.Add(BridgeElements.Hint(
                "The socket lands previewing; move it around your plug. No plug? Test plug drops a baked capsule."));
            _pages.Add(make);

            var props = new BridgeElements.Card("Props and prefabs", null, false);
            props.Section("Props");
            var status = Status();
            props.Body.Add(BridgeElements.ButtonRow(BridgeElements.Btn("Make the selected test object a prop",
                () => MakeProp(Selection.activeGameObject, status))));
            props.Body.Add(BridgeElements.Hint(
                "Select the test object's top first. It becomes a spawnable to upload and try with someone."));
            props.Body.Add(status);
            props.Section("Prefabs");
            props.Body.Add(BridgeElements.ButtonRow(
                BridgeElements.Btn("Create universal socket prefabs", YapsSocketBuilder.CreatePrefabs),
                BridgeElements.Btn("Create a ring-and-socket prop prefab", YapsSocketBuilder.CreateSocketPropPrefab),
                BridgeElements.Btn("Create a plug prop prefab", YapsSocketBuilder.CreatePlugPropPrefab)));
            props.Body.Add(BridgeElements.Hint(
                "Socket prefabs go to Assets/YAPS/Prefabs; drag one under a bone. The plug prop is a ready spawnable."));
            props.Section("After an update");
            props.Body.Add(BridgeElements.ButtonRow(
                BridgeElements.Btn("Update every YAPS shader in this project", RefreshShaders)));
            props.Body.Add(BridgeElements.Hint(
                "A prop keeps the shader it was built with. This updates every YAPS material in the project; " +
                "upload old props again after."));
            _pages.Add(props);
        }

        // Every patched material in the project, not just what a bake reaches.
        [MenuItem("Tools/YAPS/Update every YAPS shader in this project")]
        static void RefreshShaders()
        {
            string said = YapsShaderPatcher.SweepProject();
            EditorUtility.DisplayDialog("YAPS", said, "OK");
            Debug.Log("[AvatarBridge] " + said);
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
            var top = picked != null ? YapsNativeBuilder.AvatarRoot(picked.transform).gameObject : null;
            // Results about the last target say nothing about this one.
            if (top != _target)
            {
                _addStatus?.Clear();
                _propStatus?.Clear();
                _tidyStatus?.Clear();
            }
            _target = top;
            if (_picker != null && _picker.value != top) _picker.SetValueWithoutNotify(top);
            if (_pickNote != null)
            {
                // Picked its own top: the field already names it, so the hint would only repeat it.
                _pickNote.EnableInClassList("ab-hidden", top != null && top == picked);
                _pickNote.text = top == null || top == picked ? PickHint
                    : $"Listing \"{top.name}\", the {(top.GetComponent<CVRAvatar>() != null ? "avatar" : top.GetComponent<CVRSpawnable>() != null ? "prop" : "top object")} above \"{picked.name}\".";
            }
            Rescan();
        }

        const string PickHint = "Drop an avatar or prop here, or anything under it. Everything on the whole thing is listed.";

        // Non-breaking spaces, so a wrap never splits the label the user is told to look for.
        const string BakeQuoted = "\"Bake\u00A0every\u00A0plug\u00A0and\u00A0verify\"";

        // The next-step line, from the scan and the selection. It names the
        // buttons to press, so it reads as a step rather than a status.
        void SayNext()
        {
            if (_next == null) return;
            if (_target == null)
            {
                _next.Set("Drag your avatar or prop into the box above. Nothing happens until you do.", Tone.Info);
                return;
            }
            int plugs = _scan.Plugs.Count, sockets = _scan.Sockets.Count;
            bool allYaps = _scan.Plugs.All(p => p.IsYapsAlready) && _scan.Sockets.All(s => s.IsYapsAlready);
            bool anyLegacy = _scan.Plugs.Any(p => !p.IsYapsAlready) || _scan.Sockets.Any(s => !s.IsYapsAlready);
            bool anyIssue = _scan.Plugs.Any(p => p.Notes.Count > 0) || _scan.Sockets.Any(s => s.Notes.Count > 0);

            // The summary is hidden when the scan is empty, so this carries the answer first.
            if (plugs + sockets == 0)
            {
                _next.Set("Nothing on it yet. Select a mesh or bone, press a button under Add, then " +
                          BakeQuoted + " in step 3.", Tone.Info);
            }
            else if (anyLegacy)
            {
                _next.Set("Some DPS, TPS or SPS here is not YAPS yet. Press \"upgrade to YAPS\" on a row, or " +
                          BakeQuoted + " in step 3 for all of it. Check a skinned plug's " +
                          "\"Root Bone\" first.", Tone.Warn);
            }
            else if (anyIssue)
            {
                _next.Set("All YAPS, but the rows marked as warnings are missing something. " + BakeQuoted +
                          " in step 3 fixes markers and bakes; turn a socket with no axis so its arrow points in.",
                          Tone.Warn);
            }
            else if (allYaps)
            {
                int bare = _scan.Plugs.Concat(_scan.Sockets).Count(f => f.Root != null
                    && f.Root.GetComponent<YapsSocket>() == null && f.Root.GetComponent<YapsPlug>() == null);
                _next.Set(bare > 0
                    ? $"All YAPS, but {bare} came from a conversion with nothing to edit. Press \"make editable\" " +
                      "on a row, or " + BakeQuoted + " in step 3 for all."
                    : "All YAPS and editable. Press \"customise\" on a row to retune it in the Inspector, then " +
                      BakeQuoted + " in step 3.", Tone.Info);
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
            _legacy?.EnableInClassList("ab-hidden", !YapsPropBuilder.HasChannel(Selection.activeGameObject));
            if (_selection == null) return;
            var go = Candidate();
            if (go == null)
            {
                _selection.text = "Select a bone for a socket, or a mesh or its bone for a plug. With nothing selected, a socket goes in a YAPS folder.";
                if (_addHole != null) _addHole.text = "Add a hole";
                if (_addRing != null) _addRing.text = "Add a ring";
                if (_makePlug != null) { _makePlug.text = "Make selected mesh a plug"; _makePlug.SetEnabled(false); }
                return;
            }
            var root = YapsSocketEditor.AvatarRootOf(go.transform);
            bool bone = root != null && go.transform != root && IsBone(go.transform, root);
            bool mesh = go.GetComponent<Renderer>() != null;
            _selection.text = bone
                ? $"Bone \"{go.name}\": a socket goes under it; a plug bakes the mesh it drives."
                : mesh ? $"Mesh \"{go.name}\": Make a plug bakes this one."
                : $"\"{go.name}\" is not a bone or mesh: a socket goes in the YAPS folder.";
            if (_addHole != null) _addHole.text = bone ? $"Add a hole under {go.name}" : "Add a hole";
            if (_addRing != null) _addRing.text = bone ? $"Add a ring under {go.name}" : "Add a ring";
            if (_makePlug != null)
            {
                _makePlug.text = mesh ? $"Make \"{go.name}\" a plug" : bone ? $"Make a plug from bone {go.name}" : "Make selected mesh a plug";
                _makePlug.SetEnabled(mesh || bone);
            }
        }

        // From a row's own chip: the list it sits in is about to be cleared.
        void RescanLater() => BridgeElements.Defer(_foundBody, Rescan);

        void Rescan()
        {
            if (_foundBody == null) return;
            _foundBody.Clear();
            RefreshSelection();
            if (_target == null)
            {
                // The notice below says it, so the faint line would only repeat it.
                _summary.AddToClassList("ab-hidden");
                _build?.SetActive(false);
                if (_buildCounts != null) _buildCounts.text = "Nothing to build yet.";
                SayNext();
                return;
            }
            _scan = YapsScanner.Scan(_target);
            _summary.text = _scan.Summary();
            // Empty, the notice below opens with the answer instead.
            _summary.EnableInClassList("ab-hidden", _scan.Total == 0);
            _build?.SetActive(_scan.Total > 0);
            // Carried meshes are not plugs to bake: their carrier bakes them.
            // Counting them promises a number the build will not do.
            int bakeable = _scan.Plugs.Count(p => p.CarriedBy == null);
            if (_buildCounts != null)
            {
                _buildCounts.text = _scan.Total > 0
                    ? $"{bakeable} plug{(bakeable == 1 ? "" : "s")} to bake and {_scan.Sockets.Count} socket{(_scan.Sockets.Count == 1 ? "" : "s")} to verify. Safe to run again."
                    : "Nothing to build yet: add a hole or ring, or make a plug, in step 2.";
            }
            SayNext();

            var rows = new List<VisualElement>();
            void Row(YapsScanner.Found f)
            {
                // Two sockets within three centimetres is one too many.
                // Said on both rows.
                if (f.Kind == YapsScanner.Kind.Socket && f.Root != null)
                {
                    var twins = _scan.Sockets.Where(o => o != f && o.Root != null
                        && Vector3.Distance(o.Root.position, f.Root.position) < 0.03f)
                        .Select(o => o.Name).ToList();
                    if (twins.Count > 0) f.Notes.Add("on the same spot as " + string.Join(", ", twins) + ", one too many?");
                }
                // A mesh another plug carries is not a plug. It gets a row, since it IS
                // being changed and a change nobody can see is the one people add a
                // second plug to fix, but a nested quiet one that says whose it is and
                // offers none of the controls that would make it a peer.
                if (f.CarriedBy != null)
                {
                    var carried = BridgeElements.ReportRow(Tone.Muted, "Part of: " + f.Name,
                        $"carried by \"{YapsToggles.LabelFor(f.CarriedBy)}\": bends with it, on its settings.");
                    carried.AddToClassList("ab-report-item-child");
                    carried.AddToClassList("ab-dim");
                    SelectOnClick(carried, f);
                    rows.Add(carried);
                    return;
                }

                // Nothing to fix but something to say stays green: working as
                // designed must never wear the colour that means "you have a problem".
                var tone = f.Notes.Count > 0 ? Tone.Warn : f.IsYapsAlready ? Tone.Good : Tone.Info;
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
                var captured = f;
                var actions = new List<VisualElement>();

                if (sc != null || pc != null)
                {
                    actions.Add(BridgeElements.Chip("customise", Tone.Info, false, () =>
                    {
                        if (captured.Root == null) { RescanLater(); return; }
                        Selection.activeTransform = captured.Root;
                        EditorGUIUtility.PingObject(captured.Root);
                        // Front the Inspector, opening one if there is none.
                        EditorApplication.ExecuteMenuItem("Window/General/Inspector");
                    }, "Selects it and opens its Inspector."));
                    // Remove: out entire, after a dialog saying what goes.
                    actions.Add(BridgeElements.Chip("remove", Tone.Bad, false, () =>
                    {
                        if (captured.Root == null) { RescanLater(); return; }
                        var s = captured.Root.GetComponent<YapsSocket>();
                        var p = captured.Root.GetComponent<YapsPlug>();
                        bool did = s != null ? YapsRemover.Ask(s) : YapsRemover.Ask(p);
                        if (did) RescanLater();
                    }, "Takes it out entire, after a dialog saying what goes. One undo step."));
                }
                if (sc != null)
                {
                    actions.Add(BridgeElements.Chip(sc.preview ? "previewing" : "preview", Tone.Good, sc.preview, () =>
                    {
                        // A reconvert can leave the chip holding a dead component.
                        if (sc == null) { RescanLater(); return; }
                        YapsPreview.Set(sc, !sc.preview);
                        RescanLater();
                    }, sc.preview ? "Previewing. Click again to stop." : "Bends baked plugs toward it in the scene view."));
                }
                else if (pc != null)
                {
                    // The plug's half of the same idea. A socket previews by dropping a
                    // plug in front of it; a plug had no row chip at all, because there was
                    // nothing for it to bend toward until the test socket existed.
                    bool testing = YapsPreview.TestSocketInScene;
                    actions.Add(BridgeElements.Chip(testing ? "previewing" : "preview", Tone.Good, testing, () =>
                    {
                        if (captured.Root == null) { RescanLater(); return; }
                        if (YapsPreview.TestSocketInScene) YapsPreview.RemoveTestSocket();
                        else YapsPreview.DropTestSocket(pc);
                        RescanLater();
                    }, testing ? "Click again to take the test socket away." : "Drops a test socket ahead and bends this plug into it."));
                }
                else if (f.Root != null)
                {
                    // No component yet. YAPS output adopts; DPS, TPS or SPS
                    // upgrades in place: adopt, then build or bake.
                    bool legacy = !f.IsYapsAlready;
                    actions.Add(BridgeElements.Chip(legacy ? "upgrade to YAPS" : "make editable", Tone.Warn, false, () =>
                    {
                        if (captured.Root == null) { RescanLater(); return; }
                        Undo.RegisterFullObjectHierarchyUndo(captured.Root.gameObject, "Adopt YAPS " + (captured.Kind == YapsScanner.Kind.Plug ? "plug" : "socket"));
                        Adopt(captured);
                        if (legacy) Upgrade(captured);
                        RescanLater();
                    }, legacy ? "Makes it YAPS in place, keeping the author's values." : "Adds the component that makes it editable here."));
                }

                var row = BridgeElements.ReportRow(tone, what + ": " + title, string.Join(" · ", detail), actions.ToArray());
                SelectOnClick(row, f);
                rows.Add(row);
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
            if (rows.Count > 0) _foundBody.Add(BridgeElements.ReportList(rows));
        }

        // A row selects what it lists. Its chips are Buttons with their own job.
        static void SelectOnClick(VisualElement row, YapsScanner.Found f)
        {
            row.RegisterCallback<ClickEvent>(e =>
            {
                if (e.target is Button || f.Root == null) return;
                Selection.activeTransform = f.Root;
                EditorGUIUtility.PingObject(f.Root);
            });
        }

        // Puts the authoring component on a found plug or socket that has none.
        static void Adopt(YapsScanner.Found f)
        {
            if (f.Root == null) return;
            // Never a mesh another plug carries. It wears a patched material, so it
            // reads as a plug, and adopting it hands it a component, which is a
            // claim on the mesh, which makes the carrier let go of it, which leaves
            // it bending on its own frame. That is Build re-creating the exact
            // component the user just deleted, every time they press it.
            if (f.CarriedBy != null) return;
            if (f.Kind == YapsScanner.Kind.Socket)
            {
                // Shape names are not on the material, so the user fills the
                // rows. An empty list, not null: it still carries the power over.
                YapsNativeBuilder.AdoptSocket(f.Root, f.Renderer, f.Material, new string[0]);
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
                // The same door the inspector's Bake goes through, menu and channel
                // included. Bake alone left the channel holding the frames of a
                // previous build and the menu animator unrefreshed, so one plug came
                // out differently depending on which button was pressed. BuildAll below
                // does those two once for the whole avatar, which is why it can call
                // the bare Bake.
                var o = YapsNativeBuilder.BakeAndRefreshMenu(plug);
                if (!o.Ok) Debug.LogError("[YAPS] " + o.Message);
                Say(_addStatus, o.Ok ? Tone.Good : Tone.Bad, o.Message, o.Notes);
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
                        _addStatus.Set($"No skinned mesh has vertices weighted to \"{go.name}\" or the bones under it. " +
                                       "Select the plug mesh itself instead, or the bone its shaft is actually skinned to.",
                                       Tone.Warn);
                        return;
                    }
                }
            }
            if (renderer == null)
            {
                _addStatus.Set("Select the mesh that should bend, or the bone the shaft grows from, in the Hierarchy, then press this.",
                               Tone.Warn);
                return;
            }
            var plug = go.GetComponent<YapsPlug>();
            if (plug == null)
            {
                plug = Undo.AddComponent<YapsPlug>(go);
                plug.renderer = renderer;
                plug.rootBone = rootBone;
                // Left on auto. Pinning it to the best-weighted slot here reads as
                // helpful and is not: an explicit slot means "this one only", so a plug
                // whose vertices span several materials silently bakes into one and
                // tears along the seam. The bake finds every slot the chain reaches; a
                // number is the author overriding that, not the tool guessing.
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
            if (_target == null) Pick(YapsSocketEditor.AvatarRootOf(go.transform).gameObject);
            Rescan();
            Say(_addStatus, o.Ok ? Tone.Good : Tone.Bad, o.Message, o.Notes);
        }

        void MakeProp(GameObject root, BridgeElements.NoticeBox status)
        {
            var o = YapsPropBuilder.MakeProp(root);
            if (!o.Ok) Debug.LogError("[YAPS] " + o.Message);
            if (o.Ok && _target == null) Pick(root);
            Rescan();
            Say(status, o.Ok ? Tone.Good : Tone.Warn, o.Message, o.Notes);
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
            string menu = YapsToggles.RefreshMenuAnimator(_target.GetComponentInChildren<CVRAvatar>(true), edits);
            if (menu != null) lines.Add(menu);
            foreach (var s in _target.GetComponentsInChildren<YapsSocket>(true))
            {
                socketsBuilt++;
                lines.AddRange(YapsNativeBuilder.BuildSocket(s));
            }
            // The writers go on the sockets; the surface they publish to goes on
            // the avatar. Only the converter used to add it, so a hand-built avatar
            // had sockets writing to a screen nothing grabbed.
            if (YapsAtlas.AddClear(_target.transform) != null
                && YapsAtlas.AddGrab(_target.transform) != null)
            {
                lines.Add("Added the screen surface plugs read each other through.");
            }

            // Last, and once: the channel reads the frames the bakes just
            // measured, and it replaces its own wiring rather than stacking.
            lines.AddRange(YapsNativeChannel.Build(_target.GetComponentInChildren<CVRAvatar>(true)));
            Rescan();
            string headline = $"Built {plugsOk} of {plugsTried} plug{(plugsTried == 1 ? "" : "s")} and " +
                              $"{socketsBuilt} socket{(socketsBuilt == 1 ? "" : "s")}.";
            ShowBuildLog(headline, lines);
        }

        // The build's own report, under its button, and in the console for
        // a bug report to quote.
        void ShowBuildLog(string headline, List<string> lines)
        {
            if (_buildLog == null) return;
            _buildLog.Clear();
            _buildLog.Add(BridgeElements.SubHeading("What Build did"));
            _buildLog.Add(BridgeElements.Notice(lines.Any(l => l.StartsWith("✗")) ? Tone.Warn : Tone.Good,
                headline + (lines.Count == 0
                    ? " Nothing needed doing."
                    : $" {lines.Count} note(s) below, and the same lines are in the Console.")));

            var rows = new List<VisualElement>();
            foreach (var line in lines)
            {
                // An unmarked line is a note, never a success.
                var tone = line.StartsWith("✗") ? Tone.Bad
                         : line.Contains("⚠") ? Tone.Warn
                         : line.StartsWith("✓") ? Tone.Good
                         : Tone.Muted;
                string text = line.TrimStart('✓', '✗', '⚠', ' ');
                int cut = text.IndexOf(':');
                string what = cut > 0 && cut < 40 ? text.Substring(0, cut) : "Build";
                string rest = cut > 0 && cut < 40 ? text.Substring(cut + 1).Trim() : text;
                rows.Add(BridgeElements.ReportRow(tone, tone == Tone.Bad ? "Failed: " + what : what, rest));
                Debug.Log("[YAPS] " + line);
            }
            if (rows.Count > 0) _buildLog.Add(BridgeElements.ReportList(rows));

            // Where the files went, and a way to get there.
            string dir = YapsNativeBuilder.OutputRoot + "/" + (_target != null ? _target.name : "");
            _buildLog.Add(BridgeElements.Hint("Generated materials, bakes and clips are in " + dir + "."));
            _buildLog.Add(BridgeElements.ButtonRow(BridgeElements.Btn("Show me the files", () =>
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
