#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // The socket inspector draws when its chosen mesh renderer has lost its mesh. It read the
    // missing mesh's shape count and threw, so the inspector drew nothing, the fix included.
    public static class SocketInspectorNoMeshTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Socket inspector with a meshless renderer")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("SocketInspectorNoMeshAvatar");
            Editor editor = null;
            try
            {
                root.AddComponent<Animator>();
                root.AddComponent<CVRAvatar>();
                var body = new GameObject("Body");
                body.transform.SetParent(root.transform, false);
                var smr = body.AddComponent<SkinnedMeshRenderer>();
                var holder = new GameObject("Socket");
                holder.transform.SetParent(root.transform, false);
                var socket = holder.AddComponent<YapsSocket>();
                var so = new SerializedObject(socket);
                so.FindProperty("renderer").objectReferenceValue = smr;
                so.ApplyModifiedPropertiesWithoutUndo();

                editor = Editor.CreateEditor(socket);
                Exception thrown = null;
                try { editor.CreateInspectorGUI(); }
                catch (Exception e) { thrown = e; }
                Check(thrown == null, "the inspector draws" + (thrown != null ? $" ({thrown.GetType().Name}: {thrown.Message})" : ""));
            }
            finally
            {
                if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(root);
            }
            Debug.Log(failures == 0 ? "[SocketInspectorNoMeshTest] PASS" : $"[SocketInspectorNoMeshTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[SocketInspectorNoMeshTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
