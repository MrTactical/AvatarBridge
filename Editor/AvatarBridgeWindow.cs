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
        const string PrefsKey = "AvatarBridge.Settings";

        [MenuItem("Tools/Avatar Bridge/VRChat to ChilloutVR Converter")]
        static void Open()
        {
            var window = GetWindow<AvatarBridgeWindow>();
            window.titleContent = new GUIContent("AvatarBridge");
            window.minSize = new Vector2(430, 560);
        }

#if CVR_CCK_EXISTS
        // Handed over by the convert and setup flows, so the last step of
        // one is the first step of the next.
        [SerializeField] GameObject toolsTarget;
        [SerializeField] GameObject toolsSource;
        [SerializeField] string settingFilter = "";

        // The tools tab exists either way: none of the Toolkit's cards need
        // the VRChat SDK, and a project without it is exactly who they are
        // for. Rebuilt each time rather than kept, so it always reads the
        // scene as it is now.
        ToolkitPanel toolkit;
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
#endif

#if VRC_SDK_VRCSDK3
        // Convert-mode only. Ungated, these earn CS0414 warnings in
        // every CCK-only project.
        bool showManual = true;
        bool showAutomated;
        bool showPhysics = true;
        bool showPhysicsTuning = false;
        // What the last Analyse found, or null if it hasn't been run for the current avatar.
        // Cleared whenever the avatar changes: advice about a different avatar is worse than none.
        List<Advice> advice;
#endif
        bool showFaceTracking = true;
        bool showAdvanced;

        VisualElement body;
        // Both paths tab: with the VRChat SDK to pick a mode, without it
        // to reach setup and the toolkit.
        VisualElement tabs;
#if VRC_SDK_VRCSDK3
        BridgeElements.PrimaryButton primary;
#endif

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
            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(settings));
        }

        // --------------------------------------------------------------------- GUI ----

        void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.AddToClassList("ab-root");
            // How the VRChat SDK does it too: one class on the root, and the stylesheet handles
            // both skins from there rather than every colour being decided in C#.
            BridgeTheme.ApplySkin(root);

            var sheet = Resources.Load<StyleSheet>("AvatarBridge");
            if (sheet != null)
            {
                root.styleSheets.Add(sheet);
            }
            // If it didn't load, everything below still works, plainly.

            root.Add(BridgeElements.Banner("AvatarBridge",
                "VRChat → ChilloutVR avatar converter", "v" + BridgeDefines.Version));

            // Held rather than added directly: the active tab is styled, so it has to be rebuilt
            // when the mode changes or the highlight goes stale on the tab you just left.
            tabs = new VisualElement();
            root.Add(tabs);

            var scroll = new ScrollView();
            scroll.AddToClassList("ab-scroll");
            // Nothing here wants to scroll sideways; labels wrap.
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
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

#if VRC_SDK_VRCSDK3
            tabs.Clear();
            tabs.Add(BridgeElements.Tabs(
                new[] { "Convert a VRChat avatar", "Set up any avatar", "Tools" },
                new[] { "Avatar Icon", "Settings", "CustomTool" },
                (int)mode,
                index => { mode = (Mode)index; ScheduleRebuild(); }));

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
            body.Add(new HelpBox(
                "Converting needs the VRChat SDK, which isn't installed. Setup below still works on any humanoid.",
                HelpBoxMessageType.Info));
            tabs.Clear();
            tabs.Add(BridgeElements.Tabs(
                new[] { "Set up any avatar", "Tools" },
                new[] { "Settings", "CustomTool" },
                (int)mode,
                index => { mode = (Mode)index; ScheduleRebuild(); }));
            if (mode == Mode.Tools) BuildToolsFlow(); else BuildSetupFlow();
#endif
            body.Add(Footer(lastReport != null));
        }

        // ------------------------------------------------------------- tools flow ----

        void BuildToolsFlow()
        {
            body.Add(BridgeElements.Hint(
                "For an avatar already set up for ChilloutVR. Also its own window: Tools ▸ Avatar Bridge ▸ ChilloutVR Toolkit."));
            toolkit = new ToolkitPanel(toolsTarget, true, toolsSource);
            toolkit.Mount(body);
        }

        // ----------------------------------------------------------- convert flow ----

