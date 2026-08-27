// YAPS, Yet Another Penetration System, for ChilloutVR.
// Property block, injected into the patched shader's Properties{}.
//
// Inspired by VRCFury's SPS, which invented this technique for VRChat.
// No SPS code is used here; see docs/YAPS-CLEAN-ROOM.md.
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
// in renderer units, so there is no scale to undo, kept because a shipped
// material carries it and removing a property a material references is a
// bigger change than leaving one that reads 1.
float _YAPS_BakeScale;   // the plug's length scale, from its bones; 1 at rest
float _YAPS_BakeGirth;   // its radial scale, likewise

// Where the plug's own frame comes from.
//   0  the renderer's transform, correct when the plug is its own object
//   1  recovered from the vertex, required on a skinned mesh, where the
//      renderer sits at the avatar root and a bone carries the plug
float _YAPS_FrameFromVertex;

// --- the socket, written by the discrete channel ---------------------
// A CVRMaterialDriver task writes these every frame from animator
// parameters, so they are identical on every camera and every viewer , 
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
// THE FRAME CHANNEL SPACE IS MEASURED IN, in the RENDERER's object space.
//
// The channel reports the socket's offset from the plug, and the shader
// used to rebuild it against the frame recovered from each VERTEX. On an
// ordinary plug every vertex recovers nearly the same frame so it worked;
// on a plug rooted high in a skeleton they recover wildly different ones,
// and the same offset decoded to a different world position per vertex.
// The mesh tore itself apart while the light path, which sends a WORLD
// position identical for everybody, looked perfect beside it.
//
// The trigger boxes ride ONE frame, the measured one, so the decode has to
// use that same one. Published here rather than derived, because the
// vertex cannot know it. Zero forward means nothing was published and the
// old per-vertex behaviour stands, so a bake from before this still works.
float4 _YAPS_ChannelOrigin;
float4 _YAPS_ChannelForward;
float4 _YAPS_ChannelUp;

float _YAPS_ChannelSpace;
float4 _YAPS_ChannelExtents;  // xyz half-extents of that box, in metres

// Which sockets belong to this plug's OWN avatar, so it can ignore them.
//
// A light carries a position and nothing else, no identity, no owner, so
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
// measured, the spike verified the SECOND decimal survives, and the range
// is reconstructed as 5·rsqrt(atten) rather than read, and it showed as
// sockets that worked or did not depending on which digit their range
// happened to land on.
// Debug view. 0 off; 1 "Resolved by". A patched shader can only be
// edited in its VERTEX stage — the patcher refuses surface shaders for
// exactly that reason — so the view cannot paint a colour and says what
// it knows in the one language every host shader shares: the plug's
// length. See YapsDeform.
float _YAPS_Debug;

float _YAPS_SelfTag;

// --- the hole taper --------------------------------------------------
//
// How a hole closes around the plug: where narrowing begins and where it
// has closed to nothing, as fractions of plug length so a big plug and a
// small one taper over the same proportion of themselves. How abruptly a
// hole grips is taste rather than physics, so both are knobs.
float _YAPS_TaperStart;
float _YAPS_TaperEnd;

// How much of its length and girth a plug keeps while nothing is using
// it, 1 being no change. TPS's idea, and the single most visible thing
// missing from this deform: a plug that never softens is at full mast
// permanently, which is wrong for almost the whole time it exists.
float _YAPS_IdleLength;
float _YAPS_IdleWidth;

// How tightly a socket grips the shaft, and how far either side of the
// opening that grip reaches. DPS and TPS both have this; without it an
// entry reads as a rod passing through a hoop rather than as anything
// tight.
float _YAPS_Squeeze;
float _YAPS_SqueezeDistance;

// The swell just SHORT of the opening, where flesh piles up rather than
// going through. Never on the far side, that would be the shaft growing
// inside whatever it entered.
float _YAPS_Bulge;
float _YAPS_BulgeDistance;

// Motion the plug makes on its own. Pumping only while engaged, wriggle
// only while idle, so the two can never fight.
float _YAPS_PumpStrength;
float _YAPS_PumpSpeed;
// How much of the shaft pumps: 1 is the whole length in one stroke, small
// values keep the base still and throw the tip. TPS has this as its own
// knob; ours was fixed at the tip-heavy end.
float _YAPS_PumpWidth;
float _YAPS_WriggleStrength;
float _YAPS_WriggleSpeed;

