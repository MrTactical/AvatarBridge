#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    using MagicaCloth2;

    // A PhysBone's radius curve tapers the particles, read 0..1 like every other curve. A curve rising
    // above 1 pushed the widest particle past the radius cap, so neighbours overlapped.
    public static class RadiusCurveCapTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/A radius curve stays under the particle cap")]
        public static void Run()
        {
            failures = 0;
            var avatar = new GameObject("RadiusCurveAvatar");
            try
            {
                Transform parent = avatar.transform, root = null;
                for (int i = 0; i < 4; i++)
                {
                    var t = new GameObject("Tail" + i).transform;
                    t.SetParent(parent, false);
                    t.localPosition = new Vector3(0f, i == 0 ? 1f : 0.01f, 0f);   // 1 cm apart: a 5 mm cap
                    if (root == null) root = t;
                    parent = t;
                }
                var ctx = new BridgeContext { Target = avatar, Report = new BridgeReport(), Settings = new BridgeSettings { capParticleRadius = true }, OutputDir = "Assets" };
                var data = new PhysBoneChainData { Root = root, SourceGameObject = root.gameObject, Pull = 0.2f, Spring = 0.2f, Radius = 0.05f,
                    RadiusCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 2f) };
                var cloth = MagicaClothWriter.Write(ctx, data, new Dictionary<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider, ColliderComponent>());
                var radius = cloth.SerializeData.radius;
                float tip = radius.Evaluate(1f), at = radius.Evaluate(0f);
                Check(tip <= 0.005f + 1e-5f, $"the widest particle stays at the cap ({tip:0.#####} m, cap 0.005)");
                Check(Mathf.Abs(tip / at - 2f) < 0.01f, $"and the taper below 1 is kept (tip {tip:0.#####}, root {at:0.#####})");
            }
            finally
            {
                Object.DestroyImmediate(avatar);
            }
            Debug.Log(failures == 0 ? "[RadiusCurveCapTest] PASS" : $"[RadiusCurveCapTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[RadiusCurveCapTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
