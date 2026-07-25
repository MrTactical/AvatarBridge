using System;
using UnityEngine;

namespace AvatarBridge
{
    public enum PhysicsTarget
    {
        MagicaCloth2,
        DynamicBone,
        None
    }

    public enum ToggleStyle
    {
        /// <summary>Every toggle becomes a classic Off/On animator layer with its own clip.</summary>
        AnimatorLayers,
        /// <summary>Object toggles are handed to CVR's builder as GameObject targets.</summary>
        CvrNativeTargets
    }

    public enum FaceTrackingMode
    {
        /// <summary>Native CVR: add and auto-map a CVRFaceTracking component (blendshape-based).</summary>
        Native,
        /// <summary>CVR-VRCFT (parameter-based). Strip any existing FT rig and inject the
        /// bundled DragonSkyRunner "CVR Eye &amp; Face Tracking" animator, repathed onto this
        /// avatar (eye-tracking empties + rotation constraints generated automatically). The
        /// enum name is kept for serialization stability; the UI labels it "CVR VRCFT".</summary>
        DragonSkyRunner,
        /// <summary>Leave face tracking entirely to the user.</summary>
        None
    }

    /// <summary>
    /// All user-facing conversion options. Serialized so the editor window remembers them.
    /// </summary>
    [Serializable]
    public class BridgeSettings
    {
        [Header("General")]
        public bool cloneAvatar = true;
        public bool deleteVrcComponents = true;
        // Run VRCFury's own "Build a Test Copy" pipeline first so Fury toggles, linked
        // clothing, full controllers etc. are baked into real layers before converting.
        public bool bakeVrcFury = true;
        // Bake Modular Avatar / NDMF (merge armature, menus, params, reactive toggles…) via
        // NDMF's manual bake before converting — for MA avatars that don't also use VRCFury
        // (a VRCFury bake already runs NDMF, so MA+Fury avatars are covered by the Fury bake).
        public bool bakeModularAvatar = true;
        public string outputFolder = "Assets/AvatarBridge/Output";

        [Header("Animator layers to merge")]
        public bool convertBaseLayer = false;
        public bool convertAdditiveLayer = false;
        public bool convertGestureLayer = true;
        public bool convertActionLayer = false;
        public bool convertFxLayer = true;

        [Header("Parameters")]
        // Master switch for rebuilding VRCFury's merged toggles into something readable.
        public bool nativizeObjectToggles = true;
        // AnimatorLayers keeps every toggle inside the generated controller (works
        // without pressing "Create Controller"); CvrNativeTargets defers object toggles
        // to the CCK's own builder via GameObject targets.
        public ToggleStyle toggleStyle = ToggleStyle.AnimatorLayers;
        // When enabled, animator parameters that are not network-synced in VRChat get the
        // CVR "#" local-only prefix so network traffic matches the original avatar.
        public bool preserveParameterSyncState = true;
        // Expose VRChat-synced parameters that have no menu control as CVR menu entries so
        // they still sync (contacts/OSC-driven setups).
        public bool exposeMenulessSyncedParameters = true;
        // Convert the CCK's native hand-pose layers to select discrete gestures via the
        // integer GestureLeftIdx/RightIdx (analog fist stays on the float parameter).
        public bool integerHandGestures = true;

        [Header("Physics")]
        public PhysicsTarget physicsTarget = PhysicsTarget.MagicaCloth2;
        public bool deleteConvertedPhysBones = true;
        // Name converted MagicaCloth objects after their PhysBone parameter so the
        // GrabbyBones mod drives the avatar's _IsGrabbed / _Angle grab-reactive logic.
        public bool grabbyBonesSupport = true;
        // Start each cloth from the MagicaCloth2 preset that fits the chain, rather than from
        // values derived out of the PhysBone. PhysBones and MagicaCloth2 are different solvers
        // (rotational spring vs particle positions), so derived numbers are analogies; a preset
        // written by MagicaCloth2's own author is a better starting point. Structure — bones,
        // colliders, ignores — still comes from the PhysBone either way.
        public bool useMagicaPresets = true;
        // Off by default: this deliberately departs from the source avatar. Both systems keep
        // explicit per-chain collider lists, so a chain the author never wired stays uncollided
        // in CVR exactly as it was in VRChat — turning this on improves on that rather than
        // reproducing it, and is worth eyeballing before you upload.
        public bool autoAssignNearbyColliders = false;

        [Header("VRChat-only system stripping")]
        // GoGo Loco is replaced by CVR's own locomotion/emotes; keeping it wastes ~15
        // synced parameters (incl. a 256-value emote int) on layers that fight CVR.
        public bool stripGogoLoco = true;
        // SPS/OGB/TPS haptics, PCS and the Wholesome add-on are VRChat-specific; their
        // contacts, shaders and parameters don't function in CVR and burn sync budget.
        public bool stripSpsSystems = true;
        // Comma-separated extra keywords; matched as parameter prefixes AND layer-name
        // substrings, for VRC-only add-ons the built-in lists don't know about.
        public string extraStripKeywords = "";

        [Header("Contacts")]
        public bool convertContacts = true;
        // Recreate VRChat's built-in hand/head/torso colliders as CVR pointers so contact
        // receivers keep reacting to other players' hands.
        public bool createDefaultColliderPointers = true;

        [Header("Other components")]
        public bool convertConstraints = true;
        public bool convertHeadChop = true;
        public bool convertSpatialAudio = true;
        // If the VRChat descriptor didn't set an eyelid/blink blendshape, auto-detect blink
        // shapes on the face mesh (e.g. "Blink L"/"Blink R") and wire CVR's Eye Blink Settings.
        public bool wireBlinkBlendshapes = true;
        // Inject the bundled avatar scaler (Linear Smoothing + generated Size layer + a
        // "Height (M)" menu input). Auto-calibrated: the menu defaults to the avatar's
        // measured eye height, so it spawns at its original size and the value is true metres.
        public bool addAvatarScaler = true;
        // How to handle face tracking. Native = auto-set-up CVRFaceTracking (blendshape-
        // based); DragonSkyRunner = inject that package's animator layers/params; None =
        // leave it to the user. Native and DragonSkyRunner both strip any existing FT rig.
        public FaceTrackingMode faceTrackingMode = FaceTrackingMode.Native;
    }
}
