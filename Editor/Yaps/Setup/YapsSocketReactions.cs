// A socket's shapes on a mesh that is not the socket, the body as a rule:
// the shader cannot open those, since it measures depth from the mesh's
// origin, so a contact does. A trigger on the socket reads the plug's tip
// pointer into a local depth parameter, and a layer in the avatar's own
// controller plays the staged shapes from it. Every client computes the
// contact for itself, so it costs no sync. TPS, SPS and YAPS plugs carry
// the pointer; a DPS light-only plug does not.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsSocketReactions
    {
        const string HostName = "YAPS Depth";

        // The plug tips a socket answers to. VRCFury's variants included.
        static readonly string[] TipTypes =
        {
            "TPS_Pen_Penetrating", "TPS_Pen_Penetrating_SelfNotOnHips",
            "SPSLL_Pen_Penetrating", "SPSLL_Pen_Penetrating_SelfNotOnHips",
        };

        // When no plug on the avatar says how long a plug is, this deep is 1.
        public const float DefaultReach = 0.25f;

        public static string Parameter(YapsSocket socket) => "#YAPS/" + Sanitise(socket.name) + "/Depth";
        public static string LayerName(YapsSocket socket) => "YAPS " + socket.name + " reactions";

        // How far in counts as depth 1, in metres: the socket's own number,
        // else the longest baked plug on the avatar, else the default. The
        // contact cannot know a visiting plug's length, so this stands for
        // it, and the preview uses the same figure so what it shows is
        // what the game does.
        public static float ReachOf(YapsSocket socket)
        {
            if (socket == null) return DefaultReach;
            if (socket.depthReach > 0f) return socket.depthReach;
            var avatar = socket.GetComponentInParent<CVRAvatar>();
            var top = avatar != null ? avatar.transform : socket.transform.root;
            float longest = LongestPlugOn(top);
            return longest > 0f ? longest : DefaultReach;
        }

        // The longest baked plug under an object, hidden ones included, in
        // metres; 0 when there is none.
        public static float LongestPlugOn(Transform top)
        {
            float longest = 0f;
            if (top == null) return 0f;
            foreach (var r in top.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !m.HasProperty("_YAPS_Bake") || m.GetTexture("_YAPS_Bake") == null || !m.HasProperty("_YAPS_Length")) continue;
                    // A socket's own mesh carries a bake too, with the deform off and a power.
                    if (m.HasProperty("_YAPS_SocketPower") && m.GetFloat("_YAPS_SocketPower") > 0f
                        && m.HasProperty("_YAPS_Enabled") && m.GetFloat("_YAPS_Enabled") <= 0f) continue;
                    float length = m.GetFloat("_YAPS_Length");
                    if (m.HasProperty("_YAPS_BakeScale")) length *= Mathf.Max(m.GetFloat("_YAPS_BakeScale"), 0.01f);
                    longest = Mathf.Max(longest, length);
                }
            }
            return longest;
        }

        // The trigger's box, in the socket's frame: from the socket plane
        // to the reach behind it, the reach wide.
        public static void TriggerBox(YapsSocket socket, out Vector3 offset, out Vector3 size)
        {
            float reach = ReachOf(socket);
            float wide = Mathf.Max(reach, 0.15f);
            offset = new Vector3(0f, 0f, -reach * 0.5f);
            size = new Vector3(wide, wide, reach);
        }

        // The controllers a socket's avatar plays: the CCK's base controller,
        // which Create Animator copies into the generated one, and the
        // animator's own, so a change lands without regenerating.
        static List<AnimatorController> Controllers(YapsSocket socket)
        {
            var list = new List<AnimatorController>();
            var avatar = socket.GetComponentInParent<CVRAvatar>();
            var animator = socket.GetComponentInParent<Animator>();
            if (avatar != null && avatar.avatarSettings != null && avatar.avatarSettings.baseController is AnimatorController based) list.Add(based);
            if (animator != null && animator.runtimeAnimatorController is AnimatorController own && !list.Contains(own)) list.Add(own);
            return list;
        }

        // Has Build run for this socket: its layer is in a controller.
        public static bool Exists(YapsSocket socket)
        {
            if (socket == null) return false;
            string layerName = LayerName(socket);
            return Controllers(socket).Any(c => c.layers.Any(l => l.name == layerName));
        }

        // The strength is the layer's weight, so it can change without a
        // rebuild: the clips hold the shapes at full. Returns whether a
        // layer took it.
        public static bool SetStrength(YapsSocket socket)
        {
            if (socket == null) return false;
            string layerName = LayerName(socket);
            bool any = false;
            foreach (var controller in Controllers(socket))
            {
                var layers = controller.layers;
                for (int i = 0; i < layers.Length; i++)
                {
                    if (layers[i].name != layerName) continue;
                    layers[i].defaultWeight = Mathf.Clamp01(socket.shapePower);
                    controller.layers = layers;
                    EditorUtility.SetDirty(controller);
                    any = true;
                }
            }
            return any;
        }

        // Builds or rebuilds the reactions for one socket. Returns what
        // happened, or null when the socket has nothing to react with.
        public static string Build(YapsSocket socket)
        {
            if (socket == null) return null;
            var renderer = socket.renderer;
            var stages = socket.shapes.Where(s => s != null && !string.IsNullOrEmpty(s.blendshape)).ToList();
            if (renderer == null || stages.Count == 0 || renderer.sharedMesh == null) return null;

            var avatar = socket.GetComponentInParent<CVRAvatar>();
            var animator = socket.GetComponentInParent<Animator>();
            if (avatar == null || animator == null)
                return $"✗ {socket.name}: the shapes need a CVRAvatar and an Animator above the socket";
            var controller = avatar.avatarSettings != null && avatar.avatarSettings.baseController is AnimatorController based
                ? based
                : animator.runtimeAnimatorController as AnimatorController;
            if (controller == null)
                return $"✗ {socket.name}: the avatar has no animator controller to put the reactions in";
            string controllerPath = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrEmpty(controllerPath))
                return $"✗ {socket.name}: the animator controller is not an asset on disk";

            var known = new HashSet<string>(Enumerable.Range(0, renderer.sharedMesh.blendShapeCount)
                .Select(renderer.sharedMesh.GetBlendShapeName));
            var missing = stages.Where(s => !known.Contains(s.blendshape)).Select(s => s.blendshape).ToList();
            stages = stages.Where(s => known.Contains(s.blendshape)).ToList();
            if (stages.Count == 0)
                return $"✗ {socket.name}: none of the named shapes are on \"{renderer.name}\"";

            string parameter = Parameter(socket);
            EnsureTrigger(socket, parameter);

            // The parameter, local: contacts are computed on every client.
            if (!controller.parameters.Any(p => p.name == parameter))
            {
                controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
            }

            // The layer: one blend tree over depth, breakpoints wherever a
            // stage starts or finishes, each child a clip holding every
            // shape at its weight for that depth. Linear between them. The
            // strength is the layer's weight, so it can change later
            // without a rebuild.
            string dir = YapsNativeBuilder.OutputRoot + "/" + Sanitise(avatar.name);
            YapsNativeBuilder.EnsureFolderPublic(dir);
            string rendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, animator.transform);
            var depths = new SortedSet<float> { 0f, 1f };
            foreach (var s in stages)
            {
                depths.Add(Mathf.Clamp01(s.startsAt));
                depths.Add(Mathf.Clamp01(s.startsAt + Mathf.Max(0.01f, s.fadeOver)));
            }

            string layerName = LayerName(socket);
            var layers = controller.layers.ToList();
            int existing = layers.FindIndex(l => l.name == layerName);
            var tree = new BlendTree
            {
                name = layerName,
                blendType = BlendTreeType.Simple1D,
                blendParameter = parameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            // Each shape opens from its authored weight, so an avatar with no
            // plug in it looks as its author left it. Read past the editor's
            // test, which may be holding the mesh at a test depth.
            var floor = new Dictionary<string, float>();
            foreach (var s in stages)
            {
                int shapeIndex = renderer.sharedMesh.GetBlendShapeIndex(s.blendshape);
                floor[s.blendshape] = shapeIndex >= 0 ? YapsShapeSim.AuthoredWeight(renderer, shapeIndex) : 0f;
            }
            int index = 0;
            foreach (float depth in depths)
            {
                var clip = new AnimationClip { name = $"{layerName} {index}" };
                foreach (var s in stages)
                {
                    float opening = Mathf.Clamp01((depth - s.startsAt) / Mathf.Max(0.01f, s.fadeOver));
                    float weight = Mathf.Lerp(floor[s.blendshape], 100f, opening);
                    clip.SetCurve(rendererPath, typeof(SkinnedMeshRenderer), "blendShape." + s.blendshape,
                        AnimationCurve.Constant(0f, 1f / 60f, weight));
                }
                string clipPath = $"{dir}/{Sanitise(layerName)} {index}.anim";
                var saved = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (saved != null) { EditorUtility.CopySerialized(clip, saved); clip = saved; }
                else AssetDatabase.CreateAsset(clip, clipPath);
                tree.AddChild(clip, depth);
                index++;
            }
            var machine = new AnimatorStateMachine { name = layerName, hideFlags = HideFlags.HideInHierarchy };
            var state = machine.AddState("Depth");
            state.writeDefaultValues = true;
            state.motion = tree;
            machine.defaultState = state;
            var layer = new AnimatorControllerLayer
            {
                name = layerName, defaultWeight = Mathf.Clamp01(socket.shapePower), stateMachine = machine,
            };
            if (existing >= 0) layers[existing] = layer; else layers.Add(layer);
            controller.layers = layers.ToArray();
            AnimatorAssetSaver.EmbedLayer(layer, controller);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            string note = $"✓ {socket.name}: {stages.Count} shape(s) on \"{renderer.name}\" react to a plug's tip through a contact, " +
                          $"depth 1 at {ReachOf(socket):0.00} m in (layer \"{layerName}\", local parameter {parameter}, no sync cost)";
            if (missing.Count > 0) note += $"; not on the mesh: {string.Join(", ", missing)}";
            if (animator.runtimeAnimatorController != controller)
                note += "; the avatar's animator is not the base controller's copy, so press Create Animator on the CVRAvatar for it to pick the layer up";
            return note;
        }

        // The trigger: a box behind the socket plane, the reach deep, its
        // local Z along the socket's forward. The client reads the tip's
        // position in the box, -1..1 along Z, and Set From Position maps
        // it: 0 where the tip crosses the plane, 1 a full reach in. Left
        // alone when it is already right, so a shape edit does not churn
        // the hierarchy.
        static void EnsureTrigger(YapsSocket socket, string parameter)
        {
            TriggerBox(socket, out var offset, out var size);
            var old = socket.transform.Find(HostName);
            if (old != null)
            {
                var have = old.GetComponent<CVRAdvancedAvatarSettingsTrigger>();
                if (have != null && have.stayTasks.Count == 1 && have.stayTasks[0].settingName == parameter
                    && have.stayTasks[0].updateMethod == CVRAdvancedAvatarSettingsTriggerTaskStay.UpdateMethod.SetFromPosition
                    && have.sampleDirection == CVRAdvancedAvatarSettingsTrigger.SampleDirection.ZNegative
                    && have.allowedTypes != null && have.allowedTypes.SequenceEqual(TipTypes)
                    && have.areaSize == size && have.areaOffset == offset)
                    return;
                Undo.DestroyObjectImmediate(old.gameObject);
            }
            var host = new GameObject(HostName);
            Undo.RegisterCreatedObjectUndo(host, "YAPS socket reactions");
            host.transform.SetParent(socket.transform, false);
            var trigger = host.AddComponent<CVRAdvancedAvatarSettingsTrigger>();
            trigger.useAdvancedTrigger = true;
            trigger.isLocalInteractable = true;
            trigger.isNetworkInteractable = true;
            trigger.allowedTypes = TipTypes.ToArray();
            trigger.areaSize = size;
            trigger.areaOffset = offset;
            trigger.sampleDirection = CVRAdvancedAvatarSettingsTrigger.SampleDirection.ZNegative;
            trigger.stayTasks.Add(new CVRAdvancedAvatarSettingsTriggerTaskStay
            {
                updateMethod = CVRAdvancedAvatarSettingsTriggerTaskStay.UpdateMethod.SetFromPosition,
                settingName = parameter,
                minValue = 0f,
                maxValue = 1f,
            });
            trigger.exitTasks.Add(new CVRAdvancedAvatarSettingsTriggerTask
            {
                updateMethod = CVRAdvancedAvatarSettingsTriggerTask.UpdateMethod.Override,
                settingName = parameter,
                settingValue = 0f,
            });
        }

        static string Sanitise(string s)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
#endif
