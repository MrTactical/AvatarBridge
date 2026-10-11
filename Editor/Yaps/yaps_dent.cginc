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
// between them; by whatever pushed its bone off its rest, from the side the push
// came from; and by hands reaching into it, the own arms and every other
// player's, from their side. All of it adds into one press. The part flattens
// along it and grows across, its facing side moving back by the press and its
// far side staying. Then whatever still crosses the plane where the pair meets
// is eased back onto it, and the own arms and hands, as capsules, ease the skin
// out of their way.
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

float _YAPS_DentPower;
float _YAPS_DentSpill;
Texture2D _YAPS_DentOwn;       // per vertex: its share of parts 0 to 3 in r, g, b, a
float4 _YAPS_DentOwn_TexelSize;
// Slots, as the provider writes them. One with nothing in it holds a node scaled to zero,
// which reads as absent.
//   0-3   each part's follow: a plain child of the bone the cloth moves most, at the part's centre
//   4-7   each part's rest: the same point on the bone above the chain, which the cloth never moves
//   8-11  left upper arm, lower arm, hand, middle finger; 12-15 the same on the right
// Parts 0 and 1 are a pair, 2 and 3 another.
float4x4 _YAPS_SquashFollow[16];
float4 _YAPS_SquashReach;   // how far each part reaches toward its partner, in its follow's units
float4 _YAPS_SquashPair;    // pair 0: x where pressing starts (follow gap, part 0's units), y part 0's share; pair 1: z, w
float4 _YAPS_SquashShape;   // x flattest, y overfill, z fullest, w contact plane on
float4 _YAPS_SquashPush;    // x push gain, y push ignored below this share of the reach, z capsule edge as a share of its radius, w arms on
float4 _YAPS_SquashLimbL;   // radius of the upper arm, lower arm and hand, each as a share of its own length
float4 _YAPS_SquashLimbR;
float _YAPS_SquashPulse;    // test builds: a press that comes and goes on its own, as a share of the start
float4 _YAPS_SquashHands;   // x own arms' press gain, y other players' hand radius in metres, z their press gain, w skin stride in bytes (0 none)
float4 _CVR_PlayerLeftHandPositions[255];
float4 _CVR_PlayerRightHandPositions[255];

// The same slots again, from a helper mesh weighted one vertex to each and skinned. The provider
// reads transforms in a script LateUpdate, before the cloth writes its bones and after it put
// them back to rest, so a follow read there never moves. Skinning runs after the cloth.
#if SHADER_TARGET >= 45
ByteAddressBuffer _YAPS_SquashSkin;
#endif

