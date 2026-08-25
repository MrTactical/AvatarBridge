# AvatarBridge — External Code Audit

**Date:** 2026-08-25 · **Auditor:** independent AI review (ox-alpha), at the maintainer's request
**Scope:** all shipping C# and HLSL under `Editor\` and `Runtime\` (~50k lines, 100 files). `Dev\` and `Regression\` skimmed for context only.
**Version audited:** `4.3.2` (`Editor\BridgeDefines.cs:23`), git head `db6839f`.

Findings are graded and each carries a verification status:

- **[VERIFIED]** — the auditor read the code and confirmed the bug personally.
- **[AGENT-VERIFIED]** — found and checked against surrounding context by a review pass; line numbers may drift a little; re-confirm before fixing.

Line numbers refer to the audited revision and will move as files change.

---

## 1. What this project is (context for the fixing agent)

A Unity **Editor** tool converting VRChat SDK3 avatars into ChilloutVR CCK4 avatars.
Entry: `AvatarBridgeWindow` → `BridgeConverter.Convert` → `BridgePipeline.Execute` over a
statically declared, trait-validated pass list sharing one `BridgeContext`. Major passes:
strip VRChat-only systems → rehome baked assets → descriptor → face tracking → menu/params →
PhysBones (MagicaCloth2/DynamicBone) → contacts → **AnimatorMerger** (9.6k lines) →
constraints → stereo shader patch → YAPS → misc → clip-editing tail → reports.
YAPS (penetration) deforms plugs via shader code reading a baked data texture; runtime
components are inert markers — everything in-game is shader- and animator-driven.

The codebase's own discipline is high: per-run cache resets, copy-vs-mutate guards,
pass-order validation as checked data (`BridgePipeline.Validate`), a regression corpus,
and `docs/Unfinished.md` as an honest known-issues ledger. The findings below are what
survived that discipline.

---

## 2. Findings — fix first

### F1. Shared submenu asset converted once; second appearance silently dropped
**Severity: MAJOR · Status [VERIFIED] · `Editor\Core\ParameterMenuConverter.cs:99, 101, 540–548`**

`WalkMenu` guards recursion with `HashSet<VRCExpressionsMenu> visited`:

```csharp
if (menu == null || visited.Contains(menu)) return;
visited.Add(menu);
```

VRChat legally allows the **same** submenu asset under two parents (e.g. a "Colors"
submenu referenced by both "Outfit A" and "Outfit B"). The second instance returns
immediately, so none of its controls are recorded into `uses`. Consequences downstream:

- Parameters used **only** by the second instance never register as menu-driven. In
  `BuildEntry` (~line 340) they hit the `!hasMenu` branch and are dropped as
  "Not referenced by any menu control" — or become sliders instead of toggles
  (~lines 398–414).
- The "menu parameter synced although VRChat marked them local" resync pass
  (lines 110–135) also misses them, because it keys off `uses`.

Net effect: working VRChat menu controls vanish or change type in CVR with **no report
entry** — violating the tool's own never-fail-silently contract.

**Fix:** the visited set conflates *asset identity* with *reachability*. Either walk
without a visited set but depth-cap (menus are shallow; VRChat caps menu nesting at 8),
or key the set on `(menu, prefix)` pairs, or record control uses for every instance and
keep the visited set purely for cycle prevention (a cycle needs only a depth cap, since
a repeated *path* is legal while a repeated *prefix+control* is not).

---

### F2. Preset JSON cache serves stale content and permanently caches misses
**Severity: MAJOR · Status [VERIFIED] · `Editor\Core\Physics\MagicaPresetLibrary.cs:309–336`**

```csharp
static readonly Dictionary<string, string> JsonCache = new Dictionary<string, string>();
...
JsonCache[presetName] = json;   // caches hits AND null misses; never cleared
return json;
```

Two failure modes:

1. **Miss is cached forever.** Convert once before `MC2_Preset_Bridge_<Class>.json`
   exists in the project → `null` is stored → the documented workflow "drop your own
   preset in and convert again" silently keeps shipping built-in defaults for the rest
   of the Unity session.
2. **Edits are invisible.** User retunes a preset JSON on disk mid-session → Unity
   reimports the `TextAsset` with fresh text, but the cache returns the old string →
   reconversion *reports "preset applied"* while applying stale values.

