// What an avatar actually does, read rather than driven.
//
// The toggle sweep moves a parameter and watches: it can say nothing
// appeared to happen and never why. This is the other half. It reads the
// controller, the menu and the components into one model, so a question
// like "can anything change this parameter" has an answer that does not
// depend on catching it in the act.
//
// Nothing here writes to the avatar.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    public static class AvatarSurvey
    {
        // How a parameter can come to hold a value. A parameter with none
        // of these is stuck at its default for the avatar's whole life.
        public enum Source { Menu, Driver, Contact, Stream, Curve, Game }

        public class Param
        {
            public string Name;
            public AnimatorControllerParameterType Type;
            public string Default;
            public bool Synced;
            public readonly SortedSet<string> Writers = new SortedSet<string>(System.StringComparer.Ordinal);
            public readonly SortedSet<string> Readers = new SortedSet<string>(System.StringComparer.Ordinal);
            public readonly SortedSet<Source> How = new SortedSet<Source>();
            // Layers that read it AND write something to the avatar, so a
            // parameter nothing can change is a feature nobody can reach.
            public int DrivesBindings;
        }

        public class Layer
        {
            public int Index;
            public string Name;
            public float Weight;
            public string Mask;
            public int States;
            public readonly SortedSet<string> Bindings = new SortedSet<string>(System.StringComparer.Ordinal);
            public readonly SortedSet<string> Reads = new SortedSet<string>(System.StringComparer.Ordinal);
        }

        public class Control
        {
            public string Name;
            public string Machine;
            public string Kind;
        }

        public class Finding
        {
            public string Kind;
            public string Subject;
            public string Detail;
        }

        // Something that could leave the avatar and be uploaded as a prop:
        // its own mesh, its own textures, hanging off one bone, switched by
        // one control. Anything skinned across the rig is clothing and stays.
        public class PropCandidate
        {
            public string Path;
            public string Bone;
            public string Control;
            public int Triangles;
            public int Materials;
            public long TextureBytes;
        }

        public class Model
        {
            public string Avatar;
            public readonly List<Param> Parameters = new List<Param>();
            public readonly List<Layer> Layers = new List<Layer>();
            public readonly List<Control> Controls = new List<Control>();
            public readonly List<PropCandidate> Props = new List<PropCandidate>();
            public readonly Dictionary<string, int> Rejected = new Dictionary<string, int>();
            public readonly List<Finding> Findings = new List<Finding>();
            public Param this[string name] => Parameters.FirstOrDefault(p => p.Name == name);
        }

        public static Model Build(CVRAvatar avatar)
        {
            var model = new Model();
            if (avatar == null) return model;
            model.Avatar = avatar.name;

            var animator = avatar.GetComponent<Animator>();
            var controller = BridgeContext.Underlying(animator != null ? animator.runtimeAnimatorController : null);
            if (controller == null) return model;

            foreach (var p in controller.parameters)
            {
                model.Parameters.Add(new Param
                {
                    Name = p.name,
                    Type = p.type,
                    Default = p.type == AnimatorControllerParameterType.Bool ? p.defaultBool.ToString()
                        : p.type == AnimatorControllerParameterType.Int ? p.defaultInt.ToString()
                        : p.type == AnimatorControllerParameterType.Float ? p.defaultFloat.ToString("0.###")
                        : "pulse",
                    // A "#" name never leaves the wearer, and a Trigger cannot sync at all.
                    Synced = !p.name.StartsWith("#", System.StringComparison.Ordinal)
                             && p.type != AnimatorControllerParameterType.Trigger,
                });
            }

            ReadLayers(controller, model);
            ReadControls(avatar, model);
            ReadComponents(avatar, model);
            MarkGameDriven(model);
            FindProps(avatar, model);
            Judge(model);
            return model;
        }

        static void ReadLayers(AnimatorController controller, Model model)
        {
            for (int i = 0; i < controller.layers.Length; i++)
            {
                var l = controller.layers[i];
                var layer = new Layer
                {
                    Index = i,
                    Name = l.name,
                    Weight = l.defaultWeight,
                    Mask = l.avatarMask != null ? l.avatarMask.name : "",
                };
                if (l.stateMachine != null) WalkMachine(l.stateMachine, layer, model);
                model.Layers.Add(layer);

                foreach (string name in layer.Reads)
                {
                    var p = model[name];
                    if (p == null) continue;
                    p.Readers.Add($"layer \"{l.name}\"");
                    if (layer.Bindings.Count > 0) p.DrivesBindings += layer.Bindings.Count;
                }
            }
        }

        static void WalkMachine(AnimatorStateMachine machine, Layer layer, Model model)
        {
            foreach (var child in machine.states)
            {
                layer.States++;
                var st = child.state;
                if (st == null) continue;

                // A state can read a parameter without any transition doing so.
                if (st.speedParameterActive) layer.Reads.Add(st.speedParameter);
                if (st.cycleOffsetParameterActive) layer.Reads.Add(st.cycleOffsetParameter);
                if (st.mirrorParameterActive) layer.Reads.Add(st.mirrorParameter);
                if (st.timeParameterActive) layer.Reads.Add(st.timeParameter);

                CollectMotion(st.motion, layer);
                foreach (var t in st.transitions) CollectConditions(t, layer);
                foreach (var b in st.behaviours)
                {
                    foreach (string target in DriverTargets(b))
                    {
                        var p = model[target];
                        if (p == null) continue;
                        p.Writers.Add($"a driver in layer \"{layer.Name}\"");
                        p.How.Add(Source.Driver);
                    }
                }
            }
            foreach (var t in machine.anyStateTransitions) CollectConditions(t, layer);
            foreach (var t in machine.entryTransitions) CollectConditions(t, layer);
            foreach (var sub in machine.stateMachines)
            {
                if (sub.stateMachine != null) WalkMachine(sub.stateMachine, layer, model);
            }
        }

        static void CollectConditions(AnimatorTransitionBase t, Layer layer)
        {
            if (t == null || t.conditions == null) return;
            foreach (var c in t.conditions)
            {
                if (!string.IsNullOrEmpty(c.parameter)) layer.Reads.Add(c.parameter);
            }
        }

        static void CollectMotion(Motion motion, Layer layer)
        {
            if (motion == null) return;
            if (motion is BlendTree tree)
            {
                if (!string.IsNullOrEmpty(tree.blendParameter)) layer.Reads.Add(tree.blendParameter);
                if (!string.IsNullOrEmpty(tree.blendParameterY)) layer.Reads.Add(tree.blendParameterY);
                foreach (var child in tree.children)
                {
                    if (!string.IsNullOrEmpty(child.directBlendParameter)) layer.Reads.Add(child.directBlendParameter);
                    CollectMotion(child.motion, layer);
                }
                return;
            }
            if (!(motion is AnimationClip clip)) return;
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                layer.Bindings.Add($"{b.path}::{b.propertyName}");
            }
            foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                layer.Bindings.Add($"{b.path}::{b.propertyName}");
            }
        }

        static void ReadControls(CVRAvatar avatar, Model model)
        {
            var settings = avatar.avatarSettings != null ? avatar.avatarSettings.settings : null;
            if (settings == null) return;
            foreach (var e in settings)
            {
                if (e == null) continue;
                string machine = e.machineName;
                model.Controls.Add(new Control { Name = e.name, Machine = machine, Kind = e.type.ToString() });
                foreach (var p in model.Parameters.Where(p => Names(machine).Contains(p.Name)))
                {
                    p.Writers.Add($"the menu control \"{e.name}\"");
                    p.How.Add(Source.Menu);
                }
            }
        }

        // A control writes its machine name, and the axis suffixes the
        // client registers for the multi-value types.
        static IEnumerable<string> Names(string machine)
        {
            if (string.IsNullOrEmpty(machine)) yield break;
            yield return machine;
            foreach (string suffix in new[] { "-x", "-y", "-z", "-r", "-g", "-b" })
            {
                yield return machine + suffix;
            }
        }

        static void ReadComponents(CVRAvatar avatar, Model model)
        {
            foreach (var trigger in avatar.GetComponentsInChildren<CVRAdvancedAvatarSettingsTrigger>(true))
            {
                if (trigger == null) continue;
                var names = trigger.enterTasks.Select(t => t.settingName)
                    .Concat(trigger.exitTasks.Select(t => t.settingName))
                    .Concat(trigger.stayTasks.Select(t => t.settingName));
                foreach (string name in names.Where(n => !string.IsNullOrEmpty(n)))
                {
                    var p = model[name];
                    if (p == null) continue;
                    p.Writers.Add($"the contact \"{trigger.name}\"");
                    p.How.Add(Source.Contact);
                }
            }

            // Streams are read by name: the CCK's entry type has moved
            // between versions and the field is not worth a hard reference.
            foreach (var stream in avatar.GetComponentsInChildren<CVRParameterStream>(true))
            {
                if (stream == null) continue;
                var field = stream.GetType().GetField("entries");
                if (!(field?.GetValue(stream) is System.Collections.IEnumerable entries)) continue;
                foreach (var entry in entries)
                {
                    if (entry == null) continue;
                    var target = entry.GetType().GetField("parameterName") ?? entry.GetType().GetField("targetName");
                    if (!(target?.GetValue(entry) is string name) || string.IsNullOrEmpty(name)) continue;
                    var p = model[name];
                    if (p == null) continue;
                    p.Writers.Add($"the parameter stream on \"{stream.name}\"");
                    p.How.Add(Source.Stream);
                }
            }
        }

        // Which renderers could be lifted off the avatar entirely.
        //
        // The test is deliberately strict, because being wrong here costs
        // somebody their outfit: one bone, textures nothing else uses, and a
        // single control switching it. Clothing skinned across the rig, and
        // anything a second layer animates, fails and stays where it is.
        static void FindProps(CVRAvatar avatar, Model model)
        {
            var root = avatar.transform;
            var renderers = avatar.GetComponentsInChildren<Renderer>(true)
                .Where(r => r != null && MeshOf(r) != null)
                .ToList();

            // A texture worth moving is one only this renderer uses.
            var users = new Dictionary<Texture, int>();
            foreach (var r in renderers)
            {
                foreach (var tex in TexturesOf(r).Distinct())
                {
                    users.TryGetValue(tex, out int n);
                    users[tex] = n + 1;
                }
            }

            foreach (var r in renderers)
            {
                string path = AnimationUtility.CalculateTransformPath(r.transform, root);
                var mesh = MeshOf(r);

                // One bone, or none. More than one is clothing.
                string bone = null;
                if (r is SkinnedMeshRenderer skin)
                {
                    // One bone, or one chain. A thing held in a hand is
                    // weighted to the hand AND its fingers, which is still
                    // extractable: attach at the root of the chain and lose
                    // the articulation. Bones on separate limbs are clothing.
                    var used = UsedBones(skin)
                        .Where(i => skin.bones != null && i >= 0 && i < skin.bones.Length && skin.bones[i] != null)
                        .Select(i => skin.bones[i])
                        .ToList();
                    if (used.Count == 0) { Reject(model, "bone list does not match the weights"); continue; }
                    var chainRoot = ChainRoot(used);
                    if (chainRoot == null) { Reject(model, $"skinned across {used.Count} bones on different parts of the rig"); continue; }
                    if (used.Count > 8) { Reject(model, $"skinned to {used.Count} bones, too much of the rig to lift out"); continue; }
                    bone = used.Count == 1 ? chainRoot.name : $"{chainRoot.name} (chain of {used.Count})";
                    if (mesh.blendShapeCount > 0) { Reject(model, "has blendshapes, so it is fitted to this body"); continue; }
                }
                else
                {
                    bone = r.transform.parent != null ? r.transform.parent.name : null;
                }
                if (string.IsNullOrEmpty(bone)) { Reject(model, "no bone to ride"); continue; }

                // A toggle almost never animates the renderer: it switches an
                // object above it. Climb while the ancestor holds this
                // renderer and nothing else, which is exactly the boundary a
                // prop would be cut along, and look for any of those paths.
                var paths = new List<string> { path };
                for (var at = r.transform.parent; at != null && at != root; at = at.parent)
                {
                    if (at.GetComponentsInChildren<Renderer>(true).Count(x => MeshOf(x) != null) != 1) break;
                    paths.Add(AnimationUtility.CalculateTransformPath(at, root));
                }

                // How many layers touch it does not matter: a prop is
                // routinely switched by its own toggle, a preset and an emote
                // all at once. What matters is whether anything does more
                // than switch it, because a material or blendshape animated
                // from the avatar cannot follow it out.
                var touching = model.Layers
                    .Where(l => l.Bindings.Any(b => paths.Any(p =>
                        b.StartsWith(p + "::", System.StringComparison.Ordinal))))
                    .ToList();
                if (touching.Count == 0) { Reject(model, "nothing switches it"); continue; }

                var touched = touching
                    .SelectMany(l => l.Bindings)
                    .Where(b => paths.Any(p => b.StartsWith(p + "::", System.StringComparison.Ordinal)))
                    .Select(b => b.Substring(b.IndexOf("::", System.StringComparison.Ordinal) + 2))
                    .Distinct()
                    .ToList();
                var beyondSwitching = touched
                    // Scaling to zero is how a great many avatars toggle a
                    // thing that must stay active. Treat it as switching: a
                    // candidate list is read by a person, and refusing every
                    // scale-toggled prop hides most of them.
                    .Where(prop => prop != "m_IsActive" && prop != "m_Enabled"
                                   && !prop.StartsWith("m_LocalScale", System.StringComparison.Ordinal))
                    .ToList();
                if (beyondSwitching.Count > 0)
                {
                    Reject(model, $"animated beyond switching ({beyondSwitching[0]})");
                    continue;
                }

                var controls = model.Controls
                    .Where(c => touching.Any(l => l.Reads.Any(name => Names(c.Machine).Contains(name))))
                    .ToList();
                if (controls.Count == 0) { Reject(model, "nothing on the menu switches it"); continue; }

                long bytes = TexturesOf(r).Distinct()
                    .Where(t => users.TryGetValue(t, out int n) && n == 1)
                    .Sum(t => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t));

                model.Props.Add(new PropCandidate
                {
                    Path = path,
                    Bone = bone,
                    Control = string.Join(", ", controls.Select(c => c.Name)),
                    Triangles = mesh.triangles.Length / 3,
                    Materials = r.sharedMaterials.Count(m => m != null),
                    TextureBytes = bytes,
                });
            }
        }

        static void Reject(Model model, string why)
        {
            model.Rejected.TryGetValue(why, out int n);
            model.Rejected[why] = n + 1;
        }

        static Mesh MeshOf(Renderer r)
        {
            if (r is SkinnedMeshRenderer skin) return skin.sharedMesh;
            var filter = r.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        // The one bone every other used bone hangs off, or null when they
        // sit on separate branches: a shirt reaching both arms has no root
        // short of the spine, and lifting that out is lifting out the body.
        static Transform ChainRoot(List<Transform> used)
        {
            foreach (var candidate in used)
            {
                if (used.All(b => b == candidate || b.IsChildOf(candidate))) return candidate;
            }
            return null;
        }

        static SortedSet<int> UsedBones(SkinnedMeshRenderer skin)
        {
            var used = new SortedSet<int>();
            var mesh = skin.sharedMesh;
            if (mesh == null) return used;
            foreach (var w in mesh.boneWeights)
            {
                if (w.weight0 > 0.001f) used.Add(w.boneIndex0);
                if (w.weight1 > 0.001f) used.Add(w.boneIndex1);
                if (w.weight2 > 0.001f) used.Add(w.boneIndex2);
                if (w.weight3 > 0.001f) used.Add(w.boneIndex3);
                if (used.Count > 24) break;   // past any prop; the caller rejects on count anyway
            }
            return used;
        }

        static IEnumerable<Texture> TexturesOf(Renderer r)
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || m.shader == null) continue;
                int count = ShaderUtil.GetPropertyCount(m.shader);
                for (int i = 0; i < count; i++)
                {
                    if (ShaderUtil.GetPropertyType(m.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                    var tex = m.GetTexture(ShaderUtil.GetPropertyName(m.shader, i));
                    if (tex != null) yield return tex;
                }
            }
        }

        // The game writes these itself whatever the avatar says.
        static void MarkGameDriven(Model model)
        {
            foreach (var p in model.Parameters.Where(p => CvrParameterNames.IsGameDriven(p.Name)))
            {
                p.Writers.Add("ChilloutVR itself");
                p.How.Add(Source.Game);
            }
        }

        static void Judge(Model model)
        {
            foreach (var p in model.Parameters)
            {
                if (p.Writers.Count == 0 && p.Readers.Count == 0)
                {
                    Add(model, "unused parameter", p.Name, "nothing reads it and nothing writes it");
                }
                else if (p.Writers.Count == 0 && p.DrivesBindings > 0 && Neutralised(p.Name, out string by))
                {
                    // Frozen on purpose. A condition reading it now always
                    // takes the same branch, which is what the setting meant,
                    // and calling that a lost feature sends people hunting.
                    Add(model, "neutralised by a setting", p.Name,
                        $"{by} removed what wrote it, so conditions reading it settle at {p.Default} " +
                        "and behave as though it never fires. Working as asked, not broken.");
                }
                else if (p.Writers.Count == 0 && p.DrivesBindings > 0 && LostItsDriver(p.Name, out string was))
                {
                    // Not an unwired feature: something used to write this and
                    // the conversion removed the thing that did.
                    Add(model, "stopped working when converted", p.Name,
                        $"{was}, so nothing writes it here. It drives {p.DrivesBindings} binding(s) " +
                        $"and now sits at {p.Default}.");
                }
                else if (p.Writers.Count == 0 && p.DrivesBindings > 0)
                {
                    // The author built the behaviour and never wired a way in.
                    Add(model, "unreachable feature", p.Name,
                        $"{p.Readers.Count} layer(s) act on it and it drives {p.DrivesBindings} binding(s), " +
                        $"but nothing can change it: no menu control, no contact, no driver. Stuck at {p.Default}.");
                }
                else if (p.Writers.Count == 0)
                {
                    Add(model, "frozen parameter", p.Name, $"read but never written, so it stays {p.Default}");
                }
                else if (p.Readers.Count == 0)
                {
                    Add(model, "write-only parameter", p.Name,
                        $"written by {string.Join(", ", p.Writers)}, and no layer reads it");
                }
            }

            foreach (var c in model.Controls)
            {
                var driven = model.Parameters.Where(p => Names(c.Machine).Contains(p.Name)).ToList();
                if (driven.Count == 0)
                {
                    Add(model, "control with no parameter", c.Name, $"\"{c.Machine}\" is not on the controller");
                }
                else if (driven.All(p => p.Readers.Count == 0))
                {
                    Add(model, "control that does nothing", c.Name, "no layer reads what it writes");
                }
            }

            foreach (var l in model.Layers.Where(l => l.States == 0))
            {
                Add(model, "empty layer", l.Name, "no states, so it can never do anything");
            }

            // One binding, two layers: the higher index wins, quietly.
            var owners = new Dictionary<string, List<string>>();
            foreach (var l in model.Layers)
            {
                foreach (string b in l.Bindings)
                {
                    if (!owners.TryGetValue(b, out var list)) owners[b] = list = new List<string>();
                    list.Add(l.Name);
                }
            }
            foreach (var pair in owners.Where(p => p.Value.Count > 1))
            {
                // An empty path is the Animator itself: humanoid muscles, root
                // motion, animated parameters. Locomotion, an additive idle and
                // an action layer all writing the body is the design, not a
                // clash, and reporting it buries the clashes that matter.
                if (pair.Key.StartsWith("::", System.StringComparison.Ordinal)) continue;
                Add(model, "contested binding", pair.Key,
                    $"written by {pair.Value.Count} layers ({string.Join(", ", pair.Value)}); the last one wins");
            }
        }

        // A parameter this tool deliberately cut the writer from. The name
        // survives because a layer still reads it beside parameters that are
        // very much alive, and the merger keeps what it references.
        static bool Neutralised(string name, out string by)
        {
            by = null;
            string bare = name.TrimStart('#');
            foreach (string prefix in new[] { "OGB", "pcs/", "WH_" })
            {
                if (!bare.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) continue;
                by = "removing the OGB and PCS haptics contacts";
                return true;
            }
            return false;
        }

        // A parameter VRChat wrote and ChilloutVR does not. Worth telling
        // apart from a feature the author never wired: this one worked
        // before the conversion, and the fix is different.
        static bool LostItsDriver(string name, out string was)
        {
            was = null;
            string bare = name.TrimStart('#');
            foreach (string suffix in new[] { "_IsGrabbed", "_Angle", "_Stretch", "_Squish", "_IsPosed" })
            {
                if (!bare.EndsWith(suffix, System.StringComparison.Ordinal)) continue;
                was = "a PhysBone wrote this in VRChat and MagicaCloth2 does not"
                      + (suffix == "_IsGrabbed" || suffix == "_Angle"
                          ? ", though the GrabbyBones mod drives it for anyone running that"
                          : "");
                return true;
            }
            if (bare.StartsWith("VRC", System.StringComparison.Ordinal))
            {
                was = "VRChat's own menu or systems wrote this and ChilloutVR has no equivalent";
                return true;
            }
            return false;
        }

        static void Add(Model model, string kind, string subject, string detail)
            => model.Findings.Add(new Finding { Kind = kind, Subject = subject, Detail = detail });

        // Reflection: the CCK's driver task type has changed shape across
        // versions and the harness reads it the same way.
        static List<string> DriverTargets(StateMachineBehaviour behaviour)
        {
            var found = new SortedSet<string>(System.StringComparer.Ordinal);
            if (behaviour == null) return found.ToList();
            var type = behaviour.GetType();
            foreach (string listName in new[] { "EnterTasks", "ExitTasks", "UpdateTasks" })
            {
                var field = type.GetField(listName);
                if (!(field?.GetValue(behaviour) is System.Collections.IEnumerable tasks)) continue;
                foreach (var task in tasks)
                {
                    if (task == null) continue;
                    var target = task.GetType().GetField("targetName");
                    if (target?.GetValue(task) is string name && !string.IsNullOrEmpty(name)) found.Add(name);
                }
            }
            return found.ToList();
        }

        // A name nobody chose and nobody can act on.
        //
        // GoGo Loco's parameters, this tool's scaffolding, what the game
        // writes, what a PhysBone used to write. Normal, and not findings.
        static readonly System.Text.RegularExpressions.Regex Scaffolding =
            new System.Text.RegularExpressions.Regex(
                @"^#?(Go/|AB_Ready_|YAPS\d|VF\d+_|VF_\d+_)|_(IsGrabbed|Stretch|Squish|Angle|IsPosed)$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        static bool Internal(string name) =>
            !string.IsNullOrEmpty(name)
            && (Scaffolding.IsMatch(name) || CvrParameterNames.IsGameDriven(name));

        // Findings a person can act on: something of theirs is broken or
        // unreachable, and the name is one they would recognise.
        static readonly HashSet<string> Actionable = new HashSet<string>
        {
            "unreachable feature", "control that does nothing", "control with no parameter",
            "stopped working when converted", "unused parameter", "empty layer",
        };

        static bool WorthTelling(Finding f) => Actionable.Contains(f.Kind) && !Internal(f.Subject);

        // What each finding is called where somebody who did not build the
        // avatar has to read it. The model's own names are for the model.
        static string Headline(string kind, int count)
        {
            switch (kind)
            {
                case "unreachable feature":
                    return $"{count} feature(s) nothing can switch on";
                case "stopped working when converted":
                    return $"{count} parameter(s) lost whatever used to drive them";
                case "neutralised by a setting":
                    return $"{count} parameter(s) frozen on purpose by a conversion setting";
                case "contested binding":
                    return $"{count} thing(s) more than one layer animates, where the higher layer quietly wins";
                case "control that does nothing":
                    return $"{count} menu control(s) nothing reads";
                case "control with no parameter":
                    return $"{count} menu control(s) naming a parameter the controller does not have";
                case "unused parameter":
                    return $"{count} parameter(s) nothing reads or writes";
                case "frozen parameter":
                    return $"{count} parameter(s) read but never written";
                case "write-only parameter":
                    return $"{count} parameter(s) written and never read";
                case "empty layer":
                    return $"{count} layer(s) with no states";
                default:
                    return $"{count} × {kind}";
            }
        }

        // The order a reader cares about, not the order they happen to be
        // in: something nobody can reach beats something merely untidy.
        static readonly string[] Order =
        {
            "unreachable feature", "stopped working when converted", "control that does nothing",
            "control with no parameter", "contested binding", "neutralised by a setting",
            "unused parameter", "frozen parameter", "write-only parameter", "empty layer",
        };

        static int Rank(string kind)
        {
            int i = System.Array.IndexOf(Order, kind);
            return i < 0 ? Order.Length : i;
        }

        // The conversion report's section: the counts in the open, the full
        // listing folded, because a complex avatar has a thousand contested
        // bindings and nobody reads a thousand lines.
        public static string Markdown(Model model)
        {
            var sb = new StringBuilder();
            sb.Append(model.Parameters.Count).Append(" parameters (")
              .Append(model.Parameters.Count(p => p.Synced)).Append(" synced), ")
              .Append(model.Layers.Count).Append(" layers, ")
              .Append(model.Controls.Count).Append(" menu controls.\n\n");

            var worth = model.Findings.Where(WorthTelling).ToList();
            foreach (var group in worth.GroupBy(f => f.Kind).OrderBy(g => Rank(g.Key)))
            {
                sb.Append("- **").Append(Headline(group.Key, group.Count())).Append("** — ")
                  .Append(string.Join(", ", group.Take(4).Select(f => f.Subject)));
                if (group.Count() > 4) sb.Append(", and ").Append(group.Count() - 4).Append(" more");
                sb.Append('\n');
            }
            int quiet = model.Findings.Count - worth.Count;
            if (quiet > 0)
            {
                sb.Append("- ").Append(quiet).Append(" more finding(s) about names nobody chose — GoGo Loco's ")
                  .Append("parameters, this tool's own scaffolding, what ChilloutVR writes and nothing reads, ")
                  .Append("the grab and stretch parameters a PhysBone used to write. Listed in full below.\n");
            }

            if (model.Props.Count > 0)
            {
                sb.Append("- **").Append(model.Props.Count)
                  .Append(" object(s) could come off as props** — ")
                  .Append(string.Join(", ", model.Props.Select(p => $"\"{p.Control}\"")))
                  .Append(". Each rides one bone chain, is switched by one control, and carries its own " +
                          "mesh and textures, so ChilloutVR could hold it as a prop instead of the avatar " +
                          "carrying it everywhere.\n");
            }

            sb.Append("\n<details>\n<summary>Everything the survey read</summary>\n\n```\n");
            sb.Append(Report(model));
            sb.Append("```\n</details>\n");
            return sb.ToString();
        }

        // The Toolkit shows rows, so one row per kind rather than per
        // finding: twelve hundred rows is not a report, it is a wall.
        public static void Fill(BridgeReport report, Model model)
        {
            report.Add(ReportStatus.Converted, "Survey", "Read",
                $"{model.Parameters.Count} parameters ({model.Parameters.Count(p => p.Synced)} synced), " +
                $"{model.Layers.Count} layers, {model.Controls.Count} menu controls.");

            var worth = model.Findings.Where(WorthTelling).ToList();
            int quiet = model.Findings.Count - worth.Count;

            foreach (var group in worth.GroupBy(f => f.Kind).OrderBy(g => Rank(g.Key)))
            {
                bool loud = group.Key == "unreachable feature"
                            || group.Key == "stopped working when converted"
                            || group.Key == "control that does nothing"
                            || group.Key == "control with no parameter";
                string detail = string.Join(", ", group.Take(8).Select(f => f.Subject));
                if (group.Count() > 8) detail += $", and {group.Count() - 8} more";
                var first = group.First();
                report.Add(loud ? ReportStatus.Warning : ReportStatus.Approximated, "Survey",
                    Headline(group.Key, group.Count()), detail + ". " + first.Detail);
            }

            foreach (var p in model.Props.OrderByDescending(p => p.TextureBytes))
            {
                report.Add(ReportStatus.Approximated, "Survey", $"\"{p.Control}\" could come off as a prop",
                    $"{p.Path} rides {p.Bone}, {p.Triangles} tris, {p.Materials} material(s), " +
                    $"{(p.TextureBytes / 1048576f):0.0} MB of textures nothing else uses.");
            }

            if (quiet > 0)
            {
                report.Add(ReportStatus.Converted, "Survey", $"{quiet} more finding(s) not worth your time",
                    "Names nobody chose and nothing anybody can act on: GoGo Loco's own parameters, the " +
                    "scaffolding this tool generates, the ones ChilloutVR writes and no layer reads, the grab " +
                    "and stretch parameters a PhysBone used to write. They are all in the report file if you " +
                    "ever want them.");
            }
        }

        public static string Report(Model model)
        {
            var sb = new StringBuilder();
            sb.Append("# Survey of ").Append(model.Avatar).Append('\n');
            sb.Append(model.Parameters.Count).Append(" parameters (")
              .Append(model.Parameters.Count(p => p.Synced)).Append(" synced), ")
              .Append(model.Layers.Count).Append(" layers, ")
              .Append(model.Controls.Count).Append(" controls\n\n");

            if (model.Rejected.Count > 0)
            {
                sb.Append("## not props, and why (").Append(model.Rejected.Values.Sum()).Append(" renderers)\n");
                foreach (var pair in model.Rejected.OrderByDescending(p => p.Value))
                {
                    sb.Append("  ").Append(pair.Value).Append(" : ").Append(pair.Key).Append('\n');
                }
                sb.Append('\n');
            }

            if (model.Props.Count > 0)
            {
                sb.Append("## could be props (").Append(model.Props.Count).Append(")\n");
                foreach (var p in model.Props.OrderByDescending(p => p.TextureBytes))
                {
                    sb.Append("  ").Append(p.Path)
                      .Append(" : on ").Append(p.Bone)
                      .Append(", switched by \"").Append(p.Control).Append("\", ")
                      .Append(p.Triangles).Append(" tris, ")
                      .Append(p.Materials).Append(" material(s), ")
                      .Append((p.TextureBytes / 1048576f).ToString("0.0")).Append(" MB of textures nothing else uses\n");
                }
                sb.Append('\n');
            }

            foreach (var group in model.Findings.GroupBy(f => f.Kind).OrderByDescending(g => g.Count()))
            {
                sb.Append("## ").Append(group.Key).Append(" (").Append(group.Count()).Append(")\n");
                foreach (var f in group.Take(40))
                {
                    sb.Append("  ").Append(f.Subject).Append(" : ").Append(f.Detail).Append('\n');
                }
                if (group.Count() > 40) sb.Append("  ... and ").Append(group.Count() - 40).Append(" more\n");
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
#endif
