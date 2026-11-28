// The accessory plan's hard part, measured: a VRChat accessory put onto an already converted
// ChilloutVR avatar by way of a stand-in. A copy of the converted avatar, with a bare VRChat
// descriptor and the accessories as children, converts the normal way into Assets/__AccessoryProbe
// (cleared from outside after), and the result is compared with the target: objects and components
// added, gone or changed, animator layers and parameters, menu entries, where each accessory bone
// landed, and which animation curves reach the accessory or the avatar's own meshes. Nothing is
// saved: a new scene, the target's prefab only read.
//
// -executeMethod AvatarBridge.Regression.AccessoryStandInProbe.Run -apTarget <CVR prefab> -apAccessory <prefab>[@<bone path>][|...] -apOut <txt>
// A bone path puts an accessory with no Armature Link where its user would drop it.
// Batch. No -quit: it exits itself.

#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    public static class AccessoryStandInProbe
    {
        const string Folder = "Assets/__AccessoryProbe";

        public static void Run()
        {
            var lines = new List<string>();
            void Say(string s) { lines.Add(s); Debug.Log("[AccessoryProbe] " + s); }
            int code = 1;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var targetAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Arg("-apTarget"));
                var target = targetAsset.GetComponent<CVRAvatar>();
                var standIn = (GameObject) PrefabUtility.InstantiatePrefab(targetAsset);
                PrefabUtility.UnpackPrefabInstance(standIn, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                standIn.name = "AccessoryStandIn";

                var accessories = new List<GameObject>();
                foreach (var spec in Arg("-apAccessory").Split('|'))
                {
                    var parts = spec.Split('@');
                    var a = (GameObject) PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(parts[0]));
                    var parent = parts.Length > 1 ? standIn.transform.Find(parts[1]) ?? throw new Exception("no bone " + parts[1]) : standIn.transform;
                    a.transform.SetParent(parent, false);
                    accessories.Add(a);
                    Say($"accessory {a.name}: {string.Join(", ", a.GetComponentsInChildren<Component>(true).Where(c => c != null).GroupBy(c => c.GetType().Name).Select(g => $"{g.Key} x{g.Count()}"))}");
                }

                // A bare VRChat descriptor: the stand-in brings the rig and meshes, the accessories their features.
                var descriptor = standIn.AddComponent<VRCAvatarDescriptor>();
                descriptor.ViewPosition = target.viewPosition;
                // The SDK's inspector fills these on first draw; added from a script they stay
                // null, and VRCFury's and the converter's controller walks both throw.
                VRCAvatarDescriptor.CustomAnimLayer Layer(VRCAvatarDescriptor.AnimLayerType t) => new VRCAvatarDescriptor.CustomAnimLayer { type = t, isDefault = true };
                descriptor.baseAnimationLayers = new[] { VRCAvatarDescriptor.AnimLayerType.Base, VRCAvatarDescriptor.AnimLayerType.Additive,
                    VRCAvatarDescriptor.AnimLayerType.Gesture, VRCAvatarDescriptor.AnimLayerType.Action, VRCAvatarDescriptor.AnimLayerType.FX }.Select(Layer).ToArray();
                descriptor.specialAnimationLayers = new[] { VRCAvatarDescriptor.AnimLayerType.Sitting, VRCAvatarDescriptor.AnimLayerType.TPose,
                    VRCAvatarDescriptor.AnimLayerType.IKPose }.Select(Layer).ToArray();
                // An empty FX of its own: a default one is the SDK's face layer, which is no accessory's.
                if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Folder.Substring("Assets/".Length));
                descriptor.customizeAnimationLayers = true;
                descriptor.baseAnimationLayers[4].isDefault = false;
                descriptor.baseAnimationLayers[4].animatorController = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/Empty FX.controller");
                var settings = new BridgeSettings { outputFolder = Folder, slimTexturesOnConvert = false };
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var report = BridgeConverter.Convert(descriptor, settings);
                Say($"converted in {clock.Elapsed.TotalSeconds:0} s: " + string.Join(", ", Enum.GetValues(typeof(ReportStatus)).Cast<ReportStatus>().Select(s => $"{s} {report.CountOf(s)}")));
                foreach (var e in report.Entries.Where(e => e.Status == ReportStatus.Error || e.Status == ReportStatus.Warning))
                    Say($"  {e.Status} [{e.Category}] {e.Subject}: {e.Detail}");
                var output = report.ConvertedRoot;
                if (output == null) throw new Exception("no converted root");

                // Objects and components, by path from the root.
                var was = Paths(targetAsset);
                var now = Paths(output);
                var added = now.Keys.Except(was.Keys).OrderBy(p => p).ToList();
                var gone = was.Keys.Except(now.Keys).OrderBy(p => p).ToList();
                Say($"objects: target {was.Count}, output {now.Count}, added {added.Count}, gone {gone.Count}");
                foreach (var p in added) Say($"  + {p}: {now[p]}");
                foreach (var p in gone) Say($"  - {p}: {was[p]}");
                foreach (var p in was.Keys.Intersect(now.Keys).Where(p => was[p] != now[p]).OrderBy(p => p))
                    Say($"  ~ {p}: {was[p]}  =>  {now[p]}");

                // Animator: layers and parameters of the controller CVR ships (overrides, else the slot).
                var (tl, tp) = Layers(target);
                var (ol, op) = Layers(output.GetComponent<CVRAvatar>());
                Say($"output layers: {string.Join(", ", ol)}");
                Say($"output parameters: {string.Join(", ", op)}");
                Say($"layers: target {tl.Count}, output {ol.Count}; new {string.Join(", ", ol.Except(tl))}; gone {string.Join(", ", tl.Except(ol))}");
                Say($"parameters: target {tp.Count}, output {op.Count}; new {string.Join(", ", op.Except(tp))}; gone {string.Join(", ", tp.Except(op))}");

                // Menu entries.
                var tm = Menu(target);
                var om = Menu(output.GetComponent<CVRAvatar>());
                Say($"menu: target {tm.Count}, output {om.Count}; new {string.Join(", ", om.Except(tm))}; gone {string.Join(", ", tm.Except(om))}");

                // Where each accessory bone landed, by name.
                foreach (var a in accessories)
                    foreach (var t in a.GetComponentsInChildren<Transform>(true).Where(t => t != a.transform))
                    {
                        var hits = output.GetComponentsInChildren<Transform>(true).Where(x => x.name == t.name).Select(x => Path(output.transform, x)).ToList();
                        if (hits.Count != 1 || !hits[0].StartsWith(a.name + "/")) Say($"  bone {t.name}: {(hits.Count == 0 ? "gone" : string.Join(" | ", hits))}");
                    }

                // Curves in the shipped controller that reach the accessories or the avatar's own meshes.
                var controller = Controller(output.GetComponent<CVRAvatar>());
                if (controller != null)
                    foreach (var clip in controller.animationClips.Where(c => c != null).Distinct())
                    {
                        var bound = AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)).ToList();
                        var onAccessory = bound.Where(b => accessories.Any(a => b.path.Contains(a.name))).ToList();
                        if (onAccessory.Count > 0 || clip.name.IndexOf("jacket", StringComparison.OrdinalIgnoreCase) >= 0)
                            Say($"  clip {clip.name}: {bound.Count} curves, {onAccessory.Count} on accessories; {string.Join(", ", bound.Select(b => $"{b.path}.{b.propertyName}").Distinct().Take(8))}");
                    }
                code = 0;
            }
            catch (Exception e)
            {
                Say("FAIL: " + e);
            }
            File.WriteAllLines(Arg("-apOut"), lines);
            EditorApplication.Exit(code);
        }

        static Dictionary<string, string> Paths(GameObject root) =>
            root.GetComponentsInChildren<Transform>(true).GroupBy(t => Path(root.transform, t)).ToDictionary(g => g.Key,
                g => string.Join(" ", g.First().GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name).OrderBy(n => n)));

        static string Path(Transform root, Transform t) => t == root ? "." : AnimationUtility.CalculateTransformPath(t, root);

        static AnimatorController Controller(CVRAvatar avatar)
        {
            var c = avatar.overrides != null ? avatar.overrides.runtimeAnimatorController : avatar.GetComponent<Animator>()?.runtimeAnimatorController;
            return c as AnimatorController;
        }

        static (List<string> layers, List<string> parameters) Layers(CVRAvatar avatar)
        {
            var c = Controller(avatar);
            return c == null ? (new List<string>(), new List<string>())
                : (c.layers.Select(l => l.name).ToList(), c.parameters.Select(p => $"{p.name}:{p.type}").ToList());
        }

        static List<string> Menu(CVRAvatar avatar) =>
            avatar.avatarSettings?.settings?.Select(s => $"{s.name} ({s.machineName}, {s.type})").ToList() ?? new List<string>();

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new Exception("missing " + name);
        }
    }
}
#endif
