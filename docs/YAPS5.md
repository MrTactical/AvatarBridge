# YAPS 5: the transport question

How a plug finds a socket is the whole system; everything else is presentation. This gathers
every route that exists, every route proposed, and every measured limit, from a month of in-game
failures and two nights reading the client. The feature wishlist (paths, portals, multi-socket)
stays in `Unfinished.md`: those ride on whatever transport wins here.

Status words used below: **shipped** (in the package), **built** (in the code, unreleased),
**proposed** (designed, not started), **postponed** (designed, deliberately not built),
**blocked** (needs something outside this repo).

---

## What runs today

*Since the rebuild (2026-08-22), a CONVERTED socket and a tool-built socket are the same object:
conversion strips VRCFury's rig and re-emits it through the native builder. Everything below
describes both at once, which is the point.*

Two channels, and which one answers is not the same question as which one is better. The channel
FINDS the socket and decides engagement; a marker light in range then replaces the position
outright, because a light is exact and sampled every frame where the channel is quantised to about
a millimetre and arrives ten times a second. So a socket carrying both is the best case, and each
alone covers a viewer the other cannot: lights reach someone whose client blocks custom shaders,
the channel reaches someone who has switched avatar lights off.

*Corrected 2026-08-27. This said a plug "tries contacts first and falls back to lights", which had
the quality ranking backwards and would lead someone to spend nine synced floats to get the worse
of the two. And until 4.4.0 a TOOL-BUILT plug had no working channel at all: it was written into a
controller ChilloutVR never uploads, so it read correctly in the editor and did nothing in game
for anybody. Converted avatars were unaffected; the converter passes its own controller.*

| channel | who reads it | what it carries |
|---|---|---|
| marker lights (DPS protocol) | legacy DPS/TPS plugs, YAPS as fallback | position + kind, via two point lights whose RANGE encodes the digit |
| contacts (`SetFromPosition`) | YAPS plugs and props | full position and axis, six axis-sampled volumes per reader |

The contact rig is **built and shipped**: six trigger volumes (X/Y/Z for the root pointer,
FX/FY/FZ for the front), each with its own `sampleDirection`, writing `(penetration.axis + 1) / 2`
into a parameter. One volume can only carry one axis because `sampleDirection` sits on the
trigger component and the component is `DisallowMultipleComponent`.

---

## The measured limits

Everything below was read from the client or measured in game. Cite this section before
designing; every failed idea so far died on one of these.

**Four vertex-light slots per mesh, filled by range.** The protocol's ranges, high to low:

| light | range |
|---|---|
| plug tracker | 0.4930 |
| front | 0.4530 |
| ring root | 0.4230 |
| hole root | 0.4130 |

The tracker belongs to whatever ENTERS a socket, a prop or the other person, so it can never
be counted at build time and one slot is always spoken for. Two lit sockets plus a tracker is
five lights for four slots, and the casualty is always the lowest range: the hole root. That is
the bug that presented as "holes broken, rings fine". **The light path carries exactly one
socket** (fix in `Places()` / `DefaultMaxLightEmittingSockets`, shipped as one).

**Match the ecosystem byte for byte.** Emit VRCFury's exact ranges, trailing digits included.
The first decimal is not free (DPS plugs saw roots with no fronts), the fourth does not survive
(range is reconstructed as `5·rsqrt(atten)`), and a tiny-range variant loses every slot fight in
company. Raliv's tolerance is 0.005, toy mods 0.001; the +0.003 offset keeps DPS and sheds the
mods deliberately.

**Contacts: 4096 registrations and 512 overlapping pairs, instance-wide.** Broadphase is brute
force but Burst: volume count is not the cost. Previous pairs re-add FIRST, so an interaction
is sticky once started and may silently never start in a saturated instance. Rejections on tags
and owner flags happen before a pair is written: **tags are the lever on the 512.**

**Contacts are wearer-only.** The wearer computes; anything remote viewers must see has to be a
synced parameter. Six floats is ~6% of the 3200-bit cap.

**Penetration space depends on the receiver's shape.** Box: local, oriented, normalised: the
right one, and the default when no Collider sits on the trigger's object. Sphere: WORLD axes, no
rotation: unusable on anything that turns. A stray Collider silently changes the shape.

**Prop writes are the toucher's job: tested, works.** `HasProbableAuthorityToApplySync` has
branches for prop senders, the world, and your OWN avatar, and none for another player's avatar.
That is not a wall, it is the wearer-only rule extended to props: the toucher's client passes the
own-avatar branch, applies the write, and the value networks to everyone from there: each client
handles only its own avatar's touches, so nothing double-writes. Proven in game 2026-08-22 with
the same shared avatar on both wearers: the prop bent for the partner's sockets on both screens,
partner on stable, spawner on beta. Consequence: a prop that ignores someone's sockets has an
avatar-side problem, pointers missing, dark, or mistyped, never a client refusal.

---

## The candidates

In the order they are worth doing, cheapest and least disruptive first.

### 1. The lighthouse: SHIPPED in 4.3.0, without the constraint

As first sketched: one marker pair per avatar instead of one per socket. Root and front lights ride a
`ParentConstraint` with every socket as a source; animating the source weights snaps the pair
onto whichever socket is active, tracking its bone. Legacy plugs see a completely standard DPS
pair and cannot tell the difference: that is the point.

- Fixes eviction outright: two lights on the avatar, nothing to evict.
- The socket menu becomes real: "active socket" moves the lighthouse, instead of Unity choosing
  by range.
- One socket at a time for legacy readers, which is not a compromise, it is DPS's own ceiling,
  and the four-slot table above shows even two were never actually possible.
- YAPS-to-YAPS can drive it automatically: plug's sender trips the socket's receiver, the synced
  parameter moves the lighthouse. Legacy props fall back to the menu.

Built without `ParentConstraint`, which dissolved both open questions: a DISABLED light never
enters Unity's ranking, so every socket keeps its own pair on its own bone and a selector layer
enables exactly one. A "Marker lights" dropdown (Int, synced, 32 bits) moves it, starts on Off,
and choosing a socket switches that socket on as well as lit: the dropdown is the one control an
old toy needs. One socket means no chooser. `YapsLighthouse.cs`, called by both the conversion and the tool.

### 2. Light colour as data: demoted 2026-08-22
*Demoted the day the gate test passed: its consumer was YAPS light-readers, and contacts are now
proven, so lights are legacy-only, and legacy plugs cannot read colour. Kept for the record.*

A vertex light hands the shader `unity_LightColor` beside position and range. The markers are
black on purpose (zero INTENSITY kills a light, black does not), so three channels per light sit
unused. Encode the socket's axis in the root light's colour and a YAPS-native socket needs ONE
light, not two: three sockets in the light budget, or one socket plus the lighthouse pair.

Strictly a YAPS-native lane: legacy plugs still need the pair, so this never replaces the
lighthouse, it rides beside it. Open: whether upload filtering clamps colour or intensity, and
how much precision survives the intensity multiply.

### 3. Contacts, kept and hardened: built

The native tier already works avatar-to-avatar and is the most information-dense channel
available without scripting. What this plan changes is posture, not machinery:

- Cross-avatar prop writes are proven working, so a silent prop means a silent SENDER: audit the
  avatar's pointers before touching the prop.
- Audit every receiver for the box/sphere trap: no Collider on trigger objects, ever.
- Keep tags tight so rejected pairs stay off the 512.

### 4. The GPU bridge: blit or camera, then a texture parser: PROPOSED, 2026-08-23

**A per-client compute channel that ends in component properties, with no scripting and no sync.**
Found by asking why PlapPlapForAll works for everyone when contacts are wearer-only.

- `CVRBlitter` runs a material from one RenderTexture into another (or several) every frame, on
  the main camera's pre-render. A full GPU pass, no camera needed. `CVRBlitterController` beside
  it. Both shipped CCK components.
- A camera rendering into a RenderTexture also survives: `SharedFilter.ProcessCamera` destroys one
  with `targetTexture == null` and KEEPS the rest, clamping `depth` to 1..99;
  `HandleRenderTextureForCamera` swaps the texture for a per-instance copy so two people wearing
  the avatar do not collide. No culling mask forced, no camera cap. A camera route also gets real
  scene geometry and per-object vertex lights, which a blit cannot see.
- `CVRTexturePropertyParser` reads one pixel, one channel, remaps `minValue`..`maxValue`, and
  writes it into a component. `CVRTexturePropertyParserTask` resolves the target by REFLECTION:
  `GetField` then `GetProperty`, public instance only, so it reaches any public field or property.
- `CVRShaderGlobals.SetGlobalTexture` publishes a RenderTexture globally, so a shader elsewhere on
  the avatar can sample the result without a parser at all.

**What it can drive:** `AudioSource.volume`/`.pitch`/`.enabled`, `Light.intensity`/`.range`,
`Transform.localPosition`/`.localScale`, and anything else exposed as a field or property.

**What it cannot:** animator parameters (behind `SetFloat`) and blendshape weights (behind
`SetBlendShapeWeight`). Both are methods; reflection here only does fields and properties. This is
a hard limit, not a gap in the reading.

**So: sound is solved and costs nothing.** A socket's audio can be computed on every listener's own
GPU and played locally: no synced parameter, no contact pair, no marker-light tolerance, nothing
to install. That beats the addon route on every axis: PCS and Wholesome each ship their own contact
receivers, which spend from the instance-wide 512-pair budget AND are forced local here, so their
sounds are wearer-only on a converted avatar today. Ship the machinery, not the audio: it cannot
redistribute Noachi's or Dismay's clips.

**PROVEN IN GAME 2026-09-07, and it survives an upload.** The rig is
`Dev/Probes/D1PrefabBuilder.cs`: a shader writes a sawtooth into a RenderTexture, `CVRBlitter`
copies it, and `CVRTexturePropertyParser` reads that pixel into a cube's height and a light's
intensity. In a live instance both ramped and reset on the sawtooth's period. Nothing else in the
prefab could have moved them: no animator, no script, no contact. A Vector3 property with a
component index and a plain float property were both written, so the reflection reading holds.

**It runs on remote copies too, and that is the whole point.** Everyone present saw the cube
moving on their own screen. A value every client can derive costs zero sync bits and never touches
the 3200-bit cap.

