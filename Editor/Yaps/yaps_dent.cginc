// YAPS, the SOFT-BODY SQUASH. Flesh keeps its volume: pressed flat one way,
// it grows the others.
//
// Every move here is a function of world position alone, smooth, and never
// folds, so every layer at a point (skin, a top, a bra) takes the same move
// and keeps its order. The first dents moved each vertex by its own facing or
// snapped layers onto one surface, and the skin came through the top in
// stripes and blotches.
//
// A soft PART (a breast, a cheek) is pressed by its partner, along the line
// between them; by hands reaching into it, the own arms and every other
// player's, from their side; and lying back, by its own weight. Pressed from one
// side it first bends away, swinging about where it meets the body. A hand then
// dents it where it presses, the flesh it moves rising in a rim around the dent;
// only what the dent cannot take moves the whole part. Heavy flesh moves where
// it is touched; a part that flattened all over under one finger read as a
// water balloon. The whole-part squash runs along the line the presses mostly
// share, each side moving in by what pressed it, so two hands from opposite
// sides squeeze instead of cancelling, and it grows across by exactly what keeps
// its volume. Neither part of a pair is ever moved away from the other: with the
// contact on, what overlaps is pressed in where the two meet, the part driven
// further keeping its round front and the other taking a hollow its shape, and
// what that pushes back swells out round it. Then the own arms and hands, as
// capsules, ease the skin out of their way.
//
// How big a part is toward any side is an ellipsoid of six reaches measured at
// rest, in a frame from bones no cloth moves: toward the partner, away from it,
// out of the body, into it, and either way across.
//
// Which part a vertex is comes from the mesh's own skin weights, baked per
// vertex: its weight to a part's bones is its share of that part. Guessed from
// position and facing instead, neighbours either side of a guess went
// different ways and tore the skin; skin weights fade into the chest the way
// the avatar was painted, so the squash does too.
//
// The positions arrive from a material data provider every frame, on every
// client, locally: nothing is synced and no camera renders.

#ifndef YAPS_DENT_INCLUDED
#define YAPS_DENT_INCLUDED

// How much of a layer's depth below a press survives it, so stacked layers stay stacked.
#define YAPS_DENT_KEEP 0.2
// How far before the contact the squash starts to give, as a share of the reach.
#define YAPS_SQUASH_SOFT 0.1
// Where the pair meets, how far short of the seam each side stops, as a share of the reach: at
// the tenth it read as a gap down the middle.
#define YAPS_SQUASH_MEET 0.03
// How wide the contact's edge rounds over, in overlap depths. Eased over a fixed sliver of the
// reach, the edge was a crease down the middle however deep the overlap.
#define YAPS_SQUASH_ROUND 1.0
// How close two squeezes count as level, as a share of the stronger: within it their lines
// share the flattening, wholly when exactly level.
#define YAPS_SQUASH_LEVEL 0.3
#define YAPS_DENT_SPAN 2.4494897   // the dent's shoulder, in t; root 6 makes it hold what the bowl moved

float _YAPS_DentPower;
float _YAPS_DentSpill;
Texture2D _YAPS_DentOwn;       // per vertex: its share of parts 0 to 3 in r, g, b, a
float4 _YAPS_DentOwn_TexelSize;
// Slots, as the provider writes them. One with nothing in it holds a node scaled to zero,
// which reads as absent.
//   0-3   each part's follow: a plain child of the bone the cloth moves most, at the part's centre
//   4-7   each part's rest: the same point on the bone above the chain, which the cloth never moves;
//         its up axis points from the part's centre to where the part hangs from
//   8-11  left upper arm, lower arm, hand, middle finger; 12-15 the same on the right
//   16-17 each pair's back: a node on the rest bone behind the pair, so the pair's middle
//         minus it points out of the body, level when the avatar stands
//   18-19 each pair's socket, when one sits between the pair: the shaft a plug runs through it
// Parts 0 and 1 are a pair, 2 and 3 another.
float4x4 _YAPS_SquashFollow[20];
// Reaches per part, in its follow's units: toward the partner, away from it, out of the body,
// into it, and along cross(toward, out) either way.
float4 _YAPS_SquashReach;
float4 _YAPS_SquashAway;
float4 _YAPS_SquashOut;
float4 _YAPS_SquashIn;
float4 _YAPS_SquashSideA;
float4 _YAPS_SquashSideB;
float4 _YAPS_SquashHangToward;  // where each part hangs from, off its rest, rest units: toward the partner,
float4 _YAPS_SquashHangOut;     // out of the body,
float4 _YAPS_SquashHangAcross;  // and along cross(toward, out)
float4 _YAPS_SquashPair;    // pair 0: x where pressing starts (follow gap, part 0's units), y part 0's share; pair 1: z, w
float4 _YAPS_SquashShape;   // x flattest, y volume kept (1 all, below 1 lets some go), z widest, w contact plane on
float4 _YAPS_SquashPush;    // x push gain, y push ignored below this share of the reach, z capsule edge as a share of its radius, w arms on
float4 _YAPS_SquashLimbL;   // radius of the upper arm, lower arm and hand, each as a share of its own length
float4 _YAPS_SquashLimbR;
float _YAPS_SquashPulse;    // test builds: a squeeze from the outside that comes and goes on its own, as a share of the start
float4 _YAPS_SquashHands;   // x own arms' press gain, y other players' hand radius in metres at a 1.6 m viewpoint, z their press gain, w skin stride in bytes (0 none)
float4 _YAPS_SquashTune;    // x lying-back weight, as a share of the reach out; y strength of every press; z bend give, as a share of the reach out; w deepest dent, as a share of the reach toward the hand
float4 _YAPS_SquashMotion;  // x stretch per reach of swing, y swing ignored below this share of the reach, z longest stretch, w unused
float _YAPS_SquashPlug;     // a plug's press gain
float4 _CVR_PlayerLeftHandPositions[255];
float4 _CVR_PlayerRightHandPositions[255];
float4 _CVR_PlayerChestPositions[255];
float4 _CVR_PlayerRootPositions[255];   // w the player's viewpoint height

// The same slots again, from a helper mesh weighted one vertex to each and skinned. The provider
// reads transforms in a script LateUpdate, before the cloth writes its bones and after it put
// them back to rest, so a follow read there never moves. Skinning runs after the cloth.
#if SHADER_TARGET >= 45
ByteAddressBuffer _YAPS_SquashSkin;
#endif

// Where the slots come from, set once per vertex: 1 the skinned buffer, 2 it reads empty, 3 it
// reads somewhere the first rest slot is not, 0 no helper. Anything but 1 reads the matrices,
// so a buffer that never arrives costs the cloth push and nothing else.
static int yapsSkin;

