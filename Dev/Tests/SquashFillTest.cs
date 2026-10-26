#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Collections.Generic;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Known-answer test for what the squash gives back where a pair meets (docs/Unfinished.md item
    // 25, mass deleted on a squeeze).
    //
    // A ball, its partner an equal ball along the line. Grown whole out of a plane, a closed part
    // gains the same volume wherever the plane lies, so the tables must not move with it. What the
    // seam takes must match a separate count of the same seam on the same ball (numpy, summing the
    // whole mesh at once), shallow below 0 where the seam's swell adds more than it cuts, and
    // bowed round the partner's front it must take less than flat once the press is deep.
    //
    // Run: -executeMethod AvatarBridge.Regression.SquashFillTest.Run
    public static class SquashFillTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test: squash give-back")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[SquashFillTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            const float R = 0.1f;
            var (at, tris) = Ball(R, 48, 96);
            var share = new float[at.Length];
            for (int v = 0; v < share.Length; v++) share[v] = 1f;
            var fill = YapsSquash.Fill(at, share, tris, Vector3.zero, Vector3.right, Vector3.forward, R, Vector3.forward, R, w => R, w => R, out _);
            var higher = YapsSquash.Fill(at, share, tris, Vector3.zero, Vector3.right, Vector3.forward, R, Vector3.forward, 2f * R, w => R, w => R, out _);
            Debug.Log($"[SquashFillTest] flat {string.Join(" ", fill.Take(32).Select(x => (x * 100f).ToString("0.00")))}");
            Debug.Log($"[SquashFillTest] round {string.Join(" ", fill.Skip(32).Select(x => (x * 100f).ToString("0.00")))}");

            float moved = fill.Zip(higher, (x, y) => Mathf.Abs(x - y)).Max();
            fail += Check($"grown from a plane twice as far, a ball moved whole takes the same ({moved * 100f:0.000}% at most apart)", moved < 0.002f);
            // From the separate count: flat and round at a quarter, half and most of the reach in.
            var known = new[] { (12, -2.65f), (20, 9.01f), (31, 46.24f), (44, -1.55f), (52, 2.01f), (63, 27.41f) };
            foreach (var (k, percent) in known)
                fail += Check($"{(k < 32 ? "flat" : "round")} at {k % 32 - 4}/32 of the reach in, {fill[k] * 100f:0.00}% for the count's {percent:0.00}%",
                    Mathf.Abs(fill[k] * 100f - percent) < 0.1f + 0.01f * Mathf.Abs(percent));
            bool less = true;
            for (int k = 20; k < 32; k++) less &= fill[32 + k] < fill[k];
            fail += Check("round the partner it takes less than flat from half the reach in", less);

            Debug.Log(fail == 0
                ? "[SquashFillTest] PASS: the give-back follows what the seam takes off a ball."
                : $"[SquashFillTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        // A closed ball, one vertex a pole, wound clockwise seen from outside as Unity's front faces.
        static (Vector3[], int[]) Ball(float r, int rings, int around)
        {
            var at = new List<Vector3> { Vector3.up * r };
            for (int i = 1; i < rings; i++)
            {
                float th = Mathf.PI * i / rings;
                for (int j = 0; j < around; j++)
                {
                    float ph = 2f * Mathf.PI * j / around;
                    at.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)) * r);
                }
            }
            at.Add(Vector3.down * r);
            int last = at.Count - 1;
            int Ring(int i, int j) => 1 + (i - 1) * around + (j % around);
            var tris = new List<int>();
            for (int j = 0; j < around; j++) tris.AddRange(new[] { 0, Ring(1, j + 1), Ring(1, j) });
            for (int i = 1; i < rings - 1; i++)
                for (int j = 0; j < around; j++)
                {
                    tris.AddRange(new[] { Ring(i, j), Ring(i, j + 1), Ring(i + 1, j) });
                    tris.AddRange(new[] { Ring(i, j + 1), Ring(i + 1, j + 1), Ring(i + 1, j) });
                }
            for (int j = 0; j < around; j++) tris.AddRange(new[] { last, Ring(rings - 1, j), Ring(rings - 1, j + 1) });
            // Built counter-clockwise, then each face turned over.
            for (int t = 0; t < tris.Count; t += 3) (tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);
            return (at.ToArray(), tris.ToArray());
        }
    }
}
#endif
