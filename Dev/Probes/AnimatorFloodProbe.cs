// Add squash on an avatar whose controller the Animator window shows: which state machines
// the build destroys, and whether the window is left holding them. The window itself is made
// here and never drawn, since a batch Unity has no GUI to draw it in; a breadcrumb or graph
// pointing at a destroyed state machine is what floods its OnGUI with MissingReferenceException.
//
// Run: Unity.exe -batchmode -projectPath <p> -executeMethod AvatarBridge.Regression.AnimatorFloodProbe.Run
//        -floodPrefab <Assets/...prefab> -floodOut <dir> -quit
// Add -floodPlay, and drop -quit, to build in Play mode instead, as the report had it. That run
// also leaves Play mode and checks what the build left behind: Unity puts the scene back, so an
// animation still switching to a squash copy the avatar no longer wears is a half-applied build.
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
    [InitializeOnLoad]
    public static class AnimatorFloodProbe
    {
        const string K = "AnimatorFloodProbe.";

        // Entering and leaving Play mode reload the domain, so the run continues from here.
        static AnimatorFloodProbe()
        {
            if (SessionState.GetInt(K + "stage", 0) > 0) EditorApplication.update += Tick;
        }
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
            if (Environment.GetCommandLineArgs().Contains("-floodPlay")) { StartPlay(dir); return; }
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

        static void StartPlay(string dir)
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            ShaderUtil.allowAsyncCompilation = false;
            var avatar = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Arg("-floodPrefab")));
            foreach (var a in UnityEngine.Object.FindObjectsOfType<Animator>()) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            SessionState.SetString(K + "out", dir);
            SessionState.SetString(K + "avatar", avatar.name);
            SessionState.SetString(K + "log", $"edit mode: {Worn(avatar)} renderer slots wear a squash copy, {Switched(avatar)} animation keys switch to one\n");
            SessionState.SetInt(K + "stage", 1);
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            int stage = SessionState.GetInt(K + "stage", 0);
            var log = new System.Text.StringBuilder(SessionState.GetString(K + "log", ""));
            try
            {
                if (stage == 1 && EditorApplication.isPlaying && Time.frameCount > 5)
                {
                    var avatar = GameObject.Find(SessionState.GetString(K + "avatar", ""));
                    var anim = avatar.GetComponent<Animator>();
                    var controller = Effective(anim);
                    var machines = Resources.FindObjectsOfTypeAll<AnimatorStateMachine>().Select(m => (m, path: AssetDatabase.GetAssetPath(m))).ToList();
                    var toolType = Type.GetType("UnityEditor.Graphs.AnimatorControllerTool, UnityEditor.Graphs");
                    var tool = (EditorWindow)ScriptableObject.CreateInstance(toolType);
                    toolType.GetProperty("animatorController", Any).SetValue(tool, controller);
                    toolType.GetProperty("animator", Any)?.SetValue(tool, anim);
                    toolType.GetMethod("ResetUI", Any).Invoke(tool, null);
                    log.AppendLine($"play mode: controller {controller?.name}, {machines.Count} state machines loaded, window breadcrumbs {Crumbs(tool, toolType)}");
                    string said = YapsSquash.Build(avatar.transform, out bool built);
                    log.AppendLine($"  build: built {built}, {said}");
                    var dead = machines.Where(x => x.m == null).ToList();
                    log.AppendLine($"  {dead.Count} of {machines.Count} state machines destroyed, controller {(controller == null ? "DESTROYED" : "alive")}, breadcrumbs {Crumbs(tool, toolType)}");
                    foreach (var g in dead.GroupBy(d => d.path)) log.AppendLine($"    {g.Count()} from {(g.Key == "" ? "(not an asset)" : g.Key)}");
                    log.AppendLine($"  {Worn(avatar)} renderer slots wear a squash copy, {Switched(avatar)} animation keys switch to one");
                    UnityEngine.Object.DestroyImmediate(tool);
                    SessionState.SetInt(K + "stage", 2);
                    SessionState.SetString(K + "log", log.ToString());
                    EditorApplication.ExitPlaymode();
                    return;
                }
                if (stage == 2 && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    var avatar = GameObject.Find(SessionState.GetString(K + "avatar", ""));
                    int worn = Worn(avatar), switched = Switched(avatar);
                    log.AppendLine($"after Play mode: {worn} renderer slots wear a squash copy, {switched} animation keys switch to one, squash host {(YapsSquash.Has(avatar.transform) ? "present" : "absent")}");
                    log.AppendLine(switched > 0 && worn == 0 ? "HALF-APPLIED: animations switch to squash copies the avatar no longer wears" : "consistent");
                }
                else return;
            }
            catch (Exception x)
            {
                log.AppendLine("ERROR " + x);
                if (EditorApplication.isPlaying) { SessionState.SetInt(K + "stage", 2); SessionState.SetString(K + "log", log.ToString()); EditorApplication.ExitPlaymode(); return; }
            }
            SessionState.SetInt(K + "stage", 0);
            EditorApplication.update -= Tick;
            File.WriteAllText(Path.Combine(SessionState.GetString(K + "out", "."), "flood-play.txt"), log.ToString());
            Debug.Log("[AnimatorFlood]\n" + log);
            EditorApplication.Exit(0);
        }

        static bool IsSquash(UnityEngine.Object o) => o is Material m && m.HasProperty("_YAPS_DentOwn");

        static int Worn(GameObject avatar) =>
            avatar.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Count(IsSquash);

        static int Switched(GameObject avatar) =>
            YapsSwapFollow.RunnableClips(avatar.transform).Sum(c => AnimationUtility.GetObjectReferenceCurveBindings(c)
                .Sum(b => AnimationUtility.GetObjectReferenceCurve(c, b).Count(k => IsSquash(k.value))));

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
