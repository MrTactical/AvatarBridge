#if CVR_CCK_EXISTS
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Known-answer test for blendshapes a plug renderer simply holds.
    //
    // The bent path rebuilds each vertex from the bake, so a shape held at a
    // fixed weight with no animation has to reach the bake one way or the
    // other: as the material's starting weight when the shape is baked, or
    // folded into the rest pose when it is not. Before, both were dropped
    // and a part hidden by a shape at 100 reappeared mid-bend.
    //
    // A strip of vertices along +Z on one bone. "Held" pushes vertex 5 along
    // x and is left out of the bake; "Baked" pushes vertex 6 along y and is
    // baked. Held at 100 and 50.
    public static class YapsHeldShapeTest
    {
        const string Dir = "Assets/__YapsHeldShapeTest";

        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[YapsHeldShapeTest] {(ok ? "ok  " : "FAIL")} {label}");
                return ok ? 0 : 1;
            }

            GameObject root = null;
            try
            {
                root = new GameObject("__YapsHeldShapeTest");
                var bone = new GameObject("Plug").transform;
                bone.SetParent(root.transform, false);

                const int n = 12;
                var mesh = new Mesh { name = "HeldShapeStrip" };
                var vertices = new Vector3[n];
                var weights = new BoneWeight[n];
                for (int i = 0; i < n; i++)
                {
                    vertices[i] = new Vector3(i % 2 == 0 ? 0.01f : -0.01f, 0f, i * 0.05f);
                    weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                }
                mesh.vertices = vertices;
                var triangles = new int[(n - 2) * 3];
                for (int t = 0; t < n - 2; t++)
                {
                    triangles[t * 3] = t; triangles[t * 3 + 1] = t + 1; triangles[t * 3 + 2] = t + 2;
                }
                mesh.triangles = triangles;
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.boneWeights = weights;
                mesh.bindposes = new[] { bone.worldToLocalMatrix * root.transform.localToWorldMatrix };

                var held = new Vector3[n];
                held[5] = new Vector3(0.1f, 0f, 0f);
                mesh.AddBlendShapeFrame("Held", 100f, held, null, null);
                var baked = new Vector3[n];
                baked[6] = new Vector3(0f, 0.1f, 0f);
                mesh.AddBlendShapeFrame("Baked", 100f, baked, null, null);

                var skin = root.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = mesh;
                skin.bones = new[] { bone };
                skin.rootBone = bone;
                skin.SetBlendShapeWeight(0, 100f);
                skin.SetBlendShapeWeight(1, 50f);

                var result = YapsBaker.Bake(skin, bone, Dir, null, out string failure, new[] { "Baked" });
                fail += Check($"the strip bakes ({failure ?? "ok"})", result != null);
                if (result != null)
                {
                    fail += Check("only the named shape is baked",
                        result.Shapes.Count == 1 && result.Shapes[0] == "Baked");
                    fail += Check("the baked shape carries its held weight",
                        result.ShapeWeights != null && result.ShapeWeights.Length == 1
                        && Mathf.Abs(result.ShapeWeights[0] - 0.5f) < 1e-4f);

                    var material = new Material(Shader.Find("Standard"));
                    YapsBaker.Apply(result, material, true);
                    fail += Check("...and the material starts there",
                        Mathf.Abs(material.GetVector("_YAPS_ShapeWeights").x - 0.5f) < 1e-4f);

                    AssetDatabase.SaveAssets();
                    var rest = ReadRest(AssetDatabase.GetAssetPath(result.Bake), n);
                    fail += Check("the unbaked held shape is folded into the rest pose",
                        Mathf.Abs(rest[5].x - (0.1f + vertices[5].x)) < 1e-3f);
                    fail += Check("the baked shape is not folded in as well",
                        Mathf.Abs(rest[6].y) < 1e-3f);
                    fail += Check("vertices no shape touches stay where they were",
                        Mathf.Abs(rest[4].x - vertices[4].x) < 1e-3f && Mathf.Abs(rest[4].y) < 1e-3f);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                fail++;
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(Dir);
            }

            Debug.Log(fail == 0
                ? "[YapsHeldShapeTest] PASS: held blendshapes survive the bend."
                : $"[YapsHeldShapeTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        // The bake is marked unreadable for upload, so read what was saved:
        // one float a texel, a header float, then ten a vertex.
        static Vector3[] ReadRest(string path, int count)
        {
            string text = File.ReadAllText(path);
            string hex = Regex.Match(text, @"_typelessdata: ([0-9a-f]+)").Groups[1].Value;
            float At(int index) => BitConverter.ToSingle(new[]
            {
                Convert.ToByte(hex.Substring(index * 8, 2), 16),
                Convert.ToByte(hex.Substring(index * 8 + 2, 2), 16),
                Convert.ToByte(hex.Substring(index * 8 + 4, 2), 16),
                Convert.ToByte(hex.Substring(index * 8 + 6, 2), 16),
            }, 0);
            var rest = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int b = 1 + i * 10;
                rest[i] = new Vector3(At(b), At(b + 1), At(b + 2));
            }
            return rest;
        }
    }
}
#endif
