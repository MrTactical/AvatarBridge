// S4 of the portal plan: how long are a real plug's triangles along the shaft?
// The portal collapses a band of 2w at the cut, w = span x length, where span is
// 1.25 x the largest rest z-span of any triangle whose three corners the bake
// marks active, over the bake's length. Above 0.2 the bake refuses the portal.
// Gate: across real plugs, median 2w <= 0.2 L.
//
// Read from what the build wrote, never re-derived: the slot's triangles from the
// renderer's mesh, and each corner's z and active weight from the bake texture,
// active as the deform reads it (weight > 0 and z >= 0). Rest pose only.
//
// Rides a corpus run when AVATARBRIDGE_PORTALSPAN names a folder: each converted
// avatar writes <scene>.txt there and _summary.txt is rebuilt over the folder.
// Unset, it does nothing, so the digests never change.
//
//   -executeMethod AvatarBridge.Regression.PortalSpanProbe.Run
// checks the measurement on baked tubes of known ring spacing.
#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    [InitializeOnLoad]
    public static class PortalSpanProbe
    {
        const string Tag = "[PortalSpan] ";
        const float Margin = 1.25f, Refuse = 0.2f, Gate = 0.2f;
        // Below one percent: a vertex that far from whole lands visibly between the
        // two bodies if it ever takes the exit.
        const float Partial = 0.99f;
        const string Folder = "Assets/__PortalSpanProbe";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static PortalSpanProbe() => RegressionRunner.Converted += Survey;

        internal sealed class Row
        {
            public int Count, MeshVertices, Active, Triangles, Counted;
            public float Length, MaxZ, Span, Feather;
            public float[] Z;
            public float Ratio => Length > 0 ? Margin * Span / Length : float.NaN;
        }

        internal static Row Measure(Renderer r, int slot, Texture2D bake, int count, float length)
        {
            var filter = r.GetComponent<MeshFilter>();
            var mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : filter != null ? filter.sharedMesh : null;
            if (mesh == null || bake == null || count <= 0) return null;
            var f = Floats(bake);
            var row = new Row { Count = count, MeshVertices = mesh.vertexCount, Length = length };
            var z = new float[count];
            var on = new bool[count];
            row.Z = z;
            for (int v = 0; v < count && 1 + v * 10 + 9 < f.Length; v++)
            {
                int at = 1 + v * 10;
                float a = f[at + 9];
                z[v] = f[at + 2];
                on[v] = a > 0 && z[v] >= 0;   // YapsReadBaked
                if (on[v]) row.Active++;
                if (a > 0.001f) row.MaxZ = Mathf.Max(row.MaxZ, z[v]);   // the bake's own length rule
                if (on[v] && a < Partial) row.Feather = Mathf.Max(row.Feather, z[v]);
            }
            int sub = Mathf.Min(slot, mesh.subMeshCount - 1);
            if (sub < 0 || mesh.GetTopology(sub) != MeshTopology.Triangles) return row;
            var tris = mesh.GetTriangles(sub);
            row.Triangles = tris.Length / 3;
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if (a >= count || b >= count || c >= count || !on[a] || !on[b] || !on[c]) continue;
                row.Counted++;
                float lo = Mathf.Min(z[a], Mathf.Min(z[b], z[c])), hi = Mathf.Max(z[a], Mathf.Max(z[b], z[c]));
                row.Span = Mathf.Max(row.Span, hi - lo);
            }
            return row;
        }

        // The GPU's own bytes: the bake is not readable once settled for upload.
        static float[] Floats(Texture2D bake)
        {
            var request = AsyncGPUReadback.Request(bake, 0);
            request.WaitForCompletion();
            if (request.hasError) throw new InvalidOperationException("the bake did not read back");
            return request.GetData<float>().ToArray();
        }

        // ------------------------------------------------------------ corpus

        static void Survey(string scenePath, GameObject target)
        {
            string dir = Environment.GetEnvironmentVariable("AVATARBRIDGE_PORTALSPAN");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder().Append("scene\t").Append(scenePath).Append('\n');
            var plugs = target != null ? target.GetComponentsInChildren<YapsPlug>(true) : new YapsPlug[0];
            sb.Append("plugs\t").Append(plugs.Length).Append('\n');
            foreach (var plug in plugs)
            {
                var slots = plug.bakedSlots.Where(b => b != null)
                    .Select(b => (r: b.renderer != null ? b.renderer : plug.Target, slot: b.slot)).Distinct().ToList();
                // A plug built by hand records nothing; its renderer's baked slots are its own.
                if (slots.Count == 0 && plug.Target != null)
                    slots = Enumerable.Range(0, plug.Target.sharedMaterials.Length).Select(i => (r: plug.Target, slot: i))
                        .Where(x => YapsMarks.IsBakedMaterial(x.r.sharedMaterials[x.slot])).ToList();
                string plugPath = PathOf(plug.transform, target.transform);
                foreach (var (r, slot) in slots)
                {
                    string where = plugPath + "\t" + (r != null ? PathOf(r.transform, target.transform) : "<none>") + "\t" + slot;
                    var m = r != null && slot >= 0 && slot < r.sharedMaterials.Length ? r.sharedMaterials[slot] : null;
                    if (!YapsMarks.IsBakedMaterial(m) || YapsMarks.IsReadoutMaterial(m))
                    {
                        sb.Append("skip\t").Append(where).Append("\tno bake on the slot\n");
                        continue;
                    }
                    var row = Measure(r, slot, m.GetTexture("_YAPS_Bake") as Texture2D,
                        (int) m.GetFloat("_YAPS_VertexCount"), m.GetFloat("_YAPS_Length"));
                    if (row == null)
                    {
                        sb.Append("skip\t").Append(where).Append("\tno mesh or no vertices\n");
                        continue;
                    }
                    sb.Append("bake\t").Append(where);
                    foreach (var x in new object[] { row.Length, row.MaxZ, row.Count, row.MeshVertices, row.Active,
                                 row.Triangles, row.Counted, row.Span, row.Ratio, row.Feather })
                        sb.Append('\t').Append(Convert.ToString(x, Inv));
                    sb.Append('\n');
                }
            }
            File.WriteAllText(Path.Combine(dir, SafeName(scenePath) + ".txt"), sb.ToString());
            string summary = Summarize(dir, out _, out _, out _);
            File.WriteAllText(Path.Combine(dir, "_summary.txt"), summary);
            Debug.Log(Tag + Path.GetFileNameWithoutExtension(scenePath) + ": " + plugs.Length + " plug(s); "
                      + summary.Split('\n').Last(l => l.Length > 0));
        }

        // Per plug, the largest ratio over its meshes: one refusing mesh refuses the plug.
        internal static string Summarize(string dir, out int plugCount, out int refused, out double median)
        {
            var perPlug = new Dictionary<string, float>();
            var bakes = new List<float>();
            int avatars = 0, withPlugs = 0, skipped = 0, empty = 0, lengthOff = 0, feathered = 0;
            foreach (var file in Directory.GetFiles(dir, "*.txt").Where(p => Path.GetFileName(p) != "_summary.txt").OrderBy(p => p, StringComparer.Ordinal))
            {
                avatars++;
                bool any = false;
                foreach (var line in File.ReadAllLines(file))
                {
                    var c = line.Split('\t');
                    if (c[0] == "skip") skipped++;
                    if (c[0] != "bake") continue;
                    any = true;
                    float P(int i) => float.Parse(c[i], Inv);
                    float length = P(4), maxZ = P(5), ratio = P(12), feather = P(13);
                    if (P(10) == 0) empty++;
                    if (Math.Abs(maxZ - length) > 1e-4f * length) lengthOff++;
                    if (length > 0 && feather / length > 2 * ratio) feathered++;
                    bakes.Add(ratio);
                    string key = Path.GetFileNameWithoutExtension(file) + " | " + c[1];
                    perPlug[key] = perPlug.TryGetValue(key, out var was) ? Math.Max(was, ratio) : ratio;
                }
                if (any) withPlugs++;
            }
            var sb = new StringBuilder();
            sb.Append($"avatars {avatars}, with a baked plug {withPlugs}; bakes {bakes.Count}, plugs {perPlug.Count}, skipped slots {skipped}\n");
            sb.Append($"bakes with no all-active triangle {empty}; bake length off the active max z (a shared frame) {lengthOff}\n");
            sb.Append($"bakes whose partial-weight vertices (active < {Partial}) reach past 2w along the shaft: {feathered}\n\n");
            var twoW = perPlug.Values.Select(r => 2.0 * r).OrderBy(x => x).ToList();
            plugCount = twoW.Count;
            refused = perPlug.Values.Count(r => r > Refuse);
            median = Median(twoW);
            sb.Append("per plug, 2w / L, sorted:\n");
            foreach (var kv in perPlug.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
                sb.Append($"  {2 * kv.Value,8:0.0000}  span {kv.Value,7:0.0000}{(kv.Value > Refuse ? "  REFUSED" : "")}  {kv.Key}\n");
            sb.Append('\n');
            if (twoW.Count > 0)
            {
                double Q(double p) => twoW[(int) Math.Round(p * (twoW.Count - 1))];
                sb.Append($"2w/L over plugs: min {twoW[0]:0.0000}, p10 {Q(0.1):0.0000}, p25 {Q(0.25):0.0000}, median {median:0.0000}, " +
                          $"p75 {Q(0.75):0.0000}, p90 {Q(0.9):0.0000}, max {twoW[twoW.Count - 1]:0.0000}\n");
                var cuts = new[] { 0.05, 0.1, 0.2, 0.4, double.MaxValue };
                double from = 0;
                foreach (var cut in cuts)
                {
                    int n = twoW.Count(x => x >= from && x < cut);
                    sb.Append($"  [{from:0.00}, {(cut == double.MaxValue ? "inf" : cut.ToString("0.00", Inv))}): {n}\n");
                    from = cut;
                }
                sb.Append($"2w/L over bakes: median {Median(bakes.Select(r => 2.0 * r).OrderBy(x => x).ToList()):0.0000} of {bakes.Count}\n");
            }
            sb.Append($"refused at span > {Refuse}: {refused} of {plugCount} plugs, {bakes.Count(r => r > Refuse)} of {bakes.Count} bakes\n");
            sb.Append(plugCount == 0 ? "FAIL gate: no plug measured\n"
                : $"{(median <= Gate ? "PASS" : "FAIL")} gate: median 2w = {median:0.0000} L over {plugCount} plugs, bound {Gate} L\n");
            return sb.ToString();
        }

        static double Median(List<double> s) =>
            s.Count == 0 ? double.NaN : s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;

        static string PathOf(Transform t, Transform root)
        {
            string path = t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        static string SafeName(string scenePath)
        {
            string s = scenePath.StartsWith("Assets/") ? scenePath.Substring(7) : scenePath;
            s = Path.ChangeExtension(s, null).Replace('/', '~');
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        // ------------------------------------------------------------ self-test

        static int failures;

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log(Tag + (ok ? "PASS " : "FAIL ") + what);
        }

        [MenuItem("Tools/AvatarBridge Dev/Probe: Portal span")]
        public static void Run()
        {
            failures = 0;
            string dir = Path.Combine(Path.GetTempPath(), "PortalSpanProbe");
            var made = new List<Object>();
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                const float length = 0.2f;
                // Fine: 0.05 L rings. Coarse: 0.5 L, which the bake must refuse. The body
                // ring sits 0.3 L behind the base on the hips, so a probe that ignored the
                // active mask would read 0.3 L on the fine tube.
                foreach (var (name, rings) in new[] { ("fine", 21), ("coarse", 3) })
                {
                    var skin = Tube(name, rings, 24, length, 0.3f * length, made);
                    var result = YapsBaker.Bake(skin, skin.bones[1], Folder, null, out string failure);
                    Check(result != null, $"{name}: baked ({failure ?? "ok"})");
                    if (result == null) continue;
                    var row = Measure(skin, 0, result.Bake, result.VertexCount, result.Length);
                    float spacing = length / (rings - 1), want = Margin * spacing / row.Length;
                    int wantTris = (rings - 1) * 24 * 2;
                    Debug.Log(Tag + $"{name}: L {row.Length:0.000000} (built {length}), max active z {row.MaxZ:0.000000}, " +
                              $"active {row.Active} of {row.Count}, triangles {row.Counted} of {row.Triangles} all active, " +
                              $"span {row.Span:0.000000} (rings {spacing:0.000000}), ratio {row.Ratio:0.000000} want {want:0.000000}");
                    Check(Math.Abs(row.Length - length) < 1e-4f && Math.Abs(row.MaxZ - row.Length) < 1e-6f,
                        $"{name}: length {row.Length:0.000000} from the bake, built {length}");
                    // The base ring is the bake's origin, so its z lands either side of 0
                    // by rounding and the deform reads those below as inactive. Bounded,
                    // not exact: the whole base ring may go either way.
                    var baseZ = row.Z.Skip(24).Take(24).ToList();
                    Debug.Log(Tag + $"{name}: base ring z from {baseZ.Min():E2} to {baseZ.Max():E2}, {baseZ.Count(v => v < 0)} of 24 below 0");
                    Check(row.Active <= rings * 24 && row.Active >= (rings - 1) * 24
                          && row.Counted <= wantTris && row.Counted >= wantTris - 48,
                        $"{name}: {row.Active} active vertices (want {(rings - 1) * 24} to {rings * 24}), " +
                        $"{row.Counted} all-active triangles (want {wantTris - 48} to {wantTris})");
                    Check(Math.Abs(row.Ratio - want) < 1e-4f,
                        $"{name}: ratio {row.Ratio:0.000000}, want {want:0.000000} (|d| {Math.Abs(row.Ratio - want):0.0000000})");
                    Check((row.Ratio > Refuse) == (name == "coarse"),
                        $"{name}: {(row.Ratio > Refuse ? "refused" : "kept")} at {Refuse}");
                    File.WriteAllText(Path.Combine(dir, name + ".txt"),
                        $"scene\t{name}\nplugs\t1\nbake\tPlug\tBody\t0\t{row.Length.ToString(Inv)}\t{row.MaxZ.ToString(Inv)}\t" +
                        $"{row.Count}\t{row.MeshVertices}\t{row.Active}\t{row.Triangles}\t{row.Counted}\t" +
                        $"{row.Span.ToString(Inv)}\t{row.Ratio.ToString(Inv)}\t{row.Feather.ToString(Inv)}\n");
                }
                // Two plugs, 2w = 0.125 L and 1.25 L: the median is their mean, one refused.
                string summary = Summarize(dir, out int plugs, out int refused, out double median);
                Debug.Log(Tag + "summary of the two tubes:\n" + summary);
                Check(plugs == 2 && refused == 1 && Math.Abs(median - 0.6875) < 1e-4,
                    $"summary: {plugs} plugs, {refused} refused, median 2w {median:0.0000} L (want 2, 1, 0.6875)");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                failures++;
            }
            finally
            {
                foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
                AssetDatabase.DeleteAsset(Folder);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            Debug.Log(Tag + (failures == 0 ? "RESULT PASS" : $"RESULT FAIL ({failures})"));
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        // Rings along +Z from the base, wholly on the plug bone, behind a body ring on the hips.
        static SkinnedMeshRenderer Tube(string name, int rings, int sides, float length, float back, List<Object> made)
        {
            var root = new GameObject("__SpanTube " + name);
            made.Add(root);
            var hips = new GameObject("Hips").transform;
            hips.SetParent(root.transform, false);
            var plug = new GameObject("Plug").transform;
            plug.SetParent(hips, false);
            var body = new GameObject("Body " + name);
            body.transform.SetParent(root.transform, false);

            var verts = new List<Vector3>();
            var weights = new List<BoneWeight>();
            var tris = new List<int>();
            for (int k = -1; k < rings; k++)
            {
                float z = k < 0 ? -back : k * length / (rings - 1);
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2 / sides;
                    verts.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * (0.1f * length) + new Vector3(0, 0, z));
                    weights.Add(new BoneWeight { boneIndex0 = k < 0 ? 0 : 1, weight0 = 1 });
                }
                if (k < 0) continue;
                int lo = (k) * sides, hi = (k + 1) * sides;   // the ring before, then this one
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    tris.AddRange(new[] { lo + i, hi + i, lo + j, lo + j, hi + i, hi + j });
                }
            }
            var mesh = new Mesh { name = "SpanTube " + name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.boneWeights = weights.ToArray();
            var bones = new[] { hips, plug };
            mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * body.transform.localToWorldMatrix).ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            made.Add(mesh);
            var skin = body.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = bones;
            skin.rootBone = hips;
            var mat = new Material(Shader.Find("Standard"));
            made.Add(mat);
            skin.sharedMaterial = mat;
            return skin;
        }
    }
}
#endif
