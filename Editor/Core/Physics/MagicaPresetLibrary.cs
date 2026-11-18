#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using MagicaCloth2;

namespace AvatarBridge
{
    // Classifies a PhysBone chain and loads a MagicaCloth2 preset for it. Deriving MC2 values
    // from PhysBone values does not work (see MagicaClothWriter); MC2 parameters only make
    // sense as a tuned set. Lookup, first hit wins: "MC2_Preset_Bridge_<Class>.json" anywhere
    // in the project, then the class's shipped MC2 fallback, then component defaults.
    // To author one: Save from the MagicaCloth inspector's Preset dropdown, named after the
    // class. See Assets/AvatarBridge/Presets/README.md.
    public static class MagicaPresetLibrary
    {
        // A kind of chain, and the MagicaCloth2 preset to use until one is tuned for it.
        public class ChainClass
        {
            public readonly string Name;
            public readonly string Fallback;
            public ChainClass(string name, string fallback) { Name = name; Fallback = fallback; }
        }

        // MagicaCloth2's own presets, in Assets/MagicaCloth2/Res/Preset.
        const string Tail = "MC2_Preset_Tail";
        const string Cape = "MC2_Preset_Cape";
        const string Skirt = "MC2_Preset_Skirt";
        const string SoftSkirt = "MC2_Preset_SoftSkirt";
        const string FrontHair = "MC2_Preset_FrontHair";
        const string LongHair = "MC2_Preset_LongHair";
        const string ShortHair = "MC2_Preset_ShortHair";
        const string Accessory = "MC2_Preset_Accessory";
        const string SoftSpring = "MC2_Preset_SoftSpring";
        const string MiddleSpring = "MC2_Preset_MiddleSpring";
        const string HardSpring = "MC2_Preset_HardSpring";

        public const string CustomPrefix = "MC2_Preset_Bridge_";

        // Every class the classifier can produce, with the MagicaCloth2 preset it falls back to.
        // Kept in one place so the set is greppable and the "what could I tune?" list is obvious.
        static readonly ChainClass
            ClsBreast = new ChainClass("Breast", SoftSpring),
            ClsButt = new ChainClass("Butt", SoftSpring),
            ClsBelly = new ChainClass("Belly", SoftSpring),
            ClsThigh = new ChainClass("Thigh", MiddleSpring),
            ClsEar = new ChainClass("Ear", SoftSpring),
            ClsWhisker = new ChainClass("Whisker", ShortHair),
            ClsFluff = new ChainClass("Fluff", ShortHair),
            ClsHairFront = new ChainClass("HairFront", FrontHair),
            ClsHairLong = new ChainClass("HairLong", LongHair),
            ClsHairShort = new ChainClass("HairShort", ShortHair),
            ClsAhoge = new ChainClass("Ahoge", ShortHair),
            ClsTailLong = new ChainClass("TailLong", Tail),
            ClsTailShort = new ChainClass("TailShort", Tail),
            ClsWing = new ChainClass("Wing", Cape),
            ClsHorn = new ChainClass("Horn", HardSpring),
            ClsSkirt = new ChainClass("Skirt", Skirt),
            ClsDress = new ChainClass("Dress", SoftSkirt),
            ClsCape = new ChainClass("Cape", Cape),
            ClsRibbon = new ChainClass("Ribbon", Accessory),
            ClsSleeve = new ChainClass("Sleeve", Accessory),
            ClsClothStrip = new ChainClass("ClothStrip", Accessory),
            ClsEarring = new ChainClass("Earring", Accessory),
            ClsNecklace = new ChainClass("Necklace", Accessory),
            ClsCharm = new ChainClass("Charm", Accessory),
            ClsFloaty = new ChainClass("Floaty", SoftSpring),
            ClsStiff = new ChainClass("Stiff", HardSpring),
            ClsSpringy = new ChainClass("Springy", MiddleSpring),
            ClsLoose = new ChainClass("Loose", SoftSpring);

