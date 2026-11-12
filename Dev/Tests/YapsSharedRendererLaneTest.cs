#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Two plugs baked onto one renderer keep their own animated values. Animation reaches every slot,
    // so both plugs read one shape weight, one bake scale and one tag chooser, and the second plug's
    // size slider grew the first.
    public static class YapsSharedRendererLaneTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Two plugs on one mesh keep their own values")]
        public static void Run()
        {
            failures = 0;
            var made = new List<Object>();
            GameObject plugA = null, plugB = null, body = null;
            try
            {
                plugA = YapsNativeBuilder.BuildTestPlug(select: false, length: 0.2f, name: "Lane Test A");
                plugB = YapsNativeBuilder.BuildTestPlug(select: false, length: 0.3f, name: "Lane Test B");
                var matA = Object.Instantiate(plugA.GetComponent<MeshRenderer>().sharedMaterial);
                var matB = Object.Instantiate(plugB.GetComponent<MeshRenderer>().sharedMaterial);
                made.Add(matA); made.Add(matB);
                Check(matA.HasProperty("_YAPS_PlugLane") && matA.shader != null && !ShaderUtil.ShaderHasError(matA.shader),
                    "the patched shader declares the lane and compiles");
                Check(matA.GetTexture("_YAPS_Bake") != matB.GetTexture("_YAPS_Bake"), "the two plugs have their own bakes");

                // Held weights as Apply leaves them, each in its own order.
                matA.SetFloat("_YAPS_ShapeCount", 2);
                matA.SetVector("_YAPS_ShapeWeights", new Vector4(0.3f, 0.4f, 0, 0));
                matB.SetFloat("_YAPS_ShapeCount", 1);
                matB.SetVector("_YAPS_ShapeWeights", new Vector4(0.7f, 0, 0, 0));

                body = new GameObject("LaneTestAvatar");
                body.AddComponent<Animator>();
                var avatar = body.AddComponent<CVRAvatar>();
                var mesh = new GameObject("Body");
                mesh.transform.SetParent(body.transform, false);
                mesh.AddComponent<MeshFilter>();
                var renderer = mesh.AddComponent<MeshRenderer>();

                // Alone, nothing changes.
                renderer.sharedMaterials = new[] { matA };
                var aloneBefore = matA.GetVector("_YAPS_ShapeWeights");
                Check(YapsBaker.ShareRenderer(renderer) == 0 && YapsBaker.LaneOf(matA) == 0
                      && matA.GetVector("_YAPS_ShapeWeights") == aloneBefore, "a lone plug is left as it is");

                renderer.sharedMaterials = new[] { matA, matB };
                Check(YapsBaker.ShareRenderer(renderer) == 0, "both fit");
                Check(YapsBaker.LaneOf(matA) == 0 && YapsBaker.LaneOf(matB) == 1, "the plug on the lower slot is lane 0, the other lane 1");
                foreach (var m in new[] { matA, matB })
                {
                    Check(m.GetVector("_YAPS_ShapeWeights") == new Vector4(0.3f, 0.4f, 0, 0)
                          && Mathf.Approximately(m.GetVector("_YAPS_ShapeWeights4").w, 0.7f),
                        $"{(m == matA ? "A" : "B")} carries both plugs' held weights, B's from the top");
                }
                YapsBaker.ShareRenderer(renderer);
                Check(Mathf.Approximately(matB.GetVector("_YAPS_ShapeWeights4").w, 0.7f) && matB.GetVector("_YAPS_ShapeWeights") == new Vector4(0.3f, 0.4f, 0, 0),
                    "sharing again keeps every weight where it is");

                // Size curves: A's slider drives A's slot only, B's B's.
                var sizeA = new AnimationClip(); var sizeB = new AnimationClip(); var both = new AnimationClip();
                made.Add(sizeA); made.Add(sizeB); made.Add(both);
                var ramp = AnimationCurve.Linear(0, 0, 1, 100);
                sizeA.SetCurve("Body", typeof(MeshRenderer), "blendShape.A_Len", ramp);
                sizeB.SetCurve("Body", typeof(MeshRenderer), "blendShape.B_Len", ramp);
                both.SetCurve("Body", typeof(MeshRenderer), "blendShape.Both", ramp);
                var clips = new[] { sizeA, sizeB, both };
                YapsCurveMirror.MirrorShapes(clips, "Body", renderer, new List<string> { "A_Len", "Both" }, null, null, 0);
                YapsCurveMirror.MirrorShapes(clips, "Body", renderer, new List<string> { "B_Len", "Both" }, null, null, 1,
                    new List<string> { "A_Len", "Both" });
                Check(Has(sizeA, "material._YAPS_ShapeWeights.x") && Bindings(sizeA).Count(b => b.StartsWith("material.")) == 1,
                    "A's slider writes A's slot alone");
                Check(Has(sizeB, "material._YAPS_ShapeWeights4.w") && !Has(sizeB, "material._YAPS_ShapeWeights.x"),
                    "B's slider writes B's slot from the top, not A's");
                Check(Has(both, "material._YAPS_ShapeWeights.y") && Has(both, "material._YAPS_ShapeWeights4.z"),
                    "a shape moving both writes each plug's own slot");

                // Bone scale: the second plug's own names.
                var bone = new GameObject("ShaftB").transform;
                bone.SetParent(body.transform, false);
                var scale = new AnimationClip();
                made.Add(scale);
                scale.SetCurve("ShaftB", typeof(Transform), "m_LocalScale.z", AnimationCurve.Linear(0, 1, 1, 2));
                YapsCurveMirror.MirrorBoneScale(new[] { scale }, new Dictionary<string, Transform> { { "ShaftB", bone } },
                    "Body", renderer, Quaternion.identity, 1);
                Check(Has(scale, "material._YAPS_BakeScale2") && !Has(scale, "material._YAPS_BakeScale"),
                    "B's size toggle writes B's scale, not A's");

                // A build from before lanes wrote B's slider onto A's slot: that copy goes, A's own stays.
                var legacy = new AnimationClip(); var aOwn = new AnimationClip();
                made.Add(legacy); made.Add(aOwn);
                legacy.SetCurve("Body", typeof(MeshRenderer), "blendShape.B_Len", ramp);
                legacy.SetCurve("Body", typeof(MeshRenderer), "material._YAPS_ShapeWeights.x", AnimationCurve.Linear(0, 0, 1, 1));
                aOwn.SetCurve("Body", typeof(MeshRenderer), "blendShape.A_Len", AnimationCurve.Linear(0, 0, 1, 50));
                aOwn.SetCurve("Body", typeof(MeshRenderer), "material._YAPS_ShapeWeights.x", AnimationCurve.Linear(0, 0, 1, 0.5f));
                YapsCurveMirror.MirrorShapes(new[] { legacy, aOwn }, "Body", renderer, new List<string> { "B_Len" }, null, null, 1,
                    new List<string> { "A_Len", "Both" });
                Check(!Has(legacy, "material._YAPS_ShapeWeights.x") && Has(legacy, "material._YAPS_ShapeWeights4.w"),
                    "B's old copy on A's slot is taken out");
                Check(Has(aOwn, "material._YAPS_ShapeWeights.x"), "A's own curve stays");

                // Tag choosers: one each, the first keeping today's key.
                var ownerA = new GameObject("PlugA").AddComponent<YapsPlug>();
                ownerA.transform.SetParent(body.transform, false);
                ownerA.renderer = renderer; ownerA.materialSlot = 0;
                var ownerB = new GameObject("PlugB").AddComponent<YapsPlug>();
                ownerB.transform.SetParent(body.transform, false);
                ownerB.renderer = renderer; ownerB.materialSlot = 1;
                string keyA = YapsTagMenu.Parameter(avatar, ownerA), keyB = YapsTagMenu.Parameter(avatar, ownerB);
                string today = "YAPS/Tags/" + ("Body".GetHashCode() & 0x7FFFFF).ToString("X6");
                Check(keyA == today, $"the first plug's chooser keeps its key ({keyA})");
                Check(keyB != keyA, $"the second plug gets a chooser of its own ({keyB})");
            }
            finally
            {
                foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
                if (body != null) Object.DestroyImmediate(body);
                if (plugA != null) Object.DestroyImmediate(plugA);
                if (plugB != null) Object.DestroyImmediate(plugB);
                foreach (var n in new[] { "Lane Test A", "Lane Test B" })
                {
                    AssetDatabase.DeleteAsset(YapsNativeBuilder.OutputRoot + "/" + n);
                    AssetDatabase.DeleteAsset(YapsNativeBuilder.OutputRoot + "/Test Plug/" + n + " Mesh.asset");
                }
            }
            Debug.Log(failures == 0 ? "[YapsSharedRendererLaneTest] PASS" : $"[YapsSharedRendererLaneTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static List<string> Bindings(AnimationClip c) => AnimationUtility.GetCurveBindings(c).Select(b => b.propertyName).ToList();
        static bool Has(AnimationClip c, string property) => Bindings(c).Contains(property);

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[YapsSharedRendererLaneTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
