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
        public enum SocketSource
        {
            [Tooltip("Write the socket straight into the material, as the contact channel will.")]
            DiscreteChannel,
            [Tooltip("Write NO position, so resolution has to find the protocol lights itself.")]
            ProtocolLights,
        }

        [Tooltip("Which path is under test. Protocol Lights deliberately withholds the " +
                 "position so the shader must decode the lights on its own.")]
        public SocketSource source = SocketSource.DiscreteChannel;

        [Tooltip("Drag this around the scene — the plug should follow it.")]
        public Transform socket;

        [Tooltip("Where the plug's own frame really is. Leave empty when the renderer's " +
                 "transform is the plug. On a skinned mesh set this to the bone, so the " +
                 "gizmos draw the curve the shader is actually building rather than one " +
                 "anchored to the avatar root.")]
        public Transform frameSource;

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

            // Engagement always comes from here — that is the rule the
            // spike settled, and it holds even in the light test. Only the
            // POSITION is withheld, so the light path is genuinely doing
            // the finding rather than being handed the answer.
            bool writePosition = source == SocketSource.DiscreteChannel;
            block.SetVector(SocketPos, writePosition ? (Vector4) socket.position : Vector4.zero);
            block.SetVector(SocketForward, writePosition ? (Vector4) socket.forward : Vector4.zero);
            block.SetVector(SocketUp, writePosition ? (Vector4) socket.up : Vector4.zero);
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
            Gizmos.DrawLine((frameSource != null ? frameSource : transform).position, socket.position);

            // Engagement radius: inside the inner sphere the bend is full,
            // outside the outer one there is no bend at all.
            float worldLength = plugLength * bakeScale;
            Gizmos.color = new Color(0f, 1f, 0.5f, 0.25f);
            Gizmos.DrawWireSphere((frameSource != null ? frameSource : transform).position, worldLength * 1.2f);
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.15f);
            Gizmos.DrawWireSphere((frameSource != null ? frameSource : transform).position, worldLength * 1.6f);

            DrawTheCurveTheShaderWalks(worldLength);
        }

        // The exact curve the shader builds, recomputed here with the same
        // arithmetic and drawn. Guessing at a fold from the silhouette is
        // hopeless; seeing whether the CURVE hairpins or the WALK misreads
        // it takes one glance.
        void DrawTheCurveTheShaderWalks(float worldLength)
        {
            Transform frame = frameSource != null ? frameSource : transform;
            Vector3 rootWorld = frame.position;
            Vector3 rootForward = frame.forward;
            Vector3 socketWorld = socket.position;
            Vector3 socketForward = socket.forward;

            float gap = Vector3.Distance(socketWorld, rootWorld);

            // Mirrors the shader, including the approach-side flip.
            if (Vector3.Dot(socketForward, socketWorld - rootWorld) < 0f)
            {
                socketForward = -socketForward;
            }

            float engage = 1f - Mathf.Clamp01((gap - worldLength * 1.2f)
                / Mathf.Max(worldLength * 1.6f - worldLength * 1.2f, 1e-6f));
            float approachHandle = gap * 0.5f;
            float rootHandle = Mathf.Lerp(worldLength * 5f, approachHandle, engage);

            Vector3 p0 = rootWorld;
            Vector3 p1 = rootWorld + rootForward * rootHandle;
            Vector3 p2 = socketWorld - socketForward * approachHandle;
            Vector3 p3 = socketWorld;

            // The control hull, so a handle reaching somewhere absurd is
            // visible as a shape rather than inferred from the result.
            Gizmos.color = new Color(1f, 0f, 1f, 0.5f);
            Gizmos.DrawLine(p0, p1);
            Gizmos.DrawLine(p3, p2);
            Gizmos.DrawWireCube(p1, Vector3.one * 0.015f);
            Gizmos.DrawWireCube(p2, Vector3.one * 0.015f);

            // The curve itself, and how much of it the plug can actually
            // reach: yellow while the plug is still on the curve, red for
            // the stretch beyond its length, which is where overrun and
            // the hole taper take over.
            Vector3 previous = p0;
            float travelled = 0f;
            const int steps = 48;
            for (int i = 1; i <= steps; i++)
            {
                float t = (float) i / steps;
                Vector3 at = Bezier(p0, p1, p2, p3, t);
                travelled += Vector3.Distance(at, previous);
                Gizmos.color = travelled <= worldLength
                    ? new Color(1f, 0.9f, 0.2f, 0.9f)
                    : new Color(1f, 0.2f, 0.2f, 0.9f);
                Gizmos.DrawLine(previous, at);
                previous = at;
            }

            Handles.color = Color.white;
            // Naming the source matters: in Protocol Lights mode this curve
            // is the one the socket TRANSFORM implies, while the shader is
            // resolving the light independently. They should agree — and if
            // they visibly do not, that disagreement is the finding.
            Handles.Label(p3, $"gap {gap:0.00}  len {worldLength:0.00}  " +
                              $"engage {engage:0.00}  curve {travelled:0.00}\n" +
                              $"source: {source}");
        }

        static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0
                 + 3f * u * u * t * p1
                 + 3f * u * t * t * p2
                 + t * t * t * p3;
        }
    }
}
#endif
