# Review for Claude

Read-only code review findings, 2026-09-08. Initial review covered the previous
checkpoint `ab76fd2`, released `v4.5.0`, and dev `10a58f0`. Before writing this
document, HEAD was rechecked at `01ba6bf` and the working tree was clean. The
references below were rechecked against that state. This document is the only
requested edit; it does not authorize fixes, builds, corpus runs, pushes or releases.

Please assess each finding against the latest code. Confirm or refute it with the
actual caller and data flow. These are static findings, not new ChilloutVR runtime
reproductions. The later tag work must be distinguished from the shipped release.

## Findings still open in dev

### 1. Limb-socket ownership fix is missing from conversion

- `Editor/Yaps/Setup/YapsNativeBuilder.cs:826` disables self-exclusion when
  `MeshIsTheSocket(...) && RidesAMovingLimb(...)` makes ownership unreliable.
- `Editor/Yaps/YapsConverter.cs:861-863` still chooses exclusion solely from
  whether an own plug rests within `p.Length + 0.1f` of the socket at conversion.
- The converter then calls `AdoptSocket`; adoption does not apply the toolkit
  bake's new ownership guard.

Trigger: a dedicated socket mesh on a limb qualifies for self-exclusion in the
conversion pose, then moves close to another player's hips. The ownership heuristic
can attribute it to that player and reject their plug. The toolkit's fix does not
protect the conversion path. This issue exists in 4.5.0 and remains partly fixed in dev.

Review target: make the ownership decision consistent across both builders and
verify the body/head cases still behave as intended. Runtime proof needs a moving
limb and a second player; compilation alone cannot establish that behavior.

### 2. Marker-light fallback bypasses the new tag filter

- `Editor/Yaps/yaps_resolve.cginc:604` clears channel engagement when
  `YapsTagsRefuse(socketTags)` returns true.
- The light search still runs. A valid light sets `found` and the socket position.
- At line 805, `socket.engaged <= 0 && litRoot` restores engagement from distance
  without applying the tag rule.
- Atlas candidates are filtered at line 461, but when none passes, the previously
  engaged light result survives.

Concrete case: require a nonzero tag, offer only an untagged legacy marker-light
socket within one plug length, and have no acceptable atlas candidate. The channel
rejects it, but the fallback can return positive engagement. This contradicts the
documented rule that a nonempty include set refuses untagged sockets.

An excluded tagged socket that also emits lights can similarly be rediscovered
without its tag identity. Decide explicitly how tag filtering interacts with that
loss of information; testing the shared predicate alone cannot prove the resolver.

This concerns the new tag feature in dev, not a shipped 4.5.0 feature claim.

### 3. Contact-channel tag payload is not wired

- The resolver reads the tag set from `_YAPS_SocketFlags.z`.
- `Editor/Yaps/YapsChannel.cs:265-273` creates the flags driver and publishes
  engagement into X and hole state into Y. It does not publish a socket tag into Z.
- The socket's new tag set is passed to its atlas writer. No equivalent channel
  writer was found in the converter, native channel, or prop path reviewed.

With the atlas unavailable and marker lights absent, a tagged socket cannot
satisfy a nonempty include filter through the generated channel because its tag
set never arrives. An exclude filter cannot distinguish those tags either.

Review target: either implement the required transport or accurately delimit the
feature's supported transports. A common shader predicate does not establish
parity when one input path never supplies the value.

### 4. README still gives the old atlas dimensions

`README.md:763` and `README.md:1119` advertise 552 by 520 pixels. Protocol version 3
adds the third socket pixel and requires 808 by 520. The work queue already records
the wider rectangle and the need to repeat camera coverage before shipping tags.
The README should agree with the current implementation and its verification status.

## Recheck of the four issues identified in 4.5.0

| Issue | Current dev evidence | Remaining limitation |
| --- | --- | --- |
| Chain continues through a hole | `yaps_resolve.cginc:542-549` stops counting links after the first hole | No new runtime reproduction performed in this review |
| Toolkit bake captures the body after climbing bones | `YapsBaker.cs:586` calls `TookTheBody` and refuses the bake | Guard requires a humanoid Animator and captured mapped extremities; it is not a general mask solution |
| Limb socket misidentifies owner | Toolkit guard exists; converter lacks it | Finding 1 remains open |
| Offset marker ranges exclude narrow-window compatible mods | Socket ranges are now `.4106`, `.4206`, `.4506`; tracker is `.4906` | Source restoration verified, external mod behavior not tested here |

The body guard checks Head, LeftFoot, RightFoot and LeftHand. It returns false when
there is no humanoid mapping. Do not describe this as protection for every rig or
every body mesh. No claim is made here that the missing RightHand entry alone
explains a demonstrated failure.

## What was actually verified about the release

- GitHub reports `v4.5.0` as published (`isDraft: false`).
- Published asset digests match the local package files:
  - AvatarBridge: `c3a12f60deafc84374fc44c8c8ac49877d5254497f06cbbd1f3a158e9b0cd685`
  - YAPS: `e72deaac45d359fc7e1aef8fc8952e3dde761829844456e39a62bbbec133eda7`
- Both archives were read in memory. All 148 packaged `.cs`, `.cginc` and `.shader`
  files checked against the release tag matched after newline normalization.
- No pathname matches for Dev, Regression, AGENTS.md, CLAUDE.md or local.cfg were
  found. The public converter package had no `/Yaps/` entries.
- README anchor and whitespace checks passed at the initial review state.

Package identity is not proof that its runtime behavior is bug-free. No builds,
Unity sessions, corpus runs or ChilloutVR tests were started for this review.

## Requested review outcome

Return a disposition for each numbered finding: confirmed, refuted with evidence,
or needs runtime proof. Keep shipped issues separate from unreleased tag work.
If fixes are subsequently requested, follow the project rule that docs move with
user-facing code, and check both conversion and toolkit paths. This review is a
handoff, not a replacement for `docs/Unfinished.md` as the work queue.
