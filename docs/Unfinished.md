# Unfinished

The single list. Every open piece of work lives here and nowhere else. Check this document
before starting on a new idea, in case something similar is already on it, and whenever the
question is "what's next" or "what is left to do". An item leaves this file by shipping, or
by a decision recorded in `archive/`. The standing rule still outranks everything on it: a bug
somebody hits while wearing an avatar comes first.

Transport facts and their build order live in `YAPS5.md`; solver maths in `SolverCalibration.md`;
what SPS code may be looked at in `YAPS-CLEAN-ROOM.md`. Finished records are in `archive/`.

## Next up

*4.5.1, a hotfix off 4.5.0, 2026-09-11: users reported YAPS failing, cause not yet known (system or
user). The plug now reads the screen atlas first and marker lights only where the atlas found
nothing; the contact channel no longer bends a plug and is no longer built, and converting takes an
old one out. It was the one route that could latch (below), and it spent up to nine synced floats a
plug. Cost: a contact-only socket (an older TPS orifice with no lights and no atlas writer) is no
longer found. Deleted on dev the same day, once merged back: `YapsChannel` (left as an empty file,
like `ContactDiagnostics`, because importing a package never deletes and an orphaned copy would stop
compiling),
`YapsPropBuilder.AddChannel` and its builders, `YapsNativeChannel.Plugs`,
`BridgeSettings.yapsSocketFollow` and `YapsSocket.previewAsChannel`. Taking an old channel OUT
stays: `YapsNativeChannel.Clear`, `YapsPropBuilder.DropChannel` and the remover. Ask reporters for the "Resolved by" debug view: a
quarter-length plug means nothing found the socket.*

*Follow-up from the same day, DONE the same day on dev: the atlas layout follows the target. A
view too small for the full 932 by 596 rect gets fewer cells instead of none, down to about 240
pixels square. See YAPS5.md, "The layout follows the target". Untested in game. FIRST CHECK:
open the self portrait wearing a socket and look at its corner for coloured specks; the size gate
used to keep writers off it, and the fix if they show is written up there.*

*4.3.0 shipped 2026-08-23: the rebuild, the lighthouse and the light budget, after corpus runs 385,
386 and 387 passed and a tester confirmed a rebuilt mouth socket with a DPS prop in game. The
hold on 4.2.0 ended there; that number was spent on a tester build and never released.*

1. **Watch the 4.3.1 reports.** The classes that reached users before are in the corpus now, but
   the first week of a release is its real corpus. A report wearing a `test-rebuild` build is
   pre-release; ask for the file name.

   *4.3.1 shipped 2026-08-24, a hotfix: a tester with **DynamicBone and no MagicaCloth2** could
   not compile the tool at all. `DynamicBoneWriter` called two helpers that lived in
   `MagicaClothWriter`, and the two files sit behind different defines, so the callee vanished
   and the caller broke. The helpers were plain `Transform.Find` code and moved to
   `PhysBoneConverter`, which both writers already depend on and which compiles unconditionally.
   Swept the codebase after: no other cross-define reference exists.*

   **The gap this exposed is CLOSED 2026-08-24: `Dev/Build/check-defines.sh`.** There are four
   install combinations (Magica ±, DynamicBone ±) and every other gate: corpus, test project,
   the editor sitting open: runs the one with both installed, so a define mistake shipped
   invisibly and killed the tool outright for whoever hit it.

   No Unity launch and no domain reload: Unity leaves the exact compile arguments for
   `Assembly-CSharp-Editor` in a response file under `Library/Bee/artifacts/*.dag/`, so the
   script reuses them, all 323 references, the source list, the language version, and only
   swaps the two `-define:` lines. Four real compiles of the real assembly, seconds each.
   Errors outside AvatarBridge are counted and ignored, because a project full of other assets'
   editor scripts is not this gate's business.

   **Self-tested against the bug it exists for**: reintroducing the `MagicaClothWriter` call in
   `DynamicBoneWriter` makes it fail on MAGICA=0 DYNBONE=1 with `CS0103` at lines 43 and 44:
   the same file, lines and columns the tester's log carried. Run it before any release that
   touches physics or the defines.
2. **The sweep's 131 carried toggle failures**: triaged as avatar-side, no tool signature; the
   prediction that Fury's wired socket toggles flip to "responded" is worth checking in the next
   digests.
3. **The body-mesh fallback: ANSWERED 2026-08-24, and the answer is no** (`archive/Closed-2026-09.md`). The
   split is built and shipped, but a skinned body mesh receives no vertex lights, so the shader
   can never compute the depth this was meant to make free. The 32 bits stay. What is left on
   this axis is the **dedicated-mesh ownership bug** the work exposed: a hand socket's own mesh
   sits at the socket, so a hand in somebody's lap resolves ownership to THEIR hip and ignores
   their plug.

   **Handled 2026-09-08, though not the way this entry expected.** A baked owner anchor cannot
   work: an offset from the socket to the wearer's hips is only true in the pose it was baked in,
   and a hand leaves that pose immediately. A skinned mesh has no usable object matrix at all. So
   the question is genuinely undecidable for a dedicated socket mesh on a limb, and the bake now
   drops self-exclusion there rather than answering it wrongly. That is the way the resolver
   already leans: a stranger's plug ignored is no effect, where the wearer's own plug holding
   their socket open is a visibly wrong one. Sockets on the body are untouched, and the head is
   deliberately not counted as a limb so a mouth socket keeps its exclusion.

   Still wants a pass in game: a hand socket should now answer somebody else's plug, and the
   wearer's own may hold it open if it rests there, which is the trade this makes.

   **The converter did not have it until 2026-09-08**, which is the more interesting half. The
   guard went into the toolkit bake and the converter kept deciding from `ownPlugRests` alone, so
   the same avatar behaved differently depending on which door it came through, and the door
   almost everybody uses was the wrong one. `RidesAMovingLimb` takes a `Transform` now instead of
   a `YapsSocket`, which is all it ever read, so both builders call it. Two builders diverge: the
   fix is not done until the other path is grepped.
4. **The GPU bridge** (`YAPS5.md`, candidate 4): **the transport is PROVEN, in game, 2026-09-07.**
   **D2 is built and waiting on a run, 2026-09-08**: whether a named `GrabPass` reaches a
   `CVRBlitter`, which is what decides whether the value can be computed by geometry on the avatar
   (cheap, one blit) or needs a camera rendering into a render texture (a lot more). Nothing else on
   this candidate is worth building until that answer is in: it picks the shape of everything after.
   `Dev/Probes/D2PrefabBuilder.cs`, staged into `Non Corpus Zone` and on the menu.

   A value computed on the GPU reaches C# on a stock client with no contact anywhere, and it does
   it on every client, remote copies included, for zero sync bits. Nothing is transmitted: the
   viewers' copies were out of phase with each other, so each computed its own.

   That makes per-client audio real, and it is what this candidate was opened for. Ship the
   machinery, not the clips.

   **What the proof constrains.** The shader may read only synced avatar state: bone transforms,
   and blendshapes driven by synced parameters. Local time, `_ScreenParams`, frame count and the
   viewer's camera each give a different answer per viewer. So a blit cannot be the source for
   anything other people must agree on, because a blit can see nothing but its own inputs; the
   source has to be a render of avatar geometry, which is the camera route.

   **Two corrections came out of building it**, both recorded in `YAPS5.md`: every render texture
   in the chain has to be linear, and animator parameters are reachable through
   `CVRAnimatorDriver` after all, given a pump clip to make it flush.
5. **A shipped plug's colour changes in VR on Poiyomi 9, and does not on Poiyomi 8.** A user
   report, so it outranks everything else here. The shape is right and only the colour moves.
   Nothing in the shipped YAPS reads the eye, the camera or the screen; it rewrites position,
   normal and tangent in the vertex stage and stops. What Poi 9 adds that Poi 8 does not have in
   the same shader is two more `ForwardBase` passes: an `EarlyZ` depth prepass (`ZWrite On`,
   `ColorMask 0`, drawn first) and an outline pass: where Poi 8 ships both as separate shader
   files you opt into. Both carry a vertex program the patcher patches. A prepass whose deform
   disagrees with the base pass's by any amount depth-tests the plug against its own undeformed
   silhouette, which reads as colour going wrong while the shape stays right. **Unproven**: ask
   the user to turn Early Z and the outline off and reupload, or reproduce it locally against the
   Poiyomi 9.3 in `Fixing The Flexing` with MockHMD.

   **Waiting on a live case, decided 2026-09-07.** Neither route is worth building a repro for
   before 4.5.0 goes out: the reporter is one avatar and the hypothesis needs a headset on a real
   upload. Watch for it in the reports after the release and take the next one that shows it,
   with the Poiyomi version and whether Early Z and the outline are on.
6. **The atlas shipped in 4.5.0. Phases A, B and C are closed.** Pass 1 passed all four steps
   2026-09-03 (`YAPS5.md`). The step-by-step log is in `archive/Closed-2026-09.md`; what stayed
   here is what nobody has worn yet, one count that was reasoned rather than read, and phase D.

   What `yaps_resolve.cginc` does now: channel, then lights, then the atlas, each
   overwriting the last where it answers, `socket.tier` recording which one did, and the
   behind-the-base guard last so it judges whichever answer survived.

   **What a pair sees when only one side has the atlas is answered too.** C2 keeps sockets
   emitting stock DPS ranges byte for byte, so a plug that does not read the atlas still finds
   them by light, and B3 proved that direction in game against a legacy avatar.

   **The one part of C1 still violated** is its own rule that the blend must not depend on
   presence: `YapsAtlasFits` is a camera test, so a target under 552 by 520 drops to the light
   tier. Accepted for the self portrait, and B1 showed every camera that matters (desktop view,
   desktop mirror, VR both eyes) fits and agrees. Revisit only if a divergence turns up between
   two cameras that BOTH fit.

   - **B2's crowded instance is NOT a ship blocker, 2026-09-06.** The platform's population
     makes it hard to arrange, and the half it was written for is arithmetic rather than
     observation. Sockets hash by spatial cell into 4096 cells per level with two homes each, so
     expected first-home collisions are about N squared over 8192: under two at a hundred
     sockets, about five at two hundred, and a first-home clash still reads from the second.
     The platform's real ceiling is nowhere near that: an instance rarely holds 28 people and
     most of them will not be carrying any of this, so a realistic crowd is a couple of dozen
     sockets and about 0.07 expected clashes. Even 28 people fully kitted is 112 sockets and
     under two. The budget is not the risk it was written down as. What the arithmetic does NOT
     cover is throughput: every avatar's socket writers run on the VIEWER's client, so a full
     instance is a hundred-odd sockets times eight quads drawn ahead of the scene every frame.
     Test it if a crowd ever turns up; do not hold a release for it.
   - **Two plugs in one socket, 2026-09-05.** The socket opens to the deepest plug in reach
     rather than the nearest. Untested in game: wants two people and one socket, and the shape
     to watch for is the second plug passing through mesh that did not open.
   - **The self portrait does not bend, by design.** It fails the size gate: the rect is 552 by
     520 real pixels and the portrait's render texture is smaller. Not fixable by scaling, see
     docs/YAPS5.md. The portrait shows whatever the contact channel resolved.

   Two more, added 2026-09-04, because both are code that has never once run against reality and
   neither can be seen from here:

   - **B5, a plug through TWO sockets.** The chain walk went in on the strength of an offline
     compile. Nobody has built the avatar that exercises it, which is why the resolver sat half
     used for a day and why the chord-versus-curve tear was found by reading rather than by
     looking. What to watch for is a torn ring of mesh at the joint, and a shaft that picks one
     socket and ignores the other.
   - **B7, a plug whose shaft bone is rolled differently from the hub it hung off.** The shaft
     descent inherits the authored UP from whichever bone it lands on. Forward does not matter,
     that is measured off the vertices and only seeded by the authored one, but roll is taken
     as given. It reaches the authored bend direction and the wriggle phase, not the bend
     toward a socket, so the shape to watch for is a plug that curves the wrong way at rest and
     bends correctly once engaged.
   - **B8, a camera that draws nothing over the atlas rect.** Moving the writers before the scene
     means the scene covers them, which is what makes the payload invisible and what stopped the
     portrait damage. A camera that renders neither geometry nor sky in that corner would leave
     them showing. Not a world: every ChilloutVR world has a skybox, so the view and the mirrors
     are covered. The shape at risk is a camera with a TRANSPARENT background and no sky, which
     is what the self portrait is, and it is only safe today because it fails the size gate. A
     portrait bigger than the rect would show the payload against nothing.
   - **B6, a multi-material plug seen by SOMEBODY ELSE.** The channel's per-slot driver tasks
     only diverge remotely: the wearer's own view is correct either way, and a second material
     left behind shows as half the mesh following the socket and half sitting still, on the other
     person's screen only. Needs a second client, which means a second person.

   Not proven, only reasoned: a uniform +1 converted and +1 skipped on nearly every avatar, which
   is the shape of a pass added since the baseline rather than per-avatar breakage. The digest
   counts those two rather than naming them, and this run overwrote the digests it would have
   been compared against. One conversion in the editor and a read of the pass list would settle
   it.

   Phase D, retire the contact channel: D1 prove the texture-parser route to the animator. D2 move
   socket shapes, depth and haptics onto it. D3 delete `YapsChannel` and its triggers: DONE 2026-09-11, with 4.5.1. D4 KEEP the
   TPS material import, which is a separate thing from the tag plumbing.

   **WHICH LEVER KILLS WHICH COST, 2026-09-06.** These get conflated, so they are written down
   apart.

   *Show the avatar's OWN depth animations to other players* costs 32 sync bits per socket and
   nothing else. The trigger and the animator layer are local and exist either way; the bits buy
   the room seeing the result. D5 below does not touch it. **D1 deletes it outright**: the reason
   the value has to be sent is that ChilloutVR runs an avatar's triggers on the wearer's machine
   alone, so only one client ever computes it. Every viewer's GPU can compute the same depth from
   the atlas, and the texture parser hands it to that viewer's own animator, so every client
   arrives at the answer independently and there is nothing left to transmit. The toggle stops
   being cheaper and stops existing.

   A second lever, for how many sockets can use the free shader route at all: **one material
   carries one bake and one origin**, see AnotherSocketBaked in YapsNativeBuilder, so the second
   socket on a mesh is pushed onto the animator whatever the transport does. A body mesh usually
   carries several. Letting one material hold SEVERAL bakes and origins, with the socket deform
   looping over blocks, would move most body-mesh sockets onto the shader route. Bigger than it
   sounds: the bake texture grows and the deform gains a loop. Not a transport problem, which is
   why no amount of atlas work reaches it.

   **D5, THE OTHER DIRECTION, raised 2026-09-06.** The atlas carries socket to plug and
   nothing else, so a plug now resolves at range while the socket it entered still finds the plug
   the old way: a tracker light in one of four vertex slots, or the contact channel. The visible
   consequence is a plug that bends while the socket stays shut, and it gets worse the more
   sockets and plugs are in the room, because the light slots are contested.

   The same machinery inverts. A plug writer quad hashes the plug's base and length into cells at
   Background-945 beside the socket writers, one grab still serves both, and the socket's vertex
   shader reads its neighbourhood the way a plug reads its own. Depth is
   (plugLength - distance) / plugLength and both operands fit a payload exactly like a socket's
   position and facing do. Roughly doubles cell occupancy, which the arithmetic above says is
   nowhere near the budget.

   What it CANNOT carry, and this is the part to keep straight: haptics, and the depth parameter
   that drives the author's own animated bulges. Those have to arrive in animator space, because
   a toy mod reads a parameter and not a texture, and the atlas never leaves the GPU. That
   crossing is D1's texture-parser route. Atlas plus D1 is the whole story; atlas alone is two
   thirds of it.

   Two things not to lose while doing it. The marker lights are not overhead to be deleted, they
   are the INTEROP surface: emitting them is how a legacy plug sees a YAPS socket (C2) and
   decoding them is how a YAPS plug sees a legacy socket (B3), both proven in game this week. The
   atlas can be primary for YAPS to YAPS without either of those going anywhere. And the reader's
   RANGE GATE has to stay: a plug already publishes its base and length as a tracker light, so
   nothing new is disclosed by publishing to the atlas, but a socket across the room must not
   start reacting to a plug that never came near it. Range is a decision in the shader, never a
   property of the transport.

   **Cosmetic, carried from the spike:** the payload pass writes colour because the payload is
   colour, so an occupied cell paints a few pixels near the corner of the screen. Count follows
   socket count, not grid, so it does not grow. A user will report it as a rendering bug.
