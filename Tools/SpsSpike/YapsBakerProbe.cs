// Phase 1c verification. Bakes a real avatar's plug with our own baker and
// checks the result three ways.
//
// The first two are self-validating and need nothing external: normals are
// written normalised, so if the byte packing or the header offset are even
// slightly wrong the decoded normals stop being unit length and the active
// weights leave 0..1. Getting unit normals out of a misread buffer does not
// happen by luck.
//
// The third is the one worth having. VRCFury baked this same mesh, and both
// bakes are indexed by mesh vertex, so vertex i in ours IS vertex i in
// theirs. Their `active` mask is directly comparable — it is frame-
// independent — and the shape is comparable up to one rigid transform, so
// the per-vertex distance from the centroid should differ by a single
// constant ratio across the whole mesh. A wandering ratio means our
// placement is wrong; a constant ratio that is not 1 means our units are.
//
//   Unity.exe -batchmode -projectPath "<test project>" \
//     -executeMethod AvatarBridge.Spike.YapsBakerProbe.RunBatch -quit
#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Spike
{
    public static class YapsBakerProbe
    {
        const string SceneRelative = "Assets/Kemonoroo/AngelaFlux/Angela_PC_SPS.unity";
        const string OutputDir = "Assets/SpsSpike/YapsBakeProbe";
        const int FloatsPerVertex = 10;
        static readonly StringBuilder Log = new StringBuilder();

        [MenuItem("AvatarBridge/Spike/Probe the YAPS baker (1c)")]
        public static void RunBatch()
        {
            Log.Clear();
            Line("# Phase 1c — baker probe");
            Line("");
            Line($"Scene: `{SceneRelative}`  ");
            Line($"Run: {DateTime.Now:yyyy-MM-dd HH:mm}");
            Line("");

            try
            {
                CaptureReferences();
                Probe();
            }
            catch (Exception e)
            {
                Line("");
                Line($"**Probe threw:** `{e.GetType().Name}: {e.Message}`");
                Line("```");
                Line(e.StackTrace ?? "");
                Line("```");
            }

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                "YapsBakerProbe.md"));
            File.WriteAllText(path, Log.ToString());
            Debug.Log($"[YAPS] Baker probe written to {path}");
        }

        // VRCFury's own bake of each plug, keyed by the plug's path, taken
        // from a first pass with SPS left ON.
        //
        // Two passes rather than one because the two configurations answer
        // different halves of the question and neither is optional. With SPS
        // on there is a reference bake to compare against per vertex, which
        // is what proves the mask and the geometry; with it suppressed there
        // is the frame a real conversion actually gets, which is where the
        // 0.427-baked-as-0.667 bug lived. Measuring only the first bought
        // confidence in a configuration that never ships. Measuring only the
        // second lost the ground truth. So: both, and diff them.
        class Reference
        {
            public Texture2D Bake;
            public float Length;
        }

        static readonly Dictionary<string, Reference> References =
            new Dictionary<string, Reference>();

        static void CaptureReferences()
        {
            References.Clear();
            var scene = EditorSceneManager.OpenScene(SceneRelative, OpenSceneMode.Single);
            var descriptor = scene.GetRootGameObjects()
                .Select(go => go.GetComponentInChildren<
                    VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true))
                .FirstOrDefault(d => d != null);
            if (descriptor == null)
            {
                return;
            }

            var working = UnityEngine.Object.Instantiate(descriptor.gameObject);
            working.name = descriptor.gameObject.name + " (reference)";
            working.SetActive(true);
            var baked = VRCFuryBaker.TryBake(
                working.GetComponentInChildren<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true),
                new BridgeReport());
            if (baked == null)
            {
                return;
            }

            foreach (var plugRoot in baked.GetComponentsInChildren<Transform>(true)
                         .Where(t => t.name.Contains("BakedSpsPlug")))
            {
                string path = PathOf(plugRoot, baked.transform);
                Texture2D texture = null;
                foreach (var renderer in baked.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material != null && material.HasProperty("_SPS_Bake"))
                        {
                            texture = texture ?? material.GetTexture("_SPS_Bake") as Texture2D;
                        }
                    }
                }
                References[path] = new Reference
                {
                    Bake = texture,
                    Length = AnimatedLength(baked, path),
                };
            }

            Line($"Captured **{References.Count}** reference bake(s) with SPS enabled, to compare " +
                 "the shipping configuration against.");
            Line("");
        }

        static void Probe()
        {
            var scene = EditorSceneManager.OpenScene(SceneRelative, OpenSceneMode.Single);
            var descriptor = scene.GetRootGameObjects()
                .Select(go => go.GetComponentInChildren<
                    VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true))
                .FirstOrDefault(d => d != null);
            if (descriptor == null)
            {
                Line("No VRC avatar descriptor in the scene. Nothing to bake.");
                return;
            }

            var working = UnityEngine.Object.Instantiate(descriptor.gameObject);
            working.name = descriptor.gameObject.name + " (baker probe)";
            working.SetActive(true);

            // SPS SUPPRESSED, exactly as the converter does it.
            //
            // This probe used to bake with SPS enabled, on the reasoning
            // that it wanted VRCFury's own bake sitting next to ours to
            // compare against. It got a perfect x1.0000 match on that basis
            // and was trusted — while the configuration that actually ships
            // was never measured at all. Suppressing SPS moves the
            // BakedSpsPlug object: its rotation stops pointing down the
            // shaft and its position moves a quarter of a metre up the
            // body, and a plug 0.427 m long baked at 0.667. That reached a
            // live test.
            //
            // A probe that measures a configuration you never ship is worse
            // than no probe, because it buys confidence you have not
            // earned.
            var report = new BridgeReport();
            var settings = new BridgeSettings { convertYapsSystems = true };
            var ctx = new BridgeContext { Settings = settings, Report = report };
            var prep = YapsBakePrep.Begin(ctx, working);
            GameObject baked;
            try
            {
                baked = VRCFuryBaker.TryBake(
                    working.GetComponentInChildren<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true),
                    report);
            }
            finally
            {
                prep.Restore();
            }
            if (baked == null)
            {
                Line("VRCFury's bake returned nothing, so there is nothing to measure.");
                return;
            }

            var plugRoots = baked.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.Contains("BakedSpsPlug"))
                .ToList();
            Line($"VRCFury baked **{plugRoots.Count}** plug(s).");
            Line("");
            if (plugRoots.Count == 0)
            {
                return;
            }

            foreach (var plugRoot in plugRoots)
            {
                ProbeOnePlug(baked, plugRoot);
            }

            EditorSceneManager.OpenScene(SceneRelative, OpenSceneMode.Single);
        }

        static void ProbeOnePlug(GameObject baked, Transform plugRoot)
        {
            Line($"## `{PathOf(plugRoot, baked.transform)}`");
            Line("");

            // The renderer the plug lives on is the one whose material
            // VRCFury gave a bake texture to.
            Renderer renderer = null;
            Texture2D reference = null;
            float referenceLength = 0f;
            foreach (var candidate in baked.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in candidate.sharedMaterials)
                {
                    if (material == null || !material.HasProperty("_SPS_Bake"))
                    {
                        continue;
                    }
                    var texture = material.GetTexture("_SPS_Bake") as Texture2D;
                    if (texture == null)
                    {
                        continue;
                    }
                    renderer = candidate;
                    reference = texture;
                    break;
                }
                if (renderer != null)
                {
                    break;
                }
            }

            // With SPS suppressed there is no `_SPS_Bake` anywhere, which is
            // the whole point — so the renderer is found the way the
            // CONVERTER finds it, by which one carries the most vertices on
            // the plug's bone chain. No reference bake means no per-vertex
            // comparison; what this measures instead is the configuration
            // that actually ships.
            // The reference pass kept VRCFury's own bake of this same plug,
            // keyed by path. It survives the scene reload because it is an
            // in-memory texture we are still holding.
            if (References.TryGetValue(PathOf(plugRoot, baked.transform), out var kept))
            {
                reference = kept.Bake;
                referenceLength = kept.Length;
            }

            if (renderer == null)
            {
                int mostVertices = 0;
                foreach (var candidate in baked.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    int score = YapsBaker.CountPlugVertices(candidate, plugRoot);
                    if (score > mostVertices)
                    {
                        mostVertices = score;
                        renderer = candidate;
                    }
                }
                Line("No `_SPS_Bake` on this avatar — SPS was suppressed, as it is in a real " +
                     "conversion. Renderer found the way the converter finds it, and there is no " +
                     "reference bake to compare against.");
                Line("");
            }
            if (renderer == null)
            {
                Line("No renderer carries vertices on this plug's bones. Skipped.");
                Line("");
                return;
            }
            Line($"Renderer: `{PathOf(renderer.transform, baked.transform)}` " +
                 $"({renderer.GetType().Name})");
            // Their length is not authored on the material — it is animated
            // onto the resolver, which is why reading the material gives a
            // flat zero. It comes from the REFERENCE pass, since the curve
            // is stripped along with everything else once SPS is suppressed.
            Line($"Their `_SPS_BakedLength`: **{referenceLength:0.00000}** " +
                 "(read from the animator curve, not the material — VRCFury drives it)");
            Line("");

            // Which transform is "the plug root" is a convention, and the
            // wrong choice shows up as a shaft that does not run along Z.
            // Try the plausible ones and print the axis extents for each
            // rather than asserting one and hoping.
            var candidates = new List<(string label, Transform frame)>
            {
                ("BakedSpsPlug", plugRoot),
            };
            foreach (Transform child in plugRoot)
            {
                candidates.Add(($"BakedSpsPlug/{child.name}", child));
            }
            if (plugRoot.parent != null)
            {
                candidates.Add(("its parent", plugRoot.parent));
            }

            Line("| Frame tried | length (Z) | X extent | Y extent | Z extent | verdict |");
            Line("|---|---:|---:|---:|---:|---|");
            YapsBaker.Result best = null;
            string bestLabel = null;
            foreach (var (label, frame) in candidates)
            {
                var attempt = YapsBaker.Bake(renderer, frame, OutputDir, null, out string failure);
                if (attempt == null)
                {
                    Line($"| `{label}` | — | — | — | — | refused — {failure} |");
                    continue;
                }
                var decoded = Decode(attempt.Bake, attempt.VertexCount);
                var bounds = ActiveBounds(decoded);
                bool alongZ = bounds.size.z >= bounds.size.x && bounds.size.z >= bounds.size.y;
                Line($"| `{label}` | {attempt.Length:0.000} | {bounds.size.x:0.000} | " +
                     $"{bounds.size.y:0.000} | {bounds.size.z:0.000} | " +
                     $"{(alongZ ? "**runs along Z**" : "not along Z")} |");
                if (alongZ && (best == null || attempt.Length > best.Length))
                {
                    best = attempt;
                    bestLabel = label;
                }
            }
            Line("");

            if (best == null)
            {
                Line("**No candidate frame put the shaft along Z.** Either the plug root " +
                     "convention is wrong or the bake is not placing vertices correctly.");
                Line("");
                return;
            }
            Line($"Taking `{bestLabel}` as the plug root for the checks below.");
            Line("");

            var ours = Decode(best.Bake, best.VertexCount);
            SelfChecks(best, ours);
            if (reference != null)
            {
                CompareWithReference(ours, reference, best, referenceLength);
            }
            else
            {
                Line("### Against VRCFury");
                Line("");
                Line($"No reference bake exists with SPS suppressed, so the only cross-check left " +
                     $"is the length: **{best.Length:0.00000} m**. VRCFury measured this plug at " +
                     "**0.42706278** when it baked it with SPS on. A number far from that means " +
                     "the frame is wrong, and the frame is what everything downstream scales by.");
                Line("");
                Line("**A proper two-pass probe — bake once with SPS on for ground truth, once " +
                     "suppressed for what ships, and diff them — is the version worth having.**");
                Line("");
            }
        }

        // --- the self-validating half ---------------------------------

        static void SelfChecks(YapsBaker.Result result, Baked ours)
        {
            int unitNormals = 0, unitTangents = 0, inRange = 0, active = 0;
            float maxZ = float.MinValue;
            for (int i = 0; i < ours.Count; i++)
            {
                if (Mathf.Abs(ours.Normals[i].magnitude - 1f) < 0.001f) unitNormals++;
                if (Mathf.Abs(ours.Tangents[i].magnitude - 1f) < 0.001f) unitTangents++;
                float a = ours.Active[i];
                if (a >= -0.0001f && a <= 1.0001f) inRange++;
                if (a > 0.001f)
                {
                    active++;
                    maxZ = Mathf.Max(maxZ, ours.Positions[i].z);
                }
            }

            float normalRate = 100f * unitNormals / ours.Count;
            float tangentRate = 100f * unitTangents / ours.Count;
            float rangeRate = 100f * inRange / ours.Count;
            // The length was measured before the texture was written, so if
            // it survives a decode the header offset and the stride are both
            // right — a shifted read would land on a different channel.
            bool lengthAgrees = Mathf.Abs(maxZ - result.Length) < 1e-4f;

            Line("### Does it decode as written");
            Line("");
            Line("| Check | Result | Reads as |");
            Line("|---|---:|---|");
            Line($"| Normals unit length | {normalRate:0.00}% | {Verdict(normalRate >= 99.9f)} |");
            Line($"| Tangents unit length | {tangentRate:0.00}% | {Verdict(tangentRate >= 99.9f)} |");
            Line($"| Active weight within 0..1 | {rangeRate:0.00}% | {Verdict(rangeRate >= 99.9f)} |");
            Line($"| Decoded length matches measured | {maxZ:0.0000} vs {result.Length:0.0000} | " +
                 $"{Verdict(lengthAgrees)} |");
            Line("");
            Line($"- {active} of {ours.Count} vertices carry any weight on the plug's bone chain " +
                 $"({100f * active / ours.Count:0.0}% of the mesh)");
            Line($"- Texture {result.Bake.width}×{result.Bake.height}, {result.Bake.format}, " +
                 $"filter {result.Bake.filterMode}, mips {result.Bake.mipmapCount}");
            Line("");
        }

        // --- the half that uses VRCFury's bake as ground truth ---------

        static void CompareWithReference(Baked ours, Texture2D reference, YapsBaker.Result result,
            float referenceLength)
        {
            Line("### Against VRCFury's bake of the same mesh");
            Line("");

            var theirs = Decode(reference, ours.Count);
            if (theirs == null)
            {
                Line("Their bake could not be read back. No comparison possible.");
                Line("");
                return;
            }

            // Their mask and ours describe the same thing — how much of each
            // vertex belongs to the plug — and neither depends on a frame,
            // so this compares directly with no fitting at all.
            int agree = 0, bothActive = 0, onlyOurs = 0, onlyTheirs = 0;
            for (int i = 0; i < ours.Count; i++)
            {
                float a = ours.Active[i];
                float b = theirs.Active[i];
                if (Mathf.Abs(a - b) < 0.05f) agree++;
                bool ourActive = a > 0.5f;
                bool theirActive = b > 0.5f;
                if (ourActive && theirActive) bothActive++;
                else if (ourActive) onlyOurs++;
                else if (theirActive) onlyTheirs++;
            }

            // Shape: both point clouds are the same mesh in some frame, so
            // distance from the centroid can only differ by one constant
            // ratio. Measure it on the vertices both agree are plug.
            var ratios = new List<float>();
            Vector3 ourCentre = Centroid(ours, theirs);
            Vector3 theirCentre = Centroid(theirs, theirs);
            for (int i = 0; i < ours.Count; i++)
            {
                if (theirs.Active[i] <= 0.5f || ours.Active[i] <= 0.5f)
                {
                    continue;
                }
                float mine = (ours.Positions[i] - ourCentre).magnitude;
                float yours = (theirs.Positions[i] - theirCentre).magnitude;
                if (yours > 1e-4f)
                {
                    ratios.Add(mine / yours);
                }
            }

            if (ratios.Count == 0)
            {
                Line("No vertex is active in both bakes, so the shapes cannot be compared.");
                Line("");
                return;
            }
            ratios.Sort();
            float median = ratios[ratios.Count / 2];
            int consistent = ratios.Count(r => Mathf.Abs(r - median) < median * 0.02f);
            float consistentRate = 100f * consistent / ratios.Count;

            Line("| Check | Result | Reads as |");
            Line("|---|---:|---|");
            Line($"| Mask weights within 0.05 | {100f * agree / ours.Count:0.0}% | " +
                 $"{Verdict(100f * agree / ours.Count >= 90f)} |");
            Line($"| Vertices both call plug | {bothActive} | — |");
            Line($"| Only ours calls plug | {onlyOurs} | {(onlyOurs > bothActive * 0.1f ? "**we over-reach**" : "fine")} |");
            Line($"| Only theirs | {onlyTheirs} | {(onlyTheirs > bothActive * 0.1f ? "**we under-reach**" : "fine")} |");
            Line($"| Shape ratio consistent | {consistentRate:0.0}% at ×{median:0.0000} | " +
                 $"{Verdict(consistentRate >= 98f)} |");
            float lengthRatio = referenceLength > 1e-5f ? result.Length / referenceLength : 0f;
            Line($"| Our length vs their `_SPS_BakedLength` | {result.Length:0.00000} vs " +
                 $"{referenceLength:0.00000} | " +
                 $"{(referenceLength <= 1e-5f ? "no curve found" : Verdict(Mathf.Abs(lengthRatio - 1f) < 0.001f))} " +
                 $"(×{lengthRatio:0.0000}) |");
            Line("");
            Line("A ratio of **×1.0000** means our bake is in the same units as theirs. Any other " +
                 "constant is a scale convention difference, which is only a problem if it is not " +
                 "the one the deform expects. A ratio that is not constant at all means the " +
                 "placement is wrong, not the units.");
            Line("");
        }

        // The largest value the curve ever takes on any object under this
        // plug. The clips hold zeros too — that is the plug switched off —
        // so the maximum is the baked length.
        static float AnimatedLength(GameObject root, string plugPath)
        {
            var animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return 0f;
            }

            float longest = 0f;
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip == null)
                {
                    continue;
                }
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.propertyName != "material._SPS_BakedLength"
                        || !binding.path.StartsWith(plugPath, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    foreach (var key in curve.keys)
                    {
                        longest = Mathf.Max(longest, key.value);
                    }
                }
            }
            return longest;
        }

        static Vector3 Centroid(Baked of, Baked mask)
        {
            var sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < of.Count; i++)
            {
                if (mask.Active[i] > 0.5f)
                {
                    sum += of.Positions[i];
                    n++;
                }
            }
            return n > 0 ? sum / n : Vector3.zero;
        }

        static Bounds ActiveBounds(Baked bake)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool started = false;
            for (int i = 0; i < bake.Count; i++)
            {
                if (bake.Active[i] <= 0.001f)
                {
                    continue;
                }
                if (!started)
                {
                    bounds = new Bounds(bake.Positions[i], Vector3.zero);
                    started = true;
                }
                else
                {
                    bounds.Encapsulate(bake.Positions[i]);
                }
            }
            return bounds;
        }

        // --- decoding --------------------------------------------------

        class Baked
        {
            public int Count;
            public Vector3[] Positions;
            public Vector3[] Normals;
            public Vector3[] Tangents;
            public float[] Active;
        }

        static Baked Decode(Texture2D texture, int count)
        {
            var pixels = ReadPixels(texture);
            if (pixels == null || pixels.Length < 1 + count * FloatsPerVertex)
            {
                return null;
            }

            var bake = new Baked
            {
                Count = count,
                Positions = new Vector3[count],
                Normals = new Vector3[count],
                Tangents = new Vector3[count],
                Active = new float[count],
            };

            var scratch = new byte[4];
            float At(int index)
            {
                var p = pixels[index];
                scratch[0] = p.r; scratch[1] = p.g; scratch[2] = p.b; scratch[3] = p.a;
                return BitConverter.ToSingle(scratch, 0);
            }

            for (int v = 0; v < count; v++)
            {
                int at = 1 + v * FloatsPerVertex;
                bake.Positions[v] = new Vector3(At(at + 0), At(at + 1), At(at + 2));
                bake.Normals[v] = new Vector3(At(at + 3), At(at + 4), At(at + 5));
                bake.Tangents[v] = new Vector3(At(at + 6), At(at + 7), At(at + 8));
                bake.Active[v] = At(at + 9);
            }
            return bake;
        }

        // Linear matters: an sRGB conversion mangles bytes that are about to
        // be reinterpreted as floats.
        static Color32[] ReadPixels(Texture2D texture)
        {
            try
            {
                return texture.GetPixels32();
            }
            catch
            {
                // fall through to the blit
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
                var pixels = copy.GetPixels32();
                UnityEngine.Object.DestroyImmediate(copy);
                return pixels;
            }
            catch
            {
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static string Verdict(bool ok) => ok ? "correct" : "**WRONG**";

        static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            while (t != null && t != root)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }
            return string.Join("/", parts);
        }

        static void Line(string s) => Log.AppendLine(s);
    }
}
#endif
