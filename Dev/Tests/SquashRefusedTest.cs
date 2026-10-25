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
    // avatar whose one skin shader would not compile once patched). Standard is refused too, but
    // its skin moves onto YAPS Standard instead.
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

            var generated = GeneratedFiles.Snapshot();
            var roots = new System.Collections.Generic.List<Transform>();
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Dir + "/SquashRefused.shader", Surface);
                AssetDatabase.ImportAsset(Dir + "/SquashRefused.shader", ImportAssetOptions.ForceSynchronousImport);
                var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Dir + "/SquashRefused.shader")) { name = "Skin" };
                AssetDatabase.CreateAsset(material, Dir + "/Skin.mat");

                var root = Avatar(material, roots);
                string said = YapsSquash.Add(root, Dir + "/Out", out bool built);
                Debug.Log("[SquashRefusedTest]   said: " + said);
                Check(!built, "a squash with no layer that can take it is not built");
                Check(!root.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("YAPS squash")),
                    "and leaves nothing on the avatar");
                Check(said.Contains("surface shader"), "the reason given is the layer's refusal");

                // Standard has no source to patch: its skin moves onto YAPS Standard, Unity's own
                // source, which Simple Lit's property list cannot stand in for.
                var standard = new Material(Shader.Find("Standard")) { name = "Plain" };
                AssetDatabase.CreateAsset(standard, Dir + "/Plain.mat");
                root = Avatar(standard, roots);
                said = YapsSquash.Add(root, Dir + "/OutPlain", out built);
                Debug.Log("[SquashRefusedTest]   said: " + said);
                var worn = root.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
                Check(built, "a plain Standard skin is squashed");
                Check(worn != standard && worn.HasProperty("_YAPS_DentOwn") && worn.HasProperty("_MetallicGlossMap"),
                    "on a patched YAPS Standard copy");
                Check(said.Contains("wears YAPS Standard"), "and the log says so");

                // A metallic map was refused while Simple Lit was the only host; it carries over now.
                var mapped = new Material(standard) { name = "Mapped" };
                // A saved texture: the built-in white one does not survive the material being saved.
                var metal = new Texture2D(4, 4);
                AssetDatabase.CreateAsset(metal, Dir + "/Metal.asset");
                mapped.SetTexture("_MetallicGlossMap", metal);
                mapped.EnableKeyword("_METALLICGLOSSMAP");
                // Transparent, as Standard's inspector leaves it: the blend, the keyword and a queue set
                // by hand, all of which the copy must keep.
                mapped.SetFloat("_Mode", 3f);
                mapped.SetFloat("_SrcBlend", (float) UnityEngine.Rendering.BlendMode.One);
                mapped.SetFloat("_DstBlend", (float) UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mapped.SetFloat("_ZWrite", 0f);
                mapped.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                mapped.renderQueue = (int) UnityEngine.Rendering.RenderQueue.Transparent;
                AssetDatabase.CreateAsset(mapped, Dir + "/Mapped.mat");
                Debug.Log($"[SquashRefusedTest]   source queue {mapped.renderQueue}, keywords {string.Join(" ", mapped.shaderKeywords)}");
                root = Avatar(mapped, roots);
                said = YapsSquash.Add(root, Dir + "/OutMapped", out built);
                Debug.Log("[SquashRefusedTest]   said: " + said);
                worn = root.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
                Check(built, "a Standard skin with a metallic map is squashed");
                Check(worn != mapped && worn.HasProperty("_YAPS_DentOwn")
                    && worn.HasProperty("_MetallicGlossMap") && worn.GetTexture("_MetallicGlossMap") == metal
                    && worn.IsKeywordEnabled("_METALLICGLOSSMAP"), "and keeps its map");
                Check(worn.renderQueue == mapped.renderQueue && worn.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON")
                    && worn.GetFloat("_DstBlend") == mapped.GetFloat("_DstBlend"),
                    $"and its blend and render queue ({worn.renderQueue} for {mapped.renderQueue})");
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                failures++;
            }
            finally
            {
                foreach (var r in roots) Object.DestroyImmediate(r.gameObject);
                AssetDatabase.DeleteAsset(Dir);
                GeneratedFiles.DeleteNew(generated);
            }
            Debug.Log(failures == 0 ? "[SquashRefusedTest] PASS" : $"[SquashRefusedTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        // A chest with a pair on it, each part a small ball of skin on its own bone.
        static Transform Avatar(Material material, System.Collections.Generic.List<Transform> roots)
        {
            var root = new GameObject("SquashRefusedAvatar").transform;
            roots.Add(root);
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
            return root;
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
