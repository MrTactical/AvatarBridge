# Spike results

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
