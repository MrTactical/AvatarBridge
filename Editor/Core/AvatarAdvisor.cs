#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
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

    // Reads an avatar and says which settings its own contents decide.
    //
    // The point is a division of labour, not cleverness. Most of the options in this window
    // have a right answer that the avatar itself gives away; an avatar with no PhysBones does
    // not need a physics target, one whose face mesh carries fifty Unified Expressions shapes
    // wants ChilloutVR's native face tracking, one whose shaders never declare stereo will lose
    // an eye in VR. Those are measurements, and asking a user to make them by reading tooltips
    // is asking them to do arithmetic the tool can do. What is left over is genuinely theirs:
    // whether the toe physics were deliberate, whether a rigid prop is rigid on purpose,
    // whether to accept a patched shader that compiles but has not been looked at in VR.
    //
    // Two rules this must keep:
    //
    // Every check asks the CONVERTER'S question, through the converter's own code; hence
    // FaceTrackingConverter.DetectShapes, ShaderSpiPatcher.DeclaresStereo,
    // SystemStripper.AvatarUsesGogo and ConstraintConverter.VrcConstraintTypeNames being
    // reachable from here. A second opinion assembled out of this file's own guesses would
    // recommend a box and then convert as if it had not been ticked, which is worse than
    // saying nothing.
    //
    // And it reads the avatar AS IT SITS IN THE SCENE. VRCFury and Modular Avatar add most of
    // their content during the bake, so on those avatars every count here is a floor and the
    // analysis says so out loud rather than quietly recommending "no physics" to someone whose
    // baked outfit brings twenty PhysBones.
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
                        "VRCFury or Modular Avatar will add to this avatar when it converts — clothing, " +
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
                // Deliberately NOT recommending "None" on a baker's avatar. The commonest shape
                // in the corpus is a base body with no PhysBones of its own whose hair and
                // clothing; every chain it has; arrive during the bake.
                advice.Add(new Advice
                {
                    Kind = baked ? AdviceKind.Manual : AdviceKind.Change,
                    Setting = "Convert PhysBones to",
                    Finding = baked
                        ? "No PhysBones on the avatar yet, but its baker usually brings them with the " +
                          "hair and clothing it adds. Left as it is — set this to \"None\" only if you " +
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
                        : $"{count} on this avatar, and the physics target is \"None\" — its hair, tail and " +
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
                                  ? " — which is installed." : ", which is not installed either."),
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
                          "Your call — turn it on if this avatar's toe physics are deliberate.",
                Apply = s => s.convertToePhysBones = true,
                Targets = chains.ToArray(),
            });
        }

        // ------------------------------------------------------------ face tracking ----

        static void FaceTracking(List<Advice> advice, GameObject root, VRCAvatarDescriptor descriptor,
            BridgeSettings settings)
        {
            // No CVRAvatar exists yet, so the descriptor-named mesh that outranks scoring during
            // conversion is not available here; the scoring pass is the same one either way.
            bool hasShapes = FaceTrackingConverter.DetectShapes(root, null,
                out var mesh, out bool unified, out int score);

            if (hasShapes)
            {
                string where = $"{score} face-tracking blendshapes on \"{mesh.name}\" " +
                               $"({(unified ? "Unified Expressions" : "Legacy / SRanipal")} naming)";
                if (settings.faceTrackingMode == FaceTrackingMode.Native)
                {
                    advice.Add(new Advice
                    {
                        Kind = AdviceKind.Confirm,
                        Setting = "Face tracking",
                        Finding = $"{where}. ChilloutVR's own component reads them directly — no OSC and " +
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
                                  "which is the cheapest and most reliable route — it needs no extra " +
                                  "package and spends no animator layers.",
                        Apply = s => s.faceTrackingMode = FaceTrackingMode.Native,
                        Targets = new UnityEngine.Object[] { mesh.gameObject },
                    });
                }
                return;
            }

            var names = ParameterNames(descriptor);
            int ftParams = names.Count(n => AvatarFeatureDetect.IsFaceTrackingParameter(n)
                                            || FaceTrackingParameters.IsFaceTracking(n));

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
                                  ? "CVR-VRCFT replaces the rig with a parameter-driven one that works here."
                                  : $"CVR-VRCFT would be the route, but \"{FaceTrackingPackages.DisplayName}\" " +
                                    "is missing — reimport AvatarBridge to get it back."),
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
                          "rig before they start.",
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
                              "spending around fifteen synced parameters.",
                }
                : new Advice
                {
                    Kind = AdviceKind.Manual,
                    Setting = "Remove GoGo Loco (recommended)",
                    Finding = "GoGo Loco is on this avatar and you have chosen to keep it. That is " +
                              "experimental: GoGo replaces ChilloutVR's locomotion layer outright, so " +
                              "Base, Additive and Action must all be ticked or the avatar has no " +
                              "locomotion at all. Poses will not lock movement and the viewpoint stays " +
                              "at standing height in floor poses — neither has an equivalent here.",
                    // Puts the way back one press away. Keeping GoGo stays a choice; this is a
                    // Manual, so the apply-everything button still leaves it alone.
                    Apply = s => s.stripGogoLoco = true,
                });
        }

        // Which optional layer settings are decided by the slot being
        // EMPTY. Ticked with nothing in the slot converts nothing either
        // way, so this is about the box telling the truth: these persist
        // across avatars, and a tick left from the last one reads as
        // "this avatar has an Action layer" to the next person to look.
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

        // A slot the avatar fills itself: assigned, not the descriptor's
        // own default, and holding something. A missing reference and an
        // empty controller both count as absent.
        internal static bool SuppliesOwnLayer(VRCAvatarDescriptor descriptor,
            VRCAvatarDescriptor.AnimLayerType type)
            => descriptor != null && CustomLayer(descriptor, type) != null;

        // Whether a baker may still put a layer in an empty slot. VRCFury
        // and Modular Avatar build controllers during the bake that the
        // scene has no trace of — a Fury avatar whose Action slot reads
        // empty here converted with an 8-layer Fury Action controller.
        internal static bool LayersDecidedByBaker(GameObject root)
            => VRCFuryBaker.HasFuryComponents(root)
               || ModularAvatarBaker.HasModularAvatarComponents(root);

        static void Layers(List<Advice> advice, VRCAvatarDescriptor descriptor, BridgeSettings settings,
            bool baked)
        {
            // OFF, empty, and on an avatar whose baker builds layers during
            // the bake. Nothing in the scene shows what is coming, so this
            // is the one case the analysis cannot measure — and staying
            // quiet about it is how a real avatar lost a Base layer holding
            // 269 states and 220 parameter drivers that VRCFury created at
            // bake time. The conversion report warned afterwards, which is
            // the wrong end: by then the user has already converted.
            //
            // One row for all of them rather than one each, because on a
            // baker's avatar every unticked layer is in this position and
            // three rows saying the same thing is noise.
            if (baked)
            {
                var unseen = OptionalLayers
                    .Where(l => !IsOn(settings, l.Type) && !SuppliesOwnLayer(descriptor, l.Type))
                    .Select(l => l.Setting)
                    .ToList();
                if (unseen.Count > 0)
                {
                    advice.Add(new Advice
                    {
                        Kind = AdviceKind.Manual,
                        Setting = string.Join(", ", unseen),
                        Finding = $"Off, and {(unseen.Count == 1 ? "its slot is" : "their slots are")} " +
                                  "empty right now — but this avatar is built by a baker, and VRCFury " +
                                  "or Modular Avatar can create these during the bake, which is what " +
                                  "the conversion actually reads. If one arrives while the box is off " +
                                  "it is dropped whole, and the only notice is a warning in the report " +
                                  "after the conversion has run. Nothing in the scene can say which " +
                                  "way it will go, so it is left to you: tick one if this avatar's " +
                                  "locomotion, resting motion or emotes are part of what it is.",
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
                    // Left ticked on purpose. The slot is empty in the
                    // scene and the baker may still fill it, and being
                    // ticked for a layer that never arrives costs nothing.
                    advice.Add(new Advice
                    {
                        Kind = AdviceKind.Confirm,
                        Setting = setting,
                        Finding = $"On, and this avatar's {type} slot is empty — but VRCFury or Modular " +
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
                    Finding = $"On, but this avatar has no {type} layer of its own — the slot is empty, " +
                              "or holds VRChat's default. Nothing converts either way; the setting is " +
                              "left over from another avatar, since these persist between them. " +
                              "Switching it off keeps the box honest about this avatar.",
                    Apply = s => SetOn(s, type, false),
                });
            }

            // Action and Additive are reported but never RECOMMENDED, because neither is a clear
            // win: Action takes full body control and its emote triggers have no ChilloutVR
            // equivalent, so states can be unreachable, and a misfire is very visible. Saying
            // nothing was the worse failure though; an avatar with a custom Action controller
            // sitting there unmentioned reads as "checked, nothing here", which is how a copter
            // avatar's whole Action layer went unnoticed.
            var actionLayer = CustomLayer(descriptor, VRCAvatarDescriptor.AnimLayerType.Action);
            if (actionLayer != null && !settings.convertActionLayer)
            {
                advice.Add(new Advice
                {
                    Kind = AdviceKind.Manual,
                    Setting = "Action (emotes, AFK)",
                    Finding = $"This avatar has its own Action layer ({actionLayer.layers.Length} layer" +
                              $"{(actionLayer.layers.Length == 1 ? "" : "s")}), and it is not being " +
                              "converted. Action holds emotes and AFK — whether that matters is yours " +
                              "to decide, which is why this is not ticked for you: the layer takes " +
                              "FULL BODY control, VRChat's emote triggers have no ChilloutVR " +
                              "equivalent so some states may be unreachable, and a misfire is very " +
                              "visible. Turn it on if this avatar's emotes are the point of it.",
                    // Gives the row its own "Turn on" button. Safe to attach on a Manual: the
                    // apply-everything button filters on IsRecommendation, which excludes them,
                    // so this can only ever be pressed deliberately.
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
                              "whatever else is playing — small, and easy not to miss. Turn it on if " +
                              "this avatar's resting motion looks dead without it.",
                    Apply = s => s.convertAdditiveLayer = true,
                });
            }

            var baseLayer = CustomLayer(descriptor, VRCAvatarDescriptor.AnimLayerType.Base);
            if (baseLayer == null || settings.convertBaseLayer)
            {
                return;
            }

            // On a GoGo avatar the Base layer is USUALLY just GoGo, and merging what is about to
            // be stripped buys nothing. Usually is not always: VRCFury merges its own content
            // into Base alongside it, and a real avatar arrived with 269 states and 220 parameter
            // drivers sitting in there. Suppressing the advice on the mere PRESENCE of GoGo hid
            // every one of them — and then the conversion report warned about the same layer
            // afterwards, telling the user to tick the setting the analysis had silently decided
            // against. The tool disagreeing with itself, and only after they had committed.
            //
            // So exempt only when what is in there really is GoGo.
            if (SystemStripper.AvatarUsesGogo(descriptor) && settings.stripGogoLoco
                && !HasContentGogoDidNotPutThere(baseLayer))
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

        // A layer GoGo did not name, with something actually in it. An empty
        // one is not content: VRCFury and friends leave placeholders behind,
        // and advising a merge for the sake of an empty layer is noise.
        static bool HasContentGogoDidNotPutThere(AnimatorController controller)
        {
            if (controller == null)
            {
                return false;
            }
            foreach (var layer in controller.layers)
            {
                if (SystemStripper.IsGogoLayerName(layer.name))
                {
                    continue;
                }
                bool any = false;
                WalkMachines(layer.stateMachine, machine => any |= machine.states.Length > 0);
                if (any)
                {
                    return true;
                }
            }
            return false;
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
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    var shader = material != null ? material.shader : null;
                    if (shader == null || !checkedShaders.Add(shader))
                    {
                        continue;
                    }
                    // A null source is an engine shader with nothing on disk to read; Unity's own
                    // ship stereo-correct, so they are not counted against the avatar.
                    string source = ShaderSpiPatcher.SourcePathOf(shader);
                    if (source != null && !ShaderSpiPatcher.DeclaresStereo(source))
                    {
                        missing.Add(shader.name);
                        // The shader asset, not the renderer: one shader is usually worn by
                        // several meshes, and the thing to go and look at is the shader.
                        offenders.Add(shader);
                    }
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
                          $"{(missing.Count == 1 ? "s" : "")} single-pass instanced stereo — " +
                          $"{string.Join(", ", missing.Take(3))}" +
                          (missing.Count > 3 ? $" and {missing.Count - 3} more" : "") + ". ChilloutVR " +
                          "renders single-pass instanced where VRChat renders double-wide, so these can " +
                          "look right in VRChat and draw into one eye only here. Patching copies them " +
                          "with the macros added and leaves the originals alone. Your call, because a " +
                          "copy is checked for compile errors and nothing more — whether it LOOKS right " +
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

        static string Nice(PhysicsTarget target) =>
            target == PhysicsTarget.MagicaCloth2 ? "MagicaCloth2"
            : target == PhysicsTarget.DynamicBone ? "DynamicBone"
            : "None";
    }
}
#endif
