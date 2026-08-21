# Where this could go next

Written the day 4.0.2 shipped. Nothing here is promised, and the standing rule still
outranks all of it: a bug somebody hits while wearing an avatar comes first. Most of what
follows exists because a real failure pointed at it.

---

## Nothing VRCFury switched off stays off by accident

**The single highest-value thing on this list, because it is not a feature — it is a class of
bug that has already cost two users and five sittings.**

VRCFury bakes a thing switched OFF and leaves one of its own runtime services to switch it back
on. Conversion deletes that service, because the service is VRChat plumbing. The thing then
stays off for good, and there is nothing anywhere saying so: it looks exactly like a feature
that does not work.

Every instance found so far, each by somebody wearing an avatar in game rather than by any check
this tool has:

| what Fury switched off | what was meant to switch it on | how it presented |
|---|---|---|
| socket branches | the SPS enable service | socket does nothing |
| `Original Object` under a baked socket | the same service | socket does nothing |
| `vrcfAlwaysVisibleHead`, and a socket baked onto it | Fury's own head service | socket does nothing |
| socket RECEIVERS (`PenSelfNewRoot`, `PenOthersNewTip`) | `OverlappingContactsFixService` | pointers live in game, no bend |
| socket exclusivity, merged at weight 0 | VRChat's Action playable | sockets animated off, nothing turns them on |

Five different objects, one mechanism, fixed one at a time over three evenings. That is the
wrong shape of work.

**The rule that covers all of them:** anything Fury left switched off, that NOTHING in the
animator can ever switch on, is stuck rather than chosen — switch it on. It is the same question
the hidden-mesh pass already answers across 84 avatars ("can anything turn this on?"), pointed at
Fury's residue instead of at renderers.

What it would have to get right:

- **A toggle still owns what it can switch.** An outfit the author left off is a decision, not
  residue. The switchable check already draws that line and is proven.
- **A layer at weight 0 owns nothing.** Socket exclusivity animates sockets off and can never
  animate them on, so its curves are residue too — the check has to read layer weight, not just
  whether a curve exists.
- **Fury disables some things on purpose.** Unknown how many; the corpus decides. If the pass
  wakes something that should have stayed dark, that shows up as an avatar wearing clothing it
  should not.

What it is NOT: removing Fury's output. After a bake, the generated objects ARE the avatar's
features — the toggles, the clothing, `BakedSpsSocket` itself. Deleting those deletes what the
user paid for. The target is the DEPENDENCY on Fury's runtime, not the things Fury built.

Cost: a day, plus a full corpus, plus an avatar with an always-visible head added to the corpus —
there are zero of those in 84 and it is a common Fury feature, which is why this class kept
reaching users instead of the regression run.

---

## The preview tells the truth about the game

The setup window's preview bends a plug toward a socket by reading transforms. The GAME needs the
socket's pointers, marker lights and receivers switched on. So a socket can preview perfectly and
do nothing in game, and it did: pointers visible in CVR's own debug view, no bend, and the tool
saying the socket was fine throughout.

4.2.0 makes the window HONEST — each socket row says which of the three is dark and what it
costs. That closes the lie. It does not close the gap.

The gap worth closing is a preview that resolves a socket the way the game does: pointer and
trigger overlap by geometry, the enabled and active state of both, the depth it would publish,
the parameters it would drive. Then "it previews" and "it works" are the same sentence, and this
entire class of report — works in editor, dead in game — stops existing.

Wants measuring first: how much of CVR's contact resolution has to be reproduced before the
answer is trustworthy. A preview that is right most of the time is worse than one that is
honest about being a preview.

---

## YAPS 5: the plug follows a path, not a point

Today `yaps_resolve.cginc` holds exactly one socket — one position, one forward, one
engagement, chosen as the single best candidate. Give it an **ordered list of sockets with
arc-length ranges** and three of the four things on the wishlist fall out of one change:

| want | how it falls out |
|---|---|
| a ring mid-shaft *and* a hole at the tip | two entries in the list |
| portal: in at one socket, out of another | two entries with a gap between their ranges |
| duplicate: the shaft showing in two places | the same range mapped twice |

A vertex shader cannot create geometry, so a "duplicate" is the existing shaft drawn at two
frames rather than a second shaft. It costs nothing and it cannot be two different lengths.

