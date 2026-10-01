// The tester's physics card, run headless on a converted avatar in Play
// mode: a push moves a chain, walking swings chains more than standing
// still, Stop puts the avatar back, the overlay switches MagicaCloth's own
// drawing on for every chain and back off, and the walk is a closed circle.
//
// Run: -executeMethod AvatarBridge.Regression.PhysicsTesterProbe.Run -probePrefab "Assets/.../X (ChilloutVR).prefab"
// (no -quit: it exits itself from Play mode)
#if CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System;
using System.Linq;
using System.Reflection;
using ABI.CCK.Components;
using MagicaCloth2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    [InitializeOnLoad]
    public static class PhysicsTesterProbe
    {
        const string Key = "PhysicsTesterProbe.step";
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static int fail;
        static CckAnimatorTester tester;
        static float stepAt, stillSwing, walkSwing;
        static Vector3 rootStart, tipBefore;
        static Quaternion[] restRotations;
        static Transform[] tips;
        static Transform pushedTip;
        static (bool always, bool enable)[] gizmosBefore;

        static PhysicsTesterProbe()
        {
            if (SessionState.GetInt(Key, -1) >= 0) EditorApplication.update += Tick;
        }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string prefab = args[Array.IndexOf(args, "-probePrefab") + 1];
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefab));
            SessionState.SetInt(Key, 0);
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            int step = SessionState.GetInt(Key, 0);
            var avatar = UnityEngine.Object.FindObjectOfType<CVRAvatar>();
            var all = avatar != null ? avatar.GetComponentsInChildren<MagicaCloth>(true) : new MagicaCloth[0];
            // A cloth on something switched off at rest never builds; only running ones can move.
            var cloths = all.Where(c => c.isActiveAndEnabled && c.IsValid()).ToArray();
            try
            {
                switch (step)
                {
                    case 0: // let the cloth build and settle
                        if (Time.time < 1.5f) return;
                        Check(cloths.Length > 0, $"{cloths.Length} of {all.Length} cloth(s) built and running");
                        tester = ScriptableObject.CreateInstance<CckAnimatorTester>();
                        tips = cloths.SelectMany(c => c.SerializeData.rootBones.Where(r => r != null))
                            .Select(r => r.GetComponentsInChildren<Transform>(true).Last()).ToArray();
                        restRotations = tips.Select(t => Local(avatar, t)).ToArray();
                        Next(1);
                        return;
                    case 1: // standing still for a second: the noise floor
                        if (Time.time - stepAt < 1f) return;
                        stillSwing = Swing();
                        // The longest chain, so a push has room to show.
                        var cloth = cloths.OrderByDescending(c => c.SerializeData.rootBones.Where(r => r != null)
                            .Max(r => r.GetComponentsInChildren<Transform>(true).Length)).First();
                        pushedTip = cloth.SerializeData.rootBones.Where(r => r != null)
                            .Select(r => r.GetComponentsInChildren<Transform>(true).Last()).First();
                        tipBefore = pushedTip.position;
                        Check(CckAnimatorTester.Push(cloth, Vector3.right, 3f), $"MagicaCloth takes a push on this version ({cloth.name})");
                        Next(2);
                        return;
                    case 2:
                        if (Time.time - stepAt < 0.25f) return;
                        float moved = Vector3.Distance(pushedTip.position, tipBefore);
                        Check(moved > 0.005f, $"a push moved the chain's tip {moved * 1000f:F0} mm");
                        rootStart = avatar.transform.position;
                        restRotations = tips.Select(t => Local(avatar, t)).ToArray();
                        Call("StartMotion", avatar, CckAnimatorTester.Moves.Walk);
                        Next(3);
                        return;
                    case 3: // walking for two seconds
                        if (Time.time - stepAt < 2f) return;
                        walkSwing = Swing();
                        float away = Vector3.Distance(avatar.transform.position, rootStart);
                        Check(away > 0.3f, $"walking moved the avatar {away:F2} m");
                        Check(walkSwing > Mathf.Max(2f * stillSwing, 1f),
                            $"chains swing more walking ({walkSwing:F1} deg) than standing ({stillSwing:F1} deg)");
                        Call("StopMotion");
                        Check(Vector3.Distance(avatar.transform.position, rootStart) < 1e-3f, "Stop puts the avatar back");
                        gizmosBefore = all.Select(c => (c.GizmoSerializeData.always, c.GizmoSerializeData.clothDebugSettings.enable)).ToArray();
                        Call("SetOverlay", avatar, true);
                        Check(all.All(c => c.GizmoSerializeData.always && c.GizmoSerializeData.clothDebugSettings.enable),
                            "the overlay turns MagicaCloth's drawing on for every chain");
                        Call("SetOverlay", avatar, false);
                        Check(all.Select((c, i) => (c.GizmoSerializeData.always, c.GizmoSerializeData.clothDebugSettings.enable) == gizmosBefore[i]).All(same => same),
                            "and puts each one back as it was");
                        var full = CckAnimatorTester.MotionPose(CckAnimatorTester.Moves.Walk, 2f * Mathf.PI * 0.75f / 1.4f);
                        Check(full.offset.magnitude < 1e-3f && Mathf.Abs(full.yaw - 360f) < 0.1f, "a walk lap closes its circle");
                        Finish();
                        return;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                fail++;
                Finish();
            }
        }

        // The largest turn any chain tip made since the last reading, against
        // the avatar, so the avatar's own turning is not counted as swing.
        static float Swing()
        {
            var avatar = UnityEngine.Object.FindObjectOfType<CVRAvatar>();
            float most = 0f;
            for (int i = 0; i < tips.Length; i++)
            {
                if (tips[i] != null) most = Mathf.Max(most, Quaternion.Angle(restRotations[i], Local(avatar, tips[i])));
            }
            return most;
        }

        static Quaternion Local(CVRAvatar avatar, Transform t) => Quaternion.Inverse(avatar.transform.rotation) * t.rotation;

        static void Next(int step)
        {
            stepAt = Time.time;
            SessionState.SetInt(Key, step);
        }

        static void Call(string method, params object[] args) =>
            typeof(CckAnimatorTester).GetMethod(method, Any).Invoke(tester, args);

        static void Check(bool ok, string what)
        {
            if (!ok) fail++;
            Debug.Log("[PhysicsTesterProbe] " + (ok ? "ok   " : "FAIL ") + what);
        }

        static void Finish()
        {
            Debug.Log("[PhysicsTesterProbe] " + (fail == 0 ? "PASS" : $"FAIL: {fail} check(s)"));
            SessionState.EraseInt(Key);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
