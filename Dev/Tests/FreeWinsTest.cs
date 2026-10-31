#if CVR_CCK_EXISTS
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Free wins on two avatars sharing one override controller: the tidied avatar gets a copy of
    // the override around the tidied controller, with its swaps, in every slot; the other avatar
    // and the shared override are left exactly as they were.
    // Batch: -executeMethod AvatarBridge.Regression.FreeWinsTest.Run
    public static class FreeWinsTest
    {
        const string Dir = "Assets/FreeWinsTest";
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Free wins on a shared override")]
        public static void Run()
        {
            failures = 0;
            GameObject one = null, two = null;
            try
            {
                AssetDatabase.DeleteAsset(Dir);
                AssetDatabase.CreateFolder("Assets", "FreeWinsTest");
                var controller = AnimatorController.CreateAnimatorControllerAtPath(Dir + "/Shared.controller");
                var wave = new AnimationClip { name = "Wave" };
                wave.SetCurve("", typeof(Transform), "m_LocalPosition.y", AnimationCurve.Constant(0, 1, 0));
                AssetDatabase.CreateAsset(wave, Dir + "/Wave.anim");
                var waved = new AnimationClip { name = "Wave swapped" };
                waved.SetCurve("", typeof(Transform), "m_LocalPosition.y", AnimationCurve.Constant(0, 1, 1));
                AssetDatabase.CreateAsset(waved, Dir + "/Wave swapped.anim");
                controller.AddMotion(wave);
                controller.AddLayer("Empty");
                var over = new AnimatorOverrideController(controller) { name = "Shared" };
                over[wave] = waved;
                AssetDatabase.CreateAsset(over, Dir + "/Shared.overrideController");
                AssetDatabase.SaveAssets();
                string overText = System.IO.File.ReadAllText(Dir + "/Shared.overrideController");

                one = Avatar("One", over);
                two = Avatar("Two", over);
                var cvr = one.GetComponent<CVRAvatar>();
                var plan = new FreeWins.Plan { Controller = controller };
                plan.Layers.Add("Empty");
                plan.LayerIndices.Add(1);
                var report = new BridgeReport();
                Undo.IncrementCurrentGroup();
                var into = FreeWins.Apply(cvr, plan, Dir + "/One tidied.controller", report);
                AssetDatabase.SaveAssets();
                foreach (var e in report.Entries) Debug.Log($"[FreeWinsTest]   {e.Status} {e.Subject}: {e.Detail}");

                Check(into != null && into.layers.Length == 1, "the copy lost its empty layer");
                var copy = cvr.overrides;
                Check(copy != null && copy != over && copy.runtimeAnimatorController == into
                      && AssetDatabase.GetAssetPath(copy).StartsWith(Dir + "/"), "the tidied avatar runs a copy of the override around it");
                Check(copy != null && copy[wave] == waved, "and the copy keeps the override's swaps");
                Check(one.GetComponent<Animator>().runtimeAnimatorController == copy
                      && cvr.avatarSettings.overrides == copy && cvr.avatarSettings.baseController == into,
                    "every slot of the tidied avatar points at the copies");
                Check(over.runtimeAnimatorController == controller
                      && System.IO.File.ReadAllText(Dir + "/Shared.overrideController") == overText,
                    "the shared override is untouched on disk");
                Check(two.GetComponent<CVRAvatar>().overrides == over && two.GetComponent<Animator>().runtimeAnimatorController == over,
                    "the other avatar still runs the shared override");
                Check(report.Entries.Any(e => e.Subject == "Override controller copied"), "the copy is reported");
                Undo.PerformUndo();
                Check(cvr.overrides == over && one.GetComponent<Animator>().runtimeAnimatorController == over,
                    "undo points the tidied avatar back at the shared override");
            }
            catch (System.Exception e)
            {
                failures++;
                Debug.LogError("[FreeWinsTest] threw: " + e);
            }
            finally
            {
                if (one != null) Object.DestroyImmediate(one);
                if (two != null) Object.DestroyImmediate(two);
                AssetDatabase.DeleteAsset(Dir);
            }
            Debug.Log(failures == 0 ? "[FreeWinsTest] PASS" : $"[FreeWinsTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static GameObject Avatar(string name, AnimatorOverrideController over)
        {
            var go = new GameObject(name);
            go.AddComponent<Animator>().runtimeAnimatorController = over;
            var cvr = go.AddComponent<CVRAvatar>();
            cvr.overrides = over;
            cvr.avatarSettings = new ABI.CCK.Scripts.CVRAdvancedAvatarSettings
            {
                baseController = over.runtimeAnimatorController, overrides = over,
            };
            return go;
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[FreeWinsTest] {(ok ? "ok  " : "FAIL")}  {what}");
        }
    }
}
#endif
