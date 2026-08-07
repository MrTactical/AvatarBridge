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
    /// <summary>
    /// The AvatarBridge control panel.
    ///
    /// Two modes, because only half of what this tool does actually needs VRChat:
    ///   Convert  — a VRChat avatar into a ChilloutVR one (needs the VRChat SDK).
    ///   Set up   — prepare ANY humanoid for ChilloutVR (needs only the CCK).
    /// With no VRChat SDK installed the window still works; it just offers Setup.
    ///
    /// Built with UI Toolkit, which is what both the CCK and the VRChat SDK use — matching them
    /// is the point, since this window's job is to sit between the two. See BridgeTheme for where
    /// the colours come from.
    /// </summary>
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
#if VRC_SDK_VRCSDK3
        // Mode only exists when there's a choice to make: without the VRChat SDK the
        // window is Setup-only, so there's nothing to switch between.
        enum Mode { Convert, Setup }
        Mode mode = Mode.Convert;
        VRCAvatarDescriptor avatar;
#endif
        GameObject setupAvatar;

        [SerializeField] BridgeSettings settings = new BridgeSettings();
        BridgeReport lastReport;
#if VRC_SDK_VRCSDK3
        // Set while a deferred conversion is in flight, so the button can't queue a second one.
        // Convert-mode only, like the two below: ungated it warns in every CCK-only project.
        bool converting;
#endif

#if VRC_SDK_VRCSDK3
        // Convert-mode only, so without the VRChat SDK nothing reads these — and an ungated
        // field earns a CS0414 "assigned but never used" in every CCK-only project. A warning
        // in a tester's console reads like something is wrong with the tool.
        bool showManual = true;
        bool showAutomated;
        bool showPhysics = true;
        /// <summary>Closed by default: these are escape hatches for a chain that converted wrong,
        /// not a set of decisions to be made before the first conversion.</summary>
        bool showPhysicsTuning = false;
        // What the last Analyse found, or null if it hasn't been run for the current avatar.
        // Cleared whenever the avatar changes: advice about a different avatar is worse than none.
        List<Advice> advice;
#endif
        bool showFaceTracking = true;
        bool showAdvanced;

        VisualElement body;
#if VRC_SDK_VRCSDK3
        VisualElement tabs;
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
            }
            // Saved settings from before 2.59.0 carry the old default, which pointed INSIDE
            // the tool's folder — where a delete-and-reimport update erases every conversion.
            // Only the old DEFAULT is rewritten; a deliberately customised path is the user's.
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
            // If it didn't load, everything below still works — it just looks plain. An unstyled
            // window that converts beats a styled one that doesn't exist.

            root.Add(BridgeElements.Banner("AvatarBridge",
                "VRChat → ChilloutVR avatar converter", "v" + BridgeDefines.Version));

#if VRC_SDK_VRCSDK3
            // Held rather than added directly: the active tab is styled, so it has to be rebuilt
            // when the mode changes or the highlight goes stale on the tab you just left.
            tabs = new VisualElement();
            root.Add(tabs);
#endif

            var scroll = new ScrollView();
            scroll.AddToClassList("ab-scroll");
            // Nothing here wants to scroll sideways — long tooltips and labels wrap. Without this
            // a narrow window grows a horizontal bar that only ever gets in the way.
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            root.Add(scroll);

            body = new VisualElement();
            scroll.Add(body);
            Rebuild();
        }

        /// <summary>
        /// Rebuilds on the next frame rather than immediately.
        ///
        /// Every caller is a control's own value-changed callback, and a rebuild destroys the
        /// control that is still dispatching it. Deferring by one frame lets the event finish
        /// against a live element first. It also stops a text field losing focus mid-keystroke.
        /// </summary>
        void ScheduleRebuild()
        {
            rootVisualElement?.schedule.Execute(Rebuild);
        }

        /// <summary>
        /// Rebuilds the body. Cheap enough to do wholesale, and it keeps every "when this changes,
        /// that section looks different" case correct without a web of update callbacks.
        /// </summary>
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
                new[] { "Convert a VRChat avatar", "Set up any avatar" },
                new[] { "Avatar Icon", "Settings" },
                (int)mode,
                index => { mode = (Mode)index; ScheduleRebuild(); }));

            if (mode == Mode.Convert)
            {
                BuildConvertFlow();
            }
            else
            {
                BuildSetupFlow();
            }
#else
            body.Add(new HelpBox(
                "AvatarBridge's main job is converting VRChat avatars, which needs the VRChat SDK — it isn't " +
                "installed, so that's unavailable here (a VRChat avatar's components can't be read without it).\n\n" +
                "Setup mode below still works: it does the ChilloutVR-side setup on any humanoid.",
                HelpBoxMessageType.Info));
            BuildSetupFlow();