#if VRC_SDK_VRCSDK3
        void BuildConvertFlow()
        {
            // Step 1 sits at the VRChat end of the bridge, step 3 at the ChilloutVR end.
            var pick = new BridgeElements.Card("Pick your VRChat avatar", null, null, 1, 0f);
            BuildAvatarPicker(pick.Body);
            body.Add(pick);

            // Two cards, split by who can answer the question. Everything the avatar decides
            // for itself is folded away under "Automated"; what is left in the open is the
            // short list nothing in the file can settle. Before this, forty-odd toggles sat at
            // one level with no way to tell which of them anyone was expected to think about.
            var choose = new BridgeElements.Card("Choose what gets set up", null, null, 2, 0.5f);
            BuildAnalyseSection(choose.Body);
            // Physics first and open: which solver to convert into is the biggest decision in
            // the window and it is the wearer's, not the avatar's.
            BuildPhysicsCard(choose.Body);
            BuildManualCard(choose.Body);
            BuildAutomatedCard(choose.Body);
            body.Add(choose);

            var run = new BridgeElements.Card("Convert", null, null, 3, 1f);
            BuildConvertButton(run.Body);
            BuildReport(run.Body);
            body.Add(run);
        }

        void BuildAvatarPicker(VisualElement parent)
        {
            var field = new ObjectField("VRChat avatar")
            {
                objectType = typeof(VRCAvatarDescriptor),
                allowSceneObjects = true,
                value = avatar,
                tooltip = "A scene object with a VRC Avatar Descriptor.",
            };
            field.AddToClassList("ab-field");
            field.RegisterValueChangedCallback(e =>
            {
                avatar = e.newValue as VRCAvatarDescriptor;
                // Findings belong to the avatar they were measured on. Keeping them across a
                // swap would show one avatar's shader and PhysBone counts under another's name.
                advice = null;
                adviceFilter = null;
                MatchOptionalLayersToAvatar();
                ScheduleRebuild();
            });
            parent.Add(field);

            if (avatar == null)
            {
                parent.Add(BridgeElements.Hint(
                    "Drag your avatar here from the Hierarchy (the object with the VRC Avatar Descriptor)."));
                return;
            }

            if (VRCFuryBaker.HasFuryComponents(avatar.gameObject))
            {
                parent.Add(new HelpBox(
                    "VRCFury detected: baked with its own builder first, so its features carry over.",
                    HelpBoxMessageType.Info));
            }
            if (ModularAvatarBaker.HasModularAvatarComponents(avatar.gameObject))
            {
                parent.Add(new HelpBox(
                    "Modular Avatar detected: baked through NDMF first, so its features carry over.",
                    HelpBoxMessageType.Info));
            }
        }

        // ------------------------------------------------------------------ analyse ----

        void BuildAnalyseSection(VisualElement parent)
        {
            parent.Add(BridgeElements.Hint(
                "The defaults suit most avatars. Analyse offers the settings this one's contents decide."));

            var button = ReportButton("Analyse this avatar",
                "Reads PhysBones, blendshapes, shaders, parameters and layers. Nothing changes until you apply it.",
                () => { Reanalyse(); ScheduleRebuild(); });
            button.SetEnabled(avatar != null);
            parent.Add(button);

            // The two settings that change nothing, only what you are told.
            // They belong with Analyse, not among the forty that build.
            parent.Add(BridgeElements.SubHeading("What the report tells you"));
            AddReadingOptions(parent);

            if (avatar == null)
            {
                parent.Add(BridgeElements.Hint("Pick an avatar in step 1 to enable this."));
                return;
            }
            if (advice == null)
            {
                return;
            }
            if (advice.Count == 0)
            {
                parent.Add(BridgeElements.Hint(
                    "Nothing to change: the current settings already suit this avatar."));
                return;
            }

            // Action first, agreement last. A list that opens with six green "already set" rows
            // buries the one red one, and the red one is why anybody pressed the button.
            var ordered = new List<Advice>();
            foreach (var kind in new[]
                     {
                         AdviceKind.Blocked, AdviceKind.Change, AdviceKind.Manual,
                         AdviceKind.Confirm, AdviceKind.Inert,
                     })
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

            // Same shape as the conversion report below: banner, chips,
            // rows. Two different-looking lists in one window is two
            // things to learn instead of one.
            int blocked = CountOf(AdviceKind.Blocked);
            int yours = CountOf(AdviceKind.Manual);
            parent.Add(new HelpBox(
                blocked > 0
                    ? $"{blocked} setting(s) can't do what they say on this avatar. See below."
                    : recommendations > 0
                        ? $"{recommendations} setting(s) don't match this avatar."
                        : yours > 0
                            ? $"Everything measurable already matches. {yours} thing(s) are your call."
                            : "Everything measurable already matches this avatar.",
                blocked > 0 ? HelpBoxMessageType.Error
                : recommendations > 0 ? HelpBoxMessageType.Warning
                : HelpBoxMessageType.Info));

            var chips = new VisualElement();
            chips.AddToClassList("ab-row");
            chips.style.flexWrap = Wrap.Wrap;
            AddAdviceChip(chips, AdviceKind.Blocked, "blocked");
            AddAdviceChip(chips, AdviceKind.Change, "recommended");
            AddAdviceChip(chips, AdviceKind.Manual, "your call");
            AddAdviceChip(chips, AdviceKind.Confirm, "already set");
            AddAdviceChip(chips, AdviceKind.Inert, "not needed");
            parent.Add(chips);

            if (recommendations > 1)
            {
                parent.Add(ReportButton($"Apply all {recommendations} recommendations",
                    "Applies the measured ones only, never the \"your call\" rows.",
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
                    }));
            }

            int shown = 0;
            foreach (var a in ordered)
            {
                if (adviceFilter.HasValue && a.Kind != adviceFilter.Value)
                {
                    continue;
                }
                parent.Add(AdviceRow(a, shown % 2 == 1));
                shown++;
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
            if (avatar == null || AvatarAdvisor.LayersDecidedByBaker(avatar.gameObject))
            {
                return;
            }
            foreach (var (type, setting) in AvatarAdvisor.OptionalLayers)
            {
                if (AvatarAdvisor.IsOn(settings, type) && !AvatarAdvisor.SuppliesOwnLayer(avatar, type))
                {
                    AvatarAdvisor.SetOn(settings, type, false);
                    autoOffLayers.Add($"\"{setting}\"");
                }
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

        void AddAdviceChip(VisualElement parent, AdviceKind kind, string noun)
        {
            int count = CountOf(kind);
            bool selected = adviceFilter == kind;
            parent.Add(BridgeElements.Chip($"{count} {noun}", KindColour(kind),
                kind != AdviceKind.Confirm && kind != AdviceKind.Inert,
                () =>
                {
                    adviceFilter = selected ? (AdviceKind?)null : kind;
                    ScheduleRebuild();
                },
                selected,
                count > 0));
        }

        static bool IsRecommendation(Advice a) => a.Apply != null && a.Kind != AdviceKind.Manual;

        VisualElement AdviceRow(Advice a, bool alternate)
        {
            var row = BridgeElements.ReportRow(KindLabel(a.Kind), a.Setting, a.Finding,
                KindColour(a.Kind), alternate);
            if (a.Targets != null && a.Targets.Length > 0)
            {
                row.Add(ReportButton(a.Targets.Length == 1 ? "Show" : $"Show {a.Targets.Length}",
                    a.Targets.Length == 1
                        ? $"Selects \"{a.Targets[0].name}\"."
                        : "Selects all of them, so you can see what the count is made of.",
                    () => Ping(a.Targets)));
            }
            if (a.Apply != null)
            {
                row.Add(ReportButton(a.Kind == AdviceKind.Manual ? "Turn on" : "Apply", null,
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
            return row;
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

        static Color KindColour(AdviceKind kind)
        {
            switch (kind)
            {
                case AdviceKind.Change: return BridgeTheme.Warn;
                case AdviceKind.Confirm: return BridgeTheme.Good;
                case AdviceKind.Inert: return BridgeTheme.Muted;
                case AdviceKind.Manual: return BridgeTheme.CvrOrange;
                default: return BridgeTheme.Bad;
            }
        }

        // Spaces out an enum identifier, the way IMGUI's EnumPopup did.
        static string Nicify(Enum value) =>
            value == null ? string.Empty : ObjectNames.NicifyVariableName(value.ToString());

        static PopupField<string> EnumPopup<T>(string label, string tooltip, T current, Action<T> set)
            where T : Enum
        {
            var values = (T[])Enum.GetValues(typeof(T));
            var names = new System.Collections.Generic.List<string>();
            foreach (var value in values)
            {
                names.Add(Nicify(value));
            }
            int index = Math.Max(0, Array.IndexOf(values, current));

            var popup = new PopupField<string>(label, names, index) { tooltip = tooltip };
            popup.AddToClassList("ab-field");
            popup.RegisterValueChangedCallback(e =>
            {
                int chosen = names.IndexOf(e.newValue);
                if (chosen >= 0)
                {
                    set(values[chosen]);
                }
            });
            return popup;
        }

        void BuildPhysicsCard(VisualElement parent)
        {
            var card = new BridgeElements.Card("Physics",
                showPhysics ? null : "which solver, and how the chains feel",
                showPhysics, null, 0f, open => { showPhysics = open; ScheduleRebuild(); });
            var b = card.Body;

            AddPhysicsOptions(b);
            parent.Add(card);
        }

        void AddPhysicsOptions(VisualElement b)
        {
            b.Add(EnumPopup<PhysicsTarget>("Convert PhysBones to",
                "MagicaCloth2 gives the best result in ChilloutVR; DynamicBone is the built-in fallback.",
                settings.physicsTarget,
                v => { settings.physicsTarget = v; ScheduleRebuild(); }));

            if (settings.physicsTarget == PhysicsTarget.MagicaCloth2 && !BridgeDefines.HasMagicaCloth2)
            {
                b.Add(new HelpBox(
                    "MagicaCloth2 is not installed in this project: import it, or switch to DynamicBone.",
                    HelpBoxMessageType.Warning));
            }
            if (settings.physicsTarget == PhysicsTarget.DynamicBone && !BridgeDefines.HasDynamicBone)
            {
                b.Add(new HelpBox(
                    "DynamicBone is not installed. The free VRLabs Dynamic-Bones-Stub also works for conversion.",
                    HelpBoxMessageType.Warning));
            }

            if (settings.physicsTarget == PhysicsTarget.MagicaCloth2)
            {
                // Only the cloth writer reads it.
                b.Add(BridgeElements.Bind("GrabbyBones mod support",
                    "Names converted physics objects so Kafe's GrabbyBones mod drives the avatar's " +
                    "_IsGrabbed / _Angle grab-reactive logic.",
                    settings.grabbyBonesSupport, v => settings.grabbyBonesSupport = v));
            }
            b.Add(BridgeElements.Bind("Delete PhysBones after converting",
                "Leave on: leftover PhysBone components upset the CCK upload checks.",
                settings.deleteConvertedPhysBones, v => settings.deleteConvertedPhysBones = v));
            // Both writers skip toe chains, so the choice is shown for both.
            b.Add(BridgeElements.Bind("Convert toe PhysBones",
                "Off by default: simulated toes wiggle with every step in ChilloutVR. Skipped chains are listed in the report.",
                settings.convertToePhysBones, v => settings.convertToePhysBones = v));

            if (settings.physicsTarget == PhysicsTarget.MagicaCloth2)
            {
                b.Add(BridgeElements.SubHeading("MagicaCloth2 feel"));
                b.Add(BridgeElements.Hint(
                    "Leave these on. Turn one off only when a chain converts wrong; it falls back to " +
                    "MagicaCloth2's defaults."));

                // Closed by default. These are escape hatches for a
                // chain that came out wrong, not decisions to make.
                var feel = new Foldout { text = "Advanced physics tuning", value = showPhysicsTuning };
                feel.AddToClassList("ab-field");
                feel.RegisterValueChangedCallback(e =>
                {
                    // Foldouts inside carry their own change events up; only the fold's own counts.
                    if (e.target == feel) showPhysicsTuning = e.newValue;
                });
                b.Add(feel);
                var outer = b;
                b = feel.contentContainer;

                b.Add(BridgeElements.Bind("Match a preset to each chain",
                    "Starts each chain from the preset that fits it: hair, tail, skirt, cape, accessory, or a spring by stiffness.",
                    settings.useMagicaPresets, v => settings.useMagicaPresets = v));
                b.Add(BridgeElements.Bind("Fit the preset to the PhysBone",
                    "Carries gravity and immobile across, and zeroes wind, which VRChat never had.",
                    settings.fitToPhysBone, v => settings.fitToPhysBone = v));
                b.Add(BridgeElements.Bind("Derive physics from the PhysBone",
                    "Converts pull, spring and stiffness into damping and angle restoration, derived from both solvers.",
                    settings.derivePhysicsFromPhysBone, v => settings.derivePhysicsFromPhysBone = v));
                b.Add(BridgeElements.Bind("Size particles from the mesh",
                    "Sizes each chain's collision to the mesh it moves, not the preset's one size for all.",
                    settings.fitRadiusToMesh, v => settings.fitRadiusToMesh = v));
                b.Add(BridgeElements.Bind("Size for the largest a slider makes the body",
                    "Collision radius cannot be animated, so it is sized for body sliders at full.",
                    settings.sizePhysicsForLargest, v => settings.sizePhysicsForLargest = v));
                b.Add(BridgeElements.Bind("Fit colliders to the mesh",
                    "Measures the limb each collider sits on and tapers the capsule to it. Off keeps the source's sizes.",
                    settings.fitCollidersToMesh, v => settings.fitCollidersToMesh = v));
                b.Add(BridgeElements.Bind("Bound swing to the source's limit",
                    "Keeps a loose chain within the PhysBone's angle limit, as a distance bound that cannot vibrate.",
                    settings.boundSwingToSourceLimit, v => settings.boundSwingToSourceLimit = v));
                b.Add(BridgeElements.Bind("Cap particle radius to bone spacing",
                    "Bounds each particle to half the gap between its bones. Try it on a long chain of close bones that misbehaves.",
                    settings.capParticleRadius, v => settings.capParticleRadius = v));

                b = outer;   // out of the fold, "Your call" is not advanced, it is a choice
                b.Add(BridgeElements.SubHeading("Your call"));
                b.Add(BridgeElements.Hint(
                    "The avatar can't answer these. Leaving them alone converts fine."));
                b.Add(BridgeElements.Bind("Add physics to toggled rigs that have none",
                    "Gives physics to a toggled rig the author left without a PhysBone, like an add-on hairstyle. " +
                    "Off by default: some are rigid on purpose. The report names them either way.",
                    settings.addPhysicsToRiggedStyles, v => settings.addPhysicsToRiggedStyles = v));
                b.Add(BridgeElements.Bind("Auto-assign nearby colliders",
                    "Lets each cloth collide with body colliders it could swing into, which VRChat did not. Check before uploading.",
                    settings.autoAssignNearbyColliders, v => settings.autoAssignNearbyColliders = v));
            }
        }

        void BuildAutomatedCard(VisualElement parent)
        {
            var card = new BridgeElements.Card("Automated options",
                showAutomated ? null : "set for you from the avatar: physics, face tracking, layers, components",
                showAutomated, null, 0f, open => { showAutomated = open; ScheduleRebuild(); });
            var b = card.Body;

            // Forty-odd settings under six headings, in a card that ships
            // collapsed. Grouping alone does not make one findable.
            var find = new TextField("Find a setting") { value = settingFilter };
            find.AddToClassList("ab-field");
            find.AddToClassList("ab-keep");
            find.RegisterValueChangedCallback(e =>
            {
                settingFilter = e.newValue;
                BridgeElements.Filter(b, settingFilter);
            });
            b.Add(find);

            b.Add(new HelpBox(
                "The avatar decides these, and \"Analyse this avatar\" sets them. Change one only if you know why.",
                HelpBoxMessageType.Warning));

            b.Add(BridgeElements.SubHeading("General"));
            b.Add(BridgeElements.Bind("Work on a clone (recommended)",
                "The original avatar object stays untouched and gets deactivated.",
                settings.cloneAvatar, v => settings.cloneAvatar = v));

            b.Add(BridgeElements.SubHeading("Face tracking"));
            AddFaceTrackingOptions(b);
            // Baking, cleanup, toggle rebuilding and masking always
            // run. Necessary steps are not options.

            b.Add(BridgeElements.SubHeading("Remove VRChat-only systems"));
            b.Add(BridgeElements.Bind("Remove GoGo Loco (recommended)",
                "ChilloutVR has its own locomotion, flight and emotes, which GoGo fights. Untick to keep GoGo's poses.",
                settings.stripGogoLoco, v => { settings.stripGogoLoco = v; ScheduleRebuild(); }));
            if (!settings.stripGogoLoco)
            {
                // GoGo cannot fully function in CVR; it leans on
                // VRChat-only animator primitives. Say so where the
                // decision is made.
                b.Add(BridgeElements.Hint(
                    "⚠ EXPERIMENTAL: GoGo replaces ChilloutVR's locomotion, so tick Base, Additive and Action " +
                    "below or there is none. Poses don't lock movement, floor poses keep a standing viewpoint, " +
                    "and the quick-menu emotes stop."));
            }
#if !AVATARBRIDGE_YAPS
            // No add-on in the project, so "Convert to YAPS" is not an answer
            // this build can give. Offering it and quietly removing instead is
            // the worst of the three.
            b.Add(new HelpBox(
                "Penetration (DPS, TPS, SPS) is removed. The YAPS add-on rebuilds it; install it and the " +
                "choice appears here.", HelpBoxMessageType.Info));
            b.Add(Link("Get the YAPS add-on (GitHub)  ↗", () => Application.OpenURL(BridgeLinks.YapsRepo)));
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
            var penetration = BridgeElements.Choice("Penetration",
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
                });
            b.Add(penetration);
            b.Add(BridgeElements.Hint("DPS, TPS and SPS, with the OGB, PCS and Wholesome stacks that ride with them."));
            // What the other two answers cost, where the choice is made.
            if (settings.stripSpsSystems && !settings.convertYapsSystems)
            {
                b.Add(new HelpBox(
                    "Every plug and socket goes; the plug mesh stays, straight. Only converting again undoes it.",
                    HelpBoxMessageType.Warning));
            }
            else if (!settings.stripSpsSystems)
            {
                b.Add(new HelpBox(
                    "Nothing will bend or open, and the haptics parameters can push the avatar over the sync cap.",
                    HelpBoxMessageType.Warning));
            }
            else if (settings.convertYapsSystems && settings.syncHapticsForOsc)
            {
                b.Add(BridgeElements.Hint(
                    "The OGB haptics stay synced for OSC toys: that is on under Manual options ▸ Opt-ins."));
            }
#endif
#if AVATARBRIDGE_YAPS
            // The other door. The add-on builds and tunes penetration on an
            // avatar already here; this converts. Absent, the card above has
            // already said so and offered the download.
            b.Add(BridgeElements.Hint(
                "Already on ChilloutVR? Tools ▸ YAPS ▸ Setup adds penetration to any avatar or prop."));
#endif
            b.Add(BridgeElements.Bind("Remove animation that can't do anything (recommended)",
                "Curves for material properties a locked shader baked away. They did nothing in VRChat either.",
                settings.stripDeadMaterialAnimation, v => settings.stripDeadMaterialAnimation = v));
            b.Add(BridgeElements.SubHeading("Animator layers to convert"));
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

            b.Add(BridgeElements.SubHeading("Parameters & toggles"));
            b.Add(BridgeElements.Bind("Preserve parameter sync state",
                "Unsynced parameters become local, except ones a menu control drives: others should see what it does.",
                settings.preserveParameterSyncState, v => settings.preserveParameterSyncState = v));
            b.Add(BridgeElements.Bind("Expose menu-less synced parameters",
                "Gives them a settings entry so their values are saved between loads.",
                settings.exposeMenulessSyncedParameters, v => settings.exposeMenulessSyncedParameters = v));

            b.Add(BridgeElements.SubHeading("Components"));
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
                "Shrinks textures to what their mesh can show, in import settings only. \"Put the textures " +
                "back\" undoes it; textures shared outside the avatar are left alone.",
                settings.slimTexturesOnConvert, v => settings.slimTexturesOnConvert = v));

            // Survives a rebuild: the box keeps its text, so the card must
            // come back filtered to match it.
            BridgeElements.Filter(b, settingFilter);
            parent.Add(card);
        }

        void BuildManualCard(VisualElement parent)
        {
            var card = new BridgeElements.Card("Manual options",
                showManual ? null : "the calls only you can make",
                showManual, null, 0f, open => { showManual = open; ScheduleRebuild(); });
            var b = card.Body;

            b.Add(BridgeElements.Hint(
                "The avatar can't answer these. Leaving them alone converts fine."));

            b.Add(BridgeElements.SubHeading("Shaders"));
            b.Add(BridgeElements.Row(
                BridgeElements.Bind("Patch non-SPI shaders for VR",
                    "A shader without stereo support draws into one eye in VR. Patches copies; originals " +
                    "untouched. Check both eyes in game.",
                    settings.patchNonSpiShaders, v => settings.patchNonSpiShaders = v),
                BridgeElements.BetaTag()));

            // Opt-ins live here, not beside the choice they qualify: a
            // feature nobody can find is a feature nobody turns on.
            b.Add(BridgeElements.SubHeading("Opt-ins"));
            var optIns = new VisualElement();
            optIns.style.paddingLeft = 10;
            optIns.style.borderLeftWidth = 2;
            optIns.style.borderLeftColor = new Color(1f, 1f, 1f, 0.10f);
            optIns.style.marginBottom = 6;
            b.Add(optIns);
            optIns.Add(BridgeElements.Hint(
                "Off unless you switch them on, and each says what it costs."));

            optIns.Add(BridgeElements.SubHeading("OSC toys"));
            optIns.Add(BridgeElements.Bind("Keep the OGB / PCS haptics contacts",
                "Toy-app contacts. Plugs and sockets work either way, but these can spend the instance's " +
                "512 contact pairs. Only for driving a toy.",
                settings.keepHapticsContacts,
                v => { settings.keepHapticsContacts = v; ScheduleRebuild(); }));
            if (settings.keepHapticsContacts)
            {
                optIns.Add(new HelpBox(
                    "Blame this if contacts get unreliable in a busy instance.",
                    HelpBoxMessageType.Warning));
            }
            optIns.Add(BridgeElements.Bind("Keep OGB haptics synced (OSCGoesBrrr, Lovense)",
                "On, OSCGoesBrrr finds them automatically, at 32 sync bits each. Off, link them by hand; " +
                "the report lists the names.",
                settings.syncHapticsForOsc, v => { settings.syncHapticsForOsc = v; ScheduleRebuild(); }));
            if (settings.syncHapticsForOsc && !settings.keepHapticsContacts)
            {
                optIns.Add(new HelpBox(
                    "There are no haptics parameters to sync unless the contacts above are kept. " +
                    "This does nothing on its own.", HelpBoxMessageType.Warning));
            }
            if (settings.syncHapticsForOsc)
            {
                optIns.Add(new HelpBox(
                    "About 290 bits per plug or socket, and over 3200 nothing syncs. Launch ChilloutVR " +
                    "with --osc-query-prefix=VRChat-Client.",
                    HelpBoxMessageType.Warning));
                // Without the add-on the answer is no whatever the setting
                // says, which is the one thing the write above used to get
                // right.
#if AVATARBRIDGE_YAPS
                if (!settings.convertYapsSystems)
#endif
                {
                    optIns.Add(new HelpBox(
                        "Does nothing unless Penetration is Convert to YAPS.", HelpBoxMessageType.Info));
                }
            }

#if AVATARBRIDGE_YAPS
            optIns.Add(BridgeElements.SubHeading("Penetration"));
            optIns.Add(BridgeElements.Bind("Show the avatar's OWN depth animations to other players",
                "The bulges the author animated on the body. Off, only you see them; on, everyone, at 32 " +
                "bits per socket.",
                settings.syncSocketDepthForOthers, v => { settings.syncSocketDepthForOthers = v; ScheduleRebuild(); }));
            if (settings.syncSocketDepthForOthers)
            {
                optIns.Add(new HelpBox(
                    "32 bits per depth parameter, and over 3200 nothing syncs. The report's sync budget says where it landed.",
                    HelpBoxMessageType.Warning));
            }
            optIns.Add(BridgeElements.Bind("Draw a debug readout on each plug",
                "An in-game readout on each plug of who found its socket and what its bake is doing. " +
                "EVERYONE SEES IT, and upload is refused while it is on.",
                settings.yapsDebugOverlay, v => { settings.yapsDebugOverlay = v; ScheduleRebuild(); }));
            if (settings.yapsDebugOverlay)
            {
                optIns.Add(new HelpBox(
                    "Turn it off and convert again before you publish.", HelpBoxMessageType.Warning));
            }
#endif

            b.Add(BridgeElements.SubHeading("Menu & extras"));
            b.Add(EnumPopup<ToggleStyle>("Toggle style",
                "Animator Layers: each toggle gets its own layer.\n" +
                "CVR Native Targets: left to the CCK; press \"Create Controller\" on the avatar.",
                settings.toggleStyle, v => settings.toggleStyle = v));
            b.Add(BridgeElements.Bind("Add height scaler  (\"Height\" slider)",
                "A menu slider from 0.25x to 4x the avatar's height, centred on its own size. Held props scale with you.",
                settings.addAvatarScaler, v => settings.addAvatarScaler = v));

            var extra = new TextField("Extra strip keywords")
            {
                value = settings.extraStripKeywords,
                tooltip = "Comma separated parameter prefixes and layer names of other VRChat-only systems to remove.",
            };
            extra.AddToClassList("ab-field");
            extra.RegisterValueChangedCallback(e => settings.extraStripKeywords = e.newValue);
            b.Add(extra);

            var output = new TextField("Output folder")
            {
                value = settings.outputFolder,
                tooltip = "Where assets and the report go, inside Assets. Kept outside the tool's folder so updating never erases them.",
            };
            output.AddToClassList("ab-field");
            output.RegisterValueChangedCallback(e => settings.outputFolder = e.newValue);
            b.Add(output);

            parent.Add(card);
        }

        void BuildConvertButton(VisualElement parent)
        {
            bool ftPackageMissing = settings.faceTrackingMode == FaceTrackingMode.DragonSkyRunner
                                    && !FaceTrackingPackages.IsInstalled();
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
                parent.Add(new HelpBox(
                    "The bundled face-tracking assets are missing: reimport AvatarBridge, or set " +
                    "Face tracking to Native or None.", HelpBoxMessageType.Warning));
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
            var chosen = settings;
            EditorApplication.delayCall += () =>
            {
                try { lastReport = BridgeConverter.Convert(target, chosen); }
                // A filter left from the previous run can select a status the new report has none of,
                // which shows an empty list and an unclickable chip to clear it with.
                finally { converting = false; reportFilter = null; Rebuild(); }
            };
        }
#endif // VRC_SDK_VRCSDK3

        // ------------------------------------------------------------- setup flow ----

        void BuildSetupFlow()
        {
            var pick = new BridgeElements.Card("Pick any avatar", null, null, 1, 0f);
            var field = new ObjectField("Avatar")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                value = setupAvatar,
                tooltip = "Any avatar in the scene. A Humanoid rig gives the best result.",
            };
            field.AddToClassList("ab-field");
            field.RegisterValueChangedCallback(e =>
            {
                setupAvatar = e.newValue as GameObject;
                ScheduleRebuild();
            });
            pick.Body.Add(field);

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
                    pick.Body.Add(new HelpBox(
                        "Not a Humanoid rig: the viewpoint is guessed and eye tracking can't be wired. Set " +
                        "Humanoid in the model's import settings.", HelpBoxMessageType.Warning));
                }
                if (setupAvatar.GetComponent<ABI.CCK.Components.CVRAvatar>() != null)
                {
                    pick.Body.Add(new HelpBox(
                        "It already has a CVRAvatar: its Advanced Avatar Settings are rebuilt from scratch.",
                        HelpBoxMessageType.Warning));
                }
            }
            body.Add(pick);

            var choose = new BridgeElements.Card("Choose what gets set up", null, null, 2, 0.5f);
            choose.Body.Add(BridgeElements.Hint("Viewpoint, visemes and blink are always detected and wired."));
            // Where the converter puts them: what the report TELLS you, kept
            // apart from what gets built.
            choose.Body.Add(BridgeElements.SubHeading("What the report tells you"));
            AddReadingOptions(choose.Body);
            BuildFaceTrackingCard(choose.Body);
            BuildExtrasCard(choose.Body);

            var advanced = new BridgeElements.Card("Advanced options",
                showAdvanced ? null : "output folder, blink",
                showAdvanced, null, 0f, open => { showAdvanced = open; ScheduleRebuild(); });
            AddCommonGeneralOptions(advanced.Body);
            AddBlinkToggle(advanced.Body);
            choose.Body.Add(advanced);
            body.Add(choose);

            var run = new BridgeElements.Card("Set up", null, null, 3, 1f);
            bool ftPackageMissing = settings.faceTrackingMode == FaceTrackingMode.DragonSkyRunner
                                    && !FaceTrackingPackages.IsInstalled();
            var button = new BridgeElements.PrimaryButton(
                setupAvatar == null ? "Set up avatar" : $"Set up \"{setupAvatar.name}\"",
                () =>
                {
                    lastReport = CvrSetup.Run(setupAvatar, settings);
                    // So the setup flow ends where the convert flow does:
                    // with somewhere to go. Setup works on the object it was
                    // given unless it cloned, and Selection is what it leaves
                    // pointing at the result either way.
                    lastReport.ConvertedRoot = Selection.activeGameObject != null
                        && Selection.activeGameObject.GetComponent<ABI.CCK.Components.CVRAvatar>() != null
                        ? Selection.activeGameObject : setupAvatar;
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
                run.Body.Add(new HelpBox(
                    "The bundled face-tracking assets are missing: reimport AvatarBridge, or set " +
                    "Face tracking to Native or None.", HelpBoxMessageType.Warning));
            }
            BuildReport(run.Body);
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

        void AddCommonGeneralOptions(VisualElement parent)
        {
            parent.Add(BridgeElements.Bind("Work on a clone (recommended)",
                "The original avatar object stays untouched and gets deactivated.",
                settings.cloneAvatar, v => settings.cloneAvatar = v));

            var output = new TextField("Output folder")
            {
                value = settings.outputFolder,
                tooltip = "Where assets and the report go, inside Assets. Kept outside the tool's folder so updating never erases them.",
            };
            output.AddToClassList("ab-field");
            output.RegisterValueChangedCallback(e => settings.outputFolder = e.newValue);
            parent.Add(output);
        }

        void AddBlinkToggle(VisualElement parent)
        {
            parent.Add(BridgeElements.Bind("Auto-wire blink blendshapes",
                "Finds blink shapes on the face mesh and turns on ChilloutVR's blinking.",
                settings.wireBlinkBlendshapes, v => settings.wireBlinkBlendshapes = v));
        }


        void BuildExtrasCard(VisualElement parent)
        {
            var card = new BridgeElements.Card("Extras");
            card.Body.Add(BridgeElements.Bind("Add height scaler  (\"Height\" slider)",
                "A menu slider from 0.25x to 4x the avatar's height, centred on its own size. Held props scale with you.",
                settings.addAvatarScaler, v => settings.addAvatarScaler = v));
            parent.Add(card);
        }

        static readonly FaceTrackingMode[] FtModes =
            { FaceTrackingMode.Native, FaceTrackingMode.DragonSkyRunner, FaceTrackingMode.None };

        static readonly string[] FtLabels =
            { "Native CVR Component", "Unity Animator Blendtrees (DSR)", "Keep the avatar's own rig" };

        void BuildFaceTrackingCard(VisualElement parent)
        {
            string summary = settings.faceTrackingMode == FaceTrackingMode.Native ? "native component"
                           : settings.faceTrackingMode == FaceTrackingMode.DragonSkyRunner ? "CVR VRCFT rig"
                           : "avatar's own rig";
            var card = new BridgeElements.Card("Face tracking", summary, showFaceTracking, null, 0f,
                open => showFaceTracking = open);
            AddFaceTrackingOptions(card.Body);
            parent.Add(card);
        }

        void AddFaceTrackingOptions(VisualElement b)
        {
            int index = Mathf.Max(0, Array.IndexOf(FtModes, settings.faceTrackingMode));
            var popup = new PopupField<string>("Face tracking",
                new System.Collections.Generic.List<string>(FtLabels), index)
            {
                tooltip = "Native CVR Component: ChilloutVR's own, a bit stiff.\n" +
                          "Unity Animator Blendtrees (DSR): DragonSkyRunner's rig, smoother.\n" +
                          "Keep the avatar's own rig: converts the existing one untouched.\n" +
                          "The first two replace any rig already there.",
            };
            popup.AddToClassList("ab-field");
            popup.RegisterValueChangedCallback(e =>
            {
                settings.faceTrackingMode = FtModes[Array.IndexOf(FtLabels, e.newValue)];
                ScheduleRebuild();
            });
            b.Add(popup);

            if (settings.faceTrackingMode == FaceTrackingMode.DragonSkyRunner)
            {
                if (FaceTrackingPackages.IsInstalled())
                {
                    b.Add(new HelpBox(
                        "Rebuilds DragonSkyRunner's bundled face and eye tracking rig onto this avatar. Gaze " +
                        "strength may want tuning.", HelpBoxMessageType.Info));
                    b.Add(Link("DragonSkyRunner's package (GitHub)  ↗",
                        () => Application.OpenURL(FaceTrackingPackages.Url)));
                }
                else
                {
                    b.Add(new HelpBox($"The bundled \"{FaceTrackingPackages.DisplayName}\" assets are missing: reimport AvatarBridge.",
                        HelpBoxMessageType.Warning));
                }
            }
        }

        // ------------------------------------------------------------------- report ---

        ReportStatus? reportFilter;

        void AddFilterChip(VisualElement parent, ReportStatus status, string noun, Color colour, bool emphasise)
        {
            int count = lastReport.CountOf(status);
            bool selected = reportFilter == status;
            parent.Add(BridgeElements.Chip($"{count} {noun}", colour, emphasise,
                () =>
                {
                    reportFilter = selected ? (ReportStatus?)null : status;
                    ScheduleRebuild();
                },
                selected,
                count > 0));
        }

        static Color StatusColour(ReportStatus status)
        {
            switch (status)
            {
                case ReportStatus.Error: return BridgeTheme.Bad;
                case ReportStatus.Warning: return BridgeTheme.Warn;
                case ReportStatus.Approximated: return BridgeTheme.Warn;
                case ReportStatus.Converted: return BridgeTheme.Good;
                default: return BridgeTheme.Muted;
            }
        }

        static Button ReportButton(string text, string tooltip, Action action)
        {
            var button = new Button(action) { text = text, tooltip = tooltip };
            button.AddToClassList("ab-btn");
            return button;
        }

        // The end of the flow: pick, analyse, tweak, convert, optimise.
        // Hands the avatar to the tools rather than acting here.
        void BuildSlimButton(VisualElement parent)
        {
            var root = lastReport != null ? lastReport.ConvertedRoot : null;
            var cvr = root != null ? root.GetComponent<ABI.CCK.Components.CVRAvatar>() : null;
            if (cvr == null) return;

            long saved = SavingFor(cvr);
            parent.Add(BridgeElements.Hint(saved > 0
                ? $"About {(saved / 1048576f):0.0} MB of texture can come off with nothing visible changing."
                : "Nothing here is obviously wasteful."));

            // The same size as Convert, so it reads as the next step rather
            // than as one option among the row of links below it. Solid CVR
            // orange: the crossing is done, and this is the far side.
            parent.Add(new BridgeElements.PrimaryButton(
                saved > 0 ? $"Make it lighter: {(saved / 1048576f):0.0} MB to reclaim" : "Optimise this avatar",
                () =>
                {
                    toolsTarget = root;
                toolsSource = SourceObject();
                    mode = Mode.Tools;
                    ScheduleRebuild();
                },
                BridgeTheme.CvrOrange));
        }

        // What "Make it lighter" would reclaim, for the offer on the button.
        // Measured once per report rather than per redraw: a survey and a
        // weight reading of a big avatar is real work, and this runs inside
        // a UI rebuild.
        long SavingFor(ABI.CCK.Components.CVRAvatar cvr)
        {
            if (_savingFor == cvr) return _saving;
            var survey = AvatarSurvey.Build(cvr);
            var plan = AvatarSlimmer.Find(cvr, survey, AvatarWeight.Measure(cvr, survey), SourceObject());
            _savingFor = cvr;
            _saving = plan.Bytes + plan.StripBytes;
            return _saving;
        }

        ABI.CCK.Components.CVRAvatar _savingFor;
        long _saving;

        // The avatar this conversion came from, whose materials point at the
        // same textures and are not somebody else's.
        GameObject SourceObject()
        {
#if VRC_SDK_VRCSDK3
            if (avatar != null) return avatar.gameObject;
#endif
            return setupAvatar;
        }

        void BuildReport(VisualElement parent)
        {
            if (lastReport == null)
            {
                return;
            }
            int errors = lastReport.CountOf(ReportStatus.Error);
            int warnings = lastReport.CountOf(ReportStatus.Warning);

            // Above the verdict, because it is good news and the verdict may
            // not be. The report's own entry lists the textures; this is the
            // number, which is the part worth reading from across the room.
            if (lastReport.BytesReclaimed > 0)
            {
                parent.Add(new HelpBox(
                    $"Optimised on the way through: {(lastReport.BytesReclaimed / 1048576f):0.0} MB of "
                    + "texture nobody was ever going to see, reclaimed. Your graphics card says thank you, "
                    + "and so does everyone standing near you. The report says which textures and what "
                    + "each one became; \"Resize oversized textures\" turns it off for next time.",
                    HelpBoxMessageType.Info));

                // The undo, beside the announcement. This one happens without
                // being asked now, so the way back cannot live in another
                // window: the record is a file in the output folder, and the
                // saved report sits in that same folder.
                string outputDir = string.IsNullOrEmpty(lastReport.SavedReportPath)
                    ? null
                    : System.IO.Path.GetDirectoryName(lastReport.SavedReportPath);
                if (!string.IsNullOrEmpty(outputDir) && AvatarSlimmer.CanRevert(outputDir))
                {
                    parent.Add(new Button(() =>
                    {
                        AvatarSlimmer.Revert(outputDir, lastReport);
                        lastReport.BytesReclaimed = 0;
                        ScheduleRebuild();
                    })
                    { text = "Put the textures back" });
                }
            }

            parent.Add(new HelpBox(
                errors > 0 ? $"Finished with {errors} error(s). See below."
                : warnings > 0 ? $"Done! {warnings} thing(s) may want a look. See below."
                : "Done! The avatar is ready for the CCK's upload checks.",
                errors > 0 ? HelpBoxMessageType.Error
                : warnings > 0 ? HelpBoxMessageType.Warning
                : HelpBoxMessageType.Info));

            BuildSlimButton(parent);

            var chips = new VisualElement();
            chips.AddToClassList("ab-row");
            chips.style.flexWrap = Wrap.Wrap;
            AddFilterChip(chips, ReportStatus.Converted, "done", BridgeTheme.Good, true);
            AddFilterChip(chips, ReportStatus.Approximated, "approximated", BridgeTheme.Warn, false);
            AddFilterChip(chips, ReportStatus.Skipped, "skipped", BridgeTheme.Muted, false);
            AddFilterChip(chips, ReportStatus.Warning, "warnings", BridgeTheme.Warn, warnings > 0);
            AddFilterChip(chips, ReportStatus.Error, "errors", BridgeTheme.Bad, errors > 0);
            parent.Add(chips);

            parent.Add(BridgeElements.Hint(reportFilter.HasValue
                ? $"Showing {reportFilter.Value.ToString().ToLowerInvariant()} only. Click the chip again to go back."
                : "Everything that needs a look. Click a chip to see just those."));

            var actions = new VisualElement();
            actions.AddToClassList("ab-report-row");

            if (!string.IsNullOrEmpty(lastReport.SavedReportPath))
            {
                if (!string.IsNullOrEmpty(lastReport.SavedHtmlPath))
                {
                    actions.Add(ReportButton("Open web report",
                        "The same report as a page: charts, filters, and the technical appendix.",
                        () => EditorUtility.OpenWithDefaultApp(lastReport.SavedHtmlPath)));
                }
                actions.Add(ReportButton("Open full report", null, () =>
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(lastReport.SavedReportPath);
                    if (asset != null) { AssetDatabase.OpenAsset(asset); }
                }));
                actions.Add(ReportButton("Show in Project", null, () =>
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(lastReport.SavedReportPath);
                    if (asset != null) { EditorGUIUtility.PingObject(asset); }
                }));
            }

            if (!string.IsNullOrEmpty(lastReport.StoreDescription))
            {
                actions.Add(ReportButton("Copy description",
                    "A store listing of what the avatar has, with room for your own words first. Also saved as Description.txt.",
                    () =>
                    {
                        EditorGUIUtility.systemCopyBuffer = lastReport.StoreDescription;
                        ShowNotification(new GUIContent("Description copied"));
                    }));

                actions.Add(ReportButton("Fill CCK description",
                    "Types it into the CCK's Description box, if empty. Open the Builder tab with this avatar first.",
                    () =>
                    {
                        var result = CckDescriptionFiller.Fill(lastReport.StoreDescription);
                        ShowNotification(new GUIContent(
                            result == CckDescriptionFiller.Result.Filled
                                ? "Description filled" : "Couldn't fill it"));
                        Debug.Log("[AvatarBridge] " + CckDescriptionFiller.Explain(result));
                    }));
            }

            // Always offered once a report exists, rather than only when something went wrong:
            // "it converted clean but the avatar is wrong in game" is a report worth having, and
            // it's the case where the button used to be missing. The footer drops its copies.
            actions.Add(ReportButton("Report an issue",
                "Opens a pre-filled GitHub issue. Please attach the report: " +
                "most bugs are diagnosed straight from it.",
                () => BridgeLinks.OpenBugReport(lastReport)));
            actions.Add(ReportButton("Copy diagnostics",
                "Copies versions and detected packages to the clipboard.",
                () =>
                {
                    BridgeLinks.CopyDiagnostics(lastReport);
                    ShowNotification(new GUIContent("Diagnostics copied"));
                }));
            actions.Add(ReportButton("Troubleshooting  ↗", "Setup and install help.",
                () => Application.OpenURL(BridgeLinks.Troubleshooting)));
            if (actions.childCount > 0)
            {
                parent.Add(actions);
            }

            // The full list lives in the report file; this shows what
            // the chips select. Default: everything needing a look.
            var list = new ScrollView();
            list.AddToClassList("ab-report-list");
            int shown = 0;
            foreach (var entry in lastReport.Entries)
            {
                bool include = reportFilter.HasValue
                    ? entry.Status == reportFilter.Value
                    : entry.Status != ReportStatus.Converted && entry.Status != ReportStatus.Approximated;
                if (!include)
                {
                    continue;
                }
                var row = BridgeElements.ReportRow(entry.Category, entry.Subject, entry.Detail,
                    StatusColour(entry.Status), shown % 2 == 1);
                // Most subjects are object paths or names. Ones that
                // still resolve become clickable; prose ones do not.
                var found = ResolveSubject(entry.Subject);
                if (found != null)
                {
                    row.Add(ReportButton("Show", $"Selects \"{found.name}\" in the Hierarchy.",
                        () => Ping(new UnityEngine.Object[] { found })));
                }
                list.Add(row);
                shown++;
            }
            // A clean run has nothing to list, and an empty bordered box reads as something that
            // failed to load rather than as good news.
            if (shown > 0)
            {
                parent.Add(list);
            }
        }

        UnityEngine.Object ResolveSubject(string subject)
        {
            var root = lastReport != null ? lastReport.ConvertedRoot : null;
            if (root == null || string.IsNullOrEmpty(subject))
            {
                return null;
            }

            var byPath = root.transform.Find(subject);
            if (byPath != null)
            {
                return byPath.gameObject;
            }

            Transform single = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != subject)
                {
                    continue;
                }
                if (single != null)
                {
                    return null;   // ambiguous, say nothing rather than pick
                }
                single = t;
            }
            return single != null ? single.gameObject : null;
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

        static Button Link(string text, Action action) => BridgeElements.Link(text, action);

        Button DiscordButton()
        {
            string label = BridgeLinks.HasDiscordLink
                ? $"Discord: {BridgeLinks.DiscordUser}"
                : $"Copy Discord: {BridgeLinks.DiscordUser}";

            var button = ReportButton(label,
                BridgeLinks.HasDiscordLink
                    ? "Opens Discord. Best for quick questions; please use GitHub issues for bugs " +
                      "so they don't get lost."
                    : "Copies the handle to your clipboard. Best for quick questions; please use " +
                      "GitHub issues for bugs so they don't get lost.",
                () =>
                {
                    BridgeLinks.OpenDiscord();
                    if (!BridgeLinks.HasDiscordLink)
                    {
                        ShowNotification(new GUIContent("Copied: " + BridgeLinks.DiscordUser));
                    }
                });
            return button;
        }

        VisualElement Footer(bool reportShown)
        {
            var footer = new VisualElement();
            footer.AddToClassList("ab-footer");
            if (!reportShown)
            {
                footer.Add(ReportButton("Troubleshooting  ↗", "Setup and install help.",
                    () => Application.OpenURL(BridgeLinks.Troubleshooting)));
                footer.Add(ReportButton("Report an issue  ↗",
                    "Opens a pre-filled GitHub issue with your versions and detected packages.",
                    () => BridgeLinks.OpenBugReport(lastReport)));
            }
            if (!string.IsNullOrEmpty(BridgeLinks.DiscordUser))
            {
                footer.Add(DiscordButton());
            }
            return footer;
        }
