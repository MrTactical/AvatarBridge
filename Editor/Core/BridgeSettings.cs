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
        AnimatorLayers,
        CvrNativeTargets
    }

    public enum FaceTrackingMode
    {
        Native,
        DragonSkyRunner,
        None
    }

    // All user-facing conversion options. Serialized so the window
    // remembers them. Settings the avatar can measure belong in the
    // Automated card with an AvatarAdvisor check; settings that depend
    // on intent belong in Manual. Headers here group by subject only;
    // the window builds its own layout.
    [Serializable]
    public class BridgeSettings
    {
        // Baking, cleanup, toggle rebuilding and masking always run.
        // Necessary steps are not options.
        [Header("General")]
        public bool cloneAvatar = true;
        // Outside the tool's own folder. Deleting Assets/AvatarBridge
        // to update must never erase conversions.
        public string outputFolder = "Assets/AvatarBridgeOutput";

        [Header("Animator layers to merge")]
        // Merged masked off the humanoid body; custom walk, stance and
        // fall clips graft into CVR's locomotion. See LocomotionGrafter.
        public bool convertBaseLayer = false;
        public bool convertAdditiveLayer = false;
        public bool convertGestureLayer = true;
        public bool convertActionLayer = false;
        public bool convertFxLayer = true;

        [Header("Parameters")]
        // AnimatorLayers keeps toggles inside the generated controller.
        // CvrNativeTargets defers object toggles to the CCK's builder.
        public ToggleStyle toggleStyle = ToggleStyle.AnimatorLayers;
        // Parameters not synced in VRChat get the "#" local prefix.
        // Network traffic then matches the original avatar.
        public bool preserveParameterSyncState = true;
        // Synced parameters with no menu control get an AAS entry
        // anyway. Not needed for sync; it buys profile persistence
        // and a place to drive the value by hand.
        public bool exposeMenulessSyncedParameters = true;

        [Header("Physics")]
        public PhysicsTarget physicsTarget = PhysicsTarget.MagicaCloth2;
        public bool deleteConvertedPhysBones = true;
        // Name converted MagicaCloth objects after their PhysBone parameter so the
        // GrabbyBones mod drives the avatar's _IsGrabbed / _Angle grab-reactive logic.
        public bool grabbyBonesSupport = true;
        // Start each cloth from the MagicaCloth2 preset that fits the
        // chain. Structure comes from the PhysBone either way.
        public bool useMagicaPresets = true;
        // After the preset loads, apply the PhysBone facts that mean
        // the same thing in MagicaCloth2: no gravity, upward gravity,
        // and immobile (world influence inverted).
        public bool fitToPhysBone = true;
        // Derive damping and angle restoration from the PhysBone's
        // pull, spring and stiffness. Both solvers are position
        // integrators at fixed rates (60 Hz vs 90 Hz), so a real
        // conversion exists; PhysBoneSolverMap holds it. Replaces the
        // preset's damping and restoration only.
        public bool derivePhysicsFromPhysBone = true;
        // Size each chain's particles from the mesh those bones move.
        // The source PhysBone radius is not used; in VRChat it only
        // governs PhysBone-collider contact and is routinely near zero.
        public bool fitRadiusToMesh = true;

        // Fit each converted collider to the body part it sits on.
        // A PhysBone collider is one radius end to end; the capsule
        // tapers. The measurement replaces the source's numbers.
        // Only the host bone's own vertices are read.
        public bool fitCollidersToMesh = true;

        // Take the humanoid Jaw mapping off a bone that is not a jaw.
        // CVR puts the Auto voice position on the jaw bone and jaw
        // visemes animate it, so a misassigned Jaw misplaces the voice
        // and waggles the object.
        public bool unmapMisplacedJaw = true;

        // Measure the mesh again with every animated blendshape at
        // full reach and keep the larger reading. MagicaCloth2's
        // radius cannot be animated, so collision covers the body
        // when a size slider is up. Shrinking shapes cost nothing.
        public bool sizePhysicsForLargest = true;
        // Honour the source's Angle/Hinge/Polar limit as a distance
        // bound from rest. A distance bound removes motion rather than
        // adding a restoring force, so it cannot start vibration.
        public bool boundSwingToSourceLimit = true;
        // Bound the particle to half the bone gap. Off since the
        // radius became a measurement; on a soft body this throws most
        // of the measurement away. The overlap it guards against only
        // matters with self-collision, which ships off.
        public bool capParticleRadius = false;
        // Simulated toes wiggle with every step in CVR, which reads as
        // broken. Chains rooted at or under a Toes bone are skipped
        // with a report entry unless this is on.
        public bool convertToePhysBones = false;
        // Deliberately departs from the source. A chain the author
        // never wired stays uncollided either way; this improves on
        // the original rather than reproducing it.
        public bool autoAssignNearbyColliders = false;
        // Invents physics the author never made. A toggled style with
        // its own rig and mesh but no PhysBone gets a synthesized
        // MagicaCloth, preset by classification. Off because some
        // rigged props are rigid on purpose. MagicaCloth2 only.
        public bool addPhysicsToRiggedStyles = false;

        [Header("VRChat-only system stripping")]
        // CVR provides locomotion and emotes natively. GoGo's layers
        // fight them while burning sync budget.
        // Measure the converted avatar and say what to fix: texture memory
        // against the surface it covers, contacts against the instance's
        // budget, triangles, cloth, locked shader copies. Reads only, and
        // changes nothing it finds.
        public bool weighAvatar = true;
        // Read the animator, the menu and the components into one model and
        // report what the avatar can and cannot do: features nothing can
        // reach, bindings two layers fight over, what lost its driver in the
        // conversion. Reads only.
        public bool surveyAvatar = true;
        public bool stripGogoLoco = true;
        // SPS/OGB/TPS, PCS and Wholesome are VRChat-specific.
        // Non-functional in CVR, and expensive in sync bits.
        public bool stripSpsSystems = true;
        // Convert DPS, TPS and SPS into YAPS instead of stripping them.
        public bool convertYapsSystems = true;
        // Keep VRChat's OGB/PCS/Wholesome haptics contacts. Off: they are
        // stripped with their triggers. Each is a contact, and ChilloutVR
        // budgets 512 overlapping pairs per frame for the whole instance,
        // so a hundred of them per avatar is what breaks contacts in a
        // crowd. Only worth it to drive a toy from them.
        public bool keepHapticsContacts = false;
        // Keep the OGB haptics parameters synced instead of local, so OSC
        // toy apps see them under their VRChat names. 32 sync bits each.
        // Needs keepHapticsContacts.
        public bool syncHapticsForOsc = false;
        // Keep each socket's depth parameter synced, so the author's own
        // depth reactions play for the room. Two per socket, 32 bits each.
        public bool syncSocketDepthForOthers = false;
        // How many sockets START lit; the lighthouse menu moves the lit
        // pair after that, so this stopped being a per-avatar setting.
        //
        // One, not two. The third slot belongs to the tracker light of the
        // prop or partner entering the socket, which lives on content this
        // avatar cannot see at build time. Two lit sockets plus that tracker
        // is five lights for four slots, and Unity drops the lowest range,
        // which is always the hole root.
        internal const int DefaultMaxLightEmittingSockets = 1;
        // How far the smoothed channel value may move per frame. Lower is a
        // heavier plug. Only the channel is buffered.
        //
        // 0.05 to 0.02 on 2026-08-27, measured rather than guessed. The
        // smoother is first order, so the fraction of an input wobble that
        // reaches the material scales with this: 86 per cent at 0.05, 29 at
        // 0.02, 14 at 0.01. The channel's own resolution is about a
        // millimetre, one quantisation step of a synced float across the
        // channel box, and the deform is sensitive enough to show one step
        // as a tremble. Rejecting it costs tracking speed by exactly as
        // much, so this is a choice of where to sit, not a bug to fix.
        //
        // 0.02 gives a third of the tremble for roughly 0.3s of trailing
        // while a socket is actually moving. It only matters where no marker
        // light is in range, because a light replaces the channel's position
        // outright and is sampled every frame; a viewer with avatar lights
        // switched off is the case this serves.
        [Range(0.01f, 0.5f)]
        public float yapsSocketFollow = DefaultSocketFollow;
        internal const float DefaultSocketFollow = 0.02f;
        // Comma-separated. Matched as parameter prefixes and
        // layer-name substrings.
        public string extraStripKeywords = "";
        // Curves writing material properties the shader lacks, the
        // signature of a locked Poiyomi shader. Dead in VRChat too.
        // Swapped-material renderers are never touched.
        public bool stripDeadMaterialAnimation = true;

        [Header("Contacts")]
        public bool convertContacts = true;
        // Recreate VRChat's built-in colliders as CVR pointers, so
        // receivers keep reacting to other players' hands.
        public bool createDefaultColliderPointers = true;

        // A zone authored on a body part a slider can grow keeps its
        // authored size while the mesh grows past it, so the touch
        // lands inside the body and never reaches it. Measured like
        // the physics sizes: at rest and with every animated shape at
        // full reach. A zone one slider grows is animated along with
        // it; growth spread across shapes holds the grown size.
        public bool sizeContactZonesForLargest = true;

        // Copy shaders lacking single-pass instanced support into
        // RehomedAssets with the macros added, and repoint materials.
        // Originals are never modified; a copy that fails to compile
        // is discarded. The macros are mode-agnostic.
        //
        // Off by default. Compilation can be verified; appearance
        // needs checking in both eyes.
        public bool patchNonSpiShaders = false;

        [Header("Other components")]
        public bool convertConstraints = true;
        public bool convertHeadChop = true;
        public bool convertSpatialAudio = true;
        // If the descriptor set no blink blendshape, detect blink
        // shapes on the face mesh and wire CVR's Eye Blink Settings.
        public bool wireBlinkBlendshapes = true;
        // Inject the bundled scaler: a "Height" slider, 0.25x to 4x
        // geometric, centred on the measured eye height. Also enables
        // ConstraintScaleRelay so props scale with the avatar.
        public bool addAvatarScaler = true;
        // Native sets up CVRFaceTracking; DragonSkyRunner injects that
        // package's rig; None leaves it alone. The two set-up modes
        // strip any existing FT rig.
        public FaceTrackingMode faceTrackingMode = FaceTrackingMode.Native;
    }
}
