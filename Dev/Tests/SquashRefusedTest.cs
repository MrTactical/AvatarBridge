#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.IO;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A squash whose every layer refuses the patch squashes nothing. It was built anyway and
    // reported as converted, with the refusals only in the detail (found by the corpus on an
    // avatar whose one skin shader would not compile once patched).
    //
    // Run: -executeMethod AvatarBridge.Regression.SquashRefusedTest.Run
    public static class SquashRefusedTest
    {
        const string Dir = "Assets/SquashRefusedTest";

        // A surface shader is always refused: Unity writes its vertex stage.
        const string Surface = @"Shader ""Test/SquashRefused""
{
    SubShader
    {
        CGPROGRAM
        #pragma surface surf Lambert
        struct Input { float2 uv_MainTex; };
        void surf (Input IN, inout SurfaceOutput o) { o.Albedo = 1; }
        ENDCG
    }
}
";

        [MenuItem("Tools/AvatarBridge Dev/Test: squash with every layer refused")]
        public static void Run()
        {
            int failures = 0;
            void Check(bool ok, string what)
            {
                if (!ok) failures++;
                Debug.Log($"[SquashRefusedTest] {(ok ? "ok   " : "WRONG")} {what}");
            }

            var root = new GameObject("SquashRefusedAvatar").transform;
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Dir + "/SquashRefused.shader", Surface);
                AssetDatabase.ImportAsset(Dir + "/SquashRefused.shader", ImportAssetOptions.ForceSynchronousImport);
                var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Dir + "/SquashRefused.shader")) { name = "Skin" };
                AssetDatabase.CreateAsset(material, Dir + "/Skin.mat");

                // A chest with a pair on it, each part a small ball of skin on its own bone.
                var chest = new GameObject("Chest").transform;
                chest.SetParent(root, false);
                chest.localPosition = new Vector3(0f, 1.3f, 0f);
                var bones = new[] { chest, Bone("Breast_L", chest, -0.08f), Bone("Breast_R", chest, 0.08f) };
                var smr = new GameObject("Body").AddComponent<SkinnedMeshRenderer>();
                smr.transform.SetParent(root, false);
                const int PerSide = 40;
                var verts = new Vector3[2 * PerSide];
                var weights = new BoneWeight[2 * PerSide];
                for (int v = 0; v < verts.Length; v++)
                {
                    int side = v / PerSide, i = v % PerSide;
                    float y = 1f - 2f * (i + 0.5f) / PerSide, r = Mathf.Sqrt(1f - y * y), a = i * 2.4f;
                    verts[v] = bones[1 + side].position + 0.05f * new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a));
                    weights[v] = new BoneWeight { boneIndex0 = 1 + side, weight0 = 1f };
                }
                var mesh = new Mesh { name = "SquashRefused", vertices = verts, boneWeights = weights };
                mesh.triangles = Enumerable.Range(0, verts.Length / 3 * 3).ToArray();
                mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * smr.transform.localToWorldMatrix).ToArray();
                smr.bones = bones;
                smr.sharedMesh = mesh;
                smr.sharedMaterial = material;

                string said = YapsSquash.Add(root, Dir + "/Out", out bool built);
                Debug.Log("[SquashRefusedTest]   said: " + said);
                Check(!built, "a squash with no layer that can take it is not built");
                Check(!root.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("YAPS squash")),
                    "and leaves nothing on the avatar");
                Check(said.Contains("surface shader"), "the reason given is the layer's refusal");
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                failures++;
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
                AssetDatabase.DeleteAsset(Dir);
            }
            Debug.Log(failures == 0 ? "[SquashRefusedTest] PASS" : $"[SquashRefusedTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static Transform Bone(string name, Transform parent, float x)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = new Vector3(x, 0f, 0.08f);
            return t;
        }
    }
}
#endif
