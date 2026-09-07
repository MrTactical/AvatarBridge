# 4.5.0 - Two downloads: the converter, and penetration as an 18+ add-on

No need to reconvert. One release, two packages, and a changelog each.

---

## AvatarBridge `AvatarBridge-4.5.0-public.unitypackage`

The converter and the ChilloutVR Toolkit. This is the download for everyone.

**Penetration is no longer in this package.** Nothing else about a conversion has changed. If an
avatar carries DPS, TPS or SPS and the add-on below is not installed, the penetration is removed,
the same as choosing *Remove* has always done, and the report says which system the avatar had
and where to get the add-on. The window says the same in place of a choice it cannot offer.

Everything else converts exactly as it did in 4.4.3.

<details>
<summary>Why it moved out</summary>

The converter is a general avatar tool and a good half of the people using it never touch
penetration. Bundling an adult system with it meant every download carried one, and there was no
honest way to label the package for who it was for. Two packages, each labelled, both installing
to the same `Assets/AvatarBridge` folder and sharing the same code, sorts both problems: the
converter now contains none of the penetration system and runs perfectly well without it.

</details>

---

## YAPS `YAPS-4.5.0-adult.unitypackage`

**Download this one too if you want the 18+ features.** Adults only.

Install it beside the converter and an avatar's penetration is rebuilt for ChilloutVR exactly as
before: the plug bends into sockets, sockets open around plugs, the author's tuning carried
across. Either install order works, and the two packages share one folder rather than duplicating
anything.

It also stands on its own, with no VRChat SDK and no converter, for building penetration on any
ChilloutVR avatar or prop: *Tools > YAPS > Setup*, same system, same shader.

### Fixed: Re-apply YAPS works without the VRChat SDK

*Tools > YAPS > Re-apply YAPS to selected materials* puts the deform back on a material whose
shader changed since it was baked, which is what unlocking a shader to edit a colour does. It was
documented for anyone using the toolkit on its own, and quietly needed the VRChat SDK to appear in
the menu at all. It now appears wherever this package is installed.

<details>
<summary>What is inside</summary>

The penetration system whole, its setup window, the prefab makers, and the passes that rebuild
penetration during a conversion. Those passes do nothing on their own: they wake up only when the
converter is installed beside them and you convert a VRChat avatar.

</details>
