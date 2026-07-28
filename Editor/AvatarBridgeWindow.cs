using System;
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
        // Physics is convert-mode only, so without the VRChat SDK nothing reads this — and an
        // ungated field earns a CS0414 "assigned but never used" in every CCK-only project.
        // A warning in a tester's console reads like something is wrong with the tool.
        bool showPhysics = true;
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
            root.AddToClassList(EditorGUIUtility.isProSkin ? "dark" : "light");

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

            var choose = new BridgeElements.Card("Choose what gets set up", null, null, 2, 0.5f);
            choose.Body.Add(BridgeElements.Hint(
                "The defaults suit most avatars — you can convert without changing anything here."));
            BuildPhysicsCard(choose.Body);
            BuildFaceTrackingCard(choose.Body);
            BuildExtrasCard(choose.Body);
            BuildAdvancedCard(choose.Body);
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
                parent.Add(new HelpBox(settings.bakeVrcFury
                        ? "VRCFury detected — it will be baked with VRCFury's own builder first, so all Fury " +
                          "features (toggles, clothing, menus) carry over."
                        : "VRCFury detected, but baking is disabled (Advanced options)! Every Fury-driven " +
                          "feature will be MISSING from the result.",
                    settings.bakeVrcFury ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning));
            }
            if (ModularAvatarBaker.HasModularAvatarComponents(avatar.gameObject))
            {
                parent.Add(new HelpBox(settings.bakeModularAvatar
                        ? "Modular Avatar detected — it will be baked via NDMF first, so MA features " +
                          "(merged armature, menus, outfits) carry over."
                        : "Modular Avatar detected, but baking is disabled (Advanced options)! MA-driven " +
                          "features will be MISSING from the result.",
                    settings.bakeModularAvatar ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning));
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

        void BuildPhysicsCard(VisualElement parent)
        {
            // Same nicified text as the dropdown below, so the collapsed summary and the open
            // control never disagree about what the setting is called.
            string summary = settings.physicsTarget == PhysicsTarget.None
                ? "not converted" : Nicify(settings.physicsTarget);
            var card = new BridgeElements.Card("Physics", summary, showPhysics, null, 0f,
                open => showPhysics = open);
            var b = card.Body;

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
                    "Bones, colliders and ignored transforms come from the PhysBone. The feel starts from " +
                    "MagicaCloth2's own tuned values, and \"Derive physics\" below replaces it with a real " +
                    "conversion of the PhysBone's numbers. Either way they're all in the report."));

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
                b.Add(BridgeElements.Bind("Cap particle radius to bone spacing",
                    "MagicaCloth2's radius is the particle size, not just a collision radius, so " +
                    "particles wider than the gap between bones shove each other apart. Leave on " +
                    "unless chains come out feeling too thin.",
                    settings.capParticleRadius, v => settings.capParticleRadius = v));
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
                b.Add(BridgeElements.Bind("Transfer angle limits",
                    "Copy each PhysBone's limit angle onto the cloth. MagicaCloth2's limit pushes on " +
                    "particle positions rather than bone rotation, at a stiffness that snaps back " +
                    "hard — so this shakes some avatars and is the best result the tool gives on " +
                    "others. Worth trying if chains feel loose; lower Angle Limit > Stiffness on any " +
                    "chain that snaps.",
                    settings.transferAngleLimits, v => settings.transferAngleLimits = v));
                b.Add(BridgeElements.Bind("Auto-assign nearby colliders",
                    "Also give each cloth the avatar's own colliders that it starts clear of and " +
                    "could swing into — so a tail that passed through the leg in VRChat collides " +
                    "with it here. This improves on the original avatar rather than copying it, so " +
                    "check the result before uploading. Every assignment is listed in the report.",
                    settings.autoAssignNearbyColliders, v => settings.autoAssignNearbyColliders = v));
            }
            parent.Add(card);
        }

        void BuildAdvancedCard(VisualElement parent)
        {
            var card = new BridgeElements.Card("Advanced options",
                showAdvanced ? null : "baking, stripping, layers, components",
                showAdvanced, null, 0f, open => { showAdvanced = open; ScheduleRebuild(); });
            var b = card.Body;

            b.Add(BridgeElements.SubHeading("General"));
            AddCommonGeneralOptions(b);
            b.Add(BridgeElements.Bind("Bake VRCFury first (recommended)",
                "Runs VRCFury's own 'Build a Test Copy' pipeline before converting.",
                settings.bakeVrcFury, v => { settings.bakeVrcFury = v; ScheduleRebuild(); }));
            b.Add(BridgeElements.Bind("Bake Modular Avatar first (recommended)",
                "For MA avatars without VRCFury: runs NDMF's manual bake before converting. " +
                "MA+VRCFury avatars are already covered by the VRCFury bake.",
                settings.bakeModularAvatar, v => { settings.bakeModularAvatar = v; ScheduleRebuild(); }));
            b.Add(BridgeElements.Bind("Delete VRC components after conversion", null,
                settings.deleteVrcComponents, v => settings.deleteVrcComponents = v));

            b.Add(BridgeElements.SubHeading("Remove VRChat-only systems"));
            b.Add(BridgeElements.Bind("Remove GoGo Loco (recommended)",
                "CVR has its own locomotion, flight and emotes. GoGo's layers fight them and " +
                "waste ~15 synced parameters. Untick to KEEP GoGo's poses and dances — they " +
                "live in the Base and Action layers, which must then be merged too (the hint " +
                "below appears until they are).",
                settings.stripGogoLoco, v => { settings.stripGogoLoco = v; ScheduleRebuild(); }));
            if (!settings.stripGogoLoco && (!settings.convertBaseLayer || !settings.convertActionLayer))
            {
                // Keeping GoGo with its home layers unmerged converts the menus but not the
                // states they drive — a dance wheel full of dead entries, indistinguishable
                // from a bug. Say so where the decision is being made.
                b.Add(BridgeElements.Hint(
                    "⚠ Keeping GoGo Loco: its poses and dances live in the BASE and ACTION layers, " +
                    "which are currently not merged — the pose wheel would convert but drive " +
                    "nothing. Tick \"Base\" and \"Action\" under \"Animator layers to merge\" in " +
                    "Advanced, or GoGo comes through as menus without motion."));
            }
            b.Add(BridgeElements.Bind("Remove SPS / OGB / PCS / Wholesome (recommended)",
                "VRChat-specific systems whose shaders, contacts and parameters do not function in CVR.",
                settings.stripSpsSystems, v => settings.stripSpsSystems = v));
            var extra = new TextField("Extra strip keywords")
            {
                value = settings.extraStripKeywords,
                tooltip = "Comma separated. Each is used as a parameter prefix and a layer-name match " +
                          "for additional VRC-only systems to remove.",
            };
            extra.AddToClassList("ab-field");
            extra.RegisterValueChangedCallback(e => settings.extraStripKeywords = e.newValue);
            b.Add(extra);

            b.Add(BridgeElements.SubHeading("Animator layers to convert"));
            b.Add(BridgeElements.Bind("FX (toggles, expressions)", null,
                settings.convertFxLayer, v => settings.convertFxLayer = v));
            b.Add(BridgeElements.Bind("Gesture (hand poses)", null,
                settings.convertGestureLayer, v => settings.convertGestureLayer = v));
            b.Add(BridgeElements.Bind("Base / locomotion",
                "Usually better left to CVR's own locomotion; enable only for custom locomotion avatars.",
                settings.convertBaseLayer, v => { settings.convertBaseLayer = v; ScheduleRebuild(); }));
            b.Add(BridgeElements.Bind("Additive", null,
                settings.convertAdditiveLayer, v => settings.convertAdditiveLayer = v));
            b.Add(BridgeElements.Bind("Action (emotes, AFK)",
                "VRC emote triggers have no CVR equivalent; states may be unreachable.",
                settings.convertActionLayer, v => { settings.convertActionLayer = v; ScheduleRebuild(); }));

            b.Add(BridgeElements.SubHeading("Parameters & toggles"));
            b.Add(BridgeElements.Bind("Rebuild VRCFury toggles (recommended)",
                "Pulls toggles out of VRCFury's merged blend tree so each one is a readable, " +
                "working toggle instead of float math.",
                settings.nativizeObjectToggles, v => { settings.nativizeObjectToggles = v; ScheduleRebuild(); }));
            var style = EnumPopup<ToggleStyle>("Toggle style",
                "Animator Layers: every toggle gets its own Off/On layer and works immediately.\n" +
                "CVR Native Targets: object toggles are left to the CCK's own builder " +
                "(you must press \"Create Controller\" on the avatar).",
                settings.toggleStyle, v => settings.toggleStyle = v);
            style.SetEnabled(settings.nativizeObjectToggles);
            b.Add(style);
            b.Add(BridgeElements.Bind("Preserve parameter sync state",
                "Non-synced VRC parameters get CVR's '#' local-only prefix.",
                settings.preserveParameterSyncState, v => settings.preserveParameterSyncState = v));
            b.Add(BridgeElements.Bind("Expose menu-less synced parameters",
                "Synced parameters without a menu control still get an Advanced Avatar Settings " +
                "entry. They would sync either way — CVR takes that from the animator — but " +
                "without an entry the value isn't saved to your avatar profile between loads.",
                settings.exposeMenulessSyncedParameters, v => settings.exposeMenulessSyncedParameters = v));
            b.Add(BridgeElements.Bind("Integer hand-pose gestures",
                "Selects discrete gestures via GestureLeftIdx/RightIdx; the analog fist " +
                "(trigger-pressure finger curl) stays on the float.",
                settings.integerHandGestures, v => settings.integerHandGestures = v));

            b.Add(BridgeElements.SubHeading("Components"));
            b.Add(BridgeElements.Bind("Convert contact senders/receivers", null,
                settings.convertContacts, v => { settings.convertContacts = v; ScheduleRebuild(); }));
            b.Add(BridgeElements.Bind("Recreate built-in VRC colliders as pointers",
                "Head/hands/fingers pointers so converted receivers keep reacting to other players.",
                settings.createDefaultColliderPointers, v => settings.createDefaultColliderPointers = v));

            var native = BridgeElements.Bind("Use ChilloutVR's native contacts",
                "Converts contacts one-to-one onto ChilloutVR's own contact components instead " +
                "of approximating them with pointers and triggers: real proximity and collision " +
                "tags kept as-is. Contacts are per-client by design — every client simulates " +
                "every avatar's contacts itself, so reactions work over the network without " +
                "costing sync bits (confirmed in game). The components aren't in the CCK, so " +
                "AvatarBridge declares them itself, verified field-for-field against the " +
                "decompiled game client — the only layout that matters, since the client is " +
                "what reads the uploaded avatar.",
                settings.useNativeContacts, v => { settings.useNativeContacts = v; ScheduleRebuild(); });
            native.SetEnabled(settings.convertContacts);
            b.Add(BridgeElements.Row(native, BridgeElements.BetaTag()));
            if (settings.convertContacts && settings.useNativeContacts)
            {
                b.Add(new HelpBox(
                    "Experimental — this talks to a component internal to the game, not the CCK, " +
                    "so any ChilloutVR update can break it, possibly for good. Treat it as a " +
                    "bonus, not something the avatar depends on.",
                    HelpBoxMessageType.Info));
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

            b.Add(BridgeElements.Bind("Mask merged layers off the humanoid rig",
                "Fixes the \"bicycle pose\" — an avatar stuck in a bent rest pose in game while " +
                "only the head and hands follow you. VRChat keeps FX on its own playable layer so " +
                "it can never write humanoid muscles; ChilloutVR runs one controller, where a " +
                "merged layer can, and then fights locomotion for the body every frame. This puts " +
                "that separation back. Confirmed in game. Layers that animate the body on purpose " +
                "are left alone, so it is safe to try on any avatar — object toggles, blendshapes " +
                "and material animation are unaffected.",
                settings.maskMergedLayers, v => settings.maskMergedLayers = v));
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
                tooltip = "Where generated assets and the report go. Must be inside Assets.",
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
                "it spawns unchanged).",
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
            card.Body.Add(popup);

            if (settings.faceTrackingMode == FaceTrackingMode.DragonSkyRunner)
            {
                if (FaceTrackingPackages.IsInstalled())
                {
                    card.Body.Add(new HelpBox(
                        "Injects DragonSkyRunner's CVR Eye & Face Tracking rig (bundled) and rebuilds it onto " +
                        "this avatar — including an auto-generated eye-tracking rig. Eye gaze strength may want " +
                        "tuning per the package readme. Credit: DragonSkyRunner.", HelpBoxMessageType.Info));
                    card.Body.Add(Link("DragonSkyRunner's package (GitHub)  ↗",
                        () => Application.OpenURL(FaceTrackingPackages.Url)));
                }
                else
                {
                    card.Body.Add(new HelpBox($"The bundled \"{FaceTrackingPackages.DisplayName}\" assets weren't " +
                        "found — reimport AvatarBridge (the button is disabled until then).",
                        HelpBoxMessageType.Warning));
                }
            }
            parent.Add(card);
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
                list.Add(BridgeElements.ReportRow(entry.Category, entry.Subject, entry.Detail,
                    StatusColour(entry.Status), shown % 2 == 1));
                shown++;
            }
            // A clean run has nothing to list, and an empty bordered box reads as something that
            // failed to load rather than as good news.
            if (shown > 0)
            {
                parent.Add(list);
            }
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
            root.AddToClassList(EditorGUIUtility.isProSkin ? "dark" : "light");
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
