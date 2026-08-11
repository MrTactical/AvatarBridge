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

            bool native = UseNativeContacts(ctx);
            foreach (var sender in senders)
            {
                if (native) { ConvertSenderNative(ctx, sender); } else { ConvertSender(ctx, sender); }
            }
            foreach (var receiver in receivers)
            {
                if (native) { ConvertReceiverNative(ctx, receiver); } else { ConvertReceiver(ctx, receiver); }
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

            if (receivers.Length > 0 && UseNativeContacts(ctx))
            {
                // Read off the shipping client, and the reason native
                // contacts are not the default: the native system
                // writes straight at the Animator, which never syncs.
                // The legacy path writes through the manager, which does.
                //
                // Reported by a user who could see their own headpat sparkles and pop sound and
                // could not work out why nobody else could. The particle that DID work for
                // everyone turned out to be one that is simply always on.
                //
                // Corrected after the system's author described the model: the contact is not
                // wearer-only, it runs on EVERY client, and the clients simply disagree. He is
                // also explicit that a native contact must drive a "#" parameter, because a synced
                // one has the AAS default written back over it.
                ctx.Report.Approximated(Category,
                    $"Native contacts are on for {receivers.Length} receiver(s)",
                    "ChilloutVR's native contact system writes its parameter directly at the " +
                    "Animator and transmits nothing — it does not need to, because every client " +
                    "runs the contact itself and reaches the same answer from the same collision. " +
                    "That only holds while the parameter is LOCAL: a synced one has the declared " +
                    "default streamed back over whatever the contact set, which is why this " +
                    "conversion moves contact parameters to \"#\" names. Confirmed in game, with " +
                    "sound and particles reaching other players. " +
                    "These components are also INTERNAL TO THE GAME — the CCK does not ship them, " +
                    "AvatarBridge declares them itself against the decompiled client, and nothing " +
                    "obliges ChilloutVR to keep them as they are. An avatar built on them can be " +
                    "broken by a client update with no warning and no fix but reconverting. " +
                    "That is the reason to prefer the legacy pointer/trigger path unless you need " +
                    "a box shape or a receiver type it does not have — not the sync, which works.");
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

        // --- ChilloutVR's native contact system ---
        //
        // The components match VRChat's almost field for field: same
        // shapes, same tags, real proximity, no Unity collider needed.
        // Contacts are per-client by design. Field layout comes from
        // the decompiled shipped client, never the author's public
        // repo; ContactStubPatcher supplies the declarations.
        //
        // Reached entirely through reflection, never a scripting
        // define. A define set from a generated file can deadlock the
        // editor assembly against Assembly-CSharp; reflection degrades
        // to the legacy path instead of bricking the project.

        const string NakSender = "NAK.Contacts.ContactSender";
        const string NakReceiver = "NAK.Contacts.ContactReceiver";
        const string NakAnimator = "NAK.Contacts.ContactAnimator";

        internal static System.Type NativeContactAnimatorType => FindType(NakAnimator);

        internal static System.Type FindType(string fullName)
        {
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(fullName, false);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch
                {
                    // Reflection-only or broken assemblies can throw; ignore them.
                }
            }
            return null;
        }

        public static void RepointContactParameters(BridgeContext ctx)
        {
            var type = FindType(NakAnimator);
            if (type == null)
            {
                return;
            }
            var field = type.GetField("parameter", BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
            {
                return;
            }
            if (ctx.AppliedParameterRenames.Count == 0)
            {
                // Nothing renamed, nothing to follow. A contact can
                // still address a missing name; ask either way.
                ReportInertContacts(ctx, type, field);
                return;
            }
            var moved = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (var component in ctx.Target.GetComponentsInChildren(type, true))
            {
                string current = field.GetValue(component) as string;
                if (string.IsNullOrEmpty(current)
                    || !ctx.AppliedParameterRenames.TryGetValue(current, out string final)
                    || final == current)
                {
                    continue;
                }
                field.SetValue(component, final);
                moved.Add($"\"{current}\" -> \"{final}\"");
            }
            if (moved.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"{moved.Count} native contact(s) repointed at their renamed parameter",
                    string.Join(", ", moved) + " — a contact component addresses its parameter by " +
                    "name, and the animator renamed those while making them CCK-safe or local. " +
                    "The component is not part of the controller, so it does not follow along by " +
                    "itself, and one left behind drives a parameter nothing declares.");
            }

            ReportInertContacts(ctx, type, field);
        }

        static void ReportInertContacts(BridgeContext ctx, System.Type type, FieldInfo field)
        {
            if (ctx.MergedController == null)
            {
                return;
            }
            var declared = new HashSet<string>(
                ctx.MergedController.parameters.Select(p => p.name), System.StringComparer.Ordinal);
            var inert = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (var component in ctx.Target.GetComponentsInChildren(type, true))
            {
                string name = field.GetValue(component) as string;
                if (!string.IsNullOrEmpty(name) && !declared.Contains(name))
                {
                    inert.Add(name);
                }
            }
            if (inert.Count == 0)
            {
                return;
            }
            ctx.Report.Warning(Category,
                $"{inert.Count} contact(s) drive a parameter nothing on this avatar reads",
                string.Join(", ", inert.Select(n => $"\"{n}\"")) + " — the animator has no such " +
                "parameter, so touching these does nothing. That is how they arrived: the " +
                "receiver is in the source avatar but whatever used to read it is not, usually " +
                "because the layer was part of a system removed before the avatar was shared. " +
                "Nothing is broken by leaving them, though every client that can see you tests " +
                "them for collisions anyway. Delete the contact objects, or wire the parameter " +
                "back up if the feature is one you wanted.");
        }

        static void SetMember(object target, string field, object value)
        {
            if (target == null || value == null)
            {
                return;
            }
            var f = target.GetType().GetField(field, BindingFlags.Public | BindingFlags.Instance);
            if (f != null)
            {
                try { f.SetValue(target, value); } catch { }
            }
        }

        static object EnumValue(System.Type sibling, string enumName, string member)
        {
            var enumType = sibling.Assembly.GetType("NAK.Contacts." + enumName, false);
            if (enumType == null)
            {
                return null;
            }
            try { return System.Enum.Parse(enumType, member, true); } catch { return null; }
        }

        static object AllContentTypes(System.Type sibling)
        {
            var enumType = sibling.Assembly.GetType("NAK.Contacts.ContentType", false);
            if (enumType == null)
            {
                return null;
            }
            try
            {
                int all = 0;
                foreach (var v in System.Enum.GetValues(enumType))
                {
                    all |= System.Convert.ToInt32(v);
                }
                return System.Enum.ToObject(enumType, all);
            }
            catch { return null; }
        }

        static bool UseNativeContacts(BridgeContext ctx)
        {
            if (!ctx.Settings.useNativeContacts)
            {
                return false;
            }
            var receiver = FindType(NakReceiver);
            if (receiver != null && FindType(NakSender) != null && FindType(NakAnimator) != null)
            {
                // The type resolving is not enough. A component only
                // survives serialization tied to a script asset, and a
                // stale compiled assembly has none; the reference
                // serializes without a GUID and fails downstream.
                // Prove it on a throwaway object first.
                var probe = new GameObject("AvatarBridge_ContactProbe") { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    // The CCK's own test: MonoScript source text must be
                    // non-empty. A non-null MonoScript alone can bind to
                    // a stale assembly with no script asset behind it.
                    var added = probe.AddComponent(receiver) as MonoBehaviour;
                    var script = added != null ? UnityEditor.MonoScript.FromMonoBehaviour(added) : null;
                    if (script != null && !string.IsNullOrEmpty(script.text))
                    {
                        return true;
                    }
                    // Almost always the same cause; name it. A dangling
                    // script reference makes Unity manufacture a
                    // placeholder MonoScript. That placeholder wins when
                    // AddComponent looks the class up, so every NEW component is bound to it and
                    // is born broken too. One bad conversion left in the scene therefore poisons
                    // every conversion after it, including ones that would otherwise be correct,
                    // and no amount of fixing the generated declarations helps while it is there.
                    bool phantom = HasPhantomContactScript(out string phantomClass);
                    string cause = phantom
                        ? $"There is already a broken \"{phantomClass}\" component in a loaded scene — most " +
                          "likely a contact object from an earlier conversion. Unity creates a placeholder " +
                          "script for it, and that placeholder takes precedence when new components are " +
                          "created, so this cannot succeed until it is gone. Delete the leftover Contact_* " +
                          "objects (or the whole previously converted avatar), reopen the scene so the " +
                          "placeholder is dropped, then convert again."
                        : "Unity produced no script asset for the component. Check that " +
                          "AvatarBridge/Runtime contains the generated declarations and that the project " +
                          "compiled cleanly, then convert again.";

                    ctx.Report.Error(Category, "Native contacts unusable; used the legacy path", cause +
                        " Contacts were converted to pointers and triggers instead, which work. " +
                        "Tools > Avatar Bridge > Diagnose native contacts prints the full picture.");
                    return false;
                }
                finally
                {
                    Object.DestroyImmediate(probe);
                }
            }
            ctx.Report.Warning(Category, "Native contacts unavailable; used the legacy path",
                "\"Use native contacts\" is on, but ChilloutVR's contact components aren't declared in this " +
                "project. That normally means the installed CCK is newer than the one AvatarBridge verified " +
                "its declarations against, so ContactStubPatcher declined to generate them — see the console " +
                "for the exact version. Contacts were converted to pointers and triggers instead.");
            return false;
        }

        static bool HasPhantomContactScript(out string className)
        {
            className = null;
            UnityEditor.MonoScript[] all;
            try { all = UnityEditor.MonoImporter.GetAllRuntimeMonoScripts(); }
            catch { return false; }

            foreach (var ms in all)
            {
                System.Type type;
                try { type = ms.GetClass(); } catch { continue; }
                if (type == null || type.Namespace != "NAK.Contacts")
                {
                    continue;
                }
                if (string.IsNullOrEmpty(ms.text) && string.IsNullOrEmpty(UnityEditor.AssetDatabase.GetAssetPath(ms)))
                {
                    className = type.FullName;
                    return true;
                }
            }
            return false;
        }

        static void ConvertSenderNative(BridgeContext ctx, VRCContactSender sender)
        {
            if (sender.collisionTags.Count == 0)
            {
                Object.DestroyImmediate(sender);
                return;
            }

            var host = NativeContactObject(sender.rootTransform, sender.transform, "Contact_Sender");
            RecordHost(ctx, sender, isSender: true, host);
            var contact = host.AddComponent(FindType(NakSender));
            ApplyShape(contact, sender.shapeType, sender.radius, sender.height, sender.position, sender.rotation);
            var tags = sender.collisionTags.Distinct().ToArray();
            SetMember(contact, "collisionTags", tags);
            SetMember(contact, "contentTypes", AllContentTypes(contact.GetType()));

            ctx.Report.Converted(Category, PathOf(ctx, sender.transform),
                $"Sender -> native ContactSender ({string.Join(", ", tags)})");
            Object.DestroyImmediate(sender);
        }

        static void ConvertReceiverNative(BridgeContext ctx, VRCContactReceiver receiver)
        {
            if (receiver.collisionTags.Count == 0 || string.IsNullOrEmpty(receiver.parameter))
            {
                Object.DestroyImmediate(receiver);
                return;
            }

            // One receiver per GameObject: ContactAnimator pairs with its receiver through
            // TryGetComponent, which would hand every animator on a shared object the same first
            // receiver.
            var host = NativeContactObject(receiver.rootTransform, receiver.transform,
                "Contact_" + receiver.parameter);
            RecordHost(ctx, receiver, isSender: false, host);

            var receiverType = FindType(NakReceiver);
            var contact = host.AddComponent(receiverType);
            ApplyShape(contact, receiver.shapeType, receiver.radius, receiver.height,
                receiver.position, receiver.rotation);
            // Same widening as the trigger path. A native receiver
            // needs the ChilloutVR pointer names too.
            SetMember(contact, "collisionTags", WithChilloutVrPointerTypes(receiver.collisionTags));
            SetMember(contact, "allowSelf", receiver.allowSelf);
            SetMember(contact, "allowOthers", receiver.allowOthers);
            SetMember(contact, "localOnly", receiver.localOnly);
            // contentTypes is written explicitly, every flag. It masks
            // the sender's source type, and the client's hand senders
            // are Player; a mask without that bit is untouchable.
            // A default is a hidden dependency; a write is a fact.
            SetMember(contact, "contentTypes", AllContentTypes(receiverType));

            string typeName = receiver.receiverType.ToString();
            string nativeType = typeName.Contains("OnEnter") ? "OnEnter"
                // 1 at the centre falling to 0 at the edge, the same reading VRChat gives.
                : typeName.Contains("Proximity") ? "ProximitySenderToReceiver"
                : "Constant";
            SetMember(contact, "receiverType", EnumValue(receiverType, "ReceiverType", nativeType));

            // The client overloads contactValue per receiver type; on an
            // OnEnter receiver it is the minimum contact velocity. The
            // declaration default is 1, and a receiver authored at 0.05
            // becomes twenty times harder to trigger if it stands.
            if (nativeType == "OnEnter")
            {
                SetMember(contact, "contactValue", receiver.minVelocity);
                if (!Mathf.Approximately(receiver.paramValue, 1f))
                {
                    ctx.Report.Approximated(Category, PathOf(ctx, receiver.transform),
                        $"OnEnter receiver \"{receiver.parameter}\" wrote {receiver.paramValue:0.###} in " +
                        "VRChat; the native receiver always writes 1 for its one frame. Anything " +
                        "conditioning on the exact value needs a look.");
                }
            }

            // A native contact must drive a local parameter. A synced
            // name has its default streamed back over the contact's
            // writes. Local, every client runs the contact and reaches
            // the same answer. The route is decided per parameter; only
            // a menu control driving the same name blocks the simple one.
            string driven = receiver.parameter;
            bool analog = typeName.Contains("Proximity");
            if (!driven.StartsWith("#", System.StringComparison.Ordinal))
            {
                bool onMenu = ctx.CvrAvatar?.avatarSettings?.settings != null
                    && ctx.CvrAvatar.avatarSettings.settings
                        .Any(s => s != null && s.machineName == driven);
                if (!onMenu)
                {
                    // The simple route: the parameter itself becomes "#name" everywhere. The
                    // rename pass moves the declaration, every clip binding and every condition
                    // together, so the animations keep reading whatever it ends up called.
                    ctx.LocalContactParameters.Add(driven);
                }
                else if (!analog)
                {
                    // The menu needs this name synced, so the contact
                    // gets a local name of its own and a driver copies
                    // the value across; driver writes are transmitted.
                    // Recorded once per pair, not per receiver;
                    // symmetric receivers share the local name.
                    string local = "#" + driven + "_contact";
                    if (!ctx.BridgedContacts.Contains((local, driven)))
                    {
                        ctx.BridgedContacts.Add((local, driven));
                    }
                    driven = local;
                }
                else
                {
                    // Analog and menu-driven. The contact gets a local
                    // name; the menu keeps the synced one; every reader
                    // moves to a per-frame maximum of the two, computed
                    // on every client. Contacts run per client, so the
                    // smooth value needs no sync at all.
                    string local = "#" + driven + "_contact";
                    if (!ctx.AnalogBridgedContacts.Contains((local, driven)))
                    {
                        ctx.AnalogBridgedContacts.Add((local, driven));
                    }
                    driven = local;
                }
            }

            var animator = host.AddComponent(FindType(NakAnimator));
            SetMember(animator, "animator", ctx.Target.GetComponent<Animator>());
            SetMember(animator, "parameter", driven);

            ctx.ContactParameters.Add(receiver.parameter);
            ctx.Report.Converted(Category, PathOf(ctx, receiver.transform),
                $"{typeName} receiver -> native ContactReceiver driving \"{driven}\"" +
                (driven != receiver.parameter
                    ? (analog
                        ? $", combined with the menu's \"{receiver.parameter}\" as a per-frame maximum"
                        : $", carried to \"{receiver.parameter}\" by a driver so other players see it")
                    : "") +
                (receiver.localOnly ? " (localOnly preserved)" : ""));
            Object.DestroyImmediate(receiver);
        }

        static void ApplyShape(Component contact, VRC.Dynamics.ContactBase.ShapeType shapeType,
            float radius, float height, Vector3 position, Quaternion rotation)
        {
            bool sphere = shapeType == VRC.Dynamics.ContactBase.ShapeType.Sphere;
            SetMember(contact, "shapeType", EnumValue(contact.GetType(), "ShapeType", sphere ? "Sphere" : "Capsule"));
            SetMember(contact, "radius", radius);
            SetMember(contact, "height", height);
            SetMember(contact, "localPosition", position);
            SetMember(contact, "localRotation", sphere ? Quaternion.identity : rotation);
        }

        static GameObject NativeContactObject(Transform root, Transform fallback, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root != null ? root : fallback, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go;
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
            var dropped = new SortedSet<string>(StableSampleOrder.Instance);
            foreach (var clip in clips)
            {
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

                    bool native = ctx.Settings != null && ctx.Settings.useNativeContacts;
                    var nakType = native ? FindType(sender ? NakSender : NakReceiver) : null;
                    string prop = binding.propertyName;

                    // What each property maps to differs by path, and each verdict is from the
                    // shipped client rather than hope:
                    //   m_Enabled     -> host object active, both paths (ContactBase and the
                    //                    legacy backing contact both register in OnEnable).
                    //   position.xyz  -> legacy: the host transform carries the offset, 1:1
                    //                    onto m_LocalPosition. Native: the offset lives in the
                    //                    component's localPosition field, which is animatable
                    //                    (ContactBase has OnDidApplyAnimationProperties).
                    //   allowSelf/allowOthers/localOnly -> native only, same field animation.
                    //                    Legacy bakes them into the backing contact at Create and
                    //                    never looks again, so those drop with the warning.
                    UnityEditor.EditorCurveBinding? target = null;
                    if (prop == "m_Enabled")
                    {
                        target = UnityEditor.EditorCurveBinding.FloatCurve(null, typeof(GameObject), "m_IsActive");
                    }
                    else if (prop.StartsWith("position.", System.StringComparison.Ordinal))
                    {
                        string axis = prop.Substring("position.".Length);
                        target = native && nakType != null
                            ? UnityEditor.EditorCurveBinding.FloatCurve(null, nakType, "localPosition." + axis)
                            : UnityEditor.EditorCurveBinding.FloatCurve(null, typeof(Transform), "m_LocalPosition." + axis);
                    }
                    else if (native && nakType != null
                             && (prop == "allowSelf" || prop == "allowOthers" || prop == "localOnly"))
                    {
                        target = UnityEditor.EditorCurveBinding.FloatCurve(null, nakType, prop);
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

            if (repointed > 0)
            {
                ctx.Report.Converted(Category, $"{repointed} contact animation(s) rewired",
                    "Curves that switched a VRChat contact on and off now toggle the converted " +
                    "contact's own object, and curves that MOVED one (a receiver riding a scaled " +
                    "body part) now drive the converted contact's offset — the forms ChilloutVR " +
                    "honours on each path. Without this the toggle's menu entry, parameter and " +
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
