// One button: make this avatar lighter, and say exactly what changed.
//
// Everything else in the tool measures and advises. This is the first pass
// that acts on its own advice, so it is deliberately narrow: it does only
// what the weight card can prove is free, and only to textures this avatar
// alone uses.
//
// Textures shrink through the IMPORTER, not by editing anything. Setting
// maxTextureSize leaves the source file untouched, costs no disk, and is one
// field in the inspector to put back — and the old value is written to a file
// beside the avatar so putting it back is a button here too.
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
        }

        public class Plan
        {
            public readonly List<Shrink> Textures = new List<Shrink>();
            public readonly List<string> Shared = new List<string>();
            public FreeWins.Plan Wins;
            public long Bytes => Textures.Sum(t => t.Bytes);
            public bool Any => Textures.Count > 0 || (Wins != null && Wins.Any);
        }

        public static Plan Find(CVRAvatar avatar, AvatarSurvey.Model survey, AvatarWeight.Report weight)
        {
            var plan = new Plan();
            if (avatar == null || weight == null) return plan;

            var mine = OwnMaterials(avatar);
            var elsewhere = MaterialsUsingTexturesOutside(mine);

            foreach (var t in weight.Textures)
            {
                if (t.Suggested <= 0 || t.Texture == null) continue;
                int longest = Mathf.Max(t.Width, t.Height);
                if (t.Suggested >= longest) continue;

                float shrink = (float)t.Suggested / longest;
                long saved = t.Bytes - (long)(t.Bytes * shrink * shrink);
                if (saved < 262144) continue;   // a quarter meg is not worth a line

                string path = AssetDatabase.GetAssetPath(t.Texture);
                if (string.IsNullOrEmpty(path)) continue;
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                if (importer.maxTextureSize <= t.Suggested) continue;

                // Somebody else's avatar may want it at full size. The
                // importer setting is global to the texture, so a texture
                // that is not ours alone is left exactly as it is.
                if (elsewhere.Contains(t.Texture))
                {
                    plan.Shared.Add(t.Name);
                    continue;
                }

                plan.Textures.Add(new Shrink
                {
                    Path = path, Name = t.Name, From = importer.maxTextureSize, To = t.Suggested, Bytes = saved,
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
                    undo.Add($"{importer.maxTextureSize}\t{t.Path}");
                    importer.maxTextureSize = t.To;
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
                report.Converted(Category, $"{done} texture(s) resized, {Mb(plan.Bytes)} off the graphics card",
                    string.Join(", ", plan.Textures.Take(8).Select(t => $"{t.Name} {t.From}→{t.To}")) +
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
                    if (parts.Length != 2 || !int.TryParse(parts[0], out int size)) continue;
                    if (!(AssetImporter.GetAtPath(parts[1]) is TextureImporter importer)) continue;
                    importer.maxTextureSize = size;
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

        static HashSet<Material> OwnMaterials(CVRAvatar avatar)
        {
            var mine = new HashSet<Material>();
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m != null) mine.Add(m);
                }
            }
            return mine;
        }

        // Every texture reachable from a material this avatar does NOT use.
        //
        // One pass over the project's materials, which is the only place a
        // texture is referenced from in practice. A material nobody has
        // applied still counts: it exists to be applied.
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