int YapsSkinState()
{
#if SHADER_TARGET >= 45
    if (_YAPS_SquashHands.w <= 0) return 0;
    uint stride = (uint) _YAPS_SquashHands.w;
    float3 s = asfloat(_YAPS_SquashSkin.Load3(4 * stride));
    if (all(s == 0)) return 2;
    // Every slot against its matrix, which agree but for a frame of motion and the cloth's swing
    // (an empty one sits on the host in both); arms by an arm's length, as the IK may hold a hand
    // far from the pose the matrices saw. One slot off reads them all from the matrices: remote
    // copies blew up in metres-long streaks, and mixing the two sources put a pair's halves in
    // different frames.
    uint i = 0;
    [loop]
    for (; i < 20; i++)
    {
        uint3 raw = _YAPS_SquashSkin.Load3(i * stride);
        // NaN and infinity by their bits: the compiler folds a float test for NaN away.
        if (any((raw & 0x7f800000) == 0x7f800000)) return 3;
        float3 d = asfloat(raw) - _YAPS_SquashFollow[i]._m03_m13_m23;
        if (dot(d, d) >= (i >= 8 && i < 16 ? 2.25 : 0.25)) return 3;
    }
    // ChilloutVR's shader filter can end the loop early, leaving slots unchecked: matrices then.
    return i == 20 ? 1 : 3;
#else
    return 0;
#endif
}

float3 YapsSlot(int i)
{
#if SHADER_TARGET >= 45
    if (yapsSkin == 1) return asfloat(_YAPS_SquashSkin.Load3(i * (uint) _YAPS_SquashHands.w));
#endif
    return _YAPS_SquashFollow[i]._m03_m13_m23;
}
float YapsUnit(int i) { return length(_YAPS_SquashFollow[i]._m00_m10_m20); }
// A limb's radius, from its bone's length in the matrices. The skinned slots can arrive spoilt
// (remote copies blew up in metres-long streaks), and a radius read from them grew with the gap.
float YapsLimb(uint k)
{
    uint a = 8 + (k / 3) * 4 + k % 3;
    return (k < 3 ? _YAPS_SquashLimbL : _YAPS_SquashLimbR)[k % 3]
        * distance(_YAPS_SquashFollow[a + 1]._m03_m13_m23, _YAPS_SquashFollow[a]._m03_m13_m23);
}

// s past a surface, eased back from soft before it to a fifth of the slope, never flat: how
// far it goes back, and the slope left.
float YapsEase(float s, float soft, out float slope)
{
    slope = 1;
    float u = s + soft;
    if (u <= 0) return 0;
    float e = exp(-u / soft);
    slope = (1 - YAPS_DENT_KEEP) * e + YAPS_DENT_KEEP;
    return s + soft - soft * (1 - YAPS_DENT_KEEP) * (1 - e) - YAPS_DENT_KEEP * u;
}

// A part as the solve sees it. Its frame comes from slots no cloth moves: a toward the
// partner, o out of the body, u across both. Unframed (no back slot, or reaches not baked), it
// is a ball of its reach.
struct YapsPart
{
    int m;
    float3 centre, a, o, u;
    // The six reaches as 1 / reach^2, toward +a, +o, +u and away from each.
    float3 front, back;
    float unit, reach;
    bool framed;
};

YapsPart YapsPartAt(int m, float3 centre, float unit)
{
    YapsPart p;
    p.m = m;
    p.centre = centre;
    p.unit = unit;
    p.reach = _YAPS_SquashReach[m] * unit;
    p.a = 0;
    p.o = 0;
    p.u = 0;
    p.front = 0;
    p.back = 0;
    p.framed = false;
    int back = 16 + (m >> 1);
    if (YapsUnit(4 + m) > 0 && YapsUnit(4 + (m ^ 1)) > 0 && YapsUnit(back) > 0
        && _YAPS_SquashAway[m] > 0 && _YAPS_SquashOut[m] > 0 && _YAPS_SquashIn[m] > 0
        && _YAPS_SquashSideA[m] > 0 && _YAPS_SquashSideB[m] > 0)
    {
        float3 r0 = YapsSlot(4 + m), r1 = YapsSlot(4 + (m ^ 1));
        float3 a = r1 - r0;
        if (dot(a, a) > 1e-10)
        {
            a = normalize(a);
            float3 o = 0.5 * (r0 + r1) - YapsSlot(back);
            o -= a * dot(o, a);
            if (dot(o, o) > 1e-10)
            {
                p.a = a;
                p.o = normalize(o);
                p.u = cross(p.a, p.o);
                p.framed = true;
                p.front = 1 / float3(_YAPS_SquashReach[m] * _YAPS_SquashReach[m], _YAPS_SquashOut[m] * _YAPS_SquashOut[m], _YAPS_SquashSideA[m] * _YAPS_SquashSideA[m]);
                p.back = 1 / float3(_YAPS_SquashAway[m] * _YAPS_SquashAway[m], _YAPS_SquashIn[m] * _YAPS_SquashIn[m], _YAPS_SquashSideB[m] * _YAPS_SquashSideB[m]);
            }
        }
    }
    return p;
}

// How far the part reaches toward w, a unit vector: the ellipsoid of its six measured reaches.
// A ball of the reach toward the partner was far too wide at the sides, and an arm hanging
// there pressed it all the time.
float YapsExtent(YapsPart p, float3 w)
{
    if (!p.framed) return p.reach;
    float3 c = float3(dot(w, p.a), dot(w, p.o), dot(w, p.u));
    return p.unit * rsqrt(max(dot(c * c, c >= 0 ? p.front : p.back), 1e-8));
}

// How far x lies inside the part's shape, back along w, a unit vector, to where it leaves it on
// that side: below 0 outside.
float YapsInside(YapsPart p, float3 x, float3 w)
{
    float3 q = x - p.centre;
    float3 Q = p.framed ? float3(dot(q, p.a), dot(q, p.o), dot(q, p.u)) : q;
    float3 D = p.framed ? float3(dot(w, p.a), dot(w, p.o), dot(w, p.u)) : w;
    float3 S = Q - D * dot(Q, D);
    float3 P0 = S + D * YapsExtent(p, w);
    float3 Wt = p.framed ? (P0 >= 0 ? p.front : p.back) / (p.unit * p.unit) : 1 / (p.reach * p.reach);
    float qa = dot(Wt, D * D), qb = dot(Wt, S * D), qc = dot(Wt, S * S) - 1;
    float disc = qb * qb - qa * qc;
    return (-qb + sign(disc) * sqrt(abs(disc))) / max(qa, 1e-12) - dot(Q, D);
}

// v turned as f turns onto t, both unit vectors, the shortest way.
float3 YapsTurn(float3 v, float3 f, float3 t)
{
    float3 k = cross(f, t);
    float c = dot(f, t);
    return v * c + cross(k, v) + k * (dot(k, v) / max(1 + c, 1e-4));
}

