#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using ABI.CCK.Components;

namespace AvatarBridge
{
    // VRC Contact system -> ChilloutVR pointers/triggers:
    //
    //   VRCContactSender    -> CVRPointer (one per collision tag) + trigger collider
    //   VRCContactReceiver  -> CVRAdvancedAvatarSettingsTrigger driving the parameter
    //   VRChat's built-in hand/head/torso colliders -> CVRPointers with standard tags,
    //     created only for tags the avatar's receivers actually listen to.
    public static class ContactsConverter
    {
        const string Category = "Contacts";

        public static void Run(BridgeContext ctx)
        {
            if (!ctx.Settings.convertContacts)
            {
                return;
            }

            var senders = ctx.Target.GetComponentsInChildren<VRCContactSender>(true);
            var receivers = ctx.Target.GetComponentsInChildren<VRCContactReceiver>(true);

            var listenedTags = new HashSet<string>();
            foreach (var receiver in receivers)
            {
                foreach (var tag in receiver.collisionTags)
                {
                    listenedTags.Add(tag);
                }
            }

            foreach (var sender in senders)
            {
                ConvertSender(ctx, sender);
            }
            foreach (var receiver in receivers)
            {
                ConvertReceiver(ctx, receiver);
            }

            ReportUnreachableTags(ctx, listenedTags);

            if (ctx.Settings.createDefaultColliderPointers && listenedTags.Count > 0)
            {
                CreateDefaultColliderPointers(ctx, listenedTags);
            }

            if (senders.Length > 0 || receivers.Length > 0)
            {
                ctx.Report.Converted(Category, $"{senders.Length} sender(s), {receivers.Length} receiver(s) converted");
            }

        }

        static void ConvertSender(BridgeContext ctx, VRCContactSender sender)
        {
            if (sender.collisionTags.Count == 0)
            {
                Object.DestroyImmediate(sender);
                return;
            }

            foreach (var tag in sender.collisionTags.Distinct())
            {
                var contactObject = CreateContactObject(AnchorOf(sender), "CVRPointer_" + tag,
                    sender.shapeType, sender.radius, sender.position, sender.height, sender.rotation);
                var pointer = contactObject.AddComponent<CVRPointer>();
                pointer.type = tag;
                RecordHost(ctx, sender, isSender: true, contactObject);
            }
            ctx.Report.Converted(Category, PathOf(ctx, sender.transform),
                $"Sender -> CVRPointer ({string.Join(", ", sender.collisionTags)})");
            Object.DestroyImmediate(sender);
        }

