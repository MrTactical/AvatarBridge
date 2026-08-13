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
using UnityEditor;
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
            int wired = 0;
            foreach (var plug in plugs)
            {
                int index = plugs.IndexOf(plug);
                if (BuildForPlug(ctx, plug, index, materialDriver, animatorDriver, animator, ref taskIndex))
                {
                    wired++;
                }
            }

            // Nothing was wired, so say nothing. Claiming otherwise
            // directly under the warning explaining why it could not be
            // is how a report stops being worth reading.
            if (wired == 0)
            {
                Object.DestroyImmediate(materialDriver);
                Object.DestroyImmediate(animatorDriver);
                return;
            }

            ctx.Report.Converted(Category,
                $"Wired {wired} plug(s) to the socket channel",
                $"{taskIndex} value(s): where the socket sits relative to the plug, " +
                "and how engaged it is. Contact triggers on the plug measure it on your own machine " +
                "every frame; a driver copies it into a synced parameter so other people see it too, " +
                "at ChilloutVR's ten-a-second parameter rate. What crosses the wire is the gap " +
                "between two bodies already touching, so that rate is generous for it — and the " +
                "marker lights sharpen the position further for anyone close enough to see them.");
        }

        static bool BuildForPlug(BridgeContext ctx, BridgeContext.YapsPlug plug, int index,
            CVRMaterialDriver materialDriver, CVRAnimatorDriver animatorDriver, Animator animator,
            ref int taskIndex)
        {
            float extent = Mathf.Max(plug.Length, 0.01f) * BoxLengths;
            var box = new Vector3(extent * 2f, extent * 2f, extent * 2f);

            // Sync slots are handed out in declaration order and run out
            // silently, so ask before spending. Engagement is bought first
            // because it is the on-switch: without it nothing deforms for
            // anyone, whereas without the offset the socket is still found
            // by its marker lights at contact range and by the player
            // globals beyond that.
            //
            // The offset is all three axes or none. Two axes out of three
            // is not a degraded position, it is a wrong one.
            // One float of slack on every threshold. The CCK's own inspector
            // counts the budget slightly more harshly than the client does —
            // it counted an avatar at 3200 that this measured at 3168 — and
            // being 32 bits over turns that inspector red on the user's
            // screen whatever the client thinks. Sitting a float short of
            // the line costs nothing; sitting a float past it costs the
            // avatar's credibility at upload time.
            int spare = SpareSyncFloats(ctx);
            bool carryOffset = spare >= 6;   // engagement, is-hole, and three axes
            bool carryEngagement = spare >= 3;   // engagement and is-hole

            plug.Material.SetFloat("_YAPS_ChannelSpace", carryOffset ? 1f : 0f);
            plug.Material.SetVector("_YAPS_ChannelExtents", new Vector4(extent, extent, extent, 0f));
            plug.Material.SetFloat("_YAPS_Enabled", 1f);

            if (!carryEngagement)
            {
                // Not a failure. The light path resolves position AND
                // engages on its own within about a plug length, which is
                // exactly how a plug meets content this tool never touched,
                // and this avatar's own sockets were re-ranged to be found
                // that way too.
                ctx.Report.Warning(Category,
                    "No sync budget left for the socket channel — marker lights only",
                    $"ChilloutVR gives an avatar {AasBitBudget} bits of parameter sync and this one " +
                    $"has {spare * 32} to spare, so adding even one more float would push it over and " +
                    "turn the CCK's budget bar red. The plug still deforms: it finds sockets by their " +
                    "marker lights at close range, the same way it finds DPS content this tool never " +
                    "converted. What it loses is the exact position at longer range and the certainty " +
                    "that every viewer agrees. Free some sync bits elsewhere — bools cost 1 bit where " +
                    "floats cost 32 — and convert again for the full channel.");
                return false;
            }

            if (!carryOffset)
            {
                ctx.Report.Warning(Category,
                    "Only room for engagement in the sync budget, not the socket's position",
                    $"ChilloutVR gives an avatar {AasBitBudget} bits of parameter sync and this one " +
                    $"has room for {spare} more float(s); the full channel needs four per plug. So it " +
                    "transmits whether it is engaged, and where the socket is comes from that " +
                    "socket's own marker lights at close range and from ChilloutVR's player positions " +
                    "further out. That is the same path used for content this tool never converted, " +
                    "and it works — it is simply less exact. Freeing sync bits elsewhere on the " +
                    "avatar and converting again gets you the exact one.");
            }

            var axes = carryOffset
                ? new[]
                {
                    ("X", CVRAdvancedAvatarSettingsTrigger.SampleDirection.XPositive),
                    ("Y", CVRAdvancedAvatarSettingsTrigger.SampleDirection.YPositive),
                    ("Z", CVRAdvancedAvatarSettingsTrigger.SampleDirection.ZPositive),
                }
                : new (string, CVRAdvancedAvatarSettingsTrigger.SampleDirection)[0];

            BuildEngagementTrigger(ctx, plug, index, box);

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
            }

            // ONE task per material property, not one per value. A driver
            // task writes the WHOLE float4 every frame, from its own
            // materialNN X/Y/Z/W group — so three tasks all pointing at
            // _YAPS_SocketPos do not contribute a component each, they take
            // turns overwriting one another with zeros. That is why the
            // channel did nothing at all: the position was being blanked as
            // fast as it was written.
            int flagsTask = materialDriver.tasks.Count + 1;
            materialDriver.tasks.Add(new CVRMaterialDriverTask
            {
                Renderer = plug.Renderer,
                Index = plug.MaterialSlot,
                PropertyName = "_YAPS_SocketFlags",
                PropertyType = CVRMaterialDriverTask.Type.Vector4,
            });
            int posTask = 0;
            if (carryOffset)
            {
                posTask = materialDriver.tasks.Count + 1;
                materialDriver.tasks.Add(new CVRMaterialDriverTask
                {
                    Renderer = plug.Renderer,
                    Index = plug.MaterialSlot,
                    PropertyName = "_YAPS_SocketPos",
                    PropertyType = CVRMaterialDriverTask.Type.Vector4,
                });
            }

            // Engagement first, deliberately: slots go out in declaration
            // order, so the one value the deform cannot work without is the
            // one that gets a slot when the avatar is nearly full.
            var values = new List<(string axis, string field)>
            {
                ("E", $"material{flagsTask:00}X"),
                ("H", $"material{flagsTask:00}Y"),
            };
            foreach (var (axis, _) in axes)
            {
                values.Add((axis, $"material{posTask:00}{axis}"));
            }

            foreach (var (axis, field) in values)
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
                // Read the SMOOTHED name, so a remote viewer follows the
                // value instead of stepping to it. Falls back to the raw
                // synced name if the template is missing.
                var smoothLayers = new HashSet<string>();
                string source = Smoothed(ctx, synced, smoothLayers);
                AddDriverLayer(ctx, $"YAPS{index}{axis} apply", source,
                    "", typeof(CVRMaterialDriver), field);
                taskIndex++;
            }
            return true;
        }

        // Its own object rather than sharing an axis trigger's: those only
        // exist when there is sync budget for them, and engagement has to
        // work either way. DisallowMultipleComponent settles it anyway.
        static void BuildEngagementTrigger(BridgeContext ctx, BridgeContext.YapsPlug plug, int index,
            Vector3 box)
        {
            var host = new GameObject($"YAPS Channel {index} E");
            host.transform.SetParent(plug.Root, false);

            var trigger = host.AddComponent<CVRAdvancedAvatarSettingsTrigger>();
            trigger.areaSize = box;
            trigger.areaOffset = Vector3.zero;
            trigger.useAdvancedTrigger = true;
            trigger.allowedTypes = SocketPointerTypes;
            trigger.stayTasks.Add(new CVRAdvancedAvatarSettingsTriggerTaskStay
            {
                settingName = Local(index, "E"),
                updateMethod = CVRAdvancedAvatarSettingsTriggerTaskStay.UpdateMethod.SetFromDistance,
                minValue = 0f,
                maxValue = 1f,
            });
            AddHoleTrigger(ctx, plug, index, box);
            // Nothing writes a stay task once the sender leaves, so without
            // this the plug stays bent at whatever it last saw, forever.
            trigger.exitTasks.Add(new CVRAdvancedAvatarSettingsTriggerTask
            {
                settingName = Local(index, "E"),
                settingValue = 0f,
                updateMethod = CVRAdvancedAvatarSettingsTriggerTask.UpdateMethod.Override,
            });
        }

        // A hole closes around the plug and stops it; a ring lets it pass
        // straight through. The difference is the whole character of the
        // effect, and the socket's own pointer tag already says which — so
        // a second trigger, filtered to hole tags alone, raises a flag
        // whenever a hole is what is in reach.
        //
        // Enter and exit rather than a stay task, because this is a fact
        // about the socket rather than a measurement of it: there is no
        // "how much of a hole" to sample.
        static void AddHoleTrigger(BridgeContext ctx, BridgeContext.YapsPlug plug, int index,
            Vector3 box)
        {
            var host = new GameObject($"YAPS Channel {index} H");
            host.transform.SetParent(plug.Root, false);

            var trigger = host.AddComponent<CVRAdvancedAvatarSettingsTrigger>();
            trigger.areaSize = box;
            trigger.areaOffset = Vector3.zero;
            trigger.useAdvancedTrigger = true;
            trigger.allowedTypes = HolePointerTypes;
            trigger.enterTasks.Add(new CVRAdvancedAvatarSettingsTriggerTask
            {
                settingName = Local(index, "H"),
                settingValue = 1f,
                updateMethod = CVRAdvancedAvatarSettingsTriggerTask.UpdateMethod.Override,
            });
            trigger.exitTasks.Add(new CVRAdvancedAvatarSettingsTriggerTask
            {
                settingName = Local(index, "H"),
                settingValue = 0f,
                updateMethod = CVRAdvancedAvatarSettingsTriggerTask.UpdateMethod.Override,
            });
        }

        // Only the tags that mean "hole". A plain TPS_Orf_Root says nothing
        // either way and is deliberately absent: unknown stays a ring,
        // which passes through rather than trapping a plug in something
        // that was never meant to hold it.
        static readonly string[] HolePointerTypes =
        {
            "SPSLL_Socket_Hole", "SPSLL_Socket_Hole_SelfNotOnHips",
        };

        // What a remote viewer receives is a stepped value: the wearer's
        // machine measures continuously, but ChilloutVR transmits avatar
        // parameters on a schedule, and in practice a viewer sees a couple
        // of updates a second. Joe watched a plug stutter for exactly this
        // reason.
        //
        // So every client eases the value it received toward its target
        // each frame instead of snapping to it. AvatarBridge already ships
        // the layer that does this — the constant-speed Linear Smoothing
        // template the avatar scaler uses — so it is cloned per value with
        // its Input and Output renamed.
        //
        // The smoothed name is "#" local on purpose. It is derived, not
        // transmitted: every client already has the synced value and can
        // smooth its own copy, so this costs no sync bits at all. Only the
        // wearer's own view is unaffected, since theirs was never stepped.
        static string Smoothed(BridgeContext ctx, string synced, HashSet<string> layerNames)
        {
            var template = AvatarScalerInjector.LoadController();
            if (template == null)
            {
                return synced;   // no template, no smoothing; the value still works
            }

            string output = "#" + synced + "sm";
            var copier = new AnimatorDeepCopier();
            var layers = ctx.MergedController.layers.ToList();
            foreach (var source in template.layers)
            {
                if (source.name != AvatarScalerInjector.SmoothingLayer)
                {
                    continue;
                }
                var clone = copier.CloneLayer(source);
                clone.name = $"{synced} smooth";
                clone.defaultWeight = 1f;
                AvatarScalerInjector.RenameParameterReferences(clone.stateMachine,
                    AvatarScalerInjector.TemplateParam, synced);
                AvatarScalerInjector.RenameParameterReferences(clone.stateMachine, "Output", output);

                // Every OTHER parameter the template uses is scratch — its
                // own frame timer and accumulators. Five copies of this
                // layer sharing one set of those would each stamp on the
                // others' working state, and the smoothing would come out
                // as noise. Give each copy its own.
                foreach (var parameter in template.parameters)
                {
                    if (parameter.name == AvatarScalerInjector.TemplateParam
                        || parameter.name == "Output")
                    {
                        continue;
                    }
                    string mine = "#" + synced + "_" + parameter.name.TrimStart('#');
                    AvatarScalerInjector.RenameParameterReferences(clone.stateMachine,
                        parameter.name, mine);
                    Declare(ctx, mine, parameter.type);
                }
                layers.Add(clone);
                layerNames.Add(clone.name);
            }
            if (layerNames.Count == 0)
            {
                return synced;
            }
            ctx.MergedController.layers = layers.ToArray();
            Declare(ctx, output);
            return output;
        }

        // ChilloutVR's own budget, the same rule BridgeDiagnostics reports
        // against: 32 bits a float, "#" names and triggers are free, and
        // anything declared past the cap silently never replicates.
        const int AasBitBudget = 3200;

        static int SpareSyncFloats(BridgeContext ctx)
        {
            int used = 0;
            foreach (var parameter in ctx.MergedController.parameters)
            {
                if (parameter.name.StartsWith("#")
                    || parameter.type == AnimatorControllerParameterType.Trigger)
                {
                    continue;
                }
                used += parameter.type == AnimatorControllerParameterType.Bool ? 1 : 32;
            }
            return Mathf.Max(0, (AasBitBudget - used) / 32);
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

            // Everything built here lives only in memory until it is made
            // part of the controller asset. Skip this and Unity serializes
            // the layer with a null state machine — the layers appear,
            // correctly named, driving nothing at all, and the channel is
            // silently dead with no error anywhere.
            string controllerPath = AssetDatabase.GetAssetPath(ctx.MergedController);
            if (!string.IsNullOrEmpty(controllerPath))
            {
                AssetDatabase.AddObjectToAsset(machine, ctx.MergedController);
                AssetDatabase.AddObjectToAsset(tree, ctx.MergedController);
                foreach (var child in tree.children)
                {
                    if (child.motion != null)
                    {
                        AssetDatabase.AddObjectToAsset(child.motion, ctx.MergedController);
                    }
                }
            }

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

        static void Declare(BridgeContext ctx, string name,
            AnimatorControllerParameterType type = AnimatorControllerParameterType.Float)
        {
            if (ctx.MergedController.parameters.Any(p => p.name == name))
            {
                return;
            }
            ctx.MergedController.AddParameter(name, type);
        }
    }
}
#endif
