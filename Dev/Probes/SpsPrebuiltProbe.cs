// Does the "already carries VRCFury's own deform" warning fire on a built copy
// and stay silent on the original?
//
// The converter asks VRCFury not to patch plug shaders before it bakes, so a
// plug shader holding VRCFury's deform means the avatar was built already, or
// the switch is gone. This bakes the scene's avatar with VRCFury left alone,
// converts that built copy, then converts the original. Slimming stays off: a
// probe converting in a shared project must not rewrite its textures.
//
// It also lists the _SPS properties a finished bake leaves on the material,
// which says whether SPS still has a switch there.
//
//   Unity.exe -batchmode -projectPath "<project>" -probeScene "<scene with an SPS plug>"
//     -executeMethod AvatarBridge.Probes.SpsPrebuiltProbe.Run -quit
#if CVR_CCK_EXISTS && VRC_SDK_VRCSDK3 && AVATARBRIDGE_YAPS
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Probes
{
    public static class SpsPrebuiltProbe
    {
        static int fail;

        public static void Run()
        {
            fail = 0;
            try
            {
                var args = Environment.GetCommandLineArgs();
                string scene = args[Array.IndexOf(args, "-probeScene") + 1];

                EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
                var copy = UnityEngine.Object.Instantiate(Source().gameObject);
                var built = VRCFuryBaker.TryBake(copy.GetComponent<VRCAvatarDescriptor>(), new BridgeReport());
                Check(built != null, "VRCFury built the copy");
                var baked = built.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                    .Where(m => m != null && m.HasProperty("_SPS_Bake")).Distinct().ToList();
                Check(baked.Count > 0, $"{baked.Count} material(s) carry VRCFury's deform after the bake");
                foreach (var m in baked)
                {
                    var names = Enumerable.Range(0, m.shader.GetPropertyCount()).Select(m.shader.GetPropertyName)
                        .Where(n => n.StartsWith("_SPS", StringComparison.Ordinal));
                    Log($"     \"{m.name}\" on {m.shader.name}: {string.Join(", ", names)}");
                }

                var onBuilt = Warnings(BridgeConverter.Convert(built.GetComponent<VRCAvatarDescriptor>(), Settings()));
                Check(onBuilt.Length > 0 && onBuilt.Length == onBuilt.Distinct().Count(),
                    $"built copy: warned once per material ({onBuilt.Length}: {string.Join(", ", onBuilt)})");

                EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
                var report = BridgeConverter.Convert(Source(), Settings());
                Check(Warnings(report).Length == 0, "original: no warning");
                Check(report.Entries.Any(e => (e.Subject ?? "").StartsWith("Asked VRCFury not to patch", StringComparison.Ordinal)),
                    "original: VRCFury was asked not to patch");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                fail++;
            }
            Log(fail == 0 ? "PASS" : $"FAIL: {fail} check(s)");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        static VRCAvatarDescriptor Source() => UnityEngine.Object.FindObjectsOfType<VRCAvatarDescriptor>(true).First();

        static BridgeSettings Settings() => new BridgeSettings { slimTexturesOnConvert = false };

        static string[] Warnings(BridgeReport report) =>
            report.Entries.Where(e => e.Detail == YapsLegacyMap.SpsPrebuilt).Select(e => e.Subject).ToArray();

        static void Check(bool ok, string what)
        {
            if (!ok) fail++;
            Log((ok ? "ok   " : "FAIL ") + what);
        }

        static void Log(string s) => Debug.Log("[SpsPrebuiltProbe] " + s);
    }
}
#endif
