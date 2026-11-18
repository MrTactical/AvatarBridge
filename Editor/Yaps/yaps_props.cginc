// YAPS, Yet Another Penetration System, for ChilloutVR.
// The uniforms behind the properties the patcher adds to Properties{}.
//
// Inspired by VRCFury's SPS. No SPS code here.
// See docs/YAPS-CLEAN-ROOM.md.
#ifndef YAPS_PROPS_INCLUDED
#define YAPS_PROPS_INCLUDED

// Own cbuffer, not $Globals (where host properties shift offsets): a fixed layout makes every
// patched vertex program the same bytes, built once per driver. Textures are hoisted out.
cbuffer YapsProps {

// --- baked mesh data -------------------------------------------------
// Header pixel, then ten floats per vertex.
// position(3) normal(3) tangent(3) active(1), one RGBA32 pixel each.
Texture2D _YAPS_Bake;
float4 _YAPS_Bake_TexelSize;

// Vertices in the base block; past it the texture holds blendshapes.
float _YAPS_VertexCount;

// --- plug description ------------------------------------------------
float _YAPS_Enabled;       // master gate / apply fraction, 0..1
float _YAPS_Length;        // plug length in its own local space
float _YAPS_Overrun;       // may the tip travel past the socket
// Length and radial scale from the plug's own bones, 1 as baked. YapsCurveMirror
// animates them from the bones' scale curves so size sliders reach the bake.
float _YAPS_BakeScale;
float _YAPS_BakeGirth;
// Which plug on a shared mesh this material is: 0 alone or first, 1 second. Never animated, so
// each slot keeps its own, while every animated value reaches every slot.
float _YAPS_PlugLane;
// The second plug's own copies.
float _YAPS_BakeScale2;
float _YAPS_BakeGirth2;
float _YAPS_Enabled2;
float _YAPS_SelfAllow2;
// Each plug's own switches, so the second plug's menu toggle leaves the first alone.
#define YAPS_ENABLED (_YAPS_PlugLane > 0.5 ? _YAPS_Enabled2 : _YAPS_Enabled)
#define YAPS_SELF_ALLOW (_YAPS_PlugLane > 0.5 ? _YAPS_SelfAllow2 : _YAPS_SelfAllow)
// A skinned plug's normal length at bake, the skin's own scale then.
// 0 is not measured, and the plug keeps its baked size.
float _YAPS_BakeSkinScale;

// Where the plug's own frame comes from.
//   0  the renderer's transform, when the plug is its own object
//   1  recovered from the vertex, required on a skinned mesh
float _YAPS_FrameFromVertex;

// --- the editor's preview socket -------------------------------------
// World space, from a property block that never ships. _YAPS_ChannelSpace 1 marks
// the removed contact channel route (see YapsResolveSocket) and is ignored.
float4 _YAPS_SocketPos;     // xyz world position
float4 _YAPS_SocketForward; // xyz world direction, w unused
float4 _YAPS_SocketFlags;   // x: engaged 0..1, y: is-hole, z/w spare
float _YAPS_ChannelSpace;

// Whether to skip the wearer's own sockets, always in reach otherwise. A light has
// no owner, so CVR player positions decide: nearest player to plug vs to light.
// A flag: >= 0 checks ownership, -1 does not. (Not a range digit: only the second
// decimal survives.)
float _YAPS_SelfTag;

// Off by default: a hole ends the chain, and the wearer's own is nearly always
// nearest, so nobody else could be reached.
float _YAPS_SelfAllow;

// Animated from a synced parameter. Zero is unknown. See YapsOwnerDecode.
float _YAPS_Owner;

// Own sockets this plug may enter, bit 0 is socket 1. Set at build.
float _YAPS_SelfSockets;

// The ones chosen by name (hand tick or SPS Self rule); they skip the tag test.
float _YAPS_SelfChosen;

// 1 to 15, keys the plug's light relay with the owner. Zero: no relay.
float _YAPS_PlugIndex;


// Property defaults to 0; conversion and Build set 1. Marker lights cover the rest.
float _YAPS_UseAtlas;

// Debug view. 0 off, 1 resolved by, 2 gap to socket, 3 engagement,
// 4 socket facing, 5 atlas taps, 6 atlas target.
// The patcher only edits the vertex stage, so it answers in plug length. See YapsDeform.
float _YAPS_Debug;

// --- the hole taper --------------------------------------------------
// Fractions of plug length, so every size tapers the same proportion.
float _YAPS_TaperStart;
float _YAPS_TaperEnd;

// Kept while idle, 1 no change.
float _YAPS_IdleLength;
float _YAPS_IdleWidth;

// Grip and its reach either side of the opening; without it entry reads as a rod through a hoop.
float _YAPS_Squeeze;
float _YAPS_SqueezeDistance;

// The swell just short of the opening, never past it.
float _YAPS_Bulge;
float _YAPS_BulgeDistance;
// Peak distance short of the opening (TPS's shape). Zero: an even hump over the reach.
float _YAPS_BulgeFalloff;

// Pumping only while engaged, wriggle only while idle, so they never fight.
float _YAPS_PumpStrength;
float _YAPS_PumpSpeed;
// 1 pumps the whole length; small values keep the base still and throw the tip.
float _YAPS_PumpWidth;
float _YAPS_WriggleStrength;
float _YAPS_WriggleSpeed;

// --- shape at rest, from DPS ------------------------------------------
// Applied to the baked position before the curve walk. Radians of turn at the tip;
// ReCurvature is an opposite bend near the tip, to sweep then hook.
float _YAPS_Curvature;
float _YAPS_ReCurvature;
// 0 bends evenly from the root, 1 turns only the far part.
float _YAPS_EntranceStiffness;

// --- the curve's shape, from TPS ---------------------------------------
// BezierSmoothness scales both handles (below 1 sharper). BezierStart holds the
// first fraction straight, SmoothStart eases into the curve.
float _YAPS_BezierSmoothness;
float _YAPS_BezierStart;
float _YAPS_SmoothStart;
// Nearer sockets count as this far, so the shaft cannot fold into a hairpin.
float _YAPS_MinimumSocketDistance;
// TPS's buffered depth is not a uniform: a vertex shader has no memory between frames.

// --- which sockets this plug will answer, from SPS ---------------------
// One tag pattern per component, zero empty (see Runtime/Yaps/YapsTags.cs). Patterns
// because a shader cannot hash a name; vectors because animation is the only route
// a menu row has in. Only the atlas carries a socket's tags: lights and contacts
// answer without knowing, so a list is a preference, not a lock.
float4 _YAPS_TagInclude;
float4 _YAPS_TagInclude2;
float4 _YAPS_TagExclude;

// --- blendshapes -----------------------------------------------------
// A vertex shader cannot read blendshape weights, so the animation driving each
// slider is mirrored onto these. They change the plug's rest mesh, not socket shapes.
float _YAPS_ShapeCount;
float4 _YAPS_ShapeWeights;    // shapes 0-3
float4 _YAPS_ShapeWeights2;   // shapes 4-7
float4 _YAPS_ShapeWeights3;   // shapes 8-11
float4 _YAPS_ShapeWeights4;   // shapes 12-15

// Band a portal collapses either side of its cut, as a fraction of rest length (widest
// triangle span plus margin). -1: too coarse to cut, entrances stay holes.
float _YAPS_PortalSpan;

// First vertex of the appended second copy; 0 none. Read only under YAPS_DUPLICATE.
float _YAPS_CopyStart;

// --- live tissue (the jelly) -----------------------------------------
// A particle lattice the blitters step each frame; parts it carries leave the squash. Here,
// not yaps_jelly.cginc, since the dent reads _YAPS_JellyParts. Four slots, one jelly each.
Texture2D<float4> _YAPS_JellyState;
float4 _YAPS_JellyLattice;  // particles across, up and deep; spacing in metres. 0: no lattice
Texture2D<float4> _YAPS_JellyState2;
float4 _YAPS_JellyLattice2;
Texture2D<float4> _YAPS_JellyState3;
float4 _YAPS_JellyLattice3;
Texture2D<float4> _YAPS_JellyState4;
float4 _YAPS_JellyLattice4;
// Per vertex its two strongest jelly parts, each as 2 slot + region plus one over 255 (0 none),
// then its share: (part, share, part, share).
Texture2D<float4> _YAPS_JellyOwn;
float4 _YAPS_JellyOwn_TexelSize;
float4 _YAPS_JellyParts;    // for each of the squash's parts (r, g, b, a of _YAPS_DentOwn) the jelly carries, its jelly part as _YAPS_JellyOwn names it
float4 _YAPS_JellySlap;     // a slap's ring: how much of its depth (0 none), speed (m/s), wavelength (m), fade (s)
};

// Sixteen, matching SPS. Eight ran out on a plug with many sliders.
inline float YapsShapeWeight(uint index)
{
    float4 pack = index < 4 ? _YAPS_ShapeWeights
                : index < 8 ? _YAPS_ShapeWeights2
                : index < 12 ? _YAPS_ShapeWeights3
                : _YAPS_ShapeWeights4;
    uint slot = index & 3;
    return slot == 0 ? pack.x : slot == 1 ? pack.y : slot == 2 ? pack.z : pack.w;
}

#endif
