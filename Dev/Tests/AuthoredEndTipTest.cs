#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    using MagicaCloth2;

    // An authored leaf bone named like "Tail_End" still gets the Endpoint Position tip. Every leaf
    // ending in "_End" was read as a tip an earlier cloth wrote, so the chain lost its last segment.
    public static class AuthoredEndTipTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Authored _End leaf gets its tip")]
        public static void Run()
        {
            failures = 0;
            var tail = new GameObject("Tail").transform;
            try
            {
                var mid = new GameObject("Tail_01").transform;
                mid.SetParent(tail, false);
                var end = new GameObject("Tail_End").transform;
                end.SetParent(mid, false);

                var sdata = new ClothSerializeData();
                sdata.rootBones = new List<Transform> { tail };
                var data = new PhysBoneChainData { Root = tail, EndpointPosition = new Vector3(0f, 0.1f, 0f) };
                var synth = typeof(MagicaClothWriter).GetMethod("SynthesizeEndpointBones", BindingFlags.NonPublic | BindingFlags.Static);
                int added = (int) synth.Invoke(null, new object[] { sdata, data });
                Check(added == 1 && end.childCount == 1, $"the authored Tail_End gets a tip ({added} added)");

                // Run again, as a second cloth on the same root: the tip it wrote is not tipped again.
                int again = (int) synth.Invoke(null, new object[] { sdata, data });
                Check(again == 0, $"a second cloth adds no Tail_End_End ({again} added)");
            }
            finally
            {
                Object.DestroyImmediate(tail.gameObject);
            }
            Debug.Log(failures == 0 ? "[AuthoredEndTipTest] PASS" : $"[AuthoredEndTipTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[AuthoredEndTipTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