7. **Three things owed in game, none of which this machine can answer.** Grouped because they all
   want a headset or a second person, and because each one gates work that is otherwise finished.

   - **The chain across two bodies.** Own-body answering resolves through the atlas, proven in
     game on 2026-09-07, and the two-body case passed in the editor: the wearer's own socket
     first, the other person's second. What has never run is that same chain with a real second
     player. The record of the spike is in `archive/Closed-2026-09.md`, including why the switch
     defaults off and why a hole has to end the chain.
   - **A socket on a limb, now that self-exclusion is dropped there.** The trade made on
     2026-09-08 is that somebody else's plug can open a hand socket, and so can the wearer's own
     if it rests against it. Both halves want looking at, and the second is the one that will
     read as a bug.
   - **The restored marker ranges.** Soba is taking a toy mod build carrying the CVRGoesBrrr
     merge against a rebuilt avatar. Until that reads back, the change is made on source rather
     than on evidence, and the ranges are baked into real lights, so an existing avatar has to be
     rebuilt before it proves anything.

## The atlas knows whose socket it is, 2026-09-11. BUILT, untested in game

Protocol version 5. Every socket writes its wearer's owner id into a FOURTH atlas pixel per
octant, and a plug compares it with its own. Where both ids are known, whose socket it is is a
fact; the nearest-hip vote runs only where either is zero. The vote was what failed when two
bodies overlap: a partner's socket pressed against the wearer's hips voted as the wearer's and
was refused mid-insertion.

**Where the id comes from.** `CVRParameterStream` type `SeedOwner` (90000) is a CRC32 of the
wearer's user id. The stream's `Mod` with 16777216 keeps its low 24 bits as an exact integer,
because the client's Mod works on the integer when the source is one, and a float holds every
integer below 2^24. It lands in `YAPS/Owner`, a synced float, and a direct blend tree weighted by
that parameter over a clip setting `material._YAPS_Owner` to 1 carries it onto every renderer
whose material has the property: plugs, atlas writers, the readout. `YapsOwner.Wire` does all of
it and is idempotent. The converter calls it after the lighthouse; the toolkit calls it from the
channel build, the socket build and both removers, and it takes itself out when nothing carries
the property any more.

**Two traps, both handled in `Wire`.** The stream is a local component, destroyed on remote
copies, so the parameter has to sync; a remote copy reads 0 until its first sync, and 0 means
unknown. And a stream added from code serialises `referenceType: World`, because only the CCK
inspector switches it to Avatar, and only when it is opened. The client computes SeedOwner for an
Avatar reference alone, so without the explicit set the id would be 0 everywhere, silently.

**The self rule.** Known and different: never refused as own. Known and the same, and the
socket numbered: the plug's own list decides (below). Known and the same, unnumbered: refused
only when INBOARD, nearer the wearer's hip than the plug root is. Unknown: the old vote,
unchanged. `_YAPS_SelfAllow` still opens everything.

**Choosing per plug, protocol 6, same day.** The plug inspector has a **Your own sockets**
checklist. A self choice only ever involves one avatar's own plug and sockets, and the build sees
both, so the choice never crosses the screen: each writer publishes its socket's number on its
avatar, 1 to 15, in the facing pixel's alpha beside the kind, and the plug holds a bit per number
in `_YAPS_SelfSockets`. No new pixel, no sync, no menu rows. `YapsPlug.selfEnter`/`selfRefuse`
store changes against the default (off the hips ticked, on the hips clear, bone found by climbing
to the first humanoid bone), so a socket added later starts at its default. `YapsOwner.ApplySelf`
numbers by hierarchy path and sets every mask; it runs from `YapsSocketBuilder.Build`, `Wire`
and every tick, materials only. Converted plugs have no component and take the default. Writer
materials are now one per kind, tag set and number.

**The rect.** A fourth pixel at 32 columns is 1064 wide, which a mirror capped at 1024 cannot
hold. Columns went to 28 with the rows per level rounded up, which the old floor division got
wrong for any column count not dividing 4096: 932 by 596. It loses targets under 596 tall that
808 by 520 fitted, 960 by 540 among them, and no layout keeps those, since the slots alone
outnumber 960 by 540's pixels.

**Unverified, in the order to check:**
1. Own avatar in game: a temporary slider on `YAPS/Owner` reads non-zero. Zero means the stream
   is not running: the reference type, the CCK version, or a type only the beta client has.
2. A second client: the remote copy's `YAPS/Owner` equals the wearer's, and its plug still leaves
   the wearer's own hip socket alone.
3. Overlap: a partner's socket pressed into the wearer's hips is answered.
4. A mirror capped at 1024 and a 1280 by 720 window both carry the atlas at 932 by 596.

5. A ticked own hand socket, switched on, is entered; an unticked one, switched on, is not; the
   plug's own sockets toggle opens the unticked one.

**Follow-ups, not started.**
- DONE 2026-09-11: SPS plug rules keep their side on conversion. Others rules go to the tag test
  as before, Self rules and hip avoidance ride on the plug's material as override tags, by name,
  and `YapsOwner.ApplySelf` turns them into ticks on every build, so a renumbering follows. Not
  done: the tag test still runs on own sockets, so an own socket the Self rules allow and the
  Others rules refuse stays refused. Letting the ticks replace the tag test for own sockets is a
  shader change, and it would move native plugs' defaults too. Untested in game, and no avatar
  on this machine has an SPS rule with its sides set; the smoke test covers the tick maths.
- A per-plug menu choice. `_YAPS_SelfSockets` is a plain float, so a dropdown could animate it
  between a few sets, at the cost of one synced parameter a plug so every viewer bends alike.
- Props and world sockets carry no id and fall back to the vote. `SeedInstance` would give a
  spawnable its own; nothing needs it while nothing refuses a prop as own.
- Stripping contacts from the transport, which is planned, removes the channel latch further
  down this file, and leaves the owner id as the one self guard that does not rest on geometry.

## Two reports from the field, 2026-09-07

### One cloth per PhysBone, 94 of them on one avatar (issue #7)

An avatar built one PhysBone per hair strand converts to one MagicaCloth component per strand:
94 components, 35 of them a single strand of the same hairstyle. Faithful to the source and the
wrong shape for MagicaCloth2, which simulates any number of independent roots per component.

**Grouping is written and compiles, on the `physbone-grouping` branch, and is deliberately NOT
on `dev`.** It is untested against the avatar that reported it, so it must not reach a release
until it converts that avatar correctly. Chains that would have received identical settings are
folded into one multi-root cloth: same classified kind, same parent on both the bone and the
component's object, same colliders, same numbers. Held out are anchored volumes (measured and
given a collision bone per side), a chain with a PhysBone parameter (the holder name IS the
GrabbyBones parameter), an ignore list (expressed as a root set), and any per-chain curve
(sampled by normalised depth, so length changes what it reads).

**Untested against the avatar that reported it.** The variant to hand is the Quest one, 8 chains,
four of them anchored volumes, none groupable. The reporter has been asked which avatar it is.
Merge to `dev` only once the PC avatar converts and the result is checked in game.

### Freeze rotation axes, VRChat vs Unity (issue #6)

Reported as the axis flags behaving differently after conversion. **Measured, and they do not.**
`Dev/Probes/ConstraintParityProbe.cs` builds the same rig twice, once with each component, and
compares: 2688 configurations across all seven axis masks, rest and offset values, constraint
weight including 0, source weight, a rotated parent, a non-uniformly scaled parent, and one or
two sources. Zero disagreements. A three-link chain agrees too, including built leaf first, where
a solver with no dependency sort would fall behind.

Both solvers keep the driven bone's LIVE local value on a masked axis. The first theory here was
that Unity substitutes `rotationAtRest` and VRChat keeps the animation; that was wrong, and the
probe is what said so rather than more reading.

**One real hazard came out of it and is fixed.** The converter reads the axis flags by name and
its reflection helper falls back to `true`, because a constraint whose flags cannot be read still
has to drive something. So a renamed field would not fail: every constraint would convert as
affecting all three axes and a frozen axis would quietly start moving, which is exactly what the
report describes. Every name resolves on the SDK here, so this is not that reporter's bug unless
their SDK differs; a name that does not resolve now warns instead of being assumed.

**Still open, and not answerable in the editor:** ChilloutVR's constraint order against its own
IK. Needs the reporter's SDK version, which bone, and what the wrong result actually looks like.

## A readout that does not suppress what it is reporting on, 2026-09-09. BUILT

The plug's `_YAPS_Debug` view answers in LENGTH, because `YapsShaderPatcher` edits a host
shader's vertex stage and nothing else, so there is no fragment of ours to paint in. Two
faults, and the second is the serious one. It says one thing at a time, and it STRAIGHTENS
THE PLUG to say it, so the bend and the reason for the bend can never be observed together:
a reading of "nobody resolved it" can only be compared against a memory of the plug bending.
That is exactly the compare-against-memory step that has produced every wrong call here.
Reported from the field as "this is hard to tell, we need a full debug mode".

`YAPS/Debug Overlay`: our own shader end to end, so it paints. A quad beside the plug, six
cells, each a colour rather than a fraction: who resolved the socket, whether it engaged, the
gap as a bar, what the atlas read, whether the atlas is on the camera drawing this view, and
whether the plug asks for the atlas at all. The plug keeps bending normally throughout.

Three things it had to get right, each of which would have made it lie:

**The queue.** Anything drawn into the screen can land in the atlas grab and corrupt the
transport it is reporting on. The whole atlas transaction runs in Background: clear at -946,
writers at -945, grab at -944. The overlay sits at Queue Overlay, thousands of levels later,
so it cannot reach the grab. Do not move it forward.

**The frame.** The deform takes its FRAME from the bone, recovered per vertex, and its SCALE
from the renderer's matrix, and those are two different transforms. The quad hangs off the
bone at the bake's own origin and rotation, and its local scale is corrected back to the
renderer's, or every distance in the readout is wrong by the ratio between them.

**Both builders.** The first version hooked `YapsNativeBuilder.Bake` and would have shipped
covering the toolkit alone: the converter bakes straight from `YapsBaker.Bake` at
`YapsConverter.cs:143` and never calls the native path. Converted avatars are most of them
and are the ones being debugged. `Apply` now takes a root and a flag rather than a `YapsPlug`,
the toolkit passes the component's tickbox and the converter passes a setting, and the
converter seeds the tickbox onto the component `AdoptPlug` writes so a later Build keeps it.
Five of six audit findings last time were one-path-only; this makes six of seven.

Property values are copied by NAME off the patched material rather than from a list kept in
the overlay, and anything the overlay's shader cannot hold is reported as a warning. A
hand-written list rots the first time a property is added to the patcher's block, and the cell
it feeds then reads zero without saying so, which is the one failure a diagnostic must not have.

A mesh built in memory and handed to a MeshFilter draws perfectly in the editor and
serialises to NOTHING. The first build of this appeared in the Scene view and was absent in
game, which reads as the shader failing rather than the mesh never arriving. The quad is an
asset now, the same as the atlas's own two meshes, which had this solved already.

