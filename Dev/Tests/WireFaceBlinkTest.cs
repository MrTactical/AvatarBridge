#if CVR_CCK_EXISTS
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // The Toolkit's Wire face blinks as the converter does: half a pair is a one-eye warning, and
    // an avatar whose own animator blinks keeps native blink off.
    // Batch: -executeMethod AvatarBridge.Regression.WireFaceBlinkTest.Run
    public static class WireFaceBlinkTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Wire face blink")]
        public static void Run()
        {
            failures = 0;
            GameObject half = null, driven = null;
            try
            {
                half = Avatar("WireFaceHalf", "Blink_L");
                var report = CvrSetup.WireFace(half, new BridgeSettings());
                var cvr = half.GetComponent<CVRAvatar>();
                Check(cvr.useBlinkBlendshapes && cvr.blinkBlendshape[0] == "Blink_L", "half a pair is wired");
                Check(report.Entries.Any(e => e.Subject == "Blink blendshape" && e.Status == ReportStatus.Warning),
                    "and warned about, not reported as wired");

                driven = Avatar("WireFaceDriven", "Blink_L", "Blink_R");
                var clip = new AnimationClip { name = "Blinking" };
                clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blink_L",
                    new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.1f, 100), new Keyframe(0.2f, 0)));
                var controller = new AnimatorController();
                controller.AddLayer("Base");
                controller.AddMotion(clip);
                driven.GetComponent<CVRAvatar>().overrides = new AnimatorOverrideController(controller);
                report = CvrSetup.WireFace(driven, new BridgeSettings());
                cvr = driven.GetComponent<CVRAvatar>();
                Check(!cvr.useBlinkBlendshapes, "an avatar that blinks in its animator keeps native blink off");
                Check(report.Entries.Any(e => e.Subject == "Native blink left off"), "and says why");
            }
            catch (System.Exception e)
            {
                failures++;
                Debug.LogError("[WireFaceBlinkTest] threw: " + e);
            }
            finally
            {
                if (half != null) Object.DestroyImmediate(half);
                if (driven != null) Object.DestroyImmediate(driven);
            }
            Debug.Log(failures == 0 ? "[WireFaceBlinkTest] PASS" : $"[WireFaceBlinkTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static GameObject Avatar(string name, params string[] shapes)
        {
            var root = new GameObject(name);
            root.AddComponent<Animator>();
            root.AddComponent<CVRAvatar>();
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            foreach (var s in shapes.Concat(new[] { "vrc.v_aa", "vrc.v_oh" }))
                mesh.AddBlendShapeFrame(s, 100, new Vector3[3], null, null);
            body.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
            return root;
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[WireFaceBlinkTest] {(ok ? "ok  " : "FAIL")}  {what}");
        }
    }
}
#endif
