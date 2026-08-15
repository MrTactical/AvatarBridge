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

#if VRC_SDK_VRCSDK3
            // Held rather than added directly: the active tab is styled, so it has to be rebuilt
            // when the mode changes or the highlight goes stale on the tab you just left.
            tabs = new VisualElement();
            root.Add(tabs);
#endif

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

            // Same shape as the conversion report below: banner, chips,
            // rows. Two different-looking lists in one window is two
            // things to learn instead of one.
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
            // Baking, cleanup, toggle rebuilding and masking always
            // run. Necessary steps are not options.

            b.Add(BridgeElements.SubHeading("Remove VRChat-only systems"));
            b.Add(BridgeElements.Bind("Remove GoGo Loco (recommended)",
                "CVR has its own locomotion, flight and emotes. GoGo's layers fight them and " +
                "waste ~15 synced parameters. Untick to KEEP GoGo's poses and dances — they " +
                "live in the Base and Action layers, which must then be merged too (the hint " +
                "below appears until they are).",
                settings.stripGogoLoco, v => { settings.stripGogoLoco = v; ScheduleRebuild(); }));
            if (!settings.stripGogoLoco)
            {
                // GoGo cannot fully function in CVR; it leans on
                // VRChat-only animator primitives. Say so where the
                // decision is made.
                b.Add(BridgeElements.Hint(
                    "⚠ Keeping GoGo Loco is EXPERIMENTAL: GoGo fully replaces ChilloutVR's own " +
                    "locomotion (that layer is removed), so Base, Additive and Action must be " +
                    "ticked under \"Animator layers to convert\" below, or the avatar has no " +
                    "locomotion at all. Known limits ChilloutVR cannot express: poses don't lock movement " +
                    "(walking mid-pose slides), the viewpoint stays at standing height in floor " +
                    "poses, and CVR's quick-menu emotes won't animate — GoGo's wheel replaces " +
                    "them. Removing GoGo remains the recommended path."));
            }
            // Two settings, one question. "Remove SPS" beside "Convert SPS
            // to YAPS" read as a contradiction — remove it AND convert it? —
            // when they are one decision with three answers: rebuild the
            // penetration as YAPS, remove it, or leave VRChat's in place. The
            // fourth combination the two ticks allowed (convert without
            // removing) was never coherent, so it is not offered.
            // Radio buttons, not a dropdown: they stack like the toggles
            // around them, and a dropdown clipped its own caption against
            // the label column.
            var penetration = BridgeElements.Choice("Penetration",
                "What happens to the avatar's penetration system — Raliv DPS, Thry TPS, VRCFury SPS, " +
                "and OGB, PCS and Wholesome that ride with them.\n\n" +
                "Convert to YAPS: keeps it and rebuilds it for ChilloutVR — plugs bend, sockets open, " +
                "and the author's own tuning (curvature, squeeze, idle shrink, the lot) carries across. " +
                "YAPS is written from scratch, no VRChat code shipped, and it reads and is read by " +
                "everything already on the platform: your plug finds DPS, TPS and SPS sockets, and " +
                "their plugs find your sockets. A plug only ever bends toward a socket — never toward " +
                "a person carrying none.\n\n" +
                "Remove: takes the whole system out. Its shaders, contacts and parameters do not work " +
                "in ChilloutVR and cost most of the sync budget.\n\n" +
                "Leave as VRChat built it: converts nothing and removes nothing. It will not function " +
                "in ChilloutVR; only for looking at what was there.",
                new[] { "Convert to YAPS (recommended)", "Remove", "Leave as VRChat built it (won't work)" },
                settings.stripSpsSystems ? (settings.convertYapsSystems ? 0 : 1) : 2,
                choice =>
                {
                    settings.stripSpsSystems = choice != 2;
                    settings.convertYapsSystems = choice == 0;
                });
            b.Add(penetration);
            b.Add(BridgeElements.Hint("DPS, TPS and SPS, and the OGB, PCS and Wholesome stacks that ride with them."));
            // The other door. The YAPS tool builds and tunes penetration on
            // an avatar already here; this converts. Present, say where it
            // is; absent, say where to get it.
            if (BridgeLinks.HasYapsTool)
            {
                b.Add(BridgeElements.Hint(
                    "Already on ChilloutVR? Tools ▸ YAPS ▸ Setup adds, tunes or upgrades penetration on any " +
                    "avatar or prop — same system, same shader. This converts; that builds."));
            }
            else
            {
                b.Add(BridgeElements.Hint(
                    "Already on ChilloutVR? The YAPS tool adds, tunes or upgrades penetration on any avatar " +
                    "or prop — same system, same shader. This converts; that builds. It is not in this project."));
                b.Add(Link("Get the YAPS tool (GitHub)  ↗", () => Application.OpenURL(BridgeLinks.YapsRepo)));
            }
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
            if (autoOffLayers.Count > 0)
            {
                b.Add(BridgeElements.Hint(
                    $"{string.Join(" and ", autoOffLayers)} switched off: this avatar has no layer " +
                    "of its own in " + (autoOffLayers.Count == 1 ? "that slot" : "those slots") +
                    ". These settings persist between avatars, so the tick came from another one. " +
                    "Nothing converts differently either way — tick it back if you want it on."));
            }

            b.Add(BridgeElements.SubHeading("Parameters & toggles"));
            b.Add(BridgeElements.Bind("Preserve parameter sync state",
                "Non-synced VRC parameters get CVR's '#' local-only prefix — except ones a menu " +
                "control drives, which sync regardless. VRChat's tight budget made de-syncing " +
                "menu parameters a common trick (VRCFury carries them through machinery that " +
                "doesn't survive conversion), and a control others can't see the effect of is a " +
                "broken feature. CVR's 3200-bit budget can afford them.",
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
            b.Add(BridgeElements.Bind("Grow contact zones with the body's sliders",
                "A zone authored on a body part a blendshape slider can grow keeps its authored " +
                "size while the mesh grows past it — so the touch lands inside the body, short of " +
                "the zone, and the contact reads as broken at high slider values. Each zone is " +
                "measured like the physics sizes are: the mesh around it at rest and with every " +
                "animated shape at full reach. A zone one slider grows follows that slider live — " +
                "authored size at rest, the measured growth at full reach; only growth spread " +
                "across several shapes holds the grown size instead. The report names each zone " +
                "and what was done.",
                settings.sizeContactZonesForLargest, v => settings.sizeContactZonesForLargest = v));

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

        void BuildManualCard(VisualElement parent)
        {
            var card = new BridgeElements.Card("Manual options",
                showManual ? null : "the calls only you can make",
                showManual, null, 0f, open => { showManual = open; ScheduleRebuild(); });
            var b = card.Body;

            b.Add(BridgeElements.Hint(
                "These are yours: the avatar doesn't say which way they should go, so nothing " +
                "sets them for you. Leaving them all alone converts fine."));

            b.Add(BridgeElements.SubHeading("Shaders"));
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
                    return null;   // ambiguous — say nothing rather than pick
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
