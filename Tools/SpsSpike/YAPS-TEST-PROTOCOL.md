# YAPS in-game test protocol

Six test sockets and one plug prop, all from *AvatarBridge ▸ Spike ▸ Build YAPS test props*.
Every path has now been proven in game (2026-08-15): lights, the contact channel on a converted
avatar, and the contact channel on the prop. This is the protocol for confirming that after a
change, and for telling the paths apart when one is not working.

## Before every upload

Run *AvatarBridge ▸ Spike ▸ Verify and repair YAPS props*. It fails a channel whose parameter
name went blank (that does not survive the CCK inspector), an animator layer with no states, and
two triggers on one object. All three are silent in game and all three have happened.

**Do not leave a prop selected with the CCK's Spawnable inspector open.**

## The colours

Colour is the whole point. A socket that emits both lights and a contact pointer cannot tell you
which of the two moved the plug.

| prop | emits | answers |
|---|---|---|
| **Green** socket | legacy DPS lights + SPS pointers | what a real user builds — YAPS *and* every DPS plug on the platform read it |
| **Pink** socket | YAPS lights + SPS pointers | the realistic mixed case |
| **Yellow** socket | YAPS lights only | the light path alone — what existing DPS content is |
| **Purple** socket | legacy HOLE lights + SPS pointers | the taper: the plug should narrow and stop, not pass through |
| **Blue** socket | SPS pointers only, no lights | **the contact channel alone**, SPS names |
| **Orange** socket | TPS pointers only, no lights | **the contact channel alone**, TPS names |
| Plug prop | 8-value contact channel + DPS tracker light | reacts to all six; bulges the tube prop |

Every socket carries a **front** pointer (`SPSLL_Socket_Front` / `TPS_Orf_Norm`, 1 cm along its
normal), because every real socket does. A socket with a root and no front is a shape that does
not exist in the wild — the plug can aim at it but not thread it, and it looks broken when it is
only under-specified. Blue was built that way once.

## The "Resolved by" debug view — use it FIRST

On the plug prop's material, *Debug view ▸ Resolved by*. The plug colours itself by WHO produced
the socket it is bending toward:

| colour | meaning |
|---|---|
| **green** | the contact channel |
| **yellow** | a marker light |
| **black** | nobody — a plug bends toward a socket or it does not bend; there is no third answer |

Dim means resolved but not yet engaged. A light that only sharpened the channel's answer still
shows green — whoever provides the *engagement* owns the colour.

**This exists because a plug bending near a socket does not say who bent it.** A stray marker
light — a lit socket nearby, or the holder's own avatar wearing one — bends the plug exactly like
a working channel. A whole day went to a channel that had never worked because the lights kept
covering for it. In this view: orange and blue must be **green**; yellow must be **yellow**;
green/pink/purple will be **green** if the channel got there first and **yellow** if only the
lights did.

## 1 — Each socket alone

Test the contact-only sockets **alone**, out of range of every other socket. A lit socket within
about a plug length refines the position and masks whether blue or orange did anything.

Bring the plug prop to each socket in turn:

- **Orange, then blue** — bend, and read *green* in the debug view. If black: the channel is
  dead. Run the editor probe (below) before touching anything in game.
- **Yellow** — bend, and read *yellow*.
- **Purple** — the plug narrows and stops at the ring instead of passing through.
- **Green, pink** — bend; either colour is correct.

The plug should relax fully when pulled away from every one of them. Sticking bent = the exit
task failed.

## 2 — Someone else sees it

Repeat test 1 while a second person watches from a couple of metres away, **not** in a mirror.

- **Yellow** — their client resolves the lights itself. Needs no sync. Should just work.
- **Orange / blue** — the value is computed on the holder's machine and synced. Rotation may look
  stepped to the viewer (expected — that is the publish rate); it must not stick or jump.

Also: the second person **holds** the plug prop against a socket you spawned. `DisallowTheft` is
what stops it being yanked out of their hands the moment your socket engages.

## 3 — A converted avatar

Wear Angela (converted with *Convert SPS to YAPS* on). Her plug should react to all six sockets.
Then the reverse: the plug prop against her sockets, with one socket lit at a time from her menu —
one lit socket is two lights against four slots, which is why the menu decides.

Then the socket deform: the plug prop against one of her sockets should bulge it **from the
tracker light alone** (the case every piece of Raliv DPS content uses). And a contact-carrying
plug against the same socket must look **exactly** as it did — visibly stronger means the
double-apply guard failed.

## 4 — Mirrors, then VR

Everything above once more in a mirror: the fear is the mirror showing a *different bend* from
the direct view. Then VR: both eyes agree, nothing swims or doubles.

## The editor probe, when a colour is wrong

The contact channel has three links in series and a failure anywhere looks like "no bend":

1. contacts → `CVRSpawnableValue` — **only exists in game**; the editor has no client
2. animator → parameter → blend tree → material property block
3. shader → reconstructed socket → deform

*AvatarBridge ▸ Spike ▸ Probe plug channel (Play mode)*, with the plug prop in the scene, writes
the eight values a real socket would have sent and reads back what landed on the renderer's
**MaterialPropertyBlock** (animated material values never appear on the material asset). It holds
the values until Play stops, so the plug stays visibly bent, and it names which link broke.

If the probe passes and orange still reads black in game, and only then, the fault is in contact
delivery.

## Recording it

For each: which socket, what the debug view said, who was looking, what happened. "Orange, read
yellow, holder view, bent" is a bug report — it means a light did the work, not the channel.
Anything that fails is more useful than anything that passes; note the colour, because the
colour is what says which path broke.
