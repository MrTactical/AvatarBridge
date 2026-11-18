#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    // The weight audit's live blendshapes on one converted avatar: the shapes ChilloutVR drives
    // itself (visemes, blink, face tracking) count as live, so the "nothing animates" figure leaves
    // them out. The scene is not saved.
    //   -executeMethod AvatarBridge.Regression.DrivenShapesProbe.Run -abScene <path> -shapesOut <file>
    public static class DrivenShapesProbe
    {
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string k) => args[Array.IndexOf(args, k) + 1];
            string line;
            int code = 1;
            try
            {
                var scene = EditorSceneManager.OpenScene(Arg("-abScene"), OpenSceneMode.Single);
                var source = scene.GetRootGameObjects().First(r => r.GetComponent<VRCAvatarDescriptor>() != null);
                var settings = (BridgeSettings) AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("AvatarBridge.Regression.RegressionRunner")).First(t => t != null)
                    .GetMethod("CorpusSettings", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    .Invoke(null, null);
                var report = BridgeConverter.Convert(source.GetComponentInChildren<VRCAvatarDescriptor>(true), settings);
                var cvr = report.ConvertedRoot.GetComponent<CVRAvatar>();
                var ft = cvr.GetComponentInChildren<CVRFaceTracking>(true);
                int driven = (cvr.visemeBlendshapes ?? new string[0]).Concat(cvr.blinkBlendshape ?? new string[0]).Count(n => !string.IsNullOrEmpty(n) && n != "-none-")
                             + (ft != null && ft.UseFacialTracking ? ft.FaceBlendShapes.Count(n => !string.IsNullOrEmpty(n) && n != "-none-") : 0);
                var weight = AvatarWeight.Measure(cvr);
                bool pass = weight.AnimatedBlendShapes > 0 && weight.AnimatedBlendShapes <= weight.BlendShapes;
                line = $"[DrivenShapes] {source.name}: {weight.BlendShapes} blendshapes, {weight.AnimatedBlendShapes} live with no animator read, {driven} slots ChilloutVR drives: {(pass ? "PASS" : "FAIL")}";
                code = pass ? 0 : 2;
            }
            catch (Exception e) { line = "[DrivenShapes] threw: " + e; }
            File.WriteAllText(Arg("-shapesOut"), line);
            Debug.Log(line);
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
#endif