        public static ChainClass Classify(PhysBoneChainData data)
        {
            int bones = CountBones(data.Root);

            var byName = ClassifyByName(data.Root.name, bones);
            if (byName != null)
            {
                return byName;
            }

            // Ask the ancestors: add-on prefabs put meaningless bone names under a container
            // named for what they are (unnamed hair came out floaty as a generic chain).
            // Stops at the Animator so the avatar's own name never matches.
            for (var p = data.Root.parent; p != null && p.GetComponent<Animator>() == null; p = p.parent)
            {
                var byAncestor = ClassifyByName(p.name, bones);
                if (byAncestor != null)
                {
                    return byAncestor;
                }
            }

            // --- nothing anywhere in the names: fall back to the PhysBone's character ------
            // A chain the author gave no gravity was never meant to fall, whatever it is.
            if (Mathf.Approximately(data.Gravity, 0f) && data.Immobile > 0.5f)
            {
                return ClsFloaty;
            }
            float restore = Mathf.Clamp01(Mathf.Max(data.Pull, data.Stiffness));
            if (restore >= 0.6f)
            {
                return ClsStiff;
            }
            return restore >= 0.3f ? ClsSpringy : ClsLoose;
        }

        static ChainClass ClassifyByName(string rawName, int bones)
        {
            string n = rawName.ToLowerInvariant();
            var t = Tokenize(rawName);

            // --- hair, before anything containing "tail" ---------------------------------
            if (Has(n, "ahoge") || HasToken(t, "antenna"))
            {
                return ClsAhoge;
            }
            if (HasToken(t, "bang") || Has(n, "hair", "twintail", "ponytail", "pigtail", "braid", "fringe"))
            {
                if (HasToken(t, "front", "bang") || Has(n, "fringe"))
                {
                    return ClsHairFront;
                }
                return bones >= 5 || Has(n, "twintail", "ponytail", "pigtail", "braid", "long")
                    ? ClsHairLong : ClsHairShort;
            }

            // --- head & face, earring before ear ------------------------------------------
            if (Has(n, "earring"))
            {
                return ClsEarring;
            }
            if (HasToken(t, "ear"))
            {
                return ClsEar;
            }
            if (HasToken(t, "whisker", "beard"))
            {
                return ClsWhisker;
            }
            if (HasToken(t, "tuft", "fluff", "fur", "mane", "ruff"))
            {
                return ClsFluff;
            }
            if (HasToken(t, "horn", "antler"))
            {
                return ClsHorn;
            }

            // --- appendages ----------------------------------------------------------------
            if (HasToken(t, "tail"))
            {
                return bones >= 5 ? ClsTailLong : ClsTailShort;
            }
            if (HasToken(t, "wing"))
            {
                return ClsWing;
            }

            // --- garments. A named PART beats the garment it hangs off: "Apron_Ribbon" is the
            //     ribbon simulating, not the apron. Generic "cloth" loses to a named garment.
            if (Has(n, "ribbon") || HasToken(t, "bow"))
            {
                return ClsRibbon;
            }
            if (Has(n, "sleeve") || HasToken(t, "cuff"))
            {
                return ClsSleeve;
            }
            if (Has(n, "skirt"))
            {
                return ClsSkirt;
            }
            if (Has(n, "dress", "apron"))
            {
                return ClsDress;
            }
            if (HasToken(t, "cape", "cloak", "mantle", "coat", "hood"))
            {
                return ClsCape;
            }
            if (Has(n, "tassel", "scarf") || HasToken(t, "cloth", "sash", "strap", "belt", "strip", "flap"))
            {
                return ClsClothStrip;
            }

            // --- jewellery hanging off the body, before the body part it hangs from: a piercing
            //     under a breast bone is a charm on a chain, not a soft body
            if (Has(n, "piercing"))
            {
                return ClsCharm;
            }

            // --- body jiggle -----------------------------------------------------------------
            if (Has(n, "breast", "boob", "oppai", "bust") || HasToken(t, "pec", "pecs", "pectoral"))
            {
                return ClsBreast;
            }
            if (Has(n, "booty", "glute") || HasToken(t, "butt", "ass", "rear") ||
                (HasToken(t, "jiggle") && HasToken(t, "hip", "hips")))
            {
                return ClsButt;
            }
            if (Has(n, "belly", "tummy", "stomach") || HasToken(t, "tum", "gut"))
            {
                return ClsBelly;
            }
            if (Has(n, "thigh"))
            {
                return ClsThigh;
            }

            // --- accessories ------------------------------------------------------------------
            if (Has(n, "necklace", "pendant", "choker") || HasToken(t, "collar", "chain"))
            {
                return ClsNecklace;
            }
            if (Has(n, "charm", "jewel", "zipper", "keychain") || HasToken(t, "bell", "tag"))
            {
                return ClsCharm;
            }

            return null;
        }

