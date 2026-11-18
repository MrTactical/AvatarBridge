// VRCFury's bake patches the plug shader with VRChat-only SPS, and the
// patcher refuses an already patched shader. Turning the plug's SPS flag off
// first loses only VRCFury's bake texture, which YapsBaker replaces.
// The flags are on the user's own avatar, so they are always put back.
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

        // Per-plug settings, read before the bake destroys the component.
        // Anything missed becomes a default: overrun came out "yes" on every
        // plug. Keyed by KeyOf the plug's object, which survives the bake as
        // the parent of BakedSpsPlug.
        public static readonly Dictionary<string, bool> AuthoredOverrun =
            new Dictionary<string, bool>();

        // Tags, same reason and key: the bake keeps only 32-bit hashes, which
        // cannot be turned back into words.
        public static readonly Dictionary<string, List<string>> AuthoredSocketTags =
            new Dictionary<string, List<string>>();
        public static readonly Dictionary<string, List<string>> AuthoredAnswers =
            new Dictionary<string, List<string>>();
        public static readonly Dictionary<string, List<string>> AuthoredRefuses =
            new Dictionary<string, List<string>>();

        // The same lists for the wearer's OWN sockets, which YAPS answers as
        // own-socket ticks rather than the tag test. Plugs with hip avoidance
        // off may enter the wearer's own hip sockets.
        public static readonly Dictionary<string, List<string>> AuthoredSelfAnswers =
            new Dictionary<string, List<string>>();
        public static readonly Dictionary<string, List<string>> AuthoredSelfRefuses =
            new Dictionary<string, List<string>>();
        public static readonly HashSet<string> AuthoredEntersOwnHips = new HashSet<string>();

        // SPS's default "global" tag. Its number means nothing here, but the
        // behaviour carries: a plug with it answers anything, without it only
        // what it named.
        const string Shared = YapsTags.Shared;

        // By key, and by name only where the name is unique: the name is the
        // fallback for objects the bake moved, and VRCFury names every plug
        // "SPS Plug", so a name key gave one plug's settings to all.
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

        // Path from the avatar root, "#n" after n earlier same-named siblings.
        // The bake keeps sibling order and appends what it adds.
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
                    $"{BridgeElements.Count(shared.Count, "object carries two plugs or two sockets, so its", "objects carry two plugs or two sockets, so their")} own settings were not carried",
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
                    // Not silent: a VRCFury update that drops the switch puts
                    // two deforms on the plug (Fury's and YAPS on top).
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
                    $"Asked VRCFury not to patch {BridgeElements.Count(prep._flipped.Count, "plug shader", "plug shaders")} before baking",
                    "A shader carrying Fury's deform can't take ours. The rest of the bake is kept, and the " +
                    "plugs are set back once it finishes.");
            }
            if (prep._unswitchable.Count > 0)
            {
                ctx.Report.Warning(Category,
                    $"Could not ask VRCFury to leave {BridgeElements.Count(prep._unswitchable.Count, "plug shader", "plug shaders")} alone",
                    "This version of VRCFury has no switch this tool knows for turning its own plug deform " +
                    "off, so VRCFury may deform the plug and YAPS may deform it again, and the two fight. " +
                    "Check each plug in game before uploading. If one bends or stretches wrongly, this is " +
                    "why, and it wants reporting with your VRCFury version. Plugs: " +
                    string.Join(", ", prep._unswitchable));
            }
            return prep;
        }

        // A plug with the shared tag answers anything; its list narrows only
        // when the author turned that off. Without this a converted plug
        // refused every socket on the avatar.
        static void ReadTags(GameObject source, List<Component> plugs, List<Component> sockets,
            Dictionary<Component, string> plugKeys, Dictionary<Component, string> socketKeys)
        {
            foreach (var component in plugs)
            {
                if (!plugKeys.TryGetValue(component, out string plug)) continue;
                var answers = Rules(component, "includeTags", self: false);
                var selfAnswers = Rules(component, "includeTags", self: true);
                // First: the bake keeps four, and appended it was cut from a
                // plug with four tags of its own.
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
            // Every humanoid bone, so the climb stops at the first as SPS's
            // does. Named ones alone made a forearm socket "chest" and a thigh
            // socket "hips", breaking the hip pair.
            for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
            {
                var t = animator.GetBoneTransform(bone);
                if (t == null || map.ContainsKey(t)) continue;
                map[t] = Named.FirstOrDefault(n => n.Bone == bone).Tags ?? new string[0];
            }
            return map;
        }

        // Climb the parents, not the nearest bone: on a short neck a mouth
        // socket is nearer the chest than the head.
        static List<string> Bones(Transform socket, Dictionary<Transform, string[]> bones)
        {
            for (var at = socket; at != null; at = at.parent)
            {
                if (bones.TryGetValue(at, out var tags)) return tags.ToList();
            }
            return new List<string>();
        }

        // Two hip sockets: the further forward is the front. With three or
        // more, the two nearest along forward, lower path first on a tie, as
        // SPS does. Skipping the split let a plug refusing "hipsback" in.
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

        // Own sockets, others', or both. Neither ticked (or an older rule
        // without the fields) reads as both, so no rule is dropped.
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
