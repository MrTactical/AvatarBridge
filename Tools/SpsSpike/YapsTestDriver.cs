// Phase 1d harness. Drives the YAPS socket uniforms from a Transform you
// can drag in the scene view, so the deform can be iterated on entirely
// in the editor — no conversion, no upload, no second person.
//
// These are exactly the uniforms the real discrete channel writes at
// runtime (animator parameters → CVRMaterialDriver → material vectors),
// and exactly the ones a WASM script would write later. So whatever looks
// right here is what will look right in game.
//
// Runs in edit mode. Add to the plug's renderer, point Socket at any
// Transform, and drag it around.
//
// DEPLOYMENT: this is a MonoBehaviour, so it must NOT live in an Editor
// folder — scripts there compile into the editor assembly and cannot be
// attached to a scene object at all. Deploy it to Assets/SpsSpike/, not
// Assets/SpsSpike/Editor/. Put there once by mistake, and the symptom was
// perfect silence: no component, no gizmos, no property block, and a plug
// reading the material's default socket at the origin — which sits on the
// plug root, degenerates the curve, and renders as an untouched rod.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Spike
{
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class YapsTestDriver : MonoBehaviour
    {
        [Tooltip("Drag this around the scene — the plug should follow it.")]
        public Transform socket;

        [Tooltip("How engaged the socket is. The real system drives this from contacts.")]
        [Range(0f, 1f)] public float engaged = 1f;

        [Tooltip("A hole swallows the tip and tapers it; a ring lets it pass through.")]
        public bool isHole = true;

        [Range(0f, 1f)] public float enabled01 = 1f;
        public bool allowOverrun = true;

        [Header("Plug description")]
        [Tooltip("Length of the plug in its own local space, along +Z.")]
        public float plugLength = 1f;
        [Tooltip("Scale the bake was taken at. 1 unless the plug transform is scaled.")]
        public float bakeScale = 1f;

        [Header("Read-only")]
        [SerializeField] int vertexCountInBake;

        static readonly int SocketPos = Shader.PropertyToID("_YAPS_SocketPos");
        static readonly int SocketForward = Shader.PropertyToID("_YAPS_SocketForward");
        static readonly int SocketUp = Shader.PropertyToID("_YAPS_SocketUp");
        static readonly int SocketFlags = Shader.PropertyToID("_YAPS_SocketFlags");
        static readonly int Enabled = Shader.PropertyToID("_YAPS_Enabled");
        static readonly int Overrun = Shader.PropertyToID("_YAPS_Overrun");
        static readonly int Length = Shader.PropertyToID("_YAPS_Length");
        static readonly int BakeScale = Shader.PropertyToID("_YAPS_BakeScale");
        static readonly int VertexCount = Shader.PropertyToID("_YAPS_VertexCount");

        MaterialPropertyBlock block;

        void Update()
        {
            var renderer = GetComponent<Renderer>();
            if (renderer == null || socket == null)
            {
                return;
            }

            // A property block keeps this out of the material asset, so
            // the harness never dirties anything that gets committed.
            block = block ?? new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);

            block.SetVector(SocketPos, socket.position);
            block.SetVector(SocketForward, socket.forward);
            block.SetVector(SocketUp, socket.up);
            block.SetVector(SocketFlags, new Vector4(engaged, isHole ? 1f : 0f, 0f, 0f));
            block.SetFloat(Enabled, enabled01);
            block.SetFloat(Overrun, allowOverrun ? 1f : 0f);
            block.SetFloat(Length, plugLength);
            block.SetFloat(BakeScale, bakeScale);

            // The bake's own vertex count, so the shader never reads past
            // the base block into blendshape data.
            var material = renderer.sharedMaterial;
            if (material != null && material.HasProperty(VertexCount))
            {
                vertexCountInBake = Mathf.RoundToInt(material.GetFloat(VertexCount));
            }

            renderer.SetPropertyBlock(block);
        }

        void OnDrawGizmos()
        {
            if (socket == null)
            {
                return;
            }

            // The socket frame, drawn the way the shader reads it.
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(socket.position, 0.02f);
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(socket.position, socket.position + socket.forward * 0.1f);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(socket.position, socket.position + socket.up * 0.06f);

            // The straight line the bend is replacing, for reference.
            Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
            Gizmos.DrawLine(transform.position, socket.position);

            // Engagement radius: inside the inner sphere the bend is full,
            // outside the outer one there is no bend at all.
            float worldLength = plugLength * bakeScale;
            Gizmos.color = new Color(0f, 1f, 0.5f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, worldLength * 1.2f);
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, worldLength * 1.6f);
        }
    }
}
#endif
