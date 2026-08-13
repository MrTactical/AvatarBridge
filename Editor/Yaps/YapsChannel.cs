// YAPS — the discrete channel. How a plug learns where a socket is, and
// whether it is engaged at all.
//
// ---------------------------------------------------------------------
// WHY IT IS THIS SHAPE
// ---------------------------------------------------------------------
//
// Three facts about ChilloutVR decide the whole design, and all three were
// read out of the client rather than guessed.
//
// A trigger only exists on the wearer. CVRAdvancedAvatarSettingsTrigger is
// on the client's LocalComponentWhitelist and nothing else, so a remote
// copy of an avatar has none. The wearer's own machine works out where the
// socket is; everyone else has to be told.
//
// A trigger reports position in its own frame. The client computes
// inverse(rotation) * (contactPoint - position) / halfExtents, so the value
// is the pointer's offset inside the receiver's box, normalised per axis.
// There is no way to ask it for a world position. Putting the box on the
// plug therefore costs nothing and gains everything: what crosses the wire
// is the gap between two bodies that are already touching, which barely
// moves, instead of a world position that changes every time either person
// walks. One axis per trigger, because sampleDirection belongs to the
// trigger rather than the task.
//
// A contact must never drive a synced parameter. The contact system writes
// straight at the animator without filling the outbound buffer, so a synced
// parameter gets the incoming stream's value written back over the top,
// intermittently. The author of that system is explicit about it. So the
// trigger drives a "#" local, and a CVRAnimatorDriver copies it into the
// synced twin — driver writes do go through the manager.
//
// Both drivers read ANIMATED FIELDS rather than parameters, which is why
// each value costs a small blend tree: a two-motion tree on the source
// parameter, blending a field between its two ends. Twice per value, once
// to publish and once to consume, because the local copy reads the "#" and
// a remote copy has only the synced one.
#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsChannel
    {
        const string Category = "YAPS";

        // Four values per plug — three for the offset, one for engagement —
        // against sixteen material driver tasks.
        const int ValuesPerPlug = 4;
        const int MaxPlugs = 4;

        // How far out the box reaches, as a multiple of plug length. The
        // deform stops responding past 1.6 lengths, so a box any larger
        // spends its precision on distances that cannot matter.
        const float BoxLengths = 1.75f;

        public static void Run(BridgeContext ctx)
        {
            if (!ctx.Settings.convertYapsSystems || ctx.YapsPlugs.Count == 0
                || ctx.MergedController == null)
            {
                return;
            }

            var plugs = ctx.YapsPlugs.Take(MaxPlugs).ToList();
            if (ctx.YapsPlugs.Count > MaxPlugs)
            {
                ctx.Report.Warning(Category,
                    $"Only the first {MaxPlugs} plug(s) are wired to the socket channel",
                    $"This avatar has {ctx.YapsPlugs.Count}. ChilloutVR's material driver carries " +
                    "sixteen values and each plug needs four of them. The rest keep their mesh and " +
                    "their shader and simply never engage.");
            }

            var materialDriver = ctx.Target.AddComponent<CVRMaterialDriver>();
            var animatorDriver = ctx.Target.AddComponent<CVRAnimatorDriver>();
            var animator = ctx.TargetAnimator;

            int taskIndex = 0;
            foreach (var plug in plugs)
            {
                int index = plugs.IndexOf(plug);
                BuildForPlug(ctx, plug, index, materialDriver, animatorDriver, animator, ref taskIndex);
            }

            ctx.Report.Converted(Category,
                $"Wired {plugs.Count} plug(s) to the socket channel",
                $"{plugs.Count * ValuesPerPlug} value(s): where the socket sits relative to the plug, " +
                "and how engaged it is. Contact triggers on the plug measure it on your own machine " +
                "every frame; a driver copies it into a synced parameter so other people see it too, " +
                "at ChilloutVR's ten-a-second parameter rate. What crosses the wire is the gap " +
                "between two bodies already touching, so that rate is generous for it — and the " +
                "marker lights sharpen the position further for anyone close enough to see them.");
        }

        static void BuildForPlug(BridgeContext ctx, BridgeContext.YapsPlug plug, int index,
            CVRMaterialDriver materialDriver, CVRAnimatorDriver animatorDriver, Animator animator,
            ref int taskIndex)
        {
            float extent = Mathf.Max(plug.Length, 0.01f) * BoxLengths;
            var box = new Vector3(extent * 2f, extent * 2f, extent * 2f);

            // The shader turns the normalised reading back into metres, so
            // it has to be told the same box.
            plug.Material.SetFloat("_YAPS_ChannelSpace", 1f);
            plug.Material.SetVector("_YAPS_ChannelExtents", new Vector4(extent, extent, extent, 0f));
            plug.Material.SetFloat("_YAPS_Enabled", 1f);

            var axes = new[]
            {
                ("X", CVRAdvancedAvatarSettingsTrigger.SampleDirection.XPositive),
                ("Y", CVRAdvancedAvatarSettingsTrigger.SampleDirection.YPositive),
                ("Z", CVRAdvancedAvatarSettingsTrigger.SampleDirection.ZPositive),
            };

            for (int a = 0; a < axes.Length; a++)
            {
                var (axis, direction) = axes[a];
                var host = new GameObject($"YAPS Channel {index} {axis}");
                host.transform.SetParent(plug.Root, false);

                var trigger = host.AddComponent<CVRAdvancedAvatarSettingsTrigger>();
                trigger.areaSize = box;
                trigger.areaOffset = Vector3.zero;
                trigger.sampleDirection = direction;
                trigger.useAdvancedTrigger = true;
                trigger.allowedTypes = SocketPointerTypes;
                trigger.stayTasks.Add(new CVRAdvancedAvatarSettingsTriggerTaskStay
                {
                    settingName = Local(index, axis),
                    updateMethod = CVRAdvancedAvatarSettingsTriggerTaskStay.UpdateMethod.SetFromPosition,
                    minValue = 0f,
                    maxValue = 1f,
                });

                // Engagement rides the first trigger's box rather than one
                // of its own: it is the same box, and a fourth object would
                // only be a fourth thing to keep in step.
                if (a == 0)
                {
                    trigger.stayTasks.Add(new CVRAdvancedAvatarSettingsTriggerTaskStay
                    {
                        settingName = Local(index, "E"),
                        updateMethod = CVRAdvancedAvatarSettingsTriggerTaskStay.UpdateMethod.SetFromDistance,
                        minValue = 0f,
                        maxValue = 1f,
                    });
                    // Nothing writes a stay task once the sender leaves, so
                    // without this the plug stays bent at whatever it last
                    // saw, forever.
                    trigger.exitTasks.Add(new CVRAdvancedAvatarSettingsTriggerTask
                    {
                        settingName = Local(index, "E"),
                        settingValue = 0f,
                        updateMethod = CVRAdvancedAvatarSettingsTriggerTask.UpdateMethod.Override,
                    });
                }
            }

            foreach (string axis in new[] { "X", "Y", "Z", "E" })
            {
                string local = Local(index, axis);
                string synced = Synced(index, axis);
                Declare(ctx, local);
                Declare(ctx, synced);
                ctx.ContactParameters.Add(local);
                ctx.PreserveParameters.Add(synced);

                // Publish: the "#" local into the synced twin.
                int slot = animatorDriver.animators.Count;
                animatorDriver.animators.Add(animator);
                animatorDriver.animatorParameters.Add(synced);
                animatorDriver.animatorParameterType.Add(0);
                AddDriverLayer(ctx, $"YAPS{index}{axis} publish", local,
                    "", typeof(CVRAnimatorDriver), $"animatorParameter{slot + 1:00}");

                // Consume: the synced value into the material. Everyone runs
                // this, wearer and viewer alike.
                var task = new CVRMaterialDriverTask
                {
                    Renderer = plug.Renderer,
                    Index = plug.MaterialSlot,
                    PropertyName = PropertyFor(axis),
                    PropertyType = CVRMaterialDriverTask.Type.Vector4,
                };
                materialDriver.tasks.Add(task);
                AddDriverLayer(ctx, $"YAPS{index}{axis} apply", synced,
                    "", typeof(CVRMaterialDriver), FieldFor(materialDriver.tasks.Count, axis));
                taskIndex++;
            }
        }

        // The socket end of a contact, exactly as the tags read on a
        // converted avatar — the client matches these as whole strings, so
        // a near miss is a miss. Filtering on them keeps the plug from
        // reaching for every hand and fingertip in the instance.
        //
        // ChilloutVR's own existing DPS content is NOT in this list and
        // cannot be: it carries no contacts at all, only marker lights.
        // That interop rides the light path in yaps_resolve.cginc instead,
        // which is why the light path is allowed to engage on its own.
        static readonly string[] SocketPointerTypes =
        {
            "TPS_Orf_Root", "TPS_Orf_Root_SelfNotOnHips",
            "SPSLL_Socket_Root", "SPSLL_Socket_Root_SelfNotOnHips",
            "SPSLL_Socket_Hole", "SPSLL_Socket_Hole_SelfNotOnHips",
            "SPSLL_Socket_Ring", "SPSLL_Socket_Ring_SelfNotOnHips",
        };

        static string Local(int index, string axis) => $"#YAPS{index}{axis}";
        static string Synced(int index, string axis) => $"YAPS{index}{axis}";

        // Three offsets go into one float4; engagement into another, whose
        // y carries hole-versus-ring. Four floats, two properties.
        static string PropertyFor(string axis) => axis == "E" ? "_YAPS_SocketFlags" : "_YAPS_SocketPos";

        static string FieldFor(int taskNumber, string axis)
        {
            string component = axis == "E" ? "X" : axis;
            return $"material{taskNumber:00}{component}";
        }

        // A two-motion blend tree is the whole trick: the driver's field is
        // an animated float, so blending between a clip that sets it to 0
        // and one that sets it to 1 makes the field track the parameter.
        static void AddDriverLayer(BridgeContext ctx, string name, string parameter,
            string path, System.Type component, string field)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = parameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            tree.AddChild(FieldClip($"{name} 0", path, component, field, 0f), 0f);
            tree.AddChild(FieldClip($"{name} 1", path, component, field, 1f), 1f);

            var machine = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
            var state = machine.AddState("Blend Tree");
            state.writeDefaultValues = true;
            state.motion = tree;
            machine.defaultState = state;

            var layers = ctx.MergedController.layers.ToList();
            layers.Add(new AnimatorControllerLayer
            {
                name = name,
                defaultWeight = 1f,
                stateMachine = machine,
            });
            ctx.MergedController.layers = layers.ToArray();
        }

        static AnimationClip FieldClip(string name, string path, System.Type component,
            string field, float value)
        {
            var clip = new AnimationClip { name = name };
            clip.SetCurve(path, component, field, AnimationCurve.Constant(0f, 1f / 60f, value));
            return clip;
        }

        static void Declare(BridgeContext ctx, string name)
        {
            if (ctx.MergedController.parameters.Any(p => p.name == name))
            {
                return;
            }
            ctx.MergedController.AddParameter(name, AnimatorControllerParameterType.Float);
        }
    }
}
#endif
