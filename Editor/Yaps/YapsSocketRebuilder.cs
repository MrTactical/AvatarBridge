// Replaces a converted socket's VRCFury rig with the one the YAPS toolkit builds:
// read what Fury placed, strip it, build fresh, repoint the author's depth reactions.
// One rig, so converted and native sockets cannot diverge.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsSocketRebuilder
    {
        const string Category = "YAPS";

        // The tip types a depth trigger listens for; a trigger filtered to
        // these and writing from a stay task is the author's depth channel.
        static readonly string[] TipTypes =
        {
            "TPS_Pen_Penetrating", "TPS_Pen_Penetrating_SelfNotOnHips",
            "SPSLL_Pen_Penetrating", "SPSLL_Pen_Penetrating_SelfNotOnHips",
        };

        public class Spec
        {
            public Transform Root;
            public bool IsHole;
            public bool EmitLights;
            // One per stay task on a tip-filtered trigger.
            public List<string> DepthParams = new List<string>();
            // Filled by the caller: only the VRChat side can read the SPS component.
            public List<string> Tags = new List<string>();
            public int Lights, Pointers, Triggers;
        }

        // Reads kind, lights and depth channel first: the evidence is what gets stripped.
        public static Dictionary<Transform, Spec> ReadAndStrip(BridgeContext ctx, List<Transform> socketRoots)
        {
            var specs = new Dictionary<Transform, Spec>();
            foreach (var root in socketRoots)
            {
                var spec = new Spec { Root = root };

                foreach (var l in root.GetComponentsInChildren<Light>(true))
                {
                    if (!YapsScanner.IsProtocolLight(l)) continue;
                    spec.EmitLights = true;
                    int d = YapsScanner.LightDigit(l);
                    if (d == 1) spec.IsHole = true;
                }
                foreach (var p in root.GetComponentsInChildren<CVRPointer>(true))
                {
                    if (p != null && p.type != null && p.type.StartsWith("SPSLL_Socket_Hole", StringComparison.Ordinal))
                        spec.IsHole = true;
                }
                foreach (var t in root.GetComponentsInChildren<CVRAdvancedAvatarSettingsTrigger>(true))
                {
                    if (!ListensForTips(t) || IsHaptics(t)) continue;
                    foreach (var task in t.stayTasks)
                    {
                        if (!string.IsNullOrEmpty(task.settingName) && !spec.DepthParams.Contains(task.settingName))
                            spec.DepthParams.Add(task.settingName);
                    }
                }

                Strip(root, spec);
                specs[root] = spec;
            }

            int lights = specs.Values.Sum(s => s.Lights);
            int pointers = specs.Values.Sum(s => s.Pointers);
            int triggers = specs.Values.Sum(s => s.Triggers);
            if (lights + pointers + triggers > 0)
            {
                ctx.Report.Converted(Category,
                    $"Stripped VRCFury's socket rig from {BridgeElements.Count(specs.Count, "socket", "sockets")}",
                    $"{BridgeElements.Count(lights, "marker light", "marker lights")}, {BridgeElements.Count(pointers, "pointer", "pointers")} and {BridgeElements.Count(triggers, "depth trigger", "depth triggers")} " +
                    "removed. Each is rebuilt by the YAPS toolkit's own code.");
            }
            return specs;
        }

        // VRCFury bakes socket branches inactive for SPS's enable service, which does
        // not survive conversion. Wakes what NO clip can switch, on the socket, below
        // it and up to the root; anything a menu owns stays as found.
        public static void Wake(BridgeContext ctx, List<Transform> socketRoots)
        {
            var switchable = Switchable(ctx);

            int woken = 0;
            foreach (var socket in socketRoots)
            {
                for (var at = socket; at != null && at != ctx.Target.transform; at = at.parent)
                {
                    if (at.gameObject.activeSelf || switchable.Contains(ctx.PathInTarget(at))) continue;
                    at.gameObject.SetActive(true);
                    woken++;
                }
                foreach (var t in socket.GetComponentsInChildren<Transform>(true))
                {
                    if (t.gameObject.activeSelf || switchable.Contains(ctx.PathInTarget(t))) continue;
                    t.gameObject.SetActive(true);
                    woken++;
                }
                foreach (var trigger in socket.GetComponentsInChildren<CVRAdvancedAvatarSettingsTrigger>(true))
                {
                    if (trigger == null || trigger.enabled) continue;
                    trigger.enabled = true;
                    woken++;
                }
            }
            if (woken > 0)
            {
                ctx.Report.Converted(Category, $"{BridgeElements.Count(woken, "object", "objects")} on the socket chains woken",
                    "VRCFury leaves the socket branch, its second head and \"Original Object\" " +
                    "switched off for its own enable service to sort out, and that service is " +
                    "deleted here. Anything a menu toggle can switch was left exactly as found.");
            }
        }

        // Only layers that can assert: Fury's exclusivity merges at weight 0, and
        // counting it left sockets dark. The first layer runs whatever its weight.
        public static HashSet<string> Switchable(BridgeContext ctx)
        {
            return Switchable(ctx.MergedController);
        }

        public static HashSet<string> Switchable(AnimatorController controller)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            if (controller == null) return paths;
            var layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (i > 0 && layers[i].defaultWeight <= 0f) continue;
                if (layers[i].stateMachine == null) continue;
                foreach (var clip in ClipsOf(layers[i].stateMachine))
                {
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (b.propertyName == "m_IsActive") paths.Add(b.path);
                    }
                }
            }
            return paths;
        }

        static IEnumerable<AnimationClip> ClipsOf(AnimatorStateMachine machine)
        {
            foreach (var s in machine.states)
            {
                foreach (var clip in ClipsOf(s.state.motion)) yield return clip;
            }
            foreach (var child in machine.stateMachines)
            {
                foreach (var clip in ClipsOf(child.stateMachine)) yield return clip;
            }
        }

        static IEnumerable<AnimationClip> ClipsOf(Motion motion)
        {
            if (motion is AnimationClip clip)
            {
                yield return clip;
            }
            else if (motion is BlendTree tree)
            {
                foreach (var child in tree.children)
                    foreach (var deeper in ClipsOf(child.motion)) yield return deeper;
            }
        }

        static bool ListensForTips(CVRAdvancedAvatarSettingsTrigger t)
        {
            return t != null && t.allowedTypes != null
                && t.allowedTypes.Any(a => TipTypes.Contains(a));
        }

        // Haptics receivers read plug tips too; their parameters tell them apart.
        // Keep in step with SystemStripper.HapticsParamPrefixes (VRChat SDK only).
        static readonly string[] HapticsPrefixes = { "OGB", "pcs/", "WH_" };

        static bool IsHaptics(CVRAdvancedAvatarSettingsTrigger t)
        {
            IEnumerable<string> names = t.stayTasks.Select(s => s.settingName)
                .Concat(t.enterTasks.Select(s => s.settingName))
                .Concat(t.exitTasks.Select(s => s.settingName));
            return names.Any(n => n != null && HapticsPrefixes
                .Any(p => n.StartsWith(p, StringComparison.Ordinal)
                          || n.StartsWith("#" + p, StringComparison.Ordinal)));
        }

        // Protocol lights, socket pointers and tip depth triggers. The root, renderer,
        // haptics and anything user-made stay.
        static void Strip(Transform root, Spec spec)
        {
            foreach (var l in root.GetComponentsInChildren<Light>(true).Where(YapsScanner.IsProtocolLight).ToList())
            {
                spec.Lights++;
                RemoveHost(l.transform, root, l);
            }
            foreach (var p in root.GetComponentsInChildren<CVRPointer>(true).ToList())
            {
                if (p == null || p.type == null) continue;
                if (!p.type.StartsWith("TPS_Orf_", StringComparison.Ordinal)
                    && !p.type.StartsWith("SPSLL_Socket_", StringComparison.Ordinal)) continue;
                spec.Pointers++;
                RemoveHost(p.transform, root, p);
            }
            // Several parameters cannot collapse to one without changing what
            // plays, so those sockets keep their triggers as authored.
            if (spec.DepthParams.Count <= 1)
            {
                foreach (var t in root.GetComponentsInChildren<CVRAdvancedAvatarSettingsTrigger>(true).ToList())
                {
                    if (!ListensForTips(t) || IsHaptics(t)) continue;
                    spec.Triggers++;
                    RemoveHost(t.transform, root, t);
                }
            }
            // Fury's constraints that pinned the lights. Below the root only: one
            // on the root places the socket itself.
            foreach (var c in root.GetComponentsInChildren<UnityEngine.Animations.IConstraint>(true).ToList())
            {
                var component = c as Component;
                if (component == null || component.transform == root) continue;
                RemoveHost(component.transform, root, component);
            }
            PruneEmpty(root);
        }

        // Before the fresh announce: Fury's tracker light would double the DPS
        // signal and take a vertex light slot, and its pointers would win the
        // dedupe. Only for plugs that converted.
        public static void StripPlugRig(Transform plugRoot)
        {
            if (plugRoot == null) return;
            foreach (var l in plugRoot.GetComponentsInChildren<Light>(true).Where(YapsScanner.IsProtocolLight).ToList())
            {
                RemoveHost(l.transform, plugRoot, l);
            }
            foreach (var p in plugRoot.GetComponentsInChildren<CVRPointer>(true).ToList())
            {
                if (p == null || p.type == null) continue;
                if (!p.type.StartsWith("TPS_Pen_", StringComparison.Ordinal)
                    && !p.type.StartsWith("SPSLL_Pen_", StringComparison.Ordinal)) continue;
                RemoveHost(p.transform, plugRoot, p);
            }
            PruneEmpty(plugRoot);
        }

        // The whole object if nothing else lives there. Never the root.
        static void RemoveHost(Transform host, Transform root, Component doomed)
        {
            if (host != root && host.childCount == 0
                && host.GetComponents<Component>().Length <= 2)   // Transform + it
            {
                UnityEngine.Object.DestroyImmediate(host.gameObject);
                return;
            }
            UnityEngine.Object.DestroyImmediate(doomed);
        }

        // Bottom-up, so an empty chain goes in one pass.
        static void PruneEmpty(Transform root)
        {
            var all = root.GetComponentsInChildren<Transform>(true)
                .Where(t => t != root)
                .OrderByDescending(t => Depth(t))
                .ToList();
            foreach (var t in all)
            {
                if (t == null || t.childCount > 0) continue;
                if (t.GetComponents<Component>().Length > 1) continue;   // more than Transform
                UnityEngine.Object.DestroyImmediate(t.gameObject);
            }
        }

        static int Depth(Transform t)
        {
            int d = 0;
            for (var at = t; at != null; at = at.parent) d++;
            return d;
        }

        // After ConvertSockets has baked and adopted: fresh rig, depth
        // channel, and the author's layers pointed at it.
        public static void Finish(BridgeContext ctx, Dictionary<Transform, Spec> specs)
        {
            // Fields on every socket first: the light cap ranks against all of them.
            foreach (var pair in specs)
            {
                if (pair.Key == null) continue;
                var socket = pair.Key.GetComponent<YapsSocket>();
                // Already stripped, and lights and pointers need no bake, so
                // rebuild even after a failed bake.
                if (socket == null) socket = pair.Key.gameObject.AddComponent<YapsSocket>();
                socket.kind = pair.Value.IsHole ? YapsSocket.SocketKind.Hole : YapsSocket.SocketKind.Ring;
                socket.emitLights = pair.Value.EmitLights;
                // Always, even empty: an author who turned SPS's shared tag off meant it.
                socket.tags = new List<string>(pair.Value.Tags);
                // Blank would hide the stale card for converted sockets.
                socket.builtBy = BridgeDefines.Version;
            }

            int rebuilt = 0, repointed = 0;
            var left = new List<string>();
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in specs)
            {
                var root = pair.Key;
                var spec = pair.Value;
                if (root == null) continue;
                var socket = root.GetComponent<YapsSocket>();
                if (socket == null) continue;

                // No rename: the author's clips may bind paths through this object.
                YapsSocketBuilder.Build(socket);
                rebuilt++;

                // Two same-kind sockets on one bone share a label, but must not
                // share a parameter.
                string wanted = YapsSocketReactions.Parameter(socket);
                for (int n = 2; taken.Contains(wanted); n++)
                {
                    wanted = YapsSocketReactions.Parameter(socket)
                        .Replace("/Depth", n.ToString() + "/Depth");
                }
                taken.Add(wanted);
                // Local by default, 32 bits a socket when synced.
                if (!ctx.Settings.syncSocketDepthForOthers) wanted = "#" + wanted;
                string parameter = YapsSocketReactions.EnsureDepthChannel(socket, ctx.MergedController, wanted);
                // Trigger-written, so the orphan prune would otherwise take it.
                ctx.ContactParameters.Add(parameter);
                // So Remove knows this name.
                socket.builtParameter = parameter;
                if (spec.DepthParams.Count == 1)
                {
                    if (RenameParameterEverywhere(ctx.MergedController, spec.DepthParams[0], parameter))
                        repointed++;
                }
                else if (spec.DepthParams.Count > 1)
                {
                    left.Add($"{socket.name}: {spec.DepthParams.Count} depth parameters left as authored");
                }
            }

            RemoveExclusivityLayers(ctx);

            if (rebuilt > 0)
            {
                ctx.Report.Converted(Category,
                    $"Rebuilt {BridgeElements.Count(rebuilt, "socket", "sockets")} through the native builder",
                    "The same rig the YAPS toolkit builds. " +
                    (repointed > 0 ? $"{BridgeElements.Count(repointed, "socket", "sockets")} had their depth reactions repointed. " : "") +
                    (left.Count > 0 ? string.Join("; ", left) + ". " : ""));
            }
        }

        // LAST of everything writing a socket's active state: it reads which toggles
        // exist, and a toggle wired after it got overridden.
        public static void Lighthouse(BridgeContext ctx)
        {
            string lighthouse = YapsLighthouse.Build(ctx.CvrAvatar, ctx.MergedController);
            if (lighthouse == null) return;
            ctx.Report.Converted(Category, "The lighthouse: one lit socket, wearer's choice",
                "The \"Marker lights\" menu lights one socket for DPS and TPS plugs, starting Off. It syncs " +
                "(32 bits).");
        }

        // Fury's exclusivity merges at weight 0 and CVR has no runtime layer weight,
        // so they never run. Only Fury's naming at exactly 0 is removed.
        static void RemoveExclusivityLayers(BridgeContext ctx)
        {
            var controller = ctx.MergedController;
            if (controller == null) return;
            int removed = 0;
            for (int i = controller.layers.Length - 1; i >= 0; i--)
            {
                var layer = controller.layers[i];
                if (layer.defaultWeight > 0f) continue;
                if (!layer.name.Contains("Exclusivity")) continue;
                if (!layer.name.Contains("SPS") && !layer.name.Contains("VF")) continue;
                controller.RemoveLayer(i);
                removed++;
            }
            if (removed > 0)
            {
                ctx.Report.Converted(Category,
                    $"{BridgeElements.Count(removed, "dead exclusivity layer", "dead exclusivity layers")} removed",
                    "VRCFury's socket exclusivity could never run here. The sockets have their own toggles.");
            }
        }

        // Every place a controller reads a parameter by name. Clips never name it.
        public static bool RenameParameterEverywhere(AnimatorController controller, string from, string to)
        {
            if (controller == null || string.IsNullOrEmpty(from) || from == to) return false;
            bool found = false;

            var parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name != from) continue;
                // Target may exist already; then just drop the old one.
                if (parameters.Any(p => p.name == to))
                {
                    controller.RemoveParameter(i);
                    parameters = controller.parameters;
                    i--;
                }
                else
                {
                    parameters[i].name = to;
                    controller.parameters = parameters;
                }
                found = true;
            }
            if (!found) return false;

            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;
                foreach (var machine in AllMachines(layer.stateMachine))
                {
                    foreach (var state in machine.states.Select(s => s.state))
                    {
                        if (state.timeParameter == from) state.timeParameter = to;
                        if (state.speedParameter == from) state.speedParameter = to;
                        if (state.cycleOffsetParameter == from) state.cycleOffsetParameter = to;
                        if (state.mirrorParameter == from) state.mirrorParameter = to;
                        foreach (var t in state.transitions) RenameConditions(t, from, to);
                        if (state.motion is BlendTree tree) RenameTree(tree, from, to);
                        foreach (var behaviour in state.behaviours) RenameDriver(behaviour as AnimatorDriver, from, to);
                    }
                    foreach (var t in machine.anyStateTransitions) RenameConditions(t, from, to);
                    foreach (var t in machine.entryTransitions) RenameConditions(t, from, to);
                    foreach (var sub in machine.stateMachines)
                        foreach (var t in machine.GetStateMachineTransitions(sub.stateMachine)) RenameConditions(t, from, to);
                    foreach (var behaviour in machine.behaviours) RenameDriver(behaviour as AnimatorDriver, from, to);
                }
            }
            EditorUtility.SetDirty(controller);
            return true;
        }

        static IEnumerable<AnimatorStateMachine> AllMachines(AnimatorStateMachine top)
        {
            yield return top;
            foreach (var child in top.stateMachines)
                foreach (var deeper in AllMachines(child.stateMachine))
                    yield return deeper;
        }

        static void RenameConditions(AnimatorTransitionBase transition, string from, string to)
        {
            var conditions = transition.conditions;
            bool changed = false;
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i].parameter != from) continue;
                conditions[i].parameter = to;
                changed = true;
            }
            if (changed) transition.conditions = conditions;
        }

        static void RenameTree(BlendTree tree, string from, string to)
        {
            if (tree == null) return;
            if (tree.blendParameter == from) tree.blendParameter = to;
            if (tree.blendParameterY == from) tree.blendParameterY = to;
            // Direct trees weight children by name. Children is a copy, so write back.
            var children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].directBlendParameter == from) children[i].directBlendParameter = to;
                if (children[i].motion is BlendTree deeper) RenameTree(deeper, from, to);
            }
            tree.children = children;
        }

        static void RenameDriver(AnimatorDriver driver, string from, string to)
        {
            if (driver == null) return;
            foreach (var task in driver.EnterTasks.Concat(driver.ExitTasks))
            {
                if (task.targetName == from) task.targetName = to;
                if (task.aType == AnimatorDriverTask.SourceType.Parameter && task.aName == from) task.aName = to;
                if (task.bType == AnimatorDriverTask.SourceType.Parameter && task.bName == from) task.bName = to;
            }
        }
    }
}
#endif
