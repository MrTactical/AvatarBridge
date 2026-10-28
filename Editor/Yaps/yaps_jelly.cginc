// YAPS, the LIVE TISSUE (the jelly). A lattice of particles in a render texture that the avatar's
// own blitters step every frame: each particle pulled toward its neighbourhood's rigid fit, the
// whole toward its rest on the chest, out of every player's hands. A vertex of a part the lattice
// carries moves as the lattice round it has moved off its rest, a function of where it is alone,
// so every layer at a point (skin, a bra, a top) takes the same move.
//
// Where the lattice is not running the part keeps its rest shape: far off, in the editor, or for a
// viewer whose client runs no blitters (the Cameras content filter), whose state is never written.
//
// The state, as Dev/Probes/JellySpikeBuilder.cs writes it: rows of 64 particles, each where it is
// in the left half (w the _Time.y of the last pass), then a side row: the hands in reach in texels
// 0 to 15 (wrist with the radius, then the tip, w 1 if kept), the far switch in 18, the anchor the
// last pass stepped on in 26 to 29 (its x, y and z axes with its scale, then its place, w 1).
#ifndef YAPS_JELLY_INCLUDED
#define YAPS_JELLY_INCLUDED
#ifndef YAPS_NO_JELLY

float4 YapsJellyTexel(uint p) { return _YAPS_JellyState.Load(int3(p % 64, p / 64, 0)); }

void YapsJellyDeform(inout float3 position, inout float3 normal, inout float3 tangent, uint vertexId)
{
    if (!(_YAPS_JellyLattice.x > 0)) return;
    uint width = (uint) round(abs(1.0 / _YAPS_DentOwn_TexelSize.x));
    float4 own = _YAPS_DentOwn.Load(int3(vertexId % width, vertexId / width, 0));
    own /= max(own.r + own.g + own.b + own.a, 1);
    float share = saturate(dot(own, saturate(_YAPS_JellyParts)));
    if (share <= 0) return;

    int3 n = int3(_YAPS_JellyLattice.xyz);
    uint side = (uint) (n.x * n.y * n.z + 63) / 64;
    float4 place = _YAPS_JellyState.Load(int3(29, side, 0));
    float far = _YAPS_JellyState.Load(int3(18, side, 0)).x;
    if (!(place.w > 0.5) || far > 0.5 || !(abs(_Time.y - YapsJellyTexel(0).w) < 0.25)) return;
    float3 ax = _YAPS_JellyState.Load(int3(26, side, 0)).xyz;
    float3 ay = _YAPS_JellyState.Load(int3(27, side, 0)).xyz;
    float3 az = _YAPS_JellyState.Load(int3(28, side, 0)).xyz;
    float spacing = _YAPS_JellyLattice.w;
    // The anchor's axes carry its scale, so one over the scale squared takes a world offset into
    // its own units.
    float per = 1 / max(dot(ax, ax) * spacing, 1e-12);

    float3 world = mul(unity_ObjectToWorld, float4(position, 1)).xyz;
    float3 n0 = mul(normal, (float3x3)unity_WorldToObject);
    bool hasNormal = dot(n0, n0) > 0;
    // Where the vertex sits on the lattice, in particles.
    float3 rel = world - place.xyz;
    float3 c = float3(dot(rel, ax), dot(rel, ay), dot(rel, az)) * per + float3(0.5 * (n.x - 1), 0.5 * (n.y - 1), 0);
    // Eased out over a particle's spacing past the sides: clamped alone, everything beyond them
    // would move with the edge.
    float3 outside = max(max(-c, c - (n - 1)), 0);
    float amount = share * saturate(1 - max(outside.x, max(outside.y, outside.z)));
    if (amount <= 0) return;
    c = clamp(c, 0, n - 1);
    int3 c0 = min(int3(c), n - 2);
    float3 t = c - c0;
    // The eight particles round it, each by how far it is off its rest, and how that changes along
    // each of the lattice's axes, for the normal.
    float3 move = 0, gx = 0, gy = 0, gz = 0;
    [unroll] for (int k = 0; k < 8; k++)
    {
        int3 d = int3(k & 1, (k >> 1) & 1, k >> 2);
        int3 l = c0 + d;
        float3 rest = float3(l.x - 0.5 * (n.x - 1), l.y - 0.5 * (n.y - 1), l.z) * spacing;
        float3 home = place.xyz + ax * rest.x + ay * rest.y + az * rest.z;
        float3 off = YapsJellyTexel((uint) ((l.z * n.y + l.y) * n.x + l.x)).xyz - home;
        float3 w = d ? t : 1 - t;
        float3 way = d ? 1 : -1;
        move += w.x * w.y * w.z * off;
        gx += way.x * w.y * w.z * off;
        gy += way.y * w.x * w.z * off;
        gz += way.z * w.x * w.y * off;
    }
    world += move * amount;
    float3 nw = hasNormal ? normalize(n0) : 0;
    // The surface turns as the lattice round it shears: to first order, less the move's gradient
    // taken through the normal.
    if (hasNormal)
        nw = normalize(nw - amount * per * (ax * dot(gx, nw) + ay * dot(gy, nw) + az * dot(gz, nw)));

    // The hands in reach push the skin itself out too: the particles sit a few centimetres apart, so
    // a finger between them would sink into the skin. Out to twice the radius, never past it, and
    // further the nearer the axis, so layers keep their order: snapped onto the radius, skin and a
    // bra met on one surface and the skin came through. Slots fill in order, so an empty first one
    // means there are none.
    [branch] if (_YAPS_JellyState.Load(int3(1, side, 0)).w > 0.5)
    {
        [unroll] for (int e = 0; e < 8; e++)
        {
            float4 a = _YAPS_JellyState.Load(int3(2 * e, side, 0)), b = _YAPS_JellyState.Load(int3(2 * e + 1, side, 0));
            if (!(b.w > 0.5)) continue;
            float3 seg = b.xyz - a.xyz;
            float3 closest = a.xyz + seg * saturate(dot(world - a.xyz, seg) / max(dot(seg, seg), 1e-9));
            float3 off = world - closest;
            float dist = length(off);
            float ease = saturate(1 - dist / (2 * a.w));
            if (dist > 1e-6) world += off * (a.w * ease * ease * share / dist);
        }
    }

    position = mul(unity_WorldToObject, float4(world, 1)).xyz;
    // Zero stays zero: a host with no normal gets none invented here.
    if (hasNormal)
    {
        normal = normalize(mul(nw, (float3x3)unity_ObjectToWorld));
        if (dot(tangent, tangent) > 0)
            tangent = normalize(tangent - normal * dot(normal, tangent));
    }
}

#endif
#endif
