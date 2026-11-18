// Does "nothing in the animator can switch it on" hold up?
//
// Checks the report's "dead renderer" claim (off, no clip reaches it) with a
// walk sharing none of its code: every animator, overrides, blend trees, sub
// machines, ancestor paths. A false positive deletes working geometry.
//
//   -executeMethod AvatarBridge.Regression.DeadRendererProbe.RunBatch
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class DeadRendererProbe
    {
        const string OutputRoot = "Assets/AvatarBridgeOutput";

        class Row
        {
            public string Avatar;
            public int Dead;
            public long DeadBytes;
            public int Clips;
            public readonly List<string> FalsePositives = new List<string>();
            public readonly List<string> SharedWithLive = new List<string>();
            public readonly List<string> Missing = new List<string>();
        }

        public static void RunBatch()
        {
            var rows = new List<Row>();
            string[] folders = AssetDatabase.GetSubFolders(OutputRoot);
            Debug.Log($"[Dead] {folders.Length} converted avatar(s) to read");

            foreach (string folder in folders)
            {
                string prefab = AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase));
                if (prefab == null) continue;

                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
                if (asset == null) continue;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                try
                {
                    var avatar = instance.GetComponent<CVRAvatar>();
                    if (avatar == null) continue;
                    rows.Add(Check(avatar, asset.name));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Dead] {asset.name}: {e.Message}");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }

            Write(rows);
            EditorApplication.Exit(0);
        }

        static Row Check(CVRAvatar avatar, string name)
        {
            var row = new Row { Avatar = name };

            var survey = AvatarSurvey.Build(avatar);
            var weight = AvatarWeight.Measure(avatar, survey);
            row.Dead = weight.Dead.Count;
            row.DeadBytes = weight.DeadBytes;
            if (row.Dead == 0) return row;

            var switched = SwitchedByAnyClip(avatar, out int clipCount);
            row.Clips = clipCount;

            // Which materials a renderer that stays visible is still using.
            // Deleting a dead renderer gives back nothing they share.
            var liveMaterials = new HashSet<Material>();
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(r.transform, avatar.transform);
                if (weight.Dead.Contains(path)) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m != null) liveMaterials.Add(m);
                }
            }

            foreach (string path in weight.Dead)
            {
                if (Reachable(path, switched)) row.FalsePositives.Add(path);

                var found = BridgeContext.FindByAnimationPath(avatar.transform, path);
                if (found == null)
                {
                    row.Missing.Add(path);
                    continue;
                }

                var renderer = found.GetComponent<Renderer>();
                if (renderer == null) continue;
                if (renderer.sharedMaterials.Any(m => m != null && liveMaterials.Contains(m)))
                {
                    row.SharedWithLive.Add(path);
                }
            }

            return row;
        }

        // Read from the clips, not the survey. The corpus digest calls this too.
        public static HashSet<string> SwitchedByAnyClip(CVRAvatar avatar, out int clipCount)
        {
            var clips = new HashSet<AnimationClip>();
            foreach (var animator in avatar.GetComponentsInChildren<Animator>(true))
            {
                CollectClips(animator.runtimeAnimatorController, clips);
            }
            clipCount = clips.Count;

            var switched = new HashSet<string>(StringComparer.Ordinal);
            foreach (var clip in clips)
            {
                if (clip == null) continue;
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (b.propertyName == "m_IsActive" || b.propertyName == "m_Enabled") switched.Add(b.path);
                }
            }
            return switched;
        }

        // The path itself or any ancestor. Switching a parent object on
        // brings everything under it back.
        public static bool Reachable(string path, HashSet<string> switched)
        {
            if (switched.Contains(path)) return true;
            for (int cut = path.LastIndexOf('/'); cut > 0; cut = path.LastIndexOf('/', cut - 1))
            {
                if (switched.Contains(path.Substring(0, cut))) return true;
            }
            return switched.Contains("");
        }

        // The survey reads the controller under an override and misses swapped clips; collect both.
        static void CollectClips(RuntimeAnimatorController runtime, HashSet<AnimationClip> clips)
        {
            if (runtime == null) return;

            if (runtime is AnimatorOverrideController over)
            {
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                over.GetOverrides(pairs);
                foreach (var pair in pairs)
                {
                    if (pair.Key != null) clips.Add(pair.Key);
                    if (pair.Value != null) clips.Add(pair.Value);
                }
                CollectClips(over.runtimeAnimatorController, clips);
                return;
            }

            if (!(runtime is AnimatorController controller)) return;
            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine != null) CollectMachine(layer.stateMachine, clips);
            }
        }

        static void CollectMachine(AnimatorStateMachine machine, HashSet<AnimationClip> clips)
        {
            foreach (var state in machine.states) CollectMotion(state.state.motion, clips);
            foreach (var sub in machine.stateMachines) CollectMachine(sub.stateMachine, clips);
        }

        static void CollectMotion(Motion motion, HashSet<AnimationClip> clips)
        {
            if (motion == null) return;
            if (motion is AnimationClip clip) { clips.Add(clip); return; }
            if (!(motion is BlendTree tree)) return;
            foreach (var child in tree.children) CollectMotion(child.motion, clips);
        }

        static void Write(List<Row> rows)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("# Is the dead-renderer call safe to act on?\n\n");

            int falsePositives = rows.Sum(r => r.FalsePositives.Count);
            int shared = rows.Sum(r => r.SharedWithLive.Count);
            int missing = rows.Sum(r => r.Missing.Count);
            long bytes = rows.Sum(r => r.DeadBytes);

            sb.Append(rows.Count).Append(" avatars, ")
              .Append(rows.Sum(r => r.Dead)).Append(" renderers called dead, ")
              .Append(Mb(bytes)).Append(" behind them.\n\n");
            sb.Append("A clip switches it after all: ").Append(falsePositives)
              .Append(falsePositives == 0 ? "  (the call holds)" : "  DELETING THESE WOULD BREAK THE AVATAR").Append('\n');
            sb.Append("Materials a live renderer also uses: ").Append(shared)
              .Append("  (deleting gives back nothing here)\n");
            sb.Append("Path does not resolve: ").Append(missing).Append('\n');

            if (falsePositives > 0)
            {
                sb.Append("\n## A clip switches these\n\n");
                foreach (var r in rows.Where(r => r.FalsePositives.Count > 0))
                {
                    sb.Append("- **").Append(r.Avatar).Append("**\n");
                    foreach (string p in r.FalsePositives) sb.Append("  - ").Append(p).Append('\n');
                }
            }

            if (missing > 0)
            {
                sb.Append("\n## Paths that do not resolve\n\n");
                foreach (var r in rows.Where(r => r.Missing.Count > 0))
                {
                    sb.Append("- **").Append(r.Avatar).Append("**: ")
                      .Append(string.Join(", ", r.Missing)).Append('\n');
                }
            }

            sb.Append("\n| avatar | dead | behind them | clips read | switched after all | shared with live |\n");
            sb.Append("|---|---|---|---|---|---|\n");
            foreach (var r in rows.Where(r => r.Dead > 0).OrderByDescending(r => r.DeadBytes))
            {
                sb.Append("| ").Append(r.Avatar)
                  .Append(" | ").Append(r.Dead)
                  .Append(" | ").Append(Mb(r.DeadBytes))
                  .Append(" | ").Append(r.Clips)
                  .Append(" | ").Append(r.FalsePositives.Count)
                  .Append(" | ").Append(r.SharedWithLive.Count)
                  .Append(" |\n");
            }

            string repo = Environment.GetEnvironmentVariable("AVATARBRIDGE_REPO") ?? ".";
            File.WriteAllText(Path.Combine(repo, "dead.md"), sb.ToString());
            Debug.Log($"[Dead] wrote dead.md: {falsePositives} false positive(s) across {rows.Count} avatar(s)");
        }

        static string Mb(long bytes) => (bytes / 1048576f).ToString("0.0") + " MB";
    }
}
#endif
