// Puts YAPS back onto a material whose shader changed since the conversion.
// Poiyomi's unlock returns to the shader it was locked from, which has no deform,
// and re-locking does not restore it. Unlock, edit, re-lock, then run this.
// The _YAPS_ values are invisible while the shader lacks them but still sit in the
// serialized property lists, so they are read from there and put back.

#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsReapply
    {
        const string Prefix = "_YAPS_";
        const string MenuPath = "Tools/Avatar Bridge/YAPS/Re-apply YAPS to selected materials";

        [MenuItem(MenuPath, true)]
        static bool Validate() => Selected().Count > 0;

        [MenuItem(MenuPath)]
        static void Run()
        {
            var materials = Selected();
            // Which properties the scenes' avatars animate, for a material worn unlocked.
            YapsLock.Animate(null, Object.FindObjectsOfType<ABI.CCK.Components.CVRAvatar>(true)
                .SelectMany(a => AvatarBridge.Yaps.YapsSwapFollow.RunnableClips(a.transform)));
            int patched = 0;
            var refusals = new List<string>();

            foreach (var material in materials)
            {
                string assetPath = AssetDatabase.GetAssetPath(material);
                if (string.IsNullOrEmpty(assetPath))
                {
                    refusals.Add($"{material.name}: not an asset on disk");
                    continue;
                }
                string folder = Path.GetDirectoryName(assetPath).Replace('\\', '/');
                // Package materials are read only. Test the path, not the folder:
                // a material directly in Assets has folder "Assets", no slash.
                if (!assetPath.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
                {
                    refusals.Add($"{material.name}: lives in {folder}, which is not in Assets; move it first");
                    continue;
                }

                // Before the shader changes: assigning one that lacks a property may drop it.
                var saved = Capture(material);

                var shader = YapsShaderPatcher.Patch(material, null, out string refusal, out _);
                if (shader == null)
                {
                    refusals.Add($"{material.name}: {refusal}");
                    continue;
                }

                YapsShaderPatcher.Wear(material, shader);
                Restore(material, saved);
                EditorUtility.SetDirty(material);
                patched++;

                if (!material.HasProperty("_YAPS_Bake")
                    || material.GetTexture("_YAPS_Bake") == null)
                {
                    // Without the bake the deform does nothing in game.
                    refusals.Add($"{material.name}: patched, but it carries no bake texture: " +
                                 "convert the avatar again to rebuild it");
                }
            }

            AssetDatabase.SaveAssets();

            string summary = $"[YAPS] Re-applied to {patched} of {BridgeElements.Count(materials.Count, "material", "materials")}.";
            if (refusals.Count > 0)
            {
                summary += "\n" + string.Join("\n", refusals);
                Debug.LogWarning(summary);
            }
            else
            {
                Debug.Log(summary + "\nUnlock, edit and re-lock as often as you like; run this " +
                          "afterwards each time.");
            }
        }

        static List<Material> Selected() =>
            Selection.GetFiltered<Material>(SelectionMode.Assets | SelectionMode.DeepAssets)
                .Where(m => m != null && m.shader != null)
                .ToList();

        // --- the values ------------------------------------------------

        class Values
        {
            public readonly Dictionary<string, Texture> Textures = new Dictionary<string, Texture>();
            public readonly Dictionary<string, float> Floats = new Dictionary<string, float>();
            public readonly Dictionary<string, Vector4> Vectors = new Dictionary<string, Vector4>();
        }

        // From m_SavedProperties: undeclared properties are invisible to HasProperty/GetFloat.
        static Values Capture(Material material)
        {
            var values = new Values();
            var serialized = new SerializedObject(material);
            var saved = serialized.FindProperty("m_SavedProperties");
            if (saved == null)
            {
                return values;
            }

            var textures = saved.FindPropertyRelative("m_TexEnvs");
            for (int i = 0; textures != null && i < textures.arraySize; i++)
            {
                var entry = textures.GetArrayElementAtIndex(i);
                string name = entry.FindPropertyRelative("first").stringValue;
                if (!name.StartsWith(Prefix))
                {
                    continue;
                }
                var texture = entry.FindPropertyRelative("second.m_Texture").objectReferenceValue;
                values.Textures[name] = texture as Texture;
            }

            var floats = saved.FindPropertyRelative("m_Floats");
            for (int i = 0; floats != null && i < floats.arraySize; i++)
            {
                var entry = floats.GetArrayElementAtIndex(i);
                string name = entry.FindPropertyRelative("first").stringValue;
                if (name.StartsWith(Prefix))
                {
                    values.Floats[name] = entry.FindPropertyRelative("second").floatValue;
                }
            }

            // Vectors are stored in m_Colors.
            var colors = saved.FindPropertyRelative("m_Colors");
            for (int i = 0; colors != null && i < colors.arraySize; i++)
            {
                var entry = colors.GetArrayElementAtIndex(i);
                string name = entry.FindPropertyRelative("first").stringValue;
                if (name.StartsWith(Prefix))
                {
                    values.Vectors[name] = entry.FindPropertyRelative("second").colorValue;
                }
            }
            return values;
        }

        static void Restore(Material material, Values values)
        {
            foreach (var pair in values.Textures)
            {
                if (pair.Value != null && material.HasProperty(pair.Key))
                {
                    material.SetTexture(pair.Key, pair.Value);
                }
            }
            foreach (var pair in values.Floats)
            {
                if (material.HasProperty(pair.Key))
                {
                    material.SetFloat(pair.Key, pair.Value);
                }
            }
            foreach (var pair in values.Vectors)
            {
                if (material.HasProperty(pair.Key))
                {
                    material.SetVector(pair.Key, pair.Value);
                }
            }
        }
    }
}
#endif
