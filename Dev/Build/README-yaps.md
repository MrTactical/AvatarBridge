# YAPS, the penetration add-on (18+)

Adults only. This package is the penetration system for ChilloutVR avatars, and it is a separate
download from AvatarBridge for that reason. No VRChat SDK required, and nothing here needs one.

**YAPS** is a penetration system built for ChilloutVR: plugs that bend toward sockets, sockets
that open around them, and shapes that react as a plug goes in. It reads and is read by the
systems already on the platform: Raliv DPS, Thry's TPS and VRCFury's SPS: so it works with the
content that is already out there, not only with itself.

- *Tools ▸ YAPS ▸ Setup* is where everything happens: it finds what an avatar already has, adds
  holes, rings and plugs, and bakes them. Press **Build** after any change.
- *Tools ▸ YAPS ▸ Create universal socket prefabs* writes drag-and-drop `YAPS Hole` and
  `YAPS Ring` prefabs, and *Create a plug prop prefab* writes a spawnable plug you can upload as
  a prop on its own.
- With AvatarBridge installed as well, its *Avatar Tester* gains **Squash** and **Plugs and
  sockets** in its **Interaction lab** card: preview a socket, drop a test plug or socket, or press
  the squash with a test hand, beside the avatar's motion in Play mode.
- **Squash** (breasts and butt flattening against each other and denting under a hand) is off
  until you add it: the **Squash** card in *Tools ▸ YAPS ▸ Setup*, **Set up squash**, or the opt-in
  *Soft body shape: squash where parts press together* when converting with AvatarBridge.
- For a body whose bones do not say which parts squash, paint them: **Paint regions** on the
  squash window's **Parts** step, or *Tools ▸ YAPS ▸ Squash regions*.
- **Portals**: set a hole's **Portal exit** to another socket on the same avatar, and a plug that
  goes in comes out there. Build both the plug's and the socket's avatars with 5.0; to an older
  plug, and by marker light, it is an ordinary hole.

**The ChilloutVR Toolkit** (*Tools ▸ Avatar Bridge ▸ ChilloutVR Toolkit*) is a set of utilities that work on any
ChilloutVR avatar: merging animator controllers, tidying an avatar before upload, adding face
tracking, and reporting what an avatar actually contains.

## Why this folder says AvatarBridge

Because these tools live in the same codebase as AvatarBridge, the VRChat-to-ChilloutVR
converter, and share a good deal of it. Keeping one folder means you can install AvatarBridge
later and it simply adds the converter here: nothing is duplicated and nothing breaks. It works
in the other order too.

The converter is not in this package. What is here is the handful of passes that rebuild an
avatar's penetration during a conversion: they do nothing on their own, and wake up only if you
install AvatarBridge and convert a VRChat avatar. Convert without this package and the
penetration is removed instead, which the report says at the time.

## Updating

**Delete `Assets/AvatarBridge` first, then import the newer package.** Unity's importer adds and
replaces files but never removes one, so a file an older version shipped and this one does not
stays behind and keeps compiling. This package installs into that same folder, so if you also
use AvatarBridge, import it again after deleting. Nothing a conversion or bake makes lives there:
those go to `Assets/YAPS` and `Assets/AvatarBridgeOutput`. A chain preset you saved into its
`Presets` folder does live there, so move it anywhere else in `Assets` first; it is found wherever
it is.

A plug or socket you have already baked keeps working, but **re-bake anything you want the newest
fixes on**: a bake carries the shader and the data it was built with, and an uploaded prop carries
its own copy.

**One route between plugs and sockets needs both sides on the same atlas protocol.** A socket
publishes itself into a small block of pixels on the screen, stamped with a protocol number, and a
plug ignores any other number. What counts is the release each avatar or prop was baked with, not
the one installed. So far: 4.5.0 and 4.5.1 use protocol 2, 4.6.0 to 4.6.2 use 7, and 4.6.4 onward
use 9. Two avatars on different numbers fall back to the older marker lights instead: everything
still bends, and what goes missing is what the lights cannot carry, which is tags, one-way rings,
portals and a wearer's own sockets. Portals kept protocol 9 but need both avatars built with 5.0. If a plug behaves with one person and not another, compare which
releases their avatars were baked with before anything else.

## Help

The full documentation, including the socket and plug reference and a troubleshooting guide,
lives at <https://github.com/MrTactical/AvatarBridge>. Bug reports and questions are welcome
there.

Licence: see `LICENSE.md` beside this file.
