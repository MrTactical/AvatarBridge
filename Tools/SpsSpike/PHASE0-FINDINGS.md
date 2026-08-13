# Spike results

## S1 — the globals resolve, and they are positioned correctly (2026-08-13)

First look at the globals probe: a **chest marker appeared at chest height on an avatar**.
That answers the two questions that gated the redesign — `_CVR_PlayerChestPositions` (and by
extension the rest of the family) **resolves for uploaded content in game**, and the value is
in the right place, not garbage or an offset frame.

The markers were hard to see, because the first build drew solid depth-tested cubes that end
up buried inside bodies. Rebuilt as an **overlay gizmo**: `ZTest Always` in the Overlay queue
so nothing occludes them, wireframe edges with dim faces so they never hide the body part they
are measuring, a minimum apparent size so a player across the room stays visible, and the
local player's own set drawn at 35% so it does not fill a first-person view from the inside.

**Rebuilt probe: full pass, 2026-08-13.** Markers land on hip, chest and head of every player,
correctly placed, tracking each person as they move, with the local player's set at index 0
(small) and remotes at full size. **They agree in mirrors.** That is the property lights failed
and the entire reason position moved to this channel — so the backbone of the redesign is
confirmed on the stable client.

## S2d — the inverted light encoding works, and lights are stronger than feared

Two results from the same session:

**The encoding fix lands.** With roots re-encoded to `0.4906`/`0.4806` and fronts down at
`0.4106`, the probe's slots fill with **magenta and cyan (roots)** instead of the blue front
wall the original protocol produced. Unity's range-based ranking now favours what we need.
Adopt this ordering for converted avatars.

**Lights behave far better avatar-to-avatar than avatar-to-prop** — the tester's own words,
"works better between avatars, not the cube prop" — and they **hold up in mirrors** in that
configuration, where the earlier prop test diverged.

That fits the frustum explanation exactly and sharpens it: the divergence is a *distance*
effect, not a mirror effect. Avatars stay near each other and their lights sit inside any
frustum that is drawing the mesh; a dropped prop sits far from the light bearer, so cameras
disagree about whether the light is visible. **The failure mode lives at range; the deform
lives at contact.** Lights are therefore a solid refinement in the exact configuration SPS
runs in — avatar to avatar, close range — and the earlier caution stands only for distance.

Design consequences:
- **Adopt the inverted encoding** on converted sockets. Keep decoding the legacy
  `0.41/0.42/0.45` too, so our plugs still react to ChilloutVR's existing DPS content.
  Emitting *both* sets would double the light count and re-create the contention we just
  fixed, so legacy emission should be an explicit opt-in setting, off by default.
- **Never test light behaviour on props and generalise to avatars.** They measurably differ,
  and the avatar case is the real one.
- The camera-independent channels remain the backbone regardless — they carry the deform at
  every range, and lights refine the last few centimetres.

## S2c — the decisive one: **the light channel is camera-dependent**

Session 2026-08-13, probe worn as an avatar. Results in order:

- **Q1 avatar → avatar: PASSED.** The tester wore the probe avatar (no lights of its own) and
  it lit with protocol colours whenever the other player's light-bearing avatar was around,
  dark otherwise. The layer question is closed — the precision channel works in the exact
  topology the deform needs.
- **But**: the same cube reads **differently depending on which camera is drawing it**. The
  personal mirror showed active colours while the direct third-person view of the same object
  showed none; at close range the readout went amber (world lights claiming slots).

### Why, and it is two mechanisms stacked

1. **Unity culls lights per camera.** `unity_4LightPos*` is filled from *that camera's*
   visible-light set, so the main camera, the third-person camera and each mirror camera each
   compute their own. A 0.41 m light sphere drops out of one frustum while staying in another.
2. **`CVRMirror` sets `QualitySettings.pixelLightCount = 0`** for the duration of its render
   and restores it afterwards (`CVRMirror.cs:195, 211, 238`). So the vertex-light pool is
   composed differently inside a mirror than outside it, in the client's own code.

### What this disqualifies, and what survives

