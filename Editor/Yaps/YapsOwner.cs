// The wearer's owner id on every renderer that can hold it, so a plug can tell its
// own avatar's sockets from others' (the atlas carries the socket's copy, see
// yaps_atlas.cginc). Then which own sockets each plug may enter: a number per socket
// writer, a bit per number on the plug.
// The parameter stream gives CRC32 of the user id only on the wearer's copy (it is
// destroyed on remote ones), so the parameter syncs; a remote copy reads 0 until
// then, which the shaders treat as unknown and fall back to a geometric guess.
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
using UnityEditor.SceneManagement;
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

        // A float holds every integer below 2^24, so Mod keeps 24 bits whole.
        const float Modulus = 16777216f;

        // Idempotent: run after any plug or socket change. Removes itself when unused.
        public static string Wire(GameObject root, AnimatorController controller)
        {
            if (root == null || controller == null) return null;
            int unnumbered = ApplySelf(root);
            Relays(root);
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
            return $"owner id on {BridgeElements.Count(targets.Count, "renderer", "renderers")}, from the parameter stream, synced (32 bits)"
                   + (unnumbered > 0
                       ? $"; {BridgeElements.Count(unnumbered, "socket", "sockets")} past the first {MaxSelfSockets} carry no number, so the plugs judge them by the hips alone"
                       : "");
        }

        // Numbers the atlas carries, 1 to 15. Must match YAPS_ATLAS_SOCKETMAX.
        public const int MaxSelfSockets = 15;

        // Numbers sockets and tells each plug which it may enter. Materials only, so
        // the inspector can run it on every click. Returns how many sockets went
        // unnumbered. Plug choices name sockets, not numbers, so renumbering is safe.
        public static int ApplySelf(GameObject root)
        {
            if (root == null) return 0;
            var human = HumanBones(root.GetComponent<Animator>());
            var numbers = NumberWriters(root, true);
            int byDefault = 0;
            foreach (var kv in numbers)
                if (kv.Value > 0 && !OnHips(kv.Key.transform, human)) byDefault |= 1 << (kv.Value - 1);

            var told = new HashSet<Material>();
            foreach (var plug in root.GetComponentsInChildren<YapsPlug>(true))
            {
                // A converted plug starts from its author's Self rules. A default
                // tick leaves the tags to the shader; a chosen one skips them.
                int start = byDefault, chosen = 0;
                var own = MaterialsOf(plug);
                var rules = RulesOf(own);
                if (rules != null) start = RulesMask(rules, numbers, human, out chosen);
                foreach (var s in plug.selfEnter)
                    if (s != null && numbers.TryGetValue(s, out int n) && n > 0) chosen |= 1 << (n - 1);
                int mask = start | chosen;
                foreach (var s in plug.selfRefuse)
                    if (s != null && numbers.TryGetValue(s, out int n) && n > 0) mask &= ~(1 << (n - 1));
                chosen &= mask;
                foreach (var m in own)
                {
                    if (told.Add(m)) SetMask(m, mask, chosen);
                }
            }
            // Plugs without a component: rules kept on the material, else the default.
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || told.Contains(m)) continue;
                    var rules = RulesOn(m);
                    int chosen = 0;
                    int mask = rules != null ? RulesMask(rules, numbers, human, out chosen) : byDefault;
                    SetMask(m, mask, chosen);
                }
            }
            return Math.Max(numbers.Count - MaxSelfSockets, 0);
        }

        // In path order so numbers hold between builds. The number is also the atlas
        // bucket, (number - 1) mod 8 turned by the owner, so 9 to 15 each share with
        // the free partner farthest away. Without an avatar there is no owner to turn
        // buckets, so a name-picked offset keeps separate props off bucket one.
        public static Dictionary<YapsSocket, int> NumberWriters(GameObject root, bool avatar)
        {
            var sockets = root.GetComponentsInChildren<YapsSocket>(true)
                .OrderBy(s => YapsScanner.StablePath(s.transform, root.transform), StringComparer.Ordinal)
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
            KeyExits(root, numbers);
            return numbers;
        }

        // Here because every renumbering passes through. An orphaned exit goes to 0
        // (publishes nothing), or plugs leave by whichever socket inherited its number.
        static void KeyExits(GameObject root, Dictionary<YapsSocket, int> numbers)
        {
            var keyed = new Dictionary<Renderer, int>();
            foreach (var kv in numbers)
            {
                if (!YapsSocketBuilder.Leads(kv.Key, kv.Key.builtExit)) continue;
                var r = kv.Key.builtExit.GetComponent<MeshRenderer>();
                if (r != null) keyed[r] = kv.Value;
            }
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!YapsAtlas.IsExit(r.sharedMaterial)) continue;
                var m = YapsAtlas.Exit(keyed.TryGetValue(r, out int n) ? n : 0);
                if (m == null || m == r.sharedMaterial) continue;
                Undo.RecordObject(r, "YAPS own sockets");
                r.sharedMaterial = m;
            }
        }

        // string.GetHashCode is not promised to hold between runs.
        static uint Fnv(string text)
        {
            uint h = 2166136261;
            foreach (char c in text) h = (h ^ c) * 16777619;
            return h;
        }

        // A converted SPS plug's own-socket rules, by tag name so they survive
        // renumbering. Override tags, not properties: the shader never reads them.
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

        static SelfRules RulesOf(IEnumerable<Material> materials) =>
            materials.Select(RulesOn).FirstOrDefault(x => x != null);

        // Numbers the relay carries, 1 to 15. Must match YAPS_ATLAS_SOCKETMAX.
        const int MaxRelays = 15;

        // Numbers each plug, in hierarchy order, on its materials and on the relay
        // that hands its marker lights to its other passes. Orphan relays go.
        internal static void Relays(GameObject root)
        {
            var keep = new HashSet<SkinnedMeshRenderer>();
            int count = 0;
            foreach (var plug in root.GetComponentsInChildren<YapsPlug>(true))
            {
                var own = MaterialsOf(plug).Where(m => m.HasProperty("_YAPS_PlugIndex")).ToList();
                if (plug.Target == null || own.Count == 0) continue;
                int n = ++count <= MaxRelays ? count : 0;
                foreach (var m in own)
                {
                    if (Mathf.RoundToInt(m.GetFloat("_YAPS_PlugIndex")) == n) continue;
                    Undo.RecordObject(m, "YAPS light relays");
                    m.SetFloat("_YAPS_PlugIndex", n);
                    EditorUtility.SetDirty(m);
                }
                if (n > 0 && YapsAtlas.AddRelay(plug.Target, n) is SkinnedMeshRenderer relay) keep.Add(relay);
            }
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (YapsAtlas.IsRelay(r.sharedMaterial) && !keep.Contains(r)) Undo.DestroyObjectImmediate(r.gameObject);
        }

        // Its slot, recorded slots, and own-mesh slots wearing the same bake, plus
        // toggle variants. Not the whole renderer (unless nothing is recorded): two
        // plugs on one mesh would share one plug's mask.
        internal static List<Material> MaterialsOf(YapsPlug plug)
        {
            var found = SlotMaterialsOf(plug);
            foreach (var v in plug.variants)
                if (v != null && v.material != null && !found.Contains(v.material)) found.Add(v.material);
            return found;
        }

        static List<Material> SlotMaterialsOf(YapsPlug plug)
        {
            var found = new List<Material>();
            var target = plug.Target;
            void Take(Renderer r, int slot)
            {
                if (r == null) return;
                var mats = r.sharedMaterials;
                if (slot >= 0 && slot < mats.Length && mats[slot] != null && !found.Contains(mats[slot]))
                    found.Add(mats[slot]);
            }
            Take(target, plug.materialSlot);
            foreach (var b in plug.bakedSlots)
                if (b != null) Take(b.renderer != null ? b.renderer : target, b.slot);
            if (target == null) return found;
            if (found.Count == 0) return target.sharedMaterials.Where(m => m != null).Distinct().ToList();
            var bakes = new HashSet<Texture>(found.Where(m => m.HasProperty("_YAPS_Bake"))
                .Select(m => m.GetTexture("_YAPS_Bake")).Where(t => t != null));
            foreach (var m in target.sharedMaterials)
            {
                if (m != null && !found.Contains(m) && m.HasProperty("_YAPS_Bake")
                    && bakes.Contains(m.GetTexture("_YAPS_Bake")))
                    found.Add(m);
            }
            return found;
        }

        // As SPS reads them, Self tag rules are the whole answer, so their sockets
        // are chosen and skip the plug's tags. Hip avoidance alone chooses nothing.
        static int RulesMask(SelfRules rules, Dictionary<YapsSocket, int> numbers,
                             Dictionary<Transform, HumanBodyBones> human, out int chosen)
        {
            int mask = 0;
            foreach (var kv in numbers)
                if (kv.Value > 0 && rules.Allows(kv.Key, human)) mask |= 1 << (kv.Value - 1);
            chosen = rules.Tagged ? mask : 0;
            return mask;
        }

        // The shader's tag test, on the words.
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

        // Unset ticks: Self tag rules if kept, else yes unless on the hips or
        // refused by the plug's built tags.
        public static bool EntersByDefault(YapsPlug plug, YapsSocket socket)
        {
            var avatar = socket != null ? socket.GetComponentInParent<CVRAvatar>(true) : null;
            if (avatar == null || plug == null) return false;
            var human = HumanBones(avatar.GetComponent<Animator>());
            var rules = RulesOf(MaterialsOf(plug));
            if (rules != null && rules.Tagged) return rules.Allows(socket, human);
            bool hips = rules != null && rules.EntersHips;
            return (hips || !OnHips(socket.transform, human))
                   && TagsPass(socket.tags, Baked(plug.answers), Baked(plug.refuses));
        }

        // First humanoid bone above, climbed not measured, so a thigh socket is the
        // thigh's. No bone found counts as hips, the safe side.
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
            string note = null;
            foreach (var controller in Targets(avatar)) note = Wire(avatar.gameObject, controller) ?? note;
            return note;
        }

        // What Targets skipping the CCK's own controller costs, or null. A failure,
        // unlike Wire's note. Create Controller copies the base over the generated
        // one, so layers in the generated one alone are lost on the next press.
        public static string SharedWarning(CVRAvatar avatar)
        {
            if (avatar == null) return null;
            if (avatar.GetComponentInChildren<YapsPlug>(true) == null
                && avatar.GetComponentInChildren<YapsSocket>(true) == null) return null;
            if (CvrSetup.SharedController(Shipped(avatar)))
            {
                return "the CCK's own controller: this avatar runs it, so YAPS kept its layers out, since every " +
                       "avatar in the project shares it and a CCK update replaces it. Set a copy of it as the " +
                       "avatar's Base Controller, create the avatar's own controller from it in Advanced Avatar " +
                       "Settings, then build again";
            }
            var based = avatar.avatarSettings != null ? BridgeContext.Underlying(avatar.avatarSettings.baseController) : null;
            if (CvrSetup.SharedController(based))
            {
                return "the CCK's own Base Controller: YAPS layers went into the uploaded controller alone, and the " +
                       "next Create Controller builds a new one from the base without them. Set a copy of the " +
                       "CCK's controller as the avatar's Base Controller, then build again";
            }
            return null;
        }

        // The uploaded controller and the base the CCK rebuilds it from: in one alone,
        // a layer is lost at the next generate or does not ship until then. Never the
        // CCK's own controller; SharedWarning says what that leaves out.
        public static List<AnimatorController> Targets(CVRAvatar avatar)
        {
            var list = new List<AnimatorController>();
            var shipped = Shipped(avatar);
            if (shipped != null && !CvrSetup.SharedController(shipped)) list.Add(shipped);
            var based = avatar != null && avatar.avatarSettings != null
                ? BridgeContext.Underlying(avatar.avatarSettings.baseController) : null;
            if (based != null && !list.Contains(based) && !CvrSetup.SharedController(based)) list.Add(based);
            return list;
        }

        public static AnimatorController Shipped(CVRAvatar avatar) => AvatarSurvey.ShippedController(avatar);

        // Direct tree weighted by the parameter over a clip writing 1, so the
        // property comes out as the id. Write defaults on keeps it a plain sum.
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
            // Embedded, or they serialise as nothing.
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

        // referenceType by hand: the CCK inspector sets it only when opened, and the
        // client computes SeedOwner only for an Avatar reference.
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
            var parts = Embedded(controller, controller.layers.Where(l => l.name == LayerName).Select(l => l.stateMachine));
            // By name, so leftovers from earlier builds go too.
            string path = AssetDatabase.GetAssetPath(controller);
            if (!string.IsNullOrEmpty(path))
                parts.AddRange(AssetDatabase.LoadAllAssetsAtPath(path)
                    .Where(o => (o is AnimationClip || o is BlendTree) && o.name == LayerName));
            bool removed = false;
            for (int i = controller.layers.Length - 1; i >= 0; i--)
            {
                if (controller.layers[i].name != LayerName) continue;
                controller.RemoveLayer(i);
                removed = true;
            }
            DropUnreached(controller, parts);
            return removed;
        }

        static bool RemoveParameter(AnimatorController controller)
        {
            var p = controller.parameters.FirstOrDefault(x => x.name == Parameter);
            if (p == null) return false;
            controller.RemoveParameter(p);
            return true;
        }

        // Sub-objects the layers embedded in the controller file. Assigning
        // controller.layers destroys none of them, so read before removal and pass
        // to DropUnreached. Never clips: they may be the author's own.
        internal static List<UnityEngine.Object> Embedded(AnimatorController controller, IEnumerable<AnimatorStateMachine> machines)
        {
            string path = controller != null ? AssetDatabase.GetAssetPath(controller) : null;
            if (string.IsNullOrEmpty(path)) return new List<UnityEngine.Object>();
            var found = new HashSet<UnityEngine.Object>();
            foreach (var machine in machines) Walk(machine, found);
            return found.Where(o => o != null && !(o is AnimationClip) && AssetDatabase.GetAssetPath(o) == path).ToList();
        }

        // With undo, and never a part a remaining layer still reaches.
        internal static void DropUnreached(AnimatorController controller, List<UnityEngine.Object> parts)
        {
            if (controller == null || parts == null || parts.Count == 0) return;
            var live = new HashSet<UnityEngine.Object>(controller.animationClips);
            foreach (var layer in controller.layers) Walk(layer.stateMachine, live);
            foreach (var part in parts)
            {
                // Null once RemoveLayer has destroyed it already.
                if (part == null || live.Contains(part)) continue;
                Undo.DestroyObjectImmediate(part);
            }
            EditorUtility.SetDirty(controller);
        }

        static void Walk(AnimatorStateMachine machine, HashSet<UnityEngine.Object> into)
        {
            if (machine == null || !into.Add(machine)) return;
            foreach (var b in machine.behaviours) into.Add(b);
            foreach (var t in machine.anyStateTransitions) into.Add(t);
            foreach (var t in machine.entryTransitions) into.Add(t);
            foreach (var child in machine.states)
            {
                var state = child.state;
                if (state == null || !into.Add(state)) continue;
                foreach (var t in state.transitions) into.Add(t);
                foreach (var b in state.behaviours) into.Add(b);
                Walk(state.motion, into);
            }
            foreach (var child in machine.stateMachines)
            {
                foreach (var t in machine.GetStateMachineTransitions(child.stateMachine)) into.Add(t);
                Walk(child.stateMachine, into);
            }
        }

        static void Walk(Motion motion, HashSet<UnityEngine.Object> into)
        {
            if (motion == null || !into.Add(motion)) return;
            if (motion is BlendTree tree)
            {
                foreach (var child in tree.children) Walk(child.motion, into);
            }
        }
    }

    // The editor has no player, so owner read 0 (unknown) and plugs bent into
    // sockets the game refuses. A per-avatar stand-in id, in property blocks only.
    // Play Mode sets the parameter instead, as the owner layer animates it there.
    [InitializeOnLoad]
    static class YapsOwnerStandIn
    {
        const string Property = "_YAPS_Owner";
        static double _next;
        static MaterialPropertyBlock _block;
        // HasProperty on Poiyomi logs a drawer error per call. Cleared per closed
        // scene so it does not hold destroyed materials.
        static readonly Dictionary<Material, bool> Carries = new Dictionary<Material, bool>();
        // Reused: this runs twice a second for as long as the editor is open.
        static readonly List<Renderer> Renderers = new List<Renderer>();
        static readonly List<Material> Mats = new List<Material>();

        static YapsOwnerStandIn()
        {
            EditorApplication.update += Tick;
            EditorSceneManager.sceneClosed += _ => Carries.Clear();
        }

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
                // The renderer's block: a slot block replaces it, and the Animator
                // writes there, so per slot this hid every menu toggle.
                avatar.GetComponentsInChildren(true, Renderers);
                foreach (var r in Renderers)
                {
                    r.GetSharedMaterials(Mats);
                    if (!Mats.Any(CarriesOwner)) continue;
                    r.GetPropertyBlock(_block);
                    if (_block.GetFloat(Property) == id) continue;
                    _block.SetFloat(Property, id);
                    r.SetPropertyBlock(_block);
                    // Clear slot blocks an earlier build left; they hide the renderer's.
                    for (int slot = 0; slot < Mats.Count; slot++)
                    {
                        if (CarriesOwner(Mats[slot])) r.SetPropertyBlock(null, slot);
                    }
                    changed = true;
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
