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
