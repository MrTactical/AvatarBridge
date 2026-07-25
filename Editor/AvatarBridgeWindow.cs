using UnityEditor;
using UnityEngine;
#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using VRC.SDK3.Avatars.Components;
#endif

namespace AvatarBridge
{
    /// <summary>
    /// The AvatarBridge control panel. Laid out as a three-step flow (pick avatar →
    /// options → convert) so a first-time user can convert without touching a single
    /// option — the defaults suit most avatars, and everything unusual lives under
    /// "Advanced options".
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

#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
        [SerializeField] BridgeSettings settings = new BridgeSettings();
        VRCAvatarDescriptor avatar;
        BridgeReport lastReport;
        Vector2 scroll;
        Vector2 reportScroll;
        bool showPhysics = true;
        bool showFaceTracking = true;
        bool showAdvanced;

        // ------------------------------------------------------------------ lifecycle --

        void OnEnable()
        {
            // Remember the user's choices across editor restarts.
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

            Separator();
            DrawFooter();

            GUILayout.Space(10);
            EditorGUILayout.EndScrollView();
        }

        /// <summary>Help + reporting links, always reachable.</summary>
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
                if (!string.IsNullOrEmpty(BridgeLinks.Discord))
                {
                    GUILayout.Space(12);
                    if (GUILayout.Button("Discord  ↗", EditorStyles.linkLabel))
                    {
                        Application.OpenURL(BridgeLinks.Discord);
                    }
                }
                GUILayout.FlexibleSpace();
            }
        }

        void DrawHeader()
        {
            EditorGUILayout.LabelField("AvatarBridge", _title);
            EditorGUILayout.LabelField($"VRChat → ChilloutVR avatar converter   ·   v{BridgeDefines.Version}", _subtitle);
            GUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Converts a copy of your avatar: viewpoint, visemes, menu, toggles, PhysBones, contacts, " +
                "constraints, face tracking and more. Your original avatar is never touched, and every " +
                "change is listed in a conversion report.",
                MessageType.Info);
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

        // ------------------------------------------------------------------ sections --

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
            }
            GUILayout.Space(4);
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
                        "Both set-up modes replace any face-tracking rig already baked into the avatar."),
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
                            "found — reimport AvatarBridge (the Convert button is disabled until then).",
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
                settings.cloneAvatar = EditorGUILayout.ToggleLeft(
                    new GUIContent("Work on a clone (recommended)",
                        "The original avatar object stays untouched and gets deactivated."),
                    settings.cloneAvatar);
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
                settings.outputFolder = EditorGUILayout.TextField(
                    new GUIContent("Output folder", "Where generated assets and the report go. Must be inside Assets."),
                    settings.outputFolder);

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
                settings.convertConstraints = EditorGUILayout.ToggleLeft("Convert VRC constraints", settings.convertConstraints);
                settings.convertHeadChop = EditorGUILayout.ToggleLeft(
                    new GUIContent("Convert VRC Head Chop", "First-person show/hide, including its toggle animations."),
                    settings.convertHeadChop);
                settings.convertSpatialAudio = EditorGUILayout.ToggleLeft("Convert spatial audio", settings.convertSpatialAudio);
                settings.wireBlinkBlendshapes = EditorGUILayout.ToggleLeft(
                    new GUIContent("Auto-wire blink blendshapes",
                        "If the VRChat descriptor set no blink shape, detect one on the face mesh " +
                        "(e.g. \"Blink L\"/\"Blink R\") and turn on CVR's Eye Blink Settings."),
                    settings.wireBlinkBlendshapes);
            }
            GUILayout.Space(4);
        }

        // ------------------------------------------------------------------ convert ---

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
                EditorGUILayout.HelpBox($"Conversion finished with {errors} error(s) — see below.", MessageType.Error);
            }
            else if (warnings > 0)
            {
                EditorGUILayout.HelpBox($"Converted! {warnings} thing(s) may want a look — see below.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("Converted! The avatar is ready for the CCK's upload checks.", MessageType.Info);
            }

            EditorGUILayout.LabelField(
                $"<b><color=#7bc97b>{lastReport.CountOf(ReportStatus.Converted)} converted</color></b>   " +
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

            // Something went wrong — make reporting it the obvious next step, with the
            // report file one click away so it actually gets attached.
            if (errors > 0 || warnings > 0)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Report an issue",
                            "Opens a pre-filled GitHub issue. Please attach the ConversionReport.md — " +
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

            // Only issues are listed here; the full list lives in ConversionReport.md.
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
#else
        void OnGUI()
        {
            GUILayout.Space(10);
            EditorGUILayout.LabelField("AvatarBridge", new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 });
            GUILayout.Space(6);
            EditorGUILayout.HelpBox("AvatarBridge needs both SDKs in this project before it can run:", MessageType.Warning);
            EditorGUILayout.LabelField(
                (BridgeDefines.HasVrcAvatarSdk ? "✔" : "✘") + "  VRChat Avatars SDK (SDK3)");
            EditorGUILayout.LabelField(
                (BridgeDefines.HasCck ? "✔" : "✘") + "  ChilloutVR CCK (4.x recommended)");
            GUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Import the missing package(s), let Unity recompile, and reopen this window. " +
                "See the AvatarBridge README for the recommended project setup.",
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
                if (!string.IsNullOrEmpty(BridgeLinks.Discord))
                {
                    GUILayout.Space(12);
                    if (GUILayout.Button("Discord  ↗", EditorStyles.linkLabel))
                    {
                        Application.OpenURL(BridgeLinks.Discord);
                    }
                }
                GUILayout.FlexibleSpace();
            }
        }
#endif
    }
}
