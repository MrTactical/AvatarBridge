using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using VRC.SDK3.Avatars.Components;
#endif

namespace AvatarBridge
{
    // The AvatarBridge control panel. Two modes: Convert (needs the
    // VRChat SDK) and Set up (any humanoid, CCK only). Without the
    // VRChat SDK the window still works and offers Setup.
    // UI Toolkit, like the CCK and the SDK; see BridgeTheme.
    public class AvatarBridgeWindow : EditorWindow
    {
        [MenuItem("Tools/Avatar Bridge/VRChat to ChilloutVR Converter")]
        static void Open()
        {
            var window = GetWindow<AvatarBridgeWindow>();
            window.titleContent = new GUIContent("AvatarBridge");
            // 480 fits "Convert a VRChat avatar" in its third of the tab strip.
            window.minSize = new Vector2(480, 560);
        }

        // One set, shared with the fallback for a project without the CCK.
        const string BannerTitle = "AvatarBridge";
        const string ConvertSubtitle = "VRChat → ChilloutVR avatar converter";
        const string SetupSubtitle = "set up any avatar for ChilloutVR";
        const string ToolsSubtitle = "utilities for any ChilloutVR avatar or prop";

        // An external link whose address is worked out on click, such as an issue pre-filled
        // with diagnostics. Built from ExternalLink so the arrow stays the kit's.
        static Button ExternalAction(string text, Action open, string tooltip)
        {
            var link = BridgeElements.ExternalLink(text, BridgeLinks.Repo, tooltip);
            link.clickable = new Clickable(open);
            return link;
        }

#if CVR_CCK_EXISTS
        // Shared with the Toolkit, which reads the Output folder from it.
        const string PrefsKey = CvrSetup.SettingsPrefsKey;

        // Handed over by the convert and setup flows, so the last step of
        // one is the first step of the next.
        [SerializeField] GameObject toolsTarget;
        [SerializeField] GameObject toolsSource;
        [SerializeField] string settingFilter = "";

        // The tools tab exists either way: none of the Toolkit's cards need
        // the VRChat SDK, and a project without it is exactly who they are
        // for. Its panel is rebuilt each time rather than kept, so it always
        // reads the scene as it is now.
#if VRC_SDK_VRCSDK3
        // Convert only exists when the VRChat SDK does: without it there is
        // nothing to convert FROM.
        enum Mode { Convert, Setup, Tools }
        [SerializeField] Mode mode = Mode.Convert;
        [SerializeField] VRCAvatarDescriptor avatar;
#else
        enum Mode { Setup, Tools }
        [SerializeField] Mode mode = Mode.Setup;
#endif
        [SerializeField] GameObject setupAvatar;

        [SerializeField] BridgeSettings settings = new BridgeSettings();
        BridgeReport lastReport;
#if VRC_SDK_VRCSDK3
        // Set while a deferred conversion is in flight, so the button can't queue a second one.
        // Convert-mode only, like the two below: ungated it warns in every CCK-only project.
        bool converting;
        // What the last Analyse found, or null if it hasn't been run for the current avatar.
        // Cleared whenever the avatar changes: advice about a different avatar is worse than none.
        List<Advice> advice;
        BridgeElements.PrimaryButton primary;
        // Held so a rebuild can put the query back once the folds it filters exist.
        ToolbarSearchField settingSearch;
#endif

        VisualElement body;
        // Banner and tabs, rebuilt with the body: the subtitle and the lit tab follow the mode.
        VisualElement header;

        // ------------------------------------------------------------------ lifecycle --

        void OnEnable()
        {
            if (EditorPrefs.HasKey(PrefsKey))
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(EditorPrefs.GetString(PrefsKey), settings);
                }
                catch
                {
                    settings = new BridgeSettings();
                }
                // Three answers on two flags. Settings saved by the release
                // with two independent ticks can hold the fourth pair, which
                // reads as "Leave" while YAPS still runs. Convert wins.
                if (settings.convertYapsSystems && !settings.stripSpsSystems)
                {
                    settings.stripSpsSystems = true;
                }
            }
            // Old saved settings can point inside the tool's folder,
            // where an update erases every conversion. Only the old
            // default is rewritten; a customised path is the user's.
            if (settings.outputFolder == "Assets/AvatarBridge/Output")
            {
                settings.outputFolder = "Assets/AvatarBridgeOutput";
            }
            OutputFolderMigration.MigrateIfNeeded();
        }

        void OnDisable()
        {
            SaveSettings();
        }

        // Also before every run: a crash mid-conversion skips OnDisable, and
        // the next session would quietly restore older settings.
        void SaveSettings()
        {
            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(settings));
        }

        // --------------------------------------------------------------------- GUI ----

        void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            BridgeElements.Root(root);

            header = new VisualElement();
            root.Add(header);

            var scroll = BridgeElements.Scroll();
            root.Add(scroll);

            body = new VisualElement();
            scroll.Add(body);
            Rebuild();
        }

        void ScheduleRebuild()
        {
            rootVisualElement?.schedule.Execute(Rebuild);
        }

        void Rebuild()
        {
            if (body == null)
            {
                return;
            }
            body.Clear();
            header.Clear();
            header.Add(BridgeElements.Banner(BannerTitle, Subtitle(), "v" + BridgeDefines.Version,
                BridgeTheme.Span.Bridge));

            // The kit runs onSelect a frame after the click, so rebuilding there is safe.
#if VRC_SDK_VRCSDK3
            header.Add(BridgeElements.Tabs(
                new[] { "Convert a VRChat avatar", "Set up any avatar", "Tools" },
                // Single-colour glyphs only: the active tab's white tint cannot recolour a colour icon.
                new[] { "GameObject Icon", "Settings", "CustomTool" },
                (int)mode,
                index => { mode = (Mode)index; _savingFor = null; Rebuild(); },
                BridgeTheme.Span.Bridge));

            if (mode == Mode.Convert)
            {
                BuildConvertFlow();
            }
            else if (mode == Mode.Tools)
            {
                BuildToolsFlow();
            }
            else
            {
                BuildSetupFlow();
            }
#else
            header.Add(BridgeElements.Tabs(
                new[] { "Set up any avatar", "Tools" },
                new[] { "Settings", "CustomTool" },
                (int)mode,
                index => { mode = (Mode)index; _savingFor = null; Rebuild(); },
                BridgeTheme.Span.Bridge));
            if (mode == Mode.Tools)
            {
                BuildToolsFlow();
            }
            else
            {
                body.Add(BridgeElements.Notice(Tone.Info,
                    "Converting needs the VRChat SDK, which isn't installed. Setup below still works on any humanoid."));
                BuildSetupFlow();
            }
#endif
            body.Add(BuildFooter());
        }

        string Subtitle()
        {
#if VRC_SDK_VRCSDK3
            if (mode == Mode.Convert)
            {
                return ConvertSubtitle;
            }
#endif
            return mode == Mode.Tools ? ToolsSubtitle : SetupSubtitle;
        }

        // ------------------------------------------------------------- tools flow ----

        void BuildToolsFlow()
        {
            // Embedded: the window already owns the scroll and the footer.
            // A new pick drops the handed-over source, or the next rebuild pairs it with the wrong avatar.
            new ToolkitPanel(toolsTarget, true, toolsSource, t => { toolsTarget = t; toolsSource = null; })
                .Mount(body, embedded: true);
        }

        // ----------------------------------------------------------- convert flow ----