**Rebuilt the same day, onto the plug's own renderer.** The quad beside the plug was a
second resolver, not a window onto the first, and the two disagreed exactly when it
mattered. Reported from the field: socket toggled on then off, plug still bent round where
the socket had been, readout saying nobody. Two things a separate renderer cannot see. The
contact channel and `_YAPS_Enabled` arrive as animated material properties, which land in
the plug renderer's MaterialPropertyBlock and nowhere else. And the plug's frame is recovered
per vertex from its own skinned normal and tangent, where the quad had only a bone matrix
fixed at the bake rotation; a plug already bent has its forward pointing somewhere else, so
its atlas scan reads different cells than the quad's. A readout that can be wrong about
whether anything is happening is worse than the length view, which at least came from
inside the plug's own draw.

So the readout is now four vertices appended to the plug's mesh in their own submesh, with
their own material slot on the same renderer. Same renderer, same block, so the channel and
the toggle are visible. The four vertices copy one plug vertex's bone weights, normal,
tangent and every blendshape delta, the anchor `YapsBaker` now picks (the lowest shaft
vertex wholly on one bone), so they skin to exactly where that vertex skins, and the shader
runs YapsDeform's own recovery on the bake entry for that vertex. The cell for "engaged but
the toggle holds it off" fell out of this for free; it is amber in the second cell.

The mesh is a copy saved beside the bake, and the plug component records the mesh it
replaced and the renderer it sits on. `Restore` runs before every bake in BOTH builders,
because a bake taken over the readout copy counts its four vertices as the plug's and could
pick one as the next anchor; and first thing in `RemovePlug`, because the readout's slot
carries `_YAPS_Bake` and would otherwise be restored as a plug material. The one thing the
mesh copy has to get right is that Unity wants every vertex channel either absent or exactly
vertex-count long, so an empty channel stays empty and a present one grows by four.

## The lighthouse outranked the socket's own toggle, 2026-09-09. FIXED

Reported as "turning the socket off no longer straightens the plug, have we regressed".
It is a real regression and 4.5.0 introduced it, but not where it looked.

The evidence, from the converted controller rather than from reasoning about it. The toggle
is correct end to end: layer `Toggle Titjob` has both states, the On clip writes
`Original Object/YAPS Socket` active 1, and the generated `Toggle Titjob restore.anim`
writes the same path 0, along with all fifteen senders and haptic triggers under it. The
empty-off-state repair did its job. What beats it is `YAPS lighthouse`, layer 73 against the
toggle's 67, Write Defaults off, any-state, always running: `YAPS lighthouse 4` writes that
same path to 1. Unity's higher layer wins, so while the selector points at a socket, that
socket cannot be switched off by anything.

That behaviour predates 4.5.0 and was deliberate, documented, and until 4.5.0 nearly
harmless: a socket forced on offered its marker lights and its contacts, and a plug still
chose its socket from the contact channel, so the wearer's toggle only failed to stop the
lights. From 4.5.0 the socket carries the atlas writer, and the atlas resolves plugs on its
own. A socket held on by the lighthouse now keeps answering every plug in the room, with no
light and no contact needed, which is exactly the "switched it off and it still works"
report. Not a hole in the atlas; the atlas turned an old cosmetic conflict into a real one.

The fix reuses the rule this codebase already had. `YapsSocketRebuilder.Switchable` collects
every `m_IsActive` path some clip can drive, and the rebuild already treats such a path as
menu-owned and leaves it alone. The lighthouse now asks the same question before writing the
socket curve: light it always, switch it on only when nothing else can. Split `Switchable`
into a controller overload so the lighthouse can call it without a `BridgeContext`.

`YapsLighthouse.Build` is shared, so the converter and the native toolkit both get it from
one edit. Verified: the native path calls it at `YapsNativeBuilder.cs:649` and `YapsRemover.cs:172`,
the converter at `YapsSocketRebuilder.cs:420`. All four define combinations compile.

Left open, and it is a different question: there is still no user-facing control for whether
a socket accepts the wearer's own plug. `_YAPS_SocketNoSelfExclude` is decided at conversion
time from whether the wearer's own plug rests within `Length + 0.1` of the socket, which is a
guess made once and never revisitable. A chest socket is out of a crotch plug's reach, so it
allows self and there is no way to say otherwise.

## The contact guard was the wrong trade, 2026-09-09. FIXED

Valkyr reconverted with the detector in and the claws still flickered. The
detector was right and did fire, naming the parameter and the layer, and then
declined to act:

    Warning: 1 control(s) may loop while switched on. "ClawT" ([FX] ClawT),
    also driven by a contact.

The reasoning behind that guard was that a contact still pulses the parameter
the way the pulse idiom wants, so the pair works from the contact and loops
only from the menu, and rewiring one end would break the other. Both halves
were true and the conclusion was still wrong: the menu is the driver that is
actually broken, and a control that switches itself several times a second is
worse than a contact that has to be rewired too.

So the merger rewires regardless, records what it rewired in
`ctx.UnlatchedParameters`, and a new pass rewires the contacts to match.
`HoldUnlatchedContacts` runs after the merge, because only the merger knows
which pairs it touched, and contacts convert a pass earlier. It finds the
enter-1 / enter-0-next-frame pulse the OnEnter branch wrote and turns it into
enter-1 / exit-0: the touch holds the control on and releases it, rather than
tapping it.

Not what VRChat did. It is the closest behaviour that survives the rewire the
menu needed, and it is reported as an approximation rather than a conversion.

**The lesson is the one already in CLAUDE.md.** The guard shipped on reasoning
about what a contact would do, without an avatar wearing one. Two passes now
disagree about a parameter unless something joins them, and nothing local
would have shown it.

## A button became a toggle and the animator kept ping-ponging, 2026-09-08. FIXED

An avatar's claw control switched itself several times a second in game, for as long as the box
was ticked. The layer read correctly in the animator window: two states, one transition each way,
no AnyState. What made it loop is that BOTH transitions carried the same condition, the same
parameter with the same mode. That is the pulse idiom: the value is meant to arrive as a one frame
flick and each flick moves the pair one step. A VRChat menu Button flicked exactly like that. A
ChilloutVR toggle holds the value instead, so the pair hands over, hands back, and never settles.

`UnlatchImpulsePingPongs` looks only at parameters already recorded as Button-derived
(`ctx.ImpulseParameters`, populated by the menu pass and until now never read), finds a pair of
states that point at each other on one shared condition, and sets the transition landing on the
layer's resting state to the opposite condition. The control then behaves like every other toggle.

Two cases are reported and left alone rather than changed. A parameter a contact also drives still
gets its flick from the contact, so the pair works from the touch and loops only from the menu, and
flipping one end would break the other. A pair where neither state is the layer's resting one gives
no way to tell which side is off. Both name the layer and the parameter so the fix is one click away.

Not the AnyState self-restart pass beside it: there is no AnyState here and no state re-entering
itself, so nothing that pass looks at matches. Two loop shapes, two detectors.

The same avatar reported a plug that would not appear (an NSFW gate on the wearer's own menu, not
a fault) and a flaccid control that does nothing (its clip only switches a PhysBone that was left
unsimulated because a VRCScaleConstraint drives a bone in the chain, which the report already says
in full). Neither needed a change.

## A scale constraint is not the loop the guard was built for, 2026-09-08. FIXED

`SkipConstraintDrivenChain` refused any chain carrying a constraint, and the guard's own comment
says why: the NaN is a feedback loop between something that WRITES a rotation every frame and a
solver integrating from its own last state. That is Parent, Position, Rotation, Aim and LookAt. A
scale constraint writes `localScale`, which the solver never touches, so the two share no channel
and there is no loop to close. 2.91.0 widened the check from `VRC*Constraint` to every `IConstraint`,
correctly, and took one type too many with it.

The cost was not just a missing swing. The control that switched the chain still converted into a
menu entry, a parameter and an animator layer, so the avatar came out with a toggle that looks
correct and does nothing, which is the presentation the warning beside it already calls the worst
one. A world-scale rig holding a bone at constant size is the common shape of this.

Scale-only chains now simulate and get an Approximated line saying so, pointing at the one thing
that could still go wrong: MagicaCloth2 measures bone lengths once, at the scale the avatar was
converted at, so a constraint that moves that scale a long way while the chain swings is worth a
look in Play mode. Not yet measured against such an avatar, which is the only thing that settles it.

## The conversion resizes oversized textures now, 2026-09-08. DONE

The measurement existed and nothing ran it: a texture carried at more resolution than its mesh can
show was named on a card and left alone unless somebody pressed a button. `slimTexturesOnConvert`
is ON, and a final pass runs the same `AvatarSlimmer.Find` the card does, sizes and formats only.

Why on by default, when the change reaches the source texture's import settings: the slimmer
already refuses any texture a material OUTSIDE this avatar uses, and names it. The one thing that
does share it is the VRChat copy of the same avatar, which wears the same mesh at the same texel
density, so the size that fits one fits the other.

Stripping renderers no clip can switch on, and the animator tidy, stay on the button. Those are
judgement calls that want the avatar in front of you; a size the mesh's own density proves is not.

The window announces it above the verdict with the megabytes, and carries the undo beside it. That
pairing is the point: something that happens without being asked cannot have its way back in
another window. The record is a file in the output folder, and the saved report is in that same
folder, which is how the button finds it.

Last pass in the pipeline, because every pass above it can still add a renderer or point a material
at a different texture.

## The public package's shape was never compiled, 2026-09-08. FIXED

`compile-check.sh` dropped `Editor/Yaps` for the combinations without the add-on and kept
`Runtime`, which no such user has: the public package prunes both. So the two combinations that
exist to prove the converter stands up without the add-on were compiling against four files the
package does not ship, and a reference from `Editor/Core`, `Editor/Toolkit` or `Editor/UI` into
`AvatarBridge.Yaps` would have passed here and failed on import, which is exactly how 4.1.0 and
4.1.1 shipped a package that would not compile.

Nothing is wrong today: with `Runtime` excluded all four combinations still pass, so this closes a
blind spot rather than a bug. It is the same blind spot the YAPS closure check in `build-package.sh`
exists for, pointing the other way: that one asks whether the add-on can stand without Core, this
asks whether the public build can stand without the add-on.

Consolidation item 7 is the other half and is still open: the closure check is a reader, not a
compiler, and the add-on's own file list has never been compiled as a list.

## The digest could not see a tag, 2026-09-08. FIXED

Run 395 was meant to show what the shared-tag default did to socket and plug
matching, and showed nothing: `tags=` appeared nowhere in a digest, and the
word `shared` appeared in zero of them. A wrong tag is silence in game, which
is exactly the class of failure the digest exists to catch first.

The reason nothing showed is that nothing survives to be shown. The words live
on the VRChat components; the conversion hashes them into a number on a
material and into the atlas, and a hash cannot be turned back into a word. The
digest reads the converted avatar, so by the time it looks there is no word
left anywhere on it.

`YapsBakePrep` already reads them and keeps them: `AuthoredSocketTags`,
`AuthoredAnswers` and `AuthoredRefuses`, cleared per conversion and filled from
the source. The digest now reads those three, and emits a `[yaps tags]` block
counting each word once per list that carries it, socket words and plug
`+`/`-` lists separated. Counted by word, not listed per socket, so a rename
does not churn the whole block.

First run that can see it is 396.

## No plug in the corpus ever refused anything, 2026-09-09. FIXED

The first thing the new `[yaps tags]` block said, on run 396: `refusing=0`,
27 times out of 27. Every socket in the corpus wears the shared tag and
nothing else, no plug names a word, and no plug refuses one. So the exclude
path was walked by `YapsTagProbe` alone, which tests the fold in isolation
and never runs a conversion. Nothing checked that an authored `excludeTags`
reaches `_YAPS_TagExclude` on the material the plug ships with.

`Fixture_TaggedPair` closes it: a socket tagged `fixturehole` on the chest
and a plug on the hips answering `fixturehole` and refusing `fixturebar`,
with the shared tag off at both ends, since with it on the pair matches
regardless and the named word proves nothing.

Built by `FixtureBuilder.RunTaggedOnly`, for the reason `RunStrafeOnly`
exists: the full build re-copies every fixture from a source scene that has
been hand-edited since, so it would diff avatars this has nothing to do with.

The next YAPS-ON run is the first that can read it. A YAPS-OFF run stops in
`YapsBakePrep.Begin` before the tags are read, so the block does not appear
there at all.

## Every YAPS run converted five avatars the exclusion list drops, 2026-09-09. FIXED

Run 397 wrote 83 digests where run 396 wrote 87, and the five missing ones
were exactly the five `[excluded]` names in `Regression/corpus.cfg`: Arlo,
Branwen, Kimmi, Satin Snake, hypsi. All five had fresh timestamps inside
396's window, so a YAPS run was converting them and a default run was not.

`CorpusConfigPath` was `Root + "/corpus.cfg"`, and `Root` carries the mode
suffix: `/Regression` by default, `/Regression/Yaps` with the flag on. Only
one of those has the file. So every YAPS run since the split read no
exclusion list at all and digested five avatars VRCFury cannot bake, whose
diffs describe Fury's failure rather than the tool's, which is the whole
reason the list exists.

It degraded silently because a missing file reads as an empty list.
`QuickSet()` throws on the same miss and names the path it wanted, so the
quick set has been failing loudly in YAPS mode the whole time, and nobody
connected the two halves.

`CorpusConfigPath` reads from the repo now, not from `Root`: which scenes
are avatars is a fact about the project and does not change with the mode.
`Repo` is the shared getter both use.

The accepted YAPS baseline holds five digests that should not exist. Harmless
to leave, since they simply stop being written; the next YAPS run drops to 83
and those five go stale in place. Delete them from `Regression/Yaps/Baseline`
and `Current` whenever it is convenient.

## Loose ends, small but real

### The settings are per USER, not per project. FIXED 2026-09-08

The window persists its settings as JSON in `EditorPrefs`, which Unity keys per user and per
editor install and NOT per project. The no-add-on branch wrote `convertYapsSystems = false` and
`stripSpsSystems = true` straight into that object, so opening the window once in a project
without the add-on set every project on the machine to Remove. The next avatar converted anywhere
had its penetration stripped and nothing rebuilt, and since the flags were saved rather than
displayed, the window could come back up reading Convert while the stored answer said otherwise.

