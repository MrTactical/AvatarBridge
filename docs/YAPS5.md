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

Two channels, tiered. A plug tries contacts first and falls back to lights; a socket speaks both.

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

### 1. The lighthouse — proposed

One marker pair per avatar instead of one per socket. Root and front lights ride a
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

Open before building: does the avatar whitelist pass `ParentConstraint`, and does constraint
evaluation order beat the light-position read (contacts run post-constraint; lights are read at
render, so both should be fine — verify, not assume).

### 2. Light colour as data — proposed, spike-sized

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

### 4. The screen-space atlas — postponed, deliberately

Sockets render encoded quads into a reserved screen region; plugs sample it back through a named
GrabPass. The only channel that dodges the light slots, the pair cap, the sync bits AND the
authority gate, with no socket-count ceiling — and where SPS's atlas is double-wide-native and
dies under ChilloutVR's instancing, this one would be instanced-native from the first line.

Unbuilt because one problem has no answer without scripting: **two avatars cannot negotiate
screen cells**, and a cell collision yields a WRONG socket position rather than a missing one.
Wrong-but-confident is the worst failure mode this system can have. Stays designed, stays
parked, until either scripting lands or a cell scheme that fails safe is found.

### 5. Scripting — blocked

The endgame: read any avatar's sockets whatever protocol they speak, no slots, no pairs, no
bits. Access was requested and declined 2026-08-19 (world scripting is their focus). Everything
above is designed so that none of it is wasted if this arrives: the resolver's tiering is
already script-first, contacts next, lights last.

---

## Order of work

1. ~~Reserve the tracker's light slot~~ — **done**, `b82e9d0`.
2. ~~The two-person authority-gate test~~ — done 2026-08-22, gate disproven: cross-avatar prop writes work, the toucher's client syncs them.
3. Lighthouse spike: `ParentConstraint` through the whitelist, pair on one test avatar.
4. Light-colour spike: does a coloured marker survive upload and decode.
5. Corpus, with the two missing avatar classes added (an always-visible head, a deforming
   socket) before any of this ships.
