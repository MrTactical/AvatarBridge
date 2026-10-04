// Tools > Avatar Bridge > ChilloutVR Toolkit. Standalone utilities for
// any ChilloutVR avatar or prop, each a card: pick, run, read the rows.
// The same passes the converter runs, on the converter's own elements.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge
{
    // The Toolkit's cards, as a panel rather than a window, so the main
    // window can host the same ones as a tab. Four windows all shaped "pick
    // an avatar, choose something, press a button" was the arrangement
    // nobody could navigate; the cards were never the problem.
    public sealed class ToolkitPanel
    {
        public ToolkitPanel(GameObject target = null, bool embedded = false, GameObject convertedFrom = null,
            System.Action<GameObject> onTargetChanged = null)
        {
            _target = target;
            _embedded = embedded;
            _convertedFrom = convertedFrom;
            _onTargetChanged = onTargetChanged;
        }

        readonly bool _embedded;

        // The panel is rebuilt from scratch on every reload or tab switch, so
        // the host keeps the pick or it falls back to whatever was handed over.
        readonly System.Action<GameObject> _onTargetChanged;

        // The avatar this one was converted from, when the window handed it
        // over. Conversion copies the materials it patches, so the source
        // keeps its own set pointing at the same textures; treating those as
        // a stranger's is what kept a converted avatar's eyes at full size.
        // Cleared once another target is picked: to that one the source IS a
        // stranger, and its shared textures would shrink along with it.
        GameObject _convertedFrom;

        // The last rows each card showed. Fix it rebuilds every card, since
        // sizes changed under them, and without this the rebuild threw away
        // the very report it had just written.
        readonly Dictionary<string, BridgeReport> _shown = new Dictionary<string, BridgeReport>();

        // Mounts into a container the host owns. The banner belongs to the
        // window, not to the panel: a tab already sits under one.
        public VisualElement Mount(VisualElement parent)
        {
            _pages = new ScrollView();
            _pages.AddToClassList("ab-scroll");
            parent.Add(_pages);
            Build();
            return _pages;
        }

        const string OutputRoot = "Assets/AvatarBridgeOutput";

        GameObject _target;
        AnimatorController _mergeTarget;
        readonly List<AnimatorController> _mergeSources = new List<AnimatorController>();
        bool _mergeIntoCopy = true;
        VisualElement _pages;

        void Build()
        {
            _pages.Clear();

            // No step numbers here. The converter and the setup flow ARE
            // sequences; this is a menu, and numbering it says you are not
            // finished until you have merged some animators.
            var pick = new BridgeElements.Card("Pick your avatar or prop", null, null, null, 0f);
            var picker = new ObjectField("Avatar or prop") { objectType = typeof(GameObject), allowSceneObjects = true, value = _target };
            picker.RegisterValueChangedCallback(e =>
            {
                _target = e.newValue as GameObject;
                _convertedFrom = null;
                _shown.Clear();
                _onTargetChanged?.Invoke(_target);
                Build();
            });
            pick.Body.Add(picker);
            if (_target == null) pick.Body.Add(BridgeElements.Hint("Drag the avatar or prop here from the Hierarchy. Every card below acts on it."));
            // By name, as the link below is: the tester is the one window here that can be absent.
            var tester = typeof(ToolkitPanel).Assembly.GetType("AvatarBridge.CckAnimatorTester", false);
            var avatarHere = _target != null ? _target.GetComponent<CVRAvatar>() : null;
            if (tester != null && avatarHere != null)
            {
                var open = new Button(() => tester.GetMethod("OpenFor", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, new object[] { avatarHere }))
                {
                    text = "Open in the CCK Animator Tester",
                    tooltip = "Opens the tester already pointed at this avatar: gestures, menu, face, physics, played the way the game does.",
                };
                open.style.marginTop = 4;
                pick.Body.Add(BridgeElements.Row(open));
            }
            _pages.Add(pick);

            var tools = new BridgeElements.Card("Tools", null, null, null, 0.5f);
            tools.Body.Add(Check());
            tools.Body.Add(Survey());
            tools.Body.Add(Weigh());
            tools.Body.Add(Tidy());
            tools.Body.Add(Stereo());
            tools.Body.Add(Face());
            tools.Body.Add(Audio());
            tools.Body.Add(Bounds());
            tools.Body.Add(Height());
            tools.Body.Add(Description());
            _pages.Add(tools);

            _pages.Add(MergeAnimators());

            // Mounted as a tab of the converter's own window, "AvatarBridge"
            // would be a link to where you already are. Each link only where
            // its menu exists: the public package has no YAPS, and the add-on
            // has neither the converter nor the tester, and a dead menu item
            // does nothing but log an error.
            var more = new BridgeElements.Card("Also in this package", null, false, null, 1f);
            var said = new List<string>();
            var links = new List<VisualElement>();
            if (!_embedded && BridgeLinks.HasAvatarBridge)
            {
                said.Add("AvatarBridge converts VRChat avatars.");
                links.Add(BridgeElements.Link("AvatarBridge",
                    () => EditorApplication.ExecuteMenuItem("Tools/Avatar Bridge/VRChat to ChilloutVR Converter")));
            }
            if (BridgeDefines.HasYaps)
            {
                said.Add("YAPS adds penetration to any avatar or prop.");
                links.Add(BridgeElements.Link("YAPS", () => EditorApplication.ExecuteMenuItem("Tools/YAPS/Setup")));
            }
            else
            {
                said.Add("YAPS, a separate 18+ add-on, adds penetration to any avatar or prop.");
                links.Add(BridgeElements.Link("Get the YAPS add-on (GitHub)  ↗", () => Application.OpenURL(BridgeLinks.YapsRepo)));
            }
            if (typeof(ToolkitPanel).Assembly.GetType("AvatarBridge.CckAnimatorTester", false) != null)
            {
                said.Add("CCK Animator Tester plays an avatar as the game does.");
                links.Add(BridgeElements.Link("CCK Animator Tester",
                    () => EditorApplication.ExecuteMenuItem("Tools/Avatar Bridge/CCK Animator Tester")));
            }
            more.Body.Add(BridgeElements.Hint(string.Join(" ", said)));
            more.Body.Add(BridgeElements.Row(links.ToArray()));
            _pages.Add(more);

            var footer = new VisualElement();
            footer.AddToClassList("ab-footer");
            footer.Add(BridgeElements.Link("Guide  ↗", () => Application.OpenURL(BridgeLinks.Repo)));
            footer.Add(BridgeElements.Link("Report an issue  ↗", () => BridgeLinks.OpenBugReport()));
            _pages.Add(footer);
        }

        // --- the cards ---------------------------------------------------------

        // A context for one target: what the converter passes read.
        BridgeContext Context(BridgeReport report)
        {
            var animator = _target != null ? _target.GetComponent<Animator>() : null;
            var avatar = _target != null ? _target.GetComponent<CVRAvatar>() : null;
            // The controller ChilloutVR uploads, as YAPS reads it. The
            // Animator's slot first skipped every animator check on an avatar
            // never built, where that slot is often empty.
            var controller = AvatarSurvey.ShippedController(avatar, animator);
            return new BridgeContext
            {
                Settings = new BridgeSettings(),
                Report = report,
                Target = _target,
                CvrAvatar = avatar,
                MergedController = controller,
                OutputDir = AvatarFolder(),
                Standalone = true,
            };
        }

        // The converter's folder for this avatar, so a resize it recorded is
        // found here. Found, never created: the cards that only read use it.
        string AvatarFolder()
        {
            string root = SavedOutputFolder();
            if (_target == null) return root;
            // The converter and Setup save the controller into that folder.
            // A converted copy is only found this way: the folder is tagged
            // with the source object, and the copy is another object.
            var avatar = _target.GetComponent<CVRAvatar>();
            if (avatar != null)
            {
                foreach (var c in new RuntimeAnimatorController[] { avatar.overrides, avatar.avatarSettings?.baseController })
                {
                    string path = c != null ? AssetDatabase.GetAssetPath(c) : "";
                    int cut = path.LastIndexOf('/');
                    if (cut < 0) continue;
                    string dir = path.Substring(0, cut);
                    // Untagged counts only under the default root, where it is
                    // an older version's output. A chosen root like "Assets"
                    // has shared folders one level down, and an avatar taking
                    // one would share its resize record with every avatar there.
                    bool underRoot = string.Equals(root, OutputRoot, System.StringComparison.OrdinalIgnoreCase)
                                     && dir.StartsWith(root + "/", System.StringComparison.OrdinalIgnoreCase)
                                     && dir.IndexOf('/', root.Length + 1) < 0;
                    if (underRoot || CvrSetup.OwnerOf(dir) != null) return dir;
                }
            }
            return CvrSetup.FindOutputDir(NamedFolder(out var source), source);
        }

        // The folder the converter names for the source, before any number:
        // a converted copy is "<source> (ChilloutVR)".
        string NamedFolder(out GameObject source)
        {
            source = _convertedFrom != null ? _convertedFrom : _target;
            string name = source.name;
            const string Suffix = " (ChilloutVR)";
            if (name.EndsWith(Suffix, System.StringComparison.Ordinal)) name = name.Substring(0, name.Length - Suffix.Length);
            return SavedOutputFolder() + "/" + CvrSetup.SafeFolderName(name);
        }

        // Where a card writes. A folder the lookup found is used as it is.
        // A new one is made and tagged the converter's way, or the next
        // lookup reads it as another avatar's and loses the record in it.
        // The unnumbered name goes in so a numbered folder says why.
        string WriteFolder(BridgeReport report)
        {
            string folder = AvatarFolder();
            if (_target == null || AssetDatabase.IsValidFolder(folder)) return folder;
            return CvrSetup.CreateOutputDir(NamedFolder(out var source), source, report, "Toolkit");
        }

        // Where a resize record waits. Before the Toolkit followed the
        // converter it wrote under the raw object name, and a record left there
        // is still somebody's way back. That one first: Fix it ran after the
        // conversion, so undoing it first puts sizes back in the order they changed.
        string RecordFolder()
        {
            string folder = AvatarFolder();
            if (_target == null || _target.name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) return folder;
            string legacy = OutputRoot + "/" + _target.name;
            return AvatarSlimmer.CanRevert(legacy) ? legacy : folder;
        }

        // The converter window's Output folder, kept in its saved settings
        // (CvrSetup.SettingsPrefsKey), and refused the way the converter
        // refuses it. The default where that window never ran.
        static string SavedOutputFolder()
        {
            var saved = new BridgeSettings();
            try { JsonUtility.FromJsonOverwrite(EditorPrefs.GetString(CvrSetup.SettingsPrefsKey, "{}"), saved); }
            catch (System.Exception) { return OutputRoot; }
            return CvrSetup.CheckedOutputFolder(saved.outputFolder) ?? OutputRoot;
        }

        VisualElement Tool(string title, string blurb, string button, System.Func<BridgeReport> run, string whenEmpty,
            string second = null, System.Func<BridgeReport> runSecond = null,
            string third = null, System.Func<BridgeReport> runThird = null,
            VisualElement option = null)
        {
            var box = new VisualElement();
            box.Add(BridgeElements.SubHeading(title));
            box.Add(BridgeElements.Hint(blurb));
            if (option != null) box.Add(option);
            var rows = new VisualElement();
            if (_shown.TryGetValue(title, out var last)) ShowReport(rows, last, whenEmpty);
            var b = new Button(() =>
            {
                if (_target == null) return;
                ShowReport(rows, _shown[title] = run(), whenEmpty);
            }) { text = button };
            b.AddToClassList("ab-btn");
            b.SetEnabled(_target != null);

            var buttons = new List<VisualElement> { b };
            foreach (var extra in new[] { (second, runSecond), (third, runThird) })
            {
                if (extra.Item1 == null) continue;
                var action = extra.Item2;
                var more = new Button(() =>
                {
                    if (_target == null) return;
                    _shown[title] = action();
                    Build();   // sizes changed, so the reading behind it did too; the rebuild shows the rows
                }) { text = extra.Item1 };
                more.AddToClassList("ab-btn");
                more.SetEnabled(_target != null);
                buttons.Add(more);
            }
            box.Add(BridgeElements.Row(buttons.ToArray()));
            box.Add(rows);
            return box;
        }

        VisualElement Check() => Tool("Check this avatar",
            "What ChilloutVR will break without saying: stripped components, the sync budget, unwired " +
            "parameters, one-eyed shaders, rootless cloth. Reads only.",
            "Check", () =>
            {
                var report = new BridgeReport();
                var ctx = Context(report);
                if (ctx.MergedController != null) BridgeDiagnostics.Run(ctx, ctx.MergedController);
                else
                {
                    BridgeDiagnostics.CheckComponentWhitelist(ctx);
                    BridgeDiagnostics.CheckStereoShaders(ctx);
                    report.Approximated("Diagnostics", "No animator controller on the root", "The animator checks were skipped.");
                }
                return report;
            }, "Nothing to report. That is the good outcome.");

        VisualElement Survey() => Tool("What this avatar does",
            "Names features never wired up, layers fighting over the same thing, menu controls nothing " +
            "reads, and objects that could be props. Reads only.",
            "Survey it", () =>
            {
                var report = new BridgeReport();
                var ctx = Context(report);
                if (ctx.CvrAvatar == null)
                {
                    report.Approximated("Survey", "No CVRAvatar on the root", "Add one and this can read it.");
                    return report;
                }
                AvatarSurvey.Fill(report, AvatarSurvey.Build(ctx.CvrAvatar));
                return report;
            }, "Nothing worth naming: everything it has is reachable and nothing collides.");

        VisualElement Weigh() => Tool("What this avatar costs",
            "Texture memory against the surface each map covers, contacts, triangles, cloth, unused " +
            "blendshapes and locked shaders. Weigh reads; Fix changes.",
            "Weigh it", () =>
            {
                var report = new BridgeReport();
                var ctx = Context(report);
                if (ctx.CvrAvatar == null)
                {
                    report.Approximated("Weight", "No CVRAvatar on the root",
                        "Add one, or use the converter, and this can measure what the game will be charged.");
                    return report;
                }
                var read = AvatarSurvey.Build(ctx.CvrAvatar);
                var measured = AvatarWeight.Measure(ctx.CvrAvatar, read);
                // The plan as well, so the card can say which advice Fix it
                // has already refused rather than repeating it every time.
                var would = AvatarSlimmer.Find(ctx.CvrAvatar, read, measured, _convertedFrom, true, _stripHidden);
                AvatarWeight.NoteLeftAlone(measured, would.Shared);
                AvatarWeight.Fill(report, measured);
                return report;
            }, "Nothing on it is worth changing.",
            "Fix it",
            () =>
            {
                var report = new BridgeReport();
                var ctx = Context(report);
                if (ctx.CvrAvatar == null)
                {
                    report.Approximated("Slim down", "No CVRAvatar on the root", "Add one and this can act.");
                    return report;
                }
                var survey = AvatarSurvey.Build(ctx.CvrAvatar);
                var weight = AvatarWeight.Measure(ctx.CvrAvatar, survey);
                var plan = AvatarSlimmer.Find(ctx.CvrAvatar, survey, weight, _convertedFrom, true, _stripHidden);
                // No folder made for a plan that writes nothing into it.
                AvatarSlimmer.Apply(ctx.CvrAvatar, plan, plan.Any ? WriteFolder(report) : ctx.OutputDir, report);
                return report;
            },
            // Its own button, not a mode the other one falls into. Import
            // settings outlive the conversion that changed them, so a record
            // can be waiting from a run days ago.
            _target != null && AvatarSlimmer.CanRevert(RecordFolder()) ? "Put the textures back" : null,
            () =>
            {
                var report = new BridgeReport();
                AvatarSlimmer.Revert(RecordFolder(), report);
                return report;
            },
            StripOption());

        // Editing someone's own avatar, not a converted copy, so this one
        // is worth being able to refuse. The renderer goes and the object
        // stays; Ctrl+Z brings it back either way.
        bool _stripHidden = true;

        VisualElement StripOption()
        {
            var toggle = new Toggle("Also remove meshes nothing can show") { value = _stripHidden };
            toggle.RegisterValueChangedCallback(e => _stripHidden = e.newValue);
            toggle.AddToClassList("ab-keep");
            var wrap = new VisualElement();
            wrap.Add(toggle);
            wrap.Add(BridgeElements.Hint(
                "A mesh that starts off and nothing turns on is downloaded and never seen."));
            return wrap;
        }

        VisualElement Tidy() => Tool("Free wins",
            "Removes empty layers and parameters nothing reads or writes, into a copy of the controller.",
            "Tidy it", () =>
            {
                var report = new BridgeReport();
                var ctx = Context(report);
                if (ctx.CvrAvatar == null)
                {
                    report.Approximated("Free wins", "No CVRAvatar on the root", "Add one and this can read it.");
                    return report;
                }
                var plan = FreeWins.Find(ctx.CvrAvatar, AvatarSurvey.Build(ctx.CvrAvatar));
                if (!plan.Any)
                {
                    FreeWins.Fill(report, plan, 0, 0, null);
                    return report;
                }
                FreeWins.Apply(ctx.CvrAvatar, plan,
                    AssetDatabase.GenerateUniqueAssetPath(WriteFolder(report) + "/" + CvrSetup.SafeFolderName(_target.name) + " tidied.controller"),
                    report);
                return report;
            }, "Nothing on it is provably inert. That is the good outcome.");

        VisualElement Stereo() => Tool("Stereo shaders",
            "A shader without stereo support draws into one eye in VR. Patches copies and points the materials " +
            "at them; materials an animation swaps in are left alone.",
            "Patch shaders for VR stereo", () =>
            {
                var report = new BridgeReport();
                Undo.RegisterFullObjectHierarchyUndo(_target, "Patch stereo shaders");
                // No controller: the clip pass would rewrite the user's own
                // clips in place. Renderers only.
                string folder = AvatarFolder();
                bool existed = System.IO.Directory.Exists(folder);
                // No OutputAssetPaths.Reset: Claim deletes whatever sits on a
                // path unclaimed this run, and this folder is shared with the
                // conversion, so the avatar may still wear the copy there.
                // A numbered copy after Undo is the safe cost.
                ShaderSpiPatcher.Patch(_target, folder + "/RehomedAssets", null, report);
                // Patch makes the folder only if it writes. Tagged after, so
                // a run that changed nothing leaves no folder claiming the name.
                if (!existed && System.IO.Directory.Exists(folder))
                    CvrSetup.CreateOutputDir(NamedFolder(out var source), source, report, "Toolkit", folder);
                return report;
            }, "Every shader on it already declares stereo support, or has no source to patch.");

        VisualElement Face() => Tool("Face: visemes and blink",
            "Wires the face mesh's viseme and blink shapes onto the CVRAvatar.",
            "Wire face", () => CvrSetup.WireFace(_target, new BridgeSettings()),
            "Nothing found to wire.");

        VisualElement Audio() => Tool("Audio limits",
            "Clamps every audio source to safe spatial settings. One with a zero minimum distance can mute the game.",
            "Clamp audio sources", () =>
            {
                var report = new BridgeReport();
                Undo.RegisterFullObjectHierarchyUndo(_target, "Clamp audio");
                AvatarHygiene.SanitizeAudioSources(Context(report));
                return report;
            }, "No audio source needed clamping.");

        VisualElement Bounds() => Tool("Mesh bounds",
            "Fits every skinned mesh's bounds to the avatar, so meshes stop vanishing at the screen's edge.",
            "Fix mesh bounds", () =>
            {
                var report = new BridgeReport();
                Undo.RegisterFullObjectHierarchyUndo(_target, "Fix mesh bounds");
                AvatarHygiene.NormalizeSkinnedBounds(Context(report));
                return report;
            }, "Bounds were already right.");

        VisualElement Height() => Tool("Height slider",
            "Adds a Height slider, 0.25x to 4x, to the menu. Edits the avatar's controller asset.",
            "Add height slider", () =>
            {
                var report = new BridgeReport();
                var ctx = Context(report);
                if (ctx.MergedController == null) { report.Error("Scaler", "No animator controller on the root"); return report; }
                if (ctx.CvrAvatar == null) { report.Error("Scaler", "No CVRAvatar on the root"); return report; }
                var controller = ctx.MergedController;
                // A never-built avatar's overrides usually wrap the CCK's stock
                // controller, shared by every avatar in the project and replaced
                // by the next CCK update. Reading it is fine; writing is not.
                if (CvrSetup.SharedController(controller))
                {
                    report.Error("Scaler", "This avatar runs the CCK's own controller",
                        "Layers added there would reach every avatar in the project, and a CCK update would erase " +
                        "them. Give the avatar its own controller first (the CVRAvatar's Advanced Avatar Settings " +
                        "create one), then press again.");
                    return report;
                }
                if (controller.layers.Any(l => l.name == "Size" || l.name.StartsWith("Size ", System.StringComparison.Ordinal)))
                {
                    report.Approximated("Scaler", "Height slider already there",
                        "The controller already carries a Size layer. Nothing was added twice.");
                    return report;
                }
                Undo.RegisterFullObjectHierarchyUndo(_target, "Height slider");
                // The layers and parameters go into the controller asset, which
                // the hierarchy record does not cover. Without this, Ctrl+Z took
                // the menu entry and left Size layers the check above then
                // refused to add again.
                Undo.RegisterCompleteObjectUndo(controller, "Height slider");
                ctx.Settings.addAvatarScaler = true;
                int before = controller.layers.Length;
                AvatarScalerInjector.Inject(controller, ctx);
                // Inject builds its layers in memory. On a persistent
                // controller they must be embedded or they serialize empty.
                var layers = controller.layers;
                for (int i = before; i < layers.Length; i++)
                {
                    AnimatorAssetSaver.EmbedLayer(layers[i], controller);
                }
                EditorUtility.SetDirty(ctx.MergedController);
                EditorUtility.SetDirty(ctx.CvrAvatar);
                AssetDatabase.SaveAssets();
                return report;
            }, "Nothing added.");

        VisualElement Description() => Tool("Store description",
            "Writes a 256-character description of the avatar into the upload page when its box is empty, and copies it.",
            "Write description", () =>
            {
                var report = new BridgeReport();
                var ctx = Context(report);
                string text = AvatarDescription.Build(ctx);
                // Never over the user's own words: an empty box is an
                // invitation, a full one a decision. The text is still copied.
                var result = CckDescriptionFiller.Fill(text);
                report.Converted("Store", "Description", text);
                if (result == CckDescriptionFiller.Result.Filled)
                    report.Converted("Store", "Upload page", CckDescriptionFiller.Explain(result));
                else if (result == CckDescriptionFiller.Result.AlreadyWritten)
                    // Explain names the window's "Copy description" button, which this card has not got.
                    report.Approximated("Store", "Upload page", "The Description box already has text in it, so " +
                        "nothing was changed. This description is on the clipboard: clear the box and press again, " +
                        "or paste it where you want it.");
                else
                    report.Approximated("Store", "Upload page", CckDescriptionFiller.Explain(result));
                EditorGUIUtility.systemCopyBuffer = text;
                return report;
            }, "");

        // Merge animators: any controllers, not tied to the picked object.
        VisualElement MergeAnimators()
        {
            var card = new BridgeElements.Card("Merge animators", null, null, null, 1f);
            card.Body.Add(BridgeElements.Hint(
                "Copies every layer and parameter of the sources into the target. Sources are never edited."));
            var target = new ObjectField("Target") { objectType = typeof(AnimatorController), value = _mergeTarget };
            target.AddToClassList("ab-field");
            target.RegisterValueChangedCallback(e => _mergeTarget = e.newValue as AnimatorController);
            card.Body.Add(target);

            var list = new VisualElement();
            void DrawSources()
            {
                list.Clear();
                for (int i = 0; i < _mergeSources.Count; i++)
                {
                    int index = i;
                    var f = new ObjectField(i == 0 ? "Sources" : " ") { objectType = typeof(AnimatorController), value = _mergeSources[i] };
                    f.AddToClassList("ab-field");
                    f.RegisterValueChangedCallback(e => _mergeSources[index] = e.newValue as AnimatorController);
                    list.Add(f);
                }
            }
            DrawSources();
            card.Body.Add(list);
            card.Body.Add(BridgeElements.Row(
                BridgeElements.Link("+ another source", () => { _mergeSources.Add(null); DrawSources(); }),
                BridgeElements.Link("clear", () => { _mergeSources.Clear(); DrawSources(); })));
            if (_mergeSources.Count == 0) { _mergeSources.Add(null); DrawSources(); }

            card.Body.Add(BridgeElements.Choice("Write to", "Into a copy leaves the target as it is.",
                new[] { "A copy beside the target (recommended)", "The target itself" }, _mergeIntoCopy ? 0 : 1,
                i => _mergeIntoCopy = i == 0));

            var rows = new VisualElement();
            var go = new BridgeElements.PrimaryButton("Merge", () =>
            {
                var report = new BridgeReport();
                var sources = _mergeSources.Where(s => s != null).ToList();
                if (_mergeTarget == null || sources.Count == 0)
                {
                    report.Warning("Animator", "Pick a target and at least one source.");
                    ShowReport(rows, report, "");
                    return;
                }
                string savePath = null;
                if (_mergeIntoCopy)
                {
                    string targetPath = AssetDatabase.GetAssetPath(_mergeTarget);
                    string dir = string.IsNullOrEmpty(targetPath) ? OutputRoot : System.IO.Path.GetDirectoryName(targetPath).Replace('\\', '/');
                    savePath = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + _mergeTarget.name + " merged.controller");
                }
                var merged = AnimatorMergeTool.Merge(_mergeTarget, sources, savePath, report);
                if (merged != null) EditorGUIUtility.PingObject(merged);
                ShowReport(rows, report, "");
            });
            card.Body.Add(go);
            card.Body.Add(rows);
            return card;
        }

        static void ShowReport(VisualElement into, BridgeReport report, string whenEmpty)
        {
            into.Clear();
            if (report.Entries.Count == 0)
            {
                if (!string.IsNullOrEmpty(whenEmpty)) into.Add(BridgeElements.Hint(whenEmpty));
                return;
            }
            bool alt = false;
            foreach (var e in report.Entries)
            {
                into.Add(BridgeElements.ReportRow(e.Status.ToString(), e.Subject, e.Detail,
                    BridgeTheme.StatusColour(e.Status), alt));
                alt = !alt;
            }
        }
    }

    // The Toolkit on its own, for anyone who wants it in its own window. The
    // main window carries the same panel as a tab; this is the same cards,
    // not a second implementation.
    public class ToolkitWindow : EditorWindow
    {
        [MenuItem("Tools/Avatar Bridge/ChilloutVR Toolkit")]
        public static void Open()
        {
            var w = GetWindow<ToolkitWindow>();
            w.titleContent = new GUIContent("Toolkit");
            w.minSize = new Vector2(440, 520);
        }

        // Kept by the window, since the panel is rebuilt with nothing after
        // every script compile and every entry into Play mode.
        [SerializeField] GameObject target;

        void CreateGUI()
        {
            var root = rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("AvatarBridge");
            if (sheet != null) root.styleSheets.Add(sheet);
            BridgeTheme.ApplySkin(root);
            root.Add(BridgeElements.Banner("ChilloutVR Toolkit", "utilities for any avatar or prop",
                "v" + BridgeDefines.Version));
            new ToolkitPanel(target, onTargetChanged: t => target = t).Mount(root);
        }
    }
}
#endif
