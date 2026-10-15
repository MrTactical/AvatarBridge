// Presses every button and flips every toggle in AvatarBridge's windows and inspectors, each
// on a freshly loaded scene, and writes down what the press changed. A press with no result a
// person could see is what this exists for: "Tune it" only set the selection, which shows
// nothing while the Inspector is a hidden tab, locked or closed.
//
// Run through Dev/Probes/button-smoke.sh, which backs up what a press may write and lists what
// it wrote.
//   Unity.exe -batchmode -projectPath <p> -executeMethod AvatarBridge.Regression.ButtonSmokeProbe.Run
//     -bsScenes "Assets/a.unity;Assets/b.unity" -bsOut <file.tsv> [-bsWindows tester,yaps,...]
//   -bsStory thighs: one user's story instead, see ButtonSmokeProbe.Story.cs.
//     [-bsDanger] [-bsNoPlay] [-bsNoPictures] [-bsMinutes 120]
// -bsWindows none -bsOnlyGrab goes straight to Play mode and does the grabs alone.
// A link (text ending in an arrow) is never pressed: it opens a browser. A Danger button, or one
// that removes, clears or reverts, is listed but pressed only with -bsDanger, since batch Unity
// answers every confirmation dialog yes. After the edit-mode passes the tester's own "Enter Play
// mode" is pressed, and every control the tester shows in Play mode is pressed in turn.
// Only Buttons and Toggles are pressed; fields, sliders and dropdowns are not.
// Each press is also judged by a picture of the avatar, front and back, taken before and after
// through a Scene view with no window, so overlays a press turns on are in it; where it moved,
// the pair is kept in pics/ beside the table, the changed pixels tinted. In Play mode a few
// chains are grabbed in that Scene view with mouse events, dragged and let go, and the bone
// has to follow and swing back; each grab keeps a strip of rest, held and after.
// Exit 1 when any press threw, had no handler, changed nothing, or only changed the selection.
#if CVR_CCK_EXISTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    [InitializeOnLoad]
    public static partial class ButtonSmokeProbe
    {
        const string PlayKey = "ButtonSmokeProbe.play";
        const int Width = 560, Height = 2600;
        // A press gets at least this many editor ticks and this long to land: delayCall, update
        // hooks and a window's schedule.Execute(...).Every(100) all run in between.
        const int SettleTicks = 8;
        const double SettleSeconds = 0.4, PlaySettleSeconds = 1.5;
        // Rows of one button repeated down a list (a Show per report row) are sampled.
        const int SamePerPath = 2;

        static readonly Regex Destructive = new Regex(@"^(Remove|Delete|Revert|Discard|Clean up|Undo|Clear|Put the .* back)\b", RegexOptions.IgnoreCase);
        // Rewrites assets across the whole project, not just the avatar's.
        static readonly Regex ProjectWide = new Regex(@"^Update every\b");
        // Opens another application.
        static readonly Regex Outside = new Regex(@"^Open (web|full) report$");
        // A ping highlights the object in the Project or Hierarchy window, which batch Unity
        // does not have, so a press that only pings reads as nothing.
        static readonly Regex PingOnly = new Regex(@"^Show in Project$");

        static readonly List<string> Logged = new List<string>();
        static readonly List<string> Said = new List<string>();
        static readonly List<string> AssetsTouched = new List<string>();
        static readonly HashSet<string> AllTouched = new HashSet<string>();
        static readonly List<string> Rows = new List<string>();

        class Watch : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                AssetsTouched.AddRange(imported.Select(p => "+" + p));
                AssetsTouched.AddRange(deleted.Select(p => "-" + p));
                AssetsTouched.AddRange(moved.Select(p => ">" + p));
            }
        }

        static ButtonSmokeProbe()
        {
            string pending = SessionState.GetString(PlayKey, "");
            if (pending.Length > 0) EditorApplication.delayCall += () => Drive(PlayPass(pending));
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static bool Flag(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        public static void Run()
        {
            string outFile = Arg("-bsOut") ?? Path.Combine(Path.GetTempPath(), "ab-button-smoke.tsv");
            var scenes = (Arg("-bsScenes") ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            var only = (Arg("-bsWindows") ?? "").Split(',').Where(s => s.Length > 0).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[ButtonSmoke] -bsScenes names no scene");
                EditorApplication.Exit(2);
                return;
            }
            Application.logMessageReceived += OnLog;
            Setup(outFile);
            if (Directory.Exists(PicturesDir)) Directory.Delete(PicturesDir, true);
#if AVATARBRIDGE_YAPS
            if (Arg("-bsStory") != null) { Drive(StoryEdit(scenes[0], outFile)); return; }
#endif
            Drive(EditPasses(scenes, only, Flag("-bsDanger"), !Flag("-bsNoPlay"), outFile));
        }

        // One picture through the Scene view beside a plain render, with its corners marked and
        // its geometry logged, to see what the capture does to it.
        //   -executeMethod AvatarBridge.Regression.ButtonSmokeProbe.ShotTest -bsScenes <scene> -bsOut <file.tsv>
        public static void ShotTest()
        {
            string outFile = Arg("-bsOut") ?? Path.Combine(Path.GetTempPath(), "ab-shot.tsv");
            Load(Arg("-bsScenes"), out _, out _);
            Setup(outFile);
            Pictures = true;
            SceneShot.Test = true;
            // A run mounts windows between pictures; the second shot has to match the first.
            var window = UiOffscreenProbe.Mount(TypeNamed("AvatarBridge.CckAnimatorTester"), Width, Height, false);
            window.Pump();
            var shot = Picture.Take();
            var again = Picture.Take();
            window.Dispose();
            SceneShot.Off = true;
            var plain = Picture.Take();
            Debug.Log("[ButtonSmoke] shot written to " + Picture.Save(PicturesDir, "shot test", plain, shot, null));
            Debug.Log("[ButtonSmoke] again: " + (Picture.Describe(shot, again, null) ?? "no change"));
            EditorApplication.Exit(0);
        }

        static bool Pictures;
        static string PicturesDir;
        static int PictureCount;

        // Again after the domain reload into Play mode, which clears statics.
        static void Setup(string outFile)
        {
            Pictures = !Flag("-bsNoPictures");
            PicturesDir = Path.Combine(Path.GetDirectoryName(outFile) ?? ".", "pics");
            PictureCount = Directory.Exists(PicturesDir) ? Directory.GetFiles(PicturesDir, "*.png").Length : 0;
        }

        static void OnLog(string message, string stack, LogType type)
        {
            string line = message.Split('\n')[0];
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Logged.Add(line);
            else if (!line.StartsWith("[ButtonSmoke]", StringComparison.Ordinal)) Said.Add(line);
        }

        // An iterator stepped once per editor tick, so the editor runs between steps. A step that
        // yields another iterator runs it to the end first. The clock is the editor's, which a
        // domain reload into Play mode does not reset.
        static void Drive(IEnumerator job)
        {
            double deadline = (double.TryParse(Arg("-bsMinutes"), out double m) ? m : 120.0) * 60.0;
            var stack = new Stack<IEnumerator>();
            stack.Push(job);
            void Tick()
            {
                try
                {
                    if (EditorApplication.timeSinceStartup > deadline)
                        throw new TimeoutException("[ButtonSmoke] out of time; raise -bsMinutes");
                    while (stack.Count > 0)
                    {
                        var top = stack.Peek();
                        if (!top.MoveNext()) { stack.Pop(); continue; }
                        if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                        return;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    EditorApplication.update -= Tick;
                    EditorApplication.Exit(3);
                    return;
                }
                EditorApplication.update -= Tick;
            }
            EditorApplication.update += Tick;
        }

        static IEnumerator Settle(UiOffscreenProbe.Mounted m, double seconds = SettleSeconds)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            for (int i = 0; i < SettleTicks || EditorApplication.timeSinceStartup < until; i++)
            {
                m.Pump(1);
                yield return null;
            }
        }

        // ---- edit mode ---------------------------------------------------------------------

        struct Target
        {
            public string Key;
            public Type Type;
            public string Pass;
            public Action<EditorWindow> Prepare;
            public bool WantsConverted;
            public string Scene;
        }

        static IEnumerator EditPasses(string[] scenes, string[] only, bool danger, bool play, string outFile)
        {
            File.WriteAllText(outFile, "scene\twindow\tpass\tcontrol\tverdict\tdetail\n");
            foreach (var scene in scenes)
            {
                foreach (var target in Targets(scene, only).ToList())
                {
                    // The controls this state shows, read once; each is then pressed on its own.
                    var mounted = Fresh(target);
                    yield return Settle(mounted);
                    Expand(mounted);
                    var controls = Controls(mounted.Content, out var sampledOut);
                    Debug.Log($"[ButtonSmoke] {scene} {target.Key} {target.Pass}: {controls.Count} control(s)");
                    mounted.Dispose();
                    foreach (var dropped in sampledOut) Row(target, dropped, "SAMPLED", "");

                    foreach (var id in controls)
                    {
                        mounted = Fresh(target);
                        yield return Settle(mounted);
                        Expand(mounted);
                        var control = Find(mounted.Content, id);
                        if (control == null)
                        {
                            Row(target, id, "GONE", "present when the window was first read, missing after a fresh build");
                            mounted.Dispose();
                            continue;
                        }
                        if (Unpressable(control, danger, out string verdict, out string why))
                        {
                            Row(target, id, verdict, why);
                            mounted.Dispose();
                            continue;
                        }
                        var revealed = new List<string>();
                        yield return PressAndJudge(target, mounted, control, id, r => revealed.AddRange(r), controls);
                        // What the press brought up (a Stop after a Preview), pressed once each in
                        // that state, so a control that only exists after another is covered too.
                        foreach (var next in revealed)
                        {
                            // A press that rebuilt the page (a tab) folded its cards again, and a
                            // control in a folded card reports into a body no one can see.
                            Expand(mounted);
                            var second = Find(mounted.Content, next);
                            if (second == null || Unpressable(second, danger, out _, out _)) continue;
                            yield return PressAndJudge(target, mounted, second, id + "  >  " + next, null, null);
                        }
                        mounted.Dispose();
                        CloseStrays();
                    }
                    File.AppendAllLines(outFile, Rows);
                    Rows.Clear();
                }
            }
            Debug.Log("[ButtonSmoke] edit-mode passes written to " + outFile);

            if (!play)
            {
                Finish(outFile);
                yield break;
            }
            // The tester's own button takes the editor into Play mode; a press that does nothing
            // is reported like any other.
            string playScene = scenes.FirstOrDefault(s => { Load(s, out _, out var c); return c != null; });
            if (playScene == null)
            {
                Finish(outFile);
                yield break;
            }
            Load(playScene, out _, out var converted);
            Selection.activeGameObject = converted;
            var tester = TypeNamed("AvatarBridge.CckAnimatorTester");
            var host = UiOffscreenProbe.Mount(tester, Width, Height, false);
            yield return Settle(host);
            Expand(host);
            var enter = host.Content.Query<Button>().Where(b => Label(b) == "Enter Play mode").First();
            if (enter == null)
            {
                File.AppendAllText(outFile, $"{playScene}\ttester\tedit\tEnter Play mode\tGONE\tno such button with an avatar selected\n");
                Finish(outFile);
                yield break;
            }
            SceneShot.Dispose();
            SessionState.SetString(PlayKey, outFile + "|" + playScene);
            Press(enter, out _);
            host.Dispose();
            // A project with domain reload off for Play mode never reruns the static constructor.
            for (int i = 0; i < 600 && !EditorApplication.isPlaying; i++) yield return null;
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.EraseString(PlayKey);
                File.AppendAllText(outFile, $"{playScene}\ttester\tedit\tEnter Play mode\tDEAD\tPlay mode never started\n");
                Finish(outFile);
            }
            else if (SessionState.GetString(PlayKey, "").Length > 0)
            {
                // Same domain: carry on here.
                yield return PlayPass(SessionState.GetString(PlayKey, ""));
            }
        }

        static IEnumerable<Target> Targets(string scene, string[] only)
        {
            Load(scene, out var source, out var converted);
            foreach (var (key, typeName, wantsConverted) in UiOffscreenProbe.Windows)
            {
                if (only.Length > 0 && !only.Contains(key)) continue;
                var type = TypeNamed(typeName);
                if (type == null) continue;
                foreach (var (suffix, prepare) in UiOffscreenProbe.Passes(key, type, source, converted))
                {
                    yield return new Target
                    {
                        Key = key, Type = type, Pass = PassName(suffix),
                        Prepare = prepare, WantsConverted = wantsConverted, Scene = scene,
                    };
                }
            }
            // Every inspector this package draws, on the first object of its kind in the scene.
            foreach (var typeName in new[] { "AvatarBridge.Yaps.YapsSocket", "AvatarBridge.Yaps.YapsPlug", "AvatarBridge.Yaps.YapsSquashSettings" })
            {
                string key = "inspector:" + typeName.Substring(typeName.LastIndexOf('.') + 1);
                if (only.Length > 0 && !only.Contains(key) && !only.Contains("inspectors")) continue;
                var type = TypeNamed(typeName);
                if (type == null || Object.FindObjectOfType(type, true) == null) continue;
                yield return new Target
                {
                    Key = key, Type = typeof(UiOffscreenProbe.Inspector), Pass = "plain", Scene = scene,
                    Prepare = w => UiOffscreenProbe.Inspector.Target = Object.FindObjectOfType(type, true),
                };
            }
        }

        static string PassName(string suffix) => suffix.Length > 0 ? suffix.TrimStart('-') : "plain";

        static UiOffscreenProbe.Mounted Fresh(Target target)
        {
            Load(target.Scene, out var source, out var converted);
            var pick = target.WantsConverted ? converted : source;
            if (pick != null) Selection.activeGameObject = pick;
            else Selection.activeObject = null;
            // Passes captured the scene's objects when they were listed; a reload made new ones.
            var prepare = target.Prepare;
            if (!target.Key.StartsWith("inspector:", StringComparison.Ordinal))
                prepare = UiOffscreenProbe.Passes(target.Key, target.Type, source, converted)
                    .FirstOrDefault(p => PassName(p.suffix) == target.Pass).prepare;
            return UiOffscreenProbe.Mount(target.Type, Width, Height, false, prepare);
        }

        static void Load(string scene, out GameObject source, out GameObject converted)
        {
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            source = UiOffscreenProbe.FindSource();
            converted = Object.FindObjectOfType<CVRAvatar>()?.gameObject;
        }

        // ---- play mode ---------------------------------------------------------------------

        static IEnumerator PlayPass(string pending)
        {
            SessionState.EraseString(PlayKey);
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            var parts = pending.Split('|');
            string outFile = parts[0], scene = parts[1];
            Setup(outFile);
            double start = EditorApplication.timeSinceStartup;
            while (!EditorApplication.isPlaying)
            {
                if (EditorApplication.timeSinceStartup - start > 180.0)
                {
                    File.AppendAllText(outFile, $"{scene}\ttester\tedit\tEnter Play mode\tDEAD\tPlay mode never started\n");
                    Finish(outFile);
                    yield break;
                }
                yield return null;
            }
            File.AppendAllText(outFile, $"{scene}\ttester\tedit\tEnter Play mode\tOK\tPlay mode started\n");
            // Cloth builds over the first seconds.
            while (EditorApplication.timeSinceStartup - start < 6.0) yield return null;

            var avatar = Object.FindObjectsOfType<CVRAvatar>().FirstOrDefault();
            if (avatar != null) Selection.activeGameObject = avatar.gameObject;
            var target = new Target { Key = "tester", Type = TypeNamed("AvatarBridge.CckAnimatorTester"), Pass = "play", Scene = scene };
            var mounted = UiOffscreenProbe.Mount(target.Type, Width, Height, false);
            yield return Settle(mounted, PlaySettleSeconds);
            Expand(mounted);
            var controls = Controls(mounted.Content, out var sampledOut);
            foreach (var dropped in sampledOut) Row(target, dropped, "SAMPLED", "");
            Debug.Log($"[ButtonSmoke] {scene} tester play: {controls.Count} control(s)");
            // Nothing is on screen in batch, and cloth culls with its Animator.
            foreach (var a in Object.FindObjectsOfType<Animator>()) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            yield return Grab(target, mounted);
            if (Flag("-bsOnlyGrab")) controls.Clear();
            // One state through the whole pass, as a person clicking down the card would have.
            // Motion is stopped between presses so the next one starts from rest.
            bool danger = Flag("-bsDanger");
            foreach (var id in controls)
            {
                var control = Find(mounted.Content, id);
                if (control == null) { Row(target, id, "GONE", "gone after an earlier press in this pass"); continue; }
                if (Unpressable(control, danger, out string verdict, out string why)) { Row(target, id, verdict, why); continue; }
                yield return PressAndJudge(target, mounted, control, id, null, null, PlaySettleSeconds);
                foreach (var stop in mounted.Content.Query<Button>().Where(b => Label(b).StartsWith("Stop", StringComparison.Ordinal) && b.enabledInHierarchy).ToList())
                    Press(stop, out _);
                yield return Settle(mounted, PlaySettleSeconds);
                CloseStrays();
            }
            mounted.Dispose();
            File.AppendAllLines(outFile, Rows);
            Rows.Clear();
            Finish(outFile);
        }

        // ---- gestures ---------------------------------------------------------------------

        const int GrabsPerAvatar = 4;

        // The tester's grab, done as a person does it in the Scene view: press on a bone's dot,
        // drag across the screen, hold, let go. The bone has to follow the drag and swing back.
        static IEnumerator Grab(Target target, UiOffscreenProbe.Mounted mounted)
        {
            var toggle = mounted.Content.Query<Toggle>().ToList().FirstOrDefault(t => Label(t) == "Grab chains in the Scene view");
            var avatar = Object.FindObjectsOfType<CVRAvatar>().FirstOrDefault();
            if (toggle == null || avatar == null)
            {
                Row(target, "gesture: grab", "GONE", toggle == null ? "no Grab toggle in Play mode" : "no avatar");
                yield break;
            }
            var tester = mounted.Window;
            var type = tester.GetType();
            var chains = ((IEnumerable<Component>)type.GetMethod("Chains", UiOffscreenProbe.Any).Invoke(null, new object[] { avatar }))
                .Where(c => c is Behaviour b && b.isActiveAndEnabled).ToList();
            if (chains.Count == 0)
            {
                Row(target, "gesture: grab", "SKIPPED", "no running chain on this avatar");
                yield break;
            }
            var grabbable = type.GetMethod("Grabbable", UiOffscreenProbe.Any);
            var roots = type.GetMethod("Roots", UiOffscreenProbe.Any);
            var held = type.GetField("_held", UiOffscreenProbe.Any);
            var heldBone = type.GetField("_heldBone", UiOffscreenProbe.Any);
            float height = Mathf.Max(0.5f, avatar.GetComponentsInChildren<Renderer>().Select(r => r.bounds.max.y).DefaultIfEmpty(1.6f).Max()
                                           - avatar.transform.position.y);
            var fwd = Vector3.ProjectOnPlane(avatar.transform.forward, Vector3.up).normalized;

            toggle.value = true;
            var go = new GameObject("ButtonSmoke grab camera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 1f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.01f;
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;

            // Spread down the list, so a long chain and a short one are both tried.
            int step = Mathf.Max(1, chains.Count / GrabsPerAvatar);
            for (int k = 0; k < chains.Count && k / step < GrabsPerAvatar; k += step)
            {
                var chain = chains[k];
                var fixedBones = ((IEnumerable<Transform>)roots.Invoke(null, new object[] { chain })).ToList();
                var tip = ((IEnumerable<Transform>)grabbable.Invoke(null, new object[] { chain }))
                    .OrderByDescending(t => fixedBones.Count == 0 ? 0f : fixedBones.Min(f => Vector3.Distance(f.position, t.position))).First();
                string id = $"gesture: grab {chain.GetType().Name} {tip.name}";
                cam.transform.position = tip.position + fwd * height * 0.6f;
                cam.transform.LookAt(tip.position);
                cam.farClipPlane = height * 4f;

                // Cloth settles for a while after Play starts or a grab before; a pull is told
                // apart from that only once it has stopped.
                float still = float.MaxValue;
                for (int tries = 0; tries < 8 && still > 0.002f; tries++)
                {
                    var start = tip.position;
                    still = 0f;
                    yield return Ticks(0.5, () => still = Mathf.Max(still, Vector3.Distance(tip.position, start)));
                }
                var rest = SceneShot.Shoot(cam);
                if (rest == null || !SceneShot.Send(EventType.MouseDown, tip.position))
                {
                    Row(target, id, "ERROR", "the probe could not build a Scene view to press in");
                    break;
                }
                var took = held.GetValue(tester) as Component;
                var bone = heldBone.GetValue(tester) as Transform;
                if (took == null || bone == null)
                {
                    Row(target, id, "DEAD", $"a press on the dot of {tip.name} took hold of nothing; {SceneShot.SaveStrip(id, rest)}");
                    continue;
                }
                // The pick may prefer a bone nearer the camera; the one held is the one measured.
                var from = bone.position;
                // Most of the way to the picture's edge, so the held point is in it.
                float halfWidth = Vector3.Distance(cam.transform.position, from) * Mathf.Tan(15f * Mathf.Deg2Rad) * Picture.W / Picture.H;
                var goal = from + cam.transform.right * halfWidth * 0.8f;
                for (int i = 1; i <= 8; i++)
                {
                    SceneShot.Send(EventType.MouseDrag, Vector3.Lerp(from, goal, i / 8f));
                    yield return Ticks(0.025, null);
                }
                float pull = Vector3.Distance(from, goal), peak = 0f;
                yield return Ticks(1.0, () => peak = Mathf.Max(peak, Vector3.Distance(bone.position, from)));
                var holding = SceneShot.Shoot(cam);
                SceneShot.Send(EventType.MouseUp, goal);
                bool released = held.GetValue(tester) == null;
                yield return Ticks(3.0, null);
                float back = Vector3.Distance(bone.position, from);
                var after = SceneShot.Shoot(cam);
                string pics = Pictures ? "; " + SceneShot.SaveStrip(id, rest, holding, after) : "";

                bool followed = peak > 0.02f && peak > 5f * still;
                bool returned = back < Mathf.Max(0.5f * peak, 3f * still + 0.005f);
                string what = (took == chain ? "" : $"held {took.GetType().Name} on {took.name}; ") +
                              $"dragged {bone.name} {pull * 1000f:F0} mm, it followed {peak * 1000f:F0} mm " +
                              $"({still * 1000f:F1} mm with no hand), {back * 1000f:F0} mm from where it was 3 s after letting go";
                string verdict = !released ? "DEAD" : !followed ? "DEAD" : !returned ? "DEAD" : "OK";
                string why = !released ? "still held after the button came up; " : !followed ? "did not follow the drag; " : !returned ? "did not swing back; " : "";
                Row(target, id, verdict, why + what + pics);
            }
            ShaderUtil.allowAsyncCompilation = async;
            Object.DestroyImmediate(go);
            toggle.value = false;
        }

        // Editor ticks for a while, with a look at something on each.
        static IEnumerator Ticks(double seconds, Action look)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < until)
            {
                look?.Invoke();
                yield return null;
            }
        }

        // ---- pressing and judging ----------------------------------------------------------

        static IEnumerator PressAndJudge(Target target, UiOffscreenProbe.Mounted mounted, VisualElement control, string id,
            Action<List<string>> revealed, List<string> known, double seconds = SettleSeconds)
        {
            // What moves with no press at all (a live readout, cloth in Play mode) is left out.
            var baseline = Snapshot.Take(mounted);
            yield return Settle(mounted, seconds);
            var before = Snapshot.Take(mounted);
            var noise = Snapshot.Noise(baseline, before);
            Logged.Clear();
            Said.Clear();
            AssetsTouched.Clear();
            bool handled = false;
            string thrown = null;
            try { Press(control, out handled); }
            catch (Exception e) { thrown = (e.InnerException ?? e).GetType().Name + ": " + (e.InnerException ?? e).Message; }
            bool wentToPlay = EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying;
            double until = EditorApplication.timeSinceStartup + seconds;
            for (int i = 0; i < SettleTicks || EditorApplication.timeSinceStartup < until; i++)
            {
                try { mounted.Pump(1); }
                catch (Exception e) { thrown = thrown ?? "while the window updated: " + e.GetType().Name + ": " + e.Message; }
                yield return null;
            }
            Expand(mounted);
            var after = Snapshot.Take(mounted);
            var changed = before.Diff(after, AssetsTouched, noise);
            // A line in the Console is an answer a person sees, if a quiet one.
            if (Said.Count > 0) changed.Add("console: " + Said[0]);
            AllTouched.UnionWith(AssetsTouched);

            string verdict;
            if (thrown != null || Logged.Count > 0) verdict = "ERROR";
            else if (!handled && changed.Count == 0) verdict = "NO HANDLER";
            else if (changed.Count == 0) verdict = "DEAD";
            else if (changed.Count == 1 && changed[0].StartsWith("selection", StringComparison.Ordinal)) verdict = "SELECT ONLY";
            else verdict = "OK";
            var detail = new List<string>(changed);
            if (thrown != null) detail.Insert(0, "threw " + thrown);
            if (verdict == "DEAD") detail.Add(before.Blind(after, noise));
            detail.AddRange(Logged.Distinct().Take(3).Select(l => "logged: " + l));
            if (wentToPlay) detail.Add("asked for Play mode");
            // The pair is kept wherever the picture moved, and wherever a person should look.
            if (before.Pic != null && after.Pic != null
                && (changed.Any(x => x.StartsWith("picture", StringComparison.Ordinal)) || verdict != "OK"))
                detail.Add(Picture.Save(PicturesDir, id, before.Pic, after.Pic, noise.Pic));
            Row(target, id, verdict, string.Join("; ", detail));

            if (revealed != null && known != null)
                revealed(Controls(mounted.Content, out _).Where(c => !known.Contains(c)).ToList());
            // A toggle kept in prefs would stay flipped for every later press.
            if (control is Toggle)
            {
                if ((Find(mounted.Content, id) ?? control) is Toggle t) t.value = !t.value;
                mounted.Pump(1);
            }
        }

        static void Press(VisualElement control, out bool handled)
        {
            handled = false;
            if (control is Button button)
            {
                // What Clickable runs on a real click: both of its events.
                var clickable = button.clickable;
                var clicked = typeof(Clickable).GetField("clicked", UiOffscreenProbe.Any)?.GetValue(clickable) as Action;
                var withInfo = typeof(Clickable).GetField("clickedWithEventInfo", UiOffscreenProbe.Any)?.GetValue(clickable) as Action<EventBase>;
                handled = clicked != null || withInfo != null;
                clicked?.Invoke();
                if (withInfo != null)
                {
                    using (var e = MouseUpEvent.GetPooled(new Event { type = EventType.MouseUp }))
                    {
                        e.target = button;
                        withInfo(e);
                    }
                }
                // A handler registered on the element rather than the clickable.
                using (var click = ClickEvent.GetPooled())
                {
                    click.target = button;
                    button.SendEvent(click);
                }
                return;
            }
            if (control is Toggle toggle)
            {
                handled = true;
                toggle.value = !toggle.value;
            }
        }

        static bool Unpressable(VisualElement control, bool danger, out string verdict, out string why)
        {
            verdict = why = null;
            string label = Label(control);
            if (!control.enabledInHierarchy)
            {
                verdict = "DISABLED";
                why = control.tooltip ?? "";
                return true;
            }
            if (label.EndsWith("↗", StringComparison.Ordinal))
            {
                verdict = "LINK";
                why = "opens a browser; not pressed";
                return true;
            }
            if (PingOnly.IsMatch(label))
            {
                verdict = "PING";
                why = "only pings, which needs the Project window; not pressed";
                return true;
            }
            if (Outside.IsMatch(label))
            {
                verdict = "OUTSIDE";
                why = "opens another application; not pressed";
                return true;
            }
            if (control.ClassListContains("ab-tab-on"))
            {
                verdict = "CURRENT";
                why = "the tab already showing";
                return true;
            }
            // A search field's own magnifier and clear buttons.
            if (control is Button part && string.IsNullOrEmpty(part.text) && part.name.StartsWith("unity-", StringComparison.Ordinal))
            {
                verdict = "PART";
                why = "part of a Unity field";
                return true;
            }
            // A toggle named for a removal only chooses it; the convert does the removing.
            if (!danger && control is Button && (control.ClassListContains("ab-btn-danger") || ProjectWide.IsMatch(label)
                || (Destructive.IsMatch(label) && label.IndexOf("test", StringComparison.OrdinalIgnoreCase) < 0)))
            {
                verdict = "SKIPPED";
                why = "removes, clears or rewrites the project; pressed only with -bsDanger";
                return true;
            }
            if (label == "Enter Play mode")
            {
                verdict = "LATER";
                why = "pressed at the end, to start the Play pass";
                return true;
            }
            return false;
        }

        // ---- what a press can change -------------------------------------------------------

        class Snapshot
        {
            string ui, window, windows, clipboard, undo, notification;
            int selection;
            bool playing;
            Dictionary<string, int> scene;
            Dictionary<int, int> materials;
            public Color32[] Pic;

            public static Snapshot Take(UiOffscreenProbe.Mounted m)
            {
                var s = new Snapshot
                {
                    // First: its camera is made and gone again before the scene is read.
                    Pic = Pictures ? Picture.Take() : null,
                    ui = UiText(m.Content),
                    window = Safe(() => EditorJsonUtility.ToJson(m.Window)),
                    windows = string.Join(",", Resources.FindObjectsOfTypeAll<EditorWindow>()
                        .Where(w => w != m.Window).Select(w => w.GetType().Name + ":" + w.titleContent.text).OrderBy(n => n)),
                    clipboard = EditorGUIUtility.systemCopyBuffer,
                    undo = Undo.GetCurrentGroupName(),
                    notification = (typeof(EditorWindow).GetField("m_Notification", UiOffscreenProbe.Any)?.GetValue(m.Window) as GUIContent)?.text,
                    selection = Selection.activeInstanceID,
                    playing = EditorApplication.isPlaying,
                    scene = new Dictionary<string, int>(),
                    materials = new Dictionary<int, int>(),
                };
                // Hidden objects too: a preview's test socket can live out of the hierarchy.
                foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (!go.scene.IsValid() || EditorUtility.IsPersistent(go)) continue;
                    s.scene[PathOf(go) + "#" + go.GetInstanceID()] = HashObject(go, s.materials);
                }
                return s;
            }

            public sealed class Moving
            {
                public int Ui;          // 0 still, 1 only digits move, 2 more than digits
                public HashSet<string> UiLines;   // the lines that moved with no press
                public bool Window;
                public HashSet<string> Scene;
                public HashSet<int> Materials;
                public bool[] Pic;
            }

            static readonly Regex Digits = new Regex("[0-9]+");
            static string[] Lines(string text) => text.Split('\n');

            public static Moving Noise(Snapshot a, Snapshot b) => new Moving
            {
                Ui = a.ui == b.ui ? 0 : Digits.Replace(a.ui, "#") == Digits.Replace(b.ui, "#") ? 1 : 2,
                UiLines = Lines(a.ui).Except(Lines(b.ui)).Concat(Lines(b.ui).Except(Lines(a.ui))).ToHashSet(),
                Window = a.window != b.window,
                Scene = new HashSet<string>(a.scene.Where(p => !b.scene.TryGetValue(p.Key, out int h) || h != p.Value).Select(p => p.Key)),
                Materials = new HashSet<int>(a.materials.Where(p => !b.materials.TryGetValue(p.Key, out int h) || h != p.Value).Select(p => p.Key)),
                Pic = Picture.Moved(a.Pic, b.Pic, null),
            };

            // Why a press read as dead: whether the window moved on its own, and by how much.
            public string Blind(Snapshot after, Moving noise) =>
                $"window moving alone: {noise.Ui}, lines the press changed: {LineDelta(ui, after.ui)}, notices: " +
                string.Join(" // ", Lines(after.ui).Where(l => l.Contains("ab-notice")));

            public List<string> Diff(Snapshot after, List<string> assets, Moving noise)
            {
                var c = new List<string>();
                if (selection != after.selection)
                {
                    var now = EditorUtility.InstanceIDToObject(after.selection);
                    c.Add("selection -> " + (now != null ? now.name : "none"));
                }
                if (noise.Ui == 0 && ui != after.ui) c.Add($"window UI ({LineDelta(ui, after.ui)} lines)");
                if (noise.Ui == 1 && Digits.Replace(ui, "#") != Digits.Replace(after.ui, "#"))
                    c.Add($"window UI ({LineDelta(Digits.Replace(ui, "#"), Digits.Replace(after.ui, "#"))} lines beyond live numbers)");
                // A page still settling (one a tab just rebuilt) moves on its own; a line the press
                // brought that was not among those still counts.
                if (noise.Ui == 2)
                {
                    var before = Lines(ui).ToHashSet();
                    int fresh = Lines(after.ui).Count(l => !before.Contains(l) && !noise.UiLines.Contains(l));
                    if (fresh > 0) c.Add($"window UI ({fresh} lines beyond what moved alone)");
                }
                if (!noise.Window && window != after.window) c.Add("window state");
                if (windows != after.windows)
                {
                    var was = new HashSet<string>(windows.Split(','));
                    var opened = after.windows.Split(',').Where(w => w.Length > 0 && !was.Contains(w)).ToList();
                    c.Add(opened.Count > 0 ? "opened " + string.Join(", ", opened) : "a window closed");
                }
                if (clipboard != after.clipboard) c.Add("clipboard");
                if (notification != after.notification && after.notification != null) c.Add("notification '" + after.notification + "'");
                if (undo != after.undo) c.Add("undo step '" + after.undo + "'");
                if (playing != after.playing) c.Add("play mode");
                int added = after.scene.Keys.Count(k => !scene.ContainsKey(k));
                int removed = scene.Keys.Count(k => !noise.Scene.Contains(k) && !after.scene.ContainsKey(k));
                int edited = scene.Count(p => !noise.Scene.Contains(p.Key) && after.scene.TryGetValue(p.Key, out int h) && h != p.Value);
                if (added + removed + edited > 0) c.Add($"scene +{added} -{removed} ~{edited}");
                int mats = materials.Count(p => !noise.Materials.Contains(p.Key) && (!after.materials.TryGetValue(p.Key, out int h) || h != p.Value))
                           + after.materials.Keys.Count(k => !materials.ContainsKey(k));
                if (mats > 0) c.Add($"materials ~{mats}");
                string seen = Picture.Describe(Pic, after.Pic, noise.Pic);
                if (seen != null) c.Add(seen);
                if (assets.Count > 0) c.Add($"assets {assets.Count}: " + string.Join(" ", assets.Distinct().Take(4)) + (assets.Count > 4 ? " …" : ""));
                return c;
            }

            static string Safe(Func<string> f)
            {
                try { return f(); }
                catch { return ""; }
            }
        }

        // ---- what a press shows ------------------------------------------------------------

        // The avatar from the front and from behind, through a camera of its own, so a Preview
        // that moved nothing reads as "picture unchanged" however much the window redrew.
        static class Picture
        {
            // Batch Unity's Screen is 640 x 480, and Handles clip their camera to it.
            public const int W = 300, H = 480;
            // Per channel, out of 255: below this is dither and filtering, not a change.
            const int Step = 12;
            // A share of the picture smaller than this is a few edge pixels flickering.
            const float Least = 0.0005f;

            public static Color32[] Take()
            {
                var subject = Object.FindObjectOfType<CVRAvatar>()?.gameObject ?? UiOffscreenProbe.FindSource();
                if (subject == null) return null;
                Bounds? box = null;
                foreach (var r in subject.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                    var b = box ?? r.bounds;
                    b.Encapsulate(r.bounds);
                    box = b;
                }
                if (box == null) return null;
                var bounds = box.Value;
                var fwd = Vector3.ProjectOnPlane(subject.transform.forward, Vector3.up);
                fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;

                var go = new GameObject("ButtonSmoke camera") { hideFlags = HideFlags.HideAndDontSave };
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 1f);
                cam.fieldOfView = 30f;
                float halfV = 15f * Mathf.Deg2Rad, halfH = Mathf.Atan(Mathf.Tan(halfV) * W / H);
                float reach = Mathf.Max(bounds.extents.x, bounds.extents.z);
                float dist = Mathf.Max(bounds.extents.y / Mathf.Tan(halfV), reach / Mathf.Tan(halfH)) * 1.1f + reach;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = dist * 4f;

                var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
                var tex = new Texture2D(W * 2, H, TextureFormat.RGBA32, false);
                var active = RenderTexture.active;
                bool async = ShaderUtil.allowAsyncCompilation;
                try
                {
                    // A material still compiling draws as the cyan stand-in.
                    ShaderUtil.allowAsyncCompilation = false;
                    cam.targetTexture = rt;
                    for (int v = 0; v < 2; v++)
                    {
                        var dir = v == 0 ? fwd : Quaternion.AngleAxis(150f, Vector3.up) * fwd;
                        cam.transform.position = bounds.center + dir * dist;
                        cam.transform.LookAt(bounds.center);
                        var shot = SceneShot.Shoot(cam);
                        if (shot != null)
                        {
                            tex.SetPixels32(v * W, 0, W, H, shot);
                            continue;
                        }
                        cam.Render();
                        RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, W, H), v * W, 0);
                    }
                    tex.Apply(false);
                    return tex.GetPixels32();
                }
                finally
                {
                    ShaderUtil.allowAsyncCompilation = async;
                    RenderTexture.active = active;
                    cam.targetTexture = null;
                    RenderTexture.ReleaseTemporary(rt);
                    Object.DestroyImmediate(tex);
                    Object.DestroyImmediate(go);
                }
            }

            static bool Differs(Color32 a, Color32 b) =>
                Math.Abs(a.r - b.r) > Step || Math.Abs(a.g - b.g) > Step || Math.Abs(a.b - b.b) > Step;

            // Pixels that differ, left out where they already moved with no press.
            public static bool[] Moved(Color32[] a, Color32[] b, bool[] mask)
            {
                if (a == null || b == null || a.Length != b.Length) return null;
                var moved = new bool[a.Length];
                for (int i = 0; i < a.Length; i++) moved[i] = (mask == null || !mask[i]) && Differs(a[i], b[i]);
                return moved;
            }

            public static string Describe(Color32[] before, Color32[] after, bool[] mask)
            {
                var moved = Moved(before, after, mask);
                if (moved == null) return null;
                int front = 0, back = 0;
                for (int i = 0; i < moved.Length; i++)
                    if (moved[i]) { if (i % (W * 2) < W) front++; else back++; }
                float share = (front + back) / (float)moved.Length;
                if (share < Least) return null;
                string where = front > 0 && back > 0 ? "both views" : front > 0 ? "from the front" : "from behind";
                return $"picture changed {share:P1} ({where})";
            }

            // Before and after side by side; on the after half, what changed is tinted magenta.
            public static string Save(string dir, string id, Color32[] before, Color32[] after, bool[] mask)
            {
                Directory.CreateDirectory(dir);
                var moved = Moved(before, after, mask);
                int w = W * 2;
                var sheet = new Color32[w * 2 * H];
                for (int y = 0; y < H; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        sheet[y * w * 2 + x] = before[i];
                        var c = after[i];
                        sheet[y * w * 2 + w + x] = moved != null && moved[i] ? new Color32((byte)((c.r + 255) / 2), (byte)(c.g / 2), (byte)((c.b + 255) / 2), 255) : c;
                    }
                }
                var tex = new Texture2D(w * 2, H, TextureFormat.RGBA32, false);
                tex.SetPixels32(sheet);
                tex.Apply(false);
                string name = $"{++PictureCount:0000} {Regex.Replace(id, @"[^A-Za-z0-9 #-]+", "_")}";
                if (name.Length > 120) name = name.Substring(0, 120);
                string file = Path.Combine(dir, name + ".png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                return "pics/" + Path.GetFileName(file);
            }
        }

        // ---- the Scene view, with no window ------------------------------------------------

        // Overlays (grab dots, chain rings, painted regions) are drawn only inside a Scene view's
        // GUI, and batch Unity paints no window. This is that GUI without the window: a SceneView
        // that is never shown lends its camera to every duringSceneGui handler, called from an
        // IMGUI container in an offscreen panel, so they draw into the capture and take mouse
        // events the way a person's drag would send them.
        sealed class SceneHost : EditorWindow { }

        static class SceneShot
        {
            const int W = Picture.W, H = Picture.H;
            static SceneView _view;
            static IMGUIContainer _gui;
            static UiOffscreenProbe.Mounted _host;
            static RenderTexture _frame;
            static Vector3? _aim;
            static bool _broken;
            // ShotTest: a plain render to compare against, and the geometry written down.
            public static bool Off, Test;

            static bool Ready()
            {
                if (_broken || Off) return false;
                try
                {
                    if (_view == null) _view = ScriptableObject.CreateInstance<SceneView>();
                    if (_view.camera == null) throw new InvalidOperationException("the SceneView made no camera");
                    if (_host == null)
                    {
                        _gui = new IMGUIContainer(OnGui) { style = { width = W, height = H, flexShrink = 0 } };
                        _host = UiOffscreenProbe.Mount(typeof(SceneHost), W, H, false, w => w.rootVisualElement.Add(_gui));
                        // Only the overlays are captured; the camera's frame goes under them.
                        _host.Content.parent.style.backgroundColor = Color.clear;
                    }
                    if (_frame == null) _frame = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
                    return true;
                }
                catch (Exception x)
                {
                    _broken = true;
                    Debug.LogWarning("[ButtonSmoke] pictures carry no Scene view overlays: " + x.Message);
                    return false;
                }
            }

            static void OnGui()
            {
                if (_view == null) return;
                var e = Event.current;
                var rect = new Rect(0, 0, W, H);
                if (Test && e.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(new Rect(0, 0, 20, 20), Color.red);
                    EditorGUI.DrawRect(new Rect(W - 20, 0, 20, 20), Color.green);
                    EditorGUI.DrawRect(new Rect(0, H - 20, 20, 20), Color.blue);
                    EditorGUI.DrawRect(new Rect(W - 20, H - 20, 20, 20), Color.yellow);
                }
                Handles.SetCamera(rect, _view.camera);
                if (Test && e.type == EventType.Repaint)
                {
                    var c = _view.camera;
                    var mid = c.transform.position + c.transform.forward * 2f;
                    Handles.color = Color.cyan;
                    Handles.DrawLine(mid - c.transform.right * 0.2f, mid + c.transform.right * 0.2f);
                    Debug.Log($"[ButtonSmoke] shot: Screen {Screen.width}x{Screen.height}, ppp {EditorGUIUtility.pixelsPerPoint}, " +
                              $"gui {_gui.worldBound} content {_gui.contentRect}, host {_host.Content.worldBound}, " +
                              $"cam pixelRect {c.pixelRect}, centre at {HandleUtility.WorldToGUIPoint(mid)}, " +
                              $"GUI.matrix {GUI.matrix.GetRow(0)} {GUI.matrix.GetRow(1)}, colour space {QualitySettings.activeColorSpace}");
                }
                // Placed here, through the same projection the handlers read it back with.
                if (_aim.HasValue && e.isMouse) e.mousePosition = HandleUtility.WorldToGUIPoint(_aim.Value);
                var during = typeof(SceneView).GetField("duringSceneGui", UiOffscreenProbe.Any)?.GetValue(null) as Action<SceneView>;
                during?.Invoke(_view);
            }

            // What the camera sees with every overlay laid over it, rows bottom-up. The view's
            // camera takes this one's place, so a handler reading view.camera agrees with it.
            // The frame is read straight back rather than drawn through the panel, which
            // lightens it: the overlays alone are captured, on a clear ground, and laid on top.
            public static Color32[] Shoot(Camera cam)
            {
                if (!Ready()) return null;
                var target = cam.targetTexture;
                var was = RenderTexture.active;
                cam.targetTexture = _frame;
                cam.Render();
                cam.targetTexture = target;
                RenderTexture.active = _frame;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                RenderTexture.active = was;
                var pixels = tex.GetPixels32();
                Object.DestroyImmediate(tex);

                _view.camera.CopyFrom(cam);
                _view.camera.targetTexture = null;
                _host.Pump(1);
                tex = UiOffscreenProbe.Capture(_host.Panel, W, H, Color.clear, alpha: true);
                var over = tex.GetPixels32();
                Object.DestroyImmediate(tex);
                for (int i = 0; i < pixels.Length; i++)
                    if (over[i].a > 0) pixels[i] = Color32.Lerp(pixels[i], over[i], over[i].a / 255f);
                return pixels;
            }

            // A left-button mouse event over a point in the world, as seen by the last Shoot.
            public static bool Send(EventType type, Vector3 at)
            {
                if (!Ready()) return false;
                var handle = typeof(IMGUIContainer).GetMethod("HandleIMGUIEvent", UiOffscreenProbe.Any, null, new[] { typeof(Event), typeof(bool) }, null);
                if (handle == null) return false;
                _aim = at;
                try
                {
                    handle.Invoke(_gui, new object[] { new Event { type = type, button = 0, mousePosition = new Vector2(W / 2f, H / 2f) }, false });
                }
                finally
                {
                    _aim = null;
                }
                return true;
            }

            // Before Play mode: a domain reload would keep the hidden SceneView and lose the handle on it.
            public static void Dispose()
            {
                _host?.Dispose();
                if (_view != null) Object.DestroyImmediate(_view);
                if (_frame != null) _frame.Release();
                _host = null;
                _view = null;
                _frame = null;
            }

            // Frames side by side, rows bottom-up; written to pics/.
            public static string SaveStrip(string id, params Color32[][] frames)
            {
                Directory.CreateDirectory(PicturesDir);
                var tex = new Texture2D(W * frames.Length, H, TextureFormat.RGBA32, false);
                for (int i = 0; i < frames.Length; i++)
                    if (frames[i] != null) tex.SetPixels32(i * W, 0, W, H, frames[i]);
                tex.Apply(false);
                string name = $"{++PictureCount:0000} {Regex.Replace(id, @"[^A-Za-z0-9 #-]+", "_")}";
                if (name.Length > 120) name = name.Substring(0, 120);
                string file = Path.Combine(PicturesDir, name + ".png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                return "pics/" + Path.GetFileName(file);
            }
        }

        static string UiText(VisualElement root)
        {
            var sb = new StringBuilder();
            foreach (var e in root.Query<VisualElement>().ToList())
            {
                sb.Append(e.GetType().Name).Append('|').Append(e.name).Append('|');
                if (e is TextElement t) sb.Append(t.text);
                if (e is INotifyValueChanged<bool> b) sb.Append("=").Append(b.value);
                if (e is INotifyValueChanged<float> f) sb.Append("=").Append(f.value.ToString("0.###"));
                if (e is INotifyValueChanged<int> n) sb.Append("=").Append(n.value);
                if (e is INotifyValueChanged<string> str && !(e is TextElement)) sb.Append("=").Append(str.value);
                sb.Append('|').Append(e.enabledSelf ? "" : "off");
                sb.Append('|').Append(e.resolvedStyle.display == DisplayStyle.None ? "hidden" : "");
                foreach (var cls in e.GetClasses()) if (cls.StartsWith("ab-", StringComparison.Ordinal)) sb.Append(' ').Append(cls);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        static int LineDelta(string a, string b)
        {
            var x = a.Split('\n');
            var y = b.Split('\n');
            var count = new Dictionary<string, int>();
            foreach (var l in x) count[l] = count.TryGetValue(l, out int k) ? k + 1 : 1;
            int same = 0;
            foreach (var l in y) if (count.TryGetValue(l, out int k) && k > 0) { count[l] = k - 1; same++; }
            return x.Length + y.Length - 2 * same;
        }

        static int HashObject(GameObject go, Dictionary<int, int> materials)
        {
            unchecked
            {
                int h = go.activeSelf ? 17 : 19;
                var t = go.transform;
                h = h * 31 + Round(t.localPosition).GetHashCode();
                h = h * 31 + Round(t.localEulerAngles).GetHashCode();
                h = h * 31 + Round(t.localScale).GetHashCode();
                foreach (var c in go.GetComponents<Component>())
                {
                    if (c == null) { h = h * 31 + 7; continue; }
                    if (c is Transform) continue;
                    // Serialized state: a preview flag, a cloth setting, a renderer's materials.
                    string json;
                    try { json = EditorJsonUtility.ToJson(c); }
                    catch { json = c.GetType().Name; }
                    h = h * 31 + json.GetHashCode();
                    if (c is Renderer r)
                    {
                        h = h * 31 + (r.HasPropertyBlock() ? 1 : 0);
                        foreach (var m in r.sharedMaterials)
                            if (m != null && !materials.ContainsKey(m.GetInstanceID())) materials[m.GetInstanceID()] = HashMaterial(m);
                    }
                }
                return h;
            }
        }

        static int HashMaterial(Material m)
        {
            unchecked
            {
                int h = m.shader != null ? m.shader.name.GetHashCode() : 0;
                h = h * 31 + string.Join(" ", m.shaderKeywords).GetHashCode();
                foreach (var n in m.GetPropertyNames(MaterialPropertyType.Float)) h = h * 31 + m.GetFloat(n).GetHashCode();
                foreach (var n in m.GetPropertyNames(MaterialPropertyType.Int)) h = h * 31 + m.GetInteger(n);
                foreach (var n in m.GetPropertyNames(MaterialPropertyType.Vector)) h = h * 31 + m.GetVector(n).GetHashCode();
                foreach (var n in m.GetPropertyNames(MaterialPropertyType.Texture))
                {
                    var tex = m.GetTexture(n);
                    h = h * 31 + (tex != null ? tex.GetInstanceID() : 0);
                }
                return h;
            }
        }

        static Vector3 Round(Vector3 v) => new Vector3(Mathf.Round(v.x * 1e4f), Mathf.Round(v.y * 1e4f), Mathf.Round(v.z * 1e4f));

        static string PathOf(GameObject go)
        {
            var parts = new List<string>();
            for (var t = go.transform; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        // ---- finding controls --------------------------------------------------------------

        // Every card open, so no button hides in a collapsed body. Open is the programmatic
        // switch: what a person last folded stays remembered.
        static void Expand(UiOffscreenProbe.Mounted m)
        {
            foreach (var card in m.Content.Query<BridgeElements.Card>().ToList()) card.Open = true;
            foreach (var fold in m.Content.Query<Foldout>().ToList()) fold.value = true;
            m.Pump(1);
        }

        static List<string> Controls(VisualElement root, out List<string> sampledOut)
        {
            var ids = new List<string>();
            var perPath = new Dictionary<string, int>();
            sampledOut = new List<string>();
            foreach (var e in root.Query<VisualElement>().Where(IsControl).ToList())
            {
                if (!Shown(e, root)) continue;
                string id = Id(e);
                string path = id.Substring(0, id.LastIndexOf('#'));
                perPath[path] = perPath.TryGetValue(path, out int n) ? n + 1 : 1;
                if (perPath[path] > SamePerPath)
                {
                    if (perPath[path] == SamePerPath + 1) sampledOut.Add(path + " (further rows not pressed)");
                    continue;
                }
                ids.Add(id);
            }
            return ids;
        }

        static VisualElement Find(VisualElement root, string id) =>
            root.Query<VisualElement>().Where(v => IsControl(v) && Shown(v, root) && Id(v) == id).First();

        // A foldout's header is a Toggle of its own; Expand keeps those open.
        static bool IsControl(VisualElement v) => v is Button || (v is Toggle && !v.ClassListContains(Foldout.toggleUssClassName));

        static bool Shown(VisualElement e, VisualElement root)
        {
            for (var p = e; p != null && p != root.parent; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None || p.ClassListContains("ab-hidden")) return false;
            return true;
        }

        // "Card / Nested card / Label #n": stable across a rebuild, readable in the report.
        static string Id(VisualElement e)
        {
            var cards = CardsOf(e);
            string kind = e is Toggle ? "[toggle] " : "";
            string path = string.Join(" / ", cards.Append(kind + Label(e)));
            int n = 0;
            var root = e;
            while (root.parent != null) root = root.parent;
            foreach (var other in root.Query<VisualElement>().Where(IsControl).ToList())
            {
                if (other == e) break;
                if (Label(other) == Label(e) && CardsOf(other).SequenceEqual(cards)) n++;
            }
            return path + " #" + n;
        }

        static List<string> CardsOf(VisualElement e)
        {
            var cards = new List<string>();
            for (var p = e.parent; p != null; p = p.parent)
                if (p is BridgeElements.Card card) cards.Add(card.Title);
            cards.Reverse();
            return cards;
        }

        static string Label(VisualElement e)
        {
            if (e is Toggle t) return !string.IsNullOrEmpty(t.label) ? t.label : !string.IsNullOrEmpty(t.text) ? t.text : t.tooltip ?? "(unnamed toggle)";
            if (e is Button b && !string.IsNullOrEmpty(b.text)) return b.text.Trim();
            string inner = string.Join(" ", e.Query<Label>().ToList().Select(l => l.text).Where(s => !string.IsNullOrEmpty(s)));
            return inner.Length > 0 ? inner.Trim() : !string.IsNullOrEmpty(e.tooltip) ? e.tooltip : !string.IsNullOrEmpty(e.name) ? e.name : "(unnamed)";
        }

        // ---- bookkeeping -------------------------------------------------------------------

        // A press that opened a window leaves it out of the next press's picture.
        static void CloseStrays()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                string name = w.GetType().FullName ?? "";
                if (name.StartsWith("AvatarBridge.", StringComparison.Ordinal) || name.EndsWith("PropertyEditor", StringComparison.Ordinal))
                {
                    try { w.Close(); } catch { Object.DestroyImmediate(w); }
                }
            }
        }

        static Type TypeNamed(string name) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);

        static void Row(Target t, string control, string verdict, string detail) =>
            Rows.Add(string.Join("\t", t.Scene, t.Key, t.Pass, control, verdict, (detail ?? "").Replace('\t', ' ').Replace('\n', ' ')));

        static void Finish(string outFile)
        {
            if (AllTouched.Count > 0) File.AppendAllLines(Path.ChangeExtension(outFile, ".assets.txt"), AllTouched.OrderBy(a => a));
            var lines = File.ReadAllLines(outFile).Skip(1).Select(l => l.Split('\t')).Where(p => p.Length >= 5).ToList();
            var bad = new[] { "ERROR", "NO HANDLER", "DEAD", "SELECT ONLY", "GONE" };
            var counts = lines.GroupBy(p => p[4]).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}");
            int failures = lines.Count(p => bad.Contains(p[4]));
            Debug.Log($"[ButtonSmoke] {lines.Count} rows: {string.Join(", ", counts)}. " +
                      (failures == 0 ? "Every pressed control did something." : $"{failures} need a look.") + " " + outFile);
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }
}
#endif
