#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Play Audio finds an AudioSource whose object has a slash in its name. Transform.Find reads the
    // slash as a step down, so the player was dropped as having no AudioSource at that path.
    public static class PlayAudioSlashPathTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Play Audio on a slash-named object")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("PlayAudioSlashAvatar");
            var behaviour = ScriptableObject.CreateInstance<VRC.SDK3.Avatars.Components.VRCAnimatorPlayAudio>();
            var state = new AnimatorState { name = "Sound" };
            try
            {
                var speaker = new GameObject("Speaker/L");
                speaker.transform.SetParent(root.transform, false);
                speaker.AddComponent<AudioSource>();
                behaviour.SourcePath = "Speaker/L";
                behaviour.PlayOnEnter = true;
                var ctx = new BridgeContext { Target = root, Report = new BridgeReport(), Settings = new BridgeSettings() };

                var merger = typeof(AnimatorMerger);
                var dropped = (List<string>) merger.GetField("DroppedPlayAudio", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                dropped.Clear();
                var convert = merger.GetMethod("ConvertPlayAudio", BindingFlags.NonPublic | BindingFlags.Static);
                // Past the lookup the conversion wants a clip; only whether it found the source is asked.
                try { convert.Invoke(null, new object[] { ctx, state, behaviour }); }
                catch (TargetInvocationException) { }
                Check(!dropped.Any(d => d.Contains("no AudioSource")), "the AudioSource is found: " + string.Join("; ", dropped));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(behaviour);
                UnityEngine.Object.DestroyImmediate(state);
            }
            Debug.Log(failures == 0 ? "[PlayAudioSlashPathTest] PASS" : $"[PlayAudioSlashPathTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[PlayAudioSlashPathTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
