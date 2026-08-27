// A YAPS plug: authoring data for the mesh that bends.
// The toolkit bakes from it and writes the knobs to the material.
// ChilloutVR strips it at upload. No SDK dependency.
using UnityEngine;

namespace AvatarBridge.Yaps
{
    [AddComponentMenu("YAPS/YAPS Plug")]
    [DisallowMultipleComponent]
    public class YapsPlug : MonoBehaviour
    {
        [Tooltip("The mesh that bends. Leave empty to use the renderer on this object.")]
        public Renderer renderer;

        [Tooltip("Which of the renderer's materials is the plug. Leave it at -1 unless you mean " +
                 "otherwise: -1 bakes EVERY material the bone chain reaches, which is one of them " +
                 "for an ordinary plug and all of them when a whole avatar is the plug. A number " +
                 "means that slot alone, so a plug whose vertices span several materials will bend " +
                 "in one and stay rigid in the rest, tearing along the seam.")]
        public int materialSlot = -1;

        [Header("Skinned mesh")]
        [Tooltip("The bone the shaft grows from. On a skinned mesh, only vertices weighted to " +
                 "this bone and its children are the plug; the rest of the mesh stays put. " +
                 "Leave empty on a mesh that is only the plug.")]
        public Transform rootBone;

        [Header("Measurement")]
        [Tooltip("The toolkit measures the shaft's axis and length from the mesh itself. If it " +
                 "picked the wrong end as the base, tick this to flip it. Skinned meshes only; a plain " +
                 "mesh bends around its own origin along +Z.")]
        public bool flipAxis;

        [Tooltip("Optional. If the measured length is wrong for your mesh, state it here in " +
                 "metres. 0 uses the measurement.")]
        public float lengthOverride;

        [Header("Shape at rest")]
        [Range(-1.5f, 1.5f), Tooltip("A resting bend along the whole shaft. Positive bends up.")]
        [YapsFrom("DPS")]
        public float curvature;
        [Range(-1.5f, 1.5f), Tooltip("A second bend near the tip, opposite in sign, so the shaft can sweep and then hook.")]
        [YapsFrom("DPS")]
        public float recurvature;
        [Range(0f, 1f), Tooltip("How much the base resists bending toward a socket. 0 bends evenly from the root.")]
        [YapsFrom("DPS")]
        public float entranceStiffness;

        [Header("Inside a socket")]
        [Range(0f, 1f), Tooltip("How much the socket narrows the shaft where it grips.")]
        [YapsFrom("DPS · TPS")]
        public float squeeze;
        [Range(0.01f, 1f), Tooltip("How far either side of the opening the grip reaches, as a fraction of length.")]
        [YapsFrom("DPS · TPS")]
        public float squeezeReach = 0.15f;
        [Range(0f, 1f), Tooltip("The swell just short of the opening, as a fraction of radius.")]
        [YapsFrom("DPS · TPS")]
        public float bulge;
        [Range(0.01f, 1f), Tooltip("How far before the opening the swell begins.")]
        [YapsFrom("DPS · TPS")]
        public float bulgeReach = 0.2f;

        [Header("Out of a socket")]
        [Range(0.1f, 1f), Tooltip("How much of its length it keeps when no socket is using it. 1 is no change.")]
        [YapsFrom("TPS")]
        public float idleLength = 1f;
        [Range(0.1f, 1f), Tooltip("How much of its width it keeps when no socket is using it.")]
        [YapsFrom("TPS")]
        public float idleWidth = 1f;
        [Range(0f, 0.5f), Tooltip("Idle motion, tip-heavy, only while out of a socket. Animates over time — the scene view shows it while this plug is selected.")]
        [YapsFrom("DPS")]
        public float wriggle;
        [Range(0f, 20f), Tooltip("How fast it wriggles.")]
        [YapsFrom("DPS")]
        public float wriggleSpeed = 2f;

        [Header("Motion inside a socket")]
        [Range(0f, 0.5f), Tooltip("A stroke along the shaft, only while a socket has it.")]
        [YapsFrom("TPS")]
        public float pumping;
        [Range(0f, 20f), Tooltip("How fast it pumps.")]
        [YapsFrom("TPS")]
        public float pumpingSpeed = 6f;
        [Range(0.05f, 1f), Tooltip("How much of the shaft pumps. 1 is the whole length; small values move only the tip.")]
        [YapsFrom("TPS")]
        public float pumpingWidth = 1f;

        [Header("The bend toward a socket")]
        [Range(0.2f, 3f), Tooltip("Below 1 arrives more directly, above 1 sweeps a wider arc.")]
        [YapsFrom("TPS")]
        public float bezierSmoothness = 1f;
        [Range(0f, 0.8f), Tooltip("A fraction of the shaft held straight before any bend.")]
        [YapsFrom("TPS")]
        public float straightBeforeBend;
        [Range(0f, 0.5f), Tooltip("Ease the join between the straight part and the curve.")]
        [YapsFrom("TPS")]
        public float easeIntoBend;
        [Range(0f, 1f), Tooltip("A socket nearer than this (fraction of length) is held off, so a plug pushed hard against one does not fold.")]
        [YapsFrom("TPS")]
        public float minimumSocketDistance;

        [Header("Past the opening")]
        [Range(0f, 1f), Tooltip("How far past a hole before the shaft narrows, as a fraction of length.")]
        [YapsFrom("YAPS")]
        public float taperStart = 0.10f;
        [Range(0f, 1f), Tooltip("...and how far past it before the shaft has closed to a point.")]
        [YapsFrom("YAPS")]
        public float taperEnd = 0.30f;
        [Tooltip("Let the tip carry on past a ring. Off, the shaft stops at every socket.")]
        [YapsFrom("SPS")]
        public bool overrun = true;

        [Header("How sockets find it")]
        [Tooltip("Emit the tip light Raliv DPS orifices read, and the contact pointers TPS and " +
                 "SPS sockets read. Both on unless you know why not.")]
        [YapsFrom("DPS")]
        public bool emitTipLight = true;
        [YapsFrom("TPS · SPS")]
        public bool emitPointers = true;

        // The material the bake replaced, so Remove can put it back. Set
        // by the first bake; the toolkit's own, not a knob.
        [HideInInspector]
        public Material bakedFrom;

        // One entry per material slot the bake replaced, because a plug's
        // vertices can span several. A whole avatar baked as one plug wears
        // three materials, and patching only one leaves the rest of the mesh
        // rigid while the patched part bends — the mesh tears along the
        // seam. bakedFrom above stays for the primary slot so an avatar
        // baked before this still restores.
        [System.Serializable]
        public class BakedSlot
        {
            public int slot;
            public Material was;

            // Which renderer's slot. A plug rooted high enough spans several
            // meshes, and slot 0 of a collar is not slot 0 of the body — a
            // record keyed on the number alone puts one mesh's material onto
            // another. Empty means the plug's own renderer, so records
            // written before a plug could span meshes still resolve.
            public Renderer renderer;
        }

        [HideInInspector]
        public System.Collections.Generic.List<BakedSlot> bakedSlots =
            new System.Collections.Generic.List<BakedSlot>();

        public Renderer Target => renderer != null ? renderer : GetComponent<Renderer>();
    }
}
