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
            var numbers = NumberWriters(root, true);
            int byDefault = 0;
            foreach (var kv in numbers)
                if (kv.Value > 0 && !OnHips(kv.Key.transform, human)) byDefault |= 1 << (kv.Value - 1);

            var told = new HashSet<Renderer>();
            foreach (var plug in root.GetComponentsInChildren<YapsPlug>(true))
            {
                // A converted plug has a component too, adopted off its
                // material, so its author's Self rules are where it starts.
                // A default tick leaves the tags to judge, so the shader still
                // applies them; one set by hand, or by tag rules, is chosen and
                // skips them.
                int start = byDefault, chosen = 0;
                var rules = RulesOf(plug.Target);
                if (rules != null) start = RulesMask(rules, numbers, human, out chosen);
                foreach (var s in plug.selfEnter)
                    if (s != null && numbers.TryGetValue(s, out int n) && n > 0) chosen |= 1 << (n - 1);
                int mask = start | chosen;
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
            // A plug with no component, its component stripped: its author's
            // rules if the material kept any, else the default.
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (told.Contains(r)) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    var rules = RulesOn(m);
                    int chosen = 0;
                    int mask = rules != null ? RulesMask(rules, numbers, human, out chosen) : byDefault;
                    SetMask(m, mask, chosen);
                }
            }
            return Math.Max(numbers.Count - MaxSelfSockets, 0);
        }

        // Every socket under the root numbered on its atlas writer, in path
        // order so the numbers hold from build to build. The number is also
        // the socket's atlas bucket, (number - 1) mod 8 turned by its owner,
        // so 9 to 15 each share a bucket with one of the first eight: each
        // takes the free partner it sits farthest from at build. Sockets with
        // no avatar carry no owner to turn them, so a root starts from an
        // offset its name picks, or every separate object's first socket
        // would share bucket one.
        public static Dictionary<YapsSocket, int> NumberWriters(GameObject root, bool avatar)
        {
            var sockets = root.GetComponentsInChildren<YapsSocket>(true)
                .OrderBy(s => AnimationUtility.CalculateTransformPath(s.transform, root.transform), StringComparer.Ordinal)
                .ToList();
            int offset = avatar || sockets.Count > 8 ? 0 : (int) (Fnv(root.name) % (uint) (16 - sockets.Count));
            var numbers = new Dictionary<YapsSocket, int>();
            var partners = new List<int>();
            for (int i = 0; i < sockets.Count; i++)
            {
                int n = 0;
                if (i < 8)
                {
                    n = i + 1 + offset;
                    if (i < MaxSelfSockets - 8) partners.Add(i);
                }
                else if (partners.Count > 0)
                {
                    var here = sockets[i].transform.position;
                    int j = partners.OrderByDescending(k => (sockets[k].transform.position - here).sqrMagnitude).First();
                    partners.Remove(j);
                    n = j + 9;
                }
                numbers[sockets[i]] = n;
                foreach (var r in sockets[i].GetComponentsInChildren<Renderer>(true))
                {
                    var numbered = YapsAtlas.Indexed(r.sharedMaterial, n);
                    if (numbered == null || numbered == r.sharedMaterial) continue;
                    Undo.RecordObject(r, "YAPS own sockets");
                    r.sharedMaterial = numbered;
                }
            }
            return numbers;
        }

        // string.GetHashCode is not promised to hold between runs.
        static uint Fnv(string text)
        {
            uint h = 2166136261;
            foreach (char c in text) h = (h ^ c) * 16777619;
            return h;
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

        sealed class SelfRules
        {
            public HashSet<string> Answers, Refuses;
            public bool EntersHips;
            public bool Tagged => Answers.Count > 0 || Refuses.Count > 0;

            public bool Allows(YapsSocket s, Dictionary<Transform, HumanBodyBones> human) =>
                TagsPass(s.tags, Answers, Refuses) && (EntersHips || !OnHips(s.transform, human));
        }

        static SelfRules RulesOn(Material m)
        {
            if (m == null) return null;
            string answered = m.GetTag(AnswersKey, false), refused = m.GetTag(RefusesKey, false);
            bool entersHips = m.GetTag(HipsKey, false) == "1";
            if (answered == "" && refused == "" && !entersHips) return null;
            return new SelfRules
            {
                Answers = Words(answered.Split(',')), Refuses = Words(refused.Split(',')), EntersHips = entersHips,
            };
        }

        static SelfRules RulesOf(Renderer r) =>
            r == null ? null : r.sharedMaterials.Select(RulesOn).FirstOrDefault(x => x != null);

        // Self tag rules are the author's whole answer for their own sockets,
        // as SPS reads them, so what they allow is chosen and skips the
        // plug's other tags. Hip avoidance alone chooses nothing.
        static int RulesMask(SelfRules rules, Dictionary<YapsSocket, int> numbers,
                             Dictionary<Transform, HumanBodyBones> human, out int chosen)
        {
            int mask = 0;
            foreach (var kv in numbers)
                if (kv.Value > 0 && rules.Allows(kv.Key, human)) mask |= 1 << (kv.Value - 1);
            chosen = rules.Tagged ? mask : 0;
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
        // said: what its author's Self tag rules allow, if it kept any; else
        // yes, unless it hangs off the hips or the plug's tags, as built,
        // refuse it.
        public static bool EntersByDefault(YapsPlug plug, YapsSocket socket)
        {
            var avatar = socket != null ? socket.GetComponentInParent<CVRAvatar>(true) : null;
            if (avatar == null || plug == null) return false;
            var human = HumanBones(avatar.GetComponent<Animator>());
            var rules = RulesOf(plug.Target);
            if (rules != null && rules.Tagged) return rules.Allows(socket, human);
            bool hips = rules != null && rules.EntersHips;
            return (hips || !OnHips(socket.transform, human))
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
            return Wire(avatar.gameObject, Shipped(avatar));
        }

        // The controller ChilloutVR will actually run for this avatar.
        public static AnimatorController Shipped(CVRAvatar avatar)
        {
            if (avatar == null) return null;
            var shipped = avatar.overrides != null ? avatar.overrides.runtimeAnimatorController : null;
            if (shipped == null && avatar.avatarSettings != null) shipped = avatar.avatarSettings.baseController;
            var animator = avatar.GetComponent<Animator>();
            if (shipped == null && animator != null) shipped = animator.runtimeAnimatorController;
            return BridgeContext.Underlying(shipped);
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

    // No player in the editor, so every plug and socket read owner 0, which
    // the shaders take as unknown: nothing was the wearer's own, and a plug
    // bent into a socket on its own shaft that the game refuses. A stand-in
    // per avatar makes the editor judge own sockets as the game does. Property
    // blocks only, never saved or uploaded. Play Mode sets the parameter
    // instead, since the owner layer animates the property there.
    [InitializeOnLoad]
    static class YapsOwnerStandIn
    {
        const string Property = "_YAPS_Owner";
        static double _next;
        static MaterialPropertyBlock _block;
        // HasProperty on a Poiyomi material logs a drawer error each call.
        static readonly Dictionary<Material, bool> Carries = new Dictionary<Material, bool>();

        static YapsOwnerStandIn() => EditorApplication.update += Tick;

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 0.5;
            Apply();
        }

        // Also called directly by the runtime tester, where no editor frame ticks.
        internal static void Apply()
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            bool changed = false;
            foreach (var avatar in UnityEngine.Object.FindObjectsOfType<CVRAvatar>(true))
            {
                // Never 0, which is unknown, and inside the 24 bits a float holds.
                float id = 1 + (avatar.GetInstanceID() & 0x7FFFFF);
                if (Application.isPlaying)
                {
                    var animator = avatar.GetComponent<Animator>();
                    if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null
                        && animator.parameters.Any(p => p.name == YapsOwner.Parameter))
                    {
                        animator.SetFloat(YapsOwner.Parameter, id);
                        continue;
                    }
                }
                foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int slot = 0; slot < mats.Length; slot++)
                    {
                        if (!CarriesOwner(mats[slot])) continue;
                        r.GetPropertyBlock(_block, slot);
                        if (_block.GetFloat(Property) == id) continue;
                        _block.SetFloat(Property, id);
                        r.SetPropertyBlock(_block, slot);
                        changed = true;
                    }
                }
            }
            if (changed) SceneView.RepaintAll();
        }

        static bool CarriesOwner(Material m)
        {
            if (m == null) return false;
            if (!Carries.TryGetValue(m, out bool has)) Carries[m] = has = m.HasProperty(Property);
            return has;
        }
    }
}
#endif
