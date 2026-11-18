// What an avatar costs in ChilloutVR, judged. Textures first, where heavy avatars are
// heavy, by texel density: texture pixels per metre of real surface, shown as arithmetic
// rather than a rule. Read only.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace AvatarBridge
{
    public static class AvatarWeight
    {
        // Texels per metre. A face at talking distance needs 1000 to 2000; past this
        // resolution only reaches the card, not the eye.
        public const float TargetDensity = 2000f;

        public class TextureUse
        {
            public Texture Texture;
            public string Name;
            public int Width;
            public int Height;
            public string Format;
            public bool Crunched;
            public bool Readable;
            public bool Compressed;
            // Point filtered, no mips: exact values a shader reads, which compression rewrites.
            public bool Data;
            public long Bytes;
            // For a CRUNCHED texture's download size, the one case it differs from card size.
            public long DiskBytes;
            public int Materials;

            // Lowest density among its surfaces decides; highest let a button shrink a shirt.
            public float Density;
            public float WorldArea;
            public int Suggested;
            // Only switched-off renderers use it, so stripping them frees it.
            public bool Dead;
            // What the advice gives back, counted once: resized, compressed or stripped.
            public long Reclaim;
        }

        public class Callout
        {
            public long Bytes;      // what taking the advice gives back
            public int Rank;        // named saving, then the tail, then advice without a figure
            public string Subject;  // the texture it is about, where it is about one
            public string Text;
        }

        public class Report
        {
            public string Avatar;

            public long TextureBytes;
            public readonly List<TextureUse> Textures = new List<TextureUse>();

            public int Renderers;
            public int Skinned;
            public int Triangles;
            public int SubMeshes;
            public int BlendShapes;
            public int AnimatedBlendShapes;
            public int Bones;

            public int Materials;
            public readonly SortedSet<string> Shaders = new SortedSet<string>(System.StringComparer.Ordinal);

            public int AudioSources;
            public int ParticleSystems;
            public int MaxParticles;
            public int Lights;
            public int MarkerLights;
            public long DownloadBytes;
            public int Cloth;
            public int ClothColliders;
            public int ClothParticles;
            public int ClothMeshVertices;   // a ceiling: MeshCloth reduction merges an unknown share
            public int Pointers;
            public int Triggers;
            public readonly List<string> Atlas = new List<string>();
            public readonly SortedDictionary<string, int> ClothParents =
                new SortedDictionary<string, int>(System.StringComparer.Ordinal);
            public readonly SortedDictionary<string, int> ContactFamilies =
                new SortedDictionary<string, int>(System.StringComparer.Ordinal);

            public long DeadBytes;
            public long DeadDownloadBytes;
            public readonly List<string> Dead = new List<string>();
            // The same renderers, as found: a path can name a same-named sibling instead.
            public readonly List<Renderer> DeadRenderers = new List<Renderer>();
            // Dead, but its textures are drawn elsewhere: stripping frees mesh and draw call only.
            public readonly List<string> DeadFreesNothing = new List<string>();
            public readonly List<Callout> Callouts = new List<Callout>();
            // Texture name to the material asset standing in the way.
            public readonly Dictionary<string, string> LeftAlone =
                new Dictionary<string, string>(System.StringComparer.Ordinal);
        }

        internal class Area
        {
            public double World;
            public double Uv;
        }

        class MeshData
        {
            public Vector3[] Vertices;
            public Vector2[] Uv;
        }

        public static Report Measure(CVRAvatar avatar, AvatarSurvey.Model survey = null)
        {
            var report = new Report();
            if (avatar == null) return report;
            report.Avatar = avatar.name;

            var renderers = avatar.GetComponentsInChildren<Renderer>(true).Where(r => r != null).ToList();
            var bones = new HashSet<Transform>();
            var materials = new HashSet<Material>();
            var textures = new Dictionary<Texture, TextureUse>();
            var areas = new Dictionary<Material, Area>();
            var meshCache = new Dictionary<Mesh, MeshData>();
            var switched = SwitchedPaths(avatar, survey);
            // Kept apart until all renderers are read, or the answer depends on loop order.
            var deadMaterials = new HashSet<Material>();
            var liveMaterials = new HashSet<Material>();

            // Blendshapes only count as live when something animates them.
            var animated = new HashSet<string>();
            if (survey != null)
            {
                foreach (string binding in survey.Layers.SelectMany(l => l.Bindings))
                {
                    int cut = binding.IndexOf("::", System.StringComparison.Ordinal);
                    if (cut < 0) continue;
                    if (binding.Substring(cut + 2).StartsWith("blendShape.", System.StringComparison.Ordinal)) animated.Add(binding);
                }
            }
            // And what CVR drives itself (visemes, blink, face tracking): missed, Native face
            // tracking's shapes raised the warning by 28.
            void Driven(Renderer face, IEnumerable<string> names)
            {
                if (face == null || names == null) return;
                string at = AnimationUtility.CalculateTransformPath(face.transform, avatar.transform);
                foreach (string n in names.Where(n => !string.IsNullOrEmpty(n) && n != "-none-")) animated.Add($"{at}::blendShape.{n}");
            }
            Driven(avatar.bodyMesh, (avatar.visemeBlendshapes ?? new string[0]).Concat(avatar.blinkBlendshape ?? new string[0]));
            foreach (var ft in avatar.GetComponentsInChildren<CVRFaceTracking>(true).Where(f => f.UseFacialTracking))
                Driven(ft.FaceMesh, ft.FaceBlendShapes);

            foreach (var r in renderers)
            {
                report.Renderers++;
                var mesh = MeshOf(r);
                if (r is SkinnedMeshRenderer skin)
                {
                    report.Skinned++;
                    if (skin.bones != null)
                    {
                        foreach (var b in skin.bones)
                        {
                            if (b != null) bones.Add(b);
                        }
                    }
                }

                string path = AnimationUtility.CalculateTransformPath(r.transform, avatar.transform);
                if (mesh != null)
                {
                    report.Triangles += AvatarDescription.TriangleCount(mesh);
                    report.SubMeshes += mesh.subMeshCount;
                    report.BlendShapes += mesh.blendShapeCount;
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                    {
                        if (animated.Contains($"{path}::blendShape.{mesh.GetBlendShapeName(i)}")) report.AnimatedBlendShapes++;
                    }
                    AccumulateArea(r, mesh, areas, meshCache);
                }

                // Off and nothing can turn it on: downloaded, never seen. Needs a controller to prove.
                bool dead = survey != null && survey.Controller != null && !Live(r, avatar.transform, switched);
                if (dead) { report.Dead.Add(path); report.DeadRenderers.Add(r); }

                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    if (dead) deadMaterials.Add(m);
                    else liveMaterials.Add(m);
                    if (!materials.Add(m)) continue;
                    if (m.shader != null) report.Shaders.Add(m.shader.name);
                    foreach (var tex in TexturesOf(m))
                    {
                        if (textures.TryGetValue(tex, out var use)) { use.Materials++; continue; }
                        textures[tex] = Describe(tex);
                    }
                }
            }

            Densities(textures, areas);

            report.Bones = bones.Count;
            report.Materials = materials.Count;
            // Ties by name and path, or two equal sizes list in material order and swap between runs.
            report.Textures.AddRange(textures.Values.OrderByDescending(t => t.Bytes)
                .ThenBy(t => t.Name, System.StringComparer.Ordinal)
                .ThenBy(t => AssetDatabase.GetAssetPath(t.Texture), System.StringComparer.Ordinal));
            report.TextureBytes = report.Textures.Sum(t => t.Bytes);
            // Crunched: packed size to download, full size on the card.
            report.DownloadBytes = report.Textures.Sum(t => t.Crunched ? t.DiskBytes : t.Bytes);
            // Only what nothing visible uses; a texture behind two dead renderers counts once.
            deadMaterials.ExceptWith(liveMaterials);
            var liveTextures = new HashSet<Texture>(liveMaterials.SelectMany(TexturesOf));
            foreach (var r2 in renderers)
            {
                string path = AnimationUtility.CalculateTransformPath(r2.transform, avatar.transform);
                if (!report.Dead.Contains(path)) continue;
                bool frees = r2.sharedMaterials.Any(m => m != null && deadMaterials.Contains(m)
                                                         && TexturesOf(m).Any(t => !liveTextures.Contains(t)));
                if (!frees) report.DeadFreesNothing.Add(path);
            }
            var deadTextures = deadMaterials
                .SelectMany(TexturesOf)
                .Distinct()
                .Where(t => !liveTextures.Contains(t) && textures.ContainsKey(t))
                .ToList();
            foreach (var t in deadTextures) textures[t].Dead = true;
            report.DeadBytes = deadTextures.Sum(t => textures[t].Bytes);
            // They leave the download too, at the packed size where crunched.
            report.DeadDownloadBytes = deadTextures.Sum(t => textures[t].Crunched
                ? textures[t].DiskBytes : textures[t].Bytes);

            report.AudioSources = avatar.GetComponentsInChildren<AudioSource>(true).Length;
            var particles = avatar.GetComponentsInChildren<ParticleSystem>(true);
            report.ParticleSystems = particles.Length;
            report.MaxParticles = particles.Sum(p => p != null ? p.main.maxParticles : 0);
            var lights = avatar.GetComponentsInChildren<Light>(true);
            report.Lights = lights.Length;
            report.MarkerLights = lights.Count(IsProtocolLight);
            report.Pointers = avatar.GetComponentsInChildren<CVRPointer>(true).Length;
            report.Triggers = avatar.GetComponentsInChildren<CVRAdvancedAvatarSettingsTrigger>(true).Length;

            CountCloth(avatar, report);
            FindAtlasCandidates(avatar, materials, report);
            CountContactFamilies(avatar, report);
            Judge(report);
            return report;
        }

        // ---- judgement ----------------------------------------------------

        static void Judge(Report r)
        {
            // The share of each texture left once its advice is taken.
            var keeps = r.Textures.ToDictionary(t => t, t => 1.0);
            var oversized = new List<KeyValuePair<TextureUse, long>>();
            foreach (var t in r.Textures)
            {
                if (t.Suggested <= 0 || t.Suggested >= Mathf.Max(t.Width, t.Height)) continue;
                float shrink = (float)t.Suggested / Mathf.Max(t.Width, t.Height);
                long saved = t.Bytes - (long)(t.Bytes * shrink * shrink);
                if (saved < 262144) continue;
                oversized.Add(new KeyValuePair<TextureUse, long>(t, saved));
                keeps[t] *= shrink * shrink;
            }
            oversized.Sort((a, b) => b.Value.CompareTo(a.Value));

            // Name the worst; the tail is a number, not thirty more lines.
            const int Named = 12;
            foreach (var pair in oversized.Take(Named))
            {
                var t = pair.Key;
                Add(r, pair.Value, $"\"{t.Name}\" is {t.Width}x{t.Height} across {t.WorldArea:0.###} m2 of surface. " +
                                   $"That is {t.Density:N0} texels per metre where {TargetDensity:N0} is ample. " +
                                   $"{t.Suggested}x{t.Suggested} is the same picture.", 0, KeyOf(pair.Key.Texture, t.Name));
            }
            if (oversized.Count > Named)
            {
                var tail = oversized.Skip(Named).ToList();
                Add(r, tail.Sum(p => p.Value), $"{tail.Count} more textures are past the target by the same maths, " +
                                               "listed under heaviest textures with their density.", 1);
            }

            // Not data textures: a YAPS bake packs float bytes the shader rebuilds exactly.
            var uncompressed = r.Textures.Where(t => !t.Compressed && !t.Data && t.Bytes > 1048576).ToList();
            foreach (var t in uncompressed)
            {
                // BC7 is 1 byte a pixel; uncompressed varies (R8: 1, RGBAHalf: 8).
                long perPixel = t.Texture != null ? GraphicsFormatUtility.GetBlockSize(t.Texture.graphicsFormat) : 4;
                if (perPixel <= 1) continue;
                long saved = t.Bytes - t.Bytes / perPixel;
                keeps[t] /= perPixel;
                Add(r, saved, $"\"{t.Name}\" is {t.Format}, {perPixel} bytes a pixel, uncompressed. " +
                              "Compressing it costs nothing anybody will see.", 0, KeyOf(t.Texture, t.Name));
            }

            var readable = r.Textures.Where(t => t.Readable).ToList();
            if (readable.Count > 0)
            {
                long saved = readable.Sum(t => t.Bytes);
                Add(r, saved, $"{BridgeElements.Count(readable.Count, "texture has", "textures have")} Read/Write Enabled ticked, which keeps a second " +
                              "copy in system memory for the whole session. Nothing on an avatar reads pixels " +
                              "back. Untick it in the importer.");
            }

            int costly = r.Dead.Count - r.DeadFreesNothing.Count;
            if (StripAdvised(r))
            {
                Add(r, r.DeadBytes, $"{BridgeElements.Count(costly, "renderer is", "renderers are")} switched off and nothing in the animator can " +
                                    "switch them on, so their textures are downloaded and never seen. Either wire " +
                                    "them to a toggle or strip them.");
            }

            // Read/Write is system memory, not the card, so it is not counted.
            foreach (var t in r.Textures)
            {
                t.Reclaim = StripAdvised(r) && t.Dead ? t.Bytes : t.Bytes - (long)(t.Bytes * keeps[t]);
            }

            // Said even at nothing reclaimed: the mesh is still skinned every frame.
            if (r.DeadFreesNothing.Count > 0)
            {
                Add(r, 0, $"{BridgeElements.Count(r.DeadFreesNothing.Count, "more renderer is", "more renderers are")} switched off with nothing to switch " +
                          "them on. Their textures are shared with something visible, so none of that comes back, " +
                          "but the mesh is still carried and still skinned every frame.");
            }

            int contacts = r.Pointers + r.Triggers;
            if (contacts > 96)
            {
                Add(r, 0, $"{contacts} contacts. The whole instance gets 512 overlapping pairs a frame, so you " +
                          "alone can eat the room's budget and the drops land on everybody, silently.", 2);
            }

            if (r.Materials >= 8 && r.Shaders.Count >= r.Materials)
            {
                Add(r, 0, $"{r.Materials} materials on {r.Shaders.Count} distinct shaders, which means every " +
                          "material carries its own locked copy. The client compiles them one by one on first " +
                          "sight of you. Lock them together in one go, so materials set alike share one shader.", 2);
            }

            if (r.ClothParticles > 900)
            {
                Add(r, 0, $"{r.Cloth} cloth solvers simulating {r.ClothParticles:N0} transforms at 90 Hz. The " +
                          "transform count is the cost, not the component count, and this is the largest one " +
                          "on the card that never shows up as a number anybody quotes.", 2);
            }

            if (r.ContactFamilies.Count > 1 && r.Pointers > 24)
            {
                Add(r, 0, $"{r.Pointers} pointers across {r.ContactFamilies.Count} families: " +
                          string.Join(", ", r.ContactFamilies.Select(f => $"{f.Value} {f.Key}")) +
                          ". Each family carries its own pair per socket, and the instance shares 512 " +
                          "overlapping pairs a frame.", 2);
            }

            // CVR's default download limit is 100 MB; past it most viewers silently see a robot.
            // The CCK measures upload size and crunch separates it from card size, so this
            // speaks only where the figure alone settles it.
            if (r.DownloadBytes > 100L * 1048576)
            {
                Add(r, 0, $"About {Mb(r.DownloadBytes)} of texture to download, against a default limit of " +
                          "100 MB. People who keep that default never see an avatar past it, and nothing tells " +
                          "them why. An estimate: the CCK settles the real figure at upload.", 2);
            }

            var crowded = r.ClothParents.Where(p => p.Value > 3).ToList();
            if (crowded.Count > 0)
            {
                Add(r, 0, string.Join("; ", crowded.Select(p => $"{p.Value} solvers under \"{p.Key}\"")) +
                          ". Named for the count, not as something to fix. Merging them would simulate the " +
                          "same transforms under fewer components, so it buys the solver overhead and " +
                          "nothing else, and one solver holds ONE set of settings for every bone it " +
                          "carries: chains tuned apart would be retuned to whichever won.", 2);
            }

            if (r.Atlas.Count > 0)
            {
                Add(r, 0, $"{BridgeElements.Count(r.Atlas.Count, "set of materials shares a shader and is", "sets of materials share a shader and are")} never animated apart: " +
                          string.Join(", ", r.Atlas) + ". One texture each would cut draw calls. Reported only, " +
                          "because merging them rewrites UVs and breaks per-slot material animation.", 2);
            }

            if (r.Triangles > 250000)
            {
                Add(r, 0, $"{r.Triangles:N0} triangles. Past a quarter million you are the reason someone's " +
                          "frame rate halved when you walked in.", 2);
            }

            int unanimated = r.BlendShapes - r.AnimatedBlendShapes;
            if (unanimated > 64)
            {
                Add(r, 0, $"{unanimated} blendshapes nothing animates. They still cost mesh memory and still slow " +
                          "every skinning pass.", 2);
            }

            r.Callouts.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : b.Bytes.CompareTo(a.Bytes));
        }

        static void Add(Report r, long bytes, string text, int rank = 0, string subject = null)
            => r.Callouts.Add(new Callout { Bytes = bytes, Text = text, Rank = rank, Subject = subject });

        static bool StripAdvised(Report r) => r.DeadBytes > 1048576 && r.Dead.Count > r.DeadFreesNothing.Count;

        // Each texture once: overlapping callouts summed past the total.
        // Left alone stops a resize, not a strip.
        static long Reclaimable(Report r)
        {
            bool strip = StripAdvised(r);
            return r.Textures.Sum(t => (strip && t.Dead) || !r.LeftAlone.ContainsKey(KeyOf(t.Texture, t.Name)) ? t.Reclaim : 0);
        }

        // By asset path: file names repeat across packages.
        internal static string KeyOf(Texture texture, string name)
        {
            string path = texture != null ? AssetDatabase.GetAssetPath(texture) : null;
            return string.IsNullOrEmpty(path) ? name : path;
        }

        internal static string Shown(string key) =>
            key != null && key.Contains("/") ? System.IO.Path.GetFileNameWithoutExtension(key) : key;

        static string LeftAloneNote(string user) =>
            $" Left alone though: {user} uses it too, and the size lives on the texture rather than on the " +
            "avatar, so shrinking it here would shrink it there.";

        // What the optimiser will not touch, and what is in the way.
        public static void NoteLeftAlone(Report r, IEnumerable<KeyValuePair<string, string>> shared)
        {
            if (r == null || shared == null) return;
            foreach (var pair in shared) r.LeftAlone[pair.Key] = pair.Value;
        }

        // ---- density ------------------------------------------------------

        internal static void Densities(Dictionary<Texture, TextureUse> textures, Dictionary<Material, Area> areas)
        {
            // One surface per map set and tiling, or per-slot material copies each get a fraction of the area.
            var surfaces = new Dictionary<string, (List<Texture> maps, Vector2 scale, Area area)>();
            foreach (var pair in areas)
            {
                var scale = pair.Key.HasProperty("_MainTex") ? pair.Key.mainTextureScale : Vector2.one;
                // A data texture has no density and is never resized. Out of the key too, so a
                // bake one copy gained does not split the surface.
                var maps = TexturesOf(pair.Key)
                    .Where(t => textures.TryGetValue(t, out var u) && !u.Data)
                    .Distinct()
                    .OrderBy(t => t.GetInstanceID())
                    .ToList();
                if (maps.Count == 0) continue;
                string key = $"{scale.x:R},{scale.y:R}|{string.Join(",", maps.Select(t => t.GetInstanceID()))}";
                if (!surfaces.TryGetValue(key, out var surface)) surfaces[key] = surface = (maps, scale, new Area());
                surface.area.World += pair.Value.World;
                surface.area.Uv += pair.Value.Uv;
            }

            foreach (var surface in surfaces.Values)
            {
                var area = surface.area;
                if (area.World <= 0.0001 || area.Uv <= 0.0001) continue;

                // Tiling repeats the map, so it multiplies coverage.
                double uv = area.Uv * Mathf.Max(0.01f, Mathf.Abs(surface.scale.x) * Mathf.Abs(surface.scale.y));

                foreach (var tex in surface.maps)
                {
                    var use = textures[tex];
                    double texels = (double)use.Width * use.Height * uv;
                    float density = (float)System.Math.Sqrt(texels / area.World);
                    if (use.Density > 0f && density >= use.Density) continue;
                    use.Density = density;
                    use.WorldArea = (float)area.World;
                }
            }

            foreach (var use in textures.Values)
            {
                if (use.Density <= 0f) continue;
                int longest = Mathf.Max(use.Width, use.Height);
                // Density scales with resolution; round down to a power of two.
                int wanted = Mathf.FloorToInt(longest * (TargetDensity / use.Density));
                // Floor: VR looks from 10 cm away, and savings under 256 are not worth the row.
                int size = 256;
                while (size * 2 <= wanted && size < longest) size *= 2;
                use.Suggested = size < longest ? size : 0;
            }
        }

        static void AccumulateArea(Renderer r, Mesh mesh, Dictionary<Material, Area> areas, Dictionary<Mesh, MeshData> cache)
        {
            // Read/Write does not matter: the editor holds the source data either way.
            if (!cache.TryGetValue(mesh, out var data))
            {
                data = new MeshData { Vertices = mesh.vertices, Uv = mesh.uv };
                cache[mesh] = data;
            }
            if (data == null || data.Vertices.Length == 0) return;

            var lossy = r.transform.lossyScale;
            float scale = (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f;
            float scaleSquared = scale * scale;
            var mats = r.sharedMaterials;
            bool haveUv = data.Uv != null && data.Uv.Length == data.Vertices.Length;

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var material = sub < mats.Length ? mats[sub] : null;
                if (material == null) continue;
                if (!areas.TryGetValue(material, out var area)) areas[material] = area = new Area();

                var tris = mesh.GetTriangles(sub);
                double world = 0, uv = 0;
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    world += TriangleArea(data.Vertices[a], data.Vertices[b], data.Vertices[c]);
                    if (haveUv) uv += TriangleArea(data.Uv[a], data.Uv[b], data.Uv[c]);
                }
                area.World += world * scaleSquared;
                area.Uv += uv;
            }
        }

        static double TriangleArea(Vector3 a, Vector3 b, Vector3 c) => Vector3.Cross(b - a, c - a).magnitude * 0.5;

        static double TriangleArea(Vector2 a, Vector2 b, Vector2 c)
            => Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) * 0.5;

        // ---- reachability ---------------------------------------------------

        // Every path some layer switches, to tell a dead renderer from a menu-controlled one.
        static HashSet<string> SwitchedPaths(CVRAvatar avatar, AvatarSurvey.Model survey)
        {
            var paths = new HashSet<string>(System.StringComparer.Ordinal);
            if (survey == null) return paths;
            foreach (string binding in survey.Layers.SelectMany(l => l.Bindings))
            {
                int cut = binding.IndexOf("::", System.StringComparison.Ordinal);
                if (cut < 0) continue;
                string property = binding.Substring(cut + 2);
                if (property == "m_IsActive" || property == "m_Enabled") paths.Add(binding.Substring(0, cut));
            }
            // Native toggles and dropdowns act from a CCK-built layer this controller may not hold yet.
            var settings = avatar.avatarSettings != null ? avatar.avatarSettings.settings : null;
            if (settings != null)
            {
                foreach (var entry in settings) paths.UnionWith(AvatarSurvey.TargetPaths(entry));
            }
            // A child Animator's paths are relative to itself: rebased onto the avatar.
            foreach (var child in avatar.GetComponentsInChildren<Animator>(true))
            {
                if (child.gameObject == avatar.gameObject || child.runtimeAnimatorController == null) continue;
                string at = AnimationUtility.CalculateTransformPath(child.transform, avatar.transform);
                foreach (var clip in child.runtimeAnimatorController.animationClips.Where(c => c != null).Distinct())
                {
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (b.propertyName == "m_IsActive" || b.propertyName == "m_Enabled")
                            paths.Add(string.IsNullOrEmpty(b.path) ? at : at + "/" + b.path);
                    }
                }
            }
            return paths;
        }

        static bool Live(Renderer r, Transform root, HashSet<string> switched)
        {
            if (r.enabled && ShownUnder(r.transform, root)) return true;
            for (var t = r.transform; t != null; t = t.parent)
            {
                if (switched.Contains(AnimationUtility.CalculateTransformPath(t, root))) return true;
                if (t == root) break;
            }
            return false;
        }

        // Active up to the avatar, not the scene: an avatar switched off in the scene read every
        // renderer as hidden, and Slim down stripped them all.
        static bool ShownUnder(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
                if (!t.gameObject.activeSelf) return false;
            return true;
        }

        // ---- measurement ----------------------------------------------------

        // By name: MagicaCloth is optional and a reference would not compile without it.
        static void CountCloth(CVRAvatar avatar, Report report)
        {
            foreach (var c in avatar.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (c == null) continue;
                string name = c.GetType().Name;
                if (name == "MagicaCloth" || name == "DynamicBone")
                {
                    report.Cloth++;
                    report.ClothParticles += ParticlesUnder(c);
                    report.ClothMeshVertices += MeshClothVertices(c);
                    // Chains under one parent could share one solver.
                    var parent = c.transform.parent;
                    if (parent != null)
                    {
                        report.ClothParents.TryGetValue(parent.name, out int n);
                        report.ClothParents[parent.name] = n + 1;
                    }
                }
                else if (name.StartsWith("Magica", System.StringComparison.Ordinal) && name.EndsWith("Collider", System.StringComparison.Ordinal)) report.ClothColliders++;
                else if (name == "DynamicBoneCollider") report.ClothColliders++;
            }
        }

        // Simulated transforms, the number that costs. Reflection: both solvers are optional.
        static int ParticlesUnder(MonoBehaviour solver)
        {
            var roots = new List<Transform>();
            var type = solver.GetType();

            var db = type.GetField("m_Root");
            if (db != null && db.GetValue(solver) is Transform single) roots.Add(single);

            var many = type.GetField("m_Roots");
            if (many != null && many.GetValue(solver) is IEnumerable<Transform> list) roots.AddRange(list);

            if (roots.Count == 0)
            {
                // MagicaCloth keeps its roots inside SerializeData.
                var data = type.GetProperty("SerializeData")?.GetValue(solver);
                var field = data?.GetType().GetField("rootBones");
                if (field?.GetValue(data) is IEnumerable<Transform> magica) roots.AddRange(magica);
            }

            int count = 0;
            foreach (var root in roots)
            {
                if (root != null) count += root.GetComponentsInChildren<Transform>(true).Length;
            }
            return count;
        }

        // MeshCloth simulates vertices, fewer after the package's reduction: a ceiling, not transforms.
        static int MeshClothVertices(MonoBehaviour solver)
        {
            var data = solver.GetType().GetProperty("SerializeData")?.GetValue(solver);
            if (data?.GetType().GetField("clothType")?.GetValue(data)?.ToString() != "MeshCloth") return 0;
            if (!(data.GetType().GetField("sourceRenderers")?.GetValue(data) is IEnumerable<Renderer> renderers)) return 0;
            int count = 0;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                // GetComponent hands back a fake null in the editor, so no ?. on it.
                var filter = r is SkinnedMeshRenderer ? null : r.GetComponent<MeshFilter>();
                var mesh = r is SkinnedMeshRenderer skin ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
                if (mesh != null) count += mesh.vertexCount;
            }
            return count;
        }

        // Atlas candidates: same shader, nothing animates them apart. Named only: atlasing
        // breaks every per-slot material property.
        static void FindAtlasCandidates(CVRAvatar avatar, HashSet<Material> materials, Report report)
        {
            var animated = new HashSet<string>(System.StringComparer.Ordinal);
            var controller = AvatarSurvey.ShippedController(avatar);
            if (controller != null)
            {
                foreach (var clip in controller.animationClips)
                {
                    if (clip == null) continue;
                    foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    {
                        foreach (var key in AnimationUtility.GetObjectReferenceCurve(clip, b))
                        {
                            if (key.value is Material m && m != null) animated.Add(m.name);
                        }
                    }
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                    {
                        // A property curve names its renderer, and reaches every material on it.
                        if (!b.propertyName.StartsWith("material.", System.StringComparison.Ordinal)) continue;
                        var at = BridgeContext.FindByAnimationPath(avatar.transform, b.path);
                        var renderer = at != null ? at.GetComponent(b.type) as Renderer : null;
                        if (renderer == null) continue;
                        foreach (var m in renderer.sharedMaterials)
                        {
                            if (m != null) animated.Add(m.name);
                        }
                    }
                }
            }

            foreach (var group in materials.Where(m => m != null && m.shader != null && !YapsMarks.IsAtlasMaterial(m)
                                                       && !YapsMarks.IsReadoutMaterial(m) && !YapsMarks.IsBakedMaterial(m))
                         .GroupBy(m => m.shader.name)
                         .Where(g => g.Count() > 1))
            {
                var still = group.Where(m => !animated.Contains(m.name)).ToList();
                if (still.Count > 1) report.Atlas.Add($"{still.Count} on {group.Key}");
            }
        }

        // Pointers by family, which is what the 512-pair budget is spent on.
        static void CountContactFamilies(CVRAvatar avatar, Report report)
        {
            foreach (var p in avatar.GetComponentsInChildren<CVRPointer>(true))
            {
                if (p == null) continue;
                string family = string.IsNullOrEmpty(p.type) ? "(no type)" : p.type.Split('/')[0];
                report.ContactFamilies.TryGetValue(family, out int n);
                report.ContactFamilies[family] = n + 1;
            }
        }

        static bool IsProtocolLight(Light l)
        {
            if (l == null || l.type != LightType.Point) return false;
            var c = l.color;
            if (c.r > 0.02f || c.g > 0.02f || c.b > 0.02f) return false;
            return l.range > 0.05f && l.range < 0.5f;
        }

        static TextureUse Describe(Texture tex)
        {
            var use = new TextureUse
            {
                Texture = tex,
                Name = tex.name,
                Width = tex.width,
                Height = tex.height,
                Materials = 1,
                Bytes = GpuBytes(tex),
                DiskBytes = DiskSize(tex),
                Compressed = tex.graphicsFormat != GraphicsFormat.None
                             && GraphicsFormatUtility.IsCompressedFormat(tex.graphicsFormat),
                Data = tex.filterMode == FilterMode.Point
                       && (!(tex is Texture2D mips) || mips.mipmapCount <= 1),
                Format = tex is Texture2D t ? t.format.ToString() : tex.GetType().Name,
            };
            string path = AssetDatabase.GetAssetPath(tex);
            if (!string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                use.Crunched = importer.crunchedCompression;
                use.Readable = importer.isReadable;
            }
            return use;
        }

        // GPU memory. Not Profiler.GetRuntimeMemorySizeLong: the editor keeps a CPU copy too,
        // so it reads double. Crunch is download only; it unpacks to plain DXT on upload.
        internal static long GpuBytes(Texture tex)
        {
            var format = tex.graphicsFormat;
            if (format == GraphicsFormat.None) return 0;

            int faces = tex is Cubemap ? 6 : 1;
            if (tex is Texture2DArray array) faces = array.depth;
            int mips = tex is Texture2D t2 ? t2.mipmapCount : 1;
            if (mips < 1) mips = 1;

            long total = 0;
            for (int level = 0; level < mips; level++)
            {
                total += LevelBytes(Mathf.Max(1, tex.width >> level), Mathf.Max(1, tex.height >> level), format);
            }
            return total * faces;
        }

        static long LevelBytes(int width, int height, GraphicsFormat format)
        {
            long block = GraphicsFormatUtility.GetBlockSize(format);
            if (!GraphicsFormatUtility.IsCompressedFormat(format)) return (long)width * height * block;

            // Blocks are 4x4 and a partial edge block costs a whole one.
            int wide = (width + 3) / 4;
            int tall = (height + 3) / 4;
            return (long)wide * tall * block;
        }

        static long DiskSize(Texture tex)
        {
            string path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path)) return 0;
            var file = new System.IO.FileInfo(System.IO.Path.GetFullPath(path));
            return file.Exists ? file.Length : 0;
        }

        internal static Mesh MeshOf(Renderer r)
        {
            if (r is SkinnedMeshRenderer skin) return skin.sharedMesh;
            var filter = r.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        internal static IEnumerable<Texture> TexturesOf(Material m)
        {
            if (m == null || m.shader == null) yield break;
            // By name, not index: a broken or recompiling shader reports more properties than it hands out.
            foreach (int id in m.GetTexturePropertyNameIDs())
            {
                // The list keeps slots from old shaders, which would count as rendered.
                if (!m.HasTexture(id)) continue;
                var tex = m.GetTexture(id);
                if (tex != null) yield return tex;
            }
        }

        public static string Text(Report r)
        {
            var sb = new StringBuilder();
            sb.Append("# Weight of ").Append(r.Avatar).Append('\n');
            sb.Append("textures   ").Append(r.Textures.Count).Append(", ")
              .Append(Mb(r.TextureBytes)).Append(" on the graphics card\n");
            sb.Append("download   ~").Append(Mb(r.DownloadBytes))
              .Append(" of that is texture to download, give or take what the CCK's build does")
              .Append(r.DownloadBytes > 100L * 1048576 ? "  (the default limit is 100 MB)" : "")
              .Append('\n');
            sb.Append("meshes     ").Append(r.Renderers).Append(" renderers (").Append(r.Skinned).Append(" skinned), ")
              .Append(r.Triangles.ToString("N0")).Append(" tris, ").Append(r.SubMeshes).Append(" submeshes, ")
              .Append(r.Bones).Append(" bones\n");
            sb.Append("blendshapes ").Append(r.BlendShapes).Append(" (").Append(r.AnimatedBlendShapes).Append(" animated)\n");
            sb.Append("materials  ").Append(r.Materials).Append(" on ").Append(BridgeElements.Count(r.Shaders.Count, "shader", "shaders")).Append("\n");
            sb.Append("contacts   ").Append(r.Pointers + r.Triggers)
              .Append(" (").Append(r.Pointers).Append(" pointers, ").Append(r.Triggers)
              .Append(" triggers) against 512 overlapping pairs a frame for the whole instance\n");
            sb.Append("physics    ").Append(r.Cloth).Append(" cloth simulating ")
              .Append(r.ClothParticles.ToString("N0")).Append(" transforms, ")
              .Append(r.ClothColliders).Append(" colliders");
            if (r.ClothMeshVertices > 0)
                sb.Append(", and mesh cloth on up to ").Append(r.ClothMeshVertices.ToString("N0")).Append(" vertices before reduction");
            sb.Append('\n');
            if (r.ContactFamilies.Count > 0)
            {
                sb.Append("pointers   ")
                  .Append(string.Join(", ", r.ContactFamilies.Select(f => $"{f.Value} {f.Key}"))).Append('\n');
            }
            if (r.Atlas.Count > 0)
            {
                sb.Append("atlas      ").Append(string.Join(", ", r.Atlas))
                  .Append(" could share a texture, if anything ever merged them\n");
            }
            sb.Append("audio      ").Append(r.AudioSources).Append(" of ChilloutVR's 100\n");
            sb.Append("lights     ").Append(r.Lights).Append(" (").Append(r.MarkerLights)
              .Append(" YAPS markers) against four vertex slots a mesh\n");
            sb.Append("particles  ").Append(r.ParticleSystems).Append(" systems, ")
              .Append(r.MaxParticles.ToString("N0")).Append(" max\n\n");

            if (r.Callouts.Count > 0)
            {
                long saved = Reclaimable(r);
                sb.Append("## what to fix");
                if (saved > 0) sb.Append(" (").Append(Mb(saved)).Append(" of it)");
                sb.Append('\n');
                foreach (var c in r.Callouts)
                {
                    sb.Append(c.Bytes > 0 ? Mb(c.Bytes).PadLeft(9) : "         ").Append("  ").Append(c.Text).Append('\n');
                }
                sb.Append('\n');
            }

            sb.Append("## heaviest textures\n");
            foreach (var t in r.Textures.Take(15))
            {
                sb.Append("  ").Append(Mb(t.Bytes).PadLeft(9)).Append("  ")
                  .Append(t.Width).Append('x').Append(t.Height).Append("  ")
                  .Append(t.Format).Append(t.Crunched ? " crunched" : "")
                  .Append(t.Readable ? " read/write" : "").Append("  ")
                  .Append(t.Density > 0f ? $"{t.Density:N0}/m  " : "")
                  .Append(t.Name).Append('\n');
            }
            return sb.ToString();
        }

        // Report section: advice open, the full card folded.
        public static string Markdown(Report r)
        {
            var sb = new StringBuilder();
            sb.Append(Summary(r, true)).Append("\n\n");
            if (r.Callouts.Count > 0)
            {
                foreach (var c in r.Callouts)
                {
                    sb.Append("- ");
                    if (c.Subject != null && r.LeftAlone.TryGetValue(c.Subject, out string user))
                    {
                        sb.Append(c.Text).Append(LeftAloneNote(user)).Append('\n');
                        continue;
                    }
                    if (c.Bytes > 0) sb.Append("**").Append(Mb(c.Bytes)).Append("**: ");
                    sb.Append(c.Text).Append('\n');
                }
                sb.Append('\n');
            }
            sb.Append("<details>\n<summary>Everything measured</summary>\n\n```\n");
            sb.Append(Text(r));
            sb.Append("```\n</details>\n");
            return sb.ToString();
        }

        // `markdown`: the window draws rows as plain text, where asterisks show literally.
        public static string Summary(Report r, bool markdown = false)
        {
            long saved = Reclaimable(r);
            string head = $"{Mb(r.TextureBytes)} of texture across {r.Textures.Count} maps, " +
                          $"{r.Triangles:N0} triangles, {r.Pointers + r.Triggers} contacts.";
            // Read/Write alone still has a line of its own below.
            if (saved <= 0) return r.Callouts.Any(c => c.Bytes > 0) ? head : head + " Nothing here is worth changing.";
            string point = $"{Mb(saved)} of that comes off with nothing visible changing.";
            return markdown ? head + $" **{point}**" : head + " " + point;
        }

        // The Toolkit shows findings as report rows rather than a document.
        public static void Fill(BridgeReport report, Report weight)
        {
            report.Add(ReportStatus.Converted, "Weight", "Measured", Summary(weight));
            foreach (var c in weight.Callouts)
            {
                // Advice the optimiser has already refused stops being advice.
                if (c.Subject != null && weight.LeftAlone.TryGetValue(c.Subject, out string user))
                {
                    report.Add(ReportStatus.Approximated, "Weight", $"\"{Shown(c.Subject)}\" left alone",
                        c.Text + LeftAloneNote(user));
                    continue;
                }
                report.Add(ReportStatus.Warning, "Weight",
                    c.Bytes > 0 ? Mb(c.Bytes) + " to reclaim" : "Costs you", c.Text);
            }
        }

        static string Mb(long bytes) => (bytes / 1048576f).ToString("0.0") + " MB";
    }
}
#endif
