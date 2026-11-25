// The jelly's HLSL, kept apart from the C# that writes it out. Write() compares the text, so
// moving it here recompiles nothing.
#if CVR_CCK_EXISTS
namespace AvatarBridge
{
    internal static partial class YapsJelly
    {
        internal const string MarkerShader = @"// The jelly's probe markers, for testing. Written by YAPS; edits here are overwritten.
Shader ""Hidden/YAPS/Jelly Probe Marker""
{
    Properties { _MainTex (""The jelly's state"", 2D) = ""black"" {} }
    SubShader
    {
        // Over everything, so a probe the body hides still shows.
        Tags { ""Queue"" = ""Overlay"" ""RenderType"" = ""Overlay"" ""IgnoreProjector"" = ""True"" }
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include ""UnityCG.cginc""
            Texture2D<float4> _MainTex;
            struct appdata { float4 vertex : POSITION; float2 at : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; fixed4 colour : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                // How far the step read this probe pushed in, metres.
                float pushed = _MainTex.Load(int3((int2) v.at, 0)).w;
                o.colour = lerp(fixed4(1, 1, 1, 1), fixed4(1, 0, 0, 1), saturate(pushed / 0.01));
                return o;
            }
            fixed4 frag(v2f i) : SV_Target { return i.colour; }
            ENDCG
        }
    }
}
";

        internal const string StepShader = @"// The jelly's step. Written by YAPS; edits here are overwritten.
//
// The frame's two passes, from one texture into its twin and back. Left half: where each particle
// is, w the _Time.y of the last pass (negative while no anchor arrives). Right half: where it was a
// pass before, w the h that pass took, so a velocity is only ever read off the two. The row after
// the lattice: the hands in reach (texels 0 to 15), the rider (16, 17), the far switch and
// whether it sleeps (18), how long it has stood still (19), where the anchor was the last time it
// stepped (20 to 22), the rider a frame before (23, 24), the step held with the smoothed frame
// time (25), the anchor the second pass stepped on (26 to 29: its axes with its scale, then its
// place, w 1), for a body's own shader to find the lattice by (yaps_jelly.cginc), and the region's
// mass, as one piece (30, its centre, w the _Time.y it stepped; 31, where it was, w that step), and
// whether any probe reads pushed (87), and the anchor the first pass stepped on, for the second's
// particles (88 to 90, its rows; 91, its turn), and whether any slot might grip (92), and the
// whether the gate at the top of frag takes _Mid, past _Half, while _Mid is on (93, y the flag before).
// Below 72 fps each pass takes a step. Above it the first only works out where each particle is
// headed and the second fits, so each neighbour is read once. The first pass also looks for the
// hands, so the second pushes against where they are this frame; below 72 fps the first pass's own
// step pushes against where they were the frame before.
Shader ""Hidden/AvatarBridge/JellyStep""
{
    Properties
    {
        _MainTex (""Last pass's state"", 2D) = ""black"" {}
        _Lattice (""Particles across, up and deep; spacing in metres"", Vector) = (16, 12, 16, 0.0133)
        _Stiff (""Pull toward the neighbourhood's shape over a 180th of a second"", Range(0, 1)) = 0.5
        _Hz (""Wobble a second, toward its rest on the chest"", Float) = 3
        _Viscous (""Damping a second of what the tissue does apart from its home's own move, a jelly at 4, flesh far more"", Float) = 4
        _Hand (""Hand radius at a 1.6 m viewpoint"", Float) = 0.05
        _Far (""Hangs rigid past this many metres from the viewer's head"", Float) = 4
        _Behind (""Hangs rigid more than this many degrees off where the viewer's camera looks, 0 never"", Float) = 100
        _Near (""Never hangs rigid for being out of view within this many metres of the viewer's camera"", Float) = 2
        // Over a sprint's 4 m/s and under flight's 10, by the client's own defaults.
        _Fast (""Hangs rigid moving faster than this many metres a second, easing in over the 2 below; 0 never"", Float) = 7
        // The bones' own physics swing it; the tissue only answers what touches it.
        _Hold (""Seconds an untouched part takes to settle onto its bones and hold there, until something touches it; 0 never"", Float) = 0.4
        _Half (""Steps at _Rate.y past this many metres from the viewer's head, short of _Far; 0 never"", Float) = 2
        // Every frame: the client reads the switch back late and one read at a time, so at 45 a
        // second and 230 fps the steps came in bursts, the draw stuttered and it never slept.
        _Rate (""Steps a second near the viewer's head (x) and past _Half (y), the draw carrying the jiggle on between; 0 every frame"", Vector) = (0, 0, 0, 0)
        // Experiments, off by default: gated in the step on its own clock, out of the parser's reach.
        _Cap (""Steps a second at most near, frames between them left as they are; 0 every frame"", Float) = 0
        _Mid (""Steps a second at most past _Half, short of _Far; 0 as near"", Float) = 0
        _Pass (""Which of the frame's passes this is: 2 copies the rider out"", Float) = 0
        _Slap (""Slaps: metres a second to fire, depth per m/s, deepest, life in seconds"", Vector) = (1.2, 0.01, 0.03, 1.5)
        _Landing (""A landing or a yank rings the tissue past this change of its speed in a step, metres a second"", Float) = 1
        _Smooth (""How long the anchor's changes of speed are spread over, seconds: a remote copy's moves come in steps, a packet each"", Float) = 0.06
        _FixedDt (""Fixed frame time, 0 for the real one"", Float) = 0
        _Stress (""The cost meter's load: steps only in odd phases"", Float) = 0
        _Rider (""Steps the rider's texels, for the parser and the sleep"", Float) = 1
        _Sleep (""Rests after standing still this many seconds, untouched and unmoved; 0 never"", Float) = 0.5
        _Rest (""Worn: stops the steps after lying still this many seconds with no hand near; 0 never"", Float) = 0.5
        _Regions (""How many regions the state packs"", Float) = 1
        _Lift (""1 lifts its rest by the sag gravity gives, along the anchor's up: modelled tissue hangs already; a free mass is lifted always"", Float) = 0
        _Gravity (""Share of gravity the tissue feels"", Float) = 1
        _MassHz (""The whole region as one piece, swinging on its root: wobbles a second, 0 off"", Float) = 0
        _MassCarried (""1: the mass rides the anchor's own move, a bone its cloth swings, and only hands move it on it"", Float) = 0
        _MassDamp (""Its damping a second"", Float) = 4
        _Give (""Share of how far a hand reaches into it that moves the whole; the particles dent the rest"", Range(0, 1)) = 0.5
        _MassRadius (""Its radius against hands, in the anchor's metres"", Float) = 0.1
        _Limit (""Furthest its centre swings off its rest, as a share of its length from the root"", Float) = 0.6
        _Taut (""How much a loaded region's swing stiffens and damps: fully strained, the spring 1 + _Taut times as stiff, the damping 1 + 3 _Taut times"", Float) = 1
        _JellyTwitch (""Which handed transform the animator moves off the first hang to kick the mass; 0 none"", Float) = 0
        _Pair (""1: regions 0 and 1 are a pair, each keeping off the other"", Float) = 0
        _PairKeep (""Nearest the pair's centres come, as a share of their distance at rest"", Float) = 0.85
        _Squish (""Share of each neighbourhood's own squash and stretch it keeps, at constant volume; 0 rigid"", Range(0, 1)) = 0
        _PairOffset (""The second region's place in the first's frame at rest, both turned alike"", Vector) = (0, 0, 0, 0)
        _PairReach (""Each region's half width toward the other at rest, and the gap between them then; x 0 no plane"", Vector) = (0, 0, 0, 0)
        _PairSkin (""Each region's skin's reach toward the other off its centre at rest; x 0 none"", Vector) = (0, 0, 0, 0)
        _JellyOwn0 (""The wearer's own capsules' radii: the left upper arm, forearm, hand and thigh; 0 none"", Vector) = (0, 0, 0, 0)
        _JellyOwn1 (""The same for the right"", Vector) = (0, 0, 0, 0)
        _JellyCarrier (""Which of those eight each region rides, plus one, left out of its own: x the first region's, y the second's; 0 none"", Vector) = (0, 0, 0, 0)
        _JellyCoreA0 (""The first region's held core, one end of its bone on the anchor, w its radius; w 0 holds the back face"", Vector) = (0, 0, 0, 0)
        _JellyCoreB0 (""Its other end"", Vector) = (0, 0, 0, 0)
        _JellyCoreA1 (""The same for the second region"", Vector) = (0, 0, 0, 0)
        _JellyCoreB1 (""Its other end"", Vector) = (0, 0, 0, 0)
        _JellyCol0 (""The avatar's own colliders' radii, those pressing; 0 none"", Vector) = (0, 0, 0, 0)
        _JellyCol1 (""The next four"", Vector) = (0, 0, 0, 0)
        _JellyBox0 (""The first region's skin on its anchor: half width, half height, depth, w 1 with its depth map; 0 the lattice's box"", Vector) = (0, 0, 0, 0)
        _JellyBox1 (""The same for the second"", Vector) = (0, 0, 0, 0)
        _JellyDepth (""How deep each region's skin lies under each face of its box, YapsJelly.DepthGrid square a face"", 2D) = ""black"" {}
        _JellyOffsetAt0 (""The first region's place on the bone it rides, in the bone's units, w its scale on it; w 0 rides none"", Vector) = (0, 0, 0, 0)
        _JellyOffsetTurn0 (""The first region's turn on that bone"", Vector) = (0, 0, 0, 1)
        _JellyOffsetAt1 (""The second region's place on its bone"", Vector) = (0, 0, 0, 0)
        _JellyOffsetTurn1 (""The second region's turn on its bone"", Vector) = (0, 0, 0, 1)
        _JellySkinStride (""The skinned helper's vertex stride, 0 for none: the bones as the cloth swings them"", Float) = 0
        _JellyProbes (""MagicaCloth probes a region has in the skinned helper, after its frames, 0 none"", Float) = 0
        _JellyProbeReach (""How far off the skin a collider pushes a probe, the probes' radius, metres"", Float) = 0
        _Deepest (""How deep a hand presses before the skin holds it, metres; past the tissue's middle it lets go"", Float) = 0.12
        _GripSlip (""How far a gripped spot drags behind a fist stroking along the skin before it slides with it, metres; 0 it never slides"", Float) = 0
        _JellyArrays (""A component a region: 0 leaves the player arrays' hands out, its probes alone"", Vector) = (1, 1, 1, 1)
        _Floor (""A board under the tissue on its anchor: x 1 for one, y its height there in metres"", Vector) = (0, 0, 0, 0)
        _Solid (""Share of the whole's own squash its homes take at constant volume, so squeezed it bulges where nothing holds it; 0 none"", Range(0, 0.95)) = 0
        _Squeeze (""Share of other players' palms' reach into the whole it flattens along their line at constant volume; 0 none"", Range(0, 1)) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 4.5
            // A blit into the state, never an eye: the stereo copies tripled the upload's compile.
            // Unity adds its own stereo variants past this; YapsStereoStrip drops those.
            #pragma skip_variants UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON STEREO_CUBEMAP_RENDER_ON
            // shader_feature: an upload builds only the variants its passes use. multi_compile built
            // all eight, each at the compiler's time limit.
            // The first pass's side row, a blit of its own: the scan, slaps, mass and the rest, which
            // only it carries every player's positions for. Both passes' particles share the variant
            // without it, so a viewer's driver builds that once, not twice.
            #pragma shader_feature_local __ JELLY_SCAN
            // Only a prop sleeps; worn, it is compiled out.
            #pragma shader_feature_local __ JELLY_SLEEP
            // The third pass, which wakes a resting jelly, reads the players' hands too.
            #pragma shader_feature_local __ JELLY_POINTS
            #include ""UnityCG.cginc""
            #include ""JellyCommon.cginc""

            float _Stiff, _Hz, _Hand, _Far, _Behind, _Near, _Fast, _Hold, _Half, _Pass, _FixedDt, _Stress, _Sleep, _Lift, _Rider, _Rest, _Regions;
            float _MassHz, _MassCarried, _MassDamp, _Give, _MassRadius, _Limit, _Pair, _PairKeep, _Squish, _Viscous, _Solid, _Squeeze, _Taut, _JellyTwitch;
            float4 _Slap, _Rate;
            float _Landing, _Cap, _Mid, _Smooth;
            // The frame's time, for how fast the homes move.
            static float FrameDt;
            // The anchor snapped to a new pose this frame: the tissue rides the jump.
            static bool Snapped = false;
            // The time since the last step, and whether it was more than a step can take.
            static float Since;
            static bool Gapped = false;
            // The part of the anchor's change of speed this step that _Smooth spreads over the next
            // steps, taken off what the particles feel, and the anchor's smoothed speed (95).
            static float3 Kick = 0;
            static float3 Smoothed = 0;
            static float Ease = 0;
            static const float Waking = 0.12;
            // A landing heard this step, rung next step if the new speed held (97 and 98).
            static float4 Landed = 0, LandedBy = 0;
            static float Scale = 1;
            float4 _PairOffset, _PairReach, _PairSkin, _JellyOwn0, _JellyOwn1, _JellyCol0, _JellyCol1, _JellyBox0, _JellyBox1, _JellyOffsetAt0, _JellyOffsetTurn0, _JellyOffsetAt1, _JellyOffsetTurn1;
            // A helper mesh skinned four points to each region's bone and four to where it hangs on
            // the chest, as the squash's slots, bound as the provider's copy of its skinned buffer.
            ByteAddressBuffer _JellySkin;
            Texture2D<float> _JellyDepth;
            float _JellySkinStride, _JellyProbes, _JellyProbeReach, _Deepest, _GripSlip;
            float4 _JellyArrays, _JellyCarrier, _JellyCoreA0, _JellyCoreB0, _JellyCoreA1, _JellyCoreB1;
            float4 _Floor;
            // The most probes a region has, YapsJelly.ProbesMax, and their readout's first column on
            // the side row, YapsJelly.Readout: columns of their own past the particles'.
            #define PROBES 64
            #define READOUT 128
            // The depth map's spots along a face, YapsJelly.DepthGrid.
            #define DEPTH_GRID 16

            // The inverse of a turn, an even scale and a place.
            float4x4 Unrigid(float4x4 m)
            {
                float3x3 r = transpose((float3x3) m) / dot(m._m00_m10_m20, m._m00_m10_m20);
                float3 t = -mul(r, m._m03_m13_m23);
                return float4x4(float4(r[0], t.x), float4(r[1], t.y), float4(r[2], t.z), float4(0, 0, 0, 1));
            }

            // A frame through skinning, from four points at base: its place, then a tenth along each
            // of its axes, in the helper's own space (skinned with no root bone, not the world's).
            // false where the buffer reads empty or spoilt.
            bool Skinned(uint base, out float4x4 f)
            {
                f = 0;
                uint stride = (uint) _JellySkinStride;
                float3 p[4];
                [unroll] for (uint i = 0; i < 4; i++)
                {
                    uint3 raw = _JellySkin.Load3((base + i) * stride);
                    // NaN and infinity by their bits: the compiler folds a float test for NaN away.
                    if (any((raw & 0x7f800000) == 0x7f800000)) return false;
                    p[i] = asfloat(raw);
                }
                float3 x = (p[1] - p[0]) * 10, y = (p[2] - p[0]) * 10, z = (p[3] - p[0]) * 10;
                if (!(dot(x, x) > 1e-8)) return false;
                f = float4x4(x.x, y.x, z.x, p[0].x, x.y, y.y, z.y, p[0].y, x.z, y.z, z.z, p[0].z, 0, 0, 0, 1);
                return true;
            }

            // Region r's anchor. One riding a bone its cloth swings is handed the bone, and its place
            // on it is here: parented under the bone, it joined the cloth's chain as a bone of its own.
            // The bone as the cloth swings it comes only through skinning: the provider reads bones
            // in a script LateUpdate, after the cloth put them back to rest and before it writes them,
            // so every region it handed stayed still on the chest. The region's frame on its bone
            // against the same frame where it hangs, both as last skinned, is the swing, a frame late,
            // put on the chest as it is now, so the body's own move is not late.
            float4x4 AnchorOf(uint r)
            {
                float4x4 a = _JellyAnchor[min(r, 3)];
                float4 at = r == 0 ? _JellyOffsetAt0 : _JellyOffsetAt1, q = r == 0 ? _JellyOffsetTurn0 : _JellyOffsetTurn1;
                if (r > 1 || !(at.w > 0)) return a;
                float3x3 t = Turn(q) * at.w;
                a = mul(a, float4x4(float4(t[0], at.x), float4(t[1], at.y), float4(t[2], at.z), float4(0, 0, 0, 1)));
                // Both in the helper's space, which their difference does not see. Kept only at its own
                // scale and within half a metre: the squash's skinned slots reached remote copies
                // metres off.
                float4x4 on, still;
                [branch] if (_JellySkinStride > 0 && Skinned(8 * r, on) && Skinned(8 * r + 4, still))
                {
                    float4x4 swung = mul(Unrigid(still), on);
                    if (abs(length(swung._m00_m10_m20) - 1) < 0.2 && length(swung._m03_m13_m23) < 0.5)
                        return mul(_JellyAnchor[2 + r], swung);
                }
                return a;
            }

            // The twitch: a transform the avatar's animator moves off the first hang, kicking both
            // regions' mass as far as it moved. 0 at rest.
            float3 Twitch() { return _JellyTwitch > 0 ? _JellyAnchor[(uint) _JellyTwitch]._m03_m13_m23 - _JellyAnchor[2]._m03_m13_m23 : 0; }

            // Whether any of a side's probes read pushed last frame: the side row's 87, which the
            // first pass works out beside the readout, so a particle reads one texel, not every probe.
            bool Pushed(uint side) { return _MainTex.Load(int3(87, side, 0)).x > 0.5; }

            // MagicaCloth's probes on region r's hang, as the build put them: back takes the skinned
            // helper's space to the world through the hang as last skinned, put on the hang as it
            // is now. false where there are none or the hang reads spoilt.
            bool Probing(uint r, out float4x4 back)
            {
                float4x4 still = 0;
                bool ok = false;
                back = 0;
                // Branched: an avatar with no probes would still take the hang's sixteen reads.
                [branch] if (_JellyProbes > 0 && _JellySkinStride > 0)
                {
                    ok = Skinned(8 * r + 4, still);
                    back = ok ? mul(_JellyAnchor[2 + r], Unrigid(still)) : 0;
                }
                return ok;
            }

            // Probe k of region r's: where it was pushed to and which way, and how far, 0 for a spoilt
            // read. Each is two points in the helper from 16, itself then its rest on the hang.
            float Probe(uint r, uint k, float4x4 back, out float3 at, out float3 way)
            {
                uint stride = (uint) _JellySkinStride, i0 = 16 + 2 * (r * min((uint) _JellyProbes, (uint) PROBES) + k);
                uint3 rp = _JellySkin.Load3(i0 * stride), rr = _JellySkin.Load3((i0 + 1) * stride);
                float3 d = mul((float3x3) back, asfloat(rp) - asfloat(rr));
                float gone = length(d);
                at = mul(back, float4(asfloat(rp), 1)).xyz;
                way = gone > 1e-6 ? d / gone : 0;
                return any((rp & 0x7f800000) == 0x7f800000) || any((rr & 0x7f800000) == 0x7f800000) || !(gone < 2 * _JellyProbeReach + 0.1) ? 0 : gone;
            }

            // A collider the probes felt: its ball and radius, where on the skin it pressed (its probes'
            // rests weighted by their pushes), how far its leading probe was pushed (0 for none), and
            // which probe that was.
            struct Press { float3 ball; float radius; float3 rest; float deep; uint k; };

            // Probe k of region r's push as Felt reads it, 0 under 2 mm, its tip and its rest. Pushed
            // past most of a reach is swept along by a sliding collider, round its side and out past
            // it, not pressed: one read 12 cm for a ball 3 cm in and rebuilt it 5 cm too deep in a
            // frame. Held to 0.8 of a reach, a press reads at most about 3 cm deep. Solving from where
            // the tips sit instead jumped up to 8 cm a frame as roots flipped.
            float Push(uint r, uint k, float4x4 back, float reach, out float3 tip, out float3 rest)
            {
                float3 way;
                float g = Probe(r, k, back, tip, way);
                rest = tip - way * g;
                return g > 0.002 ? min(g, 0.8 * reach) : 0;
            }

            // The sizes a felt collider is tried at, a fingertip's to a hand's.
            static const float Sizes[8] = { 0.005, 0.0075, 0.01, 0.015, 0.02, 0.03, 0.04, 0.05 };
            #define MEMBERS 8
            #define FREES 16

            // Region r's probes as at most two colliders, so a slot and a ring are each a collider's,
            // not a probe's: within two hands, two probes 8 cm apart took two slots, and the one a
            // collider met slantwise swings along its bone twice as fast as it came, a slow press
            // read as a slap. The furthest pushed leads, and the furthest pushed whose rest is past
            // 1.25 reaches of its rest leads a second; each takes the pushed probes resting within
            // 1.2 reaches of its own. ponytail: two colliders a region; a third reads as one of those.
            // Each one's place over the rests of its probes, weighted by their pushes, a reach less
            // its leader's push out along the skin: a probe pushed g had the collider's skin, a probe
            // radius out from it, come g past its rest. By the tips, which a collider sweeps round it,
            // two balls 12 cm apart shared the probes and each slot sat 3 to 5 cm off its ball; by
            // the rests, 1.5 cm. Put out along one probe's normal, a ball over a face's middle sat 4 cm
            // too far out to dent at all.
            // Its size from the probes round it: each it pushed has its tip a collider radius and a
            // probe radius from the collider's middle, each it left alone at least that far from its
            // rest. Tried at each of Sizes, the middle settled on the pushed tips, the size that fits
            // them best keeping clear of the rest. Every collider was a hand's 5 cm ball before, a
            // finger's dent as wide as a palm's; modelled offline, fingers came out 10 to 15 mm, 15 and
            // 30 mm balls their own size, palms 40. A thin collider's depth between probes is not
            // there to read: a probe on the skin slides round its side, so the depth is the leader's.
            void Felt(uint r, uint upto, float4x4 back, out Press p[2])
            {
                // Out of the body from the middle of the lattice at rest, through each probe's rest.
                float3 mid = mul(_JellyAnchor[2 + r], float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1)).xyz;
                float reach = _Hand + _JellyProbeReach;
                uint n = min(min((uint) _JellyProbes, upto), (uint) PROBES);
                p[0] = (Press) 0;
                p[1] = (Press) 0;
                float best = 0, next = 0;
                float3 from = 0, from2 = 0;
                uint k0 = 0, k1 = 0;
                [loop] for (uint k = 0; k < n; k++)
                {
                    float3 tip, rest;
                    float g = Push(r, k, back, reach, tip, rest);
                    bool more = g > best;
                    from = more ? rest : from;
                    k0 = more ? k : k0;
                    best = max(best, g);
                }
                // Untouched, as nearly always, none of this is looked at: the slaps run it every frame.
                [branch] if (best > 0)
                {
                    [loop] for (uint k = 0; k < n; k++)
                    {
                        float3 tip, rest;
                        float g = Push(r, k, back, reach, tip, rest);
                        bool far = g > next && distance(rest, from) >= 1.25 * reach;
                        from2 = far ? rest : from2;
                        k1 = far ? k : k1;
                        next = far ? g : next;
                    }
                    [loop] for (uint i = 0; i < 2; i++)
                    {
                        float lg = i == 0 ? best : next;
                        float3 lead = i == 0 ? from : from2;
                        [branch] if (lg > 0)
                        {
                            // ponytail: the first MEMBERS pushed in probe order and FREES untouched,
                            // enough for a hand's; a bigger press sizes from those.
                            float3 tips[MEMBERS], frees[FREES];
                            float pull[MEMBERS];
                            uint held = 0, open = 0;
                            float w = 0;
                            float3 rest = 0;
                            [loop] for (uint k = 0; k < n; k++)
                            {
                                float3 tip, kr;
                                float g = Push(r, k, back, reach, tip, kr);
                                float apart = distance(kr, lead);
                                [branch] if (g > 0 && apart < 1.2 * reach)
                                {
                                    w += g;
                                    rest += kr * g;
                                    [branch] if (held < MEMBERS) { tips[held] = tip; pull[held] = g * g; held++; }
                                }
                                else if (g == 0 && apart < 2 * reach && open < FREES) { frees[open] = kr; open++; }
                            }
                            rest /= max(w, 1e-6);
                            float3 outward = normalize(rest - mid), centre = 0;
                            float sum = 0;
                            [loop] for (uint q = 0; q < held; q++) { centre += tips[q] * pull[q]; sum += pull[q]; }
                            centre /= max(sum, 1e-12);
                            float size = _Hand, least = 1e9;
                            [loop] for (uint s = 0; s < 8; s++)
                            {
                                float tried = min(Sizes[s], _Hand), d = tried + _JellyProbeReach;
                                float3 c = centre + outward * d;
                                [loop] for (uint it = 0; it < 6; it++)
                                {
                                    float3 to = 0;
                                    [loop] for (uint q = 0; q < held; q++)
                                    {
                                        float3 v = c - tips[q];
                                        to += (tips[q] + v * (d / max(length(v), 1e-6))) * pull[q];
                                    }
                                    c = to / max(sum, 1e-12);
                                }
                                float miss = 0, into = 0, worst = 0;
                                [loop] for (uint q = 0; q < held; q++) { float e = distance(c, tips[q]) - d; miss += e * e; }
                                [loop] for (uint q = 0; q < open; q++) { float h = max(0, d - distance(c, frees[q])); into += h * h; worst = max(worst, h); }
                                float cost = miss / max(held, 1) + 4 * into / max(open, 1) + 4 * worst * worst;
                                size = cost < least ? tried : size;
                                least = min(least, cost);
                            }
                            p[i].ball = rest + outward * (size + _JellyProbeReach - lg);
                            p[i].radius = size;
                            p[i].rest = rest;
                            p[i].deep = lg;
                            p[i].k = i == 0 ? k0 : k1;
                        }
                    }
                }
            }

            // Worked out once a pixel at the top of the step, as each is read many times over: through
            // skinning each is sixteen reads, and inlined at every caller the anchors were a fifth of
            // the step. Its own region's, Mine; the other's, Theirs, only where it is read. MineOf
            // stays its region where Region changes to pick a region's box.
            static float4x4 Mine, Theirs;
            static uint Region, MineOf;
            float4x4 Anchor(uint r) { return r == MineOf ? Mine : Theirs; }
            // Its own region's probes' frame, as Probing gives it: once a pixel too.
            static float4x4 Back;
            static bool Probed;
            // The second pass's particles, which read the anchor's turn off the side row.
            static bool Turned = false;

            // A world point in an anchor's rest frame, metres from it along its axes.
            float3 Into(float4x4 a, float3 x)
            {
                float3 d = x - a._m03_m13_m23, ax = a._m00_m10_m20;
                return float3(dot(d, ax), dot(d, a._m01_m11_m21), dot(d, a._m02_m12_m22)) / dot(ax, ax);
            }

            // A kept hand from its slot's texels a (wrist, radius) and b (an open hand's tip, 10 plus the
            // gesture, and 10 a millimetre the skin holds it back), as the squash reads one
            // (yaps_dent.cginc): the palm from a to pb, ending at the knuckles as it closes to a fist,
            // and a finger out, thin, from fa to fb of radius rf, 0
            // for none. A pointing finger read as a whole open hand shoved the breast away from it.
            void Hand(float4 a, float4 b, out float3 pb, out float3 fa, out float3 fb, out float rf)
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

            // Whether a slot's hand is a closed fist: its gesture's 0 to 1 past three quarters.
            bool Fist(float4 b) { float g = fmod(b.w - 10, 10); return b.w > 0.5 && g > 0.75 && g < 1.5; }

            // Where a fist meets what it closes on: its knuckles' far side.
            float3 Knuckles(float4 a, float4 b)
            {
                float3 pb, fa, fb;
                float rf;
                Hand(a, b, pb, fa, fb, rf);
                float3 seg = b.xyz - a.xyz;
                return pb + (dot(seg, seg) > 1e-12 ? normalize(seg) : 0) * a.w;
            }

            // Slot e's grip as last frame left it (texel 112 + e): where on the anchor its fist closed
            // on the skin, in at; and how far the hand has pulled that since, on the anchor, capped at
            // half the tissue's depth, w 1; raw, the pull uncapped. 0 with no grip or its hand gone.
            // A grip's spot on the anchor once its fist has moved to k: across the skin, the way back
            // up the hand from its tip, it trails the fist by _GripSlip at most, so a fist stroking
            // along the skin carries the grip with it and the skin drags behind; out of the skin it
            // stays, so pulled out too far it still lets go. 0, the squash settings' Grip slides off,
            // it stays where the fist closed. The way out from the lattice's middle tilted on a flat
            // front, so a stroke read half as a pull and slid half as far.
            float3 Slid(float3 at, float3 k, float3 up)
            {
                up = dot(up, up) > 1e-12 ? normalize(up) : at - float3(0, 0, 0.5 * (Count().z - 1) * _Lattice.w);
                up = dot(up, up) > 1e-12 ? normalize(up) : float3(0, 0, 1);
                float3 d = k - at, across = d - up * dot(d, up);
                float l = length(across);
                return _GripSlip > 0 && l > _GripSlip ? at + across * (1 - _GripSlip / l) : at;
            }

            float4 Pulled(uint e, float4x4 m, uint side, out float3 at, out float raw)
            {
                float4 g = _MainTex.Load(int3(112 + e, side, 0)), a = _MainTex.Load(int3(2 * e, side, 0)), b = _MainTex.Load(int3(2 * e + 1, side, 0));
                at = g.xyz;
                raw = 0;
                if (!(g.w > 0 && b.w > 0.5)) return 0;
                float3 d = Into(m, Knuckles(a, b)) - g.xyz;
                raw = length(d);
                return float4(d * min(1, 0.5 * (Count().z - 1) * _Lattice.w / max(raw, 1e-6)), 1);
            }

            // next on ahead of a capsule from a to b of radius r toward centre, the way a hand
            // presses: pushed out of the hand's nearest side, a particle a little off its axis went
            // round it and the hand passed between them, 35 mm of tissue moved for 130 mm in. One
            // behind the hand's line is left be, as a hand pushes and never pulls. Pushed on through,
            // a particle its spring had flung past the line came out the far side and was flung back,
            // a deep press at 60 frames a second shaking 10 cm a frame; pushed back out behind, the
            // ones a withdrawing hand had passed were dragged out with it, 11 cm past their rest.
            float3 Plow(float3 next, float3 a, float3 b, float r, float3 centre)
            {
                float3 seg = b - a, in_ = centre - (a + 0.5 * seg);
                float3 off = next - (a + seg * saturate(dot(next - a, seg) / max(dot(seg, seg), 1e-9)));
                float d2 = dot(off, off);
                if (d2 >= r * r || !(dot(in_, in_) > 1e-8)) return next;
                float3 way = normalize(in_);
                float along = dot(off, way);
                if (along < 0) return next;
                return next + way * (sqrt(max(along * along - d2 + r * r, 0)) - along);
            }

            // next out of a capsule the nearest way, as the wearer's own body pushes. Plowed toward
            // the centre like a hand, a particle whose rest lay behind a held arm slid round it and
            // was thrown back over the top, 50 mm a frame, round and round. One behind the line from
            // the limb to centre is left be, as hands do: pushed out either way, an arm moving off
            // dragged the tissue it had passed with it, and the breast stuck to it.
            float3 Out(float3 next, float3 a, float3 b, float r, float3 centre)
            {
                float3 seg = b - a;
                float3 off = next - (a + seg * saturate(dot(next - a, seg) / max(dot(seg, seg), 1e-9)));
                float d2 = dot(off, off);
                if (d2 >= r * r || d2 < 1e-12 || dot(off, centre - (a + 0.5 * seg)) < 0) return next;
                return next + off * (r * rsqrt(d2) - 1);
            }

            // The face of the tissue a hand came in by, a local axis, and the tissue's half thickness
            // across it: the face the wrist is over while it is outside; inside, for another player's
            // hand, the one it lies least deep past, of those not facing away from its arm (its
            // player's chest) with its deeper end a radius r short of the middle; else the face a
            // point 30 cm back along the arm is over. That point alone read a player's palm flat on a
            // breast's side, the arm from the front, as in by the front and held it 18 cm out in
            // front; the least deep alone read a hand pushed straight in past the middle as in by a
            // side, held it 15 cm out to it and tore 330 particles. The wearer's own hands keep the
            // point, the wearer's elbow being where the hand came from: by the least deep, a hand
            // pushed in from the front was held there and swung the whole 2 cm back and 2 cm out.
            float3 Facing(float3 wrist, float3 tip, float r, bool other, float4x4 m, float scale, out float3 mid, out float across)
            {
                float4 box = Region == 0 ? _JellyBox0 : _JellyBox1;
                float3 half = box.z > 0 ? float3(box.xy, 0.5 * box.z) : 0.5 * (float3) (Count() - 1) * _Lattice.w;
                mid = float3(0, 0, half.z);
                float3 wl = Into(m, wrist), tl = Into(m, tip), q = (wl - mid) / half, least = 1e9, way = 1;
                if (max(abs(q.x), max(abs(q.y), abs(q.z))) < 1 && dot(wl - tl, wl - tl) > 1e-12)
                {
                    // How far the deeper end lies past each face, a face more than 60 degrees off
                    // the arm left out.
                    float3 arm = normalize(wl - tl);
                    float3 inPlus = arm < -0.5 ? 1e9 : half - (min(wl, tl) - mid);
                    float3 inMinus = arm > 0.5 ? 1e9 : half + (max(wl, tl) - mid);
                    least = min(inPlus, inMinus);
                    way = inPlus <= inMinus ? 1 : -1;
                    least = other && least <= half - r / scale ? least : 1e9;
                    q = (Into(m, wrist + normalize(wrist - tip) * 0.3 * scale) - mid) / half;
                }
                float3 face = min(least.x, min(least.y, least.z)) < 1e8
                    ? (least.x <= least.y && least.x <= least.z ? float3(way.x, 0, 0) : least.y <= least.z ? float3(0, way.y, 0) : float3(0, 0, way.z))
                    : (abs(q.x) >= abs(q.y) && abs(q.x) >= abs(q.z) ? float3(sign(q.x), 0, 0) : abs(q.y) >= abs(q.z) ? float3(0, sign(q.y), 0) : float3(0, 0, sign(q.z)));
                across = dot(abs(face), half);
                return face;
            }

            // How deep the skin lies under face at l, a point on the anchor as Into gives it: the
            // region's depth map between its spots. 0 with no map, the face then taken for the skin.
            float Skin(float3 face, float3 l)
            {
                float4 box = Region == 0 ? _JellyBox0 : _JellyBox1;
                if (!(box.w > 0.5) || !(box.z > 0)) return 0;
                float3 half = float3(box.xy, 0.5 * box.z), q = (l - float3(0, 0, half.z)) / half;
                uint k = abs(face.x) > 0.5 ? 0 : abs(face.y) > 0.5 ? 1 : 2;
                float2 uv = k == 0 ? q.yz : k == 1 ? q.xz : q.xy;
                float2 g = saturate(0.5 * uv + 0.5) * (DEPTH_GRID - 1);
                int2 g0 = min((int2) g, DEPTH_GRID - 2);
                float2 t = g - g0;
                int row = (int) ((Region * 6 + 2 * k + (face.x + face.y + face.z < 0 ? 1 : 0)) * DEPTH_GRID) + g0.y;
                float a = _JellyDepth.Load(int3(g0.x, row, 0)), b = _JellyDepth.Load(int3(g0.x + 1, row, 0));
                float c = _JellyDepth.Load(int3(g0.x, row + 1, 0)), d = _JellyDepth.Load(int3(g0.x + 1, row + 1, 0));
                // A map that reads as no depth inside the box is taken as no map: one saved as
                // garbage on a tester's machine read -4e8, and every hand was held 4e8 m away.
                float skin = lerp(lerp(a, b, t.x), lerp(c, d, t.x), t.y);
                return skin >= 0 && skin < 2 * dot(abs(face), half) ? skin : 0;
            }

            // A hand past _Deepest into the skin, or a third of the tissue, is held there, as skin
            // would stop a real one; in VR it goes on through, and once its tip is past the tissue's
            // middle it lets go over its radius, as a body a hand has passed into springs back.
            // Followed in, a hand 11 cm in dragged 44 particles over 10 cm and one 17 cm in 120, the
            // tissue torn, and again as it was pulled back out. hold is how far the skin keeps it
            // short of where it is, which the whole still feels (Unheld): skin that stops a hand is
            // pushed along by it, and with the mass held too a squeeze no longer met in the middle.
            // The face is judged with the hand at from: for another player's, where the same hand was
            // held a step before, just past the face it came in by however deep it goes. Judged where
            // it was, a finger pushed in deep from behind read as in by the top once past the middle,
            // was held 8.6 cm up and dented the skin above it, none under it. The tip has its own
            // radius rTip, a finger's when one is out: by the palm's, a finger 2.6 cm into a seat read
            // as at its 7.9 cm and was held there, the rest of a poke sinking into the skin.
            // Both depths are past the skin, which lies under the face by the region's depth map: past
            // the face, a poke on a seat used its whole 7.9 cm before the finger met the skin 8 cm under
            // it, and dented nothing. The middle it lets go at, and a third of the tissue, are the box's.
            void Held(inout float3 wrist, inout float3 tip, float r, float rTip, bool other, float3 fromWrist, float3 fromTip, float4x4 m, float scale, out float hold)
            {
                float3 mid;
                float across;
                float3 face = Facing(fromWrist, fromTip, r, other, m, scale, mid, across);
                hold = 0;
                if (!any(face) || !(r > 0)) return;
                float3 lw = Into(m, wrist), lt = Into(m, tip);
                float pastW = across - dot(lw - mid, face), pastT = across - dot(lt - mid, face), skin = Skin(face, lt);
                float inW = (pastW - Skin(face, lw)) * scale, inT = (pastT - skin) * scale;
                float deep = max(inW + r, inT + rTip);
                float most = min(_Deepest * scale, 2 * max(across - skin, 0) * scale / 3), go = saturate((max(pastW, pastT) - across) * scale / r);
                float back = max(deep - most, 0) + most * go;
                // Never more than it is held back: short of skin under the middle, deep is below 0.
                hold = clamp(back - deep * go, 0, back);
                float3 out_ = normalize(mul((float3x3) m, face));
                wrist += out_ * back;
                tip += out_ * back;
            }

            // A held hand back where it is, less only its letting go: what moves the whole.
            void Unheld(inout float3 wrist, inout float3 tip, float r, bool other, float hold, float4x4 m, float scale)
            {
                float3 mid;
                float across;
                float3 face = Facing(wrist, tip, r, other, m, scale, mid, across);
                float3 in_ = any(face) ? normalize(mul((float3x3) m, face)) * hold : 0;
                wrist -= in_;
                tip -= in_;
            }

            // Which particles ride the anchor: the back face, on the chest wall; on a limb, those within
            // the core's radius of its bone, as a thigh's flesh rides its bone all round.
            bool Pinned(int3 l)
            {
                if (!Cored) return l.z == 0;
                float4 a = Region == 0 ? _JellyCoreA0 : _JellyCoreA1, b = Region == 0 ? _JellyCoreB0 : _JellyCoreB1;
                float3 r = Rest(l), s = b.xyz - a.xyz;
                return distance(r, a.xyz + s * saturate(dot(r - a.xyz, s) / max(dot(s, s), 1e-12))) < a.w;
            }

            // The wearer's own capsule c of the sixteen, end to end after the four anchors: the left
            // upper arm, forearm, hand and thigh, then the right's, bone to bone; then the avatar's
            // colliders, each between its two ends. r 0 for none.
            void Own(uint c, float scale, out float3 a, out float3 b, out float r)
            {
                uint s = c < 8 ? 4 + (c >> 2) * 6 + ((c & 3) < 3 ? (c & 3) : 4) : 2 * c;
                a = _JellyAnchor[s]._m03_m13_m23;
                b = _JellyAnchor[s + 1]._m03_m13_m23;
                r = (c < 4 ? _JellyOwn0[c & 3] : c < 8 ? _JellyOwn1[c & 3] : c < 12 ? _JellyCol0[c & 3] : _JellyCol1[c & 3]) * scale;
                // The limb a region rides lies through it and moves with it, so it presses only the other.
                if (c + 1 == (uint) round(Region == 0 ? _JellyCarrier.x : _JellyCarrier.y)) r = 0;
            }

            // How much of the pair's plane holds, from the turn between the two frames, the partner's
            // as its row last wrote it. Built side by side and turned alike, the plane is where they
            // press; a limb turned across the other, as a thigh crossed over its partner some 55 to 75
            // degrees off it, lay past the plane with nothing there to meet and was pressed into a
            // blade. All of it to 40 degrees, none from 55; knees pressed together turn 31.
            float Meet(float4x4 m, uint ps)
            {
                float4 x = _MainTex.Load(int3(26, ps, 0)), y = _MainTex.Load(int3(27, ps, 0)), z = _MainTex.Load(int3(28, ps, 0));
                if (!(x.w > 0.5)) return 1;
                float t = dot(normalize(m._m00_m10_m20), normalize(x.xyz)) + dot(normalize(m._m01_m11_m21), normalize(y.xyz)) + dot(normalize(m._m02_m12_m22), normalize(z.xyz));
                return smoothstep(0.5736, 0.7660, 0.5 * (t - 1));
            }

            // Whether the pair press on each other as Moved's plane has them: a cloth swinging them
            // together touches like a hand, so the hold lets go.
            bool Meeting(float4x4 m, float scale, uint side)
            {
                if (!(_Pair > 0 && _PairReach.x > 0)) return false;
                uint rows = side - Base, other = Region ^ 1, ps = other * (rows + 1) + rows;
                float zc = 0.5 * (Count().z - 1) * _Lattice.w;
                float4 ta = _MainTex.Load(int3(30, side, 0)), tb = _MainTex.Load(int3(30, ps, 0));
                float3 ca = ta.w > 0 ? ta.xyz : mul(m, float4(0, 0, zc, 1)).xyz;
                float3 cb = tb.w > 0 ? tb.xyz : mul(AnchorOf(other), float4(0, 0, zc, 1)).xyz;
                // 2 mm in: at rest they sit at exactly the distance they were built at, and the float
                // noise either side of it let the hold go every other step while standing.
                return (length(_PairOffset.xyz) - _PairReach.z) * scale - 0.002 * scale > distance(ca, cb) && Meet(m, ps) > 0;
            }

            // How far a capsule from a to b of radius r leans the mass's spring target: c of its
            // overlap with the mass at rest, centred on p, out of the hand, fading to nothing within a
            // quarter of the reach of a hand lying through the centre, which pushes no way more than
            // another.
            float3 Lean(float3 p, float3 a, float3 b, float r, float radius, float c)
            {
                float3 seg = b - a;
                float3 off = p - (a + seg * saturate(dot(p - a, seg) / max(dot(seg, seg), 1e-9)));
                float d = length(off);
                return d < r + radius ? off * (c * (r + radius - d) / max(d, 0.25 * (r + radius))) : 0;
            }

            // How far a capsule from a to b of radius r is off the skin: off its box on the anchor (the
            // lattice's, where none is set), less its radius and however far the mass has swung off its
            // rest. Steps + 1 points along the capsule, none more than half a step off any point of it.
            float Gap(float3 a, float3 b, float r, float4x4 m, float scale, uint side, int steps = 8)
            {
                float4 box = Region == 0 ? _JellyBox0 : _JellyBox1;
                float3 half = box.z > 0 ? float3(box.xy, 0.5 * box.z) : 0.5 * (float3) (Count() - 1) * _Lattice.w, mid = float3(0, 0, half.z);
                float4 mass = _MainTex.Load(int3(30, side, 0));
                float swung = mass.w > 0 ? distance(mass.xyz, mul(m, float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1)).xyz) : 0;
                // Into is affine, so the ends taken in once and the points between found there: a point
                // at a time, its 128 hands' worth timed the compiler out.
                float3 ia = Into(m, a) - mid, ib = Into(m, b) - mid;
                float most = 1e9;
                [unroll] for (int k = 0; k <= steps; k++) most = min(most, length(max(abs(lerp(ia, ib, k / (float) steps)) - half, 0)));
                return most * scale - r - swung - distance(a, b) / (2 * steps);
            }

            // Whether it can reach the skin: within 8 cm, for what a press bulges out of the box. A sphere
            // round the lattice took in the thighs below it, so every particle and breast vertex tested
            // them with the arms down.
            bool InReach(float3 a, float3 b, float r, float4x4 m, float scale, uint side, int steps = 8) { return Gap(a, b, r, m, scale, side, steps) <= 0.08 * scale; }

            // How far the wearer's capsules in reach have moved against the anchor since the state at
            // side was stepped: their ends as it kept them (texels 42 to 57 and 64 to 79) on its anchor
            // then (26 to 29), against where they are now on m. Fresh, the reach is judged again and
            // one newly in it counts as a metre; otherwise those in reach then, which the count, run
            // every frame, can afford.
            float Shifted(float4x4 m, float scale, uint side, bool fresh)
            {
                float4 x0 = _MainTex.Load(int3(26, side, 0)), y0 = _MainTex.Load(int3(27, side, 0)), z0 = _MainTex.Load(int3(28, side, 0)), o0 = _MainTex.Load(int3(29, side, 0));
                float4x4 last = float4x4(x0.x, y0.x, z0.x, o0.x, x0.y, y0.y, z0.y, o0.y, x0.z, y0.z, z0.z, o0.z, 0, 0, 0, 1);
                uint was = (uint) _MainTex.Load(int3(41, side, 0)).x;
                float most = 0;
                [loop] for (uint c = 0; c < 16; c++)
                {
                    float3 a, b;
                    float r;
                    Own(c, scale, a, b, r);
                    bool kept = (was & (1u << c)) != 0;
                    [branch] if (r > 0 && (fresh ? InReach(a, b, r, m, scale, side) : kept))
                    {
                        uint t = c < 8 ? 42 + 2 * c : 48 + 2 * c;
                        float3 ka = _MainTex.Load(int3(t, side, 0)).xyz, kb = _MainTex.Load(int3(t + 1, side, 0)).xyz;
                        float moved = max(length(Into(m, a) - Into(last, ka)), length(Into(m, b) - Into(last, kb))) * scale;
                        most = max(most, kept ? moved : 1);
                    }
                }
                return most;
            }

#if defined(JELLY_SCAN) || defined(JELLY_POINTS)
            // The client's first is the local player's head, then the others': heads press as hands do.
            float4 _CVR_PlayerHeadPositions[64];
#else
            // The client's first is the local player's head; only it is read.
            float4 _CVR_PlayerHeadPositions[1];
#endif

            // The client's rendering buffer: where the local player's camera is and how it is turned,
            // a frame late. Unbound, as in the editor or on a client without it, it reads zeros and
            // nothing counts as out of view.
            struct CVRRenderingData
            {
                int renderingCamera;
                int renderingMirroredView;
                int renderingCullingMask;
                float3 renderingCameraPos;
                float3 renderingPlayerCameraPos;
                float4 renderingPlayerCameraRot;
                float3 padding;
            };
            StructuredBuffer<CVRRenderingData> _CVRRenderingBuffer;

            // Well behind the viewer: past _Near of the camera and more than _Behind degrees off where
            // it looks, less the angle size takes up there. Waking comes back through the parser's
            // readback a few frames late, so it takes ten degrees more to go than to come back.
            bool Unseen(float3 at, float size, bool was)
            {
                if (!(_Behind > 0)) return false;
                CVRRenderingData d = _CVRRenderingBuffer[0];
                float4 q = d.renderingPlayerCameraRot;
                if (abs(dot(q, q) - 1) > 0.01) return false;
                float3 look = float3(2 * (q.x * q.z + q.w * q.y), 2 * (q.y * q.z - q.w * q.x), 1 - 2 * (q.x * q.x + q.y * q.y));
                float3 to = at - d.renderingPlayerCameraPos;
                float gone = length(to);
                if (!(gone > _Near)) return false;
                float off = degrees(acos(clamp(dot(look, to / gone), -1, 1))) - degrees(asin(saturate(size / gone)));
                return off > _Behind + (was ? 0 : 10);
            }

            // Far from the viewer's head or well behind their camera, judged on the first region for
            // every region alike, half a metre's give on the distance so it does not flicker.
            // Held on its bones, untouched (_Hold): stepped and scanned still, so a hand wakes it.
            bool Held() { return Ease >= 1 && Ease < 1.5; }

            bool Away(bool was)
            {
                float4x4 a = Anchor(0);
                float3 head = _CVR_PlayerHeadPositions[0].xyz, at = a._m03_m13_m23;
                return dot(head, head) > 1e-6 && distance(head, at) > _Far - (was ? 0.5 : 0)
                    || Unseen(at, 2 * length(float3(Count()) * _Lattice.w) * length(a._m00_m10_m20), was);
            }

            // Past _Half of the viewer's head the parser runs the steps at _Rate.y, slower still
            // where the jiggle's own rate does not show.
            // With a quarter metre of give either way of _Half from where it stood last step: a viewer
            // standing at the edge flipped the rate, and the tissue's feel, back and forth.
            bool Halved(bool was)
            {
                float3 head = _CVR_PlayerHeadPositions[0].xyz;
                return _Half > 0 && dot(head, head) > 1e-6 && distance(head, Anchor(0)._m03_m13_m23) > _Half + (was ? -0.25 : 0.25);
            }

#if defined(JELLY_SCAN) || defined(JELLY_POINTS)
            // The first 64 of the client's 255, all the scan reads: the whole arrays made every first
            // pass upload 16 kB of constants.
            float4 _CVR_PlayerLeftHandPositions[64];
            float4 _CVR_PlayerRightHandPositions[64];
            float4 _CVR_PlayerChestPositions[64];
            float4 _CVR_PlayerRootPositions[64];
            float4 CVRGlobalParams1;

            // Player who's head as a ball: the client gives the head bone, at the skull's base, so its
            // middle is put up from it, away from their chest. w 0 with no head.
            float4 HeadOf(int who, float tall)
            {
                float3 h = _CVR_PlayerHeadPositions[who].xyz, c = _CVR_PlayerChestPositions[who].xyz;
                // An avatar with no chest bone mapped hands a zero there.
                float3 up = h - (dot(c, c) > 1e-6 ? c : _CVR_PlayerRootPositions[who].xyz);
                if (!(dot(h, h) >= 1e-6)) return 0;
                return float4(h + (dot(up, up) > 1e-6 ? normalize(up) : 0) * (0.08 * tall / 1.6), 0.1 * tall / 1.6);
            }

            // Whether player who is the wearer, both hands where the own hand capsules start: the
            // wearer is in the arrays too, on every machine, and their hands already press as the own
            // capsules, rimless. Counted again, a hand of their own pressed twice.
            bool Wearer(int who)
            {
                return distance(_CVR_PlayerLeftHandPositions[who].xyz, _JellyAnchor[6]._m03_m13_m23) < 0.05
                    && distance(_CVR_PlayerRightHandPositions[who].xyz, _JellyAnchor[12]._m03_m13_m23) < 0.05;
            }

            // Hand slot texel x of the eight, two texels each, (wrist, radius) then (an open hand's
            // tip, 10 plus the gesture), an empty one 0. Each looks through every player's hands and keeps the one it is due, so a
            // pass reads eight however many players there are.
            float4 Scan(int x, float4x4 m, float scale, uint side, bool felt, Press presses[2])
            {
                int slot = x >> 1;
                int3 n = Count();
                // A sphere round the lattice at rest, with room for it to bulge.
                float3 centre = mul(m, float4(0, 0, 0.5 * (n.z - 1) * _Lattice.w, 1)).xyz;
                float reach = (0.5 * length(float3(n - 1)) * _Lattice.w + 0.05) * scale;
                int players = (Region == 0 ? _JellyArrays.x : _JellyArrays.y) > 0.5 ? min((int) round(CVRGlobalParams1.y), 64) : 0;
                int found = 0;
                float4 kept = 0, kw = 0, kt = 0;
                // Every player's hands as the squash reads them: from the wrist, away from that
                // player's chest, a ninth of their viewpoint height. Rolled: written out for all 64,
                // the hands were a fifth of a step every player's driver took seconds to build the
                // first time it saw one. Shader Filtering cuts a rolled loop short, but player 0 is
                // the local player, so a viewer's own hands always count (on the wearer's machine it is
                // the wearer, skipped as Wearer). Most players are passed over on where they stand,
                // since a hand is never further from its player's feet than half as much again as
                // their height.
                [loop] for (int who = 0; who < players; who++)
                {
                    float4 root = _CVR_PlayerRootPositions[who];
                    float tall = root.w > 0.1 ? root.w : 1.6;
                    float3 to = centre - root.xyz;
                    float around = reach + 1.5 * tall + 0.5;
                    [branch] if (dot(to, to) < around * around && !Wearer(who))
                    {
                        float r = _Hand * tall / 1.6;
                        float3 chest = _CVR_PlayerChestPositions[who].xyz;
                        [unroll] for (int hand = 0; hand < 2; hand++)
                        {
                            float4 held = hand == 0 ? _CVR_PlayerLeftHandPositions[who] : _CVR_PlayerRightHandPositions[who];
                            float3 wrist = held.xyz;
                            float3 arm = wrist - chest;
                            float3 tip = wrist + (dot(arm, arm) > 1e-6 ? normalize(arm) : 0) * max(0.11 * tall - r, 0);
                            // ponytail: eight hands at most, the first in player order; a crowd round one
                            // jelly needs more slots.
                            // Twice the radius, the skin's rim the draw eases out round a hand. Three
                            // points: nine, when this was written out 128 times, timed the compiler out.
                            bool hit = dot(wrist, wrist) >= 1e-6 && InReach(wrist, tip, 2 * r, m, scale, side, 2);
                            kw = hit && found == slot ? float4(wrist, r) : kw;
                            kt = hit && found == slot ? float4(tip, 10 + clamp(held.w, 0, 9.9)) : kt;
                            found += hit;
                        }
                    }
                }
                // Then the heads, after every hand: slots go by order, and a grip lives in its slot, so
                // a head coming into reach among the hands moved a later hand to another slot and broke
                // its grip. Each is kept as an open hand of no length, a ball, which never grips.
                [loop] for (int who = 0; who < players; who++)
                {
                    float4 root = _CVR_PlayerRootPositions[who];
                    float tall = root.w > 0.1 ? root.w : 1.6;
                    float3 to = centre - root.xyz;
                    float around = reach + tall + 0.5;
                    [branch] if (dot(to, to) < around * around && !Wearer(who))
                    {
                        float4 head = HeadOf(who, tall);
                        bool hit = head.w > 0 && InReach(head.xyz, head.xyz, 2 * head.w, m, scale, side, 1);
                        kw = hit && found == slot ? head : kw;
                        kt = hit && found == slot ? float4(head.xyz, 10) : kt;
                        found += hit;
                    }
                }
                // Held once, for the hand this slot keeps: inside the loop it was 128 copies, too many to unroll.
                [branch] if (kt.w > 0.5)
                {
                    float3 w = kw.xyz, t = kt.xyz, pb, fa, fb;
                    float rf, hold;
                    // The slot's hand a step before, if it is this one: within its hold and a step's
                    // move of where it was held.
                    float4 pw = _MainTex.Load(int3(2 * slot, side, 0)), pt = _MainTex.Load(int3(2 * slot + 1, side, 0));
                    bool same = pt.w > 0.5 && distance(w, pw.xyz) < floor((pt.w - 10) / 10) / 1000 + 0.15 * scale;
                    // Opening and closing eases in over a fifth of a second. Taken at once, a hand deep
                    // in the tissue shortened or grew by 5 cm in a frame, and a gesture flickering
                    // between open and fist, as tracked fingers do, shoved the part 3 cm each frame
                    // and started a grip on every close.
                    float gp = fmod(pt.w - 10, 10), gt = kt.w - 10;
                    if (same && gp < 1.5 && gt < 1.5) kt.w = 10 + gp + clamp(gt - gp, -5 * FrameDt, 5 * FrameDt);
                    // Held by as far as the hand reaches as drawn, a fist's knuckles or a finger out:
                    // by the open fingertips, a fist closing on the skin sat 3 cm off it, and gripped
                    // only if the frame it closed on caught it still in.
                    Hand(kw, kt, pb, fa, fb, rf);
                    float3 reach = rf > 0 ? fb : pb, was = w;
                    float3 fw = same ? pw.xyz : w;
                    Held(w, reach, kw.w, rf > 0 ? rf : kw.w, true, fw, fw + reach - w, m, scale, hold);
                    t += w - was;
                    // How far it is held back rides on the gesture, 10 a millimetre: 10 + gesture + 10 k.
                    kept = (x & 1) == 0 ? float4(w, kw.w) : float4(t, kt.w + 10 * round(1000 * hold));
                }
                // Then where MagicaCloth's colliders pushed the probes, each a hand's ball whose side
                // is the point it pushed: pushed out its nearest way, a probe lies on the collider's
                // skin, the collider on past it the way it came from. Its push is on the hang, as
                // last skinned, put on the hang as it is now. Felt before the dispatch.
                [branch] if (felt)
                {
                    // Felt leaves two pressing at most, taken in their leaders' probe order so two
                    // presses alike never swap slots frame to frame. Every probe tested in turn
                    // timed the compiler out.
                    uint first = presses[1].deep > 0 && presses[1].k < presses[0].k ? 1 : 0;
                    [loop] for (uint j = 0; j < 2; j++)
                    {
                        uint i = j ^ first;
                        bool hit = false;
                        float3 led = presses[i].ball;
                        float rad = presses[i].radius;
                        [branch] if (presses[i].deep > 0)
                        {
                            // A point, so its two ends do.
                            hit = InReach(led, led, rad, m, scale, side, 1);
                            // A collider on a hand the arrays gave last frame is that hand, which has
                            // its slot: the client puts a capsule on every player's hands. The ball
                            // sits up to a reach out from the collider, the way its probe was pushed.
                            [loop] for (uint e = 0; e < 8; e++)
                            {
                                float4 ha = _MainTex.Load(int3(2 * e, side, 0)), hb = _MainTex.Load(int3(2 * e + 1, side, 0));
                                float3 seg = hb.xyz - ha.xyz;
                                float3 p = ha.xyz + seg * saturate(dot(led - ha.xyz, seg) / max(dot(seg, seg), 1e-12));
                                hit = hit && !(hb.w > 0.5 && dot(seg, seg) > 1e-12 && distance(led, p) < ha.w + rad + _JellyProbeReach);
                            }
                        }
                        kept = hit && found == slot ? ((x & 1) == 0 ? float4(led, rad) : float4(led, 10)) : kept;
                        found += hit;
                    }
                }
                return kept;
            }
#endif

#ifdef JELLY_POINTS
            // What the avatar's animator is told, 3 to 5, through the parser and an animator driver:
            // the most loaded region's strain, 1 while any fist grips, and the last slap's depth
            // against the deepest, fading over its life. 0 while the step is off.
            float4 Feedback(uint x)
            {
                uint rows = Rows(), regions = (uint) round(_Regions);
                float felt = 0;
                // ponytail: two regions, a pair; a second pair needs four here.
                [unroll] for (uint r = 0; r < 2; r++)
                [branch] if (r < regions)
                {
                    uint side = r * (rows + 1) + rows;
                    if (!(abs(_Time.y - _MainTex.Load(int3(25, side, 0)).z) < 0.1)) continue;
                    if (x == 3) felt = max(felt, _MainTex.Load(int3(86, side, 0)).x);
                    else if (x == 4) { [unroll] for (uint e = 0; e < 8; e++) if (_MainTex.Load(int3(112 + e, side, 0)).w > 0) felt = 1; }
                    else [unroll] for (uint k = 0; k < 2; k++)
                    {
                        float age = _Time.y - _MainTex.Load(int3(80 + 2 * k, side, 0)).w;
                        felt = max(felt, saturate(_MainTex.Load(int3(81 + 2 * k, side, 0)).w / _Slap.z) * saturate(1 - age / _Slap.w));
                    }
                }
                return float4(felt, 0, 0, 1);
            }

            // How far point p is off region r's box on its anchor m, in metres.
            float BoxGap(float3 p, float4x4 m, float scale, uint r)
            {
                float4 box = r == 0 ? _JellyBox0 : _JellyBox1;
                float3 half = box.z > 0 ? float3(box.xy, 0.5 * box.z) : 0.5 * (float3) (Count() - 1) * _Lattice.w, mid = float3(0, 0, half.z);
                return length(max(abs(Into(m, p) - mid) - half, 0)) * scale;
            }

            // Every region resting by its own count, and nothing changed since its last step: the
            // anchor within 1 cm and 3 degrees of where it was, nothing of the wearer's in reach moved
            // a centimetre against it or come into reach, and no player's hand where the scan would
            // keep it, its wrist, middle or tip within 8 cm of either box. The steps' blitters are off
            // meanwhile, so this pass alone wakes them. The players walked as the scan walks them.
            bool Resting()
            {
                uint rows = Rows(), regions = (uint) round(_Regions);
                int players = min((int) round(CVRGlobalParams1.y), 64);
                bool resting = regions > 0;
                float4x4 on[2];
                float scales[2];
                float3 mid = 0;
                // ponytail: two regions, a pair; a second pair needs four here.
                [unroll] for (uint r = 0; r < 2; r++)
                {
                    // The second's here, past the far switch: built for the parser's pass, a far prop cost a fifth more.
                    float4x4 m = r == 0 || regions < 2 ? Mine : AnchorOf(1);
                    float scale = length(m._m00_m10_m20);
                    on[r] = m;
                    scales[r] = scale;
                    mid += 0.5 * m._m03_m13_m23;
                    [branch] if (r < regions)
                    {
                        uint side = r * (rows + 1) + rows;
                        float4 x0 = _MainTex.Load(int3(26, side, 0)), z0 = _MainTex.Load(int3(28, side, 0)), o0 = _MainTex.Load(int3(29, side, 0));
                        Region = r;
                        resting = resting && _MainTex.Load(int3(18, side, 0)).y > 0.5 && scale > 1e-6 && distance(m._m03_m13_m23, o0.xyz) < 0.01 * scale
                            && dot(normalize(m._m00_m10_m20), normalize(x0.xyz)) > 0.9986 && dot(normalize(m._m02_m12_m22), normalize(z0.xyz)) > 0.9986
                            && Shifted(m, scale, side, true) < 0.01 * scale;
                    }
                }
                float reach = (length(float3(Count() - 1)) * _Lattice.w + 0.3) * scales[0];
                bool near = false;
                // Rolled, as the scan's: player 0, the local player, always counts (the wearer, on
                // their own machine, skipped as Wearer).
                [loop] for (int who = 0; who < players; who++)
                {
                    float4 root = _CVR_PlayerRootPositions[who];
                    float tall = root.w > 0.1 ? root.w : 1.6;
                    float3 to = mid - root.xyz;
                    float around = reach + 1.5 * tall + 0.5;
                    [branch] if (dot(to, to) < around * around && !Wearer(who))
                    {
                        float hr = _Hand * tall / 1.6;
                        float3 chest = _CVR_PlayerChestPositions[who].xyz;
                        [unroll] for (int hand = 0; hand < 2; hand++)
                        {
                            float3 wrist = (hand == 0 ? _CVR_PlayerLeftHandPositions[who] : _CVR_PlayerRightHandPositions[who]).xyz, arm = wrist - chest;
                            float3 tip = wrist + (dot(arm, arm) > 1e-6 ? normalize(arm) : 0) * max(0.11 * tall - hr, 0);
                            [unroll] for (uint r = 0; r < 2; r++)
                            {
                                float gap = min(min(BoxGap(wrist, on[r], scales[r], r), BoxGap(0.5 * (wrist + tip), on[r], scales[r], r)), BoxGap(tip, on[r], scales[r], r));
                                near = near || dot(wrist, wrist) >= 1e-6 && gap < 2 * hr + 0.08 * scales[r];
                            }
                        }
                        float4 head = HeadOf(who, tall);
                        [unroll] for (uint q = 0; q < 2; q++)
                            near = near || head.w > 0 && BoxGap(head.xyz, on[q], scales[q], q) < 2 * head.w + 0.08 * scales[q];
                    }
                }
                // Or a probe pushed, a collider's press.
                [loop] for (uint q = 0; q < 2; q++)
                {
                    float4x4 back;
                    [branch] if (q < regions)
                    [branch] if (Probing(q, back))
                    {
                        [loop] for (uint k = 0; k < (uint) _JellyProbes && k < PROBES; k++)
                        {
                            float3 at, way;
                            near = near || Probe(q, k, back, at, way) > 0.002;
                        }
                    }
                }
                return resting && !near;
            }
#endif

            // The whole's own squash the homes take, from texels 32 to 34, a column each: the identity
            // when there is none.
            float3x3 Shape(uint side)
            {
                float4 x = _MainTex.Load(int3(32, side, 0)), y = _MainTex.Load(int3(33, side, 0)), z = _MainTex.Load(int3(34, side, 0));
                float3x3 shape = transpose(float3x3(x.xyz, y.xyz, z.xyz));
                // Bad or folded, every home was bad with it, so even a reset landed bad.
                if (!(_Solid > 0) || !(x.w > 0) || Bad(x) || Bad(y) || Bad(z) || !(determinant(shape) > 0.2)) return float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
                return shape;
            }

            // Particle l's place on the anchor's rest frame with the whole's squash taken: none at the
            // back face, on the chest wall, all of it at the front.
            float3 Shaped(int3 l, float3x3 shape)
            {
                float3 r = Rest(l);
                return r + (float) l.z / max(Count().z - 1, 1) * (mul(shape, r) - r);
            }

            // How that stretches the rest round l: its gradient there.
            float3x3 ShapedAt(int3 l, float3x3 shape)
            {
                float zmax = max(Count().z - 1, 1);
                float3x3 grow = shape - float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
                float3 g = mul(grow, Rest(l)) / (zmax * _Lattice.w);
                return float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1) + (float) l.z / zmax * grow + float3x3(0, 0, g.x, 0, 0, g.y, 0, 0, g.z);
            }

            // The region's mass, as one piece: its centre, the lattice's at rest, swings on the root,
            // the anchor's place. Each particle's home turns with it, more the nearer the front, so the
            // back face stays on the chest wall, and follows its centre in and out at constant volume.
            // Off, or not stepped this tenth of a second, home is the rest on the anchor alone. swing is
            // the turn the particle's home took.
            float3 HomeOn(int3 l, float4x4 m, float4 tip, bool swings, float3x3 shape, out float4 swing)
            {
                float3 local = mul((float3x3) m, Shaped(l, shape));
                float3 root = m._m03_m13_m23;
                swing = float4(0, 0, 0, 1);
                if (!swings) return root + local;
                int3 n = Count();
                float3 rest = mul((float3x3) m, float3(0, 0, 0.5 * (n.z - 1) * _Lattice.w)), now = tip.xyz - root;
                float len0 = max(length(rest), 1e-6), len = max(length(now), 1e-6);
                float3 a = rest / len0, b = now / len;
                float w = (float) l.z / max(n.z - 1, 1);
                swing = normalize(lerp(float4(0, 0, 0, 1), normalize(float4(cross(a, b), 1 + dot(a, b))), w));
                float s = lerp(1, len / len0, w), along = dot(local, a);
                local = a * (along * s) + (local - a * along) * rsqrt(s);
                return root + mul(Turn(swing), local);
            }

            // The solid, once a frame in the first pass, column k of the squash the homes take: the best
            // linear map from a 4 x 4 x 4 of the particles' rest places to where they are on the anchor,
            // its turn taken out and its volume made the rest's, _Solid of it, the rest the rest. Flesh
            // barely changes volume, so squeezed one way it bulges the others, most where nothing holds
            // it, and let go it comes back. A pressure in its place swelled the whole evenly, into the
            // squeezing hands too, as a balloon would. w is the volume the map had, kept for the trace.
            // The map is of the particles, which sit on the homes, so it holds the homes' own squash:
            // taking _Solid of it outright, a 3 cm poke flattened the whole part to 0.87 of its depth and
            // kept sinking while held. Weighted _Solid / (1 + _Solid), the homes end near _Solid of what
            // the hands do: about 0.9 of it at 0.7, as the homes' squash ramps in with depth and the fit
            // sees part of it.
            // Other players' palms squeezing the whole flatten it along their line at constant volume,
            // so it grows up and forward. Fitted from the particles alone, a hard squeeze read as a
            // hand-shaped hole that averaged to almost no squash, and the skin closed round the
            // hands. Worked out fresh from where the hands are each frame, so nothing builds up;
            // half at most. The finger is left out, so a poke still dents rather than flattens.
            float3x3 Squeezed(float4x4 m, float scale, uint side)
            {
                float3x3 id = float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
                if (!(_Squeeze > 0) || !(_MainTex.Load(int3(1, side, 0)).w > 0.5)) return id;
                float radius = _MassRadius * scale;
                float3 rest = mul(m, float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1)).xyz, at = Into(m, rest);
                float3x3 s = 0;
                [loop] for (int e = 0; e < 8; e++)
                {
                    float4 a = _MainTex.Load(int3(2 * e, side, 0)), b = _MainTex.Load(int3(2 * e + 1, side, 0));
                    if (!(b.w > 0.5)) continue;
                    if (_MainTex.Load(int3(112 + e, side, 0)).w > 0) continue;
                    float3 ua = a.xyz, ub = b.xyz;
                    Unheld(ua, ub, a.w, true, floor((b.w - 10) / 10) / 1000, m, scale);
                    a.xyz = ua;
                    b.xyz = ub;
                    float3 pb, fa, fb;
                    float rf;
                    Hand(a, b, pb, fa, fb, rf);
                    float3 leaned = Lean(rest, a.xyz, pb, a.w, radius, 1);
                    float l = length(leaned);
                    if (!(l > 1e-6)) continue;
                    float3 n = normalize(Into(m, rest + leaned) - at);
                    s += l / (2 * radius) * float3x3(n.x * n, n.y * n, n.z * n);
                }
                float t = _Squeeze * (s[0][0] + s[1][1] + s[2][2]);
                if (!(t > 1e-6)) return id;
                float3x3 h = id - s * (_Squeeze * min(1, 0.5 / t));
                float v = determinant(h);
                return v > 0.1 ? h * pow(v, -1.0 / 3.0) : id;
            }

            float4 Bulk(int k, float4x4 m, float scale, bool distant, uint side, bool predicted)
            {
                float4 none = float4(k == 0, k == 1, k == 2, 1);
                if (distant) return none;
                // Snapped, the particles still lie where the jump left them: last frame's squash kept.
                if (Snapped) return _MainTex.Load(int3(32 + k, side, 0));
                float3x3 hands = Squeezed(m, scale, side);
                float4 squeezed = float4(transpose(hands)[k], 1);
                int3 n = Count();
                // Each particle against its home with no squash: fitted to the rest alone, the mass's
                // swing and stretch, which turn the front more than the back, read as a shear.
                float4 tip = _MainTex.Load(int3(30, side, 0)), unused;
                bool swings = _MassHz > 0 && tip.w > 0 && abs(_Time.y - tip.w) < 0.1;
                // On the anchor they were stepped on: the particles and the mass are last frame's, and
                // against this frame's the mass lagged a frame's move, which read as a stretch along
                // it. Fed back through the homes, the tissue sat 53 mm ahead at a run.
                // ponytail: one pass only. Under 72 fps the two whole passes leave the front a frame
                // behind for a reason not found yet, and this frame's anchor happens to cancel it
                // (25 fps walk: 15 mm against 65 mm fitted on the stepped one). Queue item 49.
                float4 x0 = _MainTex.Load(int3(26, side, 0)), y0 = _MainTex.Load(int3(27, side, 0)), z0 = _MainTex.Load(int3(28, side, 0)), o0 = _MainTex.Load(int3(29, side, 0));
                float4x4 on = predicted && o0.w > 0.5 ? float4x4(x0.x, y0.x, z0.x, o0.x, x0.y, y0.y, z0.y, o0.y, x0.z, y0.z, z0.z, o0.z, 0, 0, 0, 1) : m;
                float3 pc = 0, qc = 0, qq = 0, c0 = 0, c1 = 0, c2 = 0;
                // Summed as each is loaded, not held, so the pass keeps few registers. Rolled, and
                // divided by as many as it took: Shader Filtering cuts it short, to the back layers.
                float taken = 0;
                [loop] for (int i = 0; i < 64; i++)
                {
                    int3 l = int3(round(float3(i % 4, (i / 4) % 4, i / 16) * (n - 1) / 3.0));
                    float3 q = Rest(l), p = Into(on, _MainTex.Load(Texel((uint) Index(l), 0)).xyz)
                        + q - Into(on, HomeOn(l, on, tip, swings, hands, unused));
                    pc += p;
                    qc += q;
                    qq += q * q;
                    c0 += p * q.x;
                    c1 += p * q.y;
                    c2 += p * q.z;
                    taken++;
                }
                pc /= max(taken, 1);
                qc /= max(taken, 1);
                // Over a box of rest places the rest's own spread is diagonal, an axis a column.
                float3 spread = qq - taken * qc * qc;
                if (!all(spread > 1e-12)) return squeezed;
                c0 = (c0 - taken * pc * qc.x) / spread.x;
                c1 = (c1 - taken * pc * qc.y) / spread.y;
                c2 = (c2 - taken * pc * qc.z) / spread.z;
                float4 q = float4(0, 0, 0, 1);
                [unroll] for (int it = 0; it < 4; it++)
                {
                    float3x3 t = Turn(q);
                    float3 t0 = float3(t[0][0], t[1][0], t[2][0]), t1 = float3(t[0][1], t[1][1], t[2][1]), t2 = float3(t[0][2], t[1][2], t[2][2]);
                    float3 spin = (cross(t0, c0) + cross(t1, c1) + cross(t2, c2)) / (abs(dot(t0, c0) + dot(t1, c1) + dot(t2, c2)) + 1e-12);
                    float w = length(spin);
                    if (w > 1e-9) q = normalize(Mul(float4(spin / w * sin(0.5 * w), cos(0.5 * w)), q));
                }
                float3x3 a = mul(transpose(Turn(q)), transpose(float3x3(c0, c1, c2)));
                a = 0.5 * (a + transpose(a));
                float volume = determinant(a);
                // A bad volume passed the range test, which the compiler turns inside out.
                if (!(volume > 0.3 && volume < 3) || Bad(float4(a[0], volume)) || Bad(a[1]) || Bad(a[2])) return squeezed;
                float share = _Solid / (1 + _Solid);
                float3x3 shape = mul(hands, (1 - share) * float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1) + share * a * pow(volume, -1.0 / 3.0));
                return float4(transpose(shape)[k], volume);
            }

            // A point as last frame's anchor held it, put where this frame's holds it.
            float3 Carried(float3 x, float4x4 m, uint side)
            {
                float4 x0 = _MainTex.Load(int3(26, side, 0)), y0 = _MainTex.Load(int3(27, side, 0)), z0 = _MainTex.Load(int3(28, side, 0)), o0 = _MainTex.Load(int3(29, side, 0));
                if (!(o0.w > 0.5)) return x;
                float4x4 last = float4x4(x0.x, y0.x, z0.x, o0.x, x0.y, y0.y, z0.y, o0.y, x0.z, y0.z, z0.z, o0.z, 0, 0, 0, 1);
                return mul(m, float4(Into(last, x), 1)).xyz;
            }

            // A predicted step's right half as the first pass handed it on, was, made where the particle
            // was: home for one the left half, s, marks put back at rest, and carried over a snap.
            float4 Handed(float4 s, float4 was, float4x4 m, uint side)
            {
                if (!(s.w > 0.5)) return float4(s.xyz, was.w);
                [branch] if (_MainTex.Load(int3(85, side, 0)).w < 0) was.xyz = Carried(was.xyz, m, side);
                return was;
            }

            float3 Home(int3 l, float4x4 m, uint side, out float4 swing)
            {
                float4 tip = _MainTex.Load(int3(30, side, 0));
                // Snapped, the first pass still has last frame's mass: on the jump with the anchor.
                [branch] if (Snapped && _Pass < 0.5) tip.xyz = Carried(tip.xyz, m, side);
                // Stamped and recent: a cleared texel reads as stepped at time 0, and at a scene's first
                // frame that passed, swinging every home toward the world's origin.
                return HomeOn(l, m, tip, _MassHz > 0 && tip.w > 0 && abs(_Time.y - tip.w) < Stale, Shape(side), swing);
            }

            // How fast particle l's home moves: from last frame's anchor and mass (texels 26 to 29 and
            // 31) to this one's, over the frame. 0 before there was a last frame.
            // ponytail: the cloth's swing is this frame's for both, so the stretch's own rate is
            // missing; keep last frame's hang in the side row if the stretch lags.
            float3 HomeSpeed(int3 l, uint side, float3 home)
            {
                float4 x = _MainTex.Load(int3(26, side, 0)), y = _MainTex.Load(int3(27, side, 0)), z = _MainTex.Load(int3(28, side, 0));
                float4 o = _MainTex.Load(int3(29, side, 0)), tip = _MainTex.Load(int3(30, side, 0)), was = _MainTex.Load(int3(31, side, 0));
                if (!(o.w > 0.5) || !(FrameDt > 0)) return 0;
                float4x4 last = float4x4(x.x, y.x, z.x, o.x, x.y, y.y, z.y, o.y, x.z, y.z, z.z, o.z, 0, 0, 0, 1);
                // Carried, 31 holds last frame's mass moved on with the anchor: put back on last frame's.
                if (_MassCarried > 0) was.xyz = mul(last, float4(Into(Anchor(Region), was.xyz), 1)).xyz;
                float4 unused;
                return (home - HomeOn(l, last, was, _MassHz > 0 && tip.w > 0 && abs(_Time.y - tip.w) < Stale, Shape(side), unused)) / FrameDt;
            }

            // Where a particle is headed from s, where it was was (w that step's time): its home's own
            // move kept whole; what it does apart from that damped by _Viscous. Flesh is viscous, so a
            // dent fills back without ringing while the mass's swing carries the whole. The home's move
            // damped too was a drag on the body's own: the tissue trailed 17 cm on a breast at 10 m/s.
            float3 Ahead(float4 s, float4 was, int3 l, uint side, float3 home, float h, out float3 vh)
            {
                float3 v = (s.xyz - was.xyz) / max(was.w, 1e-4);
                // Snapped, the jump is no speed of the home's: the particle was carried over it.
                vh = Snapped ? v : HomeSpeed(l, side, home);
                float3 own = (v - vh + Kick) * exp(-_Viscous * was.w) * (Hot >= 2 ? 0.5 : 1);
                // Its own speed against its home held to 4 m/s at the anchor's scale: a runaway was
                // only ever caught once a particle was a metre out.
                own *= min(1, 4 * Scale / max(length(own), 1e-9));
                return s.xyz + (vh + own) * h;
            }

            // The mass's step, once a frame by the whole frame time, in the first pass: texel 30 its
            // centre (w the _Time.y it stepped), 31 where it was (w that step's time). Verlet under
            // gravity; a spring of _MassHz toward its rest, held up by the sag gravity gives when the
            // tissue was modelled hanging; pushed out of the hands in reach by _Give of how far they
            // reach into it, the particles left to dent the rest; kept off its partner's centre, each
            // taking half; its swing held within _Limit of its length, in and out within a tenth.
            // The hands are the slots the last frame's scan kept.
            float4 Mass(int x, float4x4 m, float scale, float dt, bool distant, uint side, uint region, uint partnerSide)
            {
                float4 tip = _MainTex.Load(int3(30, side, 0)), was = _MainTex.Load(int3(31, side, 0));
                // Carried, both are moved on with the anchor since last frame, so only the hands move
                // the mass on it: lagging a bone its cloth swings, it swung the whole a second time.
                [branch] if (_MassCarried > 0 || Snapped)
                {
                    float4 x0 = _MainTex.Load(int3(26, side, 0)), y0 = _MainTex.Load(int3(27, side, 0)), z0 = _MainTex.Load(int3(28, side, 0)), o0 = _MainTex.Load(int3(29, side, 0));
                    if (o0.w > 0.5)
                    {
                        float4x4 last = float4x4(x0.x, y0.x, z0.x, o0.x, x0.y, y0.y, z0.y, o0.y, x0.z, y0.z, z0.z, o0.z, 0, 0, 0, 1);
                        tip.xyz = mul(m, float4(Into(last, tip.xyz), 1)).xyz;
                        was.xyz = mul(m, float4(Into(last, was.xyz), 1)).xyz;
                    }
                }
                float3 root = m._m03_m13_m23;
                float zc = 0.5 * (Count().z - 1) * _Lattice.w;
                float3 rest = mul(m, float4(0, 0, zc, 1)).xyz;
                bool fresh = distant || Held() || Bad(tip) || Bad(was) || Hot >= 8 || !(tip.w > 0 && abs(_Time.y - tip.w) < Stale) || !(was.w > 0 && was.w < 0.1) || !(distance(tip.xyz, rest) < Reach);
                if (x == 31) return float4(fresh ? rest : tip.xyz, dt);
                if (fresh) return x == 86 ? 0 : float4(rest, _Time.y);
                // Strained, as last frame's 86 has it, flesh goes taut: pulled out by a fist or
                // pressed and squeezed, it swings stiffer and settles sooner, from moving, shaking or
                // a slap alike.
                float4 strained = _MainTex.Load(int3(86, side, 0));
                float taut = strained.w > 0 && abs(_Time.y - strained.w) < 0.1 ? _Taut * strained.x : 0;
                // Carried, the cloth gives the weight its sag, so the mass takes none of its own.
                float3 next = tip.xyz + (tip.xyz - was.xyz) * (exp(-_MassDamp * (1 + 3 * taut) * dt) * dt / max(was.w, 1e-4)) + (_MassCarried > 0 ? 0 : Gravity * dt * dt + Kick * dt);
                float spring = 6.2831853 * _MassHz * dt;
                // How hard it is loaded: each push and pull on it by its size, so a squeeze from both
                // sides, which leaves the whole where it was, loads it twice over.
                float load = 0;
                // The hands move where the spring holds it, a force, not the mass itself: pushed out
                // a share of the way each frame, it left any hand within a few frames and the
                // particles nothing to dent. Each takes give of its overlap with the mass at rest,
                // where it settles. Taken where the mass was, at give / (1 - give), it settled the
                // same but fed back: past a give of a half two hands squeezing buzzed at 37 frames a
                // second or fewer, and fingertips level with a centre tipped one back 25 mm.
                float radius = _MassRadius * scale;
                float3 pushed = 0;
                [branch] if (_MainTex.Load(int3(1, side, 0)).w > 0.5)
                {
                    [loop] for (int e = 0; e < 8; e++)
                    {
                        float4 a = _MainTex.Load(int3(2 * e, side, 0)), b = _MainTex.Load(int3(2 * e + 1, side, 0));
                        if (!(b.w > 0.5)) continue;
                        if (_MainTex.Load(int3(112 + e, side, 0)).w > 0) continue;
                        float3 ua = a.xyz, ub = b.xyz;
                        Unheld(ua, ub, a.w, true, floor((b.w - 10) / 10) / 1000, m, scale);
                        a.xyz = ua;
                        b.xyz = ub;
                        // A finger out leans by as much less as it is thinner, as the tissue's stiffness
                        // under a contact goes with its radius.
                        float3 pb, fa, fb;
                        float rf, c = min(_Give, 0.95);
                        Hand(a, b, pb, fa, fb, rf);
                        float3 leaned = Lean(rest, a.xyz, pb, a.w, radius, c) + (rf > 0 ? Lean(rest, fa, fb, rf, radius, c * rf / a.w) : 0);
                        pushed += leaned;
                        // The load as if all of the press moved it: a part its cloth carries takes a give
                        // of 0, and its Squeeze read 0 under any hand.
                        load += length(Lean(rest, a.xyz, pb, a.w, radius, 1) + (rf > 0 ? Lean(rest, fa, fb, rf, radius, rf / a.w) : 0));
                    }
                    // Each grip pulls the whole by the share of a press it takes, the skin it holds
                    // following the hand the rest of the way.
                    [loop] for (uint gi = 0; gi < 8; gi++)
                    {
                        float3 gat;
                        float graw;
                        float4 d = Pulled(gi, m, side, gat, graw);
                        float3 pull = mul((float3x3) m, d.xyz) * d.w;
                        pushed += pull * min(_Give, 0.95);
                        load += length(pull);
                    }
                }
                // The wearer's own hands too; their arms and thighs only dent it, or an arm resting
                // against its side would shove the whole away.
                uint own = (uint) _MainTex.Load(int3(41, side, 0)).x & 0x44u;
                [branch] if (own != 0)
                {
                    float c = min(_Give, 0.95);
                    [loop] for (uint k = 2; k < 8; k += 4)
                    {
                        [branch] if (own & (1u << k))
                        {
                            float3 a, b;
                            float r;
                            Own(k, scale, a, b, r);
                            float hold;
                            Held(a, b, r, r, false, a, b, m, scale, hold);
                            Unheld(a, b, r, false, hold, m, scale);
                            float3 leaned = Lean(rest, a, b, r, radius, 1);
                            pushed += leaned * c;
                            load += length(leaned);
                        }
                    }
                }
                // Strained half way at a load a tenth of its length, wholly well past it.
                if (x == 86) return float4(load / (load + 0.1 * zc * scale), load, 0, _Time.y);
                // A twitch kicks it as a hand would, never loading it.
                pushed += Twitch();
                spring *= 1 + taut;
                // Free, lifted by its own sag whatever _Lift says of the tissue: a prop's tissue sags
                // onto its plate, and a mass let sag dragged the whole down after it. Taken at the taut
                // spring: lifted for the slack one, a press sprang the whole 10 cm up.
                float3 lift = !(_MassCarried > 0) ? normalize(m._m01_m11_m21) * 9.81 * _Gravity * (1 - spring * spring) / (39.478418 * _MassHz * _MassHz * (1 + taut) * (1 + taut)) : 0;
                next = lerp(next, rest + lift + pushed, min(spring * spring, 1));
                [branch] if (_Pair > 0)
                {
                    float4 other = _MainTex.Load(int3(30, partnerSide, 0));
                    // As far apart as they sat as built: from where the anchors are now, two breasts
                    // the cloth swung together read as resting that close.
                    // Kept apart across the seam they rested at and free along it: kept apart along
                    // the line between their centres, a squeeze at their middles sheared them past
                    // each other, one up and one down, as two balls pressed together slip.
                    float keep = _PairKeep * length(_PairOffset.xyz) * scale;
                    float3 seam = normalize(mul((float3x3) m, _PairOffset.xyz));
                    float s = dot(next - other.xyz, seam), side_ = s < 0 ? -1 : 1;
                    // Not over a snap: the partner's mass is still where the jump left it, and the
                    // push shoved this one 6 cm in a step, the tissue flung after it.
                    if (!Snapped && other.w > 0 && abs(_Time.y - other.w) < 0.1 && abs(s) < keep) next += seam * (0.5 * (side_ * keep - s));
                }
                float len0 = zc * scale;
                float3 off = next - root;
                float len = max(length(off), 1e-6);
                next = root + off * (clamp(len, 0.9 * len0, 1.1 * len0) / len);
                float3 swung = next - rest;
                float most = _Limit * len0;
                if (dot(swung, swung) > most * most) next = rest + swung * (most / length(swung));
                return float4(next, _Time.y);
            }

            // Where particle p, at l, goes this pass, and its neighbourhood's turn: toward the rigid
            // fit of its neighbourhood, toward its rest on the anchor, and out of any hand in reach.
            // With predicted, where each particle is headed is the first pass's; otherwise it is
            // worked out here from both halves.
            float3 Moved(uint p, int3 l, float4x4 m, float scale, float h, float3 home, bool predicted, bool distant, uint side, float4 swing, out float4 q)
            {
                float4 turn;
#ifndef JELLY_SCAN
                // A second pass's particle takes the anchor's turn the first pass's side row worked out (91).
                [branch] if (Turned) turn = _MainTex.Load(int3(91, side, 0)); else
#endif
                turn = Quat((float3x3) m / scale);
                q = Mul(swing, turn);
                // The back face rides the anchor, or on a limb the core round its bone.
                if (Pinned(l) || distant || Held()) return home;
                float4 s = _MainTex.Load(Texel(p, 0));
                float3 ahead, base;
                float kh = 0;
                float3 drift = 0;
                if (predicted)
                {
                    // The first pass's mark: 0 for a particle it put back at rest.
                    if (!(s.w > 0.5)) return home;
                    ahead = base = s.xyz;
                }
                else
                {
                    float4 was = _MainTex.Load(Texel(p, 1));
                    // Carried over a snap once, by the first pass; the second steps on from there.
                    [branch] if (Snapped && _Pass < 0.5) { s.xyz = Carried(s.xyz, m, side); was.xyz = Carried(was.xyz, m, side); }
                    if (Fresh(s, was, home)) return home;
                    // Every right-half w is the h of the same pass, so this holds for the neighbours too,
                    // each headed on by this home's speed, which they share, and its own apart from it.
                    float keep = exp(-_Viscous * was.w);
                    float3 vh;
                    kh = keep / max(was.w, 1e-4) * h;
                    base = Ahead(s, was, l, side, home, h, vh);
                    drift = vh * (h * (1 - keep)) + Kick * (h * keep);
                    ahead = base + Gravity * h * h;
                }
                float3 c, rc;
                float4 lin;
                // The rest round l as the homes have it, the whole's squash taken. Held to the plain
                // rest while the homes moved, each particle was pulled two ways, and the lattice's sides
                // flickered a frame apart under a held press.
                float3x3 sw = ShapedAt(l, Shape(side));
                q = Fit(p, l, ahead, base, kh, drift, m, h, home, q, predicted, sw, c, rc, lin);
                // Rigid, or keeping _Squish of its neighbourhood's own squash and stretch at constant
                // volume, so flesh pressed in one way bulges out the others.
                float3 rigid = mul(Turn(q), mul(sw, Rest(l) - rc) * scale);
                float3 goal = c + lerp(rigid, lin.xyz, _Squish * lin.w);
                // _Stiff is the pull over a 180th of a second, so the tissue feels the same
                // whether a frame takes one pass or two.
                float3 next = lerp(ahead, goal, 1 - pow(1 - _Stiff, h * 180));
                // And the whole of it toward its rest on the anchor, as a spring of _Hz: the
                // neighbourhoods alone keep the shape near each particle but let a block 16
                // across fold under its own weight, 42 cm at the front.
                float spring = 6.2831853 * _Hz * h;
                // Upright, gravity hangs the tissue g (1 - spring^2) / w^2 below where this pull holds
                // it; tissue modelled on a body hangs already, so it is held as far above, against the
                // world's up however the anchor lies. Along the anchor's up the two cancelled only
                // upright: upside down they added, 84 mm on a breast, and a seated thigh lifted along
                // its bone sat 32 mm off its modelled shape. It keeps its shape and still swings.
                float3 lift = 0;
                if (_Lift > 0) lift = _Lift * float3(0, 1, 0) * 9.81 * _Gravity * (1 - spring * spring) / (39.478418 * _Hz * _Hz);
                // The pair meet at a plane between them, moving and tilting with them, with no friction,
                // so they slide along it and never stick: they push together.
                // Each home past it is eased back behind it in order, the face it meets flat, and the
                // whole grows across by the volume that takes, so what is pressed flat goes up, down and
                // forward, never into nothing. Read from the
                // masses, where each breast is as a whole; only closer than they sat as built do they
                // press, each taking half. Before, each read the other's homes through its body's
                // distance field: the lens between them was pressed to half and lost, and a hard push
                // passed through.
                float3 held = home, pn = 0;
                float pd = 0, meet = 0;
                bool plane = false;
                [branch] if (_Pair > 0 && _PairReach.x > 0)
                {
                    uint rows = side - Base, other = Region ^ 1, ps = other * (rows + 1) + rows;
                    float zc = 0.5 * (Count().z - 1) * _Lattice.w;
                    float4 ta = _MainTex.Load(int3(30, side, 0)), tb = _MainTex.Load(int3(30, ps, 0));
                    float3 ca = ta.w > 0 ? ta.xyz : mul(m, float4(0, 0, zc, 1)).xyz;
                    float3 cb = tb.xyz;
                    // Its own copy: a particle never has the other's anchor to hand.
                    [branch] if (!(tb.w > 0)) cb = mul(AnchorOf(other), float4(0, 0, zc, 1)).xyz;
                    float dist = distance(ca, cb), wa = (other == 1 ? _PairReach.x : _PairReach.y) * scale;
                    float e = 0.5 * max((length(_PairOffset.xyz) - _PairReach.z) * scale - dist, 0);
                    meet = e > 0 ? Meet(m, ps) : 0;
                    [branch] if (e > 0 && dist > 1e-4 && meet > 0)
                    {
                        plane = true;
                        pn = (cb - ca) / dist;
                        pd = dot(ca, pn) + wa - e;
                        // Across, the cap past the plane of a ball the breast's half width given back,
                        // none at the chest wall.
                        float cap = e * e * (3 * wa - e) / (4 * wa * wa * wa), w = (float) l.z / max(Count().z - 1, 1);
                        float3 from = held - ca;
                        held += (from - pn * dot(from, pn)) * ((rsqrt(max(1 - cap, 0.5)) - 1) * w);
                        // Behind the plane by a soft minimum with it, so layers keep their order and the
                        // face is flat to a twelfth of the half width; eased in as they meet.
                        float a = dot(held, pn) - pd, soft = 0.08 * wa;
                        float eased = min(a, 0) - soft * log(1 + exp(-abs(a) / soft));
                        held += pn * ((eased - a) * saturate(e / soft));
                        held = lerp(home, held, meet);
                    }
                }
                next = lerp(next, held + lift, min(spring * spring, 1));
                // The hands found in reach, each from the wrist to its tip with a radius. Slots fill in
                // order, so an empty first one means there are none. Rolled, as every loop here over
                // slots, capsules and probes: Shader Filtering cuts them short, the first slots first.
                // Each goes on ahead of the palm and any finger out, toward the region's centre.
                [branch] if (_MainTex.Load(int3(1, side, 0)).w > 0.5)
                {
                    float4 mass = _MainTex.Load(int3(30, side, 0));
                    float3 centre = mass.w > 0 ? mass.xyz : mul(m, float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1)).xyz;
                    [loop] for (int e = 0; e < 8; e++)
                    {
                        float4 a = _MainTex.Load(int3(2 * e, side, 0)), b = _MainTex.Load(int3(2 * e + 1, side, 0));
                        if (!(b.w > 0.5)) continue;
                        // A gripping hand carries the skin below rather than pushing it.
                        if (_MainTex.Load(int3(112 + e, side, 0)).w > 0) continue;
                        float3 pb, fa, fb;
                        float rf;
                        Hand(a, b, pb, fa, fb, rf);
                        // Let go deep in the tissue, the hand pushes it out over a fifth of a second:
                        // at once, a fist opening 12 cm in threw the skin it held 36 mm in a frame.
                        float3 before = next;
                        next = Plow(next, a.xyz, pb, a.w, centre);
                        if (rf > 0) next = Plow(next, fa, fb, rf, centre);
                        next = lerp(before, next, saturate((_Time.y - _MainTex.Load(int3(120 + e, side, 0)).w) / 0.2));
                    }
                    // Each grip takes the skin it closed on along with the hand: all of it at the
                    // grip, none a hand's breadth off, the neighbourhoods' fit bending what lies
                    // between. Let go, the tissue's own spring brings it back. None can grip, one read (92).
                    [branch] if (_MainTex.Load(int3(92, side, 0)).x > 0.5)
                    [loop] for (uint gi = 0; gi < 8; gi++)
                    {
                        float3 gat;
                        float graw;
                        float4 d = Pulled(gi, m, side, gat, graw);
                        [branch] if (d.w > 0)
                        {
                            float w = smoothstep(2 * _MainTex.Load(int3(2 * gi, side, 0)).w / scale, 0, distance(Rest(l), gat));
                            // Its rest on the anchor carried by the pull, so the skin under the fist
                            // sits at the fist: on its home, the whole's lean after the hand counted twice.
                            next = lerp(next, lerp(home, mul(m, float4(Rest(l) + d.xyz, 1)).xyz, w), w);
                        }
                    }
                }
                // The wearer's own arms, hands and thighs in reach. None in reach, one read.
                uint own = (uint) _MainTex.Load(int3(41, side, 0)).x;
                [branch] if (own != 0)
                {
                    float4 mass = _MainTex.Load(int3(30, side, 0));
                    float3 centre = mass.w > 0 ? mass.xyz : mul(m, float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1)).xyz;
                    [loop] for (uint c = 0; c < 16; c++)
                    {
                        [branch] if (own & (1u << c))
                        {
                            float3 a, b;
                            float r;
                            Own(c, scale, a, b, r);
                            float hold;
                            if (c == 2 || c == 6) Held(a, b, r, r, false, a, b, m, scale, hold);
                            next = Out(next, a, b, r, centre);
                        }
                    }
                }
                // A particle the hands drive past the plane goes half the way back to its home's side of
                // it each pass, each to its own home's depth, so the layers keep their order: put on the
                // plane itself, a slab of the lattice lay flat there, skin and what is worn over it on
                // one plane, and the two drew through each other in patches.
                [branch] if (plane)
                {
                    // Never further back than its own home, eased by how hard they press: sent onto
                    // the plane outright, the edge moved 13.6 mm with the pair resting as built.
                    float a = dot(next, pn) - pd, hb = dot(held, pn) - pd;
                    if (a > max(hb, 0)) next -= pn * (0.5 * (a - hb) * meet);
                }
                // A board under it on its anchor, which it rests on: dropped, it sagged through.
                [branch] if (_Floor.x > 0)
                {
                    float3 lp = Into(m, next);
                    if (lp.y < _Floor.y) next = mul(m, float4(lp.x, _Floor.y, lp.z, 1)).xyz;
                }
                // Easing toward rigid while fast: a pull home at up to 20 a second.
                [branch] if (Ease > 0 && Ease < 1.5) next = lerp(next, home, 1 - exp(-20 * Ease * h));
                else if (Ease < 0) next = lerp(next, home, Ease * Ease * (3 + 2 * Ease));
                return next;
            }

            // The texture parser's two texels, each a range between its task's bounds: where the
            // front centre is in the anchor's own space, x, y and z each from -0.5 to 0.5; and how its
            // neighbourhood has turned against the anchor, as Unity's Euler angles, each from -180 to
            // 180. From the same step the front centre's own texel takes, at and q.
            float4 Rider(int x, float4x4 m, float scale, float3 at, float4 q)
            {
                float3x3 r = (float3x3) m / scale;
                if (x == 16) return float4(saturate(mul(transpose(r), at - m._m03_m13_m23) / scale + 0.5), 1);
                float3x3 t = mul(transpose(r), Turn(q));
                // Unity turns by z, then x, then y: t = Ry Rx Rz.
                float3 euler = degrees(float3(asin(clamp(-t[1][2], -1, 1)), atan2(t[0][2], t[2][2]), atan2(t[1][0], t[1][1])));
                return float4(saturate(euler / 360 + 0.5), 1);
            }

            // The anchor's move since it last stepped, at the lattice's far corner, about 0.2 of the
            // anchor's units out.
            float AnchorMoved(float4x4 m, uint side)
            {
                float3 x = _MainTex.Load(int3(20, side, 0)).xyz, y = _MainTex.Load(int3(21, side, 0)).xyz, at = _MainTex.Load(int3(22, side, 0)).xyz;
                return distance(at, m._m03_m13_m23) + 0.2 * (distance(x, m._m00_m10_m20) + distance(y, m._m01_m11_m21));
            }

            // How long the tissue has stood still, in summed frame times so it holds at any frame
            // rate: the rider's texels a frame apart, the front centre under 2 mm/s and its
            // neighbourhood's turn under a degree a second, and the anchor under 2 mm/s. Read off
            // the second pass's own row, since a stillness test in the first, beside the hand scan,
            // slowed every step a fast GPU took near other players.
            float4 Still(float4 here, float4x4 m, float scale, float dt, bool distant, uint side)
            {
                float3 front = _MainTex.Load(int3(16, side, 0)).xyz - _MainTex.Load(int3(23, side, 0)).xyz;
                float3 turn = _MainTex.Load(int3(17, side, 0)).xyz - _MainTex.Load(int3(24, side, 0)).xyz;
                bool still = !distant && AnchorMoved(m, side) < 0.002 * dt && length(front) * scale < 0.002 * dt && length(turn) * 360 < dt;
                return float4(still ? here.x + dt : 0, 0, 0, 1);
            }

            // How long the region has lain still with no hand near, in x, counted at _Time.y in y: no
            // hand kept, nothing of the wearer's in reach moving 2 cm a second against the anchor
            // (texel 37, the first pass's), and nine particles of the front moving under 1 cm a second
            // against it. Still, not at rest, so an arm resting on it rests with its dent. Counted
            // afresh after any gap, as when the parser switched the steps off, so a wake needs _Rest
            // of quiet before it rests again. Predicted, the first pass's places carry a step of
            // gravity the fit takes back, 1.2 mm a frame at 90: taken off here.
            float4 Settling(float4 here, float4x4 m, float scale, float dt, bool distant, uint side, bool predicted, float3 fall)
            {
                float4 x0 = _MainTex.Load(int3(26, side, 0)), y0 = _MainTex.Load(int3(27, side, 0)), z0 = _MainTex.Load(int3(28, side, 0)), o0 = _MainTex.Load(int3(29, side, 0));
                float4x4 last = float4x4(x0.x, y0.x, z0.x, o0.x, x0.y, y0.y, z0.y, o0.y, x0.z, y0.z, z0.z, o0.z, 0, 0, 0, 1);
                bool quiet = !distant && o0.w > 0.5 && !(_MainTex.Load(int3(1, side, 0)).w > 0.5) && !Pushed(side) && _MainTex.Load(int3(37, side, 0)).x < 0.02 * scale * dt;
                int3 n = Count();
                [unroll] for (int k = 0; k < 9; k++)
                {
                    uint p = (uint) Index(int3((k % 3) * (n.x - 1) / 2, (k / 3) * (n.y - 1) / 2, n.z - 1));
                    float4 at = _MainTex.Load(Texel(p, 0)), was = _MainTex.Load(Texel(p, 1));
                    [branch] if (predicted) was = Handed(at, was, m, side);
                    float3 now = at.xyz - fall;
                    quiet = quiet && length(Into(m, now) - Into(last, was)) * scale < 0.01 * scale * dt;
                }
                bool counting = here.y > 0 && _Time.y - here.y < 0.1;
                return float4(quiet ? (counting ? here.x : 0) + dt : 0, _Time.y, 0, 1);
            }

            // Whether both passes may leave the tissue be next frame: still long enough, no hand in
            // reach this frame, and the anchor within 0.1 mm of where it last stepped. Decided here,
            // a frame ahead, since a pass cannot read its own row.
            bool Sleepy(float4x4 m, bool distant, uint side)
            {
                // Any slot, a hand or a probe's ball: it was w == 1, from before a slot's w carried
                // 10 and the gesture, so a sleeping prop woke for no hand at all. Or a probe pushed.
                bool hand = _MainTex.Load(int3(1, side, 0)).w > 0.5 || Pushed(side);
                return _Sleep > 0 && !(_Stress > 0) && !distant && !hand && AnchorMoved(m, side) < 1e-4 && _MainTex.Load(int3(19, side, 0)).x >= _Sleep;
            }

            // The slaps, texels 80 to 84, which the draw spreads over the skin as rings on top of the
            // lattice's own give. A slap is a hand reaching the skin faster than
            // _Slap.x metres a second, judged on the skin's box taken as an ellipsoid: 84 keeps the
            // nearest palm's distance off it from last frame, so a hand already resting in fires
            // nothing, nor does the first frame after a gap. Each lands as a ring for the draw (80 and
            // 81, 82 and 83: where and when, then its depth in w, _Slap.y a metre a second up to
            // _Slap.z), in the slot with less left of its own, never over a live stronger one. The
            // hands are the slots the scan wrote last frame, less the probes', which ring below.
            // How fast the anchor moved since the state was last stepped, in metres a second; 0 with
            // no anchor kept.
            float3 AnchorVelocity(float4x4 m, float dt, uint side)
            {
                float4 at = _MainTex.Load(int3(29, side, 0));
                return at.w > 0 ? (m._m03_m13_m23 - at.xyz) / max(dt, 1e-4) : 0;
            }

            float4 Slap(int x, float4x4 m, float scale, float dt, bool distant, uint side, bool felt, Press presses[2])
            {
                float4 box = Region == 0 ? _JellyBox0 : _JellyBox1;
                float3 half = box.z > 0 ? float3(box.xy, 0.5 * box.z) : 0.5 * (float3) (Count() - 1) * _Lattice.w, mid = float3(0, 0, half.z);
                float near = 1e9;
                float3 at = 0;
                [branch] if (!distant && _MainTex.Load(int3(1, side, 0)).w > 0.5)
                {
                    float3 centre = mul(m, float4(mid, 1)).xyz;
                    [loop] for (int e = 0; e < 8; e++)
                    {
                        float4 a = _MainTex.Load(int3(2 * e, side, 0)), b = _MainTex.Load(int3(2 * e + 1, side, 0));
                        // A probe's ball has no length. Taken as a palm, its first frame read as the
                        // nearest palm closing from wherever another one was.
                        if (!(b.w > 0.5) || all(b.xyz == a.xyz)) continue;
                        float3 pb, fa, fb;
                        float rf;
                        Hand(a, b, pb, fa, fb, rf);
                        float3 seg = pb - a.xyz, p = a.xyz + seg * saturate(dot(centre - a.xyz, seg) / max(dot(seg, seg), 1e-9));
                        float3 q = (Into(m, p) - mid) / half;
                        float lq = max(length(q), 1e-6), d = (lq - 1) * min(half.x, min(half.y, half.z)) * scale - a.w;
                        if (d < near)
                        {
                            near = d;
                            at = mul(m, float4(mid + q / lq * half, 1)).xyz;
                        }
                    }
                }
                if (x == 84) return float4(near, 0, 0, _Time.y);
                float4 keep = _MainTex.Load(int3(x, side, 0)), last = _MainTex.Load(int3(84, side, 0));
                float4 r0 = _MainTex.Load(int3(80, side, 0)), s0 = _MainTex.Load(int3(81, side, 0));
                float4 r1 = _MainTex.Load(int3(82, side, 0)), s1 = _MainTex.Load(int3(83, side, 0));
                bool timed = last.w > 0 && _Time.y - last.w < 0.1;
                float speed = (last.x - near) / max(dt, 1e-4);
                bool fired = timed && last.x > 0 && last.x < 0.5 && near <= 0 && speed > _Slap.x;
                // A collider only the probes know of: a probe's push crossing its reach, the collider
                // reaching the skin, timed over the frame as a palm's crossing of the box is. Measured
                // from the skin itself, the slap's end, a film's slap read a third as fast. Last
                // frame's push is the readout's. A ring under 0.2 s old within two reaches
                // is the same collider's: a hand carrying one as well rang through the arrays a few
                // frames before the probes, and as a palm goes in the probe furthest in changes and
                // crosses faster. Such a one deepens that ring and never adds a second.
                int into = -1;
                // Felt before the dispatch, while timed.
                [branch] if (!fired && felt)
                {
                    // The probes read a slap about a third as fast as it came, so they fire at 0.4 of
                    // a hand's speed: a ball slapped in at 3 m/s read 1.05.
                    speed = 0.4 * _Slap.x;
                    float skin = max(_JellyProbeReach, 0.002);
                    [loop] for (uint i = 0; i < 2; i++)
                    [branch] if (presses[i].deep >= skin)
                    {
                        float gone = presses[i].deep, was = _MainTex.Load(int3(READOUT + presses[i].k, side, 0)).w;
                        float rate = (gone - was) / max(dt, 1e-4);
                        float3 rest = presses[i].rest;
                        float apart = 2 * (_Hand + _JellyProbeReach);
                        int same = _Time.y - r0.w < 0.2 && distance(r0.xyz, rest) < apart ? 0 : _Time.y - r1.w < 0.2 && distance(r1.xyz, rest) < apart ? 1 : -1;
                        float had = same == 0 ? s0.w : same == 1 ? s1.w : 0;
                        [flatten] if (was >= 0 && was < skin && gone >= skin && rate > speed && min(_Slap.y * rate, _Slap.z) > had)
                        {
                            speed = rate;
                            at = rest;
                            fired = true;
                            into = same;
                        }
                    }
                }
                // The tissue's own impact: its anchor stopping short, dropped onto something or
                // landing, rings it from the face its own weight carries on into, as a slap into that
                // face would; so does a yank, the tissue lagging into the face it leaves by. Only the
                // slaps that hit it rang it, never it hitting anything. Fired by more than
                // _Landing's change of speed in a frame, at that speed; a frame faster than 8 m/s is a
                // teleport, not a fall. Heard last step (97, 98), rung this one only if the new speed
                // held, and dated back to then: a remote copy's late packet stops its anchor a step
                // and catches up, and rang every time.
                float born = _Time.y;
                [branch] if (!fired && timed && !distant)
                {
                    float4 heard = _MainTex.Load(int3(97, side, 0)), by = _MainTex.Load(int3(98, side, 0));
                    [branch] if (!Snapped && heard.w > 0 && heard.w < _Time.y && _Time.y - heard.w < 0.1 && length(AnchorVelocity(m, dt, side) - heard.xyz) < 0.3 * by.w)
                    {
                        float3 q = Into(m, m._m03_m13_m23 + by.xyz) / half;
                        at = mul(m, float4(mid + q / max(length(q), 1e-6) * half, 1)).xyz;
                        speed = by.w;
                        fired = true;
                        born = heard.w;
                    }
                }
                // A slap that drives one of a pair into the other, their skins within a cell, rings
                // both from where they meet, out from between them, as fast as it drove that way: a
                // ring either row started on the last step from its far side. Their masses close a
                // tenth as fast as the hands that squeeze them, so never struck hard enough to ring.
                [branch] if (!fired && timed && !distant && _Pair > 0 && _PairSkin.x > 0)
                {
                    uint ps = (Region ^ 1) * (side - Base + 1) + side - Base;
                    float4 ta = _MainTex.Load(int3(30, side, 0)), tb = _MainTex.Load(int3(30, ps, 0));
                    float self = (Region == 0 ? _PairSkin.x : _PairSkin.y) * scale, other = (Region == 0 ? _PairSkin.y : _PairSkin.x) * scale;
                    float dist = distance(ta.xyz, tb.xyz);
                    [branch] if (ta.w > 0 && tb.w > 0 && dist > 1e-4 && dist - self - other < _Lattice.w * scale)
                    {
                        float3 u = (tb.xyz - ta.xyz) / dist;
                        float drove = 0;
                        [unroll] for (uint k = 0; k < 4; k++)
                        {
                            uint row = k < 2 ? side : ps;
                            float4 hit = _MainTex.Load(int3(80 + 2 * (k & 1), row, 0));
                            float way = dot(normalize((k < 2 ? ta.xyz : tb.xyz) - hit.xyz), k < 2 ? u : -u);
                            if (hit.w == last.w) drove = max(drove, _MainTex.Load(int3(81 + 2 * (k & 1), row, 0)).w / _Slap.y * way);
                        }
                        [branch] if (drove > _Slap.x)
                        {
                            at = ta.xyz + (tb.xyz - ta.xyz) * (self / (self + other));
                            speed = drove;
                            fired = true;
                        }
                    }
                }
                // A grip let go, or slipped: the skin it held springs back from as far as it was
                // pulled, at the speed the tissue's own spring gives that far out, ringing from where
                // it was gripped. 120 on keep each one's last pull. A pull is held to half the
                // tissue's depth, so on a thin soft part even the furthest pull came back slower than
                // a slap's line and never rang: three quarters of that furthest rings it.
                [branch] if (!fired && timed && !distant)
                {
                    float least = min(_Slap.x, 0.75 * 0.5 * (Count().z - 1) * _Lattice.w * scale * 6.2831853 * _Hz);
                    [loop] for (uint gi = 0; gi < 8; gi++)
                    {
                        float3 gat;
                        float graw;
                        Pulled(gi, m, side, gat, graw);
                        float4 hb = _MainTex.Load(int3(2 * gi + 1, side, 0)), pull = _MainTex.Load(int3(120 + gi, side, 0));
                        bool gripped = _MainTex.Load(int3(112 + gi, side, 0)).w > 0;
                        float back = length(pull.xyz) * scale * 6.2831853 * _Hz;
                        [flatten] if (gripped && (!Fist(hb) || graw > (Count().z - 1) * _Lattice.w) && back > max(fired ? speed : 0, least))
                        {
                            at = mul(m, float4(gat, 1)).xyz;
                            speed = back;
                            fired = true;
                        }
                    }
                }
                if (!fired) return keep;
                // Taut, it rings less.
                float4 strained = _MainTex.Load(int3(86, side, 0));
                float depth = min(_Slap.y * speed, _Slap.z) / (1 + (strained.w > 0 && abs(_Time.y - strained.w) < 0.1 ? _Taut * strained.x : 0));
                float left0 = s0.w > 0 ? s0.w * saturate(1 - (_Time.y - r0.w) / _Slap.w) : 0;
                float left1 = s1.w > 0 ? s1.w * saturate(1 - (_Time.y - r1.w) / _Slap.w) : 0;
                int slot = into >= 0 ? into : left0 <= left1 ? 0 : 1;
                if (into < 0 && min(left0, left1) > depth || (x - 80) / 2 != slot) return keep;
                return (x & 1) == 0 ? float4(at, born) : float4(0, 0, 0, depth);
            }

            // The watchdog (texel 36): nine of the front's particles measured off their rest on the
            // anchor, the mass's swing taken, in the lattice's depths. Out past 1.5, or past 0.6
            // with no hand or probe on it, the region counts as out of control; x the steps running,
            // y how far, z whether any read bad. Nothing read its own state before: a runaway was
            // only caught particle by particle, a metre out.
            float4 Watchdog(float4x4 m, float scale, bool distant, uint side)
            {
                if (distant) return 0;
                int3 n = Count();
                float zc = 0.5 * (n.z - 1) * _Lattice.w, L = max((n.z - 1) * _Lattice.w * scale, 1e-6);
                float4 tip = _MainTex.Load(int3(30, side, 0));
                float3 swing = tip.w > 0 && !Bad(tip) ? tip.xyz - mul(m, float4(0, 0, zc, 1)).xyz : 0;
                float most = 0;
                bool bad = Bad(tip) || Bad(_MainTex.Load(int3(31, side, 0)));
                [unroll] for (int k = 0; k < 9; k++)
                {
                    int3 l = int3((k % 3) * (n.x - 1) / 2, (k / 3) * (n.y - 1) / 2, n.z - 1);
                    float4 s = _MainTex.Load(Texel((uint) Index(l), 0));
                    bad = bad || Bad(s);
                    most = max(most, length(s.xyz - mul(m, float4(Rest(l), 1)).xyz - swing));
                }
                float off = bad ? 0 : most / L;
                bool touched = _MainTex.Load(int3(1, side, 0)).w > 0.5 || Pushed(side);
                bool hot = bad || off > 1.5 || off > 0.6 && !touched;
                float4 last = _MainTex.Load(int3(36, side, 0));
                return float4(hot ? (last.w > 0 && _Time.y - last.w < 0.1 ? last.x : 0) + 1 : 0, off, bad, _Time.y);
            }

            // Where a step splits in two. Capped, two frames at 144 a second land a hair either side
            // of 1/72, and the second pass a step over them took would spend the cap's saving.
            // A step after a _Mid skip stays one predicted pass up to 1/44: two passes over it cost
            // what the skip saved. Keyed to the flag the gate took (93: x last frame's, y the one the
            // first pass read), so both passes split alike; a _Cap catch-up near is left as it was.
            float Split(float frames, uint side)
            {
                [branch] if (_Mid > 0 && frames > 1.5)
                {
                    float4 halved = _MainTex.Load(int3(93, side, 0));
                    if ((_Pass < 0.5 ? halved.x : halved.y) > 0.5) return 1.0 / 44;
                }
                return _Cap > 0 || _Mid > 0 ? 1.0 / 69 : 1.0 / 72;
            }

            // The step's time from the clock (texel 25): held is the step held, smooth the frame time
            // smoothed, frames how many it takes.
            float StepDt(float4 clock, out float held, out float smooth, out float frames)
            {
                float real = clamp(unity_DeltaTime.x, 1.0 / 240, 1.0 / 30);
                smooth = clock.y > 0 ? lerp(clock.y, real, 0.05) : real;
                held = _Pass > 0.5 ? max(clock.x, 1.0 / 240) : clock.x > 0 && abs(smooth - clock.x) < 0.15 * clock.x ? clock.x : smooth;
                // The step takes the time that really passed, in held steps: counted against this
                // frame's own time, a dropped frame read as one, and the anchor's move over it as
                // twice its speed, which kicked the tissue on every hitch. Within 15% of a whole
                // count it takes the count, so the even frames keep the held step. Past what one
                // step takes, the tissue is carried over the rest (Gapped). The first pass counts;
                // the second reads its count. Not asked whether it is halved: && runs both sides,
                // and that alone cost every step 10%.
                frames = clock.w;
                [branch] if (_Pass < 0.5)
                {
                    float steady = _FixedDt > 0 ? _FixedDt : held;
                    Since = clock.z > 0 && _Time.y > clock.z ? _Time.y - clock.z : steady;
                    float f = Since / steady, r = max(round(f), 1);
                    frames = abs(f - r) < 0.15 * r ? r : f;
                    // Against the steady step, not 1/30: under 30 fps every step read as a gap and the
                    // tissue was carried rigid. A gap is carried, so it takes one held step.
                    Gapped = Since > max(1.0 / 30, 2.5 * steady) + 1e-4;
                    if (Gapped) frames = 1;
                }
                return min((_FixedDt > 0 ? _FixedDt : held) * max(frames, 0.25), 1.0 / 30);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                int2 px = int2(i.pos.xy);
                int3 n = Count();
                uint count = n.x * n.y * n.z, side = Rows();
                // The cost meter's load rests in even phases, as cheap as a pass can be.
                if (_Pass < 1.5 && _Stress > 0 && fmod(floor(_Time.y / Window), 2) < 0.5) return _MainTex.Load(int3(px, 0));
                // Packed regions share the passes, the scan's player arrays and the draw's texture:
                // each is a block of rows with its own anchor, and from here on side is its own side
                // row. The third pass draws one row, so reads the first region's.
                // In floats: a division by a value known only at run time is emulated in integers.
                uint region = (uint) floor((px.y + 0.5) / (side + 1));
                Base = region * (side + 1);
                side += Base;
#ifdef JELLY_SCAN
                // The side row's own pass, after the particles' first: it leaves what that wrote.
                if ((uint) px.y != side) discard;
#elif !defined(JELLY_POINTS)
                // The first pass's side row is the next blit's, so here it is no work.
                if ((uint) px.y == side && _Pass < 0.5) return 0;
#endif
                // The rows padding the region out, before any work.
                if ((uint) px.y != side && (uint) px.x % Row + ((uint) px.y - Base) * Row >= count) return 0;
                // The probes' readout columns hold nothing on the particles' rows.
                if ((uint) px.x >= 2 * Row && (uint) px.y != side) return 0;
                // Capped (_Cap, or _Mid past _Half as texel 93 last said): a frame too soon after the last step
                // leaves the state as it is, and the next step takes the gap as StepDt's frames. On the
                // step's own clock (25): through the parser, read back late, a gate came in bursts.
                // The first pass copies, as a discard in its variant slowed near steps; the second runs
                // only where the first stepped this frame.
                [branch] if (_Pass < 1.5 && (_Cap > 0 || _Mid > 0) && !(_Stress > 0))
                {
                    float4 ck = _MainTex.Load(int3(25, side, 0));
                    [branch] if (_Pass < 0.5)
                    {
                        float4 halved = _MainTex.Load(int3(93, side, 0));
                        float rate = _Mid > 0 && halved.x > 0.5 ? _Mid : _Cap, real = _FixedDt > 0 ? _FixedDt : ck.y;
                        if (rate > 0 && ck.z > 0 && _Time.y >= ck.z && _Time.y - ck.z + 0.5 * real < 1 / rate) return _MainTex.Load(int3(px, 0));
                    }
                    else if (ck.z > 0 && ck.z != _Time.y) discard;
                }
                Region = region;
                MineOf = region;
                Cored = (region == 0 ? _JellyCoreA0.w : _JellyCoreA1.w) > 0;
#if !defined(JELLY_SCAN) && !defined(JELLY_POINTS)
                // At 72 fps and up the first pass's right half only hands on where each particle was:
                // the second puts back at rest the ones the left half marked, and carries the rest
                // over a snap, so no anchor or home is worked out here. Near last frame only, as the
                // far test is the particles' then.
                [branch] if (_Pass < 0.5 && (uint) px.y != side && (uint) px.x >= Row)
                {
                    float4 sw = _MainTex.Load(int3(18, side, 0));
                    float held, smooth, frames, dt = StepDt(_MainTex.Load(int3(25, side, 0)), held, smooth, frames);
                    bool lean = !(dt > Split(frames, side)) && !(_Stress > 0) && !(sw.x > 0.5);
#ifdef JELLY_SLEEP
                    lean = lean && !(sw.y > 0.5);
#endif
                    if (lean) return float4(_MainTex.Load(int3(px.x - Row, px.y, 0)).xyz, dt);
                }
                // The second pass's particles take the anchor the first pass's side row worked out this
                // frame (88 to 90, its rows), and leave before anything else where they only copy:
                // far, asleep, or a predicted step's right half. The clock is the side row's own test.
                [branch] if (_Pass > 0.5 && (uint) px.y != side)
                {
                    float4 sw = _MainTex.Load(int3(18, side, 0)), ck = _MainTex.Load(int3(25, side, 0));
                    if (sw.x > 0.5) discard;
#ifdef JELLY_SLEEP
                    [branch] if (sw.y > 0.5)
                    {
                        float4 still = _MainTex.Load(int3(px, 0));
                        return (uint) px.x < Row ? float4(still.xyz, _Time.y) : still;
                    }
#endif
                    float dt1 = min((_FixedDt > 0 ? _FixedDt : max(ck.x, 1.0 / 240)) * max(ck.w, 0.25), 1.0 / 30);
                    Mine = float4x4(_MainTex.Load(int3(88, side, 0)), _MainTex.Load(int3(89, side, 0)), _MainTex.Load(int3(90, side, 0)), float4(0, 0, 0, 1));
                    Turned = true;
                    [branch] if ((uint) px.x >= Row && !(dt1 > Split(ck.w, side)) && !(_Stress > 0))
                    {
                        if (!(sw.w > 0.5)) return 0;
                        return Handed(_MainTex.Load(int3(px.x - Row, px.y, 0)), _MainTex.Load(int3(px, 0)), Mine, side);
                    }
                }
                else Mine = AnchorOf(region);
#else
                Mine = AnchorOf(region);
#endif
                // The other region's only where it is read: the far switch measures from the first's,
                // and the side row's pair plane. Built for every pixel, far-off steps cost half again.
                // A first pass's particle tests far only when it was far last frame, below.
                Theirs = Mine;
                [branch] if (_Regions > 1.5 && (region == 1 && _Pass < 0.5 && _MainTex.Load(int3(18, side, 0)).x > 0.5 || (uint) px.y == side)) Theirs = AnchorOf(region ^ 1);
                // A third pass, where there is a rider, copies its two texels into a texture of their
                // own: the texture parser reads back a whole texture, and the whole state costs it
                // ten times two texels. Its third texel says whether the steps should run at all: the
                // parser switches their blitters off far from the viewer or well behind them, and on
                // again once near and in view. Off, the state keeps its last far switch, so the same
                // rule brings them back.
                if (_Pass > 1.5)
                {
                    if (px.x < 2) return _MainTex.Load(int3(16 + px.x, side, 0));
#ifdef JELLY_POINTS
                    [branch] if (px.x >= 3) return Feedback(px.x);
#endif
                    bool off = Away(_MainTex.Load(int3(18, side, 0)).x > 0.5);
#ifdef JELLY_POINTS
                    [branch] if (!off && _Rest > 0) off = Resting();
#endif
                    // On for one frame's time in each step's, by the clock, not by frames, so the
                    // parser's few frames of lag only shift which. At or under the rate, every frame.
                    [branch] if (!off)
                    {
                        float rate = Halved(_MainTex.Load(int3(93, side, 0)).x > 0.5) ? _Rate.y : _Rate.x, frame = _MainTex.Load(int3(25, side, 0)).x;
                        off = rate > 0 && frame > 0 && frac(_Time.y * rate) >= frame * rate;
                    }
                    return float4(off ? 0 : 1, 0, 0, 1);
                }
#ifdef JELLY_POINTS
                // A points material only ever runs the block above: the step left out of it, the
                // compiler took twenty minutes over code it never runs.
                return 0;
#else
                float4 here = _MainTex.Load(int3(px, 0));
                float4x4 m = Mine;
                float scale = length(m._m00_m10_m20);
                bool anchored = scale > 1e-6 && !Bad(m[0]) && !Bad(m[1]) && !Bad(m[2]);
                Scale = scale;
                Reach = 1.5 * length((float3) (n - 1)) * _Lattice.w * scale;
                // The watchdog's count, as the side row left it last step (36).
                float4 dog = _MainTex.Load(int3(36, side, 0));
                Hot = dog.w > 0 && _Time.y - dog.w < 0.1 ? dog.x : 0;
                // The step is held while the frame time, smoothed, stays within 15% of it: where the
                // tissue rests moves with the step, so stepping each uneven frame by its own time
                // kept a settled prop trembling, never asleep. The first pass decides; the second
                // reads its answer.
                float held, smooth, frames;
                float dt = StepDt(_MainTex.Load(int3(25, side, 0)), held, smooth, frames);
                FrameDt = dt;
                // An anchor whose speed changes by more than 8 m/s in a frame has snapped to a new pose:
                // an emote, sitting, a limb's tracking coming back. No limb moves so and a fall stops
                // slower, as the landing's own 8 m/s says. Left behind by the jump, a part on an arm
                // sat 0.5 m off its rest for a tenth of a second; now it and its mass ride it. The first
                // pass decides and signs 85 with it; the second reads its answer.
                float4 sped = _MainTex.Load(int3(85, side, 0));
                // Over a gap a step cannot take, near last step, the state is carried on with the
                // anchor up to a second old: past a tenth it started again at rest, a pop on every
                // hitch and the tissue rigid under 10 fps.
                Stale = Gapped && !(_MainTex.Load(int3(18, side, 0)).x > 0.5) ? 1 : 0.1;
                // A gap longer than a step takes is carried the same way: a hitch's frame, stepped
                // short, read its move as a kick.
                // So is a turn faster than 12 radians a second, MagicaCloth's own limit: a snap turn,
                // or a remote copy's turn caught up in a frame, flung the tissue.
                float3 vNow = AnchorVelocity(m, Gapped ? Since : dt, side);
                Snapped = sped.w < 0;
                [branch] if (_Pass < 0.5)
                {
                    float3 x0 = _MainTex.Load(int3(26, side, 0)).xyz, z0 = _MainTex.Load(int3(28, side, 0)).xyz;
                    float turned = min(dot(normalize(m._m00_m10_m20), normalize(x0)), dot(normalize(m._m02_m12_m22), normalize(z0)));
                    Snapped = anchored && sped.w != 0 && _Time.y - abs(sped.w) < Stale && (Gapped || length(vNow - sped.xyz) > 8 || dot(x0, x0) > 0 && turned < cos(min(12 * dt, 1.5)));
                    // A remote copy's anchor moves in straight runs from packet to packet, its speed
                    // changing in steps, and the tissue rang with every one. Its change of speed is
                    // spread over _Smooth on every copy alike, the wearer's own included, so all
                    // viewers see the same tissue with nothing synced.
                    float4 was95 = _MainTex.Load(int3(95, side, 0));
                    bool smooth = !Snapped && _Smooth > 0 && sped.w > 0 && _Time.y - sped.w < 0.1 && was95.w > 0 && _Time.y - was95.w < 0.1;
                    Smoothed = smooth ? lerp(vNow, was95.xyz, exp(-dt / _Smooth)) : vNow;
                    Kick = smooth ? (vNow - sped.xyz) - (Smoothed - was95.xyz) : 0;
                    // Fast, as in flight, it eases home over the 2 m/s under _Fast and hangs rigid past it
                    // until back under them: flying, every turn and gust threw it about. 0 to 1 easing, 2
                    // rigid. Off the smoothed speed, so every copy agrees, and a snap keeps the last.
                    float wasEase = _MainTex.Load(int3(18, side, 0)).z;
                    float toward = _Fast > 0 ? saturate((length(Smoothed) - _Fast + 2) / 2) : 0;
                    // Untouched, it eases onto its bones over _Hold and holds there (1): walking, the
                    // tissue's own swing on top of the cloth's stretched it twice over. Any hand in reach,
                    // the wearer's own hand into the box, a probe, a grip or the pair pressing together
                    // last step lets it go at once. An arm or thigh lies in the box at rest, so those
                    // dent the skin through the draw alone while it holds.
                    bool touched = _MainTex.Load(int3(1, side, 0)).w > 0.5 || Pushed(side) || ((uint) _MainTex.Load(int3(41, side, 0)).y & 0x44u) != 0 || _MainTex.Load(int3(92, side, 0)).x > 0.5 || Meeting(m, scale, side);
                    // Touched while held, it lets go over Waking, eased at both ends, the share still
                    // held home kept negative: let go at once, an arm already inside threw the skin
                    // 80 mm in a frame, and a linear let go left a 26 mm step where it ended.
                    float letting = wasEase >= 1 && wasEase < 1.5 ? 1 : max(-wasEase, 0);
                    float hold = _Hold > 0 && !touched ? min(min(wasEase, 1) + dt / _Hold, 1) : -max(letting - dt / Waking, 0);
                    Ease = Snapped ? wasEase : (wasEase > 1.5 && toward > 0) || toward >= 1 ? 2 : toward > 0 ? max(toward, hold) : hold;
                    // A change of speed past _Landing in the step, kept for the next step: against the
                    // smoothed speed, a steady speed-up read as one and rang every step.
                    float3 by = sped.xyz - vNow;
                    bool heard = !Snapped && sped.w > 0 && _Time.y - sped.w < 0.1 && length(sped.xyz) < 8 && length(by) > _Landing;
                    Landed = heard ? float4(vNow, _Time.y) : 0;
                    LandedBy = heard ? float4(by, length(by)) : 0;
                }
                // Snapped, one predicted pass: its predictions are carried, where two whole passes fit
                // each particle to neighbours the jump left behind and pulled it most of the way back.
                int passes = dt > Split(frames, side) && !Snapped ? 2 : 1;
                float h = dt / passes;
                // The meter's load always takes a whole step, the cost it is there to show.
                bool predicted = passes == 1 && !(_Stress > 0);
                // Far from the viewer's head or well behind them the tissue hangs rigid. The first pass
                // decides and the second reads its answer. No head, as without a humanoid avatar, counts
                // as near.
                float4 switches = _MainTex.Load(int3(18, side, 0));
                bool wasDistant = switches.x > 0.5, distant = wasDistant;
                [branch] if (!(_Pass < 0.5)) Ease = switches.z;
                // A particle near last frame takes that: going far, the second pass discards this one's
                // step, so the twin's particles it wrote are never read; coming back is tested here.
                [branch] if (_Pass < 0.5 && ((uint) px.y == side || wasDistant)) distant = !(_Stress > 0) && (Away(wasDistant) || Ease > 1.5);
                // Asleep: settled, untouched, on an anchor that has not moved. The second pass decided
                // it last frame, so both passes take the same way; a moved anchor or a hand wakes it
                // the frame after.
#ifdef JELLY_SLEEP
                bool asleep = switches.y > 0.5 && !distant;
#else
                bool asleep = false;
#endif
                // The probes' frame only where it is read: the side row.
                Probed = false;
                [branch] if ((uint) px.y == side) Probed = Probing(region, Back);

                bool rider = false;
                if ((uint) px.y == side)
                {
                    if (!anchored) return 0;
#ifdef JELLY_SCAN
                    {
                        if (px.x == 18) return float4(distant, asleep, Ease, 1);
                        if (px.x == 25) return float4(held, smooth, _Time.y, frames);
                        // Whether the gate above takes _Mid next frame: halved once here, not per particle. A
                        // flag, not the rate, so a changed material takes at once.
                        [branch] if (px.x == 93 && _Mid > 0) return float4(Halved(here.x > 0.5), here.x, 0, 1);
                        // The probes as Felt has them, for the scan's texels and the slaps': one copy of
                        // it between them, as a copy each took the compiler past its time limit. The
                        // scan's only once a cloth probe was pushed last frame, as the readout
                        // has it: a collider is felt a reach off the skin, so the frame this starts late
                        // by is still short of it, and at rest this is most of the step's cost. World
                        // probes press as flat faces in the step, never as hands, so the scan takes the
                        // cloth's alone. The slaps' while their last frame was timed. Each test
                        // branched: `&&` works out both sides.
                        Press presses[2];
                        presses[0] = (Press) 0;
                        presses[1] = (Press) 0;
                        bool feel = false, felt = false;
                        uint upto = PROBES;
#ifdef JELLY_SCAN
                        [branch] if (px.x < 16 && !distant)
                        {
                            upto = (uint) _JellyProbes;
                            [loop] for (uint k = 0; k < upto && k < PROBES && !feel; k++) feel = _MainTex.Load(int3(READOUT + k, side, 0)).w > 0.001;
                        }
#endif
                        [branch] if (px.x >= 80 && px.x < 84 && !distant)
                        {
                            float4 last = _MainTex.Load(int3(84, side, 0));
                            feel = last.w > 0 && _Time.y - last.w < 0.1;
                        }
                        [branch] if (feel && Probed)
                        {
                            Felt(Region, upto, Back, presses);
                            felt = true;
                        }
#ifdef JELLY_SCAN
                        [branch] if (px.x < 16)
                        {
                            if (distant) return 0;
                            return Scan(px.x, m, scale, side, felt, presses);
                        }
#endif
                        [branch] if (px.x >= 32 && px.x <= 34 && _Solid > 0) return Bulk(px.x - 32, m, scale, distant, side, predicted);
                        // How far the wearer's capsules in reach moved against the anchor since last frame,
                        // for the rest's count.
                        [branch] if (px.x == 37) return float4(Shifted(m, scale, side, false), 0, 0, 1);
                        // A readout for the verify: the plane's distances as the step reads them.
                        [branch] if (px.x == 40)
                        {
                            float zc = 0.5 * (Count().z - 1) * _Lattice.w;
                            uint ps = (region ^ 1) * (side - Base + 1) + side - Base;
                            float4 ta = _MainTex.Load(int3(30, side, 0)), tb = _MainTex.Load(int3(30, ps, 0));
                            float3 ca = ta.w > 0 ? ta.xyz : mul(m, float4(0, 0, zc, 1)).xyz;
                            float3 cb = tb.xyz;
                            [branch] if (!(tb.w > 0)) cb = mul(Anchor(region ^ 1), float4(0, 0, zc, 1)).xyz;
                            return float4(distance(ca, cb), length(_PairOffset.xyz) * scale, _PairReach.z * scale, scale);
                        }
                        // Each of the wearer's own capsules' ends for the draw, as the hands' slots are,
                        // and on its far end whether it is in reach: judged here side by side, the
                        // second pass gathers them into 41. One texel judging all sixteen in turn was a
                        // seventh of the step.
                        [branch] if ((px.x >= 42 && px.x <= 57) || (px.x >= 64 && px.x <= 79))
                        {
                            float3 a, b;
                            float r;
                            uint c = px.x < 64 ? (uint) (px.x - 42) >> 1 : 8 + ((uint) (px.x - 64) >> 1);
                            Own(c, scale, a, b, r);
                            // 1 in reach, 2 into the skin's box itself: an arm at the side is always in
                            // reach, so only 2 counts as touching for the hold.
                            float reach = 0;
                            [branch] if ((px.x & 1) && r > 0) { float gap = Gap(a, b, r, m, scale, side); reach = gap <= 0 ? 2 : gap <= 0.08 * scale ? 1 : 0; }
                            float hold;
                            [branch] if (c == 2 || c == 6) Held(a, b, r, r, false, a, b, m, scale, hold);
                            return (px.x & 1) == 0 ? float4(a, r) : float4(b, reach);
                        }
                        // The draw's last guard: the plane midway between the two skins where they reach
                        // toward each other, moved on by half however far they overlapped as built, and
                        // the give it eases skin past it into.
                        [branch] if (px.x == 58 || px.x == 59)
                        {
                            if (!(_Pair > 0 && _PairSkin.x > 0) || distant) return 0;
                            float zc = 0.5 * (Count().z - 1) * _Lattice.w;
                            uint ps = (region ^ 1) * (side - Base + 1) + side - Base;
                            float4 ta = _MainTex.Load(int3(30, side, 0)), tb = _MainTex.Load(int3(30, ps, 0));
                            float3 ca = ta.w > 0 ? ta.xyz : mul(m, float4(0, 0, zc, 1)).xyz;
                            float3 cb = tb.xyz;
                            [branch] if (!(tb.w > 0)) cb = mul(Anchor(region ^ 1), float4(0, 0, zc, 1)).xyz;
                            float dist = distance(ca, cb);
                            if (!(dist > 1e-4)) return 0;
                            float self = (region == 0 ? _PairSkin.x : _PairSkin.y) * scale, partner = (region == 0 ? _PairSkin.y : _PairSkin.x) * scale;
                            float overlap = max(self + partner - length(_PairOffset.xyz) * scale, 0);
                            float3 u = (cb - ca) / dist;
                            float meet = Meet(m, ps);
                            if (!(meet > 0)) return 0;
                            return px.x == 58 ? float4(u, dot(ca, u) + 0.5 * (self + dist - partner) + 0.5 * overlap) : float4(0.004 * scale, meet, 0, 1);
                        }
                        [branch] if ((px.x == 30 || px.x == 31 || px.x == 86) && _MassHz > 0)
                        {
                            uint side0 = side - Base;
                            return Mass(px.x, m, scale, dt, distant, side, region, (region ^ 1) * (side0 + 1) + side0);
                        }
                        if (px.x >= 80 && px.x <= 84) return Slap(px.x, m, scale, dt, distant, side, felt, presses);
                        // Over the time that passed: over the step's, a hitch's move read as a jump next frame.
                        if (px.x == 85) return float4(vNow, Snapped ? -_Time.y : _Time.y);
                        if (px.x == 95) return float4(Smoothed, _Time.y);
                        [branch] if (px.x == 36) return Watchdog(m, scale, distant, side);
                        // The anchor's turn rate since the last step, for the draw between steps (94):
                        // carrying each particle on at its own speed carried the turn twice, the skin
                        // leading every turn on the skipped frames.
                        [branch] if (px.x == 94)
                        {
                            float4 q91 = _MainTex.Load(int3(91, side, 0));
                            if (Snapped || !(dot(q91, q91) > 0.5)) return float4(0, 0, 0, 1);
                            float4 dq = Mul(Quat((float3x3) m / scale), float4(-q91.xyz, q91.w));
                            return float4(2 * dq.xyz * (dq.w < 0 ? -1 : 1) / max(Gapped ? Since : dt, 1e-4), 1);
                        }
                        if (px.x == 97) return distant ? 0 : Landed;
                        if (px.x == 98) return distant ? 0 : LandedBy;
                        // The anchor's rows and its turn, for the second pass's particles.
                        if (px.x >= 88 && px.x <= 91) return px.x == 88 ? m[0] : px.x == 89 ? m[1] : px.x == 90 ? m[2] : Quat((float3x3) m / scale);
                        // Whether any slot might grip this pass, for Moved: 112 to 119 below grip only a
                        // fist near that was not let go, so none such, none grips. The texture's own
                        // 92 always answers for its own 112 to 119, as the second pass copies both.
                        [branch] if (px.x == 92)
                        {
                            bool any = false;
                            [loop] for (uint e = 0; e < 8 && !any; e++) any = !distant && Fist(_MainTex.Load(int3(2 * e + 1, side, 0))) && _MainTex.Load(int3(112 + e, side, 0)).w >= 0;
                            return float4(any, 0, 0, 1);
                        }
                        // Each hand slot's grip (112 to 119), as Pulled reads it: a fist closed on the
                        // skin grips it where it closed, or where Slid takes it, until it opens or has
                        // pulled twice as far as the skin follows; a fist closed off the skin, or that
                        // came in closed, never grips, as a punch is no grab. Its pull (120 to 127) kept
                        // for the ring when it lets go.
                        [branch] if (px.x >= 112 && px.x < READOUT)
                        {
                            uint e = ((uint) px.x - 112) & 7;
                            float4 a = _MainTex.Load(int3(2 * e, side, 0)), b = _MainTex.Load(int3(2 * e + 1, side, 0)), was = _MainTex.Load(int3(112 + e, side, 0));
                            float3 at;
                            float raw;
                            float4 d = Pulled(e, m, side, at, raw);
                            // w the last time it gripped, kept after, for the push's ease back in.
                            if (px.x >= 120) return float4(d.xyz, d.w > 0 ? _Time.y : _MainTex.Load(int3(px.x, side, 0)).w);
                            if (distant || !Fist(b)) return 0;
                            if (was.w > 0) return raw > (Count().z - 1) * _Lattice.w ? float4(0, 0, 0, -1) : float4(Slid(was.xyz, Into(m, Knuckles(a, b)), Into(m, a.xyz) - Into(m, b.xyz)), was.w);
                            float3 pb, fa, fb;
                            float rf;
                            Hand(a, b, pb, fa, fb, rf);
                            return was.w == 0 && Gap(a.xyz, pb, a.w, m, scale, side) <= 0.01 * scale ? float4(Into(m, Knuckles(a, b)), _Time.y) : float4(0, 0, 0, -1);
                        }
                        // Whether any probe below reads pushed, for Pushed.
                        [branch] if (px.x == 87)
                        {
                            bool any = false;
                            [loop] for (uint k = 0; k < (uint) _JellyProbes && k < PROBES && !any; k++)
                            {
                                float3 at, way;
                                any = (Probed ? Probe(Region, k, Back, at, way) : -1) > 0.002;
                            }
                            return float4(any, 0, 0, 1);
                        }
                        // Each probe as the scan reads it, where it is and how far pushed: the slaps' last
                        // frame, and a readout for the verify.
                        [branch] if (px.x >= READOUT && px.x < READOUT + PROBES)
                        {
                            float3 at, way;
                            float gone = Probed ? Probe(Region, px.x - READOUT, Back, at, way) : -1;
                            return float4(at, gone);
                        }
                        return here;
                    }
#else
                    if (px.x >= 26 && px.x <= 29)
                        return float4(px.x == 26 ? m._m00_m10_m20 : px.x == 27 ? m._m01_m11_m21 : px.x == 28 ? m._m02_m12_m22 : m._m03_m13_m23, 1);
                    // Which of the wearer's own capsules are in reach, a bit each, from the first pass's
                    // judgements. Read a frame late by the first pass, so the reach has room.
                    [branch] if (px.x == 41)
                    {
                        uint bits = 0, into = 0;
                        [unroll] for (uint c = 0; c < 16; c++)
                        {
                            float w = _MainTex.Load(int3(c < 8 ? 43 + 2 * c : 49 + 2 * c, side, 0)).w;
                            bits |= w > 0.5 ? 1u << c : 0;
                            into |= w > 1.5 ? 1u << c : 0;
                        }
                        return float4(distant ? 0 : bits, distant ? 0 : into, 0, 1);
                    }
#ifdef JELLY_SLEEP
                    if (px.x == 18) return float4(distant, Sleepy(m, distant, side), Ease, 1);
                    if (asleep) return here;
                    if (px.x == 19) return Still(here, m, scale, dt, distant, side);
                    if (px.x >= 20 && px.x <= 22) return float4(px.x == 20 ? m._m00_m10_m20 : px.x == 21 ? m._m01_m11_m21 : m._m03_m13_m23, 1);
                    if (px.x == 23 || px.x == 24) return _MainTex.Load(int3(px.x - 7, side, 0));
#else
                    // Worn, resting: the third pass then switches the steps off and the draw keeps the
                    // state as it lies. Only after a count stepped without a gap.
                    if (px.x == 18)
                    {
                        float4 rest = _MainTex.Load(int3(35, side, 0));
                        return float4(distant, _Rest > 0 && !distant && rest.y > 0 && _Time.y - rest.y < 0.1 && rest.x >= _Rest, Ease, 1);
                    }
                    if (px.x == 35) return Settling(here, m, scale, dt, distant, side, predicted, predicted ? Gravity * h * h : 0);
#endif
                    // The rider takes the particles' own path below, so the step is written out once.
                    rider = (px.x == 16 || px.x == 17) && _Rider > 0;
                    if (!rider) return here;
#endif
                }
#ifdef JELLY_SCAN
                return here;
#else
                int3 l;
                uint p;
                float3 home;
                float4 swing = float4(0, 0, 0, 1), q;
                [branch] if (rider)
                {
                    // The front centre, homed on the anchor alone.
                    int3 n = Count();
                    l = int3(n.x / 2, n.y / 2, n.z - 1);
                    p = (uint) Index(l);
                    home = mul(m, float4(Rest(l), 1)).xyz;
                }
                else
                {
                    // Far off, both passes leave the state be, as cheap as a pass can be: the surface
                    // draws the rest shape off the far switch, and a stale state starts again at rest.
                    // The second discards, writing nothing. Compiled into its variant alone: a discard
                    // anywhere in the first, which carries the hand scan, slowed a fast GPU's near steps,
                    // even one it never reached.
                    [branch] if (distant)
                    {
#ifndef JELLY_SCAN
                        if (_Pass > 0.5) discard;
#endif
                        return here;
                    }
                    // Asleep, the same, but stamped, so it reads as stepping and wakes where it lay.
                    [branch] if (asleep) return _Pass > 0.5 && (uint) px.x < Row ? float4(here.xyz, _Time.y) : here;
                    p = (uint) px.x % Row + ((uint) px.y - Base) * Row;
                    if (p >= count) return 0;
                    int half_ = (uint) px.x >= Row;
                    l = Place(p);
                    // No anchor yet: nothing to hang from. Marked, so the surface can say so.
                    if (!anchored) return half_ == 0 ? float4(0, 0, 0, -1) : 0;
                    // The fit's right half was settled in the first pass: no home to work out.
                    if (predicted && _Pass > 0.5 && half_ == 1) return here;
                    home = Home(l, m, side, swing);
                    if (predicted && _Pass < 0.5)
                    {
                        // Where each particle is headed, for the second pass to fit, w 0 for one put back
                        // at rest; the right half already takes where it was.
                        float4 s = _MainTex.Load(Texel(p, 0)), was = _MainTex.Load(Texel(p, 1));
                        [branch] if (Snapped) { s.xyz = Carried(s.xyz, m, side); was.xyz = Carried(was.xyz, m, side); }
                        bool rest = Fresh(s, was, home);
                        if (half_ == 1) return float4(rest ? home : s.xyz, h);
                        if (rest) return float4(home, 0);
                        if (Pinned(l)) return float4(home, 1);
                        float3 vh, ahead = Ahead(s, was, l, side, home, h, vh) + Gravity * h * h;
                        return Bad(ahead) ? float4(home, 0) : float4(ahead, 1);
                    }
                    // A whole step's right half only moves along, so half the texture does next to no work.
                    if (!predicted && half_ == 1)
                    {
                        float4 s = _MainTex.Load(Texel(p, 0));
                        [branch] if (Snapped && _Pass < 0.5) s.xyz = Carried(s.xyz, m, side);
                        return float4(Fresh(s, here, home) ? home : s.xyz, h);
                    }
                }
                // The fit from the first pass's predictions, or a whole step. One call: each call
                // writes the whole step out again.
                float3 at = Moved(p, l, m, scale, h, home, predicted, distant, side, swing, q);
                // Nothing bad is ever written: home, or the rigid rest where even that is bad.
                [branch] if (Bad(at)) at = Bad(home) ? mul(m, float4(Rest(l), 1)).xyz : home;
                [branch] if (rider) return Rider(px.x, m, scale, at, q);
                return float4(at, _Time.y);
#endif
#endif
            }
            ENDCG
        }
    }
    Fallback Off
}
";

        internal const string CommonInclude = @"// The jelly's lattice, shared by its passes. Written by YAPS; edits here are overwritten.
#ifndef JELLY_COMMON
#define JELLY_COMMON

Texture2D<float4> _MainTex;
float4 _Lattice;
// One anchor a region. Packed, each region is a block of rows of the state, its side row last.
// After the four, the wearer's own bones (YapsJelly's ownBones).
float4x4 _JellyAnchor[32];
// The first row of this pixel's region, set once a pixel and added to every lattice texel.
static uint Base = 0;

static const uint Row = 64;
static const float Window = 0.5;
// This pixel's region holds a core round its bone instead of its back face.
static bool Cored = false;
// The step's share of gravity. A worn part's lift cancels its steady sag along the world's up, so there
// it only swings; on a prop with no lift, 1 hangs like flesh and 0 keeps the modelled shape.
float _Gravity;
#define Gravity float3(0, -9.81 * _Gravity, 0)
// The rigid fit's rotation steps. Each starts from the anchor's turn, which a neighbourhood's own
// is never far from, so three land where six did.
static const int Turns = 3;

int3 Count() { return int3(_Lattice.xyz); }
// A region's lattice rows, padded so with its side row it is a whole number of eight: a block of
// rows the GPU runs together never holds both a side row's work and particles'.
uint Rows() { int3 n = Count(); return ((uint) (n.x * n.y * n.z + Row - 1) / Row + 8) / 8 * 8 - 1; }
int Index(int3 l) { int3 n = Count(); return (l.z * n.y + l.y) * n.x + l.x; }
// In floats, where a division is cheap and exact enough for any lattice a texture holds; integer
// division by a value only known at run time is emulated, a few dozen instructions.
int3 Place(uint p)
{
    int3 n = Count();
    float row = floor((p + 0.5) / n.x), slab = floor((row + 0.5) / n.y);
    return int3(p - row * n.x, row - slab * n.y, slab);
}
// Unsigned, so a neighbour's index wrapped below 0 lands outside the texture, where a load reads 0.
int3 Texel(uint p, int half_) { return int3(p % Row + half_ * Row, p / Row + Base, 0); }
float3 Rest(int3 l) { int3 n = Count(); return float3(l.x - 0.5 * (n.x - 1), l.y - 0.5 * (n.y - 1), l.z) * _Lattice.w; }

// A fresh texture, a new copy of the avatar, garbage, a teleport, or a state left still a tenth
// of a second, far off or switched off: start at rest.
// NaN and infinity by their bits: the compiler folds every float test for NaN away, so the tests
// below let one through, it spread over the fit, and its skin dropped out for good.
bool Bad(float4 v) { return any((asuint(v) & 0x7f800000) == 0x7f800000); }
bool Bad(float3 v) { return any((asuint(v) & 0x7f800000) == 0x7f800000); }

// How far off its home a particle may be before it starts again at rest: half again the lattice's
// diagonal at the anchor's scale. A metre, as it was, let a small avatar's tissue fly twenty depths.
static float Reach = 1;
// The watchdog's count (texel 36): the steps the region has run out of control. Past 8, at rest.
static float Hot = 0;
// How old a state may be and still be stepped on: carried over a hitch, up to a second.
static float Stale = 0.1;

bool Fresh(float4 s, float4 was, float3 home)
{
    return Bad(s) || Bad(was) || Hot >= 8 || !(was.w > 0 && was.w < 0.1) || !(abs(s.x) + abs(s.y) + abs(s.z) + abs(was.x) + abs(was.y) + abs(was.z) < 1e6)
        || !(distance(s.xyz, home) < Reach) || !(_Time.y - s.w < Stale);
}

float4 Mul(float4 a, float4 b) { return float4(a.w * b.xyz + b.w * a.xyz + cross(a.xyz, b.xyz), a.w * b.w - dot(a.xyz, b.xyz)); }

float3x3 Turn(float4 q)
{
    float x = q.x, y = q.y, z = q.z, w = q.w;
    return float3x3(1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w),
                    2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w),
                    2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y));
}

// Of the anchor alone, so every lane takes the same way.
float4 Quat(float3x3 m)
{
    float t = m[0][0] + m[1][1] + m[2][2];
    float4 q;
    [branch] if (t > 0) { float s = sqrt(t + 1) * 2; q = float4((m[2][1] - m[1][2]) / s, (m[0][2] - m[2][0]) / s, (m[1][0] - m[0][1]) / s, 0.25 * s); }
    else
    {
        [branch] if (m[0][0] > m[1][1] && m[0][0] > m[2][2]) { float s = sqrt(1 + m[0][0] - m[1][1] - m[2][2]) * 2; q = float4(0.25 * s, (m[0][1] + m[1][0]) / s, (m[0][2] + m[2][0]) / s, (m[2][1] - m[1][2]) / s); }
        else
        {
            [branch] if (m[1][1] > m[2][2]) { float s = sqrt(1 + m[1][1] - m[0][0] - m[2][2]) * 2; q = float4((m[0][1] + m[1][0]) / s, 0.25 * s, (m[1][2] + m[2][1]) / s, (m[0][2] - m[2][0]) / s); }
            else { float s = sqrt(1 + m[2][2] - m[0][0] - m[1][1]) * 2; q = float4((m[0][2] + m[2][0]) / s, (m[1][2] + m[2][1]) / s, 0.25 * s, (m[1][0] - m[0][1]) / s); }
        }
    }
    return normalize(q);
}

// The best rigid fit of l's 3x3x3 neighbourhood, from where each of them is headed: its turn
// (Mueller and others, 2016, started from q, the anchor's), its centre c and its rest centre rc.
// Each neighbour is read once, at an offset the whole pass shares, and taken against where l is
// headed, so the sums keep their digits; one out of the lattice counts 0. With predicted, where
// each is headed is the first pass's, its texel; otherwise it is worked out here, with gravity,
// the same for all, left out of both sides of each difference. Behind a particle one in from the
// back face are the anchor's own places, since the back face rides it.
float4 Fit(uint p, int3 l, float3 ahead, float3 base, float kh, float3 drift, float4x4 m, float h, float3 home, float4 q, bool predicted, float3x3 sw, out float3 c, out float3 rc, out float4 lin)
{
    int3 n = Count();
    bool3 lo = l > 0, hi = l < n - 1;
    float s = _Lattice.w;
    float3 mx = m._m00_m10_m20 * s, my = m._m01_m11_m21 * s;
    float3 behind = home - mul((float3x3) m, mul(sw, float3(0, 0, s))) - Gravity * h * h;
    int sy = n.x, sz = n.x * n.y;
    // The differences summed, and summed signed by each axis's offset.
    float3 sd = 0, ax = 0, ay = 0, az = 0;
    [unroll] for (int dz = -1; dz <= 1; dz++)
    [unroll] for (int dy = -1; dy <= 1; dy++)
    [unroll] for (int dx = -1; dx <= 1; dx++)
    {
        // l itself adds nothing.
        if (dx != 0 || dy != 0 || dz != 0)
        {
            bool ok = (dx < 0 ? lo.x : dx > 0 ? hi.x : true) && (dy < 0 ? lo.y : dy > 0 ? hi.y : true) && (dz < 0 ? lo.z : dz > 0 ? hi.z : true);
            uint o = p + (uint) (dx + dy * sy + dz * sz);
            // Predicted, where each is headed is the first pass's, with no velocity to read. One
            // body for both: the step's single Moved call carries predicted as it comes.
            float3 x = _MainTex.Load(Texel(o, 0)).xyz, w = x;
            [branch] if (!predicted) w = _MainTex.Load(Texel(o, 1)).xyz;
            float3 d = predicted ? x - ahead : x + (x - w) * kh + drift - base;
            if (!predicted && dz < 0 && !Cored) d = l.z == 1 ? behind + dx * mx + dy * my - base : d;
            // A neighbour gone bad counts as at rest: read, it made every one round it bad too.
            d = ok && !Bad(d) ? d : 0;
            sd += d;
            if (dx != 0) ax += dx * d;
            if (dy != 0) ay += dy * d;
            if (dz != 0) az += dz * d;
        }
    }
    // How many neighbours l has along each axis, and what their offsets sum to along it.
    float3 cnt = 1 + (float3) lo + (float3) hi, bal = (float3) hi - (float3) lo;
    float k = cnt.x * cnt.y * cnt.z;
    float3 sr = bal * float3(cnt.y * cnt.z, cnt.x * cnt.z, cnt.x * cnt.y) * s;
    c = ahead + sd / k;
    rc = Rest(l) + sr / k;
    // The spread of where they are headed against their rest places, about both centres, a column
    // for each rest axis.
    float3 a0 = ax * s - sd * (sr.x / k), a1 = ay * s - sd * (sr.y / k), a2 = az * s - sd * (sr.z / k);
    // Its own linear fit, A = Apq Aqq^-1, with its volume kept (Mueller and others, 2005): over a box
    // of offsets Aqq is diagonal, each axis's spread about the rest centre. Scaled to the anchor's
    // own scale cubed, l's place on it, and the volume kept the one the homes' stretch sw gives;
    // w 0 where it has folded or has no spread along an axis.
    float3 side2 = float3(cnt.y * cnt.z, cnt.x * cnt.z, cnt.x * cnt.y);
    float3 spread = s * s * ((float3) lo + (float3) hi - bal * bal * side2 / k) * side2;
    lin = 0;
    if (all(spread > 1e-12))
    {
        float3 c0 = a0 / spread.x, c1 = a1 / spread.y, c2 = a2 / spread.z;
        float sc = length(m._m00_m10_m20), vol = dot(c0, cross(c1, c2)) / (sc * sc * sc);
        float3 r = Rest(l) - rc;
        // Eased in from a fold, and the volume's make-up held to half again: a nearly folded
        // neighbourhood was blown up 2.7 times, then dropped to rigid at a hard line, a cliff in
        // the force a squeeze rode back and forth over.
        float dsw = determinant(sw);
        if (vol > 0.05 && dsw > 0.05) lin = float4((c0 * r.x + c1 * r.y + c2 * r.z) * min(pow(vol / dsw, -1.0 / 3.0), 1.5), smoothstep(0.05, 0.3, vol));
    }
    [unroll] for (int it = 0; it < Turns; it++)
    {
        float3x3 t = Turn(q);
        float3 t0 = float3(t[0][0], t[1][0], t[2][0]), t1 = float3(t[0][1], t[1][1], t[2][1]), t2 = float3(t[0][2], t[1][2], t[2][2]);
        float3 spin = (cross(t0, a0) + cross(t1, a1) + cross(t2, a2)) / (abs(dot(t0, a0) + dot(t1, a1) + dot(t2, a2)) + 1e-12);
        float w = length(spin);
        if (w > 1e-9) q = normalize(Mul(float4(spin / w * sin(0.5 * w), cos(0.5 * w)), q));
    }
    return q;
}

#endif
";
    }
}
#endif
