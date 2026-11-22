#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    using MagicaCloth2;

    // A PhysBone's ignored bone must stay out of the cloth even when nothing else is left to move,
    // and a stand-in cloth must honour the ignores the source PhysBones carried. Modular Avatar
    // marks its hidden-skin bones that way, at zero offset from the bone they ride; simulated, they
    // tore the skin weighted to them.
    public static class IgnoredHelperBoneTest
    {
        const string Dir = "Assets/ZZ_IgnoredHelperBoneTest";
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Ignored helper bones stay out of the cloth")]
        public static void Run()
        {
            failures = 0;
            var avatar = new GameObject("IgnoredHelperAvatar");
            try
            {
                Transform Bone(string n, Transform parent, float y = 0.05f)
                {
                    var t = new GameObject(n).transform;
                    t.SetParent(parent, false);
                    t.localPosition = y == 0f ? Vector3.zero : new Vector3(0f, y, 0.03f);
                    return t;
                }
                var chest = Bone("Chest", avatar.transform, 1.2f);
                var outer = Bone("Breast", chest);
                var inner = Bone("Breast", outer);
                var buffer = Bone("NaNimatedBuffer", inner, 0f);   // exactly on its parent, as Modular Avatar makes it
                var nan = Bone("NaNimatedBone", buffer, 0f);
                var lower = Bone("Breast.2", outer);
                Bone("Breast.3", lower);

                // Skin on the hidden-skin bone and on the bone it rides.
                var skin = new GameObject("Body").AddComponent<SkinnedMeshRenderer>();
                skin.transform.SetParent(avatar.transform, false);
                var mesh = new Mesh { name = "Body" };
                mesh.vertices = new[] { inner.position + Vector3.forward * 0.02f, nan.position + Vector3.up * 0.02f, inner.position + Vector3.right * 0.02f };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 1, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f } };
                mesh.bindposes = new[] { inner.worldToLocalMatrix, nan.worldToLocalMatrix };
                skin.bones = new[] { inner, nan };
                skin.sharedMesh = mesh;
                var skinBefore = Skinned(skin);

                Directory();
                var ctx = new BridgeContext { Target = avatar, Report = new BridgeReport(), Settings = new BridgeSettings(), OutputDir = Dir };
                ctx.PhysBoneIgnores.Add(buffer);

                // 1. The PhysBone on the inner bone: its one child is ignored.
                var data = new PhysBoneChainData { Root = inner, SourceGameObject = inner.gameObject, Pull = 0.2f, Spring = 0.2f };
                data.Ignores.Add(buffer);
                var own = MagicaClothWriter.Write(ctx, data, new Dictionary<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider, ColliderComponent>());
                Check(own != null && KeptOut(own, buffer), "a chain whose only child is ignored keeps it out");
                Check(own != null && KeptOut(own, nan), "and everything below it");
                Check(own != null && !KeptOut(own, inner), "the bone it rides is still in the cloth");

                // 2. The stand-in for a helper rig on the outer bone, with no PhysBone of its own.
                var write = typeof(HelperRigCleanup).GetMethod("Write", BindingFlags.NonPublic | BindingFlags.Static);
                var feel = new BridgeContext.HelperRigChain { Root = outer, Pull = 0.2f, Spring = 0.5f };
                write.Invoke(null, new object[] { ctx, outer, feel });
                var standIn = avatar.GetComponentsInChildren<MagicaCloth>(true).FirstOrDefault(c => c != own);
                Check(standIn != null && KeptOut(standIn, buffer), "a stand-in keeps a source PhysBone's ignored bone out");
                Check(standIn != null && !KeptOut(standIn, inner) && !KeptOut(standIn, lower), "a stand-in still moves the rest of its chain");

                // 3. Stepping the bone off its parent must not move the skin.
                var skinAfter = Skinned(skin);
                Check(skinBefore.Zip(skinAfter, (a, b) => (a - b).magnitude).Max() < 1e-5f, "the skin stays where it was");
            }
            finally
            {
                Object.DestroyImmediate(avatar);
                AssetDatabase.DeleteAsset(Dir);
            }
            Debug.Log(failures == 0 ? "[IgnoredHelperBoneTest] PASS" : $"[IgnoredHelperBoneTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Directory()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", Dir.Substring("Assets/".Length));
        }

        static Vector3[] Skinned(SkinnedMeshRenderer r)
        {
            var m = r.sharedMesh;
            return m.vertices.Select((v, i) =>
            {
                int b = m.boneWeights[i].boneIndex0;
                return (r.bones[b].localToWorldMatrix * m.bindposes[b]).MultiplyPoint3x4(v);
            }).ToArray();
        }

        // MagicaCloth2 gives each bone the nearest selection point's mark, so a bone is out only when
        // every point within a millimetre of it is invalid. No selection at all means every bone moves.
        static bool KeptOut(MagicaCloth cloth, Transform t)
        {
            var sel = cloth.GetSerializeData2().selectionData;
            if (sel == null || sel.positions == null) return false;
            Vector3 at = cloth.ClothTransform.worldToLocalMatrix.MultiplyPoint3x4(t.position);
            var near = Enumerable.Range(0, sel.positions.Length).Where(i => ((Vector3) sel.positions[i] - at).sqrMagnitude < 1e-6f).ToList();
            return near.Count > 0 && near.All(i => sel.attributes[i].IsInvalid());
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[IgnoredHelperBoneTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
