#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Reflection;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A material whose properties an animation drives is not advised for an atlas. The curve's path
    // was compared with material names, which never match, so it was advised anyway.
    public static class AtlasAnimatedPropertyTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Atlas advice skips animated materials")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("AtlasAnimatedAvatar");
            var shader = Shader.Find("Standard");
            var a = new Material(shader) { name = "Skin" };
            var b = new Material(shader) { name = "Cloth" };
            var clip = new AnimationClip();
            var controller = new AnimatorController();
            try
            {
                var animator = root.AddComponent<Animator>();
                var avatar = root.AddComponent<CVRAvatar>();
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = "Body";
                body.transform.SetParent(root.transform, false);
                body.GetComponent<Renderer>().sharedMaterial = a;

                clip.SetCurve("Body", typeof(MeshRenderer), "material._Color.r", AnimationCurve.Constant(0f, 1f, 0.5f));
                controller.AddLayer("Base");
                controller.layers[0].stateMachine.AddState("Glow").motion = clip;
                animator.runtimeAnimatorController = controller;

                var reportType = typeof(AvatarWeight).GetNestedType("Report", BindingFlags.Public | BindingFlags.NonPublic);
                var report = System.Activator.CreateInstance(reportType, true);
                typeof(AvatarWeight).GetMethod("FindAtlasCandidates", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { avatar, new HashSet<Material> { a, b }, report });
                var atlas = (List<string>) reportType.GetField("Atlas").GetValue(report);
                Check(atlas.Count == 0, "the animated one is left out, so no pair is advised: " + string.Join("; ", atlas));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(controller);
            }
            Debug.Log(failures == 0 ? "[AtlasAnimatedPropertyTest] PASS" : $"[AtlasAnimatedPropertyTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[AtlasAnimatedPropertyTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
