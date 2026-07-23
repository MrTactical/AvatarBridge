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
        public string outputFolder = "Assets/AvatarBridge/Output";

        [Header("Animator layers to merge")]
        public bool convertBaseLayer = false;
        public bool convertAdditiveLayer = false;
        public bool convertGestureLayer = true;
        public bool convertActionLayer = false;
        public bool convertFxLayer = true;

        [Header("Parameters")]
        // Simple object on/off toggles get handed to CVR's own animator builder: the
        // Fury-baked FX layer is removed and the CCK generates a clean bool toggle.
        public bool nativizeObjectToggles = true;
        // When enabled, animator parameters that are not network-synced in VRChat get the
        // CVR "#" local-only prefix so network traffic matches the original avatar.
        public bool preserveParameterSyncState = true;
        // Expose VRChat-synced parameters that have no menu control as CVR menu entries so
        // they still sync (contacts/OSC-driven setups).
        public bool exposeMenulessSyncedParameters = true;

        [Header("Physics")]
        public PhysicsTarget physicsTarget = PhysicsTarget.MagicaCloth2;
        public bool deleteConvertedPhysBones = true;

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
    }
}
