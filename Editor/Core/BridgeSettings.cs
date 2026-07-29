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
        // Steps that used to be options here — VRCFury/MA baking, VRC-component cleanup,
        // Fury toggle rebuilding, humanoid masking — always run now. Every off-state
        // produced a conversion that was broken or read as broken; necessary steps
        // aren't options.
        [Header("General")]
        public bool cloneAvatar = true;
        // OUTSIDE the tool's own folder, deliberately: conversions used to default into
        // Assets/AvatarBridge/Output, and the delete-and-reimport update flow everyone uses
        // for .unitypackage tools erased every converted controller, override, prefab and
        // rehomed material along with the old version — Missing references and pink particles
        // across every converted avatar in the project. User data lives where the tool's
        // uninstall can't reach it.
        public string outputFolder = "Assets/AvatarBridgeOutput";

        [Header("Animator layers to merge")]
        public bool convertBaseLayer = false;
        public bool convertAdditiveLayer = false;
        public bool convertGestureLayer = true;
        public bool convertActionLayer = false;
        public bool convertFxLayer = true;

        [Header("Parameters")]
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
        // Off by default, same reason as above: it invents physics the author never made. Some
        // avatars ship a toggled style (an add-on hairstyle, usually) whose container carries
        // its own bone rig and skinned mesh but NO PhysBone — rigid in VRChat, whether by
        // intent or by the author forgetting. With this on, such rigs get a synthesized
        // MagicaCloth: preset chosen by the chain classifier (which also reads ancestor names,
        // so a nondescript rig under "Vampy Hair" still classifies as hair), no derived feel
        // because there is nothing to derive from. Off because some rigged props are rigid on
        // purpose. MagicaCloth2 target only.
        public bool addPhysicsToRiggedStyles = false;

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
        // Animation writing to material properties the renderer's shader does not have — the
        // signature of a locked Poiyomi shader that baked them away. Such a curve does nothing
        // in ChilloutVR and did nothing in VRChat either. Removing it, and anything left with
        // no purpose, keeps dead sliders and toggles out of the avatar's menu. Renderers whose
        // materials are swapped by an animation are never touched.
        public bool stripDeadMaterialAnimation = true;

        [Header("Contacts")]
        public bool convertContacts = true;
        // Recreate VRChat's built-in hand/head/torso colliders as CVR pointers so contact
        // receivers keep reacting to other players' hands.
        public bool createDefaultColliderPointers = true;

        // Use ChilloutVR's native contact components instead of the pointer/trigger
        // approximation. They line up with VRChat's almost field for field — same Sphere/Capsule
        // shapes, same collision tags, real proximity.
        //
        // The network story, settled with the system's author (NotAKidoS, 2026-07-28) and
        // confirmed in game: contacts are per-client BY DESIGN. Every client simulates every
        // avatar's contacts itself, so reactions work over the network with no sync involved
        // and no sync bits spent; a driven parameter's own AAS declaration decides whether its
        // VALUE also replicates. (This comment has flip-flopped: "replicates without costing
        // sync bits" → "not synced, others may not see it" → this. The middle take
        // over-corrected from a second-hand report; the test that settled it was a second
        // player seeing the reaction with no synced parameter in the loop.)
        //
        // Off by default, and the window notes while it is on that this is EXPERIMENTAL: it
        // talks to a component internal to the game, so any ChilloutVR update can break it —
        // possibly for good, since nothing on this side can resurrect a component the game
        // stops shipping. A same-shaped breakage already happened once in reverse (2.50.3
        // trusted the author's public repo over the shipped game and every receiver went deaf;
        // see ContactStubPatcher). The components are not in the CCK: AvatarBridge declares
        // them itself, verified field-for-field against the DECOMPILED CLIENT, which is the
        // only authority — do not import the public repo into a conversion project while it
        // disagrees with the game.
        public bool useNativeContacts = false;


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
