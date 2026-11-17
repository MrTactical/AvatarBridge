#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AvatarBridge.Regression
{
    // The soft bake's presses run side by side, each on a tissue of its own. They must come out
    // bit for bit as one press at a time on the shared tissue did. A pair of spheres on a chest.
    public static class SoftBakeTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Soft bake presses side by side")]
        public static void Run()
        {
            int failures = 0;
            var at = new List<Vector3>();
            var tris = new List<int>();
            var share = new List<Vector2>();
            Sphere(at, tris, share, Vector3.zero, 0.14f, Vector2.zero);
            Vector3 c0 = new Vector3(-0.06f, 0f, 0.19f), c1 = new Vector3(0.06f, 0f, 0.19f);
            Sphere(at, tris, share, c0, 0.06f, new Vector2(1f, 0f));
            Sphere(at, tris, share, c1, 0.06f, new Vector2(0f, 1f));

            (Vector3[][] moves, Vector3[][] turns, string log) Bake(bool oneAtATime)
            {
                YapsSoftBody.OnePressAtATimeForTest = oneAtATime;
                try
                {
                    return YapsSoftBody.Bake(at.ToArray(), tris.ToArray(), new List<int[]>(), share.ToArray(), c0, c1, 0.06f,
                        Vector3.up, Vector3.forward, Vector3.down, (j, dir) => 0.06f);
                }
                finally { YapsSoftBody.OnePressAtATimeForTest = false; }
            }

            var clock = Stopwatch.StartNew();
            var before = Bake(true);
            double was = clock.Elapsed.TotalSeconds;
            clock.Restart();
            var now = Bake(false);
            double side = clock.Elapsed.TotalSeconds;
            Debug.Log($"[SoftBakeTest]   one at a time {was:0.0} s, side by side {side:0.0} s; {now.log}");
            bool baked = before.moves != null && now.moves != null;
            if (!baked) failures++;
            Debug.Log($"[SoftBakeTest] {(baked ? "ok   " : "WRONG")} the pair bakes");
            if (baked)
            {
                int differ = 0, compared = 0;
                for (int e = 0; e < before.moves.Length; e++)
                {
                    if (before.moves[e] == null && now.moves[e] == null) continue;
                    compared++;
                    if (before.moves[e] == null || now.moves[e] == null
                        || !before.moves[e].SequenceEqual(now.moves[e]) || !before.turns[e].SequenceEqual(now.turns[e])) differ++;
                }
                bool same = compared > 0 && differ == 0;
                if (!same) failures++;
                Debug.Log($"[SoftBakeTest] {(same ? "ok   " : "WRONG")} side by side matches one at a time bit for bit ({compared - differ} of {compared} answers)");
            }
            Debug.Log(failures == 0 ? "[SoftBakeTest] PASS" : $"[SoftBakeTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Sphere(List<Vector3> at, List<int> tris, List<Vector2> share, Vector3 centre, float r, Vector2 part)
        {
            const int rings = 24, segs = 32;
            int first = at.Count;
            for (int i = 0; i <= rings; i++)
            {
                float v = Mathf.PI * i / rings;
                for (int j = 0; j < segs; j++)
                {
                    float u = 2f * Mathf.PI * j / segs;
                    at.Add(centre + r * new Vector3(Mathf.Sin(v) * Mathf.Cos(u), Mathf.Cos(v), Mathf.Sin(v) * Mathf.Sin(u)));
                    share.Add(part);
                }
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < segs; j++)
                {
                    int a = first + i * segs + j, b = first + i * segs + (j + 1) % segs, c = a + segs, d = b + segs;
                    // Clockwise seen from outside, Unity's front face.
                    tris.AddRange(new[] { a, c, b, b, c, d });
                }
        }
    }
}
#endif