// --- shape at rest, from DPS ------------------------------------------
//
// A plug is not a straight rod. DPS gives it a resting bend and a curl at
// the tip, and lets the base resist bending toward a socket. All three
// are applied to the BAKED position before the curve walk, so the walk
// carries a shaft that already has a shape, exactly as it would carry one
// with a girth slider on.
//
// Curvature bends the whole shaft up (+) or down (-) along its length,
// most at the tip. ReCurvature adds a second bend of the opposite sign
// concentrated near the tip, so a plug can sweep up and then hook. Both
// are radians of total turn at the tip, so a big plug and a small one
// curve the same fraction of themselves.
float _YAPS_Curvature;
float _YAPS_ReCurvature;
// How much the BASE resists the bend toward a socket. 0 is the shaft
// bending evenly from the root, as it does now; 1 holds the first part of
// the shaft on the plug's own forward and lets only the far part turn.
// Reads as a plug that is stiff where it joins the body.
float _YAPS_EntranceStiffness;

// --- the curve's shape, from TPS ---------------------------------------
//
// The bezier's handles decide how the shaft arrives. The defaults are the
// law the deform has always used; these move it.
//
// BezierSmoothness scales BOTH handles: below 1 the curve turns sharper
// and arrives more directly, above 1 it sweeps in a wider arc. Two dials
// TPS also has: BezierStart holds the first fraction of the shaft
// straight before any bend starts, and SmoothStart eases the transition
// from that straight part into the curve rather than kinking at it.
float _YAPS_BezierSmoothness;
float _YAPS_BezierStart;
float _YAPS_SmoothStart;
// A socket nearer than this (fraction of plug length) is treated as if it
// were this far away, so a plug pushed hard against a socket does not
// fold into a hairpin trying to reach a point behind its own root.
float _YAPS_MinimumSocketDistance;
//
// TPS's BUFFERED DEPTH, the deform following the socket with a lag, so a
// thrust has weight, is deliberately NOT a uniform here. A vertex shader
// has no memory between frames, so a lag cannot live in it honestly. It
// already exists in the right place: the channel's smoothing layer moves
// the received value at most StepSize per frame, and that step is the
// buffer. The converter exposes it as "socket follow" and writes it into
// the layer's constant. Lights and legacy content have no channel and no
// lag, which is also honest, a DPS plug never had one either.

// --- which sockets this plug will answer, from SPS ---------------------
//
// SPS lets a plug name up to four tags it insists on and four it refuses.
// Here it is two: one tag to require, one to refuse, each carried as a
// small integer the converter hashes from the tag string, and the channel
// carries the socket's own tag the same way (_YAPS_SocketFlags.z). A
// socket whose tag is refused is ignored; if a required tag is set, only
// sockets carrying it are answered. Zero means "no filter" on both.
//
// Marker lights carry no tag, so a light-only socket cannot be filtered
// and is always answered, the filter only bites where the channel
// speaks, which is where a tag exists to compare.
float _YAPS_TagInclude;
float _YAPS_TagExclude;

// --- blendshapes -----------------------------------------------------
//
// How many shape blocks follow the base one, and what each is currently
// worth. A vertex shader cannot read a blendshape weight, so the converter
// mirrors the animation driving each slider onto these instead.
//
// Eight, in two float4s. These are the shapes that change the PLUG's own
// rest mesh, length, girth, shape variants, and not the bulges a socket
// plays when something arrives, which are ordinary animation driven by
// depth and have no limit at all.
float _YAPS_ShapeCount;
float4 _YAPS_ShapeWeights;    // shapes 0-3
float4 _YAPS_ShapeWeights2;   // shapes 4-7
float4 _YAPS_ShapeWeights3;   // shapes 8-11
float4 _YAPS_ShapeWeights4;   // shapes 12-15

// Sixteen, matching SPS. Eight was chosen when the bake was new and cost
// was the worry; a plug with separate length, girth, curve and knot
// sliders spends eight without trying, and a shape the bake does not
// carry is one the deform silently ignores.
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
