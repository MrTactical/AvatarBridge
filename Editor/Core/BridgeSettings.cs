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

    // Serialized so the window remembers them. Settings the avatar can
    // measure go in the Automated card with an AvatarAdvisor check;
    // intent-dependent ones go in Manual. Headers group by subject only.
    [Serializable]
    public class BridgeSettings
    {
        // Baking, cleanup, toggle rebuilding and masking always run, so are not options.
        [Header("General")]
        public bool cloneAvatar = true;
        // Outside the tool's folder, so deleting it to update never erases conversions.
        public string outputFolder = "Assets/AvatarBridgeOutput";
        // Off: converting again overwrites the saved prefab, and an older linked
        // scene copy would change with it.
        public bool linkConvertedPrefab = false;

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
        // Name physics holders after their PhysBone parameter so the GrabbyBones
        // mod drives the avatar's _IsGrabbed / _Angle logic.
        public bool grabbyBonesSupport = true;
        // Merge identical PhysBone chains into one multi-root cloth: each MagicaCloth2
        // component costs a proxy mesh and a solver team. Only chains matching on
        // settings, colliders, parent, kind, bone lengths and mesh thickness, and
        // that nothing toggles on its own.
        public bool groupIdenticalPhysBones = true;
        // Start each cloth from the MagicaCloth2 preset that fits the
        // chain. Structure comes from the PhysBone either way.
        public bool useMagicaPresets = true;
        // Over the preset: PhysBone gravity (as settle point), immobile (as inertia)
        // and wind (none); preset angle limit and depth inertia off, speed limits raised.
        public bool fitToPhysBone = true;
        // Both solvers integrate positions at fixed rates (60 Hz vs 90 Hz), so a real
        // conversion exists (PhysBoneSolverMap). Replaces damping, restoration and
        // velocity attenuation only.
        public bool derivePhysicsFromPhysBone = true;
        // The PhysBone radius only governs PhysBone-collider contact in VRChat and is
        // routinely near zero, so particles are sized from the mesh instead.
        public bool fitRadiusToMesh = true;

        // A PhysBone collider is one radius end to end; the capsule tapers.
        // Only the host bone's own vertices are read.
        public bool fitCollidersToMesh = true;


        // MagicaCloth2's radius cannot be animated, so measure again with every animated
        // blendshape at full reach and keep the larger reading.
        public bool sizePhysicsForLargest = true;
        // Source Angle/Hinge/Polar limit as a distance bound from rest: it removes motion
        // instead of adding force, so it cannot start vibration. Soft bodies have no
        // distance bounds in MagicaCloth2: breast or belly get an angle limit, thighs and butt none.
        public bool boundSwingToSourceLimit = true;
        // Half the bone gap. Off: on a soft body it throws away most of the measured
        // radius, and the overlap it guards only matters with self-collision, which ships off.
        public bool capParticleRadius = false;
        // Simulated toes wiggle with every step in CVR and read as broken, so Toes chains are skipped.
        public bool convertToePhysBones = false;
        // Departs from the source: a chain the author never wired stays uncollided otherwise.
        public bool autoAssignNearbyColliders = false;
        // Synthesizes MagicaCloth for a toggled rigged style with no PhysBone. Off: some
        // rigged props are rigid on purpose. The Toolkit leaves it off, it cannot see hand-placed cloth.
        public bool addPhysicsToRiggedStyles = false;

        [Header("VRChat-only system stripping")]
        // Read-only: texture memory per surface, contacts against the instance budget,
        // triangles, cloth, locked shader copies.
        public bool weighAvatar = true;
        // Read-only: unreachable features, overridden toggles, states that never play,
        // what lost its driver in the conversion.
        public bool surveyAvatar = true;
        // CVR provides locomotion and emotes natively. GoGo's layers
        // fight them while burning sync budget.
        public bool stripGogoLoco = true;
        // GoGo's own emotes, then its sit and lie poses, fill ChilloutVR's eight emote
        // slots in place of the stock eight. Only while GoGo is removed.
        public bool gogoEmotes = true;
        // SPS/OGB/TPS, PCS and Wholesome are VRChat-specific.
        // Non-functional in CVR, and expensive in sync bits.
        public bool stripSpsSystems = true;
        // Convert DPS, TPS and SPS into YAPS instead of stripping them.
        public bool convertYapsSystems = true;
        // Off by default: ChilloutVR budgets 512 overlapping contact pairs per frame for
        // the whole instance, and a hundred haptics contacts per avatar breaks contacts
        // in a crowd. Only worth it to drive a toy.
        public bool keepHapticsContacts = false;
        // So OSC toy apps see them under their VRChat names. 32 sync bits each. Needs keepHapticsContacts.
        public bool syncHapticsForOsc = false;
        // So the author's depth reactions play for the room. 32 bits per depth parameter.
        public bool syncSocketDepthForOthers = false;
        // Needs the YAPS add-on. A humanoid's pairs go on the jelly (GPU tissue that only
        // dents the skin; cloth and Dynamic Bones still swing them). Solved presses apply
        // only to parts the tissue cannot take.
        public bool squashSoftParts = false;
        // Comma-separated parameter prefixes and layer-name substrings.
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

        // A zone on a part a slider grows stays authored size while the mesh grows past it,
        // so touches land inside the body. One slider: animated along; spread growth: held grown.
        public bool sizeContactZonesForLargest = true;

        // Resizes over-resolved textures and records it so it can be undone. On: the only
        // other user of such a texture is the VRChat copy of the same avatar, where the same
        // size fits. Textures shared with outside materials are refused and named. Sizes, plus a
        // smaller format where a PNG or JPG's own pixels allow. Renderer stripping stays on the button.
        public bool slimTexturesOnConvert = true;

        // Copies non-SPI shaders with the macros added; originals untouched, copies that fail
        // to compile are discarded. Off: compiling can be verified, both-eye appearance cannot.
        public bool patchNonSpiShaders = false;

        [Header("Other components")]
        public bool convertConstraints = true;
        public bool convertHeadChop = true;
        public bool convertSpatialAudio = true;
        // If the descriptor set no blink blendshape, detect blink
        // shapes on the face mesh and wire CVR's Eye Blink Settings.
        public bool wireBlinkBlendshapes = true;
        // "Height" slider, 0.25x to 4x geometric around the measured eye height. Also
        // enables ConstraintScaleRelay so props scale with the avatar.
        public bool addAvatarScaler = true;
        // Native sets up CVRFaceTracking, DragonSkyRunner injects that package's rig, None
        // leaves it alone. Both set-up modes strip any existing FT rig.
        public FaceTrackingMode faceTrackingMode = FaceTrackingMode.Native;
    }
}
