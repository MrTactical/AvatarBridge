#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A portal through every door that touches it: the inspector's
    // markers-only build, a socket added ahead of the entrance, a rebuild,
    // the converter's own socket rebuild, a retarget with and without a
    // rebuild, refusals, the owner layer, Remove on each end and what it
    // says first, and the sweep of a writer whose entrance was deleted by
    // hand. Editor only; what the exit publishes in game is not reachable
    // from here.
    public static class PortalRoundTripTest
    {
        const string Tag = "[PortalRoundTripTest]";
        // At the root: Assets/YAPS is only made by the first build, after this.
        const string ControllerPath = "Assets/__PortalRoundTripTest.controller";
        static int failures;

        [MenuItem("Tools/AvatarBridge Dev/Test: portal round trip")]
        public static void Run()
        {
            failures = 0;
            AssetDatabase.DeleteAsset(ControllerPath);
            var root = new GameObject("PortalTestAvatar");
            try
            {
                var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
                root.AddComponent<Animator>().runtimeAnimatorController = controller;
                var cvr = root.AddComponent<CVRAvatar>();
                var entrance = Socket(root, "B Entrance");
                var exit = Socket(root, "C Exit");
                var other = Socket(root, "D Other");
                foreach (var s in new[] { entrance, exit, other }) YapsSocketBuilder.Build(s);

                // What the inspector's Rebuild markers and the window's upgrade
                // run, with no Wire of the test's own after it.
                entrance.portalExit = exit;
                YapsNativeBuilder.RebuildMarkers(entrance);
                string path = "C Exit/Atlas Exit (from B Entrance)";
                Check(OwnerCurve(controller, path), $"a markers-only build wires the owner layer to \"{path}\"");
                var writer = entrance.builtExit;
                Check(writer != null && writer.transform.parent == exit.transform
                      && writer.name == "Atlas Exit (from B Entrance)", $"the writer sits under the exit (got {Where(writer)})");
                Check(Key(writer) == Number(root, entrance) && Key(writer) == 1,
                    $"keyed by the entrance's number (key {Key(writer)}, number {Number(root, entrance)})");
                Check(YapsSocketBuilder.PortalNote(entrance)?.StartsWith("✓") == true, $"the note says it works: {YapsSocketBuilder.PortalNote(entrance)}");

                // Sorted ahead of the entrance, so every number after it moves.
                var first = Socket(root, "A First");
                YapsSocketBuilder.Build(first);
                Check(Number(root, entrance) == 2 && Key(entrance.builtExit) == 2,
                    $"a socket added ahead renumbers and the key follows (key {Key(entrance.builtExit)}, number {Number(root, entrance)})");

                // A second Build.
                YapsSocketBuilder.Build(entrance);
                YapsOwner.Wire(root, controller);
                int writers = Writers(exit);
                Check(writers == 1 && Key(entrance.builtExit) == 2, $"a rebuild replaces rather than stacks ({writers} writers, key {Key(entrance.builtExit)})");
                Check(OwnerCurve(controller, path), "and the owner layer still reaches it");

                // A reconvert: the converter's socket rebuild over sockets that
                // already carry a portal, then its Wire, in the converter's order.
                var ctx = new BridgeContext
                {
                    Settings = new BridgeSettings(), Report = new BridgeReport(),
                    Target = root, CvrAvatar = cvr, MergedController = controller
                };
                YapsSocketRebuilder.Finish(ctx, Specs(true, entrance, exit, other, first));
                YapsOwner.Wire(ctx.Target, ctx.MergedController);
                writers = Writers(exit);
                Check(writers == 1 && Key(entrance.builtExit) == 2 && OwnerCurve(controller, path),
                    $"a reconvert keeps one keyed, wired writer ({writers} writers, key {Key(entrance.builtExit)})");
                // The converter sets each kind from the rig it finds; a ring cannot cut.
                YapsSocketRebuilder.Finish(ctx, Specs(false, entrance));
                YapsOwner.Wire(ctx.Target, ctx.MergedController);
                Check(Writers(exit) == 0 && entrance.builtExit == null
                      && YapsSocketBuilder.PortalNote(entrance)?.StartsWith("⚠ a ring") == true,
                    $"a reconvert that makes the entrance a ring leaves no exit behind ({Writers(exit)} writers): {YapsSocketBuilder.PortalNote(entrance)}");
                entrance.kind = YapsSocket.SocketKind.Hole;
                YapsSocketBuilder.Build(entrance);
                Check(Writers(exit) == 1, "and a hole again builds it again");

                // Retargeted by a script that never rebuilt: the old writer
                // stops publishing and the card stops saying it works.
                var stale = entrance.builtExit;
                entrance.portalExit = other;
                YapsOwner.NumberWriters(root, true);
                Check(Key(stale) == 0 && YapsSocketBuilder.PortalNote(entrance)?.StartsWith("⚠ the portal is not built") == true,
                    $"retargeted unbuilt, the old writer is keyed 0 (key {Key(stale)}): {YapsSocketBuilder.PortalNote(entrance)}");
                YapsSocketBuilder.Build(entrance);
                Check(exit.transform.Cast<Transform>().All(t => !YapsSocketBuilder.IsExitWriter(t))
                      && entrance.builtExit != null && entrance.builtExit.transform.parent == other.transform,
                    $"retargeted, the old exit loses its writer (now {Where(entrance.builtExit)})");

                entrance.portalExit = entrance;
                YapsSocketBuilder.Build(entrance);
                Check(entrance.builtExit == null && YapsSocketBuilder.PortalNote(entrance)?.StartsWith("⚠") == true,
                    $"an entrance that is its own exit is refused: {YapsSocketBuilder.PortalNote(entrance)}");

                // Deleted by hand: the writer it left is inert at once and
                // gone after the sweep.
                entrance.portalExit = exit;
                YapsSocketBuilder.Build(entrance);
                var left = entrance.builtExit;
                Object.DestroyImmediate(entrance.gameObject);
                YapsOwner.NumberWriters(root, true);
                Check(left != null && Key(left) == 0, $"a writer with no entrance is keyed 0 (key {Key(left)})");
                YapsRemover.Sweep(root.transform);
                Check(left == null, "and the sweep takes it out");

                var second = Socket(root, "B Second");
                second.portalExit = exit;
                YapsSocketBuilder.Build(second);
                Check(second.builtExit != null, "a second entrance builds");
                var said = YapsRemover.Plan(second);
                Check(said.Any(l => l.Contains("portal writer under \"C Exit\"")), "Remove on the entrance says it takes the writer: " + string.Join(" | ", said));
                said = YapsRemover.Plan(exit);
                Check(said.Any(l => l.Contains("Atlas Exit (from B Second)")), "Remove on the exit says it takes the writer: " + string.Join(" | ", said));
                YapsRemover.RemoveSocket(second);
                Check(exit.transform.Cast<Transform>().All(t => !YapsSocketBuilder.IsExitWriter(t)), "Remove on the entrance takes its writer");

                var third = Socket(root, "B Third");
                third.portalExit = exit;
                YapsSocketBuilder.Build(third);
                var thirdWriter = third.builtExit;
                YapsRemover.RemoveSocket(exit);
                Check(thirdWriter == null, "Remove on the exit takes the writers under it");
            }
            finally
            {
                Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(ControllerPath);
            }
            Debug.Log(failures == 0 ? $"{Tag} PASS" : $"{Tag} FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static YapsSocket Socket(GameObject root, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var s = go.AddComponent<YapsSocket>();
            s.kind = YapsSocket.SocketKind.Hole;
            s.emitLights = false;
            return s;
        }

        static Dictionary<Transform, YapsSocketRebuilder.Spec> Specs(bool hole, params YapsSocket[] sockets) =>
            sockets.ToDictionary(s => s.transform, s => new YapsSocketRebuilder.Spec { Root = s.transform, IsHole = hole });

        static int Writers(YapsSocket exit) => exit.transform.Cast<Transform>().Count(t => YapsSocketBuilder.IsExitWriter(t));

        static int Number(GameObject root, YapsSocket s) =>
            YapsOwner.NumberWriters(root, true).TryGetValue(s, out int n) ? n : -1;

        static int Key(GameObject writer)
        {
            var r = writer != null ? writer.GetComponent<MeshRenderer>() : null;
            return r != null && r.sharedMaterial != null ? Mathf.RoundToInt(r.sharedMaterial.GetFloat("_YAPS_EntranceIndex")) : -1;
        }

        static string Where(GameObject writer) =>
            writer == null ? "none" : writer.transform.parent.name + "/" + writer.name;

        static bool OwnerCurve(AnimatorController controller, string path)
        {
            var layer = controller.layers.FirstOrDefault(l => l.name == "YAPS owner");
            var tree = layer?.stateMachine?.defaultState?.motion as BlendTree;
            var clip = tree?.children.Select(c => c.motion).OfType<AnimationClip>().FirstOrDefault();
            return clip != null && AnimationUtility.GetCurveBindings(clip)
                .Any(b => b.path == path && b.propertyName == "material._YAPS_Owner");
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"{Tag} {(ok ? "PASS" : "FAIL")} {what}");
        }
    }
}
#endif
