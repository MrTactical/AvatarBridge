// Makes an avatar lighter, and reports what changed.
//
// Only what the weight card proves is free. Only textures this avatar
// alone uses.
//
// Size and format only, through the importer. A format goes on as a
// Standalone override, and only on a PNG or JPG of the Default texture
// type whose own pixels allow it: one channel repeated, an alpha never
// used, or four bytes a pixel uncompressed. The source file is never
// edited and old settings are recorded, so Revert restores them.
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
            // The part of Bytes the format gives, taken back if it is refused.
            public long FormatBytes;
            // BC4 for one channel repeated, BC1 for alpha that is never used.
            // Both are 4 bits a pixel where BC7 and DXT5 are 8.
            public TextureImporterFormat? Format;
            public string Why;
        }

        public class Plan
        {
            public readonly List<Shrink> Textures = new List<Shrink>();
            // Texture name to the material asset outside this avatar that
            // keeps it where it is.
            public readonly Dictionary<string, string> Shared =
                new Dictionary<string, string>(System.StringComparer.Ordinal);
            // Renderers no clip can switch on. The component goes, the
            // object stays: bones, contacts and constraints parented under
            // it are the usual casualty of removing the object itself.
            public readonly List<string> Strip = new List<string>();
            public long StripBytes;
            public FreeWins.Plan Wins;
            public long Bytes => Textures.Sum(t => t.Bytes);
            public bool Any => Textures.Count > 0 || Strip.Count > 0 || (Wins != null && Wins.Any);
        }

        // `alsoMine` is the avatar this one was converted from.
        // Conversion copies patched materials, so the source keeps its own
        // set pointing at the same textures. Those are not a stranger's.
        public static Plan Find(CVRAvatar avatar, AvatarSurvey.Model survey, AvatarWeight.Report weight,
            GameObject alsoMine = null, bool inspectContent = true, bool stripDead = true)
        {
            var plan = new Plan();
            if (avatar == null || weight == null) return plan;

            // Built on first need: it loads every material in the project,
            // and only a texture worth changing asks. Usually none is.
            Dictionary<string, List<string>> users = null;
            HashSet<Material> mine = null;

            foreach (var t in weight.Textures)
            {
                if (t.Texture == null) continue;
                string path = AssetDatabase.GetAssetPath(t.Texture);
                if (string.IsNullOrEmpty(path)) continue;
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;

                int longest = Mathf.Max(t.Width, t.Height);
                // With the Standalone override on, its size is the one Unity reads.
                var standalone = importer.GetPlatformTextureSettings(Platform);
                int size = standalone.overridden ? standalone.maxTextureSize : importer.maxTextureSize;
                bool resize = t.Suggested > 0 && t.Suggested < longest && size > t.Suggested;

                long saved = 0, formatSaved = 0;
                long after = t.Bytes;
                if (resize)
                {
                    float shrink = (float)t.Suggested / longest;
                    after = (long)(t.Bytes * shrink * shrink);
                    saved = t.Bytes - after;
                }

                // Half again, where the content does not need eight bits.
                TextureImporterFormat? format = null;
                string why = null;
                // Crunch packs the download, and the formats below are not
                // crunched: swapping one in halves the card and grows the download.
                bool crunched = t.Crunched || (t.Texture is Texture2D packed
                    && (packed.format == TextureFormat.DXT1Crunched || packed.format == TextureFormat.DXT5Crunched));
                // What a pixel costs now. The mip chain is in it and in what a
                // format would cost, so it cancels.
                float bits = t.Bytes * 8f / Mathf.Max(1, t.Width * t.Height);
                // A data texture is never touched. Its pixels are numbers a
                // shader reads back exactly, and a lossy format ruins them.
                if (inspectContent && !t.Data && !crunched && importer.textureType == TextureImporterType.Default
                    // Any other source makes an alpha the file does not have.
                    && importer.alphaSource == TextureImporterAlphaSource.FromInput
                    // At four bits a pixel or fewer no format below is
                    // smaller, so decoding the file could not change the answer.
                    && bits > 4f
                    && TimedContent(path, out bool greyscale, out bool opaque))
                {
                    // BC4 holds one linear channel. Unity refuses it on an
                    // sRGB texture, so only data maps qualify however grey
                    // a colour map happens to look.
                    if (greyscale && !importer.sRGBTexture)
                    {
                        format = TextureImporterFormat.BC4;
                        why = "one linear channel repeated three times";
                    }
                    else if (opaque)
                    {
                        format = TextureImporterFormat.DXT1;
                        why = "an alpha channel that is white everywhere";
                    }
                    else if (!t.Compressed)
                    {
                        // Four bytes a pixel, and an alpha that earns its
                        // keep. DXT5 holds the alpha and costs one byte.
                        format = TextureImporterFormat.DXT5;
                        why = "four bytes a pixel, uncompressed";
                    }
                    if (format.HasValue && CurrentFormat(importer) == format.Value) format = null;
                    if (format.HasValue)
                    {
                        float wanted = format == TextureImporterFormat.DXT5 ? 8f : 4f;
                        if (wanted < bits)
                        {
                            formatSaved = (long)(after * (1f - wanted / bits));
                            saved += formatSaved;
                        }
                        else format = null;
                    }
                }

                if (saved < 262144) continue;   // a quarter meg is not worth a line

                // The importer setting is global to the texture.
                // A texture others use is left alone.
                if (users == null)
                {
                    mine = OwnMaterials(avatar);
                    if (alsoMine != null) mine.UnionWith(MaterialsOn(alsoMine));
                    Users.Start();
                    users = MaterialsByDependency();
                    Users.Stop();
                }
                Outside.Start();
                string user = MaterialOutsideUsing(t.Texture, users, mine);
                Outside.Stop();
                if (user != null)
                {
                    plan.Shared[t.Name] = user;
                    continue;
                }

                plan.Textures.Add(new Shrink
                {
                    Path = path, Name = t.Name, Bytes = saved, FormatBytes = formatSaved,
                    Format = format, Why = why,
                    From = size, To = resize ? t.Suggested : size,
                });
            }

            // Off in the scene with nothing able to switch it on. Checked
            // across the corpus against a walk of every clip, override
            // controllers included, before it was ever allowed to act.
            if (stripDead)
            {
                plan.Strip.AddRange(weight.Dead);
                plan.StripBytes = weight.DeadBytes;
            }

            if (survey != null) plan.Wins = FreeWins.Find(avatar, survey);
            return plan;
        }

        // The conversion's own call, at the end of the pipeline. Sizes and
        // formats only: stripping a renderer no clip switches on and tidying
        // an animator are judgement calls that want the avatar in front of
        // you, and they stay on the button. A size the mesh's own texel
        // density proves is not a judgement call.
        public static void SlimOnConvert(BridgeContext ctx)
        {
            if (ctx == null || !ctx.Settings.slimTexturesOnConvert || ctx.CvrAvatar == null)
            {
                return;
            }
            GameObject alsoMine = null;
#if VRC_SDK_VRCSDK3
            if (ctx.SourceDescriptor != null) alsoMine = ctx.SourceDescriptor.gameObject;
#endif
            // No survey: it feeds only the dead-renderer test and the
            // animator tidy, and this call does neither.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Decode.Reset(); Users.Reset(); Outside.Reset();
            var weight = AvatarWeight.Measure(ctx.CvrAvatar);
            long measured = clock.ElapsedMilliseconds;
            var plan = Find(ctx.CvrAvatar, null, weight, alsoMine, true, false);
            long found = clock.ElapsedMilliseconds;
            Debug.Log($"[AvatarBridge] Texture sizes steps (ms): {weight.Textures.Count} textures measured in {measured}, " +
                $"planned in {found - measured} (decoded {Decode.ElapsedMilliseconds}, project materials {Users.ElapsedMilliseconds}, " +
                $"outside users {Outside.ElapsedMilliseconds}), {plan.Textures.Count} to change");
            // Before the early return: a shared texture is refused even when
            // nothing else needs shrinking, and the weight card must not
            // advise shrinking it.
            ctx.SlimLeftAlone = plan.Shared;
            if (plan.Textures.Count == 0)
            {
                // Silence, not a line saying nothing happened. An avatar
                // whose textures already fit is the common case and does
                // not need a paragraph about it.
                return;
            }
            Apply(ctx.CvrAvatar, plan, ctx.OutputDir, ctx.Report);
        }

        public static void Apply(CVRAvatar avatar, Plan plan, string outputDir, BridgeReport report)
        {
            if (plan == null || !plan.Any)
            {
                report.Converted(Category, "Nothing to do", "Nothing here is both provably free and worth the change.");
                return;
            }

            var undo = new List<string>();
            var applied = new List<(Shrink t, TextureImporterFormat before)>();
            int refused = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var t in plan.Textures)
                {
                    if (!(AssetImporter.GetAtPath(t.Path) is TextureImporter importer)) continue;
                    var platform = importer.GetPlatformTextureSettings(Platform);
                    undo.Add($"{importer.maxTextureSize}\t{(int)importer.textureCompression}" +
                             $"\t{(int)CurrentFormat(importer)}\t{platform.maxTextureSize}\t{t.Path}");
                    var before = CurrentFormat(importer);
                    // Only a resize touches the default tab, and only down: From
                    // and To are the override's size while one is on.
                    if (t.To < t.From) importer.maxTextureSize = Mathf.Min(importer.maxTextureSize, t.To);
                    if (t.Format.HasValue) SetFormat(importer, t.Format.Value, t.To);
                    // An override already on would otherwise keep its own size
                    // and the resize would go nowhere.
                    else if (platform.overridden) SetFormat(importer, before, t.To);
                    EditorUtility.SetDirty(importer);
                    importer.SaveAndReimport();
                    applied.Add((t, before));
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            // A format the platform will not take leaves the texture broken
            // and the inspector shouting. Put it back rather than leave
            // somebody to find out. Checked only now: inside the batch the
            // import is queued and the texture still holds its old format.
            foreach (var (t, before) in applied)
            {
                if (!t.Format.HasValue || Took(t.Path, t.Format.Value)) continue;
                if (!(AssetImporter.GetAtPath(t.Path) is TextureImporter importer)) continue;
                SetFormat(importer, before, t.To);
                importer.SaveAndReimport();
                t.Bytes -= t.FormatBytes;
                t.FormatBytes = 0;
                t.Format = null;
                t.Why = null;
                if (t.From != t.To) refused++;
            }

            // A refused format on a texture that kept its size changed nothing.
            var changed = applied.Select(a => a.t).Where(t => t.From != t.To || t.Format.HasValue).ToList();
            long bytes = changed.Sum(t => t.Bytes);
            if (changed.Count > 0)
            {
                WriteUndo(outputDir, undo);
                report.BytesReclaimed += bytes;
                report.Converted(Category, $"{BridgeElements.Count(changed.Count, "texture", "textures")} changed, {Mb(bytes)} off the graphics card",
                    string.Join("; ", changed.Take(8).Select(Describe)) +
                    (changed.Count > 8 ? $"; and {changed.Count - 8} more" : "") +
                    ". Import settings only; \"Put the textures back\" undoes it." +
                    (refused > 0 ? $" {refused} refused the format, so only their size changed." : ""));
            }

            StripDead(avatar, plan, report);

            foreach (var left in plan.Shared)
            {
                report.Approximated(Category, $"\"{left.Key}\" left alone",
                    $"{left.Value} uses it too, and the size lives on the texture rather than on the " +
                    "avatar, so shrinking it here would shrink it there.");
            }

            if (plan.Wins != null && plan.Wins.Any)
            {
                FreeWins.Apply(avatar, plan.Wins,
                    AssetDatabase.GenerateUniqueAssetPath(outputDir + "/" + CvrSetup.SafeFolderName(avatar.name) + " tidied.controller"), report);
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
                    // Older records carry fewer fields; the path is last.
                    if (parts.Length < 2 || !int.TryParse(parts[0], out int size)) continue;
                    string asset = parts[parts.Length - 1];
                    if (!(AssetImporter.GetAtPath(asset) is TextureImporter importer)) continue;
                    importer.maxTextureSize = size;
                    if (parts.Length >= 3 && int.TryParse(parts[1], out int compression))
                    {
                        importer.textureCompression = (TextureImporterCompression)compression;
                    }
                    if (parts.Length >= 4 && int.TryParse(parts[2], out int format))
                    {
                        // Records written before the override carried a size
                        // end at the path here, which will not parse.
                        int overrideSize = parts.Length >= 5 && int.TryParse(parts[3], out int o) ? o : -1;
                        SetFormat(importer, (TextureImporterFormat)format, overrideSize);
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
            report.Converted(Category, $"{BridgeElements.Count(done, "texture", "textures")} put back", "Their import sizes are what they were.");
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

        // What was done to one texture, and why, in the report's own words.
        static string Describe(Shrink t)
        {
            var parts = new List<string>();
            if (t.From != t.To) parts.Add($"{t.From} to {t.To}");

            if (t.Format.HasValue) parts.Add($"{t.Format.Value} because it is {t.Why}");
            return $"{t.Name}: {string.Join(", ", parts)}";
        }

        // The component only. Taking the object would take whatever hangs
        // off it, and a hidden mesh doubling as a bone parent or a contact
        // anchor is exactly the shape that breaks.
        //
        // Undo.DestroyObjectImmediate rather than a written undo file: the
        // editor rebuilds the whole component, and "Put the textures back"
        // has nothing to say about geometry.
        static int StripDead(CVRAvatar avatar, Plan plan, BridgeReport report)
        {
            if (plan.Strip.Count == 0) return 0;

            var gone = new List<string>();
            foreach (string path in plan.Strip)
            {
                var found = BridgeContext.FindByAnimationPath(avatar.transform, path);
                if (found == null) continue;
                var renderer = found.GetComponent<Renderer>();
                if (renderer == null) continue;

                // A MeshRenderer draws what the filter beside it holds, and
                // the filter alone draws nothing once the renderer is gone.
                var filter = renderer is MeshRenderer ? found.GetComponent<MeshFilter>() : null;
                Undo.DestroyObjectImmediate(renderer);
                if (filter != null) Undo.DestroyObjectImmediate(filter);
                gone.Add(path);
            }

            if (gone.Count == 0) return 0;

            report.Converted(Category, $"{BridgeElements.Count(gone.Count, "hidden renderer", "hidden renderers")} stripped, {Mb(plan.StripBytes)} off the card",
                string.Join("; ", gone.Take(6)) + (gone.Count > 6 ? $"; and {gone.Count - 6} more" : "") +
                ". No clip could switch them on. The objects stay; Ctrl+Z puts the renderers back.");
            return gone.Count;
        }

        // Did the reimport actually produce the format asked for?
        //
        // Unity reports an incompatible choice through the inspector and
        // carries on with something else, so the importer still claims the
        // value it was given. What the texture IS is the honest answer.
        static bool Took(string path, TextureImporterFormat wanted)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) return true;
            switch (wanted)
            {
                case TextureImporterFormat.BC4: return texture.format == TextureFormat.BC4;
                case TextureImporterFormat.DXT1: return texture.format == TextureFormat.DXT1;
                case TextureImporterFormat.DXT5: return texture.format == TextureFormat.DXT5;
                default: return true;
            }
        }

        // Block formats live on the PLATFORM tab, not the Default one.
        //
        // The Default tab offers RGBA32, RGB24 and friends: formats that mean
        // something everywhere. DXT1 and BC4 are what a desktop GPU wants, so
        // they are only valid as a Standalone override, and setting them on
        // the default settings is refused with an error per texture.
        const string Platform = "Standalone";

        static TextureImporterFormat CurrentFormat(TextureImporter importer)
        {
            var settings = importer.GetPlatformTextureSettings(Platform);
            return settings.overridden ? settings.format : TextureImporterFormat.Automatic;
        }

        // The override carries its OWN maximum size, and Unity reads that one
        // rather than the default tab's for as long as the override is on.
        // Writing a format without the size leaves a resize that was recorded,
        // reported and never applied.
        static void SetFormat(TextureImporter importer, TextureImporterFormat format, int maxSize = -1)
        {
            var settings = importer.GetPlatformTextureSettings(Platform);
            settings.format = format;
            if (maxSize > 0) settings.maxTextureSize = maxSize;
            // Automatic hands the choice back to Unity, which is an override
            // switched off rather than a value written.
            settings.overridden = format != TextureImporterFormat.Automatic;
            importer.SetPlatformTextureSettings(settings);
        }

        // What a texture actually contains, from its own file.
        //
        // Automatic picks a format from the CHANNELS a source has, not from
        // what is in them. A mask whose three colour channels are identical
        // is one channel stored three times, and an alpha channel that is
        // white everywhere is a channel stored for nothing. Both are 8 bits
        // a pixel where 4 would do.
        //
        // Read through LoadImage rather than GetPixels: a shipped texture is
        // not readable, and this needs no reimport to find out. PNG and JPG
        // only, which is nearly all of them; anything else is left alone.
        static readonly System.Diagnostics.Stopwatch Decode = new System.Diagnostics.Stopwatch(),
            Users = new System.Diagnostics.Stopwatch(), Outside = new System.Diagnostics.Stopwatch();

        // Verdicts kept in Library by the database's hash of the file and its import settings: a
        // reconvert decoded every texture again, 22 s on one avatar. Only decoded verdicts are kept,
        // so a file that failed to read is tried again. Rename the file when Content changes.
        static Dictionary<string, int> verdicts;
        static readonly string VerdictFile = Path.Combine("Library", "AvatarBridge", "TextureContent-1.txt");

        static bool TimedContent(string path, out bool greyscale, out bool opaque)
        {
            var hash = AssetDatabase.GetAssetDependencyHash(path);
            string key = hash.isValid ? hash.ToString() : null;
            if (key != null && Verdicts().TryGetValue(key, out int known))
            {
                greyscale = (known & 1) != 0;
                opaque = (known & 2) != 0;
                return true;
            }
            Decode.Start();
            try
            {
                if (!Content(path, out greyscale, out opaque)) return false;
                if (key != null)
                {
                    int verdict = (greyscale ? 1 : 0) | (opaque ? 2 : 0);
                    verdicts[key] = verdict;
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(VerdictFile));
                        File.AppendAllText(VerdictFile, $"{key} {verdict}\n");
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                return true;
            }
            finally { Decode.Stop(); }
        }

        // ponytail: one line per texture version, never pruned; trim when the file grows past a few MB.
        static Dictionary<string, int> Verdicts()
        {
            if (verdicts != null) return verdicts;
            verdicts = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                foreach (string line in File.ReadAllLines(VerdictFile))
                {
                    int space = line.IndexOf(' ');
                    if (space > 0 && int.TryParse(line.Substring(space + 1), out int verdict))
                        verdicts[line.Substring(0, space)] = verdict;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return verdicts;
        }

        static bool Content(string path, out bool greyscale, out bool opaque)
        {
            greyscale = opaque = false;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg") return false;

            // No mip chain: with one, LoadImage builds and uploads every level to read the first.
            var probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!probe.LoadImage(File.ReadAllBytes(path))) return false;
                var pixels = probe.GetPixels32();
                if (pixels.Length == 0) return false;

                // Every pixel. LoadImage has decoded them all already, and a
                // sample that misses one cutout pixel turns it into a solid
                // quad under DXT1. A stride of length/4096 read one column.
                greyscale = true;
                opaque = true;
                for (int i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    if (p.a < 250) opaque = false;
                    if (Mathf.Abs(p.r - p.g) > 2 || Mathf.Abs(p.g - p.b) > 2) greyscale = false;
                    if (!greyscale && !opaque) break;
                }
                greyscale &= opaque;   // a mask with real alpha is not one channel
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        // ---- who else uses it ---------------------------------------------

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

        // Every material this avatar can wear, animated swaps included.
        //
        // A swap lives in a clip as an object-reference curve, not in any
        // renderer's sharedMaterials. Renderers alone miss outfit variants.
        static HashSet<Material> OwnMaterials(CVRAvatar avatar)
        {
            var mine = MaterialsOn(avatar.gameObject);

            var controller = AvatarSurvey.ShippedController(avatar);
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

        // Every material in the project, by each asset it depends on. No material is loaded:
        // reading a material's textures builds its property sheet, which logs every fault in
        // it (a cubemap in a 2D slot, a missing third-party drawer) to the console, and a
        // project holds thousands that have nothing to do with this avatar. Direct
        // dependencies only: a recursive list walks every shader's includes, 18 s against
        // 3.5 s over 4600 materials, where loading them all took 150 s. Unapplied materials
        // still count.
        static Dictionary<string, List<string>> MaterialsByDependency()
        {
            var users = new Dictionary<string, List<string>>();
            var pathOf = new Dictionary<string, string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material"))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                // Itself added: a font or a controller can hold a material and its texture in
                // one file, and the list does not name the file.
                foreach (string dependency in new HashSet<string>(DependenciesOf(assetPath, pathOf)) { assetPath })
                {
                    if (!users.TryGetValue(dependency, out var list)) users[dependency] = list = new List<string>();
                    list.Add(assetPath);
                }
            }
            return users;
        }

        // A text material names every asset it holds by GUID, so its own file is a superset of
        // what the database lists. Read as text: over one project the database took from 4 to
        // 46 s, the files about 3 s.
        static IEnumerable<string> DependenciesOf(string assetPath, Dictionary<string, string> pathOf)
        {
            string text = null;
            if (assetPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
            {
                try { text = File.ReadAllText(assetPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (text == null || !text.StartsWith("%YAML", StringComparison.Ordinal))
            {
                return AssetDatabase.GetDependencies(assetPath, false);
            }
            var found = new List<string>();
            for (int at = text.IndexOf("guid: ", StringComparison.Ordinal); at >= 0 && at + 38 <= text.Length;
                 at = text.IndexOf("guid: ", at + 38, StringComparison.Ordinal))
            {
                string guid = text.Substring(at + 6, 32);
                if (!pathOf.TryGetValue(guid, out string path)) pathOf[guid] = path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path)) found.Add(path);
            }
            return found;
        }

        // The first material this avatar does not use that renders the texture, or null. The
        // dependency list is a superset (it keeps slots from shaders a material used to have),
        // so each candidate is read for what it really renders. The first one found is enough
        // to name. Listing every material using a common texture is a wall, not an answer.
        static string MaterialOutsideUsing(Texture texture, Dictionary<string, List<string>> users, HashSet<Material> mine)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path) || !users.TryGetValue(path, out var candidates)) return null;
            // A variant depends on its parent, not on the textures it inherits through it.
            var seen = new HashSet<string>();
            var queue = new Queue<string>(candidates);
            while (queue.Count > 0)
            {
                string assetPath = queue.Dequeue();
                if (!seen.Add(assetPath)) continue;
                if (users.TryGetValue(assetPath, out var variants)) foreach (string v in variants) queue.Enqueue(v);
                var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                if (material == null || mine.Contains(material)) continue;
                if (AvatarWeight.TexturesOf(material).Contains(texture)) return assetPath;
            }
            return null;
        }

        static string Mb(long bytes) => (bytes / 1048576f).ToString("0.0") + " MB";
    }
}
#endif
