// YAPS, the SOFT-BODY DENT. Skin inside a pressing sphere moves out onto
// its surface and turns to face the presser, so a press reads as flesh
// giving way where it is touched, not as the whole part sliding back on its
// bones. The flesh it displaces swells in a ring round the press, so the
// part stays full instead of hollowing.
//
// The dent's edge is a smooth max of the vertex's distance and the radius,
// never a hard clamp: a clamp leaves a crease where the dent meets the skin.
//
// A soft PAIR pressed together (two balls squeezed) is a plane between them:
// skin that crossed it and still faces across is the one side inside the
// other, so it flattens onto the plane, and each side swells round the flat.
// Which side a vertex belongs to is read from where it faces, so one mesh
// carrying both needs no tagging.
//
// Spheres are world space, xyz and radius; radius 0 is an empty slot. How
// they arrive is still open: the dev probe sets them globally.

#ifndef YAPS_DENT_INCLUDED
#define YAPS_DENT_INCLUDED

// How much of a layer's depth below a press survives it, so stacked layers stay stacked.
#define YAPS_DENT_KEEP 0.2

float _YAPS_DentPower;
float _YAPS_DentSoftness;
float _YAPS_DentBulge;
float4 _YAPS_DentSpheres[4];
float4 _YAPS_DentPlane;   // xyz from the first of a pair toward the second, w the plane's offset along it
float4 _YAPS_DentZone;    // xyz the middle of the pair, w its reach; 0 when there is no pair
float _YAPS_DentOverlap;  // how far each of the pair is pressed into the other

void YapsDentDeform(inout float3 position, inout float3 normal, inout float3 tangent)
{
    if (_YAPS_DentPower <= 0) return;

    // A skinned mesh arrives in world space under an identity matrix, a plain
    // mesh in its own; going through the matrix serves both.
    float3 world = mul(unity_ObjectToWorld, float4(position, 1)).xyz;
    float3 n = mul(normal, (float3x3)unity_WorldToObject);
    bool hasNormal = dot(n, n) > 0;
    n = hasNormal ? normalize(n) : 0;
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
        float stop = r - YAPS_DENT_KEEP * max(r - dist, 0);
        float h = max(k - abs(dist - stop), 0) / k;
        float pushed = max(dist, stop) + h * h * k * 0.25;
        offset += away * (pushed - dist);
        // The dented skin faces back toward the sphere's centre, out of the body.
        float w = saturate((pushed - dist) / k);
        facing -= away * w;
        turn = max(turn, w);
        if (hasNormal)
        {
            // How far the sphere reaches under this skin's own plane, so the ring rises
            // with the press's depth wherever on the curve it sits.
            // Capped so the ring never rises faster than half its run: steeper, the skin under
            // a top rose past it and showed through in blotches.
            float depth = max(r - dot(c - world, n), 0);
            float t = saturate((dist - r) / r);
            offset += n * (min(_YAPS_DentBulge * depth, 0.25 * r) * 2 * t * (1 - t));
        }
    }

    if (_YAPS_DentZone.w > 0 && hasNormal)
    {
        float3 axis = _YAPS_DentPlane.xyz;
        float s = dot(world, axis) - _YAPS_DentPlane.w;
        float face = dot(n, axis);
        float zone = saturate(1 - length(world - _YAPS_DentZone.xyz) / _YAPS_DentZone.w);
        // Past the plane and facing on across it is one side inside the other. Bounded
        // by depth: the FAR side of the second also sits past the plane facing on, and
        // only distance tells it apart.
        float reach = 0.4 * _YAPS_DentZone.w;
        if (s * face > 0)
        {
            float w = zone * saturate((reach - abs(s)) / (0.25 * reach));
            offset -= axis * s * w * (1 - YAPS_DENT_KEEP);
            facing += sign(face) * axis * w;
            turn = max(turn, w);
        }
        else
        {
            // Own side: the flesh squeezed out rises round the flat, most where the skin
            // faces neither toward nor away from the other.
            float t = saturate(1 - abs(s) / (0.5 * _YAPS_DentZone.w));
            // The same cap as the ring round a press.
            float lift = min(_YAPS_DentBulge * _YAPS_DentOverlap, _YAPS_DentZone.w / 8);
            offset += n * (lift * t * t * (1 - abs(face)) * zone);
        }
    }

    if (dot(offset, offset) == 0) return;
    position = mul(unity_WorldToObject, float4(world + offset * _YAPS_DentPower, 1)).xyz;
    turn *= _YAPS_DentPower;
    // Zero stays zero: a host with no normal gets none invented here.
    if (hasNormal && turn > 0 && dot(facing, facing) > 1e-8)
    {
        float3 bent = normalize(lerp(n, normalize(facing), turn));
        normal = normalize(mul(bent, (float3x3)unity_ObjectToWorld));
        if (dot(tangent, tangent) > 0)
            tangent = normalize(tangent - normal * dot(normal, tangent));
    }
}

#endif
