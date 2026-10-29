// YAPS, the LIVE TISSUE (the jelly). Lattices of particles in a render texture that the avatar's
// own blitters step every frame, one region a part: each region swings on its root as one piece,
// its particles pulled toward their neighbourhood's rigid fit and toward their home on that swing,
// out of every player's hands. A vertex of a part a region carries moves as the lattice round it
// has moved off its rest, a function of where it is alone, so every layer at a point (skin, a bra,
// a top) takes the same move.
//
// Where a region is not running the part keeps its rest shape: far off, in the editor, or for a
// viewer whose client runs no blitters (the Cameras content filter), whose state is never written.
//
// The state, as Dev/Probes/JellySpikeBuilder.cs writes it: a block of rows a region, each rows of
// 64 particles, each where it is in the left half (w the _Time.y of the last pass), then a side
// row: the hands in reach in texels 0 to 15, the far switch in 18 (resting, switched off too, in its y), the anchor the last pass stepped
// on in 26 to 29 (its x, y and z axes with its scale, then its place, w 1), the swing's centre in 30,
// which of the wearer's own capsules are in reach in 41 (a bit each) and their ends in 42 to 57 as
// the hands' slots are, the avatar's colliders pressing likewise in 64 to 79, the plane between the pair in 58 (its normal toward the other, its offset)
// and 59 (its give, 0 for none).
#ifndef YAPS_JELLY_INCLUDED
#define YAPS_JELLY_INCLUDED
#ifndef YAPS_NO_JELLY

float4 YapsJellyTexel(uint p, uint base) { return _YAPS_JellyState.Load(int3(p % 64, p / 64 + base, 0)); }

// A kept hand from its slot's texels a (wrist, radius) and b (an open hand's tip, 10 plus the
// gesture), as the squash reads one (yaps_dent.cginc): the palm from a to pb, ending at the
// knuckles as it closes to a fist, and a finger out, thin, from fa to fb of radius rf, 0 for none.
void YapsJellyHand(float4 a, float4 b, out float3 pb, out float3 fa, out float3 fb, out float rf)
{
    float3 seg = b.xyz - a.xyz;
    float len = length(seg), g = b.w - 10, r = a.w;
    pb = a.xyz;
    fa = fb = a.xyz;
    rf = 0;
    if (len < 1e-6) return;
    float3 dir = seg / len;
    float tall = (len + r) / 0.11, fist = g > 1.5 ? 1 : saturate(g);
    pb = a.xyz + dir * max(lerp(0.11, 0.06, fist) * tall - r, 0);
    rf = g > 2.5 ? (g > 4.5 && g < 5.5 ? 0.35 : 0.2) * r : 0;
    fa = a.xyz + dir * (0.06 * tall);
    fb = a.xyz + dir * max(0.11 * tall - rf, 0.06 * tall);
}

// How far a capsule from a to b of radius r pushes skin at at on, toward centre: out of it, and
// with rim the crater's rim eased out to twice its radius, further the nearer the axis.
float3 YapsJellyPress(float3 at, float3 a, float3 b, float r, float3 centre, float rim)
{
    float3 seg = b - a, in_ = centre - (a + 0.5 * seg);
    if (!(dot(in_, in_) > 1e-8) || !(r > 0)) return 0;
    float3 way = normalize(in_);
    float3 off = at - (a + seg * saturate(dot(at - a, seg) / max(dot(seg, seg), 1e-9)));
    float d2 = dot(off, off), along = dot(off, way);
    float ease = saturate(1 - sqrt(d2) / (2 * r));
    float through = d2 < r * r ? sqrt(max(along * along - d2 + r * r, 0)) - along : 0;
    return way * max(through, rim * r * ease * ease);
}

