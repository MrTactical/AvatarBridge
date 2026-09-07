# 4.5.0 - Penetration is a separate 18+ download

No need to reconvert. If your avatars use penetration, download the add-on as well as the
converter, then convert as usual.

## Two downloads instead of one

The converter and the ChilloutVR Toolkit are one package. Penetration is now a second package,
`YAPS-<version>-adult.unitypackage`, and it is adults only.

Nothing about how the conversion works has changed. Install both and an avatar's penetration is
rebuilt for ChilloutVR exactly as before, in the same folder, in either install order. Install
only the converter and the penetration is removed instead, the same as choosing *Remove*, and the
report says which system the avatar had and where to get the add-on. The window says so too, in
place of a choice it cannot offer.

The add-on still stands on its own for ChilloutVR avatars and props with no VRChat history: same
setup window, same shader, no VRChat SDK needed.

<details>
<summary>Why split it</summary>

The converter is a general avatar tool and a good half of the people using it never touch
penetration. Bundling an adult system with it meant every download carried one, and there was no
honest way to label the package for who it was for. Two packages, each labelled, each installing
to the same folder and sharing the same code, sorts both problems.

The converter no longer contains a byte of the penetration system, and compiles and runs without
it. What ships in the add-on is the system whole, plus the passes that rebuild it during a
conversion, which do nothing unless the converter is installed beside them.

</details>

## Re-apply YAPS works without the VRChat SDK

*Tools > YAPS > Re-apply YAPS to selected materials* puts the deform back on a material whose
shader you have changed since baking, which is what unlocking a shader to edit a colour does. It
was documented for anyone using the toolkit on its own, and quietly needed the VRChat SDK to
appear in the menu. It now appears wherever the add-on is installed.
