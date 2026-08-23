# YAPS, and the ChilloutVR Toolkit

Two tools for ChilloutVR avatars. No VRChat SDK required, and nothing here needs one.

**YAPS** is a penetration system built for ChilloutVR: plugs that bend toward sockets, sockets
that open around them, and shapes that react as a plug goes in. It reads and is read by the
systems already on the platform — Raliv DPS, Thry's TPS and VRCFury's SPS — so it works with the
content that is already out there, not only with itself.

- *Tools ▸ YAPS ▸ Setup* is where everything happens: it finds what an avatar already has, adds
  holes, rings and plugs, and bakes them. Press **Build** after any change.
- *Tools ▸ YAPS ▸ Create universal socket prefabs* writes drag-and-drop `YAPS Hole` and
  `YAPS Ring` prefabs, and *Create a plug prop prefab* writes a spawnable plug you can upload as
  a prop on its own.

**The ChilloutVR Toolkit** (*Tools ▸ Avatar Bridge ▸ ChilloutVR Toolkit*) is a set of utilities that work on any
ChilloutVR avatar: merging animator controllers, tidying an avatar before upload, adding face
tracking, and reporting what an avatar actually contains.

## Why this folder says AvatarBridge

Because these tools live in the same codebase as AvatarBridge, the VRChat-to-ChilloutVR
converter, and share a good deal of it. Keeping one folder means you can install AvatarBridge
later and it simply adds the converter here — nothing is duplicated and nothing breaks. It works
in the other order too.

If you only ever wanted YAPS, nothing in here is a converter: those files are not in this
package at all.

## Updating

Import the newer package over the top; it replaces what it needs to. A plug or socket you have
already baked keeps working, but **re-bake anything you want the newest fixes on** — a bake
carries the shader and the data it was built with, and an uploaded prop carries its own copy.

## Help

The full documentation, including the socket and plug reference and a troubleshooting guide,
lives at <https://github.com/MrTactical/AvatarBridge>. Bug reports and questions are welcome
there.

Licence: see `LICENSE.md` beside this file.
