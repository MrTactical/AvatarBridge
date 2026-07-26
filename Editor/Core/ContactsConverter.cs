#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
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
        // than an impersonation: same shapes, same collision tags, real proximity, and localOnly
        // finally honoured. They also need no Unity collider — the shape lives on the component —
        // and because they sit on the avatar whitelist rather than the local-only one, every
        // client simulates them for every avatar. The value is reproduced on each machine instead
        // of being synced, which is why nothing here costs sync bits.
        //
        // ContactStubPatcher supplies the declarations; the game holds the implementation.

        static bool UseNativeContacts(BridgeContext ctx)
        {
            if (!ctx.Settings.useNativeContacts)
            {
                return false;
            }
#if AVATARBRIDGE_CONTACTS
            return true;
#else
            ctx.Report.Warning(Category, "Native contacts unavailable; used the legacy path",
                "\"Use native contacts\" is on, but ChilloutVR's contact components aren't declared in this " +
                "project. That normally means the installed CCK is newer than the one AvatarBridge verified " +
                "its declarations against, so it declined to generate them — see the console for the exact " +
                "version. Contacts were converted to pointers and triggers instead.");
            return false;
#endif
        }

#if AVATARBRIDGE_CONTACTS
        static void ConvertSenderNative(BridgeContext ctx, VRCContactSender sender)
        {
            if (sender.collisionTags.Count == 0)
            {
                Object.DestroyImmediate(sender);
                return;
            }

            var host = NativeContactObject(sender.rootTransform, sender.transform, "Contact_Sender");
            var contact = host.AddComponent<NAK.Contacts.ContactSender>();
            ApplyShape(contact, sender.shapeType, sender.radius, sender.height, sender.position, sender.rotation);
            contact.collisionTags = sender.collisionTags.Distinct().ToArray();

            ctx.Report.Converted(Category, PathOf(ctx, sender.transform),
                $"Sender -> native ContactSender ({string.Join(", ", contact.collisionTags)})");
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

            var contact = host.AddComponent<NAK.Contacts.ContactReceiver>();
            ApplyShape(contact, receiver.shapeType, receiver.radius, receiver.height,
                receiver.position, receiver.rotation);
            contact.collisionTags = receiver.collisionTags.Distinct().ToArray();
            contact.allowSelf = receiver.allowSelf;
            contact.allowOthers = receiver.allowOthers;
            contact.localOnly = receiver.localOnly;

            string typeName = receiver.receiverType.ToString();
            if (typeName.Contains("OnEnter"))
            {
                contact.receiverType = NAK.Contacts.ReceiverType.OnEnter;
            }
            else if (typeName.Contains("Proximity"))
            {
                // 1 at the centre falling to 0 at the edge, the same reading VRChat gives.
                contact.receiverType = NAK.Contacts.ReceiverType.ProximitySenderToReceiver;
            }
            else
            {
                contact.receiverType = NAK.Contacts.ReceiverType.Constant;
            }

            var animator = host.AddComponent<NAK.Contacts.ContactAnimator>();
            animator.animator = ctx.Target.GetComponent<Animator>();
            animator.parameter = receiver.parameter;

            ctx.ContactParameters.Add(receiver.parameter);
            ctx.Report.Converted(Category, PathOf(ctx, receiver.transform),
                $"{typeName} receiver -> native ContactReceiver driving \"{receiver.parameter}\"" +
                (receiver.localOnly ? " (local only, now actually honoured)" : ""));
            Object.DestroyImmediate(receiver);
        }

        static void ApplyShape(NAK.Contacts.ContactBase contact,
            VRC.Dynamics.ContactBase.ShapeType shapeType, float radius, float height,
            Vector3 position, Quaternion rotation)
        {
            bool sphere = shapeType == VRC.Dynamics.ContactBase.ShapeType.Sphere;
            contact.shapeType = sphere ? NAK.Contacts.ShapeType.Sphere : NAK.Contacts.ShapeType.Capsule;
            contact.radius = radius;
            contact.height = height;
            contact.localPosition = position;
            contact.localRotation = sphere ? Quaternion.identity : rotation;
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
#else
        static void ConvertSenderNative(BridgeContext ctx, VRCContactSender sender) => ConvertSender(ctx, sender);
        static void ConvertReceiverNative(BridgeContext ctx, VRCContactReceiver receiver) => ConvertReceiver(ctx, receiver);
#endif

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
