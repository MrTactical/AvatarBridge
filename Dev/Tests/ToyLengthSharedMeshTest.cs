#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Rebuilding a duplicated avatar's plug leaves the original's toy-length mesh alone. The copy
    // shares the mesh, and the rebuild rewrote it in place, or deleted it with the tip light off.
    public static class ToyLengthSharedMeshTest
    {
        static int failures;
        const string Dir = YapsNativeBuilder.OutputRoot + "/__ToyLengthSharedTest";

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Duplicated avatar keeps its own toy length")]
        public static void Run()
        {
            failures = 0;
            GameObject a = null, b = null;
            try
            {
                a = new GameObject("ToyLengthAvatar");
                var plugA = new GameObject("Plug").transform;
                plugA.SetParent(a.transform, false);
                YapsNativeBuilder.AnnouncePlug(plugA, null, null, 0.2f, 0.02f, true, true, Dir);
                var meshA = Toy(plugA);
                Check(meshA != null, "the original has a toy-length mesh");
                var sizeA = meshA != null ? meshA.bounds.size : Vector3.zero;

                b = Object.Instantiate(a);
                var plugB = b.transform.Find("Plug");
                YapsNativeBuilder.AnnouncePlug(plugB, null, null, 0.4f, 0.02f, true, true, Dir);
                var nowA = Toy(plugA);
                Check(nowA != null && nowA.bounds.size == sizeA, $"the original's length is unchanged ({sizeA} -> {(nowA != null ? nowA.bounds.size.ToString() : "gone")})");
                Check(nowA != Toy(plugB), "the copy has a mesh of its own");

                YapsNativeBuilder.AnnouncePlug(plugB, null, null, 0.4f, 0.02f, false, true, Dir);
                Check(Toy(plugA) != null, "switching the copy's tip light off leaves the original's mesh");
            }
            finally
            {
                if (a != null) Object.DestroyImmediate(a);
                if (b != null) Object.DestroyImmediate(b);
                AssetDatabase.DeleteAsset(Dir);
            }
            Debug.Log(failures == 0 ? "[ToyLengthSharedMeshTest] PASS" : $"[ToyLengthSharedMeshTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static Mesh Toy(Transform plug) => plug.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh)
            .FirstOrDefault(m => m != null && AssetDatabase.GetAssetPath(m).StartsWith(Dir));

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[ToyLengthSharedMeshTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