float3 YapsSlot(int i)
{
#if SHADER_TARGET >= 45
    if (_YAPS_SquashHands.w > 0) return asfloat(_YAPS_SquashSkin.Load3(i * (uint) _YAPS_SquashHands.w));
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

// A capsule from a to b of radius r reaching into a part: the press toward it, by how far.
float3 YapsPress(float3 centre, float reach, float3 a, float3 b, float r)
{
    float3 ab = b - a;
    float len2 = dot(ab, ab);
    float3 to = a + ab * saturate(dot(centre - a, ab) / max(len2, 1e-10)) - centre;
    float dist = length(to);
    float press = reach + r - dist;
    return press > 0 && dist > 1e-6 ? to / dist * press : 0;
}

// Hands press the whole part, so it flattens under them. Only carving the skin out of their
// way left the hand sunk in a groove.
float3 YapsHandsPress(float3 centre, float reach)
{
    float3 pressing = 0;
    if (_YAPS_SquashPush.w > 0 && _YAPS_SquashHands.x > 0)
    {
        // Per arm, its deepest bone: upper arm, forearm and hand overlap at the joints, and
        // adding all three pressed one forearm in twice.
        float3 arm[2] = { float3(0, 0, 0), float3(0, 0, 0) };
        [unroll]
        for (uint k = 0; k < 6; k++)
        {
            uint a = 8 + (k / 3) * 4 + k % 3;
            if (YapsUnit(a) <= 0 || YapsUnit(a + 1) <= 0) continue;
            float3 pa = YapsSlot(a), pb = YapsSlot(a + 1);
            float r = (k < 3 ? _YAPS_SquashLimbL : _YAPS_SquashLimbR)[k % 3] * length(pb - pa);
            float3 p = YapsPress(centre, reach, pa, pb, r);
            if (dot(p, p) > dot(arm[k / 3], arm[k / 3])) arm[k / 3] = p;
        }
        pressing += (arm[0] + arm[1]) * _YAPS_SquashHands.x;
    }
    float r = _YAPS_SquashHands.y;
    if (_YAPS_SquashHands.z > 0 && r > 0)
    {
        // Every player's hands, the wearer's among them, which the arms above already are: a
        // hand by either own hand is skipped. Capped at 64 players for cost.
        float3 ownL = YapsSlot(10), ownR = YapsSlot(14);
        float mine = 2.25 * r * r;
        int count = min((int) round(CVRGlobalParams1.y), 64);
        [loop]
        for (int i = 0; i < count; i++)
        {
            float3 hands[2] = { _CVR_PlayerLeftHandPositions[i].xyz, _CVR_PlayerRightHandPositions[i].xyz };
            [unroll]
            for (int j = 0; j < 2; j++)
            {
                float3 h = hands[j];
                if (dot(h, h) < 1e-6 || dot(h - ownL, h - ownL) < mine || dot(h - ownR, h - ownR) < mine) continue;
                pressing += YapsPress(centre, reach, h, h, r) * _YAPS_SquashHands.z;
            }
        }
    }
    return pressing;
}

void YapsDentDeform(inout float3 position, inout float3 normal, inout float3 tangent, uint vertexId)
{
    if (_YAPS_DentPower <= 0) return;

    uint width = (uint) round(abs(1.0 / _YAPS_DentOwn_TexelSize.x));
    float4 own = _YAPS_DentOwn.Load(int3(vertexId % width, vertexId / width, 0));
    float partShare = saturate(own.r + own.g + own.b + own.a);
    if (partShare <= 0) return;
    float shares[4] = { own.r, own.g, own.b, own.a };

    // A skinned mesh arrives in world space under an identity matrix, a plain
    // mesh in its own; going through the matrix serves both.
    float3 world = mul(unity_ObjectToWorld, float4(position, 1)).xyz;
    float3 n = mul(normal, (float3x3)unity_WorldToObject);
    bool hasNormal = dot(n, n) > 0;
    n = hasNormal ? normalize(n) : 0;
    float3 start = world;
    float3 startN = n;

    float3 squashAxis[4], squashCentre[4];
    float squashPlane[4], squashReach[4];
    bool squashContact[4];
    float3 mapped = 0;
    float3 mappedN = n;
    [unroll]
    for (int m = 0; m < 4; m++)
    {
        squashPlane[m] = 0;
        squashContact[m] = false;
        squashAxis[m] = 0;
        squashCentre[m] = 0;
        squashReach[m] = 0;
        float unit = YapsUnit(m);
        if (shares[m] <= 0 || unit <= 0) continue;
        float3 centre = YapsSlot(m);
        float reach = _YAPS_SquashReach[m] * unit;
        float3 d = world - centre;
        // Two copies of one avatar sharing a material would share each other's follows: a
        // vertex far from the part it belongs to is not this copy's, and stays.
        if (reach <= 0 || dot(d, d) > 16 * reach * reach) continue;

        float3 pressing = 0;
        float3 toward = YapsSlot(m ^ 1) - centre;
        if (YapsUnit(m ^ 1) > 0 && length(toward) > 1e-6)
        {
            float3 axis = normalize(toward);
            float first = m < 2 ? _YAPS_SquashPair.y : _YAPS_SquashPair.w;
            // Both parts in the first one's units, so each is pushed back to the same plane.
            float start = (m < 2 ? _YAPS_SquashPair.x : _YAPS_SquashPair.z) * YapsUnit(m & 2);
            float pulse = _YAPS_SquashPulse * start * (0.5 - 0.5 * cos(_Time.y * 2));
            float press = (max(start - length(toward), 0) + pulse) * ((m & 1) == 0 ? first : 1 - first);
            pressing = axis * press;
            squashAxis[m] = axis;
            squashCentre[m] = centre;
            squashReach[m] = reach;
            squashPlane[m] = dot(axis, centre) + reach - press;
            squashContact[m] = press > 0 && _YAPS_SquashShape.w > 0;
        }
        // Pushed off its rest by anything its cloth collides with: pressed from the side the
        // push came from. Downward is left out, or the sag the cloth settles into would read
        // as a press from above all the time.
        if (YapsUnit(4 + m) > 0)
        {
            float3 push = centre - YapsSlot(4 + m);
            push.y = max(push.y, 0);
            float moved = length(push);
            float press = max(moved - _YAPS_SquashPush.y * reach, 0) * _YAPS_SquashPush.x;
            if (press > 0) pressing -= push / moved * press;
        }
        pressing += YapsHandsPress(centre, reach);

        float press = length(pressing);
        if (press <= 0) continue;
        float3 axis = pressing / press;
        float along = clamp(1 - press / (2 * reach), _YAPS_SquashShape.x, 1);
        float across = min(1 + _YAPS_SquashShape.y * (1 - along), _YAPS_SquashShape.z);
        float t = dot(d, axis);
        float3 moved = centre + across * d + (along - across) * axis * t - axis * reach * (1 - along);
        mapped += (moved - world) * shares[m];
        if (hasNormal)
            mappedN = normalize(lerp(mappedN, normalize(n + axis * dot(axis, n) * (across / along - 1)), shares[m]));
    }
    world += mapped;
    n = mappedN;

    // The map flattens the whole part, so the pair still overlapped where it reached past its
    // reach. What crosses the plane where the two meet is eased back onto it, and what is
    // pushed back spills away from the line between them.
    [unroll]
    for (int c = 0; c < 4; c++)
    {
        if (!squashContact[c]) continue;
        float3 facing = squashAxis[c];
        float slope;
        float back = YapsEase(dot(world, facing) - squashPlane[c], YAPS_SQUASH_SOFT * squashReach[c], slope) * shares[c];
        if (back <= 0) continue;
        float3 q = world - squashCentre[c];
        float3 away = q - facing * dot(q, facing);
        away /= max(length(away), 0.5 * squashReach[c]);
        world += (away * _YAPS_DentSpill - facing) * back;
        if (hasNormal)
            n = normalize(lerp(n, normalize(n + facing * dot(n, facing) * (1 / slope - 1)), shares[c]));
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
