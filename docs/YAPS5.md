# YAPS 5: the transport question

How a plug finds a socket is the whole system; everything else is presentation. This gathers
every route that exists, every route proposed, and every measured limit, from a month of in-game
failures and two nights reading the client. The feature wishlist (paths, portals, multi-socket)
stays in `Unfinished.md` — those ride on whatever transport wins here.

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

The tracker belongs to whatever ENTERS a socket — a prop or the other person — so it can never
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
force but Burst — volume count is not the cost. Previous pairs re-add FIRST, so an interaction
is sticky once started and may silently never start in a saturated instance. Rejections on tags
and owner flags happen before a pair is written: **tags are the lever on the 512.**

**Contacts are wearer-only.** The wearer computes; anything remote viewers must see has to be a
synced parameter. Six floats is ~6% of the 3200-bit cap.

**Penetration space depends on the receiver's shape.** Box: local, oriented, normalised — the
right one, and the default when no Collider sits on the trigger's object. Sphere: WORLD axes, no
rotation — unusable on anything that turns. A stray Collider silently changes the shape.

**Prop writes are the toucher's job — tested, works.** `HasProbableAuthorityToApplySync` has
branches for prop senders, the world, and your OWN avatar, and none for another player's avatar.
That is not a wall, it is the wearer-only rule extended to props: the toucher's client passes the
own-avatar branch, applies the write, and the value networks to everyone from there — each client
handles only its own avatar's touches, so nothing double-writes. Proven in game 2026-08-22 with
the same shared avatar on both wearers: the prop bent for the partner's sockets on both screens,
partner on stable, spawner on beta. Consequence: a prop that ignores someone's sockets has an
avatar-side problem — pointers missing, dark, or mistyped — never a client refusal.

---

## The candidates

In the order they are worth doing, cheapest and least disruptive first.

### 1. The lighthouse — SHIPPED in 4.3.0, without the constraint

As first sketched: one marker pair per avatar instead of one per socket. Root and front lights ride a
`ParentConstraint` with every socket as a source; animating the source weights snaps the pair
onto whichever socket is active, tracking its bone. Legacy plugs see a completely standard DPS
pair and cannot tell the difference — that is the point.

- Fixes eviction outright: two lights on the avatar, nothing to evict.
- The socket menu becomes real: "active socket" moves the lighthouse, instead of Unity choosing
  by range.
- One socket at a time for legacy readers — which is not a compromise, it is DPS's own ceiling,
  and the four-slot table above shows even two were never actually possible.
- YAPS-to-YAPS can drive it automatically: plug's sender trips the socket's receiver, the synced
  parameter moves the lighthouse. Legacy props fall back to the menu.

Built without `ParentConstraint`, which dissolved both open questions: a DISABLED light never
enters Unity's ranking, so every socket keeps its own pair on its own bone and a selector layer
enables exactly one. A "Marker lights" dropdown (Int, synced, 32 bits) moves it, starts on Off,
and choosing a socket switches that socket on as well as lit — the dropdown is the one control an
old toy needs. One socket means no chooser. `YapsLighthouse.cs`, called by both the conversion and the tool.

### 2. Light colour as data — demoted 2026-08-22
*Demoted the day the gate test passed: its consumer was YAPS light-readers, and contacts are now
proven, so lights are legacy-only — and legacy plugs cannot read colour. Kept for the record.*

A vertex light hands the shader `unity_LightColor` beside position and range. The markers are
black on purpose (zero INTENSITY kills a light, black does not), so three channels per light sit
unused. Encode the socket's axis in the root light's colour and a YAPS-native socket needs ONE
light, not two — three sockets in the light budget, or one socket plus the lighthouse pair.

Strictly a YAPS-native lane: legacy plugs still need the pair, so this never replaces the
lighthouse, it rides beside it. Open: whether upload filtering clamps colour or intensity, and
how much precision survives the intensity multiply.

### 3. Contacts, kept and hardened — built

The native tier already works avatar-to-avatar and is the most information-dense channel
available without scripting. What this plan changes is posture, not machinery:

- Cross-avatar prop writes are proven working, so a silent prop means a silent SENDER: audit the
  avatar's pointers before touching the prop.
- Audit every receiver for the box/sphere trap: no Collider on trigger objects, ever.
- Keep tags tight so rejected pairs stay off the 512.

### 4. The GPU bridge: blit or camera, then a texture parser — PROPOSED, 2026-08-23

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
  writes it into a component. `CVRTexturePropertyParserTask` resolves the target by REFLECTION —
  `GetField` then `GetProperty`, public instance only — so it reaches any public field or property.