Note the contrast inside the same codebase: `GrabbyBonesSupport.Reset()` **is** called
per conversion (`PhysBoneConverter.cs:188`). This cache never got the same treatment.

**Fix:** stop caching misses (return null without storing); for hits, either drop the
cache entirely (the find+load is cheap and runs per chain, not per frame) or key it on
`AssetDatabase.GetAssetDependencyHash(path)` and re-read when the hash moves.

---

### F3. Locomotion graft folds left/right strafe into one clip
**Severity: MAJOR (fidelity) · Status [VERIFIED] · `Editor\Core\LocomotionGrafter.cs:473–485, 409–423, 526–563`**

`DirectionOf` mirrors east/west into one direction bucket:

```csharp
float angle = Vector2.Angle(Vector2.up, new Vector2(Mathf.Abs(position.x), position.y));
```

`GraftTree` keeps the **first** clip per slot (line 419, with the comment "later
duplicates (mirrored diagonals reusing one clip) would be the same clip anyway"), and
`ReplaceAt` then writes that single, unmirrored clip to **every** CCK child classified
into the slot — both the left and right positions of CVR's locomotion tree.

The comment states an assumption that is often false: avatars with genuinely distinct
left/right strafe animations (VRChat's own stock tree has them, and custom locomotion
frequently does) silently lose the left clip; walking left plays the right-strafe
animation. This is consistent with the CCK's own idiom — the stock controller reuses
Right-variant clips at both positions (per `docs/Unfinished.md`, "the CCK reuses eleven
clips, every one of them a Right variant") — but the whole point of the grafter vs.
override slots was to keep the author's real per-position clips.

**Fix:** keep `DirectionOf` folding for *classification* (CVR's slot set is
direction-pair shaped) but remember the sign: store per-slot the best **left** pick and
best **right** pick separately, and have `ReplaceAt` write the side-appropriate clip to
each child. Fall back to one clip for both sides only when the source truly had one.

---

### F4. Stale `ctx.AutoExposedParameters` across the parameter rename pass
**Severity: MAJOR · Status [AGENT-VERIFIED] · `Editor\Core\AnimatorMerger.cs:2003–2012, 2055–2068` (rename), `3721–3731` (consumer), filled at `Editor\Core\ParameterMenuConverter.cs:356`**

`RenamePass` rewrites parameters whose machine names contain spaces/illegal characters
and updates the auto-created menu entry's `machineName` — but never updates
`ctx.AutoExposedParameters`, which still holds the **original** VRC name. Later,
`WithdrawSelfDrivenExposures` checks `ctx.AutoExposedParameters.Contains(entry.machineName)`
against the post-rename name, misses, and leaves a live menu control on a parameter the
animator's own drivers also write — the exact "two entries, one responds" fight this
withdrawal pass exists to remove. Spaces in VRC parameter names are common, so the
trigger is not exotic.

**Fix:** when `RenamePass` renames a parameter, apply the same rename to every string in
`ctx.AutoExposedParameters` (and anywhere else context sets hold parameter names —
audit `BridgeContext` for sibling sets: `PreserveParameters`, `ContactParameters`).

---

### F5. YAPS plug bounds ratchet upward across reconverts (shared mesh mutated)
**Severity: MAJOR · Status [VERIFIED] · `Editor\Yaps\YapsBaker.cs:701–713` (`ExtendBounds`)**

```csharp
var bounds = mesh.bounds;
bounds.Expand(length * 2f);
mesh.bounds = bounds;
```

For a plain `MeshFilter` plug, this mutates the **shared** mesh asset (everything
referencing that mesh is affected) and the expansion is **additive per run**: each
conversion grows the current bounds again by 2× length per axis. The tool's own reports
encourage reconverts, so a session of tuning produces a mesh whose bounds have ratcheted
several times over, degrading culling and shadow bounds. (Skinned renderers take the safe
`updateWhenOffscreen = true` branch; only the MeshFilter path is affected — but there it
is unbounded.)

**Fix:** compute the expanded bounds from the mesh's **authored** bounds, not the current
ones — e.g. store the original size once (a hidden property on the mesh asset, like the
existing `_YAPS_SourceShader` pattern) or recompute from `vertices` before expanding.
Never accumulate onto an already-expanded value.

---

### F6. Two blendshape evaluators disagree above 100 weight
**Severity: MAJOR (measurement integrity) · Status [AGENT-VERIFIED] · `Editor\Core\Physics\MagicaClothWriter.cs:1294–1313` vs `Editor\Core\MeshGrowth.cs:341–375`**

When an animated blendshape weight exceeds its final frame weight (slider curves
animated past 100):

- `MagicaClothWriter.ApplyBlendShape` computes `t > 1` and uses `LerpUnclamped`,
  **extrapolating** deltas the artist never authored;
- `MeshGrowth.Apply` **clamps** (`Clamp01`) to the last frame — which is what Unity's own
  skinning does.

These two implementations serve the same purpose (measuring the mesh at its largest
animated state) and feed each other: collider fit and particle radii can be sized to an
impossible body shape whenever any growth slider tops 100, while contact-zone growth
measures a different body. The tool's reports explicitly promise sizes "measured from
your mesh" — this breaks that promise at the edge.

**Fix:** pick one semantic (clamp, matching Unity skinning, is the defensible one) and
make both call sites share a single evaluator — `MeshGrowth` already exists as the
shared home for mesh measurement; move `ApplyBlendShape`'s logic there.

---

### F7. Three different definitions of "protocol light"
**Severity: MAJOR (cross-component contract) · Status [AGENT-VERIFIED] ·
`Editor\Yaps\yaps_resolve.cginc:250–252` (socket decoder) ·
`Editor\Yaps\yaps_socket.cginc:117–118` (plug decoder) ·
`Editor\Yaps\Setup\YapsScanner.cs:95–101` (editor scanner, 0.02 threshold, range 0.05–0.5)**

The same physical marker light is classified by three code paths with three different
tests:

- socket side rejects a slot as "somebody's real lighting" only when `rgb > ε && a > 0`
  — a light with colour but **alpha 0** passes as protocol and can win one of the four
  vertex-light slots as a fake root/front;
- the plug tracker search uses a stricter pure-black test;
- the C# scanner uses a third threshold (0.02) and a range window.

The editor scan, the plug decoder and the socket decoder can therefore disagree about
the same light: the preview says a socket is wired, one decoder agrees, the other
ignores it. This is precisely the "works in editor, dead in game" class the project's
own docs fight against.

**Fix:** write the canonical test once (a shared `.cginc` function plus one C# mirror of
it, cross-referenced by comment), covering: colour ≈ 0, intensity > 0, range in protocol
band. Then make all three call it.

---

## 3. Findings — minor / robustness

### F8. `ToggleNativizer.Run` early return abandons partial cleanup
**Status [AGENT-VERIFIED] · `Editor\Core\ToggleNativizer.cs:95–98` vs deferred cleanup at `189–231`**
If `ApplyTargets` returns false after earlier layers already nativized successfully, the
early return skips the `master.layers` filtering, float→bool retyping, and menu `usedType`
sync — leaving removed-from-`vrcLayers` layers still present in `master.layers` driving
natively-claimed objects. Today the reflection failure is deterministic per CCK version
so it fires before any mutation, but the pattern is one CCK update away from corrupting
output. **Fix:** run the cleanup in a `finally`, or validate all targets before mutating any.

### F9. Duplicate native toggle targets from one parameter in two Direct-tree branches
**Status [AGENT-VERIFIED] · `Editor\Core\ToggleNativizer.cs:247–267` (contrast the `expandedParams` guard at 294)**
`NativizeTreeToggles` never checks an already-nativized set before `ApplyTargets`, so the
same toggle parameter appearing in two service-tree branches appends duplicate entries to
the CCK's `gameObjectTargets` (and double-reports). `ExpandToggleBranches` has the guard;
this path lacks it. **Fix:** carry the same `expandedParams`-style set.

### F10. Exception mid-transplant leaves orphaned `[AB]` states in the CCK locomotion machine
**Status [AGENT-VERIFIED] · `Editor\Core\AnimatorMerger.cs` — adds at `4107–4160`; `armed == 0` rollback at `4216–4224` skipped when the `catch` at `4456–4461` fires**
The catch reports a warning but leaves already-added `[AB] …` pose states embedded in
`Locomotion/Emotes` with no arming — dead but shipped content, contradicting the pass's
own no-dead-states rule. **Fix:** roll back added states in the catch.

### F11. Slider hole-drop degrades to hole-filling
**Status [AGENT-VERIFIED] · `Editor\Core\AnimatorMerger.cs:583–594` vs report text at `650–661`**
When a slider tree loses holes but fewer than 2 real children survive (`kept.Count < 2`),
the branch falls through to the generic filler inserting placeholder clips — exactly the
"eligible neighbour that animates nothing" failure the accompanying report text says must
never happen to sliders. **Fix:** keep the slider's own default child even alone, and
report the tree as reduced rather than filling it.

### F12. Gesture-hand promotion by substring can hijack unrelatedly named layers
**Status [AGENT-VERIFIED] · `Editor\Core\AnimatorMerger.cs:9501–9511`**
`layerName.Contains("left")` / `.Contains("right")`: a gesture layer named "Copyright
pose" or "Leftover idle" is promoted into a hand slot, deleting CVR's own hand layer and
renaming the impostor — reported as a *successful* hand takeover. **Fix:** require the
token to stand alone (word-boundary match) or the layer to actually write a gesture
parameter.

### F13. Pruned-branch count double-counted
**Status [AGENT-VERIFIED] · `Editor\Core\SystemStripper.cs:515–531` (increment at 519) and again `566–571` (569)**
A Direct-tree clip child writing only stripped params increments `pruned` inside
`PruneMotion` and again at the call site. Report-only distortion of "Pruned N stripped
branch(es)". **Fix:** count in one place.

### F14. `AnimationSelfContainer.Fix` edits blend trees owned by foreign, non-copied assets
**Status [AGENT-VERIFIED] · `Editor\Core\AnimationSelfContainer.cs:165–178`**
When a tree lives in another asset but `NeedsCopy` is false (CCK package path), the else
branch maps the tree to itself and then rewrites `tree.children` in place if any child
motion needs copying — mutating an asset the tool does not own. Narrow trigger (a copied
clip nested under a package-owned tree) but violates the file's own containment contract.
**Fix:** when the tree's owner is not copied but a child is, copy the tree too.

### F15. `WalkMachines` has no cycle guard (both copies)
**Status [AGENT-VERIFIED] · `Editor\Core\AnimatorMerger.cs:9482–9493` · `Editor\Core\SystemStripper.cs:631–642`**
Unlike `LocomotionGrafter.AllStates` (`788–816`, which has a `seen` set), these recurse
through `stateMachines` unguarded; cyclic state-machine parentage (script-constructed or
corrupt asset) means stack overflow instead of a clean skip. **Fix:** carry a `seen` set
as the Grafter does.

### F16. `ConstraintScaleRelay.Run` catch leaves half-relayed constraints
**Status [AGENT-VERIFIED] · `Editor\Core\ConstraintScaleRelay.cs:47–57`**
An exception partway through `Apply` leaves some constraints re-anchored (sources
repointed, offsets zeroed) while others keep metre offsets — a mixed state the warning
("hats may drift") undersells. **Fix:** per-constraint try/catch so one bad relay cannot
poison the batch, and report which were skipped.

### F17. Vertex-measurement static caches never reset
**Status [AGENT-VERIFIED] · `Editor\Core\Physics\MagicaClothWriter.cs:1175–1176, 1228–1229, 1316–1317` · same pattern `Editor\Core\MeshGrowth.cs:26–29, 243–252`**
Deformed-vertex buffers and blendshape-reach tables are cached keyed by context identity
with no reset; a `BridgeContext` reused across a re-run serves stale measurements, and
every renderer's full vertex array stays pinned until domain reload. Only
`GrabbyBonesSupport` got the `Reset()` precedent. **Fix:** a shared static `Reset()`
called from `BridgeConverter` next to `OutputAssetPaths.Reset()`.

### F18. Owner-rule renderer election skips the humanoid/body guard
**Status [AGENT-VERIFIED] · `Editor\Yaps\YapsConverter.cs:287–300` interacting with `104–114`**
When the plug object's direct parent carries a `SkinnedMeshRenderer`, that renderer wins
outright with `chainLevel = null`, so the `HumanoidBoneName(chainLevel)` refusal (which
catches "your chain is the body") never runs. A plug empty parented directly under a
foreign skinned mesh object (clothing accessory case) bakes that whole mesh as the plug
instead of warning. **Fix:** run the humanoid guard for the shortcut path too.

### F19. `YapsShaderPatcher` regex soft spots (four related)
**Status [AGENT-VERIFIED] · `Editor\Yaps\YapsShaderPatcher.cs`**

- **m-a (`257–258`):** `Regex.Replace(text, @"Shader\s+""[^"]+""", newName)` is unanchored
  and global — occurrences inside comments or string literals (flattened Poiyomi sources
  carry commented examples) get renamed too. Cosmetic today; one quote-slip from invalid HLSL.
- **m-b (`196–199, 229–236`):** the "already carries YAPS" guard only checks `_YAPS_Bake`,
  and `Properties\s*\{` matches inside comments — a commented `Properties {` gets the
  block injected into the comment; the shader compiles but every `_YAPS_*` uniform
  silently reads its default. A pre-existing `_YAPS_*` property yields a duplicate-name
  block caught only later as an unrelated compile refusal.
- **m-c (`320–327`):** `CustomEditor` is appended before the last `}` of the file — a
  trailing comment containing `}` (common in hand-edited shaders) puts the directive
  inside it, silently dropping the custom inspector.
- **m-d:** the rename hash now derives from emitted source (`EmittedVersion`, fixed
  2026-08-24 per `docs/Unfinished.md`) — good — but the regexes above remain the
  fragile surface.

**Fix direction:** anchor patterns to line starts, strip comments before matching (or
match against a token stream), and verify post-patch invariants (exactly one `Shader ""`
line, exactly one `Properties {`, `CustomEditor` outside any block) with a hard failure
naming the shader — consistent with the patcher's existing compile-proof step.

### F20. Asymmetric colour/alpha acceptance also affects `_YAPS_SelfTag` ownership
**Status [AGENT-VERIFIED] · `Editor\Yaps\yaps_resolve.cginc:250–252`**
Same root as F7 but worth its own line: the ownership filter (`YapsSameBodyAs`) consumes
whatever `YapsClassifyLight` admits, so a fake-protocol light can also defeat the
self-exclusion test, not just socket classification. Fixing F7 fixes this.

---

## 4. Checked and cleared (do not re-audit)

These were explicitly hunted and are correct at the audited revision:

- **YAPS bake ↔ shader binary contract** — exact match on all counts: header offset 1
  float; `FloatsPerVertex = 10` (pos3/norm3/tan3/active1); shape blocks at
  `1 + N*10 + s*N*9`; little-endian `BitConverter.GetBytes` ↔ `r | g<<8 | b<<16 | a<<24`
  (`YapsBaker.cs:629–697` ↔ `yaps_deform.cginc:130–179`, `yaps_socket.cginc:200–222`);
  texture linear/point/clamp with mips off; width recovered from `_YAPS_Bake_TexelSize`;
  dynamic height makes 8192-width overflow impossible below Unity's 16384 cap; float
  index precision safe below 2²⁴ vertices. Raw `Load(int3)` reads (not UV samples) make
  row-order/flip bugs structurally impossible.
- **PhysBone chain decomposition** (`PhysBoneData.Read`, root/ignore lists, endpoint
  synthesis, stacked-`_End` protection, dead-root rescue, humanoid/toe exclusion) —
  survived a determined off-by-one hunt.
- **Solver math** (`PhysBoneSolverMap`) — matches the documented derivation
  (`docs/SolverCalibration.md`): `r^(60/90)` rebasing, 3-iteration un-compounding,
  0.2 restoration scale, simplified-mode ceiling; divide-by-zero guarded on every ratio.
- **Static per-run state** — `LocomotionGrafter.ResetClones`, audio/pose-space lists,
  `MergedLayerNames`, `_sourceControllerGuids` (`AnimatorMerger.cs:76–94`),
  `restoreVerdicts`, ConstraintConverter's `reparented/relocated/animatedRotationPaths`
  (`42–46`), mask caches, `OutputAssetPaths.Reset()` — all reset per conversion.
  (Exceptions: F17's measurement caches, F2's JSON cache.)
- **Collection-modified-during-iteration** — layer/children removals consistently use
  snapshots or backwards indices.
- **Source-asset mutation discipline** — clips cloned before editing (clone-on-write
  keyed by `clipMap`), `SafeToRewrite` protects non-output clips, behaviours destroyed
  on deep-copier clones, `useAutomaticThresholds` pinned around blend-tree writes
  (`SafeguardBlendParameters 3624–3632` and similar) — correct throughout.
- **Layer-ownership arbitration** (`RestorePartialOffStates`, `FillEmptyStatesWithRestoreClips`,
  `AssertOwnedBindingsEverywhere` via shared `BuildRestoreOwnership`) — consistent.
- **Gesture remap math** (`FloatBand`/`FloatBandInverse` vs `GestureMap.VrcToCvr`) —
  internally consistent including the 0.1 analog-fist band boundary.
- **Optional-dependency defines** (`BridgeDefines`) — reflection-only, always compiles;
  the 4.3.1 cross-define bug is closed by `Dev/Build/check-defines.sh` (four-combination
  compile gate).
- **Exception handling generally** — 16 `catch (Exception e)` sites all log to report or
  console; only 4 bare `catch { }` sites, all defensible reflection probes
  (`AnimatorMerger.cs:5168, 5178`, `YapsShaderGUI.cs:402`) plus one `IOException` swallow
  in preflight (`BridgePreflight.cs:75`).

---

## 5. Structural notes (not bugs; context for planning)

1. **`AnimatorMerger.cs` is 9,598 lines** — the project's own docs call it "the most
   fragile thing in the package" (`docs/Unfinished.md`, "The animator merger" section).
   F4, F10, F11, F12, F15 all live there. A `.Physics.cs` partial already exists as
   precedent; splitting toggle/gesture/audio/locomotion concerns into partials would
   shrink review surface without behaviour change.
2. **No asmdef anywhere** — everything is `Assembly-CSharp-Editor`. No internal API
   boundaries; the 4.3.1 shipped-compile-bug (cross-`#if` reference) was a direct
   consequence, and `check-defines.sh` mitigates rather than removes the class.
3. **Regex-based shader patching** (`ShaderSpiPatcher`, `YapsShaderPatcher`) is
   inherently fragile against arbitrary shader source; compile-proofing catches the
   hard failures, F19 lists the silent ones.
4. **Reflection-based integration** with VRCFury ("Build a Test Copy") and CCK
   internals (`CVRParameterStream` entries at `AnimatorMerger.cs:5135–5180`) — guarded
   with report fallbacks today; breaks silently if either SDK reshapes. The preflight
   (`BridgePreflight`) covers Fury/MA presence; consider the same for CCK version drift.

---

## 6. Suggested fix order

| # | Finding | Why this order |
|---|---|---|
| 1 | F1 submenu visited-set | Silent data loss on legal avatars; small, contained fix |
| 2 | F2 preset JSON cache | Silently defeats a documented workflow; trivial fix |
| 3 | F4 stale AutoExposedParameters | User-visible menu/driver fight; small fix in rename pass |
| 4 | F5 bounds ratchet | Corrupts shared meshes cumulatively; small fix |
| 5 | F7 (+F20) protocol-light unification | Contract drift across three decoders; medium |
| 6 | F6 blendshape evaluator split | Measurement integrity; medium (merge two implementations) |
| 7 | F3 left/right graft | Fidelity loss; medium (needs per-side picks + CCK tree check) |
| 8 | F8–F18 robustness batch | Narrow triggers; batch them |
| 9 | F19 shader-patcher regexes | Harden + post-patch invariants |

Every fix should land with a corpus run (`Dev\Corpus`) per the project's own discipline,
and F1/F3/F4 with a new fixture if the corpus lacks the shape (shared-submenu avatar;
asymmetric-strafe avatar; space-named driver-exposed parameter).

---

*Method note: findings came from a full read of the conversion core, physics writers,
and YAPS subsystem including cross-checks of every C#↔HLSL contract; suspicious
patterns were traced to their guards before reporting, and guarded code was excluded
(see §4). Line numbers are from the audited revision and will drift.*
