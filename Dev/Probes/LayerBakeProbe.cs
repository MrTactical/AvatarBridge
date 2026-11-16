// Which playable layers does VRCFury's bake hand back, default or filled?
//
// An avatar whose Additive layer is left at VRChat's default lost the stock idle in conversion
// while a sibling avatar with the same default kept it. This prints each descriptor's layers
// before and after the same bake the converter runs.
//
//   -executeMethod AvatarBridge.Regression.LayerBakeProbe.Run -lbScenes "a.unity;b.unity" -lbOut <file>
#if CVR_CCK_EXISTS && VRC_SDK_VRCSDK3
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    public static class LayerBakeProbe
    {
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string key)
            {
                int i = Array.IndexOf(args, key);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            string output = Arg("-lbOut");
            var sb = new StringBuilder();
            try
            {
                foreach (string path in (Arg("-lbScenes") ?? "").Split(';').Where(p => p.Trim().Length > 0))
                {
                    var scene = EditorSceneManager.OpenScene(path.Trim(), OpenSceneMode.Single);
                    var descriptor = scene.GetRootGameObjects()
                        .SelectMany(g => g.GetComponentsInChildren<VRCAvatarDescriptor>(true))
                        .FirstOrDefault(d => d.gameObject.activeInHierarchy)
                        ?? scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<VRCAvatarDescriptor>(true)).FirstOrDefault();
                    sb.Append("== ").Append(path).Append(" : ").Append(descriptor == null ? "no descriptor" : descriptor.name)
                      .Append(descriptor != null && PrefabUtility.IsPartOfPrefabInstance(descriptor) ? " (prefab instance)" : "").Append('\n');
                    if (descriptor == null) continue;
                    Dump("source", descriptor, sb);
                    var report = new BridgeReport();
                    var baked = VRCFuryBaker.TryBake(descriptor, report);
                    var bakedDescriptor = baked != null ? baked.GetComponentInChildren<VRCAvatarDescriptor>(true) : null;
                    sb.Append("  bake returned ").Append(baked == null ? "null" : baked.name).Append('\n');
                    if (bakedDescriptor != null) Dump("baked", bakedDescriptor, sb);
                    foreach (var e in report.Entries.Take(20)) sb.Append("  report: ").Append(e.Subject).Append(" | ").Append(e.Detail).Append('\n');
                    File.WriteAllText(output, sb.ToString());
                }
            }
            catch (Exception e)
            {
                sb.Append("THREW ").Append(e).Append('\n');
            }
            File.WriteAllText(output, sb.ToString());
            EditorApplication.Exit(0);
        }

        static void Dump(string label, VRCAvatarDescriptor d, StringBuilder sb)
        {
            sb.Append("  ").Append(label).Append(" customizeAnimationLayers=").Append(d.customizeAnimationLayers).Append('\n');
            foreach (var layer in d.baseAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0])
            {
                var controller = layer.animatorController as AnimatorController;
                sb.Append("    ").Append(layer.type).Append(" isDefault=").Append(layer.isDefault)
                  .Append(" controller=").Append(layer.animatorController == null ? "none" : layer.animatorController.name)
                  .Append(controller == null ? "" : " [" + string.Join(", ", controller.layers.Select(l => l.name)) + "]")
                  .Append('\n');
            }
        }
    }
}
#endif
