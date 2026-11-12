#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A squash rebuild that refuses leaves the build it found. It stripped the avatar first and then
    // refused, so a rig with no pair left it with no squash and a message saying nothing changed.
    public static class SquashRefusalKeepsBuildTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/A refused squash rebuild keeps the build")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("SquashRefusalAvatar");
            try
            {
                root.AddComponent<Animator>();
                // The mark of a build; no pair of bones to squash, so a rebuild refuses.
                new GameObject("YAPS squash (shader)").transform.SetParent(root.transform, false);
                Check(YapsSquash.Has(root.transform), "set up as built");
                string said = YapsSquash.Add(root.transform, "Assets/SquashRefusalTest", out bool built);
                Check(!built, $"the rebuild refuses ({said})");
                Check(YapsSquash.Has(root.transform), "the build it found is still there");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            Debug.Log(failures == 0 ? "[SquashRefusalKeepsBuildTest] PASS" : $"[SquashRefusalKeepsBuildTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[SquashRefusalKeepsBuildTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
