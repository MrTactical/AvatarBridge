#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    // A face-tracking add-on that installs onto a copy of the avatar leaves the original beside
    // it, and the original is the one that got converted: its card and its report said "no
    // shapes", read as the converter losing them. And an avatar carrying its own rig as well as
    // the shapes had that rig replaced by Native with no word said. Shape names come from the
    // converter's own list, so this pins the decisions, not a guess at the naming.
    public static class FaceTrackingAdviceTest
    {
        const string KeepRig = "Keep the avatar's own face-tracking rig";

        [MenuItem("Tools/AvatarBridge Dev/Test: face-tracking advice")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[FaceTrackingAdviceTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Static;
                var names = (string[]) typeof(FaceTrackingConverter).GetMethod("ReadShapeList", hidden).Invoke(null,
                    new[] { "UnifiedShapeNames", typeof(FaceTrackingConverter).GetField("UnifiedFallback", hidden).GetValue(null) });

                var stock = Avatar("__FtAdvice", new string[0]);
                var settings = new BridgeSettings { faceTrackingMode = FaceTrackingMode.Native };
                var alone = Face(stock, settings);
                fail += Check("alone in the scene, the card names no other avatar",
                    alone != null && (alone.Targets == null || alone.Targets.Length == 0));

                var copy = Avatar("__FtAdvice-FaceTracking", names);
                var face = Face(stock, settings);
                fail += Check("an avatar without the shapes names the copy that has them",
                    face != null && face.Finding.Contains(copy.name) && face.Targets != null && face.Targets.Contains(copy));

                var advice = AvatarAdvisor.Analyse(copy.GetComponent<VRCAvatarDescriptor>(), settings);
                fail += Check("shapes and no rig of its own: no keep-the-rig card", advice.All(a => a.Setting != KeepRig));

                // The rig arrives the way an add-on ships it: a merge component, baked in later.
                var merge = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator", false))
                    .FirstOrDefault(t => t != null);
                if (merge == null)
                {
                    fail += Check("Modular Avatar is installed here (the rig is attached through it)", false);
                }
                else
                {
                    var rig = new AnimatorController
                    {
                        parameters = new[] { "FT/v2/JawOpen", "FT/v2/MouthClosed", "Drive/FT/v2/TongueX" }
                            .Select(n => new AnimatorControllerParameter { name = n, type = AnimatorControllerParameterType.Float })
                            .ToArray(),
                    };
                    var host = new GameObject("Face Tracking");
                    host.transform.SetParent(copy.transform);
                    merge.GetField("animator").SetValue(host.AddComponent(merge), rig);

                    advice = AvatarAdvisor.Analyse(copy.GetComponent<VRCAvatarDescriptor>(), settings);
                    var keep = advice.FirstOrDefault(a => a.Setting == KeepRig);
                    fail += Check("its own rig on a merge component: a your-call card offers to keep it",
                        keep != null && keep.Kind == AdviceKind.Manual && keep.Apply != null);
                    fail += Check("...which Apply all leaves alone", keep != null && !AvatarAdvisor.IsRecommendation(keep));
                    fail += Check("...and Native stays confirmed beside it",
                        advice.Any(a => a.Setting == "Face tracking" && a.Kind == AdviceKind.Confirm));

                    keep?.Apply(settings);
                    fail += Check("turning it on picks Keep", settings.faceTrackingMode == FaceTrackingMode.None);

                    advice = AvatarAdvisor.Analyse(copy.GetComponent<VRCAvatarDescriptor>(), settings);
                    fail += Check("with Keep chosen, nothing recommends switching back",
                        advice.Where(a => a.Setting == "Face tracking" || a.Setting == KeepRig).All(a => !AvatarAdvisor.IsRecommendation(a))
                        && advice.Any(a => a.Setting == "Face tracking" && a.Kind == AdviceKind.Confirm));
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                fail++;
            }
            Debug.Log($"[FaceTrackingAdviceTest] {(fail == 0 ? "PASS" : fail + " WRONG")}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        static Advice Face(GameObject avatar, BridgeSettings settings) =>
            AvatarAdvisor.Analyse(avatar.GetComponent<VRCAvatarDescriptor>(), settings).FirstOrDefault(a => a.Setting == "Face tracking");

        static GameObject Avatar(string name, string[] shapes)
        {
            var root = new GameObject(name);
            root.AddComponent<VRCAvatarDescriptor>();
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform);
            var mesh = new Mesh { name = "Body", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            foreach (var shape in shapes)
            {
                mesh.AddBlendShapeFrame(shape, 100, new Vector3[3], null, null);
            }
            body.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
            return root;
        }
    }
}
#endif
