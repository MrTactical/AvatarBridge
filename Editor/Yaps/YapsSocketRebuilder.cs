// The rebuild: a converted socket stops being VRCFury's rig retuned and
// becomes the thing the YAPS tool builds. Read what Fury placed, strip
// Fury's machinery, let the native builder emit a fresh rig, and point
// the author's depth reactions at the rebuilt channel. Every socket bug
// that reached a user lived in the difference between the two rigs.
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
        const string Category = "YAPS penetration system";

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
            // What the author's reaction layers read, one entry per stay
            // task on a penetrating-filtered trigger.
            public List<string> DepthParams = new List<string>();
            public int Lights, Pointers, Triggers;
        }

        // Runs before anything retunes the sockets. Kind, light emission
        // and the depth channel are read here because the evidence is
        // exactly what gets stripped.
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
                    $"Stripped VRCFury's socket rig from {specs.Count} socket(s)",
                    $"{lights} marker light(s), {pointers} pointer(s) and {triggers} depth " +
                    "trigger(s) removed. Each socket keeps its place, its mesh and its menu " +
                    "entry; the rig itself is rebuilt from scratch by the same code the YAPS " +
                    "window uses, so a converted socket and a tool-built socket are the same " +
                    "thing from here on. Fury's rig arrives baked half-off and every switch " +
                    "the enable service would have flipped is somewhere a bug can live; a " +
                    "fresh rig has no switches to miss.");
            }
            return specs;
        }

        // VRCFury bakes every socket branch inactive and relies on SPS's
        // enable service, which does not survive conversion. Wake the
        // socket, everything under it, and every STUCK ancestor up to the
        // avatar root. An object some clip can switch stays as found: the
        // menu owns it, and only what NOTHING can switch is woken. This
        // used to key off the marker lights; the strip removes those, so
        // it keys off the socket itself.
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
                ctx.Report.Converted(Category, $"{woken} object(s) on the socket chains woken",
                    "VRCFury leaves the socket branch, its second head and \"Original Object\" " +
                    "switched off for its own enable service to sort out, and that service is " +
                    "deleted here. Anything a menu toggle can switch was left exactly as found.");
            }
        }

        // Every object path a clip can switch — counting only layers that
        // can actually assert. Fury merges its socket exclusivity at
        // weight zero, and a path animated ONLY by a dead layer is not
        // controllable by anything; treating it as menu-owned left sockets
        // dark, which is the exact bug the rebuild exists to end. The
        // first layer always runs regardless of its stated weight.
        public static HashSet<string> Switchable(BridgeContext ctx)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            if (ctx.MergedController == null) return paths;
            var layers = ctx.MergedController.layers;
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

        // OGB and its kin read plug tips too, so a haptics receiver the
        // user chose to keep looks exactly like a depth trigger to the
        // tip filter. Its parameters say what it is. Kept in step with
        // SystemStripper.HapticsParamPrefixes, which cannot be referenced
        // here: the stripper only exists when the VRChat SDK does.
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

        // Fury's rig off the subtree: protocol lights, socket-role
        // pointers, and the depth triggers whose parameters were just
        // recorded. The renderer, the root and anything user-made stay.
        // Haptics and any trigger not filtered to a plug tip stay too.
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
            // Only a channel that can be repointed is stripped. Several
            // parameters cannot collapse to one without changing what
            // plays when, so those sockets keep their triggers and their
            // layers exactly as authored.
            if (spec.DepthParams.Count <= 1)
            {
                foreach (var t in root.GetComponentsInChildren<CVRAdvancedAvatarSettingsTrigger>(true).ToList())
                {
                    if (!ListensForTips(t) || IsHaptics(t)) continue;
                    spec.Triggers++;
                    RemoveHost(t.transform, root, t);
                }
            }
            // Fury's WorldSpace plumbing: constraints that pinned the
            // stripped lights. Below the root only — a constraint ON the
            // root is what places the socket itself and stays.
            foreach (var c in root.GetComponentsInChildren<UnityEngine.Animations.IConstraint>(true).ToList())
            {
                var component = c as Component;
                if (component == null || component.transform == root) continue;
                RemoveHost(component.transform, root, component);
            }
            PruneEmpty(root);
        }

        // Fury's rig off a converted plug, before the fresh announce: its
        // tracker light would sit beside the new one, doubling the Raliv
        // signal and taking a second vertex slot, and its pointers win the
        // announce's dedupe over fresh ones on the measured frame. Only a
        // plug that converted is stripped; a failed one keeps what works.
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

        // The component's object when nothing else lives there, otherwise
        // just the component. The root itself is never removed.
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

        // Holders Fury made that now hold nothing. Bottom-up, so an empty
        // chain goes in one pass.
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
            // Fields first, on every socket, then the builds: the light
            // cap ranks a socket against ALL of them, so building while
            // half still carry default fields hands out too many slots.
            foreach (var pair in specs)
            {
                if (pair.Key == null) continue;
                var socket = pair.Key.GetComponent<YapsSocket>();
                // A failed bake reported itself, but the socket was already
                // stripped, and lights and pointers need no bake. Rebuild
                // the rig anyway or the failure costs more than it used to.
                if (socket == null) socket = pair.Key.gameObject.AddComponent<YapsSocket>();
                socket.kind = pair.Value.IsHole ? YapsSocket.SocketKind.Hole : YapsSocket.SocketKind.Ring;
                socket.emitLights = pair.Value.EmitLights;
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

                // No rename: the author's clips may bind paths through this
                // object, and a renamed path is a silenced channel. The
                // label, and so the depth parameter, derives from the bone.
                YapsSocketBuilder.Build(socket);
                rebuilt++;

                // Two same-kind sockets on one bone share a label; sharing
                // a parameter as well would hand both reactions to whichever
                // socket a plug touched last.
                string wanted = YapsSocketReactions.Parameter(socket);
                for (int n = 2; taken.Contains(wanted); n++)
                {
                    wanted = YapsSocketReactions.Parameter(socket)
                        .Replace("/Depth", n.ToString() + "/Depth");
                }
                taken.Add(wanted);
                // The sync choice is the wearer's setting, same as before
                // the rebuild: local and free by default, synced at 32
                // bits a socket when they ask the room to see the shapes.
                // The setting worked by matching Fury's names, which the
                // repoint erases, so it is honored here by the prefix.
                if (!ctx.Settings.syncSocketDepthForOthers) wanted = "#" + wanted;
                string parameter = YapsSocketReactions.EnsureDepthChannel(socket, ctx.MergedController, wanted);
                // Trigger-written, so nothing in the controller reads it unless
                // the repoint happened, and the orphan prune would take it.
                ctx.ContactParameters.Add(parameter);
                if (spec.DepthParams.Count == 1)
                {
                    if (RenameParameterEverywhere(ctx.MergedController, spec.DepthParams[0], parameter))
                        repointed++;
                }
                else if (spec.DepthParams.Count > 1)
                {
                    // Several channels was a deliberate authoring choice;
                    // collapsing them to one would change what plays when.
                    left.Add($"{socket.name}: {spec.DepthParams.Count} depth parameters left as authored");
                }
            }

            RemoveExclusivityLayers(ctx);

            if (rebuilt > 0)
            {
                ctx.Report.Converted(Category,
                    $"Rebuilt {rebuilt} socket(s) through the native builder",
                    "Marker lights within the light budget, pointers with their self twins, and " +
                    "a depth trigger writing a synced parameter — the exact rig the YAPS window " +
                    "builds, because it is the same code. " +
                    (repointed > 0
                        ? $"{repointed} socket(s) had their depth reactions repointed onto the " +
                          "rebuilt channel, so the author's animations play from the new trigger. "
                        : "") +
                    (left.Count > 0 ? string.Join("; ", left) + ". " : ""));
            }
        }

        // LAST of everything that writes a socket's active state. The
        // lighthouse asserts the chosen socket ON as well as lit, and a
        // layer wins by coming later, so it has to follow the wired
        // toggles. A user on a converted avatar switched the mouth on
        // from Fury's toggle, saw it in the hierarchy, and held a DPS
        // prop to a dark pair the dropdown still had at the anus.
        public static void Lighthouse(BridgeContext ctx)
        {
            string lighthouse = YapsLighthouse.Build(ctx.CvrAvatar, ctx.MergedController);
            if (lighthouse == null) return;
            ctx.Report.Converted(Category, "The lighthouse: one lit socket, wearer's choice",
                "Every lit-capable socket carries its marker pair and the \"Marker lights\" " +
                "dropdown lights exactly one — and switches that socket on, so choosing it " +
                "is the whole job for a DPS or TPS toy. It starts on Off: nothing is lit " +
                "until the wearer says so. A disabled light never competes for Unity's four " +
                "vertex-light slots, which is what makes several DPS-findable sockets on one " +
                "avatar work at all; before this, a second lit socket evicted the hole's root " +
                "light and holes broke while rings kept working. The selector syncs (32 bits) " +
                "so everyone sees the same socket lit.");
        }

        // Fury's socket exclusivity merges at weight zero, and ChilloutVR
        // has no runtime layer-weight control, so the layers can never
        // assert: pure residue, and residue this table of bugs grew in.
        // Only the exact Fury naming at exactly zero weight is removed.
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
                    $"{removed} dead exclusivity layer(s) removed",
                    "VRCFury's socket exclusivity merges at weight zero and ChilloutVR has no " +
                    "runtime layer-weight control, so these could never assert — but their clips " +
                    "made the sockets they switch off look menu-owned to every check that walks " +
                    "the controller. The rebuilt sockets have their own toggles.");
            }
        }

        // The one rename the repoint needs: every place a controller reads
        // a parameter by name. Clips are deliberately untouched — they
        // animate blendshapes and never name the parameter.
        public static bool RenameParameterEverywhere(AnimatorController controller, string from, string to)
        {
            if (controller == null || string.IsNullOrEmpty(from) || from == to) return false;
            bool found = false;

            var parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name != from) continue;
                // The target may already exist (a second socket repointed
                // here first); then the old parameter is simply dropped.
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
                    }
                    foreach (var t in machine.anyStateTransitions) RenameConditions(t, from, to);
                    foreach (var t in machine.entryTransitions) RenameConditions(t, from, to);
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
            foreach (var child in tree.children)
                if (child.motion is BlendTree deeper) RenameTree(deeper, from, to);
        }
    }
}
#endif