// How far region r's lattice round world has moved off its rest, and in bend how the surface there
// turns: to first order, the move's gradient taken through the normal nw. Past its sides, the edge's:
// the part's share already eases skin out, and eased out here too, skin with a share but outside
// the box stayed behind a swing and tore. 0 where the region is not running.
float3 YapsJellyRegion(uint r, float3 world, float3 nw, out float3 bend)
{
    bend = 0;
    int3 n = int3(_YAPS_JellyLattice.xyz);
    // Padded to a whole number of eight with the side row (JellySpikeBuilder's Side).
    uint rows = ((uint) (n.x * n.y * n.z + 63) / 64 + 8) / 8 * 8 - 1;
    uint base = r * (rows + 1), side = base + rows;
    float4 place = _YAPS_JellyState.Load(int3(29, side, 0));
    // Far, where the parser switches the steps off, it keeps its rest shape; resting, switched off
    // too, the state as it lies, any dent in it included, however long ago it was stepped.
    float2 off = _YAPS_JellyState.Load(int3(18, side, 0)).xy;
    if (!(place.w > 0.5) || off.x > 0.5 || !(off.y > 0.5 || abs(_Time.y - YapsJellyTexel(0, base).w) < 0.25)) return 0;
    float3 ax = _YAPS_JellyState.Load(int3(26, side, 0)).xyz;
    float3 ay = _YAPS_JellyState.Load(int3(27, side, 0)).xyz;
    float3 az = _YAPS_JellyState.Load(int3(28, side, 0)).xyz;
    float spacing = _YAPS_JellyLattice.w;
    // The anchor's axes carry its scale, so one over the scale squared takes a world offset into
    // its own units.
    float per = 1 / max(dot(ax, ax) * spacing, 1e-12);
    // Where the vertex sits on the lattice, in particles.
    float3 rel = world - place.xyz;
    float3 c = float3(dot(rel, ax), dot(rel, ay), dot(rel, az)) * per + float3(0.5 * (n.x - 1), 0.5 * (n.y - 1), 0);
    c = clamp(c, 0, n - 1);
    int3 c0 = min(int3(c), n - 2);
    float3 t = c - c0;
    float3 move = 0, gx = 0, gy = 0, gz = 0;
    [unroll] for (int k = 0; k < 8; k++)
    {
        int3 d = int3(k & 1, (k >> 1) & 1, k >> 2);
        int3 l = c0 + d;
        float3 rest = float3(l.x - 0.5 * (n.x - 1), l.y - 0.5 * (n.y - 1), l.z) * spacing;
        float3 home = place.xyz + ax * rest.x + ay * rest.y + az * rest.z;
        float3 off = YapsJellyTexel((uint) ((l.z * n.y + l.y) * n.x + l.x), base).xyz - home;
        float3 w = d ? t : 1 - t;
        float3 way = d ? 1 : -1;
        move += w.x * w.y * w.z * off;
        gx += way.x * w.y * w.z * off;
        gy += way.y * w.x * w.z * off;
        gz += way.z * w.x * w.y * off;
    }
    bend = per * (ax * dot(gx, nw) + ay * dot(gy, nw) + az * dot(gz, nw));
    // The hands in reach push the skin itself on too, the palm and any finger out: the particles sit
    // a few centimetres apart, so a finger between them would sink into the skin. The way the hand
    // presses, toward the region's centre, as the particles go: out of its nearest side, skin went
    // round a hand pushed in deep. Eased, not snapped onto the radius, so layers keep their order:
    // snapped, skin and a bra met on one surface and the skin came through. Slots fill in order, so
    // an empty first one means there are none.
    [branch] if (_YAPS_JellyState.Load(int3(1, side, 0)).w > 0.5)
    {
        float3 at = world + move;
        float4 mass = _YAPS_JellyState.Load(int3(30, side, 0));
        float3 centre = mass.w > 0 ? mass.xyz : place.xyz + az * (0.5 * (n.z - 1) * spacing);
        [unroll] for (int e = 0; e < 8; e++)
        {
            float4 a = _YAPS_JellyState.Load(int3(2 * e, side, 0)), b = _YAPS_JellyState.Load(int3(2 * e + 1, side, 0));
            if (!(b.w > 0.5)) continue;
            float3 pb, fa, fb;
            float rf;
            YapsJellyHand(a, b, pb, fa, fb, rf);
            move += YapsJellyPress(at, a.xyz, pb, a.w, centre, 1);
            if (rf > 0) move += YapsJellyPress(at, fa, fb, rf, centre, 1);
        }
    }
    // The wearer's own arms, hands and thighs, with no rim: an arm resting at the side would dent
    // it from a hand's width off.
    uint own = (uint) _YAPS_JellyState.Load(int3(41, side, 0)).x;
    [branch] if (own != 0)
    {
        float3 at = world + move;
        float4 mass = _YAPS_JellyState.Load(int3(30, side, 0));
        float3 centre = mass.w > 0 ? mass.xyz : place.xyz + az * (0.5 * (n.z - 1) * spacing);
        [unroll] for (uint c = 0; c < 16; c++)
        {
            [branch] if (own & (1u << c))
            {
                uint t = c < 8 ? 42 + 2 * c : 48 + 2 * c;
                float4 a = _YAPS_JellyState.Load(int3(t, side, 0)), b = _YAPS_JellyState.Load(int3(t + 1, side, 0));
                move += YapsJellyPress(at, a.xyz, b.xyz, a.w, centre, 0);
            }
        }
    }
    // Last, skin past the plane between the pair, further than they overlapped as built, eased back
    // to within its give of it, the deeper the further, so layers keep their order: the lattice keeps
    // the two apart a few centimetres coarse, and here they cannot cross. By give's y, how much the
    // pair still meet there: a limb turned across the other lies past it with nothing to meet.
    float4 give = _YAPS_JellyState.Load(int3(59, side, 0));
    [branch] if (give.x > 0)
    {
        float4 plane = _YAPS_JellyState.Load(int3(58, side, 0));
        float x = dot(world + move, plane.xyz) - plane.w;
        if (x > 0) move -= plane.xyz * ((x - give.x * (1 - exp(-x / give.x))) * give.y);
    }
    return move;
}

