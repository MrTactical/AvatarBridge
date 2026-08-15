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
                GrowZoneForSliders(ctx, contactObject, PathOf(ctx, sender.transform));
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

        // Both penetration families tag the same points; a converted receiver
        // hears either name. Kept apart from the table above, which also
        // decides what the unreachable-tag report leaves out.
        static readonly Dictionary<string, string[]> PenetrationTagTwins =
            new Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "TPS_Pen_Penetrating",   new[] { "SPSLL_Pen_Penetrating" } },
                { "SPSLL_Pen_Penetrating", new[] { "TPS_Pen_Penetrating" } },
                { "TPS_Pen_Root",          new[] { "SPSLL_Pen_Root" } },
                { "SPSLL_Pen_Root",        new[] { "TPS_Pen_Root" } },
                { "TPS_Pen_Width",         new[] { "SPSLL_Pen_Width" } },
                { "SPSLL_Pen_Width",       new[] { "TPS_Pen_Width" } },
                { "TPS_Orf_Root",          new[] { "SPSLL_Socket_Root" } },
                { "SPSLL_Socket_Root",     new[] { "TPS_Orf_Root" } },
                { "TPS_Orf_Norm",          new[] { "SPSLL_Socket_Front" } },
                { "SPSLL_Socket_Front",    new[] { "TPS_Orf_Norm" } },
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
                if (ChilloutVrPointerTypes.TryGetValue(tag, out var equivalents))
                {
                    foreach (string equivalent in equivalents)
                    {
                        if (!types.Contains(equivalent))
                        {
                            types.Add(equivalent);
                        }
                    }
                }
                if (PenetrationTagTwins.TryGetValue(tag, out var twins))
                {
                    foreach (string twin in twins)
                    {
                        if (!types.Contains(twin))
                        {
                            types.Add(twin);
                        }
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
            GrowZoneForSliders(ctx, contactObject, PathOf(ctx, receiver.transform));
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
                // ChilloutVR's stay task writes only while something is inside; the
                // parameter keeps its last reading after. Reset to 0 on exit.
                trigger.exitTasks.Add(MakeTask(receiver.parameter, 0f, 0f));
                ctx.Report.Converted(Category, PathOf(ctx, receiver.transform),
                    $"Proximity receiver -> distance-driven \"{receiver.parameter}\", 0 on exit");
            }

            ctx.ContactParameters.Add(receiver.parameter);
            Object.DestroyImmediate(receiver);
        }

        // A boop zone authored at 7 mm on the nose is fine; a slap zone
        // authored on a body a slider doubles is not - the mesh grows
        // past it and every touch lands inside the body, short of the
        // zone. Measured the way the physics sizes are: the mesh around
        // the zone at rest and with every animated shape at full reach.
        // When one slider owns the growth the zone is left authored-size
        // and ScaleZonesWithSliders animates it along; only growth spread
        // across shapes falls back to statically sizing for the largest.
        static void GrowZoneForSliders(BridgeContext ctx, GameObject zone, string reportPath)
        {
            if (!ctx.Settings.sizeContactZonesForLargest)
            {
                return;
            }
            var sphere = zone.GetComponent<SphereCollider>();
            var capsule = zone.GetComponent<CapsuleCollider>();
            float radius = sphere != null ? sphere.radius : capsule != null ? capsule.radius : 0f;
            if (radius <= 0f)
            {
                return;
            }
            var scale = zone.transform.lossyScale;
            float mean = (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f;
            if (mean <= 0f)
            {
                return;
            }
            // Capture enough mesh to say what the body does here; a tiny
            // authored zone still needs the surrounding surface read.
            float worldRadius = radius * mean;
            float capture = Mathf.Max(worldRadius * 2.5f, 0.06f);
            float push = MeshGrowth.Around(ctx, zone.transform.position, capture, out var perShape);
            if (push < 0.005f)
            {
                return;   // within measurement noise
            }
            // The surface can travel this far past where it rests, so
            // the zone extends by the same distance. Capped so a shape
            // that hurls vertices cannot make a zone the size of a room.
            float growth = Mathf.Min((worldRadius + push) / worldRadius, 3f);

            // One dominant slider lets the zone follow it live. Contributions are
            // grouped by what animates together, judged against the measured total.
            var groups = new Dictionary<string, float>();
            var reps = new Dictionary<string, (string key, float push)>();
            foreach (var pair in perShape)
            {
                string g = MeshGrowth.GroupOf(ctx, pair.Key);
                groups.TryGetValue(g, out float sum);
                groups[g] = sum + pair.Value;
                if (!reps.TryGetValue(g, out var rep) || pair.Value > rep.push)
                {
                    reps[g] = (pair.Key, pair.Value);
                }
            }
            string bestGroup = null;
            float bestSum = 0f;
            foreach (var pair in groups)
            {
                if (pair.Value > bestSum)
                {
                    bestSum = pair.Value;
                    bestGroup = pair.Key;
                }
            }
            if (bestGroup != null && bestSum >= 0.7f * push)
            {
                string representative = reps[bestGroup].key;
                float reach = MeshGrowth.ReachOf(ctx, representative);
                if (reach > 0.01f)
                {
                    ctx.ZoneSliderGrowth.Add((
                        BridgeContext.RelativePath(ctx.Target.transform, zone.transform),
                        representative, growth, reach, reportPath));
                    return;
                }
            }

            string contributors = perShape.Count == 0
                ? "no single animated shape could be attributed"
                : "largest: " + string.Join(", ", perShape
                    .OrderByDescending(p => p.Value).Take(3)
                    .Select(p => $"\"{p.Key.Substring(p.Key.LastIndexOf('|') + 1)}\" {p.Value * 100f:0.#} cm"));
            GrowStatically(zone, growth);
            ctx.Report.Converted(Category, reportPath,
                $"Zone grown ×{growth:0.00} ({push * 100f:0.#} cm of surface travel) for the largest " +
                "the sliders make the body — an animated blendshape can push the mesh past the " +
                "authored size, and a zone inside the body cannot be touched. The growth here is " +
                $"spread across shapes ({contributors}), so the zone holds the grown size instead " +
                "of following one slider.");
        }

        internal static void GrowStatically(GameObject zone, float growth)
        {
            var sphere = zone.GetComponent<SphereCollider>();
            var capsule = zone.GetComponent<CapsuleCollider>();
            if (sphere != null)
            {
                sphere.radius *= growth;
            }
            if (capsule != null)
            {
                capsule.radius *= growth;
                capsule.height *= growth;
            }
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
                // A toggle on the contact's object gated it in VRChat too. The host
                // sits at the anchor, so the curve is copied onto it, not moved.
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

        // A zone one slider grows follows the slider instead of holding
        // the grown size: every clip driving that blendshape gains scale
        // curves on the zone mapped through the same keyframes, and the
        // contact behind the trigger takes its size from the transform
        // every frame. Authored size at rest, the measured growth at the
        // slider's full reach, scaled between. A clip that latches the
        // shape latches the zone with it, so the two never disagree.
        internal static void ScaleZonesWithSliders(BridgeContext ctx)
        {
            if (ctx.ZoneSliderGrowth.Count == 0)
            {
                return;
            }
            if (ctx.MergedController == null)
            {
                foreach (var entry in ctx.ZoneSliderGrowth)
                {
                    HoldGrownSize(ctx, entry);
                }
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

            var wired = new HashSet<string>();
            foreach (var entry in ctx.ZoneSliderGrowth)
            {
                var zone = ctx.Target.transform.Find(entry.zonePath);
                if (zone == null)
                {
                    continue;
                }
                Vector3 authored = zone.localScale;
                int carriers = 0;
                foreach (var clip in clips)
                {
                    foreach (var binding in UnityEditor.AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type != typeof(SkinnedMeshRenderer)
                            || !binding.propertyName.StartsWith("blendShape.", System.StringComparison.Ordinal)
                            || binding.path + "|" + binding.propertyName.Substring("blendShape.".Length)
                               != entry.shapeKey)
                        {
                            continue;
                        }
                        var curve = UnityEditor.AnimationUtility.GetEditorCurve(clip, binding);
                        if (curve == null || curve.length == 0)
                        {
                            continue;
                        }
                        WriteScaleCurves(clip, entry.zonePath, authored, curve, entry.growth, entry.reach);
                        carriers++;
                        break;   // a binding appears once per clip
                    }
                }
                if (carriers > 0)
                {
                    wired.Add(entry.zonePath);
                    string shape = entry.shapeKey.Substring(entry.shapeKey.LastIndexOf('|') + 1);
                    ctx.Report.Converted(Category, entry.reportPath,
                        $"Zone follows the \"{shape}\" slider — authored size at rest, " +
                        $"×{entry.growth:0.00} at full reach, scaled between. Grown statically it " +
                        "covered the body's largest shape while the body was small; instead the " +
                        "zone's scale is animated in the slider's own clips, and the contact " +
                        "reads its transform every frame.");
                }
                else
                {
                    HoldGrownSize(ctx, entry);
                }
            }
            if (wired.Count > 0)
            {
                CarryZonesInMasks(ctx, wired);
            }
        }

        static void HoldGrownSize(BridgeContext ctx,
            (string zonePath, string shapeKey, float growth, float reach, string reportPath) entry)
        {
            var zone = ctx.Target.transform.Find(entry.zonePath);
            if (zone == null)
            {
                return;
            }
            GrowStatically(zone.gameObject, entry.growth);
            ctx.Report.Converted(Category, entry.reportPath,
                $"Zone grown ×{entry.growth:0.00} for the largest the sliders make the body — " +
                "the slider's clips were not found in the converted animator, so the zone " +
                "holds the grown size instead of following the slider.");
        }

        // The slider's keyframes, remapped: weight 0 is the authored
        // scale, the reachable top is the measured growth, tangents
        // scaled by the same factor.
        static void WriteScaleCurves(AnimationClip clip, string zonePath, Vector3 authored,
            AnimationCurve weights, float growth, float reach)
        {
            string[] props = { "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };
            float[] axes = { authored.x, authored.y, authored.z };
            for (int a = 0; a < 3; a++)
            {
                float slope = axes[a] * (growth - 1f) / reach;
                var keys = new Keyframe[weights.length];
                for (int i = 0; i < weights.length; i++)
                {
                    var w = weights.keys[i];
                    keys[i] = new Keyframe(w.time,
                        axes[a] * (1f + (growth - 1f) * Mathf.Clamp01(w.value / reach)),
                        w.inTangent * slope, w.outTangent * slope);
                }
                var binding = UnityEditor.EditorCurveBinding.FloatCurve(
                    zonePath, typeof(Transform), props[a]);
                UnityEditor.AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(keys));
            }
        }

        // Masks pass float curves through but gate transform curves by
        // their transform list, and the zones did not exist when any
        // author mask was baked. Every layer whose clips now scale a
        // zone gets that path carried active in its mask.
        static void CarryZonesInMasks(BridgeContext ctx, HashSet<string> zonePaths)
        {
            foreach (var layer in ctx.MergedController.layers)
            {
                var mask = layer.avatarMask;
                if (mask == null)
                {
                    continue;
                }
                var clips = new HashSet<AnimationClip>();
                var seenTrees = new HashSet<UnityEditor.Animations.BlendTree>();
                void Collect(Motion motion)
                {
                    if (motion is AnimationClip clip)
                    {
                        clips.Add(clip);
                    }
                    else if (motion is UnityEditor.Animations.BlendTree tree && seenTrees.Add(tree))
                    {
                        foreach (var child in tree.children)
                        {
                            Collect(child.motion);
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
                        if (child.state != null)
                        {
                            Collect(child.state.motion);
                        }
                    }
                    foreach (var sub in machine.stateMachines)
                    {
                        Walk(sub.stateMachine);
                    }
                }
                Walk(layer.stateMachine);

                var needed = new HashSet<string>();
                foreach (var clip in clips)
                {
                    foreach (var binding in UnityEditor.AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type == typeof(Transform) && zonePaths.Contains(binding.path))
                        {
                            needed.Add(binding.path);
                        }
                    }
                }
                if (needed.Count == 0)
                {
                    continue;
                }
                bool dirty = false;
                for (int i = 0; i < mask.transformCount; i++)
                {
                    if (needed.Remove(mask.GetTransformPath(i)) && !mask.GetTransformActive(i))
                    {
                        mask.SetTransformActive(i, true);
                        dirty = true;
                    }
                }
                foreach (var path in needed)
                {
                    int i = mask.transformCount;
                    mask.transformCount = i + 1;
                    mask.SetTransformPath(i, path);
                    mask.SetTransformActive(i, true);
                    dirty = true;
                }
                if (dirty)
                {
                    UnityEditor.EditorUtility.SetDirty(mask);
                }
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
