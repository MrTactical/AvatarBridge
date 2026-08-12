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

Still open: **S2b cross-content** — lights on one avatar, probe on a *different* avatar or
prop with a second person present. That is the case SPS actually needs, and it is the one
where light culling masks and per-player layers could still bite.

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