        public static bool TryApply(ClothSerializeData sdata, ChainClass cls,
            out string usedPreset, out bool isCustom, out string error)
        {
            usedPreset = null;
            isCustom = false;
            error = null;

            string custom = CustomPrefix + cls.Name;
            string json = LoadJson(custom);
            if (json != null)
            {
                usedPreset = custom;
                isCustom = true;
            }
            else
            {
                json = LoadJson(cls.Fallback);
                usedPreset = cls.Fallback;
            }

            if (string.IsNullOrEmpty(json))
            {
                error = $"neither \"{custom}.json\" nor \"{cls.Fallback}.json\" is in this project";
                usedPreset = null;
                return false;
            }

            var import = typeof(ClothSerializeData).GetMethod("ImportJson",
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
            if (import == null)
            {
                error = "this MagicaCloth2 version has no ImportJson";
                usedPreset = null;
                return false;
            }
            if (!(import.Invoke(sdata, new object[] { json }) is bool ok) || !ok)
            {
                error = $"MagicaCloth2 rejected \"{usedPreset}.json\"";
                usedPreset = null;
                return false;
            }
            return true;
        }

        public static string DisplayName(string presetName)
        {
            string bare = presetName.Replace(CustomPrefix, "").Replace("MC2_Preset_", "");
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < bare.Length; i++)
            {
                if (i > 0 && char.IsUpper(bare[i]) && !char.IsUpper(bare[i - 1]))
                {
                    sb.Append(' ');
                }
                sb.Append(bare[i]);
            }
            return sb.ToString();
        }

        static readonly Dictionary<string, string> JsonCache = new Dictionary<string, string>();

        // Per conversion, or a preset edited on disk mid-session keeps serving stale text.
        public static void Reset() => JsonCache.Clear();

        static string LoadJson(string presetName)
        {
            if (JsonCache.TryGetValue(presetName, out string cached))
            {
                return cached;
            }
            string json = null;
            foreach (string guid in AssetDatabase.FindAssets(presetName))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".json") ||
                    !System.IO.Path.GetFileNameWithoutExtension(path).Equals(presetName,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (asset != null)
                {
                    json = asset.text;
                    break;
                }
            }
            // Misses are cached too, or every chain searches the project again.
            JsonCache[presetName] = json;
            return json;
        }

        static bool Has(string haystack, params string[] needles)
        {
            foreach (string needle in needles)
            {
                if (haystack.Contains(needle))
                {
                    return true;
                }
            }
            return false;
        }

        static bool HasToken(string[] tokens, params string[] needles)
        {
            foreach (string token in tokens)
            {
                foreach (string needle in needles)
                {
                    if (token == needle || (token.Length == needle.Length + 1
                        && token[token.Length - 1] == 's' && token.StartsWith(needle)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        static string[] Tokenize(string name)
        {
            string spaced = Regex.Replace(name, @"([a-z0-9])([A-Z])", "$1 $2");
            return spaced.ToLowerInvariant()
                .Split(new[] { ' ', '_', '.', '-', '(', ')', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' },
                    System.StringSplitOptions.RemoveEmptyEntries);
        }

        static int CountBones(Transform root)
        {
            int count = 0;
            var current = root;
            while (current != null && current.childCount > 0 && count < 32)
            {
                count++;
                current = current.GetChild(0);
            }
            return count;
        }
    }
}
#endif
