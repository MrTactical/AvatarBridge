// VRCFury patches the plug's shader with SPS during its own bake. That
// patched shader is VRChat-only, and the patcher correctly refuses to
// touch a shader that already carries it, so a normal bake would leave
// no clean input to work from.
//
// Turning the plug's own SPS flag off before the bake solves it, and
// costs nothing that is needed: the plug and socket objects, the
// protocol lights and the contacts all still appear, and only VRCFury's
// own bake texture is lost, which is what YapsBaker replaces.
//
// The flags are flipped on the user's own avatar in their own scene, so
// they are put back afterwards no matter how the bake ends.
#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using AvatarBridge.Yaps;

namespace AvatarBridge
{
    public class YapsBakePrep
    {
        const string Category = "YAPS";
        readonly List<(Component plug, bool was)> _flipped = new List<(Component, bool)>();

        // The author's own per-plug settings, read off the component while it
        // still exists. The bake destroys it, so anything not captured here is
        // lost and quietly replaced by a default, which is what happened to
        // overrun: VRCFury lets an author say whether the tip may travel past
        // the socket, and every converted plug was getting "yes" regardless.
        //
        // Keyed by the object the plug component sits on, which survives the
        // bake as the parent of BakedSpsPlug.
        public static readonly Dictionary<string, bool> AuthoredOverrun =
            new Dictionary<string, bool>();

        // Tags, same reason and same key. SPS writes them as 32-bit hashes
        // into a generated animation clip, so the baked avatar has the
        // numbers and not the words; the components still hold the words,
        // and a hash cannot be turned back into one.
        public static readonly Dictionary<string, List<string>> AuthoredSocketTags =
            new Dictionary<string, List<string>>();
        public static readonly Dictionary<string, List<string>> AuthoredAnswers =
            new Dictionary<string, List<string>>();
        public static readonly Dictionary<string, List<string>> AuthoredRefuses =
            new Dictionary<string, List<string>>();

        // The same two lists for the wearer's OWN sockets. SPS asks each rule
        // which side it governs, and YAPS answers the two sides in different
        // places: the lists above become the plug's tag test, these become its
        // own-socket ticks. Plugs whose author turned hip avoidance off may
        // enter the wearer's own hip sockets.
        public static readonly Dictionary<string, List<string>> AuthoredSelfAnswers =
            new Dictionary<string, List<string>>();
        public static readonly Dictionary<string, List<string>> AuthoredSelfRefuses =
            new Dictionary<string, List<string>>();
        public static readonly HashSet<string> AuthoredEntersOwnHips = new HashSet<string>();

        // SPS's "global" tag, which nearly every socket and plug carries by
        // default. It is a fixed number over there rather than a hashed
        // word; the number cannot mean anything here, since a VRChat plug
        // and a ChilloutVR socket never meet, but the BEHAVIOUR has to
        // carry: a plug with it answers anything, and a plug without it
        // answers only what it named.
        const string Shared = YapsTags.Shared;

        public static YapsBakePrep Begin(BridgeContext ctx, GameObject source)
        {
            var prep = new YapsBakePrep();
            AuthoredOverrun.Clear();
            AuthoredSocketTags.Clear();
            AuthoredAnswers.Clear();
            AuthoredRefuses.Clear();
            AuthoredSelfAnswers.Clear();
            AuthoredSelfRefuses.Clear();
            AuthoredEntersOwnHips.Clear();
            if (ctx == null || !ctx.Settings.convertYapsSystems || source == null)
            {
                return prep;
            }

            ReadTags(source);

            foreach (var component in source.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().Name != "VRCFuryHapticPlug")
                {
                    continue;
                }
                var overrun = component.GetType().GetField("spsOverrun",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (overrun != null && overrun.FieldType == typeof(bool))
                {
                    AuthoredOverrun[component.gameObject.name] = (bool) overrun.GetValue(component);
                }

                var field = component.GetType().GetField("enableSps",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null || field.FieldType != typeof(bool))
                {
                    continue;
                }
                bool was = (bool) field.GetValue(component);
                if (!was)
                {
                    continue;
                }
                field.SetValue(component, false);
                EditorUtility.SetDirty(component);
                prep._flipped.Add((component, true));
            }

            if (prep._flipped.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"Asked VRCFury not to patch {prep._flipped.Count} plug shader(s) before baking",
                    "A shader carrying Fury's deform can't take ours. The rest of the bake is kept, and the " +
                    "plugs are set back once it finishes.");
            }
            return prep;
        }

