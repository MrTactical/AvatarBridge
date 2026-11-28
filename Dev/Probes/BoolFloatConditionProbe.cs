// Does a Float condition fire on a parameter declared Bool? The accessories card keeps the avatar's
// type when the two differ, so an accessory's IsLocal Greater/Less conditions meet a Bool.
// Batch: -executeMethod AvatarBridge.Regression.BoolFloatConditionProbe.Run (exits itself).

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class BoolFloatConditionProbe
    {
        const string Path = "Assets/__BoolFloatProbe.controller";

        public static void Run()
        {
            try
            {
                // If and IfNot first: they must fire, or the harness is what fails.
                foreach (var (mode, value) in new[] { (AnimatorConditionMode.If, true), (AnimatorConditionMode.IfNot, false),
                             (AnimatorConditionMode.Greater, true), (AnimatorConditionMode.Less, false),
                             (AnimatorConditionMode.Greater, false), (AnimatorConditionMode.Less, true) })
                    Debug.Log($"[BoolFloat] {mode} 0.5 on a Bool set {value}: {(Fires(mode, value) ? "fires" : "does not fire")}");
                foreach (bool value in new[] { false, true })
                    Debug.Log($"[BoolFloat] a 1D blend tree on a Bool set {value} blends to {Blend(value):0.00} (0 = off clip, 1 = on clip)");
            }
            finally
            {
                AssetDatabase.DeleteAsset(Path);
                EditorApplication.Exit(0);
            }
        }

        // A tree from x = 0 at threshold 0 to x = 1 at threshold 1, read through a Bool parameter.
        static float Blend(bool value)
        {
            AssetDatabase.DeleteAsset(Path);
            var c = AnimatorController.CreateAnimatorControllerAtPath(Path);
            c.AddParameter("IsLocal", AnimatorControllerParameterType.Bool);
            AnimationClip Clip(float x)
            {
                var clip = new AnimationClip();
                clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0, 1, x));
                AssetDatabase.AddObjectToAsset(clip, c);
                return clip;
            }
            var tree = new BlendTree { blendType = BlendTreeType.Simple1D, blendParameter = "IsLocal", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, c);
            tree.AddChild(Clip(0), 0);
            tree.AddChild(Clip(1), 1);
            var state = c.layers[0].stateMachine.AddState("Tree");
            state.motion = tree;
            var go = new GameObject("BoolFloat blend probe");
            try
            {
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = c;
                animator.Rebind();
                animator.SetBool("IsLocal", value);
                for (int i = 0; i < 5; i++) animator.Update(0.1f);
                return go.transform.localPosition.x;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        static bool Fires(AnimatorConditionMode mode, bool value)
        {
            AssetDatabase.DeleteAsset(Path);
            var c = AnimatorController.CreateAnimatorControllerAtPath(Path);
            c.AddParameter("IsLocal", AnimatorControllerParameterType.Bool);
            var sm = c.layers[0].stateMachine;
            var from = sm.AddState("From");
            var to = sm.AddState("To");
            sm.defaultState = from;
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = 0;
            t.AddCondition(mode, 0.5f, "IsLocal");
            var go = new GameObject("BoolFloat probe");
            try
            {
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = c;
                animator.Rebind();
                animator.SetBool("IsLocal", value);
                for (int i = 0; i < 5; i++) animator.Update(0.1f);
                return animator.GetCurrentAnimatorStateInfo(0).IsName("To");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
#endif