#endif
            body.Add(Footer(lastReport != null));
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
                    "VRCFury detected — it will be baked with VRCFury's own builder first, so all Fury " +
                    "features (toggles, clothing, menus) carry over.",
                    HelpBoxMessageType.Info));
            }
            if (ModularAvatarBaker.HasModularAvatarComponents(avatar.gameObject))
            {
                parent.Add(new HelpBox(
                    "Modular Avatar detected — it will be baked via NDMF first, so MA features " +
                    "(merged armature, menus, outfits) carry over.",
                    HelpBoxMessageType.Info));
            }
        }

        // ------------------------------------------------------------------ analyse ----

        /// <summary>
        /// Reads the avatar and offers the settings its own contents decide.
        ///
        /// Greyed out rather than hidden until an avatar is picked: a control that isn't drawn
        /// yet teaches nobody that the step exists, and someone who never learns this button is
        /// here is exactly the person who converts on defaults that didn't suit their avatar.
        ///
        /// Nothing is applied without a press. The analysis is a measurement and a
        /// recommendation, and the two are shown separately — the reader can see what was found
        /// before deciding to act on it, which also means a wrong recommendation is arguable
        /// rather than silent.
        /// </summary>
        void BuildAnalyseSection(VisualElement parent)
        {
            parent.Add(BridgeElements.Hint(
                "The defaults suit most avatars — you can convert without changing anything here. " +
                "Analysing reads this avatar and offers the settings its own contents decide."));

            var button = ReportButton("Analyse this avatar",
                "Reads the avatar as it sits in the scene — PhysBones, blendshapes, shaders, " +
                "parameters and layers — and offers the settings those decide. Nothing changes " +
                "until you apply it.",
                () => { Reanalyse(); ScheduleRebuild(); });
            button.SetEnabled(avatar != null);
            parent.Add(button);

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
                    "Nothing to change — the current settings already suit this avatar."));
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

            // Deliberately the same shape as the conversion report below — banner, then chips,
            // then rows. They answer the same kind of question at opposite ends of the job, and
            // two different-looking lists in one window is two things to learn instead of one.
            int blocked = CountOf(AdviceKind.Blocked);
            int yours = CountOf(AdviceKind.Manual);
            parent.Add(new HelpBox(
                blocked > 0
                    ? $"{blocked} setting(s) can't do what they say on this avatar — see below."
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
                    "Applies the measured ones only. The \"your call\" rows are never included — " +
                    "nothing in the avatar says which way those should go.",
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

        /// <summary>
        /// Which kind the findings are filtered to, or null for all of them.
        ///
        /// Unlike the conversion report this defaults to showing EVERYTHING, including the rows
        /// that agree. The report hides its successes because there are hundreds of them; there
        /// are six findings here, and "already set" is load-bearing — it is the difference
        /// between "checked, and it's right" and "never looked at".
        /// </summary>
        AdviceKind? adviceFilter;

        /// <summary>
        /// Re-measures and drops any filter with it. Applying one setting can change what the
        /// others should be, so every path that changes a setting comes back through here rather
        /// than marking a row done — and a filter left pointing at a kind the new findings have
        /// none of shows an empty list with an unclickable chip to clear it.
        /// </summary>
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

        /// <summary>
        /// A recommendation is something measured. A Manual row also carries an Apply — it is a
        /// one-press shortcut for a decision the reader has just made — but it must never be
        /// swept up by "apply all", or the tool would be quietly deciding the questions it just
        /// finished saying it cannot answer.
        /// </summary>
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

        /// <summary>Spaces out an enum identifier, the way IMGUI's EnumPopup did.</summary>
        static string Nicify(Enum value) =>
            value == null ? string.Empty : ObjectNames.NicifyVariableName(value.ToString());

        /// <summary>
        /// A dropdown over an enum showing spaced-out names. EnumField would be the obvious
        /// choice but has no format hooks in 2022.3, so it shows the raw identifier —
        /// "MagicaCloth2" where IMGUI gave "Magica Cloth 2".
        /// </summary>
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

        /// <summary>
        /// Physics in one place, and out in the open.
        ///
        /// It used to be split across both cards: the solver choice and the feel defaults under
        /// "Automated", the departures from the source under "Manual". That split is true on
        /// paper — some of these the avatar answers and some it cannot — but it is the wrong cut
        /// for the person using the window. Which solver an avatar converts into is not something
        /// the avatar decides; it depends on what the wearer has installed and what they want,
        /// and burying it behind a warning that says "you don't need to touch anything in here"
        /// is exactly backwards. Everything downstream of that choice belongs beside it.
        /// </summary>
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
                    "MagicaCloth2 is not installed in this project — import it, or switch to DynamicBone.",
                    HelpBoxMessageType.Warning));
            }
            if (settings.physicsTarget == PhysicsTarget.DynamicBone && !BridgeDefines.HasDynamicBone)
            {
                b.Add(new HelpBox(
                    "DynamicBone is not installed. The free VRLabs Dynamic-Bones-Stub also works for conversion.",
                    HelpBoxMessageType.Warning));
            }

            b.Add(BridgeElements.Bind("GrabbyBones mod support",
                "Names converted physics objects so Kafe's GrabbyBones mod drives the avatar's " +
                "_IsGrabbed / _Angle grab-reactive logic.",
                settings.grabbyBonesSupport, v => settings.grabbyBonesSupport = v));
            b.Add(BridgeElements.Bind("Delete PhysBones after converting",
                "Leave on — leftover PhysBone components upset the CCK upload checks.",
                settings.deleteConvertedPhysBones, v => settings.deleteConvertedPhysBones = v));

            if (settings.physicsTarget == PhysicsTarget.MagicaCloth2)
            {
                b.Add(BridgeElements.SubHeading("MagicaCloth2 feel"));
                b.Add(BridgeElements.Hint(
                    "Every one of these is on, and on is what you want — each is either a measurement " +
                    "of your own avatar or a conversion of the PhysBone's numbers, and turning one off " +
                    "gives that part of the chain back to MagicaCloth2's generic defaults. They are here " +
                    "to be turned off when a specific chain converts wrong, not to be chosen between. " +
                    "Whatever they do is named in the report."));

                // Closed by default. Eight checkboxes nobody should have to reason about on a first
                // conversion is a wall, and reading it as a decision to make is the wrong reading —
                // these are escape hatches for a chain that came out wrong.
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
                    "Start each chain from the MagicaCloth2 preset that fits it — hair, tail, skirt, " +
                    "cape or accessory by name, otherwise a soft/middle/hard spring chosen by how " +
                    "firmly the PhysBone held its rest pose. Turn off to give every chain " +
                    "MagicaCloth2's global defaults instead.",
                    settings.useMagicaPresets, v => settings.useMagicaPresets = v));
                b.Add(BridgeElements.Bind("Fit the preset to the PhysBone",
                    "After the preset loads, apply the few PhysBone facts that mean the same " +
                    "thing in MagicaCloth2: a chain with no gravity keeps none, negative gravity " +
                    "points up, immobile becomes world influence (MagicaCloth2 measures the " +
                    "same thing the other way round), and wind influence goes to zero because " +
                    "VRChat has no wind — ChilloutVR worlds do, and it would move the chain in a " +
                    "way it never moved before. Each adjustment is named in the report.",
                    settings.fitToPhysBone, v => settings.fitToPhysBone = v));
                b.Add(BridgeElements.Bind("Derive physics from the PhysBone",
                    "Convert each chain's pull, spring and stiffness into MagicaCloth2's damping and " +
                    "angle restoration, replacing the preset's feel. Both systems turned out to move " +
                    "positions with per-step values at a fixed rate — 60 Hz against 90 Hz — so the " +
                    "conversion is derived from both solvers rather than guessed, and confirmed " +
                    "against a full avatar in ChilloutVR. Turn it off to get the preset's feel back.",
                    settings.derivePhysicsFromPhysBone, v => settings.derivePhysicsFromPhysBone = v));
                b.Add(BridgeElements.Bind("Size particles from the mesh",
                    "MagicaCloth2's radius is the collision body of a simulated bone. Left alone it " +
                    "is whatever the matched preset shipped — the same size on a breast as on a hair " +
                    "strand — so collision in game covers a fraction of what you can see. This measures " +
                    "the mesh those bones actually move and sizes each chain to it. The source " +
                    "PhysBone's own radius is not used: in VRChat it only governs contact with PhysBone " +
                    "colliders, so it is routinely near zero.",
                    settings.fitRadiusToMesh, v => settings.fitRadiusToMesh = v));
                b.Add(BridgeElements.Bind("Size for the largest a slider makes the body",
                    "A body slider grows the mesh, but MagicaCloth2's radius is fixed — of its " +
                    "parameters only pose ratio, gravity, damping, inertia, wind and blend weight " +
                    "can be animated at all, so collision is right at one slider position and " +
                    "wrong at the rest. This measures the mesh again with every animated " +
                    "blendshape pushed as far as the animator can take it and keeps the larger " +
                    "reading, so collision covers the body when the slider is up and is a little " +
                    "generous when it is down. Shapes that SHRINK the body cost nothing — the " +
                    "saved reading simply wins. Turn it off to size for the avatar as saved.",
                    settings.sizePhysicsForLargest, v => settings.sizePhysicsForLargest = v));
                b.Add(BridgeElements.Bind("Fit colliders to the mesh",
                    "A PhysBone collider is one radius from end to end, so an author covering a " +
                    "thigh has to choose between fitting the hip and fitting the knee. " +
                    "MagicaCloth2's capsule takes a start and an end radius separately, so the " +
                    "converted one can taper the way the limb does. This measures the body part " +
                    "the collider sits on and fits the capsule to it. The measurement replaces " +
                    "the source's numbers, because a PhysBone collider's size is invisible in " +
                    "VRChat unless something collides with it and is routinely one default " +
                    "stamped onto every collider on the avatar. Only the bone's own vertices are " +
                    "read, so a leg collider can only come out leg-sized. The report gives the " +
                    "before and after for each; turn this off to keep the source's dimensions.",
                    settings.fitCollidersToMesh, v => settings.fitCollidersToMesh = v));
                b.Add(BridgeElements.Bind("Bound swing to the source's limit",
                    "A PhysBone's angle limit is often the only thing keeping a deliberately loose " +
                    "chain presentable — convert the looseness without it and the chain swings much " +
                    "further here than it ever did in VRChat. This bounds how far each bone may " +
                    "travel from rest, worked out from that limit and the chain's length. It is a " +
                    "distance bound rather than an angle limit, so it removes motion instead of " +
                    "adding a restoring force and cannot set the chain vibrating.",
                    settings.boundSwingToSourceLimit, v => settings.boundSwingToSourceLimit = v));
                b.Add(BridgeElements.Bind("Cap particle radius to bone spacing",
                    "Bounds each particle to half the gap between its bones. Off by default now " +
                    "that the radius above is measured from the mesh rather than guessed: on a " +
                    "soft-body chain, where two or three bones carry a large volume, this throws " +
                    "away most of that measurement. The overlap it guards against only bites with " +
                    "self-collision, which MagicaCloth2 leaves off. Turn it on if a long chain of " +
                    "closely-spaced bones misbehaves.",
                    settings.capParticleRadius, v => settings.capParticleRadius = v));

                b = outer;   // out of the fold — "Your call" is not advanced, it is a choice
                b.Add(BridgeElements.SubHeading("Your call"));
                b.Add(BridgeElements.Hint(
                    "The avatar doesn't answer these — each either departs from the source or turns on " +
                    "intent only you know. Leaving them alone converts fine."));
                b.Add(BridgeElements.Bind("Convert toe PhysBones",
                    "Off by default: simulated toes wiggle with every step in ChilloutVR, which " +
                    "reads as broken rather than expressive. Chains on or under the humanoid Toes " +
                    "bones (or named like toes) are skipped and listed in the report. Turn on if " +
                    "this avatar's toe physics are deliberate.",
                    settings.convertToePhysBones, v => settings.convertToePhysBones = v));
                b.Add(BridgeElements.Bind("Add physics to toggled rigs that have none",
                    "Some avatars ship a toggled style (usually an add-on hairstyle) whose " +
                    "container carries its own bone rig and mesh but NO PhysBone — rigid in " +
                    "VRChat, whether by intent or oversight. This synthesizes a MagicaCloth for " +
                    "such rigs, preset chosen by the chain classifier, wired to the style's " +
                    "toggle. Off by default because it invents physics the author never made, " +
                    "and some rigged props are rigid on purpose. The report names every rig " +
                    "this would apply to either way.",
                    settings.addPhysicsToRiggedStyles, v => settings.addPhysicsToRiggedStyles = v));
                b.Add(BridgeElements.Bind("Auto-assign nearby colliders",
                    "Also give each cloth the avatar's own colliders that it starts clear of and " +
                    "could swing into — so a tail that passed through the leg in VRChat collides " +
                    "with it here. This improves on the original avatar rather than copying it, so " +
                    "check the result before uploading. Every assignment is listed in the report.",
                    settings.autoAssignNearbyColliders, v => settings.autoAssignNearbyColliders = v));
            }
        }

        /// <summary>
        /// Everything the avatar decides for itself, folded away behind a warning.
        ///
        /// These are not "advanced" in the sense of being for experts — they are settings with a
        /// right answer that this avatar already gives, which is exactly why they should not be
        /// the first thing anyone reads. The old flat list of forty-odd toggles gave no way to
        /// tell the two kinds apart, so people either changed nothing and hoped, or changed the
        /// wrong ones. Analyse sets these; opening the card is for overriding it deliberately.
        /// </summary>
        void BuildAutomatedCard(VisualElement parent)
        {
            var card = new BridgeElements.Card("Automated options",
                showAutomated ? null : "set for you from the avatar — physics, face tracking, layers, components",
                showAutomated, null, 0f, open => { showAutomated = open; ScheduleRebuild(); });
            var b = card.Body;

            b.Add(new HelpBox(
                "These are decided by the avatar itself, and \"Analyse this avatar\" sets them to " +
                "match it. You don't need to touch anything in here — changing one means overriding " +
                "what was measured, so only do it if you know why this avatar is the exception.",
                HelpBoxMessageType.Warning));

            b.Add(BridgeElements.SubHeading("General"));
            b.Add(BridgeElements.Bind("Work on a clone (recommended)",
                "The original avatar object stays untouched and gets deactivated.",
                settings.cloneAvatar, v => settings.cloneAvatar = v));

            b.Add(BridgeElements.SubHeading("Face tracking"));
            AddFaceTrackingOptions(b);
            // VRCFury/Modular Avatar baking, VRC-component cleanup, Fury toggle rebuilding and
            // humanoid masking used to be toggles here. Every off-state produced a conversion
            // that was broken or read as broken — an avatar still wearing its VRC descriptor
            // convinced even the maintainer that "it just didn't convert". Necessary steps
            // aren't options; they always run now.

            b.Add(BridgeElements.SubHeading("Remove VRChat-only systems"));
            b.Add(BridgeElements.Bind("Remove GoGo Loco (recommended)",
                "CVR has its own locomotion, flight and emotes. GoGo's layers fight them and " +
                "waste ~15 synced parameters. Untick to KEEP GoGo's poses and dances — they " +
                "live in the Base and Action layers, which must then be merged too (the hint " +
                "below appears until they are).",
                settings.stripGogoLoco, v => { settings.stripGogoLoco = v; ScheduleRebuild(); }));
            if (!settings.stripGogoLoco)
            {
                // The verdict after several tester rounds and a client decompile: GoGo cannot
                // fully function in ChilloutVR. Its pose/flight machinery leans on VRChat-only
                // primitives with no CVR equivalent — VRCAnimatorLocomotionControl (poses would
                // slide with the capsule), TemporaryPoseSpace (viewpoint shifts, removed at
                // conversion), PlayableLayerControl — and CVR's own IK overrides limbs wherever
                // no tracking-control existed to convert. CVR ships locomotion, emotes, AFK and
                // flight natively. Say all of this where the decision is made.
                b.Add(BridgeElements.Hint(
                    "⚠ Keeping GoGo Loco is EXPERIMENTAL: GoGo fully replaces ChilloutVR's own " +
                    "locomotion (that layer is removed), so Base, Additive and Action must be " +
                    "ticked under \"Animator layers to convert\" below, or the avatar has no " +
                    "locomotion at all. Known limits ChilloutVR cannot express: poses don't lock movement " +
                    "(walking mid-pose slides), the viewpoint stays at standing height in floor " +
                    "poses, and CVR's quick-menu emotes won't animate — GoGo's wheel replaces " +
                    "them. Removing GoGo remains the recommended path."));
            }
            b.Add(BridgeElements.Bind("Remove SPS / OGB / PCS / Wholesome (recommended)",
                "VRChat-specific systems whose shaders, contacts and parameters do not function in CVR.",
                settings.stripSpsSystems, v => settings.stripSpsSystems = v));
            b.Add(BridgeElements.Bind("Remove animation that can't do anything (recommended)",
                "Curves writing to material properties the shader doesn't have — the signature of a " +
                "locked Poiyomi shader that baked them away. They do nothing here and did nothing in " +
                "VRChat either, so removing them keeps dead sliders and toggles out of your menu " +
                "instead of leaving controls that move and change nothing. Renderers whose materials " +
                "an animation swaps are never touched, and only the conversion's own copies of the " +
                "clips are edited. The report names everything removed.",
                settings.stripDeadMaterialAnimation, v => settings.stripDeadMaterialAnimation = v));
            b.Add(BridgeElements.SubHeading("Animator layers to convert"));
            b.Add(BridgeElements.Bind("FX (toggles, expressions)", null,
                settings.convertFxLayer, v => settings.convertFxLayer = v));
            b.Add(BridgeElements.Bind("Gesture (hand poses)", null,
                settings.convertGestureLayer, v => settings.convertGestureLayer = v));
            b.Add(BridgeElements.Bind("Base / locomotion",
                "Brings across what VRChat kept in its Base layer — object toggles, blendshapes, materials, " +
                "parameters, additive motion — and GRAFTS the avatar's own walking, crouching, crawling, " +
                "falling and stance animations into ChilloutVR's locomotion layer, matched by their position " +
                "in the movement blend trees. The layer itself is masked off the body: merged in it would sit " +
                "above ChilloutVR's locomotion and replace it rather than add to it, killing the movement " +
                "sliders and stance buttons — so the structure stays ChilloutVR's while the animations become " +
                "the avatar's, loop settings matched to each slot. A flight pose lands on ChilloutVR's own " +
                "flight mode, which answers speed and movement itself. VRChat's proxy_* placeholder clips " +
                "are skipped; those live in the VRChat client, and ChilloutVR's own animation set is this " +
                "platform's version of them.",
                settings.convertBaseLayer, v => { settings.convertBaseLayer = v; ScheduleRebuild(); }));
            b.Add(BridgeElements.Bind("Additive", null,
                settings.convertAdditiveLayer, v => settings.convertAdditiveLayer = v));
            b.Add(BridgeElements.Bind("Action (emotes, AFK)",
                "VRC emote triggers have no CVR equivalent; states may be unreachable.",
                settings.convertActionLayer, v => { settings.convertActionLayer = v; ScheduleRebuild(); }));

            b.Add(BridgeElements.SubHeading("Parameters & toggles"));
            b.Add(BridgeElements.Bind("Preserve parameter sync state",
                "Non-synced VRC parameters get CVR's '#' local-only prefix.",
                settings.preserveParameterSyncState, v => settings.preserveParameterSyncState = v));
            b.Add(BridgeElements.Bind("Expose menu-less synced parameters",
                "Synced parameters without a menu control still get an Advanced Avatar Settings " +
                "entry. They would sync either way — CVR takes that from the animator — but " +
                "without an entry the value isn't saved to your avatar profile between loads.",
                settings.exposeMenulessSyncedParameters, v => settings.exposeMenulessSyncedParameters = v));

            b.Add(BridgeElements.SubHeading("Components"));
            b.Add(BridgeElements.Bind("Convert contact senders/receivers", null,
                settings.convertContacts, v => { settings.convertContacts = v; ScheduleRebuild(); }));
            b.Add(BridgeElements.Bind("Recreate built-in VRC colliders as pointers",
                "Head/hands/fingers pointers so converted receivers keep reacting to other players.",
                settings.createDefaultColliderPointers, v => settings.createDefaultColliderPointers = v));

            b.Add(BridgeElements.Bind("Convert VRC constraints", null,
                settings.convertConstraints, v => settings.convertConstraints = v));
            b.Add(BridgeElements.Bind("Convert VRC Head Chop",
                "First-person show/hide, including its toggle animations.",
                settings.convertHeadChop, v => settings.convertHeadChop = v));
            b.Add(BridgeElements.Bind("Convert spatial audio", null,
                settings.convertSpatialAudio, v => settings.convertSpatialAudio = v));
            AddBlinkToggle(b);

            parent.Add(card);
        }

        /// <summary>
        /// The options nothing in the avatar file can settle.
        ///
        /// Every one of these is either a departure from the source avatar (inventing physics an
        /// author never made, improving on collisions they never wired), a judgement about intent
        /// that only they know (were the toe physics deliberate?), a workflow choice about how
        /// YOU finish the avatar (toggle style decides whether "Create Controller" is needed), or
        /// something whose result can only be judged by wearing it (a patched shader compiles;
        /// whether it looks right is a different question). Analyse counts the evidence for each
        /// and says which way it leans — it never ticks them for you.
        /// </summary>
        void BuildManualCard(VisualElement parent)
        {
            var card = new BridgeElements.Card("Manual options",
                showManual ? null : "the calls only you can make",
                showManual, null, 0f, open => { showManual = open; ScheduleRebuild(); });
            var b = card.Body;

            b.Add(BridgeElements.Hint(
                "These are yours: the avatar doesn't say which way they should go, so nothing " +
                "sets them for you. Leaving them all alone converts fine."));

            b.Add(BridgeElements.SubHeading("Contacts & shaders"));
            var native = BridgeElements.Bind("Use ChilloutVR's native contacts",
                "Converts contacts one-to-one onto ChilloutVR's own contact components instead " +
                "of approximating them with pointers and triggers: real proximity, box shapes and " +
                "collision tags kept as-is.\n\n" +
                "WHAT A NATIVE CONTACT SWITCHES ON, ONLY YOU WILL SEE. The native system writes " +
                "its parameter straight at the Animator, and the sync cache only fills when " +
                "something writes through the avatar's animator manager, so the value never " +
                "leaves your machine.\n\n" +
                "An effect left permanently ON still appears for everyone — every client is " +
                "already running it and it never needed the parameter. Only what the contact has " +
                "to switch on is lost. The legacy pointer/trigger path writes through the manager " +
                "and does sync, which is why it is the default.\n\n" +
                "The components also aren't in the CCK: AvatarBridge declares them itself, " +
                "verified field-for-field against the decompiled game client. Nothing obliges " +
                "ChilloutVR to keep them as they are, so an avatar built on them can be broken by " +
                "a client update. Take them only for a shape or receiver type the legacy triggers " +
                "cannot do.",
                settings.useNativeContacts, v => { settings.useNativeContacts = v; ScheduleRebuild(); });
            native.SetEnabled(settings.convertContacts);
            b.Add(BridgeElements.Row(native, BridgeElements.BetaTag()));
            if (!settings.convertContacts)
            {
                b.Add(BridgeElements.Hint(
                    "Contact conversion is off under Automated options, so this does nothing."));
            }
            if (settings.convertContacts && settings.useNativeContacts)
            {
                b.Add(new HelpBox(
                    "WHAT A NATIVE CONTACT SWITCHES ON, ONLY YOU WILL SEE. The native system " +
                    "writes its parameter straight at the Animator, and ChilloutVR only sends a " +
                    "value written through the avatar's animator manager — so the value never " +
                    "leaves your machine and nobody else's copy is ever told to play the effect.\n\n" +
                    "This is not occasional, and it is not random. An effect left permanently ON " +
                    "still appears for everyone — not because it syncs, but because every client " +
                    "is already running it and it never needed the parameter. An effect the " +
                    "contact has to switch on appears for you alone. That is why two effects on " +
                    "the same avatar behave differently.\n\n" +
                    "The legacy pointer/trigger path writes through the manager and does sync, " +
                    "which is why it is the default. Confirmed in game, both ways.\n\n" +
                    "Experimental — this also talks to a component internal to the game, not the " +
                    "CCK, so any ChilloutVR update can break it, possibly for good.",
                    HelpBoxMessageType.Warning));

                b.Add(BridgeElements.Bind("Let native contacts reach other players",
                    "Fixes the problem above. The contact is pointed at a local parameter and a " +
                    "small animator layer copies it into the original name with a driver — and a " +
                    "driver's writes DO go out over the network, because they go through the " +
                    "avatar's animator manager. Every animation still reads the name it always " +
                    "read, so nothing else about the avatar changes.\n\n" +
                    "This costs no sync bits. The parameter a contact drives is already declared " +
                    "and already counted against ChilloutVR's 3200-bit budget — it has been " +
                    "transmitted all along, just carrying a value nothing ever wrote.\n\n" +
                    "On/off contacts only. A proximity contact reports how close the toucher is, " +
                    "and a driver can only write that range in steps — so it is left exactly as " +
                    "it is rather than trading the smooth value you see now for a stepped one " +
                    "other people can see. The report names any it left.\n\n" +
                    "Receivers the author marked local-only are left alone.",
                    settings.syncNativeContacts, v => settings.syncNativeContacts = v));
            }

            b.Add(BridgeElements.Row(
                BridgeElements.Bind("Patch non-SPI shaders for VR",
                    "Shaders that don't support single-pass instanced stereo draw into one eye only in " +
                    "VR. This copies them into RehomedAssets with the required macros added and points " +
                    "this avatar's materials at the copies — the originals are never modified, and a " +
                    "copy that fails to compile is thrown away. Only plainly written vertex/fragment " +
                    "shaders can be patched; anything else is reported instead. ChilloutVR renders " +
                    "single-pass instanced where VRChat renders double-wide, so a shader can look fine " +
                    "in VRChat and lose an eye here — the patched copy stays valid under both. Check it " +
                    "in both eyes: compilation is verified, appearance isn't.",
                    settings.patchNonSpiShaders, v => settings.patchNonSpiShaders = v),
                BridgeElements.BetaTag()));

            b.Add(BridgeElements.SubHeading("Menu & extras"));
            b.Add(EnumPopup<ToggleStyle>("Toggle style",
                "Animator Layers: every toggle gets its own Off/On layer and works immediately.\n" +
                "CVR Native Targets: object toggles are left to the CCK's own builder " +
                "(you must press \"Create Controller\" on the avatar).",
                settings.toggleStyle, v => settings.toggleStyle = v));
            b.Add(BridgeElements.Bind("Add height scaler  (\"Height\" slider)",
                "A smooth avatar scaler: a quick-menu slider covering 0.25×–4× of this avatar's " +
                "measured height geometrically, with dead centre = exactly its original size (the default, so " +
                "it spawns unchanged). Props held by a parent constraint — hats, held items — are re-anchored " +
                "so they scale with you instead of drifting off; the report lists any it had to leave alone.",
                settings.addAvatarScaler, v => settings.addAvatarScaler = v));

            var extra = new TextField("Extra strip keywords")
            {
                value = settings.extraStripKeywords,
                tooltip = "Comma separated. Each is used as a parameter prefix and a layer-name match " +
                          "for additional VRC-only systems to remove.",
            };
            extra.AddToClassList("ab-field");
            extra.RegisterValueChangedCallback(e => settings.extraStripKeywords = e.newValue);
            b.Add(extra);

            var output = new TextField("Output folder")
            {
                value = settings.outputFolder,
                tooltip = "Where generated assets and the report go. Must be inside Assets. " +
                          "The default is deliberately OUTSIDE the tool's folder, so deleting " +
                          "Assets/AvatarBridge to update it can never erase conversions.",
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
                    "The bundled face-tracking assets are missing — reimport AvatarBridge, or set " +
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
                    "Drag any avatar here from the Hierarchy — it doesn't need to be a VRChat avatar."));
            }
            else
            {
                var animator = setupAvatar.GetComponent<Animator>();
                if (animator == null || !animator.isHuman)
                {
                    pick.Body.Add(new HelpBox(
                        "This isn't a Humanoid rig. Setup still runs, but the viewpoint is estimated from the " +
                        "mesh bounds and eye tracking can't be wired. Set the rig to Humanoid in the model's " +
                        "import settings for a proper result.", HelpBoxMessageType.Warning));
                }
                if (setupAvatar.GetComponent<ABI.CCK.Components.CVRAvatar>() != null)
                {
                    pick.Body.Add(new HelpBox(
                        "This avatar already has a CVRAvatar component. Setup will reconfigure it — its " +
                        "Advanced Avatar Settings are rebuilt from scratch.", HelpBoxMessageType.Warning));
                }
            }
            body.Add(pick);

            var choose = new BridgeElements.Card("Choose what gets set up", null, null, 2, 0.5f);
            choose.Body.Add(BridgeElements.Hint("Viewpoint, visemes and blink are always detected and wired."));
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
                    "The bundled face-tracking assets are missing — reimport AvatarBridge, or set " +
                    "Face tracking to Native or None.", HelpBoxMessageType.Warning));
            }
            BuildReport(run.Body);
            body.Add(run);
        }

        // ---------------------------------------------------------- shared sections ----

        void AddCommonGeneralOptions(VisualElement parent)
        {
            parent.Add(BridgeElements.Bind("Work on a clone (recommended)",
                "The original avatar object stays untouched and gets deactivated.",
                settings.cloneAvatar, v => settings.cloneAvatar = v));

            var output = new TextField("Output folder")
            {
                value = settings.outputFolder,
                tooltip = "Where generated assets and the report go. Must be inside Assets. " +
                          "The default is deliberately OUTSIDE the tool's folder, so deleting " +
                          "Assets/AvatarBridge to update it can never erase conversions.",
            };
            output.AddToClassList("ab-field");
            output.RegisterValueChangedCallback(e => settings.outputFolder = e.newValue);
            parent.Add(output);
        }

        void AddBlinkToggle(VisualElement parent)
        {
            parent.Add(BridgeElements.Bind("Auto-wire blink blendshapes",
                "Detect blink blendshapes on the face mesh (e.g. \"Blink L\"/\"Blink R\") and turn on " +
                "CVR's Eye Blink Settings.",
                settings.wireBlinkBlendshapes, v => settings.wireBlinkBlendshapes = v));
        }


        void BuildExtrasCard(VisualElement parent)
        {
            var card = new BridgeElements.Card("Extras");
            card.Body.Add(BridgeElements.Bind("Add height scaler  (\"Height\" slider)",
                "A smooth avatar scaler: a quick-menu slider covering 0.25×–4× of this avatar's " +
                "measured height geometrically, with dead centre = exactly its original size (the default, so " +
                "it spawns unchanged). Props held by a parent constraint — hats, held items — are re-anchored " +
                "so they scale with you instead of drifting off; the report lists any it had to leave alone.",
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

        /// <summary>
        /// The face-tracking control without a card around it. Setup mode gives it its own card;
        /// in Convert mode it is one section of the automated list, because which mode fits is
        /// something the avatar's own blendshapes and parameters answer.
        /// </summary>
        void AddFaceTrackingOptions(VisualElement b)
        {
            int index = Mathf.Max(0, Array.IndexOf(FtModes, settings.faceTrackingMode));
            var popup = new PopupField<string>("Face tracking",
                new System.Collections.Generic.List<string>(FtLabels), index)
            {
                tooltip = "The two set-up modes replace any face-tracking rig already on the avatar.\n\n" +
                          "Native CVR Component: ChilloutVR's built-in CVRFaceTracking drives the " +
                          "blendshapes directly. Self-contained, but a bit stiff.\n\n" +
                          "Unity Animator Blendtrees (DSR): DragonSkyRunner's bundled rig — face shapes " +
                          "driven by animator blend trees, eye tracking via generated empties and rotation " +
                          "constraints, rebuilt onto this avatar automatically. Smoother and more expressive.\n\n" +
                          "Keep the avatar's own rig: nothing is stripped — the existing FT rig " +
                          "(Jerry's, Pawlygon, OSCmooth setups…) converts with the rest of the animator. " +
                          "Smoothing proxies VRChat never synced automatically become '#' local (zero " +
                          "sync cost), synced FT parameters keep syncing. This used to be labelled " +
                          "\"None\", which undersold it.",
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
                        "Injects DragonSkyRunner's CVR Eye & Face Tracking rig (bundled) and rebuilds it onto " +
                        "this avatar — including an auto-generated eye-tracking rig. Eye gaze strength may want " +
                        "tuning per the package readme. Credit: DragonSkyRunner.", HelpBoxMessageType.Info));
                    b.Add(Link("DragonSkyRunner's package (GitHub)  ↗",
                        () => Application.OpenURL(FaceTrackingPackages.Url)));
                }
                else
                {
                    b.Add(new HelpBox($"The bundled \"{FaceTrackingPackages.DisplayName}\" assets weren't " +
                        "found — reimport AvatarBridge (the button is disabled until then).",
                        HelpBoxMessageType.Warning));
                }
            }
        }

        // ------------------------------------------------------------------- report ---

        /// <summary>
        /// Which status the report list is filtered to, or null for the default view — everything
        /// that isn't a plain success. Clicking the selected chip again clears it.
        /// </summary>
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

        void BuildReport(VisualElement parent)
        {
            if (lastReport == null)
            {
                return;
            }
            int errors = lastReport.CountOf(ReportStatus.Error);
            int warnings = lastReport.CountOf(ReportStatus.Warning);

            parent.Add(new HelpBox(
                errors > 0 ? $"Finished with {errors} error(s) — see below."
                : warnings > 0 ? $"Done! {warnings} thing(s) may want a look — see below."
                : "Done! The avatar is ready for the CCK's upload checks.",
                errors > 0 ? HelpBoxMessageType.Error
                : warnings > 0 ? HelpBoxMessageType.Warning
                : HelpBoxMessageType.Info));

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
                ? $"Showing {reportFilter.Value.ToString().ToLowerInvariant()} only — click the chip again to go back."
                : "Everything that needs a look. Click a chip to see just those."));

            var actions = new VisualElement();
            actions.AddToClassList("ab-report-row");

            if (!string.IsNullOrEmpty(lastReport.SavedReportPath))
            {
                if (!string.IsNullOrEmpty(lastReport.SavedHtmlPath))
                {
                    actions.Add(ReportButton("Open web report",
                        "The same report as a page — charts, filters, and the technical appendix.",
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
                    "Puts a ready-made store listing on your clipboard for the CCK's Description " +
                    "box — what the avatar has, counted from this conversion. It opens with a gap " +
                    "for your own words, so it reads as the footer of your description rather than " +
                    "all of it. Also saved beside the report as Description.txt.",
                    () =>
                    {
                        EditorGUIUtility.systemCopyBuffer = lastReport.StoreDescription;
                        ShowNotification(new GUIContent("Description copied"));
                    }));

                actions.Add(ReportButton("Fill CCK description",
                    "Types it straight into the Content Manager's Description box. Open the CCK " +
                    "Control Panel on the Builder tab with this avatar selected first. It won't " +
                    "touch the box if you've already written something there.",
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
                "Opens a pre-filled GitHub issue. Please attach the report — " +
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

            // The full list lives in the report file; this shows whatever the chips select. By
            // default that's everything needing a look — an unfiltered dump would be hundreds of
            // "converted fine" lines with the useful entries lost in them.
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
                // Most subjects are an object path or an object name — the field's own comment
                // says so — so the ones that still resolve on the converted avatar become
                // clickable for free, and prose subjects simply don't and get no button.
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

        /// <summary>
        /// Turns a report entry's subject back into the object it is about, or null.
        ///
        /// Two ways, both exact: the subject as a path from the converted root, then as the name
        /// of exactly one descendant. "Exactly one" is the whole safety rule — an avatar with
        /// four objects called "Body" cannot tell you which one an entry meant, and selecting the
        /// wrong one is worse than offering nothing, because the reader believes it.
        ///
        /// Never guesses, never fuzzy-matches, never strips punctuation to try harder. A subject
        /// that is a sentence resolves to nothing and the row keeps its old shape.
        /// </summary>
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
                    return null;   // ambiguous — say nothing rather than pick
                }
                single = t;
            }
            return single != null ? single.gameObject : null;
        }

        /// <summary>
        /// Selects and pings, so the object is both highlighted in place and left selected for
        /// whatever the reader wants to do to it next.
        /// </summary>
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

        static Button Link(string text, Action action)
        {
            var button = new Button(action) { text = text };
            button.AddToClassList("ab-btn");
            return button;
        }

        Button DiscordButton()
        {
            string label = BridgeLinks.HasDiscordLink
                ? $"Discord: {BridgeLinks.DiscordUser}"
                : $"Copy Discord: {BridgeLinks.DiscordUser}";

            var button = ReportButton(label,
                BridgeLinks.HasDiscordLink
                    ? "Opens Discord. Best for quick questions — please use GitHub issues for bugs " +
                      "so they don't get lost."
                    : "Copies the handle to your clipboard. Best for quick questions — please use " +
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

        /// <summary>
        /// The footer only carries what the report's own button row isn't already showing.
        /// Two "Report an issue" buttons a few pixels apart, doing the same thing, is worse than
        /// either one alone.
        /// </summary>
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
                (BridgeDefines.HasVrcAvatarSdk ? "✔" : "✘") + "  VRChat Avatars SDK (SDK3)  — to read the avatar"));
            body.Add(new Label(
                (BridgeDefines.HasCck ? "✔" : "✘") + "  ChilloutVR CCK (4.x recommended)  — always required"));
            body.Add(new HelpBox(
                "Import the missing package(s), let Unity recompile, and reopen this window. " +
                "With just the CCK you can still use Setup mode to prepare any avatar for ChilloutVR.",
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
