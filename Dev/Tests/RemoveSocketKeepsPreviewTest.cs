#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Removing one socket leaves another socket's preview running. The remover switched the removed
    // socket's preview off whether it was on or not, and that takes the one preview plug in the scene.
    public static class RemoveSocketKeepsPreviewTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Removing a socket keeps another's preview")]
        public static void Run()
        {
            failures = 0;
            var a = new GameObject("RemoveSocketA");
            var b = new GameObject("RemoveSocketB");
            try
            {
                var gone = a.AddComponent<YapsSocket>();
                var watching = b.AddComponent<YapsSocket>();
                b.transform.position = new Vector3(10f, 0f, 0f);
                YapsPreview.Set(watching, true);
                Check(GameObject.Find(YapsPreview.PlugName) != null, "B's preview plug is out");

                YapsRemover.RemoveSocket(gone);
                Check(GameObject.Find(YapsPreview.PlugName) != null, "removing A leaves B's preview plug");
                Check(watching.preview, "and B still previews");
            }
            finally
            {
                if (b != null) { var s = b.GetComponent<YapsSocket>(); if (s != null) YapsPreview.Set(s, false); }
                YapsPreview.RemoveTestPlug();
                if (a != null) Object.DestroyImmediate(a);
                if (b != null) Object.DestroyImmediate(b);
            }
            Debug.Log(failures == 0 ? "[RemoveSocketKeepsPreviewTest] PASS" : $"[RemoveSocketKeepsPreviewTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[RemoveSocketKeepsPreviewTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