**Nothing is transmitted, and this was measured, not assumed.** The viewers' ramps were out of
phase with each other, offset by when each had loaded, which is `_Time.y` running from their own
level load. So the rule for anything built on this: **the shader may read only synced avatar
state**, meaning bone transforms and blendshapes driven by synced parameters. Local time,
`_ScreenParams`, frame count and the viewer's camera give a different answer per viewer. A blit
shader has no shared clock available to it at all, which is why a blit can never be the source for
a value other people must agree on; the source has to be a render of avatar geometry.

**Two things the reading above got wrong, both found building the rig:**

- **Render textures in this chain must be linear.** A default RenderTexture is sRGB, so the gamma
  curve lands on the value going in and again coming out. The first probe read 0.6431 back from a
  written 0.3725, which is the sRGB curve exactly, and looked like a broken transport.
- **Animator parameters are reachable after all.** They are behind `SetFloat`, but
  `CVRAnimatorDriver` exposes sixteen public float fields and pushes them into parameters itself.
  The catch is that it only pushes from `OnDidApplyAnimationProperties`, which Unity raises only
  when a clip animates a property on that same component, so a field written from C# sets the
  field and stops there. A looping clip animating one unused slot, with that slot's parameter name
  set to `-none-`, makes the callback fire every frame and flush the other fifteen. Built as
  `D1Pump.anim`; not yet run in game. Blendshape weights stay unreachable.

Still untested: what a per-frame blit or camera actually costs.

### 5. The screen-space atlas: postponed, deliberately

Sockets render encoded quads into a reserved screen region; plugs sample it back through a named
GrabPass. The only channel that dodges the light slots, the pair cap, the sync bits AND the
authority gate, with no socket-count ceiling, and where SPS's atlas is double-wide-native and
dies under ChilloutVR's instancing, this one would be instanced-native from the first line.

**SPIKED AND PROVEN IN GAME, 2026-08-27.** A writer quad stamping a known value into a fixed
corner of clip space, a named `GrabPass`, a reader sampling it back: the value returns exactly,
and the verdict was green locally, in the self portrait, in the CVR camera, in a MIRROR, and on a
remote user's client. So ChilloutVR keeps a `GrabPass` through an avatar upload and it works
cross-avatar, which was the whole gate.

What it measured: the grab comes back **ARGBHalf**, sixteen bits of float per channel, worst error
0.00005, which across a two metre range is about **0.1 mm**. Twelve times finer than the contact
channel, every frame instead of ten a second, and needing no smoothing, so none of the
resolution-versus-lag trade the channel has applies. `Assets/YapsSpike/` and
`Assets/Editor/SpikeAtlas.cs` in the Dracaionan project.

Writing the patch in CLIP space, ignoring both the object's transform and the eye, makes it
stereo-proof by construction: both slices of the eye texture array get identical content, so there
is no double-wide layout maths to get wrong. That is the part of SPS that does not survive
conversion, and it is simply never available. It also means the patch lands at the same UV in every
camera's frame, so a grab leaking between cameras still finds the right pixels.

**The cell problem may have an answer that fails safe.** Give each socket a build-time random id,
hash it to a cell, and write the id into the cell beside the position. A collision then decodes to
some other socket's position, which is almost always metres away, and the engagement range gate
already rejects that. Collisions would degrade to "no socket found", falling back to lights or
contacts, rather than "wrong socket found". Untested.

### The cost is smaller than it looked

A **named** `GrabPass` grabs once per frame per NAME, not once per object: Unity does the grab for
the first object that uses that texture name and every other material referencing it reuses the
result. So twenty sockets in a room sharing one `_YAPS_Atlas` cost ONE grab per camera, not
twenty. The cost is bounded by cameras rather than by content, which is the opposite of the
contact system where cost scales with pairs. Worth confirming with a frame timing, but the
architecture is not the problem it was assumed to be.

### Rendezvous is the hard part, not collisions

The framing has been wrong. Two avatars picking the same cell is a real problem, but it comes
SECOND. The prior question is how a plug knows which cell to read at all. With sixty-four cells
and no scheme, a plug samples all sixty-four, per vertex: on a three thousand vertex plug that is
nearly two hundred thousand taps a frame. **The search is what kills it**, and no amount of
collision handling helps.

### Proposed: hash the socket's WORLD position into the cell

A socket quantises its world position to a grid, half a metre say, and hashes that to a cell. A
plug hashes its OWN position and the neighbouring grid cells, about eight of them, and samples
only those. Neither side negotiates: both compute the same function from the same world.

    search cost   about eight taps, fixed, however many sockets exist
    collisions    only between sockets in the SAME half-metre cube, which are physically
                  adjacent, so either answer is nearly right rather than wrong
    range         falls out for free, since a plug only reads cells within its own reach
    atlas size    small, because cells are reused as people move and only occupied ones matter

It also fails safe in the way the earlier id-hash idea wanted: a collision returns a socket a few
centimetres away rather than one across the room, and the engagement gate handles the rest.

### Spike 2, 2026-08-27: a moving position survives, and precision is set by BOX SIZE

A marker publishing its own world position from its own matrix, dragged around the scene, decoded
from the grab and measured against the truth:

    1.2 m from the origin    4.10 mm error
    3.9 m from the origin    4.60 mm error
    8.6 m out, box is 8 m    clamped at the edge, not a precision failure

**The prediction that error grows with distance was WRONG.** Normalising to 0..1 before encoding
removes the absolute-magnitude problem, so the error is roughly constant wherever you stand. It is
`box_size x half-float-step`: a half near 0.5 steps by about 0.000488, and 0.000488 x 16 m is 7.8
mm worst case, which is what was measured.

**That makes the case for cells stronger, for a different reason than assumed.** Precision trades
directly against range, and the clamp shows range cannot be bought by growing the box. A **0.5 m
cell gives 0.5 x 0.000488, about 0.24 mm**, five times finer than the contact channel, with
range coming from HOW MANY cells exist rather than from how big one is. That is the argument for
the spatial hash, measured rather than asserted.

Also confirmed by construction: the payload has to come from the object's own matrix, since no
script runs on someone else's avatar to set a material value. So **a socket marker for this can
never be a skinned mesh**: Unity skins into world space and hands a SkinnedMeshRenderer identity,
so a skinned marker cannot say where it is. Same fact as the channel decoding unrotated, from the
other side.

Rig: `Dev/Probes/Atlas/`.

### Spike 3, 2026-08-27: the rendezvous WORKS

A socket publishes into a cell derived from its own world position; a plug derives its own cell,
reads that and the neighbours out of the grab, and finds the socket without being told anything.
Measured in Play Mode:

    plug at (0.3500, 1.1500, 0.2500)   cell (0,2,0)
    taps    27 (radius 1)
    found   (0.2000, 1.1000, 0.2998)
    nearest Socket A at (0.2000, 1.1000, 0.3000)
    error   0.20 mm

**0.20 mm**, against the 0.24 the scheme predicted, and six times finer than the contact channel.
Two independent parties derived the same cell from the same world coordinates with no negotiation,
which is the whole design.

**Occupancy needs a CLEAR PASS, and now has one.** Alpha cannot be trusted on its own: a grab
returns whatever the screen had at those pixels, the screen is opaque, so every cell reads as
occupied. The first run of this spike reported all twenty-seven cells as hits, every one decoding
the floor. One quad covering the atlas rect, queued at `Overlay-200` against the writers'
`Overlay-100`, paints alpha 0 first. Ordering comes from the QUEUE rather than from distance,
which matters because the clear and the writers sit on different avatars and nobody controls
distance sorting. After it: two hits out of twenty-seven.

**A collision happened and failed safe, in the log.** Cell (-1,3,0) reported a hit carrying Socket
A's payload: two world cells landed on the same square of a 64-cell grid, so A's offset decoded
against the wrong origin and produced a phantom 0.79 m away. The real socket was 0.166 m away and
nearest-wins ignored it. That is the fail-safe property demonstrated rather than argued.

**It also names the knob:** 27 taps into 64 squares makes collisions likely, not rare. A 32x32
grid is 1024 squares for the same 27 taps. Grid size trades screen area against collision
frequency, and the spike makes both adjustable.

**Also settled:** the grab reads back with Y FLIPPED relative to clip space. The reader shader
compensates through `_TexelSize.y`; anything doing its own readback has to as well.

### What is left

    encoding a moving position   done, spike 2
    rendezvous                   done, spike 3
    occupancy                    done, the clear pass
    per-camera cost              measured in the editor, below the noise floor; see below
    frustum culling at range     partly, huge bounds worked in spike 1
    a plug reading it in ITS
      OWN SHADER rather than
      in C#                      not started, and the real work

**Still unanswered before this could ship:** the per-frame cost. A named grab runs PER CAMERA:
main view, each eye, the portrait, the CVR camera, every mirror, so the atlas has to be ONE grab
shared by everybody, never one per socket, or it scales catastrophically. That constraint is also
why cell allocation matters: a shared atlas is the only affordable shape. And a viewer whose
safety settings block custom shaders gets nothing from it, where marker lights survive that,
because lights are components rather than shader work. So it is a third leg, not a replacement.

### 6. Scripting: blocked

The endgame: read any avatar's sockets whatever protocol they speak, no slots, no pairs, no
bits. Access was requested and declined 2026-08-19 (world scripting is their focus). Everything
above is designed so that none of it is wasted if this arrives: the resolver's tiering is
already script-first, contacts next, lights last.

---

## Order of work

1. ~~Reserve the tracker's light slot~~: **done**, `b82e9d0`.
2. ~~The two-person authority-gate test~~: done 2026-08-22, gate disproven: cross-avatar prop writes work, the toucher's client syncs them.
3. ~~The socket rebuild~~: **shipped in 4.3.0** (2026-08-23): corpus 385/386/387 clean, a tester's rebuilt mouth socket working with a DPS prop in game.
4. ~~Lighthouse~~: **built 2026-08-22** without the constraint: per-socket pairs, one enabled, dropdown selector.
5. Light colour: shelved: its consumer was YAPS light-readers, and the gate test proved contacts trustworthy, so lights are legacy-only and legacy plugs cannot read colour. Revisit only if fallback pressure appears.
6. ~~Corpus, with the two missing avatar classes added~~: done: Fixture_DeformSocket and
   Fixture_HeadTransplant are in the corpus and its baseline, and gated 4.3.0.
