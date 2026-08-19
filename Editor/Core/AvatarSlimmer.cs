// Makes an avatar lighter, and reports what changed.
//
// Only what the weight card proves is free. Only textures this avatar
// alone uses.
//
// Textures shrink through the importer. maxTextureSize leaves the source
// file alone and costs no disk. Old sizes are recorded so Revert works.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class AvatarSlimmer
    {
        const string Category = "Slim down";
        const string UndoFile = "TextureSizes.undo";

        public class Shrink
        {
            public string Path;
            public string Name;
            public int From;
            public int To;
            public long Bytes;
            // Uncompressed is four bytes a pixel. Compressing costs one.
            public bool Compress;
        }

        public class Plan
        {
            public readonly List<Shrink> Textures = new List<Shrink>();
            public readonly List<string> Shared = new List<string>();
            public FreeWins.Plan Wins;
            public long Bytes => Textures.Sum(t => t.Bytes);
            public bool Any => Textures.Count > 0 || (Wins != null && Wins.Any);
        }

        // `alsoMine` is the avatar this one was converted from.
        // Conversion copies patched materials, so the source keeps its own
        // set pointing at the same textures. Those are not a stranger's.
        public static Plan Find(CVRAvatar avatar, AvatarSurvey.Model survey, AvatarWeight.Report weight,
            GameObject alsoMine = null)
        {
            var plan = new Plan();
            if (avatar == null || weight == null) return plan;

            var mine = OwnMaterials(avatar);
            if (alsoMine != null) mine.UnionWith(MaterialsOn(alsoMine));
            var elsewhere = MaterialsUsingTexturesOutside(mine);

            foreach (var t in weight.Textures)
            {
                if (t.Texture == null) continue;
                string path = AssetDatabase.GetAssetPath(t.Texture);
                if (string.IsNullOrEmpty(path)) continue;
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;

                int longest = Mathf.Max(t.Width, t.Height);
                bool resize = t.Suggested > 0 && t.Suggested < longest && importer.maxTextureSize > t.Suggested;
                bool compress = !t.Compressed
                                && importer.textureCompression == TextureImporterCompression.Uncompressed;

                long saved = 0;
                if (resize)
                {
                    float shrink = (float)t.Suggested / longest;
                    saved = t.Bytes - (long)(t.Bytes * shrink * shrink);
                }
                // Uncompressed is four bytes a pixel against BC7's one, and
                // the two compound: resize first, then a quarter of that.
                if (compress) saved += (resize ? (long)(t.Bytes * 0.25f) : t.Bytes) * 3 / 4;
                if (saved < 262144) continue;   // a quarter meg is not worth a line

                // The importer setting is global to the texture.
                // A texture others use is left alone.
                if (elsewhere.Contains(t.Texture))
                {
                    plan.Shared.Add(t.Name);
                    continue;
                }

                plan.Textures.Add(new Shrink
                {
                    Path = path, Name = t.Name, Bytes = saved, Compress = compress,
                    From = importer.maxTextureSize, To = resize ? t.Suggested : importer.maxTextureSize,
                });
            }

            if (survey != null) plan.Wins = FreeWins.Find(avatar, survey);
            return plan;
        }

        public static void Apply(CVRAvatar avatar, Plan plan, string outputDir, BridgeReport report)
        {
            if (plan == null || !plan.Any)
            {
                report.Converted(Category, "Nothing to do", "Nothing here is both provably free and worth the change.");
                return;
            }

            var undo = new List<string>();
            int done = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var t in plan.Textures)
                {
                    if (!(AssetImporter.GetAtPath(t.Path) is TextureImporter importer)) continue;
                    undo.Add($"{importer.maxTextureSize}\t{(int)importer.textureCompression}\t{t.Path}");
                    importer.maxTextureSize = t.To;
                    if (t.Compress) importer.textureCompression = TextureImporterCompression.Compressed;
                    EditorUtility.SetDirty(importer);
                    importer.SaveAndReimport();
                    done++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            if (done > 0)
            {
                WriteUndo(outputDir, undo);
                report.Converted(Category, $"{done} texture(s) changed, {Mb(plan.Bytes)} off the graphics card",
                    string.Join(", ", plan.Textures.Take(8).Select(t =>
                        t.From != t.To
                            ? $"{t.Name} {t.From}→{t.To}{(t.Compress ? " and compressed" : "")}"
                            : $"{t.Name} compressed")) +
                    (plan.Textures.Count > 8 ? $", and {plan.Textures.Count - 8} more" : "") +
                    ". Import settings only — no texture file was edited, every one of these is a field in the " +
                    "inspector to put back, and \"Put the textures back\" here does the same thing.");
            }

            foreach (string name in plan.Shared)
            {
                report.Approximated(Category, $"\"{name}\" left alone",
                    "Something outside this avatar uses it, and the size lives on the texture rather than on " +
                    "the avatar, so shrinking it here would shrink it there too.");
            }

            if (plan.Wins != null && plan.Wins.Any)
            {
                FreeWins.Apply(avatar, plan.Wins,
                    AssetDatabase.GenerateUniqueAssetPath(outputDir + "/" + avatar.name + " tidied.controller"), report);
            }
        }

        // ---- putting it back ----------------------------------------------

        public static bool CanRevert(string outputDir) => File.Exists(UndoPath(outputDir));

        public static void Revert(string outputDir, BridgeReport report)
        {
            string path = UndoPath(outputDir);
            if (!File.Exists(path))
            {
                report.Approximated(Category, "Nothing to put back", "No record of a resize for this avatar.");
                return;
            }

            int done = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string line in File.ReadAllLines(path))
                {
                    var parts = line.Split('\t');
                    // Two fields is the older record: size and path only.
                    if (parts.Length < 2 || !int.TryParse(parts[0], out int size)) continue;
                    string asset = parts[parts.Length - 1];
                    if (!(AssetImporter.GetAtPath(asset) is TextureImporter importer)) continue;
                    importer.maxTextureSize = size;
                    if (parts.Length >= 3 && int.TryParse(parts[1], out int compression))
                    {
                        importer.textureCompression = (TextureImporterCompression)compression;
                    }
                    EditorUtility.SetDirty(importer);
                    importer.SaveAndReimport();
                    done++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            File.Delete(path);
            report.Converted(Category, $"{done} texture(s) put back", "Their import sizes are what they were.");
        }

        static string UndoPath(string outputDir) =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", outputDir, UndoFile));

        static void WriteUndo(string outputDir, List<string> lines)
        {
            string path = UndoPath(outputDir);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // Appended, so two runs in a row can both be undone, oldest first.
            var all = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            foreach (string line in lines)
            {
                string asset = line.Split('\t').Last();
                if (!all.Any(existing => existing.EndsWith("\t" + asset, StringComparison.Ordinal))) all.Add(line);
            }
            File.WriteAllLines(path, all);
        }

        // ---- who else uses it ---------------------------------------------

        // Every material this avatar can wear, animated swaps included.
        //
        // A swap lives in a clip as an object-reference curve, not in any
        // renderer's sharedMaterials. Renderers alone miss outfit variants.
        static HashSet<Material> MaterialsOn(GameObject root)
        {
            var found = new HashSet<Material>();
            if (root == null) return found;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m != null) found.Add(m);
                }
            }
            return found;
        }

        static HashSet<Material> OwnMaterials(CVRAvatar avatar)
        {
            var mine = MaterialsOn(avatar.gameObject);

            var animator = avatar.GetComponent<Animator>();
            var controller = BridgeContext.Underlying(animator != null ? animator.runtimeAnimatorController : null);
            if (controller == null) return mine;

            foreach (var clip in controller.animationClips)
            {
                if (clip == null) continue;
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                    if (keys == null) continue;
                    foreach (var key in keys)
                    {
                        if (key.value is Material m) mine.Add(m);
                    }
                }
            }
            return mine;
        }

        // Textures reachable from materials this avatar does not use.
        // One pass over the project. Unapplied materials still count.
        static HashSet<Texture> MaterialsUsingTexturesOutside(HashSet<Material> mine)
        {
            var outside = new HashSet<Texture>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material"))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material == null || mine.Contains(material)) continue;
                var shader = material.shader;
                if (shader == null) continue;
                int count = ShaderUtil.GetPropertyCount(shader);
                for (int i = 0; i < count; i++)
                {
                    if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                    var tex = material.GetTexture(ShaderUtil.GetPropertyName(shader, i));
                    if (tex != null) outside.Add(tex);
                }
            }
            return outside;
        }

        static string Mb(long bytes) => (bytes / 1048576f).ToString("0.0") + " MB";
    }
}
#endif
