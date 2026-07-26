using UnityEditor;
using UnityEngine;
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
    /// </summary>
    public class AvatarBridgeWindow : EditorWindow
    {
        const string PrefsKey = "AvatarBridge.Settings";

        [MenuItem("Tools/Avatar Bridge/VRChat to ChilloutVR Converter")]
        static void Open()
        {
            var window = GetWindow<AvatarBridgeWindow>();
            window.titleContent = new GUIContent("AvatarBridge");
            window.minSize = new Vector2(400, 540);
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
        Vector2 scroll;
        Vector2 reportScroll;
#if VRC_SDK_VRCSDK3
        bool showPhysics = true;   // PhysBone conversion is convert-mode only
#endif
        bool showFaceTracking = true;
        bool showAdvanced;

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

        // ------------------------------------------------------------------ styling ----

        static GUIStyle _title, _subtitle, _step, _rich;
        static void EnsureStyles()
        {
            if (_title != null)
            {
                return;
            }
            _title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 };
            _subtitle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 11 };
            _step = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            _rich = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true };
        }

        static void Separator()
        {
            GUILayout.Space(8);
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.25f));
            GUILayout.Space(8);
        }

        static void StepHeader(string number, string title)
        {
            GUILayout.Space(2);
            EditorGUILayout.LabelField($"{number}   {title}", _step);
            GUILayout.Space(2);
        }

        // --------------------------------------------------------------------- GUI ----

        void OnGUI()
        {
            EnsureStyles();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            GUILayout.Space(10);

            DrawHeader();
            Separator();

#if VRC_SDK_VRCSDK3
            DrawModeSelector();
            Separator();
            if (mode == Mode.Convert)
            {
                DrawConvertFlow();
            }
            else
            {
                DrawSetupFlow();
            }
#else
            EditorGUILayout.HelpBox(
                "AvatarBridge's main job is converting VRChat avatars, which needs the VRChat SDK — it isn't " +
                "installed, so that's unavailable here (a VRChat avatar's components can't be read without it).\n\n" +
                "Setup mode below still works: it does the ChilloutVR-side setup on any humanoid.",
                MessageType.Info);
            GUILayout.Space(6);
            DrawSetupFlow();
#endif

            Separator();
            DrawFooter();

            GUILayout.Space(10);
            EditorGUILayout.EndScrollView();
        }

        void DrawHeader()
        {
            EditorGUILayout.LabelField("AvatarBridge", _title);
            EditorGUILayout.LabelField($"VRChat → ChilloutVR avatar converter   ·   v{BridgeDefines.Version}", _subtitle);
        }

#if VRC_SDK_VRCSDK3
        void DrawModeSelector()
        {
            mode = (Mode)GUILayout.Toolbar((int)mode,
                new[] { "Convert a VRChat avatar", "Set up any avatar" }, GUILayout.Height(24));
            GUILayout.Space(6);
            EditorGUILayout.LabelField(mode == Mode.Convert
                ? "<i>The main event: translates a VRChat avatar to ChilloutVR — menu, toggles, physics, " +
                  "contacts, constraints and more.</i>"
                : "<i>Just the ChilloutVR-side setup — viewpoint, visemes, blink, face tracking and the height " +
                  "scaler — on any humanoid. Handy for non-VRChat avatars; nothing here is converted.</i>", _rich);
        }
#endif

        // ----------------------------------------------------------- convert flow ----

