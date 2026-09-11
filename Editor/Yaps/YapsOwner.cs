// The wearer's owner id, onto every renderer whose material can hold it, so
// a plug can tell its own avatar's sockets from anybody else's at any
// distance. The atlas carries the socket's copy; see yaps_atlas.cginc.
// Then which of those own sockets each plug may enter: a number per socket
// on its writer, a bit per number on the plug.
//
// ChilloutVR's parameter stream hands over a CRC32 of the wearer's user id,
// but only on the wearer's own copy: the stream is a local component and is
// destroyed on everybody else's. So the parameter SYNCS, and a remote copy
// reads zero until its first sync, which the shaders take as unknown and
// answer with the geometric guess they used before.
#if CVR_CCK_EXISTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsOwner
    {
        public const string Parameter = "YAPS/Owner";
        const string LayerName = "YAPS owner";
        const string Property = "_YAPS_Owner";

        // The client's enum numbers, written as numbers: a CCK whose enum
        // predates a name still serialises what the client reads.
        const int SeedOwner = 90000, AvatarAnimator = 2, Mod = 141, AvatarReference = 1;

        // The client's Mod works on the id as an integer, and a float holds
        // every integer below 2^24, so this keeps 24 bits of it whole.
        const float Modulus = 16777216f;

        // Idempotent, like the lighthouse: run it after any plug or socket
        // change. With nothing left carrying the property it takes itself out.
        public static string Wire(GameObject root, AnimatorController controller)
        {
            if (root == null || controller == null) return null;
            int unnumbered = ApplySelf(root);
            var targets = root.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.sharedMaterials.Any(m => m != null && m.HasProperty(Property)))
                .ToList();
            bool had = RemoveLayer(controller);
            if (targets.Count == 0)
            {
                had |= RemoveParameter(controller);
                had |= Unstream(root);
                return had ? "owner id removed: nothing left carries it" : null;
            }
            if (!Stream(root))
            {
                return "owner id not wired: this CCK's parameter stream has no entry list";
            }
            if (!controller.parameters.Any(p => p.name == Parameter))
            {
                controller.AddParameter(Parameter, AnimatorControllerParameterType.Float);
            }
            AddLayer(root.transform, controller, targets);
            return $"owner id on {targets.Count} renderer(s), from the parameter stream, synced (32 bits)"
                   + (unnumbered > 0
                       ? $"; {unnumbered} socket(s) past the first {MaxSelfSockets} carry no number, so the plugs judge them by the hips alone"
                       : "");
        }

        // Numbers the atlas carries, 1 to 15. Must match YAPS_ATLAS_SOCKETMAX.
        public const int MaxSelfSockets = 15;

        // Every socket of the avatar numbered on its atlas writer, and every
        // plug told which of them it may enter, a bit per number. Materials
        // only, nothing in the animator, so the inspector can run it on every
        // click. Returns how many sockets were past the numbers.
        //
        // Numbered by hierarchy path, so the order holds from build to build;
        // a plug's choices name sockets, not numbers, so a socket added in the
        // middle renumbers the rest and every answer follows.
        public static int ApplySelf(GameObject root)
        {
            if (root == null) return 0;
            var human = HumanBones(root.GetComponent<Animator>());
            var sockets = root.GetComponentsInChildren<YapsSocket>(true)
                .OrderBy(s => AnimationUtility.CalculateTransformPath(s.transform, root.transform), StringComparer.Ordinal)
                .ToList();
            var numbers = new Dictionary<YapsSocket, int>();
            int byDefault = 0;
            for (int i = 0; i < sockets.Count; i++)
            {
                int n = i < MaxSelfSockets ? i + 1 : 0;
                numbers[sockets[i]] = n;
                if (n > 0 && !OnHips(sockets[i].transform, human)) byDefault |= 1 << (n - 1);
                foreach (var r in sockets[i].GetComponentsInChildren<Renderer>(true))
                {
                    var numbered = YapsAtlas.Indexed(r.sharedMaterial, n);
                    if (numbered == null || numbered == r.sharedMaterial) continue;
                    Undo.RecordObject(r, "YAPS own sockets");
                    r.sharedMaterial = numbered;
                }
            }

            var told = new HashSet<Renderer>();
            foreach (var plug in root.GetComponentsInChildren<YapsPlug>(true))
            {
                // A default tick leaves the tags to judge, so the shader still
                // applies them; one set by hand is chosen and skips them.
                int chosen = 0;
                foreach (var s in plug.selfEnter)
                    if (s != null && numbers.TryGetValue(s, out int n) && n > 0) chosen |= 1 << (n - 1);
                int mask = byDefault | chosen;
                foreach (var s in plug.selfRefuse)
                    if (s != null && numbers.TryGetValue(s, out int n) && n > 0) mask &= ~(1 << (n - 1));
                chosen &= mask;
                // Every mesh the bake reached, not only the named one.
                var meshes = plug.bakedSlots.Where(b => b != null && b.renderer != null)
                    .Select(b => b.renderer).Append(plug.Target).Where(r => r != null);
                foreach (var r in meshes)
                {
                    if (told.Add(r))
                        foreach (var m in r.sharedMaterials) SetMask(m, mask, chosen);
                }
            }
            // A converted plug has no component to choose with: its author's
            // rules if the material kept any, else the default.
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (told.Contains(r)) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    int mask = RulesMask(m, sockets, human, byDefault, out int chosen);
                    SetMask(m, mask, chosen);
                }
            }
            return Math.Max(sockets.Count - MaxSelfSockets, 0);
        }

        // A converted SPS plug's rules for its wearer's own sockets, kept on
        // its material BY TAG NAME, so the ticks follow a renumbering: every
        // later build works them out again from the sockets as they are.
        // Override tags rather than properties because the shader never
        // reads them, so nothing has to be declared or can go stale.
        const string AnswersKey = "YapsSelfAnswers";
        const string RefusesKey = "YapsSelfRefuses";
        const string HipsKey = "YapsSelfEntersHips";

        public static void KeepSelfRules(Material m, IEnumerable<string> answers, IEnumerable<string> refuses,
                                         bool entersHips)
        {
            if (m == null) return;
            m.SetOverrideTag(AnswersKey, string.Join(",", YapsTags.Listed(answers)));
            m.SetOverrideTag(RefusesKey, string.Join(",", YapsTags.Listed(refuses)));
            m.SetOverrideTag(HipsKey, entersHips ? "1" : "");
        }

        // Self tag rules are the author's whole answer for their own sockets,
        // as SPS reads them, so what they allow is chosen and skips the
        // plug's other tags. Hip avoidance alone chooses nothing.
        static int RulesMask(Material m, List<YapsSocket> sockets,
                             Dictionary<Transform, HumanBodyBones> human, int byDefault, out int chosen)
        {
            chosen = 0;
            string answered = m.GetTag(AnswersKey, false), refused = m.GetTag(RefusesKey, false);
            bool entersHips = m.GetTag(HipsKey, false) == "1";
            if (answered == "" && refused == "" && !entersHips) return byDefault;
            var answers = Words(answered.Split(','));
            var refuses = Words(refused.Split(','));
            int mask = 0;
            for (int i = 0; i < sockets.Count && i < MaxSelfSockets; i++)
            {
                if (!TagsPass(sockets[i].tags, answers, refuses)) continue;
                if (!entersHips && OnHips(sockets[i].transform, human)) continue;
                mask |= 1 << i;
            }
            if (answers.Count > 0 || refuses.Count > 0) chosen = mask;
            return mask;
        }

        // The same test the shader runs on the tag word, on the words: a
        // refused tag refuses, and a non-empty answer list must name one.
        static bool TagsPass(IEnumerable<string> socketTags, HashSet<string> answers, HashSet<string> refuses)
        {
            var tags = socketTags ?? Enumerable.Empty<string>();
            if (answers.Count > 0 && !tags.Any(answers.Contains)) return false;
            return !tags.Any(refuses.Contains);
        }

        static HashSet<string> Words(IEnumerable<string> tags) =>
            new HashSet<string>(YapsTags.Listed(tags), StringComparer.OrdinalIgnoreCase);

        // What the bake keeps of a plug's list: the first four.
        static HashSet<string> Baked(IList<string> tags) =>
            Words(YapsTags.Listed(tags).Take(YapsTags.PlugSlots));

        // Whether a plug may enter this socket of its wearer's when nobody has
        // said: yes, unless it hangs off the hips or the plug's tags, as
        // built, refuse it.
        public static bool EntersByDefault(YapsPlug plug, YapsSocket socket)
        {
            var avatar = socket != null ? socket.GetComponentInParent<CVRAvatar>(true) : null;
            return avatar != null && plug != null
                   && !OnHips(socket.transform, HumanBones(avatar.GetComponent<Animator>()))
                   && TagsPass(socket.tags, Baked(plug.answers), Baked(plug.refuses));
        }

        // The first humanoid bone above the socket, climbed rather than
        // measured. A thigh socket is the thigh's, though the hips are its
        // grandparent. Nothing to go by counts as the hips: the safe side.
        static bool OnHips(Transform socket, Dictionary<Transform, HumanBodyBones> human)
        {
            for (var at = socket; at != null; at = at.parent)
            {
                if (human.TryGetValue(at, out var bone)) return bone == HumanBodyBones.Hips;
            }
            return true;
        }

        static Dictionary<Transform, HumanBodyBones> HumanBones(Animator animator)
        {
            var map = new Dictionary<Transform, HumanBodyBones>();
            if (animator == null || !animator.isHuman) return map;
            for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++)
            {
                var t = animator.GetBoneTransform(b);
                if (t != null && !map.ContainsKey(t)) map[t] = b;
            }
            return map;
        }

        static void SetMask(Material m, int mask, int chosen)
        {
            SetBits(m, "_YAPS_SelfSockets", mask);
            SetBits(m, "_YAPS_SelfChosen", chosen);
        }

        static void SetBits(Material m, string property, int bits)
        {
            if (m == null || !m.HasProperty(property)) return;
            if (Mathf.RoundToInt(m.GetFloat(property)) == bits) return;
            Undo.RecordObject(m, "YAPS own sockets");
            m.SetFloat(property, bits);
            EditorUtility.SetDirty(m);
        }

        // Into the controller ChilloutVR uploads: the overrides, else the
        // base, else the Animator's.
        public static string Wire(CVRAvatar avatar)
        {
            if (avatar == null) return null;
            var shipped = avatar.overrides != null ? avatar.overrides.runtimeAnimatorController : null;
            if (shipped == null && avatar.avatarSettings != null) shipped = avatar.avatarSettings.baseController;
            var animator = avatar.GetComponent<Animator>();
            if (shipped == null && animator != null) shipped = animator.runtimeAnimatorController;
            return Wire(avatar.gameObject, BridgeContext.Underlying(shipped));
        }

        // A direct blend tree weighted by the parameter itself, over a clip
        // setting the property to 1, so the property comes out AS the id.
        // Write defaults on, over a default of zero, so the tree is a plain
        // sum and nothing of last frame's value is left in it.
        static void AddLayer(Transform root, AnimatorController controller, List<Renderer> targets)
        {
            var clip = new AnimationClip { name = LayerName };
            foreach (var r in targets)
            {
                clip.SetCurve(AnimationUtility.CalculateTransformPath(r.transform, root), r.GetType(),
                    "material." + Property, AnimationCurve.Constant(0f, 1f / 60f, 1f));
            }
            var tree = new BlendTree
            {
                name = LayerName,
                blendType = BlendTreeType.Direct,
                hideFlags = HideFlags.HideInHierarchy,
                children = new[] { new ChildMotion { motion = clip, timeScale = 1f, directBlendParameter = Parameter } },
            };
            var machine = new AnimatorStateMachine { name = LayerName, hideFlags = HideFlags.HideInHierarchy };
            // Built after the controller became an asset, so the parts embed
            // themselves or serialise as nothing.
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(controller)))
            {
                AssetDatabase.AddObjectToAsset(machine, controller);
                AssetDatabase.AddObjectToAsset(tree, controller);
                AssetDatabase.AddObjectToAsset(clip, controller);
            }
            var state = machine.AddState("Owner");
            state.motion = tree;
            state.writeDefaultValues = true;
            machine.defaultState = state;

            var layers = controller.layers.ToList();
            layers.Add(new AnimatorControllerLayer { name = LayerName, defaultWeight = 1f, stateMachine = machine });
            controller.layers = layers.ToArray();
            EditorUtility.SetDirty(controller);
        }

        // referenceType by hand. The CCK's inspector sets it only when it is
        // opened, so a stream added from code stays World, and the client
        // computes SeedOwner for an Avatar reference alone.
        static bool Stream(GameObject root)
        {
            var stream = root.GetComponent<CVRParameterStream>();
            if (stream == null) stream = root.AddComponent<CVRParameterStream>();
            var entries = Entries(stream);
            var entryType = entries?.GetType().GetGenericArguments().FirstOrDefault();
            if (entryType == null) return false;

            Unstream(entries);
            var entry = Activator.CreateInstance(entryType);
            Set(entry, "type", SeedOwner);
            Set(entry, "targetType", AvatarAnimator);
            Set(entry, "applicationType", Mod);
            Set(entry, "staticValue", Modulus);
            Set(entry, "parameterName", Parameter);
            entries.Add(entry);
            Set(stream, "referenceType", AvatarReference);
            EditorUtility.SetDirty(stream);
            return true;
        }

        static bool Unstream(GameObject root)
        {
            var stream = root.GetComponent<CVRParameterStream>();
            var entries = stream != null ? Entries(stream) : null;
            if (entries == null || !Unstream(entries)) return false;
            if (entries.Count == 0) Undo.DestroyObjectImmediate(stream);
            else EditorUtility.SetDirty(stream);
            return true;
        }

        static bool Unstream(IList entries)
        {
            bool removed = false;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                var name = e?.GetType().GetField("parameterName")?.GetValue(e) as string;
                if (name != Parameter) continue;
                entries.RemoveAt(i);
                removed = true;
            }
            return removed;
        }

        static IList Entries(CVRParameterStream stream)
        {
            var field = stream.GetType().GetField("entries", BindingFlags.Public | BindingFlags.Instance);
            if (field == null) return null;
            if (!(field.GetValue(stream) is IList list))
            {
                list = (IList)Activator.CreateInstance(field.FieldType);
                field.SetValue(stream, list);
            }
            return list;
        }

        static void Set(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field == null) return;
            field.SetValue(target, field.FieldType.IsEnum ? Enum.ToObject(field.FieldType, value) : value);
        }

        static bool RemoveLayer(AnimatorController controller)
        {
            bool removed = false;
            for (int i = controller.layers.Length - 1; i >= 0; i--)
            {
                if (controller.layers[i].name != LayerName) continue;
                controller.RemoveLayer(i);
                removed = true;
            }
            return removed;
        }

        static bool RemoveParameter(AnimatorController controller)
        {
            var p = controller.parameters.FirstOrDefault(x => x.name == Parameter);
            if (p == null) return false;
            controller.RemoveParameter(p);
            return true;
        }
    }
}
#endif
