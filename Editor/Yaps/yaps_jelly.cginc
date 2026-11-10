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
// The state, as YapsJelly.cs lays it out: a block of rows a region, each rows of
// 64 particles, each where it is in the left half (w the _Time.y of the last pass), then a side
// row: the hands in reach in texels 0 to 15, the far switch in 18 (resting, switched off too, in its y), the anchor the last pass stepped
// on in 26 to 29 (its x, y and z axes with its scale, then its place, w 1), the swing's centre in 30,
// which of the wearer's own capsules are in reach in 41 (a bit each) and their ends in 42 to 57 as
// the hands' slots are, the avatar's colliders pressing likewise in 64 to 79, the plane between the pair in 58 (its normal toward the other, its offset)
// and 59 (its give, 0 for none), and slaps in 80 to 83.
#ifndef YAPS_JELLY_INCLUDED
#define YAPS_JELLY_INCLUDED
#ifndef YAPS_NO_JELLY

// A texel of a slot's state. The region is compiled once for all four slots, its texture picked here
// at each read: compiled once a slot, its four copies beside the squash took a body's vertex program
// past Unity's compile time limit (346 s for one variant). All four are read, the other three off
// the texture, where a Load returns 0: a branch at every read, with the loops below rolled, took a
// Poiyomi Pro body's vertex program from 20 s to 28 s in fxc.
float4 YapsJellyLoad(uint slot, int3 at)
{
    int3 none = int3(-1, -1, 0);
    return _YAPS_JellyState.Load(slot == 0 ? at : none) + _YAPS_JellyState2.Load(slot == 1 ? at : none)
         + _YAPS_JellyState3.Load(slot == 2 ? at : none) + _YAPS_JellyState4.Load(slot == 3 ? at : none);
}

float4 YapsJellyTexel(uint slot, uint p, uint base) { return YapsJellyLoad(slot, int3(p % 64, p / 64 + base, 0)); }

