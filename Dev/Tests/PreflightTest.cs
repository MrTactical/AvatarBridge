#if CVR_CCK_EXISTS
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // The preflight's "the upload drops them" rests on what a saved prefab
    // keeps. This saves one the way the CCK's upload does and looks, then
    // checks the preflight names the same two things.
    public static class PreflightTest
    {
        const string Path = "Assets/PreflightTest.prefab";
        static int failures;

        [MenuItem("Tools/AvatarBridge Dev/Test: upload preflight")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("PreflightAvatar");
            var outside = new GameObject("Elsewhere");
            GameObject instance = null;
            try
            {
                root.AddComponent<Animator>();
                var avatar = root.AddComponent<CVRAvatar>();
                var body = new GameObject("Body");
                body.transform.SetParent(root.transform, false);
                body.AddComponent<MeshFilter>().sharedMesh = new Mesh { name = "InMemory" };
                var skin = new GameObject("Skin").AddComponent<SkinnedMeshRenderer>();
                skin.transform.SetParent(root.transform, false);
                skin.rootBone = outside.transform;
#if AVATARBRIDGE_YAPS
                // What Thry's Unlock all leaves: the values of a shader the material no longer wears.
                var unlocked = new Material(Shader.Find("Standard")) { name = "Unlocked" };
                unlocked.SetFloat("_YAPS_Enabled", 1f);
                skin.sharedMaterial = unlocked;
#endif

                var report = new BridgeReport();
                UploadPreflight.Run(report, avatar);
                foreach (var e in report.Entries) Debug.Log($"[PreflightTest]   {e.Subject}: {e.Detail}");
                Check(report.Entries.Any(e => e.Subject.Contains("not saved") && e.Detail.Contains("InMemory")), "names the unsaved mesh");
                Check(report.Entries.Any(e => e.Subject.Contains("outside the avatar") && e.Detail.Contains("Elsewhere")), "names the outside reference");
                Check(!report.Entries.Any(e => e.Subject.Contains("no Animator")), "an Animator on the root passes");
#if AVATARBRIDGE_YAPS
                Check(report.Entries.Any(e => e.Subject.Contains("lost the YAPS shader") && e.Detail.Contains("Unlocked")), "names the material that lost its YAPS shader");
#endif

                var saved = PrefabUtility.SaveAsPrefabAsset(root, Path);
                Check(saved.GetComponentInChildren<MeshFilter>().sharedMesh == null, "the saved prefab lost the unsaved mesh");
                Check(saved.GetComponentInChildren<SkinnedMeshRenderer>().rootBone == null, "the saved prefab lost the outside reference");

                // A prefab instance's own bookkeeping points at a scene object, and is not lost.
                instance = (GameObject)PrefabUtility.InstantiatePrefab(saved);
                report = new BridgeReport();
                UploadPreflight.Run(report, instance.GetComponent<CVRAvatar>());
                Check(!report.Entries.Any(e => e.Subject.Contains("not saved")), "a prefab instance has nothing unsaved");

                Object.DestroyImmediate(root.GetComponent<CVRAvatar>());
                Object.DestroyImmediate(root.GetComponent<Animator>());
                var bare = root.AddComponent<CVRAvatar>();
                report = new BridgeReport();
                UploadPreflight.Run(report, bare);
                Check(report.Entries.Any(e => e.Subject.Contains("no Animator")), "a root with no Animator is refused");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(outside);
                if (instance != null) Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(Path);
            }
            Debug.Log(failures == 0 ? "[PreflightTest] PASS" : $"[PreflightTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[PreflightTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
