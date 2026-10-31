#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // The squash painter drew its regions, and aimed its brush, off the skin of a scaled avatar:
    // bigger and higher at a scale under 1, right at 1. Its bake and its drawing frame must put
    // every vertex where the skinning does, whatever scale the root or the body carries.
    public static class PainterScaleTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Squash painter on a scaled avatar")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[PainterScaleTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            fail += Check("root scaled 0.5", Case(0.5f, 1f));
            fail += Check("root scaled 1.6", Case(1.6f, 1f));
            fail += Check("body scaled 100 under a root of 0.01", Case(0.01f, 100f));
            fail += Check("unscaled", Case(1f, 1f));

            Debug.Log($"[PainterScaleTest] {(fail == 0 ? "PASS" : fail + " WRONG")}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        // One bone carries every vertex, so the skinned position is bone * bindpose * vertex,
        // worked out here without BakeMesh.
        static bool Case(float rootScale, float bodyScale)
        {
            var root = new GameObject("PainterScaleTest");
            var mesh = new Mesh();
            var baked = new Mesh();
            try
            {
                var body = new GameObject("Body").transform;
                body.SetParent(root.transform, false);
                body.localScale = Vector3.one * bodyScale;
                var bone = new GameObject("Chest").transform;
                bone.SetParent(root.transform, false);
                bone.localPosition = new Vector3(0f, 1.3f, 0.05f);
                bone.localRotation = Quaternion.Euler(10f, 20f, 0f);

                var smr = body.gameObject.AddComponent<SkinnedMeshRenderer>();
                mesh.vertices = new[] { new Vector3(0.1f, 1.3f, 0.1f), new Vector3(-0.1f, 1.4f, 0.1f), new Vector3(0f, 1.2f, 0.15f) };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.boneWeights = new[] { new BoneWeight { weight0 = 1f }, new BoneWeight { weight0 = 1f }, new BoneWeight { weight0 = 1f } };
                var bind = bone.worldToLocalMatrix * body.localToWorldMatrix;
                mesh.bindposes = new[] { bind };
                smr.sharedMesh = mesh;
                smr.bones = new[] { bone };
                smr.rootBone = bone;

                root.transform.localScale = Vector3.one * rootScale;
                root.transform.position = new Vector3(0.3f, 0f, -0.2f);

                var skin = bone.localToWorldMatrix * bind;
                var at = YapsSquashPainter.WorldPoints(smr, baked);
                var frame = YapsSquashPainter.Frame(smr);
                float worst = 0f;
                for (int v = 0; v < mesh.vertexCount; v++)
                {
                    var want = skin.MultiplyPoint3x4(mesh.vertices[v]);
                    worst = Mathf.Max(worst, (at[v] - want).magnitude, (frame.MultiplyPoint3x4(baked.vertices[v]) - want).magnitude);
                }
                Debug.Log($"[PainterScaleTest]   root {rootScale}, body {bodyScale}: worst {worst * 1000f:0.###} mm");
                return worst < 1e-4f;
            }
            catch (Exception e)
            {
                Debug.LogError($"[PainterScaleTest] {e}");
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(baked);
            }
        }
    }
}
#endif
