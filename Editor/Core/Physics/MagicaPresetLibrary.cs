#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using MagicaCloth2;

namespace AvatarBridge
{
    /// <summary>
    /// Picks the MagicaCloth2 preset that best fits a PhysBone chain, and applies it.
    ///
    /// This exists because deriving MagicaCloth2 settings from PhysBone settings does not
    /// really work. PhysBones and DynamicBone are the same algorithm — per-bone rotational
    /// springs — so their parameters correspond one to one. MagicaCloth2 is a particle
    /// position solver: it moves particles and derives bone rotations from where they land.
    /// Numbers carried across that gap are analogies, and analogies drift under load.
    ///
    /// MagicaCloth2 ships its own presets (Assets/MagicaCloth2/Res/Preset), which are what
    /// its users actually start from — nobody hand-derives the eight values in that
    /// inspector. Starting from a preset built by the solver's own author and matched to the
    /// kind of chain is a far better first approximation than any conversion arithmetic, and
    /// it leaves the avatar somewhere a human would recognise as a starting point.
    ///
    /// The PhysBone still supplies everything structural — which bones, which colliders,
    /// which transforms to ignore — and its values go into the report so a chain that wants
    /// tuning can get it.
    /// </summary>
    public static class MagicaPresetLibrary
    {
        public const string Tail = "MC2_Preset_Tail";
        public const string Cape = "MC2_Preset_Cape";
        public const string Skirt = "MC2_Preset_Skirt";
        public const string SoftSkirt = "MC2_Preset_SoftSkirt";
        public const string FrontHair = "MC2_Preset_FrontHair";
        public const string LongHair = "MC2_Preset_LongHair";
        public const string ShortHair = "MC2_Preset_ShortHair";
        public const string Accessory = "MC2_Preset_Accessory";
        public const string SoftSpring = "MC2_Preset_SoftSpring";
        public const string MiddleSpring = "MC2_Preset_MiddleSpring";
        public const string HardSpring = "MC2_Preset_HardSpring";

        static readonly Dictionary<string, string> JsonCache = new Dictionary<string, string>();

        /// <summary>
        /// Chooses a preset from what the chain IS where the name says so, and from how hard
        /// the PhysBone pulled back to its rest pose where it doesn't.
        /// </summary>
        public static string ChooseFor(PhysBoneChainData data)
        {
            string n = data.Root.name.ToLowerInvariant();
            var tokens = Tokenize(data.Root.name);

            // Hair is checked first on purpose: "twintail" and "ponytail" contain "tail" but
            // are hair, and want hair's lighter, faster settling rather than a tail's weight.
            if (HasToken(tokens, "bang") || Has(n, "hair", "twintail", "ponytail", "pigtail", "braid", "ahoge", "fringe"))
            {
                if (HasToken(tokens, "front", "bang") || Has(n, "fringe", "ahoge"))
                {
                    return FrontHair;
                }
                return CountBones(data.Root) >= 5 ? LongHair : ShortHair;
            }
            if (HasToken(tokens, "tail"))
            {
                return Tail;
            }
            if (HasToken(tokens, "cape", "cloak", "mantle", "coat"))
            {
                return Cape;
            }
            if (Has(n, "skirt", "dress", "apron"))
            {
                return Skirt;
            }
            if (HasToken(tokens, "bell", "tag", "collar", "strap")
                || Has(n, "earring", "ribbon", "charm", "jewel", "pendant", "necklace", "zipper", "accessor"))
            {
                return Accessory;
            }

            // Nothing structural in the name — breast, butt, tummy, ears, unnamed props. Pick
            // by how strongly the PhysBone restored to rest, which is the one axis these three
            // presets differ along and the one PhysBone value that means the same on both sides.
            float restore = Mathf.Clamp01(Mathf.Max(data.Pull, data.Stiffness));
            if (restore >= 0.6f)
            {
                return HardSpring;
            }
            return restore >= 0.3f ? MiddleSpring : SoftSpring;
        }

        /// <summary>
        /// Loads the preset's physics parameters over <paramref name="sdata"/>. MagicaCloth2's
        /// ImportJson preserves the structural fields — clothType, rootBones, colliderList,
        /// updateMode, animationPoseRatio, rootRotation — so this can run before or after the
        /// chain is wired up without disturbing it.
        ///
        /// Called through reflection rather than directly: a MagicaCloth2 version without
        /// ImportJson would otherwise stop the whole package compiling, where this just falls
        /// back to deriving values from the PhysBone.
        /// </summary>
        public static bool TryApply(ClothSerializeData sdata, string presetName, out string error)
        {
            error = null;
            string json = LoadJson(presetName);
            if (string.IsNullOrEmpty(json))
            {
                error = $"\"{presetName}.json\" was not found in this project";
                return false;
            }

            var import = typeof(ClothSerializeData).GetMethod("ImportJson",
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
            if (import == null)
            {
                error = "this MagicaCloth2 version has no ImportJson";
                return false;
            }
            if (!(import.Invoke(sdata, new object[] { json }) is bool ok) || !ok)
            {
                error = $"MagicaCloth2 rejected \"{presetName}.json\"";
                return false;
            }
            return true;
        }

        /// <summary>Human-readable preset name for the report ("MC2_Preset_LongHair" -> "Long Hair").</summary>
        public static string DisplayName(string presetName)
        {
            string bare = presetName.Replace("MC2_Preset_", "");
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < bare.Length; i++)
            {
                if (i > 0 && char.IsUpper(bare[i]))
                {
                    sb.Append(' ');
                }
                sb.Append(bare[i]);
            }
            return sb.ToString();
        }

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
                if (!path.EndsWith(".json"))
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
            JsonCache[presetName] = json;
            return json;
        }

        /// <summary>
        /// Substring match, for needles distinctive enough to be safe anywhere in a name
        /// ("hair" inside "backhair"). Short words must not use this: "bell" is inside "belly",
        /// "coat" inside "petticoat", "tag" inside "stage" — all of which shipped as
        /// mis-assigned presets before the split.
        /// </summary>
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

        /// <summary>
        /// Whole-word match, for short needles that hide inside unrelated words. A trailing
        /// "s" counts, so "Bagpack Straps" still reads as straps.
        /// </summary>
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

        /// <summary>
        /// Splits a bone name into words on separators, digits and camelCase humps, so
        /// "Yaoomi_DemonTail" yields "tail" while "detail_bone" does not.
        /// </summary>
        static string[] Tokenize(string name)
        {
            string spaced = Regex.Replace(name, @"([a-z0-9])([A-Z])", "$1 $2");
            return spaced.ToLowerInvariant()
                .Split(new[] { ' ', '_', '.', '-', '(', ')', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' },
                    System.StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>Depth of the chain, following first children — long hair hangs differently to short.</summary>
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