**Multiple plugs per socket is a separate, much smaller job.** The bending already works:
every plug resolves sockets in its own shader, so two plugs into one socket needs no change
at all. What breaks is the socket's *shapes* — a ChilloutVR trigger writes from whichever
sender touched it last, so two plugs fight over the depth instead of the deepest winning.
Socket-side, no extra sync, and it can ship on its own.

**What the platform gives and charges for.** Unity hands a mesh four vertex-light slots and a
socket takes two, so the light path carries exactly two sockets for free — a portal pair is
two sockets, so the headline case can cost nothing. The contact channel is where bits get
expensive: eight values per socket, 96–288 bits per plug by tier, doubled by a second socket.
Give socket two its own lower tier (engagement and position, orientation dropped first).

Socket depth stays one synced parameter per socket. That is deliberate: sharing a slot
between sockets assumes one is engaged at a time, which is exactly the assumption
multi-socket and portal exist to break.

**Order to build it in:** deepest-plug-wins first (small, self-contained), then the socket
list with socket two on lights, then portal and duplicate as ranges on top, then the
converter repointing an author's reactions onto YAPS's own depth — last, because it deletes
VRCFury's solver and their setups vary wildly.

---

## The converter: prove it before the instance does

Everything here comes from the same observation: the expensive bugs are the ones nobody can
see in the editor, and every one this month was found by a person wearing the avatar.

**A bench, so "optimised" stops being a claim.** Spawn the avatar N times in a fixed scene,
let it settle, sample frame timings over a few hundred frames, and report CPU and GPU
milliseconds. The absolute number means nothing; the delta between two runs on one machine
means everything, and nothing in the project can currently produce one.

It would settle questions the tool answers by assertion today. Does Free wins cost less to
run or only read tidier? Do 164 cloth solvers over 1,070 transforms actually hurt? The texture
pass is the interesting case, because the honest expectation is **~0 ms**: shrinking a map
buys memory and load, not frame time, and a bench that reports zero there is doing its job.

**It has to answer in VR terms, or it answers the wrong question.** Almost everyone wearing
these avatars is in a headset, and the two halves of the cost scale differently:

- **CPU transfers nearly one to one.** Cloth, contacts, animator evaluation and skinning run
  once a frame however many eyes are drawn. What changes is the budget: 90 Hz allows 11.1 ms
  against a desktop 60 Hz frame's 16.7, so the same 2 ms of cloth is 12% of one and 18% of the
  other. Report the share, not the milliseconds.
- **GPU transfers not at all.** Stereo roughly doubles it and per-eye resolution dwarfs an
  editor viewport, so a bench rendering into the game view reports a number nobody will
  experience. Render single-pass instanced, which is what ChilloutVR uses, at real per-eye
  resolution.

And it wants calibrating once against a real session in a headset, because play mode has no
compositor and no reprojection. Measure one avatar both ways, keep the ratio, apply it.

Belongs with the twin below: both exist so a bug or a cost shows up in the editor rather than
in somebody's instance.

**A remote twin.** The strongest idea on this page. Spawn a second copy of the converted
avatar beside the first, driven *only* by parameters that actually sync, and let the tester
walk between them. Every wearer-only bug becomes visible in the editor: the socket shapes
that moved for nobody, the toggle that never left the wearer's machine, the smoothing rig
that froze at its defaults. The Animator Tester already snaps `#` locals to their defaults
for its Remote view; this is that idea given a body.

**A conversion diff.** The regression harness writes a digest per avatar and compares runs.
Users get the same tool: convert, change a setting, convert again, and read what moved.
"Reconverting on a new release" stops being an act of faith.

**A toggle sweep, shipped.** `Dev/Corpus/ToggleSweep.cs` flips every menu toggle on and off
and reports anything that did not come back. It found real bugs. It should be a button in
the Toolkit rather than a thing only the harness runs.

**A budget planner.** The sync tally exists in the report; make it interactive. Tick the
features you want, watch the bits, and let it name the cheapest thing to drop. Most avatars
that go over do it by accident, in a menu they never counted.

**An upload preflight.** One list, run before uploading: our diagnostics, the CCK's own
validators, the texture flags, parameters past the cap, missing metas. Everything that
currently fails at the last moment, in an order that says which to fix first.

