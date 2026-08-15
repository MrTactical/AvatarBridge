// A YAPS plug, as a component you place on the mesh that should bend.
//
// Authoring intent only, like YapsSocket: which renderer and material,
// which bones carry the shaft (for a skinned mesh), where the base is if
// the measurement picked the wrong end, and the knobs. The toolkit reads
// it, measures the mesh, bakes, patches the material's own shader and
// writes the knobs onto it. ChilloutVR strips this at upload; leaving it
// on means the toolkit edits your choices next time instead of guessing.
//
// The knobs here are the same ones the material carries after a bake.
// They live here too so a plug can be set up BEFORE it has been baked,
// and so re-baking (a new mesh, a changed bone chain) does not lose them.
//
// No dependency on any SDK, no #if. It compiles in an empty project.
using UnityEngine;

namespace AvatarBridge.Yaps
{
    [AddComponentMenu("YAPS/YAPS Plug")]
    [DisallowMultipleComponent]
    public class YapsPlug : MonoBehaviour
    {
        [Tooltip("The mesh that bends. Leave empty to use the renderer on this object.")]
        public Renderer renderer;

        [Tooltip("Which of the renderer's materials is the plug. -1 means the first material " +
                 "whose vertices the bone chain moves, or slot 0 on a static mesh.")]
        public int materialSlot = -1;

        [Header("Skinned mesh")]
        [Tooltip("The bone the shaft grows from. On a skinned mesh, only vertices weighted to " +
                 "this bone and its children are the plug; the rest of the mesh stays put. " +
                 "Leave empty on a mesh that is only the plug.")]
        public Transform rootBone;

        [Header("Measurement")]
        [Tooltip("The toolkit measures the shaft's axis and length from the mesh itself. If it " +
                 "picked the wrong end as the base, tick this to flip it.")]
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
        [Range(0.01f, 0.5f), Tooltip("How fast the plug follows a socket the contact channel reports. Lower is heavier.")]
        [YapsFrom("TPS · YAPS")]
        public float socketFollow = 0.05f;

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

        [Header("Which sockets it answers")]
        [Tooltip("Only sockets carrying this tag. Blank answers all.")]
        [YapsFrom("SPS")]
        public string onlySocketsTagged = "";
        [Tooltip("Never sockets carrying this tag.")]
        [YapsFrom("SPS")]
        public string neverSocketsTagged = "";

        [Header("How sockets find it")]
        [Tooltip("Emit the tip light Raliv DPS orifices read, and the contact pointers TPS and " +
                 "SPS sockets read. Both on unless you know why not.")]
        [YapsFrom("DPS")]
        public bool emitTipLight = true;
        [YapsFrom("TPS · SPS")]
        public bool emitPointers = true;

        public Renderer Target => renderer != null ? renderer : GetComponent<Renderer>();
    }
}
