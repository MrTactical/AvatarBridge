#if CVR_CCK_EXISTS
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A renderer only an Animator further down the avatar switches on is not
    // dead, so Fix it must not strip it. One nothing switches still is.
    public static class ChildAnimatorDeadTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Child Animator keeps a renderer live")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("ChildAnimatorAvatar");
            try
            {
                root.AddComponent<Animator>().runtimeAnimatorController = Controller(null);
                var avatar = root.AddComponent<CVRAvatar>();

                var prop = new GameObject("Prop");
                prop.transform.SetParent(root.transform, false);
                var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mesh.name = "Mesh";
                mesh.transform.SetParent(prop.transform, false);
                mesh.SetActive(false);
                var clip = new AnimationClip();
                clip.SetCurve("Mesh", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 1f, 1f));
                prop.AddComponent<Animator>().runtimeAnimatorController = Controller(clip);

                var other = GameObject.CreatePrimitive(PrimitiveType.Cube);
                other.name = "Other";
                other.transform.SetParent(root.transform, false);
                other.SetActive(false);

                var weight = AvatarWeight.Measure(avatar, AvatarSurvey.Build(avatar));
                Check(!weight.Dead.Contains("Prop/Mesh"), "a renderer the child Animator switches on is live");
                Check(weight.Dead.Contains("Other"), "one nothing switches on is still dead");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            Debug.Log(failures == 0 ? "[ChildAnimatorDeadTest] PASS" : $"[ChildAnimatorDeadTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static AnimatorController Controller(AnimationClip clip)
        {
            var c = new AnimatorController();
            c.AddLayer("Base");
            c.layers[0].stateMachine.AddState("On").motion = clip;
            return c;
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[ChildAnimatorDeadTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