**Advanced Tagging, inferred.** Content tags are currently listed as "not converted, set
them yourself". An avatar carrying YAPS knows what it is. Offer the tags rather than
assuming them, and nobody uploads an NSFW avatar untagged by accident.

**Incremental reconversion.** Whole-avatar every time today. Keyed on source hashes, the
passes whose inputs did not change could be skipped. Worth it only once the corpus can prove
a skipped pass and a run pass produce the same output.

---

## GoGoLoco's poses, through ChilloutVR's own front door

The most visible thing a converted avatar loses. GoGo is stripped because its layers assert body
poses while ChilloutVR's locomotion is asserting them too, and two writers on one body is the
bicycle pose everybody has seen. Merging it harder does not help; the fight is the problem.

**ChilloutVR already has the door.** The CCK ships a full set of override slots, and the client
plays them from its own state machine, so nothing competes:

| slots | what they would hold | where it shows up |
|---|---|---|
| `Emote1`–`Emote8` | the popular poses, one each | the quick-menu emote wheel |
| `ToggleDefault`, `ToggleState1`–`7` | stances meant to be held | the toggle list |
| `LocSitting` | the avatar's sit pose | automatically, in chairs |
| `LocCrouch* / LocProne* / LocFlying / LocSwimming*` | stance art | CVR's own states |
| `LocIdle / LocWalking* / LocRunning*` | already done, by LocomotionGrafter | — |

Locomotion stays with the grafter, and the reason is measured rather than preferred: the CCK
reuses eleven clips, every one of them a Right variant (`LocWalkingStrafeRight`,
`LocRunningStrafeRight*`, `LocCrouchRight`, `LocProneRight`), at both the left and right
positions with the tree mirroring them. An override is one clip per ASSET, so it would put a
mirrored right strafe on the left and throw away the author's real left clip; the grafter
matches by POSITION and keeps it. Overrides would also mean the avatar has to run an override
controller, which the CCK's "Create Controller" regenerates, so a user pressing that button
would wipe their locomotion.

**And the client names the menu from the clip.** `AvatarAnimatorManager.FindLegacyEmotesAndToggles`
switches on the ORIGINAL slot name and takes the OVERRIDE clip's name as the label:

```csharp
case "Emote1": _legacyOverrideEmoteNames[0] = originalOverride.Value.name;
case "ToggleDefault": _legacyToggleNames[0] = originalOverride.Value.name;
```

So a clip named "Sit Cross-Legged" in `Emote1` is what the wearer reads in the wheel. No AAS
entry, no parameter, no sync bits, and it works on every avatar this tool converts today,
because grafting CVR's locomotion is what puts those slots on the controller in the first place.

**Three constraints that shape the feature:**

1. **Sixteen, not fifty.** Eight emotes and eight toggles. GoGo ships far more, plus puppet-style
   fine positioning with no equivalent here. This carries the popular poses, not the system.
2. **Names are load-bearing twice.** A clip whose name contains "Emote", or matches the stock
   eight (Wave, Bow, Die, Backflip, Point, Sad, Salute, Dance), makes the client mute both hand
   layers while it plays. The harvested names must be sanitised even though they are also the
   labels the wearer sees.
3. **An emote ends when you move.** Right for a dance, wrong for a sit somebody wants to hold:
   those belong in the toggle slots, which is also how to tell the two apart when harvesting.

**Order to build it in:** identify GoGo's pose clips and classify each as held or momentary;
fill the toggle slots from the held ones and the emote slots from the rest, best-known first;
sanitise the names; write the override controller. Report what landed where and what did not
fit, because "which of my poses survived" is the first question anyone will ask.

## The animator merger: make the invisible visible

Ten thousand lines, the most fragile thing in the package, and the place where a bug is
hardest to see because nothing throws — the avatar simply behaves slightly wrongly.

**A conflict map.** Which layers write the same binding, in what order, at what weight, and
who wins. Half the toggle failures this project has fixed are two layers writing one
property and nobody knowing. It would also have caught the strength bug on sight: a layer at
weight 0.2 writing a blendshape *no other layer writes* creeps to full, because the value it
blends from is its own last frame. That is a rule a map can check.

**A weight audit.** Flag every partial-weight layer whose properties nothing else writes.
That single check is worth writing on its own.

