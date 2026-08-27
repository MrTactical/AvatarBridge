# 4.4.1

A hotfix for one bug, reported the day 4.4.0 shipped.

## A plug on a locked Poiyomi lost its deform the moment you pressed Play

If your plug's material was **already locked** in Poiyomi before you converted, the plug
looked right in the scene and went back to its undeformed shape as soon as you entered play
mode. It reads exactly like the bake coming undone, and the bake was never the problem.

Nothing about your avatar or your bake needs redoing. Convert again on 4.4.1 and it holds.

**If you are stuck on 4.4.0:** unlock the plug's material in Poiyomi, convert, and it works.

<details>
<summary>What was actually happening</summary>

Poiyomi has an auto-lock sweep that takes any material whose shader looks unlocked and
resolves every property in it to a constant. AvatarBridge already knew to stay out of its
way: the shader it writes for a plug is named `Hidden/Locked/YAPS/...` when the source is a
Thry shader, because that prefix is the exact test the sweep uses for "already locked".

It decided whether a source was a Thry shader by looking for two marker properties,
`shader_is_using_thry_editor` and `ThryShaderOptimizerLockButton`. **Locking strips both.**
So an already-locked Poiyomi looked like no Thry shader at all, the copy was named plainly,
and the sweep locked it — resolving `_YAPS_SocketPos`, `_YAPS_SocketFlags` and every other
property the deform reads into numbers. A declaration became a constant:

```
float4 _YAPS_SocketPos;   ->   float4 0.5;
'float4' already defined as a type
syntax error: unexpected integer constant
```

The shaders that most needed the protective name were the only ones that could not get it.

The reason it waited for play mode is that the failing passes are the **shadow casters**.
Unity compiles shader variants when something asks for them, and nothing in the scene view
asks for those. Entering play mode does, all three patched passes fail at once, and the plug
loses its deform in front of you.

An already-locked source is now a third signal for "this is a Thry shader", read off the
shader's own name.

</details>

Found by a user on 4.4.0 with a locked Poiyomi 8.1 plug. The regression corpus has no
already-locked plug in it, which is why this shipped.
