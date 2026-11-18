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

        [Tooltip("Entered from the front only, the side its markers sit on. Screen atlas only: " +
                 "by marker light, either side.")]
        public bool oneWay;

        [Header("Shapes that open as a plug goes in")]
        [Tooltip("The mesh whose shapes open, usually the body. Empty for a socket that only bends plugs.")]
        // new: hides the obsolete Component.renderer. Renaming would drop serialized data.
        public new SkinnedMeshRenderer renderer;

        [Tooltip("Up to sixteen, each opening over its own depth range, as fractions of the plug's " +
                 "length. Once open, a shape stays open.")]
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

        [Header("Animations that play as a plug goes in")]
        [Tooltip("Your own clips, blended in by depth. Write defaults are on, so animate only " +
                 "what nothing else animates.")]
        public List<DepthAnimation> depthAnimations = new List<DepthAnimation>();

        [Serializable]
        public class DepthAnimation
        {
            public AnimationClip clip;
            [Range(0f, 1f)] public float startsAt = 0f;
            [Range(0.01f, 1f)] public float fadeOver = 0.3f;
        }

        [Header("Advanced")]
        [Range(0f, 1f), Tooltip("Overall strength of the shapes. 1 is as authored.")]
        public float shapePower = 1f;

        [Tooltip("How far in, in metres, counts as full depth, since a contact cannot know a " +
                 "plug's length. 0 takes the longest plug on the avatar, else 0.25 m.")]
        [Min(0f)]
        public float depthReach = 0f;

        [Tooltip("Marker lights for DPS plugs, and for YAPS plugs the screen atlas misses. A mesh " +
                 "has four light slots, so only the first socket or two get them.")]
        public bool emitLights = true;

        [Tooltip("What this socket is, for plugs that choose. Any words, case ignored; SPS names " +
                 "like hips or hand mean the same here. Every plug answers \"shared\". Screen " +
                 "atlas only: by marker light, every plug answers.")]
        public List<string> tags = new List<string> { YapsTags.Shared };

        // Version that built or baked this. A prop is never rebuilt on its own, so
        // the inspector uses this to say when it misses later fixes.
        [HideInInspector] public string builtBy = "";

        // The material the socket bake replaced on its own mesh, so Remove
        // can put it back. Set by the first bake; the toolkit's own.
        [HideInInspector]
        public Material bakedFrom;

        // Where it came off. Slot 0 is wrong for a socket baked into another one,
        // and the mesh field may be None by then.
        [HideInInspector]
        public Renderer bakedRenderer;
        [HideInInspector]
        public int bakedSlot = -1;

        // Last build's layer and parameter names. They follow the bone, so a moved
        // socket needs the old names to clean up.
        [HideInInspector]
        public string builtLayer;
        [HideInInspector]
        public string builtParameter;
        // The same for the depth animations layer.
        [HideInInspector]
        public string builtAnimations;

        // Whether a build adds an in-game readout. Off unless asked for.
        [HideInInspector]
        public bool readout;

        // Editor state, never saved: while on, every YAPS plug in the scene
        // bends toward this socket. Only one socket previews at a time.
        [NonSerialized]
        public bool preview;

        // Outside the #if: a serialized field the player build lacks gives
        // the component two different layouts.
        [Tooltip("For a plug that only appears in Play Mode, or to see the bend on a posed avatar.")]
        public bool previewInPlayMode;

        // Outside the #if for the same reason.
        [Tooltip("A plug with Two At Once that goes into this hole comes out of the exit. Screen atlas " +
                 "only: to any other plug, by marker light, and to plugs built before 5.0, this is an ordinary hole.")]
        public YapsSocket portalExit;

        // The exit's writer from the last build. It lives under the exit, so a
        // rebuild or Remove needs this to find it.
        [HideInInspector]
        public GameObject builtExit;

