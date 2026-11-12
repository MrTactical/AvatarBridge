#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Reflection;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A mirrored prop's grab box fits the prop. A negative scale fell to the 1e-4 floor, so the box
    // came out ten thousand times too wide on the mirrored axis.
    public static class MirroredGrabBoxTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Mirrored prop grab box fits")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("MirroredGrabBoxProp");
            try
            {
                root.transform.localScale = new Vector3(-1f, 1f, 1f);
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform, false);
                var add = typeof(YapsPropBuilder).GetMethod("AddCollider", BindingFlags.NonPublic | BindingFlags.Static);
                add.Invoke(null, new object[] { root, null, new YapsPropBuilder.Outcome() });
                var box = root.GetComponent<BoxCollider>();
                Check(box != null, "a grab box was added");
                if (box != null)
                    Check(Mathf.Abs(Mathf.Abs(box.size.x) - 1f) < 0.01f, $"it is the cube's width on the mirrored axis (was {box.size.x})");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            Debug.Log(failures == 0 ? "[MirroredGrabBoxTest] PASS" : $"[MirroredGrabBoxTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[MirroredGrabBoxTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
