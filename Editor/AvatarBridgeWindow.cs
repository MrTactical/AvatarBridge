using UnityEditor;
using UnityEngine;
#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using VRC.SDK3.Avatars.Components;
#endif

namespace AvatarBridge
{
    public class AvatarBridgeWindow : EditorWindow
    {
        [MenuItem("Tools/Avatar Bridge/VRChat to ChilloutVR Converter")]
        static void Open()
        {
            var window = GetWindow<AvatarBridgeWindow>("Avatar Bridge");
            window.minSize = new Vector2(380, 500);
        }

#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
        [SerializeField] BridgeSettings settings = new BridgeSettings();
        VRCAvatarDescriptor avatar;
        BridgeReport lastReport;
        Vector2 scroll;
        Vector2 reportScroll;
        bool showAnimatorOptions = true;
        bool showPhysicsOptions = true;
        bool showOtherOptions;

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            GUILayout.Space(8);
            EditorGUILayout.LabelField("VRChat → ChilloutVR avatar conversion", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Pick a VRChat avatar from the scene. AvatarBridge clones it, sets up a CVRAvatar, " +
                "merges the animators, rebuilds the menu as Advanced Avatar Settings, converts " +
                "PhysBones, contacts and constraints, then writes a conversion report.",
                MessageType.Info);

            avatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(
                "VRChat avatar", avatar, typeof(VRCAvatarDescriptor), true);

            GUILayout.Space(6);
            settings.cloneAvatar = EditorGUILayout.ToggleLeft(
                new GUIContent("Work on a clone (recommended)",
                    "The original avatar object stays untouched and gets deactivated."),
                settings.cloneAvatar);
            settings.deleteVrcComponents = EditorGUILayout.ToggleLeft(
                "Delete VRC components after conversion", settings.deleteVrcComponents);
            settings.bakeVrcFury = EditorGUILayout.ToggleLeft(
                new GUIContent("Bake VRCFury first (recommended)",
                    "Runs VRCFury's own 'Build a Test Copy' pipeline before converting, so Fury " +
                    "toggles, linked clothing and full controllers become real layers that convert."),
                settings.bakeVrcFury);

            if (avatar != null && VRCFuryBaker.HasFuryComponents(avatar.gameObject))
            {
                EditorGUILayout.HelpBox(settings.bakeVrcFury
                        ? "VRCFury detected on this avatar. It will be baked with VRCFury's own builder " +
                          "first, so all Fury features carry over into the conversion."
                        : "VRCFury detected on this avatar! With baking disabled, every Fury-driven " +
                          "feature (toggles, clothing, menus) will be MISSING from the result.",
                    settings.bakeVrcFury ? MessageType.Info : MessageType.Warning);
            }

            // ---- Physics -------------------------------------------------------------
            GUILayout.Space(6);
            showPhysicsOptions = EditorGUILayout.Foldout(showPhysicsOptions, "PhysBones", true);
            if (showPhysicsOptions)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    settings.physicsTarget = (PhysicsTarget)EditorGUILayout.EnumPopup(
                        "Convert PhysBones to", settings.physicsTarget);

