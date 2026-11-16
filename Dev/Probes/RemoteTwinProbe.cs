#if CVR_CCK_EXISTS
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // The Avatar Tester's "Show a copy as others see it", run in batch Play mode on a made avatar:
    // a synced float, a "#" local, a client float (GestureLeft), IsLocal, a float a clip drives, and
    // one float past the sync budget. Passes when the copy hears the synced and client values, keeps
    // the local, the clip-driven float and the one past the budget at their defaults, and has IsLocal
    // off. Run without -quit; it exits itself:
    //   -executeMethod AvatarBridge.Regression.RemoteTwinProbe.Run
    // Leaves Assets/__RemoteTwin for the caller to move out.
    [InitializeOnLoad]
    public static class RemoteTwinProbe
    {
        const string Folder = "Assets/__RemoteTwin";
        const string On = "AvatarBridge.RemoteTwinProbe.on";
        const string Source = "Twin Probe Avatar";
        const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;
        static int frame;

        static RemoteTwinProbe()
        {
            if (SessionState.GetBool(On, false)) EditorApplication.update += Tick;
        }

        static void Log(string s) => Debug.Log("[RemoteTwin] " + s);

        public static void Run()
        {
            try
            {
                if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Folder.Substring("Assets/".Length));
                var controller = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/Twin.controller");
                foreach (var (name, type) in new[] { ("Synced", AnimatorControllerParameterType.Float), ("#Local", AnimatorControllerParameterType.Float),
                             ("GestureLeft", AnimatorControllerParameterType.Float), ("IsLocal", AnimatorControllerParameterType.Bool),
                             ("Driven", AnimatorControllerParameterType.Float) })
                    controller.AddParameter(name, type);
                // Enough 32-bit floats after Synced to fill the budget, then one more.
                int budget = (int)typeof(BridgeDiagnostics).GetField("AasBitBudget", Hidden).GetRawConstantValue();
                int fill = budget / 32 - 1;
                for (int i = 0; i <= fill; i++) controller.AddParameter("Fill" + i, AnimatorControllerParameterType.Float);
                var clip = new AnimationClip { name = "Drive" };
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "Driven"), AnimationCurve.Constant(0f, 1f, 0.25f));
                AssetDatabase.CreateAsset(clip, Folder + "/Drive.anim");
                controller.layers[0].stateMachine.AddState("Drive").motion = clip;
                AssetDatabase.SaveAssets();

                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                var avatar = new GameObject(Source);
                var animator = avatar.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                Log($"budget {budget} bits, {fill + 1} fill floats after Synced, the last past it");
                SessionState.SetBool(On, true);
                EditorApplication.update += Tick;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static void Tick()
        {
            if (!Application.isPlaying) return;
            frame++;
            var live = GameObject.Find(Source)?.GetComponent<Animator>();
            if (live == null) return;
            int last = live.parameters.Count(p => p.name.StartsWith("Fill")) - 1;
            if (frame == 5)
            {
                typeof(CckAnimatorTester).GetMethod("ShowTwin", Hidden).Invoke(null, new object[] { live });
                Log("ShowTwin called");
            }
            live.SetFloat("Synced", 0.7f);
            live.SetFloat("#Local", 0.9f);
            live.SetFloat("GestureLeft", 0.5f);
            live.SetBool("IsLocal", true);
            live.SetFloat("Fill0", 1f);
            live.SetFloat("Fill" + last, 1f);
            if (frame < 60) return;

            EditorApplication.update -= Tick;
            SessionState.SetBool(On, false);
            var twin = typeof(CckAnimatorTester).GetField("_twin", Hidden).GetValue(null) as GameObject;
            var copy = typeof(CckAnimatorTester).GetField("_twinAnimator", Hidden).GetValue(null) as Animator;
            int dropped = (int)typeof(CckAnimatorTester).GetField("_twinDropped", Hidden).GetValue(null);
            string note = (string)typeof(CckAnimatorTester).GetMethod("TwinNote", Hidden).Invoke(null, null);
            bool ok = twin != null && copy != null;
            void Expect(string what, bool pass, string seen)
            {
                ok &= pass;
                Log($"{(pass ? "ok  " : "FAIL")} {what}: {seen}");
            }
            if (ok)
            {
                Expect("the copy is apart from the wearer", (twin.transform.position - live.transform.position).magnitude > 0.1f, twin.name);
                Expect("Synced is heard", Mathf.Approximately(copy.GetFloat("Synced"), 0.7f), $"{copy.GetFloat("Synced")}");
                Expect("GestureLeft is heard", Mathf.Approximately(copy.GetFloat("GestureLeft"), 0.5f), $"{copy.GetFloat("GestureLeft")}");
                Expect("#Local stays at its default", copy.GetFloat("#Local") == 0f, $"{copy.GetFloat("#Local")}");
                Expect("IsLocal is off on the copy, on for the wearer", !copy.GetBool("IsLocal") && live.GetBool("IsLocal"), $"copy {copy.GetBool("IsLocal")}, wearer {live.GetBool("IsLocal")}");
                Expect("the clip drives Driven on the copy itself", Mathf.Approximately(copy.GetFloat("Driven"), 0.25f), $"{copy.GetFloat("Driven")}");
                Expect("Fill0, inside the budget, is heard", Mathf.Approximately(copy.GetFloat("Fill0"), 1f), $"{copy.GetFloat("Fill0")}");
                Expect($"Fill{last}, past the budget, stays at its default", copy.GetFloat("Fill" + last) == 0f, $"{copy.GetFloat("Fill" + last)}");
                Expect("one parameter counted as past the budget", dropped == 1, $"{dropped}; the note says: {note}");
                // Driven reads the same on both copies whoever writes it, so its absence is read here.
                var hears = ((ValueTuple<string, AnimatorControllerParameterType>[])typeof(CckAnimatorTester).GetField("_twinHears", Hidden).GetValue(null)).Select(h => h.Item1).ToList();
                Expect("the copy listens for Synced and GestureLeft, never #Local, IsLocal or Driven",
                    hears.Contains("Synced") && hears.Contains("GestureLeft") && !hears.Contains("#Local") && !hears.Contains("IsLocal") && !hears.Contains("Driven"),
                    $"{hears.Count} heard: {string.Join(", ", hears.Where(h => !h.StartsWith("Fill")))} and {hears.Count(h => h.StartsWith("Fill"))} fills");
            }
            else Log("FAIL no copy was made");
            Log((ok ? "PASS" : "FAIL") + $" after {frame} frames");
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
#endif
