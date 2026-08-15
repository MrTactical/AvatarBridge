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

        // Which way the socket faces is the object's +Z. The front pointer
        // and the normal light both sit a centimetre along it.
        public Vector3 Forward => transform.forward;
    }
}
