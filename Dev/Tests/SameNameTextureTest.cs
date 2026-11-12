#if CVR_CCK_EXISTS
using System.IO;
using System.Linq;
using System.Reflection;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Two textures of one file name are told apart in the weight card. Keyed by name, a texture left
    // alone because another material uses it took the other one with it, hiding a real saving.
    public static class SameNameTextureTest
    {
        static int failures;
        const string Folder = "Assets/__SameNameTextureTest";

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Same-named textures kept apart")]
        public static void Run()
        {
            failures = 0;
            GameObject root = null;
            try
            {
                AssetDatabase.DeleteAsset(Folder);
                AssetDatabase.CreateFolder("Assets", "__SameNameTextureTest");
                AssetDatabase.CreateFolder(Folder, "a");
                AssetDatabase.CreateFolder(Folder, "b");
                var a = Png(Folder + "/a/Body.png");
                var b = Png(Folder + "/b/Body.png");
                var shader = Shader.Find("Standard");
                var outside = new Material(shader) { mainTexture = a };
                AssetDatabase.CreateAsset(outside, Folder + "/Outside.mat");
                var matA = new Material(shader) { mainTexture = a };
                var matB = new Material(shader) { mainTexture = b };
                AssetDatabase.CreateAsset(matA, Folder + "/A.mat");
                AssetDatabase.CreateAsset(matB, Folder + "/B.mat");

                root = new GameObject("SameNameTextureAvatar");
                root.AddComponent<Animator>();
                var avatar = root.AddComponent<CVRAvatar>();
                foreach (var (name, mat) in new[] { ("QuadA", matA), ("QuadB", matB) })
                {
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    quad.name = name;
                    quad.transform.SetParent(root.transform, false);
                    quad.GetComponent<Renderer>().sharedMaterial = mat;
                }

                var weight = AvatarWeight.Measure(avatar);
                var plan = AvatarSlimmer.Find(avatar, null, weight);
                AvatarWeight.NoteLeftAlone(weight, plan.Shared);
                var useB = weight.Textures.FirstOrDefault(t => t.Texture == b);
                Check(useB != null && useB.Reclaim > 0, $"the avatar's own Body can be shrunk ({(useB != null ? useB.Reclaim : -1)} bytes)");
                long counted = (long) typeof(AvatarWeight).GetMethod("Reclaimable", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { weight });
                Check(useB != null && counted >= useB.Reclaim, $"the headline counts it, though the other Body is left alone ({counted})");
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(Folder);
            }
            Debug.Log(failures == 0 ? "[SameNameTextureTest] PASS" : $"[SameNameTextureTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static Texture2D Png(string path)
        {
            var t = new Texture2D(2048, 2048, TextureFormat.RGBA32, false);
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[SameNameTextureTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
