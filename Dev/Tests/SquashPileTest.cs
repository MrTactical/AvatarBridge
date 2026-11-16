#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A fresh avatar's squash writes over the last build of its name instead of adding a numbered
    // set each time, unless another avatar in the scene wears that set.
    //
    // Run: -executeMethod AvatarBridge.Regression.SquashPileTest.Run
    public static class SquashPileTest
    {
        const string Dir = "Assets/SquashPileTest";
        const string Name = "SquashPileAvatar";
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Squash set is reused, not piled up")]
        public static void Run()
        {
            failures = 0;
            var generated = GeneratedFiles.Snapshot();
            var roots = new List<Transform>();
            try
            {
                Directory.CreateDirectory(Dir);
                AssetDatabase.Refresh();
                var skin = new Material(Shader.Find("Standard")) { name = "Skin" };
                AssetDatabase.CreateAsset(skin, Dir + "/Skin.mat");
                string folder = YapsNativeBuilder.OutputRoot + "/Squash";
                string[] Files() => Directory.Exists(folder)
                    ? Directory.GetFiles(folder).Where(f => Path.GetFileName(f).StartsWith(Name) && !f.EndsWith(".meta")).ToArray()
                    : new string[0];

                var first = Fresh(roots, skin);
                YapsSquash.Build(first, out bool built);
                int count = Files().Length;
                Check(built && count > 0, $"the first build writes its set ({count} files)");
                string worn = AssetDatabase.GetAssetPath(Body(first).sharedMaterial);

                // The same avatar converted again: the old copy is gone, the new one wears nothing.
                Object.DestroyImmediate(first.gameObject);
                roots.Remove(first);
                var again = Fresh(roots, skin);
                YapsSquash.Build(again, out built);
                Check(built && Files().Length == count, $"a fresh build writes over it ({Files().Length} files, was {count})");
                Check(AssetDatabase.GetAssetPath(Body(again).sharedMaterial) == worn,
                    $"and wears the same material ({AssetDatabase.GetAssetPath(Body(again).sharedMaterial)})");

                // Another avatar of the name still wearing the set keeps it.
                var mat = Body(again).sharedMaterial;
                var other = Fresh(roots, skin);
                YapsSquash.Build(other, out built);
                Check(built && Body(again).sharedMaterial == mat && AssetDatabase.GetAssetPath(mat) == worn, "a set another avatar wears is left to it");
                Check(AssetDatabase.GetAssetPath(Body(other).sharedMaterial) != worn, $"and the new build gets its own ({AssetDatabase.GetAssetPath(Body(other).sharedMaterial)})");
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                failures++;
            }
            finally
            {
                foreach (var r in roots) if (r != null) Object.DestroyImmediate(r.gameObject);
                AssetDatabase.DeleteAsset(Dir);
                GeneratedFiles.DeleteNew(generated);
            }
            Debug.Log(failures == 0 ? "[SquashPileTest] PASS" : $"[SquashPileTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static Renderer Body(Transform root) => root.Find("Body").GetComponent<Renderer>();

        // A head with a cheek each side, each a small ball of skin on its own bone, the cheeks
        // picked as the squash's pair.
        static Transform Fresh(List<Transform> roots, Material skin)
        {
            var root = new GameObject(Name).transform;
            roots.Add(root);
            var head = new GameObject("Head").transform;
            head.SetParent(root, false);
            head.localPosition = new Vector3(0f, 1.5f, 0f);
            var bones = new[] { head, Bone("Cheek_L", head, -0.04f), Bone("Cheek_R", head, 0.04f) };
            var smr = new GameObject("Body").AddComponent<SkinnedMeshRenderer>();
            smr.transform.SetParent(root, false);
            const int PerSide = 40;
            var verts = new Vector3[2 * PerSide];
            var weights = new BoneWeight[2 * PerSide];
            for (int v = 0; v < verts.Length; v++)
            {
                int side = v / PerSide, i = v % PerSide;
                float y = 1f - 2f * (i + 0.5f) / PerSide, r = Mathf.Sqrt(1f - y * y), a = i * 2.4f;
                verts[v] = bones[1 + side].position + 0.02f * new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a));
                weights[v] = new BoneWeight { boneIndex0 = 1 + side, weight0 = 1f };
            }
            var mesh = new Mesh { name = "Skin", vertices = verts, boneWeights = weights };
            mesh.triangles = Enumerable.Range(0, verts.Length / 3 * 3).ToArray();
            mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * smr.transform.localToWorldMatrix).ToArray();
            smr.bones = bones;
            smr.sharedMesh = mesh;
            smr.sharedMaterial = skin;
            root.gameObject.AddComponent<YapsSquashSettings>().partBones = new List<Transform> { bones[1], bones[2] };
            return root;
        }

        static Transform Bone(string name, Transform parent, float x)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = new Vector3(x, -0.03f, 0.06f);
            return t;
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[SquashPileTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
