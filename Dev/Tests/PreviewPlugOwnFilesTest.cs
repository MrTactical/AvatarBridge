#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Collections.Generic;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // The socket's preview plug and the window's Test plug keep their own files. Both wrote one mesh
    // path and one bake path, so each new one emptied the other, and ending the preview deleted them.
    public static class PreviewPlugOwnFilesTest
    {
        static int failures;
        const string Hold = "Assets/__PreviewPlugHold";

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Preview plug keeps its own files")]
        public static void Run()
        {
            failures = 0;
            string prefab = YapsSocketBuilder.PlugPropPrefabPath;
            string held = null;
            GameObject setup = null, holder = null;
            var before = new HashSet<string>(Generated());
            try
            {
                // Built, not instanced from the prop prefab, which owns its own files.
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefab) != null)
                {
                    if (!AssetDatabase.IsValidFolder(Hold)) AssetDatabase.CreateFolder("Assets", "__PreviewPlugHold");
                    held = Hold + "/" + System.IO.Path.GetFileName(prefab);
                    string moved = AssetDatabase.MoveAsset(prefab, held);
                    Check(string.IsNullOrEmpty(moved), "the prop prefab moved aside " + moved);
                }

                setup = YapsNativeBuilder.BuildTestPlug(select: false);
                var mf = setup.GetComponent<MeshFilter>();
                var mesh = mf.sharedMesh;
                string meshPath = AssetDatabase.GetAssetPath(mesh);
                string bakePath = Bakes().FirstOrDefault(p => p.Contains("/YAPS Test Plug/"));
                Check(mesh != null && bakePath != null, $"the window's test plug is built and baked ({meshPath}, {bakePath})");

                holder = new GameObject("PreviewPlugOwnFilesSocket");
                holder.transform.position = new Vector3(10f, 0f, 0f);
                var socket = holder.AddComponent<YapsSocket>();
                YapsPreview.DropTestPlug(socket);
                Check(mf.sharedMesh != null && mf.sharedMesh == mesh, "dropping a preview plug leaves its mesh");

                YapsPreview.RemoveTestPlug();
                Check(mf.sharedMesh != null, "taking the preview plug away leaves its mesh");
                Check(bakePath != null && AssetDatabase.LoadMainAssetAtPath(bakePath) != null, "and its bake");
            }
            finally
            {
                if (setup != null) Object.DestroyImmediate(setup);
                if (holder != null) Object.DestroyImmediate(holder);
                YapsPreview.RemoveTestPlug();
                if (held != null)
                {
                    AssetDatabase.MoveAsset(held, prefab);
                    AssetDatabase.DeleteAsset(Hold);
                }
                foreach (var path in Generated().Where(p => !before.Contains(p)
                             && (p.Contains("/YAPS Test Plug/") || p.Contains("/YAPS Preview Plug/") || p.Contains("/Test Plug/"))))
                    AssetDatabase.DeleteAsset(path);
            }
            Debug.Log(failures == 0 ? "[PreviewPlugOwnFilesTest] PASS" : $"[PreviewPlugOwnFilesTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static IEnumerable<string> Generated() =>
            AssetDatabase.FindAssets("", new[] { YapsNativeBuilder.OutputRoot }).Select(AssetDatabase.GUIDToAssetPath).Distinct().ToList();

        static IEnumerable<string> Bakes() => Generated().Where(p => p.EndsWith(" bake.asset"));

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[PreviewPlugOwnFilesTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
