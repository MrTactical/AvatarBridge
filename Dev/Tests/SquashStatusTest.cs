#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A tester could not tell whether the squash was built, or whether a stroke since had
    // left the build behind. The status line every squash window shows must say both.
    public static class SquashStatusTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Squash status")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[SquashStatusTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            var root = new GameObject("SquashStatusTest");
            try
            {
                var s = root.AddComponent<YapsSquashSettings>();
                string said = YapsSquash.Status(root.transform, out var tone);
                fail += Check($"no build reads not built ({said})", said.StartsWith("Not built") && tone == Tone.Info);

                new GameObject("YAPS squash (shader)").transform.SetParent(root.transform, false);
                s.builtFrom = YapsSquash.RegionsHash(s);
                said = YapsSquash.Status(root.transform, out tone);
                fail += Check($"built by bone name ({said})", tone == Tone.Good && said.Contains("by bone name"));

                s.pairs.Add(new YapsSquashSettings.Pair { a = new byte[] { 0, 255, 128 }, b = new byte[] { 255, 0, 0 } });
                said = YapsSquash.Status(root.transform, out tone);
                fail += Check($"regions painted after the build ask for a rebuild ({said})", tone == Tone.Warn && said.Contains("rebuild"));

                s.builtFrom = YapsSquash.RegionsHash(s);
                said = YapsSquash.Status(root.transform, out tone);
                fail += Check($"rebuilt from them ({said})", tone == Tone.Good && said.Contains("painted regions"));

                s.pairs[0].a[1] = 200;
                said = YapsSquash.Status(root.transform, out tone);
                fail += Check($"one stroke after that asks again ({said})", tone == Tone.Warn);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            Debug.Log($"[SquashStatusTest] {(fail == 0 ? "PASS" : fail + " WRONG")}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
