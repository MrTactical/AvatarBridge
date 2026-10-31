#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Known-answer test for the face letting go when tracking turns off (audit FT-02).
    //
    // The bundled rig's off states are empty, Write Defaults on. ChilloutVR writes nothing back
    // when a state stops animating a property, so turning Face or Eye Tracking off left the face in
    // its last tracked expression. Each off state must play the face mesh's own rest values, read
    // from the mesh rather than assumed 0, and then hand over to an empty state, because the rig
    // sits above the avatar's own layers: rest values held there would cover every expression that
    // shares a shape.
    public static class FaceTrackingOffResetTest
    {
        const string Face = "Body";
        const string Prefix = "blendShape.";

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Face lets go when tracking turns off")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[FaceTrackingOffResetTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            const string dir = "Assets/__FaceTrackingOffResetTest";
            AssetDatabase.DeleteAsset(dir);
            AssetDatabase.CreateFolder("Assets", "__FaceTrackingOffResetTest");

            GameObject avatar = null;
            Mesh mesh = null;
            try
            {
                var rig = FaceTrackingPackages.LoadController();
                fail += Check("the bundled rig is in the project", rig != null);
                if (rig == null) return;

                // What the rig's own layers expect: an empty default state beside states that drive
                // face shapes. Read from the rig, so a rig update moves the expectation with it.
                var expect = new Dictionary<string, HashSet<string>>();
                foreach (var layer in rig.layers)
                {
                    var machine = layer.stateMachine;
                    if (machine.defaultState == null || machine.defaultState.motion != null) continue;
                    var shapes = new HashSet<string>(machine.states
                        .Where(s => s.state != machine.defaultState)
                        .SelectMany(s => Clips(s.state.motion))
                        .SelectMany(Shapes));
                    if (shapes.Count > 0) expect["[FT] " + layer.name] = shapes;
                }
                fail += Check($"the rig has off states to reset ({expect.Count})", expect.Count > 0);

                // A face mesh with every shape the rig drives; one rests away from 0.
                var all = new SortedSet<string>(expect.Values.SelectMany(s => s));
                mesh = new Mesh
                {
                    vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                    triangles = new[] { 0, 1, 2 }
                };
                foreach (string shape in all)
                {
                    mesh.AddBlendShapeFrame(shape, 100f, new Vector3[3], null, null);
                }
                avatar = new GameObject("__FaceTrackingOffResetTest");
                var body = new GameObject(Face);
                body.transform.SetParent(avatar.transform, false);
                var smr = body.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                string resting = all.First();
                smr.SetBlendShapeWeight(mesh.GetBlendShapeIndex(resting), 30f);

                var controller = AnimatorController.CreateAnimatorControllerAtPath(dir + "/test.controller");
                var ctx = new BridgeContext
                {
                    Target = avatar,
                    Report = new BridgeReport(),
                    Settings = new BridgeSettings { faceTrackingMode = FaceTrackingMode.DragonSkyRunner },
                    MergedController = controller,
                };
                FaceTrackingInjector.Inject(controller, ctx);

                foreach (var pair in expect)
                {
                    var layer = controller.layers.FirstOrDefault(l => l.name == pair.Key);
                    fail += Check($"{pair.Key}: injected", layer != null);
                    if (layer == null) continue;
                    var off = layer.stateMachine.defaultState;
                    var clip = off.motion as AnimationClip;
                    fail += Check($"{pair.Key}: \"{off.name}\" plays a clip", clip != null);
                    if (clip == null) continue;

                    var written = AnimationUtility.GetCurveBindings(clip)
                        .Where(b => b.path == Face && b.propertyName.StartsWith(Prefix))
                        .ToDictionary(b => b.propertyName.Substring(Prefix.Length),
                            b => AnimationUtility.GetEditorCurve(clip, b).Evaluate(0f));
                    var missing = pair.Value.Where(s => !written.ContainsKey(s)).ToList();
                    fail += Check($"{pair.Key}: it writes every shape the layer drives ({missing.Count} missing)",
                        missing.Count == 0);
                    var wrong = written.Where(kv => !Mathf.Approximately(kv.Value, kv.Key == resting ? 30f : 0f)).ToList();
                    fail += Check($"{pair.Key}: each at the mesh's own rest value ({wrong.Count} wrong)", wrong.Count == 0);

                    // The hand-over: after the clip, an empty state that leaves the same way.
                    var handOver = off.transitions.FirstOrDefault(t => t.hasExitTime && t.conditions.Length == 0);
                    var hold = handOver?.destinationState;
                    fail += Check($"{pair.Key}: it hands over to an empty state",
                        hold != null && hold != off && hold.motion == null);
                    if (hold == null) continue;
                    string Leaves(AnimatorState s) => string.Join(";", s.transitions
                        .Where(t => t != handOver)
                        .Select(t => t.destinationState?.name + ":" +
                                     string.Join(",", t.conditions.Select(c => $"{c.parameter}{c.mode}{c.threshold}")))
                        .OrderBy(x => x));
                    fail += Check($"{pair.Key}: the empty state leaves as \"{off.name}\" does ({Leaves(off)})",
                        Leaves(off).Length > 0 && Leaves(hold) == Leaves(off));
                }
            }
            finally
            {
                if (avatar != null) Object.DestroyImmediate(avatar);
                if (mesh != null) Object.DestroyImmediate(mesh);
                AssetDatabase.DeleteAsset(dir);

                Debug.Log(fail == 0
                    ? "[FaceTrackingOffResetTest] PASS: turning tracking off puts the face back once, then lets go."
                    : $"[FaceTrackingOffResetTest] FAIL: {fail} case(s) wrong.");
                if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
            }
        }

        static IEnumerable<AnimationClip> Clips(Motion motion)
        {
            if (motion is AnimationClip clip) yield return clip;
            if (motion is BlendTree tree)
                foreach (var child in tree.children)
                    foreach (var c in Clips(child.motion)) yield return c;
        }

        static IEnumerable<string> Shapes(AnimationClip clip) => AnimationUtility.GetCurveBindings(clip)
            .Where(b => b.path == Face && b.propertyName.StartsWith(Prefix))
            .Select(b => b.propertyName.Substring(Prefix.Length));
    }
}
#endif