#if VRC_SDK_VRCSDK3
        void DrawConvertFlow()
        {
            StepHeader("1", "Pick your VRChat avatar");
            DrawAvatarPicker();
            Separator();

            StepHeader("2", "Choose what gets set up");
            EditorGUILayout.LabelField(
                "<i>The defaults suit most avatars — you can convert without changing anything here.</i>", _rich);
            GUILayout.Space(4);
            DrawPhysicsSection();
            DrawFaceTrackingSection();
            DrawScalerToggle();
            DrawAdvancedSection();
            Separator();

            StepHeader("3", "Convert");
            DrawConvertButton();
            DrawReport();
        }

        void DrawAvatarPicker()
        {
            avatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(
                new GUIContent("VRChat avatar", "A scene object with a VRC Avatar Descriptor."),
                avatar, typeof(VRCAvatarDescriptor), true);

            if (avatar == null)
            {
                EditorGUILayout.HelpBox(
                    "Drag your avatar here from the Hierarchy (the object with the VRC Avatar Descriptor).",
                    MessageType.None);
                return;
            }

            if (VRCFuryBaker.HasFuryComponents(avatar.gameObject))
            {
                EditorGUILayout.HelpBox(settings.bakeVrcFury
                        ? "VRCFury detected — it will be baked with VRCFury's own builder first, so all Fury " +
                          "features (toggles, clothing, menus) carry over."
                        : "VRCFury detected, but baking is disabled (Advanced options)! Every Fury-driven " +
                          "feature will be MISSING from the result.",
                    settings.bakeVrcFury ? MessageType.Info : MessageType.Warning);
            }
            if (ModularAvatarBaker.HasModularAvatarComponents(avatar.gameObject))
            {
                EditorGUILayout.HelpBox(settings.bakeModularAvatar
                        ? "Modular Avatar detected — it will be baked via NDMF first, so MA features " +
                          "(merged armature, menus, outfits) carry over."
                        : "Modular Avatar detected, but baking is disabled (Advanced options)! MA-driven " +
                          "features will be MISSING from the result.",
                    settings.bakeModularAvatar ? MessageType.Info : MessageType.Warning);
            }
        }

        void DrawPhysicsSection()
        {
            showPhysics = EditorGUILayout.Foldout(showPhysics, "Physics  (PhysBones)", true, EditorStyles.foldoutHeader);
            if (!showPhysics)
            {
                return;
            }
            using (new EditorGUI.IndentLevelScope())
            {
                settings.physicsTarget = (PhysicsTarget)EditorGUILayout.EnumPopup(
                    new GUIContent("Convert PhysBones to",
                        "MagicaCloth2 gives the best result in ChilloutVR; DynamicBone is the built-in fallback."),
                    settings.physicsTarget);

                if (settings.physicsTarget == PhysicsTarget.MagicaCloth2 && !BridgeDefines.HasMagicaCloth2)
                {
                    EditorGUILayout.HelpBox("MagicaCloth2 is not installed in this project — import it, or switch to DynamicBone.",
                        MessageType.Warning);
                }
                if (settings.physicsTarget == PhysicsTarget.DynamicBone && !BridgeDefines.HasDynamicBone)
                {
                    EditorGUILayout.HelpBox(
                        "DynamicBone is not installed. The free VRLabs Dynamic-Bones-Stub also works for conversion.",
                        MessageType.Warning);
                }
                settings.grabbyBonesSupport = EditorGUILayout.ToggleLeft(
                    new GUIContent("GrabbyBones mod support",
                        "Names converted physics objects so Kafe's GrabbyBones mod drives the avatar's " +
                        "_IsGrabbed / _Angle grab-reactive logic."),
                    settings.grabbyBonesSupport);
                settings.deleteConvertedPhysBones = EditorGUILayout.ToggleLeft(
                    new GUIContent("Delete PhysBones after converting",
                        "Leave on — leftover PhysBone components upset the CCK upload checks."),
                    settings.deleteConvertedPhysBones);

                if (settings.physicsTarget == PhysicsTarget.MagicaCloth2)
                {
                    GUILayout.Space(6);
                    EditorGUILayout.LabelField("MagicaCloth2 feel", EditorStyles.miniBoldLabel);
                    EditorGUILayout.HelpBox(
                        "Each chain gets its bones, colliders and ignored transforms from the PhysBone. " +
                        "Everything else starts from MagicaCloth2's own tuned values — the two systems " +
                        "simulate differently, so PhysBone numbers don't carry over. They're listed in the " +
                        "report if you want to tune a chain by hand.",
                        MessageType.None);

                    settings.useMagicaPresets = EditorGUILayout.ToggleLeft(
                        new GUIContent("Match a preset to each chain",
                            "Start each chain from the MagicaCloth2 preset that fits it — hair, tail, skirt, " +
                            "cape or accessory by name, otherwise a soft/middle/hard spring chosen by how " +
                            "firmly the PhysBone held its rest pose. Turn off to give every chain " +
                            "MagicaCloth2's global defaults instead."),
                        settings.useMagicaPresets);
                    settings.fitToPhysBone = EditorGUILayout.ToggleLeft(
                        new GUIContent("Fit the preset to the PhysBone",
                            "After the preset loads, apply the few PhysBone facts that mean the same " +
                            "thing in MagicaCloth2: a chain with no gravity keeps none, negative gravity " +
                            "points up, and immobile becomes world influence (MagicaCloth2 measures the " +
                            "same thing the other way round). Pull, spring and stiffness are left out — " +
                            "they have no MagicaCloth2 counterpart. Each adjustment is named in the report."),
                        settings.fitToPhysBone);
                    settings.capParticleRadius = EditorGUILayout.ToggleLeft(
                        new GUIContent("Cap particle radius to bone spacing",
                            "MagicaCloth2's radius is the particle size, not just a collision radius, so " +
                            "particles wider than the gap between bones shove each other apart. Leave on " +
                            "unless chains come out feeling too thin."),
                        settings.capParticleRadius);
                    settings.transferAngleLimits = EditorGUILayout.ToggleLeft(
                        new GUIContent("Transfer angle limits",
                            "Copy each PhysBone's limit angle onto the cloth. MagicaCloth2's limit pushes on " +
                            "particle positions rather than bone rotation, at a stiffness that snaps back " +
                            "hard — so this shakes some avatars and is the best result the tool gives on " +
                            "others. Worth trying if chains feel loose; lower Angle Limit > Stiffness on any " +
                            "chain that snaps."),
                        settings.transferAngleLimits);
                    settings.autoAssignNearbyColliders = EditorGUILayout.ToggleLeft(
                        new GUIContent("Auto-assign nearby colliders",
                            "Also give each cloth the avatar's own colliders that it starts clear of and " +
                            "could swing into — so a tail that passed through the leg in VRChat collides " +
                            "with it here. This improves on the original avatar rather than copying it, so " +
                            "check the result before uploading. Every assignment is listed in the report."),
                        settings.autoAssignNearbyColliders);
                }
            }
            GUILayout.Space(4);
        }

        void DrawAdvancedSection()
        {
            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced options", true, EditorStyles.foldoutHeader);
            if (!showAdvanced)
            {
                return;
            }
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("General", EditorStyles.boldLabel);
                DrawCommonGeneralOptions();
                settings.bakeVrcFury = EditorGUILayout.ToggleLeft(
                    new GUIContent("Bake VRCFury first (recommended)",
                        "Runs VRCFury's own 'Build a Test Copy' pipeline before converting."),
                    settings.bakeVrcFury);
                settings.bakeModularAvatar = EditorGUILayout.ToggleLeft(
                    new GUIContent("Bake Modular Avatar first (recommended)",
                        "For MA avatars without VRCFury: runs NDMF's manual bake before converting. " +
                        "MA+VRCFury avatars are already covered by the VRCFury bake."),
                    settings.bakeModularAvatar);
                settings.deleteVrcComponents = EditorGUILayout.ToggleLeft(
                    "Delete VRC components after conversion", settings.deleteVrcComponents);

                GUILayout.Space(6);
                EditorGUILayout.LabelField("Remove VRChat-only systems", EditorStyles.boldLabel);
                settings.stripGogoLoco = EditorGUILayout.ToggleLeft(
                    new GUIContent("Remove GoGo Loco (recommended)",
                        "CVR has its own locomotion, flight and emotes. GoGo's layers fight them and " +
                        "waste ~15 synced parameters."),
                    settings.stripGogoLoco);
                settings.stripSpsSystems = EditorGUILayout.ToggleLeft(
                    new GUIContent("Remove SPS / OGB / PCS / Wholesome (recommended)",
                        "VRChat-specific systems whose shaders, contacts and parameters do not function in CVR."),
                    settings.stripSpsSystems);
                settings.extraStripKeywords = EditorGUILayout.TextField(
                    new GUIContent("Extra strip keywords",
                        "Comma separated. Each is used as a parameter prefix and a layer-name match " +
                        "for additional VRC-only systems to remove."),
                    settings.extraStripKeywords);

                GUILayout.Space(6);
                EditorGUILayout.LabelField("Animator layers to convert", EditorStyles.boldLabel);
                settings.convertFxLayer = EditorGUILayout.ToggleLeft("FX (toggles, expressions)", settings.convertFxLayer);
                settings.convertGestureLayer = EditorGUILayout.ToggleLeft("Gesture (hand poses)", settings.convertGestureLayer);
                settings.convertBaseLayer = EditorGUILayout.ToggleLeft(
                    new GUIContent("Base / locomotion",
                        "Usually better left to CVR's own locomotion; enable only for custom locomotion avatars."),
                    settings.convertBaseLayer);
                settings.convertAdditiveLayer = EditorGUILayout.ToggleLeft("Additive", settings.convertAdditiveLayer);
                settings.convertActionLayer = EditorGUILayout.ToggleLeft(
                    new GUIContent("Action (emotes, AFK)",
                        "VRC emote triggers have no CVR equivalent; states may be unreachable."),
                    settings.convertActionLayer);

                GUILayout.Space(6);
                EditorGUILayout.LabelField("Parameters & toggles", EditorStyles.boldLabel);
                settings.nativizeObjectToggles = EditorGUILayout.ToggleLeft(
                    new GUIContent("Rebuild VRCFury toggles (recommended)",
                        "Pulls toggles out of VRCFury's merged blend tree so each one is a readable, " +
                        "working toggle instead of float math."),
                    settings.nativizeObjectToggles);
                using (new EditorGUI.DisabledScope(!settings.nativizeObjectToggles))
                {
                    settings.toggleStyle = (ToggleStyle)EditorGUILayout.EnumPopup(
                        new GUIContent("Toggle style",
                            "Animator Layers: every toggle gets its own Off/On layer and works immediately.\n" +
                            "CVR Native Targets: object toggles are left to the CCK's own builder " +
                            "(you must press \"Create Controller\" on the avatar)."),
                        settings.toggleStyle);
                }
                settings.preserveParameterSyncState = EditorGUILayout.ToggleLeft(
                    new GUIContent("Preserve parameter sync state",
                        "Non-synced VRC parameters get CVR's '#' local-only prefix."),
                    settings.preserveParameterSyncState);
                settings.exposeMenulessSyncedParameters = EditorGUILayout.ToggleLeft(
                    new GUIContent("Expose menu-less synced parameters",
                        "Synced parameters without a menu control still get a CVR menu entry so they sync."),
                    settings.exposeMenulessSyncedParameters);
                settings.integerHandGestures = EditorGUILayout.ToggleLeft(
                    new GUIContent("Integer hand-pose gestures",
                        "Selects discrete gestures via GestureLeftIdx/RightIdx; the analog fist " +
                        "(trigger-pressure finger curl) stays on the float."),
                    settings.integerHandGestures);

                GUILayout.Space(6);
                EditorGUILayout.LabelField("Components", EditorStyles.boldLabel);
                settings.convertContacts = EditorGUILayout.ToggleLeft("Convert contact senders/receivers", settings.convertContacts);
                settings.createDefaultColliderPointers = EditorGUILayout.ToggleLeft(
                    new GUIContent("Recreate built-in VRC colliders as pointers",
                        "Head/hands/fingers pointers so converted receivers keep reacting to other players."),
                    settings.createDefaultColliderPointers);
                using (new EditorGUI.DisabledScope(!settings.convertContacts))
                {
                    settings.useNativeContacts = EditorGUILayout.ToggleLeft(
                        new GUIContent("Use ChilloutVR's native contacts (experimental)",
                            "Converts contacts one-to-one onto ChilloutVR's own contact components instead " +
                            "of approximating them with pointers and triggers: real proximity, collision " +
                            "tags kept as-is, local-only receivers finally honoured, and no sync cost. " +
                            "The components aren't in the CCK, so AvatarBridge declares them itself — " +
                            "which can only be proven correct by putting an avatar in game. If it isn't, " +
                            "the contact objects show a missing script and you turn this back off."),
                        settings.useNativeContacts);
                }
                settings.convertConstraints = EditorGUILayout.ToggleLeft("Convert VRC constraints", settings.convertConstraints);
                settings.convertHeadChop = EditorGUILayout.ToggleLeft(
                    new GUIContent("Convert VRC Head Chop", "First-person show/hide, including its toggle animations."),
                    settings.convertHeadChop);
                settings.convertSpatialAudio = EditorGUILayout.ToggleLeft("Convert spatial audio", settings.convertSpatialAudio);
                DrawBlinkToggle();
            }
            GUILayout.Space(4);
        }

        void DrawConvertButton()
        {
            bool ftPackageMissing = settings.faceTrackingMode == FaceTrackingMode.DragonSkyRunner
                                    && !FaceTrackingPackages.IsInstalled();
            using (new EditorGUI.DisabledScope(avatar == null || ftPackageMissing))
            {
                var previous = GUI.backgroundColor;
                GUI.backgroundColor = avatar != null && !ftPackageMissing
                    ? new Color(0.55f, 0.85f, 0.55f)
                    : previous;
                string label = avatar == null ? "Convert avatar" : $"Convert \"{avatar.gameObject.name}\"";
                if (GUILayout.Button(label, GUILayout.Height(36)))
                {
                    lastReport = BridgeConverter.Convert(avatar, settings);
                }
                GUI.backgroundColor = previous;
            }

            if (avatar == null)
            {
                EditorGUILayout.HelpBox("Pick an avatar in step 1 first.", MessageType.None);
            }
            else if (ftPackageMissing)
            {
                EditorGUILayout.HelpBox(
                    "The bundled face-tracking assets are missing — reimport AvatarBridge, or set " +
                    "Face tracking to Native or None.", MessageType.Warning);
            }
        }
