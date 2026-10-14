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
// its volume. Then whatever still crosses the plane where the pair meets is
// eased back onto it, and the own arms and hands, as capsules, ease the skin out
// of their way.
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
// How far before the contact plane the squash starts to give, as a share of the reach.
#define YAPS_SQUASH_SOFT 0.1
#define YAPS_DENT_SPAN 2.4494897   // the dent's shoulder, in t; root 6 makes it hold what the bowl moved

float _YAPS_DentPower;
float _YAPS_DentSpill;
Texture2D _YAPS_DentOwn;       // per vertex: its share of parts 0 to 3 in r, g, b, a
float4 _YAPS_DentOwn_TexelSize;
// Slots, as the provider writes them. One with nothing in it holds a node scaled to zero,
// which reads as absent.
//   0-3   each part's follow: a plain child of the bone the cloth moves most, at the part's centre
//   4-7   each part's rest: the same point on the bone above the chain, which the cloth never moves
//   8-11  left upper arm, lower arm, hand, middle finger; 12-15 the same on the right
//   16-17 each pair's back: a node on the rest bone behind the pair, so the pair's middle
//         minus it points out of the body, level when the avatar stands
// Parts 0 and 1 are a pair, 2 and 3 another.
float4x4 _YAPS_SquashFollow[18];
// Reaches per part, in its follow's units: toward the partner, away from it, out of the body,
// into it, and along cross(toward, out) either way.
float4 _YAPS_SquashReach;
float4 _YAPS_SquashAway;
float4 _YAPS_SquashOut;
float4 _YAPS_SquashIn;
float4 _YAPS_SquashSideA;
float4 _YAPS_SquashSideB;
float4 _YAPS_SquashPair;    // pair 0: x where pressing starts (follow gap, part 0's units), y part 0's share; pair 1: z, w
float4 _YAPS_SquashShape;   // x flattest, y volume kept (1 all, below 1 lets some go), z widest, w contact plane on
float4 _YAPS_SquashPush;    // x push gain, y push ignored below this share of the reach, z capsule edge as a share of its radius, w arms on
float4 _YAPS_SquashLimbL;   // radius of the upper arm, lower arm and hand, each as a share of its own length
float4 _YAPS_SquashLimbR;
float _YAPS_SquashPulse;    // test builds: a squeeze from the outside that comes and goes on its own, as a share of the start
float4 _YAPS_SquashHands;   // x own arms' press gain, y other players' hand radius in metres at a 1.6 m viewpoint, z their press gain, w skin stride in bytes (0 none)
float4 _YAPS_SquashTune;    // x lying-back weight, as a share of the reach out; y strength of every press; z bend give, as a share of the reach out; w deepest dent, as a share of the reach toward the hand
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
    float3 s = asfloat(_YAPS_SquashSkin.Load3(4 * (uint) _YAPS_SquashHands.w));
    if (all(s == 0)) return 2;
    // The rest slot rides a bone no cloth moves, so the two agree but for a frame of motion.
    float3 d = s - _YAPS_SquashFollow[4]._m03_m13_m23;
    return dot(d, d) < 0.25 ? 1 : 3;
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
    float x = dot(w, p.a), y = dot(w, p.o), z = dot(w, p.u);
    float ex = x >= 0 ? _YAPS_SquashReach[p.m] : _YAPS_SquashAway[p.m];
    float ey = y >= 0 ? _YAPS_SquashOut[p.m] : _YAPS_SquashIn[p.m];
    float ez = z >= 0 ? _YAPS_SquashSideA[p.m] : _YAPS_SquashSideB[p.m];
    return p.unit * rsqrt(max(x * x / (ex * ex) + y * y / (ey * ey) + z * z / (ez * ez), 1e-8));
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
float3 YapsPress(YapsPart p, float3 a, float3 b, float r)
{
    float3 ab = b - a;
    float len2 = dot(ab, ab);
    float3 to = a + ab * saturate(dot(p.centre - a, ab) / max(len2, 1e-10)) - p.centre;
    float dist = length(to);
    if (dist <= 1e-6) return 0;
    float press = YapsExtent(p, to / dist) + r - dist;
    return press > 0 ? to / dist * press : 0;
}