        // Everything the bake is about to destroy, in words.
        //
        // A plug carrying the shared tag answers anything, so its own list
        // only narrows when the author turned that off. Reproducing that is
        // what stops a converted plug refusing every socket on the avatar:
        // carrying an include list without carrying what the sockets ARE
        // would do exactly that.
        static void ReadTags(GameObject source)
        {
            var sockets = new List<Component>();
            foreach (var component in source.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                string type = component.GetType().Name;
                if (type == "VRCFuryHapticSocket") { sockets.Add(component); continue; }
                if (type != "VRCFuryHapticPlug") continue;

                string plug = component.gameObject.name;
                var answers = Rules(component, "includeTags", self: false);
                var selfAnswers = Rules(component, "includeTags", self: true);
                if (Flag(component, "useSharedTag")) { answers.Add(Shared); selfAnswers.Add(Shared); }
                var refuses = Rules(component, "excludeTags", self: false);
                var selfRefuses = Rules(component, "excludeTags", self: true);
                if (answers.Count > 0) AuthoredAnswers[plug] = answers;
                if (refuses.Count > 0) AuthoredRefuses[plug] = refuses;
                if (selfAnswers.Count > 0) AuthoredSelfAnswers[plug] = selfAnswers;
                if (selfRefuses.Count > 0) AuthoredSelfRefuses[plug] = selfRefuses;
                // Missing reads as on: the safe side, and what YAPS does anyway.
                var hips = component.GetType().GetField("useHipAvoidance",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (hips != null && !Flag(component, "useHipAvoidance")) AuthoredEntersOwnHips.Add(plug);
            }

            var animator = source.GetComponentInChildren<Animator>(true);
            var bones = BoneMap(animator);
            var derived = new Dictionary<Component, List<string>>();
            foreach (var socket in sockets)
            {
                derived[socket] = Flag(socket, "useSharedTag")
                    ? Bones(socket.transform, bones)
                    : new List<string>();
            }
            FrontAndBack(derived, animator);

            foreach (var socket in sockets)
            {
                var tags = Strings(socket, "tags");
                tags.AddRange(derived[socket]);
                if (Flag(socket, "useSharedTag")) tags.Add(Shared);
                if (tags.Count > 0) AuthoredSocketTags[socket.gameObject.name] = tags;
            }
        }

        // The bones SPS names a socket after. Nothing else is a tag there,
        // so nothing else is worth mapping.
        static readonly (HumanBodyBones Bone, string[] Tags)[] Named =
        {
            (HumanBodyBones.Hips, new[] { "hips" }),
            (HumanBodyBones.Head, new[] { "head" }),
            (HumanBodyBones.Jaw, new[] { "head" }),
            (HumanBodyBones.Chest, new[] { "chest" }),
            (HumanBodyBones.UpperChest, new[] { "chest" }),
            (HumanBodyBones.LeftHand, new[] { "hand", "handleft" }),
            (HumanBodyBones.RightHand, new[] { "hand", "handright" }),
            (HumanBodyBones.LeftFoot, new[] { "foot", "footleft" }),
            (HumanBodyBones.LeftToes, new[] { "foot", "footleft" }),
            (HumanBodyBones.RightFoot, new[] { "foot", "footright" }),
            (HumanBodyBones.RightToes, new[] { "foot", "footright" }),
        };

        static Dictionary<Transform, string[]> BoneMap(Animator animator)
        {
            var map = new Dictionary<Transform, string[]>();
            if (animator == null || !animator.isHuman) return map;
            foreach (var entry in Named)
            {
                var t = animator.GetBoneTransform(entry.Bone);
                if (t != null && !map.ContainsKey(t)) map[t] = entry.Tags;
            }
            return map;
        }

        // Climb to the bone the socket hangs off rather than measuring to
        // the nearest one. A socket is parented where it belongs, and a
        // distance answers wrongly as soon as two bones sit close: a mouth
        // socket is nearer the chest than the head on a short neck.
        static List<string> Bones(Transform socket, Dictionary<Transform, string[]> bones)
        {
            for (var at = socket; at != null; at = at.parent)
            {
                if (bones.TryGetValue(at, out var tags)) return tags.ToList();
            }
            return new List<string>();
        }

        // Two sockets on the hips: the one further forward is the front.
        //
        // Only when there are exactly two, which is SPS's own rule as well.
        // With three the choice is a heuristic, and naming a front socket
        // "hipsback" is worse than leaving both unnamed, since a plug that
        // refuses one would then refuse the wrong one.
        static void FrontAndBack(Dictionary<Component, List<string>> derived, Animator animator)
        {
            if (animator == null || !animator.isHuman) return;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hips == null || hand == null) return;
            var onHips = derived.Where(p => p.Value.Contains("hips")).Select(p => p.Key).ToList();
            if (onHips.Count != 2) return;
            var right = hand.position - hips.position;
            if (right.sqrMagnitude <= 0.000001f) return;
            var forward = Vector3.Cross(right.normalized, Vector3.up);
            if (forward.sqrMagnitude <= 0.000001f) return;
            forward.Normalize();
            var ordered = onHips
                .OrderBy(c => Vector3.Dot(c.transform.position - hips.position, forward))
                .ToList();
            derived[ordered[0]].Add("hipsback");
            derived[ordered[1]].Add("hipsfront");
        }

