#if CVR_CCK_EXISTS
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // An avatar switched off in the scene is still the avatar: its shown renderers are live, and Slim
    // down must not strip them. One hidden under it that nothing switches on is still dead.
    public static class InactiveRootLiveTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Inactive avatar keeps its renderers live")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("InactiveRootAvatar");
            try
            {
                var c = new AnimatorController();
                c.AddLayer("Base");
                c.layers[0].stateMachine.AddState("Idle");
                root.AddComponent<Animator>().runtimeAnimatorController = c;
                var avatar = root.AddComponent<CVRAvatar>();

                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = "Body";
                body.transform.SetParent(root.transform, false);
                var hidden = GameObject.CreatePrimitive(PrimitiveType.Cube);
                hidden.name = "Hidden";
                hidden.transform.SetParent(root.transform, false);
                hidden.SetActive(false);
                root.SetActive(false);

                var weight = AvatarWeight.Measure(avatar, AvatarSurvey.Build(avatar));
                Check(!weight.Dead.Contains("Body"), "a shown renderer of a switched-off avatar is live");
                Check(weight.Dead.Contains("Hidden"), "one hidden under it that nothing switches is dead");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            Debug.Log(failures == 0 ? "[InactiveRootLiveTest] PASS" : $"[InactiveRootLiveTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[InactiveRootLiveTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