7. ~~Spike the screen-space atlas~~: **done 2026-08-27, and it works.** GrabPass survives a
   ChilloutVR avatar upload, cross-avatar, in mirrors. See candidate 5 for the measurements and
   for what is still unanswered: the per-camera cost, and cell allocation.
8. The contact channel reaching a tool-built plug: **fixed in 4.4.0**. It had never worked in game
   for anybody on that path.

### Cost, measured in the editor 2026-08-27

180 frames with the atlas active against 180 without, same scene:

    cameras 1, sockets 2     4.494 ms with, 4.522 without, difference -0.028 ms
    cameras 7, sockets 32    4.567 ms with, 4.793 without, difference -0.226 ms

**Both differences are NEGATIVE**, which is impossible and is the point: the grab costs less than
the frame-to-frame noise, so any number read off this is fiction. What the run does establish is
the architectural claim: **thirty-two sockets cost no more than two.** A named grab happens once
per frame per NAME, so cost is bounded by cameras rather than by content. Had that been wrong the
32-socket run would have moved.

**What it does NOT establish is the per-camera slope.** A camera only grabs if something carrying
the GrabPass renders in it, and the extra cameras in this rig look at a point from a distance, so
the reader quad may simply have been culled out of them. The camera number is unverified.

**A real verdict still needs the game**, with a mirror open and a headset rendering two eyes, and
with a plug's shader doing the 27 taps rather than C# doing them. The taps are the cost that has
never been measured at all: one grab per camera is cheap, twenty-seven texture reads per vertex
on a three thousand vertex plug is a different question.

### The taps are affordable too, 2026-08-27

Measured properly, holding every object still and changing ONLY the tap count:

    1,318,400 vertex reads a frame    0.177 ms and 1.031 ms across two runs

A real plug is about three thousand vertices at twenty-seven taps, which is 81,000 reads: a
SIXTEENTH of what was measured. Ten plugs in view is still under a millisecond. Both halves of the
cost question are cheap.

**Three wrong readings came out of this before the right one, all from one harness flaw.** The
first said the taps cost 7.5 ms; the second, seeing that cost stay flat against twelve times the
workload, said it must be a pipeline stall. The truth was that the on/off toggle disabled the
whole spike root, so it was changing whether the atlas ran AND how much geometry was in the scene.
Forty spheres cost about 7.5 ms whether they read the atlas sixty-four times or not at all, and
the 0-tap run is what exposed it.

**An A/B harness has to change exactly one thing.** This one changed the scene, and every reading
after that was interpretation of a confounded number.

### Where the atlas stands

    GrabPass survives upload, cross-avatar, mirrors   proven in game
    stereo                                            solved by construction
    a moving position encodes and decodes             spike 2
    rendezvous with no negotiation, 0.20 mm           spike 3
    occupancy                                         the clear pass
    collisions fail safe                              observed in the log
    grab cost                                         below the noise, bounded by cameras
    tap cost                                          under 1 ms at 16x realistic load

**What is left is all in-game or unbuilt:** a plug reading the atlas in its OWN shader rather than
in C#, VR two-eye rendering, a real mirror, and what a viewer's safety settings do to a shader
that has to run for the transport to work at all.

### Visibility, closed 2026-08-27

The last question that could have forced a redesign: any pixel the atlas writes IS on screen.
There is no off-screen region an avatar shader can reach, clip space beyond the unit cube is
clipped, and there is no scratch buffer. So the atlas cannot be hidden; it has to be
imperceptible. It turned out to have two independent answers rather than one compromise.

**The clear pass is invisible, for free.** It covers the whole atlas rect and was by far the most
conspicuous part, a black square in the corner of every frame. It only ever needed to write ALPHA,
so `ColorMask A` leaves the colour channels untouched: the rect keeps whatever the scene drew and
still reads alpha 0 for occupancy. Payload unchanged after the mask, 0.0004 and 0.0002.

**The cells go down to ONE PIXEL.** Measured at 6 px, 2 px and 1 px, the payload reads back
identically every time. An 8x8 grid at one pixel a cell is an **8 by 8 pixel atlas** carrying
sixty-four addressable cells at 0.2 mm.

**Getting there needed pixel snapping, and that is a real implementation requirement.** Placing
the atlas in CLIP space works at six pixels a cell and fails at two: clip space knows nothing
about where pixel boundaries are, so a cell 0.002 wide is 1.92 px at 1920 and each cell drifts a
fraction further than the last. The failure is abrupt and position-dependent, which is how it was
identified: the cell at grid (0,4) read perfectly while (6,7) read a neighbour, because drift
accumulates with the grid index. Placing the atlas in PIXELS from `_ScreenParams`, with the
readback doing identical arithmetic, makes cell centres exact by construction.

It also retires the Y question. Pixel rows count down from the top while clip space counts up, so
the flip stops being an empirical discovery and becomes one line of the same maths.

**What the visible footprint actually is:** one clear pass that paints no colour, and one pixel
per socket in a screen corner. The reader needs no visible geometry at all, since in the real
system the plug's own shader carries the GrabPass.

---

## The atlas is now an implementation job, not a research one

Every question that could have killed it has an answer:

    GrabPass survives a CVR upload, cross-avatar, in mirrors   proven in game
    the WHOLE atlas: hash, buckets, levels, two-pass socket    proven in game 2026-09-03
    stereo, single-pass instanced                              proven in game, VR, both eyes
    a moving world position                                    0.2 mm
    rendezvous with no negotiation                             works, spike 3
    occupancy                                                  clear pass, ColorMask A
    collisions                                                 fail safe, observed
    grab cost                                                  below noise, bounded by cameras
    tap cost                                                   under 1 ms at 16x realistic load
    visibility                                                 8x8 pixels, clear invisible

**What is left is building it**, and three things worth settling first:

1. **Can a patched Poiyomi vertex stage take it?** The spikes are clean tiny shaders. A real plug
   wears somebody's Poiyomi, patched by us, already doing vertex work. Instruction counts,
   register pressure and Poiyomi's variants are all unknown.
2. **One renderer per socket.** A socket today is lights and pointers with no mesh of its own. The
   atlas needs each socket to draw something every frame: a draw call per socket per camera, cheap
   individually, but a new per-socket cost where lights had none.
3. **The resolver holds ONE socket.** `yaps_resolve.cginc` picks a single best candidate, and the
   atlas returns a NEIGHBOURHOOD. Spike 3 found two and threw one away. The feature wishlist in
   `Unfinished.md`, a ring mid-shaft and a hole at the tip, portals, duplication, is exactly
   "stop discarding what the atlas already returns", so the transport and the features want the
   same change.

### Two of the three implementation questions are closed, 2026-08-27

**1. A patched Poiyomi CAN take it.** The real shader a plug wears, 723 KB of locked Poiyomi with
the deform already injected, took the hash and twenty-seven reads: **compiles, zero messages**.
`Dev/Probes/Atlas/PoiyomiHeadroom.cs` copies the shader a plug is actually wearing, injects the
block beside the existing `YapsSocketDeform` call, imports it and reports. It touches nothing that
ships.

Two things learned building it, both worth keeping. Anchor on the CALL, `YapsSocketDeform(yapsPosition`,
never on the string `YapsSocketDeform(`: the DEFINITION matches first, spans two lines, and an
insert lands in the middle of its parameter list. And read through `_YAPS_Bake`, a `Texture2D`
already declared and already read with `.Load`: that is an unfiltered point read at integer
coordinates, which is exactly what a one-pixel cell needs, since filtering would blend the
neighbours that break it. The production version wants `.Load` on a `Texture2D`, not `tex2Dlod` on
a `sampler2D`.

**2. One renderer per socket costs nothing measurable**: already answered by the cost run. The
"extra sockets" in that test were real renderers, one mesh each, and thirty-two measured the same
as two. A socket gaining a mesh is a draw call it did not have, but it does not show.

**3. The resolver still holds ONE socket.** That is the work, not a question. `yaps_resolve.cginc`
picks a single best candidate; the atlas hands back a neighbourhood. Everything on the feature
wishlist in `Unfinished.md`, a ring mid-shaft and a hole at the tip, portals, duplication, is
described there as needing "an ordered list of sockets with arc-length ranges", which is what the
atlas already returns and the resolver currently discards.
### Spike 4, 2026-08-27: a plug and a socket, on the atlas alone

The third implementation question was "the resolver still holds ONE socket, and that is
the work". It is built, in `Dev/Probes/Atlas/`, and it runs: a plug threads a chain of
sockets with no light, no contact, no animator parameter and no sync anywhere in it.

**The socket takes two pixels, not one.** Slot 0 is `frac(worldPos / cellSize)`, slot 1 is
`forward * 0.5 + 0.5`. One quad covers both and the fragment picks by where it landed
inside the quad, so it is still one draw. Position alone is enough to FIND a socket and not
enough to ENTER one: a plug curving toward a bare point aims at its centre and stops, and
rotating the socket does nothing. Orientation is also the thing the light protocol
structurally cannot carry, since a marker light encodes its kind in two free digits of a
range and a mesh only gets four slots.

**The resolver keeps an ordered list.** Sockets are sorted nearest-first and each claims a
range of arc length along the shaft. The shaft is a chain of cubics: out of the root along
the plug's axis, into socket one against its facing, out of socket one the far side, into
socket two. Position and tangent match at every join because the segments either side share
them. This is the shape `Unfinished.md` asks for, and it removes the flip between two
sockets by construction: there is no winner to flip to, and nothing has to ask which way the
plug points, because the order IS the path.

**A socket is turned to meet its approach.** A ring is enterable from either face, so which
of the two the author pointed it is not information. Trusting it means the cubic is asked to
arrive travelling backwards, and it ties itself in a hairpin to do it. A HOLE is one-sided
and would want the sign respected, so the moment holes and rings share a list the flip has
to be conditional on the socket's kind, and the atlas has no field for kind yet.

