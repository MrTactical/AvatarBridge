#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Reflection;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Baking a plug on a mesh asset that is not the toolkit's leaves that asset's bounds alone. Any
    // .asset mesh was widened in place, so every other prefab using it took the wider box.
    public static class BoundsSharedMeshTest
    {
        static int failures;
        const string Folder = "Assets/__BoundsSharedMeshTest";

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Bake leaves a shared mesh asset's bounds")]
        public static void Run()
        {
            failures = 0;
            var go = new GameObject("BoundsSharedPlug");
            try
            {
                AssetDatabase.DeleteAsset(Folder);
                AssetDatabase.CreateFolder("Assets", "__BoundsSharedMeshTest");
                AssetDatabase.CreateFolder(Folder, "out");
                var temp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                var mesh = Object.Instantiate(temp.GetComponent<MeshFilter>().sharedMesh);
                Object.DestroyImmediate(temp);
                AssetDatabase.CreateAsset(mesh, Folder + "/Plug.asset");
                var before = mesh.bounds;

                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                typeof(YapsBaker).GetMethod("ExtendBounds", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null,
                    new object[] { renderer, mesh, 0.2f, 0.02f, Folder + "/out", Vector3.zero, go.transform, false });

                var shared = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Plug.asset");
                Check(shared.bounds.size == before.size, $"the asset keeps its bounds ({before.size} -> {shared.bounds.size})");
                var own = go.GetComponent<MeshFilter>().sharedMesh;
                Check(own != shared && own.bounds.size.x > before.size.x, "the plug draws from a widened copy of its own");
            }
            finally
            {
                Object.DestroyImmediate(go);
                AssetDatabase.DeleteAsset(Folder);
            }
            Debug.Log(failures == 0 ? "[BoundsSharedMeshTest] PASS" : $"[BoundsSharedMeshTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[BoundsSharedMeshTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
