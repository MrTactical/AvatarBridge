// The per-vertex frame, recovered on the CPU exactly as the deform does it:
// each vertex's baked position, normal and tangent plus every baked shape
// at the material's weight, against the same vertex skinned by Unity with
// the renderer's own blendshape weights. A plug that holds together
// recovers one root for every vertex; a vertex whose root lands elsewhere
// is bent round the wrong place, which is a tear.
//
// Run: -executeMethod AvatarBridge.Regression.HeldShapeFrameProbe.Run -yapsAvatar <prefab path>
// Twice: as shipped, then with every shape released on both sides. With
// -rebake, baked again by this build and measured a third time.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class HeldShapeFrameProbe
    {
        public static void Run()
        {
            try
            {
                string path = Arg("-yapsAvatar");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) throw new Exception("no prefab at " + path);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var go = (GameObject) PrefabUtility.InstantiatePrefab(prefab);
                foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var m = r.sharedMaterials.FirstOrDefault(x => x != null && x.HasProperty("_YAPS_Bake")
                        && x.GetTexture("_YAPS_Bake") is Texture2D && x.GetFloat("_YAPS_VertexCount") > 0);
                    if (m == null) continue;
                    Log($"renderer {r.name}, material {m.name}, {r.sharedMesh.blendShapeCount} shape(s) on the mesh");
                    Measure(r, m, "as shipped", false);
                    Measure(r, m, "every shape released", true);
                    Compare(r, m);
                    if (Arg("-rebake") == null) continue;

                    // Baked again by this build, into the materials the
                    // prefab ships, so the bend tester reads the new bake.
                    var plug = go.GetComponentsInChildren<AvatarBridge.Yaps.YapsPlug>(true).First();
                    string dir = System.IO.Path.GetDirectoryName(path).Replace("\\", "/") + "/YAPS";
                    var result = YapsBaker.Bake(r, plug.transform, dir, null, out string failure);
                    if (result == null) throw new Exception("rebake failed: " + failure);
                    foreach (var mat in r.sharedMaterials.Where(x => x != null && x.HasProperty("_YAPS_Bake")))
                    {
                        YapsBaker.Apply(result, mat, true);
                        EditorUtility.SetDirty(mat);
                    }
                    AssetDatabase.SaveAssets();
                    Log($"rebaked: {result.VertexCount} vertices, {result.Shapes.Count} shape(s), texture {AssetDatabase.GetAssetPath(result.Bake)}");
                    Measure(r, m, "rebaked", false);
                    Compare(r, m);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static void Measure(SkinnedMeshRenderer r, Material m, string label, bool release)
        {
            var mesh = r.sharedMesh;
            var held = new float[mesh.blendShapeCount];
            for (int s = 0; s < held.Length; s++)
            {
                held[s] = r.GetBlendShapeWeight(s);
                if (release) r.SetBlendShapeWeight(s, 0f);
            }

            var tex = (Texture2D) m.GetTexture("_YAPS_Bake");
            var px = Pixels(tex);
            int width = tex.width;
            float F(int i)
            {
                var c = px[i];
                return BitConverter.ToSingle(new[] { c.r, c.g, c.b, c.a }, 0);
            }
            Vector3 F3(int i) => new Vector3(F(i), F(i + 1), F(i + 2));

            int total = (int) m.GetFloat("_YAPS_VertexCount");
            int shapes = Mathf.Max((int) m.GetFloat("_YAPS_ShapeCount"), 0);
            var packs = new[] { "_YAPS_ShapeWeights", "_YAPS_ShapeWeights2", "_YAPS_ShapeWeights3", "_YAPS_ShapeWeights4" }
                .Select(n => m.HasProperty(n) ? m.GetVector(n) : Vector4.zero).ToArray();
            float W(int s) => release ? 0f : packs[s / 4][s % 4];
            float girth = Mathf.Max(m.GetFloat("_YAPS_BakeGirth"), 0.0001f), scale = Mathf.Max(m.GetFloat("_YAPS_BakeScale"), 0.0001f);
            Log($"  {label}: header {F(0)}, vertex 0 at {F3(1)}, texture {tex.width}x{tex.height} {tex.format}, " +
                $"girth {girth}, scale {scale}, renderer lossy scale {r.transform.lossyScale}");
            Log($"  {label}: {total} baked vertices, {shapes} baked shape(s), weights " +
                string.Join(" ", Enumerable.Range(0, shapes).Select(s => W(s).ToString("0.##"))) +
                $"; renderer holds " + string.Join(", ", Enumerable.Range(0, held.Length)
                    .Where(s => r.GetBlendShapeWeight(s) != 0).Select(s => $"{mesh.GetBlendShapeName(s)} {r.GetBlendShapeWeight(s)}")));

            var skinned = new Mesh();
            r.BakeMesh(skinned, false);
            var sp = skinned.vertices;
            var sn = skinned.normals;
            var st = skinned.tangents;

            // Which mesh shapes move each vertex, for grouping.
            var movedBy = new List<string>[mesh.vertexCount];
            var dv = new Vector3[mesh.vertexCount];
            for (int s = 0; s < mesh.blendShapeCount; s++)
            {
                if (held[s] == 0) continue;
                mesh.GetBlendShapeFrameVertices(s, mesh.GetBlendShapeFrameCount(s) - 1, dv, new Vector3[mesh.vertexCount], new Vector3[mesh.vertexCount]);
                for (int v = 0; v < dv.Length; v++)
                    if (dv[v].sqrMagnitude > 1e-12f) (movedBy[v] ??= new List<string>()).Add(mesh.GetBlendShapeName(s));
            }

            var roots = new Vector3[total];
            var ok = new bool[total];
            for (int v = 0; v < total && v < sp.Length; v++)
            {
                int at = 1 + v * 10;
                var bp = F3(at);
                var bn = F3(at + 3);
                var bt = F3(at + 6);
                float active = F(at + 9);
                int block = 1 + total * 10;
                for (int s = 0; s < shapes; s++)
                {
                    float w = W(s);
                    if (Mathf.Abs(w) <= 0.0001f) continue;
                    int sa = block + s * total * 9 + v * 9;
                    bp += F3(sa) * w;
                    bn += F3(sa + 3) * w;
                    bt += F3(sa + 6) * w;
                }
                if (active <= 0 || bp.z < 0) continue;
                bp = Vector3.Scale(bp, new Vector3(girth, girth, scale));
                // BakeMesh without scale: unscaled, relative to the renderer.
                var p = r.transform.position + r.transform.rotation * sp[v];
                var n = r.transform.rotation * sn[v];
                var t = r.transform.rotation * (Vector3) st[v];
                if (!Basis(bn, bt, n, t, out var a1, out var a2, out var a3, out var b1, out var b2, out var b3)) continue;
                var c = new Vector3(Vector3.Dot(a1, bp), Vector3.Dot(a2, bp), Vector3.Dot(a3, bp));
                roots[v] = p - (b1 * c.x + b2 * c.y + b3 * c.z);
                ok[v] = true;
            }

            // The root the untouched vertices agree on.
            var clean = Enumerable.Range(0, total).Where(v => ok[v] && movedBy[v] == null).ToList();
            var reference = new Vector3(Median(clean.Select(v => roots[v].x)), Median(clean.Select(v => roots[v].y)), Median(clean.Select(v => roots[v].z)));
            float Dev(int v) => (roots[v] - reference).magnitude * 1000f;
            var cleanDev = clean.Select(Dev).OrderBy(d => d).ToList();
            Log($"  {label}: untouched vertices {clean.Count}, root spread median {Pct(cleanDev, 0.5f):0.00} mm, 99% {Pct(cleanDev, 0.99f):0.00} mm, max {cleanDev.LastOrDefault():0.00} mm");
            var groups = Enumerable.Range(0, total).Where(v => ok[v] && movedBy[v] != null)
                .GroupBy(v => string.Join("+", movedBy[v]));
            foreach (var g in groups.OrderByDescending(g => g.Count()))
            {
                var d = g.Select(Dev).OrderBy(x => x).ToList();
                Log($"    moved by {g.Key}: {d.Count} vertices, root off median {Pct(d, 0.5f):0.0} mm, 90% {Pct(d, 0.9f):0.0} mm, max {d.Last():0.0} mm, past 5 mm {d.Count(x => x > 5f)}");
            }
            int invalid = Enumerable.Range(0, Mathf.Min(total, sp.Length)).Count(v => !ok[v]);
            Log($"  {label}: {invalid} vertices skipped (inactive, behind the base, or no basis)");

            for (int s = 0; s < held.Length; s++) r.SetBlendShapeWeight(s, held[s]);
        }

        // Where a held shape disagrees: its position delta, and the normal and
        // tangent it leaves, each set against Unity's own skinning. Both sides
        // are turned into the world by the frame the released state recovers,
        // which holds for every vertex.
        static void Compare(SkinnedMeshRenderer r, Material m)
        {
            var mesh = r.sharedMesh;
            var held = Enumerable.Range(0, mesh.blendShapeCount).Select(r.GetBlendShapeWeight).ToArray();
            var px = Pixels((Texture2D) m.GetTexture("_YAPS_Bake"));
            float F(int i) { var c = px[i]; return BitConverter.ToSingle(new[] { c.r, c.g, c.b, c.a }, 0); }
            Vector3 F3(int i) => new Vector3(F(i), F(i + 1), F(i + 2));
            int total = (int) m.GetFloat("_YAPS_VertexCount");
            int shapes = Mathf.Max((int) m.GetFloat("_YAPS_ShapeCount"), 0);
            var packs = new[] { "_YAPS_ShapeWeights", "_YAPS_ShapeWeights2", "_YAPS_ShapeWeights3", "_YAPS_ShapeWeights4" }
                .Select(n => m.GetVector(n)).ToArray();
            float W(int k) => packs[k / 4][k % 4];

            Vector3[] P, N, T;
            void Skin(out Vector3[] p, out Vector3[] n, out Vector3[] t)
            {
                var baked = new Mesh();
                r.BakeMesh(baked, false);
                var q = r.transform.rotation;
                p = baked.vertices.Select(v => r.transform.position + q * v).ToArray();
                n = baked.normals.Select(v => q * v).ToArray();
                t = baked.tangents.Select(v => q * (Vector3) v).ToArray();
            }
            Skin(out var pHeld, out var nHeld, out var tHeld);
            for (int s = 0; s < held.Length; s++) r.SetBlendShapeWeight(s, 0f);
            Skin(out P, out N, out T);
            for (int s = 0; s < held.Length; s++) r.SetBlendShapeWeight(s, held[s]);

            var moved = new List<string>[mesh.vertexCount];
            var dv = new Vector3[mesh.vertexCount];
            for (int s = 0; s < mesh.blendShapeCount; s++)
            {
                if (held[s] == 0) continue;
                mesh.GetBlendShapeFrameVertices(s, 0, dv, new Vector3[mesh.vertexCount], new Vector3[mesh.vertexCount]);
                for (int v = 0; v < dv.Length; v++)
                    if (dv[v].sqrMagnitude > 1e-12f) (moved[v] ??= new List<string>()).Add(mesh.GetBlendShapeName(s));
            }

            var rows = new Dictionary<string, List<(float ratio, float cos, float nAngle, float tAngle, float uLen, float bLen)>>();
            var turns = new Dictionary<string, List<(float unityTurn, float bakeTurn, float meshDn, float bakeDn)>>();
            var dnMesh = new Vector3[mesh.vertexCount];
            var dnSum = new Vector3[mesh.vertexCount];
            for (int s = 0; s < mesh.blendShapeCount; s++)
            {
                if (held[s] == 0) continue;
                mesh.GetBlendShapeFrameVertices(s, 0, new Vector3[mesh.vertexCount], dnMesh, new Vector3[mesh.vertexCount]);
                for (int v = 0; v < dnMesh.Length; v++) dnSum[v] += dnMesh[v] * held[s] * 0.01f;
            }
            var meshN = mesh.normals;
            int block = 1 + total * 10;
            for (int v = 0; v < total && v < P.Length; v++)
            {
                if (moved[v] == null) continue;
                int at = 1 + v * 10;
                var bn = F3(at + 3);
                var bt = F3(at + 6);
                if (!Basis(bn, bt, N[v], T[v], out var a1, out var a2, out var a3, out var b1, out var b2, out var b3)) continue;
                Vector3 Turn(Vector3 x) { var c = new Vector3(Vector3.Dot(a1, x), Vector3.Dot(a2, x), Vector3.Dot(a3, x)); return b1 * c.x + b2 * c.y + b3 * c.z; }
                Vector3 dp = Vector3.zero, dn = Vector3.zero, dt = Vector3.zero;
                for (int k = 0; k < shapes; k++)
                {
                    float w = W(k);
                    if (Mathf.Abs(w) <= 0.0001f) continue;
                    int sa = block + k * total * 9 + v * 9;
                    dp += F3(sa) * w; dn += F3(sa + 3) * w; dt += F3(sa + 6) * w;
                }
                var bakeDelta = Turn(dp);
                var unityDelta = pHeld[v] - P[v];
                var bakeN = Turn((bn + dn).normalized);
                var bakeT = Turn((bt + dt).normalized);
                string key = string.Join("+", moved[v]);
                if (!rows.TryGetValue(key, out var list)) rows[key] = list = new List<(float, float, float, float, float, float)>();
                if (!turns.TryGetValue(key, out var tl)) turns[key] = tl = new List<(float, float, float, float)>();
                tl.Add((Vector3.Angle(nHeld[v], N[v]), Vector3.Angle(bakeN, Turn(bn.normalized)), dnSum[v].magnitude / Mathf.Max(meshN[v].magnitude, 1e-6f), dn.magnitude / Mathf.Max(bn.magnitude, 1e-6f)));
                list.Add((unityDelta.magnitude > 1e-7f ? bakeDelta.magnitude / unityDelta.magnitude : 0f,
                    unityDelta.magnitude > 1e-7f && bakeDelta.magnitude > 1e-7f ? Vector3.Dot(bakeDelta.normalized, unityDelta.normalized) : 0f,
                    Vector3.Angle(bakeN, nHeld[v]), Vector3.Angle(bakeT, tHeld[v]), unityDelta.magnitude * 1000f, bakeDelta.magnitude * 1000f));
            }
            foreach (var kv in rows.OrderByDescending(k => k.Value.Count))
            {
                var l = kv.Value;
                float Med(Func<(float ratio, float cos, float nAngle, float tAngle, float uLen, float bLen), float> f) => Median(l.Select(f));
                Log($"  compare {kv.Key}: {l.Count} vertices; delta unity {Med(x => x.uLen):0.0} mm vs bake {Med(x => x.bLen):0.0} mm, " +
                    $"ratio {Med(x => x.ratio):0.000}, direction cos {Med(x => x.cos):0.000}; normal off {Med(x => x.nAngle):0.0} deg, tangent off {Med(x => x.tAngle):0.0} deg " +
                    $"(90%: {Pct(l.Select(x => x.nAngle).OrderBy(x => x).ToList(), 0.9f):0.0} / {Pct(l.Select(x => x.tAngle).OrderBy(x => x).ToList(), 0.9f):0.0})");
                var tl = turns[kv.Key];
                Log($"    normal turned by unity {Median(tl.Select(x => x.unityTurn)):0.0} deg, by the bake {Median(tl.Select(x => x.bakeTurn)):0.0} deg; " +
                    $"delta against its normal: mesh {Median(tl.Select(x => x.meshDn)):0.000}, bake {Median(tl.Select(x => x.bakeDn)):0.000}");
            }
        }

        // A bake saved unreadable comes back through the GPU, one texel each.
        static Color32[] Pixels(Texture2D tex)
        {
            if (tex.isReadable) return tex.GetPixels32();
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(tex, rt);
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, true);
            RenderTexture.active = rt;
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            var px = copy.GetPixels32();
            UnityEngine.Object.DestroyImmediate(copy);
            return px;
        }

        static bool Basis(Vector3 bn, Vector3 bt, Vector3 sn, Vector3 st,
            out Vector3 a1, out Vector3 a2, out Vector3 a3, out Vector3 b1, out Vector3 b2, out Vector3 b3)
        {
            a1 = a2 = a3 = b1 = b2 = b3 = Vector3.zero;
            if (bn.sqrMagnitude < 1e-12f || bt.sqrMagnitude < 1e-12f || sn.sqrMagnitude < 1e-12f || st.sqrMagnitude < 1e-12f) return false;
            a1 = bn.normalized;
            var aFlat = bt - a1 * Vector3.Dot(bt, a1);
            b1 = sn.normalized;
            var bFlat = st - b1 * Vector3.Dot(st, b1);
            if (aFlat.sqrMagnitude < 1e-8f || bFlat.sqrMagnitude < 1e-8f) return false;
            a2 = aFlat.normalized;
            a3 = Vector3.Cross(a1, a2);
            b2 = bFlat.normalized;
            b3 = Vector3.Cross(b1, b2);
            return true;
        }

        static float Median(IEnumerable<float> xs)
        {
            var l = xs.OrderBy(x => x).ToList();
            return l.Count == 0 ? 0f : l[l.Count / 2];
        }

        static float Pct(List<float> sorted, float p) =>
            sorted.Count == 0 ? 0f : sorted[Mathf.Clamp((int) (p * (sorted.Count - 1)), 0, sorted.Count - 1)];

        static void Log(string s) => Debug.Log("[HeldShapeFrame] " + s);

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
#endif
