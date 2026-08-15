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

        [Header("At rest — from DPS")]
        [Range(-1.5f, 1.5f), Tooltip("A resting bend along the whole shaft. Positive bends up.")]
        public float curvature;
        [Range(-1.5f, 1.5f), Tooltip("A second bend near the tip, opposite in sign, so the shaft can sweep and then hook.")]
        public float recurvature;
        [Range(0f, 1f), Tooltip("How much the base resists bending toward a socket. 0 bends evenly from the root.")]
        public float entranceStiffness;

        [Header("When it is in — DPS and TPS")]
        [Range(0f, 1f)] public float squeeze;
        [Range(0.01f, 1f)] public float squeezeReach = 0.15f;
        [Range(0f, 1f)] public float bulge;
        [Range(0.01f, 1f)] public float bulgeReach = 0.2f;

        [Header("When it is not — TPS and DPS")]
        [Range(0.1f, 1f), Tooltip("How much of its length it keeps when nothing is using it.")]
        public float idleLength = 1f;
        [Range(0.1f, 1f)] public float idleWidth = 1f;
        [Range(0f, 0.5f)] public float wriggle;
        [Range(0f, 20f)] public float wriggleSpeed = 2f;

        [Header("Motion — TPS")]
        [Range(0f, 0.5f)] public float pumping;
        [Range(0f, 20f)] public float pumpingSpeed = 6f;
        [Range(0.05f, 1f), Tooltip("How much of the shaft pumps. 1 is the whole length; small values move only the tip.")]
        public float pumpingWidth = 1f;

        [Header("The curve — TPS")]
        [Range(0.2f, 3f), Tooltip("Below 1 arrives more directly, above 1 sweeps a wider arc.")]
        public float bezierSmoothness = 1f;
        [Range(0f, 0.8f), Tooltip("A fraction of the shaft held straight before any bend.")]
        public float straightBeforeBend;
        [Range(0f, 0.5f), Tooltip("Ease the join between the straight part and the curve.")]
        public float easeIntoBend;
        [Range(0f, 1f), Tooltip("A socket nearer than this (fraction of length) is held off, so a plug pushed hard against one does not fold.")]
        public float minimumSocketDistance;
        [Range(0.01f, 0.5f), Tooltip("How fast the plug follows a socket the contact channel reports. Lower is heavier.")]
        public float socketFollow = 0.05f;

        [Header("Hole")]
        [Range(0f, 1f)] public float taperStart = 0.10f;
        [Range(0f, 1f)] public float taperEnd = 0.30f;
        [Tooltip("Let the tip carry on past a ring. Off, the shaft stops at every socket.")]
        public bool overrun = true;

        [Header("Who it answers — SPS")]
        [Tooltip("Only sockets carrying this tag. Blank answers all.")]
        public string onlySocketsTagged = "";
        [Tooltip("Never sockets carrying this tag.")]
        public string neverSocketsTagged = "";

        [Header("Announcing itself")]
        [Tooltip("Emit the tip light Raliv DPS orifices read, and the contact pointers TPS and " +
                 "SPS sockets read. Both on unless you know why not.")]
        public bool emitTipLight = true;
        public bool emitPointers = true;

        public Renderer Target => renderer != null ? renderer : GetComponent<Renderer>();
    }
}