#if VRC_SDK_VRCSDK3
        void BuildConvertFlow()
        {
            // Step 1 sits at the VRChat end of the bridge, step 3 at the ChilloutVR end.
            var pick = new BridgeElements.Card("Pick your VRChat avatar").Step(1, 3, BridgeTheme.Span.Bridge);
            BuildAvatarPicker(pick.Body);
            body.Add(pick);

            // Three folds, split by who can answer the question. Everything the avatar decides
            // for itself is folded away under "Automated"; what is left in the open is the
            // short list nothing in the file can settle. Before this, forty-odd toggles sat at
            // one level with no way to tell which of them anyone was expected to think about.
            var choose = new BridgeElements.Card("Choose what gets set up").Step(2, 3, BridgeTheme.Span.Bridge);
            BuildAnalyseSection(choose.Body);

            // The two settings that change nothing, only what you are told. Under the results,
            // which are why anybody pressed the button.
            choose.Section("What the report tells you");
            AddReadingOptions(choose.Body);

            // Physics first and open: which solver to convert into is the biggest decision in
            // the window and it is the wearer's, not the avatar's.
            var physics = BuildPhysicsFold(out var tuning);
            var manual = BuildManualFold();
            var automated = BuildAutomatedFold();

            // Grouping alone does not make one setting findable, and two of the folds are often
            // shut, so one box searches all of them.
            settingSearch = BridgeElements.SearchField(tuning != null
                ? new[] { physics, tuning, manual, automated }
                : new[] { physics, manual, automated });
            settingSearch.tooltip = "Find a setting: searches Physics, Manual options and Automated options.";
            settingSearch.RegisterValueChangedCallback(e => settingFilter = e.newValue);
            // The box has no placeholder in 2022.3; without a heading it read as a stray input
            // belonging to the report toggles above.
            choose.Section("Find a setting");
            choose.Body.Add(settingSearch);
            choose.Body.Add(physics);
            choose.Body.Add(manual);
            choose.Body.Add(automated);
            body.Add(choose);

            // A toggle that rebuilds must not drop the query. Attached by now, so setting it
            // filters exactly as typing does.
            if (!string.IsNullOrEmpty(settingFilter))
            {
                settingSearch.value = settingFilter;
            }

            var run = new BridgeElements.Card("Convert").Step(3, 3, BridgeTheme.Span.Bridge);
            BuildConvertButton(run.Body);
            // Whenever the report's own button does not show: a second run finds the
            // textures already shrunk, reclaims nothing, and the record is still there.
            if (lastReport == null || lastReport.BytesReclaimed <= 0) BuildStoredRevert(run.Body);
            BuildReport(run);
            body.Add(run);
        }

        // The report's own "Put the textures back" goes with the report, which
        // a domain reload or a closed window drops. The record is a file in
        // the output folder, so it is found again by the lookup that made it.
        void BuildStoredRevert(VisualElement parent)
        {
            if (avatar == null) return;
            var source = avatar.gameObject;
            string root = CvrSetup.CheckedOutputFolder(settings.outputFolder) ?? "Assets/AvatarBridgeOutput";
            string dir = CvrSetup.FindOutputDir(root + "/" + CvrSetup.SafeFolderName(source.name), source);
            if (!AvatarSlimmer.CanRevert(dir)) return;
            parent.Add(BridgeElements.Notice(Tone.Info,
                "An earlier conversion changed this avatar's texture import settings and kept the old ones in its output folder.",
                action: BridgeElements.Btn("Put the textures back", () =>
                {
                    var report = new BridgeReport();
                    AvatarSlimmer.Revert(dir, report);
                    ShowNotification(new GUIContent(report.Entries.Count > 0 ? report.Entries[0].Subject : "Textures put back"));
                    ScheduleRebuild();
                })));
        }

        void BuildAvatarPicker(VisualElement parent)
        {
            // The kit hands the pick over a frame later, so rebuilding here is safe.
            parent.Add(BridgeElements.ObjectPicker<VRCAvatarDescriptor>("VRChat avatar", avatar, picked =>
            {
                avatar = picked;
                // Findings belong to the avatar they were measured on. Keeping them across a
                // swap would show one avatar's shader and PhysBone counts under another's name.
                advice = null;
                adviceFilter = null;
                MatchOptionalLayersToAvatar();
                Rebuild();
            }, "A scene object with a VRC Avatar Descriptor."));

            if (avatar == null)
            {
                parent.Add(BridgeElements.Hint("Drag your VRChat avatar here from the Hierarchy."));
                return;
            }

            if (VRCFuryBaker.HasFuryComponents(avatar.gameObject))
            {
                parent.Add(BridgeElements.Notice(Tone.Info,
                    "VRCFury detected: baked with its own builder first, so its features carry over."));
            }
            if (ModularAvatarBaker.HasModularAvatarComponents(avatar.gameObject))
            {
                parent.Add(BridgeElements.Notice(Tone.Info,
                    "Modular Avatar detected: baked through NDMF first, so its features carry over."));
            }
        }

        // ------------------------------------------------------------------ analyse ----

        // Action first, agreement last. A list that opens with six green "already set" rows
        // buries the one red one, and the red one is why anybody pressed the button. The chips
        // follow the same order.
        static readonly (AdviceKind kind, string noun)[] AdviceOrder =
        {
            (AdviceKind.Blocked, "blocked"), (AdviceKind.Change, "recommended"), (AdviceKind.Manual, "your call"),
            (AdviceKind.Confirm, "already set"), (AdviceKind.Inert, "not needed"),
        };

        void BuildAnalyseSection(VisualElement parent)
        {
            parent.Add(BridgeElements.Hint(
                "The defaults suit most avatars. Analyse offers the settings this one's contents decide."));

            var analyse = BridgeElements.Btn("Analyse this avatar", () => { Reanalyse(); ScheduleRebuild(); },
                avatar != null
                    ? "Reads PhysBones, blendshapes, shaders, parameters and layers. Nothing changes until you apply it."
                    : "Pick an avatar in step 1 first.",
                ButtonKind.Strong);
            analyse.SetEnabled(avatar != null);
            parent.Add(BridgeElements.ButtonRow(analyse));

            if (avatar == null || advice == null)
            {
                return;
            }
            if (advice.Count == 0)
            {
                parent.Add(BridgeElements.Notice(Tone.Good,
                    "Nothing to change: the current settings already suit this avatar."));
                return;
            }

            var ordered = new List<Advice>();
            foreach (var (kind, _) in AdviceOrder)
            {
                foreach (var a in advice)
                {
                    if (a.Kind == kind)
                    {
                        ordered.Add(a);
                    }
                }
            }

            int recommendations = 0;
            foreach (var a in ordered)
            {
                if (IsRecommendation(a))
                {
                    recommendations++;
                }
            }

            // Same shape as the conversion report below: verdict, chips,
            // rows. Two different-looking lists in one window is two
            // things to learn instead of one.
            int blocked = CountOf(AdviceKind.Blocked);
            int yours = CountOf(AdviceKind.Manual);
            parent.Add(BridgeElements.Notice(
                blocked > 0 ? Tone.Bad : recommendations > 0 ? Tone.Info : Tone.Good,
                blocked > 0
                    ? $"{N(blocked, "setting can't", "settings can't")} do what they say on this avatar. See below."
                    : recommendations > 0
                        ? $"{N(recommendations, "setting doesn't", "settings don't")} match this avatar."
                        : yours > 0
                            ? $"Everything measurable already matches. {N(yours, "thing is", "things are")} your call."
                            : "Everything measurable already matches this avatar."));

            var chips = new List<VisualElement>();
            foreach (var (kind, noun) in AdviceOrder)
            {
                chips.Add(AdviceChip(kind, noun));
            }
            parent.Add(BridgeElements.Chips(chips.ToArray()));

            if (recommendations > 1)
            {
                parent.Add(BridgeElements.ButtonRow(BridgeElements.Btn($"Apply all {recommendations} recommendations",
                    () =>
                    {
                        foreach (var a in ordered)
                        {
                            if (IsRecommendation(a))
                            {
                                a.Apply(settings);
                            }
                        }
                        Reanalyse();
                        ScheduleRebuild();
                    },
                    "Applies the measured ones only, never the \"your call\" rows.",
                    ButtonKind.Strong)));
            }

            var rows = new List<VisualElement>();
            foreach (var a in ordered)
            {
                if (adviceFilter.HasValue && a.Kind != adviceFilter.Value)
                {
                    continue;
                }
                rows.Add(AdviceRow(a));
            }
            if (rows.Count > 0)
            {
                parent.Add(BridgeElements.ReportList(rows));
            }
        }

        AdviceKind? adviceFilter;

        // Named for the hint under the layer list. Cleared and re-decided
        // on every avatar swap, so it always describes the current one.
        readonly List<string> autoOffLayers = new List<string>();

        // The optional layer settings persist between avatars, so a tick
        // meant for the last one rides along and claims this avatar has a
        // layer it does not. Runs on selection only, never on rebuild: a
        // box the user ticks deliberately must stay ticked.
        //
        // Skipped entirely for a baker's avatar. VRCFury and Modular
        // Avatar build controllers during the bake, and the conversion
        // reads the slot afterwards; unticking on the strength of an
        // empty slot here would drop a layer that does arrive.
        void MatchOptionalLayersToAvatar()
        {
            autoOffLayers.Clear();
            foreach (var setting in AvatarAdvisor.MatchOptionalLayers(avatar, settings))
            {
                autoOffLayers.Add($"\"{setting}\"");
            }
        }

        void Reanalyse()
        {
            advice = AvatarAdvisor.Analyse(avatar, settings);
            adviceFilter = null;
        }

        int CountOf(AdviceKind kind)
        {
            int n = 0;
            foreach (var a in advice)
            {
                if (a.Kind == kind)
                {
                    n++;
                }
            }
            return n;
        }

        // A zero-count chip is disabled, so it dims and cannot select an empty list.
        VisualElement AdviceChip(AdviceKind kind, string noun)
        {
            int count = CountOf(kind);
            bool selected = adviceFilter == kind;
            var chip = BridgeElements.Chip($"{count} {noun}", KindTone(kind), selected,
                () =>
                {
                    adviceFilter = selected ? (AdviceKind?)null : kind;
                    ScheduleRebuild();
                },
                selected ? "Click again to show every row." : $"Show only the {noun} rows.");
            chip.SetEnabled(count > 0);
            return chip;
        }

        static bool IsRecommendation(Advice a) => AvatarAdvisor.IsRecommendation(a);

        VisualElement AdviceRow(Advice a)
        {
            var actions = new List<VisualElement>();
            if (a.Targets != null && a.Targets.Length > 0)
            {
                actions.Add(BridgeElements.Btn(a.Targets.Length == 1 ? "Show" : $"Show {a.Targets.Length}",
                    () => Ping(a.Targets),
                    a.Targets.Length == 1
                        ? $"Selects \"{a.Targets[0].name}\"."
                        : "Selects all of them, so you can see what the count is made of."));
            }
            if (a.Apply != null)
            {
                actions.Add(BridgeElements.Btn(a.Kind == AdviceKind.Manual ? "Turn on" : "Apply",
                    () =>
                    {
                        a.Apply(settings);
                        // Re-measure rather than mark the row done: applying one setting can
                        // change what the others should be (a physics target of None has no toe
                        // question), and a stale list is how a reader ends up applying advice
                        // that stopped being true two presses ago.
                        Reanalyse();
                        ScheduleRebuild();
                    }));
            }
            string label = KindLabel(a.Kind);
            return BridgeElements.ReportRow(KindTone(a.Kind),
                string.IsNullOrEmpty(a.Setting) ? label : label + ": " + a.Setting,
                a.Finding, actions.ToArray());
        }

        static string KindLabel(AdviceKind kind)
        {
            switch (kind)
            {
                case AdviceKind.Change: return "Recommended";
                case AdviceKind.Confirm: return "Already set";
                case AdviceKind.Inert: return "Not needed";
                case AdviceKind.Manual: return "Your call";
                default: return "Blocked";
            }
        }

        static Tone KindTone(AdviceKind kind)
        {
            switch (kind)
            {
                case AdviceKind.Change: return Tone.Info;
                case AdviceKind.Confirm: return Tone.Good;
                case AdviceKind.Inert: return Tone.Muted;
                case AdviceKind.Manual: return Tone.Warn;
                default: return Tone.Bad;
            }
        }

        // One label per value, in declaration order. Spelled out, not derived:
        // nicifying gave "Magica Cloth 2" and "Cvr Native Targets", which the
        // tooltips and the README spell otherwise.
        static PopupField<string> EnumPopup<T>(string label, string tooltip, string[] labels, T current, Action<T> set)
            where T : Enum
        {
            var values = (T[])Enum.GetValues(typeof(T));
            return BridgeElements.Popup(label, tooltip, labels, Array.IndexOf(values, current), i => set(values[i]));
        }

        BridgeElements.Card BuildPhysicsFold(out BridgeElements.Card tuning)
        {
            tuning = null;
            var card = new BridgeElements.Card("Physics", "which solver, and how the chains feel", true)
                .Nested().Remember("Converter.Physics");
            var b = card.Body;

            b.Add(EnumPopup("Convert PhysBones to",
                "MagicaCloth2 gives the best result in ChilloutVR; DynamicBone is the built-in fallback.",
                new[] { "MagicaCloth 2", "DynamicBone", "None" },
                settings.physicsTarget,
                v => { settings.physicsTarget = v; ScheduleRebuild(); }));

            if (settings.physicsTarget == PhysicsTarget.MagicaCloth2 && !BridgeDefines.HasMagicaCloth2)
            {
                b.Add(BridgeElements.Notice(Tone.Warn,
                    "MagicaCloth2 is not installed in this project: import it, or switch to DynamicBone."));
            }
            if (settings.physicsTarget == PhysicsTarget.DynamicBone && !BridgeDefines.HasDynamicBone)
            {
                b.Add(BridgeElements.Notice(Tone.Warn,
                    "DynamicBone is not installed. The free VRLabs Dynamic-Bones-Stub also works for conversion."));
            }

            // Both writers name their holders from it.
            b.Add(BridgeElements.Bind("GrabbyBones mod support",
                "Names converted physics objects so Kafe's GrabbyBones mod drives the avatar's " +
                "_IsGrabbed / _Angle grab-reactive logic.",
                settings.grabbyBonesSupport, v => settings.grabbyBonesSupport = v));
            b.Add(BridgeElements.Bind("Delete PhysBones after converting",
                "Leave on: once converted, the PhysBones do nothing in ChilloutVR.",
                settings.deleteConvertedPhysBones, v => settings.deleteConvertedPhysBones = v));
            // Both writers skip toe chains, so the choice is shown for both.
            b.Add(BridgeElements.Bind("Convert toe PhysBones",
                "Off by default: simulated toes wiggle with every step in ChilloutVR. Skipped chains are listed in the report.",
                settings.convertToePhysBones, v => settings.convertToePhysBones = v));
            // Both writers grow radii and colliders from it, so hiding it on
            // DynamicBone left a setting nobody could see still changing the output.
            b.Add(BridgeElements.Bind("Size for the largest a slider makes the body",
                "Collision radius cannot be animated, so it is sized for body sliders at full.",
                settings.sizePhysicsForLargest, v => settings.sizePhysicsForLargest = v));

            if (settings.physicsTarget == PhysicsTarget.MagicaCloth2)
            {
                // Closed by default. These are escape hatches for a
                // chain that came out wrong, not decisions to make.
                tuning = new BridgeElements.Card("Advanced physics tuning", "MagicaCloth2 feel", false)
                    .Nested().Remember("Converter.PhysicsTuning");
                var t = tuning.Body;
                t.Add(BridgeElements.Hint(
                    "Leave these on. Turn one off only when a chain converts wrong; it falls back to " +
                    "MagicaCloth2's defaults."));
                t.Add(BridgeElements.Bind("Match a preset to each chain",
                    "Starts each chain from the preset that fits it: hair, tail, skirt, cape, accessory, or a spring by stiffness.",
                    settings.useMagicaPresets, v => settings.useMagicaPresets = v));
                t.Add(BridgeElements.Bind("Fit the preset to the PhysBone",
                    "Carries gravity and immobile across, and zeroes wind, which VRChat never had.",
                    settings.fitToPhysBone, v => settings.fitToPhysBone = v));
                t.Add(BridgeElements.Bind("Derive physics from the PhysBone",
                    "Converts pull, spring and stiffness into damping and angle restoration, derived from both solvers.",
                    settings.derivePhysicsFromPhysBone, v => settings.derivePhysicsFromPhysBone = v));
                t.Add(BridgeElements.Bind("Size particles from the mesh",
                    "Sizes each chain's collision to the mesh it moves, not the preset's one size for all.",
                    settings.fitRadiusToMesh, v => settings.fitRadiusToMesh = v));
                t.Add(BridgeElements.Bind("Fit colliders to the mesh",
                    "Measures the limb each collider sits on and tapers the capsule to it. Off keeps the source's sizes.",
                    settings.fitCollidersToMesh, v => settings.fitCollidersToMesh = v));
                t.Add(BridgeElements.Bind("Bound swing to the source's limit",
                    "Keeps a loose chain within the PhysBone's angle limit, as a distance bound that cannot vibrate.",
                    settings.boundSwingToSourceLimit, v => settings.boundSwingToSourceLimit = v));
                t.Add(BridgeElements.Bind("Cap particle radius to bone spacing",
                    "Bounds each particle to half the gap between its bones. Try it on a long chain of close bones that misbehaves.",
                    settings.capParticleRadius, v => settings.capParticleRadius = v));
                b.Add(tuning);

                // Out of the fold: "Your call" is not advanced, it is a choice.
                card.Section("Your call");
                b.Add(BridgeElements.Bind("Add physics to toggled rigs that have none",
                    "Gives physics to a toggled rig the author left without a PhysBone, like an add-on hairstyle. " +
                    "Off by default: some are rigid on purpose. The report names them either way.",
                    settings.addPhysicsToRiggedStyles, v => settings.addPhysicsToRiggedStyles = v));
                b.Add(BridgeElements.Bind("Auto-assign nearby colliders",
                    "Lets each cloth collide with body colliders it could swing into, which VRChat did not. Check before uploading.",
                    settings.autoAssignNearbyColliders, v => settings.autoAssignNearbyColliders = v));
            }
            return card;
        }

        BridgeElements.Card BuildAutomatedFold()
        {
            var card = new BridgeElements.Card("Automated options",
                "set for you from the avatar: face tracking, layers, components", false)
                .Nested().Remember("Converter.Automated");
            var b = card.Body;

            b.Add(BridgeElements.Hint("Analyse sets these. Change one only if you know why."));

            card.Section("General");
            AddCloneToggle(b);

            card.Section("Face tracking");
            AddFaceTrackingOptions(b);
            // Baking, cleanup, toggle rebuilding and masking always
            // run. Necessary steps are not options.

            card.Section("Remove VRChat-only systems");
            b.Add(BridgeElements.Bind("Remove GoGo Loco (recommended)",
                "ChilloutVR has its own locomotion, flight and emotes, which GoGo fights. Untick to keep GoGo's poses.",
                settings.stripGogoLoco, v => { settings.stripGogoLoco = v; ScheduleRebuild(); }));
            if (!settings.stripGogoLoco)
            {
                // GoGo cannot fully function in CVR; it leans on
                // VRChat-only animator primitives. Say so where the
                // decision is made.
                b.Add(BridgeElements.Notice(Tone.Warn,
                    "Experimental. GoGo replaces ChilloutVR's locomotion, so tick Base, Additive and Action below " +
                    "or there is none.",
                    "Poses don't lock movement, floor poses keep a standing viewpoint, and the quick-menu emotes stop."));
            }
#if !AVATARBRIDGE_YAPS
            // No add-on in the project, so "Convert to YAPS" is not an answer
            // this build can give. Offering it and quietly removing instead is
            // the worst of the three.
            b.Add(BridgeElements.Notice(Tone.Info,
                "Penetration (DPS, TPS, SPS) is removed. The YAPS add-on rebuilds it; install it and the " +
                "choice appears here.",
                action: BridgeElements.ExternalLink("Get the YAPS add-on (GitHub)", BridgeLinks.YapsRepo)));
            // Nothing written to the settings here. These persist in
            // EditorPrefs, which are per USER on this machine and not per
            // project, so forcing them off in a project without the add-on
            // forced them off in every project WITH it: the next avatar
            // converted anywhere had its penetration removed and rebuilt
            // nothing, with the window still reading Convert. BridgeConverter
            // already collapses the choice for the run itself, which is where
            // it belongs, since that touches only the copy being converted.
#else
            // One question over two settings, three answers; the fourth
            // combination the ticks allowed is not offered. Radio buttons stack
            // like the toggles around them.
            b.Add(BridgeElements.Choice("Penetration",
                "Convert to YAPS: rebuilt for ChilloutVR with the author's tuning, and works with DPS, TPS " +
                "and SPS already on the platform.\n" +
                "Remove: takes it all out.\n" +
                "Leave as VRChat built it: nothing works; only for looking at what was there.",
                new[] { "Convert to YAPS (recommended)", "Remove", "Leave as VRChat built it (won't work)" },
                settings.stripSpsSystems ? (settings.convertYapsSystems ? 0 : 1) : 2,
                choice =>
                {
                    settings.stripSpsSystems = choice != 2;
                    settings.convertYapsSystems = choice == 0;
                    ScheduleRebuild();
                }));
            b.Add(BridgeElements.Hint("DPS, TPS and SPS, with the OGB, PCS and Wholesome stacks that ride with them."));
            // What the other two answers cost, where the choice is made.
            if (settings.stripSpsSystems && !settings.convertYapsSystems)
            {
                b.Add(BridgeElements.Notice(Tone.Warn,
                    "Every plug and socket goes; the plug mesh stays, straight. Only converting again undoes it."));
            }
            else if (!settings.stripSpsSystems)
            {
                b.Add(BridgeElements.Notice(Tone.Warn,
                    "Nothing will bend or open, and the haptics parameters can push the avatar over the sync cap."));
            }
            else if (settings.convertYapsSystems && settings.syncHapticsForOsc)
            {
                b.Add(BridgeElements.Hint(
                    "The OGB haptics stay synced for OSC toys: that is on under Manual options ▸ Opt-ins."));
            }
            // The other door. The add-on builds and tunes penetration on an
            // avatar already here; this converts.
            b.Add(BridgeElements.Hint(
                "Already on ChilloutVR? Tools ▸ YAPS ▸ Setup adds penetration to any avatar or prop."));
#endif
            b.Add(BridgeElements.Bind("Remove animation that can't do anything (recommended)",
                "Curves for material properties a locked shader baked away. They did nothing in VRChat either.",
                settings.stripDeadMaterialAnimation, v => settings.stripDeadMaterialAnimation = v));

            card.Section("Animator layers to convert");
            b.Add(BridgeElements.Bind("FX (toggles, expressions)", null,
                settings.convertFxLayer, v => settings.convertFxLayer = v));
            b.Add(BridgeElements.Bind("Gesture (hand poses)", null,
                settings.convertGestureLayer, v => settings.convertGestureLayer = v));
            b.Add(BridgeElements.Bind("Base / locomotion",
                "Toggles and parameters from the Base layer, and the avatar's own walk, crouch, crawl and fall " +
                "animations grafted into ChilloutVR's locomotion.",
                settings.convertBaseLayer, v => { settings.convertBaseLayer = v; ScheduleRebuild(); }));
            b.Add(BridgeElements.Bind("Additive", null,
                settings.convertAdditiveLayer, v => settings.convertAdditiveLayer = v));
            b.Add(BridgeElements.Bind("Action (emotes, AFK)",
                "VRC emote triggers have no CVR equivalent; states may be unreachable.",
                settings.convertActionLayer, v => { settings.convertActionLayer = v; ScheduleRebuild(); }));
            if (autoOffLayers.Count > 0)
            {
                b.Add(BridgeElements.Hint(
                    $"{string.Join(" and ", autoOffLayers)} switched off: this avatar has nothing in " +
                    (autoOffLayers.Count == 1 ? "that slot." : "those slots.")));
            }

            card.Section("Parameters & toggles");
            b.Add(BridgeElements.Bind("Preserve parameter sync state",
                "Unsynced parameters become local, except ones a menu control drives: others should see what it does.",
                settings.preserveParameterSyncState, v => settings.preserveParameterSyncState = v));
            b.Add(BridgeElements.Bind("Expose menu-less synced parameters",
                "Gives them a settings entry so their values are saved between loads.",
                settings.exposeMenulessSyncedParameters, v => settings.exposeMenulessSyncedParameters = v));

            card.Section("Components");
            b.Add(BridgeElements.Bind("Convert contact senders/receivers", null,
                settings.convertContacts, v => { settings.convertContacts = v; ScheduleRebuild(); }));
            b.Add(BridgeElements.Bind("Recreate built-in VRC colliders as pointers",
                "Head/hands/fingers pointers so converted receivers keep reacting to other players.",
                settings.createDefaultColliderPointers, v => settings.createDefaultColliderPointers = v));
            b.Add(BridgeElements.Bind("Grow contact zones with the body's sliders",
                "A contact on a part a slider grows follows the slider, so touches still land at full size.",
                settings.sizeContactZonesForLargest, v => settings.sizeContactZonesForLargest = v));

            b.Add(BridgeElements.Bind("Convert VRC constraints", null,
                settings.convertConstraints, v => settings.convertConstraints = v));
            b.Add(BridgeElements.Bind("Convert VRC Head Chop",
                "First-person show/hide, including its toggle animations.",
                settings.convertHeadChop, v => settings.convertHeadChop = v));
            b.Add(BridgeElements.Bind("Convert spatial audio", null,
                settings.convertSpatialAudio, v => settings.convertSpatialAudio = v));
            AddBlinkToggle(b);
            b.Add(BridgeElements.Bind("Resize oversized textures",
                "Shrinks textures to what their mesh can show, and gives a PNG or JPG a smaller format where " +
                "its pixels allow, in import settings only. \"Put the textures " +
                "back\" undoes it; textures shared outside the avatar are left alone.",
                settings.slimTexturesOnConvert, v => settings.slimTexturesOnConvert = v));
            return card;
        }

        BridgeElements.Card BuildManualFold()
        {
            // No summary: the hint under the header says the same thing.
            var card = new BridgeElements.Card("Manual options", null, true)
                .Nested().Remember("Converter.Manual");
            var b = card.Body;

            b.Add(BridgeElements.Hint(
                "The avatar can't answer these. Leaving them alone converts fine."));

            card.Section("Shaders");
            b.Add(BridgeElements.Row(
                BridgeElements.Bind("Patch non-SPI shaders for VR",
                    "A shader without stereo support draws into one eye in VR. Patches copies; originals " +
                    "untouched. Check both eyes in game.",
                    settings.patchNonSpiShaders, v => settings.patchNonSpiShaders = v),
                BridgeElements.BetaTag()));

            // Opt-ins live here, not beside the choice they qualify: a
            // feature nobody can find is a feature nobody turns on.
            card.Section("Opt-ins");
            var optIns = BridgeElements.Indent(
                BridgeElements.Hint("Off unless you switch them on, and each says what it costs."));
            b.Add(optIns);

            optIns.Add(BridgeElements.SubHeading("OSC toys"));
            optIns.Add(BridgeElements.Bind("Keep the OGB / PCS haptics contacts",
                "Toy-app contacts. Plugs and sockets work either way, but these can spend the instance's " +
                "512 contact pairs. Only for driving a toy.",
                settings.keepHapticsContacts,
                v => { settings.keepHapticsContacts = v; ScheduleRebuild(); }));
            if (settings.keepHapticsContacts)
            {
                optIns.Add(BridgeElements.Notice(Tone.Warn,
                    "Blame this if contacts get unreliable in a busy instance."));
            }
#if AVATARBRIDGE_YAPS
            // Only a converted penetration system keeps these parameters, and
            // without the add-on there is none, so the tick could never act.
            optIns.Add(BridgeElements.Bind("Keep OGB haptics synced (OSCGoesBrrr, Lovense)",
                "On, OSCGoesBrrr finds them automatically, at 32 sync bits each. Off, link them by hand; " +
                "the report lists the names.",
                settings.syncHapticsForOsc, v => { settings.syncHapticsForOsc = v; ScheduleRebuild(); }));
            if (settings.syncHapticsForOsc)
            {
                const string cost = "About 290 bits per plug or socket, and over 3200 nothing syncs. Launch " +
                                    "ChilloutVR with --osc-query-prefix=VRChat-Client.";
                string blocker = !settings.keepHapticsContacts ? "Does nothing unless the contacts above are kept."
                               : !settings.convertYapsSystems ? "Does nothing unless Penetration is Convert to YAPS."
                               : null;
                // One notice per toggle: what stops it working comes first, the cost behind More.
                optIns.Add(BridgeElements.Notice(Tone.Warn, blocker ?? cost, blocker != null ? cost : null));
            }

            optIns.Add(BridgeElements.SubHeading("Penetration"));
            optIns.Add(BridgeElements.Bind("Show the avatar's OWN depth animations to other players",
                "The bulges the author animated on the body. Off, only you see them; on, everyone, at 32 " +
                "bits per socket.",
                settings.syncSocketDepthForOthers, v => { settings.syncSocketDepthForOthers = v; ScheduleRebuild(); }));
            if (settings.syncSocketDepthForOthers)
            {
                optIns.Add(BridgeElements.Notice(Tone.Warn,
                    "32 bits per depth parameter, and over 3200 nothing syncs. The report's sync budget says where it landed."));
            }
#endif

            card.Section("Menu & extras");
            b.Add(EnumPopup("Toggle style",
                "Animator Layers: each toggle gets its own layer.\n" +
                "CVR Native Targets: left to the CCK; press \"Create Controller\" on the avatar.",
                new[] { "Animator Layers", "CVR Native Targets" },
                settings.toggleStyle, v => settings.toggleStyle = v));
            AddHeightScaler(b);
            b.Add(BridgeElements.Text("Extra strip keywords",
                "Comma separated parameter prefixes and layer names of other VRChat-only systems to remove.",
                settings.extraStripKeywords, v => settings.extraStripKeywords = v));
            AddOutputFolder(b);
            return card;
        }

        void BuildConvertButton(VisualElement parent)
        {
            bool ftPackageMissing = FaceTrackingAssetsMissing;
            string label = converting ? "Converting…"
                         : avatar == null ? "Convert avatar"
                         : $"Convert \"{avatar.gameObject.name}\"";

            primary = new BridgeElements.PrimaryButton(label, StartConversion);
            primary.SetActive(avatar != null && !ftPackageMissing && !converting);
            parent.Add(primary);

            if (avatar == null)
            {
                parent.Add(BridgeElements.Hint("Pick an avatar in step 1 first."));
            }
            else if (ftPackageMissing)
            {
                parent.Add(FaceTrackingMissing("Automated options ▸ Face tracking"));
            }
        }

        void StartConversion()
        {
            if (converting || avatar == null)
            {
                return;
            }
            // Deferred on purpose. The conversion runs asset imports, prefab work and thousands of
            // log calls; running it straight out of an event callback freezes the panel mid-repaint
            // and gives no chance to show that anything is happening.
            converting = true;
            primary?.SetLabel("Converting…");
            primary?.SetActive(false);

            var target = avatar;
            // Taken now: converting in place deletes the descriptor.
            var source = avatar.gameObject;
            var chosen = settings;
            SaveSettings();
            EditorApplication.delayCall += () =>
            {
                try { lastReport = BridgeConverter.Convert(target, chosen); reportSource = source; }
                // A filter left from the previous run can select a status the new report has none of,
                // which shows an empty list and an unclickable chip to clear it with.
                finally { converting = false; reportFilter = null; Rebuild(); }
            };
        }
