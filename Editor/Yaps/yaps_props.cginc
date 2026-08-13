// YAPS — Yet Another Penetration System, for ChilloutVR.
// Property block, injected into the patched shader's Properties{}.
//
// Inspired by VRCFury's SPS, which invented this technique for VRChat.
// No SPS code is used here; see Tools/SpsSpike/LICENSE-POSTURE.md.
#ifndef YAPS_PROPS_INCLUDED
#define YAPS_PROPS_INCLUDED

// --- baked mesh data -------------------------------------------------
// One header pixel, then ten floats per vertex: position(3), normal(3),
// tangent(3), active(1). Each float is the four bytes of one RGBA32 pixel.
Texture2D _YAPS_Bake;
float4 _YAPS_Bake_TexelSize;

// Vertices in the base block. Beyond this the texture holds blendshape
// data, so reading past it returns nonsense.
float _YAPS_VertexCount;

// --- plug description ------------------------------------------------
float _YAPS_Enabled;       // master gate / apply fraction, 0..1
float _YAPS_Length;        // plug length in its own local space
float _YAPS_Overrun;       // may the tip travel past the socket
float _YAPS_BakeScale;     // baked vectors are plug-local; divide by this

// Where the plug's own frame comes from.
//   0  the renderer's transform — correct when the plug is its own object
//   1  recovered from the vertex — required on a skinned mesh, where the
//      renderer sits at the avatar root and a bone carries the plug
float _YAPS_FrameFromVertex;

// --- the socket, written by the discrete channel ---------------------
// A CVRMaterialDriver task writes these every frame from animator
// parameters, so they are identical on every camera and every viewer —
// which is the whole reason position lives here rather than in lights.
// The editor harness writes the same four, which is why the deform can
// be developed without uploading anything.
float4 _YAPS_SocketPos;    // see _YAPS_ChannelSpace
float4 _YAPS_SocketForward; // xyz world direction, w unused
float4 _YAPS_SocketUp;     // xyz world direction,  w unused
float4 _YAPS_SocketFlags;  // x: engaged 0..1, y: is-hole, z/w spare

// How to read _YAPS_SocketPos.
//   0  a world position, written directly. What the editor harness does,
//      and what a WASM script would do once that ships.
//   1  the socket's offset in the PLUG's own frame, each axis squeezed
//      into 0..1 across the box below. ChilloutVR's contact channel
//      cannot express anything else: a trigger reports where a pointer
//      sits inside its own box, normalised per axis, and that is the only
//      shape the value can arrive in.
float _YAPS_ChannelSpace;
float4 _YAPS_ChannelExtents;  // xyz half-extents of that box, in metres

#endif
