// One avatar through the corpus's conversion settings and its toggle sweep, outside the
// corpus: for chasing a crash a run hit on one avatar without launching another run.
//
// Run: -executeMethod AvatarBridge.Regression.SweepCrashProbe.Run -probeScene "Assets/.../x.unity"
// (exits 0 when the sweep finishes; a native crash ends the process, which is the answer too)
#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    public static class SweepCrashProbe
    {
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string scenePath = args[Array.IndexOf(args, "-probeScene") + 1];
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var descriptor = scene.GetRootGameObjects().Select(r => r.GetComponentInChildren<VRCAvatarDescriptor>(true))
                .FirstOrDefault(d => d != null);
            for (var t = descriptor.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
            Environment.SetEnvironmentVariable("AVATARBRIDGE_YAPS", "1");
            var settings = (BridgeSettings)typeof(RegressionRunner)
                .GetMethod("CorpusSettings", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            var report = BridgeConverter.Convert(descriptor, settings);
            Debug.Log($"[SweepCrash] converted {descriptor.name}: {report.Entries.Count} report lines");
            int broken = ToggleSweep.Sweep(report.ConvertedRoot);
            Debug.Log($"[SweepCrash] sweep finished, {broken} broken");
            EditorApplication.Exit(0);
        }
    }
}
#endif
