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

        // How far out the trigger reaches, in metres: the depth reads 1 at
        // the socket and 0 at this distance from it.
        const float Reach = 0.25f;

        public static string Parameter(YapsSocket socket) => "#YAPS/" + Sanitise(socket.name) + "/Depth";

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
            Undo.RegisterFullObjectHierarchyUndo(socket.gameObject, "YAPS socket reactions");

            // The trigger: a box on the socket, its forward as depth. Distance
            // to the centre, 1 there and 0 at the reach.
            var old = socket.transform.Find(HostName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var host = new GameObject(HostName);
            host.transform.SetParent(socket.transform, false);
            var trigger = host.AddComponent<CVRAdvancedAvatarSettingsTrigger>();
            trigger.useAdvancedTrigger = true;
            trigger.isLocalInteractable = true;
            trigger.isNetworkInteractable = true;
            trigger.allowedTypes = TipTypes.ToArray();
            trigger.areaSize = new Vector3(Reach * 2f, Reach * 2f, Reach * 2f);
            trigger.areaOffset = Vector3.zero;
            trigger.stayTasks.Add(new CVRAdvancedAvatarSettingsTriggerTaskStay
            {
                updateMethod = CVRAdvancedAvatarSettingsTriggerTaskStay.UpdateMethod.SetFromDistance,
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

            // The parameter, local: contacts are computed on every client.
            if (!controller.parameters.Any(p => p.name == parameter))
            {
                controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
            }

            // The layer: one blend tree over depth, breakpoints wherever a
            // stage starts or finishes, each child a clip holding every
            // shape at its weight for that depth. Linear between them.
            string dir = YapsNativeBuilder.OutputRoot + "/" + Sanitise(avatar.name);
            YapsNativeBuilder.EnsureFolderPublic(dir);
            string rendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, animator.transform);
            var depths = new SortedSet<float> { 0f, 1f };
            foreach (var s in stages)
            {
                depths.Add(Mathf.Clamp01(s.startsAt));
                depths.Add(Mathf.Clamp01(s.startsAt + Mathf.Max(0.01f, s.fadeOver)));
            }

            string layerName = "YAPS " + socket.name + " reactions";
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
            int index = 0;
            foreach (float depth in depths)
            {
                var clip = new AnimationClip { name = $"{layerName} {index}" };
                foreach (var s in stages)
                {
                    float weight = Mathf.Clamp01((depth - s.startsAt) / Mathf.Max(0.01f, s.fadeOver)) * socket.shapePower * 100f;
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
            var layer = new AnimatorControllerLayer { name = layerName, defaultWeight = 1f, stateMachine = machine };
            if (existing >= 0) layers[existing] = layer; else layers.Add(layer);
            controller.layers = layers.ToArray();
            AnimatorAssetSaver.EmbedLayer(layer, controller);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            string note = $"✓ {socket.name}: {stages.Count} shape(s) on \"{renderer.name}\" react to a plug's tip through a contact " +
                          $"(layer \"{layerName}\", local parameter {parameter}, no sync cost)";
            if (missing.Count > 0) note += $"; not on the mesh: {string.Join(", ", missing)}";
            return note;
        }

        static string Sanitise(string s)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
#endif
