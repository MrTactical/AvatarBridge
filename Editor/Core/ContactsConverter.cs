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
                // Both halves of this are read off the shipping client, and they are the reason
                // native contacts are not the default.
                //
                // NAK.Contacts.ContactAnimator.ApplyValue writes the parameter with
                // animator.SetFloat / SetBool — straight at the Animator. The outbound sync cache
                // is only updated inside CVRAnimatorManager's own setters, so a value written that
                // way never leaves the machine. The legacy path does the opposite:
                // TriggerToContact calls PlayerSetup.ChangeAnimatorParam, which goes through the
                // manager and therefore syncs.
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

        /// <summary>
        /// Names the receivers only another copy of this same avatar can ever set off.
        ///
        /// A contact tag is just a word, and a receiver fires only when something SENDS that word.
        /// Body-part tags are fine — everyone has hands. A tag the author invented ("pump",
        /// "Balloon", a system's own private name) exists nowhere else in the game, so that
        /// receiver is dead to every player who isn't wearing this avatar. That is often exactly
        /// what the author intended, and just as often a surprise, so it is worth stating plainly
        /// instead of leaving someone to wonder why nobody can trigger it.
        /// </summary>
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

        /// <summary>Body-part tags every player carries, whichever platform the avatar came from.</summary>
        static readonly HashSet<string> UniversalTags = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "Head", "Torso", "Foot", "FootL", "FootR",
            "FingerMiddle", "FingerMiddleL", "FingerMiddleR",
            "FingerRing", "FingerRingL", "FingerRingR",
            "FingerLittle", "FingerLittleL", "FingerLittleR",
        };

        /// <summary>
        /// VRChat contact tag -> the ChilloutVR pointer type that means the same thing, so an
        /// ordinary ChilloutVR player can trigger a converted receiver.
        ///
        /// A receiver only ever fires for a tag something else is actually SENDING, and the two
        /// platforms name the same body parts differently. Everyone in ChilloutVR carries pointers
        /// on their hands and index fingers whatever avatar they're wearing — the client turns each
        /// CVRPointer into a contact sender tagged with its <c>type</c> string
        /// (<c>PointerToContact.Create</c>) — but those types are "LeftHand", "RightHand", "index",
        /// where VRChat says "HandL", "HandR", "FingerIndexL". A converted head-pat receiver
        /// listening for "HandR" therefore sits there forever, because nothing in the game sends
        /// that word.
        ///
        /// "Hand" happens to be spelled the same on both platforms, which is why some converted
        /// contacts work and others don't — a difference that looks arbitrary until you see the
        /// list.
        ///
        /// Adding rather than replacing: the VRChat tags stay so converted avatars still trigger
        /// each other exactly as they did, and the ChilloutVR names are extra ways in.
        /// </summary>
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

        /// <summary>
        /// Native contact components address their parameter by NAME, so every rename the animator
        /// merge applied has to be followed here too. Runs after the merge for that reason.
        ///
        /// Without it a contact whose parameter was sanitised for the CCK — or made local, which
        /// the native system requires — kept pointing at a name the finished controller no longer
        /// declares, and drove nothing. Nothing in the controller looks wrong when this happens:
        /// it renamed itself consistently, and the component is not part of it.
        /// </summary>
        public static void RepointContactParameters(BridgeContext ctx)
        {
            var type = FindType(NakAnimator);
            if (type == null || ctx.AppliedParameterRenames.Count == 0)
            {
                return;
            }
            var field = type.GetField("parameter", BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
            {
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
            // Same widening as the CVRAdvancedAvatarSettingsTrigger path — a native receiver is
            // matched against the very same tags, so it needs the ChilloutVR pointer names too.
            SetMember(contact, "collisionTags", WithChilloutVrPointerTypes(receiver.collisionTags));
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

            // The contact writes a LOCAL parameter and a driver copies it into the one the
            // animations already read. Costs no sync bits that were not already being spent:
            // the original is unprefixed, so ChilloutVR has always counted it against the 3200
            // and always transmitted it — it simply transmitted a value nothing ever wrote,
            // because the native path writes at the Animator and never through the manager.
            // The bridge does not buy bits, it makes bits already paid for do something.
            //
            // localOnly receivers are left alone: the author asked for local, and honouring that
            // is the whole point of the flag.
            //
            // PROXIMITY receivers are left alone too, and that is not an oversight. A driver
            // writes a value on entering a state, so it can carry an on/off reading exactly and
            // an analog one only in steps — and the steps would replace the smooth reading the
            // WEARER gets today. Trading their working analog contact for a stepped one other
            // people can see is not a trade this tool makes silently, so a proximity receiver
            // keeps behaving exactly as it does without the option, and the report says so.
            string driven = receiver.parameter;
            bool analog = typeName.Contains("Proximity");
            if (ctx.Settings.syncNativeContacts && !receiver.localOnly
                && !driven.StartsWith("#", System.StringComparison.Ordinal))
            {
                if (analog)
                {
                    ctx.Report.Skipped(Category, PathOf(ctx, receiver.transform),
                        $"\"{driven}\" is a proximity contact, so it is not carried to other " +
                        "players even with the option on: it reports how close the toucher is, " +
                        "and that whole range cannot be carried without replacing the smooth " +
                        "value you see now with a handful of steps. It behaves exactly as it " +
                        "would with the option off. On/off contacts on this avatar are carried " +
                        "normally. Switch this one to the legacy contact path if other players " +
                        "need to see what it drives.");
                }
                else
                {
                    string local = "#" + driven + "_contact";
                    ctx.BridgedContacts.Add((local, driven));
                    driven = local;
                }
            }
            // Without the bridge, the parameter the contact writes has to become local. Left
            // synced it is written raw by the contact and then overwritten by the AAS stream's
            // declared default, which is the "it only kinda syncs sometimes" people report — and
            // it can cost the WEARER the contact too, not just everyone else. The rename pass
            // moves the declaration, every clip binding and every condition together, so the
            // animations keep reading whatever the parameter ends up called.
            //
            // Not applied when the bridge is on: there the contact already writes its own "#"
            // name and a driver carries the value into the original, which must stay synced to
            // be broadcast at all.
            if (driven == receiver.parameter
                && !driven.StartsWith("#", System.StringComparison.Ordinal))
            {
                // Unless a MENU control drives the same parameter. Making that local would take
                // the entry off the network to fix a contact, which is a trade the wearer never
                // asked for and would not connect to this setting. Said out loud instead: the
                // contact is the thing that misbehaves, and the bridge fixes it without moving
                // anyone's menu.
                bool onMenu = ctx.CvrAvatar?.avatarSettings?.settings != null
                    && ctx.CvrAvatar.avatarSettings.settings
                        .Any(s => s != null && s.machineName == driven);
                if (onMenu)
                {
                    ctx.Report.Warning(Category, PathOf(ctx, receiver.transform),
                        $"\"{driven}\" is driven by this native contact AND by a menu control, so it " +
                        "is left synced. A native contact writes straight at the Animator without " +
                        "filling the outbound buffer, so the sync stream writes the declared default " +
                        "back over whatever the contact set — the contact will behave erratically. " +
                        "Turn on \"Let native contacts reach other players\" to fix it without " +
                        "touching the menu entry, or move the menu control to its own parameter.");
                }
                else
                {
                    ctx.LocalContactParameters.Add(driven);
                }
            }

            var animator = host.AddComponent(FindType(NakAnimator));
            SetMember(animator, "animator", ctx.Target.GetComponent<Animator>());
            SetMember(animator, "parameter", driven);

            ctx.ContactParameters.Add(receiver.parameter);
            ctx.Report.Converted(Category, PathOf(ctx, receiver.transform),
                $"{typeName} receiver -> native ContactReceiver driving \"{driven}\"" +
                (driven != receiver.parameter
                    ? $", carried to \"{receiver.parameter}\" by a driver so other players see it"
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


        /// <summary>
        /// The object a VRC contact's shape is actually anchored to. VRChat positions the shape
        /// relative to <c>rootTransform</c> when it is set — the component itself often lives
        /// somewhere central (VRCFury bakes them that way; hand-authored head-pat receivers do it
        /// too) while the shape rides a bone. The native path always honoured this; the legacy
        /// path used to parent under the component's own object, so any contact using the
        /// override converted mis-anchored and did not follow its bone. Found by a completion
        /// verification pass, wild frequency measured by ComponentCensus.
        ///
        /// One VRChat behaviour is knowingly NOT reproduced by anchoring here: disabling the
        /// COMPONENT's GameObject disables the contact in VRChat even when the shape rides a
        /// different bone. Animated enable curves are repointed at the host and still work; a raw
        /// object toggle of the component's (now different) object no longer reaches it. The
        /// native path has always had the same trade, and a shape that follows its bone is the
        /// half testers actually see.
        /// </summary>
        static GameObject AnchorOf(VRC.Dynamics.ContactBase contact)
        {
            return contact.rootTransform != null
                ? contact.rootTransform.gameObject
                : contact.gameObject;
        }

        /// <summary>
        /// Remembers where a VRC contact's replacement landed, keyed by the ORIGINAL component's
        /// animator path — which is exactly what an m_Enabled curve binding carries. The path is
        /// captured here, before the VRC component is destroyed.
        /// </summary>
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

        /// <summary>
        /// Rewires animated contact on/off switches at the converted contacts. Runs after the
        /// animator merge, from BridgeConverter, exactly like the constraint-curve repoint.
        ///
        /// VRChat avatars animate <c>VRCContactReceiver.m_Enabled</c> to switch a contact off —
        /// "disable head pats" is built this way. Conversion deletes that component, and a curve
        /// still addressing it plays as silence: the menu entry converts, the parameter syncs, the
        /// layer plays, and the contact never turns off. Found by a tester reading the converted
        /// inspector against the VRChat one.
        ///
        /// The retarget is the generated host object's ACTIVE state, not the new component's
        /// enabled flag, and the choice is from the decompiled client, both paths:
        ///   - Native: NAK.Contacts.ContactBase registers in OnEnable and de-registers in
        ///     OnDisable, so object active works — and it also carries
        ///     OnDidApplyAnimationProperties, so these components are BUILT to be animated.
        ///   - Legacy: TriggerToContact.Create DISABLES the CVRAdvancedAvatarSettingsTrigger
        ///     wrapper the moment it builds the backing contact, so animating the wrapper's
        ///     enabled flag does nothing — but the backing contact is created on the wrapper's
        ///     own GameObject, so deactivating the object disables it properly.
        /// Every replacement lives on a generated host object of its own, which is what makes one
        /// rule serve both paths. A legacy sender with several tags became several pointer
        /// objects, so one enable curve fans out to each.
        /// </summary>
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
                    //   position.xyz  -> legacy: the host TRANSFORM carries the offset, so the
                    //                    curve maps 1:1 onto m_LocalPosition. Native: the host
                    //                    sits at identity and the offset lives in the component's
                    //                    localPosition FIELD — animating that works because
                    //                    ContactBase carries OnDidApplyAnimationProperties.
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