                    if (settings.physicsTarget == PhysicsTarget.MagicaCloth2 && !BridgeDefines.HasMagicaCloth2)
                    {
                        EditorGUILayout.HelpBox("MagicaCloth2 is not installed in this project.", MessageType.Warning);
                    }
                    if (settings.physicsTarget == PhysicsTarget.DynamicBone && !BridgeDefines.HasDynamicBone)
                    {
                        EditorGUILayout.HelpBox(
                            "DynamicBone is not installed. The free VRLabs Dynamic-Bones-Stub also works for conversion.",
                            MessageType.Warning);
                    }
                    settings.deleteConvertedPhysBones = EditorGUILayout.ToggleLeft(
                        "Delete PhysBones after converting", settings.deleteConvertedPhysBones);
                }
            }

            // ---- Animator ------------------------------------------------------------
            GUILayout.Space(6);
            showAnimatorOptions = EditorGUILayout.Foldout(showAnimatorOptions, "Animator layers", true);
            if (showAnimatorOptions)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    settings.convertFxLayer = EditorGUILayout.ToggleLeft("FX (toggles, expressions)", settings.convertFxLayer);
                    settings.convertGestureLayer = EditorGUILayout.ToggleLeft("Gesture (hand poses)", settings.convertGestureLayer);
                    settings.convertBaseLayer = EditorGUILayout.ToggleLeft(
                        new GUIContent("Base / locomotion (advanced)",
                            "Usually better left to CVR's own locomotion; enable only for custom locomotion avatars."),
                        settings.convertBaseLayer);
                    settings.convertAdditiveLayer = EditorGUILayout.ToggleLeft("Additive", settings.convertAdditiveLayer);
                    settings.convertActionLayer = EditorGUILayout.ToggleLeft(
                        new GUIContent("Action (emotes, AFK)", "VRC emote triggers have no CVR equivalent; states may be unreachable."),
                        settings.convertActionLayer);
                }
            }

            // ---- Everything else -----------------------------------------------------
            GUILayout.Space(6);
            showOtherOptions = EditorGUILayout.Foldout(showOtherOptions, "Contacts, parameters & misc", true);
            if (showOtherOptions)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    settings.convertContacts = EditorGUILayout.ToggleLeft("Convert contact senders/receivers", settings.convertContacts);
                    settings.createDefaultColliderPointers = EditorGUILayout.ToggleLeft(
                        new GUIContent("Recreate built-in VRC colliders as pointers",
                            "Head/hands/fingers pointers so converted receivers keep reacting to other players."),
                        settings.createDefaultColliderPointers);
                    settings.preserveParameterSyncState = EditorGUILayout.ToggleLeft(
                        new GUIContent("Preserve parameter sync state",
                            "Non-synced VRC parameters get CVR's '#' local-only prefix."),
                        settings.preserveParameterSyncState);
                    settings.exposeMenulessSyncedParameters = EditorGUILayout.ToggleLeft(
                        new GUIContent("Expose menu-less synced parameters",
                            "Synced parameters without a menu control still get a CVR menu entry so they sync."),
                        settings.exposeMenulessSyncedParameters);
                    settings.convertConstraints = EditorGUILayout.ToggleLeft("Convert VRC constraints", settings.convertConstraints);
                    settings.convertHeadChop = EditorGUILayout.ToggleLeft("Convert VRC Head Chop", settings.convertHeadChop);
                    settings.convertSpatialAudio = EditorGUILayout.ToggleLeft("Convert spatial audio", settings.convertSpatialAudio);
                    settings.outputFolder = EditorGUILayout.TextField("Output folder", settings.outputFolder);
                }
            }

            // ---- Convert -------------------------------------------------------------
            GUILayout.Space(10);
            using (new EditorGUI.DisabledScope(avatar == null))
            {
                if (GUILayout.Button("Convert avatar", GUILayout.Height(32)))
                {
                    lastReport = BridgeConverter.Convert(avatar, settings);
                }
            }
            if (avatar == null)
            {
                EditorGUILayout.HelpBox("Assign a scene object with a VRCAvatarDescriptor.", MessageType.None);
            }

            DrawReport();
            EditorGUILayout.EndScrollView();
        }

        void DrawReport()
        {
            if (lastReport == null)
            {
                return;
            }
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Last conversion", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"Converted {lastReport.CountOf(ReportStatus.Converted)}   " +
                $"Approximated {lastReport.CountOf(ReportStatus.Approximated)}   " +
                $"Skipped {lastReport.CountOf(ReportStatus.Skipped)}   " +
                $"Warnings {lastReport.CountOf(ReportStatus.Warning)}   " +
                $"Errors {lastReport.CountOf(ReportStatus.Error)}");

            reportScroll = EditorGUILayout.BeginScrollView(reportScroll, GUILayout.MinHeight(120), GUILayout.MaxHeight(220));
            foreach (var entry in lastReport.Entries)
            {
                if (entry.Status == ReportStatus.Converted)
                {
                    continue; // the full list lives in the report file; show only issues here
                }
                var style = entry.Status == ReportStatus.Error ? EditorStyles.boldLabel : EditorStyles.miniLabel;
                EditorGUILayout.LabelField(
                    $"[{entry.Status}] {entry.Category}: {entry.Subject}" +
                    (string.IsNullOrEmpty(entry.Detail) ? "" : $" — {entry.Detail}"),
                    style);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.HelpBox("The full report (including everything that converted cleanly) is saved as ConversionReport.md next to the generated animator.", MessageType.None);
        }
#else
        void OnGUI()
        {
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
        }
#endif
    }
}
