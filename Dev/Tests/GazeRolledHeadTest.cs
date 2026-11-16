#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // FT-05: the gaze clips key the empties' local angles ("Down" +X, "Left" -Y), so on a head
    // bone that is not axis-aligned the eyes must still look down and to the avatar's left.
    // Constraints are only evaluated by the player loop, so the look is read in Play mode.
    //
    // Run: -executeMethod AvatarBridge.Regression.GazeRolledHeadTest.Run, without -quit.
    [InitializeOnLoad]
    public static class GazeRolledHeadTest
    {
        const string K = "GazeRolledHeadTest.";
        static readonly (string name, Vector3 head)[] Rigs =
        {
            ("GazeStraight", Vector3.zero),
            ("GazeRolled", new Vector3(-90f, 0f, 37f)),
        };

        static GazeRolledHeadTest()
        {
            if (SessionState.GetInt(K + "stage", 0) > 0) EditorApplication.update += Tick;
        }

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var build = typeof(FaceTrackingInjector).GetMethod("BuildEyeRig", BindingFlags.NonPublic | BindingFlags.Static);
            float x = 0f;
            foreach (var (name, headAngles) in Rigs)
            {
                var root = new GameObject(name).transform;
                root.position = new Vector3(x += 2f, 0f, 0f);
                root.rotation = Quaternion.Euler(0f, 30f, 0f);
                var head = new GameObject("Head").transform;
                head.SetParent(root, false);
                head.localPosition = new Vector3(0f, 1.5f, 0f);
                head.localRotation = Quaternion.Euler(headAngles);
                foreach (var (eye, side) in new[] { ("Eye.L", -1f), ("Eye.R", 1f) })
                {
                    var t = new GameObject(eye).transform;
                    t.SetParent(head, true);
                    t.position = head.position + root.rotation * new Vector3(0.03f * side, 0.05f, 0.08f);
                    t.rotation = Quaternion.Euler(13f * side, 70f, -20f);
                }
                var ctx = new BridgeContext { Target = root.gameObject, Report = new BridgeReport(), Settings = new BridgeSettings() };
                build.Invoke(null, new object[] { ctx, new Dictionary<string, string>() });
            }
            SessionState.SetInt(K + "stage", 1);
            SessionState.SetInt(K + "frames", 0);
            SessionState.SetString(K + "log", "");
            SessionState.SetInt(K + "fail", 0);
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            int stage = SessionState.GetInt(K + "stage", 0);
            if (!EditorApplication.isPlaying) return;
            int frames = SessionState.GetInt(K + "frames", 0) + 1;
            SessionState.SetInt(K + "frames", frames);
            if (frames < 5) return;

            int fail = SessionState.GetInt(K + "fail", 0);
            var log = new System.Text.StringBuilder(SessionState.GetString(K + "log", ""));
            try
            {
                foreach (var (name, _) in Rigs)
                {
                    var root = GameObject.Find(name).transform;
                    var frame = root.Find("Head/EyeTracking.Frame");
                    foreach (var side in new[] { "L", "R" })
                    {
                        var eye = root.Find("Head/Eye." + side);
                        var empty = frame.Find("EyeTracking." + side);
                        string key = K + name + side;
                        if (stage == 1)
                        {
                            SessionState.SetString(key, JsonUtility.ToJson(eye.rotation));
                            empty.localEulerAngles = new Vector3(20f, -20f, 0f);
                            continue;
                        }
                        var rest = JsonUtility.FromJson<Quaternion>(SessionState.GetString(key, ""));
                        // Where the eye's own forward went, in the avatar's frame.
                        var look = Quaternion.Inverse(root.rotation) * (eye.rotation * Quaternion.Inverse(rest)) * root.forward;
                        bool ok = look.y < -0.25f && look.x < -0.25f;
                        if (!ok) fail++;
                        log.AppendLine($"[GazeRolledHeadTest] {(ok ? "ok  " : "WRONG")} {name} eye {side} looks down and left: " +
                                       $"down {-look.y:0.000}, left {-look.x:0.000}, ahead {look.z:0.000}");
                    }
                }
            }
            catch (Exception e)
            {
                fail++;
                log.AppendLine("[GazeRolledHeadTest] ERROR " + e);
            }
            SessionState.SetInt(K + "fail", fail);
            SessionState.SetString(K + "log", log.ToString());
            SessionState.SetInt(K + "frames", 0);
            if (stage == 1)
            {
                SessionState.SetInt(K + "stage", 2);
                return;
            }

            SessionState.SetInt(K + "stage", 0);
            EditorApplication.update -= Tick;
            Debug.Log(log + (fail == 0 ? "[GazeRolledHeadTest] PASS" : $"[GazeRolledHeadTest] FAIL: {fail}"));
            EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
