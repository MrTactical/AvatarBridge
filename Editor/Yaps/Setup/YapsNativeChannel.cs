// The contact channel on an avatar the toolkit built. No longer built:
// Build only takes out what an older version left. A plug finds a socket
// through the screen atlas, with marker lights where the atlas cannot
// answer.
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

        // Takes out any channel an earlier build left, and builds none. The
        // plug's shader stopped reading it: a contact only clears on exit, and
        // a socket deleted or switched off inside one never exits, so the plug
        // stayed bent toward nothing. Returns the lines for the build log.
        public static List<string> Build(CVRAvatar avatar)
        {
            var lines = new List<string>();
            if (avatar == null) return lines;
            int cleared = Clear(avatar);
            if (cleared > 0)
            {
                lines.Add($"✓ the old contact channel taken out ({cleared} object(s), layer(s) and parameter(s)): "
                          + "plugs find sockets through the screen atlas and marker lights, and its synced "
                          + "parameters are free again");
            }

            // A socket-only avatar carries the id too, so a plug elsewhere
            // knows as a fact the socket is not its own.
            string owner = YapsOwner.Wire(avatar);
            if (owner != null) lines.Add("✓ " + owner);
            // After the bakes, like the id: it switches every plug's readout.
            string readout = YapsDebugOverlayBuilder.Menu(avatar, YapsOwner.Shipped(avatar));
            if (readout != null) lines.Add("✓ " + readout);
            return lines;
        }

        // Clean up leftovers asks for this when a plug has gone by hand:
        // the hosts, the drivers, the layers and the parameters, wherever
        // the avatar keeps its controller. Returns how much it took.
        public static int Clear(CVRAvatar avatar)
        {
            if (avatar == null) return 0;
            var animator = avatar.GetComponent<Animator>();
            var controller = BridgeContext.Underlying(animator != null ? animator.runtimeAnimatorController : null);
            int before = Count(avatar, controller);
            Clear(avatar, controller);
            return before - Count(avatar, controller);
        }

        static int Count(CVRAvatar avatar, AnimatorController controller)
        {
            int n = avatar.GetComponentsInChildren<Transform>(true)
                .Count(t => t != null && (t.name.StartsWith("YAPS Channel ") || t.name.StartsWith("YAPS Driver ")));
            if (controller != null)
            {
                n += controller.layers.Count(l => Mine.IsMatch(l.name));
                n += controller.parameters.Count(p => Mine.IsMatch(p.name));
            }
            return n;
        }

        // What a previous build wired, and nothing else. Drivers are only
        // taken when everything they carry is the channel's own: an avatar
        // may have drivers of its own, which are not the toolkit's to delete.
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

            // Sweep reaches here with no controller when the avatar has none.
            if (controller == null) return;
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
