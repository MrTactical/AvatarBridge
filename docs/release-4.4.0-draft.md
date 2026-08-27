# 4.4.0

**Plugs now find sockets without marker lights.** The contact channel has been in the toolkit for
months and had never once worked in game, on anybody's avatar. It does now. If you have avatar
lights turned off, or a socket has run out of light slots, a plug still bends toward it.

**A socket with lights and contacts is smoother than either alone.** They were fighting rather
than cooperating: the light gave the plug a steady target while the strength kept shaking, so a
socket carrying both trembled exactly as hard as one carrying contacts alone and the light looked
like it was doing nothing.

**The plug goes back to straight when you walk away.** It used to keep the last position it was
given and stay bent, or snap to an angle from several metres off.

**A ring stays a ring.** Only a hole ever wrote the flag that tells them apart, so a ring that
arrived after a hole was treated as a hole — and a freshly spawned ring prop would swallow a plug
whole.

**Whole-avatar and multi-material plugs work properly.** A plug whose mesh spans several
materials, or one rooted at the armature so it covers the whole body, used to bake one material
and leave the rest behind. The head would bend ninety degrees while the body sat still.

**A plug that ships switched off is no longer invisible to the toolkit.** Most avatars hide the
plug until a toggle brings it in. The preview, the baked-plug count and the knob sync all walked
straight past those, which looked exactly like a broken feature.

**Poiyomi-locked materials survive the bake.** A patched lock kept a name the build could not
find, so the material never reached the upload.

**A socket built by an older version now says so.** Nothing revisits a socket once it is made, so
every fix reached new ones and no existing one, with no way to tell them apart. Its inspector now
leads with which version built it and what to do about it.

<details>
<summary>Everything else, in detail</summary>

**The channel, which is the headline**

- The channel was built into a controller ChilloutVR never uploads. The toolkit followed the
  Animator's own slot; the client uploads `avatar.overrides`. On an avatar whose generated
  Advanced Settings folders have drifted apart, those can be different assets with the *same
  display name*, so nothing in the inspector could show it. The build log now names both when
  they disagree.
- Every trigger is a box, and its size is the full size. They were being halved on a belief that a
  distance-only trigger becomes a sphere, which the client does not do.
- The trigger writes the synced parameter directly, so a remote viewer sees the same bend the
  wearer does.
- Channel space needs one frame for the mesh, not one per vertex, and engagement must not be asked
  per vertex either.
- A re-bake set five of the seven fields a bake sets.
- Engagement follows whoever provided the position. It was measured from the channel's gap while
  the light replaced the position afterwards, so a lit socket had a steady target and a trembling
  strength.
- The axis triggers had no exit task, so a position stuck at the edge of the box after a socket
  left. They reset to the far edge now, deliberately not the middle: the middle decodes to the
  plug's own base, which is the strongest bend there is.
- A ring asserts the hole flag as well as a hole clearing it, so neither depends on an exit that
  may never fire — a prop despawning inside the trigger, the plug being toggled off, an instance
  change.
- Smoothing default lowered from 0.05 to 0.02, measured: the channel resolves to about a
  millimetre and the deform is sensitive enough to show one step of it.

**Plugs across several materials**

- Bake every material the plug's vertices touch, not just the first.
- A plug rooted at the armature is every mesh, not one.
- The length override reached one renderer out of two.
- A carried mesh takes its carrier's engagement gate rather than its own, so a collar no longer
  bends before the body it is attached to.
- A carried mesh is no longer listed as a plug of its own.

**Marker lights**

- The nearest light is not the right light when there are two.
- Overrun is a ring's switch and only holes were reading it.
- A socket sitting on the avatar root has no direction to be behind.

**Toggles and animation**

- Two switches on one property, and the default-off one won. YAPS's own menu toggle fought the
  avatar's erection slider for the same material property.
- An erection slider drove a component the enable mirror did not recognise.
- The toolkit told every plug its avatar had no sockets.

**Materials and shaders**

- A patched Poiyomi keeps Poiyomi's name, or it never reaches the build.
- A shader's own editor needs its own properties, all of them.
- A generated material lands on the same name twice, so a rebake does not multiply materials.
- A warning when a material is behind the toolkit and needs rebaking.

**Props**

- Build was re-creating the component the user had just deleted.
- The tool pinned the light slot it was about to widen.
- The ring-and-socket prop prefab has a button in the Setup window, not only a menu entry.

**The editor**

- The preview can keep running in Play mode, which on most avatars is the only state where the
  plug exists at all, and the only way to see a posed one.
- The preview socket is built with marker lights again, so it shows the route you actually get
  rather than the fallback.
- Two checkbox labels overflowed their tick.
- The debug View dropdown keeps Resolved by, Gap to socket, Engagement and Socket facing. Two
  entries that existed only for chasing one bug are gone, and the help text is no longer nine
  hundred words of notes.

**Scale**

- A bone's scale was being spent twice.
- The bake scale is mirrored as a ratio to the bake pose, so a plug on a bone that was not sitting
  at 1 when baked no longer squashes.

</details>
