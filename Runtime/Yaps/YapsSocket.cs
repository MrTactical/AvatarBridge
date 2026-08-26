// A YAPS socket: authoring data the prefabs carry and the toolkit bakes.
// ChilloutVR strips it at upload. No SDK dependency.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AvatarBridge.Yaps
{
    [ExecuteAlways]
    [AddComponentMenu("YAPS/YAPS Socket")]
    [DisallowMultipleComponent]
    public class YapsSocket : MonoBehaviour
    {
        public enum SocketKind { Hole, Ring }

        [Tooltip("A hole closes around the plug and stops it. A ring lets it pass straight through.")]
        public SocketKind kind = SocketKind.Hole;

        [Header("Shapes that open as a plug goes in")]
        [Tooltip("The mesh whose blendshapes should react. Usually the body this socket sits on. " +
                 "Leave empty for a socket that only bends plugs and plays no shape.")]
        public SkinnedMeshRenderer renderer;

        [Tooltip("Up to sixteen, each with its own depth: it starts opening at its start depth and is fully " +
                 "open by start + fade, both as fractions of the plug's length. Several may share a depth. " +
                 "They accumulate: once a shape has arrived it stays.")]
        public List<ShapeStage> shapes = new List<ShapeStage>();

        [Serializable]
        public class ShapeStage
        {
            [Tooltip("A blendshape on the renderer above.")]
            public string blendshape = "";
            [Range(0f, 1f), Tooltip("How far in the plug is when this shape starts opening.")]
            public float startsAt = 0f;
            [Range(0.01f, 1f), Tooltip("How much further in before it is fully open.")]
            public float fadeOver = 0.3f;
        }

        [Header("Advanced")]
        [Range(0f, 1f), Tooltip("Overall strength of the shapes. 1 is as authored.")]
        public float shapePower = 1f;

        [Tooltip("For shapes on a mesh that is not the socket's own (the body): how far in, in metres, " +
                 "counts as depth 1. The contact that drives them cannot know a plug's length, so this " +
                 "stands for it. 0 = the longest baked plug on this avatar when built, else 0.25 m.")]
        [Min(0f)]
        public float depthReach = 0f;

        [Tooltip("Emit the marker lights that let plugs with no contacts (Raliv DPS, and any " +
                 "converted plug with no sync budget) find this socket. A mesh gets four vertex " +
                 "light slots, a socket needs two and a plug's tracker takes one, so only the " +
                 "first socket or two carry lights; past that a plug sees roots without fronts " +
                 "and cannot enter any of them. The rest are found by contact, which has no limit.")]
        public bool emitLights = true;

        // The material the socket bake replaced on its own mesh, so Remove
        // can put it back. Set by the first bake; the toolkit's own.
        [HideInInspector]
        public Material bakedFrom;

        // What the last build called this socket's reactions layer and its
        // depth parameter. Names follow the bone, so they move when the
        // socket does; without a record of the old ones a rebuild would
        // leave the old layer behind and Remove would miss it.
        [HideInInspector]
        public string builtLayer;
        [HideInInspector]
        public string builtParameter;

        // Editor state, never saved: while on, every YAPS plug in the scene
        // bends toward this socket. Only one socket previews at a time.
        [NonSerialized]
        public bool preview;

        // Faces along +Z; the front markers sit a centimetre along it.
        public Vector3 Forward => transform.forward;

#if UNITY_EDITOR
        // Editor preview: writes the socket into every YAPS plug in the scene.
        static YapsSocket _previewing;

        // Property blocks only. They are not saved and not uploaded.
        struct Touched { public Renderer Renderer; public int Slot; public float ChannelSpace; }
        readonly System.Collections.Generic.List<Touched> _touched = new System.Collections.Generic.List<Touched>();
        // Created on first use: a static initializer runs inside AddComponent,
        // where Unity forbids creating native objects.
        static MaterialPropertyBlock _block;
        static MaterialPropertyBlock Block => _block ?? (_block = new MaterialPropertyBlock());

        // The inspector also ticks this on every scene repaint.
        void Update() => PreviewTick();

        [Tooltip("Preview the way the game does it: the socket's offset from the plug, normalised " +
                 "across the channel's box, rather than a world position. The world route is simpler " +
                 "and is what the preview always used — and it is NOT what the game runs, which is how " +
                 "a contact channel that had never worked once looked perfect in the editor.")]
        public bool previewAsChannel = true;

        public void PreviewTick()
        {
            if (!preview)
            {
                if (_previewing == this) { Release(); _previewing = null; }
                return;
            }
            if (_previewing != null && _previewing != this) { _previewing.preview = false; _previewing.Release(); }
            _previewing = this;
            if (UnityEngine.Application.isPlaying) return;

            foreach (var r in FindObjectsOfType<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int slot = 0; slot < mats.Length; slot++)
                {
                    var m = mats[slot];
                    if (m == null || !m.HasProperty("_YAPS_Bake") || !m.HasProperty("_YAPS_SocketPos")) continue;
                    if (!_touched.Exists(t => t.Renderer == r && t.Slot == slot))
                    {
                        _touched.Add(new Touched
                        {
                            Renderer = r, Slot = slot,
                            ChannelSpace = m.HasProperty("_YAPS_ChannelSpace") ? m.GetFloat("_YAPS_ChannelSpace") : 0f,
                        });
                    }
                    float length = m.HasProperty("_YAPS_Length") ? m.GetFloat("_YAPS_Length") : 0.25f;
                    float gap = Vector3.Distance(PlugOrigin(r), transform.position);
                    float engaged = 1f - Mathf.Clamp01((gap - length * 1.2f) / Mathf.Max(length * 0.4f, 0.001f));
                    r.GetPropertyBlock(Block, slot);
                    if (previewAsChannel)
                    {
                        WriteAsChannel(r, m, length, engaged);
                    }
                    else
                    {
                        Block.SetFloat("_YAPS_ChannelSpace", 0f);
                        Block.SetVector("_YAPS_SocketPos", transform.position);
                        Block.SetVector("_YAPS_SocketForward", transform.forward);
                        Block.SetVector("_YAPS_SocketUp", transform.up);
                    }
                    Block.SetVector("_YAPS_SocketFlags", new Vector4(engaged, kind == SocketKind.Hole ? 1f : 0f, 0f, 0f));
                    r.SetPropertyBlock(Block, slot);
                }
            }
        }

        // What the CONTACT CHANNEL would put on the material, exactly.
        //
        // The preview has always written a world position with
        // _YAPS_ChannelSpace 0, which the game never does: in game the
        // triggers report the socket's offset from the plug, normalised per
        // axis across the channel's box, and the shader rebuilds it. So the
        // editor exercised a decode path the game does not run, and a
        // channel that had never once worked in game looked perfect here.
        // That is how it stayed hidden.
        //
        // This is the exact inverse of the decode in yaps_resolve.cginc:
        //
        //     offset   = (SocketPos * 2 - 1) * Extents * BakeScale
        //     position = origin + right*offset.x + up*offset.y + fwd*offset.z
        //
        // so encoding is that read backwards. Anything the two disagree
        // about shows up here rather than after an upload.
        void WriteAsChannel(Renderer r, Material m, float length, float engaged)
        {
            // The MARKERS frame, always. The shader decodes the channel in
            // the per-vertex recovered frame, which at rest pose equals the
            // measured frame the markers sit at. There is no object-space
            // route: Unity skins into world space, unity_ObjectToWorld is
            // identity for a skinned mesh at draw time, so a frame published
            // in renderer space decodes unrotated — on a Blender-imported
            // body carrying -90 on X, "up" arrived pointing forward and the
            // avatar bent toward a socket behind it.
            PlugFrame(r, out Vector3 origin, out var rotation);
            Vector3 forward = rotation * Vector3.forward;
            Vector3 up = rotation * Vector3.up;
            Vector3 right = Vector3.Cross(up, forward);

            Vector3 extents = m.HasProperty("_YAPS_ChannelExtents")
                ? (Vector3) m.GetVector("_YAPS_ChannelExtents") : Vector3.zero;
            // No channel built yet: the same box the builder would have used.
            if (extents.sqrMagnitude < 1e-8f) extents = Vector3.one * (length * 1.75f);
            float bakeScale = m.HasProperty("_YAPS_BakeScale") ? Mathf.Max(m.GetFloat("_YAPS_BakeScale"), 0.0001f) : 1f;
            Vector3 span = extents * bakeScale;

            Block.SetFloat("_YAPS_ChannelSpace", 1f);
            Block.SetVector("_YAPS_ChannelExtents", extents);
            Block.SetVector("_YAPS_SocketPos", Normalised(transform.position, origin, right, up, forward, span));
            // The socket's second point, a centimetre along its own
            // forward, which is what a real front pointer sits at.
            Block.SetVector("_YAPS_SocketFront",
                Normalised(transform.position + transform.forward * 0.01f, origin, right, up, forward, span));
            // The channel publishes position and a front point, never a
            // rotation, so these stay zero exactly as in game. A preview
            // that quietly handed over the true forward would hide any
            // fault in deriving it from the pair.
            Block.SetVector("_YAPS_SocketForward", Vector4.zero);
            Block.SetVector("_YAPS_SocketUp", Vector4.zero);
        }

        static Vector4 Normalised(Vector3 at, Vector3 origin, Vector3 right, Vector3 up, Vector3 forward, Vector3 span)
        {
            Vector3 d = at - origin;
            var local = new Vector3(Vector3.Dot(d, right), Vector3.Dot(d, up), Vector3.Dot(d, forward));
            return new Vector4(
                Mathf.Clamp01((local.x / Mathf.Max(span.x, 1e-5f) + 1f) * 0.5f),
                Mathf.Clamp01((local.y / Mathf.Max(span.y, 1e-5f) + 1f) * 0.5f),
                Mathf.Clamp01((local.z / Mathf.Max(span.z, 1e-5f) + 1f) * 0.5f),
                0f);
        }

        // The plug's measured frame. The bake leaves "YAPS Markers" at the
        // origin and rotation it measured, which is what the shader bends
        // from; without it the plug object's own transform is the best
        // available guess.
        static void PlugFrame(Renderer r, out Vector3 origin, out Quaternion rotation)
        {
            foreach (var plug in FindObjectsOfType<YapsPlug>())
            {
                if (plug == null || plug.Target != r) continue;
                var markers = plug.transform.Find("YAPS Markers");
                if (markers != null) { origin = markers.position; rotation = markers.rotation; return; }
                origin = plug.transform.position; rotation = plug.transform.rotation; return;
            }
            origin = r is SkinnedMeshRenderer ? r.bounds.center : r.transform.position;
            rotation = r.transform.rotation;
        }

        // Where the plug on this renderer actually STARTS.
        //
        // The preview used the renderer's own transform, which is right for
        // a prop and wrong for every skinned plug: a skinned renderer sits
        // at the avatar root, so a plug on the hips measured its distance
        // to a socket from between the avatar's feet. That reads as far too
        // far, the simulated channel never engages, and a marker light
        // quietly carries the preview instead — so the editor showed the
        // light path while appearing to show the channel.
        //
        // The bake leaves "YAPS Markers" on the plug at the frame origin it
        // measured, which is exactly the point the shader bends from.
        static Vector3 PlugOrigin(Renderer r)
        {
            foreach (var plug in FindObjectsOfType<YapsPlug>())
            {
                if (plug == null || plug.Target != r) continue;
                var markers = plug.transform.Find("YAPS Markers");
                return markers != null ? markers.position : plug.transform.position;
            }
            // No component to ask: a converted avatar keeps the bake and
            // loses the authoring component. The bounds centre is nearer the
            // truth than the avatar root, and never worse than it.
            return r is SkinnedMeshRenderer ? r.bounds.center : r.transform.position;
        }

        void OnDisable() { if (_previewing == this) { Release(); _previewing = null; } }
        void OnDestroy() { if (_previewing == this) { Release(); _previewing = null; } }

        // Back to what the material says. A block cannot drop a property.
        void Release()
        {
            foreach (var t in _touched)
            {
                if (t.Renderer == null) continue;
                t.Renderer.GetPropertyBlock(Block, t.Slot);
                Block.SetFloat("_YAPS_ChannelSpace", t.ChannelSpace);
                Block.SetVector("_YAPS_SocketFlags", Vector4.zero);
                Block.SetVector("_YAPS_SocketPos", Vector4.zero);
                Block.SetVector("_YAPS_SocketForward", Vector4.zero);
                Block.SetVector("_YAPS_SocketUp", Vector4.zero);
                t.Renderer.SetPropertyBlock(Block, t.Slot);
            }
            _touched.Clear();
        }
#endif
    }
}
