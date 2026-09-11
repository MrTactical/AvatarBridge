// YAPS, Yet Another Penetration System, for ChilloutVR.
// Property block, injected into the patched shader's Properties{}.
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
// Always 1 now. The baker writes renderer units, so nothing to undo.
// Kept because shipped materials still reference it.
float _YAPS_BakeScale;   // the plug's length scale, from its bones; 1 at rest
float _YAPS_BakeGirth;   // its radial scale, likewise

// Where the plug's own frame comes from.
//   0  the renderer's transform, when the plug is its own object
//   1  recovered from the vertex, required on a skinned mesh
float _YAPS_FrameFromVertex;

// --- the socket, written by the discrete channel ---------------------
// A CVRMaterialDriver task writes these every frame from animator
// parameters, so every camera and every viewer agrees.
// The editor harness writes the same four.
float4 _YAPS_SocketPos;    // see _YAPS_ChannelSpace
float4 _YAPS_SocketForward; // xyz world direction, w unused
float4 _YAPS_SocketUp;     // xyz world direction,  w unused
float4 _YAPS_SocketFlags;  // x: engaged 0..1, y: is-hole, z/w spare

// The socket's SECOND point, in the same space as _YAPS_SocketPos.
// Every socket already publishes one: TPS_Orf_Norm, SPSLL_Socket_Front.
// The pair IS the axis. Subtracting gives it outright.
float4 _YAPS_SocketFront;

// How to read _YAPS_SocketPos.
//   0  a world position, written directly. The editor harness does this.
//   1  the socket's offset in the PLUG's frame, each axis 0..1 across the
//      box below. The contact channel cannot express anything else.
//
// THE FRAME CHANNEL SPACE IS MEASURED IN, in the RENDERER's object space.
// Never the frame recovered per vertex. Tried; a plug rooted high in a
// skeleton decoded a different position per vertex and tore itself apart.
// Zero forward means nothing was published, so old bakes still work.
float4 _YAPS_ChannelOrigin;
float4 _YAPS_ChannelForward;
float4 _YAPS_ChannelUp;

float _YAPS_ChannelSpace;
float4 _YAPS_ChannelExtents;  // xyz half-extents of that box, in metres

// Which sockets belong to this plug's OWN avatar, so it can ignore them.
//
// A light carries a position and nothing else, no owner. Its wearer's own
// sockets are always in reach, so a plug spends its life bent into them.
//
// So ChilloutVR's player positions answer it: nearest player to the plug,
// nearest to the light, same person or not. Nothing is transmitted.
//
// A FLAG, not a tag. Zero or more means check ownership, -1 means do not.
// It used to be a digit in the range's fourth decimal. Only the second
// decimal survives, so sockets worked or not by luck.
float _YAPS_SelfTag;

// Whether the wearer's OWN sockets may be answered. Off by default, and that
// default is the point: a hole ends the chain, and a hole on the wearer's own
// body is nearly always nearer to the plug than anybody else's socket, so
// admitting them unasked would end the chain at home and no one else could
// ever be reached.
float _YAPS_SelfAllow;

// The wearer's owner id, animated from a synced parameter onto every plug
// and socket material the avatar carries. Zero is unknown. See
// YapsOwnerDecode in the atlas protocol.
float _YAPS_Owner;

// Which of the wearer's own sockets this plug may enter, a bit per socket
// number (bit 0 is socket 1). Chosen in the editor, set at build.
float _YAPS_SelfSockets;


// The screen atlas, off by default. A THIRD transport beside the channel
// and the lights, not a replacement for either.
float _YAPS_UseAtlas;

// Debug view. 0 off, 1 "Resolved by".
// The patcher only edits the VERTEX stage, so the view cannot paint.
// It answers in plug LENGTH instead. See YapsDeform.
float _YAPS_Debug;

// --- the hole taper --------------------------------------------------
//
// Where narrowing begins and where it closes to nothing, as fractions of
// plug length, so big and small plugs taper the same proportion.
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
//
// A resting bend and a curl at the tip, applied to the BAKED position
// before the curve walk.
//
// Curvature bends the whole shaft, most at the tip. ReCurvature adds an
// opposite bend near the tip, so a plug can sweep up then hook.
// Both are radians of turn at the tip.
float _YAPS_Curvature;
float _YAPS_ReCurvature;
// How much the BASE resists the bend toward a socket.
// 0 bends evenly from the root, 1 turns only the far part.
float _YAPS_EntranceStiffness;

// --- the curve's shape, from TPS ---------------------------------------
//
// The bezier handles decide how the shaft arrives.
// BezierSmoothness scales both: below 1 sharper, above 1 a wider arc.
// BezierStart holds the first fraction straight, SmoothStart eases into
// the curve rather than kinking at it.
float _YAPS_BezierSmoothness;
float _YAPS_BezierStart;
float _YAPS_SmoothStart;
// A socket nearer than this fraction of plug length is treated as this
// far, so the shaft cannot fold into a hairpin.
float _YAPS_MinimumSocketDistance;
//
// TPS's BUFFERED DEPTH is deliberately NOT a uniform. A vertex shader has
// no memory between frames. The channel's smoothing layer is the buffer,
// exposed as "socket follow". Lights and legacy content have no lag.

// --- which sockets this plug will answer, from SPS ---------------------
//
// Four tags to require and four to refuse, one PATTERN per component. A
// tag is a free-form string hashed FNV-1a exactly as SPS hashes it, then
// folded to three bits of twenty; a socket publishes the OR of its tags in
// the atlas's third pixel, and the test is whether all three of a tag's
// bits are lit there. Zero means the slot is empty.
//
// Patterns rather than the strings themselves because a shader cannot
// hash a name it never receives, and vectors rather than a set of floats
// because an animation drives a component the same way it drives a float,
// which is the only route a menu row has into a shader.
//
// The fold costs a false yes about one time in twenty on a socket wearing
// three tags. That is deliberate and the direction matters: a stray yes on
// the include side answers a socket that was not asking, and on the exclude
// side refuses one it need not have. See Runtime/Yaps/YapsTags.cs.
//
// ONLY THE ATLAS carries a socket's word. A marker light's RANGE is its
// whole message and the digits belong to Raliv; a contact says a socket is
// there and not what it is. Both routes answer without knowing, so a plug
// with a list still resolves through them. A preference, not a lock.
float4 _YAPS_TagInclude;
float4 _YAPS_TagExclude;

// --- blendshapes -----------------------------------------------------
//
// How many shape blocks follow the base one, and what each is worth.
// A vertex shader cannot read a blendshape weight, so the converter
// mirrors the animation driving each slider onto these instead.
//
// These change the PLUG's own rest mesh, not the bulges a socket plays.
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

#endif
