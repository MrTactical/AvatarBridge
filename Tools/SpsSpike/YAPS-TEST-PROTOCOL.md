# YAPS in-game test protocol

Everything proven so far was proven on **one client** and through the **light path only**. The
prop plug carries no parameters at all, so nothing yet has exercised the contact channel, and
nothing has shown that a second person sees the same thing.

That is what this session is for. Two people, about twenty minutes.

## What you need

**Joe uploads:** Angela converted with *Convert SPS to YAPS* ticked, plus the four props from
*AvatarBridge ▸ Spike ▸ Build YAPS test props*.

The sockets are colour-coded, and the colours are the whole point — a socket that emits both
lights and a contact pointer cannot tell you which of the two moved the plug.

| prop | emits | answers |
|---|---|---|
| **Pink** socket | marker lights + contact pointer | what a real socket is like |
| **Yellow** socket | marker lights only | the path existing DPS content uses |
| **Blue** socket | contact pointer only | **the channel, on its own** |
| Plug prop | — (no channel, no parameters) | reacts to pink and yellow, never blue |

Angela's converted plug should react to **all three**. The prop plug should react to pink and
yellow and **ignore blue completely** — if it moves for blue, something is resolving a socket
it should not be able to see.

## 1 — The channel exists (Joe alone)

Wear Angela. Spawn the **blue** socket and bring it to her plug.

- **Deforms** → the contact channel works. This is the first time it has ever run.
- **Nothing** → the channel is dead. Try the yellow socket: if that deforms, lights are fine and
  the fault is in the trigger → parameter → driver → material chain specifically.

Then the **yellow** socket, then **pink**. All three should work, and pink should look no worse
than either.

## 2 — Someone else sees it (Fluffy watches)

Repeat test 1 while Fluffy watches Angela's plug from a couple of metres away, **not** in a
mirror.

This is the one that matters most, because the two paths reach him completely differently.

- **Yellow socket** — his client resolves the lights itself, near his copy of the plug. Needs no
  sync at all. Should just work.
- **Blue socket** — the value is computed on Joe's machine and published as a synced parameter at
  ten a second. If this works for Fluffy, the whole publish chain is proven end to end.

Watch for: does it move *at all* for him; does rotation look stepped compared to Joe's view
(expected — that is the 10 Hz); does it ever stick bent after the socket is pulled away
(the exit task failing).

## 3 — Angela's own sockets

Fluffy holds the **plug prop** at Angela's sockets. This tests the twelve sockets the converter
re-ranged — and those were dark until today's intensity fix, so this is the first real test of
them.

Also: Joe wears Angela, Fluffy spawns and holds the plug prop. Reverse it if Fluffy can wear a
converted avatar too.

## 4 — Mirrors

Everything above, once more, in a mirror.

The specific fear is **disagreement**: the mirror showing a different bend from the direct view.
That is what happened with the light probe at range, and it is why engagement comes from the
channel wherever a channel exists. At contact range it held up in the earlier spikes — confirm
it still does now there is a real deform to look at.

## 5 — VR

Both eyes agree, and the deform does not swim or double. Desktop has proven nothing about this.

## Recording it

For each: which socket, who was looking, what happened. "Blue socket, Fluffy watching, bent but
lagged when I turned it" is worth more than "works".

Anything that fails is more useful than anything that passes — note it exactly, including which
colour, because the colour is what says which path broke.