void YapsJellyDeform(inout float3 position, inout float3 normal, inout float3 tangent, uint vertexId)
{
    if (!(_YAPS_JellyLattice.x > 0)) return;
    uint width = (uint) round(abs(1.0 / _YAPS_DentOwn_TexelSize.x));
    float4 own = _YAPS_DentOwn.Load(int3(vertexId % width, vertexId / width, 0));
    own /= max(own.r + own.g + own.b + own.a, 1);
    // Each part a region carries names it, one past its index.
    if (!any(own * _YAPS_JellyParts > 0)) return;

    float3 world = mul(unity_ObjectToWorld, float4(position, 1)).xyz;
    float3 n0 = mul(normal, (float3x3)unity_WorldToObject);
    bool hasNormal = dot(n0, n0) > 0;
    float3 nw = hasNormal ? normalize(n0) : 0;
    float3 move = 0, bend = 0;
    // Skin between two parts takes each one's move by its share.
    [unroll] for (int k = 0; k < 4; k++)
    {
        [branch] if (own[k] > 0 && _YAPS_JellyParts[k] > 0)
        {
            float3 b;
            move += own[k] * YapsJellyRegion((uint) round(_YAPS_JellyParts[k]) - 1, world, nw, b);
            bend += own[k] * b;
        }
    }
    world += move;

    position = mul(unity_WorldToObject, float4(world, 1)).xyz;
    // Zero stays zero: a host with no normal gets none invented here.
    if (hasNormal)
    {
        normal = normalize(mul(normalize(nw - bend), (float3x3)unity_ObjectToWorld));
        if (dot(tangent, tangent) > 0)
            tangent = normalize(tangent - normal * dot(normal, tangent));
    }
}

#endif
#endif