Harmless until the 4.5.0 split, which is what made a machine hold both kinds of project at once.

`BridgeConverter` has always collapsed the choice for the run itself, and that is the right place:
it touches the copy being converted and nothing that outlives it. The window writes nothing now.
The one thing the write got right was the OSC hint below it, which asks whether penetration is set
to Convert; without the add-on the answer is no whatever the setting says, so that is a define now
rather than a value.

Not fixable in reverse: a project already carrying the stored Remove keeps it, because a
deliberate Remove and a poisoned one are the same two booleans. Anyone whose penetration choice
reads Remove after updating should set it back.

### The refusal cell read red on every plug that resolved nothing, 2026-09-09. FIXED same day

Shipped and caught in the first screenshot. `YapsResolveSocket` only copied the chain onto the
socket when `chain.count > 0`, so a scan that accepted nothing left `socket.chain` zeroed, and
`refusedD` of zero is a refusal at the origin: the cell went red exactly when it was being read,
which is when a plug resolves nobody.

The chain is carried out of the atlas block unconditionally now. `count` still gates every
consumer (`yaps_deform.cginc:746` is the only one), so a scan that took nothing behaves as before.
The cell also treats a non-positive `refusedD` as nothing refused, for the case where the atlas
block never runs at all.

Worth remembering as a class rather than a fix: the comment at `yaps_resolve.cginc:395` already
said "zero would read as a refusal at the origin". The sentinel was documented and the diagnostic
still walked into it, because it read the field from a struct that had never been through the
initialiser.

### The readout could not see the bones, 2026-09-09. BUILT

Six cells all reported the RESOLVE. Faced with a plug sitting bent while the strip read nobody and
not engaged, the readout could say the deform was not doing it and nothing more, and the search
then went to the animator and the cloth by hand.

The cell that was asked for cannot exist on its own. Every quantity a vertex shader can take from
ONE point is in world space, because Unity skins into world space and no C#-readable matrix admits
it, so the anchor's live frame against its baked frame moves when the avatar turns round. There is
no reference to subtract it against.

Two points a shaft apart do not have that problem, so the readout grew a pair of markers instead.
The mesh now carries three quads rather than one: the strip and a white marker weighted to the base
anchor, drawn where the tip WOULD be from the recovered frame, and a magenta marker weighted to a
tip vertex, drawn where it actually arrives. Together means the bones are at bake pose whatever
else is happening; apart means cloth, an animation or a constraint is moving them. `TipVertex` is
picked by the baker with the anchor's own test run the other way up, and falls back to the anchor
on a one-bone shaft, where the markers then correctly never separate.

The strip went to twelve cells in two rows at the same time, the top row unchanged so old
screenshots still read. The new six are frame recovery (a plain-mesh plug is grey and fine, red is
a skinned plug whose recovery refused and whose bend is around the wrong axis), the anchor vertex
being inside the bake, whether the bake row read anything, the own-body rule, the atlas chain
length, and a socket refused by tags while nearer than whatever answered.

Only the strip resolves; the markers skip `YapsResolveSocket` entirely, because 27 atlas cells per
vertex for a result that is thrown away is a frame rate bug wearing a diagnostic's clothes. One
submesh for all three quads, so the readout still costs one material slot.

`_YAPS_SocketDepth` was going to be a cell and is not: it lives in `yaps_socket.cginc`, which a
plug's include chain does not carry, and the shader would not compile. The own-body rule took the
slot.

### A look-swap toggle left the plug rigid, 2026-09-09. FIXED

`YapsSwapFollow.Follow` repairs one swap: the clip that assigns the material the bake was taken
FROM back into the slot the patched copy went into. Everything else an animation can put in that
slot was left alone, so a second skin, a glow version, an alternate colour, anything the author
toggles for looks, arrived carrying no deform. The plug went rigid for as long as that toggle was
on, and nothing said so anywhere: the editor slot still holds the baked copy, so the window, the
scanner and the report all read baked.

Found wearing an avatar whose look toggle swaps the plug between its own material and a locked
Poiyomi copy out of an `OptimizedShaders` folder. The readout on the plug said nobody and not
engaged while the plug sat bent, which is what sent the search past the deform in the first place.

`FollowVariants` reads every runnable clip for object-reference keys on the same renderer and
slot, takes the distinct materials that are not already ours, patches each the way the worn one
was patched (Simple Lit fallback, DPS straight to Simple Lit, legacy deform switched off), applies
the SAME bake result to it, and repoints the clip keys at the copy. The bake belongs to the mesh,
not the material, so every variant carries identical vertex data.

Wired into both builders, the toolkit off `RunnableClips` and the converter off the merged
controller's own clones. A variant whose shader refuses is named in a warning rather than skipped
quietly.

Not done: the scanner does not warn about this on an avatar baked before 4.5.0. The material is
only reachable through a clip, so a scan of the avatar as it stands cannot see the gap; re-baking
is what fixes those and the README says so.

### Three found in the 2026-09-07 cleanup pass

Not bugs anyone can hit, but each is a promise the repo half makes.

1. **RESOLVED 2026-09-07.** The three pictures were the 2.6.2 window and two orphans nothing
   linked; all three are gone. `docs/images/window-450.png` replaces them, taken on 4.5.0 with the
   rebuilt UI Toolkit window and a generically named avatar.

2. **RESOLVED 2026-09-07.** `unmapMisplacedJaw` is gone as a setting: the unmapping is always
   on, and `JawUnmapper` carries the reason. Nothing implied a choice that was never offered.

3. **RESOLVED 2026-09-07.** `.gitattributes` added: `* text=auto`, Unity YAML and shader sources
   marked text, images and `.unitypackage` marked binary. The per-file LF warning is gone.


### Four found wearing the avatar, 2026-08-25/26

A whole avatar baked as one plug turned up four separate faults. Recorded here
because they were found in a session, not in a report, and the queue is the only thing that
outlives scrollback.

1. **The bake made a new material every time: FIXED 2026-08-26.** Both material sites asked
   `AssetDatabase.GenerateUniqueAssetPath`, so a plug removed and baked again left `Fur_YAPS_`,
   `Fur_YAPS_ 1`, `Fur_YAPS_ 2` behind it, one full material per click, in a user's project.
   `YapsBaker.Generated` now derives a path that does not move and re-derives the values from the
   source onto whatever is already there. The file keeps its GUID, so anything already pointing at
   it stays pointed at the right thing, and what was there is never trusted, only its identity,
   so an asset left by an older version cannot carry stale settings forward. One helper serves
   both sites; the primary and the mirrored slots had drifted into two different conventions.

   **The BAKE TEXTURE was the same bug and was missed: FIXED 2026-09-03.** Only the material
   sites were changed in August. The 10 MB bake asset still asked for a unique path, so a
   reconvert left `YAPS Body bake` and `YAPS Body bake 1` side by side, and the name it was
   carefully avoiding belonged to the same plug from the previous run. Named for the renderer and
   the plug now, and the existing one is deleted first. Ceiling: two plugs on one renderer whose
   roots share a name AND a parent name would still collide.

2. **Additional meshes under the armature do not bend: FIXED 2026-08-26, UNTESTED IN UNITY.**
   A plug whose root bone is the Armature patched ONE renderer's materials; every other skinned
   mesh weighted to the same bones kept its own shader and stayed rigid, which is the seam the
   multi-material work closes, one level up. `MirrorToRenderers` now bakes each of them, and the
   bake is per mesh because the texture is indexed by mesh-global vertex id: what they share is
   the primary's FRAME and LENGTH, via a new `shareFrameWith` on `YapsBaker.Bake`, so they bend
   as one object rather than each measuring its own idea of where the shaft is. Scoped to the
   `CVRAvatar`, not the scene root, or two avatars under one container lend each other meshes.
   Skipped entirely when the author names a material slot: that says "this mesh, this slot".

   **`BakedSlot` gained a `renderer`**, because a plug spanning meshes has a slot 0 on each of
   them. Matching on the number alone would have handed one mesh's material to another on Remove:
the "IT BROKE, my fur!!!" failure of 2026-08-25 exactly, one level up.

   *Compile-checked only.* `check-defines.sh` links the project's compiled Runtime assembly, so
   a Runtime change cannot be validated there: the documented blind spot. Needs a real Unity
   compile and a bake on an avatar with a second weighted mesh.

3. **Poiyomi's auto-lock: ANSWERED 2026-08-26, and the pink fix had already closed it.**
   Read out of `ShaderOptimizer.SetLockedForAllMaterials` rather than guessed. The sweep takes
   every material whose shader uses the optimizer and is not already locked, and its test for
   "already locked" is `shader.name.StartsWith("Hidden/Locked/")`: the exact prefix the pink fix
   gave it. So auto-lock-on-upload skips these materials, and has been skipping them since that fix.

   Worth knowing what it would have done: locking resolves properties to constants, and the YAPS
   properties are the ones animation drives (`_YAPS_Enabled`, `_YAPS_BakeScale`, the knobs). A
   locked YAPS material would have frozen at whatever it happened to hold: a plug that never
   toggles and never resizes, in game only.

   **One gap closed with it.** `IsShaderUsingThryOptimizer` keys on the `ThryShaderOptimizerLockButton`
   attribute, while `PatchedName` keyed only on `shader_is_using_thry_editor`. A shader carrying
   the lock button without the editor marker would have been named plainly and swept in. Both
   markers now.

   **Still open, and left alone on purpose:** an explicit "Unlock all materials" grabs these too,
   and tries to restore a `TAG_ORIGINAL_SHADER` that was never written. The result is a broken material,
   recovered by re-baking. Guarding it means writing Thry's own lock records, which is
   impersonating another tool's bookkeeping to survive a button the user deliberately pressed.

4. **Parallel paths: AUDITED 2026-08-26, two closed and one recorded.**

   *Window vs inspector, CLOSED.* Three doors bake a single plug and only one went through
   `BakeAndRefreshMenu`. The window's row button and its make-this-a-plug flow called the bare
   `Bake`, so they left the contact channel holding a previous build's frames and the menu
   animator unrefreshed: the same divergence as yesterday's, in two more places. Both now go
   through the same door as the inspector. `BuildAll` keeps the bare `Bake` deliberately: it does
   the menu and the channel once, for the whole avatar, which is the point of a batch.

   *Remove vs Sweep, CLOSED.* Both clear the channel and refresh the menu animator. The rest of
   the difference is real: Remove undoes one plug, Sweep collects orphans. They are not two doors
   to one job.

   *Native builder vs converter. The slot half is FIXED 2026-09-03, the renderer half is OPEN.*
   **The converter has its own bake path** (`YapsConverter`) and used to patch ONE material slot
   on ONE renderer, so the multi-material fix of 2026-08-25 and the multi-renderer fix of
   2026-08-26 reached the native toolkit only and a CONVERTED avatar tore along the seam.

   The slot half turned up in the wild on 2026-09-03: a plug whose tip was modelled on a second
   material bent along its shaft and left the tip hanging in the air, and nothing in the report
   said so, because `MaterialSlotOf` counted plug vertices per submesh and then returned the
   winner. `MaterialSlotsOf` now returns every submesh that carries plug vertices, biggest first;
   `PatchPlugSlot` patches each one off the same bake; the first that takes it is the slot the
   plug record and the authoring component use, and any slot that refuses says why in the report.

   The CHANNEL half of the same gap closed on 2026-09-04: the static settings reached every baked
   material, but the CVRMaterialDriver tasks that carry the live socket vectors were built against
   `plug.MaterialSlot` alone, so a two-material plug had half its mesh following the socket and
   half sitting on whatever the bake left, and only remotely, only in game. `plug.MaterialSlots`
   now records the slots beside the materials, and the channel builds a task and a driver layer
   per slot. `PlugMaterials` also stopped sweeping the renderer for anything with a bake, which
   put one plug's channel extents into another plug's material when two shared a mesh.

   Still open: **one renderer**. A plug split across two meshes gets one of them. The mirroring
   takes a `YapsPlug` and the converter holds a VRCFury plug, so closing it is a refactor to pass
   values rather than the component. Low impact in practice, and it changes conversion output, so
   it wants a corpus run to land.

4. **Parallel paths that disagree.** Two doors to the same job diverged on the same day: the
   window's Build wired the contact channel and the inspector's Bake did not, and Remove cleared
   channels where Sweep did the leftovers. Both were fixed one at a time. That is a class, not
   two bugs: every pair of entry points into one operation wants auditing against each other:
   window Build vs inspector Bake, Remove vs Sweep, native builder vs converter.

### A stale material is invisible, and it wasted three readings

2026-08-26. Three times in one afternoon a test result was wrong because the material was still
running the previous shader, and nothing said so. The tell each time was a debug view answering
a question the shader it was running had never been asked.

**DONE 2026-08-26, in the two places a person actually looks.** The material's own YAPS panel
gets a warning under the banner, because that is where somebody reads a value and believes it:
which is exactly how all three readings were lost. And the Setup window's row goes amber with
"its shader is older than the toolkit: Build refreshes it", because there the fix is one click
away and the row already had a mechanism for saying so.

Still open below: the project-wide sweep. A warning only reaches a material somebody happens to
select or an avatar somebody happens to scan, and a prop prefab is neither.

### Nothing revisits a prop, so a shader fix never reaches it

Found 2026-08-26, after the two-socket prop fix. A patched shader's name hashes the emitted
source, so `IsStale` knows when a material has fallen behind and both bake paths re-patch it:
but only when something bakes. An avatar gets baked because its scene is open and Build is
pressed. **A prop is a prefab sitting in the project, and nothing ever opens it**, so every
shader-level fix reaches everyone except the props, and the only cure today is to make a new one.

**DONE 2026-09-08.** `YapsShaderPatcher.SweepProject` walks every material in the project, keeps
the ones carrying `_YAPS_Bake`, asks `IsStale` and refreshes what has fallen behind; the Props
card carries the button and says to upload the prop again afterwards, since the copy on the
platform is not the copy in the project. The property is the test rather than the shader's name,
so a material patched by an older version is still found. The new shader is written BESIDE the one
it replaces, so a prop's shader stays with the prop instead of moving into this tool's output
folder where a later cleanup would take it. A material whose source shader has left the project
cannot be rebuilt and is counted out loud rather than skipped quietly.

