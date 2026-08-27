# 4.4.0

**Plugs now find sockets without marker lights.** The contact channel has been in
the toolkit for months and had never once worked in game, on anybody's avatar.
It does now. If you have avatar lights turned off, or a socket has run out of
light slots, a plug still bends toward it.

**Whole-avatar and multi-material plugs work properly.** A plug whose mesh spans
several materials, or one rooted at the armature so it covers the whole body,
used to bake one material and leave the rest behind. The head would bend ninety
degrees while the body sat still. All of them are baked and configured now.

**A plug that ships switched off is no longer invisible to the toolkit.** Most
avatars hide the plug until a toggle brings it in. The preview, the baked-plug
count and the knob sync all walked straight past those, which looked exactly
like a broken feature.

**Poiyomi-locked materials survive the bake.** A patched lock kept a name the
build could not find, so the material never reached the upload.

**The editor preview shows what the game actually does.** It used to run a
simpler route than the one the game runs, so a channel that had never worked
looked perfect on your screen.

<details>
<summary>Everything else, in detail</summary>

**The channel, which is the headline**

- The channel was built into a controller ChilloutVR never uploads. The toolkit
  followed the Animator's own slot; the client uploads `avatar.overrides`. On an
  avatar whose generated Advanced Settings folders have drifted apart, those can
  be different assets with the *same display name*, so nothing in the inspector
  could show it. The build log now names both when they disagree.
- Every trigger is a box, and its size is the full size. They were being halved
  on a belief that a distance-only trigger becomes a sphere, which the client
  does not do.
- The trigger writes the synced parameter directly, so a remote viewer sees the
  same bend the wearer does.
- Channel space needs one frame for the mesh, not one per vertex, and engagement
  must not be asked per vertex either.
- A re-bake set five of the seven fields a bake sets.
- The axis triggers had no exit task, so a position stuck at the box edge after a
  socket left and the next one to arrive snapped the plug toward it for a frame.
- Smoothing default lowered from 0.05 to 0.02, measured: the channel resolves to
  about a millimetre and the deform is sensitive enough to show one step.

**Plugs across several materials**

- Bake every material the plug's vertices touch, not just the first.
- A plug rooted at the armature is every mesh, not one.
- The length override reached one renderer out of two.
- A carried mesh takes its carrier's engagement gate rather than its own, so a
  collar no longer bends before the body it is attached to.
- A carried mesh is no longer listed as a plug of its own.

**Marker lights**

- The nearest light is not the right light when there are two.
- Overrun is a ring's switch and only holes were reading it.
- A socket sitting on the avatar root has no direction to be behind.

**Toggles and animation**

- Two switches on one property, and the default-off one won. YAPS's own menu
  toggle fought the avatar's erection slider for the same material property.
- An erection slider drove a component the enable mirror did not recognise.
- The toolkit told every plug its avatar had no sockets.

**Materials and shaders**

- A patched Poiyomi keeps Poiyomi's name, or it never reaches the build.
- A shader's own editor needs its own properties, all of them.
- A generated material lands on the same name twice, so a rebake does not
  multiply materials.
- A warning when a material is behind the toolkit and needs rebaking.

**Props**

- Build was re-creating the component the user had just deleted.
- The tool pinned the light slot it was about to widen.

**Debug views**

- The "Resolved by" view existed everywhere except the shader that draws it.
- New views for engagement, socket facing, the decoded gap, and the GPU's own
  inputs.
- A warning when a debug view would ship with the avatar.

**Scale**

- A bone's scale was being spent twice.
- The bake scale is mirrored as a ratio to the bake pose, so a plug on a bone
  that was not sitting at 1 when baked no longer squashes.

</details>