- `CVRShaderGlobals.SetGlobalTexture` publishes a RenderTexture globally, so a shader elsewhere on
  the avatar can sample the result without a parser at all.

**What it can drive:** `AudioSource.volume`/`.pitch`/`.enabled`, `Light.intensity`/`.range`,
`Transform.localPosition`/`.localScale`, and anything else exposed as a field or property.

**What it cannot:** animator parameters (behind `SetFloat`) and blendshape weights (behind
`SetBlendShapeWeight`). Both are methods; reflection here only does fields and properties. This is
a hard limit, not a gap in our reading.

**So: sound is solved and costs nothing.** A socket's audio can be computed on every listener's own
GPU and played locally — no synced parameter, no contact pair, no marker-light tolerance, nothing
to install. That beats the addon route on every axis: PCS and Wholesome each ship their own contact
receivers, which spend from the instance-wide 512-pair budget AND are forced local here, so their
sounds are wearer-only on a converted avatar today. Ship the machinery, not the audio: we cannot
redistribute Noachi's or Dismay's clips.

Untested: whether it survives an upload, and what a per-frame blit or camera actually costs. Steps
are local Play mode, then upload, then a second client.

### 5. The screen-space atlas — postponed, deliberately

Sockets render encoded quads into a reserved screen region; plugs sample it back through a named
GrabPass. The only channel that dodges the light slots, the pair cap, the sync bits AND the
authority gate, with no socket-count ceiling — and where SPS's atlas is double-wide-native and
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
conversion, and we simply never have it. It also means the patch lands at the same UV in every
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
cell gives 0.5 x 0.000488, about 0.24 mm** — five times finer than the contact channel — with
range coming from HOW MANY cells exist rather than from how big one is. That is the argument for
the spatial hash, measured rather than asserted.

Also confirmed by construction: the payload has to come from the object's own matrix, since no
script runs on someone else's avatar to set a material value. So **a socket marker for this can
never be a skinned mesh** — Unity skins into world space and hands a SkinnedMeshRenderer identity,
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

**Still unanswered before this could ship:** the per-frame cost. A named grab runs PER CAMERA —
main view, each eye, the portrait, the CVR camera, every mirror — so the atlas has to be ONE grab
shared by everybody, never one per socket, or it scales catastrophically. That constraint is also
why cell allocation matters: a shared atlas is the only affordable shape. And a viewer whose
safety settings block custom shaders gets nothing from it, where marker lights survive that,
because lights are components rather than shader work. So it is a third leg, not a replacement.

### 6. Scripting — blocked

The endgame: read any avatar's sockets whatever protocol they speak, no slots, no pairs, no
bits. Access was requested and declined 2026-08-19 (world scripting is their focus). Everything
above is designed so that none of it is wasted if this arrives: the resolver's tiering is
already script-first, contacts next, lights last.

---

## Order of work

1. ~~Reserve the tracker's light slot~~ — **done**, `b82e9d0`.
2. ~~The two-person authority-gate test~~ — done 2026-08-22, gate disproven: cross-avatar prop writes work, the toucher's client syncs them.
3. ~~The socket rebuild~~ — **shipped in 4.3.0** (2026-08-23): corpus 385/386/387 clean, a tester's rebuilt mouth socket working with a DPS prop in game.
4. ~~Lighthouse~~ — **built 2026-08-22** without the constraint: per-socket pairs, one enabled, dropdown selector.
5. Light colour: shelved — its consumer was YAPS light-readers, and the gate test proved contacts trustworthy, so lights are legacy-only and legacy plugs cannot read colour. Revisit only if fallback pressure appears.
6. ~~Corpus, with the two missing avatar classes added~~ — done: Fixture_DeformSocket and
   Fixture_HeadTransplant are in the corpus and its baseline, and gated 4.3.0.
7. ~~Spike the screen-space atlas~~ — **done 2026-08-27, and it works.** GrabPass survives a
   ChilloutVR avatar upload, cross-avatar, in mirrors. See candidate 5 for the measurements and
   for what is still unanswered: the per-camera cost, and cell allocation.
8. The contact channel reaching a tool-built plug — **fixed in 4.4.0**. It had never worked in game
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
    stereo, single-pass instanced                              solved by construction
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
   `Unfinished.md` — a ring mid-shaft and a hole at the tip, portals, duplication — is exactly
   "stop discarding what the atlas already returns", so the transport and the features want the
   same change.