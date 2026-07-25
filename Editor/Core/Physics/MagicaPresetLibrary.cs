#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System.Collections.Generic;
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

            // Hair is checked first on purpose: "twintail" and "ponytail" contain "tail" but
            // are hair, and want hair's lighter, faster settling rather than a tail's weight.
            if (Has(n, "hair", "bang", "ahoge", "fringe", "twintail", "ponytail", "pigtail", "braid"))
            {
                if (Has(n, "front", "bang", "fringe", "ahoge"))
                {
                    return FrontHair;
                }
                return CountBones(data.Root) >= 5 ? LongHair : ShortHair;
            }
            if (Has(n, "tail"))
            {
                return Tail;
            }
            if (Has(n, "cape", "cloak", "mantle", "coat"))
            {
                return Cape;
            }
            if (Has(n, "skirt", "dress", "apron"))
            {
                return Skirt;
            }
            if (Has(n, "earring", "ribbon", "bell", "charm", "jewel", "pendant", "necklace",
                     "zipper", "collar", "strap", "tag", "accessor"))
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
        /// Loads the preset over <paramref name="sdata"/>. Wipes everything, including root
        /// bones and collider list, so callers must apply structural data AFTER this.
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
            if (!sdata.ImportJson(json))
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
