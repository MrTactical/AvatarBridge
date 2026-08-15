// A YAPS socket, as a component you place. This is what the universal
// Hole and Ring prefabs carry, and what the toolkit reads when it bakes.
//
// It holds AUTHORING intent only — kind, tag, which blendshapes open as a
// plug goes in and at what depth. Nothing here runs in game: the toolkit
// turns it into marker lights, contact pointers and a baked deform on the
// mesh, and ChilloutVR strips unknown MonoBehaviours at upload anyway. So
// this can be left on the avatar; it does no harm and it means running
// the toolkit again edits what you set instead of re-guessing it.
//
// No dependency on any SDK, no #if. It compiles in an empty project.
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

        [Tooltip("Optional. A plug can be told to answer only sockets with a given tag, or never " +
                 "ones with another. Leave blank for a socket every plug answers.")]
        public string tag = "";

        [Header("Shapes that open as a plug goes in")]
        [Tooltip("The mesh whose blendshapes should react. Usually the body this socket sits on. " +
                 "Leave empty for a socket that only bends plugs and plays no shape.")]
        public SkinnedMeshRenderer renderer;

        [Tooltip("Up to four, staged by depth. Shape 0 is the entry, shape 3 the deepest. Each " +
                 "starts opening at its start depth and is fully open by start + fade, both as " +
                 "fractions of the plug's length. They accumulate: once a shape has arrived it stays.")]
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

        [Tooltip("Emit the marker lights that let plugs with no contacts (Raliv DPS, and any " +
                 "converted plug with no sync budget) find this socket. Costs nothing; only turn " +
                 "off on an avatar with many sockets, where the toolkit wires them to a menu instead.")]
        public bool emitLights = true;

        [Header("Preview (editor only)")]
        [Tooltip("While on, every YAPS plug in the scene bends toward this socket in the editor, so " +
                 "you can place it and see the result before uploading. Writes nothing that ships: " +
                 "it drives the same material values the game's contact channel would, and clears " +
                 "them when turned off. Only one socket previews at a time.")]
        public bool preview;

        // Which way the socket faces is the object's +Z. The front pointer
        // and the normal light both sit a centimetre along it.
        public Vector3 Forward => transform.forward;

#if UNITY_EDITOR
        // The interaction preview. In the editor there is no ChilloutVR
        // client, so no contact channel; the plug shader still reads the
        // socket values off its material, and this writes them — a world
        // position, this object's forward and up, engaged from distance —
        // exactly as the game's channel would. Marker lights work in the
        // scene view already, so a lit socket previews on its own; this
        // exists for the CONTACT half and for holes, whose kind rides in the
        // flags. Only touches materials carrying _YAPS_Bake, and only while
        // `preview` is on; turning it off, disabling, or destroying clears
        // what it wrote.
        static YapsSocket _previewing;
        readonly System.Collections.Generic.HashSet<Material> _touched =
            new System.Collections.Generic.HashSet<Material>();

        // Update runs in edit mode only when something in the scene changed,
        // which is not reliably "now": the inspector calls PreviewTick
        // itself when preview goes on and on every scene repaint while the
        // socket is selected, so the plug bends the moment preview starts
        // and follows the socket as it is dragged.
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
            if (UnityEngine.Application.isPlaying) return;   // the sim's job, not this one's

            foreach (var r in FindObjectsOfType<Renderer>())
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !m.HasProperty("_YAPS_Bake") || !m.HasProperty("_YAPS_SocketPos")) continue;
                    _touched.Add(m);
                    // A plug's own socket must not bend it: skip materials on
                    // this socket's own avatar root when it is a body mesh
                    // that also carries a socket bake.
                    float length = m.HasProperty("_YAPS_Length") ? m.GetFloat("_YAPS_Length") : 0.25f;
                    float gap = Vector3.Distance(r.transform.position, transform.position);
                    float engaged = 1f - Mathf.Clamp01((gap - length * 1.2f) / Mathf.Max(length * 0.4f, 0.001f));
                    m.SetFloat("_YAPS_ChannelSpace", 0f);
                    m.SetVector("_YAPS_SocketPos", transform.position);
                    m.SetVector("_YAPS_SocketForward", transform.forward);
                    m.SetVector("_YAPS_SocketUp", transform.up);
                    m.SetVector("_YAPS_SocketFlags", new Vector4(engaged, kind == SocketKind.Hole ? 1f : 0f, 0f, 0f));
                }
            }
        }

        void OnDisable() { if (_previewing == this) { Release(); _previewing = null; } }
        void OnDestroy() { if (_previewing == this) { Release(); _previewing = null; } }

        void Release()
        {
            foreach (var m in _touched)
            {
                if (m == null) continue;
                m.SetVector("_YAPS_SocketFlags", Vector4.zero);
                m.SetVector("_YAPS_SocketPos", Vector4.zero);
                m.SetVector("_YAPS_SocketForward", Vector4.zero);
                m.SetVector("_YAPS_SocketUp", Vector4.zero);
            }
            _touched.Clear();
        }
#endif
    }
}
