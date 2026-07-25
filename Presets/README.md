# Chain presets

Drop a tuned MagicaCloth2 preset in here and AvatarBridge will use it for every chain of that
kind, on every avatar you convert.

## Why

AvatarBridge doesn't derive MagicaCloth2 values from PhysBone values — the two are different
kinds of simulation, so the numbers don't correspond (the main README explains this). What it does
instead is work out **what kind of chain** each PhysBone is, and load a preset someone tuned for
that kind.

MagicaCloth2 ships presets for cloth types — hair, skirt, cape, tail, three generic springs. Those
are what every class falls back to today. They say nothing about breasts, ears or belly jiggle,
which is most of what a VRChat avatar's PhysBones actually are.

That's the gap this folder fills. **Nothing here is guessed** — if a class has no file, it uses
MagicaCloth2's own preset exactly as before.

## How to author one

1. Convert an avatar, then find a chain of the kind you want to tune — say a breast chain.
2. Select its `MagicaCloth_*` object and tune the MagicaCloth component in play mode until it
   feels right.
3. Press **Save** on the component's **Preset** dropdown.
4. Save it into this folder as `MC2_AvatarBridge_<Class>.json`, using a class name from the table
   below.

MagicaCloth2's Save button writes exactly the JSON AvatarBridge reads, so there's no conversion
step. The file takes effect on the next conversion.

Presets are found anywhere in the project, so you can keep them outside this folder if you'd
rather — the file **name** is what matters.

## Classes

Each row is a file you could add. The fallback column is what's used until you do.

| `MC2_AvatarBridge_…` | matched by | falls back to |
|---|---|---|
| `Breast` | breast, boob, oppai, bust | Soft Spring |
| `Butt` | butt, booty, ass, glute, rear | Soft Spring |
| `Belly` | belly, tummy, tum, stomach, gut | Soft Spring |
| `Thigh` | thigh | Soft Spring |
| `Ear` | ear *(not earring)* | Soft Spring |
| `Whisker` | whisker, beard | Short Hair |
| `HairFront` | hair + front / bang / fringe | Front Hair |
| `HairLong` | hair with 5+ bones, twintail, ponytail, braid | Long Hair |
| `HairShort` | any other hair | Short Hair |
| `Ahoge` | ahoge, antenna | Short Hair |
| `TailLong` | tail with 5+ bones | Tail |
| `TailShort` | shorter tail | Tail |
| `Wing` | wing | Cape |
| `Horn` | horn, antler | Hard Spring |
| `Skirt` | skirt | Skirt |
| `Dress` | dress, apron | Soft Skirt |
| `Cape` | cape, cloak, mantle, coat, hood | Cape |
| `Ribbon` | ribbon, bow | Accessory |
| `Sleeve` | sleeve, cuff | Accessory |
| `ClothStrip` | cloth, sash, strap, belt, tassel, scarf, flap | Accessory |
| `Earring` | earring | Accessory |
| `Necklace` | necklace, pendant, choker, collar, chain | Accessory |
| `Charm` | charm, jewel, bell, tag, zipper, keychain | Accessory |
| `Floaty` | *no name match* — PhysBone had no gravity and high immobile | Soft Spring |
| `Stiff` | *no name match* — pull/stiffness ≥ 0.6 | Hard Spring |
| `Springy` | *no name match* — pull/stiffness ≥ 0.3 | Middle Spring |
| `Loose` | *no name match* — anything else | Soft Spring |

The conversion report names the class it read for every chain:

> `tail — BoneCloth on the MagicaCloth2 "Tail" preset (read as a "TailLong" chain), 3 collider(s).`

So if a chain is classified wrongly, the report says so before you have to work it out from how it
moves.

## Caveats

- **The classifier reads bone names.** An unhelpfully named chain (`p_thing.001`) falls through to
  the character classes, which is a coarse guess. That's why the class is in the report.
- **Presets are global.** A file here applies to every avatar you convert. If one avatar wants
  something different, tune that cloth directly after converting.
- **Tune in play mode.** MagicaCloth2 builds at runtime, so edit-mode values tell you very little
  about how a chain will actually move.
