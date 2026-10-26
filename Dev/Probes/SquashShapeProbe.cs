#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS && UNITY_EDITOR
// Gate 1 of the mesh-to-mesh squash (docs/Unfinished.md item 25): is a map of each part's real
// skin clearly closer to it than the ellipsoid the shader draws today?
//
// -executeMethod AvatarBridge.Regression.SquashShapeProbe.Run -ssPrefabs "<a.prefab>;<b.prefab>" [-ssOut <dir>]
//
// Each converted prefab is unpacked into an empty scene and measured by the squash build itself,
// which stops after measuring. Per part, every vertex's radial distance (along the line from the
// part's centre) to the ellipsoid of its six reaches and to a radial map: an octahedral grid of
// directions in the part's frame, the outermost skin per cell, empty cells filled from their
// neighbours, one texel of smoothing, read bilinearly. The map is built from every other vertex and
// judged on the rest, so it is not graded on what it was fitted to. Mean, 95th and max in cm, for
// the whole part and for its underside, its top and its side toward the partner. With -ssOut, a PNG
// per part of two sections through its centre: skin grey, ellipsoid red, map green.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class SquashShapeProbe
    {
        const string Tag = "[SquashShape] ";
        const string Scratch = "Assets/__SquashShapeProbe";

        sealed class Part
        {
            public string name;
            public Vector3 centre, a, o, u;
            public float[] reach;
            public List<Vector3> points;
        }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string k) { int i = Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            string outDir = Arg("-ssOut");
            if (outDir != null) Directory.CreateDirectory(outDir);
            int code = 0;
            try
            {
                foreach (string path in (Arg("-ssPrefabs") ?? "").Split(';').Where(p => p.Length > 0))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) { Debug.Log(Tag + "no prefab at " + path); code = 1; continue; }
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    var go = (GameObject) PrefabUtility.InstantiatePrefab(prefab);
                    PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    var parts = new List<Part>();
                    YapsSquash.MeasuredForTest = (name, centre, toward, o, points, reach) => parts.Add(new Part
                    {
                        name = name, centre = centre, a = toward, o = o, u = Vector3.Cross(toward, o),
                        reach = reach, points = points,
                    });
                    string said = YapsSquash.Add(go.transform, Scratch, out _);
                    YapsSquash.MeasuredForTest = null;
                    Debug.Log(Tag + $"{prefab.name}: {parts.Count} parts; {said}");
                    foreach (var p in parts) Judge(prefab.name, p, outDir);
                }
            }
            catch (Exception e) { Debug.LogException(e); code = 2; }
            finally
            {
                YapsSquash.MeasuredForTest = null;
                if (AssetDatabase.IsValidFolder(Scratch)) AssetDatabase.DeleteAsset(Scratch);
            }
            Debug.Log(Tag + (code == 0 ? "done" : "FAILED"));
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static void Judge(string avatar, Part p, string outDir)
        {
            var fit = p.points.Where((_, i) => i % 2 == 0).ToList();
            var test = p.points.Where((_, i) => i % 2 == 1).ToList();
            var map16 = Map(p, fit, 16);
            var map32 = Map(p, fit, 32);
            string Row(string label, Func<Vector3, bool> keep)
            {
                var dirs = test.Select(x => x - p.centre).Where(x => x.sqrMagnitude > 1e-12f && keep(x.normalized)).ToList();
                if (dirs.Count < 10) return $"{label} -";
                string Stat(Func<Vector3, float> surface)
                {
                    var e = dirs.Select(x => (x.magnitude - surface(x.normalized)) * 100f).ToList();
                    var abs = e.Select(Mathf.Abs).OrderBy(x => x).ToList();
                    return $"{abs.Average():0.00}/{abs[(int) (0.95f * (abs.Count - 1))]:0.00}/{abs[abs.Count - 1]:0.00} (signed {e.Average():+0.00;-0.00})";
                }
                return $"{label} n {dirs.Count}: ellipsoid {Stat(d => Extent(p, d))}, map16 {Stat(d => Read(p, map16, 16, d))}, map32 {Stat(d => Read(p, map32, 32, d))}";
            }
            Debug.Log(Tag + $"{avatar} {p.name}: reaches toward {p.reach[0] * 100f:0.0} away {p.reach[1] * 100f:0.0} out {p.reach[2] * 100f:0.0} " +
                      $"in {p.reach[3] * 100f:0.0} across {p.reach[4] * 100f:0.0}/{p.reach[5] * 100f:0.0} cm; mean/95th/max cm");
            Debug.Log(Tag + "  " + Row("all", _ => true));
            Debug.Log(Tag + "  " + Row("underside", d => d.y < -0.5f));
            Debug.Log(Tag + "  " + Row("top", d => d.y > 0.5f));
            Debug.Log(Tag + "  " + Row("toward partner", d => Vector3.Dot(d, p.a) > 0.5f));
            if (outDir != null) Picture(Path.Combine(outDir, string.Concat($"{avatar}-{p.name}".Where(c => char.IsLetterOrDigit(c) || c == '-')) + ".png"), p, map32);
        }

        // The shader's YapsExtent: an ellipsoid per octant of the frame, front reaches where a
        // component is positive.
        static float Extent(Part p, Vector3 d)
        {
            var c = new Vector3(Vector3.Dot(d, p.a), Vector3.Dot(d, p.o), Vector3.Dot(d, p.u));
            float Term(float x, float pos, float neg) { float r = x >= 0f ? pos : neg; return x * x / (r * r); }
            return 1f / Mathf.Sqrt(Mathf.Max(Term(c.x, p.reach[0], p.reach[1]) + Term(c.y, p.reach[2], p.reach[3]) + Term(c.z, p.reach[4], p.reach[5]), 1e-8f));
        }

        // Octahedral, folded on the side into the body, where a part has no skin of its own, so the
        // skin's side is one continuous diamond.
        static Vector2 Oct(Part p, Vector3 d)
        {
            var q = new Vector3(Vector3.Dot(d, p.a), Vector3.Dot(d, p.u), Vector3.Dot(d, p.o));
            q /= Mathf.Abs(q.x) + Mathf.Abs(q.y) + Mathf.Abs(q.z);
            var e = new Vector2(q.x, q.y);
            if (q.z < 0f) e = new Vector2((1f - Mathf.Abs(q.y)) * Mathf.Sign(q.x), (1f - Mathf.Abs(q.x)) * Mathf.Sign(q.y));
            return e * 0.5f + Vector2.one * 0.5f;
        }

        static float[,] Map(Part p, List<Vector3> points, int n)
        {
            var map = new float[n, n];
            var set = new bool[n, n];
            foreach (var x in points)
            {
                var v = x - p.centre;
                if (v.sqrMagnitude < 1e-12f) continue;
                var uv = Oct(p, v.normalized);
                int i = Mathf.Clamp((int) (uv.x * n), 0, n - 1), j = Mathf.Clamp((int) (uv.y * n), 0, n - 1);
                map[i, j] = Mathf.Max(map[i, j], v.magnitude);
                set[i, j] = true;
            }
            // Empty cells from their filled neighbours, ring by ring.
            for (int pass = 0; pass < 4 * n; pass++)
            {
                var next = (float[,]) map.Clone();
                var nextSet = (bool[,]) set.Clone();
                bool any = false;
                for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    if (set[i, j]) continue;
                    any = true;
                    float sum = 0f; int count = 0;
                    for (int di = -1; di <= 1; di++)
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        int a = i + di, b = j + dj;
                        if (a < 0 || b < 0 || a >= n || b >= n || !set[a, b]) continue;
                        sum += map[a, b]; count++;
                    }
                    if (count > 0) { next[i, j] = sum / count; nextSet[i, j] = true; }
                }
                map = next; set = nextSet;
                if (!any) break;
            }
            // One texel of smoothing.
            var smooth = new float[n, n];
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                float sum = 4f * map[i, j]; float weight = 4f;
                for (int di = -1; di <= 1; di++)
                for (int dj = -1; dj <= 1; dj++)
                {
                    int a = i + di, b = j + dj;
                    if ((di == 0 && dj == 0) || a < 0 || b < 0 || a >= n || b >= n) continue;
                    sum += map[a, b]; weight += 1f;
                }
                smooth[i, j] = sum / weight;
            }
            return smooth;
        }

        static float Read(Part p, float[,] map, int n, Vector3 d)
        {
            var f = Oct(p, d) * n - Vector2.one * 0.5f;
            int i0 = Mathf.Clamp(Mathf.FloorToInt(f.x), 0, n - 1), j0 = Mathf.Clamp(Mathf.FloorToInt(f.y), 0, n - 1);
            int i1 = Mathf.Min(i0 + 1, n - 1), j1 = Mathf.Min(j0 + 1, n - 1);
            float s = Mathf.Clamp01(f.x - i0), t = Mathf.Clamp01(f.y - j0);
            return Mathf.Lerp(Mathf.Lerp(map[i0, j0], map[i1, j0], s), Mathf.Lerp(map[i0, j1], map[i1, j1], s), t);
        }

        // Two sections through the centre: toward-and-out (seen along across) and across-and-out
        // (seen along toward).
        static void Picture(string file, Part p, float[,] map)
        {
            const int S = 400;
            var tex = new Texture2D(2 * S, S, TextureFormat.RGB24, false);
            tex.SetPixels32(Enumerable.Repeat(new Color32(16, 16, 16, 255), 2 * S * S).ToArray());
            float span = p.reach.Max() * 1.4f;
            var planes = new[] { (x: p.a, y: p.o, n: p.u), (x: p.u, y: p.o, n: p.a) };
            for (int k = 0; k < 2; k++)
            {
                var (ex, ey, en) = planes[k];
                void Dot(Vector3 v, Color32 c, int r)
                {
                    int px = k * S + S / 2 + (int) (Vector3.Dot(v, ex) / span * S / 2), py = S / 2 + (int) (Vector3.Dot(v, ey) / span * S / 2);
                    for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                        if (px + dx >= k * S && px + dx < (k + 1) * S && py + dy >= 0 && py + dy < S) tex.SetPixel(px + dx, py + dy, c);
                }
                float slab = 0.08f * p.reach.Max();
                foreach (var x in p.points)
                {
                    var v = x - p.centre;
                    if (Mathf.Abs(Vector3.Dot(v, en)) < slab) Dot(v, new Color32(150, 150, 150, 255), 0);
                }
                for (int step = 0; step < 720; step++)
                {
                    float th = step * Mathf.PI / 360f;
                    var d = Mathf.Cos(th) * ex + Mathf.Sin(th) * ey;
                    Dot(d * Extent(p, d), new Color32(230, 60, 60, 255), 1);
                    Dot(d * Read(p, map, 32, d), new Color32(60, 220, 90, 255), 1);
                }
            }
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
#endif