Not yet run against a project holding a stale prop, which is the only thing that proves it.

- **External audit 2026-08-25, the five deferred findings.** An audit by another agent
  (`AUDIT-external-2026-08-25.md`, kept in the repo). Nine of twenty were verified in source and
  fixed the same day; two were wrong (F1's consequence, F17's premise); these five are real,
  deferred on purpose, and each changes behaviour, so each wants evidence before it moves:
  - **F3, strafe folding: FIXED 2026-08-25, fixture first.** `DirectionOf` folded east into west
    and the replacement wrote one clip to both sides, so an author's distinct left-strafe clip was
    lost. Checked against the CCK's own `AvatarAnimator.controller` before touching anything: it
    has separate children at `x: -0.5` and `x: +0.5` and points BOTH at clip `004085e0…`, so CVR
    ships mirrored strafe, but the slots are real and can hold different clips.

    **The corpus could not judge it, so the corpus was given the shape it lacked.**
    `Fixture_AsymmetricStrafe` (built by `FixtureBuilder.RunStrafeOnly`, so the two existing
    fixtures are not rebuilt from a source scene that has been hand-edited since) carries a
    velocity tree with a different clip on each side of every sideways direction. Run against the
    unfixed tool it lost three clips, `(1,0)` played `Fix_StrafeL`, and both diagonals the same,
    which is the point of building it before the fix rather than after.

    `Slot` still describes the direction PAIR, because CVR's slot set is pair-shaped; a `Side`
    now rides beside it, picks are keyed `(Slot, Side)`, and replacement is child-driven so each
    CCK position asks for its own side. A source with one clip for both sides still fills both,
    by falling back to the opposite side, which is what every symmetric avatar relies on.
    After: `(-1,0)=>Fix_StrafeL (1,0)=>Fix_StrafeR`, both diagonals likewise.
  - **F11, slider hole-drop degrades to hole-filling. FIXED 2026-09-08.** `kept.Count >= 2` sent a
    slider tree with one surviving child to the generic filler, which inserted the placeholder clip
    the report text beside it promised sliders never get.

    Dropping every hole instead is not the fix and was the trap here: a 1D tree holding ONE child
    plays it at full weight wherever the slider sits, so a slider whose other clips had gone
    missing would have come out with the survivor permanently on. The threshold is what carries a
    slider's meaning and one child has no other end to travel to. So one placeholder is kept, at
    the far end from the survivor, and the rest still go: the slider fades the one real clip in
    across its whole travel instead of standing it on. The report says so rather than describing a
    rule the code no longer follows.
  - **F12, gesture-hand promotion by substring. FIXED 2026-09-08.** `layerName.Contains("left")` on
    a lowercased name: "Copyright pose" contains "right", so the whole layer went into CVR's
    RightHand slot and whatever it did to the rest of the body went with it.

    What the layer READS decides it now: a gesture layer driven by GestureLeft or GestureRight is
    that hand, read off blend tree parameters and transition conditions, and a layer reading both
    or neither is not promoted at all. The mask still wins where there is one. The NAME is last and
    no longer a substring: the word after something that is not a letter, or the capital in the
    middle of a name, which is the pair of spellings a boundary alone cannot cover, since there is
    no boundary inside "GestureRight" and lowercasing flattens the hump that would have shown one.
    Checked against both failures and the ordinary spellings: "Copyright pose" and "COPYRIGHT POSE"
    match neither hand, "GestureRight", "Right Hand", "hand_right" and "Gesture (Right)" all match
    right, "cleft palate" matches neither.
  - **F18, owner-rule shortcut skips the humanoid guard. FIXED 2026-09-08.** `FindPlugRenderer`
    returned the parent's SkinnedMeshRenderer before `chainLevel` was ever set, so the refusal
    below it asked `HumanoidBoneName(null)`, null is not a bone, and the "your chain is the body"
    check never ran on that path. Silent bypass, not a crash: a component put on the object
    carrying the body mesh baked the whole avatar as one plug and reported success.

    A new test was the wrong shape here, and bone counts or head-and-foot weights would have
    refused legitimate plugs, including the avatar deliberately built as a single mesh. The
    existing guard is right; it was only reached with nothing to judge. So the shortcut sets
    `chainLevel` to the object the component sits on before returning, and the guard reads it: a
    dedicated plug mesh object is not a humanoid bone and passes, and a body mesh sitting on the
    skeleton is exactly what the guard was written to refuse.

    Unchanged for legitimate plugs by construction rather than by measurement, so the corpus is
    still owed: a plug object is not in `skin.bones` and carries none beneath it, so the climb
    from `plugRoot` lands where it did before.
  - **F19/F20, the shader surface.** The patcher's regexes are unanchored and comment-blind, and
    the socket decoder admits a coloured light whose alpha is zero (`&& colour.a > 0`), which also
    defeats the self-exclusion test that consumes its verdict. Held back because a shader change
    cannot be judged from this machine; see the body-mesh section for what that cost on 2026-08-24.

- **A rebuild refreshes the shader now, not just the bake: FIXED 2026-08-24.** Every shader-level
  fix used to reach nobody who had already converted. `BakeSocket` and `Bake` took a refresh
  branch whenever the material carried `_YAPS_Bake` and never called `Patch` again, and the patch
  was named `Hash(sourcePath + Revision + unit.Count)`: a hand-written `Revision` constant that
  did not move when the emitted code did. Found by adding a property and watching it never arrive:
  the material's shader had nine copies of a property added that morning and none of one added
  that evening, because the morning's landed on a FIRST patch and the evening's needed a second.
  The constant was forgotten twice in one day, which is the argument against constants like it.
  Now the name hashes the emitted source itself (`EmittedVersion`), so it moves exactly when the
  code moves, and both bake paths ask `IsStale` and re-patch when it has fallen behind. A material
  keeps its values across a shader swap, and a property the old code never had arrives at its
  declared default, which the bake then sets.

  **The original is recovered from the PATCH, not from the component.** The first version asked
  `socket.bakedFrom`, which only the tool's own bake ever fills in: a CONVERTED avatar adopts its
  components and leaves it null, so the check would have done nothing for the majority of avatars
  and looked like it worked. `_YAPS_SourceShader` already carried the source shader's name in a
  hidden property's description, so `OriginalShaderOf` reads it back and `Refresh` re-patches
  through a stand-in material. Verified in the editor: a socket material's shader moved from
  `56f6502ef13e` to `253145a5a99b` on a rebuild and gained the property it had been missing.

- **Corpus classes: CLOSED 2026-08-22**: Fixture_DeformSocket and Fixture_HeadTransplant are in
  the corpus and its baseline; the transplant fixture came out on the real Head with zero errors.
- **Pointer capping: CLOSED 2026-08-24, declined with the measurement.** Two questions, both
  answered without a run. *Which families does anything read?* A census over the 93 corpus files
  carrying contact receivers, senders split from receivers: `TPS_Orf_Root`/`SPSLL_Socket_Root`
  heard in 4 files, `SPSLL_Socket_Hole` 3, `Ring` 2, twins alongside, and the front pair,
  `TPS_Orf_Norm`/`SPSLL_Socket_Front`, heard by NOTHING. That looked like four dead pointers a
  socket until `YapsPropBuilder.FrontTypes` turned out to be exactly that pair: it is the prop
  channel's FX/FY/FZ front axis. No corpus avatar hears it because no corpus avatar carries a
  prop, and the corpus enumerates scenes while props are spawnables. This tool's own system is the
  consumer; capping the family would silently cost props their axis.
  *Should the COUNT be capped like marker lights?* No, the limits are not alike. Lights are four
  slots a MESH and a fifth evicts the lowest range, which is why the lighthouse had to exist.
  Pointers are 512 overlapping PAIRS instance-wide, and a pointer costs nothing until it is
  inside a receiver: 174 idle pointers standing in a room spend none of the budget. A count cap
  would break sockets in the common case to save a resource nobody is spending. If pair
  exhaustion ever appears in the wild, the lever is the overlap, not the socket's description of
  itself.
- **DPS range offset, LIFTED 2026-09-08**: the +0.003 offset made YAPS sockets and plugs
  invisible to every mod decoding at 0.001, sound mods included: NAK's PlapPlapForAll needs
  `RoundToInt(Repeat(range*500+500,50)+200)` to hit 205/210/225/245, and the 0.4130/0.4230/0.4530/
  0.4930 all landed on x.5 and read Invalid. No range separates a sound mod from a toy mod, so it
  was one choice for both. The condition set on 2026-08-28 was "the PR merges AND users can take
  it"; CVRGoesBrrr's merge is in the latest build, so the ranges are now VRCFury's exact
  0.4106/0.4206/0.4506/0.4906. The bystander case the offset answered is what the merge closes,
  by bounding reach with the plug's stated length rather than a guess from the first renderer.
  `OwnPlugRestsOn` widened from an exact match to the protocol's own 0.001 in the same commit, so
  a wearer's plain DPS plug counts toward self-exclusion the way a YAPS one does.
  **NOT YET TESTED IN GAME.** Soba is taking the merged mod build against a rebuilt avatar; until
  that reads back, this is a change made on source, not on evidence. Anyone on a toy mod build
  from before mid-2026 keeps the old across-a-room reach, which is a property of the mod and not
  of the avatar.
- **Consolidation remainder**: items after 5 in `archive/Consolidation.md`'s order, minus 6,
  which was skipped on purpose.

---

## The preview tells the truth about the game
*Status: partial. The honest window shipped in 4.3.0; the game-accurate resolver is unstarted.*

The setup window's preview bends a plug toward a socket by reading transforms. The GAME needs the
socket's pointers, marker lights and receivers switched on. So a socket can preview perfectly and
do nothing in game, and it did: pointers visible in CVR's own debug view, no bend, and the tool
saying the socket was fine throughout.

4.3.0 makes the window HONEST: each socket row says which of the three is dark and what it
costs. That closes the lie. It does not close the gap.

The gap worth closing is a preview that resolves a socket the way the game does: pointer and
trigger overlap by geometry, the enabled and active state of both, the depth it would publish,
the parameters it would drive. Then "it previews" and "it works" are the same sentence, and this
entire class of report, works in editor, dead in game, stops existing.

Wants measuring first: how much of CVR's contact resolution has to be reproduced before the
answer is trustworthy. A preview that is right most of the time is worse than one that is
honest about being a preview.

**One member of this class is FIXED 2026-08-25: the squish.** A plug worn in game was
compressed hard; the editor showed it correct, and scaling the plug made the mismatch worse.
`_YAPS_BakeScale` is written as `1` at Bake, meaning "the size it was baked at", but
`MirrorBoneScale` copied the bone's `m_LocalScale` curve across as an ABSOLUTE number. The two
agree only for a bone that happened to sit at exactly 1 when it was baked; on any other rig the
bone's scale was applied a second time, on top of the skinning that had already applied it. The
editor hid it because no animator runs there, so the material's static `1` stood. Mirrored as a
ratio to the bake pose now, tangents divided with the values. The same change gives each bone its
own along-axis, which a child further down the chain never shared with its root.

**The scaled-bone check is in: `Dev/Probes/BakeScaleCheck.cs`.** Every plug in the corpus sits at
scale 1, the one value where the old code was right, so 87 avatars passed green for weeks while
this was live. Not a corpus scene: the digest records no curve values, so no baseline would have
moved even with a scaled bone in it. Three cases asserted directly instead, in seconds, with no
avatar and no baseline: a bone baked at 0.4 that reads 1 at the bake pose and 3 at the top of its
slider, the halfway point that only comes out right if the tangents were divided too, a bone at 1
that must still pass through untouched, and a turned child whose length axis is not its root's.

Still open in the same class, because a ratio only fixes the reference: a plug whose size layer
holds a different value in game than the scene pose does in the editor is still two different
plugs.

---

## YAPS 5: the plug follows a path, not a point
*Status: planned, unstarted.*

*The other half of YAPS 5, how a plug FINDS a socket, lives in `YAPS5.md`, which gathers every
transport, every measured limit, and the order they get built in. This section is what the plug
does once found.*

Today `yaps_resolve.cginc` holds exactly one socket: one position, one forward, one
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
at all. What breaks is the socket's *shapes*: a ChilloutVR trigger writes from whichever
sender touched it last, so two plugs fight over the depth instead of the deepest winning.
Socket-side, no extra sync, and it can ship on its own.

**What the platform gives and charges for.** Unity hands a mesh four vertex-light slots and a
socket takes two, but the third slot is spoken for by the tracker light of whatever enters the
socket, and that tracker lives on a prop or on the other person, so it can never be counted at
build time. **The light path therefore carries exactly ONE socket, not two.** Two lit sockets
plus a tracker is five lights for four slots, and Unity fills by range, so the casualty is
always the lowest range in the protocol: the hole root at 0.4130, behind a ring root at 0.4230
and two fronts at 0.4530, with the tracker on top at 0.4930. An avatar over the budget loses its
holes and keeps its rings, which is exactly how this reached us. A portal pair costs two sockets
and so cannot ride the light path at all; it needs contacts or a script.

The contact channel is where bits get expensive: eight values per socket, 96–288 bits per plug
by tier, doubled by a second socket. Give socket two its own lower tier (engagement and
position, orientation dropped first).

Socket depth stays one parameter per socket, local by default and synced at the wearer's
choice. That is deliberate: sharing a slot
between sockets assumes one is engaged at a time, which is exactly the assumption
multi-socket and portal exist to break.

**Order to build it in:** deepest-plug-wins first (small, self-contained), then the socket
list with socket two on contacts (the light path carries one socket, see above), then portal and
duplicate as ranges on top. The converter repointing an author's reactions onto YAPS's own
depth, once last on this list, shipped in 4.3.0 with the rebuild.

---

## The converter: prove it before the instance does
*Status: planned, unstarted.*

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

