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
// Always 1 now. The baker measures its own frame from the mesh and writes
// in renderer units, so there is no scale to undo — kept because a shipped
// material carries it and removing a property a material references is a
// bigger change than leaving one that reads 1.
float _YAPS_BakeScale;

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

// The socket's SECOND point, in the same channel space as _YAPS_SocketPos.
//
// Every socket in both ecosystems already publishes one: TPS places a
// TPS_Orf_Norm sender a centimetre along the orifice's forward from its
// TPS_Orf_Root, and SPS does the same under the name SPSLL_Socket_Front.
// The pair IS the axis, and it is how those systems have always described
// which way a socket faces. Subtracting one from the other gives it
// outright, where TPS has to infer the same direction from three proximity
// readings because VRChat contacts can only report a distance.
float4 _YAPS_SocketFront;

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

// Which sockets belong to this plug's OWN avatar, so it can ignore them.
//
// A light carries a position and nothing else — no identity, no owner — so
// a plug cannot otherwise tell its wearer's own socket from a stranger's.
// On an avatar carrying both, its own sockets are permanently in reach and
// permanently nearest, so it spends its life bent into its wearer's hip.
//
// So the question is put to ChilloutVR's player positions instead: which
// player is nearest the plug, which is nearest the light, and are they the
// same person. Nothing is transmitted and nothing is spent.
//
// This is a FLAG now, not a tag. Zero or more means "this plug is on an
// avatar that also carries sockets, so ownership is worth checking"; -1
// means there is nothing to check for and every light counts.
//
// It used to be a digit stamped into the range's fourth decimal, compared
// against the same digit here. That was built on precision nobody had
// measured — the spike verified the SECOND decimal survives, and the range
// is reconstructed as 5·rsqrt(atten) rather than read — and it showed as
// sockets that worked or did not depending on which digit their range
// happened to land on.
float _YAPS_SelfTag;

// --- the hole taper --------------------------------------------------
//
// How a hole closes around the plug: where narrowing begins and where it
// has closed to nothing, as fractions of plug length so a big plug and a
// small one taper over the same proportion of themselves. How abruptly a
// hole grips is taste rather than physics, so both are knobs.
float _YAPS_TaperStart;
float _YAPS_TaperEnd;

// --- blendshapes -----------------------------------------------------
//
// How many shape blocks follow the base one, and what each is currently
// worth. A vertex shader cannot read a blendshape weight, so the converter
// mirrors the animation driving each slider onto these instead.
//
// Eight, in two float4s. These are the shapes that change the PLUG's own
// rest mesh — length, girth, shape variants — and not the bulges a socket
// plays when something arrives, which are ordinary animation driven by
// depth and have no limit at all.
float _YAPS_ShapeCount;
float4 _YAPS_ShapeWeights;    // shapes 0-3
float4 _YAPS_ShapeWeights2;   // shapes 4-7

inline float YapsShapeWeight(uint index)
{
    float4 pack = index < 4 ? _YAPS_ShapeWeights : _YAPS_ShapeWeights2;
    uint slot = index & 3;
    return slot == 0 ? pack.x : slot == 1 ? pack.y : slot == 2 ? pack.z : pack.w;
}

#endif
