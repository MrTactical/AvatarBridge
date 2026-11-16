#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Reflection;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // The first hole added to an avatar's YAPS folder is "YAPS Hole", the second "YAPS Hole 2". The
    // counter counted the new object itself, so the first came out "YAPS Hole 2" and the menu read so.
    public static class FirstSocketNameTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/First socket takes the plain name")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("FirstSocketNameAvatar");
            var window = ScriptableObject.CreateInstance<YapsSetupWindow>();
            try
            {
                root.AddComponent<Animator>();
                root.AddComponent<CVRAvatar>();
                Selection.activeGameObject = null;
                typeof(YapsSetupWindow).GetField("_target", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, root);
                var add = typeof(YapsSetupWindow).GetMethod("AddSocket", BindingFlags.NonPublic | BindingFlags.Instance);
                Selection.activeGameObject = null;
                var first = (YapsSocket) add.Invoke(window, new object[] { YapsSocket.SocketKind.Hole });
                Selection.activeGameObject = null;
                var second = (YapsSocket) add.Invoke(window, new object[] { YapsSocket.SocketKind.Hole });
                Check(first != null && first.name == "YAPS Hole", $"the first is \"YAPS Hole\" (was \"{(first != null ? first.name : "none")}\")");
                Check(second != null && second.name == "YAPS Hole 2", $"the second is \"YAPS Hole 2\" (was \"{(second != null ? second.name : "none")}\")");
            }
            finally
            {
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(root);
            }
            Debug.Log(failures == 0 ? "[FirstSocketNameTest] PASS" : $"[FirstSocketNameTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[FirstSocketNameTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