// The hands reaching into the part, as presses toward each hand by how far it is in, with its
// radius: both own arms first, then the two deepest other hands.
// ponytail: a third hand from others is dropped; keep more slots if crowds touch at once.
void YapsHandsPress(YapsPart p, inout float3 hv[4], inout float hr[4])
{
    if (_YAPS_SquashPush.w > 0 && _YAPS_SquashHands.x > 0)
    {
        // Per arm, its deepest bone: upper arm, forearm and hand overlap at the joints, and
        // adding all three pressed one forearm in twice.
        [unroll]
        for (uint k = 0; k < 6; k++)
        {
            uint a = 8 + (k / 3) * 4 + k % 3;
            if (YapsUnit(a) <= 0 || YapsUnit(a + 1) <= 0) continue;
            float3 pa = YapsSlot(a), pb = YapsSlot(a + 1);
            float r = (k < 3 ? _YAPS_SquashLimbL : _YAPS_SquashLimbR)[k % 3] * length(pb - pa);
            float3 v = YapsPress(p, pa, pb, r) * _YAPS_SquashHands.x;
            if (dot(v, v) > dot(hv[k / 3], hv[k / 3])) { hv[k / 3] = v; hr[k / 3] = r; }
        }
    }
    if (_YAPS_SquashHands.z > 0 && _YAPS_SquashHands.y > 0)
    {
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
        int count = min((int) round(CVRGlobalParams1.y), 64);
        [loop]
        for (int i = 0; i < count; i++)
        {
            float tall = _CVR_PlayerRootPositions[i].w;
            tall = tall > 0.1 ? tall : 1.6;
            float r = _YAPS_SquashHands.y * tall / 1.6;
            float mine = 2.25 * r * r;
            float3 chest = _CVR_PlayerChestPositions[i].xyz;
            float4 hands[2] = { _CVR_PlayerLeftHandPositions[i], _CVR_PlayerRightHandPositions[i] };
            [unroll]
            for (int j = 0; j < 2; j++)
            {
                float3 h = hands[j].xyz;
                float g = hands[j].w;
                if (dot(h, h) < 1e-6 || dot(h - ownL, h - ownL) < mine || dot(h - ownR, h - ownR) < mine) continue;
                float3 arm = h - chest;
                float3 dir = dot(chest, chest) > 1e-6 && dot(arm, arm) > 1e-6 ? normalize(arm) : 0;
                float fist = g > 1.5 ? 1 : saturate(g);
                float3 v = YapsPress(p, h, h + dir * max(lerp(0.11, 0.06, fist) * tall - r, 0), r) * _YAPS_SquashHands.z;
                float vr = r;
                if (g > 2.5)
                {
                    float rf = (g > 4.5 && g < 5.5 ? 0.35 : 0.2) * r;
                    float3 f = YapsPress(p, h + dir * (0.06 * tall), h + dir * max(0.11 * tall - rf, 0.06 * tall), rf) * _YAPS_SquashHands.z;
                    if (dot(f, f) > dot(v, v)) { v = f; vr = rf; }
                }
                float vv = dot(v, v);
                if (vv > dot(hv[2], hv[2])) { hv[3] = hv[2]; hr[3] = hr[2]; hv[2] = v; hr[2] = vr; }
                else if (vv > dot(hv[3], hv[3])) { hv[3] = v; hr[3] = vr; }
            }
        }
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

    float3 squashAxis[4], squashCentre[4];
    float squashPlane[4], squashReach[4], squashContact[4];
    float3 mapped = 0;
    float3 mappedN = n;
    [unroll]
    for (int m = 0; m < 4; m++)
    {
        squashPlane[m] = 0;
        squashContact[m] = 0;
        squashAxis[m] = 0;
        squashCentre[m] = 0;
        squashReach[m] = 0;
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
        if (YapsUnit(m ^ 1) > 0 && length(toward) > 1e-6)
        {
            float3 axis = normalize(toward);
            float first = m < 2 ? _YAPS_SquashPair.y : _YAPS_SquashPair.w;
            // Both parts in the first one's units, so each is pushed back to the same plane.
            float start = (m < 2 ? _YAPS_SquashPair.x : _YAPS_SquashPair.z) * YapsUnit(m & 2);
            float share = (m & 1) == 0 ? first : 1 - first;
            float over = start - length(toward);
            float press = max(over, 0) * share;
            YapsAdd(S, M, axis * press * strength);
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
            squashPlane[m] = dot(axis, p.centre) + reach - over * share;
            squashContact[m] = press > 0 && _YAPS_SquashShape.w > 0 ? 1 : 0;
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
        float3 hv[4] = { float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0) };
        float hr[4] = { 0, 0, 0, 0 };
        YapsHandsPress(p, hv, hr);

        // Each hand dents the part first, up to the deepest dent, easing in: soft tissue takes a
        // light touch where it lands. Bent away first, a touch swung the whole part. Past both
        // the dent and the moves below, the hand goes in and the capsules carry the skin.
        float3 dentW[4];
        float dentDepth[4], dentR[4], spare[4];
        float3 F = 0;
        [unroll]
        for (int h = 0; h < 4; h++)
        {
            dentW[h] = 0;
            dentDepth[h] = 0;
            dentR[h] = hr[h];
            spare[h] = 0;
            float in0 = length(hv[h]) * strength;
            if (in0 <= 1e-6) continue;
            float3 w = hv[h] / length(hv[h]);
            float deepest = _YAPS_SquashTune.w * YapsExtent(p, w);
            float local = deepest > 0 ? deepest * (1 - exp(-in0 / deepest)) : 0;
            dentW[h] = w;
            dentDepth[h] = local;
            spare[h] = in0 - local;
            F += w * spare[h];
        }
        F += squeeze;
        // What the dents leave, from one side, bends the part away, swinging about where it
        // meets the body, and each hand is that much less far in. Squeezed from both sides it
        // has nowhere to bend. Its partner never bends it: the pair's gap is where the cloth put
        // it. The rest moves the whole part.
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
        // Bent toward its partner, it meets it: without the plane the two passed through each
        // other. Eased in with the bend, as the plane moves skin that is near it, not only past.
        if (squashReach[m] > 0 && _YAPS_SquashShape.w > 0)
            squashContact[m] = max(squashContact[m], saturate(dot(bend, squashAxis[m]) / (YAPS_SQUASH_SOFT * reach)));

        float total = M[0][0] + M[1][1] + M[2][2];
        // The squeeze line, M's strongest direction, found starting from the sum.
        float3 x = total > 1e-8 ? S : float3(0, 1, 0);
        if (total > 1e-8 && dot(x, x) < 1e-6 * total * total)
            x = M[0][0] >= M[1][1] && M[0][0] >= M[2][2] ? M[0] : (M[1][1] >= M[2][2] ? M[1] : M[2]);
        x = normalize(x);
        [unroll]
        for (int it = 0; it < 4; it++)
        {
            float3 y = mul(M, x);
            if (dot(y, y) > 1e-16) x = normalize(y);
        }
        // Exact when every press lies on that line: a squeeze puts half on each side.
        float onLine = dot(x, mul(M, x)), net = dot(S, x);   // both 0 with no press
        float pFront = max(0.5 * (onLine + net), 0);
        float pBack = max(0.5 * (onLine - net), 0);

        // Flattened along x, each side moved in by the press from that side, and grown across
        // by just enough to keep the volume. Grown faster, the old overfill made a squashed
        // part a third bigger than it started.
        float rFront = YapsExtent(p, x), rBack = YapsExtent(p, -x);
        float pressAll = pFront + pBack;
        float k = 1, across = 1, shift = 0;
        if (pressAll > 0)
        {
            k = max(1 - pressAll / (rFront + rBack), _YAPS_SquashShape.x);
            float kept = (1 - k) * (rFront + rBack) / pressAll;   // below 1 only at the flattest
            pFront *= kept;
            pBack *= kept;
            across = min(pow(max(k, 1e-3), -0.5 * _YAPS_SquashShape.y), _YAPS_SquashShape.z);
            shift = rFront * (1 - k) - pFront;
        }
        bool dented = dentDepth[0] + dentDepth[1] + dentDepth[2] + dentDepth[3] > 0;
        if (pressAll <= 0 && dot(bend, bend) <= 0 && !dented) continue;

        // The bend leans the squashed shape, so it keeps the volume the squash kept.
        float3 squashed = across * d + (k - across) * x * dot(d, x) + x * shift;
        float lean = saturate((dot(squashed, p.o) + bendFrom) / bendSpan);
        float3 moved = p.centre + squashed + bend * lean;
        float3 nm = n;
        if (hasNormal)
        {
            if (pressAll > 0) nm = normalize(nm + x * dot(x, nm) * (across / k - 1));
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
        if (hasNormal) mappedN = normalize(lerp(mappedN, nm, shares[m]));
    }
    world += mapped;
    n = mappedN;

    // The map flattens the whole part, so the pair still overlapped where it reached past its
    // reach. What crosses the plane where the two meet is eased back onto it, and what is
    // pushed back spills away from the line between them.
    [unroll]
    for (int c = 0; c < 4; c++)
    {
        if (squashContact[c] <= 0) continue;
        float3 facing = squashAxis[c];
        float slope;
        float held = shares[c] * squashContact[c];
        float back = YapsEase(dot(world, facing) - squashPlane[c], YAPS_SQUASH_SOFT * squashReach[c], slope) * held;
        if (back <= 0) continue;
        float3 q = world - squashCentre[c];
        float3 away = q - facing * dot(q, facing);
        away /= max(length(away), 0.5 * squashReach[c]);
        world += (away * _YAPS_DentSpill - facing) * back;
        if (hasNormal)
            n = normalize(lerp(n, normalize(n + facing * dot(n, facing) * (1 / slope - 1)), held));
    }

    // The own arms and hands, a capsule per bone from one joint to the next. Skin inside is
    // eased out along the line from the bone. A hand meets no resistance and passes through a
    // body, so the move tops out smoothly at half the radius: following it all the way
    // scooped a tunnel through the part.
    if (_YAPS_SquashPush.w > 0)
    {
        [unroll]
        for (uint k = 0; k < 6; k++)
        {
            uint a = 8 + (k / 3) * 4 + k % 3;
            if (YapsUnit(a) <= 0 || YapsUnit(a + 1) <= 0) continue;
            float3 pa = YapsSlot(a);
            float3 ab = YapsSlot(a + 1) - pa;
            float len2 = dot(ab, ab);
            float r = (k < 3 ? _YAPS_SquashLimbL : _YAPS_SquashLimbR)[k % 3] * sqrt(len2);
            if (len2 < 1e-10 || r <= 0) continue;
            float3 d = world - (pa + ab * saturate(dot(world - pa, ab) / len2));
            float dist = length(d);
            float soft = max(_YAPS_SquashPush.z * r, 1e-5);
            if (dist >= r + soft || dist < 1e-6) continue;
            float slope;
            float back = YapsEase(r - dist, soft, slope);
            float cap = 0.5 * r;
            float held = exp(-back / cap);
            back = cap * (1 - held) * partShare;
            if (back <= 0) continue;
            float3 away = d / dist;
            world += away * back;
            if (hasNormal)
                n = normalize(lerp(n, normalize(n + away * dot(n, away) * (1 / (1 - held * (1 - slope)) - 1)), partShare));
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
