#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Taking one jelly part off removes that part's collider ends alone. Matched as a piece of the
    // name, "Thighs" also took the ends of "Thighs inner", whose jelly stayed with its slots Missing.
    public static class JellyPartNameTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Jelly part takes only its own collider ends")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("JellyPartNameAvatar");
            try
            {
                root.AddComponent<Animator>();
                var cloth = new GameObject("Cloth").transform;
                cloth.SetParent(root.transform, false);
                var mine = new GameObject("Jelly collider L 0 Thighs").transform;
                mine.SetParent(cloth, false);
                var other = new GameObject("Jelly collider L 0 Thighs inner").transform;
                other.SetParent(cloth, false);

                YapsJelly.Unworn(root.transform, "Thighs");
                Check(cloth.Find("Jelly collider L 0 Thighs") == null, "the part's own end goes");
                Check(cloth.Find("Jelly collider L 0 Thighs inner") != null, "another part whose name holds it keeps its end");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            Debug.Log(failures == 0 ? "[JellyPartNameTest] PASS" : $"[JellyPartNameTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[JellyPartNameTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