A deform runs in the vertex shader of every camera that draws the mesh. If its input differs
per camera, **the mesh is bent differently in the mirror than in the direct view** — which is
exactly the artifact seen here, and precisely why VRCFury moved SPS 2.x off lights and onto
the screen-space atlas.

So: **lights cannot be the primary position source, and above all cannot drive the blend.**
If the choice of source depends on "are lights present", that choice is itself camera-dependent
and the deform diverges per view.

The other two channels are immune, and it is worth being precise about why:
- `Shader.SetGlobalVectorArray` is set once per frame, globally — identical for every camera.
- `CVRMaterialDriver` writes a material property — identical for every camera, every viewer.

### Revised architecture (supersedes the earlier ladder)

1. **Primary position: the discrete channel.** Socket pointers → `SetFromPosition` triggers →
   animator parameters → `CVRMaterialDriver` → material vectors. Exact, rotation-aware,
   camera-independent, viewer-independent. Its 10 Hz remote ceiling is answered with an
   animator smoothing layer, turning steps into a frame-rate exponential follow.
2. **Universal floor: P0 shader globals.** Camera-independent, always present, approximate.
3. **Lights: demoted to a refinement, bounded to contact range**, and only *within* an
   engagement state that the discrete channel decides. At contact range the light and the mesh
   are effectively co-located, so any camera drawing the mesh has the light in frustum — the
   regime where the channel is stable. They also remain valuable for **legacy DPS interop**,
   which is a genuine feature, just not the backbone.

The blend factor must come from a camera-independent signal. That is the single sharpest
constraint to come out of the spike.

### Q3 stress test — the slots fill with the wrong lights

12 sockets / 24 lights on one avatar, which is what a real partner carries. The readout went
unstable ("freak out") and, more damning, the slots filled with **mostly blue** — front
lights — with only one red or green root among them.

The cause is Unity's vertex-light scoring: with all our lights black and intensity 1, the
score comes down to range and distance, and the front light is authored at **0.4506** while
the roots sit at **0.4106 / 0.4206**. The longer-ranged front therefore outranks its own root
and crowds it out. What comes back is several fronts and a root or two — not the matched
root+front pair a socket frame needs. A front without its root is useless: it gives a
direction with no origin.

Caveat worth stating: the stress rig clustered all 12 sockets inside a 0.25 m circle, so
distance could not discriminate between them and range dominated. A real body spreads sockets
further apart. But sockets *do* cluster around the hips in practice, so this is close to a
realistic worst case rather than a contrived one.

Together with the camera-dependence above, this settles it: **the light channel cannot carry
position on an avatar with a realistic socket count.** It stays as legacy-DPS interop and as
a contact-range refinement, nothing more.

### Still inconclusive

**Q4 cross-pass** — the reserved corner stayed black in these shots, meaning the ForwardAdd
pass never ran (no pixel lights on the cube in that world). Needs a brightly lit world to
answer. Lower stakes now that lights are demoted.

## S2a — vertex light slots: **PASSED in game, 2026-08-12**

Probe prop uploaded to a lit apartment world. All four rows decoded to their protocol
colours — hole (red), ring (green), front (blue), tip (magenta) — which means:

- ChilloutVR **does** populate `unity_4LightPos*` / `unity_4LightAtten0` for uploaded
  content shaders. The precision channel has a transport.
- The authored range survives the upload intact: `5·rsqrt(atten)` recovered values whose
  second decimal still classified correctly, so the encoding is not being quantised or
  rescaled anywhere in the pipeline.
- **No white swatches** in a room full of visible lamps: the four protocol lights held all
  four slots against the world's own lighting, at ~0.35 m range. Slot order differed from
  authoring order, which is expected — Unity assigns slots by its own priority.
- Border never went red → the **vertex and fragment stages agree** on slot 0. The
  within-pass half of R2 is clear; cross-pass (ForwardAdd/shadow) is still untested.
- Confirmed working mounted on an avatar as well as on a prop.

## S2b — cross-content: **CONFIRMED 2026-08-13**

