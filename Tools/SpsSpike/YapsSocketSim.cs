// Test every flavour of socket at your desk, without launching anything.
//
// Of the three ways a plug finds a socket, only ONE works in the editor by
// itself. Marker lights do, because Unity fills the vertex light slots in
// the scene view exactly as it does in game. The contact channel does not,
// because triggers are a ChilloutVR client system and nothing runs them
// here. The player globals do not either, for the same reason.
//
// So this stands in for the client. Drop it on any object — a socket prop,
// an empty, a bone — and it writes onto every YAPS plug in the scene the
// same values the channel would have carried, which lets engagement, the
// hole taper and the whole deform be judged without a single upload.
//
// NOT in an Editor folder, deliberately. A MonoBehaviour in one cannot be
// attached to anything, and the symptom is total silence rather than an
// error — an afternoon went into learning that once already.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using UnityEngine;

namespace AvatarBridge.Spike
{
    [ExecuteAlways]
    [AddComponentMenu("AvatarBridge/YAPS Socket Simulator")]
    public class YapsSocketSim : MonoBehaviour
    {
        [Tooltip("A hole closes around the plug and stops it. A ring lets it pass straight " +
                 "through. This is the flag the contact channel would carry.")]
        public bool isHole;

        [Tooltip("How close before the plug reacts, in metres. The real thing scales this off " +
                 "plug length; here it is yours to play with.")]
        public float reach = 0.6f;

        [Tooltip("Off leaves the plugs alone, so you can compare against the light path on its " +
                 "own — a socket prop's marker lights work in the scene view unaided.")]
        public bool drive = true;

        [Tooltip("Forces engagement rather than deriving it from distance. -1 leaves it automatic.")]
        [Range(-1f, 1f)] public float engagedOverride = -1f;

        // Everything written here lands on the SHARED material, which is an
        // asset on disk. Nothing else would work in edit mode, but it means
        // the last values written outlive this component: delete the socket
        // and the plug stays bent, because a stale socket position still
        // counts as a socket having been found. Worse, it could be uploaded
        // that way.
        //
        // So every material touched is remembered and released.
        readonly HashSet<Material> _touched = new HashSet<Material>();

        void Update()
        {
            foreach (var renderer in FindObjectsOfType<Renderer>())
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !material.HasProperty("_YAPS_Bake"))
                    {
                        continue;
                    }
                    _touched.Add(material);
                    Apply(renderer, material);
                }
            }
        }

        void OnDisable() => Release();
        void OnDestroy() => Release();

        void Release()
        {
            foreach (var material in _touched)
            {
                if (material != null)
                {
                    Clear(material);
                }
            }
            _touched.Clear();
        }

        static void Clear(Material material)
        {
            material.SetVector("_YAPS_SocketFlags", Vector4.zero);
            material.SetVector("_YAPS_SocketPos", Vector4.zero);
            material.SetVector("_YAPS_SocketForward", Vector4.zero);
            material.SetVector("_YAPS_SocketUp", Vector4.zero);
        }

#if UNITY_EDITOR
        // For materials already left dirty by an earlier run, or by a
        // simulator deleted before this existed.
        [UnityEditor.MenuItem("AvatarBridge/Spike/Clear stuck YAPS socket values")]
        static void ClearAll()
        {
            int cleared = 0;
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Material"))
            {
                var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (material == null || !material.HasProperty("_YAPS_SocketFlags"))
                {
                    continue;
                }
                if (material.GetVector("_YAPS_SocketFlags") == Vector4.zero
                    && material.GetVector("_YAPS_SocketPos") == Vector4.zero)
                {
                    continue;
                }
                Clear(material);
                UnityEditor.EditorUtility.SetDirty(material);
                cleared++;
            }
            UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log($"[YAPS] Released {cleared} material(s) still holding a socket. A plug stuck " +
                      "bent with nothing near it was reading one of these.");
        }
#endif

        void Apply(Renderer renderer, Material material)
        {
            if (!drive)
            {
                material.SetVector("_YAPS_SocketFlags", Vector4.zero);
                return;
            }

            // World space, not the plug-local mode a converted avatar
            // normally runs in. The shader rebuilds the same world position
            // either way, and asking for plug-local here would mean
            // recovering the plug's own frame in C# — which on a skinned
            // mesh is exactly the thing the vertex shader was written to do
            // and C# is badly placed to repeat.
            //
            // The cost is that this exercises everything EXCEPT the
            // normalise-and-rebuild step. That step needs the game.
            material.SetFloat("_YAPS_ChannelSpace", 0f);

            float gap = Vector3.Distance(transform.position, renderer.bounds.center);
            float engaged = engagedOverride >= 0f
                ? engagedOverride
                : 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(gap / Mathf.Max(reach, 0.001f)));

            material.SetVector("_YAPS_SocketPos", transform.position);
            material.SetVector("_YAPS_SocketForward", transform.forward);
            material.SetVector("_YAPS_SocketUp", transform.up);
            material.SetVector("_YAPS_SocketFlags",
                new Vector4(engaged, isHole ? 1f : 0f, 0f, 0f));
        }

        void OnDrawGizmos()
        {
            // Blue forward, because which way a socket faces decides how the
            // plug arrives at it, and that is half of what there is to look
            // at. Matches Unity's own axis colouring.
            Gizmos.color = isHole ? new Color(0.6f, 0.2f, 0.8f) : new Color(0.95f, 0.8f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, 0.05f);
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.12f);
            Gizmos.color = new Color(1f, 1f, 1f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, reach);
        }
    }
}
#endif