#else
        void CreateGUI()
        {
            var root = rootVisualElement;
            BridgeTheme.ApplySkin(root);
            var sheet = Resources.Load<StyleSheet>("AvatarBridge");
            if (sheet != null)
            {
                root.styleSheets.Add(sheet);
            }

            root.Add(BridgeElements.Banner("AvatarBridge",
                "VRChat → ChilloutVR avatar converter", "v" + BridgeDefines.Version));

            var body = new VisualElement();
            body.AddToClassList("ab-scroll");
            root.Add(body);

            body.Add(new HelpBox(
                "AvatarBridge converts VRChat avatars to ChilloutVR. It needs both SDKs for that:",
                HelpBoxMessageType.Warning));
            body.Add(new Label(
                (BridgeDefines.HasVrcAvatarSdk ? "✔" : "✘") + "  VRChat Avatars SDK (SDK3), to read the avatar"));
            body.Add(new Label(
                (BridgeDefines.HasCck ? "✔" : "✘") + "  ChilloutVR CCK (4.x recommended), always required"));
            body.Add(new HelpBox(
                "Import what's missing and reopen this window. With the CCK alone, Setup mode still works.",
                HelpBoxMessageType.Info));

            var footer = new VisualElement();
            footer.AddToClassList("ab-footer");
            var guide = new Button(() => Application.OpenURL(BridgeLinks.Troubleshooting)) { text = "Setup guide  ↗" };
            guide.AddToClassList("ab-btn");
            var issue = new Button(() => BridgeLinks.OpenBugReport()) { text = "Report an issue  ↗" };
            issue.AddToClassList("ab-btn");
            footer.Add(guide);
            footer.Add(issue);
            body.Add(footer);
        }
#endif
    }
}
