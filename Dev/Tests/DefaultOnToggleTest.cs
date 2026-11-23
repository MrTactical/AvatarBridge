#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A Modular Avatar toggle that defaults on. MA leaves the object in its switched look in the scene
    // and writes the real rest in a base layer below; the toggle's tree has the switch on one side and
    // nothing on the other. Balancing the tree from the scene copied the switched look into the empty
    // side, so both sides wrote the same value and the toggle did nothing.
    public static class DefaultOnToggleTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/A default-on toggle keeps its empty side")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[DefaultOnToggleTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            const string dir = "Assets/__DefaultOnToggleTest";
            AssetDatabase.DeleteAsset(dir);
            AssetDatabase.CreateFolder("Assets", "__DefaultOnToggleTest");
            GameObject avatar = null;
            try
            {
                avatar = new GameObject("__DefaultOnToggleTest");
                var bra = new GameObject("Bra");
                bra.transform.SetParent(avatar.transform, false);
                bra.SetActive(false);   // the default-on toggle hides it, and MA leaves it hidden
                var lone = new GameObject("Lone");
                lone.transform.SetParent(avatar.transform, false);
                lone.SetActive(true);   // a switch nothing lower writes: balancing still fills it
                var cvr = avatar.AddComponent<CVRAvatar>();
                cvr.avatarSettings = new CVRAdvancedAvatarSettings
                {
                    settings = new List<CVRAdvancedSettingsEntry>
                    {
                        new CVRAdvancedSettingsEntry { name = "Bra", machineName = "BraToggle", type = CVRAdvancedSettingsEntry.SettingsType.Slider },
                    },
                };

                var controller = AnimatorController.CreateAnimatorControllerAtPath(dir + "/test.controller");
                controller.AddParameter("BraToggle", AnimatorControllerParameterType.Float);
                var bindBra = EditorCurveBinding.FloatCurve("Bra", typeof(GameObject), "m_IsActive");
                var bindLone = EditorCurveBinding.FloatCurve("Lone", typeof(GameObject), "m_IsActive");
                AnimationClip Clip(string name, params (EditorCurveBinding b, float v)[] curves)
                {
                    var c = new AnimationClip { name = name };
                    foreach (var (b, v) in curves) AnimationUtility.SetEditorCurve(c, b, AnimationCurve.Constant(0f, 1f / 60f, v));
                    AssetDatabase.AddObjectToAsset(c, controller);
                    return c;
                }

                // Layer 0: MA's base, the real rest (Bra shown).
                var baseLayer = controller.layers[0];
                baseLayer.name = "MA/RC Base";
                var rest = baseLayer.stateMachine.AddState("Base");
                rest.motion = Clip("Base", (bindBra, 1f));
                baseLayer.stateMachine.defaultState = rest;

                // Layer 1: the toggle, Bra hidden at 1, nothing at 0; Lone at 1 only.
                var apply = new AnimatorControllerLayer { name = "MA/RC Apply", defaultWeight = 1f,
                    stateMachine = new AnimatorStateMachine { name = "MA/RC Apply", hideFlags = HideFlags.HideInHierarchy } };
                AssetDatabase.AddObjectToAsset(apply.stateMachine, controller);
                var tree = new BlendTree { name = "Toggle", blendType = BlendTreeType.Simple1D, blendParameter = "BraToggle", useAutomaticThresholds = false };
                AssetDatabase.AddObjectToAsset(tree, controller);
                tree.AddChild(Clip("Empty"), 0f);
                tree.AddChild(Clip("Effect", (bindBra, 0f), (bindLone, 0f)), 1f);
                var state = apply.stateMachine.AddState("Apply");
                state.motion = tree;
                apply.stateMachine.defaultState = state;
                controller.layers = new[] { baseLayer, apply };

                var ctx = new BridgeContext
                {
                    Target = avatar,
                    Report = new BridgeReport(),
                    Settings = new BridgeSettings(),
                    MergedController = controller,
                    CvrAvatar = cvr,
                };
                AnimatorMerger.BalanceMenuTreeActivesForTest(controller, ctx);

                var empty = (AnimationClip)((BlendTree)controller.layers[1].stateMachine.defaultState.motion).children[0].motion;
                var bindings = AnimationUtility.GetCurveBindings(empty);
                fail += Check("the empty side leaves Bra to the base layer",
                    !bindings.Contains(bindBra));
                var loneCurve = AnimationUtility.GetEditorCurve(empty, bindLone);
                fail += Check("a switch nothing lower writes is still balanced to its scene rest",
                    loneCurve != null && Mathf.Approximately(loneCurve.Evaluate(0f), 1f));
            }
            finally
            {
                if (avatar != null) Object.DestroyImmediate(avatar);
                AssetDatabase.DeleteAsset(dir);
            }

            Debug.Log(fail == 0
                ? "[DefaultOnToggleTest] PASS: a default-on toggle still switches."
                : $"[DefaultOnToggleTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
