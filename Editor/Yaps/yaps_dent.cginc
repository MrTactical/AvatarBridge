// YAPS, the SOFT-BODY DENT. Flesh keeps its volume: pressed flat one way,
// it grows the others. Two layers do that here.
//
// A soft PART (a breast, a cheek) pressed by its partner gives only on the side
// facing it: that side is pushed back to where the two meet, flattening
// against its partner, and each slice pushed back grows across by one over the
// root of how much it was compressed, so the flesh spills out round the seam
// and the volume stays. The far side never moves. Carving the overlap away
// left two crescents, a part visibly losing volume; squashing the whole part
// toward its centre pulled its far side in and the pair still slid through
// each other. How far each part reaches and how much it gives are measured
// from its own vertices on the C# side; the shader only applies it.
//
// Which part a vertex is comes from the mesh's own skin weights, baked per
// vertex: its weight to a part's bones is its share of that part. Guessed from
// position and facing instead, neighbours either side of a guess went
// different ways and tore the skin; skin weights fade into the chest the way
// the avatar was painted, so the squash does too.
//
// A pressing SPHERE (a hand) dents locally: skin inside moves out toward its
// surface and turns to face the presser, and the displaced flesh rises in a
// ring round it. Only so deep: a hand in VR meets no resistance and passes
// through a body, and a dent that followed it scooped a cup out of the part.
// The dent's edge is a smooth max, never a hard clamp, which leaves a crease
// where the dent meets the skin.
//
// All world space. How they arrive is still open: the dev probe sets them.

#ifndef YAPS_DENT_INCLUDED
#define YAPS_DENT_INCLUDED

// How much of a layer's depth below a press survives it, so stacked layers stay stacked.
#define YAPS_DENT_KEEP 0.2
// How deep a dent may go, as a share of the presser's radius.
#define YAPS_DENT_DEPTH 0.25

float _YAPS_DentPower;
float _YAPS_DentSoftness;
float _YAPS_DentBulge;
float _YAPS_DentSpill;
float4 _YAPS_DentSpheres[4];   // xyz centre, w radius; 0 is an empty slot
Texture2D _YAPS_DentOwn;       // per vertex: its share of parts 0 to 3 in r, g, b, a
float4 _YAPS_DentOwn_TexelSize;
float4 _YAPS_DentPart[4];      // xyz the part's centre, w how far it reaches toward its partner
float4 _YAPS_DentAxis[4];      // xyz toward its partner, w how far it is pushed back (0 none)

void YapsDentDeform(inout float3 position, inout float3 normal, inout float3 tangent, uint vertexId)
{
    if (_YAPS_DentPower <= 0) return;

    // A skinned mesh arrives in world space under an identity matrix, a plain
    // mesh in its own; going through the matrix serves both.
    float3 world = mul(unity_ObjectToWorld, float4(position, 1)).xyz;
    float3 n = mul(normal, (float3x3)unity_WorldToObject);
    bool hasNormal = dot(n, n) > 0;
    n = hasNormal ? normalize(n) : 0;
    float3 start = world;
    float3 startN = n;

    uint width = (uint) round(abs(1.0 / _YAPS_DentOwn_TexelSize.x));
    float4 own = _YAPS_DentOwn.Load(int3(vertexId % width, vertexId / width, 0));
    float shares[4] = { own.r, own.g, own.b, own.a };
    float3 squash = 0;
    [unroll]
    for (int p = 0; p < 4; p++)
    {
        float reach = _YAPS_DentPart[p].w;
        float press = min(_YAPS_DentAxis[p].w * _YAPS_DentPower, 0.5 * reach);
        if (shares[p] <= 0 || press <= 0) continue;
        float3 axis = _YAPS_DentAxis[p].xyz;
        float3 q = world - _YAPS_DentPart[p].xyz;
        float t = dot(q, axis);
        // The band that gives starts four presses back from the surface: the slice at the
        // surface keeps a quarter of its depth, so it grows at most double across.
        float span = min(4 * press, 2 * reach);
        float u = (t - (reach - span)) / span;
        if (u <= 0) continue;
        // How much a slice keeps of its depth eases from all of it at the band's start, so
        // the band joins the untouched flesh without a crease.
        float give = 3 * press / span;
        float keep = u < 1 ? 1 - give * u * u : 1 - give;
        float moved = u < 1 ? reach - span + span * (u - give * u * u * u / 3) : reach - press + (t - reach) * keep;
        // The spill: the skin round the seam rises along its own sideways facing, at most a
        // press or so, so the face itself (facing the partner) stays flat. Grown outward from
        // the line between the parts instead, a broad inner face sat far from that line and
        // was thrown two or three times its distance.
        float ease = saturate(u);
        float spill = _YAPS_DentSpill * press * ease * ease * (3 - 2 * ease);
        float na = dot(n, axis);
        float3 side = n - axis * na;
        squash += (axis * (moved - t) + side * spill) * shares[p];
        if (hasNormal)
        {
            // Pressed thinner along the axis, the skin faces more along it; a spill that rises
            // toward the seam faces back away from it.
            float rate = u < 1 ? _YAPS_DentSpill * press * 6 * u * (1 - u) / span : 0;
            float3 bent = side + axis * (na / keep - rate * dot(side, side));
            n = normalize(lerp(n, normalize(bent), shares[p]));
        }
    }
    world += squash;

    float3 offset = 0;
    float3 facing = 0;
    float turn = 0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float r = _YAPS_DentSpheres[i].w;
        if (r <= 0) continue;
        float3 c = _YAPS_DentSpheres[i].xyz;
        float3 d = world - c;
        float dist = length(d);
        if (dist > 2 * r) continue;
        float3 away = dist > 1e-5 ? d / dist : -n;
        float k = max(r * _YAPS_DentSoftness, 1e-5);
        // Short of the surface by a share of the depth, never onto it: clothing and the skin
        // under it both landed on the sphere, at one depth, and fought in stripes. Kept, the
        // share keeps their order.
        // Past the cap the dent stops deepening and the press passes through it.
        float cap = YAPS_DENT_DEPTH * r;
        float stop = r - YAPS_DENT_KEEP * max(r - dist, 0);
        stop = min(stop, dist + cap);
        float h = max(k - abs(dist - stop), 0) / k;
        float pushed = max(dist, stop) + h * h * k * 0.25;
        offset += away * (pushed - dist);
        // The dented skin faces back toward the sphere's centre, out of the body.
        float w = saturate((pushed - dist) / k);
        facing -= away * w;
        turn = max(turn, w);
        if (hasNormal)
        {
            // How far the sphere reaches under this skin's own plane, so the ring rises with
            // the press's depth wherever on the curve it sits. Capped so the ring never rises
            // faster than half its run: steeper, the skin under a top rose past it and showed
            // through in blotches.
            float depth = max(r - dot(c - world, n), 0);
            float t = saturate((dist - r) / r);
            offset += n * (min(_YAPS_DentBulge * depth, 0.25 * r) * 2 * t * (1 - t));
        }
    }
    world += offset * _YAPS_DentPower;
    turn *= _YAPS_DentPower;
    if (hasNormal && turn > 0 && dot(facing, facing) > 1e-8)
        n = normalize(lerp(n, normalize(facing), turn));

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