        static void ReportUnreachableTags(BridgeContext ctx, HashSet<string> listenedTags)
        {
            var custom = listenedTags
                .Where(t => !string.IsNullOrEmpty(t)
                            && !ChilloutVrPointerTypes.ContainsKey(t)
                            && !UniversalTags.Contains(t))
                .OrderBy(t => t).ToList();
            if (custom.Count == 0)
            {
                return;
            }
            ctx.Report.Approximated(Category,
                $"{custom.Count} contact tag(s) only this avatar can trigger",
                $"\"{string.Join("\", \"", custom.Take(8))}\"" + (custom.Count > 8 ? ", …" : "") +
                ". A contact fires when something sends a matching tag, and these aren't body parts " +
                "— nothing any other player carries sends them. Between two copies of this avatar " +
                "they work exactly as they did in VRChat; to anyone else those receivers do " +
                "nothing. That's usually deliberate (an avatar's own private system), so nothing " +
                "was changed. If you wanted strangers to set one off, add a body-part tag to it: " +
                "\"Hand\", \"HandL\"/\"HandR\" or \"FingerIndexR\" all reach an ordinary " +
                "ChilloutVR player, because the conversion pairs them with that player's own " +
                "pointer names.");
        }

        static readonly HashSet<string> UniversalTags = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "Head", "Torso", "Foot", "FootL", "FootR",
            "FingerMiddle", "FingerMiddleL", "FingerMiddleR",
            "FingerRing", "FingerRingL", "FingerRingR",
            "FingerLittle", "FingerLittleL", "FingerLittleR",
        };

        static readonly Dictionary<string, string[]> ChilloutVrPointerTypes =
            new Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "Hand",         new[] { "grab" } },          // "Hand" already matches by name
                { "HandL",        new[] { "LeftHand" } },
                { "HandR",        new[] { "RightHand" } },
                { "Finger",       new[] { "index" } },
                { "FingerL",      new[] { "index" } },
                { "FingerR",      new[] { "index" } },
                { "FingerIndex",  new[] { "index" } },
                { "FingerIndexL", new[] { "index" } },
                { "FingerIndexR", new[] { "index" } },
            };

        static string[] WithChilloutVrPointerTypes(IEnumerable<string> vrcTags)
        {
            var types = new List<string>();
            foreach (string tag in vrcTags)
            {
                if (string.IsNullOrEmpty(tag) || types.Contains(tag))
                {
                    continue;
                }
                types.Add(tag);
                if (!ChilloutVrPointerTypes.TryGetValue(tag, out var equivalents))
                {
                    continue;
                }
                foreach (string equivalent in equivalents)
                {
                    if (!types.Contains(equivalent))
                    {
                        types.Add(equivalent);
                    }
                }
            }
            return types.ToArray();
        }

        static void ConvertReceiver(BridgeContext ctx, VRCContactReceiver receiver)
        {
            if (receiver.collisionTags.Count == 0 || string.IsNullOrEmpty(receiver.parameter))
            {
                Object.DestroyImmediate(receiver);
                return;
            }

            var contactObject = CreateContactObject(AnchorOf(receiver), "CVRTrigger_" + receiver.parameter,
                receiver.shapeType, receiver.radius, receiver.position, receiver.height, receiver.rotation);
            RecordHost(ctx, receiver, isSender: false, contactObject);

            var trigger = contactObject.AddComponent<CVRAdvancedAvatarSettingsTrigger>();
            trigger.useAdvancedTrigger = true;
            trigger.isLocalInteractable = receiver.allowSelf;
            trigger.isNetworkInteractable = receiver.allowOthers;
            trigger.allowedTypes = WithChilloutVrPointerTypes(receiver.collisionTags);

            string typeName = receiver.receiverType.ToString();
            if (typeName.Contains("Constant"))
            {
                trigger.enterTasks.Add(MakeTask(receiver.parameter, 1f, 0f));
                trigger.exitTasks.Add(MakeTask(receiver.parameter, 0f, 0f));
                ctx.Report.Approximated(Category, PathOf(ctx, receiver.transform),
                    $"Constant receiver \"{receiver.parameter}\": exit resets to 0 even if a second pointer is still inside.");
            }
            else if (typeName.Contains("OnEnter"))
            {
                trigger.enterTasks.Add(MakeTask(receiver.parameter, 1f, 0f));
                trigger.enterTasks.Add(MakeTask(receiver.parameter, 0f, 1f / 60f));
                ctx.Report.Converted(Category, PathOf(ctx, receiver.transform),
                    $"OnEnter receiver -> trigger pulse on \"{receiver.parameter}\"");
            }
            else // Proximity
            {
                trigger.stayTasks.Add(new CVRAdvancedAvatarSettingsTriggerTaskStay
                {
                    updateMethod = CVRAdvancedAvatarSettingsTriggerTaskStay.UpdateMethod.SetFromDistance,
                    settingName = receiver.parameter
                    // No min/max. The client computes proximity as
                    // 1 at the centre, exactly like VRChat, and
                    // SetFromDistance writes the value raw.
                });
                ctx.Report.Converted(Category, PathOf(ctx, receiver.transform),
                    $"Proximity receiver -> distance-driven \"{receiver.parameter}\"");
            }

            ctx.ContactParameters.Add(receiver.parameter);
            Object.DestroyImmediate(receiver);
        }

        static CVRAdvancedAvatarSettingsTriggerTask MakeTask(string parameter, float value, float delay)
        {
            return new CVRAdvancedAvatarSettingsTriggerTask
            {
                updateMethod = CVRAdvancedAvatarSettingsTriggerTask.UpdateMethod.Override,
                settingName = parameter,
                settingValue = value,
                delay = delay,
                holdTime = 0f
            };
        }

        static GameObject AnchorOf(VRC.Dynamics.ContactBase contact)
        {
            return contact.rootTransform != null
                ? contact.rootTransform.gameObject
                : contact.gameObject;
        }

        static void RecordHost(BridgeContext ctx, Component original, bool isSender, GameObject host)
        {
            string originalPath = BridgeContext.RelativePath(ctx.Target.transform, original.transform);
            string hostPath = BridgeContext.RelativePath(ctx.Target.transform, host.transform);
            var key = (originalPath, isSender);
            if (!ctx.ContactHosts.TryGetValue(key, out var hosts))
            {
                ctx.ContactHosts[key] = hosts = new List<string>();
            }
            hosts.Add(hostPath);
        }

        internal static void RepointContactEnableCurves(BridgeContext ctx)
        {
            if (ctx.MergedController == null || ctx.ContactHosts.Count == 0)
            {
                return;
            }

            var clips = new HashSet<AnimationClip>();
            foreach (var clip in ctx.MergedController.animationClips)
            {
                if (clip != null)
                {
                    clips.Add(clip);
                }
            }

            int repointed = 0;
            int mirrored = 0;
            var dropped = new SortedSet<string>(StableSampleOrder.Instance);
            foreach (var clip in clips)
            {
                // A toggle that animates the contact's OBJECT rather than
                // the component gated the contact just as hard in VRChat:
                // the component died with its container. The host is
                // parented at the shape's anchor, not under the container,
                // so the curve is copied onto it — copied, not moved,
                // because the container often holds the reaction's sound
                // and particles too.
                foreach (var binding in UnityEditor.AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(GameObject) || binding.propertyName != "m_IsActive")
                    {
                        continue;
                    }
                    // Ancestors count: deactivating a parent killed every
                    // contact below it, and toggles usually switch the
                    // group object rather than each component's own.
                    foreach (var entry in ctx.ContactHosts)
                    {
                        string owner = entry.Key.path;
                        if (owner != binding.path
                            && !owner.StartsWith(binding.path + "/", System.StringComparison.Ordinal))
                        {
                            continue;
                        }
                        var curve = UnityEditor.AnimationUtility.GetEditorCurve(clip, binding);
                        foreach (var hostPath in entry.Value)
                        {
                            var hostBinding = UnityEditor.EditorCurveBinding.FloatCurve(
                                hostPath, typeof(GameObject), "m_IsActive");
                            if (UnityEditor.AnimationUtility.GetEditorCurve(clip, hostBinding) == null)
                            {
                                UnityEditor.AnimationUtility.SetEditorCurve(clip, hostBinding, curve);
                                mirrored++;
                            }
                        }
                    }
                }

                foreach (var binding in UnityEditor.AnimationUtility.GetCurveBindings(clip))
                {
                    bool sender = binding.type == typeof(VRCContactSender);
                    bool receiver = binding.type == typeof(VRCContactReceiver);
                    if (!sender && !receiver)
                    {
                        continue;
                    }
                    var curve = UnityEditor.AnimationUtility.GetEditorCurve(clip, binding);
                    // Whatever happens below, the original binding goes: its component is deleted,
                    // so leaving it means a curve that silently does nothing.
                    UnityEditor.AnimationUtility.SetEditorCurve(clip, binding, null);

                    if (!ctx.ContactHosts.TryGetValue((binding.path, sender), out var hosts))
                    {
                        // The contact this drove was skipped rather than converted.
                        dropped.Add($"\"{clip.name}\" -> {binding.path} ({binding.propertyName})");
                        continue;
                    }

                    string prop = binding.propertyName;

                    // What each property maps to, each verdict from the
                    // shipped client rather than hope:
                    //   m_Enabled     -> host object active (the backing
                    //                    contact registers in OnEnable).
                    //   position.xyz  -> the host transform carries the
                    //                    offset, 1:1 onto m_LocalPosition.
                    //   allowSelf/allowOthers/localOnly -> baked into the
                    //                    backing contact at Create and never
                    //                    read again, so those drop with the
                    //                    warning.
                    UnityEditor.EditorCurveBinding? target = null;
                    if (prop == "m_Enabled")
                    {
                        target = UnityEditor.EditorCurveBinding.FloatCurve(null, typeof(GameObject), "m_IsActive");
                    }
                    else if (prop.StartsWith("position.", System.StringComparison.Ordinal))
                    {
                        string axis = prop.Substring("position.".Length);
                        target = UnityEditor.EditorCurveBinding.FloatCurve(null, typeof(Transform), "m_LocalPosition." + axis);
                    }

                    if (target == null)
                    {
                        dropped.Add($"\"{clip.name}\" -> {binding.path} ({prop})");
                        continue;
                    }
                    foreach (var hostPath in hosts)
                    {
                        var tb = target.Value;
                        tb.path = hostPath;
                        UnityEditor.AnimationUtility.SetEditorCurve(clip, tb, curve);
                    }
                    repointed++;
                }
            }

            if (repointed > 0 || mirrored > 0)
            {
                ctx.Report.Converted(Category, $"{repointed + mirrored} contact animation(s) rewired",
                    "Curves that switched a VRChat contact on and off now toggle the converted " +
                    "contact's own object, and curves that MOVED one (a receiver riding a scaled " +
                    "body part) now drive the converted contact's offset — the forms ChilloutVR " +
                    "honours. " +
                    (mirrored > 0
                        ? $"{mirrored} of them switched the contact's parent object rather than " +
                          "the component — in VRChat the contact died with its container, and " +
                          "the converted zone lives at the shape's anchor instead, so those " +
                          "curves now reach it too. "
                        : "") +
                    "Without this the toggle's menu entry, parameter and " +
                    "layer all convert and the contact just never switches or moves.");
            }
            if (dropped.Count > 0)
            {
                ctx.Report.Warning(Category, $"{dropped.Count} contact-animating curve(s) could not be carried",
                    string.Join("; ", dropped.Take(6)) + (dropped.Count > 6 ? ", …" : "") +
                    " — each animated something with no equivalent on the converted contact " +
                    "(a shape radius, or a filter on the pointer/trigger path, which bakes its " +
                    "filters once at load), or a contact that was not converted. The curve was " +
                    "removed rather than left silently addressing a deleted component.");
            }
        }

        // Runs after both repoint passes, because the merge's own restore
        // passes ran before the rewiring existed, on bindings naming
        // deleted VRChat components they could not sample.
        //
        // The rewiring folds two VRChat properties into one zone binding,
        // and layers that never fought before now write over each other.
        // ChilloutVR restores nothing a state does not write, so each
        // layer is settled by what it says across ALL its clips:
        //   on and off  -> a real toggle; untouched, and it owns the zone.
        //   on only     -> VRCFury's baked Write Defaults residue,
        //                  asserting rest from a later layer every frame,
        //                  which is what overrode the toggles. Stripped.
        //   off only    -> a switch-off nothing takes back. In a blend
        //                  tree (the spawn-time receiver guard) the off
        //                  curve is removed; suppression for a few load
        //                  frames is not worth zones dead forever. In
        //                  plain states the other states get the rest
        //                  value written in, the same answer the old
        //                  Write Defaults gave.
        internal static void BalanceRewiredZoneCurves(BridgeContext ctx)
        {
            if (ctx.MergedController == null)
            {
                return;
            }
            var hostPaths = new HashSet<string>();
            foreach (var hosts in ctx.ContactHosts.Values)
            {
                hostPaths.UnionWith(hosts);
            }
            foreach (var hosts in ctx.PhysicsColliderHosts.Values)
            {
                hostPaths.UnionWith(hosts);
            }
            if (hostPaths.Count == 0)
            {
                return;
            }

            int balanced = 0;
            int stripped = 0;
            int layersTouched = 0;
            foreach (var layer in ctx.MergedController.layers)
            {
                // A constant in an additive layer adds instead of asserting.
                if (layer.blendingMode == UnityEditor.Animations.AnimatorLayerBlendingMode.Additive)
                {
                    continue;
                }

                var states = new List<UnityEditor.Animations.AnimatorState>();
                var trees = new List<UnityEditor.Animations.BlendTree>();
                var clips = new HashSet<AnimationClip>();
                var clipsInTrees = new HashSet<AnimationClip>();
                var clipsInServiceTrees = new HashSet<AnimationClip>();
                var seenTrees = new HashSet<UnityEditor.Animations.BlendTree>();

                // A tree a menu control drives is a toggle. A Direct tree,
                // or one keyed on an internal parameter (VRCFury's
                // counters and math), is service plumbing; a zone write
                // inside one is baked residue, never the user's switch.
                var menuParameters = new HashSet<string>();
                if (ctx.CvrAvatar?.avatarSettings?.settings != null)
                {
                    foreach (var entry in ctx.CvrAvatar.avatarSettings.settings)
                    {
                        if (entry != null && !string.IsNullOrEmpty(entry.machineName))
                        {
                            menuParameters.Add(entry.machineName);
                        }
                    }
                }

                void Collect(Motion motion, bool viaTree, bool viaService)
                {
                    if (motion is AnimationClip clip)
                    {
                        clips.Add(clip);
                        if (viaTree)
                        {
                            clipsInTrees.Add(clip);
                        }
                        if (viaService)
                        {
                            clipsInServiceTrees.Add(clip);
                        }
                    }
                    else if (motion is UnityEditor.Animations.BlendTree tree && seenTrees.Add(tree))
                    {
                        trees.Add(tree);
                        // Not inherited: VRCFury reuses the same toggle
                        // tree as a child of its math layer, and a menu
                        // gated tree is a toggle wherever it sits. Its
                        // clips are shared with the toggle's own layer,
                        // so stripping them through the service route
                        // would break the toggle everywhere at once.
                        bool service = tree.blendType == UnityEditor.Animations.BlendTreeType.Direct
                            || !menuParameters.Contains(tree.blendParameter);
                        foreach (var child in tree.children)
                        {
                            Collect(child.motion, true, service);
                        }
                    }
                }

                void Walk(UnityEditor.Animations.AnimatorStateMachine machine)
                {
                    if (machine == null)
                    {
                        return;
                    }
                    foreach (var child in machine.states)
                    {
                        if (child.state == null)
                        {
                            continue;
                        }
                        states.Add(child.state);
                        Collect(child.state.motion, false, false);
                    }
                    foreach (var sub in machine.stateMachines)
                    {
                        Walk(sub.stateMachine);
                    }
                }
                Walk(layer.stateMachine);

                // Every clip in this layer writing a zone.
                var carriers = new Dictionary<UnityEditor.EditorCurveBinding, List<AnimationClip>>();
                foreach (var clip in clips)
                {
                    foreach (var binding in UnityEditor.AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type != typeof(GameObject) || binding.propertyName != "m_IsActive"
                            || !hostPaths.Contains(binding.path))
                        {
                            continue;
                        }
                        var curve = UnityEditor.AnimationUtility.GetEditorCurve(clip, binding);
                        if (curve == null || curve.length == 0)
                        {
                            continue;
                        }
                        if (!carriers.TryGetValue(binding, out var list))
                        {
                            carriers[binding] = list = new List<AnimationClip>();
                        }
                        list.Add(clip);
                    }
                }
                if (carriers.Count == 0)
                {
                    continue;
                }

                var stripPairs = new List<(AnimationClip clip, UnityEditor.EditorCurveBinding binding)>();
                var toFill = new List<UnityEditor.EditorCurveBinding>();
                foreach (var binding in carriers.Keys)
                {
                    // Service writes go regardless of value; the toggle
                    // verdict is taken from what remains.
                    var owned = new List<AnimationClip>();
                    foreach (var clip in carriers[binding])
                    {
                        if (clipsInServiceTrees.Contains(clip))
                        {
                            stripPairs.Add((clip, binding));
                        }
                        else
                        {
                            owned.Add(clip);
                        }
                    }
                    if (owned.Count == 0)
                    {
                        continue;
                    }
                    float top = float.MinValue;
                    float bottom = float.MaxValue;
                    foreach (var clip in owned)
                    {
                        var curve = UnityEditor.AnimationUtility.GetEditorCurve(clip, binding);
                        if (curve == null || curve.length == 0)
                        {
                            continue;
                        }
                        top = Mathf.Max(top, curve.keys.Max(k => k.value));
                        bottom = Mathf.Min(bottom, curve.keys.Min(k => k.value));
                    }
                    bool switchesOn = top > 0.5f;
                    bool switchesOff = bottom <= 0.5f;
                    if (switchesOn && switchesOff)
                    {
                        continue;   // a real toggle; it owns the zone
                    }
                    if (switchesOn)
                    {
                        // Asserts rest and nothing else: Write Defaults
                        // residue, and it overrides real toggles below it.
                        foreach (var clip in owned)
                        {
                            stripPairs.Add((clip, binding));
                        }
                        continue;
                    }
                    // Off with no way back.
                    if (owned.Any(c => clipsInTrees.Contains(c)))
                    {
                        // A constant restore in a sibling slot fights the
                        // blend; drop the suppression instead.
                        foreach (var clip in owned)
                        {
                            stripPairs.Add((clip, binding));
                        }
                    }
                    else
                    {
                        // Restore to what the avatar rests at. A zone
                        // authored inactive rests off; 0 is already right.
                        var t = ctx.Target.transform.Find(binding.path);
                        if (t != null && t.gameObject.activeSelf)
                        {
                            toFill.Add(binding);
                        }
                    }
                }
                if (stripPairs.Count == 0 && toFill.Count == 0)
                {
                    continue;
                }

                // Cloned per layer, never shared: the same clip (the
                // empty-slot filler especially) sits in other layers too,
                // and an edit written into the shared asset would have an
                // unrelated layer changing zones it knows nothing about.
                var perLayer = new Dictionary<AnimationClip, AnimationClip>();
                AnimationClip Owned(AnimationClip clip)
                {
                    if (!perLayer.TryGetValue(clip, out var owned))
                    {
                        owned = Object.Instantiate(clip);
                        owned.name = clip.name;
                        owned.hideFlags = HideFlags.None;
                        UnityEditor.AssetDatabase.AddObjectToAsset(owned, ctx.MergedController);
                        perLayer[clip] = owned;
                    }
                    return owned;
                }

                foreach (var (clip, binding) in stripPairs)
                {
                    UnityEditor.AnimationUtility.SetEditorCurve(Owned(clip), binding, null);
                    stripped++;
                }
                foreach (var clip in clips)
                {
                    // Blend-tree children are left alone: a constant in a
                    // blended slot fights the tree instead of resting it.
                    if (clipsInTrees.Contains(clip))
                    {
                        continue;
                    }
                    var drives = new HashSet<UnityEditor.EditorCurveBinding>(
                        UnityEditor.AnimationUtility.GetCurveBindings(clip));
                    foreach (var binding in toFill)
                    {
                        if (drives.Contains(binding))
                        {
                            continue;   // this motion says its own piece already
                        }
                        UnityEditor.AnimationUtility.SetEditorCurve(Owned(clip), binding,
                            AnimationCurve.Constant(0f, 1f / 60f, 1f));
                        balanced++;
                    }
                }
                if (perLayer.Count == 0)
                {
                    continue;
                }
                layersTouched++;
                foreach (var state in states)
                {
                    if (state.motion is AnimationClip clip && perLayer.TryGetValue(clip, out var owned))
                    {
                        state.motion = owned;
                    }
                }
                foreach (var tree in trees)
                {
                    var children = tree.children;
                    bool changed = false;
                    for (int i = 0; i < children.Length; i++)
                    {
                        if (children[i].motion is AnimationClip clip && perLayer.TryGetValue(clip, out var owned))
                        {
                            children[i].motion = owned;
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        // Writing children renumbers thresholds while
                        // automatic thresholds are on; pin them first.
                        bool auto = tree.useAutomaticThresholds;
                        tree.useAutomaticThresholds = false;
                        tree.children = children;
                        tree.useAutomaticThresholds = auto;
                    }
                }
            }

            if (balanced > 0 || stripped > 0)
            {
                ctx.Report.Converted(Category,
                    $"Contact zone ownership settled across {layersTouched} layer(s)",
                    $"{stripped} curve(s) that only asserted a zone's resting state were removed and " +
                    $"{balanced} restore(s) were written in. VRChat let several layers write a " +
                    "contact's enabled flags and reconciled them through Write Defaults; " +
                    "ChilloutVR restores nothing a state does not write, so those same curves " +
                    "here either held every zone off from the moment the avatar loaded (VRCFury " +
                    "disables all receivers for the first frames after load) or held them on " +
                    "over the menu toggle that should switch them off. A layer that switches a " +
                    "zone both off and on is a real toggle and now owns it outright.");
            }
        }

        static GameObject CreateContactObject(GameObject parent, string name,
            VRC.Dynamics.ContactBase.ShapeType shapeType, float radius, Vector3 position, float height, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = Vector3.one;

            if (shapeType == VRC.Dynamics.ContactBase.ShapeType.Sphere)
            {
                go.transform.localRotation = Quaternion.identity;
                var sphere = go.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                sphere.radius = radius;
            }
            else
            {
                go.transform.localRotation = rotation;
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.isTrigger = true;
                capsule.radius = radius;
                capsule.height = height;
                capsule.direction = 1; // Y, matching VRC capsule contacts
            }
            return go;
        }

        // --- VRChat's built-in avatar colliders --------------------------------------

        static void CreateDefaultColliderPointers(BridgeContext ctx, HashSet<string> listenedTags)
        {
            var vrc = ctx.SourceDescriptor;
            int created = 0;
            created += AddPointers(ctx, listenedTags, vrc.collider_head, HumanBodyBones.Head, false, "Head");
            created += AddPointers(ctx, listenedTags, vrc.collider_torso, HumanBodyBones.Chest, false, "Torso");
            created += AddPointers(ctx, listenedTags, vrc.collider_handL, HumanBodyBones.LeftHand, false, "Hand", "HandL");
            created += AddPointers(ctx, listenedTags, vrc.collider_handR, HumanBodyBones.RightHand, false, "Hand", "HandR");
            created += AddPointers(ctx, listenedTags, vrc.collider_footL, HumanBodyBones.LeftFoot, false, "Foot", "FootL");
            created += AddPointers(ctx, listenedTags, vrc.collider_footR, HumanBodyBones.RightFoot, false, "Foot", "FootR");
            created += AddPointers(ctx, listenedTags, vrc.collider_fingerIndexL, HumanBodyBones.LeftIndexDistal, true, "Finger", "FingerL", "FingerIndex", "FingerIndexL");
            created += AddPointers(ctx, listenedTags, vrc.collider_fingerIndexR, HumanBodyBones.RightIndexDistal, true, "Finger", "FingerR", "FingerIndex", "FingerIndexR");
            created += AddPointers(ctx, listenedTags, vrc.collider_fingerMiddleL, HumanBodyBones.LeftMiddleDistal, true, "Finger", "FingerL", "FingerMiddle", "FingerMiddleL");
            created += AddPointers(ctx, listenedTags, vrc.collider_fingerMiddleR, HumanBodyBones.RightMiddleDistal, true, "Finger", "FingerR", "FingerMiddle", "FingerMiddleR");
            created += AddPointers(ctx, listenedTags, vrc.collider_fingerRingL, HumanBodyBones.LeftRingDistal, true, "Finger", "FingerL", "FingerRing", "FingerRingL");
            created += AddPointers(ctx, listenedTags, vrc.collider_fingerRingR, HumanBodyBones.RightRingDistal, true, "Finger", "FingerR", "FingerRing", "FingerRingR");
            created += AddPointers(ctx, listenedTags, vrc.collider_fingerLittleL, HumanBodyBones.LeftLittleDistal, true, "Finger", "FingerL", "FingerLittle", "FingerLittleL");
            created += AddPointers(ctx, listenedTags, vrc.collider_fingerLittleR, HumanBodyBones.RightLittleDistal, true, "Finger", "FingerR", "FingerLittle", "FingerLittleR");

            if (created > 0)
            {
                ctx.Report.Converted(Category, "Built-in VRChat colliders",
                    $"{created} CVRPointer(s) created for tags the avatar's receivers listen to.");
            }
        }

        static int AddPointers(BridgeContext ctx, HashSet<string> listenedTags,
            VRCAvatarDescriptor.ColliderConfig config, HumanBodyBones bone, bool forceSphere, params string[] tags)
        {
            if (config.state == VRCAvatarDescriptor.ColliderConfig.State.Disabled)
            {
                return 0;
            }
            var wantedTags = tags.Where(listenedTags.Contains).ToArray();
            if (wantedTags.Length == 0)
            {
                return 0;
            }

            Transform sourceParent = config.transform;
            if (sourceParent == null)
            {
                var animator = ctx.SourceDescriptor.GetComponent<Animator>();
                sourceParent = animator != null && animator.isHuman ? animator.GetBoneTransform(bone) : null;
            }
            Transform parent = ctx.FindInTarget(sourceParent);
            if (parent == null)
            {
                ctx.Report.Warning(Category, $"Built-in collider {bone}", "Bone not found; pointer not created.");
                return 0;
            }

            int created = 0;
            foreach (var tag in wantedTags)
            {
                var shape = config.height <= 0f || forceSphere
                    ? VRC.Dynamics.ContactBase.ShapeType.Sphere
                    : VRC.Dynamics.ContactBase.ShapeType.Capsule;
                var go = CreateContactObject(parent.gameObject, $"{parent.name}_{tag}",
                    shape, config.radius, config.position, config.height, config.rotation);
                go.AddComponent<CVRPointer>().type = tag;
                created++;
            }
            return created;
        }

        static string PathOf(BridgeContext ctx, Transform t) => ctx.PathInTarget(t);
    }
}
#endif