#if UNITY_EDITOR
        // Editor preview: writes the socket into every YAPS plug in the scene.
        static YapsSocket _previewing;

        // Property blocks only. They are not saved and not uploaded.
        struct Touched { public Renderer Renderer; public float ChannelSpace; }
        readonly System.Collections.Generic.List<Touched> _touched = new System.Collections.Generic.List<Touched>();
        // Created on first use: a static initializer runs inside AddComponent,
        // where Unity forbids creating native objects.
        static MaterialPropertyBlock _block;
        static MaterialPropertyBlock Block => _block ?? (_block = new MaterialPropertyBlock());
        // Reused so the per-frame scan does not allocate an array per renderer.
        static readonly System.Collections.Generic.List<Material> _mats = new System.Collections.Generic.List<Material>();

        // The inspector also ticks this on every scene repaint.
        void Update() => PreviewTick();

        public void PreviewTick()
        {
            if (!preview)
            {
                if (_previewing == this) { Release(); _previewing = null; }
                return;
            }
            if (_previewing != null && _previewing != this) { _previewing.preview = false; _previewing.Release(); }
            _previewing = this;
            // Off in Play Mode so it cannot race what drives the material there,
            // unless asked: many plug meshes only appear in Play Mode via a toggle.
            if (UnityEngine.Application.isPlaying && !previewInPlayMode) return;

            // Fetched lazily, once per tick, not per renderer.
            YapsPlug[] plugs = null;

            // Inactive ones too: plugs ship switched off, and an unwritten block
            // reads as zeros, which look exactly like a bad encode.
            // The renderer's block, never a slot's: a slot block replaces the
            // renderer's one, where the Animator writes, hiding every menu toggle.
            foreach (var r in FindObjectsOfType<Renderer>(true))
            {
                r.GetSharedMaterials(_mats);
                var m = _mats.Find(x => x != null && IsYapsMaterial(x)
                    && x.HasProperty("_YAPS_VertexCount") && x.GetFloat("_YAPS_VertexCount") > 0f);
                if (m == null) continue;
                if (plugs == null) plugs = FindObjectsOfType<YapsPlug>(true);
                var origin = PlugOrigin(r, plugs);
                if (!_touched.Exists(t => t.Renderer == r))
                {
                    _touched.Add(new Touched
                    {
                        Renderer = r,
                        ChannelSpace = m.HasProperty("_YAPS_ChannelSpace") ? m.GetFloat("_YAPS_ChannelSpace") : 0f,
                    });
                }
                float length = m.HasProperty("_YAPS_Length") ? m.GetFloat("_YAPS_Length") : 0.25f;
                float gap = Vector3.Distance(origin, transform.position);
                float engaged = 1f - Mathf.Clamp01((gap - length * 1.2f) / Mathf.Max(length * 0.4f, 0.001f));
                // What the atlas does with a one-way ring and a plug behind it.
                if (kind == SocketKind.Ring && oneWay
                    && Vector3.Dot(transform.forward, origin - transform.position) < 0f) engaged = 0f;
                r.GetPropertyBlock(Block);
                // World space: the only route the shader still reads.
                Block.SetFloat("_YAPS_ChannelSpace", 0f);
                Block.SetVector("_YAPS_SocketPos", transform.position);
                Block.SetVector("_YAPS_SocketForward", transform.forward);
                Block.SetVector("_YAPS_SocketFlags", new Vector4(engaged, kind == SocketKind.Hole ? 1f : 0f, 0f, 0f));
                r.SetPropertyBlock(Block);
            }
        }

        // The plug this renderer belongs to: its own, or the one carrying it. A mesh
        // swept in by an armature plug must share that plug's origin and gate, or it
        // engages and bends before everything around it.
        static YapsPlug CarrierOf(Renderer r, YapsPlug[] plugs)
        {
            YapsPlug carried = null;
            foreach (var plug in plugs)
            {
                if (plug == null) continue;
                if (plug.Target == r) return plug;
                if (carried == null && plug.transform.root == r.transform.root) carried = plug;
            }
            return carried;
        }

        // Where the plug starts. Not the renderer's transform: a skinned renderer
        // sits at the avatar root, so the channel never engaged and a marker light
        // carried the preview instead. "YAPS Markers" sits at the measured frame
        // origin, the point the shader bends from.
        static Vector3 PlugOrigin(Renderer r, YapsPlug[] plugs)
        {
            var carrier = CarrierOf(r, plugs);
            if (carrier != null)
            {
                var markers = carrier.transform.Find("YAPS Markers");
                return markers != null ? markers.position : carrier.transform.position;
            }
            // A converted avatar keeps the bake but not the component. Bounds
            // centre is never worse than the avatar root.
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
                t.Renderer.GetPropertyBlock(Block);
                Block.SetFloat("_YAPS_ChannelSpace", t.ChannelSpace);
                Block.SetVector("_YAPS_SocketFlags", Vector4.zero);
                Block.SetVector("_YAPS_SocketPos", Vector4.zero);
                Block.SetVector("_YAPS_SocketForward", Vector4.zero);
                t.Renderer.SetPropertyBlock(Block);
            }
            _touched.Clear();
            _isYaps.Clear();
        }

        // Cached: Poiyomi's drawers log "Failed to create material drawer" on every
        // HasProperty, which per material per frame buried the console.
        static readonly System.Collections.Generic.Dictionary<Material, bool> _isYaps =
            new System.Collections.Generic.Dictionary<Material, bool>();

        static bool IsYapsMaterial(Material m)
        {
            if (_isYaps.TryGetValue(m, out bool yes)) return yes;
            yes = m.HasProperty("_YAPS_Bake") && m.HasProperty("_YAPS_SocketPos");
            _isYaps[m] = yes;
            return yes;
        }
#endif
    }
}