#endif // VRC_SDK_VRCSDK3

        // ------------------------------------------------------------- setup flow ----

        void DrawSetupFlow()
        {
            StepHeader("1", "Pick any avatar");
            setupAvatar = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Avatar", "Any avatar in the scene. A Humanoid rig gives the best result."),
                setupAvatar, typeof(GameObject), true);

            if (setupAvatar == null)
            {
                EditorGUILayout.HelpBox(
                    "Drag any avatar here from the Hierarchy — it doesn't need to be a VRChat avatar.",
                    MessageType.None);
            }
            else
            {
                var animator = setupAvatar.GetComponent<Animator>();
                if (animator == null || !animator.isHuman)
                {
                    EditorGUILayout.HelpBox(
                        "This isn't a Humanoid rig. Setup still runs, but the viewpoint is estimated from the " +
                        "mesh bounds and eye tracking can't be wired. Set the rig to Humanoid in the model's " +
                        "import settings for a proper result.",
                        MessageType.Warning);
                }
                if (setupAvatar.GetComponent<ABI.CCK.Components.CVRAvatar>() != null)
                {
                    EditorGUILayout.HelpBox(
                        "This avatar already has a CVRAvatar component. Setup will reconfigure it — its " +
                        "Advanced Avatar Settings are rebuilt from scratch.",
                        MessageType.Warning);
                }
            }
            Separator();

            StepHeader("2", "Choose what gets set up");
            EditorGUILayout.LabelField(
                "<i>Viewpoint, visemes and blink are always detected and wired.</i>", _rich);
            GUILayout.Space(4);
            DrawFaceTrackingSection();
            DrawScalerToggle();
            DrawSetupAdvancedSection();
            Separator();

            StepHeader("3", "Set up");
            DrawSetupButton();
            DrawReport();
        }

        void DrawSetupAdvancedSection()
        {
            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced options", true, EditorStyles.foldoutHeader);
            if (!showAdvanced)
            {
                return;
            }
            using (new EditorGUI.IndentLevelScope())
            {
                DrawCommonGeneralOptions();
                DrawBlinkToggle();
            }
            GUILayout.Space(4);
        }

        void DrawSetupButton()
        {
            bool ftPackageMissing = settings.faceTrackingMode == FaceTrackingMode.DragonSkyRunner
                                    && !FaceTrackingPackages.IsInstalled();
            using (new EditorGUI.DisabledScope(setupAvatar == null || ftPackageMissing))
            {
                var previous = GUI.backgroundColor;
                GUI.backgroundColor = setupAvatar != null && !ftPackageMissing
                    ? new Color(0.55f, 0.85f, 0.55f)
                    : previous;
                string label = setupAvatar == null ? "Set up avatar" : $"Set up \"{setupAvatar.name}\"";
                if (GUILayout.Button(label, GUILayout.Height(36)))
                {
                    lastReport = CvrSetup.Run(setupAvatar, settings);
                }
                GUI.backgroundColor = previous;
            }

            if (setupAvatar == null)
            {
                EditorGUILayout.HelpBox("Pick an avatar in step 1 first.", MessageType.None);
            }
            else if (ftPackageMissing)
            {
                EditorGUILayout.HelpBox(
                    "The bundled face-tracking assets are missing — reimport AvatarBridge, or set " +
                    "Face tracking to Native or None.", MessageType.Warning);
            }
        }

        // ---------------------------------------------------------- shared sections ----

        void DrawCommonGeneralOptions()
        {
            settings.cloneAvatar = EditorGUILayout.ToggleLeft(
                new GUIContent("Work on a clone (recommended)",
                    "The original avatar object stays untouched and gets deactivated."),
                settings.cloneAvatar);
            settings.outputFolder = EditorGUILayout.TextField(
                new GUIContent("Output folder", "Where generated assets and the report go. Must be inside Assets."),
                settings.outputFolder);
        }

        void DrawBlinkToggle()
        {
            settings.wireBlinkBlendshapes = EditorGUILayout.ToggleLeft(
                new GUIContent("Auto-wire blink blendshapes",
                    "Detect blink blendshapes on the face mesh (e.g. \"Blink L\"/\"Blink R\") and turn on " +
                    "CVR's Eye Blink Settings."),
                settings.wireBlinkBlendshapes);
        }

        static readonly FaceTrackingMode[] FtModes =
            { FaceTrackingMode.Native, FaceTrackingMode.DragonSkyRunner, FaceTrackingMode.None };

        void DrawFaceTrackingSection()
        {
            showFaceTracking = EditorGUILayout.Foldout(showFaceTracking, "Face tracking", true, EditorStyles.foldoutHeader);
            if (!showFaceTracking)
            {
                return;
            }
            using (new EditorGUI.IndentLevelScope())
            {
                var labels = new[]
                {
                    new GUIContent("Native CVR Component",
                        "ChilloutVR's built-in CVRFaceTracking component drives the blendshapes directly. " +
                        "Auto-added and mapped. Self-contained, but a bit stiff."),
                    new GUIContent("Unity Animator Blendtrees (DSR)",
                        "DragonSkyRunner's bundled rig: face shapes driven by animator blend trees, eye tracking " +
                        "via generated empties + rotation constraints, rebuilt onto this avatar automatically. " +
                        "Smoother and more expressive."),
                    new GUIContent("None", "Leave face tracking entirely to you."),
                };
                int index = Mathf.Max(0, System.Array.IndexOf(FtModes, settings.faceTrackingMode));
                index = EditorGUILayout.Popup(
                    new GUIContent("Face tracking",
                        "Both set-up modes replace any face-tracking rig already on the avatar."),
                    index, labels);
                settings.faceTrackingMode = FtModes[index];

                if (settings.faceTrackingMode == FaceTrackingMode.DragonSkyRunner)
                {
                    if (FaceTrackingPackages.IsInstalled())
                    {
                        EditorGUILayout.HelpBox(
                            "Injects DragonSkyRunner's CVR Eye & Face Tracking rig (bundled) and rebuilds it onto " +
                            "this avatar — including an auto-generated eye-tracking rig. Eye gaze strength may want " +
                            "tuning per the package readme. Credit: DragonSkyRunner.", MessageType.Info);
                        if (GUILayout.Button("DragonSkyRunner's package (GitHub)  ↗", EditorStyles.linkLabel))
                        {
                            Application.OpenURL(FaceTrackingPackages.Url);
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox($"The bundled \"{FaceTrackingPackages.DisplayName}\" assets weren't " +
                            "found — reimport AvatarBridge (the button is disabled until then).",
                            MessageType.Warning);
                    }
                }
            }
            GUILayout.Space(4);
        }

        void DrawScalerToggle()
        {
            settings.addAvatarScaler = EditorGUILayout.ToggleLeft(
                new GUIContent("Add height scaler  (\"Height (M)\" menu)",
                    "A smooth avatar scaler. Auto-calibrated: the menu value is real metres and defaults to this " +
                    "avatar's measured height, so it spawns at exactly its original size."),
                settings.addAvatarScaler);
            GUILayout.Space(4);
        }

        // ------------------------------------------------------------------- report ---

        void DrawReport()
        {
            if (lastReport == null)
            {
                return;
            }
            GUILayout.Space(8);

            int errors = lastReport.CountOf(ReportStatus.Error);
            int warnings = lastReport.CountOf(ReportStatus.Warning);

            if (errors > 0)
            {
                EditorGUILayout.HelpBox($"Finished with {errors} error(s) — see below.", MessageType.Error);
            }
            else if (warnings > 0)
            {
                EditorGUILayout.HelpBox($"Done! {warnings} thing(s) may want a look — see below.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("Done! The avatar is ready for the CCK's upload checks.", MessageType.Info);
            }

            EditorGUILayout.LabelField(
                $"<b><color=#7bc97b>{lastReport.CountOf(ReportStatus.Converted)} done</color></b>   " +
                $"<color=#c9b97b>{lastReport.CountOf(ReportStatus.Approximated)} approximated</color>   " +
                $"<color=#999999>{lastReport.CountOf(ReportStatus.Skipped)} skipped</color>   " +
                $"<color=#e0a96d>{warnings} warnings</color>   " +
                $"<color=#e07b7b>{errors} errors</color>", _rich);

            if (!string.IsNullOrEmpty(lastReport.SavedReportPath))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open full report", GUILayout.Width(130)))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<Object>(lastReport.SavedReportPath);
                        if (asset != null)
                        {
                            AssetDatabase.OpenAsset(asset);
                        }
                    }
                    if (GUILayout.Button("Show in Project", GUILayout.Width(130)))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<Object>(lastReport.SavedReportPath);
                        if (asset != null)
                        {
                            EditorGUIUtility.PingObject(asset);
                        }
                    }
                }
            }

            if (errors > 0 || warnings > 0)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Report an issue",
                            "Opens a pre-filled GitHub issue. Please attach the report — " +
                            "most bugs are diagnosed straight from it."), GUILayout.Height(24)))
                    {
                        BridgeLinks.OpenBugReport(lastReport);
                    }
                    if (GUILayout.Button(new GUIContent("Copy diagnostics",
                            "Copies versions and detected packages to the clipboard."),
                            GUILayout.Width(130), GUILayout.Height(24)))
                    {
                        BridgeLinks.CopyDiagnostics(lastReport);
                        ShowNotification(new GUIContent("Diagnostics copied"));
                    }
                }
            }

            // Only issues are listed here; the full list lives in the report file.
            reportScroll = EditorGUILayout.BeginScrollView(reportScroll, GUILayout.MinHeight(100), GUILayout.MaxHeight(200));
            foreach (var entry in lastReport.Entries)
            {
                if (entry.Status == ReportStatus.Converted || entry.Status == ReportStatus.Approximated)
                {
                    continue;
                }
                var style = entry.Status == ReportStatus.Error ? EditorStyles.boldLabel : EditorStyles.miniLabel;
                EditorGUILayout.LabelField(
                    $"[{entry.Status}] {entry.Category}: {entry.Subject}" +
                    (string.IsNullOrEmpty(entry.Detail) ? "" : $" — {entry.Detail}"),
                    style);
            }
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------- footer ---

        void DrawFooter()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Troubleshooting  ↗", EditorStyles.linkLabel))
                {
                    Application.OpenURL(BridgeLinks.Troubleshooting);
                }
                GUILayout.Space(12);
                if (GUILayout.Button("Report an issue  ↗", EditorStyles.linkLabel))
                {
                    BridgeLinks.OpenBugReport(lastReport);
                }
                if (!string.IsNullOrEmpty(BridgeLinks.DiscordUser))
                {
                    GUILayout.Space(12);
                    if (GUILayout.Button(new GUIContent($"Discord: {BridgeLinks.DiscordUser}",
                            "Click to copy the handle. Best for quick questions — please use " +
                            "GitHub issues for bugs so they don't get lost."), EditorStyles.linkLabel))
                    {
                        BridgeLinks.CopyDiscord();
                        ShowNotification(new GUIContent("Copied: " + BridgeLinks.DiscordUser));
                    }
                }
                GUILayout.FlexibleSpace();
            }
        }
#else
        void OnGUI()
        {
            GUILayout.Space(10);
            EditorGUILayout.LabelField("AvatarBridge", new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 });
            GUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "AvatarBridge converts VRChat avatars to ChilloutVR. It needs both SDKs for that:",
                MessageType.Warning);
            EditorGUILayout.LabelField(
                (BridgeDefines.HasVrcAvatarSdk ? "✔" : "✘") + "  VRChat Avatars SDK (SDK3)  — to read the avatar");
            EditorGUILayout.LabelField(
                (BridgeDefines.HasCck ? "✔" : "✘") + "  ChilloutVR CCK (4.x recommended)  — always required");
            GUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Import the missing package(s), let Unity recompile, and reopen this window. " +
                "With just the CCK you can still use Setup mode to prepare any avatar for ChilloutVR.",
                MessageType.Info);

            GUILayout.Space(10);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Setup guide  ↗", EditorStyles.linkLabel))
                {
                    Application.OpenURL(BridgeLinks.Troubleshooting);
                }
                GUILayout.Space(12);
                if (GUILayout.Button("Report an issue  ↗", EditorStyles.linkLabel))
                {
                    BridgeLinks.OpenBugReport();
                }
                GUILayout.FlexibleSpace();
            }
        }
#endif
    }
}
