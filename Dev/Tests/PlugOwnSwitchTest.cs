#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Two plugs on one mesh each get their own deform toggle. The switch was one renderer-wide
    // property, so the second plug read the first's toggle as already switching it and got none,
    // and the one toggle switched both.
    public static class PlugOwnSwitchTest
    {
        static int failures;
        const string Name = "PlugOwnSwitchAvatar";

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Two plugs on one mesh switch alone")]
        public static void Run()
        {
            failures = 0;
            GameObject plugA = null, plugB = null, body = null;
            Material matA = null, matB = null;
            try
            {
                plugA = YapsNativeBuilder.BuildTestPlug(select: false, name: "Switch Test A");
                plugB = YapsNativeBuilder.BuildTestPlug(select: false, name: "Switch Test B");
                matA = Object.Instantiate(plugA.GetComponent<MeshRenderer>().sharedMaterial);
                matB = Object.Instantiate(plugB.GetComponent<MeshRenderer>().sharedMaterial);
                Check(matB.HasProperty("_YAPS_Enabled2") && !ShaderUtil.ShaderHasError(matB.shader),
                    "the patched shader declares the second plug's switch and compiles");

                body = new GameObject(Name);
                body.AddComponent<Animator>();
                var avatar = body.AddComponent<CVRAvatar>();
                var mesh = new GameObject("Body");
                mesh.transform.SetParent(body.transform, false);
                mesh.AddComponent<MeshFilter>();
                var renderer = mesh.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new[] { matA, matB };
                matB.SetFloat("_YAPS_Enabled", 0f);
                YapsBaker.ShareRenderer(renderer);
                Check(matB.GetFloat("_YAPS_Enabled2") == 0f && matA.GetFloat("_YAPS_Enabled2") == 1f,
                    "each plug's second-lane copy starts from its own switch");

                var a = new GameObject("PlugA").AddComponent<YapsPlug>();
                a.transform.SetParent(body.transform, false);
                a.renderer = renderer; a.materialSlot = 0;
                var b = new GameObject("PlugB").AddComponent<YapsPlug>();
                b.transform.SetParent(body.transform, false);
                b.renderer = renderer; b.materialSlot = 1;

                for (int pass = 0; pass < 2; pass++)
                {
                    Debug.Log("[PlugOwnSwitchTest] " + YapsToggles.EnsurePlugToggle(a, avatar, matA, YapsToggles.MenuLabelFor(a)));
                    Debug.Log("[PlugOwnSwitchTest] " + YapsToggles.EnsurePlugToggle(b, avatar, matB, YapsToggles.MenuLabelFor(b)));
                }
                var entries = avatar.avatarSettings?.settings?.Where(e => e.toggleSettings?.animationClip != null).ToList();
                Check(entries != null && entries.Count == 2, $"two toggles, built twice ({entries?.Count ?? 0})");
                if (entries != null && entries.Count == 2)
                {
                    var props = entries.Select(e => string.Join(",", AnimationUtility.GetCurveBindings(e.toggleSettings.animationClip)
                        .Where(x => x.path == "Body").Select(x => x.propertyName))).ToList();
                    Check(props.Contains("material._YAPS_Enabled") && props.Contains("material._YAPS_Enabled2"),
                        $"the first writes its switch, the second its own ({string.Join(" | ", props)})");
                    Check(entries[0].name != entries[1].name, $"each row is named for its plug ({entries[0].name} / {entries[1].name})");
                }
            }
            finally
            {
                if (body != null) Object.DestroyImmediate(body);
                if (plugA != null) Object.DestroyImmediate(plugA);
                if (plugB != null) Object.DestroyImmediate(plugB);
                if (matA != null) Object.DestroyImmediate(matA);
                if (matB != null) Object.DestroyImmediate(matB);
                AssetDatabase.DeleteAsset(YapsNativeBuilder.OutputRoot + "/" + Name);
                foreach (var n in new[] { "Switch Test A", "Switch Test B" })
                {
                    AssetDatabase.DeleteAsset(YapsNativeBuilder.OutputRoot + "/" + n);
                    AssetDatabase.DeleteAsset(YapsNativeBuilder.OutputRoot + "/Test Plug/" + n + " Mesh.asset");
                }
            }
            Debug.Log(failures == 0 ? "[PlugOwnSwitchTest] PASS" : $"[PlugOwnSwitchTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[PlugOwnSwitchTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
