// Puts YAPS back onto a material whose shader has been changed since the
// conversion that first patched it.
//
// The case this exists for: a converted avatar's plug wears a Poiyomi
// shader with the deform patched into it, and Poiyomi's unlock button goes
// back to the shader it was locked FROM. That is the original, which has no
// deform. So unlocking to change a colour silently removes the deform, and
// re-locking does not bring it back — there was no way back at all except
// converting the whole avatar again.
//
// Now there is. Unlock, edit, re-lock, run this. The patch is applied to
// whatever shader the material is wearing at the time, so every Poiyomi
// feature and every property the user set survives it.
//
// The awkward part is the VALUES. A material's _YAPS_ properties only exist
// while its shader declares them, and the unlocked Poiyomi shader does not —
// so by the time this runs, material.HasProperty says no and GetTexture
// returns nothing. They are not gone though: Unity keeps them in the
// material's serialized property lists, orphaned but intact. So they are
// read from there rather than through the material API, put back after the
// patched shader is assigned, and the bake texture survives a round trip it
// would otherwise not.

#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
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
        const string MenuPath = "Tools/Avatar Bridge/Re-apply YAPS to selected materials";

        [MenuItem(MenuPath, true)]
        static bool Validate() => Selected().Count > 0;

        [MenuItem(MenuPath)]
        static void Run()
        {
            var materials = Selected();
            int patched = 0;
            var refusals = new List<string>();

            foreach (var material in materials)
            {
                string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(material))?.Replace('\\', '/');
                // A material inside a package is not ours to write beside.
                if (folder != null && !folder.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
                {
                    refusals.Add($"{material.name}: lives in {folder}, which is not in Assets; move it first");
                    continue;
                }
                if (string.IsNullOrEmpty(folder))
                {
                    refusals.Add($"{material.name}: not an asset on disk");
                    continue;
                }

                // BEFORE the shader changes. Assigning a shader that lacks a
                // property is one of the moments Unity may drop it.
                var saved = Capture(material);

                var shader = YapsShaderPatcher.Patch(material, folder, null,
                    out string refusal, out _);
                if (shader == null)
                {
                    refusals.Add($"{material.name}: {refusal}");
                    continue;
                }

                material.shader = shader;
                Restore(material, saved);
                EditorUtility.SetDirty(material);
                patched++;

                if (!material.HasProperty("_YAPS_Bake")
                    || material.GetTexture("_YAPS_Bake") == null)
                {
                    // Worth saying rather than leaving them to find out in
                    // game: the deform is in the shader but the mesh data it
                    // bends by is not on the material, so it will do nothing.
                    refusals.Add($"{material.name}: patched, but it carries no bake texture — " +
                                 "convert the avatar again to rebuild it");
                }
            }

            AssetDatabase.SaveAssets();

            string summary = $"[YAPS] Re-applied to {patched} of {materials.Count} material(s).";
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

        // Read from the SERIALIZED lists, not through the material. A
        // property whose shader does not declare it is invisible to
        // HasProperty and GetFloat, which is precisely the state an
        // unlocked material is in — but Unity still has the values, sitting
        // in m_SavedProperties with nothing to apply them to.
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

            // Vector properties live in m_Colors; Unity stores both as a
            // colour and does not distinguish them here.
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
