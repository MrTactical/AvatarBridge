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
    /// <summary>
    /// VRC Contact system -> ChilloutVR pointers/triggers:
    ///
    ///   VRCContactSender    -> CVRPointer (one per collision tag) + trigger collider
    ///   VRCContactReceiver  -> CVRAdvancedAvatarSettingsTrigger driving the parameter
    ///   VRChat's built-in hand/head/torso colliders -> CVRPointers with standard tags,
    ///     created only for tags the avatar's receivers actually listen to.
    /// </summary>
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
                var contactObject = CreateContactObject(sender.gameObject, "CVRPointer_" + tag,
                    sender.shapeType, sender.radius, sender.position, sender.height, sender.rotation);
                var pointer = contactObject.AddComponent<CVRPointer>();
                pointer.type = tag;
            }
            ctx.Report.Converted(Category, PathOf(ctx, sender.transform),
                $"Sender -> CVRPointer ({string.Join(", ", sender.collisionTags)})");
            Object.DestroyImmediate(sender);
        }

        static void ConvertReceiver(BridgeContext ctx, VRCContactReceiver receiver)
        {
            if (receiver.collisionTags.Count == 0 || string.IsNullOrEmpty(receiver.parameter))
            {
                Object.DestroyImmediate(receiver);
                return;
            }

            var contactObject = CreateContactObject(receiver.gameObject, "CVRTrigger_" + receiver.parameter,
                receiver.shapeType, receiver.radius, receiver.position, receiver.height, receiver.rotation);

            var trigger = contactObject.AddComponent<CVRAdvancedAvatarSettingsTrigger>();
            trigger.useAdvancedTrigger = true;
            trigger.isLocalInteractable = receiver.allowSelf;
            trigger.isNetworkInteractable = receiver.allowOthers;
            trigger.allowedTypes = receiver.collisionTags.Distinct().ToArray();

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
                    // No min/max. This used to set minValue = 1, maxValue = 0 to "invert" the
                    // range, on the belief that ChilloutVR measures distance outward while VRChat
                    // reads 1 at the centre. That belief was wrong: the client computes
                    // 1 - saturate(distance / extent), which is 1 at the centre exactly like
                    // VRChat. The inversion was only ever harmless because SetFromDistance writes
                    // the proximity value raw — min/max are read solely by Add, Subtract and
                    // SetFromPosition. Left in, it was a trap waiting for the day the CCK started
                    // honouring them here.
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

        // --- ChilloutVR's native contact system ---------------------------------------
        //
        // The components line up with VRChat's almost field for field, so this is a copy rather
        // than an impersonation: same shapes, same collision tags, same allowSelf/allowOthers/
        // localOnly, real proximity. They also need no Unity collider — the shape lives on the
        // component — and contacts are per-client by design (confirmed in game): every client
        // simulates every avatar's contacts itself, so reactions cross the network with no sync
        // involved and nothing here costs sync bits; whether a driven parameter's VALUE
        // replicates is its own AAS declaration's business. The field layout comes from the
        // DECOMPILED SHIPPED CLIENT, never from the author's public repo — see ContactStubPatcher
        // for the revision-5 incident where trusting the repo made every receiver deaf.
        //
        // ContactStubPatcher supplies the declarations; the game holds the implementation.

        // ChilloutVR's native contact components, reached entirely through reflection.
        //
        // Deliberately NOT behind a scripting define. An earlier version gated this on
        // AVATARBRIDGE_CONTACTS and named NAK.Contacts directly, which deadlocks: the define is
        // set from a generated file in Assembly-CSharp while this file lives in the editor
        // assembly, and the moment those two disagree the editor assembly stops compiling — which
        // takes BridgeDefines with it, so the one piece of code that could clear the define can no
        // longer run. The only way out was editing Player Settings by hand. Reflection removes the
        // possibility entirely: nothing here needs those types at compile time, so a missing or
        // half-written stub degrades to the legacy path instead of bricking the project.
        //
        // Same approach CreateParameterStreams already takes with the CCK.

        const string NakSender = "NAK.Contacts.ContactSender";
        const string NakReceiver = "NAK.Contacts.ContactReceiver";
        const string NakAnimator = "NAK.Contacts.ContactAnimator";

        static System.Type FindType(string fullName)
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

        /// <summary>
        /// Every flag ContentType defines, OR'd together — computed from the live enum rather
        /// than a literal, so a client build adding a flag is included automatically.
        /// </summary>
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
                // The type resolving is not enough. A component only survives serialization if
                // Unity can tie it back to a script asset, and it can't when the declarations
                // exist solely in a stale compiled assembly — the .cs having been deleted, or not
                // yet imported. AddComponent still succeeds there, and writes a script reference
                // with no GUID at all, which fails in the CCK's validator and again in ChilloutVR
                // as "the referenced script on this Behaviour is missing". The avatar looks
                // perfectly converted right up until it doesn't work.
                //
                // So prove it on a throwaway object before committing the whole conversion to it.
                var probe = new GameObject("AvatarBridge_ContactProbe") { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    // Exactly the CCK's own test, from its BrokenMonoBehaviourStep validator:
                    // MonoScript.FromMonoBehaviour(...).text must be non-empty. Checking only for
                    // a non-null MonoScript is not enough and let this through once already — when
                    // Assembly-CSharp has failed to compile, Unity keeps the last good assembly
                    // loaded, so the type still resolves and AddComponent still succeeds, but it
                    // binds to something with no script asset behind it. The reference serializes
                    // without a GUID, the CCK rejects the avatar, and ChilloutVR reports the script
                    // as missing. Empty source text is what distinguishes that from a real binding.
                    var added = probe.AddComponent(receiver) as MonoBehaviour;
                    var script = added != null ? UnityEditor.MonoScript.FromMonoBehaviour(added) : null;
                    if (script != null && !string.IsNullOrEmpty(script.text))
                    {
                        return true;
                    }
                    // Almost always the same cause, so name it rather than describe symptoms.
                    //
                    // A scene holding a MonoBehaviour whose script reference is dangling makes
                    // Unity manufacture a placeholder MonoScript for that class — no asset, no
                    // source text, a negative instance id. That placeholder then wins when
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

        /// <summary>
        /// True when Unity holds a MonoScript for one of the contact classes that has no asset
        /// behind it — the placeholder it manufactures for a component whose script reference is
        /// dangling. Its presence is what stops any new component binding correctly.
        /// </summary>
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

            var receiverType = FindType(NakReceiver);
            var contact = host.AddComponent(receiverType);
            ApplyShape(contact, receiver.shapeType, receiver.radius, receiver.height,
                receiver.position, receiver.rotation);
            SetMember(contact, "collisionTags", receiver.collisionTags.Distinct().ToArray());
            SetMember(contact, "allowSelf", receiver.allowSelf);
            SetMember(contact, "allowOthers", receiver.allowOthers);
            SetMember(contact, "localOnly", receiver.localOnly);
            // contentTypes is written EXPLICITLY, always, to every flag the client defines. It
            // is a mask over the SENDER's source type, and the client's built-in hand/finger
            // senders are SourceContentType.Player (ContactsTools, decompiled) — a receiver
            // whose mask lacks the Player bit can never be touched by another player's hands.
            // 2.50.3 relied on the stub's field default for this, the stub's default briefly
            // lost the Player flag, and every converted receiver went silently deaf: a default
            // is a hidden dependency, an explicit write is a fact.
            SetMember(contact, "contentTypes", AllContentTypes(receiverType));

            string typeName = receiver.receiverType.ToString();
            string nativeType = typeName.Contains("OnEnter") ? "OnEnter"
                // 1 at the centre falling to 0 at the edge, the same reading VRChat gives.
                : typeName.Contains("Proximity") ? "ProximitySenderToReceiver"
                : "Constant";
            SetMember(contact, "receiverType", EnumValue(receiverType, "ReceiverType", nativeType));

            var animator = host.AddComponent(FindType(NakAnimator));
            SetMember(animator, "animator", ctx.Target.GetComponent<Animator>());
            SetMember(animator, "parameter", receiver.parameter);

            ctx.ContactParameters.Add(receiver.parameter);
            ctx.Report.Converted(Category, PathOf(ctx, receiver.transform),
                $"{typeName} receiver -> native ContactReceiver driving \"{receiver.parameter}\"" +
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

        /// <summary>
        /// A child of whatever the VRChat contact was anchored to — its rootTransform when it set
        /// one, otherwise its own transform — left at identity so the component's own
        /// localPosition/localRotation carry the offset, as the native system expects.
        /// </summary>
        static GameObject NativeContactObject(Transform root, Transform fallback, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root != null ? root : fallback, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go;
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
