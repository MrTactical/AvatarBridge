#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A fresh conversion's jelly writes over the last build's folder of its name instead of adding
    // a numbered one each time, unless another avatar in the scene wears that folder.
    public static class JellyFolderTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Jelly folder is reused, not piled up")]
        public static void Run()
        {
            failures = 0;
            var generated = GeneratedFiles.Snapshot();
            var root = new GameObject("JellyFolderAvatar");
            var other = new GameObject("JellyFolderAvatar");
            Material worn = null;
            try
            {
                string folder = YapsNativeBuilder.OutputRoot + "/JellyFolderTest";
                YapsNativeBuilder.EnsureFolderPublic(folder);
                string mine = folder + "/JellyFolderAvatar Jelly";
                AssetDatabase.CreateFolder(folder, "JellyFolderAvatar Jelly");
                Check(YapsJelly.FolderFor(root.transform, folder) == mine, $"the last build's folder is written over ({YapsJelly.FolderFor(root.transform, folder)})");

                // Another avatar of the same name wearing that folder keeps it.
                var shader = AssetDatabase.FindAssets("t:Shader", new[] { YapsNativeBuilder.OutputRoot })
                    .Select(g => AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(g)))
                    .FirstOrDefault(s => s != null && s.FindPropertyIndex(YapsJelly.StateOf(0)) >= 0);
                if (shader == null) Debug.Log("[JellyFolderTest] skip  no jelly shader in the project to wear the folder with");
                else
                {
                    var state = new Texture2D(2, 2);
                    AssetDatabase.CreateAsset(state, mine + "/state.asset");
                    worn = new Material(shader);
                    worn.SetTexture(YapsJelly.StateOf(0), state);
                    other.AddComponent<MeshRenderer>().sharedMaterial = worn;
                    string got = YapsJelly.FolderFor(root.transform, folder);
                    Check(got == mine + " 2", $"a folder another avatar wears is left to it ({got})");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(other);
                if (worn != null) Object.DestroyImmediate(worn);
                GeneratedFiles.DeleteNew(generated);
            }
            Debug.Log(failures == 0 ? "[JellyFolderTest] PASS" : $"[JellyFolderTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[JellyFolderTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