**State-machine reachability.** States nothing can enter, transitions whose conditions can
never be true, parameters read but never written. The pieces exist across the diagnostics;
gathering them into one pass would name a class of bug that currently only shows up as "the
toggle does nothing".

**Explain a parameter.** Pick one and get its whole life: where it came from, what renamed
it, whether it syncs and why, which layers read it, which clips write it. Debugging today
means reading three report sections and inferring the join.

**Deterministic ordering, stated.** Layer order decides who wins. The merger has rules for
it; they should be visible in the report, so a surprising result can be traced to a rule
rather than to luck.

---

## Marker lights are a broadcast, and the room is listening

Added 2026-08-17, after a user reported their controllers vibrating whenever they came within
two metres of a converted avatar. Everything here was learned from source, and it changes what a
socket is allowed to be.

**A marker light is the only thing an avatar does that reaches into someone else's.** Its range
is a message — every decoder reads `range % 0.1` and matches 0.01 hole, 0.02 ring, 0.05 front,
0.09 plug tip — and anything on the platform may listen. Raliv's shader matches within 0.005; a
toy mod reading the same protocol in C# matches within 0.001. VRChat authors +0.0006, inside
both, which is why a stock converted avatar drove a stranger's toy across a room. YAPS authors
+0.003, which DPS reads and that mod does not.

**Toy integration cannot be made safe from the socket side, and that is not our bug.** The mod
computes reach as `1 - distance / giver.Length`, and estimates `Length` from whichever renderer
sits first under the avatar root rather than reading the length DPS states in the tip light's
intensity. A socket is only ever the target of somebody else's number. So:

- **plug side, one day: yes.** A plug can declare its own length honestly, by giving the tracker
  light a sibling whose mesh states the measured length. The mod climbs to the first renderer
  under the light, so an inactive one on the marker object is enough, and then it engages within
  a plug length rather than a room. That is a switch that risks only the wearer.
- **socket side: never.** Any "let toy mods read my sockets" control hands strangers the right
  to decide how far away they can reach you.