// The part's frame turned with its swing about the bone it hangs from: a swing turns a part as
// much as it moves it, and read as sliding, a part turned to face its partner front first met it
// with the side it no longer showed.
YapsPart YapsTurned(YapsPart p)
{
    if (!p.framed) return p;
    // In the rest's units: the bone it hangs from rides the rest's bone, and a bone between them
    // scaled by a toggle scales the follow but never moves it.
    float3 hang = (p.a * _YAPS_SquashHangToward[p.m] + p.o * _YAPS_SquashHangOut[p.m] + p.u * _YAPS_SquashHangAcross[p.m]) * YapsUnit(4 + p.m);
    // Not baked, or the part hangs from its own centre: nothing to turn about.
    if (dot(hang, hang) < 1e-12) return p;
    float3 t = p.centre - YapsSlot(4 + p.m) - hang;
    if (dot(t, t) < 1e-12) return p;
    float3 f = normalize(-hang);
    t = normalize(t);
    p.a = YapsTurn(p.a, f, t);
    p.o = YapsTurn(p.o, f, t);
    p.u = YapsTurn(p.u, f, t);
    return p;
}

// Presses add up as their sum S and their outer products over length M. The sum alone let two
// hands from opposite sides cancel; M keeps them as a squeeze along their line.
void YapsAdd(inout float3 S, inout float3x3 M, float3 v)
{
    float l = length(v);
    if (l <= 1e-8) return;
    S += v;
    M += float3x3(v.x * v, v.y * v, v.z * v) / l;
}

// A capsule from a to b of radius r reaching into a part: the press toward it, by how far.
// Never more than the line is from the part's centre: an arm passing through turns the line to
// it right over in a few frames, and pressed full depth there the whole part flipped from one
// side to the other. Held to that, the turn moves the skin no faster than the arm.
float3 YapsPress(YapsPart p, float3 a, float3 b, float r)
{
    float3 ab = b - a;
    float len2 = dot(ab, ab);
    float3 to = a + ab * saturate(dot(p.centre - a, ab) / max(len2, 1e-10)) - p.centre;
    float dist = length(to);
    if (dist <= 1e-6) return 0;
    float press = min(YapsExtent(p, to / dist) + r - dist, dist);
    return press > 0 ? to / dist * press : 0;
}

// Two presses as one, each weighted by its depth to the fourth: the deeper leads, and one
// overtaking the other eases across. Taking the deeper alone jumped the dent from one to the
// other the moment it overtook, and the part swung with it.
void YapsMerge(inout float3 v, inout float r, float3 v2, float r2)
{
    float a = dot(v, v), b = dot(v2, v2);
    if (b <= 0) return;
    if (a <= 0) { v = v2; r = r2; return; }
    float wa = a * a, wb = b * b;
    float3 dir = v * (a * sqrt(a)) + v2 * (b * sqrt(b));
    float l = length(dir);
    float depth = (sqrt(a) * wa + sqrt(b) * wb) / max(wa + wb, 1e-36);
    v = l > 1e-6 * (wa + wb) ? dir / l * depth : (wa >= wb ? v : v2);
    r = (r * wa + r2 * wb) / max(wa + wb, 1e-36);
}

// The hands reaching into the part, as presses toward each hand by how far it is in, with its
// radius: both own arms first, then other hands, the deepest and the rest merged.
// One loop over every source, arms, then hands, then plugs, so the press and the merge are
// compiled once each: a loop per source inlined them four times, and an upload compiles this
// for every variant of every patched shader.
void YapsHandsPress(YapsPart p, inout float3 hv[4], inout float hr[4])
{
    // Per arm, its bones merged, the deepest leading: upper arm, forearm and hand overlap at
    // the joints, and adding all three pressed one forearm in twice.
    int arms = _YAPS_SquashPush.w > 0 && _YAPS_SquashHands.x > 0 ? 6 : 0;
    // Every player's hands, the wearer's among them, which the arms above already are: a
    // hand by either own hand is skipped. Capped at 64 players for cost. The globals carry
    // only the wrist, a palm's length out from the skin while the palm is on it, so a ball
    // there never reached the part: the hand runs on from it, away from that player's chest,
    // for a ninth of their viewpoint height.
    // The gesture rides in each hand's w, the numbers an avatar's GestureLeft reads: -1 open,
    // 0 to 1 a fist closing, 2 thumbs up, 3 finger gun, 4 point, 5 victory, 6 rock and roll.
    // A fist ends at the knuckles; a finger out runs on, thin, to where the fingertips are.
    // Which way the palm faces is not carried, so two fingers out are one thicker one.
    // ponytail: the line from the chest stands in for the forearm, which the globals do not carry.
    float3 ownL = YapsSlot(10), ownR = YapsSlot(14);
    int hands = _YAPS_SquashHands.z > 0 && _YAPS_SquashHands.y > 0 ? 2 * min((int) round(CVRGlobalParams1.y), 64) : 0;
    // A plug in the socket between the pair, anyone's, from the light it announces itself with:
    // its base and its length. The light gives no direction, so the shaft runs from the base
    // through the socket for the plug's length, and presses like another hand. Only while it
    // reaches the socket, so a plug merely near the pair parts nothing, easing out over the last
    // fifth of its length rather than letting go at once.
    // ponytail: the radius is a tenth of the length, since the light carries none; 6 cm at most
    // (3.5 cm drew a long thick plug thin). Real girth needs a channel the light does not have.
    int socket = 18 + (p.m >> 1);
    int plugs = _YAPS_SquashPlug > 0 && YapsUnit(socket) > 0 ? 4 : 0;
    float3 at = YapsSlot(socket);
    [loop]
    for (int s = 0; s < arms + hands + plugs; s++)
    {
        float3 a, b, fa = 0, fb = 0;
        float r, rf = 0, scale;
        bool finger = false;
        int into = 3;
        if (s < arms)
        {
            uint k = s;
            uint slot = 8 + (k / 3) * 4 + k % 3;
            if (YapsUnit(slot) <= 0 || YapsUnit(slot + 1) <= 0) continue;
            a = YapsSlot(slot);
            b = YapsSlot(slot + 1);
            r = YapsLimb(k);
            scale = _YAPS_SquashHands.x;
            into = k / 3;
        }
        else if (s < arms + hands)
        {
            int i = (s - arms) >> 1;
            float tall = _CVR_PlayerRootPositions[i].w;
            tall = tall > 0.1 ? tall : 1.6;
            r = _YAPS_SquashHands.y * tall / 1.6;
            float mine = 2.25 * r * r;
            float3 chest = _CVR_PlayerChestPositions[i].xyz;
            float4 hand = ((s - arms) & 1) == 0 ? _CVR_PlayerLeftHandPositions[i] : _CVR_PlayerRightHandPositions[i];
            float3 h = hand.xyz;
            float g = hand.w;
            // Most hands in a full room are nowhere near: out past the part's reach, a hand's
            // length and its radius, it cannot press. Saves the press for the ones that can.
            float near = 3 * p.reach + 0.11 * tall + r;
            if (dot(h - p.centre, h - p.centre) > near * near) continue;
            if (dot(h, h) < 1e-6) continue;
            // Eased out by either own hand rather than cut, since the globals and the slots
            // are read at different moments and a quick own hand would flicker in as another's.
            float own = saturate(min(dot(h - ownL, h - ownL), dot(h - ownR, h - ownR)) / mine - 1);
            if (own <= 0) continue;
            float3 arm = h - chest;
            float3 dir = dot(chest, chest) > 1e-6 && dot(arm, arm) > 1e-6 ? normalize(arm) : 0;
            float fist = g > 1.5 ? 1 : saturate(g);
            a = h;
            b = h + dir * max(lerp(0.11, 0.06, fist) * tall - r, 0);
            scale = _YAPS_SquashHands.z * own;
            finger = g > 2.5;
            rf = (g > 4.5 && g < 5.5 ? 0.35 : 0.2) * r;
            fa = h + dir * (0.06 * tall);
            fb = h + dir * max(0.11 * tall - rf, 0.06 * tall);
        }
        else
        {
            uint i = s - arms - hands;
            if (dot(unity_LightColor[i].rgb, unity_LightColor[i].rgb) > 0.0001) continue;
            int digit = YapsLightDigit(i);
            if (digit != 8 && digit != 9) continue;
            float len = unity_LightColor[i].a;
            float3 base = YapsLightPosition(i);
            float d = distance(at, base);
            if (len <= 0.0001 || d >= len || d <= 1e-4) continue;
            r = clamp(0.1 * len, 0.012, 0.06);
            a = base;
            b = base + (at - base) / d * len;
            scale = _YAPS_SquashPlug * saturate((len - d) / (0.2 * len));
        }
        float3 v = YapsPress(p, a, b, r) * scale;
        float vr = r;
        if (finger) YapsMerge(v, vr, YapsPress(p, fa, fb, rf) * scale, rf);
        // Into the slots others share, 2 and 3: the deepest press, and the rest merged into
        // the other. With two presses that is both as they are; a third eases into the
        // shallower instead of one dropping out whole as another overtook it.
        if (into == 3 && dot(v, v) > dot(hv[2], hv[2]))
        {
            float3 tv = hv[2];
            float tr = hr[2];
            hv[2] = v;
            hr[2] = vr;
            v = tv;
            vr = tr;
        }
        YapsMerge(hv[into], hr[into], v, vr);
    }
}

