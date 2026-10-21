// Add squash on an avatar whose controller the Animator window shows: which state machines
// the build destroys, and whether the window is left holding them. The window itself is made
// here and never drawn, since a batch Unity has no GUI to draw it in; a breadcrumb or graph
// pointing at a destroyed state machine is what floods its OnGUI with MissingReferenceException.
//
// Run: Unity.exe -batchmode -projectPath <p> -executeMethod AvatarBridge.Regression.AnimatorFloodProbe.Run
//        -floodPrefab <Assets/...prefab> -floodOut <dir> -quit
#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class AnimatorFloodProbe
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        static IEnumerable<AnimatorStateMachine> Machines(AnimatorStateMachine m)
        {
            yield return m;
            foreach (var c in m.stateMachines)
                foreach (var x in Machines(c.stateMachine)) yield return x;
        }

        public static void Run()
        {
            string dir = Arg("-floodOut");
            Directory.CreateDirectory(dir);
            var log = new System.Text.StringBuilder();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                ShaderUtil.allowAsyncCompilation = false;
                var avatar = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Arg("-floodPrefab")));
                var anim = avatar.GetComponent<Animator>();
                var controller = Effective(anim);
                log.AppendLine($"controller {controller?.name} at {AssetDatabase.GetAssetPath(controller)}, {controller?.layers.Length} layers");
                // Every state machine loaded, whichever controller holds it: the window may show
                // one the avatar's Animator does not name.
                var machines = Resources.FindObjectsOfTypeAll<AnimatorStateMachine>()
                    .Select(m => (m, path: AssetDatabase.GetAssetPath(m), id: m.GetInstanceID())).ToList();
                log.AppendLine($"{machines.Count} state machines loaded before, {controller.layers.SelectMany(l => Machines(l.stateMachine)).Count()} in the controller");

                var toolType = Type.GetType("UnityEditor.Graphs.AnimatorControllerTool, UnityEditor.Graphs");
                var tool = (EditorWindow)ScriptableObject.CreateInstance(toolType);
                toolType.GetProperty("animatorController", Any).SetValue(tool, controller);
                toolType.GetMethod("ResetUI", Any).Invoke(tool, null);
                log.AppendLine("window breadcrumbs before: " + Crumbs(tool, toolType));

                Selection.activeGameObject = avatar;
                for (int pass = 1; pass <= 2; pass++)
                {
                    string said = YapsSquash.Build(avatar.transform, out bool built);
                    log.AppendLine($"build {pass}: built {built}, {said}");
                    log.AppendLine($"  controller: {(controller == null ? "DESTROYED" : "alive")}, effective now " +
                                   $"{Effective(anim)?.GetInstanceID()} against {controller?.GetInstanceID()}");
                    var dead = machines.Where(x => x.m == null).ToList();
                    log.AppendLine($"  {dead.Count} of {machines.Count} state machines destroyed");
                    foreach (var g in dead.GroupBy(d => d.path)) log.AppendLine($"    {g.Count()} from {(g.Key == "" ? "(not an asset)" : g.Key)}");
                    log.AppendLine("  window breadcrumbs: " + Crumbs(tool, toolType));
                }
                UnityEngine.Object.DestroyImmediate(tool);
            }
            catch (Exception x)
            {
                log.AppendLine("ERROR " + x);
            }
            File.WriteAllText(Path.Combine(dir, "flood.txt"), log.ToString());
            Debug.Log("[AnimatorFlood]\n" + log);
            EditorApplication.Exit(0);
        }

        // What the Animator window shows for an Animator: an override's base controller.
        static AnimatorController Effective(Animator a) =>
            (a.runtimeAnimatorController is AnimatorOverrideController o ? o.runtimeAnimatorController : a.runtimeAnimatorController) as AnimatorController;

        static string Crumbs(EditorWindow tool, Type toolType)
        {
            var list = toolType.GetField("m_BreadCrumbs", Any)?.GetValue(tool) as IList;
            if (list == null) return "none";
            return string.Join(", ", list.Cast<object>().Select(c =>
            {
                var target = c.GetType().GetProperty("target", Any)?.GetValue(c) as UnityEngine.Object;
                return target == null ? "DESTROYED" : target.name;
            }));
        }
    }
}
#endif
