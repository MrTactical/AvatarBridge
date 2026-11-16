#if CVR_CCK_EXISTS
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // The tester's "as others see it" copy starts from the values the avatar had before its
    // animations ran, not the wearer's animated ones: a remote client starts from the upload, and a
    // state that writes no curve keeps that value there.
    public static class TwinRestTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Tester copy starts at rest")]
        public static void Run()
        {
            failures = 0;
            var avatar = new GameObject("TwinRestAvatar");
            var clip = new AnimationClip();
            var controller = new AnimatorController();
            GameObject twin = null;
            try
            {
                var hat = new GameObject("Hat");
                hat.transform.SetParent(avatar.transform, false);
                hat.SetActive(false);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Hat", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0f, 0f, 1f));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Hat", typeof(Transform), "m_LocalPosition.y"), AnimationCurve.Constant(0f, 0f, 0.5f));
                controller.AddLayer("Base");
                controller.layers[0].stateMachine.AddState("On").motion = clip;
                var animator = avatar.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;

                var rest = CckAnimatorTester.RestOf(animator);
                Check(rest.Count == 2, $"both values read ({rest.Count})");
                // As the wearer's animations leave it.
                clip.SampleAnimation(avatar, 0f);
                twin = Object.Instantiate(avatar);
                Check(twin.transform.Find("Hat").gameObject.activeSelf, "the copy is made with the animated values");

                CckAnimatorTester.ApplyRest(twin.GetComponent<Animator>(), rest);
                var twinHat = twin.transform.Find("Hat");
                Check(!twinHat.gameObject.activeSelf, "the copy's hat is back off, as uploaded");
                Check(Mathf.Abs(twinHat.localPosition.y) < 1e-5f, "and back at its rest position");
                Check(avatar.transform.Find("Hat").gameObject.activeSelf, "the wearer is left as it was");
            }
            finally
            {
                Object.DestroyImmediate(avatar);
                if (twin != null) Object.DestroyImmediate(twin);
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(controller);
            }
            Debug.Log(failures == 0 ? "[TwinRestTest] PASS" : $"[TwinRestTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[TwinRestTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
