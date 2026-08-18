// What an avatar costs, measured against what ChilloutVR charges for it,
// and then judged.
//
// Textures first, because that is where a heavy avatar is heavy and the
// number nobody has. The judgement is texel density: how many texture
// pixels land on how much real surface. A ring two centimetres across
// wearing a 2K map is not a matter of taste, it is arithmetic, and the
// card shows the arithmetic rather than asserting a rule.
//
// Nothing here writes to the avatar.
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
        // Texels per metre of surface. A face at conversational distance is
        // well served by one to two thousand; this is the point past which
        // resolution stops reaching the eye and only reaches the card.
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
            public long Bytes;
            public int Materials;

            // Highest density among the materials using it, never the
            // average: the mesh that shows it closest decides.
            public float Density;
            public float WorldArea;
            public int Suggested;
        }

        public class Callout
        {
            public long Bytes;      // what taking the advice gives back
            public int Rank;        // named saving, then the tail, then advice without a figure
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
            public int Cloth;
            public int ClothColliders;
            public int Pointers;
            public int Triggers;

            public long DeadBytes;
            public readonly List<string> Dead = new List<string>();
            public readonly List<Callout> Callouts = new List<Callout>();
        }

        class Area
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
            var switched = SwitchedPaths(survey);
            var deadMaterials = new HashSet<Material>();

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
                    report.Triangles += mesh.triangles.Length / 3;
                    report.SubMeshes += mesh.subMeshCount;
                    report.BlendShapes += mesh.blendShapeCount;
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                    {
                        if (animated.Contains($"{path}::blendShape.{mesh.GetBlendShapeName(i)}")) report.AnimatedBlendShapes++;
                    }
                    AccumulateArea(r, mesh, areas, meshCache);
                }

                // Off in the scene and nothing in the animator can turn it
                // on: it is carried, downloaded and never seen.
                bool dead = survey != null && !Live(r, avatar.transform, switched);
                if (dead) report.Dead.Add(path);

                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    if (dead) deadMaterials.Add(m);
                    else deadMaterials.Remove(m);
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
            report.Textures.AddRange(textures.Values.OrderByDescending(t => t.Bytes));
            report.TextureBytes = report.Textures.Sum(t => t.Bytes);
            report.DeadBytes = deadMaterials
                .SelectMany(TexturesOf)
                .Distinct()
                .Where(textures.ContainsKey)
                .Sum(t => textures[t].Bytes);

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
            Judge(report);
            return report;
        }

        // ---- judgement ----------------------------------------------------

        static void Judge(Report r)
        {
            var oversized = new List<KeyValuePair<TextureUse, long>>();
            foreach (var t in r.Textures)
            {
                if (t.Suggested <= 0 || t.Suggested >= Mathf.Max(t.Width, t.Height)) continue;
                float shrink = (float)t.Suggested / Mathf.Max(t.Width, t.Height);
                long saved = t.Bytes - (long)(t.Bytes * shrink * shrink);
                if (saved < 262144) continue;
                oversized.Add(new KeyValuePair<TextureUse, long>(t, saved));
            }
            oversized.Sort((a, b) => b.Value.CompareTo(a.Value));

            // Name the worst; the tail is a number, not thirty more lines.
            const int Named = 12;
            foreach (var pair in oversized.Take(Named))
            {
                var t = pair.Key;
                Add(r, pair.Value, $"\"{t.Name}\" is {t.Width}x{t.Height} across {t.WorldArea:0.###} m2 of surface. " +
                                   $"That is {t.Density:N0} texels per metre where {TargetDensity:N0} is ample. " +
                                   $"{t.Suggested}x{t.Suggested} is the same picture.");
            }
            if (oversized.Count > Named)
            {
                var tail = oversized.Skip(Named).ToList();
                Add(r, tail.Sum(p => p.Value), $"{tail.Count} more textures are past the target by the same maths, " +
                                               "listed under heaviest textures with their density.", 1);
            }

            var uncompressed = r.Textures.Where(t => !t.Compressed && t.Bytes > 1048576).ToList();
            foreach (var t in uncompressed)
            {
                // BC7 is a byte a pixel; anything uncompressed is four.
                long saved = t.Bytes - t.Bytes / 4;
                Add(r, saved, $"\"{t.Name}\" is {t.Format}, four bytes a pixel, uncompressed. " +
                              "Compressing it costs nothing anybody will see.");
            }

            var readable = r.Textures.Where(t => t.Readable).ToList();
            if (readable.Count > 0)
            {
                long saved = readable.Sum(t => t.Bytes);
                Add(r, saved, $"{readable.Count} texture(s) have Read/Write Enabled ticked, which keeps a second " +
                              "copy in system memory for the whole session. Nothing on an avatar reads pixels " +
                              "back. Untick it in the importer.");
            }

            if (r.DeadBytes > 1048576)
            {
                Add(r, r.DeadBytes, $"{r.Dead.Count} renderer(s) are switched off and nothing in the animator can " +
                                    "switch them on, so their textures are downloaded and never seen. Either wire " +
                                    "them to a toggle or delete them.");
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
                          "sight of you. Unlock, or lock once and share.", 2);
            }

            if (r.Cloth > 24)
            {
                Add(r, 0, $"{r.Cloth} cloth solvers running at 90 Hz. This is the largest cost on the card that " +
                          "never shows up as a number anybody quotes.", 2);
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

        static void Add(Report r, long bytes, string text, int rank = 0)
            => r.Callouts.Add(new Callout { Bytes = bytes, Text = text, Rank = rank });

        // ---- density ------------------------------------------------------

        static void Densities(Dictionary<Texture, TextureUse> textures, Dictionary<Material, Area> areas)
        {
            foreach (var pair in areas)
            {
                var area = pair.Value;
                if (area.World <= 0.0001 || area.Uv <= 0.0001) continue;

                // Tiling repeats the map, so it multiplies coverage.
                var scale = pair.Key.HasProperty("_MainTex") ? pair.Key.mainTextureScale : Vector2.one;
                double uv = area.Uv * Mathf.Max(0.01f, Mathf.Abs(scale.x) * Mathf.Abs(scale.y));

                foreach (var tex in TexturesOf(pair.Key))
                {
                    if (!textures.TryGetValue(tex, out var use)) continue;
                    double texels = (double)use.Width * use.Height * uv;
                    float density = (float)System.Math.Sqrt(texels / area.World);
                    if (density <= use.Density) continue;
                    use.Density = density;
                    use.WorldArea = (float)area.World;
                }
            }

            foreach (var use in textures.Values)
            {
                if (use.Density <= 0f) continue;
                int longest = Mathf.Max(use.Width, use.Height);
                // Density scales with resolution, so the size that meets the
                // target falls straight out, rounded down to a power of two.
                int wanted = Mathf.FloorToInt(longest * (TargetDensity / use.Density));
                // Never advise below this. A tiny map on a tiny surface still
                // gets looked at from ten centimetres away in VR, and the
                // memory left on the table under 256 is not worth the row.
                int size = 256;
                while (size * 2 <= wanted && size < longest) size *= 2;
                use.Suggested = size < longest ? size : 0;
            }
        }

        static void AccumulateArea(Renderer r, Mesh mesh, Dictionary<Material, Area> areas, Dictionary<Mesh, MeshData> cache)
        {
            // Read/Write Enabled is off on most shipped meshes and does not
            // matter here: the editor holds the source data either way.
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

        // Every object path some layer switches on or off, so a renderer that
        // is off in the scene can be told apart from one a menu controls.
        static HashSet<string> SwitchedPaths(AvatarSurvey.Model survey)
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
            return paths;
        }

        static bool Live(Renderer r, Transform root, HashSet<string> switched)
        {
            if (r.gameObject.activeInHierarchy && r.enabled) return true;
            for (var t = r.transform; t != null; t = t.parent)
            {
                if (switched.Contains(AnimationUtility.CalculateTransformPath(t, root))) return true;
                if (t == root) break;
            }
            return false;
        }

        // ---- measurement ----------------------------------------------------

        // MagicaCloth is optional, so it is counted by name rather than by
        // a reference that would not compile without the package.
        static void CountCloth(CVRAvatar avatar, Report report)
        {
            foreach (var c in avatar.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (c == null) continue;
                string name = c.GetType().Name;
                if (name == "MagicaCloth") report.Cloth++;
                else if (name.StartsWith("Magica", System.StringComparison.Ordinal) && name.EndsWith("Collider", System.StringComparison.Ordinal)) report.ClothColliders++;
                else if (name == "DynamicBone") report.Cloth++;
                else if (name == "DynamicBoneCollider") report.ClothColliders++;
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
                Compressed = tex.graphicsFormat != GraphicsFormat.None
                             && GraphicsFormatUtility.IsCompressedFormat(tex.graphicsFormat),
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

        // What the card is spent on: the memory the graphics card holds.
        //
        // Profiler.GetRuntimeMemorySizeLong is not it. In the editor every
        // texture also keeps a copy on the CPU side, so it answers twice the
        // truth for all of them, which is worse than a rough number because
        // it looks precise. Crunch does not appear here either: it is a
        // download size, and a crunched texture unpacks to its plain DXT
        // size the moment it is uploaded.
        static long GpuBytes(Texture tex)
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

            // Compressed formats store one block per 4x4 patch, and a patch is
            // spent whole however little of it the last row or column uses.
            int wide = (width + 3) / 4;
            int tall = (height + 3) / 4;
            return (long)wide * tall * block;
        }

        static Mesh MeshOf(Renderer r)
        {
            if (r is SkinnedMeshRenderer skin) return skin.sharedMesh;
            var filter = r.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        static IEnumerable<Texture> TexturesOf(Material m)
        {
            if (m == null || m.shader == null) yield break;
            int count = ShaderUtil.GetPropertyCount(m.shader);
            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyType(m.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                var tex = m.GetTexture(ShaderUtil.GetPropertyName(m.shader, i));
                if (tex != null) yield return tex;
            }
        }

        public static string Text(Report r)
        {
            var sb = new StringBuilder();
            sb.Append("# Weight of ").Append(r.Avatar).Append('\n');
            sb.Append("textures   ").Append(r.Textures.Count).Append(", ")
              .Append(Mb(r.TextureBytes)).Append(" on the graphics card\n");
            sb.Append("meshes     ").Append(r.Renderers).Append(" renderers (").Append(r.Skinned).Append(" skinned), ")
              .Append(r.Triangles.ToString("N0")).Append(" tris, ").Append(r.SubMeshes).Append(" submeshes, ")
              .Append(r.Bones).Append(" bones\n");
            sb.Append("blendshapes ").Append(r.BlendShapes).Append(" (").Append(r.AnimatedBlendShapes).Append(" animated)\n");
            sb.Append("materials  ").Append(r.Materials).Append(" on ").Append(r.Shaders.Count).Append(" shader(s)\n");
            sb.Append("contacts   ").Append(r.Pointers + r.Triggers)
              .Append(" (").Append(r.Pointers).Append(" pointers, ").Append(r.Triggers)
              .Append(" triggers) against 512 overlapping pairs a frame for the whole instance\n");
            sb.Append("physics    ").Append(r.Cloth).Append(" cloth, ").Append(r.ClothColliders).Append(" colliders\n");
            sb.Append("audio      ").Append(r.AudioSources).Append(" of ChilloutVR's 100\n");
            sb.Append("lights     ").Append(r.Lights).Append(" (").Append(r.MarkerLights)
              .Append(" YAPS markers) against four vertex slots a mesh\n");
            sb.Append("particles  ").Append(r.ParticleSystems).Append(" systems, ")
              .Append(r.MaxParticles.ToString("N0")).Append(" max\n\n");

            if (r.Callouts.Count > 0)
            {
                long saved = r.Callouts.Sum(c => c.Bytes);
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

        static string Mb(long bytes) => (bytes / 1048576f).ToString("0.0") + " MB";
    }
}
#endif
