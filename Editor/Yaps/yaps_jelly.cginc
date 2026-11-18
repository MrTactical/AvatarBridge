// YAPS, the LIVE TISSUE (the jelly). Particle lattices in a render texture, stepped each frame
// by the avatar's blitters, one region a part. A vertex moves as the lattice round it moved off
// rest, a function of position alone, so every layer at a point (skin, bra, top) moves alike.
// A region not running (far off, editor, a viewer with no blitters) keeps its rest shape.
//
// State layout (YapsJelly.cs): per region, rows of 64 particles (xyz in the left half, w the
// _Time.y of the last pass), then a side row: hands 0-15, far switch 18 (y: resting), anchor
// 26-29 (scaled axes, then place with w 1), swing centre 30, own capsules in reach 41 (a bit each)
// with ends 42-57, colliders 64-79, plane between the pair 58 (normal toward the other, offset)
// and 59 (give, 0 none), slaps 80-83.
#ifndef YAPS_JELLY_INCLUDED
#define YAPS_JELLY_INCLUDED
#ifndef YAPS_NO_JELLY

// The region compiles once for all slots, the texture picked per read: one copy per slot passed
// Unity's compile limit (346 s a variant). Unused slots load off-texture (returns 0); a branch
// per read took a Poiyomi Pro body from 20 s to 28 s in fxc. Only YAPS_JELLY_SLOTS are read.
#ifndef YAPS_JELLY_SLOTS
#define YAPS_JELLY_SLOTS 4
#endif
float4 YapsJellyLoad(uint slot, int3 at)
{
#if YAPS_JELLY_SLOTS == 1
    return _YAPS_JellyState.Load(at);
#else
    int3 none = int3(-1, -1, 0);
    return _YAPS_JellyState.Load(slot == 0 ? at : none) + _YAPS_JellyState2.Load(slot == 1 ? at : none)
#if YAPS_JELLY_SLOTS > 2
         + _YAPS_JellyState3.Load(slot == 2 ? at : none)
#endif
#if YAPS_JELLY_SLOTS > 3
         + _YAPS_JellyState4.Load(slot == 3 ? at : none)
#endif
         ;
#endif
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

// How far capsule a-b (radius r) pushes skin at `at` toward centre, with `rim` easing the crater
// out to 2r. grad tilts the normal so a dent is lit. It is the rim bowl's slope, rounded over
// half the radius: the push's own slope is vertical at the outline and drew a hard ring.
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

// A deep palm sinks a dish that widens with depth and hold, its volume rising round it. Following
// the palm's outline drew a hand-shaped pocket.
#define YAPS_JELLY_SPREAD 1.5   // dish half-width gained per metre of press
#define YAPS_JELLY_WIDEST 4     // in hand radii
#define YAPS_JELLY_BULGE 0.2    // ring height per metre of press
float YapsJellyDish(float3 at, float3 a, float3 b, float r, float3 way, float hold, out float3 grad, out float rise, out float3 riseGrad)
{
    float3 seg = b - a;
    float3 off = at - (a + seg * saturate(dot(at - a, seg) / max(dot(seg, seg), 1e-9)));
    // 0 until the hand reaches this skin's level, so nothing dents from a distance.
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

// Overlapping pushes joined as a 4-norm: the deepest leads, the rest add a little. A plain sum
// pushed skin past both at a knuckle or wrist.
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
    // Not normalized: opposite hands balance to nothing, where a normalized way flipped and tore.
    float3 way = p.way / p.s, grad = p.grad / (f * f * f);
    grad -= nw * dot(grad, nw);
    bend += grad * dot(way, nw);
    return way * f;
}

// Each slap rides out as a damped ring, drawn not stepped (the lattice is too coarse for a ripple).
// Two texels a slap: where and when (_Time.y), then depth in w (0 none). Its slope tilts the
// normal through bend, which is where a ripple shows.
float3 YapsJellySlap(uint slot, uint side, float3 world, float3 nw, inout float3 bend)
{
    float3 move = 0;
    float speed = _YAPS_JellySlap.y, len = max(_YAPS_JellySlap.z, 1e-3), fade = max(_YAPS_JellySlap.w, 1e-3), k2 = 6.2831853 / len;
    [unroll] for (int k = 0; k < 2; k++)
    {
        // The depth first: no slap, one texel read and not two.
        float4 how = YapsJellyLoad(slot, int3(81 + 2 * k, side, 0));
        [branch] if (!(how.w > 0)) continue;
        float4 hit = YapsJellyLoad(slot, int3(80 + 2 * k, side, 0));
        float age = _Time.y - hit.w;
        [branch] if (age >= 0 && age < 4 * fade)
        {
            float3 out_ = world - hit.xyz;
            float d = length(out_), x = d - speed * age;
            // Capped at 0.08 wavelengths: a 3 cm ring on a 10 cm wave folded the skin into crinkles.
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

// How far region r's lattice round world moved off rest; bend is the first-order turn of the
// surface through nw. Eased out past the box edge, or skin outside it stayed behind a swing and
// tore. 0 where not running. m is the squash's part for it (yaps_dent.cginc), -1 none.
float3 YapsJellyRegion(uint slot, float4 lattice, int m, uint r, float3 world, float3 nw, out float3 bend)
{
    bend = 0;
    // A clip keying .x alone zeroes the rest in the block, which tore the skin into a spike.
    if (!(lattice.x > 1.5 && lattice.y > 1.5 && lattice.z > 1.5 && lattice.w > 0)) return 0;
    int3 n = int3(lattice.xyz);
    // Padded to a whole number of eight with the side row.
    uint rows = ((uint) (n.x * n.y * n.z + 63) / 64 + 8) / 8 * 8 - 1;
    uint base = r * (rows + 1), side = base + rows;
    float4 place = YapsJellyLoad(slot, int3(29, side, 0));
    // Far: rest shape. Resting: the state as it lies, however old.
    float3 off = YapsJellyLoad(slot, int3(18, side, 0)).xyz;
    if (!(place.w > 0.5) || off.x > 0.5 || !(off.y > 0.5 || abs(_Time.y - YapsJellyTexel(slot, 0, base).w) < 0.25)) return 0;
    // Held on its bones (Ease 1): exactly the bones' skin. Off the lattice it lagged a frame,
    // 56 mm at 25 fps and 1.4 m/s. Hands, limbs and slaps still dent it.
    bool held = off.z >= 1 && off.z < 1.5;
    float3 ax = YapsJellyLoad(slot, int3(26, side, 0)).xyz;
    float3 ay = YapsJellyLoad(slot, int3(27, side, 0)).xyz;
    float3 az = YapsJellyLoad(slot, int3(28, side, 0)).xyz;
    float spacing = lattice.w;
    // Axes carry the scale, so 1/scale^2 takes a world offset into lattice units.
    float per = 1 / max(dot(ax, ax) * spacing, 1e-12);
    // In particles.
    float3 rel = world - place.xyz;
    float3 c = float3(dot(rel, ax), dot(rel, ay), dot(rel, az)) * per + float3(0.5 * (n.x - 1), 0.5 * (n.y - 1), 0);
    // A vertex cells off the lattice is another copy with these materials (the game's MiniMe),
    // which the pair's plane dragged across. Faded from one cell off to three, not cut, so edge
    // skin does not tear.
    float3 outside = abs(c - clamp(c, 0, n - 1));
    float keep = saturate((3 - max(outside.x, max(outside.y, outside.z))) * 0.5);
    if (keep <= 0) return 0;
    c = clamp(c, 0, n - 1);
    int3 c0 = min(int3(c), n - 2);
    float3 t = c - c0;
    float3 move = 0, gx = 0, gy = 0, gz = 0;
    // Steps run every other frame or less, so between them each particle's give (its move less
    // the anchor's, texel 85) carries on at its last speed. Give only: the whole move pushed skin
    // ahead of the body's walk. None if stepped this frame or stalled 0.1 s.
    float since = _Time.y - YapsJellyTexel(slot, 0, base).w;
    float4 carried = YapsJellyLoad(slot, int3(85, side, 0));
    // Not after a snap: 85 holds the jump's own speed, and the skin was pulled back by it.
    bool between = since > 1e-4 && since < 0.1 && carried.w > 0;
    // Rolled: unrolled it was a fifth of the vertex program every viewer builds. Shader Filtering
    // may cut it short, so a cut lattice draws at rest, detected by the weights' sum (a corner
    // count the compiler proves is 8 and drops).
    float weights = 0;
    [loop] for (int k = 0; k < 8; k++)
    {
        int3 d = int3(k & 1, (k >> 1) & 1, k >> 2);
        int3 l = c0 + d;
        // Not "pi": Poiyomi 7.3 defines it as a macro, and the body would not compile.
        uint corner = (uint) ((l.z * n.y + l.y) * n.x + l.x);
        float4 pt = YapsJellyTexel(slot, corner, base);
        // Rest is linear in the corner, so it comes off once below.
        float3 off = pt.xyz - place.xyz;
        // The step's right half: where it was before the step, and the step's time.
        [branch] if (between)
        {
            float4 was = YapsJellyLoad(slot, int3(corner % 64 + 64, corner / 64 + base, 0));
            off += ((pt.xyz - was.xyz) / max(was.w, 1e-4) - carried.xyz) * since;
        }
        float3 w = d ? t : 1 - t;
        float3 way = d ? 1 : -1;
        weights += w.x * w.y * w.z;
        move += w.x * w.y * w.z * off;
        gx += way.x * w.y * w.z * off;
        gy += way.y * w.x * w.z * off;
        gz += way.z * w.x * w.y * off;
    }
    if (weights < 0.999) return 0;
    float3 rest = float3(c.x - 0.5 * (n.x - 1), c.y - 0.5 * (n.y - 1), c.z) * spacing;
    move -= ax * rest.x + ay * rest.y + az * rest.z;
    // Less the anchor's own turn since (94), which the skin already rides.
    [branch] if (between) move -= cross(YapsJellyLoad(slot, int3(94, side, 0)).xyz, world - place.xyz) * since;
    gx -= ax * spacing;
    gy -= ay * spacing;
    gz -= az * spacing;
    bend = per * (ax * dot(gx, nw) + ay * dot(gy, nw) + az * dot(gz, nw));
    // Never more than the tissue's depth: a lattice thrown off is drawn held in, not stretched across the room.
    float depth = (n.z - 1) * spacing * length(ax);
    move *= min(1, depth / max(length(move), 1e-9));
    if (held) { move = 0; bend = 0; }
    [branch] if (_YAPS_JellySlap.x > 0 && _YAPS_JellySlap.z > 0) move += YapsJellySlap(slot, side, world, nw, bend);
    // Hands push the skin directly too: particles are cm apart, so a finger would sink between.
    // Toward the region centre (out of the nearest side, skin wrapped a deep hand). Eased, not
    // snapped to the radius, or skin and bra meet on one surface and the skin shows through.
    // Slots fill in order, so an empty first means none.
    [branch] if (YapsJellyLoad(slot, int3(1, side, 0)).w > 0.5)
    {
        float3 at = world + move;
        float4 mass = YapsJellyLoad(slot, int3(30, side, 0));
        float3 centre = mass.w > 0 ? mass.xyz : place.xyz + az * (0.5 * (n.z - 1) * spacing);
        YapsJellyPushes hands = (YapsJellyPushes) 0;
        float lift = 0;
        float3 liftGrad = 0;
        // Rolled, like the loops below: unrolled, the three cost a third of the compile.
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
    // A plug in the pair's socket parts it as in the squash (YapsSource), eased over its last
    // fifth so a plug merely near parts nothing. Drawn only, the lattice does not feel it. Each
    // region swings away as far as the shaft cuts the part, within its give (none at the back,
    // all at the front), then remaining skin is pressed out. The press alone looked a hundred
    // times smaller than the squash's parting.
    // Lights first: with none lit, the skin state's 20 buffer reads are skipped.
    uint plugs = 0;
    [unroll] for (uint q = 0; q < 4; q++)
        if (YapsLightIsMarker(q) && (YapsLightDigit(q) == 8 || YapsLightDigit(q) == 9) && !(unity_LightColor[q].a <= 1e-4)) plugs |= 1u << q;
    [branch] if (plugs != 0 && _YAPS_SquashPlug > 0 && m >= 0 && YapsUnit(18 + (m >> 1)) > 0)
    {
        // The squash sets this per vertex, and leaves a vertex the jelly carries before it does.
        yapsSkin = YapsSkinState();
        float3 socket = YapsSlot(18 + (m >> 1));
        float4 mass = YapsJellyLoad(slot, int3(30, side, 0));
        float3 centre = mass.w > 0 ? mass.xyz : place.xyz + az * (0.5 * (n.z - 1) * spacing);
        // From the squash part's own centre: a wide part's lattice box is centred elsewhere.
        YapsPart part = YapsPartAt(m, YapsSlot(m), YapsUnit(m));
        float give = _YAPS_SquashTune.z * _YAPS_SquashOut[m] * part.unit, front = c.z / max(n.z - 1, 1);
        [loop] for (uint i = 0; i < 4; i++)
        {
            float len = unity_LightColor[i].a;
            float3 base = YapsLightPosition(i);
            float d = distance(socket, base);
            if (!(plugs & (1u << i)) || d >= len || d <= 1e-4) continue;
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
    // Skin past the plane between the pair eases back within its give, so the two cannot cross
    // (the lattice is cm coarse). Scaled by give.y, how much they still meet.
    float4 give = YapsJellyLoad(slot, int3(59, side, 0));
    [branch] if (give.x > 0)
    {
        float4 plane = YapsJellyLoad(slot, int3(58, side, 0));
        float x = dot(world + move, plane.xyz) - plane.w;
        if (x > 0) move -= plane.xyz * ((x - give.x * (1 - exp(-x / give.x))) * give.y);
    }
    // Within the part's depth of the bones, and outward a quarter of it at most: a deep hand drew
    // the part into a cone twice its length.
    float3 outward = normalize(az);
    move -= outward * max(dot(move, outward) - 0.25 * depth, 0);
    move *= min(1, depth / max(length(move), 1e-9));
    bend *= keep;
    return move * keep;
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
    // A bad move drew its triangles away: the skin shows at rest instead of a hole.
    if (any((asuint(float4(move, dot(bend, bend))) & 0x7f800000) == 0x7f800000)) { move = 0; bend = 0; }
    world += move;

    position = mul(unity_WorldToObject, float4(world, 1)).xyz;
    // Zero stays zero: a host with no normal gets none invented here.
    if (hasNormal)
    {
        float3 bent = nw - bend;
        normal = normalize(mul(dot(bent, bent) > 1e-12 ? bent : nw, (float3x3)unity_ObjectToWorld));
        float3 across = tangent - normal * dot(normal, tangent);
        if (dot(across, across) > 1e-12)
            tangent = normalize(across);
    }
}

#endif
#endif
