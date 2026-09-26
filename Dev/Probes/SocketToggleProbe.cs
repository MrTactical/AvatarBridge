// Sockets built one after another through the toolkit's door must each get
// a menu toggle of their own, and keep it when built again. A synthetic
// avatar with a controller of its own, so no test avatar is touched.
//
// Run: -executeMethod AvatarBridge.Regression.SocketToggleProbe.Run
#if CVR_CCK_EXISTS
using System;
using System.Linq;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class SocketToggleProbe
    {
        const string Dir = "Assets/__SocketToggleProbe";
        static int fail;

        public static void Run()
        {
            fail = 0;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(Dir);
                AssetDatabase.CreateFolder("Assets", "__SocketToggleProbe");
                var controller = AnimatorController.CreateAnimatorControllerAtPath(Dir + "/Probe.controller");
                var root = new GameObject("__SocketToggleProbe");
                root.AddComponent<Animator>().runtimeAnimatorController = controller;
                var avatar = root.AddComponent<CVRAvatar>();

                var sockets = new[]
                {
                    Socket(root, "Hips", YapsSocket.SocketKind.Hole),
                    Socket(root, "Head", YapsSocket.SocketKind.Hole),
                    Socket(root, "Hand", YapsSocket.SocketKind.Ring),
                };
                // Markers first, as adding a socket does, so the lighthouse the
                // first full build writes covers every socket, including the
                // ones with no toggle yet. Built from scratch one at a time,
                // each had its toggle before the lighthouse saw it.
                foreach (var s in sockets) YapsSocketBuilder.Build(s);
                for (int round = 1; round <= 2; round++)
                {
                    foreach (var s in sockets)
                    {
                        foreach (var line in YapsNativeBuilder.BuildSocket(s)) Log($"  {s.name}: {line}");
                    }
                    foreach (var s in sockets)
                    {
                        int n = TogglesFor(avatar, s.gameObject);
                        Check(n == 1, $"round {round}: {s.name} has its own menu toggle ({n}); " +
                                      $"switched by {YapsToggles.ToggledBy(s.gameObject, avatar, YapsToggles.LabelFor(s)) ?? "nothing else"}");
                    }
                }
                var lighthouse = avatar.avatarSettings.settings.FirstOrDefault(e => e.machineName == YapsLighthouse.Parameter);
                Check(lighthouse != null && lighthouse.dropDownSettings.options.Count == sockets.Length + 1,
                    $"one Marker lights row, Off plus one option per socket ({lighthouse?.dropDownSettings.options.Count})");
                UnityEngine.Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(Dir);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                fail++;
            }
            Log(fail == 0 ? "PASS" : $"FAIL: {fail} check(s)");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        static YapsSocket Socket(GameObject root, string bone, YapsSocket.SocketKind kind)
        {
            var parent = new GameObject(bone).transform;
            parent.SetParent(root.transform, false);
            var go = new GameObject("YAPS Socket");
            go.transform.SetParent(parent, false);
            var socket = go.AddComponent<YapsSocket>();
            socket.kind = kind;
            return socket;
        }

        static int TogglesFor(CVRAvatar avatar, GameObject target) =>
            avatar.avatarSettings?.settings?.Count(e => e != null && e.type == CVRAdvancedSettingsEntry.SettingsType.Toggle
                && e.toggleSettings?.gameObjectTargets != null
                && e.toggleSettings.gameObjectTargets.Any(g => g != null && g.gameObject == target)) ?? 0;

        static void Check(bool ok, string what)
        {
            if (!ok) fail++;
            Log((ok ? "ok   " : "FAIL ") + what);
        }

        static void Log(string s) => Debug.Log("[SocketToggleProbe] " + s);
    }
}
#endif
