#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Poiyomi's Rename Animated puts a suffix on a property when it locks, "_Color_Skin0", and
    // the author's clips bind that name. The suffix is the material's NAME unless a tag pins it,
    // and a YAPS copy is saved under a name of its own, so a copy of a material the author had
    // locked got a new suffix: the next unlock lost the renamed values and the next lock moved
    // the property off every clip. A copy keeps its source's suffix. Needs Poiyomi; it locks.
    //
    // Run: -executeMethod AvatarBridge.Regression.RenameSuffixTest.Run
    public static class RenameSuffixTest
    {
        const string Dir = "Assets/RenameSuffixTest";

        [MenuItem("Tools/AvatarBridge Dev/Test: Poiyomi rename suffix on YAPS copies")]
        public static void Run()
        {
            int failures = 0;
            void Check(bool ok, string what)
            {
                if (!ok) failures++;
                Debug.Log($"[RenameSuffixTest] {(ok ? "ok   " : "WRONG")} {what}");
            }

            var poiyomi = Shader.Find(".poiyomi/Poiyomi Toon");
            var optimizer = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Thry.ThryEditor.ShaderOptimizer", false) ?? a.GetType("Thry.ShaderOptimizer", false))
                .FirstOrDefault(t => t != null);
            var suffixOf = optimizer?.GetMethod("GetRenamedPropertySuffix", BindingFlags.Public | BindingFlags.Static);
            var lockCall = YapsLock.LockCall(out _);
            if (poiyomi == null || YapsLock.ThryLocked(poiyomi) != false || suffixOf == null || lockCall == null)
            {
                Debug.Log("[RenameSuffixTest] SKIP: no unlocked Poiyomi Toon with its lock in the project");
                if (Application.isBatchMode) EditorApplication.Exit(0);
                return;
            }
            string Suffix(Material m) => (string)suffixOf.Invoke(null, new object[] { m });

            var made = new List<string>();
            var roots = new List<GameObject>();
            try
            {
                foreach (bool lockFirst in new[] { false, true })
                {
                    string how = lockFirst ? "locked" : "unlocked";
                    string dir = $"{Dir}/{how}";
                    Directory.CreateDirectory(dir);
                    AssetDatabase.Refresh();
                    var source = new Material(poiyomi) { name = "Skin 0" };
                    source.SetColor("_Color", new Color(0.2f, 0.4f, 0.6f, 1f));
                    source.SetOverrideTag("_ColorAnimated", "2");
                    AssetDatabase.CreateAsset(source, dir + "/Skin 0.mat");
                    if (lockFirst)
                    {
                        lockCall(new List<Material> { source });
                        made.Add(Path.GetDirectoryName(AssetDatabase.GetAssetPath(source.shader)).Replace('\\', '/'));
                        Check(YapsLock.ThryLocked(source.shader) == true && source.HasProperty("_Color_Skin0"),
                            "the author's lock renamed _Color to _Color_Skin0");
                    }
                    var basis = YapsLock.Basis(source);

                    // The squash's copy of the slot.
                    var root = Avatar(source);
                    roots.Add(root.gameObject);
                    string said = YapsSquash.Add(root, dir + "/Out", out bool built);
                    var copy = root.Find("Body").GetComponent<SkinnedMeshRenderer>().sharedMaterial;
                    Check(built && copy != source, $"{how}: the squash copied the slot ({said})");
                    Check(copy.name != source.name, $"{how}: under a name of its own (\"{copy.name}\")");
                    Check(copy.HasProperty("_Color_Skin0"), $"{how}: the copy's shader keeps the renamed property");
                    Check(Suffix(copy) == "Skin0", $"{how}: the squash copy's suffix is the source's (\"{Suffix(copy)}\")");

                    // The bake's copy.
                    var patched = YapsShaderPatcher.Patch(source, null, out string refusal, out _);
                    var baked = patched != null ? YapsBaker.Generated(source, patched, dir + "/Out/Skin 0 baked.mat") : null;
                    Check(baked != null && Suffix(baked) == "Skin0",
                        $"{how}: the bake copy's suffix is the source's (\"{(baked != null ? Suffix(baked) : refusal)}\")");

                    var stand = basis != source ? AssetDatabase.GetAssetPath(basis) : null;
                    if (stand != null)
                    {
                        made.Add(stand);
                        made.Add(Path.GetDirectoryName(AssetDatabase.GetAssetPath(basis.shader)).Replace('\\', '/'));
                    }
                    if (patched != null) made.Add(AssetDatabase.GetAssetPath(patched));
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                failures++;
            }
            finally
            {
                foreach (var root in roots) Object.DestroyImmediate(root);
                foreach (string path in made.Where(p => !string.IsNullOrEmpty(p)).Distinct()) AssetDatabase.DeleteAsset(path);
                AssetDatabase.DeleteAsset(Dir);
            }
            Debug.Log(failures == 0 ? "[RenameSuffixTest] PASS" : $"[RenameSuffixTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        // A chest with a pair on it, each part a small ball of skin on its own bone.
        static Transform Avatar(Material material)
        {
            var root = new GameObject("RenameSuffixAvatar").transform;
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
            var mesh = new Mesh { name = "RenameSuffix", vertices = verts, boneWeights = weights };
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