        static bool Flag(Component component, string name)
        {
            var field = component.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field != null && field.FieldType == typeof(bool) && (bool) field.GetValue(component);
        }

        static List<string> Strings(Component component, string name)
        {
            var found = new List<string>();
            var field = component.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (!(field?.GetValue(component) is System.Collections.IEnumerable list)) return found;
            foreach (var entry in list)
            {
                if (entry is string tag && !string.IsNullOrWhiteSpace(tag)) found.Add(tag.Trim());
            }
            return found;
        }

        // A plug rule says which side it governs: the wearer's own sockets,
        // everybody else's, or both. Neither ticked, or a rule from a version
        // without the fields, reads as both, so no rule the author wrote is
        // dropped.
        static List<string> Rules(Component component, string name, bool self)
        {
            var found = new List<string>();
            var field = component.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (!(field?.GetValue(component) is System.Collections.IEnumerable list)) return found;
            foreach (var entry in list)
            {
                if (entry == null) continue;
                var tag = entry.GetType().GetField("tag",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (!(tag?.GetValue(entry) is string text) || string.IsNullOrWhiteSpace(text)) continue;
                bool onSelf = Side(entry, "allowSelf"), onOthers = Side(entry, "allowOthers");
                if (!onSelf && !onOthers) onSelf = onOthers = true;
                if (self ? onSelf : onOthers) found.Add(text.Trim());
            }
            return found;
        }

        static bool Side(object entry, string name)
        {
            var field = entry.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field != null && field.FieldType == typeof(bool) && (bool) field.GetValue(entry);
        }

        public void Restore()
        {
            foreach (var (plug, was) in _flipped)
            {
                if (plug == null)
                {
                    continue;
                }
                var field = plug.GetType().GetField("enableSps",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(plug, was);
                    EditorUtility.SetDirty(plug);
                }
            }
            _flipped.Clear();
        }
    }
}
#endif