#endif // VRC_SDK_VRCSDK3

        // ------------------------------------------------------------- setup flow ----

        void BuildSetupFlow()
        {
            var pick = new BridgeElements.Card("Pick any avatar").Step(1, 3, BridgeTheme.Span.Bridge);
            pick.Body.Add(BridgeElements.ObjectPicker<GameObject>("Avatar", setupAvatar, picked =>
            {
                setupAvatar = picked;
                Rebuild();
            }, "Any avatar in the scene. A Humanoid rig gives the best result."));

            if (setupAvatar == null)
            {
                pick.Body.Add(BridgeElements.Hint(
                    "Drag any avatar here from the Hierarchy; it doesn't need to be a VRChat avatar."));
            }
            else
            {
                var animator = setupAvatar.GetComponent<Animator>();
                if (animator == null || !animator.isHuman)
                {
                    pick.Body.Add(BridgeElements.Notice(Tone.Warn,
                        "Not a Humanoid rig: the viewpoint is guessed and eye tracking can't be wired. Set " +
                        "Humanoid in the model's import settings."));
                }
                if (setupAvatar.GetComponent<ABI.CCK.Components.CVRAvatar>() != null)
                {
                    pick.Body.Add(BridgeElements.Notice(Tone.Warn,
                        "It already has a CVRAvatar: its Advanced Avatar Settings are rebuilt from scratch."));
                }
            }
            body.Add(pick);

            var choose = new BridgeElements.Card("Choose what gets set up").Step(2, 3, BridgeTheme.Span.Bridge);
            choose.Body.Add(BridgeElements.Hint("Viewpoint, visemes and blink are always detected and wired."));
            // Where the converter puts them: what the report TELLS you, kept
            // apart from what gets built.
            choose.Section("What the report tells you");
            AddReadingOptions(choose.Body);

            // The summary reads the popup's own labels, so the two never disagree.
            var faceTracking = new BridgeElements.Card("Face tracking", FtLabels[FtIndex], true)
                .Nested().Remember("Converter.SetupFaceTracking");
            AddFaceTrackingOptions(faceTracking.Body);
            choose.Body.Add(faceTracking);

            choose.Section("Extras");
            AddHeightScaler(choose.Body);

            var advanced = new BridgeElements.Card("Advanced options", "clone, output folder, blink", false)
                .Nested().Remember("Converter.SetupAdvanced");
            AddCloneToggle(advanced.Body);
            AddOutputFolder(advanced.Body);
            AddBlinkToggle(advanced.Body);
            choose.Body.Add(advanced);
            body.Add(choose);

            var run = new BridgeElements.Card("Set up").Step(3, 3, BridgeTheme.Span.Bridge);
            bool ftPackageMissing = FaceTrackingAssetsMissing;
            var button = new BridgeElements.PrimaryButton(
                setupAvatar == null ? "Set up avatar" : $"Set up \"{setupAvatar.name}\"",
                () =>
                {
                    SaveSettings();
                    var selectedBefore = Selection.activeGameObject;
                    lastReport = CvrSetup.Run(setupAvatar, settings);
                    reportSource = setupAvatar;
                    // So the setup flow ends where the convert flow does:
                    // with somewhere to go. Setup works on the object it was
                    // given unless it cloned, and Selection is what it leaves
                    // pointing at the result. Only a selection the run changed
                    // counts: a run that threw leaves the user's own one.
                    var selected = Selection.activeGameObject;
                    if (lastReport.ConvertedRoot == null)
                    {
                        lastReport.ConvertedRoot = selected != null && selected != selectedBefore
                            && selected.GetComponent<ABI.CCK.Components.CVRAvatar>() != null
                            ? selected : setupAvatar;
                    }
                    reportFilter = null;
                    ScheduleRebuild();
                });
            button.SetActive(setupAvatar != null && !ftPackageMissing);
            run.Body.Add(button);

            if (setupAvatar == null)
            {
                run.Body.Add(BridgeElements.Hint("Pick an avatar in step 1 first."));
            }
            else if (ftPackageMissing)
            {
                run.Body.Add(FaceTrackingMissing("Face tracking in step 2"));
            }
            BuildReport(run);
            body.Add(run);
        }

        // ---------------------------------------------------------- shared sections ----

        // The two that only ever READ. Shared by both flows: an avatar built
        // for ChilloutVR has as much to learn about itself as a converted
        // one, and the setup report is the same report.
        void AddReadingOptions(VisualElement parent)
        {
            parent.Add(BridgeElements.Bind("Say what this avatar costs (recommended)",
                "Adds texture memory, contacts, triangles and cloth to the report. Reads only.",
                settings.weighAvatar, v => settings.weighAvatar = v));
            parent.Add(BridgeElements.Bind("Say what this avatar does (recommended)",
                "Adds unwired features, layers fighting over the same thing and possible props to the report. Reads only.",
                settings.surveyAvatar, v => settings.surveyAvatar = v));
        }

        // One copy of each control both flows show: the README quotes these
        // labels verbatim, and a second copy is a second place to drift.
        void AddCloneToggle(VisualElement parent)
        {
            parent.Add(BridgeElements.Bind("Work on a clone (recommended)",
                "The original avatar object stays untouched and gets deactivated.",
                settings.cloneAvatar, v => settings.cloneAvatar = v));
        }

        void AddHeightScaler(VisualElement parent)
        {
            parent.Add(BridgeElements.Bind("Add height scaler  (\"Height\" slider)",
                "A menu slider from 0.25x to 4x the avatar's height, centred on its own size. Held props scale with you.",
                settings.addAvatarScaler, v => settings.addAvatarScaler = v));
        }

        bool FaceTrackingAssetsMissing =>
            settings.faceTrackingMode == FaceTrackingMode.DragonSkyRunner && !FaceTrackingPackages.IsInstalled();

        // Names the popup's real answers and where it is, since it sits in a fold.
        static VisualElement FaceTrackingMissing(string where) => BridgeElements.Notice(Tone.Warn,
            $"The bundled face-tracking assets are missing. Reimport AvatarBridge, or set {where} to " +
            $"{FtLabels[0]} or {FtLabels[2]}.");

        void AddOutputFolder(VisualElement parent)
        {
            parent.Add(BridgeElements.Text("Output folder",
                "Where assets and the report go, inside Assets. Kept outside the tool's folder so updating never erases them.",
                settings.outputFolder, v => settings.outputFolder = v));
        }

        void AddBlinkToggle(VisualElement parent)
        {
            parent.Add(BridgeElements.Bind("Auto-wire blink blendshapes",
                "Finds blink shapes on the face mesh and turns on ChilloutVR's blinking.",
                settings.wireBlinkBlendshapes, v => settings.wireBlinkBlendshapes = v));
        }

        static readonly FaceTrackingMode[] FtModes =
            { FaceTrackingMode.Native, FaceTrackingMode.DragonSkyRunner, FaceTrackingMode.None };

        static readonly string[] FtLabels =
            { "Native CVR Component", "Unity Animator Blendtrees (DSR)", "Keep the avatar's own rig" };

        int FtIndex => Mathf.Max(0, Array.IndexOf(FtModes, settings.faceTrackingMode));

        void AddFaceTrackingOptions(VisualElement b)
        {
            b.Add(BridgeElements.Popup("Face tracking",
                "Native CVR Component: ChilloutVR's own, a bit stiff.\n" +
                "Unity Animator Blendtrees (DSR): DragonSkyRunner's rig, smoother.\n" +
                "Keep the avatar's own rig: converts the existing one untouched.\n" +
                "The first two replace any rig already there.",
                FtLabels, FtIndex,
                i =>
                {
                    settings.faceTrackingMode = FtModes[i];
                    ScheduleRebuild();
                }));

            if (settings.faceTrackingMode == FaceTrackingMode.DragonSkyRunner)
            {
                if (FaceTrackingPackages.IsInstalled())
                {
                    b.Add(BridgeElements.Notice(Tone.Info,
                        "Rebuilds DragonSkyRunner's bundled face and eye tracking rig onto this avatar. Gaze " +
                        "strength may want tuning.",
                        action: BridgeElements.ExternalLink("DragonSkyRunner's package (GitHub)", FaceTrackingPackages.Url)));
                }
                else
                {
                    b.Add(BridgeElements.Notice(Tone.Warn,
                        $"The bundled \"{FaceTrackingPackages.DisplayName}\" assets are missing: reimport AvatarBridge."));
                }
            }
        }

        // ------------------------------------------------------------------- report ---

        ReportStatus? reportFilter;

        // "1 error", not "1 error(s)": visible copy should not read like a template.
        static string N(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

        // One noun pair per status, for its chip; the plural also names the filter in the hint.
        static readonly (ReportStatus status, string one, string many)[] ReportChips =
        {
            (ReportStatus.Converted, "done", "done"), (ReportStatus.Approximated, "approximated", "approximated"),
            (ReportStatus.Skipped, "skipped", "skipped"), (ReportStatus.Warning, "warning", "warnings"),
            (ReportStatus.Error, "error", "errors"),
        };

        VisualElement ReportChip(ReportStatus status, string one, string many)
        {
            int count = lastReport.CountOf(status);
            bool selected = reportFilter == status;
            var chip = BridgeElements.Chip(N(count, one, many), BridgeTheme.ToneOf(status), selected,
                () =>
                {
                    reportFilter = selected ? (ReportStatus?)null : status;
                    ScheduleRebuild();
                });
            chip.SetEnabled(count > 0);
            return chip;
        }

        // The end of the flow: pick, analyse, tweak, convert, optimise. Hands the avatar to the
        // tools rather than acting here, and only offered when there is something to take off.
        Button SlimButton()
        {
            var root = lastReport.ConvertedRoot;
            var cvr = root != null ? root.GetComponent<ABI.CCK.Components.CVRAvatar>() : null;
            if (cvr == null) return null;

            long saved = SavingFor(cvr);
            if (saved <= 0) return null;
            return BridgeElements.Btn($"Make it lighter: {(saved / 1048576f):0.0} MB to reclaim",
                () =>
                {
                    toolsTarget = root;
                    toolsSource = reportSource;
                    mode = Mode.Tools;
                    ScheduleRebuild();
                },
                "Texture that can come off with nothing visible changing. Opens the Tools tab on the result.",
                ButtonKind.Strong);
        }

        // What "Make it lighter" would reclaim, for the offer on the button.
        // Measured once per report rather than per redraw: a survey and a
        // weight reading of a big avatar is real work, and this runs inside
        // a UI rebuild. Keyed on the report, not the avatar: converting again
        // in place keeps the same CVRAvatar. Cleared by the revert and on every
        // tab switch, since a Toolkit fix changes the saving without a new report.
        long SavingFor(ABI.CCK.Components.CVRAvatar cvr)
        {
            if (_savingFor != null && _savingFor == lastReport) return _saving;
            var survey = AvatarSurvey.Build(cvr);
            var plan = AvatarSlimmer.Find(cvr, survey, AvatarWeight.Measure(cvr, survey), reportSource);
            _savingFor = lastReport;
            _saving = plan.Bytes + plan.StripBytes;
            return _saving;
        }

        BridgeReport _savingFor;
        long _saving;

        // The avatar the last report's run came from, whose materials point
        // at the same textures and are not somebody else's. Recorded at run
        // time: the other tab's avatar is unrelated to this report.
        GameObject reportSource;

        void BuildReport(BridgeElements.Card card)
        {
            if (lastReport == null)
            {
                return;
            }
            var b = card.Body;
            int errors = lastReport.CountOf(ReportStatus.Error);
            int warnings = lastReport.CountOf(ReportStatus.Warning);

            b.Add(BridgeElements.Notice(
                errors > 0 ? Tone.Bad : warnings > 0 ? Tone.Warn : Tone.Good,
                errors > 0 ? $"Finished with {N(errors, "error", "errors")}. See below."
                : warnings > 0 ? $"Done! {N(warnings, "thing may", "things may")} want a look. See below."
                : "Done! The avatar is ready for the CCK's upload checks."));

            var chips = new List<VisualElement>();
            foreach (var (status, one, many) in ReportChips)
            {
                chips.Add(ReportChip(status, one, many));
            }
            b.Add(BridgeElements.Chips(chips.ToArray()));

            // The full list lives in the report file; this shows what
            // the chips select. Default: everything needing a look.
            var rows = new List<VisualElement>();
            // Filled on the first subject that is not a path, then shared by
            // every row: one walk of the avatar per rebuild, not one per row.
            Dictionary<string, Transform> byName = null;
            foreach (var entry in lastReport.Entries)
            {
                bool include = reportFilter.HasValue
                    ? entry.Status == reportFilter.Value
                    : entry.Status != ReportStatus.Converted && entry.Status != ReportStatus.Approximated;
                if (!include)
                {
                    continue;
                }
                // Most subjects are object paths or names. Ones that
                // still resolve become clickable; prose ones do not.
                var found = ResolveSubject(entry.Subject, ref byName);
                rows.Add(found != null
                    ? BridgeElements.ReportRow(entry, BridgeElements.Btn("Show",
                        () => Ping(new UnityEngine.Object[] { found }),
                        $"Selects \"{found.name}\" in the Hierarchy."))
                    : BridgeElements.ReportRow(entry));
            }
            // A clean run has nothing to list, and an empty bordered box reads as something that
            // failed to load rather than as good news.
            if (rows.Count > 0)
            {
                b.Add(BridgeElements.Hint(reportFilter.HasValue
                    ? $"Showing {Array.Find(ReportChips, c => c.status == reportFilter.Value).many} only. " +
                      "Click the chip again to show everything."
                    : "Everything that needs a look. Click a chip to see just those."));
                b.Add(BridgeElements.ReportList(rows));
            }

            if (lastReport.BytesReclaimed > 0)
            {
                // The undo, beside the announcement. This one happens without
                // being asked now, so the way back cannot live in another
                // window: the record is a file in the output folder, and the
                // saved report sits in that same folder.
                string outputDir = string.IsNullOrEmpty(lastReport.SavedReportPath)
                    ? null
                    : System.IO.Path.GetDirectoryName(lastReport.SavedReportPath);
                var undo = !string.IsNullOrEmpty(outputDir) && AvatarSlimmer.CanRevert(outputDir)
                    ? BridgeElements.Btn("Put the textures back", () =>
                    {
                        AvatarSlimmer.Revert(outputDir, lastReport);
                        lastReport.BytesReclaimed = 0;
                        _savingFor = null;
                        ScheduleRebuild();
                    })
                    : null;
                b.Add(BridgeElements.Notice(Tone.Good,
                    $"{(lastReport.BytesReclaimed / 1048576f):0.0} MB reclaimed by Resize oversized textures.",
                    action: undo));
            }

            card.Section("Next");
            var slim = SlimButton();
            if (slim != null)
            {
                b.Add(BridgeElements.ButtonRow(slim));
            }

            if (!string.IsNullOrEmpty(lastReport.SavedReportPath))
            {
                b.Add(BridgeElements.ButtonRow(
                    BridgeElements.Caption("Report"),
                    string.IsNullOrEmpty(lastReport.SavedHtmlPath) ? null : BridgeElements.Btn("Open web report",
                        () => EditorUtility.OpenWithDefaultApp(lastReport.SavedHtmlPath),
                        "The same report as a page: charts, filters, and the technical appendix."),
                    BridgeElements.Btn("Open full report", () =>
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(lastReport.SavedReportPath);
                        if (asset != null) { AssetDatabase.OpenAsset(asset); }
                    }),
                    BridgeElements.Btn("Show in Project", () =>
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(lastReport.SavedReportPath);
                        if (asset != null) { EditorGUIUtility.PingObject(asset); }
                    })));
            }

            if (!string.IsNullOrEmpty(lastReport.StoreDescription))
            {
                b.Add(BridgeElements.ButtonRow(
                    BridgeElements.Caption("Store"),
                    BridgeElements.Btn("Copy description", () =>
                    {
                        EditorGUIUtility.systemCopyBuffer = lastReport.StoreDescription;
                        ShowNotification(new GUIContent("Description copied"));
                    }, "A store listing of what the avatar has, with room for your own words first. Also saved as Description.txt."),
                    BridgeElements.Btn("Fill CCK description", () =>
                    {
                        var result = CckDescriptionFiller.Fill(lastReport.StoreDescription);
                        ShowNotification(new GUIContent(
                            result == CckDescriptionFiller.Result.Filled
                                ? "Description filled" : "Couldn't fill it"));
                        Debug.Log("[AvatarBridge] " + CckDescriptionFiller.Explain(result));
                    }, "Types it into the CCK's Description box, if empty. Open the Builder tab with this avatar first.")));
            }

            // Always offered once a report exists, rather than only when something went wrong:
            // "it converted clean but the avatar is wrong in game" is a report worth having, and
            // it's the case where the button used to be missing. The footer drops its copies.
            b.Add(BridgeElements.ButtonRow(
                BridgeElements.Caption("Help"),
                BridgeElements.Btn("Copy diagnostics", () =>
                {
                    BridgeLinks.CopyDiagnostics(lastReport);
                    ShowNotification(new GUIContent("Diagnostics copied"));
                }, "Copies versions and detected packages to the clipboard."),
                ExternalAction("Report an issue", () => BridgeLinks.OpenBugReport(lastReport),
                    "Opens a pre-filled GitHub issue. Please attach the report: most bugs are diagnosed straight from it."),
                BridgeElements.ExternalLink("Troubleshooting", BridgeLinks.Troubleshooting, "Setup and install help.")));
        }

        UnityEngine.Object ResolveSubject(string subject, ref Dictionary<string, Transform> byName)
        {
            var root = lastReport != null ? lastReport.ConvertedRoot : null;
            if (root == null || string.IsNullOrEmpty(subject))
            {
                return null;
            }

            // Read the way the animator reads it: Find misses a name with a slash in it.
            var byPath = BridgeContext.FindByAnimationPath(root.transform, subject);
            if (byPath != null)
            {
                return byPath.gameObject;
            }

            if (byName == null)
            {
                byName = new Dictionary<string, Transform>();
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    // A repeated name is ambiguous: say nothing rather than pick.
                    byName[t.name] = byName.ContainsKey(t.name) ? null : t;
                }
            }
            return byName.TryGetValue(subject, out var single) && single != null ? single.gameObject : null;
        }

        static void Ping(UnityEngine.Object[] targets)
        {
            if (targets == null || targets.Length == 0)
            {
                return;
            }
            Selection.objects = targets;
            EditorGUIUtility.PingObject(targets[0]);
        }

        // ------------------------------------------------------------------- footer ---

        Button DiscordLink()
        {
            if (string.IsNullOrEmpty(BridgeLinks.DiscordUser))
            {
                return null;
            }
            const string why = "Best for quick questions; please use GitHub issues for bugs so they don't get lost.";
            if (BridgeLinks.HasDiscordLink)
            {
                return ExternalAction($"Discord: {BridgeLinks.DiscordUser}", BridgeLinks.OpenDiscord,
                    "Opens Discord. " + why);
            }
            // Nowhere to open, so this one copies and carries no arrow.
            return BridgeElements.TextLink($"Copy Discord: {BridgeLinks.DiscordUser}", () =>
            {
                BridgeLinks.CopyDiscord();
                ShowNotification(new GUIContent("Copied: " + BridgeLinks.DiscordUser));
            }, "Copies the handle to your clipboard. " + why);
        }

        VisualElement BuildFooter()
        {
            // A shown report's Help row already carries these two.
            bool reportShown = lastReport != null && mode != Mode.Tools;
            return BridgeElements.Footer(
                reportShown ? null : BridgeElements.ExternalLink("Troubleshooting", BridgeLinks.Troubleshooting,
                    "Setup and install help."),
                reportShown ? null : ExternalAction("Report an issue", () => BridgeLinks.OpenBugReport(lastReport),
                    "Opens a pre-filled GitHub issue with your versions and detected packages."),
                DiscordLink());
        }