**The ceiling is 2r+1 sockets, and it is not the list length.** A plug spanning L must see
cells covering L/2 either side of its midpoint, so `cell >= L / 2r`; one cell holds one
socket, so sockets sit at least a cell apart. Divide and the cell size cancels: three at
radius 1, five at radius 2 for 125 reads against 27. Raising the list length past that buys
nothing. The way out is a cell holding SEVERAL sockets, which decouples spacing from cell
size and is not built. Note also that cell size is a protocol constant, not a per-plug
setting, since sockets hash with it too.

Hashing moved to the MIDPOINT of the shaft rather than the root, which halves the radius the
same coverage needs. One line, no extra taps.

#### Three failures worth keeping

**The flip signal was the wrong signal.** `_TexelSize.y < 0` says a UV SAMPLE needs its v
inverted. It says nothing about which end of memory holds row zero, and `.Load` indexes
memory rows. Reading it as one sent every tap to row 906, where the opaque scene answers
alpha 1, so all twenty-seven cells claimed a socket and the plug bent at the nearest piece of
floor. Row order is `UNITY_UV_STARTS_AT_TOP`, a compile-time platform fact. Settled by
instrumenting rather than arguing: debug colours came back WHITE with the runtime signal
(27 hits, flipped) and pure GREEN with it forced top-down (1 hit, not flipped).

**A wider read aliases into itself.** Sixty-four grid slots and a radius-2 read looks at 125
cells, so by pigeonhole some land on a slot another cell owns, and what comes back is a real
socket's payload decoded against the wrong cell: a convincing phantom a few centimetres away
that the sort then threads the shaft through. The payload cannot reveal this on its own,
because an offset within a cell decodes into that cell whichever cell you decode it against.
Alpha was carrying one bit in a sixteen-bit channel; it now carries a second independent hash
of the cell across its top half, 256 levels, four half-float steps apart, with the bottom half
still meaning empty. The reader recomputes and drops what does not match.

**A deadzone exactly one cell tall is a slot clash.** Socket B was dead between y 1.258 and
1.437, working either side; at eighteen-centimetre cells those are the bounds of one cell.
Two sockets on one slot means the second to draw overwrites the first and the tag check
correctly drops the loser, silently, for as long as it stays in that cell. Three sockets in
64 slots clash about seven per cent of the time and a plug wandering meets those odds in
every cell it crosses. **The grid must be sized to the expected socket count, not to the
atlas budget**: 64x64 is 4096 slots for a 136x72 pixel atlas. And a clash has to be
NON-SILENT in the shipped version, because a socket that quietly stops existing for one cell
of space looks identical to every other reason one does not bend.

#### What is still open

- A cell holds one socket. Buckets decouple spacing from cell size and lift 2r+1.
- No socket KIND in the payload, so a hole cannot be told from a ring, and the
  approach-flip above is only correct for rings.
- Portal and duplicate are ranges on top of the list, unbuilt.
- Radius 2 is 125 reads a vertex, measured only for 27. The tap harness exists.
- The platform default for row order is right on D3D by construction and unverified in game.
  `_ForceRow` is kept as the one-click check.

### Spike 5, 2026-08-27: buckets and levels, which are the same change

Both open items from spike 4 are built, and they turned out to be one idea: a cell that
holds more than one thing.

**Eight sockets to a cell, indexed by OCTANT.** Which octant of the cell the socket sits in
is deterministic, needs no negotiation between sockets that cannot see each other, and
separates anything more than half a cell apart in any axis. The bound moves from 2r-1 to
4r-1, and it closes the one clash double hashing could not: two homes are a property of the
CELL, so two sockets in one cell shared both of them.

**A HEADER pixel makes buckets affordable.** Reading all eight octants everywhere would
multiply every tap by eight. Instead the cell leads with one pixel whose alpha is built by
ADDITIVE blending on alpha alone: each socket adds 1/255, nobody sees anybody else, and the
sum is exactly how many sockets are in that slot. Nearly every cell is empty and still costs
one tap; a cell holding anything costs eight reads nothing else needed.

It was a BITMASK of live octants first, and that is the interesting failure. **Addition is
only an OR while the bits differ.** Two sockets in one octant give 1+1=2, so the octant that
IS occupied stops being advertised and a neighbour that is not starts being, and with
several sockets in a cell the carries cascade until the cell reads empty. Measured exactly:
five sockets on a thirty metre plug threaded and six did not, because a level-4 cell is
5.12 m and six sockets space at 4.29 m, which is where pairs start sharing a cell. A count
cannot carry. Strictly less information, and it survives contact with the failure case.

**Several cell sizes at once.** Cell size is a protocol constant, so one number has to serve
a twenty centimetre plug and a twenty metre one, and it cannot: coverage wants about L/2r.
The atlas now holds N levels, each four times the last, each in its own band of rows. A
socket publishes to every level, one small draw each; a plug picks the level nearest its own
L/2r and reads only there, so read cost does not move at all. Six levels span 2 cm to 20 m.

Confirmed in the editor: a **thirty metre plug threading six sockets**, with the remaining
two correctly reported past the tip rather than lost.

#### What this costs, and what is still open

- **Visibility went backwards.** The 8x8 pixel atlas is now grid x 17 slots wide by
  levels x grid tall, and every socket paints a pixel per level per home. At slot size 4 and
  eight levels that measured 1096 x 520 px of scattered dots. Slot size 1 and four levels is
  sixteen times smaller and probably all anyone needs.
- **The chain is longer than the shaft.** Sockets spread over a plug's full length make a
  path that snakes, and arc length is measured along the path, so the last socket or two can
  sit past the tip even though the spacing looked fine. The practical count is a little
  under what the spacing bound suggests.
- Two sockets in one OCTANT is the last clash, and nothing here fixes it. It is also the
  genuinely ambiguous case.
- ~~No socket KIND in the payload~~: **built.** The facing pixel's alpha was carrying a
  second copy of the tag that nothing read, so it carries the kind instead: sixteen fit.
  A ring is turned to meet its approach; a HOLE keeps its sign and is dropped from the chain
  when approached from behind. The facing read moved into the gather with it, which costs one
  read per socket FOUND rather than per cell searched and deleted the separate facing pass.
- Portal and duplicate are ranges on top of the list, unbuilt.
- 343 headers a vertex at radius 3 has not been benchmarked. The tap harness exists.
- ~~NONE OF IT HAS BEEN IN GAME.~~ **CLOSED 2026-09-03, all four steps of pass 1.** Spike 1
  had proved only that a named GrabPass survives a ChilloutVR upload and returns what another
  avatar rendered. Everything since: the cell hash, the tag, the two homes, the octant
  buckets, the additive alpha header, the level pyramid, the whole two-pass socket shader:
  had only ever run in the editor. It all runs in game. See the pass 1 record below.
- **Nothing is in the shipped shaders.** `yaps_resolve.cginc` still resolves ONE socket from
  lights and contacts. How the atlas coexists with those two, and what happens when only one
  side of a pair has it, is unanswered and is the real design work.
- **The atlas result can reach the animator without contacts** (recorded 2026-09-01, not
  acted on). Decompiling the client's component whitelists turned up
  `CVRTexturePropertyParser`: async GPU readback of a RenderTexture, per-pixel tasks that
  write any public field on any component. Atlas shader to a one-pixel RT via `CVRBlitter`,
  parser task into a `CVRAnimatorDriver` field, and the "seated" bit becomes a SYNCED
  parameter with no contact receivers at all. That is the answer to "how does resolution
  drive toggles and haptics", not a rival transport: the deform stays on the atlas, this is
  the exit ramp. Unproven in game, beta DLL, viewer-side it sits behind the Cameras content
  filter (own avatar skips filters, and wearer-computes-AAS-syncs is the contract anyway).
  Full sweep in the memory file `cvr-avatar-component-channels`. Do nothing with it until
  the atlas itself has been in game.

### A1, 2026-09-03: the protocol, and the one number that cannot be frozen yet

The atlas is the first shared mutable surface in this system. Every other transport is
private: a socket owns its lights, a plug owns its contact receivers, and an avatar built by
an older version of the tool simply does not participate. The atlas has ONE grid, ONE cell
size, ONE origin and ONE level count, and a socket hashes its world position with all of
them, so an avatar built by a later version writes cells this reader addresses differently.
That is not absence. It decodes as a real socket a few centimetres away, which is the same
phantom a slot clash produces and the reason the tag exists at all.

**The version rides the tag.** Built, in both spike shaders. `CellTag` mixes
`YAPS_ATLAS_VERSION` into its hash, so a socket written under a different protocol fails the
check the reader already performs and is dropped through the path already written: no extra
pixel, no extra read, no extra branch, no new failure mode. It cannot be retrofitted, because
the thing a later version would negotiate with is the version that predates negotiation.

    frozen      the cell hash, the second-home hash, the tag hash and the version in it
    frozen      the payload layout: header alpha counts, slot 0 position, slot 1 facing+kind
    frozen      the octant bucket rule and the additive alpha header
    frozen      levels step by 4x, each in its own band of rows
    NOT frozen  grid, cell size, level count, slot size

**Grid size is the open one, and it is an occupancy question.** Spike defaults are grid 16,
which is 256 slots. Sockets clash by hashing to a slot another cell owns, and the second home
is the recovery, so what matters is the chance of clashing on BOTH. For n sockets at one
level:

    slots   grid   both homes clash, 120 sockets
    256     16     ~14%
    1024    32     ~1.2%
    4096    64     ~0.08%

Twenty people carrying six sockets each is 120, which is an ordinary public instance, not a
worst case. At grid 16 that is a socket silently vanishing about one time in seven, and a
clash has to be non-silent in anything shipped. Grid 64 is the first size where it stops
mattering.

**Grid size does NOT cost visibility, which was the fear.** The atlas rect grows with the
grid, but the rect is not painted: the clear writes alpha only, so an empty cell keeps
whatever the scene drew there. What is visible is one pixel per socket per level per home,
and that count follows the SOCKET count, not the grid. A bigger grid scatters the same dots
over more screen. It costs no read either, since a plug still taps its neighbourhood.

