# Chain presets

AvatarBridge sorts every PhysBone into one of 28 **chain classes**, then loads a MagicaCloth2
preset for that class. This folder is where those presets live, and where you override them.

Lookup order, first hit wins:

1. `MC2_AvatarBridge_<Class>.json` — anywhere in the project
2. the class's MagicaCloth2 fallback — one of MagicaCloth2's own shipped presets
3. MagicaCloth2's component defaults

## Authoring one

1. Convert an avatar, find a chain of the kind you want to tune, and tune its MagicaCloth
   component **in play mode** until it feels right.
2. Press **Save** on the component's **Preset** dropdown.
3. Save it here as `MC2_AvatarBridge_<Class>.json`.

MagicaCloth2 writes exactly the JSON AvatarBridge reads, so there's no conversion step. It takes
effect on the next conversion.

## What ships here, and what falls back

MagicaCloth2's own presets cover *cloth* — hair, skirt, cape, tail, three generic springs. Where
one of those already fits, **nothing ships here** and the class falls straight through to it.
Fourteen classes have no MagicaCloth2 equivalent at all, and those are the files in this folder.

Each shipped preset is a real MagicaCloth2 preset with **four fields changed** — the same four
MagicaCloth2's own author varies between presets. Radius, inertia, shape restoration and culling
are untouched, exactly as the base had them.

| class | based on | gravity | damping | angle stiffness | vel. attenuation | reasoning |
|---|---|---|---|---|---|---|
| `Breast` | Soft Spring | 0 | 0.25 | 0.30 | 0.70 | no gravity or the bone droops permanently; firmer than Soft Spring so it settles instead of wobbling on |
| `Butt` | Soft Spring | 0 | 0.30 | 0.35 | 0.65 | heavier, less travel than breast |
| `Belly` | Soft Spring | 0 | 0.22 | 0.25 | 0.75 | softest and slowest of the body group |
| `Thigh` | Middle Spring | 0 | 0.30 | 0.40 | 0.60 | tightest — thighs barely travel |
| `Ear` | Soft Spring | 1.0 | 0.15 | 0.25 | 0.70 | light, quick to settle, a little droop |
| `Whisker` | Short Hair | 0.5 | 0.12 | 0.20 | 0.60 | very thin, almost weightless |
| `Fluff` | Short Hair | 1.0 | 0.15 | 0.25 | 0.65 | fur tufts — light, settles fast |
| `Ahoge` | Short Hair | 1.0 | 0.08 | 0.12 | 0.70 | a single springy strand; lowest damping here so it bounces |
| `Wing` | Cape | 4.0 | 0.15 | 0.30 | 0.65 | large like a cape but structured, so stiffer |
| `Horn` | Hard Spring | 0 | 0.35 | 0.70 | 0.35 | nearly rigid — barely moves |
| `TailShort` | Tail | 0 | 0.10 | 0.25 | 0.55 | stiffer than the long Tail preset; stubby tails don't whip |
| `Ribbon` | Accessory | 2.0 | 0.10 | 0.12 | 0.65 | very light cloth, low stiffness so it flutters |
| `Sleeve` | Accessory | 2.0 | 0.12 | 0.18 | 0.65 | cloth, but anchored along the arm |
| `ClothStrip` | Accessory | 3.0 | 0.10 | 0.15 | 0.70 | generic hanging panel |

**These are a starting point, not tuned results.** They're reasoned from MagicaCloth2's own values
and from how each kind of chain should behave — nobody has watched them move. Re-save any of them
over the top once you have.

Classes that fall through to MagicaCloth2's presets, unchanged: `HairFront` → Front Hair,
`HairLong` → Long Hair, `HairShort` → Short Hair, `TailLong` → Tail, `Skirt` → Skirt, `Dress` →
Soft Skirt, `Cape` → Cape, `Earring` / `Necklace` / `Charm` → Accessory, and the four
name-less classes `Floaty` / `Loose` → Soft Spring, `Springy` → Middle Spring, `Stiff` → Hard
Spring.

## After the preset

Three PhysBone facts are applied on top, because they mean the same thing in both systems:
gravity of zero stays zero, negative gravity points up, and `immobile` becomes world influence
(MagicaCloth2 measures the same thing inverted). So a preset's gravity is a *default* — a chain
whose author gave it none keeps none. Turn that off with **Fit the preset to the PhysBone**.

## Caveats

- **The classifier reads bone names.** An unhelpfully named chain (`p_thing.001`) falls through to
  the name-less classes, which is a coarse guess. The report always names the class it read.
- **Presets are global.** A file here applies to every avatar you convert. If one avatar wants
  something different, tune that cloth after converting.
- **Tune in play mode.** MagicaCloth2 builds at runtime, so edit-mode values tell you very little.
