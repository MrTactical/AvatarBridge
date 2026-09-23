// The weigh pass on one avatar: no data texture (point filtered, no mips,
// a bake or a lookup) may be given a smaller size, and ordinary textures
// still are, so the check is not passing by advising nothing.
//
// Run: -executeMethod AvatarBridge.Regression.WeighDataProbe.Run -yapsAvatar <prefab path>
#if CVR_CCK_EXISTS
using System;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class WeighDataProbe
    {
        public static void Run()
        {
            int fail = 0;
            try
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(args, "-yapsAvatar");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(args[i + 1]);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var go = (GameObject) PrefabUtility.InstantiatePrefab(prefab);
                var report = AvatarWeight.Measure(go.GetComponent<CVRAvatar>());
                var data = report.Textures.Where(t => t.Data).ToList();
                foreach (var t in data)
                    Debug.Log($"[WeighData] data texture {t.Name} {t.Width}x{t.Height}: density {t.Density:0}, suggested {t.Suggested}");
                int offered = data.Count(t => t.Suggested > 0);
                int ordinary = report.Textures.Count(t => !t.Data && t.Suggested > 0);
                if (offered > 0) fail++;
                Debug.Log($"[WeighData] {(offered == 0 ? "ok  " : "FAIL")} no data texture offered smaller ({offered} of {data.Count})");
                Debug.Log($"[WeighData] info: {ordinary} ordinary texture(s) still offered smaller");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                fail++;
            }
            Debug.Log("[WeighData] " + (fail == 0 ? "PASS" : "FAIL"));
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
