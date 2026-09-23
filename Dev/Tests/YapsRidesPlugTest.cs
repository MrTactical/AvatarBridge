#if CVR_CCK_EXISTS
using System;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Known-answer test for which meshes join a plug.
    //
    // A two-bone chain, Base then Shaft, beside an unrelated Hips. The body
    // meets the plug at Base and nowhere further; a harness rides Shaft with
    // most of its vertices elsewhere; a tip sits wholly on Base. Before, the
    // converter took only a mesh more than half on the chain, so the harness
    // stayed rigid while the shaft bent through it.
    public static class YapsRidesPlugTest
    {
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[YapsRidesPlugTest] {(ok ? "ok  " : "FAIL")} {label}");
                return ok ? 0 : 1;
            }

            GameObject root = null;
            try
            {
                root = new GameObject("__YapsRidesPlugTest");
                var hips = new GameObject("Hips").transform;
                hips.SetParent(root.transform, false);
                var plugBase = new GameObject("Base").transform;
                plugBase.SetParent(hips, false);
                var shaft = new GameObject("Shaft").transform;
                shaft.SetParent(plugBase, false);
                var bones = new[] { hips, plugBase, shaft };

                // count vertices; the first `on` get `weights`, the rest sit on Hips.
                SkinnedMeshRenderer Make(string name, int count, int on, Func<BoneWeight> weights)
                {
                    var mesh = new Mesh { name = name, vertices = new Vector3[count] };
                    var all = new BoneWeight[count];
                    for (int i = 0; i < count; i++)
                        all[i] = i < on ? weights() : new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                    mesh.boneWeights = all;
                    var go = new GameObject(name);
                    go.transform.SetParent(root.transform, false);
                    var skin = go.AddComponent<SkinnedMeshRenderer>();
                    skin.sharedMesh = mesh;
                    skin.bones = bones;
                    return skin;
                }
                BoneWeight On(int bone) => new BoneWeight { boneIndex0 = bone, weight0 = 1f };

                fail += Check("a body meeting the plug at its root stays out",
                    !YapsBaker.RidesPlug(Make("Body", 100, 5, () => On(1)), plugBase));
                fail += Check("...even with a trace of the shaft bone",
                    !YapsBaker.RidesPlug(Make("BodyTrace", 100, 5, () => new BoneWeight
                        { boneIndex0 = 0, weight0 = 0.9f, boneIndex1 = 2, weight1 = 0.1f }), plugBase));
                fail += Check("a harness riding the shaft joins, though most of it is elsewhere",
                    YapsBaker.RidesPlug(Make("Harness", 100, 20, () => On(2)), plugBase));
                fail += Check("a tip wholly on the root joins",
                    YapsBaker.RidesPlug(Make("Tip", 10, 10, () => On(1)), plugBase));
                fail += Check("a mesh off the chain stays out",
                    !YapsBaker.RidesPlug(Make("Hat", 10, 0, () => On(0)), plugBase));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                fail++;
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }

            Debug.Log(fail == 0
                ? "[YapsRidesPlugTest] PASS: accessories join the plug, the body does not."
                : $"[YapsRidesPlugTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
