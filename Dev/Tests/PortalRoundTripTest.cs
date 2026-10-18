#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A portal through every door that touches it: build, a socket added
    // ahead of the entrance, a rebuild, a retarget, a refusal, the owner
    // layer, Remove on each end, and the sweep of a writer whose entrance
    // was deleted by hand. Editor only; what the exit publishes in game is
    // not reachable from here.
    public static class PortalRoundTripTest
    {
        const string Tag = "[PortalRoundTripTest]";
        const string ControllerPath = "Assets/YAPS/PortalRoundTripTest.controller";
        static int failures;

        [MenuItem("Tools/AvatarBridge Dev/Test: portal round trip")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("PortalTestAvatar");
            try
            {
                root.AddComponent<Animator>();
                root.AddComponent<CVRAvatar>();
                var entrance = Socket(root, "B Entrance");
                var exit = Socket(root, "C Exit");
                var other = Socket(root, "D Other");
                foreach (var s in new[] { entrance, exit, other }) YapsSocketBuilder.Build(s);

                entrance.portalExit = exit;
                YapsSocketBuilder.Build(entrance);
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

                var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
                string wired = YapsOwner.Wire(root, controller);
                string path = "C Exit/Atlas Exit (from B Entrance)";
                Check(OwnerCurve(controller, path), $"the owner layer reaches the writer at \"{path}\" ({wired})");

                // A rebuild, as a reconvert or a second Build runs it.
                YapsSocketBuilder.Build(entrance);
                YapsOwner.Wire(root, controller);
                int writers = exit.transform.Cast<Transform>().Count(t => YapsSocketBuilder.IsExitWriter(t));
                Check(writers == 1 && Key(entrance.builtExit) == 2, $"a rebuild replaces rather than stacks ({writers} writers, key {Key(entrance.builtExit)})");
                Check(OwnerCurve(controller, path), "and the owner layer still reaches it");

                entrance.portalExit = other;
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
