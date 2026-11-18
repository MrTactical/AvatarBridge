// Takes out the contact channel older versions built; plugs find sockets
// through the atlas and marker lights. Every plug bake ends here, so the
// avatar-wide wiring lives here too: owner id, tag choosers, readout toggle,
// and the check that every curve binds.
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

        // Builds none: a contact clears only on exit, so a socket switched off
        // inside one left the plug bent toward nothing. Returns log lines.
        public static List<string> Build(CVRAvatar avatar)
        {
            var lines = new List<string>();
            if (avatar == null) return lines;
            int cleared = Clear(avatar);
            if (cleared > 0)
            {
                lines.Add($"✓ the old contact channel taken out ({BridgeElements.Count(cleared, "object, layer or parameter", "objects, layers and parameters")}): "
                          + "plugs find sockets through the screen atlas and marker lights, and its synced "
                          + "parameters are free again");
            }

            // A socket-only avatar carries the id too, so a plug elsewhere
            // knows as a fact the socket is not its own.
            string owner = YapsOwner.Wire(avatar);
            if (owner != null) lines.Add("✓ " + owner);
            // After the bakes, so plug-only avatars and inspector bakes get one.
            var dead = new List<string>();
            foreach (var controller in YapsOwner.Targets(avatar))
            {
                string tags = YapsTagMenu.Build(avatar, controller);
                if (tags != null && !lines.Contains("✓ " + tags)) lines.Add("✓ " + tags);
                string readout = YapsDebugOverlayBuilder.Menu(avatar, controller);
                if (readout != null && !lines.Contains("✓ " + readout)) lines.Add("✓ " + readout);
                dead.AddRange(YapsToggles.DeadBindings(avatar.gameObject, controller));
            }
            foreach (string d in dead.Distinct())
            {
                lines.Add("✗ a curve that changes nothing, Unity cannot attach it: " + d);
            }
            return lines;
        }

        // Clean up leftovers asks for this when a plug has gone by hand:
        // the hosts, the drivers, the layers and the parameters, wherever
        // the avatar keeps its controller. Returns how much it took.
        public static int Clear(CVRAvatar avatar)
        {
            if (avatar == null) return 0;
            // The shipped controllers plus the Animator's slot older builds used.
            var animator = avatar.GetComponent<Animator>();
            var legacy = BridgeContext.Underlying(animator != null ? animator.runtimeAnimatorController : null);
            var controllers = YapsOwner.Targets(avatar);
            if (legacy != null && !controllers.Contains(legacy)) controllers.Add(legacy);
            int before = Count(avatar, controllers);
            Clear(avatar, controllers);
            return before - Count(avatar, controllers);
        }

        static bool Host(Transform t) => t.name.StartsWith("YAPS Channel ") || t.name.StartsWith("YAPS Driver ");

        // Only a driver carrying the channel's own values. An empty one is the
        // avatar's, and deleting it went unreported.
        static bool ChannelDriver(CVRAnimatorDriver d) => d != null && d.animatorParameters.Count > 0
            && d.animatorParameters.All(p => p != null && Mine.IsMatch(p));

        static bool ChannelDriver(CVRMaterialDriver d) => d != null && d.tasks.Count > 0
            && d.tasks.All(t => t != null && t.PropertyName != null && t.PropertyName.StartsWith("_YAPS_"));

        static int Count(CVRAvatar avatar, List<AnimatorController> controllers)
        {
            int n = avatar.GetComponentsInChildren<Transform>(true).Count(t => t != null && Host(t));
            // A driver on a host goes with it and is counted there; one on the
            // avatar root, where older builds put the material driver, was not.
            n += avatar.GetComponentsInChildren<CVRAnimatorDriver>(true).Count(d => ChannelDriver(d) && !Host(d.transform));
            n += avatar.GetComponentsInChildren<CVRMaterialDriver>(true).Count(d => ChannelDriver(d) && !Host(d.transform));
            foreach (var controller in controllers)
            {
                n += controller.layers.Count(l => Mine.IsMatch(l.name));
                n += controller.parameters.Count(p => Mine.IsMatch(p.name));
            }
            return n;
        }

        // Drivers only when everything they carry is the channel's own.
        static void Clear(CVRAvatar avatar, List<AnimatorController> controllers)
        {
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true).ToList())
            {
                if (t != null && Host(t)) Undo.DestroyObjectImmediate(t.gameObject);
            }
            foreach (var driver in avatar.GetComponentsInChildren<CVRAnimatorDriver>(true).ToList())
            {
                if (ChannelDriver(driver)) Undo.DestroyObjectImmediate(driver);
            }
            foreach (var driver in avatar.GetComponentsInChildren<CVRMaterialDriver>(true).ToList())
            {
                if (ChannelDriver(driver)) Undo.DestroyObjectImmediate(driver);
            }

            foreach (var controller in controllers)
            {
                var layers = controller.layers.Where(l => !Mine.IsMatch(l.name)).ToArray();
                var parameters = controller.parameters.Where(p => Mine.IsMatch(p.name)).ToList();
                if (layers.Length == controller.layers.Length && parameters.Count == 0) continue;
                if (layers.Length != controller.layers.Length)
                {
                    var old = YapsOwner.Embedded(controller,
                        controller.layers.Where(l => Mine.IsMatch(l.name)).Select(l => l.stateMachine));
                    controller.layers = layers;
                    YapsOwner.DropUnreached(controller, old);
                }
                foreach (var p in parameters) controller.RemoveParameter(p);
                EditorUtility.SetDirty(controller);
            }
        }
    }
}
#endif