**What blocks the freeze is a render target size, and it is measurable today.** The atlas
rect at grid 64, slot 1, four levels is 1088 x 256 pixels, and the writer places pixels from
`_ScreenParams`. Every camera that has to carry the atlas must be at least that wide.
ChilloutVR renders mirrors into their own render texture, and a mirror is routinely rendered
smaller than the main view. The in-game mirror test passed at grid 16, which is 272 pixels wide, so
it says nothing about 1088.

**The measurement:** raise the spike's grid to 64 and repeat the mirror test. If the mirror
clips the atlas, the answer is not a smaller grid, it is a rect that scales with
`_ScreenParams` or a per-camera fallback, and that is a protocol decision that has to happen
BEFORE version 1 ships rather than after.

### The wear rig, 2026-09-01: built to close the biggest gap

The in-game test no longer needs hand assembly. `SpikePlug.cs` grew a second build path:
select the CVR avatar, press **Build on the selected avatar**, and the same four draws are
placed as plain renderers on the shared materials. No scripts, no animator work, no menu
entries, so the CCK packs everything the upload needs. The clear and grabber quads sit under
the avatar root, counter-scaled so import scale cannot shrink them back into culling range.

The sockets ride the hands and the head, which answers "how do I move them in VR" with the
controllers themselves: moving a hand is moving a socket, and rotating the wrist is the
arrival-axis test. Ring on the left hand, HOLE on the right, ring on the head, all facing
the wearer's back, where the plug approaches from. The plug grows forward from the hips.
No toggles: a socket that should vanish is a hand put behind the back, out of the read box.

Defaults dropped to **four levels** on the visibility note above (cells 2 cm to 1.28 m,
plugs to about five metres); the slider still goes to eight if a longer plug needs it.

Pass 1, in order, each step only after the one before holds:

1. Play mode in the editor, worn. The plug bends to a hand brought near it. Proves the
   build, not the game.
2. In game, flat screen: the same three sockets. The first time anything past spike 1 has
   run in game at all.
3. In VR. The eye buffers are the real unknown: the writer places pixels in clip space per
   eye, but what an instanced-stereo GrabPass returns has never been measured, and whether
   spike 1's in-game proof covered VR was not recorded.
4. A mirror. The mirror camera runs its own grab at its own queue order, so the mirrored
   plug should bend on its own.

### Pass 1 PASSED, all four steps, 2026-09-03

Worn rig on a humanoid avatar, uploaded through the CCK, tested in game.

    editor Play, worn        bends to a hand brought near it
    in game, flat screen     bends, engaged colour
    in game, VR              bends, both eyes agree, and CROSS-AVATAR
    in game, a mirror        bends in the reflection, on the mirror's own grab

**The cross-avatar result is the one that was not on the list.** A socket riding somebody
else's hands published into the shared grab and this plug read it, which is the property the
whole transport exists for and the one no editor test can show. Spike 1 proved a value
survives the round trip; this proves the addressed, hashed, bucketed, levelled version of it
does, between two people, in a public instance.

**Stereo needed nothing.** The reasoning in the plug shader's header: the patch is written
in clip space ignoring the eye, so both slices of the instanced array carry identical content
and slice 0 is always right: held without a line changing. Same for the row order: the
platform default was correct in game and `_ForceRow` was never needed.

**The mirror settles the per-camera question**, which is the one that killed marker lights as
a primary transport. A deform runs per camera, so a transport whose answer depends on which
camera is asking bends the mesh differently in a mirror than in the view. The atlas does not:
the mirror runs its own grab, decodes the same cells, and arrives at the same shape.

What that leaves is not a research question any more. Every transport property the atlas
needed is measured, in game, on real hardware.

Synced to the Dracaionan project (`Assets/Editor` + `Assets/YapsSpike`) 2026-09-01, along
with the eight older spike files the project still had pre-collection copies of.

### Grid 64 passed everywhere, 2026-09-03: the last open number closes

The blocker in A1 was a render target width. It was real, and it bit before the mirror ever
got a chance to: at grid 64 the editor showed a bend dropping out every few centimetres of
idle sway. That reads as a distance threshold and is not one. A clipped slot does not come
back empty, it comes back as whatever opaque pixel the screen had there, so a socket whose
cell hashes past the right edge is discarded by the tag and simply vanishes until it moves
cell. Dragging the Game view wider cured it, which is what identified it.

**The fix separates two numbers that were one.** Cell count sets correctness, since it is
what clash probability is computed from. Column count sets fit, since it is what decides how
wide the rect is. They were equal out of habit, so raising the grid to cut clashes silently
pushed the atlas off the side of the screen. `_Cols` is now derived once in C# as the divisor
of the grid closest to square and pushed to both shaders. Grid 64 lays out as 32 x 128 cells,
552 x 520 pixels, instead of a 1088 x 256 strip. The clash table is untouched: 4096 cells
before, 4096 after.

Protocol version 2, since the layout moved. An old socket and a new plug now refuse each
other through the tag rather than decoding each other's rows.

**Every camera passes at grid 64**, tested by wearing it:

    editor Play                     bends
    desktop, first person           bends
    desktop, third person           bends
    desktop mirror                  bends
    VR                              bends, both eyes
    VR mirror                       bends
    personal mirror                 bends

That is the full set, including the two cameras that were only assumed before: third person
renders from a camera the wearer does not control, and a personal mirror is a second mirror
render on top of the world one. Grid 64 is settled, and with it the last number A1 left open.

**One cosmetic item is now visible and belongs on the shipping list.** The payload pass
writes colour as well as alpha, because the payload IS colour, so an occupied cell paints a
few pixels on screen. It shows as a small coloured dot near the corner. The count follows
socket count rather than grid, so it does not grow with the atlas, but it is not nothing and
it is the kind of thing a user reports as a rendering bug.

### A2 and A3 pass in Play, 2026-09-03: the shipped path publishes

A converted avatar's own socket bent a plug, with nothing of the spike in the scene except the
plug itself. The shipped writer wrote it, the shipped clear made the cell readable, the shipped
grab carried it.

The test only says that because of what was NOT in the scene. The wear rig now detects the
shipped clear and grab on the root and builds a reader alone. Its own clear covers the same 552
by 520 rect and would have masked a broken shipped one completely; its own sockets ride the
hands, which is the easiest thing in the scene to bring near a plug, so a bend could have been
attributed to either publisher. One reader and everything else shipped is the only arrangement
where a pass means what it looks like it means.

Editor Play only. In game is a separate claim and has not been made.



## A4 passes, 2026-09-03: the shipped plug resolves

A CONVERTED plug bent toward a converted socket with no spike shader anywhere in the scene. That
is the whole path in shipped code: the writer under the socket, the clear and the grab on the
root, the reader inlined into a patched shader, tier 3 taken over the marker light that was also
in range.

Three compile failures came first and none of them were visible. The patcher inlines a
hand-written list of includes and strips the rest, so yaps_atlas.cginc was removed and never
added back; then twice on the rule that a small local array only stays in registers while every
index is a compile-time constant. A failed patch is CAUGHT and the plug converts as an ordinary
mesh, so the avatar looked perfect and did not bend. Dev/Probes/Hlsl compiles the same include
set with fxc in about a second and would have found all three without a reconvert.

The Atlas taps view was built during this, and earned itself immediately: it separated "nothing
published" from "published and thrown away", which is what turned the last failure from a guess
into a reading.

A ring resolved and a hole did not, and I read that as the hole-back test working. WRONG, and
corrected the same day: see "The hole-back test was a contradiction" below. The deform already
flips a socket's axis to meet the approach, so rejecting a hole on facing threw away sockets the
deform would have handled, and under the documented convention it threw away the correctly aimed
ones. The test is gone; range is the only inclusion test now.

Editor Play only. In game is a separate claim and has not been made.

### The toolkit's own Build got the avatar half, 2026-09-03

A2 put the writer on the socket and A3 put the clear and the grab on the avatar root, but only
on the CONVERT path. An avatar built by hand out of the socket prefabs had every writer and no
surface: sockets publishing to a screen nothing grabbed, and no clear, so every cell read as
occupied. `BuildAll` now adds the same two objects behind the same `YapsAtlas.Enabled` switch.

Regenerating the prefabs (Tools, YAPS, Create universal socket prefabs) is what puts the writer
into `YAPS Hole` and `YAPS Ring`; `Build` is what completes the avatar. The plug prop prefab is
separate, and it bakes and patches its shader when it is created, so it needs recreating to pick
up the resolver.

### The hole-back test was a contradiction, 2026-09-03

A loose hole in the scene read *two thirds* on Atlas taps: its payload reached the plug and
matched the cell tag, and then the candidate filter threw it away. The rejector was the facing
test, `kind > 0.5 && dot(fwd, at - root) > 0`, carried over from the spike.

