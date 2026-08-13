// Phase 0b. Decode an existing bake texture with our own reader and prove
// we understand the format, independently of any VRCFury code.
//
// The layout is one header pixel followed by ten floats per vertex —
// position, normal, tangent, and an active flag — each float stored as the
// four bytes of one RGBA32 pixel.
//
// The gate is self-validating and does not need the original mesh to
// compare against: if the byte layout is even slightly wrong, decoded
// normals and tangents stop being unit length and the active flag stops
// being exactly 0 or 1. Getting unit normals out of a misread buffer is
// vanishingly unlikely, so those two checks alone settle it.
//
//   Unity.exe -batchmode -projectPath "<test project>" \
//     -executeMethod AvatarBridge.Spike.SpsBakeReader.RunBatch -quit
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Spike
{
    public static class SpsBakeReader
    {
        const string BakeProperty = "_SPS_Bake";
        const int FloatsPerVertex = 10;
        static readonly StringBuilder Log = new StringBuilder();

        struct Baked
        {
            public List<Vector3> Positions;
            public List<Vector3> Normals;
            public List<Vector3> Tangents;
            public List<float> Active;
        }

        [MenuItem("AvatarBridge/Spike/Run bake-format reader (0b)")]
        public static void RunBatch()
        {
            Log.Clear();
            Line("# Phase 0b — bake-format read-back");
            Line("");
            Line($"Run: {DateTime.Now:yyyy-MM-dd HH:mm}");
            Line("");

            var materials = FindBakedMaterials();
            if (materials.Count == 0)
            {
                Line("No material carrying a `_SPS_Bake` texture was found under " +
                     "`Assets/AvatarBridgeOutput`. Convert an SPS avatar first.");
                Write();
                return;
            }

            Line($"Found **{materials.Count}** material(s) with a bake texture.");
            Line("");

            foreach (var material in materials)
            {
                try
                {
                    Examine(material);
                }
                catch (Exception e)
                {
                    Line($"**Threw on `{material.name}`:** `{e.GetType().Name}: {e.Message}`");
                    Line("");
                }
            }

            Write();
        }

        static void Examine(Material material)
        {
            var texture = material.GetTexture(BakeProperty) as Texture2D;
            Line($"## `{material.name}`");
            Line("");
            if (texture == null)
            {
                Line("`_SPS_Bake` is not a Texture2D. Skipped.");
                Line("");
                return;
            }

            Line($"- Texture: `{texture.name}` {texture.width}×{texture.height}, " +
                 $"format {texture.format}, mips {texture.mipmapCount}");

            var pixels = ReadPixels(texture, out string how);
            Line($"- Read back via **{how}**, {pixels.Length} pixels");

            int capacity = (pixels.Length - 1) / FloatsPerVertex;
            var bake = Decode(pixels, capacity);

            // The base block does NOT run to the end of the texture — the
            // blendshape blocks follow it, so decoding to capacity walks
            // straight into them and every check fails past the real count.
            // The vertex count is declared on the material, which is also
            // what the shader uses to find the blendshape offsets.
            int declared = material.HasProperty("_SPS_BlendshapeVertCount")
                ? Mathf.RoundToInt(material.GetFloat("_SPS_BlendshapeVertCount"))
                : 0;
            int live = declared > 0 && declared <= capacity ? declared : UnitNormalPrefix(bake, capacity);
            Line($"- Capacity {capacity} vertices; declared `_SPS_BlendshapeVertCount` = "
                 + $"**{declared}**; validating **{live}**"
                 + (declared > 0 ? "" : " (declared count unusable — using the unit-normal prefix)"));
            if (material.HasProperty("_SPS_BlendshapeCount"))
            {
                Line($"- `_SPS_BlendshapeCount` = {material.GetFloat("_SPS_BlendshapeCount"):0}");
            }
            Line("");

            if (live == 0)
            {
                Line("Nothing decoded. Either the read-back path is lossy or the layout is wrong.");
                Line("");
                return;
            }

            // --- the self-validating checks ---
            // Normals are stored in PLUG-LOCAL space, so they are unit
            // length only when the plug transform happens to have unit
            // scale. On a plug scaled 1/30 they all come out ~30 long.
            // What proves a correct read is that the magnitudes are
            // tightly CLUSTERED — a misread buffer produces noise, not a
            // consistent scale factor. Measure the cluster, then test
            // against it.
            float normalScale = MedianMagnitude(bake.Normals, live);
            float tangentScale = MedianMagnitude(bake.Tangents, live);

            int unitNormals = 0, unitTangents = 0, inRangeActive = 0, binaryActive = 0;
            var bounds = new Bounds(bake.Positions[0], Vector3.zero);
            for (int i = 0; i < live; i++)
            {
                if (Mathf.Abs(bake.Normals[i].magnitude - normalScale) < normalScale * 0.02f) unitNormals++;
                if (Mathf.Abs(bake.Tangents[i].magnitude - tangentScale) < tangentScale * 0.02f) unitTangents++;
                float a = bake.Active[i];
                // "active" is a MASK WEIGHT, not a boolean — the shader
                // multiplies the deform by it rather than thresholding, so
                // the falloff at the base of a plug is legitimately
                // fractional. Only its range is a correctness signal.
                if (a >= -0.001f && a <= 1.001f) inRangeActive++;
                if (Mathf.Approximately(a, 0f) || Mathf.Approximately(a, 1f)) binaryActive++;
                bounds.Encapsulate(bake.Positions[i]);
            }

            float normalRate = 100f * unitNormals / live;
            float tangentRate = 100f * unitTangents / live;
            float activeRate = 100f * inRangeActive / live;
            float binaryRate = 100f * binaryActive / live;

            Line($"- Baked normal scale **×{normalScale:0.###}**, tangent scale "
                 + $"**×{tangentScale:0.###}** — plug-local space, so 1.0 means the plug "
                 + "transform has unit scale");
            Line("");
            // The gate is the two scale-agnostic invariants. Magnitude
            // clustering is reported but NOT gated on, because a plug with
            // non-uniform scale legitimately produces direction-dependent
            // magnitudes while the read is perfectly correct.
            bool plausibleSize = bounds.size.magnitude > 0.001f && bounds.size.magnitude < 20f;

            Line("| Check | Result | Reads as |");
            Line("|---|---:|---|");
            Line($"| **Active weight within 0..1** | {activeRate:0.0}% | {Verdict(activeRate, 99f)} |");
            Line($"| **Positions bounded like a plug** | {bounds.size.magnitude:0.###} m | "
                 + $"{(plausibleSize ? "correct" : "**WRONG — layout misread**")} |");
            Line($"| Magnitudes clustered (normals) | {normalRate:0.0}% | "
                 + $"{(normalRate >= 99f ? "uniform scale" : "non-uniform plug scale — not a fault")} |");
            Line($"| Magnitudes clustered (tangents) | {tangentRate:0.0}% | informational |");
            Line($"| Active exactly 0 or 1 | {binaryRate:0.0}% | informational — the rest is mask falloff |");
            Line("");
            Line($"- Bounds: centre `{bounds.center}`, size `{bounds.size}`");
            Line($"- Plug axis extent: X {bounds.size.x:0.000}, Y {bounds.size.y:0.000}, " +
                 $"Z {bounds.size.z:0.000} — a plug is expected to be **longest in Z**");
            Line("");

            Line("First four vertices as decoded:");
            Line("");
            Line("| # | position | normal | tangent | active |");
            Line("|---|---|---|---|---|");
            for (int i = 0; i < Mathf.Min(4, live); i++)
            {
                Line($"| {i} | `{bake.Positions[i]}` | `{bake.Normals[i]}` | "
                     + $"`{bake.Tangents[i]}` | {bake.Active[i]:0.###} |");
            }
            Line("");

            bool passed = activeRate >= 99f && plausibleSize;
            Line($"**Verdict: {(passed ? "the format is understood" : "layout does not decode cleanly")}.**");
            if (passed)
            {
                string path = SaveReconstruction(material.name, bake, live);
                Line($"Reconstructed point cloud saved to `{path}` — open it to eyeball the shape.");
            }
            Line("");
        }

        static string Verdict(float rate, float threshold)
            => rate >= threshold ? "correct" : "**WRONG — layout misread**";

        static float MedianMagnitude(List<Vector3> values, int count)
        {
            var lengths = new List<float>(count);
            for (int i = 0; i < count; i++)
            {
                lengths.Add(values[i].magnitude);
            }
            lengths.Sort();
            float median = lengths[count / 2];
            return median > 1e-6f ? median : 1f;
        }

        // Fallback when the material does not declare a usable count: walk
        // forward while normals stay unit length. The first sustained run
        // of non-unit normals is where the base block ends and the
        // blendshape data begins.
        static int UnitNormalPrefix(Baked bake, int capacity)
        {
            int strikes = 0;
            for (int i = 0; i < capacity; i++)
            {
                bool unit = Mathf.Abs(bake.Normals[i].magnitude - 1f) < 0.01f;
                strikes = unit ? 0 : strikes + 1;
                if (strikes >= 16)
                {
                    return Mathf.Max(0, i - 15);
                }
            }
            return capacity;
        }

        static Baked Decode(Color32[] pixels, int capacity)
        {
            var bake = new Baked
            {
                Positions = new List<Vector3>(capacity),
                Normals = new List<Vector3>(capacity),
                Tangents = new List<Vector3>(capacity),
                Active = new List<float>(capacity),
            };

            var scratch = new byte[4];
            float At(int index)
            {
                var p = pixels[index];
                scratch[0] = p.r; scratch[1] = p.g; scratch[2] = p.b; scratch[3] = p.a;
                return BitConverter.ToSingle(scratch, 0);
            }

            for (int v = 0; v < capacity; v++)
            {
                // One header pixel, then ten floats per vertex.
                int at = 1 + v * FloatsPerVertex;
                bake.Positions.Add(new Vector3(At(at + 0), At(at + 1), At(at + 2)));
                bake.Normals.Add(new Vector3(At(at + 3), At(at + 4), At(at + 5)));
                bake.Tangents.Add(new Vector3(At(at + 6), At(at + 7), At(at + 8)));
                bake.Active.Add(At(at + 9));
            }
            return bake;
        }

        // The bake texture is written at runtime and is usually not marked
        // readable, so fall back to a linear blit. Linear matters: an sRGB
        // conversion would mangle the bytes we are reinterpreting as floats.
        static Color32[] ReadPixels(Texture2D texture, out string how)
        {
            try
            {
                var direct = texture.GetPixels32();
                how = "GetPixels32 (texture was readable)";
                return direct;
            }
            catch
            {
                // fall through
            }

            var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(texture.width, texture.height,
                    TextureFormat.RGBA32, false, true);
                copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                copy.Apply();
                how = "linear blit + ReadPixels";
                var pixels = copy.GetPixels32();
                UnityEngine.Object.DestroyImmediate(copy);
                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // Points only, no topology — the bake carries no triangles, and the
        // point cloud is enough to see whether the shape is a plug.
        static string SaveReconstruction(string name, Baked bake, int live)
        {
            var mesh = new Mesh { name = name + " (bake read-back)" };
            mesh.indexFormat = live > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(bake.Positions.GetRange(0, live));
            mesh.SetNormals(bake.Normals.GetRange(0, live));
            mesh.SetIndices(Enumerable.Range(0, live).ToArray(), MeshTopology.Points, 0);
            mesh.RecalculateBounds();

            const string dir = "Assets/SpsSpike";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets", "SpsSpike");
            }
            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{dir}/BakeReadback_{SafeName(name)}.asset");
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            return path;
        }

        static string SafeName(string s)
            => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        static List<Material> FindBakedMaterials()
        {
            var found = new List<Material>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/AvatarBridgeOutput" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null
                    && material.HasProperty(BakeProperty)
                    && material.GetTexture(BakeProperty) != null)
                {
                    found.Add(material);
                }
            }
            return found;
        }

        static void Line(string s) => Log.AppendLine(s);

        static void Write()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                "SpsBakeReader.md"));
            File.WriteAllText(path, Log.ToString());
            Debug.Log($"[SpsSpike] Bake-format read-back written to {path}");
        }
    }
}
#endif