// A kept hand from its slot's texels a (wrist, radius) and b (an open hand's tip, 10 plus the
// gesture, and 10 a millimetre the skin holds it back), as the squash reads one (yaps_dent.cginc):
// the palm from a to pb, ending at the knuckles as it closes to a fist, and a finger out, thin, from
// fa to fb of radius rf, 0 for none.
void YapsJellyHand(float4 a, float4 b, out float3 pb, out float3 fa, out float3 fb, out float rf)
{
    float3 seg = b.xyz - a.xyz;
    float len = length(seg), g = fmod(b.w - 10, 10), r = a.w;
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
// with rim the crater's rim eased out to twice its radius, further the nearer the axis. Returned as
// the push's size along way, with in grad a slope to turn the skin's normal by, so a finger's dent
// is lit and not only seen in silhouette. The slope is the rim's bowl, its middle rounded off over
// half the radius, not the push's own: that stands straight up at the hand's outline, and lit so it
// drew a hard ring round every dent.
float YapsJellyPush(float3 at, float3 a, float3 b, float r, float3 centre, float rim, out float3 way, out float3 grad)
{
    way = 0;
    grad = 0;
    float3 seg = b - a, in_ = centre - (a + 0.5 * seg);
    if (!(dot(in_, in_) > 1e-8) || !(r > 0)) return 0;
    way = normalize(in_);
    float ss = dot(seg, seg), u = dot(at - a, seg) / max(ss, 1e-9);
    float3 off = at - (a + seg * saturate(u));
    float d2 = dot(off, off), d = sqrt(d2), along = dot(off, way);
    float ease = saturate(1 - d / (2 * r));
    float through = d2 < r * r ? sqrt(max(along * along - d2 + r * r, 0)) - along : 0;
    float3 g = -rim * ease * off / max(d, 0.5 * r);
    // Along the segment's middle the nearest point slides with at, so nothing changes that way.
    float3 s = ss > 1e-9 && u > 0 && u < 1 ? seg * rsqrt(ss) : 0;
    grad = g - s * dot(g, s);
    return max(through, rim * r * ease * ease);
}

float3 YapsJellyPress(float3 at, float3 a, float3 b, float r, float3 centre, float rim)
{
    float3 way, grad;
    float f = YapsJellyPush(at, a, b, r, centre, rim, way, grad);
    return way * f;
}

// A palm in deep sinks the skin as a dish that widens the deeper it goes and the harder the skin
// holds it back, and the dish's volume rises round it along the skin's normal. Following the
// palm's outline drew a hand-shaped pocket, the skin closed round its sides under a hard squeeze.
#define YAPS_JELLY_SPREAD 1.5   // dish half-width gained per metre of press
#define YAPS_JELLY_WIDEST 4     // in hand radii
#define YAPS_JELLY_BULGE 0.2    // ring height per metre of press
float YapsJellyDish(float3 at, float3 a, float3 b, float r, float3 way, float hold, out float3 grad, out float rise, out float3 riseGrad)
{
    float3 seg = b - a;
    float3 off = at - (a + seg * saturate(dot(at - a, seg) / max(dot(seg, seg), 1e-9)));
    // How far the hand reaches past this skin's own level along the press: 0 where it has not
    // reached, so nothing dents from a distance.
    float along = dot(off, way), pen = max(r - along, 0);
    float3 lat = off - way * along;
    float l = length(lat), press = pen + hold;
    float w = min(r + YAPS_JELLY_SPREAD * press, YAPS_JELLY_WIDEST * r), x = l / w, q = saturate(1 - x * x);
    float3 dir = l > 1e-6 ? lat / l : 0;
    grad = -4 * pen * x * q / w * dir;
    float y = saturate(x - 1), h = min(YAPS_JELLY_BULGE * press, 0.3 * r), sy = sin(3.14159265 * y);
    rise = h * sy * sy;
    riseGrad = (y > 0 && y < 1 ? h * 3.14159265 * sin(6.2831853 * y) / w : 0) * dir;
    return pen * q * q;
}

// Pushes that cover the same skin, joined: each one's size to the fourth summed, so where one acts
// it stands, and where several overlap the deepest leads and the rest add a little. Summed outright,
// the palm and finger at the knuckle, or a forearm and hand at the wrist, pushed skin past both.
// Its way is their mean by the same shares, its slope each one's by its share.
struct YapsJellyPushes { float s; float3 way, grad; };

void YapsJellyAdd(inout YapsJellyPushes p, float f, float3 way, float3 grad)
{
    float f2 = f * f, f4 = f2 * f2;
    p.s += f4;
    p.way += way * f4;
    p.grad += grad * (f2 * f);
}

// The joined push, and into bend its slope through the normal nw, across the skin only.
float3 YapsJellyJoined(YapsJellyPushes p, float3 nw, inout float3 bend)
{
    if (!(p.s > 1e-20)) return 0;
    float f = sqrt(sqrt(p.s));
    // Not normalized: two hands pressing from either side balance to nothing between them, where a
    // normalized way flipped from one to the other and tore the skin.
    float3 way = p.way / p.s, grad = p.grad / (f * f * f);
    grad -= nw * dot(grad, nw);
    bend += grad * dot(way, nw);
    return way * f;
}

// How far region r's lattice round world has moved off its rest, and in bend how the surface there
// turns: to first order, the move's gradient taken through the normal nw. Past its sides, the edge's:
// the part's share already eases skin out, and eased out here too, skin with a share but outside
// the box stayed behind a swing and tore. 0 where the region is not running.
// Each slap the step caught rides out across the skin as a damped ring a wavelength wide, at
// _YAPS_JellySlap's speed, fading in time and as it spreads, on top of the lattice's own give: the
// lattice is too coarse to carry a ripple. A slap is two texels, 80 and 81 then 82 and 83: where it
// landed and when (its _Time.y), then how deep in w, 0 for none. Its slope tilts the normal through
// bend, which is where a ripple shows. Drawn, not stepped, so a ring costs the same however long.
float3 YapsJellySlap(uint slot, uint side, float3 world, float3 nw, inout float3 bend)
{
    float3 move = 0;
    float speed = _YAPS_JellySlap.y, len = max(_YAPS_JellySlap.z, 1e-3), fade = max(_YAPS_JellySlap.w, 1e-3), k2 = 6.2831853 / len;
    [unroll] for (int k = 0; k < 2; k++)
    {
        float4 hit = YapsJellyLoad(slot, int3(80 + 2 * k, side, 0)), how = YapsJellyLoad(slot, int3(81 + 2 * k, side, 0));
        float age = _Time.y - hit.w;
        [branch] if (how.w > 0 && age >= 0 && age < 4 * fade)
        {
            float3 out_ = world - hit.xyz;
            float d = length(out_), x = d - speed * age;
            // At most 0.08 of a wavelength high, a slope of a half: a 3 cm slap's ring 3 cm high on
            // a 10 cm wave folded the skin over itself into crinkles.
            float env = min(_YAPS_JellySlap.x * how.w, 0.08 * len) * exp(-age / fade - x * x / (len * len)) / (1 + d / len);
            float h = env * cos(k2 * x);
            move -= nw * h;
            float dh = -env * (k2 * sin(k2 * x) + 2 * x / (len * len) * cos(k2 * x)) - h / (len + d);
            float3 across = out_ - nw * dot(out_, nw);
            float al = length(across);
            if (al > 1e-5) bend -= across * (dh / al);
        }
    }
    return move;
}

// m is the squash's own part for it (yaps_dent.cginc's), -1 for one the squash does not carry.
float3 YapsJellyRegion(uint slot, float4 lattice, int m, uint r, float3 world, float3 nw, out float3 bend)
{
    bend = 0;
    if (!(lattice.x > 0)) return 0;
    int3 n = int3(lattice.xyz);
    // Padded to a whole number of eight with the side row.
    uint rows = ((uint) (n.x * n.y * n.z + 63) / 64 + 8) / 8 * 8 - 1;
    uint base = r * (rows + 1), side = base + rows;
    float4 place = YapsJellyLoad(slot, int3(29, side, 0));
    // Far, where the parser switches the steps off, it keeps its rest shape; resting, switched off
    // too, the state as it lies, any dent in it included, however long ago it was stepped.
    float2 off = YapsJellyLoad(slot, int3(18, side, 0)).xy;
    if (!(place.w > 0.5) || off.x > 0.5 || !(off.y > 0.5 || abs(_Time.y - YapsJellyTexel(slot, 0, base).w) < 0.25)) return 0;
    float3 ax = YapsJellyLoad(slot, int3(26, side, 0)).xyz;
    float3 ay = YapsJellyLoad(slot, int3(27, side, 0)).xyz;
    float3 az = YapsJellyLoad(slot, int3(28, side, 0)).xyz;
    float spacing = lattice.w;
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
    // Between steps, as the parser runs them every other frame or fewer, each particle's give carried
    // on at its last speed: its move over the last step less the anchor's (texel 85), for as long as
    // it has been since. Its give, not where it is: carried on whole, the body's own walk pushed the
    // skin ahead of it. Stepped this frame, none; stalled a tenth of a second, none.
    float since = _Time.y - YapsJellyTexel(slot, 0, base).w;
    float3 carried = YapsJellyLoad(slot, int3(85, side, 0)).xyz;
    bool between = since > 1e-4 && since < 0.1;
    [unroll] for (int k = 0; k < 8; k++)
    {
        int3 d = int3(k & 1, (k >> 1) & 1, k >> 2);
        int3 l = c0 + d;
        float3 rest = float3(l.x - 0.5 * (n.x - 1), l.y - 0.5 * (n.y - 1), l.z) * spacing;
        float3 home = place.xyz + ax * rest.x + ay * rest.y + az * rest.z;
        uint pi = (uint) ((l.z * n.y + l.y) * n.x + l.x);
        float4 pt = YapsJellyTexel(slot, pi, base);
        float3 off = pt.xyz - home;
        // The step's right half: where it was before the step, and the step's time.
        [branch] if (between)
        {
            float4 was = YapsJellyLoad(slot, int3(pi % 64 + 64, pi / 64 + base, 0));
            off += ((pt.xyz - was.xyz) / max(was.w, 1e-4) - carried) * since;
        }
        float3 w = d ? t : 1 - t;
        float3 way = d ? 1 : -1;
        move += w.x * w.y * w.z * off;
        gx += way.x * w.y * w.z * off;
        gy += way.y * w.x * w.z * off;
        gz += way.z * w.x * w.y * off;
    }
    bend = per * (ax * dot(gx, nw) + ay * dot(gy, nw) + az * dot(gz, nw));
    [branch] if (_YAPS_JellySlap.x > 0) move += YapsJellySlap(slot, side, world, nw, bend);
    // The hands in reach push the skin itself on too, the palm and any finger out: the particles sit
    // a few centimetres apart, so a finger between them would sink into the skin. The way the hand
    // presses, toward the region's centre, as the particles go: out of its nearest side, skin went
    // round a hand pushed in deep. Eased, not snapped onto the radius, so layers keep their order:
    // snapped, skin and a bra met on one surface and the skin came through. Slots fill in order, so
    // an empty first one means there are none.
    [branch] if (YapsJellyLoad(slot, int3(1, side, 0)).w > 0.5)
    {
        float3 at = world + move;
        float4 mass = YapsJellyLoad(slot, int3(30, side, 0));
        float3 centre = mass.w > 0 ? mass.xyz : place.xyz + az * (0.5 * (n.z - 1) * spacing);
        YapsJellyPushes hands = (YapsJellyPushes) 0;
        float lift = 0;
        float3 liftGrad = 0;
        // Rolled, as the limbs' and the plug's lights' below: unrolled, the three cost a third of
        // the vertex program's compile. Cut short by Shader Filtering, the first hands still push.
        [loop] for (int e = 0; e < 8; e++)
        {
            float4 a = YapsJellyLoad(slot, int3(2 * e, side, 0)), b = YapsJellyLoad(slot, int3(2 * e + 1, side, 0));
            if (!(b.w > 0.5)) continue;
            // A hand gripping the skin holds it out, and dents nothing.
            if (YapsJellyLoad(slot, int3(112 + e, side, 0)).w > 0) continue;
            float3 pb, fa, fb, way, grad;
            float rf;
            YapsJellyHand(a, b, pb, fa, fb, rf);
            // The palm as a dish (YapsJellyDish), the crater kept only where it reaches further in.
            float f = YapsJellyPush(at, a.xyz, pb, a.w, centre, 0, way, grad);
            float rise;
            float3 dg, rg;
            float dish = YapsJellyDish(at, a.xyz, pb, a.w, way, floor((b.w - 10) / 10) / 1000, dg, rise, rg);
            if (dish > f) { f = dish; grad = dg; }
            YapsJellyAdd(hands, f, way, grad);
            lift += rise;
            liftGrad += rg;
            if (rf > 0)
            {
                f = YapsJellyPush(at, fa, fb, rf, centre, 1, way, grad);
                YapsJellyAdd(hands, f, way, grad);
            }
        }
        move += YapsJellyJoined(hands, nw, bend);
        move += nw * lift;
        bend += liftGrad - nw * dot(liftGrad, nw);
    }
    // The wearer's own arms, hands and thighs, with no rim: an arm resting at the side would dent
    // it from a hand's width off.
    uint own = (uint) YapsJellyLoad(slot, int3(41, side, 0)).x;
    [branch] if (own != 0)
    {
        float3 at = world + move;
        float4 mass = YapsJellyLoad(slot, int3(30, side, 0));
        float3 centre = mass.w > 0 ? mass.xyz : place.xyz + az * (0.5 * (n.z - 1) * spacing);
        YapsJellyPushes limbs = (YapsJellyPushes) 0;
        [loop] for (uint c = 0; c < 16; c++)
        {
            [branch] if (own & (1u << c))
            {
                uint t = c < 8 ? 42 + 2 * c : 48 + 2 * c;
                float4 a = YapsJellyLoad(slot, int3(t, side, 0)), b = YapsJellyLoad(slot, int3(t + 1, side, 0));
                float3 way, grad;
                float f = YapsJellyPush(at, a.xyz, b.xyz, a.w, centre, 0, way, grad);
                YapsJellyAdd(limbs, f, way, grad);
            }
        }
        move += YapsJellyJoined(limbs, nw, bend);
    }
    // A plug in the pair's socket parts it as it parts the squash (yaps_dent.cginc's YapsSource):
    // from the base its light gives, through the socket, for its length, eased out over its last
    // fifth, so a plug merely near the pair parts nothing. Drawn alone, the lattice not feeling it.
    // Each region swings away as far as the shaft cuts into the squash's shape of the part, within
    // the squash's own give, none at the back face and all of it at the front; the skin then still
    // in the shaft is pressed out of it. The press alone moved only skin on the shaft: the squash's
    // parting moved the pair a hundred times as much of the picture.
    [branch] if (_YAPS_SquashPlug > 0 && m >= 0 && YapsUnit(18 + (m >> 1)) > 0)
    {
        // The squash sets this per vertex, and leaves a vertex the jelly carries before it does.
        yapsSkin = YapsSkinState();
        float3 socket = YapsSlot(18 + (m >> 1));
        float4 mass = YapsJellyLoad(slot, int3(30, side, 0));
        float3 centre = mass.w > 0 ? mass.xyz : place.xyz + az * (0.5 * (n.z - 1) * spacing);
        // The squash's own part, from its own centre: its reaches are measured from there, and a
        // wide part's lattice box is centred elsewhere, which left the shaft short of a seat.
        YapsPart part = YapsPartAt(m, YapsSlot(m), YapsUnit(m));
        float give = _YAPS_SquashTune.z * _YAPS_SquashOut[m] * part.unit, front = c.z / max(n.z - 1, 1);
        [loop] for (uint i = 0; i < 4; i++)
        {
            int digit = YapsLightDigit(i);
            float len = unity_LightColor[i].a;
            float3 base = YapsLightPosition(i);
            float d = distance(socket, base);
            if (!YapsLightIsMarker(i) || digit != 8 && digit != 9 || len <= 1e-4 || d >= len || d <= 1e-4) continue;
            float3 tip = base + (socket - base) / d * len, seg = tip - base;
            float girth = clamp(0.1 * len, 0.012, 0.06), gain = _YAPS_SquashPlug * saturate((len - d) / (0.2 * len));
            float3 out_ = part.centre - (base + seg * saturate(dot(part.centre - base, seg) / dot(seg, seg)));
            float apart = length(out_);
            [branch] if (apart > 1e-5 && give > 0)
            {
                float3 w = out_ / apart;
                float cut = (girth + YapsSpan(part, -w) - apart) * gain * _YAPS_SquashTune.y;
                if (cut > 0) move += w * (give * (1 - exp(-cut / give)) * front);
            }
            move += YapsJellyPress(world + move, base, tip, girth, centre, 1) * gain;
        }
    }
    // Last, skin past the plane between the pair, further than they overlapped as built, eased back
    // to within its give of it, the deeper the further, so layers keep their order: the lattice keeps
    // the two apart a few centimetres coarse, and here they cannot cross. By give's y, how much the
    // pair still meet there: a limb turned across the other lies past it with nothing to meet.
    float4 give = YapsJellyLoad(slot, int3(59, side, 0));
    [branch] if (give.x > 0)
    {
        float4 plane = YapsJellyLoad(slot, int3(58, side, 0));
        float x = dot(world + move, plane.xyz) - plane.w;
        if (x > 0) move -= plane.xyz * ((x - give.x * (1 - exp(-x / give.x))) * give.y);
    }
    return move;
}

void YapsJellyDeform(inout float3 position, inout float3 normal, inout float3 tangent, uint vertexId)
{
    if (!(_YAPS_JellyLattice.x > 0 || _YAPS_JellyLattice2.x > 0 || _YAPS_JellyLattice3.x > 0 || _YAPS_JellyLattice4.x > 0)) return;
    uint width = (uint) round(abs(1.0 / _YAPS_JellyOwn_TexelSize.x));
    float4 own = _YAPS_JellyOwn.Load(int3(vertexId % width, vertexId / width, 0));
    uint2 part = (uint2) round(own.xz * 255);
    if (!(part.x > 0 && own.y > 0 || part.y > 0 && own.w > 0)) return;

    float3 world = mul(unity_ObjectToWorld, float4(position, 1)).xyz;
    float3 n0 = mul(normal, (float3x3)unity_WorldToObject);
    bool hasNormal = dot(n0, n0) > 0;
    float3 nw = hasNormal ? normalize(n0) : 0;
    float3 move = 0, bend = 0;
    // Skin between two parts takes each one's move by its share; a part with no lattice, none.
    // Rolled, the region's call in it once: unrolled, it would be drawn twice.
    [loop] for (uint j = 0; j < 2; j++)
    {
        uint p = part[j];
        float s = j == 0 ? own.y : own.w;
        [branch] if (p > 0 && s > 0)
        {
            uint slot = (p - 1) >> 1, r = (p - 1) & 1;
            int m = -1;
            [unroll] for (int c = 0; c < 4; c++) if ((uint) round(_YAPS_JellyParts[c]) == p) m = c;
            float3 b;
            float4 lattice = slot == 0 ? _YAPS_JellyLattice : slot == 1 ? _YAPS_JellyLattice2 : slot == 2 ? _YAPS_JellyLattice3 : _YAPS_JellyLattice4;
            float3 mv = YapsJellyRegion(slot, lattice, m, r, world, nw, b);
            move += s * mv;
            bend += s * b;
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
