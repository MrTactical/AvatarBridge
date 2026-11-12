#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Reflection;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A socket's reaction clips are pathed from the avatar's Animator, whose controllers the layer
    // goes into. Under an accessory with an Animator of its own they were pathed from that, and
    // bound nothing.
    public static class SocketReactionsRootTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Socket reactions path from the avatar")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("SocketReactionsRootAvatar");
            try
            {
                var rootAnimator = root.AddComponent<Animator>();
                root.AddComponent<CVRAvatar>();
                var accessory = new GameObject("Accessory");
                accessory.transform.SetParent(root.transform, false);
                accessory.AddComponent<Animator>();
                var holder = new GameObject("Socket");
                holder.transform.SetParent(accessory.transform, false);
                var socket = holder.AddComponent<YapsSocket>();

                var targets = typeof(YapsSocketReactions).GetMethod("Targets", BindingFlags.NonPublic | BindingFlags.Static);
                var args = new object[] { socket, null, null, null };
                targets.Invoke(null, args);
                var animator = args[1] as Animator;
                Check(animator == rootAnimator, $"the paths start at the avatar's Animator (was \"{(animator != null ? animator.name : "none")}\")");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            Debug.Log(failures == 0 ? "[SocketReactionsRootTest] PASS" : $"[SocketReactionsRootTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[SocketReactionsRootTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
