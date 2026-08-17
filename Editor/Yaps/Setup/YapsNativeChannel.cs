// The contact channel on an avatar the toolkit built, rather than one a
// conversion produced. A plug finds a socket by the channel first and by
// marker lights second, so without this a native socket has only its
// lights, and an avatar that turns them off has nothing at all.
// Same builder as the conversion: this fills in the site it works from.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsNativeChannel
    {
        // The channel's own layers and parameters, told apart from a
        // socket's reactions and lights layers by the digit: it writes
        // "YAPS0E publish", they write "YAPS <label> reactions".
        static readonly Regex Mine = new Regex(@"^#?YAPS\d");

        // Builds the channel for every baked plug on the avatar, replacing
        // what a previous run left. Returns the lines for the build log.
        public static List<string> Build(CVRAvatar avatar)
        {
            var lines = new List<string>();
            if (avatar == null) return lines;

            var animator = avatar.GetComponent<Animator>();
            var controller = YapsSocketReactions.Underlying(animator != null ? animator.runtimeAnimatorController : null);
            if (animator == null || controller == null)
            {
                lines.Add("✗ the channel needs an Animator with a controller on the avatar");
                return lines;
            }

            var plugs = Plugs(avatar);
            Clear(avatar, controller);
            if (plugs.Count == 0)
            {
                return lines;
            }

            var report = new BridgeReport();
            YapsChannel.Run(new YapsChannel.Site
            {
                Target = avatar.gameObject,
                Animator = animator,
                Controller = controller,
                Report = report,
                Plugs = plugs,
                Follow = BridgeSettings.DefaultSocketFollow,
                PathIn = t => AnimationUtility.CalculateTransformPath(t, avatar.transform),
            });
            EditorUtility.SetDirty(avatar.gameObject);
            AssetDatabase.SaveAssets();

            foreach (var entry in report.Entries)
            {
                lines.Add((entry.Status == ReportStatus.Converted ? "✓ " : "! ") + entry.Subject);
            }
            return lines;
        }

        // Every plug the channel can carry: baked, with a material of its
        // own. The frame comes from the markers the bake left, so a trigger
        // box sits on the shaft rather than on the object.
        static List<BridgeContext.YapsPlug> Plugs(CVRAvatar avatar)
        {
            var found = new List<BridgeContext.YapsPlug>();
            foreach (var plug in avatar.GetComponentsInChildren<YapsPlug>(true))
            {
                var renderer = plug != null ? plug.Target : null;
                if (renderer == null) continue;
                var mats = renderer.sharedMaterials;
                int slot = -1;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (plug.materialSlot >= 0 && i != plug.materialSlot) continue;
                    if (mats[i] != null && mats[i].HasProperty("_YAPS_Bake")
                        && mats[i].GetTexture("_YAPS_Bake") != null) { slot = i; break; }
                }
                if (slot < 0) continue;

                if (!YapsPreview.PlugFrame(plug, out var origin, out var forward,
                        out var up, out float length))
                {
                    continue;
                }
                found.Add(new BridgeContext.YapsPlug
                {
                    Root = plug.transform,
                    Renderer = renderer,
                    Material = mats[slot],
                    MaterialSlot = slot,
                    Length = length,
                    Origin = origin,
                    Rotation = Quaternion.LookRotation(forward, up),
                    Radius = length * 0.12f,
                });
            }
            return found;
        }

        // What a previous build wired, and nothing else. Drivers are only
        // taken when everything they carry is the channel's own: an avatar
        // may have drivers of its own and they are not ours to delete.
        static void Clear(CVRAvatar avatar, AnimatorController controller)
        {
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true).ToList())
            {
                if (t == null) continue;
                if (t.name.StartsWith("YAPS Channel ") || t.name.StartsWith("YAPS Driver "))
                {
                    Undo.DestroyObjectImmediate(t.gameObject);
                }
            }
            foreach (var driver in avatar.GetComponentsInChildren<CVRAnimatorDriver>(true).ToList())
            {
                if (driver == null) continue;
                if (driver.animatorParameters.Count == 0
                    || driver.animatorParameters.All(p => p != null && Mine.IsMatch(p)))
                {
                    Undo.DestroyObjectImmediate(driver);
                }
            }
            foreach (var driver in avatar.GetComponentsInChildren<CVRMaterialDriver>(true).ToList())
            {
                if (driver == null) continue;
                if (driver.tasks.Count == 0
                    || driver.tasks.All(t => t != null && t.PropertyName != null
                                             && t.PropertyName.StartsWith("_YAPS_")))
                {
                    Undo.DestroyObjectImmediate(driver);
                }
            }

            var layers = controller.layers.Where(l => !Mine.IsMatch(l.name)).ToArray();
            if (layers.Length != controller.layers.Length) controller.layers = layers;
            foreach (var p in controller.parameters.Where(p => Mine.IsMatch(p.name)).ToList())
            {
                controller.RemoveParameter(p);
            }
        }
    }
}
#endif
