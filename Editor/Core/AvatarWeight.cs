// What an avatar costs, measured against what ChilloutVR charges for it.
//
// Textures first, because that is where a heavy avatar is heavy and the
// number nobody has: VRAM comes from Unity's own accounting rather than
// from width times height, so a crunched BC7 map is not mistaken for a
// cheap one. Everything else is counted against the limit it spends.
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
        public class TextureUse
        {
            public Texture Texture;
            public string Name;
            public int Width;
            public int Height;
            public string Format;
            public bool Crunched;
            public bool Readable;
            public long Bytes;
            public int Materials;
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

            // Blendshapes only count as live when something animates them.
            var animated = new HashSet<string>();
            if (survey != null)
            {
                foreach (string binding in survey.Layers.SelectMany(l => l.Bindings))
                {
                    int cut = binding.IndexOf("::", System.StringComparison.Ordinal);
                    if (cut < 0) continue;
                    string property = binding.Substring(cut + 2);
                    if (property.StartsWith("blendShape.", System.StringComparison.Ordinal)) animated.Add(binding);
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
                if (mesh != null)
                {
                    report.Triangles += mesh.triangles.Length / 3;
                    report.SubMeshes += mesh.subMeshCount;
                    report.BlendShapes += mesh.blendShapeCount;
                    string path = AnimationUtility.CalculateTransformPath(r.transform, avatar.transform);
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                    {
                        if (animated.Contains($"{path}::blendShape.{mesh.GetBlendShapeName(i)}")) report.AnimatedBlendShapes++;
                    }
                }

                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !materials.Add(m)) continue;
                    if (m.shader != null) report.Shaders.Add(m.shader.name);
                    foreach (var tex in TexturesOf(m))
                    {
                        if (textures.TryGetValue(tex, out var use)) { use.Materials++; continue; }
                        textures[tex] = Describe(tex);
                    }
                }
            }

            report.Bones = bones.Count;
            report.Materials = materials.Count;
            report.Textures.AddRange(textures.Values.OrderByDescending(t => t.Bytes));
            report.TextureBytes = report.Textures.Sum(t => t.Bytes);

            report.AudioSources = avatar.GetComponentsInChildren<AudioSource>(true).Length;
            var particles = avatar.GetComponentsInChildren<ParticleSystem>(true);
            report.ParticleSystems = particles.Length;
            report.MaxParticles = particles.Sum(p => p != null ? p.main.maxParticles : 0);
            var lights = avatar.GetComponentsInChildren<Light>(true);
            report.Lights = lights.Length;
            report.MarkerLights = lights.Count(YapsScannerIsProtocolLight);
            report.Pointers = avatar.GetComponentsInChildren<CVRPointer>(true).Length;
            report.Triggers = avatar.GetComponentsInChildren<CVRAdvancedAvatarSettingsTrigger>(true).Length;

            CountCloth(avatar, report);
            return report;
        }

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

        static bool YapsScannerIsProtocolLight(Light l)
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
              .Append(Mb(r.TextureBytes)).Append(" on the graphics card");
            int readable = r.Textures.Count(t => t.Readable);
            if (readable > 0)
            {
                sb.Append(", ").Append(readable).Append(" of them read/write so a second copy sits in system memory (")
                  .Append(Mb(r.Textures.Where(t => t.Readable).Sum(t => t.Bytes))).Append(" more)");
            }
            sb.Append('\n');
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

            sb.Append("## heaviest textures\n");
            foreach (var t in r.Textures.Take(15))
            {
                sb.Append("  ").Append(Mb(t.Bytes).PadLeft(9)).Append("  ")
                  .Append(t.Width).Append('x').Append(t.Height).Append("  ")
                  .Append(t.Format).Append(t.Crunched ? " crunched" : "")
                  .Append(t.Readable ? " read/write" : "").Append("  ")
                  .Append(t.Name).Append('\n');
            }
            return sb.ToString();
        }

        static string Mb(long bytes) => (bytes / 1048576f).ToString("0.0") + " MB";
    }
}
#endif
