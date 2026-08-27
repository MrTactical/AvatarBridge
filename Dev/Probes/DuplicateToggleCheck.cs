// Does a conversion leave TWO menu toggles on one object?
//
// A user reported it on 4.3.4: their SPS toggles are listed, and converting
// to YAPS adds a second set, so the menu carries two of each and one of them
// is inert. YapsToggles already tries not to do that. ToggledBy asks whether
// anything already switches the object and stands its own toggle down if so.
// But that check only recognises an entry driving the object through
// gameObjectTargets, and a converted VRChat or SPS toggle very often drives
// it through an ANIMATION CLIP instead, which the check cannot see.
//
// This finds every object that more than one menu entry controls, by either
// route, and says which route each took. If every duplicate turns out to
// pair one Targets with one Clip, the blind spot in ToggledBy is the cause.
// Running it over the corpus finds every affected avatar at once, which
// beats reproducing one.
//
//   Tools/AvatarBridge/Dev/Duplicate menu toggles (this avatar)
//   -executeMethod AvatarBridge.Probes.DuplicateToggleCheck.RunBatch
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Probes
{
    public static class DuplicateToggleCheck
    {
        enum Route { Targets, Clip }

        struct Control
        {
            public string Entry;
            public Route How;
        }

        [MenuItem("Tools/AvatarBridge/Dev/Duplicate menu toggles (this avatar)")]
        static void RunHere()
        {
            var avatar = UnityEngine.Object.FindObjectOfType<CVRAvatar>();
            if (avatar == null) { Debug.LogError("[DupToggle] no CVRAvatar in the scene"); return; }
            var lines = Check(avatar);
            Debug.Log(lines.Count == 0
                ? "[DupToggle] no object is controlled by more than one menu entry"
                : "[DupToggle] " + lines.Count + " object(s) under more than one entry:\n  "
                  + string.Join("\n  ", lines));
        }

        // Every object any menu entry controls, and by which route.
        public static List<string> Check(CVRAvatar avatar)
        {
            var found = new List<string>();
            if (avatar == null || avatar.avatarSettings == null
                || avatar.avatarSettings.settings == null) return found;

            var controllers = new Dictionary<string, List<Control>>(StringComparer.Ordinal);
            RuntimeAnimatorController shipped =
                avatar.overrides != null ? avatar.overrides.runtimeAnimatorController : null;
            if (shipped == null) shipped = avatar.avatarSettings.baseController;
            var ac = shipped as AnimatorController;

            foreach (var entry in avatar.avatarSettings.settings)
            {
                if (entry == null || string.IsNullOrEmpty(entry.machineName)) continue;

                // Route one: the entry names the objects outright.
                var toggle = entry.setting as ABI.CCK.Scripts.CVRAdvancesAvatarSettingGameObjectToggle;
                if (toggle != null && toggle.gameObjectTargets != null)
                {
                    foreach (var t in toggle.gameObjectTargets)
                    {
                        if (t == null || t.gameObject == null) continue;
                        Note(controllers, PathOf(t.gameObject.transform, avatar.transform),
                             entry.machineName, Route.Targets);
                    }
                }

                // Route two: a layer this parameter drives animates
                // m_IsActive on some path. The route ToggledBy cannot see,
                // and the one a converted SPS toggle usually takes.
                if (ac == null) continue;
                foreach (var layer in ac.layers)
                {
                    if (layer == null || layer.stateMachine == null) continue;
                    if (!Drives(layer, entry.machineName)) continue;
                    foreach (var path in ActivePaths(layer))
                    {
                        Note(controllers, path, entry.machineName, Route.Clip);
                    }
                }
            }

            foreach (var pair in controllers)
            {
                var distinct = pair.Value.Select(c => c.Entry).Distinct().ToList();
                if (distinct.Count < 2) continue;
                found.Add(pair.Key + "  <- " + string.Join(", ",
                    pair.Value.Select(c => c.Entry + " by " + c.How).Distinct()));
            }
            return found;
        }

        static void Note(Dictionary<string, List<Control>> map, string path, string entry, Route how)
        {
            if (string.IsNullOrEmpty(path)) return;
            List<Control> list;
            if (!map.TryGetValue(path, out list)) map[path] = list = new List<Control>();
            list.Add(new Control { Entry = entry, How = how });
        }

        // A layer belongs to a parameter if a transition reads it, or a
        // blend tree blends on it.
        static bool Drives(AnimatorControllerLayer layer, string parameter)
        {
            foreach (var st in layer.stateMachine.states)
            {
                foreach (var tr in st.state.transitions)
                    foreach (var c in tr.conditions)
                        if (c.parameter == parameter) return true;
                var bt = st.state.motion as BlendTree;
                if (bt != null && Blends(bt, parameter)) return true;
            }
            foreach (var any in layer.stateMachine.anyStateTransitions)
                foreach (var c in any.conditions)
                    if (c.parameter == parameter) return true;
            return false;
        }

        static bool Blends(BlendTree tree, string parameter)
        {
            if (tree.blendParameter == parameter || tree.blendParameterY == parameter) return true;
            foreach (var child in tree.children)
            {
                if (child.directBlendParameter == parameter) return true;
                var sub = child.motion as BlendTree;
                if (sub != null && Blends(sub, parameter)) return true;
            }
            return false;
        }

        static IEnumerable<string> ActivePaths(AnimatorControllerLayer layer)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var st in layer.stateMachine.states)
                foreach (var clip in Clips(st.state.motion))
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                        if (b.propertyName == "m_IsActive" && seen.Add(b.path))
                            yield return b.path;
        }

        static IEnumerable<AnimationClip> Clips(Motion m)
        {
            var c = m as AnimationClip;
            if (c != null) { yield return c; yield break; }
            var t = m as BlendTree;
            if (t == null) yield break;
            foreach (var child in t.children)
                foreach (var inner in Clips(child.motion)) yield return inner;
        }

        static string PathOf(Transform t, Transform root)
        {
            return t == root ? "" : AnimationUtility.CalculateTransformPath(t, root);
        }

        // Convert every corpus avatar and check each, in one Unity session.
        public static void RunBatch()
        {
            var scenes = AssetDatabase.FindAssets("t:Scene")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && p.StartsWith("Assets/", StringComparison.Ordinal))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();

            var report = new List<string>();
            int affected = 0, converted = 0;
            foreach (var scene in scenes)
            {
                try
                {
                    EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
                    var source = UnityEngine.Object.FindObjectsOfType<VRCAvatarDescriptor>(true)
                        .FirstOrDefault();
                    if (source == null) continue;
                    var result = BridgeConverter.Convert(source, new BridgeSettings());
                    converted++;
                    var cvr = result.ConvertedRoot != null
                        ? result.ConvertedRoot.GetComponentInChildren<CVRAvatar>(true) : null;
                    var dups = Check(cvr);
                    if (dups.Count == 0) continue;
                    affected++;
                    report.Add(scene);
                    foreach (var d in dups) report.Add("    " + d);
                }
                catch (Exception e)
                {
                    report.Add(scene + "    FAILED: " + e.Message);
                }
            }

            string dir = System.IO.Path.GetDirectoryName(Application.dataPath) + "/Regression";
            Directory.CreateDirectory(dir);
            string outPath = dir + "/DuplicateToggles.txt";
            File.WriteAllText(outPath,
                affected + " avatar(s) with an object under more than one menu entry, of "
                + converted + " converted\n" + string.Join("\n", report) + "\n");
            Debug.Log("[DupToggle] " + affected + " of " + converted + ", written to " + outPath);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
#endif