Setup: the protocol lights rode **one player's avatar** (BalloonDog). The other player wore a
plain avatar with no lights. Two receiver-only cubes — one spawned by each player — carrying
no lights of their own.

| Condition | Readout |
|---|---|
| No light-bearing avatar in the instance | **completely dark** — every row empty |
| Light-bearing avatar present and in frame | protocol colours, **distance bars reading metres** |
| That player switches to a plain avatar | **goes dark again** |

Airtight in both directions, and it establishes two distinct crossings:

- **Avatar → prop**: a cube with no lights displays lights carried by an avatar, metres away.
  Separate uploads, separate bundles.
- **Remote avatar → another player's prop**: the *second* player's cube, on the *second*
  player's client, lit up from the first player's **remote** avatar. That is the topology the
  deform needs — content B's shader reading content A's socket on a third machine — and it is
  the result that matters most.

Confirmed from both participants' viewpoints and in two different worlds.

**Not yet directly tested: avatar → avatar.** Props and avatars sit on different layers
(`PlayerLocal` / `PlayerNetwork` vs the prop layer), and the spike rig deliberately sets
`cullingMask = Everything` so a layer mismatch could not masquerade as a failure. Real socket
lights come from the VRCFury bake, whose culling mask has not been checked — and the CCK ships
`CheckIfMisconfiguredPlayerLight`, which flags lights hitting one player layer but not the
other and auto-fixes by OR-ing both in. That validator existing is strong evidence the layer
question is real.

**Checked the same day:** `VRCFuryHapticSocketBaker` never assigns `cullingMask` at all, so
the baked socket lights keep Unity's default of `-1` (Everything) — both player layers
included. Avatar→avatar should therefore work untouched. The converter should still assert
it and say so in the report: it is one line of insurance against an author, or a future
VRCFury, narrowing the mask.

**Socket light geometry, read from the same method** (drives the decoder):
- Root light at the socket origin; range `0.4106` (hole) or `0.4206` (ring/ring-one-way).
- Front light at **`forward * 0.01 / worldScale.x`** — a **1 cm** baseline — range `0.4506`.
- Both `LightType.Point`, `Color.black`, `LightShadows.None`, `ForceVertex`.
- Whole rig offset by `up * 0.03` when the socket uses a radius offset.

That 1 cm baseline is what the socket's forward axis must be reconstructed from
(`normalize(front − root)`), so it is inherently low-precision and sensitive to any jitter in
either light's transform. SPS ships with it, so it works, but the decoder should normalise
defensively and fall back to the plug-to-socket direction if the two positions land closer
than a sane epsilon.

### The limitation this exposed, and why it is survivable

Reported alongside the pass: the readout flickers, and *"the cube only turns on when I look
at you"* — at the avatar carrying the lights. Looking straight at the cube instead sends
rows grey.

Cause: **Unity culls lights per camera.** Vertex-light slots are filled from the camera's
visible-light set, and a protocol light is a sphere of radius 0.41 m — trivially small, so
it leaves the frustum the moment the avatar wearing it does, and the slot empties. CVR's own
remote-avatar hiding (`PuppetMaster.ProcessAvatarVisibility` → `AvatarObject.SetActive`)
is **distance**-gated, not view-gated, so it explains nothing here; it will, however, kill
the lights outright past `disablePlayerAtDistance`, which is worth knowing.

Why this is survivable, and why the tiered design was the right call:
- At real working distance the plug and socket are within ~0.4 m of each other, so a light
  sphere that size is in frame whenever the plug is. Culling only bites during *approach*,
  at ranges where the deform is barely engaged.
- The other two channels are **immune**: shader globals are uniforms and the discrete
  contact channel writes material properties — neither is frustum-culled.

