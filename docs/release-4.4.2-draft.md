# 4.4.2

A second hotfix for the same report as 4.4.1, which turned out to be two bugs stacked on
each other. 4.4.1 fixed the first and uncovered this one.

## A toggle that swaps the material put your plug back to unbaked

If any animation on your avatar assigns a material to the same mesh slot the bake replaced —
a skin picker, a body variant, an NSFW toggle — the plug straightened the moment you pressed
Play, and the YAPS window said **"not baked yet"** even though you had just baked it.

Convert again on 4.4.2, or set the plug up again if you built it natively. Nothing about
your bake or your avatar needs redoing.

**If you are stuck on 4.4.1:** find the animation that assigns the plug's material and point
it at the `_YAPS_` copy by hand, or turn that toggle off while you test.

<details>
<summary>What was actually happening</summary>

Baking repoints the renderer's material slot at a patched copy that carries the deform and
the baked mesh data. That holds right up until the animator runs.

An animation that assigns a material to that same slot hands it straight back to the
material you baked FROM, which has no deform and no bake in it. So on entering play mode the
plug goes to its rest shape, and the tool reports it as never baked — because from the
material's side there is genuinely nothing to find.

Every repoint is now recorded against its **renderer and slot**, and the clips are made to
follow. Scoped that tightly deliberately: the original material is usually worn by other
meshes too, and those have no bake of their own, so handing them a plug's deform would bend
the wrong mesh.

The report names how many swaps were repointed.

</details>

## Both fixes cover the native toolkit too

We missed a couple of things on the first pass and they are in here as well.

The fix above first shipped as a converter step, so it only ran when converting an avatar
from VRChat. **Setting a plug or socket up natively with the YAPS window replaces the same
slot on the same kind of renderer**, so the same toggle put the same unbaked material back —
and if anything it is likelier there, since an avatar you are building on already has its
toggles. That now runs on both paths, for sockets as well as plugs.

4.4.1's locked-Poiyomi fix already covered the native path, because both routes patch
shaders through the same code. Nothing to do there; noting it so the question does not have
to be asked.

Two more things turned up while checking the rest of that seam, and both are in here.

**A plug with a size slider could bend against the wrong rest pose.** A shader cannot read a
blendshape weight, so every animation that moves one of the plug's own shapes has to write
the same value onto its material too. Native setup was looking for those animations in the
wrong place — the Animator's own slot, rather than what ChilloutVR actually uploads — and on
an avatar whose controller lives where CVR expects it, that found nothing and mirrored
nothing. Quietly. Your size slider would move the mesh and the deform would keep measuring
against the shape it started at.

**And a swap in a clip that is not yours is now reported instead of edited.** A clip inside a
package, or the CCK's own, is shared with every project that has it, so we leave it alone —
but you get told, with the clip named, because a plug that works until somebody presses one
particular toggle is worse than one that says what it could not reach.

<details>
<summary>Why the native path needed its own version</summary>

The converter walks the merged controller it has just built. The native builder has no such
thing, so it reads what the avatar actually runs: `avatar.overrides` first, then
`avatarSettings.baseController`, then every Animator under the root. A clip only has to be
reachable to fire, and a swap firing from a controller nobody expected breaks the plug just
the same.

</details>

Found on the same avatar as 4.4.1, once the shader fix let the real fault show. The plug's
material read `Dick HD 1 _YAPS_` in edit mode and `Dick HD 1` in play mode, with the shader
reverting to the author's Poiyomi alongside it.
