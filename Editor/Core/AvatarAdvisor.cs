#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge
{
    // What a piece of advice is asking of the reader.
    public enum AdviceKind
    {
        Change,
        Confirm,
        Inert,
        Manual,
        Blocked,
    }

    // One finding about one setting.
    public sealed class Advice
    {
        public AdviceKind Kind;
        public string Setting;
        public string Finding;
        public Action<BridgeSettings> Apply;

        public UnityEngine.Object[] Targets;
    }

    // Says which settings the avatar's own contents decide; judgement calls stay the user's.
    // Every check goes through the converter's own code (DetectShapes, DeclaresStereo,
    // AvatarUsesGogo, VrcConstraintTypeNames): a separate guess would recommend a box and then
    // convert as if it were unticked. Reads the scene, pre-bake: on VRCFury and Modular Avatar
    // avatars every count is a floor, and the advice says so.
    public static class AvatarAdvisor
    {
        public static List<Advice> Analyse(VRCAvatarDescriptor descriptor, BridgeSettings settings)
        {
            var advice = new List<Advice>();
            if (descriptor == null || settings == null)
            {
                return advice;
            }

            var root = descriptor.gameObject;
            bool baked = VRCFuryBaker.HasFuryComponents(root)
                         || ModularAvatarBaker.HasModularAvatarComponents(root);

            if (baked)
            {
                advice.Add(new Advice
                {
                    Kind = AdviceKind.Manual,
                    Setting = "This avatar is built by a baker",
                    Finding =
                        "VRCFury or Modular Avatar will add to this avatar when it converts: clothing, " +
                        "toggles, physics, contacts and parameters that are not on it yet. Everything " +
                        "counted below is therefore a floor, not a total, and a count of zero means " +
                        "\"none before baking\" rather than \"none\". The conversion report is measured " +
                        "after the bake and is the honest number.",
                });
            }

            Safe(advice, "Physics", () => Physics(advice, root, settings, baked));
            Safe(advice, "Face tracking", () => FaceTracking(advice, root, descriptor, settings));
            Safe(advice, "Remove VRChat-only systems", () => Gogo(advice, descriptor, settings));
            Safe(advice, "Animator layers to convert", () => Layers(advice, descriptor, settings, baked));
            Safe(advice, "Patch non-SPI shaders for VR", () => Shaders(advice, root, settings));
            Safe(advice, "Components", () => Components(advice, root, baked));

            return advice;
        }

        static void Safe(List<Advice> advice, string what, Action check)
        {
            try
            {
                check();
            }
            catch (Exception e)
            {
                advice.Add(new Advice
                {
                    Kind = AdviceKind.Blocked,
                    Setting = what,
                    Finding = $"This check could not run on this avatar ({e.GetType().Name}: {e.Message}). " +
                              "Nothing was changed. Convert and read the report, which measures the same " +
                              "things on the baked avatar.",
                });
            }
        }

        // ------------------------------------------------------------------ physics ----

        static void Physics(List<Advice> advice, GameObject root, BridgeSettings settings, bool baked)
        {
            var physBones = root.GetComponentsInChildren<VRCPhysBone>(true);

            if (physBones.Length == 0)
            {
                // Never "None" on a baker's avatar: hair and clothing chains usually arrive in the bake.
                advice.Add(new Advice
                {
                    Kind = baked ? AdviceKind.Manual : AdviceKind.Change,
                    Setting = "Convert PhysBones to",
                    Finding = baked
                        ? "No PhysBones on the avatar yet, but its baker usually brings them with the " +
                          "hair and clothing it adds. Left as it is: set this to \"None\" only if you " +
                          "know nothing it adds has physics."
                        : "No PhysBones on this avatar, and nothing here bakes any in. There is nothing " +
                          "for the physics target to convert.",
                    Apply = baked ? (Action<BridgeSettings>)null : s => s.physicsTarget = PhysicsTarget.None,
                });
                return;
            }

            string count = $"{physBones.Length} PhysBone{(physBones.Length == 1 ? "" : "s")}";

            if (settings.physicsTarget == PhysicsTarget.None)
            {
                var target = BridgeDefines.HasMagicaCloth2 ? PhysicsTarget.MagicaCloth2
                    : BridgeDefines.HasDynamicBone ? PhysicsTarget.DynamicBone
                    : PhysicsTarget.None;
                advice.Add(new Advice
                {
                    Kind = target == PhysicsTarget.None ? AdviceKind.Blocked : AdviceKind.Change,
                    Setting = "Convert PhysBones to",
                    Finding = target == PhysicsTarget.None
                        ? $"{count} on this avatar, and neither MagicaCloth2 nor DynamicBone is installed, " +
                          "so there is nothing to convert them into. Import MagicaCloth2 for the best " +
                          "result in ChilloutVR."
                        : $"{count} on this avatar, and the physics target is \"None\": its hair, tail and " +
                          $"clothing would come across rigid. {Nice(target)} is installed and can take them.",
                    Apply = target == PhysicsTarget.None ? (Action<BridgeSettings>)null
                        : s => s.physicsTarget = target,
                });
            }
            else if (settings.physicsTarget == PhysicsTarget.MagicaCloth2 && !BridgeDefines.HasMagicaCloth2)
            {
                advice.Add(new Advice
                {
                    Kind = AdviceKind.Blocked,
                    Setting = "Convert PhysBones to",
                    Finding = $"MagicaCloth2 is not installed, so {count} cannot be converted. Import it, " +
                              "or switch to DynamicBone" + (BridgeDefines.HasDynamicBone
                                  ? ", which is installed." : ", which is not installed either."),
                    Apply = BridgeDefines.HasDynamicBone
                        ? s => s.physicsTarget = PhysicsTarget.DynamicBone : (Action<BridgeSettings>)null,
                });
            }
            else if (settings.physicsTarget == PhysicsTarget.DynamicBone && !BridgeDefines.HasDynamicBone)
            {
                advice.Add(new Advice
                {
                    Kind = AdviceKind.Blocked,
                    Setting = "Convert PhysBones to",
                    Finding = $"DynamicBone is not installed, so {count} cannot be converted. The free " +
                              "VRLabs Dynamic-Bones-Stub works for conversion" + (BridgeDefines.HasMagicaCloth2
                                  ? ", or switch to MagicaCloth2, which is installed and gives the better result."
                                  : "."),
                    Apply = BridgeDefines.HasMagicaCloth2
                        ? s => s.physicsTarget = PhysicsTarget.MagicaCloth2 : (Action<BridgeSettings>)null,
                });
            }
            else
            {
                advice.Add(new Advice
                {
                    Kind = AdviceKind.Confirm,
                    Setting = "Convert PhysBones to",
                    Finding = $"{count} will be converted to {Nice(settings.physicsTarget)}.",
                });
            }

            Toes(advice, root, physBones, settings);
        }

        static void Toes(List<Advice> advice, GameObject root, VRCPhysBone[] physBones, BridgeSettings settings)
        {
            if (settings.physicsTarget == PhysicsTarget.None || settings.convertToePhysBones)
            {
                return;
            }

            var animator = root.GetComponent<Animator>();
            var chains = new List<UnityEngine.Object>();
            foreach (var pb in physBones)
            {
                if (pb == null)
                {
                    continue;
                }
                if (PhysBoneChainData.Read(pb, animator, true).ToeExclusions.Count > 0)
                {
                    chains.Add(pb.gameObject);
                }
            }
            if (chains.Count == 0)
            {
                return;
            }

            advice.Add(new Advice
            {
                Kind = AdviceKind.Manual,
                Setting = "Convert toe PhysBones",
                Finding = $"{chains.Count} chain{(chains.Count == 1 ? " runs" : "s run")} through toe bones. " +
                          "They are excluded by default: simulated toes splay and swing while the foot " +
                          "itself is planted by IK, which reads as broken feet rather than as physics. " +
                          "Your call: turn it on if this avatar's toe physics are deliberate.",
                Apply = s => s.convertToePhysBones = true,
                Targets = chains.ToArray(),
            });
        }

        // ------------------------------------------------------------ face tracking ----

        static void FaceTracking(List<Advice> advice, GameObject root, VRCAvatarDescriptor descriptor,
            BridgeSettings settings)
        {
            // No CVRAvatar yet, so no descriptor-named mesh; the scoring pass is the same.
            bool hasShapes = FaceTrackingConverter.DetectShapes(root, null,
                out var mesh, out bool unified, out int score);

            if (hasShapes)
            {
                string where = $"{score} face-tracking blendshapes on \"{mesh.name}\" " +
                               $"({(unified ? "Unified Expressions" : "Legacy / SRanipal")} naming)";
                int rig = OwnRigParameters(root, descriptor, out int bits);
                // Binary-bit rigs cannot run here: CVR lists the bits to the app as floats, so the
                // app never sends them, and as floats they cost 32 bits each.
                if (bits > 0 && settings.faceTrackingMode == FaceTrackingMode.None)
                {
                    advice.Add(new Advice
                    {
                        Kind = AdviceKind.Change,
                        Setting = "Face tracking",
                        Finding = $"{where}. The avatar's own rig syncs them through {bits} binary bits, which do not " +
                                  "work in ChilloutVR: the tracking app sends nothing to them here, and they would cost " +
                                  $"{bits * 32} of the 3200 synced bits. ChilloutVR's native component drives the same " +
                                  "shapes and syncs them for nothing.",
                        Apply = s => s.faceTrackingMode = FaceTrackingMode.Native,
                        Targets = new UnityEngine.Object[] { mesh.gameObject },
                    });
                    return;
                }
                if (rig > 0 && settings.faceTrackingMode == FaceTrackingMode.None)
                {
                    advice.Add(new Advice
                    {
                        Kind = AdviceKind.Confirm,
                        Setting = "Face tracking",
                        Finding = $"{where}, driven by the avatar's own rig ({rig} face-tracking parameters). " +
                                  "Kept, it converts with the rest of the animator, smoothing included.",
                        Targets = new UnityEngine.Object[] { mesh.gameObject },
                    });
                    return;
                }
                if (settings.faceTrackingMode == FaceTrackingMode.Native)
                {
                    advice.Add(new Advice
                    {
                        Kind = AdviceKind.Confirm,
                        Setting = "Face tracking",
                        Finding = $"{where}. ChilloutVR's own component reads them directly: no OSC and " +
                                  "no per-shape animator layers needed.",
                        Targets = new UnityEngine.Object[] { mesh.gameObject },
                    });
                }
                else
                {
                    advice.Add(new Advice
                    {
                        Kind = AdviceKind.Change,
                        Setting = "Face tracking",
                        Finding = $"{where}. ChilloutVR reads those directly through its native component, " +
                                  "which is the cheapest and most reliable route: it needs no extra " +
                                  "package and spends no animator layers.",
                        Apply = s => s.faceTrackingMode = FaceTrackingMode.Native,
                        Targets = new UnityEngine.Object[] { mesh.gameObject },
                    });
                }
                // Native is better, but either mode replaces a paid-for rig, so not in Apply all.
                if (rig > 0 && bits == 0)
                {
                    string mode = settings.faceTrackingMode == FaceTrackingMode.Native
                        ? "Native CVR Component" : "Unity Animator Blendtrees (DSR)";
                    advice.Add(new Advice
                    {
                        Kind = AdviceKind.Manual,
                        Setting = "Keep the avatar's own rig",
                        Finding = $"The avatar also carries its own face-tracking rig: {rig} parameters, the " +
                                  $"animator layers that drive and smooth those shapes. \"{mode}\" replaces it. " +
                                  "Your call: turn this on to keep the rig, and it converts with the rest of the animator.",
                        Apply = s => s.faceTrackingMode = FaceTrackingMode.None,
                    });
                }
                return;
            }

            string elsewhere = FaceTrackingConverter.WhereTheShapesAre(root, out var copy);
            string hint = elsewhere != null ? " " + elsewhere : "";
            var copyTarget = copy != null ? new UnityEngine.Object[] { copy } : null;

            var names = ParameterNames(descriptor);
            // The stripper's rule, so the advice counts exactly what the mode would replace.
            int ftParams = names.Count(FaceTrackingParameters.IsFaceTracking);

            if (ftParams > 0)
            {
                bool installed = FaceTrackingPackages.IsInstalled();
                advice.Add(new Advice
                {
                    Kind = installed
                        ? (settings.faceTrackingMode == FaceTrackingMode.DragonSkyRunner
                            ? AdviceKind.Confirm : AdviceKind.Change)
                        : AdviceKind.Blocked,
                    Setting = "Face tracking",
                    Finding = $"{ftParams} face-tracking parameters, but no mesh carrying enough " +
                              "recognisable blendshapes for ChilloutVR's native component to drive. " +
                              (installed
                                  ? "\"Unity Animator Blendtrees (DSR)\" replaces the rig with a parameter-driven one that works here."
                                  : $"\"Unity Animator Blendtrees (DSR)\" would be the route, but \"{FaceTrackingPackages.DisplayName}\" " +
                                    "is missing: reimport AvatarBridge to get it back.") + hint,
                    Targets = copyTarget,
                    Apply = installed && settings.faceTrackingMode != FaceTrackingMode.DragonSkyRunner
                        ? s => s.faceTrackingMode = FaceTrackingMode.DragonSkyRunner
                        : (Action<BridgeSettings>)null,
                });
                return;
            }

            advice.Add(new Advice
            {
                Kind = settings.faceTrackingMode == FaceTrackingMode.None
                    ? AdviceKind.Confirm : AdviceKind.Change,
                Setting = "Face tracking",
                Finding = "No face-tracking blendshapes and no face-tracking parameters on this avatar. " +
                          "There is nothing for either mode to set up, and both of them strip an existing " +
                          "rig before they start." + hint,
                Targets = copyTarget,
                Apply = settings.faceTrackingMode == FaceTrackingMode.None
                    ? (Action<BridgeSettings>)null : s => s.faceTrackingMode = FaceTrackingMode.None,
            });
        }

        // ------------------------------------------------------------------- systems ----

        static void Gogo(List<Advice> advice, VRCAvatarDescriptor descriptor, BridgeSettings settings)
        {
            if (!SystemStripper.AvatarUsesGogo(descriptor))
            {
                return;
            }

            advice.Add(settings.stripGogoLoco
                ? new Advice
                {
                    Kind = AdviceKind.Confirm,
                    Setting = "Remove GoGo Loco (recommended)",
                    Finding = "GoGo Loco is on this avatar and will be removed. ChilloutVR ships its own " +
                              "locomotion, emotes, AFK and flight, and GoGo's layers fight them while " +
                              "spending around fifteen synced parameters." +
                              (settings.gogoEmotes ? " Its sit and lie poses go into ChilloutVR's emote wheel." : ""),
                }
                : new Advice
                {
                    Kind = AdviceKind.Manual,
                    Setting = "Remove GoGo Loco (recommended)",
                    Finding = "GoGo Loco is on this avatar and you have chosen to keep it. That is " +
                              "experimental: GoGo replaces ChilloutVR's locomotion layer outright, so " +
                              "Base, Additive and Action must all be ticked or the avatar has no " +
                              "locomotion at all. Poses will not lock movement and the viewpoint stays " +
                              "at standing height in floor poses: neither has an equivalent here.",
                    // A Manual, so Apply all leaves it alone.
                    Apply = s => s.stripGogoLoco = true,
                });
        }

        // Layer settings decided by the slot being EMPTY. They persist across
        // avatars, and a stale tick reads as "this avatar has an Action layer".
        internal static readonly (VRCAvatarDescriptor.AnimLayerType Type, string Setting)[] OptionalLayers =
        {
            (VRCAvatarDescriptor.AnimLayerType.Base, "Base / locomotion"),
            (VRCAvatarDescriptor.AnimLayerType.Additive, "Additive"),
            (VRCAvatarDescriptor.AnimLayerType.Action, "Action (emotes, AFK)"),
        };

        internal static bool IsOn(BridgeSettings settings, VRCAvatarDescriptor.AnimLayerType type)
        {
            switch (type)
            {
                case VRCAvatarDescriptor.AnimLayerType.Base: return settings.convertBaseLayer;
                case VRCAvatarDescriptor.AnimLayerType.Additive: return settings.convertAdditiveLayer;
                case VRCAvatarDescriptor.AnimLayerType.Action: return settings.convertActionLayer;
                default: return false;
            }
        }

        internal static void SetOn(BridgeSettings settings, VRCAvatarDescriptor.AnimLayerType type, bool on)
        {
            switch (type)
            {
                case VRCAvatarDescriptor.AnimLayerType.Base: settings.convertBaseLayer = on; break;
                case VRCAvatarDescriptor.AnimLayerType.Additive: settings.convertAdditiveLayer = on; break;
                case VRCAvatarDescriptor.AnimLayerType.Action: settings.convertActionLayer = on; break;
            }
        }

        // Assigned, not the default, and not empty.
        internal static bool SuppliesOwnLayer(VRCAvatarDescriptor descriptor,
            VRCAvatarDescriptor.AnimLayerType type)
            => descriptor != null && CustomLayer(descriptor, type) != null;

        // Whether a baker may still put a layer in an empty slot: VRCFury and
        // Modular Avatar build controllers during the bake.
        internal static bool LayersDecidedByBaker(GameObject root)
            => VRCFuryBaker.HasFuryComponents(root)
               || ModularAvatarBaker.HasModularAvatarComponents(root);

        // On picking an avatar, unticks layers whose slot is empty; returns their names.
        // Here, not in the window, so every caller shares one definition.
        internal static List<string> MatchOptionalLayers(VRCAvatarDescriptor avatar, BridgeSettings settings)
        {
            var unticked = new List<string>();
            if (avatar == null || LayersDecidedByBaker(avatar.gameObject))
            {
                return unticked;
            }
            foreach (var (type, setting) in OptionalLayers)
            {
                if (IsOn(settings, type) && !SuppliesOwnLayer(avatar, type))
                {
                    SetOn(settings, type, false);
                    unticked.Add(setting);
                }
            }
            return unticked;
        }

        // Rows Apply all takes: a fix that is not the user's own call.
        internal static bool IsRecommendation(Advice a) => a.Apply != null && a.Kind != AdviceKind.Manual;

        static void Layers(List<Advice> advice, VRCAvatarDescriptor descriptor, BridgeSettings settings,
            bool baked)
        {
            // Off, empty, and a baker may fill it. Manual, so Apply all leaves them alone.
            if (baked)
            {
                foreach (var (type, setting) in OptionalLayers)
                {
                    if (IsOn(settings, type) || SuppliesOwnLayer(descriptor, type))
                    {
                        continue;
                    }
                    advice.Add(new Advice
                    {
                        Kind = AdviceKind.Manual,
                        Setting = setting,
                        Finding = "Off, and its slot is empty right now, but this avatar is built " +
                                  "by a baker, and VRCFury or Modular Avatar can create one during " +
                                  "the bake, which is what the conversion actually reads. If one " +
                                  "arrives while the box is off it is dropped whole, and the only " +
                                  "notice is a warning in the report after the conversion has run. " +
                                  "Nothing in the scene can say which way it will go, so it is left " +
                                  "to you.",
                        Apply = s => SetOn(s, type, true),
                    });
                }
            }

            foreach (var (type, setting) in OptionalLayers)
            {
                if (!IsOn(settings, type) || SuppliesOwnLayer(descriptor, type))
                {
                    continue;
                }
                if (baked)
                {
                    // Left ticked: the baker may fill it, and a tick for nothing costs nothing.
                    advice.Add(new Advice
                    {
                        Kind = AdviceKind.Confirm,
                        Setting = setting,
                        Finding = $"On, and this avatar's {type} slot is empty, but VRCFury or Modular " +
                                  "Avatar can build one during the bake, which is where the conversion " +
                                  "reads it from. Left on, because that costs nothing if no layer ever " +
                                  "arrives and loses the whole layer if one does.",
                    });
                    continue;
                }
                advice.Add(new Advice
                {
                    Kind = AdviceKind.Change,
                    Setting = setting,
                    Finding = $"On, but this avatar has no {type} layer of its own: the slot is empty, " +
                              "or holds VRChat's default. Nothing converts either way; the setting is " +
                              "left over from another avatar, since these persist between them. " +
                              "Switching it off keeps the box honest about this avatar.",
                    Apply = s => SetOn(s, type, false),
                });
            }

            // Reported, never RECOMMENDED: Action takes full body control and its emote triggers
            // have no CVR equivalent. Silence was worse: a whole Action layer went unnoticed.
            var actionLayer = CustomLayer(descriptor, VRCAvatarDescriptor.AnimLayerType.Action);
            if (actionLayer != null && !settings.convertActionLayer)
            {
                advice.Add(new Advice
                {
                    Kind = AdviceKind.Manual,
                    Setting = "Action (emotes, AFK)",
                    Finding = $"This avatar has its own Action layer ({actionLayer.layers.Length} layer" +
                              $"{(actionLayer.layers.Length == 1 ? "" : "s")}), and it is not being " +
                              "converted. Action holds emotes and AFK: whether that matters is yours " +
                              "to decide, which is why this is not ticked for you: the layer takes " +
                              "FULL BODY control, VRChat's emote triggers have no ChilloutVR " +
                              "equivalent so some states may be unreachable, and a misfire is very " +
                              "visible. Turn it on if this avatar's emotes are the point of it.",
                    // Its own "Turn on" button; Apply all filters on IsRecommendation, so Manuals are safe.
                    Apply = s => s.convertActionLayer = true,
                });
            }

            var additiveLayer = CustomLayer(descriptor, VRCAvatarDescriptor.AnimLayerType.Additive);
            if (additiveLayer != null && !settings.convertAdditiveLayer)
            {
                advice.Add(new Advice
                {
                    Kind = AdviceKind.Manual,
                    Setting = "Additive",
                    Finding = $"This avatar has its own Additive layer ({additiveLayer.layers.Length} " +
                              $"layer{(additiveLayer.layers.Length == 1 ? "" : "s")}), and it is not " +
                              "being converted. Additive is usually idle breathing, blended on top of " +
                              "whatever else is playing: small, and easy not to miss. Turn it on if " +
                              "this avatar's resting motion looks dead without it.",
                    Apply = s => s.convertAdditiveLayer = true,
                });
            }

            var baseLayer = CustomLayer(descriptor, VRCAvatarDescriptor.AnimLayerType.Base);
            if (baseLayer == null)
            {
                return;
            }

            // A copied stock controller passes as custom, but its proxy_ clips are VRChat runtime
            // placeholders that would play over CVR locomotion. Empty layers are skipped, or a
            // stateless add-on layer fails the test and lets the stock one through.
            var filled = baseLayer.layers.Where(HasStates).ToArray();
            if (filled.Length > 0 && filled.All(AnimatorMerger.IsProxyOnlyLayer))
            {
                if (settings.convertBaseLayer)
                {
                    advice.Add(new Advice
                    {
                        Kind = AdviceKind.Change,
                        Setting = "Base / locomotion",
                        Finding = "On, but this avatar's Base layer is VRChat's stock locomotion " +
                                  "copied into the slot: every animation in it is a placeholder " +
                                  "that VRChat swaps for its own while you play. Merged, they play " +
                                  "as they are, on top of ChilloutVR's own locomotion, and add " +
                                  "nothing it lacks. Switching it off leaves this avatar on " +
                                  "ChilloutVR's locomotion, the same as an avatar with the slot empty.",
                        Apply = s => s.convertBaseLayer = false,
                    });
                }
                return;
            }

            if (settings.convertBaseLayer)
            {
                return;
            }

            // Exempt only when Base really is GoGo; VRCFury can merge content beside it.
            if (settings.stripGogoLoco && !HasContentGogoDidNotPutThere(baseLayer))
            {
                return;
            }

            advice.Add(new Advice
            {
                Kind = AdviceKind.Change,
                Setting = "Base / locomotion",
                Finding = $"This avatar has its own Base layer ({baseLayer.layers.Length} layer" +
                          $"{(baseLayer.layers.Length == 1 ? "" : "s")}), so its walking, crouching, " +
                          "falling and stance animations are its own rather than VRChat's stock set. " +
                          "Merging grafts them into ChilloutVR's locomotion by their position in the " +
                          "movement blend trees, keeping ChilloutVR's structure and the avatar's motion.",
                Apply = s => s.convertBaseLayer = true,
            });
        }

        // A non-GoGo layer with something in it; bakers leave empty placeholders behind.
        static bool HasContentGogoDidNotPutThere(AnimatorController controller)
        {
            if (controller == null)
            {
                return false;
            }
            foreach (var layer in controller.layers)
            {
                if (!SystemStripper.IsGogoLayer(layer) && HasStates(layer))
                {
                    return true;
                }
            }
            return false;
        }

        static bool HasStates(AnimatorControllerLayer layer)
        {
            bool any = false;
            WalkMachines(layer.stateMachine, machine => any |= machine.states.Length > 0);
            return any;
        }

        static void WalkMachines(AnimatorStateMachine machine, Action<AnimatorStateMachine> visit)
        {
            if (machine == null)
            {
                return;
            }
            visit(machine);
            foreach (var child in machine.stateMachines)
            {
                WalkMachines(child.stateMachine, visit);
            }
        }

        // ------------------------------------------------------------------- shaders ----

        static void Shaders(List<Advice> advice, GameObject root, BridgeSettings settings)
        {
            if (settings.patchNonSpiShaders)
            {
                return;
            }

            var checkedShaders = new HashSet<Shader>();
            var missing = new List<string>();
            var offenders = new List<UnityEngine.Object>();
            // Swapped-in materials too: the patcher reaches both.
            var materials = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Concat(SwappedMaterials(root.GetComponent<VRCAvatarDescriptor>())).ToList();
            // Planned on the thread pool first; the loop below then reads cached verdicts.
            ShaderSpiPatcher.Prejudge(materials.Select(m => m != null ? m.shader : null), forPatch: false);
            foreach (var material in materials)
            {
                var shader = material != null ? material.shader : null;
                if (shader == null || !checkedShaders.Add(shader))
                {
                    continue;
                }
                // Null source: a built-in shader, already stereo-correct.
                string source = ShaderSpiPatcher.SourcePathOf(shader);
                if (source != null && !ShaderSpiPatcher.DeclaresStereo(source))
                {
                    missing.Add(shader.name);
                    // The shader, not the renderer: one shader usually covers several meshes.
                    offenders.Add(shader);
                }
            }

            if (missing.Count == 0)
            {
                return;
            }

            advice.Add(new Advice
            {
                Kind = AdviceKind.Manual,
                Setting = "Patch non-SPI shaders for VR",
                Finding = $"{missing.Count} shader{(missing.Count == 1 ? "" : "s")} never declare" +
                          $"{(missing.Count == 1 ? "s" : "")} single-pass instanced stereo: " +
                          $"{string.Join(", ", missing.Take(3))}" +
                          (missing.Count > 3 ? $" and {missing.Count - 3} more" : "") + ". ChilloutVR " +
                          "renders single-pass instanced where VRChat renders double-wide, so these can " +
                          "look right in VRChat and draw into one eye only here. Patching copies them " +
                          "with the macros added and leaves the originals alone. Your call, because a " +
                          "copy is checked for compile errors and nothing more: whether it LOOKS right " +
                          "can only be judged with the avatar on, in both eyes.",
                Apply = s => s.patchNonSpiShaders = true,
                Targets = offenders.ToArray(),
            });
        }

        // ---------------------------------------------------------------- components ----

        static void Components(List<Advice> advice, GameObject root, bool baked)
        {
            var present = new List<string>();
            var absent = new List<string>();

            int contacts = root.GetComponentsInChildren<VRCContactSender>(true).Length
                           + root.GetComponentsInChildren<VRCContactReceiver>(true).Length;
            Tally(contacts, "contacts", present, absent);

            int constraints = root.GetComponentsInChildren<Component>(true)
                .Count(c => c != null
                            && Array.IndexOf(ConstraintConverter.VrcConstraintTypeNames, c.GetType().Name) >= 0);
            Tally(constraints, "VRC constraints", present, absent);

            Tally(root.GetComponentsInChildren<VRCHeadChop>(true).Length, "Head Chop", present, absent);
            Tally(root.GetComponentsInChildren<AudioSource>(true).Length, "spatial audio", present, absent);

            if (present.Count > 0)
            {
                advice.Add(new Advice
                {
                    Kind = AdviceKind.Confirm,
                    Setting = "Components",
                    Finding = "Will be converted: " + string.Join(", ", present) + ".",
                });
            }
            if (absent.Count > 0)
            {
                advice.Add(new Advice
                {
                    Kind = AdviceKind.Inert,
                    Setting = "Components",
                    Finding = "Nothing on this avatar for: " + string.Join(", ", absent) +
                              ". Those settings make no difference either way" +
                              (baked ? ", unless the bake adds some." : "."),
                });
            }
        }

        static void Tally(int count, string label, List<string> present, List<string> absent)
        {
            if (count > 0)
            {
                present.Add($"{count} {label}");
            }
            else
            {
                absent.Add(label);
            }
        }

        // ------------------------------------------------------------------- helpers ----

        static IEnumerable<Material> SwappedMaterials(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null) yield break;
            var layers = (descriptor.baseAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0])
                .Concat(descriptor.specialAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0]);
            foreach (var layer in layers)
            {
                if (!(layer.animatorController is AnimatorController controller)) continue;
                foreach (var clip in controller.animationClips.Where(c => c != null).Distinct())
                {
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    {
                        foreach (var key in AnimationUtility.GetObjectReferenceCurve(clip, binding))
                        {
                            if (key.value is Material material) yield return material;
                        }
                    }
                }
            }
        }

        static AnimatorController CustomLayer(VRCAvatarDescriptor descriptor,
            VRCAvatarDescriptor.AnimLayerType type)
        {
            if (descriptor.baseAnimationLayers == null)
            {
                return null;
            }
            foreach (var layer in descriptor.baseAnimationLayers)
            {
                if (layer.type == type && !layer.isDefault
                    && layer.animatorController is AnimatorController controller
                    && controller.layers.Length > 0)
                {
                    return controller;
                }
            }
            return null;
        }

        static IEnumerable<string> ParameterNames(VRCAvatarDescriptor descriptor)
        {
            var vrcParams = descriptor.expressionParameters;
            if (vrcParams == null || vrcParams.parameters == null)
            {
                return Enumerable.Empty<string>();
            }
            return vrcParams.parameters.Where(p => p != null && !string.IsNullOrEmpty(p.name))
                .Select(p => p.name);
        }

        // Face-tracking parameters of the avatar's own rig, including controllers a merge
        // component hands over, so a rig merged at bake time is counted before the bake.
        static int OwnRigParameters(GameObject root, VRCAvatarDescriptor descriptor) => OwnRigParameters(root, descriptor, out _);

        // bits: of those, the binary sync bits a VRCFaceTracking rig sends its values to others with.
        static int OwnRigParameters(GameObject root, VRCAvatarDescriptor descriptor, out int bits)
        {
            var names = new HashSet<string>(ParameterNames(descriptor));
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                {
                    continue;
                }
                var property = new SerializedObject(behaviour).GetIterator();
                // Large arrays are weights and vertex data, never a list of controllers.
                while (property.Next(!(property.isArray && property.arraySize > 1024)))
                {
                    if (property.propertyType == SerializedPropertyType.ObjectReference
                        && property.objectReferenceValue is AnimatorController controller)
                    {
                        names.UnionWith(controller.parameters.Select(p => p.name));
                    }
                }
            }
            bits = names.Count(FaceTrackingParameters.IsBinarySyncBit);
            return names.Count(FaceTrackingParameters.IsFaceTracking);
        }

        static string Nice(PhysicsTarget target) =>
            target == PhysicsTarget.MagicaCloth2 ? "MagicaCloth2"
            : target == PhysicsTarget.DynamicBone ? "DynamicBone"
            : "None";
    }
}
#endif
