#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge
{
    // The Toolkit's "PhysBones to cloth" card: the converter's physics pass alone, in place, on
    // an avatar already on ChilloutVR that still carries PhysBones. Here rather than in the
    // Toolkit, which the add-on's package ships without the physics, so it is handed in.
    [InitializeOnLoad]
    static class PhysBonesInPlace
    {
        const string Category = "PhysBones";
        const string UndoName = "PhysBones to cloth";

        static PhysBonesInPlace() => ToolkitPanel.ConvertPhysBones = Run;

        // folder is asked only when a controller copy is written, so a run that rewires
        // nothing makes no output folder.
        internal static BridgeReport Run(GameObject target, BridgeSettings settings, System.Func<BridgeReport, string> folder)
        {
            var report = new BridgeReport();
            // A VRChat avatar, not a port that kept its descriptor: deleting its PhysBones in place
            // breaks it for VRChat, and the converter does the same work on a copy.
            if (target.GetComponent<VRCAvatarDescriptor>() != null && target.GetComponent<CVRAvatar>() == null)
            {
                report.Warning(Category, "This is a VRChat avatar",
                    "Convert it in the Convert tab instead: that works on a copy and rewires the toggles " +
                    "that switch its physics. This card is for an avatar already on ChilloutVR that still " +
                    "carries PhysBones.");
                return report;
            }
            var physBones = target.GetComponentsInChildren<VRCPhysBone>(true);
            if (physBones.Length == 0) return report;
            // With Delete off a second press would add a second set on the same bones.
            foreach (string name in new[] { "MagicaCloth Phys", "DynamicBone Phys" })
            {
                var home = target.transform.Find(name);
                if (home == null || home.childCount == 0) continue;
                report.Warning(Category, "It already carries converted cloth",
                    $"\"{name}\" holds {BridgeElements.Count(home.childCount, "cloth object", "cloth objects")}. " +
                    "Converting again adds a second set on the same bones, and the two fight. " +
                    $"Delete \"{name}\" first, then press again.");
                return report;
            }

            // Its synthesizer cannot see cloth placed by hand, and would add a second set on those bones.
            settings.addPhysicsToRiggedStyles = false;
            // An avatar already on ChilloutVR toggles through clips, which the collider repoint
            // carries onto hosts; native targets would leave an off collider's host live.
            settings.toggleStyle = ToggleStyle.AnimatorLayers;
            var avatar = target.GetComponent<CVRAvatar>();
            var animator = target.GetComponent<Animator>();
            var shipped = AvatarSurvey.ShippedController(avatar, animator);
            var over = avatar != null && avatar.overrides != null ? avatar.overrides
                : animator != null ? animator.runtimeAnimatorController as AnimatorOverrideController : null;
            // A slot playing something else is not what ships.
            if (over != null && BridgeContext.Underlying(over) != shipped) over = null;
            var ctx = new BridgeContext
            {
                Settings = settings,
                Report = report,
                Target = target,
                CvrAvatar = avatar,
                MergedController = shipped,
                Standalone = true,
            };
            // Read before the run, which deletes the components the clips point at.
            var touches = Touches(ctx, physBones);
            string left = null;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            Undo.RegisterFullObjectHierarchyUndo(target, UndoName);
            var before = new HashSet<Component>(target.GetComponentsInChildren<Component>(true));
            try
            {
                PhysBoneConverter.Run(ctx);
                // The override's own clips too: one it swaps in is refused, not missed.
                if (shipped != null && (ctx.ConvertedPhysicsChains.Count > 0 || ctx.PhysicsColliderHosts.Count > 0)
                    && shipped.animationClips.Concat(over != null ? over.animationClips : new AnimationClip[0]).Any(touches))
                {
                    left = Refusal(shipped, over, touches) ?? RewireInCopy(ctx, shipped, over, touches, folder);
                }
            }
            finally
            {
#if AVATARBRIDGE_MAGICA
                MagicaClothWriter.ReleaseCaches();
#endif
                // The writers make objects and components without Undo. Recorded after, topmost
                // only: a child goes with its new parent.
                foreach (var c in target.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || before.Contains(c)) continue;
                    if (c is Transform t)
                    {
                        if (before.Contains(t.parent)) Undo.RegisterCreatedObjectUndo(t.gameObject, UndoName);
                    }
                    else if (before.Contains(c.transform)) Undo.RegisterCreatedObjectUndo(c, UndoName);
                }
                Undo.CollapseUndoOperations(group);
            }

            if (left != null)
            {
                report.Warning(Category, "Animations left as they are", left +
                    " Animations that switch or animate a PhysBone no longer reach the cloth: cloth for a part " +
                    "that is off at rest stays off, and toggles that hide a part with physics leave its cloth " +
                    "running. Rows here saying a toggle switches the cloth do not apply.");
            }
            // Destroyed components read as null: the run deleted them, converted or skipped.
            if (target.GetComponent<VRCAvatarDescriptor>() != null && physBones.Any(pb => pb == null))
            {
                report.Warning(Category, "It also carries a VRChat avatar descriptor",
                    "Its PhysBones are deleted, so if this same object still uploads to VRChat, it uploads " +
                    "without them. To keep them, press Ctrl+Z, turn off \"Delete PhysBones after converting\" " +
                    "in the Convert tab's Physics options, and press again.");
            }
            if (settings.sizePhysicsForLargest && settings.physicsTarget == PhysicsTarget.MagicaCloth2
                && ctx.ConvertedPhysicsChains.Count > 0)
            {
                report.Approximated(Category, "Colliders fit the body at rest",
                    "\"Size for the largest a slider makes the body\" reads the blendshapes the VRChat " +
                    "controllers push, and there are none here, so collision fits the mesh as it stands.");
            }
            return report;
        }

        static string Sample(List<string> names) =>
            string.Join(", ", names.Take(6)) + (names.Count > 6 ? ", …" : "");

        // Every clip one of the three clip passes would edit: a PhysBone or collider curve, or
        // an object toggle at or above one. The rewire matches the PhysBone's own object, the
        // collider pass each collider's.
        static System.Func<AnimationClip, bool> Touches(BridgeContext ctx, VRCPhysBone[] physBones)
        {
            var root = ctx.Target.transform;
            var holders = new HashSet<string>(System.StringComparer.Ordinal);
            var starts = physBones.SelectMany(pb => new[] { pb.transform, pb.rootTransform })
                .Concat(ctx.Target.GetComponentsInChildren<VRCPhysBoneCollider>(true).Select(c => c.transform));
            foreach (var start in starts)
            {
                for (var t = start; t != null && t != root; t = t.parent) holders.Add(ctx.PathInTarget(t));
            }
            return clip => clip != null && AnimationUtility.GetCurveBindings(clip).Any(b =>
                b.type == typeof(VRCPhysBone) || b.type == typeof(VRCPhysBoneCollider)
                || (b.type == typeof(GameObject) && b.propertyName == "m_IsActive" && holders.Contains(b.path)));
        }

        // The rewire reads the base controller's clips, so an override swapping in one that
        // drives physics would keep playing the unrewired clip.
        static string Refusal(AnimatorController shipped, AnimatorOverrideController over, System.Func<AnimationClip, bool> touches)
        {
            if (over == null) return null;
            if (over.runtimeAnimatorController != shipped)
                return "Its override controller wraps another override controller, which this card does not copy.";
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            over.GetOverrides(pairs);
            var swapped = pairs.Where(p => p.Value != null && touches(p.Value)).Select(p => p.Value.name).Distinct().ToList();
            return swapped.Count == 0 ? null : $"Its override controller swaps in {Sample(swapped)}, which this card does not rewire.";
        }

        // Null once the avatar runs the rewired copy; otherwise why nothing was wired, with the
        // copies and their rows gone. The rows wait in a quiet report, so a refusal leaves no
        // console line describing a rewire that was thrown away.
        static string RewireInCopy(BridgeContext ctx, AnimatorController shipped, AnimatorOverrideController over,
            System.Func<AnimationClip, bool> touches, System.Func<BridgeReport, string> folder)
        {
            var report = ctx.Report;
            string name = folder(report) + "/" + CvrSetup.SafeFolderName(ctx.Target.name) + " cloth";
            var copy = Copy(shipped, name + ".controller");
            if (copy == null) return "Its controller could not be copied.";
            string copyPath = AssetDatabase.GetAssetPath(copy);
            AnimatorOverrideController overCopy = null;
            var rows = ctx.Report = new BridgeReport { Quiet = true };
            bool wired = false;
            try
            {
                var owned = new Dictionary<Motion, Motion>();
                foreach (var layer in copy.layers) Own(layer.stateMachine, copyPath, touches, owned);
                // A synced layer keeps its own motions, which the walk does not reach, so they
                // would be edited where they lie.
                var missed = copy.animationClips.Where(c => touches(c) && AssetDatabase.Contains(c)
                    && AssetDatabase.GetAssetPath(c) != copyPath).Select(c => c.name).Distinct().ToList();
                if (missed.Count > 0) return $"{Sample(missed)} play from a synced layer, which this card does not copy.";

                ctx.MergedController = copy;
                AnimatorMerger.ResetMaskCache();
                AnimatorMerger.RewirePhysicsToggles(copy, ctx);
                foreach (var layer in copy.layers) AnimatorAssetSaver.EmbedLayer(layer, copy);
                PhysBoneConverter.RepointColliderEnableCurves(ctx);
                PhysBoneConverter.ReportAnimatedPhysBoneProperties(ctx);
                foreach (var clip in copy.animationClips.Where(c => c != null && AssetDatabase.GetAssetPath(c) == copyPath))
                    EditorUtility.SetDirty(clip);
                EditorUtility.SetDirty(copy);

                var swaps = new Dictionary<Object, Object> { [shipped] = copy };
                if (over != null)
                {
                    overCopy = Copy(over, name + ".overrideController");
                    if (overCopy == null) return "Its override controller could not be copied.";
                    overCopy.runtimeAnimatorController = copy;
                    EditorUtility.SetDirty(overCopy);
                    // Measured, not assumed: a swap keyed on a clip the copy replaced is gone.
                    if (Live(overCopy) != Live(over))
                        return "Its override controller swaps clips that a copy of the controller would lose.";
                    swaps[over] = overCopy;
                }
                AssetDatabase.SaveAssets();
                if (!Wire(ctx.Target, swaps)) return "No slot on the avatar holds its controller itself.";
                wired = true;
            }
            finally
            {
                ctx.Report = report;
                if (wired)
                {
                    foreach (var e in rows.Entries) report.Add(e.Status, e.Category, e.Subject, e.Detail);
                }
                else
                {
                    AssetDatabase.DeleteAsset(copyPath);
                    if (overCopy != null) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(overCopy));
                }
            }

            report.Converted(Category, "Animations rewired in a copy of the controller", copyPath +
                (overCopy != null ? $", and {AssetDatabase.GetAssetPath(overCopy)} around it" : "") +
                ". The avatar now runs the copy. Its own controller and clips are as they were: Ctrl+Z " +
                "points it back at them and leaves the copy unused in that folder.");
            var settings = ctx.CvrAvatar != null ? ctx.CvrAvatar.avatarSettings : null;
            var based = settings != null ? BridgeContext.Underlying(settings.baseController) : null;
            if (based != null && based != copy)
            {
                report.Approximated(Category, "Create Controller would undo the rewiring",
                    "The CVRAvatar's Create Controller rebuilds the uploaded controller from the base " +
                    "controller, which this card left as it was. To rebuild it, press Ctrl+Z here first, " +
                    "then Create Controller, then this card again.");
            }
            return null;
        }

        // The folder exists by now (WriteFolder made it), so CopyAsset can land in it.
        static T Copy<T>(T source, string path) where T : Object
        {
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            return AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path) ? AssetDatabase.LoadAssetAtPath<T>(path) : null;
        }

        static int Live(AnimatorOverrideController over)
        {
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            over.GetOverrides(pairs);
            return pairs.Count(p => p.Value != null);
        }

        // The copy still points at the user's clips and at trees saved in other assets. Each one
        // the passes would edit is cloned in memory; EmbedLayer then stores it in the copy.
        static void Own(AnimatorStateMachine machine, string copyPath, System.Func<AnimationClip, bool> touches,
            Dictionary<Motion, Motion> owned)
        {
            if (machine == null) return;
            foreach (var child in machine.states) child.state.motion = Own(child.state.motion, copyPath, touches, owned);
            foreach (var child in machine.stateMachines) Own(child.stateMachine, copyPath, touches, owned);
        }

        static Motion Own(Motion motion, string copyPath, System.Func<AnimationClip, bool> touches,
            Dictionary<Motion, Motion> owned)
        {
            if (motion == null) return null;
            if (owned.TryGetValue(motion, out var known)) return known;
            Motion mine = motion;
            bool foreign = AssetDatabase.GetAssetPath(motion) != copyPath;
            if (motion is BlendTree tree)
            {
                var children = tree.children;
                bool changed = false;
                for (int i = 0; i < children.Length; i++)
                {
                    var child = Own(children[i].motion, copyPath, touches, owned);
                    changed |= child != children[i].motion;
                    children[i].motion = child;
                }
                if (changed)
                {
                    if (foreign) mine = tree = Object.Instantiate(tree);
                    // Assigning children re-derives thresholds under automatic mode, as in the rewire.
                    bool auto = tree.useAutomaticThresholds;
                    tree.useAutomaticThresholds = false;
                    tree.children = children;
                    tree.useAutomaticThresholds = auto;
                }
            }
            else if (foreign && motion is AnimationClip clip && touches(clip))
            {
                mine = Object.Instantiate(clip);
            }
            if (mine != motion)
            {
                mine.name = motion.name;
                mine.hideFlags = HideFlags.None;
            }
            owned[motion] = mine;
            return mine;
        }

        // Every slot that held the original, as Free wins wires its copy, but onto copies only:
        // retargeting the user's own override would reach every avatar sharing it. False when no
        // slot held it.
        static bool Wire(GameObject target, Dictionary<Object, Object> swaps)
        {
            bool hit = false;
            T Swap<T>(T held) where T : Object
            {
                if (held == null || !swaps.TryGetValue(held, out var to)) return held;
                hit = true;
                return (T)to;
            }
            var avatar = target.GetComponent<CVRAvatar>();
            if (avatar != null)
            {
                Undo.RecordObject(avatar, UndoName);
                avatar.overrides = Swap(avatar.overrides);
                if (avatar.avatarSettings != null)
                {
                    avatar.avatarSettings.baseController = Swap(avatar.avatarSettings.baseController);
                    avatar.avatarSettings.baseOverrideController = Swap(avatar.avatarSettings.baseOverrideController);
                    // What Create Controller made. Left behind, the CCK offers to attach it again,
                    // which would put the unrewired override back.
                    avatar.avatarSettings.animator = Swap(avatar.avatarSettings.animator);
                    avatar.avatarSettings.overrides = Swap(avatar.avatarSettings.overrides);
                }
                EditorUtility.SetDirty(avatar);
            }
            var animator = target.GetComponent<Animator>();
            if (animator != null)
            {
                Undo.RecordObject(animator, UndoName);
                animator.runtimeAnimatorController = Swap(animator.runtimeAnimatorController);
            }
            return hit;
        }
    }
}
#endif