Both are moot if [ddakebono/CVRGoesBrrr#2](https://github.com/ddakebono/CVRGoesBrrr/pull/2)
merges, since it bounds the estimate with the stated length. Old builds stay broken either way,
so the quiet offset remains the default long after any merge.

**A tempting idea that does not work: shrinking the ranges.** Since only `range % 0.1` is read,
0.0106 decodes exactly like 0.4106 and reaches a fortieth as far. It would ease vertex-slot
pressure — but Unity ranks the four per-object light slots BY RANGE, so a tiny light is the
first evicted by every stock DPS avatar in the room. It would work alone and fail in company,
which is the opposite of what a compatibility feature needs. Worth keeping only as a "YAPS talks
to YAPS and nothing else" mode, where our own decoder sets the rules.

## The WASM route, and what it would make of all this

Read from Joe's client on 2026-08-17. **His install is the `public-scripting` beta**, and the
bridge's initialiser sits behind `#if WASM_SCRIPTING_ENABLED`, which a ChilloutVR dev confirmed is
defined only on that branch. So none of this is in stable, and nothing here can be built yet.
Written down because it changes what YAPS 5 should aim at.

**What a script actually gets.** Not the thin, movement-shaped API the `CVR_*` binding names
suggest. Underneath is a generated binder, `WasmBinder.Links.UnityEngine`, mapping **107 Unity
types** including `Material`, `MaterialPropertyBlock`, `Renderer`, `SkinnedMeshRenderer`,
`Shader` and `Transform`. `MaterialLink` alone exports **276 functions**, `SetFloat`, `SetVector`
and `SetTexture` among them. Plus `CVR_Avatar_GetAllAvatars`, avatar root transforms, and a
`Networking` binding set that is not the AAS budget.

**And the permission model is the right shape.** Reading a transform has no access check;
*writing* one, and every material setter, demands `CVRScriptScopeContext.Self`. A script may look
at everything in the instance and touch only its own avatar.

That is the entire YAPS resolver, in ordinary code:

| fought today | under a script |
|---|---|
| contacts, 512 overlapping pairs a frame for the whole instance | read transforms directly |
| marker lights, four vertex slots a mesh | write the material directly |
| the socket's kind encoded in a light's RANGE, two free digits | any data, any shape |
| one socket per plug | a list, arc ranges, portals, whatever the code says |
| 3200 AAS bits | its own networking |

**The backwards-compatibility prize is bigger than the feature.** A script does not need anyone
else to cooperate: it can walk another avatar's hierarchy and read their sockets *whatever
protocol they speak* — Raliv's lights, TPS and SPS pointers, YAPS markers — because it reads the
components rather than waiting for Unity to hand it four light slots or for a contact pair to
survive the budget. Everything on the platform becomes findable, including content that will
never be converted and whose authors are long gone. That is what "force everything over to YAPS"
can actually mean: not converting other people's avatars, but understanding them.

**It stays a tier, not a replacement.** Our sockets must keep emitting lights and pointers, since
that is the only way somebody else's plug finds us, and a script cannot run on stable or for a
wearer who refuses it. So: script when it is there, contacts next, lights last, which is the
tiering the resolver already has.

### What YAPS becomes with a script

Three features, in the order they are worth building.

**Bones, not vertices.** A script may write `Transform.position` on its own avatar, and that is
the whole of what YAPS does today expressed as ordinary code: pose a bone chain toward the socket
rather than displace vertices in a shader. Everything the vertex path fights disappears with it.
No shader patching, so a plug works on a shader that refuses to be patched and on whatever
Poiyomi does next. No bake texture, which is megabytes of VRAM per plug and the reason a re-bake
is needed after every update. Correct shadows and depth, because the mesh really is where it
looks. Cloth and colliders can interact with it. And a bone chain is what most plugs already have.

**A shader fallback, kept forever.** A plug modelled as a single rigid mesh with no bone chain
cannot be posed, and neither can a plug on a client with no script. So the vertex deform stays as
the floor, and the rule is the same shape as the socket resolver's: **bones if the mesh has them
and the script runs, the shader otherwise.** The toolkit already knows which it is: the bake
records `_YAPS_FrameFromVertex`, and the survey knows a mesh's bone chain.

**A detector for everything already out there.** A script can walk any avatar in the instance and
read its sockets whatever protocol they speak, because it reads components rather than waiting
for a light slot or a contact pair. Old Raliv DPS lights, TPS and SPS pointers, YAPS markers, all
of it, with no cost to the four vertex slots and none to the 512-pair budget. Content whose
authors left the platform years ago becomes usable, and that is worth more than any new feature.

**Access: asked, and declined, 2026-08-19.** ChilloutVR gates the scripting CCK. The request was
made and the answer was no, for reasons that matter more than the refusal:

- **They are building WORLD scripting.** Avatar and prop scripting is not what they are working
  on, so feedback on it is not useful to them yet. Everything below is designed against an
  implementation that does not exist and may not for a long time.
- **They asked that AI tools not be used to analyse their scripting implementation.** Joining the
  testing group means agreeing to that. It is their call and worth respecting past the letter of
  it: no AI-assisted digging into the scripting side, and nothing here waits on doing so.

**So this waits for public release rather than for an invitation.** When scripting ships to
everyone, writing against a documented API is ordinary work and none of the above applies. Until
then this section is a design sketch, not a plan, and nothing else in the project depends on it.

**The authoring half exists.** An experimental CCK with WASM components is in closed testing.
So this is moving rather than hypothetical, and Joe already runs the scripting branch, which
means the week it reaches everybody a prototype is possible: one plug, one socket, a resolver
that reads transforms and writes `_YAPS_SocketPos`, and the answer to whether any of the rest is
worth designing.

**Unknowns to settle before building:** what approving a script costs a wearer socially, whether
open read access survives to release, what a per-frame resolver costs in a full instance, and
whether a scripted avatar can still be found by everyone else's plugs (it must: the lights and
pointers stay either way).

## Two constraints worth remembering before designing anything

**ChilloutVR runs an avatar's triggers on the wearer's machine alone.** Anything
contact-driven that the room must see has to be a synced parameter. This is not VRChat's
model and it invalidates the instinct to make contact parameters local because they are
"free everywhere".

**An animator-driven parameter can never sync.** `IsReadOnly` is true for anything
controlled by a curve, and `IsSynced` requires `!IsReadOnly`. Any design that computes a
value in the animator and expects the room to see it is already wrong; sync the inputs, or
compute it on every client from something that does sync.
