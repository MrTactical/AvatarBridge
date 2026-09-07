# 4.5.0 - Plugs find sockets off the screen, and penetration becomes its own 18+ download

**Reconvert to get any of this.** One release, two downloads, a changelog each. Existing avatars
keep working as they are; nothing below reaches them until they are converted or rebuilt.

**Updating from 4.4.x: delete `Assets/AvatarBridge` first, then import what you want.** Importing
a package never removes a file, so a 4.4.x install leaves its copy of the penetration system on
disk whatever you import over it, and you end up running last version's half beside this one's.
Deleting the folder is safe: your converted avatars live in `Assets/AvatarBridgeOutput`, which is
a sibling folder and is not touched. Delete, import the converter, then import the add-on as well
if you want penetration.

---

## AvatarBridge `AvatarBridge-4.5.0-public.unitypackage`

The converter and the ChilloutVR Toolkit. This is the download for everyone.

### Penetration is no longer in this package

Nothing about how a conversion works has changed. If an avatar carries DPS, TPS or SPS and the
add-on below is not installed, that system is removed, exactly as choosing *Remove* has always
done, and the report says which system the avatar had and where to get the add-on. The window
says the same in place of a choice it cannot offer.

Everything else converts as it did in 4.4.3.

<details>
<summary>Why it moved out</summary>

The converter is a general avatar tool and a good half of the people using it never touch
penetration. Bundling an adult system with it meant every download carried one, and there was no
honest way to label the package for who it was for. Two packages, each labelled, both installing
to the same `Assets/AvatarBridge` folder and sharing the same code, sorts both problems. The
converter now contains none of the penetration system and runs perfectly well without it.

</details>

### Smaller things

- A report warning about a plug material that could not be driven described the wrong loss. It now
  says what that material actually keeps and what it does not.
- The rig setting that took a humanoid Jaw mapping off a bone that is not a jaw was never writable
  and always on. It is a rule now, rather than a setting implying otherwise.

---

## YAPS `YAPS-4.5.0-adult.unitypackage`

**Download this one too if you want the 18+ features.** Adults only. It installs beside the
converter, in the same folder, in either order, and still stands alone for building penetration on
any ChilloutVR avatar or prop with no VRChat SDK and no converter.

### A plug can now find a socket off the screen

This is the release's real change, and it is invisible until you use it.

Until now a plug found a socket two ways: the marker lights every DPS-style socket carries, and a
contact channel. Lights are limited to four at a time and are coarse; the contact channel is
computed on the wearer's own machine, so anything driven from it costs sync bits to show anyone
else. Both stay, and both still work exactly as before.

The new way: **every socket publishes where it is into a tiny block of pixels at the corner of the
frame, drawn before the scene so the scene covers it, and every plug reads that block.** No sync
bits. No contact pairs out of ChilloutVR's budget. Sub-millimetre, updated every frame, and it
crosses between avatars, so a plug reads other people's sockets the same way it reads its own.

What that gets you:

- **A plug follows a whole path, not the nearest point.** With more than one socket on its way, it
  passes through each in turn instead of bending at the first.
- **A socket opens to the deepest plug in reach**, rather than the closest one. Two plugs at one
  socket no longer means the second passes through mesh that never opened.
- **It works where you actually see yourself**: both eyes in VR, desktop and personal mirrors, and
  on other people's clients, all confirmed in game rather than in the editor.
- **Nothing is required of the other person.** A plug still reads an unconverted DPS avatar's
  sockets, and YAPS sockets still emit the same marker ranges every existing system decodes, so
  both directions work with content that has never heard of this tool.
- **A viewer with custom shaders switched off sees the avatar and nothing else.** The published
  pixels cannot be drawn by a replacement shader at all.

Avatars built by hand in *Tools ▸ YAPS ▸ Setup* get this too, not only converted ones, and the
material panel now carries a row for it, so you can see it is on and switch it off on one plug to
compare against the older route.

**Not covered:** the personal self-portrait camera renders too small a picture to carry the block,
so a plug in that view shows whatever the contact channel resolved. Crowded instances have not
been measured with this yet, though the arithmetic says the space is nowhere near full at any
population the platform actually reaches.

<details>
<summary>How it works, and what was hard about it</summary>

Sockets draw a handful of tiny quads into a fixed rectangle of the frame, at a queue that puts
them before the scene, so scene geometry covers them and nothing is visible. A clear quad on the
avatar root runs before those, since an empty cell would otherwise read as whatever the screen
already held. A grab pass captures the rectangle and each plug's shader decodes it.

A socket hashes into one of 4096 cells per level, with a second home if the first is taken, and
the protocol version rides inside the cell tag rather than in a pixel, so a socket speaking a
different version is dropped by a check that was already running. Positions come back at about a
tenth of a millimetre.

The awkward parts, in case they explain something you saw in an earlier build: render targets are
not the screen, and the block wrote upside down into every one of them until the write was made
target-agnostic; the corners of those quads had to leave vertex positions entirely, so a client
with custom shaders off has nothing to draw; and the block had to move ahead of the scene, which
is what stopped it appearing in self portraits, including other people's.

</details>

### Fixes

Each of these could put a plug's shape wrong, and none of them announced itself.

- **A plug modelled across more than one material** had only its largest material patched. The rest
  stayed rigid while the shaft bent. Every material a plug's triangles use is patched now.
- **Several plugs painted from one material** shared a single bake, so all but the last deformed
  against a mesh they are not, at a length that is not theirs. The same went for two different
  materials that happen to share a name, and for two objects with the same name under one parent.
  Each bake is keyed to the thing it measured now.
- **A plug measured from the wrong starting point.** The component is usually placed on a parent
  above the shaft, so everything else hanging off that parent was measured as part of the plug: the
  length spanned it and the bend began behind it. The shaft is found by following the bone chain
  that reaches furthest now, with two guards, because a wrong root is worse than an inherited one.
- **A material-swap animation put the unbaked material back**, on a plug or a socket, when the clip
  moved a material between slots or the part sat in a slot other than the first. Swaps are followed
  by which material they carry now, not by where it sat.
- **A plug with many materials silently lost the last of them.** The component that drives them
  declares sixteen and nothing counted. The room is measured now, and a plug that will not fit says
  so instead of dropping slots in silence.
- **A socket refused a plug arriving from one particular direction**, because a leftover facing test
  threw away exactly the case the deform is written to handle.
- **A converted socket never recorded which version built it**, so the "built by an older version,
  rebuild this" notice could never fire on the avatars most likely to need it.

### Re-apply YAPS works without the VRChat SDK

*Tools ▸ YAPS ▸ Re-apply YAPS to selected materials* puts the deform back on a material whose
shader changed since it was baked, which is what unlocking a shader to edit a colour does. It was
documented for anyone using the toolkit on its own, and quietly needed the VRChat SDK to appear in
the menu at all. It now appears wherever this package is installed.

<details>
<summary>What is inside this package</summary>

The penetration system whole, its setup window, the prefab makers, the ChilloutVR Toolkit, and the
passes that rebuild penetration during a conversion. Those passes do nothing on their own: they
wake up only when the converter is installed beside them and you convert a VRChat avatar.

</details>
