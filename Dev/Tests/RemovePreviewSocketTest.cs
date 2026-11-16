#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Removing the dropped preview socket removes it without an error. Turning its preview off
    // destroys it, and the remover went on reading the destroyed socket.
    public static class RemovePreviewSocketTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Removing the preview socket")]
        public static void Run()
        {
            failures = 0;
            GameObject go = null;
            try
            {
                go = YapsSocketBuilder.BuildPreviewSocket(YapsPreview.SocketName, YapsSocket.SocketKind.Hole);
                Check(go != null && go.GetComponent<YapsSocket>() != null, "the preview socket was built");
                string said = null;
                Exception thrown = null;
                try { said = YapsRemover.RemoveSocket(go.GetComponent<YapsSocket>()); }
                catch (Exception e) { thrown = e; }
                Check(thrown == null, "removing it throws nothing" + (thrown != null ? $" ({thrown.GetType().Name}: {thrown.Message})" : ""));
                Check(go == null, $"it is gone ({said})");
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            Debug.Log(failures == 0 ? "[RemovePreviewSocketTest] PASS" : $"[RemovePreviewSocketTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[RemovePreviewSocketTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