**An upload preflight.** One list, run before uploading: these diagnostics, the CCK's own
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
*Status: planned, unstarted.*

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
| `LocIdle / LocWalking* / LocRunning*` | already done, by LocomotionGrafter | - |

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
*Status: planned, unstarted.*

Ten thousand lines, the most fragile thing in the package, and the place where a bug is
hardest to see because nothing throws: the avatar simply behaves slightly wrongly.

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
*Status: reference: measured facts, kept for design work; the distilled limits are in `YAPS5.md`.*

Added 2026-08-17, after a user reported their controllers vibrating whenever they came within
two metres of a converted avatar. Everything here was learned from source, and it changes what a
socket is allowed to be.

**A marker light is the only thing an avatar does that reaches into someone else's.** Its range
is a message: every decoder reads `range % 0.1` and matches 0.01 hole, 0.02 ring, 0.05 front,
0.09 plug tip, and anything on the platform may listen. Raliv's shader matches within 0.005; a
toy mod reading the same protocol in C# matches within 0.001. VRCFury authors +0.0006, inside
both, which is why a stock converted avatar drove a stranger's toy across a room. YAPS authored
+0.003 from 2026-08-23, which DPS read and that mod did not; that lifted on 2026-09-08, see the
work queue entry.

**Toy integration cannot be made safe from the socket side, and that is not a bug here.** The mod
computes reach as `1 - distance / giver.Length`, and estimates `Length` from whichever renderer
sits first under the avatar root rather than reading the length DPS states in the tip light's
intensity. A socket is only ever the target of somebody else's number. So:

- **plug side, one day: yes.** A plug can declare its own length honestly, by giving the tracker
  light a sibling whose mesh states the measured length. The mod climbs to the first renderer
  under the light, so an inactive one on the marker object is enough, and then it engages within
  a plug length rather than a room. That is a switch that risks only the wearer.
- **socket side: never.** Any "let toy mods read my sockets" control hands strangers the right
  to decide how far away they can reach you.

