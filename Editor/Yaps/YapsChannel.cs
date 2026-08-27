// YAPS: the discrete channel. How a plug learns where a socket is.
// A trigger exists only on the wearer's copy and reports position in
// its own frame, one axis per trigger, so the box sits on the plug.
// The trigger writes the SYNCED parameter itself: an advanced trigger's
// write goes through PlayerSetup and out over the network, so no driver
// round trip is needed, and the driver that used to do it clobbered the
// value on every remote copy. See cvr-animator-driver-clobbers-remote.
#if CVR_CCK_EXISTS
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

        // Four plugs. The ceiling is the sync budget, not the driver
        // tasks: publishing used to spend two of sixteen per plug and no
        // longer spends any, because the trigger writes the synced
        // parameter itself.
        const int MaxPlugs = 4;

        // Box reach as a multiple of plug length. The deform stops
        // responding past 1.6 lengths.
        const float BoxLengths = 1.75f;

        // The avatar the channel is being built on. A conversion fills this
        // from its context; the toolkit fills it from an avatar sitting in
        // the scene. Both then walk the same code, so a socket built by hand
        // is found the same way a converted one is.
        public class Site
        {
            public GameObject Target;
            public Animator Animator;
            public AnimatorController Controller;
            public BridgeReport Report;
            public List<BridgeContext.YapsPlug> Plugs = new List<BridgeContext.YapsPlug>();
            public float Follow = 0.05f;
            // A conversion keeps these so later passes do not rename or
            // localise what the channel just wired. Nothing else needs them.
            public ICollection<string> PreserveParameters = new List<string>();
            public ICollection<string> ContactParameters = new List<string>();
            public System.Func<Transform, string> PathIn;
        }

        public static void Run(BridgeContext ctx)
        {
            if (!ctx.Settings.convertYapsSystems) return;
            Run(new Site
            {
                Target = ctx.Target,
                Animator = ctx.TargetAnimator,
                Controller = ctx.MergedController,
                Report = ctx.Report,
                Plugs = ctx.YapsPlugs,
                Follow = ctx.Settings.yapsSocketFollow,
                PreserveParameters = ctx.PreserveParameters,
                ContactParameters = ctx.ContactParameters,
                PathIn = ctx.PathInTarget,
            });
        }

        public static void Run(Site site)
        {
            if (site == null || site.Plugs.Count == 0 || site.Controller == null)
            {
                return;
            }

            var plugs = site.Plugs.Take(MaxPlugs).ToList();
            if (site.Plugs.Count > MaxPlugs)
            {
                site.Report.Warning(Category,
                    $"Only the first {MaxPlugs} plug(s) are wired to the socket channel",
                    $"This avatar has {site.Plugs.Count}. ChilloutVR's material driver carries " +
                    "sixteen material driver tasks and each plug needs two, plus five synced floats. " +
                    "The rest keep their mesh and their shader and simply never engage.");
            }

            var materialDriver = site.Target.AddComponent<CVRMaterialDriver>();

            int taskIndex = 0;
            int wired = 0;
            foreach (var plug in plugs)
            {
                int index = plugs.IndexOf(plug);
                if (BuildForPlug(site, plug, index, materialDriver, ref taskIndex))
                {
                    wired++;
                }
            }

            // Nothing wired: remove the drivers and say nothing.
            if (wired == 0)
            {
                Object.DestroyImmediate(materialDriver);
                return;
            }

            // A layer built after the controller became an asset must embed
            // itself, or it serializes with a null state machine, silently.
            var hollow = site.Controller.layers
                .Where(l => l.name.StartsWith("YAPS")
                            && (l.stateMachine == null || l.stateMachine.states.Length == 0))
                .Select(l => l.name)
                .ToList();
            if (hollow.Count > 0)
            {
                site.Report.Warning(Category,
                    $"{hollow.Count} channel layer(s) came out empty",
                    "The penetration system's own animator layers did not survive being saved, so " +
                    "the values they carry stay at zero and contact-driven sockets will do nothing. " +
                    "Marker-light sockets are unaffected. Please report this at the AvatarBridge " +
                    $"repo, quoting: {string.Join(", ", hollow)}.");
            }

            site.Report.Converted(Category,
                $"Wired {wired} plug(s) to the socket channel",
                $"{taskIndex} value(s): where the socket sits relative to the plug, " +
                "and how engaged it is. Contact triggers on the plug measure it on your own machine " +
                "every frame, straight into a synced parameter so other people see it too, " +
                "at ChilloutVR's ten-a-second parameter rate. What crosses the wire is the gap " +
                "between two bodies already touching, so that rate is generous for it — and the " +
                "marker lights sharpen the position further for anyone close enough to see them.");
        }


        static bool BuildForPlug(Site site, BridgeContext.YapsPlug plug, int index,
            CVRMaterialDriver materialDriver,
            ref int taskIndex)
        {
            float extent = Mathf.Max(plug.Length, 0.01f) * BoxLengths;
            var box = new Vector3(extent * 2f, extent * 2f, extent * 2f);

            // Sync slots run out silently, so ask before spending. One float
            // of slack: the CCK inspector counts the budget more harshly than the client.
            int spare = SpareSyncFloats(site);
            // Orientation is bought last and dropped first: without it the
            // plug reaches the socket rather than threading it.
            bool carryOrientation = spare >= 9;   // the offset tier, and three more
            bool carryOffset = spare >= 6;   // engagement, is-hole, and three axes
            bool carryEngagement = spare >= 3;   // engagement and is-hole

            foreach (var m in PlugMaterials(plug))
            {
                m.SetFloat("_YAPS_ChannelSpace", carryOffset ? 1f : 0f);
                m.SetVector("_YAPS_ChannelExtents", new Vector4(extent, extent, extent, 0f));
                m.SetFloat("_YAPS_Enabled", 1f);
            }

            if (!carryEngagement)
            {
                // Not a failure: the light path resolves position and
                // engages on its own within about a plug length.
                site.Report.Warning(Category,
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
                site.Report.Warning(Category,
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

            BuildEngagementTrigger(site, plug, index, box);

            for (int a = 0; a < axes.Length; a++)
            {
                var (axis, direction) = axes[a];
                AddAxisTrigger(site, plug, index, box, axis, direction, SocketPointerTypes, "");

                // The same axes again on the socket's second point; the
                // difference is the socket's facing.
                if (carryOrientation)
                {
                    AddAxisTrigger(site, plug, index, box, axis, direction, FrontPointerTypes, "F");
                }
            }

            // One task per material property. A task writes the whole float4
            // from its own materialNN X/Y/Z/W group; three tasks would overwrite each other.
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
            int frontTask = 0;
            if (carryOrientation)
            {
                frontTask = materialDriver.tasks.Count + 1;
                materialDriver.tasks.Add(new CVRMaterialDriverTask
                {
                    Renderer = plug.Renderer,
                    Index = plug.MaterialSlot,
                    PropertyName = "_YAPS_SocketFront",
                    PropertyType = CVRMaterialDriverTask.Type.Vector4,
                });
            }

            // Engagement first: slots go out in declaration order.
            var values = new List<(string axis, string field)>
            {
                ("E", $"material{flagsTask:00}X"),
                ("H", $"material{flagsTask:00}Y"),
            };
            foreach (var (axis, _) in axes)
            {
                values.Add((axis, $"material{posTask:00}{axis}"));
            }
            if (carryOrientation)
            {
                foreach (var (axis, _) in axes)
                {
                    // "F" only names the parameter; the field keeps the bare
                    // axis letter, since a task's fields are X/Y/Z/W of its float4.
                    values.Add(("F" + axis, $"material{frontTask:00}{axis}"));
                }
            }

            foreach (var (axis, field) in values)
            {
                // THE TRIGGER WRITES THE SYNCED PARAMETER ITSELF.
                //
                // It used to write a "#" local and a CVRAnimatorDriver
                // copied that into a synced twin, on the belief that a
                // contact writing a synced parameter would have it
                // overwritten by the incoming stream. That is true of the
                // NATIVE contact system, which was removed in 3.7.5, and
                // not of the advanced triggers this uses. Read out of the
                // client 2026-08-26: every TriggerToContact call is
                // `ChangeAnimatorParam(name, value)` with no source, so
                // source defaults to Default, and PlayerSetup calls
                // SendAdvancedAvatarUpdate for Default — the value goes out
                // over the network from the trigger itself.
                //
                // The round trip was worse than unnecessary, it was the bug.
                // CVRAnimatorDriver has no ownership gate: it runs on every
                // client, and on a REMOTE copy of the avatar it takes the
                // `animator.SetFloat(...)` branch, reading a "#" local that
                // is 0 there because the trigger never fired on that
                // machine. So each remote client stamped 0 over the synced
                // value the moment it arrived, every frame. Perfect for the
                // wearer, permanently still for everyone else, silently.
                //
                // Writing the synced parameter directly also frees the two
                // driver tasks per plug that publishing cost, out of
                // sixteen.
                string synced = Synced(index, axis);
                Declare(site, synced);
                site.ContactParameters.Add(synced);
                site.PreserveParameters.Add(synced);

                // Consume: the synced value into the material, on every client.
                // Read the smoothed name so a remote viewer follows the value.
                var smoothLayers = new HashSet<string>();
                string source = Smoothed(site, synced, smoothLayers);
                AddDriverLayer(site, $"YAPS{index}{axis} apply", source,
                    "", typeof(CVRMaterialDriver), field);
                taskIndex++;
            }
            return true;
        }

        // Its own object: axis triggers exist only with sync budget, and
        // engagement must work either way.
        static void BuildEngagementTrigger(Site site, BridgeContext.YapsPlug plug, int index,
            Vector3 box)
        {
            var host = TriggerHost($"YAPS Channel {index} E", plug);

            var trigger = host.AddComponent<CVRAdvancedAvatarSettingsTrigger>();
            // EVERY trigger is a BOX, and areaSize is its FULL size.
            //
            // This used to halve the box "because a distance-only trigger is
            // a sphere whose radius is areaSize.x outright". Read out of the
            // client 2026-08-26 and that is not true, if it ever was:
            // TriggerToContact.ImportReceiverShape sets shapeType = Box and
            // boxSize = areaSize with no sphere case anywhere, and
            // ContactConversion takes boxSize from BoxCollider.size, which
            // is a full size. So the halving was not converting a box to a
            // sphere radius, it was simply making the engagement volume half
            // the size intended, and the same for the hole flag beside it.
            //
            // The axis triggers below were always right: they pass `box`
            // whole, which matches _YAPS_ChannelExtents at half of it.
            trigger.areaSize = box;
            trigger.areaOffset = Vector3.zero;
            trigger.useAdvancedTrigger = true;
            trigger.allowedTypes = SocketPointerTypes;
            trigger.stayTasks.Add(new CVRAdvancedAvatarSettingsTriggerTaskStay
            {
                settingName = Synced(index, "E"),
                updateMethod = CVRAdvancedAvatarSettingsTriggerTaskStay.UpdateMethod.SetFromDistance,
                minValue = 0f,
                maxValue = 1f,
            });
            AddHoleTrigger(site, plug, index, box);
            // Nothing writes a stay task after the sender leaves; reset on exit.
            trigger.exitTasks.Add(new CVRAdvancedAvatarSettingsTriggerTask
            {
                settingName = Synced(index, "E"),
                settingValue = 0f,
                updateMethod = CVRAdvancedAvatarSettingsTriggerTask.UpdateMethod.Override,
            });
        }

        // A hole closes around the plug; a ring lets it pass. The socket's
        // pointer tag says which, so a second trigger raises a flag for holes.
        static void AddHoleTrigger(Site site, BridgeContext.YapsPlug plug, int index,
            Vector3 box)
        {
            var host = TriggerHost($"YAPS Channel {index} H", plug);

            var trigger = host.AddComponent<CVRAdvancedAvatarSettingsTrigger>();
            // A box like every other, full size. See the engagement trigger
            // above for why this is no longer halved.
            trigger.areaSize = box;
            trigger.areaOffset = Vector3.zero;
            trigger.useAdvancedTrigger = true;
            trigger.allowedTypes = HolePointerTypes;
            trigger.enterTasks.Add(new CVRAdvancedAvatarSettingsTriggerTask
            {
                settingName = Synced(index, "H"),
                settingValue = 1f,
                updateMethod = CVRAdvancedAvatarSettingsTriggerTask.UpdateMethod.Override,
            });
            trigger.exitTasks.Add(new CVRAdvancedAvatarSettingsTriggerTask
            {
                settingName = Synced(index, "H"),
                settingValue = 0f,
                updateMethod = CVRAdvancedAvatarSettingsTriggerTask.UpdateMethod.Override,
            });
        }

        // Only the tags that mean "hole". A plain TPS_Orf_Root says nothing
        // either way; unknown stays a ring.
        static readonly string[] HolePointerTypes =
        {
            "SPSLL_Socket_Hole", "SPSLL_Socket_Hole_SelfNotOnHips",
        };

        // Synced parameters arrive stepped, so every client eases toward the
        // received value. The "#" smoothed name is derived and costs no sync bits.
        static string Smoothed(Site site, string synced, HashSet<string> layerNames)
        {
            var template = AvatarScalerInjector.LoadController();
            if (template == null)
            {
                return synced;   // no template, no smoothing; the value still works
            }

            string output = "#" + synced + "sm";
            var copier = new AnimatorDeepCopier();
            var layers = site.Controller.layers.ToList();
            // Every parameter this layer writes, old name to new. Clip
            // bindings are not parameter references; renaming references misses them.
            var renames = new Dictionary<string, string>();
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
                renames[AvatarScalerInjector.TemplateParam] = synced;
                renames["Output"] = output;

                // Every other template parameter is scratch. Each copy of the
                // layer needs its own, or the copies stamp on each other's state.
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
                    // Carry the template's defaults; declared bare they come
                    // out 0. StepSize is the socket follow setting.
                    float value = parameter.name.EndsWith("StepSize", System.StringComparison.Ordinal)
                        ? site.Follow
                        : parameter.defaultFloat;
                    Declare(site, mine, parameter.type, value);
                    renames[parameter.name] = mine;
                }
                // The clips bind these parameters as Animator properties;
                // renaming references leaves those bindings on the template names.
                RebindMachine(clone.stateMachine, renames, site,
                    new Dictionary<AnimationClip, AnimationClip>());

                // The controller is already an asset here, so the clone must
                // embed itself or it serializes with a null state machine.
                AnimatorAssetSaver.EmbedLayer(clone, site.Controller);

                layers.Add(clone);
                layerNames.Add(clone.name);
            }
            if (layerNames.Count == 0)
            {
                return synced;
            }
            site.Controller.layers = layers.ToArray();
            Declare(site, output);
            return output;
        }

        // ChilloutVR's budget, as BridgeDiagnostics reports it: 32 bits a
        // float, "#" names and triggers free, past the cap never replicates.
        const int AasBitBudget = 3200;

        static int SpareSyncFloats(Site site)
        {
            int used = 0;
            foreach (var parameter in site.Controller.parameters)
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

        // The socket end of a contact. The client matches these as whole
        // strings. DPS content carries no contacts and rides the light path.
        static readonly string[] SocketPointerTypes =
        {
            "TPS_Orf_Root", "TPS_Orf_Root_SelfNotOnHips",
            "SPSLL_Socket_Root", "SPSLL_Socket_Root_SelfNotOnHips",
            "SPSLL_Socket_Hole", "SPSLL_Socket_Hole_SelfNotOnHips",
            "SPSLL_Socket_Ring", "SPSLL_Socket_Ring_SelfNotOnHips",
        };

        // The socket's second point, which says which way it faces. TPS calls
        // it TPS_Orf_Norm, SPS calls it a Front; a converted avatar carries both.
        static readonly string[] FrontPointerTypes =
        {
            "TPS_Orf_Norm", "TPS_Orf_Norm_SelfNotOnHips",
            "SPSLL_Socket_Front", "SPSLL_Socket_Front_SelfNotOnHips",
        };

        static string Synced(int index, string axis) => $"YAPS{index}{axis}";


        // A two-motion blend tree: the driver's field is an animated float,
        // so blending 0 and 1 clips makes the field track the parameter.
        static void AddDriverLayer(Site site, string name, string parameter,
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

            var layer = new AnimatorControllerLayer
            {
                name = name,
                defaultWeight = 1f,
                stateMachine = machine,
            };
            // Built in memory on a saved controller. The walk embeds the
            // machine, the state, the tree and the clips; a hand-written
            // list once missed the state and the layer played nothing.
            AnimatorAssetSaver.EmbedLayer(layer, site.Controller);

            var layers = site.Controller.layers.ToList();
            layers.Add(layer);
            site.Controller.layers = layers.ToArray();
        }

        static AnimationClip FieldClip(string name, string path, System.Type component,
            string field, float value)
        {
            var clip = new AnimationClip { name = name };
            clip.SetCurve(path, component, field, AnimationCurve.Constant(0f, 1f / 60f, value));
            return clip;
        }

        // One box for one axis, one family of pointers. Called twice per
        // axis: the socket's root, then its second point.
        static void AddAxisTrigger(Site site, BridgeContext.YapsPlug plug, int index,
            Vector3 box, string axis, CVRAdvancedAvatarSettingsTrigger.SampleDirection direction,
            string[] types, string prefix)
        {
            var host = TriggerHost($"YAPS Channel {index} {prefix}{axis}", plug);
            var trigger = host.AddComponent<CVRAdvancedAvatarSettingsTrigger>();
            trigger.areaSize = box;
            trigger.areaOffset = Vector3.zero;
            trigger.sampleDirection = direction;
            trigger.useAdvancedTrigger = true;
            trigger.allowedTypes = types;
            trigger.stayTasks.Add(new CVRAdvancedAvatarSettingsTriggerTaskStay
            {
                settingName = Synced(index, prefix + axis),
                updateMethod = CVRAdvancedAvatarSettingsTriggerTaskStay.UpdateMethod.SetFromPosition,
                minValue = 0f,
                maxValue = 1f,
            });
            // Back to the middle of the box on the way out, which is the
            // plug's own origin and means nothing in particular.
            //
            // A stay task with no exit keeps its last reading forever, and
            // the last reading before a socket leaves is taken at the edge:
            // measured in game as 0.99 and 1.00 on the axis it left by. That
            // is harmless while engagement is 0, but engagement rises the
            // instant the NEXT socket arrives, and for the tick before its
            // own stay task reports, the plug snaps toward wherever the last
            // one left. Half is the only neutral value the axis has.
            //
            // This does nothing for a sender that VANISHES inside the box: no
            // exit runs at all then, and the axes and engagement both keep
            // their last values with nothing left to clear them.
            trigger.exitTasks.Add(new CVRAdvancedAvatarSettingsTriggerTask
            {
                settingName = Synced(index, prefix + axis),
                settingValue = 0.5f,
                updateMethod = CVRAdvancedAvatarSettingsTriggerTask.UpdateMethod.Override,
            });
        }

        // A trigger measures from its own transform, and the shader
        // reconstructs the socket against the measured mesh frame. Same frame.
        // EVERY baked material on the plug's mesh, not just the primary.
        //
        // A plug's vertices can span several materials — a whole avatar
        // baked as one plug wears three — and the channel configured only
        // plug.Material. The others kept channel space off and no frame at
        // all, so on one mesh some materials decoded the socket through the
        // channel and the rest fell back to the per-vertex frame: the head
        // bending ninety degrees while the body sat still. The bake learned
        // to mirror across slots on 2026-08-25; the channel never did.
        static IEnumerable<Material> PlugMaterials(BridgeContext.YapsPlug plug)
        {
            if (plug.Material != null) yield return plug.Material;
            if (plug.Renderer == null) yield break;
            foreach (var m in plug.Renderer.sharedMaterials)
            {
                if (m != null && m != plug.Material && m.HasProperty("_YAPS_Bake")) yield return m;
            }
        }

        static GameObject TriggerHost(string name, BridgeContext.YapsPlug plug)
        {
            var host = new GameObject(name);
            host.transform.SetParent(plug.Root, false);

            // An all-zero quaternion means no frame was recorded. Sit on the
            // object rather than at world zero.
            if (plug.Rotation.x != 0f || plug.Rotation.y != 0f
                || plug.Rotation.z != 0f || plug.Rotation.w != 0f)
            {
                host.transform.SetPositionAndRotation(plug.Origin, plug.Rotation);
            }
            return host;
        }

        // Point every clip in a cloned layer at the renamed parameters.
        static void RebindMachine(AnimatorStateMachine machine, Dictionary<string, string> renames,
            Site site, Dictionary<AnimationClip, AnimationClip> cache)
        {
            if (machine == null)
            {
                return;
            }
            foreach (var child in machine.states)
            {
                child.state.motion = RebindMotion(child.state.motion, renames, site, cache);
            }
            foreach (var child in machine.stateMachines)
            {
                RebindMachine(child.stateMachine, renames, site, cache);
            }
        }

        static Motion RebindMotion(Motion motion, Dictionary<string, string> renames,
            Site site, Dictionary<AnimationClip, AnimationClip> cache)
        {
            if (motion is AnimationClip clip)
            {
                if (!cache.TryGetValue(clip, out var bound))
                {
                    bound = RebindClip(clip, renames, site);
                    cache[clip] = bound;
                }
                return bound;
            }
            if (motion is BlendTree tree)
            {
                // Assigning children back is required; the getter hands out
                // a copy of the array rather than the array itself.
                var children = tree.children;
                for (int i = 0; i < children.Length; i++)
                {
                    children[i].motion = RebindMotion(children[i].motion, renames, site, cache);
                }
                tree.children = children;
            }
            return motion;
        }

        // A copy, never the original: these clips live in the scaler package
        // and are shared by every converted avatar.
        static AnimationClip RebindClip(AnimationClip source, Dictionary<string, string> renames,
            Site site)
        {
            var clip = new AnimationClip { name = source.name, frameRate = source.frameRate };
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                var bound = binding;
                if (binding.type == typeof(Animator)
                    && renames.TryGetValue(binding.propertyName, out string renamed))
                {
                    bound.propertyName = renamed;
                }
                AnimationUtility.SetEditorCurve(clip, bound, curve);
            }
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(site.Controller)))
            {
                clip.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(clip, site.Controller);
            }
            return clip;
        }

        // Defaults matter for template clones: "One" ships at 1, "StepSize"
        // at 0.05, and bare declarations arrive as 0. AddParameter cannot set one.
        static void Declare(Site site, string name,
            AnimatorControllerParameterType type = AnimatorControllerParameterType.Float,
            float defaultFloat = 0f)
        {
            if (site.Controller.parameters.Any(p => p.name == name))
            {
                return;
            }
            var parameters = site.Controller.parameters.ToList();
            parameters.Add(new AnimatorControllerParameter
            {
                name = name,
                type = type,
                defaultFloat = defaultFloat,
            });
            site.Controller.parameters = parameters.ToArray();
        }
    }
}
#endif