void YapsDentDeform(inout float3 position, inout float3 normal, inout float3 tangent, uint vertexId)
{
    if (_YAPS_DentPower <= 0) return;

    uint width = (uint) round(abs(1.0 / _YAPS_DentOwn_TexelSize.x));
    float4 own = _YAPS_DentOwn.Load(int3(vertexId % width, vertexId / width, 0));
    float partShare = saturate(own.r + own.g + own.b + own.a);
    if (partShare <= 0) return;
    float shares[4] = { own.r, own.g, own.b, own.a };
    yapsSkin = YapsSkinState();

    // A skinned mesh arrives in world space under an identity matrix, a plain
    // mesh in its own; going through the matrix serves both.
    float3 world = mul(unity_ObjectToWorld, float4(position, 1)).xyz;
    float3 n = mul(normal, (float3x3)unity_WorldToObject);
    bool hasNormal = dot(n, n) > 0;
    n = hasNormal ? normalize(n) : 0;
    float3 start = world;
    float3 startN = n;

    float3 squashAxis[4], squashCentre[4], partN[4];
    float squashPlane[4], squashReach[4], squashContact[4], squashDeep[4], squashBow[4], partDone[4];
    // Cleared before the loop, not in it: ChilloutVR's shader filter can end the loop early, and
    // the meet below read the parts it never reached from uninitialised registers.
    [unroll]
    for (int z = 0; z < 4; z++)
    {
        squashPlane[z] = 0;
        squashContact[z] = 0;
        squashDeep[z] = 0;
        squashAxis[z] = 0;
        squashCentre[z] = 0;
        squashReach[z] = 0;
        squashBow[z] = 0;
        partN[z] = 0;
        partDone[z] = 0;
    }
    // The filter adds each loop pass's instruction count to one counter and breaks every loop once
    // it passes the Loop Limit, which this loop's first pass always does. So the vertex's own part
    // goes first, and is the one that survives.
    int first = 0;
    [unroll]
    for (int f = 1; f < 4; f++) first = shares[f] > shares[first] ? f : first;
    float3 mapped = 0;
    float3 mappedN = n;
    // Looped, not unrolled, here and in the arm, hand and plug loops: four copies of this body took the
    // compiler minutes on every patched shader, and a user waits on that at the first build.
    [loop]
    for (int j = 0; j < 4; j++)
    {
        int m = (first + j) & 3;
        float unit = YapsUnit(m);
        if (shares[m] <= 0 || unit <= 0) continue;
        YapsPart p = YapsPartAt(m, YapsSlot(m), unit);
        float reach = p.reach;
        float3 d = world - p.centre;
        // Two copies of one avatar sharing a material would share each other's follows: a
        // vertex far from the part it belongs to is not this copy's, and stays.
        if (reach <= 0 || dot(d, d) > 16 * reach * reach) continue;

        float3 S = 0;
        float3x3 M = 0;
        float3 squeeze = 0;
        float strength = _YAPS_SquashTune.y;
        float3 toward = YapsSlot(m ^ 1) - p.centre;
        bool paired = YapsUnit(m ^ 1) > 0 && length(toward) > 1e-6;
        float3 axis = toward / max(length(toward), 1e-6);
        // Both parts in the first one's units, so each is pushed back to the same plane.
        float start = (m < 2 ? _YAPS_SquashPair.x : _YAPS_SquashPair.z) * YapsUnit(m & 2);
        // How far each reaches toward the other as turned by its swing.
        float frontS = reach, frontO = start - reach;
        if (paired)
        {
            frontS = YapsExtent(YapsTurned(p), axis);
            YapsPart po = YapsPartAt(m ^ 1, YapsSlot(m ^ 1), YapsUnit(m ^ 1));
            if (po.framed) frontO = YapsExtent(YapsTurned(po), -axis);
        }
        float over = frontS + frontO - length(toward);

        // The hands on this part, and on its partner when that could reach this one: of the
        // partner only how far its hands bend it this way is kept, so a hand pushing one part into
        // the other makes it the one that leads below, as a swing does. Seen from the pushed part
        // alone, the other was never pushed, and both pressed flat down the middle. One loop, so
        // the press and the dent compile once.
        // Each hand dents the part first, up to the deepest dent, easing in: soft tissue takes a
        // light touch where it lands. Bent away first, a touch swung the whole part. Past both
        // the dent and the moves below, the hand goes in and the capsules carry the skin.
        // Cleared before the loop, which the shader filter may cut short.
        float3 dentW[4] = { float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0) };
        float dentDepth[4] = { 0, 0, 0, 0 }, dentR[4] = { 0, 0, 0, 0 }, spare[4] = { 0, 0, 0, 0 };
        float3 F = 0;
        float bendSelf = 0, bendOther = 0;
        [loop]
        for (int who = 0; who < 2; who++)
        {
            YapsPart pq = p;
            if (who == 1)
            {
                // A bend never goes past its give, so a partner further off than that cannot meet.
                float giveO = _YAPS_SquashTune.z * _YAPS_SquashOut[m ^ 1] * YapsUnit(m ^ 1);
                if (!paired || over + bendSelf + giveO <= 0) break;
                pq = YapsPartAt(m ^ 1, YapsSlot(m ^ 1), YapsUnit(m ^ 1));
                if (!pq.framed) break;
            }
            float3 hv[4] = { float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0) };
            float hr[4] = { 0, 0, 0, 0 };
            YapsHandsPress(pq, hv, hr);
            float3 Fq = 0;
            [unroll]
            for (int h = 0; h < 4; h++)
            {
                if (who == 0) dentR[h] = hr[h];
                float in0 = length(hv[h]) * strength;
                if (in0 <= 1e-6) continue;
                float3 w = hv[h] / length(hv[h]);
                float deepest = _YAPS_SquashTune.w * YapsExtent(pq, w);
                float local = deepest > 0 ? deepest * (1 - exp(-in0 / deepest)) : 0;
                Fq += w * (in0 - local);
                if (who == 0)
                {
                    dentW[h] = w;
                    dentDepth[h] = local;
                    spare[h] = in0 - local;
                }
            }
            // How far the hands alone bend it toward the other, as the bend below does.
            float3 sideQ = Fq - pq.o * dot(Fq, pq.o);
            float slQ = length(sideQ);
            float giveQ = _YAPS_SquashTune.z * _YAPS_SquashOut[pq.m] * pq.unit;
            float byQ = pq.framed && slQ > 1e-6 && giveQ > 0 ? giveQ * (1 - exp(-slQ / giveQ)) : 0;
            float bentIn = paired && slQ > 1e-6 ? max(-dot(sideQ, who == 0 ? axis : -axis) / slQ * byQ, 0) : 0;
            if (who == 0)
            {
                F = Fq;
                bendSelf = bentIn;
            }
            else bendOther = bentIn;
        }

        if (paired)
        {
            float first = m < 2 ? _YAPS_SquashPair.y : _YAPS_SquashPair.w;
            float share = (m & 1) == 0 ? first : 1 - first;
            // The overlap, the hands' bends with it, is pressed in where the two touch and never
            // moves either part away: swung or shunted apart, the skin between them, held by the
            // body, stretched into a tube. The part driven further keeps its round front and the
            // other takes a hollow its shape; driven alike, each gives its share. Read as the
            // difference of the two swings, as the follows settle well off their edit-mode rests
            // in play, inward too.
            float meet = over + bendSelf + bendOther;
            float self = bendSelf, other = bendOther;
            if (YapsUnit(4 + m) > 0 && YapsUnit(4 + (m ^ 1)) > 0)
            {
                self += dot(p.centre - YapsSlot(4 + m), axis);
                other += dot(YapsSlot(4 + (m ^ 1)) - YapsSlot(m ^ 1), axis);
            }
            float lead = clamp((other - self) / max(abs(other) + abs(self), 1e-6), -1, 1);
            float takes = lead > 0 ? lerp(share, 1, lead) : share * (1 + lead);
            squashBow[m] = lead;
            squashDeep[m] = max(meet, 0) * takes;
            // Test builds show where the slots came from by where the pulse plays: both pairs
            // for the skinned buffer, the first pair alone when it reads empty, the second when
            // it reads wrong. It squeezes from the outside, as a share of the start, so the pair
            // is seen to swing in and meet; flattened where it stood, it read as no contact.
            bool shown = yapsSkin <= 1 || (yapsSkin == 2) == (m < 2);
            float pulse = shown ? _YAPS_SquashPulse * start * (0.5 - 0.5 * cos(_Time.y * 2)) : 0;
            squeeze = -axis * pulse * strength;
            squashAxis[m] = axis;
            squashCentre[m] = p.centre;
            squashReach[m] = reach;
            // Signed, so a pair apart meets midway across the gap.
            squashPlane[m] = dot(axis, p.centre) + frontS + bendSelf - meet * takes;
            // Eased in over the last of the gap: switched on at the touch, the skin by the seam
            // jumped back onto it.
            squashContact[m] = takes > 0 && _YAPS_SquashShape.w > 0 ? saturate(1 + meet / (YAPS_SQUASH_SOFT * reach)) : 0;
        }
        // Pushed off its rest by anything its cloth collides with: pressed from the side the
        // push came from. Downward is left out, or the sag the cloth settles into would read
        // as a press from above all the time. Off by default: every swing of the cloth read
        // as a press too.
        if (YapsUnit(4 + m) > 0 && _YAPS_SquashPush.x > 0)
        {
            float3 push = p.centre - YapsSlot(4 + m);
            push.y = max(push.y, 0);
            float moved = length(push);
            float press = max(moved - _YAPS_SquashPush.y * reach, 0) * _YAPS_SquashPush.x;
            if (press > 0) YapsAdd(S, M, -push / moved * press * strength);
        }
        // Lying back, its weight presses the part into the body from the front.
        if (p.framed && _YAPS_SquashTune.x > 0)
            YapsAdd(S, M, p.o * (_YAPS_SquashTune.x * smoothstep(0.3, 1, p.o.y) * YapsExtent(p, p.o) * strength));
        F += squeeze;
        // What the dents leave, from one side, bends the part away, swinging about where it
        // meets the body, and each hand is that much less far in. Squeezed from both sides it
        // has nowhere to bend. The rest moves the whole part.
        float3 bend = 0, bendDir = 0;
        float bendBy = 0, bendFrom = 0, bendSpan = 1;
        if (p.framed && _YAPS_SquashTune.z > 0)
        {
            float3 side = F - p.o * dot(F, p.o);
            float sl = length(side);
            float outE = _YAPS_SquashOut[m] * unit;
            float give = _YAPS_SquashTune.z * outE;
            if (sl > 1e-6 && give > 0)
            {
                bendBy = give * (1 - exp(-sl / give));
                bendDir = side / sl;
                bend = -bendDir * bendBy;
                bendFrom = _YAPS_SquashIn[m] * unit;
                bendSpan = bendFrom + outE;
            }
        }
        [unroll]
        for (int hl = 0; hl < 4; hl++)
            YapsAdd(S, M, dentW[hl] * max(spare[hl] - bendBy * max(dot(dentW[hl], bendDir), 0), 0));
        float sq = length(squeeze);
        if (sq > 0) YapsAdd(S, M, squeeze * max(1 - bendBy * max(dot(squeeze, bendDir), 0) / (sq * sq), 0));
        // Bent toward its partner further than its hands took it, by the test pulse, it meets it
        // there too: without the contact the two passed through each other. Eased in with the
        // bend, as the contact moves skin that is near it, not only past.
        if (squashReach[m] > 0 && _YAPS_SquashShape.w > 0)
        {
            squashContact[m] = max(squashContact[m], saturate(dot(bend, squashAxis[m]) / (YAPS_SQUASH_SOFT * reach)));
            squashDeep[m] += max(dot(bend, squashAxis[m]) - bendSelf, 0);
        }

        // The squeeze lines, solved exactly: M's largest eigenvalue in closed form, its line as
        // the longest cross of two rows of M less that, then the two across it in their plane, so
        // the three stay square. Flattened along the strongest alone, two presses at an angle
        // swung the whole flattening over as one overtook the other. Iterated, a line was only as
        // good as its start: the second grew from rounding noise and threw the part a hand's
        // width; the first, from the sum, never turned up while presses from either side
        // cancelled in it, and from the longest column it jumped when another grew longer with
        // two squeezes nearly level. Each put the whole part on a wrong line for a frame. Which
        // way a line points never matters: flattened along its reverse, front and back swap.
        float3 ax[3];
        float3x3 I3 = float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
        float mean = (M[0][0] + M[1][1] + M[2][2]) / 3;
        float3 dg = float3(M[0][0], M[1][1], M[2][2]) - mean;
        float spread = sqrt((dot(dg, dg) + 2 * (M[0][1] * M[0][1] + M[0][2] * M[0][2] + M[1][2] * M[1][2])) / 6);
        float c0 = dot(M[0], M[0]), c1 = dot(M[1], M[1]), c2 = dot(M[2], M[2]);
        float3 x = c0 >= c1 && c0 >= c2 ? M[0] : (c1 >= c2 ? M[1] : M[2]);
        if (spread > 1e-9)
        {
            float top = mean + 2 * spread * cos(acos(clamp(determinant((M - mean * I3) / spread) / 2, -1, 1)) / 3);
            float3x3 A = M - top * I3;
            float3 x01 = cross(A[0], A[1]), x02 = cross(A[0], A[2]), x12 = cross(A[1], A[2]);
            float d01 = dot(x01, x01), d02 = dot(x02, x02), d12 = dot(x12, x12);
            float3 c = d01 >= d02 && d01 >= d12 ? x01 : (d02 >= d12 ? x02 : x12);
            // Two strongest exactly level leave A no line: any in their plane will do.
            if (dot(c, c) > 1e-10 * spread * spread * spread * spread) x = c;
        }
        if (dot(x, x) < 1e-20) x = float3(0, 1, 0);
        x = normalize(x);
        ax[0] = x;
        float3 e1 = normalize(cross(x, abs(x.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0)));
        float3 e2 = cross(x, e1);
        float m11 = dot(e1, mul(M, e1)), m22 = dot(e2, mul(M, e2)), m12 = dot(e1, mul(M, e2));
        float2 twice = float2(m11 - m22, 2 * m12);
        float turn = dot(twice, twice) > 1e-20 ? 0.5 * atan2(twice.y, twice.x) : 0;
        ax[1] = e1 * cos(turn) + e2 * sin(turn);
        ax[2] = cross(ax[0], ax[1]);

        // Along each line, each side moved in by the press from that side: exact when every
        // press lies on a line, a squeeze putting half on each side. Grown along the others by
        // just enough to keep the volume. Grown faster, the old overfill made a squashed part a
        // third bigger than it started.
        // Two squeezes nearly level leave their lines free to swing round each other in a few
        // frames, and each line read its own width of the part, so the flattening swung with
        // them. Toward level, the pair shares one strength and one width, which no turn of the
        // lines within their plane can change; each keeps how far off centre the part reaches.
        float flat[3], grow[3], shift[3], along[3];
        float onL[3], wide[3], off[3];
        [unroll]
        for (int b0 = 0; b0 < 3; b0++)
        {
            onL[b0] = dot(ax[b0], mul(M, ax[b0]));
            float rF = YapsExtent(p, ax[b0]), rB = YapsExtent(p, -ax[b0]);
            wide[b0] = rF + rB;
            off[b0] = 0.5 * (rF - rB);
        }
        float u01 = 1 - saturate((onL[0] - onL[1]) / (YAPS_SQUASH_LEVEL * max(onL[0], 1e-9)));
        float u12 = 1 - saturate((onL[1] - onL[2]) / (YAPS_SQUASH_LEVEL * max(onL[1], 1e-9)));
        float2 o01 = lerp(float2(onL[0], onL[1]), 0.5 * (onL[0] + onL[1]), u01);
        float2 w01 = lerp(float2(wide[0], wide[1]), 0.5 * (wide[0] + wide[1]), u01);
        onL[0] = o01.x; onL[1] = o01.y; wide[0] = w01.x; wide[1] = w01.y;
        float2 o12 = lerp(float2(onL[1], onL[2]), 0.5 * (onL[1] + onL[2]), u12);
        float2 w12 = lerp(float2(wide[1], wide[2]), 0.5 * (wide[1] + wide[2]), u12);
        onL[1] = o12.x; onL[2] = o12.y; wide[1] = w12.x; wide[2] = w12.y;
        float pressAll = 0;
        [unroll]
        for (int b = 0; b < 3; b++)
        {
            float onLine = onL[b], net = dot(S, ax[b]);   // both 0 with no press
            float pFront = max(0.5 * (onLine + net), 0);
            float pBack = max(0.5 * (onLine - net), 0);
            float rFront = 0.5 * wide[b] + off[b], rBack = 0.5 * wide[b] - off[b];
            float pressed = pFront + pBack;
            flat[b] = 1;
            grow[b] = 1;
            shift[b] = 0;
            if (pressed > 0)
            {
                flat[b] = max(1 - pressed / (rFront + rBack), _YAPS_SquashShape.x);
                pFront *= (1 - flat[b]) * (rFront + rBack) / pressed;   // below 1 only at the flattest
                grow[b] = min(pow(max(flat[b], 1e-3), -0.5 * _YAPS_SquashShape.y), _YAPS_SquashShape.z);
                shift[b] = rFront * (1 - flat[b]) - pFront;
            }
            pressAll += pressed;
        }
        along[0] = flat[0] * min(grow[1] * grow[2], _YAPS_SquashShape.z);
        along[1] = flat[1] * min(grow[0] * grow[2], _YAPS_SquashShape.z);
        along[2] = flat[2] * min(grow[0] * grow[1], _YAPS_SquashShape.z);
        bool dented = dentDepth[0] + dentDepth[1] + dentDepth[2] + dentDepth[3] > 0;

        // In motion: the physics swing the part off its rest, and the flesh stretches away from
        // where it hangs and squashes back toward it, keeping its volume. A bone chain only
        // swings it rigid. The swing is how far the follow is off the rest, which for a spring
        // is the push the motion gives it, so it rings as long as the physics do. Sideways
        // swings bend it and are the physics' own. About the side it hangs from, which stays.
        // ponytail: small swings stand in for the sag the physics settle into, which the edit
        // mode rest never saw; a slow average of the swing needs memory an avatar has not got.
        float stretch = 1;
        float3 stretchW = 0, hangAt = 0;
        float3 hang = _YAPS_SquashFollow[4 + m]._m01_m11_m21;
        if (_YAPS_SquashMotion.x > 0 && YapsUnit(4 + m) > 0 && dot(hang, hang) > 1e-10)
        {
            hang = normalize(hang);
            float3 swing = p.centre - YapsSlot(4 + m);
            float sl = length(swing);
            float off = sl - _YAPS_SquashMotion.y * reach;
            if (off > 0)
            {
                stretchW = swing / sl;
                float by = _YAPS_SquashMotion.x * off / reach * -dot(stretchW, hang);
                stretch = clamp(1 + by, 1 / _YAPS_SquashMotion.z, _YAPS_SquashMotion.z);
                hangAt = hang * YapsExtent(p, hang);
            }
        }
        if (pressAll <= 0 && dot(bend, bend) <= 0 && !dented && stretch == 1) continue;
        float3 nmS = n;
        if (stretch != 1)
        {
            float thin = rsqrt(stretch);
            float3 r0 = d - hangAt;
            float along = dot(r0, stretchW);
            d = hangAt + stretchW * along * stretch + (r0 - stretchW * along) * thin;
            if (hasNormal) nmS = normalize(n + stretchW * dot(stretchW, n) * (thin / stretch - 1));
        }

        // The bend leans the squashed shape, so it keeps the volume the squash kept.
        float3 squashed = 0;
        [unroll]
        for (int c3 = 0; c3 < 3; c3++) squashed += ax[c3] * (dot(d, ax[c3]) * along[c3] + shift[c3]);
        float lean = saturate((dot(squashed, p.o) + bendFrom) / bendSpan);
        float3 moved = p.centre + squashed + bend * lean;
        float3 nm = nmS;
        if (hasNormal)
        {
            if (pressAll > 0)
                nm = normalize(ax[0] * (dot(ax[0], nm) / along[0]) + ax[1] * (dot(ax[1], nm) / along[1]) + ax[2] * (dot(ax[2], nm) / along[2]));
            if (lean > 0 && lean < 1) nm = normalize(nm - p.o * dot(bend / bendSpan, nm));
        }
        // Each dent, centred where the hand meets the part at rest. Inside, the skin lies on the
        // hand: a bowl as deep as the dent and as wide as a ball of the hand's size pressed that
        // deep, (1 - t) of the depth at t = (distance / width)^2. Round its edge what the bowl
        // moved rises into a shoulder, (1 - t) o^2 with o falling from 1 to 0, a third of the
        // depth high at 1.35 widths and back to rest by 1.86: tissue does not compress, and the
        // old low rim spread the same volume out to three widths, too thin to see, so the dent
        // read as lost volume. Both move along the line from the part's centre, which keeps the
        // volume exactly on a ball; moved along the hand's line instead, the rim slid round the
        // sides unseen and a deep dent lost 6%. Never narrower than one and a half depths, or
        // the skin could fold.
        [unroll]
        for (int e = 0; e < 4; e++)
        {
            float depth = dentDepth[e];
            if (depth <= 0) continue;
            float3 w = dentW[e];
            float wide = max(sqrt(2 * dentR[e] * min(depth, dentR[e])), 1.5 * depth);
            float reachW = YapsExtent(p, w);
            float3 q = d - w * reachW;
            float t = dot(q, q) / (wide * wide);
            if (t >= 1 + YAPS_DENT_SPAN) continue;
            float o = saturate((1 + YAPS_DENT_SPAN - t) / YAPS_DENT_SPAN);
            float sink = depth * (1 - t) * o * o / reachW;
            float slope = t > 1 ? -o * (o + 2 * (1 - t) / YAPS_DENT_SPAN) : -1;
            moved -= d * sink;
            if (hasNormal)
            {
                float3 g = q * (2 * depth * slope / (wide * wide * reachW));
                nm = normalize(nm + g * dot(d, nm) / max(1 - sink - dot(g, d), 0.1));
            }
        }
        mapped += (moved - world) * shares[m];
        partN[m] = nm;
        partDone[m] = 1;
    }
    // Blended in part order whichever part went first: each lerp leans on the last, so a vertex
    // two parts share took a different normal when the order changed.
    [unroll]
    for (int o = 0; o < 4; o++)
        if (hasNormal && partDone[o] > 0) mappedN = normalize(lerp(mappedN, partN[o], shares[o]));
    world += mapped;
    n = mappedN;

    // Where the pair still overlaps, each side is pressed against the seam where the two meet,
    // stopping just short of it, deeper layers closer behind. The seam is a ball bowed round the
    // leader's front, so the part that led stays round and the other takes a hollow; pressed flat
    // to a plane whichever moved, the patch showed past the partner as a dark fold. Letting a
    // fifth through as the other surfaces do, both sides crossed into each other lying nearly
    // flat, where two meshes can draw as a jagged seam. Short of the seam the press fades out
    // over a width that grows with the overlap, so the patch rounds into the part as flesh does
    // where it is pressed: at v before the stop it goes to KEEP v + (1 - KEEP) v (1 - e^(v / width)),
    // level with the seam at the patch's edge and never folding. What is pushed back swells out
    // round the patch.
    [unroll]
    for (int c = 0; c < 4; c++)
    {
        if (squashContact[c] <= 0) continue;
        float3 facing = squashAxis[c];
        float bow = squashBow[c];
        float stop = YAPS_SQUASH_MEET * squashReach[c];
        float width = max(YAPS_SQUASH_ROUND * squashDeep[c], stop);
        // The seam bows by the lead round the leader's front, from its six reaches: toward this
        // part when the other leads, away when this one does, flat when they lead alike. The
        // skin is pressed back along the line. A ball of the reach toward the partner was twice
        // the front's real reach out of the body, and the hollow cut the still part open where
        // the leader was not; pressed back along the ball's radius, its skin wrapped round over
        // the leader's front as a lip.
        float3 q = world - squashCentre[c];
        float along = dot(q, facing);
        float3 side = q - facing * along;
        // How far the leader's front falls back along the line at this distance off it: where
        // the line through here leaves the leader's turned shape on the side facing the other.
        int lc = bow > 0 ? c ^ 1 : c;
        YapsPart L = YapsTurned(YapsPartAt(lc, YapsSlot(lc), YapsUnit(lc)));
        float3 w = bow > 0 ? -facing : facing;
        float pole = YapsExtent(L, w);
        float3 S = L.framed ? float3(dot(side, L.a), dot(side, L.o), dot(side, L.u)) : float3(length(side), 0, 0);
        float3 Wd = L.framed ? float3(dot(w, L.a), dot(w, L.o), dot(w, L.u)) : float3(0, 1, 0);
        float3 P0 = S + Wd * pole;
        float3 Wt = L.framed ? (P0 >= 0 ? L.front : L.back) / (L.unit * L.unit) : 1 / (L.reach * L.reach);
        float qa = dot(Wt, Wd * Wd), qb = dot(Wt, S * Wd), qc = dot(Wt, S * S) - 1;
        float disc = qb * qb - qa * qc;
        // Past the leader's outline the seam carries on away from the part pressed, so the hollow
        // stays whole at any lead. Curving back on the leader's own side, it dragged the skin
        // past the fitted shape back into a point.
        float leave = (-qb + (bow > 0 ? sign(disc) : 1) * sqrt(abs(disc))) / max(qa, 1e-12);
        float v = along - (squashPlane[c] - dot(squashCentre[c], facing) + bow * (pole - leave)) + stop;
        // Leading, it gives only where it is inside the other, as far as it leads: pressed all
        // over its front, its edge creased where nothing touched it. Driven alike, both go to the
        // seam, as the other's fitted shape misses some of its skin and the two tore apart there.
        if (bow < 0) v = lerp(v, min(v, YapsInside(YapsTurned(YapsPartAt(c ^ 1, YapsSlot(c ^ 1), YapsUnit(c ^ 1))), world, -facing)), -bow);
        if (v <= -10 * width) continue;
        // The seam's own facing there, which the pressed skin turns to: the leader's front, as
        // much as it leads.
        float3 X = (S + Wd * leave) * Wt;
        float3 nL = L.framed ? L.a * X.x + L.o * X.y + L.u * X.z : facing;
        nL *= dot(nL, facing) >= 0 ? 1 : -1;
        float3 seamN = normalize(facing * (1 - abs(bow)) + normalize(nL) * abs(bow));
        float to, slope;
        if (v < 0)
        {
            float e = exp(v / width);
            to = YAPS_DENT_KEEP * v + (1 - YAPS_DENT_KEEP) * v * (1 - e);
            slope = YAPS_DENT_KEEP + (1 - YAPS_DENT_KEEP) * (1 - e * (1 + v / width));
        }
        else
        {
            // Past the stop, the slope carries on from KEEP and dies away, never past the seam.
            float room = 0.95 * stop;
            float e = exp(-v * YAPS_DENT_KEEP / room);
            to = room * (1 - e);
            slope = YAPS_DENT_KEEP * e;
        }
        float held = shares[c] * squashContact[c];
        float back = (v - to) * held;
        // What the press pushes back swells out round the patch, as a marshmallow pressed on
        // glass: the patch spreads and the side swells most at the seam, level there and dying
        // away over a width. Falling away from the seam itself, the two sides met in a ridge
        // along the bottom of a pair pressed hard together. The swell holds the cap the overlap
        // pushed back, as deep as the press across a patch of radius sqrt(2 reach deep); its
        // profile spans two widths. A rim half that size a width out read as two parts cut flat
        // down a seam.
        float deep = squashDeep[c];
        float ring = sqrt(max(2 * squashReach[c] * deep, 1e-12));
        float behind = max(-v, 0) / width;
        float swell = _YAPS_DentSpill * squashReach[c] * deep * deep / (4 * ring * width) * held * (1 + behind) * exp(-behind);
        float3 away = side / max(length(side), ring);
        world += away * swell - facing * back;
        if (hasNormal)
        {
            n = normalize(n + seamN * dot(n, seamN) * (1 / max(1 - held * (1 - slope), 1e-3) - 1));
            // Swelling more toward the seam leans the side back from it.
            if (v < 0) n = normalize(n - facing * (swell * behind / ((1 + behind) * width)) * dot(away, n));
        }
    }

    // The own arms and hands, a capsule per bone from one joint to the next. Skin inside is
    // eased out along the line from the bone. A hand meets no resistance and passes through a
    // body, so the move tops out smoothly at half the radius: following it all the way
    // scooped a tunnel through the part. Near the bone it lets go, since skin either side of
    // it went opposite ways and the bone sweeping past flicked it across.
    if (_YAPS_SquashPush.w > 0 && _YAPS_SquashHands.x > 0)
    {
        [loop]
        for (uint k = 0; k < 6; k++)
        {
            uint a = 8 + (k / 3) * 4 + k % 3;
            if (YapsUnit(a) <= 0 || YapsUnit(a + 1) <= 0) continue;
            float3 pa = YapsSlot(a);
            float3 ab = YapsSlot(a + 1) - pa;
            float len2 = dot(ab, ab);
            float r = YapsLimb(k);
            if (len2 < 1e-10 || r <= 0) continue;
            float3 d = world - (pa + ab * saturate(dot(world - pa, ab) / len2));
            float dist = length(d);
            float soft = max(_YAPS_SquashPush.z * r, 1e-5);
            if (dist >= r + soft || dist < 1e-6) continue;
            float slope;
            float back = YapsEase(r - dist, soft, slope);
            float cap = 0.5 * r;
            float held = exp(-back / cap);
            float near = saturate(dist / cap);
            back = cap * (1 - held) * partShare * near;
            if (back <= 0) continue;
            float3 away = d / dist;
            world += away * back;
            if (hasNormal)
                n = normalize(lerp(n, normalize(n + away * dot(n, away) * (1 / (1 - held * (1 - slope)) - 1)), partShare * near));
        }
    }

    if (all(world == start)) return;
    position = mul(unity_WorldToObject, float4(world, 1)).xyz;
    // Zero stays zero: a host with no normal gets none invented here.
    if (hasNormal && any(n != startN))
    {
        normal = normalize(mul(n, (float3x3)unity_ObjectToWorld));
        if (dot(tangent, tangent) > 0)
            tangent = normalize(tangent - normal * dot(normal, tangent));
    }
}

#endif