#else
        void CreateGUI()
        {
            var root = BridgeElements.Root(rootVisualElement);
            root.Add(BridgeElements.Banner(BannerTitle, ConvertSubtitle, "v" + BridgeDefines.Version,
                BridgeTheme.Span.Bridge));

            var body = BridgeElements.Scroll();
            root.Add(body);

            body.Add(BridgeElements.Notice(Tone.Warn,
                "AvatarBridge converts VRChat avatars to ChilloutVR. It needs both SDKs for that:"));
            body.Add(BridgeElements.KeyValue("VRChat SDK",
                BridgeDefines.HasVrcAvatarSdk ? "✔ Avatars SDK3 installed" : "✘ Missing: the Avatars SDK3 reads the avatar",
                BridgeDefines.HasVrcAvatarSdk ? Tone.Good : Tone.Bad));
            body.Add(BridgeElements.KeyValue("ChilloutVR CCK",
                BridgeDefines.HasCck ? "✔ Installed" : "✘ Missing: always required, 4.x recommended",
                BridgeDefines.HasCck ? Tone.Good : Tone.Bad));
            body.Add(BridgeElements.Notice(Tone.Info,
                "Import what's missing and reopen this window. With the CCK alone, Setup mode still works."));

            body.Add(BridgeElements.Footer(
                BridgeElements.ExternalLink("Setup guide", BridgeLinks.Troubleshooting, "Setup and install help."),
                ExternalAction("Report an issue", () => BridgeLinks.OpenBugReport(),
                    "Opens a pre-filled GitHub issue with your versions and detected packages.")));
        }
#endif
    }
}
