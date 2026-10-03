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
        readonly List<Component> _flipped = new List<Component>();
        readonly List<string> _unswitchable = new List<string>();

        // The author's own per-plug settings, read off the component while it
        // still exists. The bake destroys it, so anything not captured here is
        // lost and quietly replaced by a default, which is what happened to
        // overrun: VRCFury lets an author say whether the tip may travel past
        // the socket, and every converted plug was getting "yes" regardless.
        //
        // Keyed by KeyOf the object the plug component sits on, which survives
        // the bake as the parent of BakedSpsPlug. PlugKey finds it again.
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

        // Every plug and socket read, by key, and by name where no other of
        // its kind has that name. The name is the fallback for an object the
        // bake moved, and it was the only key once: VRCFury's own menu names
        // every plug "SPS Plug" and every socket "SPS Socket", so the last one
        // read gave its tags and overrun to all of them.
        static readonly HashSet<string> PlugKeys = new HashSet<string>(), SocketKeys = new HashSet<string>();
        static readonly Dictionary<string, string> PlugNames = new Dictionary<string, string>(),
            SocketNames = new Dictionary<string, string>();

        // The key a baked plug's or socket's owner was read under, or null
        // when nothing was read for it.
        public static string PlugKey(Transform owner, Transform root) => Find(owner, root, PlugKeys, PlugNames);
        public static string SocketKey(Transform owner, Transform root) => Find(owner, root, SocketKeys, SocketNames);

        static string Find(Transform owner, Transform root, HashSet<string> keys, Dictionary<string, string> names)
        {
            if (owner == null) return null;
            string key = KeyOf(root, owner);
            if (keys.Contains(key)) return key;
            // Moved by the bake, which also puts its id on the name.
            return names.TryGetValue(YapsScanner.StripFuryId(owner.name), out string named) ? named : null;
        }

        // The path from the avatar root, with "#n" on an object that has n
        // earlier siblings of the same name, so two sockets of one name under
        // one bone are still two keys. The bake keeps it: the copy keeps the
        // order, and what the bake adds goes after.
        static string KeyOf(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (var at = t; at != null && at != root; at = at.parent)
            {
                int n = 0;
                for (int i = 0; at.parent != null && i < at.GetSiblingIndex(); i++)
                {
                    if (at.parent.GetChild(i).name == at.name) n++;
                }
                parts.Insert(0, n > 0 ? at.name + "#" + n : at.name);
            }
            return string.Join("/", parts);
        }

        // Two of a kind on one object have one key and cannot be told apart
        // after the bake, so neither is keyed and both are named in `shared`.
        static Dictionary<Component, string> Index(List<Component> found, Transform root,
            HashSet<string> keys, Dictionary<string, string> names, List<string> shared)
        {
            var keyed = new Dictionary<Component, string>();
            var named = found.GroupBy(c => YapsScanner.StripFuryId(c.gameObject.name))
                .ToDictionary(g => g.Key, g => g.Count());
            foreach (var group in found.GroupBy(c => KeyOf(root, c.transform)))
            {
                if (group.Count() > 1) { shared.Add(group.Key); continue; }
                var one = group.First();
                keyed[one] = group.Key;
                keys.Add(group.Key);
                string name = YapsScanner.StripFuryId(one.gameObject.name);
                if (named[name] == 1) names[name] = group.Key;
            }
            return keyed;
        }

        public static YapsBakePrep Begin(BridgeContext ctx, GameObject source)
        {
            var prep = new YapsBakePrep();
            PlugKeys.Clear();
            SocketKeys.Clear();
            PlugNames.Clear();
            SocketNames.Clear();
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

            var plugs = new List<Component>();
            var sockets = new List<Component>();
            foreach (var component in source.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                string type = component.GetType().Name;
                if (type == "VRCFuryHapticPlug") plugs.Add(component);
                else if (type == "VRCFuryHapticSocket") sockets.Add(component);
            }
            var shared = new List<string>();
            var plugKeys = Index(plugs, source.transform, PlugKeys, PlugNames, shared);
            var socketKeys = Index(sockets, source.transform, SocketKeys, SocketNames, shared);
            if (shared.Count > 0)
            {
                ctx.Report.Warning(Category,
                    $"{shared.Count} object(s) carry two plugs or two sockets, so their own settings were not carried",
                    "Once baked the two cannot be told apart. Each takes the defaults: a plug answers anything and " +
                    "may pass the socket, a socket keeps the shared tag. Give each its own object and convert " +
                    "again: " + string.Join(", ", shared));
            }

            ReadTags(source, plugs, sockets, plugKeys, socketKeys);

            foreach (var component in plugs)
            {
                var overrun = component.GetType().GetField("spsOverrun",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (overrun != null && overrun.FieldType == typeof(bool) && plugKeys.TryGetValue(component, out string key))
                {
                    AuthoredOverrun[key] = (bool) overrun.GetValue(component);
                }

                var field = component.GetType().GetField("enableSps",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null || field.FieldType != typeof(bool))
                {
                    // This used to be a silent continue, and it is the one
                    // place a VRCFury update can put two deforms on a plug:
                    // with the switch gone Fury patches its own shader, and
                    // YAPS patches on top unless the patcher still recognises
                    // what Fury wrote. Nothing about the new system needs to
                    // be known to notice the switch is missing.
                    prep._unswitchable.Add(component.gameObject.name);
                    continue;
                }
                bool was = (bool) field.GetValue(component);
                if (!was)
                {
                    continue;
                }
                field.SetValue(component, false);
                EditorUtility.SetDirty(component);
                prep._flipped.Add(component);
            }

            if (prep._flipped.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"Asked VRCFury not to patch {prep._flipped.Count} plug shader(s) before baking",
                    "A shader carrying Fury's deform can't take ours. The rest of the bake is kept, and the " +
                    "plugs are set back once it finishes.");
            }
            if (prep._unswitchable.Count > 0)
            {
                ctx.Report.Warning(Category,
                    $"Could not ask VRCFury to leave {prep._unswitchable.Count} plug shader(s) alone",
                    "This version of VRCFury has no switch this tool knows for turning its own plug deform " +
                    "off, so VRCFury may deform the plug and YAPS may deform it again, and the two fight. " +
                    "Check each plug in game before uploading. If one bends or stretches wrongly, this is " +
                    "why, and it wants reporting with your VRCFury version. Plugs: " +
                    string.Join(", ", prep._unswitchable));
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
        static void ReadTags(GameObject source, List<Component> plugs, List<Component> sockets,
            Dictionary<Component, string> plugKeys, Dictionary<Component, string> socketKeys)
        {
            foreach (var component in plugs)
            {
                if (!plugKeys.TryGetValue(component, out string plug)) continue;
                var answers = Rules(component, "includeTags", self: false);
                var selfAnswers = Rules(component, "includeTags", self: true);
                // First, never last: the bake keeps four, and the word that
                // widens the list is the one that must not be cut. Appended,
                // a plug with four tags of its own lost it and narrowed to them.
                if (Flag(component, "useSharedTag")) { answers.Insert(0, Shared); selfAnswers.Insert(0, Shared); }
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
            FrontAndBack(derived, animator, source.transform);

            foreach (var socket in sockets)
            {
                if (!socketKeys.TryGetValue(socket, out string key)) continue;
                var tags = Strings(socket, "tags");
                tags.AddRange(derived[socket]);
                if (Flag(socket, "useSharedTag")) tags.Add(Shared);
                if (tags.Count > 0) AuthoredSocketTags[key] = tags;
            }
        }

        // The bones SPS names a socket after. Nothing else is a tag there.
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
            // Every humanoid bone, the unnamed ones with no tags, so the climb
            // stops at the first bone of any kind as SPS's does. Mapping the
            // named ones alone climbed past them: a forearm socket came out
            // "chest", and a thigh socket "hips", which also broke the hip pair.
            for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
            {
                var t = animator.GetBoneTransform(bone);
                if (t == null || map.ContainsKey(t)) continue;
                map[t] = Named.FirstOrDefault(n => n.Bone == bone).Tags ?? new string[0];
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
        // With three or more, the pair is the two nearest the hips along the
        // forward axis, the lower path first on a tie, which is SPS's choice.
        // Dropping the split there instead left a plug refusing "hipsback"
        // free to enter a socket it never entered in VRChat.
        static void FrontAndBack(Dictionary<Component, List<string>> derived, Animator animator, Transform root)
        {
            if (animator == null || !animator.isHuman) return;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hips == null || hand == null) return;
            var onHips = derived.Where(p => p.Value.Contains("hips")).Select(p => p.Key).ToList();
            if (onHips.Count < 2) return;
            var right = hand.position - hips.position;
            if (right.sqrMagnitude <= 0.000001f) return;
            var forward = Vector3.Cross(right.normalized, Vector3.up);
            if (forward.sqrMagnitude <= 0.000001f) return;
            forward.Normalize();
            float Along(Component c) => Vector3.Dot(c.transform.position - hips.position, forward);
            var ordered = onHips
                .OrderBy(c => Mathf.Abs(Along(c)))
                .ThenBy(c => KeyOf(root, c.transform), System.StringComparer.Ordinal)
                .Take(2)
                .OrderBy(Along)
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
            // Only plugs that were on are listed, so on is what goes back.
            foreach (var plug in _flipped)
            {
                if (plug == null)
                {
                    continue;
                }
                var field = plug.GetType().GetField("enableSps",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(plug, true);
                    EditorUtility.SetDirty(plug);
                }
            }
            _flipped.Clear();
        }
    }
}
#endif
