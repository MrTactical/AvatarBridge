// The squash on an avatar: how it feels, what presses it, and optionally the regions painted
// with the squash region tool in place of finding the parts by bone name. The toolkit and the
// converter build from it and write the knobs to the materials. ChilloutVR strips it at upload.
// No SDK dependency.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AvatarBridge.Yaps
{
    [AddComponentMenu("YAPS/YAPS Squash")]
    [DisallowMultipleComponent]
    public class YapsSquashSettings : MonoBehaviour
    {
        [Serializable]
        public class Pair
        {
            public string name = "Pair";
            // Per vertex of the body mesh, 0 to 255. A is the side toward the avatar's left
            // (its -x), B the other. Bytes, so a body's worth stays small in the scene file.
            public byte[] a = new byte[0], b = new byte[0];
        }

        [Header("How it gives")]
        [Range(0f, 2f), Tooltip("How hard every press lands: the partner, hands and plugs alike.")]
        public float strength = 1f;
        [Range(0.2f, 1f), Tooltip("How flat a part can be pressed, as a share of its depth. 1 never flattens.")]
        public float flattest = 0.6f;
        [Range(1f, 2.5f), Tooltip("How far a flattened part may widen to keep its volume.")]
        public float widest = 1.5f;
        [Range(0f, 1f), Tooltip("1 keeps the volume, so it widens as it flattens. Lower lets some of it go.")]
        public float volumeKept = 1f;
        [Range(0f, 1f), Tooltip("How far a part swings away from a press before it squashes, as a share of its reach out of the body.")]
        public float bendGive = 0.35f;
        [Range(0f, 1f), Tooltip("How deep a hand dents before the whole part moves, as a share of its depth.")]
        public float deepestDent = 0.35f;
        [Range(0f, 1f), Tooltip("Lying back, how much its own weight flattens it into the body.")]
        public float lyingBack = 0.25f;

        [Header("The pair")]
        [Tooltip("Off, what still overlaps after the parts swing apart and flatten passes through. On, the two press flat where both push into each other, swelling out round the contact. One swung or pushed by a hand into a still partner shunts it aside either way.")]
        public bool meet = true;
        [Range(0f, 2f), Tooltip("How much of what the meeting pushes back swells out round the contact: 1 keeps its volume.")]
        public float spill = 1f;

        [Header("In motion")]
        [Tooltip("As its physics swing it, a part stretches away from where it hangs and squashes back toward it, " +
                 "keeping its volume. The swing is the physics' own, unchanged.")]
        public bool stretchInMotion = true;
        [Range(0f, 1f), Tooltip("0 like real flesh: a small stretch, and small swings ignored. 1 like anime: a long " +
                                "stretch and a deep squash on every bounce.")]
        public float motionStyle = 0f;

        [Header("What presses it")]
        [Range(0f, 2f), Tooltip("The wearer's own arms and hands. 0 leaves them out.")]
        public float ownArms = 1f;
        [Range(0f, 2f), Tooltip("Other players' hands, from the hand positions ChilloutVR gives every shader.")]
        public float othersHands = 1f;
        [Range(0.02f, 0.1f), Tooltip("Another player's hand, as a ball of this radius in metres on a 1.6 m player, scaled to their height.")]
        public float handSize = 0.05f;
        [Range(0f, 2f), Tooltip("A plug in a socket between the pair, parting it.")]
        public float plugs = 1f;
        [Range(0f, 2f), Tooltip("Pressed by whatever the part's physics collide with, from the side it was pushed. " +
                                "Off by default: every swing read as a press.")]
        public float clothPush = 0f;

        // Drawn by the inspector's Parts card, not with the knobs: both need a rebuild.
        [Tooltip("Bones picked by hand in place of the name search, two to a pair, one each side, " +
                 "next to each other in the list: cheeks, thighs, anything that comes in two. Two pairs at most. " +
                 "Painted regions win over these.")]
        public List<Transform> partBones = new List<Transform>();
        [Tooltip("Materials the squash never touches. A layer left out stays still while the skin under it " +
                 "squashes, so a press can show the skin through it.")]
        public List<Material> leaveAlone = new List<Material>();

        // The regions, painted in the inspector's first step. None: the parts come from the
        // picked bones, or else the bone names.
        [HideInInspector]
        public SkinnedMeshRenderer body;

        // Two at most: the shader has room for four parts, a pair each.
        [HideInInspector]
        public List<Pair> pairs = new List<Pair>();

        // What the weights were painted against. A mesh that changed since is refused rather
        // than read with every weight on the wrong vertex.
        [HideInInspector] public int vertexCount;
        [HideInInspector] public int fingerprint;

        // The regions the last build read, so the windows can say a stroke since needs a rebuild.
        [HideInInspector] public int builtFrom;
    }
}