It cannot stand beside the deform. `yaps_deform.cginc` flips the socket axis to meet the
approach, and says why in a comment: a converter inherits whatever convention the source avatar
used and cannot dictate one. The resolver was rejecting on exactly the thing the deform is
written not to care about, and on the convention the toolkit documents ("point its +Z the way a
plug should enter") it rejected the correctly aimed holes and kept the backwards ones.

Removed. Range is the only inclusion test now, which is also what rejects a hash collision from
across the world. If a hole still reads two thirds, the rejector is `d > far`, which is
`worldLength * 2.56`, and that is now unambiguous.

### A4 confirmed on the atlas alone, 2026-09-03

The earlier A4 pass resolved a socket on the avatar's own hand, which left a marker light as an
alternative explanation. This one does not: a standalone hole in the scene, its `YAPS Lights` and
`YAPS Pointers` folders switched OFF, with the plug's preview off so the C# route wrote nothing.
*Resolved by* read FULL, which is tier 3, and tier 3 is set in one place.

Nothing but the screen carried the position. The socket's writer published it, the avatar's grab
read it back, and the plug bent.

Removing the facing test is what turned it on: the same hole read a tenth an hour earlier with
Atlas taps at two thirds.

## A5 passes, 2026-09-03: Phase A is closed

A locked Poiyomi 9.0.61, through the converter, reading the atlas.

The plug material became `Hidden/Locked/YAPS/f06d81a2d0d6` off a 400 KB locked Poiyomi Toon with
six passes and three vertex stages. The patch took, `_YAPS_UseAtlas` came out of the converter
already at 1, and with the socket's lights and pointers both switched off the plug bent fully.
Nothing but the screen could have carried that position.

That closes the last question the headroom probe left open. The probe said a locked 723 KB
Poiyomi could take the hash and 27 reads with zero messages, but a probe is a synthetic shader
with room to spare. This is a real one, already flattened and locked by somebody else's tooling,
and it had the registers.

Phase A is done: protocol, socket writer, clear and grab, resolver, real shader. What is left is
Phase B, which is the part no editor can answer.

### What A5 also turned up: a swap clip that moves a material between slots

WRONG FIRST READING, kept because it is the mistake worth not repeating. The plug's renderer
showed one patched material and one untouched one, and it looked like the multi-slot bug fixed
earlier the same day. I explained it as weights: the bake stores every vertex with
`WeightOnPlug` as an active flag, so geometry riding a bone outside the plug's chain is baked
inactive and patching its material would change nothing. All true, and not what was happening.

The conversion report says both materials were patched, by name, on all three of that avatar's
plugs. What the inspector showed was a material-swap ANIMATION putting the unbaked original back.

`RepointSwappedMaterials` matched a clip's key by the SLOT the bake had touched and rewrote it
only when the key held the material that had been in that slot. A swap clip is free to move a
material between slots, and a mesh with two baked materials has clips that do: one puts material
B where the bake found A. Those keys matched no slot's `from` and were left pointing at the
unbaked original, so the part came back rigid the moment the toggle played, on an avatar whose
report correctly said both were patched.

Now keyed by the original MATERIAL, still scoped to the renderer. Every patched copy on a mesh
was baked for that mesh, so moving one between that mesh's own slots is safe; handing it to
another mesh would not be, and that protection is unchanged.

### What A5 also turned up: an unbent part that is not a slot problem

The plug's renderer carries two materials and only one was patched. It looks exactly like the
multi-slot bug fixed earlier the same day, and it is not.

`MaterialSlotsOf` marks vertices weighted to bones beneath the plug root and counts each
submesh's triangles against them. The second submesh scored zero, so it is not plug geometry by
that definition, and patching it would change nothing: `TryPlaceVertices` writes EVERY vertex of
the mesh into the bake and stores `WeightOnPlug` alongside as the active flag, so a vertex
weighted outside the plug's bones is baked inactive and the shader leaves it where it is.
Patching its material would hand it a deform that is switched off for exactly those vertices.

So the blocker is the WEIGHTS, not the slot. Geometry that sits on the plug but rides a bone
outside the plug's chain cannot bend, whatever material it wears, and it is the author's rig that
decides that.

Worth doing, not done: the converter can SEE this. It has the plug's measured frame, length and
radius, so a submesh with no plug-weighted vertices whose vertices sit inside the plug's own
volume is detectable, and saying "this part will not bend, its vertices are weighted to a bone
outside the plug" turns a silent visual fault into a told one. Detection only. Re-weighting
somebody's mesh is not the converter's business.


## The cubic chain, wired (2026-09-04)

A4 shipped as a working single-socket atlas resolver: `YapsResolveChain` built the ordered list
and the arc-length ranges, and `YapsResolveSocket` handed the deform link 0 and threw the rest
away. Review caught it, and "A4 passes" had been reading as the whole contract to anybody who
was not in the room. The walk is now in.

**The chain rides on the socket.** `YapsChain` moved above `YapsSocket` in `yaps_resolve.cginc`
so it can be a field of it. Every other resolver source leaves `count` at 0, and 0 means the
single-socket fields are the only answer there is, so tiers 1 and 2 take exactly the path they
took before.

**One vertex is inside one link.** The walk stays a single cubic and costs what it always did.
`yaps_deform.cginc` picks the link whose arc range contains the vertex's distance along the
shaft, and walks from the previous socket to that one. The segment leaves the previous socket
along *that socket's* forward, which is the same direction the previous segment arrived on, so
the joint is smooth rather than kinked.

**The base's knobs stay on the base's segment.** Pullout handle, entrance stiffness and the
straight start apply to link 0 only. Past the first socket the shaft is being carried by sockets,
not by its own base, and a mid-chain segment gets the same plain handle at both ends.

**No index computed from data.** The link choice is an unrolled cascade over `YAPS_CHAIN_MAX`
with literal subscripts, not a search that produces an index. One runtime subscript spills the
whole chain out of registers, which is the rule that already shaped the resolver's sort. The
compare is per-vertex; the subscripts are not.

**The taper asks the link, not link 0.** `isHole` now comes from the link the vertex is in.
Only the last link can leave anything over, because every earlier one has the next link's range
waiting directly behind it.

**Every socket now flips to meet its approach, ring and hole alike.** The flip in the arc loop
used to be ring-only, on the grounds that a hole aimed away had been rejected before it got
there. That rejection was removed when it turned out to be a contradiction with the deform, and
the ring-only flip was left behind: a hole with the other convention would hairpin the path. It
matches the deform's own unconditional flip now.

**The ranges are measured on the curve, not the chord.** Written first as "a vertex lands a
little short of its socket, harmless slack", which was wrong on inspection: the last vertex of a
segment stops short of the socket while the first vertex of the next segment starts exactly on
it, so it is a step, a ring of the mesh torn open at every joint. The resolver still orders by
chord, which is the right thing to sort by, and the deform remeasures each segment where the
handles that decide its shape are known. `YapsCurveLength` is the mean of the chord and the
control net, within about a percent, because four walks per vertex to choose one walk is the
wrong trade. The plug covers less ground than the chord list suggested, which is what an
inextensible shaft bent through two sockets genuinely does.

**Squeeze and bulge measure from the vertex's own socket.** `entry` was `z - gap`, the distance
to link 0, so on a chain a mid-chain socket did not grip at all. It is now the running arc to
whichever socket owns the vertex.

Compiles clean through `Dev/Probes/Hlsl/yaps-fxc.sh`. Not yet seen in ChilloutVR: nobody has a
plug through two sockets to test it with, which is why the resolver sat half-used for a day.

## An audit, and five things it was right about (2026-09-04)

**Two materials called "Material" overwrote each other's bake.** Generated material paths were
built from `source.name` alone, and `YapsBaker.Generated` reuses an asset at a path, which is
exactly what it should do when the same material is baked twice. Two DIFFERENT materials with
the same name is not a corner case, it is what an exporter writes when nobody renamed anything:
the second bake loaded the first one's asset, overwrote it, and left the first renderer pointing
at a bake describing a different mesh. `YapsBaker.Tail` appends six bytes of the source's GUID
and local id, so two materials embedded in one FBX stay apart and reconverting still lands on
the same file. Converter and toolkit both.

**The toolkit built the atlas and then read none of it.** `Build` adds the socket writers and
the avatar's clear and grab quads, but `_YAPS_UseAtlas` was set on the convert path only, so a
hand-built avatar published to the screen and fell silently back to the marker lights. One line
in the native bake, from the same `YapsAtlas.Enabled` switch.

**The channel drove one slot.** Static settings already reached every baked material; the
CVRMaterialDriver tasks carrying the live socket vectors did not. A two-material plug followed
the socket across half its mesh, remotely and in game only. `plug.MaterialSlots` records the
slots beside the materials now, and the channel builds one task and one driver layer per slot.
`PlugMaterials` stopped sweeping the renderer for anything carrying a bake at the same time,
which had been writing one plug's channel extents into another plug's material.

**Socket baking always patched slot 0.** A socket modelled into a mesh with several materials
had its shader patched onto whichever submesh came first while the submesh holding the opening
kept the shader it came with. `SocketSlot` asks the blendshapes: a shape's deltas are non-zero
on exactly the vertices it moves, and those vertices are the socket's own triangles.

**The toggle scan missed the controller ChilloutVR actually uploads.** `ClipsOfAvatar` read the
Animator's slot and `avatarSettings`, but not `avatar.overrides`, and `DrivenByOwnClip` read the
Animator's slot alone and returned null when it was empty. An avatar whose deform is already
animated from the shipped controller read as not animated at all, and YAPS built a competing
toggle against it, which is the two-drivers-one-property fight that leaves a plug permanently
undeformed. Both go through `ClipsOfAvatar` now and it reads `avatar.overrides` first.

**And the socket's swap repair still named slot 0.** Caught on the second pass, after the slot
selection above went in: `YapsSwapFollow.Follow` was still called with a literal 0 while the bake
had gone into the selected slot. The repair is keyed by renderer and slot, so a swap animation on
a non-zero socket slot matched nothing, was left unrepaired, and put the unbaked material back the
first time it fired. Exactly the shape of the converter bug fixed two days earlier, in the other
code path.

Also: the test plug left a uniquely named mesh asset behind on every spawn. One asset now,
deleted and rewritten, since the test plug exists to try something and be deleted.

## Own body, and the switch turned on (2026-09-04)

**The atlas had no own-body exclusion.** The light path has had one since the beginning: a
wearer's own sockets are permanently in reach and permanently nearest, so a plug that does not
skip them never looks at anybody else's. `YapsResolveChain` never asked, and range was its only
filter, so on any avatar wearing both a plug and a socket the chain would have taken its own
socket as link 0 for ever.

The fix costs no protocol and no new knob. `YapsSameBodyAs` split into `YapsSameBodyAt`, which
takes a world position instead of a light slot, and the chain calls it on a candidate that has
already passed range. `_YAPS_SelfTag` is the same switch the lights read and both builders
already set it, so an avatar wearing no sockets of its own skips the test entirely.

**Nothing is lost by excluding them, and this is the rule worth stating:** own body is the
contact channel's job, everybody else is the atlas's. A wearer's own sockets already reach the
plug through the channel, exactly, locally, on every client, with no screen read at all. The
atlas exists for the sockets the channel cannot see.

The scan inside is over players, not over taps, and it runs only for a socket that already
passed range, which is a handful per frame rather than 27.

**Testing note.** The exclusion needs a socket that is INBOARD of the plug, nearer the wearer's
hip than the plug is. A standalone hole placed in the world is not, so the A4 and B5 setups still
resolve; two sockets modelled on the same avatar as the plug will not, and want `_YAPS_SelfTag`
set to -1 on the plug's material for the duration of the test.

**`YapsAtlas.Enabled` is now true by default.** It was off while nothing read the atlas, because
publishing to a screen nobody reads charges the whole room for a handful of draws and a full
screen copy per camera. Both builders read it now. What it costs in a crowded instance, how it
behaves in mirrors and VR, and whether a viewer with custom shaders blocked sees anything at all
are all still unmeasured: that is Phase B, and this switch is what makes Phase B runnable rather
than a claim that it is finished. C3's corpus run is still owed before any of this ships.

## Three plugs, one material, one bake (2026-09-04)

The toolkit window listed three plugs on one avatar and gave all three the same length, 0.82 m,
which is plainly not true of three different shaft meshes. The baked materials in the output
folder held three different lengths, so the bake had measured them correctly and something after
it had not.

All three `YapsPlug` components pointed at three DIFFERENT renderers, each at material slot 0,
and all three of those slots held the SAME material asset. Three plug meshes painted from one
body material is ordinary; a material holds ONE bake; so the last plug baked won and the other
two deformed against vertex positions belonging to a mesh they are not. Identical reported
lengths were the symptom, not the fault.

`YapsBaker.Apply` clones the source material precisely so this cannot happen, and its comment
has said so since it was written. The clone's PATH was keyed on the source material alone, so
all three plugs resolved to one clone and the clone did what a shared asset does. The morning's
fix for two different materials sharing a NAME was the same shape of bug one level up and did
not reach this: here the source material really is one asset, correctly hashed to one tail.

The key now includes the mesh: `Tail(source, renderer)` hashes the material's GUID and local id
together with the renderer's path under its avatar. Path rather than instance id, so a rebuild
lands on the same asset instead of leaving the old one behind. A bake is indexed by mesh-global
vertex id and no two meshes share one, which is the reason the pair is the right key and the
material alone never was.

**Anything converted before this needs converting again.** Two plugs in three were deforming
against the wrong mesh, and nothing in the report said so.

## The plug's base sits where the shaft starts, not where it was inherited (2026-09-04)

A plug's root is whatever the original avatar's author put their plug component on, and authors
routinely put it on the hub ABOVE the shaft. Everything else hanging off that hub is then
measured as part of the plug: the length spans it, and the bend begins behind it.

`YapsBaker.SuggestShaftRoot` reads the bone chains under the stated root and takes the one that
reaches furthest. Bone positions, not vertices: a skinned mesh's vertices are in bind space and
relating them to a bone costs the whole placement pass the bake does, while the bones are already
posed and the only question is which chain goes furthest.

Two guards, because a wrong root is worse than an inherited one. There has to be more than one
chain carrying vertices, or the stated root already is the shaft's and this would chop its first
bone off. And the winner has to reach at least twice as far as the runner-up: two chains of
similar length is a shape this cannot read, and it says nothing.

It runs inside `YapsBaker.Bake`, which is the one place both builders pass through, and it is
reported rather than silent because it moves where the bend begins and changes what the length
means. Sockets bake in their own frame and have no shaft to find, so `objectFrame` skips it.

Moving the plug onto the bone you want overrides it: a root whose children are a single chain
has nothing to choose between and the suggestion stands down.

What the descent inherits from the bone it lands on is the authored UP, and only that. Forward is
measured by power iteration on the vertex covariance and the authored one is a seed with its sign
fixed against the attachment point, so a child bone pointing somewhere odd changes nothing. Roll is
taken as given, and it reaches the authored bend direction and the wriggle phase. Both are rest
cosmetics: the bend toward a socket is built from the socket, not from the frame's up. Wants a real
avatar whose shaft bone is rolled against its hub before that is stated as fact.

The chosen root travels back out in `YapsBaker.Result.Root`, and both builders ask THAT which
material slots are the plug's and where the chain starts. They used to ask the root they came in
with, which is the hub: everything the descent had just excluded came back through the slot scan,
so a sibling chain's material got patched and deformed against a bake that never measured it.

Wants the corpus before shipping. It changes the measured length and origin of every plug whose
root sits on a hub, which is most of the ones that came from another format.


## Sixteen driver tasks for the whole avatar (2026-09-04)

`CVRMaterialDriver` declares `material01` through `material16` and no more. Anything past the
sixteenth task is written into a field that does not exist: no error, no log, the layer simply
drives nothing.

A plug spends three tasks per material slot it patches (flags, position, front), so the ceiling is
not a plug count. `MaxPlugs` (4) used to bound it back when a plug cost three altogether, and it
stopped bounding anything when the channel began driving every patched slot rather than the first.
Four two-material plugs want twenty-four.

`YapsChannel` now counts what is already in the driver before each plug, divides the room left by
that plug's per-slot cost, and trims the plug's slot list to fit. Trimmed rather than refused: the
slots that do fit still get their channel, and the ones dropped fall back to whatever the atlas
can see. It warns, naming the plug and the count, because a silently missing slot on a
multi-material plug reads as the bake being wrong.


## What a generated material is named after (2026-09-04)

The path is the source material's GUID and local id, plus the renderer's hierarchy path, hashed.
The material alone was not enough: three plugs sharing one source material got one generated
material and therefore one bake, and two of the three wore a length measured off the third.

Same-named siblings are legal in Unity and would collide again, so a step whose name is shared by
a sibling carries its occurrence number. Only the ambiguous step, so an ordinary path reads as
itself.

Renaming or reparenting a renderer changes the path and so generates a fresh material, leaving the
old one in the output folder. Left alone deliberately: it is an unreferenced asset in a folder the
converter owns, and sweeping it would mean deciding what else in there is still wanted, which the
native builder cannot answer because it works one plug at a time.


## The atlas erased the self portrait (2026-09-04)

ChilloutVR's self portrait is a camera rendering into a texture with a transparent background,
composited by a RawImage. Its ALPHA is the mask. The atlas clear writes alpha 0 across a 552 by 520
rect of whatever target it is drawn into, invisible on the back buffer because nothing reads screen
alpha, and fatal to a portrait: a portrait smaller than the rect is erased outright. With *Reflect
Other Players* on, the wearer's avatar renders into everybody else's portrait camera and erases
theirs too, which is how it was found.

The scale slider does not resize the target. `SelfPortrait.ApplySelfPortraitSettings` lerps the
RawImage's localScale between 0.3 and 2, and nothing in the client resizes the texture behind it,
so the portrait's resolution is a fixed authored number.

`YapsAtlasFits` is the gate, on the clear, both socket writer passes and the reader. It is a
precondition rather than a guess at which camera this is: the rect sits at fixed pixel coordinates
in the target's corner, so a target that cannot hold it could never have carried the protocol, and
painting one is pure damage. The reader is gated by the same call at the tier 3 branch, because a
target nobody painted decodes to whatever the scene drew there.

Gated in the vertex stage by moving the vertex outside the clip cube, not by a fragment discard.
The clear's cost is the rect it covers, so a discard would still shade every pixel of it.

What this does NOT fix is a portrait target LARGER than the rect, which keeps a transparent corner.
Nothing available to a shader distinguishes that camera from a view: the client sets no flag a
shader can see, and layer culling cannot be used because the avatar's layers are reassigned on load.
It needs somebody in game with a portrait bigger than 552 by 520 before there is anything to
measure.


## The plug bent in front of you and stood still in the mirror (2026-09-04, WRONG)

Kept as a record of a wrong diagnosis. The fix below was shipped, broke the view, and was reverted.
The reasoning was that a render to texture flips the projection and lands the writer's rows
mirrored against a reader that indexes memory. It is plausible and it is not what is happening: with
the multiply in, the clear missed the same cells the reader reads, so those cells held alpha 1, read
as occupied, and the plug locked onto sockets decoded out of screen noise. The mirror still needs a
measurement rather than another theory.

It also went out in the same deploy as the queue move, so neither could be attributed on its own.
One change per test.



The writer places its pixels in CLIP space and the reader indexes MEMORY rows against
`UNITY_UV_STARTS_AT_TOP`, a compile-time constant. On the back buffer those two agree. Rendering
into a texture, Unity flips the projection: clip +1 becomes the last memory row rather than the
first, the writer's rows land mirrored, and the reader looks where nothing was written.

Every render-to-texture is affected, so a mirror showed an undeformed plug while the same plug bent
in the view beside it. A 4K mirror ruled out the size gate and left only this.

`YapsAtlasToClip` now multiplies y by `_ProjectionParams.x`, which is that flip's runtime sign. It
is the one thing in the placement that may be read at runtime; the ROW constant must stay
compile-time, for the reason recorded above it.

The self portrait bends by the contact channel, not the atlas. It fails the size gate, and a plug
engaged with its wearer's own socket is the channel's job anyway.


## The atlas draws before the scene, not after it (2026-09-04)

The payload pass writes opaque RGBA, because the payload needs the whole pixel. Only the clear was
alpha-masked, and only the clear was invisible: every socket painted coloured specks on screen, and
they became obvious as soon as anything started resolving.

Alpha alone cannot carry it. Eight bits a pixel against thirty-two triples the rect, and the note
on YAPS_ATLAS_COLS already records that anything past 1088 px wide clips at 720p.

SPS2 solves it by ordering rather than by masking: its resolver sits at Queue Background-944, grabs
immediately, and lets the entire scene render on top. The data lives in the grabbed texture and the
pixels never survive to the screen. The atlas now does the same, at Background-946 for the clear,
-945 for the writers and -944 for the grab.

Three things fall out of the move. The specks are covered by whatever the camera draws. The self
portrait damage goes with them, because a clear painting alpha 0 before the scene writes what a
camera clearing to a transparent background wrote there anyway, and the avatar then draws over it.
And the plug reads the CURRENT frame: the grab used to sit at Overlay, after the plugs had already
drawn at Geometry, so every bend was one frame stale.

The size gate stays. It is still true that a target too small to hold the rect cannot carry the
protocol, and it now also keeps the writers off targets where there may be nothing to cover them.


## A debug view that describes the CAMERA (2026-09-04)

Every view on the plug describes the plug. None of them could say why the same plug answers one way
in the view, another in a mirror and another in the self portrait, which is how two wrong
diagnoses got shipped in one deploy.

*Atlas target* reports three camera facts in one length. A tenth: the target cannot hold the rect.
Four tenths: it can, but the grab is a different SIZE from the target being drawn, so the grab did
not happen for this camera and the plug is reading somebody else's screen. Seven tenths: right
screen, no cell reported anything, so writer and reader address different pixels of it. Full: the
transport is on this camera.

What the client says about mirrors, for when that reading comes back. `CVRMirror` renders into
`RenderTexture.GetTemporary(min(setting, cam.pixelWidth), min(setting, cam.pixelHeight), 24,
RenderTextureFormat.ARGBHalf)` with `CalculateObliqueMatrix` on the near plane. Three things follow.
The size setting tops out at 4096 but is clamped to the camera, so a 1080p view gives a 1920 by 1080
mirror, far larger than the rect: the size gate is not what stops it. The oblique matrix rewrites
the third row only, so clip x and y placement is untouched. The target is HALF FLOAT, not eight bit,
which is the one difference that reaches the payload.

## The mirror was never the thing that was broken (2026-09-05)

For two days the report was "bends in front of you, stands still in the mirror". Three theories
were built on it, one of them was shipped, and it broke the working view badly enough to need a
revert. None of them were about the mirror, because the mirror was fine.

The plug being watched had a debug view set. `yaps_deform.cginc` handles that first:

    if (_YAPS_Debug >= 0.5)
    {
        ...
        return;
    }

The return sits before the enabled test and before every bend line, on purpose: the views report
by LENGTH, and a bending plug would make the length unreadable. So a plug with any view set is
straight in every camera, always. Switching to a plug with the view Off showed it bending in the
mirror on the first look.

What actually fixed the mirror, if anything did, was the queue move to Background-946/-945/-944.
Unity grabs once per frame per texture NAME, and at Overlay the reflection camera was drawing
after the only grab that had happened. Ahead of the scene, each camera reaches the grab pass
inside its own render.

The lesson is not about mirrors. An instrument that changes the thing it measures has to be
suspect number one when the measurement disagrees with a known-good case, and the debug view says
so in its own help text. It was read as a colouring, not as a replacement.

## Two avatars, and both eyes (2026-09-05)

B1 and the stereo half of B2 pass. Two converted avatars in one instance read each other's
sockets out of the atlas, and a plug bends in VR through both eyes.

The stereo result was the one worth watching. ChilloutVR renders single-pass instanced, so both
eyes share one wide target and each eye is a viewport inside it. The rect is placed from
_ScreenParams, which reports the per-eye size rather than the whole texture, and the grab is
sampled the same way, so writer and reader agree without either of them knowing there are two
eyes. Nothing in the transport had to be told about stereo, which is the reason it works and also
the reason it could not have been proven from here.

## A guard applied by its position in the file (2026-09-05, C1)

The resolver refuses a socket sitting a real way behind the plug's base, because a plug that
bends toward something behind its own root folds back on itself. The guard was correct and had
been correct for weeks. It ran in the wrong place.

Order was: light refinement, behind-the-base guard, "nothing found means nothing engaged", then
the atlas. The atlas sets `socket.engaged = chain.engaged` outright, so every tier-3 answer
arrived after the guard had already run and skipped it entirely. A socket decoded from the screen
behind the plug engaged in full.

The fix is a move, not a line of logic: the guard now sits last in the function, so it judges
whichever answer survived rather than whichever answer happened to be current when control
reached it. Anything that decides engagement now belongs above it, and the comment says so.

Worth naming the shape, because it is not a bug in either piece: two correct blocks, ordered by
when they were written rather than by what depends on what. A new tier added at the end of a
function inherits none of the checks written above it, silently.

## The socket lights are stock DPS and stay that way (2026-09-05, C2)

Checked rather than assumed. One writer, `YapsSocketBuilder`: 0.4130 for a hole root, 0.4230 for
a ring root, 0.4530 for a front, which are the values every DPS plug already on the platform
looks for. It only ever adds a light that was missing, so an authored socket keeps its own; the
single rewrite path is an explicit kind change in the toolkit, which is the author asking for it.

The check turned up a stale design instead. The resolver's header described YAPS authoring its
own ordering, root 0.4706 and front 0.4006, on the reasoning that stock puts fronts above roots
and Unity ranks vertex lights by range, so fronts evict the roots they belong to. That reasoning
is sound and the scheme was never adopted, because 7 and 0 mean nothing to a legacy plug and the
sockets would go dark for everything that is not YAPS. The comment now records it as a rejected
option with the condition for revisiting it, which is emitting both sets rather than swapping.

## What a blocked shader draws (2026-09-05, B4)

B4 fails, and the failure is loud. A viewer with custom shaders turned off sees metre-wide grey
slabs hanging off the avatar, several of them, large enough to fill the view.

ChilloutVR replaces the SHADER, not the mesh. The atlas quads had their corner offsets in
POSITION, and the whole design rests on the vertex stage ignoring the transform and writing clip
space directly, so under a replacement shader that reads POSITION the ordinary way they become
exactly what they are: unit quads at the avatar root.

Detection is not possible and is the wrong question. Nothing of this converter's runs on that
viewer's machine, so there is nothing to detect with, and the toggle is theirs rather than the
wearer's. The fix is to make the mesh undrawable by anything that does not understand it: every
position is now zero and the corner lives in UV0, so every triangle is degenerate. A degenerate
triangle rasterises no pixels under any shader. The payload was never in the vertices anyway, it
is in the object-to-world matrix, which is why this costs nothing.

The saved mesh assets are rebuilt when their UV0 channel is missing, because the vertex count did
not change and counting alone would have kept handing back the drawable ones.

## The self portrait cannot carry the atlas, and that is the deal (2026-09-05)

The plug bends in the view and in a mirror, and stands still in the self portrait. That is the
size gate doing its job rather than a bug, and it is worth writing down because it looks like the
mirror question all over again and is not.

The rect is 552 by 520 pixels, and those are PIXELS rather than a fraction of the target, because
each cell is one pixel and there are 4096 of them across four levels. The portrait renders into a
small serialized render texture, so it fails YapsAtlasFits and the plug falls back to whatever
the contact channel resolved.

It cannot be scaled to fit. A fraction-of-target rect on a 512-wide portrait would ask 544
columns to share fewer pixels than there are columns, and the cells would be destroyed by the
first sample. Shrinking the level count only trims the HEIGHT, and the portrait fails on width.
The alternative is a smaller grid on small targets, which both sides could agree on from
_ScreenParams alone, at the cost of a coarser spatial hash on the one camera that is a preview
window a few centimetres across. Not worth it. The portrait keeps the channel's answer.

## Legacy sockets answer a YAPS plug (2026-09-05, B3)

B3 passes. A YAPS plug found and bent toward the sockets on another player's unconverted DPS
avatar, in an instance, with nothing installed on their side.

That is the marker-light tier doing the only job it exists for. Most of the content already on
ChilloutVR announces itself with lights and nothing else, no contacts and certainly no atlas, and
a plug that insisted on either would have been blind to all of it. The digit table is confirmed
against real content rather than against a socket this toolkit wrote: 1 and 3 read as a hole
root, 2 and 4 as a ring root, 5 and 6 as a front.

Read alongside C2, this is the two halves of the same promise. YAPS sockets emit stock DPS ranges
so legacy plugs see them, and YAPS plugs decode stock DPS ranges so legacy sockets answer them.
Neither side has to convert for the other to work, and both directions are now proven in game.

## Somebody else's client agrees (2026-09-05, B6)

B6 passes. A second person, on their own machine, saw the plug do what the wearer saw it do, with
the whole mesh moving together.

This is the only test that could have found a per-slot driver failure. A plug split across
several material slots needs one set of driver tasks per slot, and the wearer's own view is
correct whether or not the extra slots were written, because the wearer's client is the one that
computed the answer. A slot left behind shows only remotely, as half the mesh following the
socket and half of it standing still. Nothing local can see that, and neither can the wearer.

Sixteen tasks is the whole avatar's budget, so a plug with many slots can still be trimmed, and
the conversion report warns when it happens. That warning is now the thing to read before
assuming a remote report is a mod problem.

## Two plugs, one socket (2026-09-05)

A socket now opens to the DEEPEST plug in reach rather than the nearest, which is both a fix and
a feature.

The plug end never needed anything. Nothing claims a socket: each plug resolves independently,
the atlas is read-only for readers, and the light tier only decodes what was emitted, so two
plugs bending into the same socket was already free.

The socket end had a single winner. It ran a search over the four light slots, kept the plug
whose base was NEAREST, and measured only that one, which left a second plug passing through
unopened mesh. Worse, nearest is not the same question as deepest: depth is
(length - distance) / length, so a longer plug standing further off is deeper than a short one
close in, and with a single plug present the search could already answer about the wrong one.

Ranking by the computed depth fixes both in the same expression. Nothing wanted the winner's
identity, only the number, so the search collapsed into a max over the slots and `YapsFindPlug`
is gone. Fewer lines than before.

## What the driver cap actually costs now (2026-09-06)

The corpus run turned up a plug driving one of its two materials, which is the sixteen-task
material driver budget being spent before the plug's second slot got its share. The warning that
reports it was written before the atlas existed and described a worse outcome than the one that
now happens.

A dropped slot loses the contact channel and nothing else. `_YAPS_UseAtlas` is set on every
material of a plug rather than on the driven ones, deliberately, so a plug spanning two materials
cannot read the atlas across half its mesh and the lights across the other. The marker lights are
read from Unity's own per-camera light slots, which cost no tasks either. So the slot keeps two of
the three tiers, and the one it keeps at the top is the exact, viewer-agreeing one.

The warning now says that when the atlas is on, and keeps the old wording when it is off. Worth
saying because a remote report of a garment behaving oddly was floating around unattributed, and
this warning is the first place to look before blaming somebody's mods.

## B4 confirmed from the other side (2026-09-06)

A remote viewer with custom shaders off looked again and the slabs are gone. The avatar renders
under ChilloutVR's replacement shader and nothing else shows, which is the whole of what was
wanted: the transport is invisible to somebody who has opted out of the shader that implements it.

Nothing local could have produced this evidence. The editor renders with the real shader, so the
failure only ever existed on a machine that had refused it.
