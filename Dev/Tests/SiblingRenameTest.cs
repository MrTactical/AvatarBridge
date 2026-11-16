#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Two sockets side by side that both strip to one baked name must not both be renamed to it: a
    // shared path is a curve that drives whichever Unity finds first.
    public static class SiblingRenameTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Sibling sockets keep distinct names")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("SiblingRenameAvatar");
            try
            {
                root.AddComponent<Animator>();
                new GameObject("[VF1] BakedSpsSocket").transform.SetParent(root.transform, false);
                new GameObject("[VF2] BakedSpsSocket").transform.SetParent(root.transform, false);
                var ctx = new BridgeContext
                {
                    Settings = new BridgeSettings { convertYapsSystems = true },
                    Report = new BridgeReport(),
                    Target = root,
                    MergedController = new AnimatorController(),
                };
                YapsRename.Run(ctx);
                var names = Enumerable.Range(0, root.transform.childCount).Select(i => root.transform.GetChild(i).name).ToList();
                Check(names.Distinct().Count() == names.Count, "the two keep distinct names: " + string.Join(", ", names));
                Check(names.Contains("YAPS Socket"), "one still takes the new name");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            Debug.Log(failures == 0 ? "[SiblingRenameTest] PASS" : $"[SiblingRenameTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[SiblingRenameTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
