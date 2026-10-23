#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // The squash component's two lists: bones picked by hand for parts no name search finds
    // (cheeks here), and materials the squash leaves alone, named directly or by the squashed
    // copy a renderer showed when it was dragged in.
    //
    // Run: -executeMethod AvatarBridge.Regression.SquashPartsTest.Run
    public static class SquashPartsTest
    {
        const string Dir = "Assets/SquashPartsTest";

        [MenuItem("Tools/AvatarBridge Dev/Test: squash picked bones and materials left alone")]
        public static void Run()
        {
            int failures = 0;
            void Check(bool ok, string what)
            {
                if (!ok) failures++;
                Debug.Log($"[SquashPartsTest] {(ok ? "ok   " : "WRONG")} {what}");
            }

            var roots = new List<Transform>();
            try
            {
                Directory.CreateDirectory(Dir);
                AssetDatabase.Refresh();
                var skin = Saved("Skin");
                var shirt = Saved("Shirt");
                var root = Avatar(roots, skin, shirt);
                var settings = root.gameObject.AddComponent<YapsSquashSettings>();
                var cheeks = new List<Transform> { root.Find("Head/Cheek_L"), root.Find("Head/Cheek_R") };
                Renderer Body() => root.Find("Body").GetComponent<Renderer>();
                Renderer Shirt() => root.Find("Shirt").GetComponent<Renderer>();

                string said = YapsSquash.Add(root, Dir + "/Out", out bool built);
                Check(!built && said.Contains("no soft pair found by name"), "cheeks are not found by name: " + said);

                settings.partBones = new List<Transform> { cheeks[0] };
                said = YapsSquash.Add(root, Dir + "/Out", out built);
                Check(!built && said.Contains("one is alone"), "one picked bone is refused: " + said);

                settings.partBones = cheeks;
                said = YapsSquash.Add(root, Dir + "/Out", out built);
                Debug.Log("[SquashPartsTest]   said: " + said);
                Check(built && said.Contains("from picked bones"), "two picked bones build a pair");
                Check(Body().sharedMaterial != skin && Body().sharedMaterial.HasProperty("_YAPS_DentOwn"), "the skin is squashed");
                var shirtCopy = Shirt().sharedMaterial;
                Check(shirtCopy != shirt && shirtCopy.HasProperty("_YAPS_DentOwn"), "and so is the shirt over it");
                Check(!ShaderUtil.ShaderHasError(Body().sharedMaterial.shader), "the squashed skin's shader compiles");
                // The client's first-person copy skips "[FPR]" renderers; without it the slot skin
                // went unskinned in the wearer's own view and the shader read a frozen buffer.
                var slotSkin = root.Find("YAPS squash (shader)")?.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Check(slotSkin != null && slotSkin.name.Contains("[FPR]"), $"the slot skin is kept off the first-person copy ({(slotSkin != null ? slotSkin.name : "none")})");
                // Squash copies carry the plug's YAPS properties, and the setup listed them as plugs.
                var scan = YapsScanner.Scan(root.gameObject);
                Check(scan.Plugs.Count == 0, $"the setup lists no squashed layer as a plug ({scan.Plugs.Count} listed)");

                // A stroke undone must leave the overlay too: its colours stayed, so it read as no undo.
                var painter = ScriptableObject.CreateInstance<YapsSquashPainter>();
                try
                {
                    Call(painter, "Pick", root.gameObject);
                    Call(painter, "AddPair");
                    Undo.IncrementCurrentGroup();
                    var w = (float[][])Field(painter, "_w");
                    for (int v = 0; v < w[0].Length; v++) w[0][v] = 1f;
                    Call(painter, "Recolour");
                    Call(painter, "Save", "Paint squash region");
                    float Red() => ((Color[])Field(painter, "_colours")).Max(c => c.r);
                    Check(Red() > 0.9f, "a stroke shows on the overlay");
                    Undo.PerformUndo();
                    Check(settings.pairs.Count == 1 && settings.pairs[0].a.All(x => x == 0), "undo takes the stroke out of the regions");
                    Check(Red() < 0.01f, $"and off the overlay (red {Red():0.00})");
                }
                finally { Object.DestroyImmediate(painter); }
                settings.pairs.Clear();
                settings.body = null;

                settings.leaveAlone = new List<Material> { shirt };
                said = YapsSquash.Add(root, Dir + "/Out", out built);
                Check(built && Shirt().sharedMaterial == shirt, "a material left alone keeps its own look");
                Check(Body().sharedMaterial != skin && Body().sharedMaterial.HasProperty("_YAPS_DentOwn"), "while the skin under it still squashes");

                // What the renderer showed when it was dragged in: the squashed copy.
                settings.leaveAlone = new List<Material>();
                YapsSquash.Add(root, Dir + "/Out", out built);
                settings.leaveAlone = new List<Material> { Shirt().sharedMaterial };
                said = YapsSquash.Add(root, Dir + "/Out", out built);
                Check(built && Shirt().sharedMaterial == shirt, "a squashed copy left alone leaves its original alone");
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
            }
            Debug.Log(failures == 0 ? "[SquashPartsTest] PASS" : $"[SquashPartsTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Private).Invoke(o, args);
        static object Field(object o, string name) => o.GetType().GetField(name, Private).GetValue(o);

        static Material Saved(string name)
        {
            var m = new Material(Shader.Find("Standard")) { name = name };
            AssetDatabase.CreateAsset(m, $"{Dir}/{name}.mat");
            return m;
        }

        // A head with a cheek each side, each a small ball of skin on its own bone, worn by
        // one renderer per material.
        static Transform Avatar(List<Transform> roots, params Material[] layers)
        {
            var root = new GameObject("SquashPartsAvatar").transform;
            roots.Add(root);
            var head = new GameObject("Head").transform;
            head.SetParent(root, false);
            head.localPosition = new Vector3(0f, 1.5f, 0f);
            var bones = new[] { head, Bone("Cheek_L", head, -0.04f), Bone("Cheek_R", head, 0.04f) };
            foreach (var layer in layers)
            {
                var smr = new GameObject(layer.name == "Skin" ? "Body" : layer.name).AddComponent<SkinnedMeshRenderer>();
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
                var mesh = new Mesh { name = layer.name, vertices = verts, boneWeights = weights };
                mesh.triangles = Enumerable.Range(0, verts.Length / 3 * 3).ToArray();
                mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * smr.transform.localToWorldMatrix).ToArray();
                smr.bones = bones;
                smr.sharedMesh = mesh;
                smr.sharedMaterial = layer;
            }
            return root;
        }

        static Transform Bone(string name, Transform parent, float x)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = new Vector3(x, -0.03f, 0.06f);
            return t;
        }
    }
}
#endif
