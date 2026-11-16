#if CVR_CCK_EXISTS
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // An avatar whose armature moved after its rig was made and whose Jaw is mapped to a bone
    // that is not a jaw: the skeleton pass saves a _Matched rig, then the jaw pass rebuilds from
    // it and saves _NoJaw. The _Matched copy was left in the output, unused.
    //
    // Run: -executeMethod AvatarBridge.Regression.JawMatchedRigTest.Run [-jawPrefab <humanoid prefab>]
    public static class JawMatchedRigTest
    {
        const string Dir = "Assets/JawMatchedRigTest";
        const string DefaultPrefab = "Assets/AvatarBridgeOutput/Ampwave/Ampwave (ChilloutVR).prefab";

        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[JawMatchedRigTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            try
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(args, "-jawPrefab");
                string path = i >= 0 && i + 1 < args.Length ? args[i + 1] : DefaultPrefab;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var avatar = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                PrefabUtility.UnpackPrefabInstance(avatar, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var animator = avatar.GetComponentInChildren<Animator>(true);
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);

                // A rig with its Jaw on a hat, built from the hierarchy as it stands.
                var hat = new GameObject("Hat").transform;
                hat.SetParent(head, false);
                hat.localPosition = new Vector3(0f, 0.1f, 0f);
                var d = animator.avatar.humanDescription;
                d.human = d.human.Where(h => h.humanName != "Jaw")
                    .Append(new HumanBone { humanName = "Jaw", boneName = hat.name, limit = new HumanLimit { useDefaultValues = true } }).ToArray();
                d.skeleton = avatar.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
                    { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
                animator.avatar = null;
                var bad = AvatarBuilder.BuildHumanAvatar(animator.gameObject, d);
                bad.name = "TestRig";

                // Then the armature moves, as a scaled or shifted armature does after import. Moved
                // before the rig is assigned: the Animator writes back the pose it bound to whenever
                // its rig is swapped, so a move made after binding is undone by the passes' detach.
                hips.parent.localPosition += new Vector3(0f, 0.2f, 0f);
                animator.avatar = bad;
                fail += Check("the test rig builds with its Jaw on the hat", bad.isValid && animator.GetBoneTransform(HumanBodyBones.Jaw) == hat);

                Directory.CreateDirectory(Dir);
                AssetDatabase.Refresh();
                var ctx = new BridgeContext { Target = avatar, OutputDir = Dir, Report = new BridgeReport(), Settings = new BridgeSettings() };
                RigSkeletonSync.Run(ctx);
                bool matched = AssetDatabase.FindAssets("t:Avatar", new[] { Dir }).Any(g => AssetDatabase.GUIDToAssetPath(g).Contains("_Matched"));
                fail += Check("the skeleton pass saved a matched rig", matched);
                Debug.Log($"[JawMatchedRigTest]   matched rig hips off {Off(animator.avatar) * 100f:0.00} cm");
                JawUnmapper.Run(ctx);

                var rigs = AssetDatabase.FindAssets("t:Avatar", new[] { Dir }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
                Debug.Log("[JawMatchedRigTest]   rigs: " + string.Join(", ", rigs));
                fail += Check("one rig in the output", rigs.Length == 1);
                fail += Check("and the avatar wears it", rigs.Length > 0 && AssetDatabase.GetAssetPath(animator.avatar) == rigs[0]);
                fail += Check("which has no Jaw", animator.avatar.isValid && animator.avatar.humanDescription.human.All(h => h.humanName != "Jaw"));

                // Stands on it: the rig puts the hips where the armature has them.
                float Off(Avatar rig)
                {
                    var skeleton = rig.humanDescription.skeleton;
                    var at = Matrix4x4.identity;
                    for (var t = hips; t != animator.transform; t = t.parent)
                    {
                        var b = skeleton.Skip(1).First(s => s.name == t.name);
                        at = Matrix4x4.TRS(b.position, b.rotation, b.scale) * at;
                    }
                    return (at.MultiplyPoint3x4(Vector3.zero) - animator.transform.InverseTransformPoint(hips.position)).magnitude;
                }
                float off = Off(animator.avatar);
                fail += Check($"and stands on it (hips {off * 100f:0.00} cm from the armature's)", off < 0.005f);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                fail++;
            }
            finally
            {
                AssetDatabase.DeleteAsset(Dir);
            }
            Debug.Log(fail == 0 ? "[JawMatchedRigTest] PASS" : $"[JawMatchedRigTest] FAIL: {fail}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