**Improved fallback ladder** (supersedes the plan's two-tier continuous channel):
1. **P1 lights** — frame-accurate exact frame, when in frustum.
2. **Discrete channel last-known** — exact-ish, 10 Hz, never culled. Holds the last good
   socket position when the lights blink out, instead of snapping.
3. **P0 globals** — approximate, always present, ultimate floor.

The deform must therefore ease between sources rather than switch hard, or a light entering
and leaving frustum will pop the mesh.

### Probe change

Ordinary (non-protocol) lights now read **amber** instead of light grey, so grey means
"slot empty" and nothing else — "two of them went grey" was ambiguous between an empty slot
and a world light taking it, and that distinction decides whether culling frees the slot or
something else claims it.

## S2b — earlier inference (superseded by the confirmation above)

Two probe rigs in one instance, one spawned locally and one spawned by another player.
Each rig carries exactly one light per class, so a single rig can only ever show one red,
one green, one blue and one magenta. With both present the readout shows **duplicates of a
class** — two reds in one shot, two magentas in another — which is only possible if lights
from the other player's prop are entering this shader's slots.

So a shader in one piece of content **does** receive vertex lights from another piece of
content spawned by a different player. That is the propagation SPS needs.

It is still inference from duplicate classes rather than a direct reading, so the probe was
extended to settle it outright:

- **Receiver-only cube** (`Spike ▸ Build probe cube only`): a cube with no lights of its
  own. Every coloured row it shows must have come from elsewhere. Pair it with the
  lights-only rig parked on an avatar and the answer needs no interpretation.
- **Distance bar** added under each row's ruler, 0–5 m ticked per metre. A light riding the
  same object reads ~0.35 m; a light on somebody across the room reads metres. Without it
  "my own lights" and "their lights" look identical on the readout — the gap that made the
  result above inferential.

### Design consequences already visible

- **The slot budget is 4, and sockets come in pairs.** Two rigs (8 protocol lights) already
  contend for 4 slots, and the allocation mixes classes rather than taking the nearest rig
  wholesale. A partner with 12 sockets carries 24 lights. The decoder therefore must pair a
  root with its front light by proximity (SPS uses `distanceSq < 0.01`) and **skip an
  unpaired root gracefully**, because Unity may hand us one without the other.
- Slot order is not authoring order and reshuffles as things move — never index by slot,
  always classify by decoded range.

## Phase 0a bake probe — R12 answered, 2026-08-12

`SpsBakeProbe.RunBatch` on `Angela_PC_SPS` (1 plug, 12 sockets), baking twice and diffing.
Full output: `D:\UnityVRCCrap\Attempt Conversion\SpsBakeProbe.md`.

| | control (`enableSps = true`) | test (`enableSps = false`) |
|---|---|---|
| BakedSpsPlug / BakedSpsSocket | 1 / 12 | 1 / 12 |
| SpsResolver renderers | 1 | **0** |
| SpsScreenMarker renderers | 12 | 12 |
| Lights (ranges) | 24 — 0.4106, 0.4206, 0.4506 | 24 — same |
| Contact senders / receivers | 29 / 90 | 29 / 90 |
| Materials on an SPS-patched shader | 1 (`Angela_Body`) | **0** |
| `_SPS_Bake` texture | 1 — `SPS Data` 8192×2 | **0** |

**R12 verdict: the clean-room premise holds.** Turning the plug's own SPS flag off leaves the
objects, all 24 protocol lights and all 119 contacts intact while the body shader comes
through **completely unpatched** — precisely the input our own patcher wants. The one thing
that does not survive is the mesh bake texture, which is why the baker was already pulled
into v1 (Phase 1c). The resolver renderer conveniently deletes itself; the 12 screen markers
still need removing.

**Bonus finding that removes work: the sockets already emit the DPS light protocol.** With
`useLights: True` (the authored default here), every socket bakes a root+front light pair at
**0.4106 / 0.4206 / 0.4506** regardless of the plug's SPS flag. So the converter does not
author lights at all — it just has to stop deleting them. Two consequences:
- The `.xx06` fourth decimal is VRCFury's SPS2 self-marker, which their own decoder uses to
  *ignore* these lights. Ours drops that rule (already planned), so it reads both these and
  the clean `0.41` ranges legacy CVR DPS content uses. The probe's classifier keys only on
  the second decimal, so it already handles both — verified against 0.41 in game and against
  0.4106 here.
- Socket `addLight` modes seen on one avatar: Hole ×3, Auto ×8, Ring ×1 — the type matters
  and comes through, so hole/ring semantics survive for free.

---

# Phase 0a — static inventory (2026-08-12)

Read off the already-converted `Angela_PC_SPS` output controller
(`Assets/AvatarBridgeOutput/Angela_PC_SPS/Angela_PC_SPS (ChilloutVR)_CVR.controller`).
No Unity session required; the curves survive conversion even though the objects they
address were stripped, which is why this was readable statically.

## Curve census: 2784 `material._SPS_*` bindings across four kinds of object

| Target (leaf name) | Bindings | What it is | Fate under clean-room |
|---|---:|---|---|
| `SpsScreenMarker` | 2000 | Socket marker renderers feeding the GrabPass atlas | **Drop all** |
| `SpsResolver` | 368 | Per-plug resolver renderer (atlas lookup) | **Adopt 5, drop the rest** |
| `[VF390]/[VF379] Handjob` | 400 | Socket-on-a-hand marker renderers | **Drop all** |
| `Cock` | 16 | The actual plug body renderer | **Adopt all 4** |

So ~86% of the SPS curve mass describes the screen-atlas machinery we are not porting.
The plan's feared "96-property re-routing table" collapses to a **small adoption list**;
everything else is dropped with one report line stating the count.

## Adoption list (the only properties the clean-room deform needs)

From `SpsResolver` → re-point onto the plug body renderer's material:

| Property | Meaning | Notes |
|---|---|---|
| `_SPS_Enabled` | apply fraction / master gate | 0..1, animator-driven |
| `_SPS_BakedLength` | plug length at bake | scales the deform curve |
| `_SPS_BakedRadius` | plug radius at bake | hole-collapse sizing |
| `_SPS_Overrun` | allow travel past the hole | toggle |
| `_SPS_Legacy` | accept DPS light sockets | becomes our P1 channel switch |

Already on the plug renderer (`Cock`), keep as-is:
`_SPS_DisableDepth`, `_SPS_DisableShadows`, `_SPS_PlayerIdLow`, `_SPS_PlayerIdHigh`.

Deliberately dropped: every `_SPS_Socket*` (marker-side description of a socket — our
sockets are lights + pointers, not markers), all `_SPS_Tag{Include,Exclude}*` (v1 cut:
no tag filtering), `_SPS_BakedRadiusSamples0..3` (resolver-side radius profile; the
clean-room deform samples the bake texture directly), `_SPS_MetadataColor`,
`_SPS_GuidedTarget*` (guided paths are a v1 cut), `_SPS_Configured`, `_SPS_Id{Low,High}`
(atlas identity — no atlas, no identity needed).

## Confirmed structural facts

- `_SPS_Enabled` really does live on `.../BakedSpsPlug/OneSpace/SpsResolver`, `classID: 23`
  (MeshRenderer) — an object the port deletes. Re-pointing is mandatory, as the plan said.
- Plug hierarchy shape: `Armature/Hips/<bone>/SPS Plug/BakedSpsPlug/OneSpace/{SpsResolver,…}`.
  The deletion list keys off `BakedSpsPlug/OneSpace/*` renderers plus `SpsScreenMarker`.
- Sockets can live on hands (`[VF390] Handjob`), not only the hips — the socket authoring
  pass must not assume a pelvis.

## Deform core size (license scoping, confirms V4)

`SPS/deform/*.cginc` + `common/sps_utils.cginc` = **744 lines** total
(bake 66, bezier 83, control points 54, curve 173, globals 59, main 185, props 34, utils 90).
The chain-read interface we replace is `resolver/sps_resolver_payload.cginc` (98 lines).
Reimplementing from behaviour is smaller than the retarget tooling would have been.

## Still needs a Unity session (Phase 0a remainder)

- Bake with `enableSps` off on one plug: confirm the objects, contacts and `_SPS_Bake`
  texture still appear while the body shader stays unpatched (risk R12).
- Whether the bake still emits legacy DPS lights with the socket's DPS-compat flag off.
- 0b: read a `_SPS_Bake` texture with our own reader and reconstruct the mesh in a gizmo.
