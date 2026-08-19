// Tools > Avatar Bridge > ChilloutVR Toolkit. Standalone utilities for
// any ChilloutVR avatar or prop, each a card: pick, run, read the rows.
// The same passes the converter runs, on the converter's own elements.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
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
        public ToolkitPanel(GameObject target = null) { _target = target; }

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

            var pick = new BridgeElements.Card("Pick your avatar or prop", null, null, 1, 0f);
            var picker = new ObjectField("Avatar or prop") { objectType = typeof(GameObject), allowSceneObjects = true, value = _target };
            picker.RegisterValueChangedCallback(e => { _target = e.newValue as GameObject; Build(); });
            pick.Body.Add(picker);
            if (_target == null) pick.Body.Add(BridgeElements.Hint("Drag the avatar or prop here from the Hierarchy. Every card below acts on it."));
            _pages.Add(pick);

            var tools = new BridgeElements.Card("Tools", null, null, 2, 0.5f);
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

            var more = new BridgeElements.Card("Also in this package", null, false, null, 1f);
            more.Body.Add(BridgeElements.Hint(
                "AvatarBridge converts a VRChat avatar to ChilloutVR. YAPS adds, tunes or upgrades penetration " +
                "on any avatar or prop. CCK Animator Tester drives an avatar the way the game does, in Play mode."));
            more.Body.Add(BridgeElements.Row(
                BridgeElements.Link("AvatarBridge", () => EditorApplication.ExecuteMenuItem("Tools/Avatar Bridge/VRChat to ChilloutVR Converter")),
                BridgeElements.Link("YAPS", () => EditorApplication.ExecuteMenuItem("Tools/YAPS/Setup")),
                BridgeElements.Link("CCK Animator Tester", () => EditorApplication.ExecuteMenuItem("Tools/Avatar Bridge/CCK Animator Tester"))));
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
            return new BridgeContext
            {
                Settings = new BridgeSettings(),
                Report = report,
                Target = _target,
                CvrAvatar = _target != null ? _target.GetComponent<CVRAvatar>() : null,
                MergedController = animator != null ? animator.runtimeAnimatorController as AnimatorController : null,
                OutputDir = _target != null ? OutputRoot + "/" + _target.name : OutputRoot,
                Standalone = true,
            };
        }

        VisualElement Tool(string title, string blurb, string button, System.Func<BridgeReport> run, string whenEmpty)
        {
            var box = new VisualElement();
            box.Add(BridgeElements.SubHeading(title));
            box.Add(BridgeElements.Hint(blurb));
            var rows = new VisualElement();
            var b = new Button(() =>
            {
                if (_target == null) return;
                var report = run();
                ShowReport(rows, report, whenEmpty);
            }) { text = button };
            b.AddToClassList("ab-btn");
            b.SetEnabled(_target != null);
            box.Add(BridgeElements.Row(b));
            box.Add(rows);
            return box;
        }

        VisualElement Check() => Tool("Check this avatar",
            "What will break silently in ChilloutVR: components the game deletes on load, the 3200-bit sync " +
            "budget, parameters used but never declared or declared but never read, menu entries whose type " +
            "disagrees with the animator's, shaders that draw into one eye, cloth with no root bones. Reads only.",
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
            "Reads the animator, the menu and the components into one model and names what the avatar can " +
            "and cannot do: features its author built and never wired to anything, things two layers both " +
            "animate where the higher one quietly wins, menu controls nothing reads, and objects that " +
            "could come off as ChilloutVR props. It answers by reading, so a toggle a preset overrides is " +
            "never mistaken for a dead one. Reads only.",
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
            "Texture memory measured against the surface each map actually covers, so a 2K texture on a " +
            "fingernail is named as one. Also contacts against the 512 overlapping pairs ChilloutVR gives the " +
            "whole instance, triangles, cloth solvers, blendshapes nothing animates, and materials each " +
            "carrying their own locked shader. Reads only, and says what to change rather than changing it.",
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
                AvatarWeight.Fill(report, AvatarWeight.Measure(ctx.CvrAvatar, AvatarSurvey.Build(ctx.CvrAvatar)));
                return report;
            }, "Nothing on it is worth changing.");

        VisualElement Tidy() => Tool("Free wins",
            "Removes only what is provably inert: layers with no states, and parameters no clip writes, no " +
            "transition reads, no driver, menu control or contact names. Decided by reading the controller, " +
            "never by flipping something and watching — an individual toggle a preset overrides looks dead " +
            "and is not. Written to a copy of the controller; your original is not edited.",
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
                    AssetDatabase.GenerateUniqueAssetPath(ctx.OutputDir + "/" + _target.name + " tidied.controller"),
                    report);
                return report;
            }, "Nothing on it is provably inert. That is the good outcome.");

        VisualElement Stereo() => Tool("Stereo shaders",
            "ChilloutVR renders single-pass instanced; a shader that never opted in draws into one eye in VR. " +
            "Copies such shaders with the stereo macros added and points this object's materials at the copies. " +
            "Originals untouched; a copy that fails to compile is thrown away. Materials an animation swaps in " +
            "are not followed here, because that would mean editing your animation clips.",
            "Patch shaders for VR stereo", () =>
            {
                var report = new BridgeReport();
                var ctx = Context(report);
                Undo.RegisterFullObjectHierarchyUndo(_target, "Patch stereo shaders");
                // No controller: the clip pass would rewrite the user's own
                // clips in place. Renderers only.
                ShaderSpiPatcher.Patch(_target, ctx.OutputDir + "/RehomedAssets", null, report);
                return report;
            }, "Every shader on it already declares stereo support, or has no source to patch.");

        VisualElement Face() => Tool("Face: visemes and blink",
            "Finds the face mesh, wires the standard viseme blendshapes and the blink shapes onto the CVRAvatar. " +
            "Needs a CVRAvatar on the root; touches nothing else on it.",
            "Wire face", () => CvrSetup.WireFace(_target, new BridgeSettings()),
            "Nothing found to wire.");

        VisualElement Audio() => Tool("Audio limits",
            "Clamps every audio source on it to spatial settings ChilloutVR handles: doppler off, distance floors " +
            "and caps, fully 3D. One source with a zero minimum distance on a wearer can mute the whole game.",
            "Clamp audio sources", () =>
            {
                var report = new BridgeReport();
                Undo.RegisterFullObjectHierarchyUndo(_target, "Clamp audio");
                AvatarHygiene.SanitizeAudioSources(Context(report));
                return report;
            }, "No audio source needed clamping.");

        VisualElement Bounds() => Tool("Mesh bounds",
            "Resizes every skinned mesh's bounds to the avatar's own volume plus clearance, so meshes stop " +
            "vanishing at the screen's edge and oversized boxes come down.",
            "Fix mesh bounds", () =>
            {
                var report = new BridgeReport();
                Undo.RegisterFullObjectHierarchyUndo(_target, "Fix mesh bounds");
                AvatarHygiene.NormalizeSkinnedBounds(Context(report));
                return report;
            }, "Bounds were already right.");

        VisualElement Height() => Tool("Height slider",
            "Adds a quick-menu Height slider, 0.25x to 4x of the avatar's measured height and centred on its " +
            "original size, into the root's animator controller and the CVRAvatar's advanced settings. Edits " +
            "the controller asset the avatar uses.",
            "Add height slider", () =>
            {
                var report = new BridgeReport();
                var ctx = Context(report);
                if (ctx.MergedController == null) { report.Error("Scaler", "No animator controller on the root"); return report; }
                if (ctx.CvrAvatar == null) { report.Error("Scaler", "No CVRAvatar on the root"); return report; }
                var controller = ctx.MergedController;
                if (controller.layers.Any(l => l.name == "Size" || l.name.StartsWith("Size ", System.StringComparison.Ordinal)))
                {
                    report.Approximated("Scaler", "Height slider already there",
                        "The controller already carries a Size layer. Nothing was added twice.");
                    return report;
                }
                Undo.RegisterFullObjectHierarchyUndo(_target, "Height slider");
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
            "Writes a description of what the avatar has, sized to the CCK's 256-character box, and types it " +
            "into the upload page's field when that window is open.",
            "Write description", () =>
            {
                var report = new BridgeReport();
                var ctx = Context(report);
                string text = AvatarDescription.Build(ctx);
                var result = CckDescriptionFiller.Fill(text, overwrite: true);
                report.Converted("Store", "Description", text);
                report.Converted("Store", "Upload page", CckDescriptionFiller.Explain(result));
                EditorGUIUtility.systemCopyBuffer = text;
                return report;
            }, "");

        // Merge animators: any controllers, not tied to the picked object.
        VisualElement MergeAnimators()
        {
            var card = new BridgeElements.Card("Merge animators", null, null, 3, 1f);
            card.Body.Add(BridgeElements.Hint(
                "Every layer and parameter of the sources goes into the target, deep-copied, layers after the " +
                "target's own. Same-named layers are renamed; a parameter present in both with different types " +
                "is named and the target's type kept. Sources are never edited."));
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
                var colour = e.Status == ReportStatus.Error ? BridgeTheme.Bad
                           : e.Status == ReportStatus.Warning || e.Status == ReportStatus.Approximated ? BridgeTheme.Warn
                           : e.Status == ReportStatus.Converted ? BridgeTheme.Good : BridgeTheme.Muted;
                into.Add(BridgeElements.ReportRow(e.Status.ToString(), e.Subject, e.Detail, colour, alt));
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

        void CreateGUI()
        {
            var root = rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("AvatarBridge");
            if (sheet != null) root.styleSheets.Add(sheet);
            BridgeTheme.ApplySkin(root);
            root.Add(BridgeElements.Banner("ChilloutVR Toolkit", "utilities for any avatar or prop",
                BridgeDefines.Version));
            new ToolkitPanel().Mount(root);
        }
    }
}
#endif
