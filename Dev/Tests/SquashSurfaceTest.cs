#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Collections.Generic;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Known-answer test for a squash part's own surface (docs/Unfinished.md item 25, Step 1).
    //
    // A part the ellipsoid of its reaches fits exactly must keep the ellipsoid: the map can only
    // tie it. A rounded box, which the ellipsoid misses at every corner, must get the map, and
    // the map must read the box's true radius back in every direction on the skin's side, in follow
    // units. The reaches are taken as the build takes them, the 98th percentile along each way.
    //
    // Run: -executeMethod AvatarBridge.Regression.SquashSurfaceTest.Run
    public static class SquashSurfaceTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test: squash part surface")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[SquashSurfaceTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            var toward = Vector3.right;
            var o = Vector3.forward;
            var u = Vector3.Cross(toward, o);
            var dirs = Fibonacci(4000);

            // Toward, away, out, in, across, across back, as the build orders them.
            float[] Reaches(List<Vector3> points) => new[] { toward, -toward, o, -o, u, -u }
                .Select(w => points.Select(x => Vector3.Dot(x, w)).OrderBy(x => x).ElementAt((int) (0.98f * (points.Count - 1)))).ToArray();

            // An octant ellipsoid, the shader's own shape.
            var r = new[] { 0.2f, 0.1f, 0.12f, 0.15f, 0.14f, 0.18f };
            float OnEllipsoid(Vector3 d)
            {
                float Term(float x, float pos, float neg) { float k = x >= 0f ? pos : neg; return x * x / (k * k); }
                return 1f / Mathf.Sqrt(Term(Vector3.Dot(d, toward), r[0], r[1]) + Term(Vector3.Dot(d, o), r[2], r[3]) + Term(Vector3.Dot(d, u), r[4], r[5]));
            }
            // Its exact reaches, so the ellipsoid fits it exactly and the map can only tie. From the
            // 98th percentile the ellipsoid sits 0.6 cm inside and the map rightly wins.
            var ellipsoid = dirs.Select(d => d * OnEllipsoid(d)).ToList();
            var kept = YapsSquash.Surface(ellipsoid, Vector3.zero, toward, o, r, 1f, out float tieMap, out float tieEllipsoid);
            fail += Check($"a part the ellipsoid fits keeps the ellipsoid (map {tieMap * 100f:0.00} cm, ellipsoid {tieEllipsoid * 100f:0.00})", kept == null);

            // A rounded box: |x|^4 + |y|^4 + |z|^4 = R^4.
            const float R = 0.1f;
            float OnBox(Vector3 d) => R / Mathf.Pow(Mathf.Pow(Mathf.Abs(d.x), 4) + Mathf.Pow(Mathf.Abs(d.y), 4) + Mathf.Pow(Mathf.Abs(d.z), 4), 0.25f);
            var box = dirs.Select(d => d * OnBox(d)).ToList();
            var map = YapsSquash.Surface(box, Vector3.zero, toward, o, Reaches(box), 1f, out float boxMap, out float boxEllipsoid);
            fail += Check($"a rounded box gets its own surface (map {boxMap * 100f:0.00} cm, ellipsoid {boxEllipsoid * 100f:0.00})", map != null);
            if (map != null)
            {
                fail += Check("and it is clearly closer", boxMap < 0.3f * boxEllipsoid);
                float worst = 0f;
                foreach (var d in dirs.Where(d => Vector3.Dot(d, o) >= 0f))
                {
                    var q = new Vector3(Vector3.Dot(d, toward), Vector3.Dot(d, u), Vector3.Dot(d, o));
                    worst = Mathf.Max(worst, Mathf.Abs(YapsSquash.SurfaceAt(map, q) / OnBox(d) - 1f));
                }
                fail += Check($"the map reads the box's radius on the skin's side within 4% ({worst * 100f:0.0}% worst)", worst < 0.04f);
                var corner = new Vector3(1f, 1f, 1f).normalized;
                fail += Check($"including a corner ({YapsSquash.SurfaceAt(map, corner):0.0000} for {OnBox(corner):0.0000})",
                    Mathf.Abs(YapsSquash.SurfaceAt(map, corner) / OnBox(corner) - 1f) < 0.03f);
                var halved = YapsSquash.Surface(box, Vector3.zero, toward, o, Reaches(box), 2f, out _, out _);
                fail += Check("in follow units: a follow scaled 2 halves every value",
                    halved != null && halved.Zip(map, (a, b) => Mathf.Abs(a * 2f - b)).Max() < 1e-6f);
            }

            Debug.Log(fail == 0
                ? "[SquashSurfaceTest] PASS: the ellipsoid stays where it fits, the map reads a part's own shape where it does not."
                : $"[SquashSurfaceTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        static List<Vector3> Fibonacci(int n)
        {
            var result = new List<Vector3>(n);
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < n; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / n, ring = Mathf.Sqrt(1f - y * y), th = golden * i;
                result.Add(new Vector3(Mathf.Cos(th) * ring, y, Mathf.Sin(th) * ring));
            }
            return result;
        }
    }
}
#endif
