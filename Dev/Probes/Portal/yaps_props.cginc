// YAPS, Yet Another Penetration System, for ChilloutVR.
// The uniforms behind the properties the patcher adds to Properties{}.
//
// Inspired by VRCFury's SPS. No SPS code here.
// See docs/YAPS-CLEAN-ROOM.md.
#ifndef YAPS_PROPS_INCLUDED
#define YAPS_PROPS_INCLUDED

// --- baked mesh data -------------------------------------------------
// Header pixel, then ten floats per vertex.
// position(3) normal(3) tangent(3) active(1), one RGBA32 pixel each.
Texture2D _YAPS_Bake;
float4 _YAPS_Bake_TexelSize;

// Vertices in the base block.
// Past this the texture holds blendshapes, so reads return nonsense.
float _YAPS_VertexCount;

// --- plug description ------------------------------------------------
float _YAPS_Enabled;       // master gate / apply fraction, 0..1
float _YAPS_Length;        // plug length in its own local space
float _YAPS_Overrun;       // may the tip travel past the socket
// The plug's length and radial scale from its OWN bones, 1 as baked.
// YapsCurveMirror animates both from the bones' scale curves, so a size
// slider reaches the bake. Held at 1, a scaled plug squashes.
float _YAPS_BakeScale;
float _YAPS_BakeGirth;

// Where the plug's own frame comes from.
//   0  the renderer's transform, when the plug is its own object
//   1  recovered from the vertex, required on a skinned mesh
float _YAPS_FrameFromVertex;

// --- the editor's preview socket -------------------------------------
// World space, from a property block that never ships. _YAPS_ChannelSpace
// is a leftover kept for old materials; a material flagged 1 is ignored.
float4 _YAPS_SocketPos;     // xyz world position
float4 _YAPS_SocketForward; // xyz world direction, w unused
float4 _YAPS_SocketFlags;   // x: engaged 0..1, y: is-hole, z/w spare
float _YAPS_ChannelSpace;

// Ignore sockets on the plug's OWN avatar. A light carries no owner and the
// wearer's own sockets are always in reach, so ChilloutVR's player positions
// decide: nearest player to the plug vs nearest to the light.
// A flag: zero or more checks ownership, -1 does not. A digit in the range
// failed: only the second decimal survives.
float _YAPS_SelfTag;

// Off by default on purpose: the wearer's own hole is nearly always nearest,
// and a hole ends the chain, so nobody else could ever be reached.
float _YAPS_SelfAllow;

// Owner id from a synced parameter, 0 unknown. See YapsOwnerDecode.
float _YAPS_Owner;

// Wearer's sockets this plug may enter, bit 0 is socket 1.
float _YAPS_SelfSockets;

// Those chosen by name skip the tag test; a default tick does not.
float _YAPS_SelfChosen;


// Defaults to 0; conversion and Build set 1. Marker lights cover the rest.
float _YAPS_UseAtlas;

// Debug view. 0 off, 1 resolved by, 2 gap to socket, 3 engagement,
// 4 socket facing, 5 atlas taps, 6 atlas target.
// Only the vertex stage is patched, so it answers in plug LENGTH, not colour.
float _YAPS_Debug;

// --- the hole taper --------------------------------------------------
// Fractions of plug length, so every size tapers in proportion.
float _YAPS_TaperStart;
float _YAPS_TaperEnd;

// How much length and girth a plug keeps while idle, 1 being no change.
// Without it a plug is at full mast permanently.
float _YAPS_IdleLength;
float _YAPS_IdleWidth;

// How tightly a socket grips, and how far either side of the opening.
// Without it entry reads as a rod through a hoop.
float _YAPS_Squeeze;
float _YAPS_SqueezeDistance;

// The swell just SHORT of the opening. Never on the far side,
// that would be the shaft growing inside what it entered.
float _YAPS_Bulge;
float _YAPS_BulgeDistance;
// Where it peaks, that far short of the opening, rising over the reach
// behind it: TPS's shape. Zero is the even hump over the reach alone.
float _YAPS_BulgeFalloff;

// Motion the plug makes alone. Pumping only while engaged,
// wriggle only while idle, so the two never fight.
float _YAPS_PumpStrength;
float _YAPS_PumpSpeed;
// How much of the shaft pumps. 1 is the whole length in one stroke,
// small values keep the base still and throw the tip.
float _YAPS_PumpWidth;
float _YAPS_WriggleStrength;
float _YAPS_WriggleSpeed;

// --- shape at rest, from DPS ------------------------------------------
// Applied to the BAKED position before the curve walk. Radians at the tip;
// ReCurvature bends back near the tip, so a plug can sweep up then hook.
float _YAPS_Curvature;
float _YAPS_ReCurvature;
// How much the BASE resists the bend toward a socket.
// 0 bends evenly from the root, 1 turns only the far part.
float _YAPS_EntranceStiffness;

// --- the curve's shape, from TPS ---------------------------------------
// BezierSmoothness scales both handles: below 1 sharper, above 1 wider.
// BezierStart holds the first fraction straight, SmoothStart avoids a kink.
float _YAPS_BezierSmoothness;
float _YAPS_BezierStart;
float _YAPS_SmoothStart;
// A socket nearer than this fraction of plug length is treated as this
// far, so the shaft cannot fold into a hairpin.
float _YAPS_MinimumSocketDistance;
// TPS's buffered depth is not a uniform: a vertex shader has no memory
// between frames.

// --- which sockets this plug will answer, from SPS ---------------------
// Four tags to require, four to refuse, one PATTERN per component: the tag
// hashed FNV-1a as SPS does, folded to three bits of twenty. A socket
// publishes the OR of its tags in the atlas's third pixel. Zero is empty.
// Patterns because a shader cannot hash a name it never receives; vectors
// because animation is the only route a menu row has into a shader.
// The fold gives a false yes about 1 in 20 on a three-tag socket. See
// Runtime/Yaps/YapsTags.cs.
// Only the atlas carries tags; lights and contacts answer regardless, so
// this is a preference, not a lock.
float4 _YAPS_TagInclude;
float4 _YAPS_TagExclude;

// --- blendshapes -----------------------------------------------------
// A vertex shader cannot read a blendshape weight, so the converter mirrors
// each slider's animation here. The plug's own rest mesh, not socket bulges.
float _YAPS_ShapeCount;
float4 _YAPS_ShapeWeights;    // shapes 0-3
float4 _YAPS_ShapeWeights2;   // shapes 4-7
float4 _YAPS_ShapeWeights3;   // shapes 8-11
float4 _YAPS_ShapeWeights4;   // shapes 12-15

// Sixteen, matching SPS. Eight ran out on a plug with separate
// length, girth, curve and knot sliders.
inline float YapsShapeWeight(uint index)
{
    float4 pack = index < 4 ? _YAPS_ShapeWeights
                : index < 8 ? _YAPS_ShapeWeights2
                : index < 12 ? _YAPS_ShapeWeights3
                : _YAPS_ShapeWeights4;
    uint slot = index & 3;
    return slot == 0 ? pack.x : slot == 1 ? pack.y : slot == 2 ? pack.z : pack.w;
}

// PORTAL SPIKE. The band a portal collapses either side of its cut, as a
// fraction of rest length: the widest triangle span along the shaft, with a
// margin. -1 when the mesh is too coarse to cut, so entrances stay holes.
float _YAPS_PortalSpan;

#ifdef YAPS_DUPLICATE
// DUPLICATE SPIKE. The first vertex of the plug's second copy, appended after
// every original vertex; 0 when the mesh carries none.
float _YAPS_CopyStart;
#endif

#endif
