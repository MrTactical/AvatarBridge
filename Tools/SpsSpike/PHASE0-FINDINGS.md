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