[ddakebono/CVRGoesBrrr#2](https://github.com/ddakebono/CVRGoesBrrr/pull/2) bounds the estimate
with the stated length, and it is in the mod's latest build, which is what allowed the offset to
lift on 2026-09-08. Both notes above still hold for the plug side: declaring length honestly is
the wearer's own risk, and a "let toy mods read my sockets" control still hands strangers the
decision, so there is none. Builds of the mod from before the merge stay broken either way.

**A tempting idea that does not work: shrinking the ranges.** Since only `range % 0.1` is read,
0.0106 decodes exactly like 0.4106 and reaches a fortieth as far. It would ease vertex-slot
pressure, but Unity ranks the four per-object light slots BY RANGE, so a tiny light is the
first evicted by every stock DPS avatar in the room. It would work alone and fail in company,
which is the opposite of what a compatibility feature needs. Worth keeping only as a "YAPS talks
to YAPS and nothing else" mode, where this decoder sets the rules.

---

## Two walls the transports hit, and whether a shader goes round them
*Status: the light-slot fix landed, and the authority-gate wall is DISPROVEN in game 2026-08-22: cross-avatar prop writes work, see `YAPS5.md`. The 512-pair cap stands.*

Read out of the client 2026-08-22 while costing a contact-based replacement for the marker-light
channel. Both of these are platform behaviour, not this tool's code, and both bound what any redesign can
achieve.

**A prop can only be driven by your own avatar.** Every contact write into a `CVRSpawnable` value
goes through `TriggerToContact.HasProbableAuthorityToApplySync`. It allows a sender that is
another prop (if either is synced by you), a sender that is *your own* avatar, or the world. There
is **no branch for another player's avatar**, so a remote sender falls through and returns false
and the value is silently never written. This is very likely a client bug rather than policy: the
prop branch falls back to `IsSyncedByMe()`, and that fallback is simply missing for avatar
senders, so even the prop's own syncer is refused. It predicts the split chased here:
avatar-to-avatar works (the gate only runs for spawnable triggers), old DPS props work (lights,
never this path), YAPS props work on your own sockets and never on someone else's. **Test before
building anything around it, and report it upstream if it holds.**

**512 overlapping contact pairs, instance-wide.** `CollectPairsJob` caps `pairs` at 512 and re-adds
*previous* pairs first, so an established interaction is sticky and cannot be evicted, but in a
saturated instance a new one may never register: "works once I am in it, will not start in a
crowd". The broadphase itself is brute force over every sender for every receiver, but Burst and
parallel with a squared-distance test, so it is sub-millisecond and not the constraint. Rejections
on tags, `contentType` and owner happen *before* a pair is written, so **tags are the lever on the
512, not volume count.**

### So: is there shader magic?

Two routes, and the cheap one is much more interesting than the famous one.

**The light colour is free, and nobody is using it.** A vertex light hands the shader
`unity_LightColor` alongside its position and range. These markers are black with non-zero intensity
(zero intensity drops a light from the per-object list entirely), so three channels per light are
sitting unused. If a root light's colour carried the socket's axis, a YAPS-native socket would
need **one** light instead of two, and with the tracker holding a slot, that is three sockets in
the budget instead of one. Legacy plugs still need the root+front pair, so this is a YAPS-native
mode alongside the compatibility pair, not a replacement. Cheap to spike, and the open questions
are small: whether CVR's asset filter clamps light colour or intensity on avatars, and how much
precision survives the intensity multiply.

**SPIKED 2026-08-27 IN PLAY MODE AND IT WORKS.** A quad writing a known value into a fixed corner
of clip space, a named `GrabPass`, and a second quad sampling it back: the value returns exactly.
The grab comes back **ARGBHalf**, 16 bits of float per channel, worst error measured at 0.00005,
which across a two metre range is about **0.1 mm**. That is twelve times finer than the contact
channel, arrives every frame rather than ten times a second, and needs no smoothing, so none of
the resolution-versus-lag trade above applies to it at all. `Assets/YapsSpike/` and
`Assets/Editor/SpikeAtlas.cs` in the Dracaionan project.

Writing the patch in clip space, ignoring the object's transform and the eye, makes it
stereo-proof by construction: both slices of the eye texture array get identical content, so
there is no double-wide layout maths to get wrong. This is the part that does not survive
conversion from VRChat, and it is simply not available here.

**Still unanswered, in order:** whether ChilloutVR's asset filter keeps a `GrabPass` through an
upload (only an upload can say); the per-frame cost of a named grab in VR; whether the patch
escapes frustum culling when the socket is off screen (scaling the quad enormously works, since
the vertex shader ignores its position and only the bounds change); and cell collisions.

**A possible answer to collisions, untested:** give each socket a build-time random id, hash it to
a cell, and write the id into the cell beside the position. A colliding cell then decodes to some
other socket's position, which is almost always metres away, and the engagement range gate already
rejects that. Collisions would degrade to "no socket found", falling back to lights or contacts,
rather than "wrong socket found".

**Original note, written before the spike:**

**The screen-space atlas is real, and it could be written better than SPS, but do not build it
yet.** Sockets render a small quad encoding their world position into a reserved screen region;
the plug samples it back through a named GrabPass. It is the only channel that dodges *both* walls
above: no light slots, no contact pairs, no parameters, so no authority gate and no sync bits, and
no cap on socket count. And the reason SPS's does not survive conversion is one that need not apply here: theirs is written for VRChat's double-wide, this would be instanced-native from the
first line (`UNITY_DECLARE_SCREENSPACE_TEXTURE`, the same family the shader patcher already
applies). Against it: a named GrabPass is a real per-frame cost; the atlas quads must escape
frustum culling; and the unsolved one is **cell collisions between avatars**, because two avatars
cannot negotiate which screen cell they own without scripting, and a collision is a wrong socket
position rather than a missing one. It also reintroduces the screen dependency that made the
contact route attractive in the first place.

**Order of work:** the light-slot fix first (done), the gate test (done, disproven), then spike light
colour. The atlas stays designed and unbuilt until something forces it.

---

## The WASM route, and what it would make of all this
*Status: blocked: access declined 2026-08-19.*

Read from the local client on 2026-08-17. **That install is the `public-scripting` beta**, and the
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
protocol they speak*, Raliv's lights, TPS and SPS pointers, YAPS markers, because it reads the
components rather than waiting for Unity to hand it four light slots or for a contact pair to
survive the budget. Everything on the platform becomes findable, including content that will
never be converted and whose authors are long gone. That is what "force everything over to YAPS"
can actually mean: not converting other people's avatars, but understanding them.

**It stays a tier, not a replacement.** These sockets must keep emitting lights and pointers, since
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
So this is moving rather than hypothetical, and the scripting branch is already installed here, which
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

## The socket preview stands down in Play Mode without saying so

`YapsSocket.PreviewTick` returns at `if (Application.isPlaying) return;`. That is correct:
in Play Mode the game systems own the property blocks and an editor preview writing over
them would fight whatever drives the material. But it stands down in total silence, and a
preview that writes nothing is indistinguishable from a feature that does not work.

This cost a full evening of diagnosis on 2026-08-26. Every reading taken in Play Mode showed
an empty block, which decodes to the far corner of the channel box, which looks exactly like
a bad encode. Out of Play Mode the same scene round-tripped to within 0 m.

**Fix:** grey the preview toggle out in Play Mode, or put a line under it reading that the
preview is inactive while playing. The user must not have to infer it.

## `YapsSocket.preview` does not survive a recompile

It is `[NonSerialized]`, so a domain reload drops it to false while the button can still read
as active until it repaints. Any script edit silently stops a running preview. Either serialise
it, or have the socket editor re-assert it after a reload.

## The channel flickers at the sync rate in game

*RESOLVED BY REMOVAL in 4.5.1: the plug no longer reads the contact channel, and `YapsChannel` is emptied on dev.*

Found 2026-08-27, immediately after the channel first worked in game. The deform twitches hard
and the flicker is at about ten a second, which is ChilloutVR's parameter rate. It happens deep
inside the socket, not only near the trigger's edge, and the light path on the same prop in the
same room is perfectly smooth.

**What is already ruled out, all measured rather than reasoned:**

- Everything downstream of the parameter. Driving the channel by hand in Play Mode with a
  constant value holds the driver fields, the material and the deform completely still. So
  smoothing, the driver layers, the material and the shader are all stable.
- A leftover `CVRAnimatorDriver`. There are no `YAPS Driver` objects on the avatar.
- Two sockets fighting. Confirmed 2026-08-27 against a prop REBUILT with a single ring and no
  hole pointer anywhere, so this is not an arbitration artifact.
- The trigger boundary. It flickers deep inside as well as at the entrance.

**So the parameter itself is alternating in game.** Ten a second is the tick at which a synced
value arrives, which points at something restoring or overwriting it between contact updates
rather than at the contact reading being noisy. Worth looking at next: whether the owner also
applies the networked echo of its own parameter, and what value it alternates to (a zero would
implicate an exit task or a default, a stale position would implicate the echo).

## The channel latches: no socket, and the plug still thinks it is in one

*RESOLVED BY REMOVAL in 4.5.1: the shader no longer reads the channel, so nothing it holds can bend a plug.*

Measured in game 2026-08-27 by giving the channel parameters temporary menu sliders, which is
the only way to read their live values. With no socket anywhere near:

    YAPS0E 61    YAPS0H 0    YAPS0X 58    YAPS0Y 49    YAPS0Z 84

Every one of those should be at rest. The plug is sitting permanently 61 per cent engaged toward
a socket position that has not existed for minutes.

**Cause, for the axes: they have no exit task at all.** `AddAxisTrigger` adds a `stayTask` and
nothing else, so X, Y, Z and the three front axes keep whatever they last saw, forever. Only the
engagement and hole triggers have exit tasks, and engagement was ALSO stale at 61, so its exit
did not fire either (a sender that despawns inside a trigger never fires one).

**Corrected the same evening, before acting on it.** Pulling AWAY from a socket cleanly does
reset engagement: the sliders then read `E 0, H 0, X 46, Y 60, Z 99`. So the exit task works, and
the axes latching is harmless while engagement is 0, because engagement gates the whole deform.

**The latch only bites when the exit does not fire at all**: a prop despawning inside the box,
the plug's own object being toggled off (disabling a collider fires no exit), an instance change,
a sender leaving the room. That is how E came to be stuck at 61 with nothing nearby, and in THAT
state the plug is bent toward a phantom socket with nothing to clear it. Not the everyday case,
but not rare either, and there is no way back short of finding another socket.

**Shape of the fix:** engagement must decay rather than rely on an exit, and the axes need
either an exit task or the same decay. Anything that can only be written while a sender is
present, and never cleared when it leaves, will latch.

**Seen again 2026-09-11, the everyday way.** A socket deleted while the plug was inside it left
the plug bent; moving away from a live socket straightened it. A deleted or disabled socket
fires no exit, which is this latch. The atlas cannot latch, since it is cleared every frame and a
disabled writer draws nothing. The plug's own *Resolved by* view settles it: half length is the
channel. Stripping contacts from the transport removes the mechanism outright; if the channel
stays, the decay above is still the fix.

## The channel's drift is quantisation, and the smoother cannot filter it

*RESOLVED BY REMOVAL in 4.5.1: the plug no longer reads the contact channel, and `YapsChannel` is emptied on dev.*

Fully characterised 2026-08-27 in the editor, no uploads. The channel's values never settle while
a socket is near: they wander by about one part in a thousand every tick, and the deform is
sensitive enough that this reads as a constant stutter. The marker light path on the same socket
is smooth, because it samples continuously rather than about ten times a second.

**How big is the wander?** Measured by simulating it. `ChannelHandDrive` can wobble its input by a
chosen number of slider units while delivering at 10 a second, and the in-game look was matched
by eye at **0.07 units**. The channel box is 1.78 m across and a slider unit is 1.8 cm, so the
apparent socket movement is about **1.2 mm**.

**That rules out body motion**, which had been the leading theory. Breathing and an idle animation
move the hips a centimetre or two, which is about one slider unit, and one unit was reported as
"way too much" wobble. The real signal is fifteen times smaller than a body sways. A millimetre is
quantisation scale: ChilloutVR does not send a full float, so a value between two steps dithers
between them.

**Why the smoothing cannot fix it as built.** The cloned layer is the AvatarScaler's "Linear
Smoothing Layer", which moves a FIXED amount per frame. Noise rejection and tracking speed are
therefore the same number:

    StepSize 0.05    ships today       stutter clearly visible
    StepSize 0.0015  kills the wobble  the plug lags the socket by 3 to 5 seconds

There is no value that does both, because the two requirements pull on one knob.

**Measured properly 2026-08-27, and the earlier conclusion was wrong: the smoother is ALREADY
proportional.** With a 0.14 unit input wobble at 10 a second, the amount reaching the material
scales linearly with StepSize:

    0.05  ships today   0.12 units survive   86 per cent
    0.02                0.04 units           29 per cent
    0.01                0.02 units           14 per cent
    0.005               0.01 units            7 per cent

Linear in the gain is the signature of a first-order filter, so it does not need replacing. It
does mean noise rejection and tracking speed are the same knob, which is the bind: the setting
that hides the wobble leaves the plug seconds behind the socket.

**A deadband was tried and is WORSE.** Two extra children on the delta blend at plus and minus
0.0015, both the zero-step clip, so the output holds still while the delta is inside the band.
Measured result: the surviving wobble went UP, to 0.29 units, over twice what went in. Inside the
band the output freezes, then the input escapes and it jumps by roughly the band width. A deadband
only helps when the noise is much smaller than the error you will tolerate, and here the noise IS
one quantisation step, so any band wide enough to swallow it produces jumps wider than it.
Reverted; do not try it again without that arithmetic in front of you.

**So this is a resolution limit, not a bug.** The channel's resolution is about 1.2 mm and the
deform is sensitive enough to show one step of it. Removing a one-step wobble requires averaging
over several samples, which is lag by definition. The choices, all with costs:

- **Lower the gain.** `BridgeSettings.DefaultSocketFollow` at 0.02 gives a third of the wobble for
  roughly 0.3 s of trailing while a socket is moving. Probably the right default.
- **Shrink the channel box**, so the same number of quantisation steps covers less distance.
  Extents are `length * BoxLengths` at about 1.75, and the engagement gate reaches zero at
  `length * 1.6`, so about 9 per cent is free. Tighter than that buys real resolution but loses
  the range where engagement fades in, between roughly 0.6 and 0.8 of a plug length.
- **Accept it.** It is a millimetre, and it only reads as stutter because the deform amplifies
  small position changes in some geometries. See the note below.

## The bend direction is ill-conditioned when a socket lines up with the plug's axis

Noticed 2026-08-27 while testing the above. With the socket dead on the plug's own axis, X and Y
both at the centre of the channel box, a sub-millimetre wobble makes the deform thrash: there is
no preferred side to bend toward, so the direction swings. Move the socket off-axis and it is
steady, and at long range it does not show because the plug is nearly straight anyway.

No amount of smoothing the POSITION fixes an unstable DIRECTION, so this is separate from the
resolution limit above. How much it matters depends on how often a real socket sits within a
millimetre of the axis, which is probably rare. Worth remembering when someone reports that a
plug "freaks out sometimes" with no other pattern to it.

## User reports from stable, 2026-08-27

Both from a user on the shipped build. Neither is reproduced here yet, and
neither should be closed on the reasoning below alone.

### Duplicate SPS toggles after conversion

The conversion lists the avatar's SPS toggles, and converting to YAPS adds a SECOND set, so the
menu carries two of each and one of them is inert.

**The first hypothesis here was wrong and is corrected.** It said `ToggledBy` only recognises an
entry driving the object through `gameObjectTargets`, so a clip-driven SPS toggle would slip past.
Reading it properly: it checks `gameObjectTargets`, the entry's own on and off animation clips,
dropdown options by both routes, AND every clip in the avatar's controller for anything hiding the
target or any of its parents. Only clips under YAPS's own output folder are skipped, so a
converted toggle's clips in `RehomedAssets` stay visible. Ordering is not it either: `Parameters
and menu` and `Animator merge` are passes 128 and 131, and YAPS is 146, so the entries and clips
exist by the time it looks.

**What YAPS does is defer, not replace.** If anything already switches the object it adds no
toggle, and it removes one an earlier build added that turned out to be redundant. So a duplicate
means `ToggledBy` returned null on an object something demonstrably switches, and no reading of
the code so far explains why.

**Cause unknown.** `Dev/Probes/DuplicateToggleCheck.cs` finds every object under more than one menu
entry and labels each by route, targets or clip, across the whole corpus in one Unity session. Run
it and let the answer come from an avatar rather than from me reading. It cannot run while a
corpus run holds the same project.

**To confirm before fixing:** take an avatar whose SPS toggle is clip-driven, convert it, and look
at whether the duplicate entry appears and whether `ToggledBy` returned null. The corpus has SPS
avatars in it; a probe counting menu entries that target the same object would find this across
all of them at once, which is better than one reproduction.

### The plug does not go back to straight when you move away

Moving away from a socket leaves the plug bent or misshapen rather than returning to rest.

This has the shape of the latch found the same day: the channel's axis triggers carried a stay
task and no exit, so X, Y and Z kept the last reading, which is taken at the EDGE of the box.
Engagement gates the deform and does reset on a clean exit, so a stale position alone should be
harmless, but any engagement that does not reset leaves the plug aimed at a phantom socket. Fixed
for the clean case in 4.4.0; a sender that vanishes inside the box still fires no exit at all.

**Worth asking the reporter to retest on 4.4.0**, and to say whether the socket was on a prop that
despawned, an avatar that left, or one they simply walked away from. Those are three different
paths through the same symptom and only the last one is fixed.

## The prop channel is a second implementation, and fixes land in one of them

*RESOLVED BY REMOVAL in 4.5.1: the plug no longer reads the contact channel, and `YapsChannel` is emptied on dev.*

Found 2026-08-27 while auditing the README. `YapsChannel` builds the contact channel for avatars
and `YapsPropBuilder.BuildChannel` builds it for props, and they are separate code that does the
same job with different component types, `CVRAdvancedAvatarSettingsTrigger` against
`CVRSpawnableTrigger`. So a fix goes into whichever one was being debugged.

Three had never crossed:

- The trigger halving. `box * 0.5f` on the engagement and hole triggers, fixed for avatars in
  `53e293c` and still live on props, so a prop's engagement volume was half what it should be
  while its axis triggers beside it were full size. Exactly the mismatch avatars had.
- No exit task on the axis triggers, so a prop's reported position stuck at the edge of the box
  after a socket left.
- No ring trigger, so the hole flag could only ever be set: a ring arriving after a hole was
  treated as a hole for as long as the prop lived.

All three are across now, but the shape of the problem is not fixed. **Two implementations of one
protocol will keep drifting**, and nothing compares them. Worth either a shared builder that emits
whichever trigger type it is handed, or a test that builds both and asserts the same box sizes,
tasks and tag lists come out.

**And the corpus cannot catch it.** It converts avatars; nothing in it builds a prop, so the prop
path has no regression cover at all. That is a separate gap from "nothing revisits an existing
prop", already recorded above, and worse: this one means the code is untested, not just that
users hold stale copies.

## SPS2 shipped, and the converter cannot see it

Published 2026-09-03 at `https://vrcfury.com/sps/`. It transports deformation through grab
passes, two per world rather than two per item, and its own notes say deformation "no longer
depends on socket lights or plug contacts". Legacy compatibility keeps the lights so it can still
meet SPS1, DPS and TPS; depth animations and some haptics still ride contacts. Each plug and each
socket costs two extra material slots. Supported shaders are a wide list: Poiyomi 7/8+, lilToon,
UTS, Mochie, XSToon, Silent, Standard, `Unity/Color`, a mobile particle shader and some Shader
Graph. The page does not publish the encoding, so there is nothing to read from it about cell
layout, hashing, or what the second grab pass carries.

**The problem for the converter is detection, not transport.** `YapsLegacyMap.Detect` knows DPS,
TPS and SPS1 by their material properties. An SPS2 plug will carry new ones, so it reads as "no
legacy system": the plug still bakes, the material still patches, the deform still works, and
nothing switches SPS2's own deform off. Two deforms on one mesh is exactly the case the DPS
branch exists to prevent, and it will arrive silently, on an avatar that looks converted.

Wanted, in order: a property signature for SPS2 on `YapsLegacyMap.Origin`, the switch-off in
`SwitchOffLegacyDeform`, and a carry map for whatever its knobs are called. Needs a real SPS2
avatar to write against, so it waits for one.

One thing it confirms rather than threatens: "The plug mesh must be straight / fully extended in
the editor" is their constraint too. The bake measures a rest pose because the technique requires
it, not because this one is weak.

### The feature gap against SPS2, DPS and TPS, read 2026-09-08

SPS2 from `docs/60-sps/index.mdx` in the vrcfury.com repository; DPS and TPS from this repository,
since the GUI already tags each knob with the system it came from and `YapsLegacyMap` records what
the converter cannot carry. Nothing here is decided. It is a list to choose from on evidence
rather than on a memory of what these systems looked like a year ago.

**1. Tags, and this is the hole, not a nice-to-have. IN PROGRESS 2026-09-08.**

DPS had channels in its day: `_OrificeChannel`, so a plug only answered orifices on a matching
channel, and the converter records it as unmapped to this day. SPS2 has tags: up to two custom
tags on a socket plus a location set derived automatically (hips, head, chest, hand, handleft,
handright, foot, footleft, footright, hipsfront, hipsback), and up to two include and two exclude
tags on a plug, each side taking `Self` and `Others` modifiers, changeable by an animation.

**Correction, 2026-09-08.** This entry said flatly that YAPS has none, and that was wrong.
`_YAPS_TagInclude` and `_YAPS_TagExclude` have been declared and read since before the atlas: one
hashed integer a side, compared for equality, on the CHANNEL tier only. Nothing in C# ever wrote
them, so the effect was the same as having none, but the shape was already there and the
correction matters because the fix was smaller than the entry implied. Read the code before
writing down what a system lacks. The switch added on 2026-09-08 for whether a plug answers its wearer's own
sockets is a one-bit stand-in for it, not a small feature that might grow into it later.

It also settles how the per-socket allow list dropped the same morning should have been built. A
tag lives on the socket and costs nothing to sync, where a per-socket list costs a bit each and
fills the wearer's menu with rows nobody opens. If that idea is ever reopened, it is reopened as
tags.

**What is built, 2026-09-08.** Protocol version 3. A socket carries a 15-bit `YapsTags` set and
publishes it in a THIRD atlas pixel, five bits a channel over rgb: the facing pixel's alpha
already holds the kind and the position pixel's holds the owner tag the whole read is gated on,
so there was nowhere else for it. That takes a cell from 17 slots to 25 and the rect from 552 by
520 to 808 by 520.

**The rect is the risk in this, and it is not yet paid.** A target that cannot hold the rect
fails `YapsAtlasFits` and drops to the light tier silently, which is the exact failure mode that
made the mirror look broken for two days. The seven cameras A1 settled grid 64 against have to be
walked again at the new width: editor, desktop first and third person, desktop mirror, VR both
eyes, VR mirror, personal mirror. Until that is done the tag work must not ship, and a width that
fails is a reason to go back to a single tag index in the byte that was already free rather than
to shrink the grid.

*Protocol 5, 2026-09-11, moved the target again: an owner pixel per octant, 28 columns, 932 by
596. The walk above now has to be done at that size.*

The plug carries an include set and an exclude set, both plain floats so an animation can drive
them. One rule tests them, `YapsTagsRefuse`, called from the channel branch and the atlas branch
alike: refuse on any bit shared with the exclude set, require any bit shared with the include set,
and an empty include set means no opinion. Two tiers cannot drift apart because there is one
function.

**A marker light can never carry a tag** and that is not a gap to close: a light's RANGE is its
whole message and the digits are Raliv's. So a socket found by light reads as untagged, and
untagged is refused only by a REQUIRE. That is the honest reading of content older than tags,
and it is what SPS does with legacy content too.

**But a REFUSED socket is not an unknown one, and that half is fixed, 2026-09-08.** The light
fallback engages on distance once nothing else has, so a socket the atlas had just read and
turned down was picked straight back up by its own marker light, at contact range, which is the
one range a refuse list is written for. `YapsResolveChain` now carries the nearest refused socket
out with it, and a light answer within a tenth of a length of that position is undone: engagement
back to zero, tier back to nobody. Only the light tier, and only where the atlas actually read
the socket. A tenth of a length so it takes the same socket and not its neighbour; deliberately
tight, since too loose refuses something nobody named.

Untested in game. The failure mode if the distance is wrong in either direction is quiet: too
tight is the old behaviour, too loose is a neighbouring socket going dark.

**The inspectors and the menu went in the same day.** The socket's set sits in "What it is"
beside its kind; the plug's two sets open a fold of their own, chipped DPS and SPS since both
had the idea first. A plug listing two or more tags gets a dropdown and an animator layer, one
state per tag plus "As built" and "Anything", driving `material._YAPS_TagInclude`. Fifteen
toggles would have been fifteen rows and fifteen parameters for a question with one answer at a
time, which is the objection that killed the per-socket allow list.

**A multi-slot plug needed care and turned up a real bug elsewhere, since fixed.** A curve on
`material._X` binds to the FIRST material only. The tag clips were written that way from the
start, but the deform toggle and the own-body toggle were not: a plug with its tip on a second
material had half its mesh switched off and the other half left running, in the row people
actually use. Fixed by moving the slot walk into `YapsToggles.Clip`, which all three now share,
so the next property to be animated cannot get it wrong again.

The matching half of the bug was in the reading. `material[1]._X` is a different string from
`material._X`, and every place hunting for a toggle compared against the second: the toolkit
could not find a toggle it had built on a second slot, so it built another, and Remove walked
past the curves and left them behind. `YapsToggles.Bare` strips the index and `Writes` compares
through it, and `YapsRemover` reads its wired list the same way.

Where no slot declares the property at all, slot 0 is still written, so a plug whose material is
assigned after the clip behaves as it did before.

**The channel tier does not filter, and that is now written down rather than broken.** The shader
read the socket's set out of `_YAPS_SocketFlags.z`, which nothing has ever written: every channel
socket read as untagged, so a plug with any include list refused the entire contact tier and a
plug tagged for one place stopped resolving at all on an avatar with no atlas. Fail-closed, from
a field with a default rather than from a branch.

Unknown is not untagged. The test is gone from that branch: a tier that cannot see the set can
prove neither a match nor a refusal, so it claims neither. Nothing was weakened by removing it,
since an exclude never fired there either, a set of zero matching no exclude.

**A "pointer suffix transport" was proposed here on 2026-09-08 and does not exist.** Read the
correction before acting on any memory of it. The reasoning was that `YapsScanner.IsSocketRootTag`
matches `SPSLL_Socket_Root_` by prefix, so SPS must put tags after that underscore. It does not.
`_SelfNotOnHips` is the only suffix SPS ever emits, `HapticUtils` declares the contact type
strings as four fixed constants, and nothing in the bake appends a tag to any of them.

**What SPS actually does, read out of `SpsConfigurer.cs` rather than inferred.** A tag is a
free-form string, lowercased and trimmed, hashed with FNV-1a to 32 bits, and written into MATERIAL
PROPERTIES: eight socket slots as `_SPS_SocketTag{1..8}Low`/`High`, sixteen bits each half, and
four plug slots as `_SPS_TagInclude{n}Low`/`High` carrying self and others flags. Two of the eight
socket slots are the author's, two are derived from the nearest humanoid bone, one is a shared
tag. The plug reads them through SPS's own shader data channel.

So tags never touch contacts in SPS either. A material property is the same CLASS of channel as
the YAPS atlas, and the contact tier not filtering is what SPS does too, rather than a shortfall.
That closes the question above: there is nothing to wire, and the current behaviour is right.

**The encoding is now SPS's, as of 2026-09-08.** A tag is a free-form string, trimmed and
lowercased, hashed FNV-1a to 32 bits with SPS's constants, so a name means the same thing on both
tools. The fifteen-value enum is gone and the four custom slots with it.

**What is NOT SPS's is where the hash goes, and that was forced.** SPS gives a socket eight slots
and writes each 32-bit hash whole into material properties. The atlas has no room: one pixel per
field per octant, every extra pixel costing 256 pixels of width, so eight slots take the rect from
808 to 2600 and no mirror resolves at all. Even one extra pixel makes it 1064, and 808 is already
the number the fit test is meant to settle.

So a socket's tags FOLD into the one pixel already there. Each tag lights three distinct bits of
twenty, chosen by its hash, and the socket publishes the OR; a plug carries up to four patterns a
side in two Vector4s and asks whether all three of a tag's bits are lit. The plug's side costs the
atlas nothing, being its own uniforms.

**The fold has a false-yes rate and the direction was chosen.** A socket wearing three tags lights
about eight bits of twenty, so an unrelated tag reads as present about one time in twenty. On the
answer list that answers a socket that was not asking, which is the permissiveness the feature
already documents everywhere but the atlas; on the refuse list it refuses one it need not have,
which errs toward not touching. Neither invents a socket.

**Two generations of the pattern generator were wrong and a review caught both.** The first took
successive remainders of one hash, which let a tag repeat a bit and light only two; "footleft" lit
two, both inside "handright", so a hand socket read as a foot socket. Distinct bits fixed that and
introduced the second: `h = h * 16777619 + 2166136261` is congruent to `3h + 1` modulo 4, so the
index alternates between two residue classes and only 200 of the 1140 three-bit patterns exist.
"tag1" and "tag13" were the same tag outright. A multiply-and-add is not its own avalanche;
murmur3's finaliser is, and reaches all 1140.

Worth recording the direction: fixing it made the eleven names' three-tag false rate go UP, from
about 1.5% to 5.4%. The old space was small enough to be lucky on eleven cherry-picked names while
being catastrophic on arbitrary ones. 5.4% is the honest number for a fold this size.

`Dev/Probes/YapsTagProbe.cs` checks the eleven hashes against values read out of VRCFury, that no
name reads as present on a socket wearing only another, that all 1140 patterns are reachable over
ten thousand arbitrary names, and that the three-tag union rate has not moved. Run it after
touching `YapsTags`.

**One normalisation, shared.** The bake dropped blanks and the menu also deduplicated, so
"hips, HIPS, head, hand, foot" made the bake spend a slot on the repeat and lose "foot" while the
menu kept it, and entering the controller's default state changed which sockets the plug answered
with nobody choosing anything. `YapsTags.Listed` is the one implementation now.

**The atlas protocol is version 4.** Same pixel count, same rect, different meaning in the third
pixel: an old untagged socket wrote alpha 1, which the new decoder reads as five bits set rather
than none, so a pattern living in those bits would match a socket that had no tags at all. Only
mixed builds of unreleased version-3 work are affected, and the version is what refuses them.
**The converter carries them now, 2026-09-08.** The words are read off the VRCFury components in
`YapsBakePrep`, beside the overrun flag and for the same reason: SPS writes tags as 32-bit hashes
into a generated animation clip, so the baked avatar has numbers and a hash cannot be turned back
into a word. Sockets travel to the rebuild in `Spec.Tags`, because the rebuild compiles without
the VRChat SDK and must not reference the prep; plugs are written straight into the patched
material beside the overrun.

The bone-derived names are derived here too, by CLIMBING to the bone a socket hangs off rather
than measuring to the nearest one: a distance answers wrongly the moment two bones sit close.
Hips front and back only when there are exactly two hip sockets, which is SPS's own rule; with
three, naming a front socket "hipsback" would make a plug refuse the wrong one.

**All three paths say when a list was too long, 2026-09-08.** Four on each list is what the bake
holds, and the fifth used to go without a word on two of the three ways in: the converter named
the words it carried and never the ones it did not, and the tag menu capped the list in `Listed`
and built four rows. Only the native builder said so. The same miss in three places is the shape
this codebase keeps producing: a rule gets a shared helper, and the SPEAKING about it stays local
to whichever path was open at the time. `YapsTags.Dropped` is now the one counter and each path
reports it. The inspector still accepts a fifth entry rather than refusing the keystroke, which is
deliberate: the author may be moving words around, and the build is where the truth is told.

**The toolkit had no such convention, and that was the hole, 2026-09-08.** Carrying the shared word
across from SPS keeps a converted plug working on a converted avatar, and says nothing about the
sockets this tool builds itself. Those defaulted to an empty tag list, which publishes an empty
word, and a plug with any answer list refuses a word that does not match. So every plug converted
from SPS, which is every one of them since it always carries the shared word, refused every socket
the toolkit ever built and every prop, on the atlas tier. The light and contact tiers never ask
about tags, which is what kept it from being obvious.

The fix is the convention, not the rule: `YapsSocket.tags` starts with the shared word, so a socket
built here says the same thing an SPS socket says. A CONVERTED socket is still whatever SPS said,
empty list included, because an author who turned the shared tag off there meant it: the rebuild
assigns the list always rather than only when it has something in it, or the component default
would quietly overrule them.

No migration: the tag field has never shipped, so no socket in anyone's project carries the old
empty default.

Third builder, same lesson. The converter and the native builder were the two that got compared;
props go through the socket builder and were not in the comparison at all.

**Carrying the shared tag is what makes it safe, and it was nearly missed.** Nearly every SPS
socket and plug carries the global tag, and a plug that has it answers everything regardless of
its own list. Carrying a plug's include list WITHOUT carrying that would have made every
converted plug with a rule refuse every socket on the avatar: a shipped regression out of a
feature meant to add fidelity. The shared tag is a fixed number in SPS rather than a hashed word;
it travels here as the word "shared", since a VRChat plug and a ChilloutVR socket never meet and
only the behaviour has to carry.

What does not survive: the per-tag self and others modifiers, since a plug has one answer for
both. Reported rather than silent.

**Distinct entries past four are now counted, not dropped in silence** (the residue of H2, which
a reviewer was right to call out as an overstated "all fixed"). `YapsTags.Dropped` counts them and
the plug build says how many it left out; the tooltips and the README say four is the room. The
inspector still accepts a fifth, which is the honest place to stop: a custom list drawer to
prevent typing one is a UI project, and the bake now tells the truth either way.

**Still owed**: the rect, above, which is the one that gates shipping. No converted avatar has
been through this yet: the corpus has not run since it landed, and nothing has been tested in
game.

**2. Not a gap, and worth saying before the rest.**

The SPS2 transport is a grab pass costing two per world rather than two per item, which is the
conclusion the screen atlas reached from the other side. Hole and ring, the overrun switch, an
animated toggle for the deform and the rest-pose requirement all have equivalents here. Their
guided path allows three stops with adjustable tangents; the chain holds four links. Same features,
different names.

From DPS and TPS, nearly everything is already carried: curvature, recurvature, entrance stiffness,
wriggle and the socket blendshape staging came from DPS; squeeze, bulge, idle shrink, pumping, the
bezier bend, smooth start and minimum socket distance came from TPS.

**3. Small and mechanical.**

- **Ring sidedness. DONE 2026-09-11 on dev, protocol 7.** A socket's **One way** tick: the atlas
  kind becomes ring, hole or one-way ring, and a plug whose base is behind a one-way ring's front
  passes it by. Lights cannot carry it. Not carried from TPS: `_TPS_TwoSidedRings` sits on the
  PLUG there, and a plug-side rule would be a second control for the same question.
- **Idle gravity.** `_TPS_IdleGravity`, recorded as unmapped. There is idle shrink and there is
  wriggle, but nothing that hangs.
- **Radius offset. DECLINED 2026-09-11.** A YAPS socket is an object the author places, so moving
  it IS the offset; SPS2 needs a field because it snaps sockets to bones. Revisit only if a
  converter needs somewhere to put SPS2's own value. Was: a per-socket nudge so the opening sits
  on the surface rather than in it,
  which SPS2's notes single out as mattering most for hand sockets.
- **Auto-rig.** Bones and physbones added to a static plug mesh at build time. This tool converts
  what an avatar already has and has never built that.
- **Bulge falloff.** `_TPS_BuldgeFalloffDistance`, a second falloff on the bulge. Minor.

One unmapped property is deliberately not on this list: `_BlendshapeBadScaleFix` from DPS, because
scale is read live here and there is nothing to fix.

**4. A feature in its own right: depth animations.**

How far a plug has entered a socket drives an animation. This is the mechanism behind the arousal
systems people ask about, where touching yourself changes a blendshape over time; those are built
by avatar authors on top of it and are not themselves part of SPS.

Their depth animations still run on contacts even in SPS2, which their own page says plainly. That
is the interesting sentence, because contacts are exactly what the GPU bridge removes: a depth
animation here could be per client and cost nothing, where theirs cannot. If any item on this list
is worth doing for its own sake rather than for parity, it is this one, and it wants the GPU bridge
machinery first.

**5. Masks: the source is fine, the recovery is missing.**

Read on 2026-09-08. SPS paints a mask texture saying which vertices belong to the shaft, with an
auto-mask from bone weights and an author override on top. YAPS derives the same number from the
rig instead: `WeightOnPlug` in `YapsBaker.cs` takes each vertex's skin weight on the plug's bone
chain and stores it as the bake's `active` channel, which the shader reads as the per-vertex blend.
That is the better source of the two, and it is not what needs work. A skin weight feathers at the
base for free, and because it is the same number the skinning itself uses, the mask cannot disagree
with the deformation the way a painted texture can.

The problem is what happens when that source is not available, and there are two such cases.

- **The climb was unbounded. Fixed 2026-09-08.** When no bone sits under the plug's root, the baker
  walks up ancestors until it finds a subtree with any weights at all and takes that whole subtree.
  A plug parented to Hips climbs to Hips, every vertex on the body reads a weight near 1, and the
  avatar bakes as one enormous shaft. It is what ten avatars did.

  The conversion has refused this since long before, by asking whether the level it climbed to is
  itself a humanoid bone. The baker did not, so a plug the converter turned away could still be
  baked into a whole-body shaft by hand from the toolkit. Another one-path-only divergence between
  the two builders, found by reading rather than by a report.

  The baker now refuses it too, and asks a narrower question than the converter does: whether the
  bones it captured include *this renderer's* head or feet. A shaft on its own renderer weighted
  only to Hips captures neither, so it still bakes, where the converter's rule turns it away.
  Vertex count cannot decide this on its own, which is what the entry first proposed: a dedicated
  shaft renderer legitimately captures every vertex it has. Socket bakes are exempt, since they
  route through the same function and a socket on a body mesh is meant to reach the body's bone.
  Nothing is claimed on a rig with no humanoid mapping.

  **Not yet exercised.** It is written against the failure and it compiles; no avatar has hit it
  since. The corpus is where that would show, and it has not been run for it.

- **A plain mesh has no mask at all.** On a non-skinned renderer the baker writes a flat weight of
  1 for every vertex. Correct when the mesh is only the plug, and wrong the moment it is not,
  with nothing to say so. The only guard today is that a vertex behind the base is dropped.

Across both, the gap against SPS is not where the mask comes from, it is that there is no override.
The plug's root bone field is the only lever, it cannot touch a plain mesh, and it cannot trim a
skinned one: a sheath weighted to the same chain as the shaft bends with it and the author has no
way to say otherwise. A painted mask is the general answer and it is a real feature, needing UV
sampling in the bake, import settings and a workflow for producing the texture. Neither the plain
mesh nor the sheath has a reported case yet, so the honest order is to bound the climb now and
leave the override until an avatar asks for it.

**What is deliberately not on the list.** SPS2's legacy compatibility switch, because reading the
legacy systems is this tool's whole job, and their material slot budget, because the atlas answers
that differently.
