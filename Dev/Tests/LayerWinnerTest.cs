#if CVR_CCK_EXISTS
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Proves the survey's layer rules in both directions: a layer that really can
    // never show is caught, and each thing that lets a lower layer through is not.
    // Controllers are built in memory with known answers.
    public static class LayerWinnerTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Layer winners")]
        public static void Run()
        {
            failures = 0;

            Expect("toggle under a layer writing it in both states", hidden: true, build: (c, top) =>
            {
                Two(top.stateMachine, Clip(0f), Clip(1f));
            });
            Expect("upper layer writes it in one state of two", hidden: false, build: (c, top) =>
            {
                Two(top.stateMachine, null, Clip(1f));
            });
            Expect("upper layer at half strength", hidden: false, weight: 0.5f, build: (c, top) =>
            {
                Two(top.stateMachine, Clip(0f), Clip(1f));
            });
            Expect("upper layer additive", hidden: false, mode: AnimatorLayerBlendingMode.Additive, build: (c, top) =>
            {
                Two(top.stateMachine, Clip(0f), Clip(1f));
            });
            Expect("upper layer at weight 0", hidden: false, weight: 0f, build: (c, top) =>
            {
                Two(top.stateMachine, Clip(0f), Clip(1f));
            });
            Expect("upper layer's mask leaves the object out", hidden: false, mask: Mask("Other"), build: (c, top) =>
            {
                Two(top.stateMachine, Clip(0f), Clip(1f));
            });
            Expect("upper layer's mask lists the object", hidden: true, mask: Mask("Obj"), build: (c, top) =>
            {
                Two(top.stateMachine, Clip(0f), Clip(1f));
            });
            Expect("masked upper layer swapping materials", hidden: false, mask: Mask(), swaps: true, build: (c, top) =>
            {
                Two(top.stateMachine, Swap(), Swap());
            });
            Expect("the upper state that lets it through cannot be reached", hidden: true, build: (c, top) =>
            {
                var machine = top.stateMachine;
                machine.AddState("Always").motion = Clip(1f);
                Contradiction(machine.AddEntryTransition(machine.AddState("Never")));
            });

            // States nothing leads to.
            var lone = Controller();
            var layer = Layer(lone, "Parked");
            var parkedMachine = layer.stateMachine;
            parkedMachine.AddState("Idle");
            var parked = parkedMachine.AddState("Hidden");
            parked.motion = Clip(1f, "Only");
            Contradiction(parkedMachine.AddEntryTransition(parked));
            var model = AvatarSurvey.ReadForTest(lone);
            Check(model.Findings.Any(f => f.Kind == "state that can never play" && f.Subject.Contains("Hidden")),
                "a contradictory entry to the only state animating something is reported");

            var library = Controller();
            var shelf = Layer(library, "Library").stateMachine;
            for (int i = 0; i < 3; i++) shelf.AddState("Pose" + i).motion = Clip(1f, "Pose" + i);
            model = AvatarSurvey.ReadForTest(library);
            Check(!model.Findings.Any(f => f.Kind == "state that can never play"), "a layer with no transitions reports nothing");
            Check(model.Layers.First(l => l.Name == "Library").Bindings.Count == 3,
                "every state's bindings still count for the weight and the slimmer");

            var nested = Controller();
            var outer = Layer(nested, "Nested").stateMachine;
            outer.AddState("Idle");
            var sub = outer.AddStateMachine("Inner");
            sub.AddState("Deep").motion = Clip(1f, "Deep");
            model = AvatarSurvey.ReadForTest(nested);
            Check(!model.Findings.Any(f => f.Kind == "state that can never play"), "a layer with a sub-machine reports nothing");

            Debug.Log(failures == 0 ? "[LayerWinnerTest] PASS" : $"[LayerWinnerTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        // A toggle layer "L" under the layer "W" that build fills.
        static void Expect(string label, bool hidden, Action<AnimatorController, AnimatorControllerLayer> build,
            float weight = 1f, AnimatorLayerBlendingMode mode = AnimatorLayerBlendingMode.Override, AvatarMask mask = null,
            bool swaps = false)
        {
            var c = Controller();
            var lower = Layer(c, "L");
            var toggle = Two(lower.stateMachine, swaps ? Swap() : Clip(0f), swaps ? Swap() : Clip(1f));
            var top = Layer(c, "W", weight, mode, mask);
            build(c, top);
            var model = AvatarSurvey.ReadForTest(c);
            bool got = model.Findings.Any(f => f.Kind == "hidden by a layer above" && f.Subject == "L");
            Check(got == hidden, $"{label}: {(hidden ? "hidden" : "not hidden")}");
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[LayerWinnerTest] {(ok ? "ok   " : "WRONG")} {what}");
        }

        static AnimatorController Controller()
        {
            var c = new AnimatorController { name = "LayerWinnerTest" };
            c.AddParameter("T", AnimatorControllerParameterType.Bool);
            c.AddParameter("F", AnimatorControllerParameterType.Bool);
            // The first layer always runs at full strength, so the cases start at the second.
            Layer(c, "Base");
            return c;
        }

        static AnimatorControllerLayer Layer(AnimatorController c, string name, float weight = 1f,
            AnimatorLayerBlendingMode mode = AnimatorLayerBlendingMode.Override, AvatarMask mask = null)
        {
            var layer = new AnimatorControllerLayer
            {
                name = name, defaultWeight = weight, blendingMode = mode, avatarMask = mask,
                stateMachine = new AnimatorStateMachine { name = name },
            };
            c.AddLayer(layer);
            return layer;
        }

        // Off and On, switched by T.
        static AnimatorState Two(AnimatorStateMachine machine, Motion off, Motion on)
        {
            var a = machine.AddState("Off");
            a.motion = off;
            var b = machine.AddState("On");
            b.motion = on;
            a.AddTransition(b).AddCondition(AnimatorConditionMode.If, 0f, "T");
            b.AddTransition(a).AddCondition(AnimatorConditionMode.IfNot, 0f, "T");
            return a;
        }

        static void Contradiction(AnimatorTransition t)
        {
            t.AddCondition(AnimatorConditionMode.If, 0f, "F");
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, "F");
        }

        static AnimationClip Clip(float value, string path = "Obj")
        {
            var clip = new AnimationClip();
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive"),
                AnimationCurve.Constant(0f, 0f, value));
            return clip;
        }

        static AnimationClip Swap()
        {
            var clip = new AnimationClip();
            AnimationUtility.SetObjectReferenceCurve(clip,
                EditorCurveBinding.PPtrCurve("Obj", typeof(MeshRenderer), "m_Materials.Array.data[0]"),
                new[] { new ObjectReferenceKeyframe { time = 0f, value = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat") } });
            return clip;
        }

        static AvatarMask Mask(params string[] paths)
        {
            var mask = new AvatarMask();
            mask.transformCount = paths.Length;
            for (int i = 0; i < paths.Length; i++)
            {
                mask.SetTransformPath(i, paths[i]);
                mask.SetTransformActive(i, true);
            }
            return mask;
        }
    }
}
#endif
