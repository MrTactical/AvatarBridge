#if CVR_CCK_EXISTS && VRC_SDK_VRCSDK3
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    // "Which avatar" for a part of a VRChat avatar not yet converted, parented under a
    // container: the avatar, not the container, or the squash painter mirrors about the
    // container's middle and puts its component there.
    public static class AvatarRootTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Avatar root of a nested VRChat avatar")]
        public static void Run()
        {
            failures = 0;
            var container = new GameObject("AvatarRootContainer");
            try
            {
                var avatar = new GameObject("Avatar");
                avatar.transform.SetParent(container.transform, false);
                avatar.transform.localPosition = new Vector3(2f, 0f, 0f);
                avatar.AddComponent<VRCAvatarDescriptor>();
                var hip = new GameObject("Hips").transform;
                hip.SetParent(avatar.transform, false);
                var loose = new GameObject("Loose").transform;
                loose.SetParent(container.transform, false);

                var root = YapsNativeBuilder.AvatarRoot(hip);
                Check(root == avatar.transform, $"a part's root is its VRChat avatar (got {root?.name})");
                Check(YapsNativeBuilder.AvatarRoot(loose) == container.transform, "an object under no avatar still takes the scene root");
            }
            finally
            {
                Object.DestroyImmediate(container);
            }
            Debug.Log(failures == 0 ? "[AvatarRootTest] PASS" : $"[AvatarRootTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[AvatarRootTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
