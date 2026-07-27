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
        // Give VRChat-synced parameters that have no menu control an Advanced Avatar Settings
        // entry anyway (contacts/OSC-driven setups).
        //
        // Not needed for syncing, despite what an earlier version of this comment said: CVR
        // decides that from the animator declaration (IsSynced => !isLocal && !IsReadOnly), and
        // an unmenued parameter syncs regardless. What the entry buys is profile persistence —
        // isAas gates CanSaveToProfile, so without one the value resets between avatar loads —
        // plus somewhere for the user to see and drive it by hand.
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
        // MagicaCloth2's global defaults. A preset written by the solver's own author is a better
        // baseline than one set of numbers for everything. Structure — bones, colliders, ignores —
        // comes from the PhysBone either way, and derivePhysicsFromPhysBone below overrides the
        // preset's damping and restoration when it is on.
        public bool useMagicaPresets = true;
        // Both of these were unconditional in 1.1.2, which one tester reports as the best
        // MagicaCloth2 result they've had — while the angle limit wrecked the jiggle chains on
        // a different avatar, and an uncapped radius turned a 0.5 into metre-wide particles on
        // a third. They are genuinely avatar-dependent, so they are exposed rather than decided.
        public bool transferAngleLimits = false;
        // After the preset loads, apply the handful of PhysBone facts that mean the same thing
        // in MagicaCloth2 — no gravity, upward gravity, and immobile (which is world influence
        // inverted).
        public bool fitToPhysBone = true;
        // Derive damping and angle restoration from the PhysBone's own pull/spring/stiffness,
        // instead of leaving the preset's. Both solvers turned out to be position integrators
        // with per-step coefficients at a fixed, known rate — PhysBone 60 Hz, MagicaCloth2 90 Hz
        // — so a real conversion exists and PhysBoneSolverMap derives it from both sources.
        //
        // ON by default as of 2.38.0, after a full avatar's chains were checked in ChilloutVR and
        // came back matching the source closely enough to call done. The three faults found on the
        // way there are all fixed and all were structural rather than errors in the derivation:
        // angle restoration compounding three times per step, MagicaCloth2's wind being live when
        // VRChat has none, and immobile reaching only one of the two inertia values.
        //
        // Replaces the preset's damping and restoration only — structure, gravity, immobile and
        // radius are untouched, and turning this off restores the preset's feel exactly.
        public bool derivePhysicsFromPhysBone = true;
        public bool capParticleRadius = true;
        // Off by default: VRChat avatars routinely carry per-toe PhysBones, and simulated toes
        // in ChilloutVR wiggle with every step — read as broken, not expressive. Chains rooted
        // at (or under) a humanoid Toes bone, or whose root is named like a toe, are skipped
        // with a report entry unless this is on.
        public bool convertToePhysBones = false;
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

        // Use ChilloutVR's native contact components instead of the pointer/trigger
        // approximation. They line up with VRChat's almost field for field — same shapes, same
        // collision tags, real proximity, and localOnly actually honoured — and because every
        // client simulates them for every avatar, they replicate without costing sync bits.
        //
        // Off by default because the components are not in the CCK: AvatarBridge declares them
        // itself (see ContactStubPatcher) and the binding can only be proven by putting an avatar
        // in game. If it were wrong, the result is contact objects with a missing script.
        public bool useNativeContacts = false;

        // Give merged VRChat layers an avatar mask that blocks humanoid muscles, restoring the
        // separation VRChat gets from FX being its own playable layer. Guards against a
        // Write-Defaults state re-asserting the rest pose over ChilloutVR's locomotion.
        //
        // CONFIRMED to fix the "bicycle pose" — an avatar standing in a bent rest pose in game
        // while only the head and hands follow tracking. Tested in ChilloutVR on a tester's
        // avatar: 20 merged layers masked, 18 blocked from muscles and 2 narrowed to the hands,
        // and the pose came right. An earlier note here said this had never been observed helping
        // anyone; that is no longer true.
        //
        // Still off by default, because it touches every merged layer on the avatar and the
        // exposure is universal while the confirmed cases are not. Layers that animate the body
        // on purpose are skipped, so it is safe to try on anything — and the report names the
        // suspect layers whether it is on or off.
        public bool maskMergedLayers = false;

        // Copy shaders that lack single-pass instanced stereo support into RehomedAssets with the
        // required macros added, and repoint this avatar's materials at the copies. Originals are
        // never modified, and a copy that fails to compile is discarded.
        //
        // The need is ChilloutVR-specific: the CCK forces single-pass instanced stereo where the
        // VRChat SDK forces double-wide, and only instancing requires the shader to opt in. The
        // macros themselves are mode-agnostic, so a patched copy stays valid under either.
        //
        // Off by default: compilation can be verified, appearance cannot. A patched shader is
        // fixing something already broken in ChilloutVR, so the downside is small, but it should
        // be looked at in both eyes before trusting it.
        public bool patchNonSpiShaders = false;

        [Header("Other components")]
        public bool convertConstraints = true;
        public bool convertHeadChop = true;
        public bool convertSpatialAudio = true;
        // If the VRChat descriptor didn't set an eyelid/blink blendshape, auto-detect blink
        // shapes on the face mesh (e.g. "Blink L"/"Blink R") and wire CVR's Eye Blink Settings.
        public bool wireBlinkBlendshapes = true;
        // Inject the bundled avatar scaler (Linear Smoothing + generated Size layer + a
        // "Height" slider, 0.25×–4× geometric). Auto-calibrated: the centre of the slider is the avatar's
        // measured eye height, so it spawns at its original size; every doubling gets equal travel.
        public bool addAvatarScaler = true;
        // How to handle face tracking. Native = auto-set-up CVRFaceTracking (blendshape-
        // based); DragonSkyRunner = inject that package's animator layers/params; None =
        // leave it to the user. Native and DragonSkyRunner both strip any existing FT rig.
        public FaceTrackingMode faceTrackingMode = FaceTrackingMode.Native;
    }
}
