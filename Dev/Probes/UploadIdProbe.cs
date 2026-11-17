#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    // Converting again keeps the upload ID: from the old copy still in the scene, then from the
    // prefab alone once every copy is deleted. The scene is never saved, and the prefab's ID is
    // cleared at the end so corpus runs never see it.
    //   -executeMethod AvatarBridge.Regression.UploadIdProbe.Run -abScene <path>
    public static class UploadIdProbe
    {
        const string Fake = "00000000-0000-4000-8000-0000000000ab";

        public static void Run()
        {
            int code = 1;
            string prefabPath = null;
            try
            {
                var args = Environment.GetCommandLineArgs();
                string Arg(string k) => args[Array.IndexOf(args, k) + 1];
                var scene = EditorSceneManager.OpenScene(Arg("-abScene"), OpenSceneMode.Single);
                var source = scene.GetRootGameObjects().First(r => r.GetComponent<VRCAvatarDescriptor>() != null);
                var settings = (BridgeSettings) AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("AvatarBridge.Regression.RegressionRunner")).First(t => t != null)
                    .GetMethod("CorpusSettings", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    .Invoke(null, null);

                GameObject Convert()
                {
                    source.SetActive(true);
                    var report = BridgeConverter.Convert(source.GetComponentInChildren<VRCAvatarDescriptor>(true), settings);
                    foreach (var e in report.Entries.Where(e => e.Category == "Upload"))
                        Debug.Log($"[UploadId]   {e.Status} {e.Subject}: {e.Detail}");
                    prefabPath = System.IO.Path.GetDirectoryName(report.SavedReportPath).Replace('\\', '/') + "/" + report.ConvertedRoot.name + ".prefab";
                    return report.ConvertedRoot;
                }
                string Id(GameObject go) => go.GetComponent<CVRAssetInfo>().objectId;

                var first = Convert();
                string fresh = Id(first);
                first.GetComponent<CVRAssetInfo>().objectId = Fake;
                var second = Convert();
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
                var third = Convert();
                bool pass = string.IsNullOrEmpty(fresh) && Id(second) == Fake && Id(third) == Fake;
                Debug.Log($"[UploadId] first \"{fresh}\", from the old copy \"{Id(second)}\", from the prefab alone \"{Id(third)}\": {(pass ? "PASS" : "FAIL")}");
                code = pass ? 0 : 2;
            }
            catch (Exception e)
            {
                Debug.LogError("[UploadId] threw: " + e);
            }
            finally
            {
                var prefab = prefabPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) : null;
                if (prefab != null && prefab.TryGetComponent(out CVRAssetInfo info) && info.objectId == Fake)
                {
                    info.objectId = "";
                    EditorUtility.SetDirty(prefab);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[UploadId] prefab's test ID cleared");
                }
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
#endif
