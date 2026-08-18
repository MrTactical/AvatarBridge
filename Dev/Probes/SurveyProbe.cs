// Runs the survey over one scene's avatars and writes the report beside
// the repo, so the model can be checked against an avatar somebody knows.
//
//   -executeMethod AvatarBridge.Regression.SurveyProbe.RunBatch
//   AVATARBRIDGE_SURVEY_SCENE = the scene to open
#if CVR_CCK_EXISTS
using System;
using System.IO;
using System.Linq;
using System.Text;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class SurveyProbe
    {
        [MenuItem("Tools/AvatarBridge Dev/Survey the selected avatar")]
        public static void Run()
        {
            var avatar = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<CVRAvatar>()
                : null;
            if (avatar == null)
            {
                Debug.LogError("[Survey] select an avatar with a CVRAvatar on it");
                return;
            }
            Debug.Log(AvatarSurvey.Report(AvatarSurvey.Build(avatar)));
        }

        public static void RunBatch()
        {
            string scene = Environment.GetEnvironmentVariable("AVATARBRIDGE_SURVEY_SCENE");
            if (string.IsNullOrEmpty(scene))
            {
                Debug.LogError("[Survey] set AVATARBRIDGE_SURVEY_SCENE");
                EditorApplication.Exit(2);
                return;
            }
            if (scene.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(scene);
                if (asset != null) PrefabUtility.InstantiatePrefab(asset);
            }
            else
            {
                EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            }
            var sb = new StringBuilder();
            var avatars = UnityEngine.Object.FindObjectsOfType<CVRAvatar>(true)
                .OrderBy(a => a.name, StringComparer.Ordinal)
                .ToList();
            if (avatars.Count == 0) sb.Append("no CVRAvatar in ").Append(scene).Append('\n');
            foreach (var avatar in avatars)
            {
                sb.Append(AvatarSurvey.Report(AvatarSurvey.Build(avatar))).Append('\n');
            }
            string repo = Environment.GetEnvironmentVariable("AVATARBRIDGE_REPO") ?? ".";
            string path = Path.Combine(repo, "survey.md");
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[Survey] wrote {path}");
            EditorApplication.Exit(0);
        }
    }
}
#endif
