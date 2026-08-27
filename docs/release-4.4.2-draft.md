# 4.4.2

A second hotfix for the same report as 4.4.1, which turned out to be two bugs stacked on
each other. 4.4.1 fixed the first one and uncovered this.

## A toggle that swaps the material put your plug back to unbaked

If any animation on your avatar assigns a material to the same mesh slot the bake replaced —
a skin picker, a body variant, an NSFW toggle — the plug straightened the moment you pressed
Play, and the YAPS window said **"not baked yet"** even though you had just baked it.

Convert again on 4.4.2. Nothing about your bake or your avatar needs redoing.

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

The report now names how many swaps were repointed.

</details>

Found on the same avatar as 4.4.1, once the shader fix let the real fault show. The plug's
material read `Dick HD 1 _YAPS_` in edit mode and `Dick HD 1` in play mode, with the shader
reverting to the author's Poiyomi alongside it.
