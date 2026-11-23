#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    // An Action layer with one AFK set per AFK type, each in its own sub-machine under the same state
    // names. Moved into CVR's locomotion layer, every type must keep its own copies: keyed by name they
    // all fell onto the first type's, whose outro then sent the other types back into AFK for ever.
    public static class ActionAfkTypesTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Action AFK types keep their own states")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[ActionAfkTypesTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            const string dir = "Assets/__ActionAfkTypesTest";
            AssetDatabase.DeleteAsset(dir);
            AssetDatabase.CreateFolder("Assets", "__ActionAfkTypesTest");
            GameObject avatar = null;
            try
            {
                avatar = new GameObject("__ActionAfkTypesTest");
                var master = AnimatorController.CreateAnimatorControllerAtPath(dir + "/master.controller");
                master.AddParameter("AFK", AnimatorControllerParameterType.Bool);
                master.AddParameter("TypeAFK", AnimatorControllerParameterType.Int);
                var loco = master.layers[0];
                loco.name = "Locomotion/Emotes";
                var stand = loco.stateMachine.AddState("Standard Locomotion");
                loco.stateMachine.defaultState = stand;
                master.layers = new[] { loco };

                var clips = new[] { "A", "B" }.Select(t => new[] { "Intro", "Loop", "Outro" }.Select(n =>
                {
                    var c = new AnimationClip { name = $"{t}_{n}" };
                    AssetDatabase.AddObjectToAsset(c, master);
                    return c;
                }).ToArray()).ToArray();

                // Built twice the same way: the merge clones the source layer before moving it.
                AnimatorControllerLayer Build(string name)
                {
                    var machine = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
                    AssetDatabase.AddObjectToAsset(machine, master);
                    var idle = machine.AddState("Idle");
                    machine.defaultState = idle;
                    var prepare = machine.AddState("Prepare AFK");
                    var up = prepare.AddStateMachineBehaviour<VRCPlayableLayerControl>();
                    up.layer = VRC.SDKBase.VRC_PlayableLayerControl.BlendableLayer.Action; up.goalWeight = 1f;
                    var restore = machine.AddState("Restore");
                    var down = restore.AddStateMachineBehaviour<VRCPlayableLayerControl>();
                    down.layer = VRC.SDKBase.VRC_PlayableLayerControl.BlendableLayer.Action; down.goalWeight = 0f;
                    var go = idle.AddTransition(prepare); go.hasExitTime = false;
                    go.AddCondition(AnimatorConditionMode.If, 0f, "AFK");
                    var home = restore.AddTransition(idle); home.hasExitTime = true; home.exitTime = 1f;
                    for (int t = 0; t < 2; t++)
                    {
                        var sub = machine.AddStateMachine(t == 0 ? "TypeA" : "TypeB");
                        var intro = sub.AddState("AFK_Intro"); intro.motion = clips[t][0];
                        var loop = sub.AddState("AFK"); loop.motion = clips[t][1];
                        var outro = sub.AddState("AFK_Outro"); outro.motion = clips[t][2];
                        var pick = prepare.AddTransition(intro); pick.hasExitTime = false;
                        pick.AddCondition(AnimatorConditionMode.Equals, t, "TypeAFK");
                        var into = intro.AddTransition(loop); into.hasExitTime = true; into.exitTime = 1f;
                        var leave = loop.AddTransition(outro); leave.hasExitTime = false;
                        leave.AddCondition(AnimatorConditionMode.IfNot, 0f, "AFK");
                        var again = outro.AddTransition(intro); again.hasExitTime = true; again.exitTime = 1f;
                        again.AddCondition(AnimatorConditionMode.If, 0f, "AFK");
                        var done = outro.AddTransition(restore); done.hasExitTime = true; done.exitTime = 1f;
                        done.AddCondition(AnimatorConditionMode.IfNot, 0f, "AFK");
                    }
                    return new AnimatorControllerLayer { name = name, stateMachine = machine, defaultWeight = 0f };
                }
                var source = Build("Action");
                var clone = Build("[Action] Action");

                var ctx = new BridgeContext
                {
                    Target = avatar,
                    Report = new BridgeReport(),
                    Settings = new BridgeSettings(),
                    MergedController = master,
                };
                var layers = master.layers.ToList();
                int moved = AnimatorMerger.TransplantActionFeatureForTest(master, layers, clone, source, ctx);
                fail += Check($"the AFK window moved ({moved} states)", moved > 0);

                var copies = master.layers[0].stateMachine.states.Select(c => c.state).Where(s => s.name.StartsWith("[AB]")).ToList();
                foreach (var t in new[] { 0, 1 })
                    foreach (var n in new[] { 0, 1, 2 })
                    {
                        var clip = clips[t][n];
                        int count = copies.Count(s => s.motion != null && s.motion.name.StartsWith(clip.name));
                        fail += Check($"{clip.name} has its own copy ({count})", count == 1);
                    }
                fail += Check("every copy has its own name", copies.Select(s => s.name).Distinct().Count() == copies.Count);

                // Each outro leaves to locomotion when AFK is off, and only ever re-enters its own intro.
                foreach (var t in new[] { 0, 1 })
                {
                    var outro = copies.FirstOrDefault(s => s.motion != null && s.motion.name.StartsWith(clips[t][2].name));
                    if (outro == null) { fail += Check($"type {t} outro copied", false); continue; }
                    var targets = outro.transitions.Select(x => x.destinationState).Where(d => d != null).ToList();
                    bool ownIntro = targets.Where(d => d.motion != null && d.motion.name.StartsWith("A_") | d.motion.name.StartsWith("B_"))
                        .All(d => d.motion.name.StartsWith(t == 0 ? "A_" : "B_"));
                    fail += Check($"type {t} outro re-enters only its own intro", ownIntro);
                    bool exits = outro.transitions.Any(x => x.destinationState == stand
                        || (x.destinationState != null && x.destinationState.transitions.Any(y => y.destinationState == stand)));
                    fail += Check($"type {t} outro can hand back to locomotion", exits);
                }
            }
            finally
            {
                if (avatar != null) Object.DestroyImmediate(avatar);
                AssetDatabase.DeleteAsset(dir);
            }

            Debug.Log(fail == 0
                ? "[ActionAfkTypesTest] PASS: every AFK type keeps its own states."
                : $"[ActionAfkTypesTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
