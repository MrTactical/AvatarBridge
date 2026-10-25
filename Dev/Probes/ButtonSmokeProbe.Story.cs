// A user's story, pressed through the same windows a person uses, with a picture of each step.
// It checks that each button and control does its job in the order a person meets them. How far the
// thighs move under a press or in motion is noted, never judged: a count of moved pixels cannot
// tell a good squash from a torn one, and a threshold on it failed for reasons outside the story.
//   -bsStory thighs -bsScenes <scene> -bsOut <file.tsv>
// "thighs": somebody wants their thighs to squash and jiggle. Convert with Soft body shape, Soft body
// physics and Keep the converted avatar a prefab on; in Squash regions keep the
// chest pair and repaint the second pair onto the thighs from their jiggle bones, then one brush
// stroke; rebuild, turn Motion Style to anime, test it in the Avatar Tester with Squeeze, Bounce and
// Test hand, a knob moved meanwhile showing on the running preview; apply the
// prefab overrides; then in Play mode Walk, Jump and Shake in the Interaction lab while the thighs
// are filmed and their jiggle measured. Pictures in <out>/pics, film frames in <out>/film/<move>.
// The converter's saved settings are machine-wide EditorPrefs: a new user's defaults are used and
// the saved ones put back after, with a copy in <out>/converter-settings-backup.json.
#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    public static partial class ButtonSmokeProbe
    {
        const string StoryKey = "ButtonSmokeProbe.story";
        const string PrefsBackupKey = "ButtonSmokeProbe.story.prefs";
        const int StoryHeight = 1400, ShotSize = 480;

        [InitializeOnLoadMethod]
        static void ResumeStory()
        {
            string pending = SessionState.GetString(StoryKey, "");
            if (pending.Length > 0) EditorApplication.delayCall += () => Drive(StoryPlay(pending));
        }

        static Target StoryTarget(string scene, string pass) => new Target { Scene = scene, Key = "story", Pass = pass };

        static void Step(Target t, string step, bool ok, string detail)
        {
            Row(t, step, ok ? "OK" : "DEAD", detail);
            Debug.Log($"[ButtonSmoke] story {t.Pass} {step}: {(ok ? "OK" : "DEAD")} {detail}");
        }

        static void Note(Target t, string step, string detail)
        {
            Row(t, step, "NOTE", detail);
            Debug.Log($"[ButtonSmoke] story {t.Pass} {step}: NOTE {detail}");
        }

        // ---- edit mode ---------------------------------------------------------------------

        static IEnumerator StoryEdit(string scene, string outFile)
        {
            File.WriteAllText(outFile, "scene\twindow\tpass\tcontrol\tverdict\tdetail\n");
            var t = StoryTarget(scene, "edit");
            string dir = Path.GetDirectoryName(outFile) ?? ".";
            BackUpPrefs(dir);

            Load(scene, out var source, out var already);
            if (source == null) { Step(t, "open the scene", false, "no VRChat avatar in it"); yield return StoryEnd(outFile); yield break; }
            Step(t, "open the scene", true, $"avatar \"{source.name}\"" + (already != null ? $", and a converted \"{already.name}\" already in it" : ""));

            // 1. Pick it in the converter and analyse, as step 1 of the window asks.
            var windowType = TypeNamed("AvatarBridge.AvatarBridgeWindow");
            var avatarField = windowType.GetField("avatar", UiOffscreenProbe.Any);
            var converter = UiOffscreenProbe.Mount(windowType, Width, StoryHeight, false,
                w => avatarField.SetValue(w, source.GetComponent(avatarField.FieldType)));
            yield return Settle(converter);
            var analyse = converter.Content.Query<Button>().ToList().FirstOrDefault(b => Label(b) == "Analyse this avatar");
            if (analyse == null) { Step(t, "Analyse this avatar", false, "no such button"); yield return StoryEnd(outFile); yield break; }
            Press(analyse, out _);
            yield return Settle(converter, 2.0);
            Step(t, "Analyse this avatar", true, Clip(Notices(converter.Content)));
            WindowShot(converter, dir, "01 converter analysed", null);

            // 2. The three options a person wanting soft thighs would turn on.
            foreach (var label in new[] { "Soft body shape: squash where parts press together", "Soft body physics: keep parts from passing through", "Keep the converted avatar a prefab" })
            {
                var toggle = converter.Content.Query<Toggle>().ToList().FirstOrDefault(x => Label(x) == label);
                if (toggle == null) { Step(t, "turn on: " + label, false, "no such toggle"); continue; }
                if (!toggle.value) Press(toggle, out _);
                yield return Settle(converter);
                toggle = converter.Content.Query<Toggle>().ToList().FirstOrDefault(x => Label(x) == label);
                Step(t, "turn on: " + label, toggle != null && toggle.value, toggle != null && toggle.value ? "on" : "would not stay on");
            }
            var squashToggle = converter.Content.Query<Toggle>().ToList().FirstOrDefault(x => Label(x) == "Soft body shape: squash where parts press together");
            WindowShot(converter, dir, "02 converter options", squashToggle);

            // 3. Convert, and wait the way a person watches "Converting...".
            var convert = converter.Content.Query<Button>().ToList().FirstOrDefault(b => Label(b).StartsWith("Convert \"", StringComparison.Ordinal));
            if (convert == null) { Step(t, "Convert", false, "no Convert button"); yield return StoryEnd(outFile); yield break; }
            var converting = windowType.GetField("converting", UiOffscreenProbe.Any);
            var lastReport = windowType.GetField("lastReport", UiOffscreenProbe.Any);
            double began = EditorApplication.timeSinceStartup;
            Press(convert, out _);
            while (EditorApplication.timeSinceStartup - began < 1200.0 &&
                   ((bool)converting.GetValue(converter.Window) || lastReport.GetValue(converter.Window) == null))
            {
                converter.Pump(1);
                yield return null;
            }
            var report = lastReport.GetValue(converter.Window) as BridgeReport;
            if (report == null || report.ConvertedRoot == null) { Step(t, "Convert", false, "no report or no converted avatar after 20 minutes"); yield return StoryEnd(outFile); yield break; }
            yield return Settle(converter, 1.0);
            var counts = report.Entries.GroupBy(e => e.Status).Select(g => $"{g.Key} {g.Count()}");
            Step(t, "Convert", true, $"{EditorApplication.timeSinceStartup - began:0}s, {string.Join(", ", counts)}");
            foreach (var e in report.Entries.Where(e => Mentions(e, "squash") || Mentions(e, "thigh") || Mentions(e, "soft bod")).Take(12))
                Note(t, "report row", $"{e.Status} [{e.Category}] {e.Subject}: {e.Detail}");
            WindowShot(converter, dir, "03 converter report", converter.Content.Query<BridgeElements.Card>().ToList().LastOrDefault());
            converter.Dispose();

            var avatar = report.ConvertedRoot;
            var root = avatar.transform;
            // Play mode finds the avatar as the one active CVRAvatar, and the pictures want it alone.
            if (source != null && source.activeSelf) source.SetActive(false);
            if (already != null && already != avatar && already.activeSelf) already.SetActive(false);

            // What the conversion should have made.
            var status = PrefabUtility.GetPrefabInstanceStatus(avatar);
            Step(t, "converted avatar is linked to its prefab", status == PrefabInstanceStatus.Connected,
                $"{status}, {AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(avatar))}");
            var thighL = Deep(root, "Thigh Jiggle L");
            var thighR = Deep(root, "Thigh Jiggle R");
            if (thighL == null || thighR == null) { Step(t, "thigh jiggle bones", false, "no Thigh Jiggle L/R on the converted avatar"); yield return StoryEnd(outFile); yield break; }
            var cloths = root.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "MagicaCloth").ToList();
            var thighCloths = cloths.Where(c => c.name.IndexOf("Thigh", StringComparison.OrdinalIgnoreCase) >= 0).Select(c => c.name).ToList();
            Step(t, "thigh jiggle became cloth", thighCloths.Count >= 2, $"{thighCloths.Count} of {cloths.Count} cloths: {string.Join(", ", thighCloths)}");
            var settings = YapsSquash.SettingsOf(root);
            Step(t, "squash built", YapsSquash.Has(root) && settings != null,
                $"{YapsSquash.Materials(root).Count} squashed material(s), component {(settings != null ? "added" : "missing")}");
            if (settings == null) { yield return StoryEnd(outFile); yield break; }
            ThighShot(avatar, thighL, thighR, dir, "04 thighs converted at rest", null);

            // 4. Squash regions: the bone names give chest and seat, two pairs is the most, so the
            // seat pair gives way to the thighs.
            var painterType = TypeNamed("AvatarBridge.YapsSquashPainter");
            var painter = UiOffscreenProbe.Mount(painterType, Width, StoryHeight, false,
                w => painterType.GetMethod("Pick", UiOffscreenProbe.Any).Invoke(w, new object[] { avatar }));
            yield return Settle(painter);
            yield return PressNamed(t, painter, "Change these areas");
            Pairs(t, settings, "after Change these areas");
            // It starts painting at once, from what the bone names gave.
            bool painting = (bool) painterType.GetProperty("IsPainting", UiOffscreenProbe.Any).GetValue(painter.Window);
            Step(t, "Change these areas starts painting", painting, painting ? "painting" : "not painting");
            if (painting) yield return PressNamed(t, painter, "Stop painting");
            if (settings.pairs.Count < 2) yield return PressNamed(t, painter, "Add a pair");
            else
            {
                // Two pairs is the cap, and the button says so by greying out.
                var add = painter.Content.Query<Button>().ToList().FirstOrDefault(b => Label(b) == "Add a pair" && Shown(b, painter.Content));
                Step(t, "Add a pair greyed at two pairs", add != null && !add.enabledInHierarchy, add == null ? "no Add a pair button" : add.enabledInHierarchy ? "pressable" : "greyed");
            }
            var pairPopup = Field<PopupField<string>>(painter.Content, "Pair");
            if (pairPopup != null && settings.pairs.Count >= 2)
            {
                pairPopup.index = 1;
                yield return Settle(painter);
                yield return PressNamed(t, painter, "Remove this pair");
                yield return PressNamed(t, painter, "Add a pair");
            }
            else if (settings.pairs.Count < 2)
            {
                yield return PressNamed(t, painter, "Add a pair");
            }
            yield return Unfold(painter, "More for this pair");
            var name = Field<TextField>(painter.Content, "Name");
            if (name != null) { name.value = "Thighs"; yield return Settle(painter); }
            Pairs(t, settings, "after naming");
            int seat = settings.pairs.Count - 1;
            if (seat < 0) { Step(t, "a pair to paint", false, "none after Add a pair"); painter.Dispose(); yield return StoryEnd(outFile); yield break; }
            for (int side = 0; side < 2; side++)
            {
                yield return Unfold(painter, "More for this pair");
                var sideGroup = Field<RadioButtonGroup>(painter.Content, "Side");
                if (sideGroup == null) { Step(t, "Side", false, "no Side choice"); break; }
                sideGroup.value = side;
                yield return Settle(painter);
                painterType.GetField("_bone", UiOffscreenProbe.Any).SetValue(painter.Window, side == 0 ? thighL : thighR);
                yield return Unfold(painter, "More for this pair");
                yield return PressNamed(t, painter, "The bone");
                // Build measures a part on the skin painted at half or more, and wants 20 of it.
                int whole = Painted(painter, 2 * seat + side, 0.5f);
                Step(t, $"painted the thigh, {(side == 0 ? "left" : "right")}", whole >= 20, $"{Painted(painter, 2 * seat + side)} vertices, {whole} at half or more");
            }

            // One brush stroke down the inside of the left thigh, as a person tidies a seed.
            var sideNow = Field<RadioButtonGroup>(painter.Content, "Side");
            if (sideNow != null) { sideNow.value = 0; yield return Settle(painter); }
            float beforeStroke = Weight(painter, 2 * seat);
            yield return PressNamed(t, painter, "Paint in the Scene view");
            var cam = ThighCamera(avatar, thighL, thighR, 0f);
            SceneShot.Shoot(cam);
            var inner = Vector3.Lerp(thighL.position, thighR.position, 0.3f);
            var points = Surface(avatar, painterType.GetField("_body", UiOffscreenProbe.Any)?.GetValue(painter.Window) as SkinnedMeshRenderer, inner, root.up, 0.12f, 6);
            if (points.Count > 1)
            {
                SceneShot.Send(EventType.MouseDown, points[0]);
                foreach (var p in points.Skip(1)) SceneShot.Send(EventType.MouseDrag, p);
                SceneShot.Send(EventType.MouseUp, points.Last());
            }
            yield return Settle(painter);
            float afterStroke = Weight(painter, 2 * seat);
            Step(t, "brush stroke on the inner left thigh", afterStroke != beforeStroke, $"painted weight {beforeStroke:0} -> {afterStroke:0} ({points.Count} points on the skin)");
            // The brush's tools sit in the Scene view panel while painting, Soften edge among them.
            var brush = new VisualElement();
            painterType.GetMethod("Brush", UiOffscreenProbe.Any).Invoke(painter.Window, new object[] { brush });
            var soften = brush.Query<Button>().ToList().FirstOrDefault(b => Label(b) == "Soften edge");
            if (soften == null) Step(t, "Soften edge", false, "not in the brush panel");
            else
            {
                Press(soften, out bool softened);
                yield return Settle(painter);
                Step(t, "Soften edge", softened, $"painted weight {afterStroke:0} -> {Weight(painter, 2 * seat):0}");
            }
            yield return PressNamed(t, painter, "Stop painting");
            WindowShot(painter, dir, "05 squash regions", null);
            SceneShotSave(cam, dir, "06 regions on the thighs (red left, blue right, yellow the chest pair)");
            Object.DestroyImmediate(cam.gameObject);
            UpperLegNote(t, avatar, thighL);
            painter.Dispose();

            // 5. Rebuild from the regions, and the jiggle a person dials in: Motion Style to anime.
            UiOffscreenProbe.Inspector.Target = settings;
            var inspector = UiOffscreenProbe.Mount(typeof(UiOffscreenProbe.Inspector), Width, StoryHeight, false);
            yield return Settle(inspector);
            var build = inspector.Content.Query<Button>().ToList().FirstOrDefault(b => Label(b) == "Rebuild the squash" || Label(b) == "Build the squash");
            if (build == null) Step(t, "Rebuild", false, "no Build the squash or Rebuild the squash button");
            else
            {
                Press(build, out _);
                yield return Settle(inspector, 2.0);
                // A pair the build drops says so mid-notice, past where the clip cuts it.
                string said = Notices(inspector.Content);
                bool dropped = said.Contains("too little skin");
                Step(t, "Rebuild", YapsSquash.Has(root) && !dropped, (dropped ? "a pair dropped for too little skin: " : "") + Clip(said));
            }
            Pairs(t, settings, "built from");
            var motion = Field<Slider>(inspector.Content, "Motion Style");
            if (motion == null) Step(t, "Motion Style to 1 (anime)", false, "no Motion Style slider");
            else
            {
                motion.value = 1f;
                yield return Settle(inspector);
                Step(t, "Motion Style to 1 (anime)", Mathf.Approximately(settings.motionStyle, 1f), $"component reads {settings.motionStyle}, material _YAPS_SquashMotion {Motion(root)}");
            }
            WindowShot(inspector, dir, "07 squash component", null);

            // 6. The edit-mode preview, run from the Avatar Tester alone: the inspector's button
            // opens it there with the preview on. Squeeze, Test hand on the thigh, Bounce.
            Selection.activeGameObject = avatar.gameObject;
            yield return PressNamed(t, inspector, "Test in the Avatar Tester");
            Step(t, "Test in the Avatar Tester starts the preview", YapsSquashPreview.On == settings, YapsSquashPreview.On == null ? "no preview running" : "running");
            var testerType = TypeNamed("AvatarBridge.CckAnimatorTester");
            var testerWindow = UiOffscreenProbe.Mount(testerType, Width, StoryHeight, false);
            yield return Settle(testerWindow);
            // Preview starts half squeezed; the rest picture is the part with no hand on it.
            var squeeze = Field<Slider>(testerWindow.Content, "Squeeze");
            if (squeeze != null) { squeeze.value = 0f; yield return Ticks(0.5, null); }
            var rest = ThighPixels(avatar, thighL, thighR);
            if (squeeze != null)
            {
                squeeze.value = 1f;
                yield return Ticks(0.5, null);
                var pressed = ThighPixels(avatar, thighL, thighR);
                Note(t, "Squeeze 1", $"{Share(rest, pressed):P1} of the thigh picture moved");
                SaveSheet(dir, "08 squeeze off, on", rest, pressed);
                squeeze.value = 0f;
                yield return Ticks(0.5, null);
            }
            else Step(t, "Squeeze", false, "no Squeeze slider after Preview");
            var hand = testerWindow.Content.Query<Toggle>().ToList().FirstOrDefault(x => Label(x) == "Test hand");
            if (hand != null)
            {
                Press(hand, out _);
                PutHand(root, OnThigh(avatar, thighL));
                yield return Ticks(0.5, null);
                var dented = ThighPixels(avatar, thighL, thighR);
                Note(t, "Test hand aims at", HandNote(root));
                Note(t, "Test hand on the left thigh",
                    $"{Share(rest, dented, Legs):P1} of the legs moved ({Share(rest, dented):P1} of the whole picture)");
                SaveSheet(dir, "09 test hand off, on", rest, dented);
                Press(hand, out _);
            }
            else Step(t, "Test hand", false, "no Test hand toggle after Preview");
            var bounce = testerWindow.Content.Query<Toggle>().ToList().FirstOrDefault(x => Label(x) == "Bounce");
            if (bounce != null)
            {
                Press(bounce, out _);
                var frames = new List<Color32[]>();
                string film = Path.Combine(dir, "film", "preview-bounce");
                Directory.CreateDirectory(film);
                for (int f = 0; f < 45; f++)
                {
                    yield return Ticks(1.0 / 30.0, null);
                    var px = ThighPixels(avatar, thighL, thighR, Path.Combine(film, $"f{f:0000}.png"));
                    frames.Add(px);
                }
                float most = frames.Max(px => Share(rest, px));
                Note(t, "Bounce", $"up to {most:P1} of the thigh picture moved over 1.5 s");
                Press(bounce, out _);
            }
            else Step(t, "Bounce", false, "no Bounce toggle after Preview");
            // A knob moved in the inspector reaches the running preview, which carries on.
            var strength = Field<Slider>(inspector.Content, "Strength");
            if (strength == null) Step(t, "Strength moved while previewing", false, "no Strength slider");
            else
            {
                float was = strength.value;
                strength.value = 0.25f;
                yield return Settle(inspector);
                float read = YapsSquash.Materials(root).FirstOrDefault()?.GetVector("_YAPS_SquashTune").y ?? -1f;
                Step(t, "Strength moved while previewing", Mathf.Approximately(read, 0.25f) && YapsSquashPreview.On == settings,
                    $"material strength {read}, preview {(YapsSquashPreview.On == settings ? "still running" : "stopped")}");
                strength.value = was;
                yield return Settle(inspector);
            }
            yield return PressNamed(t, testerWindow, "Stop the preview");
            testerWindow.Dispose();
            inspector.Dispose();

            // 7. Keep the converted avatar a prefab: the changes since converting apply to it.
            bool overrides = PrefabUtility.HasPrefabInstanceAnyOverrides(avatar, false);
            if (status == PrefabInstanceStatus.Connected)
            {
                PrefabUtility.ApplyPrefabInstance(avatar, InteractionMode.AutomatedAction);
                var asset = PrefabUtility.GetCorrespondingObjectFromSource(avatar);
                var kept = asset != null ? asset.GetComponentInChildren<YapsSquashSettings>(true) : null;
                Step(t, "Overrides > Apply All", kept != null && kept.pairs.Select(p => p.name).Contains("Thighs") && Mathf.Approximately(kept.motionStyle, 1f),
                    $"overrides before: {overrides}; the prefab now holds {(kept != null ? string.Join(", ", kept.pairs.Select(p => p.name)) + $", Motion Style {kept.motionStyle}" : "no squash component")}");
            }

            // 8. Into Play mode through the tester, as its own button does it.
            Selection.activeGameObject = avatar;
            var tester = UiOffscreenProbe.Mount(TypeNamed("AvatarBridge.CckAnimatorTester"), Width, StoryHeight, false);
            yield return Settle(tester);
            var enter = tester.Content.Query<Button>().ToList().FirstOrDefault(b => Label(b) == "Enter Play mode");
            if (enter == null) { Step(t, "Enter Play mode", false, "no such button"); tester.Dispose(); yield return StoryEnd(outFile); yield break; }
            File.AppendAllLines(outFile, Rows);
            Rows.Clear();
            SceneShot.Dispose();
            SessionState.SetString(StoryKey, outFile + "|" + scene);
            Press(enter, out _);
            tester.Dispose();
            for (int i = 0; i < 600 && !EditorApplication.isPlaying; i++) yield return null;
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.EraseString(StoryKey);
                Step(t, "Enter Play mode", false, "Play mode never started");
                yield return StoryEnd(outFile);
            }
            else if (SessionState.GetString(StoryKey, "").Length > 0)
                yield return StoryPlay(SessionState.GetString(StoryKey, ""));
        }

        // ---- play mode ---------------------------------------------------------------------

        static IEnumerator StoryPlay(string pending)
        {
            SessionState.EraseString(StoryKey);
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            var parts = pending.Split('|');
            string outFile = parts[0], scene = parts[1], dir = Path.GetDirectoryName(outFile) ?? ".";
            Setup(outFile);
            var t = StoryTarget(scene, "play");
            double start = EditorApplication.timeSinceStartup;
            while (!EditorApplication.isPlaying)
            {
                if (EditorApplication.timeSinceStartup - start > 180.0) { Step(t, "Enter Play mode", false, "never started"); yield return StoryEnd(outFile); yield break; }
                yield return null;
            }
            Step(t, "Enter Play mode", true, "");
            foreach (var a in Object.FindObjectsOfType<Animator>()) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Time.captureFramerate = 60;
            // Cloth builds over the first seconds.
            for (int i = 0; i < 300; i++) yield return null;

            var avatar = Object.FindObjectsOfType<CVRAvatar>().FirstOrDefault()?.gameObject;
            var thighL = avatar != null ? Deep(avatar.transform, "Thigh Jiggle L") : null;
            var thighR = avatar != null ? Deep(avatar.transform, "Thigh Jiggle R") : null;
            if (thighL == null || thighR == null) { Step(t, "find the thighs", false, "no converted avatar or thigh bones in Play mode"); yield return StoryEnd(outFile); yield break; }
            Selection.activeGameObject = avatar;
            var tester = UiOffscreenProbe.Mount(TypeNamed("AvatarBridge.CckAnimatorTester"), Width, StoryHeight, false);
            yield return Settle(tester, PlaySettleSeconds);
            var lab = CardTitled(tester.Content, "Interaction lab");
            WindowShot(tester, dir, "10 tester in Play mode", lab);

            Film.Begin(avatar, thighL, thighR);
            yield return Film.Run("rest", 60, dir);
            foreach (var move in new[] { "Walk", "Jump", "Shake" })
            {
                var button = tester.Content.Query<Button>().ToList().FirstOrDefault(b => Label(b) == move);
                if (button == null) { Step(t, move, false, "no such motion in the Interaction lab"); continue; }
                Press(button, out _);
                yield return Film.Run(move.ToLowerInvariant(), 180, dir);
                Note(t, move,
                    $"thigh jiggle tips moved up to {Film.Peak * 100f:0.0} cm against the leg (left {Film.PeakL * 100f:0.0}, right {Film.PeakR * 100f:0.0}), a chain {Film.Reach * 100f:0.0} cm long; " +
                    $"the thighs' squash slots moved up to {Film.SlotPeak * 100f:0.0} cm");
                var stop = tester.Content.Query<Button>().ToList().FirstOrDefault(b => Label(b) == "Stop");
                if (stop != null) Press(stop, out _);
                for (int i = 0; i < 90; i++) yield return null;
            }

            // The squash preview in the lab: another player's hand on the thigh, with real cloth.
            var preview = CardTitled(tester.Content, "Squash")?.Query<Button>().ToList().FirstOrDefault(b => Label(b) == "Preview");
            if (preview != null)
            {
                Press(preview, out _);
                yield return Settle(tester, PlaySettleSeconds);
                var hand = CardTitled(tester.Content, "Squash")?.Query<Toggle>().ToList().FirstOrDefault(x => Label(x) == "Test hand");
                var squeeze = Field<Slider>(CardTitled(tester.Content, "Squash"), "Squeeze");
                if (squeeze != null) { squeeze.value = 0f; for (int i = 0; i < 30; i++) yield return null; }
                Note(t, "lab Squeeze before the hand", squeeze == null ? "no Squeeze slider" : $"{squeeze.value:0.##}, the preview's own {TypeNamed("AvatarBridge.YapsSquashPreview")?.GetField("Squeeze")?.GetValue(null)}");
                if (hand != null)
                {
                    // The cloth sways on its own, so the same wait without the hand is the floor.
                    var still = Film.Shot(null);
                    for (int i = 0; i < 30; i++) yield return null;
                    var before = Film.Shot(null);
                    float sway = Share(still, before, Legs);
                    Press(hand, out _);
                    PutHand(avatar.transform, OnThigh(avatar, thighL));
                    for (int i = 0; i < 30; i++) yield return null;
                    var after = Film.Shot(null);
                    Note(t, "lab Test hand aims at", HandNote(avatar.transform));
                    float pressed = Share(before, after, Legs);
                    Note(t, "lab Test hand on the left thigh",
                        $"{pressed:P1} of the legs moved with the hand, {sway:P1} over the same wait without it");
                    SaveSheet(dir, "11 lab test hand off, on", before, after);
                }
                else Step(t, "lab Test hand", false, "no Test hand toggle after Preview");
            }
            else Step(t, "lab squash Preview", false, "no Preview button in the Interaction lab");
            Film.End();
            tester.Dispose();
            yield return StoryEnd(outFile);
        }

        static IEnumerator StoryEnd(string outFile)
        {
            Time.captureFramerate = 0;
            RestorePrefs();
            File.AppendAllLines(outFile, Rows);
            Rows.Clear();
            Finish(outFile);
            yield break;
        }

        // ---- the film ----------------------------------------------------------------------

        // Rendered and measured at the end of each frame, after the cloth wrote its bones. The
        // camera rides the hips, so what moves in the picture is the flesh, not the jump.
        static class Film
        {
            static Camera _cam;
            static Transform _avatar, _hips, _l, _r, _tipL, _tipR, _legL, _legR;
            static Vector3 _offset, _restL, _restR;
            static Vector3[] _slotRest;
            static string _dir;
            static int _frames, _taken;
            static bool _rest;
            public static float Peak, PeakL, PeakR, Reach, SlotPeak;

            sealed class Step { }

            public static void Begin(GameObject avatar, Transform l, Transform r)
            {
                _avatar = avatar.transform;
                var animator = avatar.GetComponent<Animator>();
                _hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : _avatar;
                _legL = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg) : l.parent;
                _legR = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightUpperLeg) : r.parent;
                _l = l; _r = r;
                _tipL = Tip(l); _tipR = Tip(r);
                Reach = Vector3.Distance(l.position, _tipL.position);
                _cam = ThighCamera(avatar, l, r, 0f);
                _offset = Quaternion.Inverse(_hips.rotation) * (_cam.transform.position - _hips.position);
                Install(true);
            }

            public static void End()
            {
                Install(false);
                if (_cam != null) Object.DestroyImmediate(_cam.gameObject);
            }

            public static IEnumerator Run(string name, int frames, string dir)
            {
                _dir = Path.Combine(dir, "film", name);
                Directory.CreateDirectory(_dir);
                _rest = name == "rest";
                if (_rest) { _restL = Vector3.zero; _restR = Vector3.zero; _slotRest = null; }
                Peak = PeakL = PeakR = SlotPeak = 0f;
                _taken = 0;
                _frames = frames;
                while (_frames > 0) yield return null;
                if (_rest && _taken > 0) { _restL /= _taken; _restR /= _taken; }
            }

            static void Install(bool on)
            {
                var loop = PlayerLoop.GetCurrentPlayerLoop();
                for (int i = 0; i < loop.subSystemList.Length; i++)
                {
                    var sys = loop.subSystemList[i];
                    var list = (sys.subSystemList ?? new PlayerLoopSystem[0]).Where(s => s.type != typeof(Step)).ToList();
                    if (on && sys.type == typeof(UnityEngine.PlayerLoop.PostLateUpdate))
                        list.Add(new PlayerLoopSystem { type = typeof(Step), updateDelegate = Frame });
                    sys.subSystemList = list.ToArray();
                    loop.subSystemList[i] = sys;
                }
                PlayerLoop.SetPlayerLoop(loop);
            }

            static void Frame()
            {
                if (_frames <= 0 || _cam == null || _hips == null) return;
                _frames--;
                var l = _legL.InverseTransformPoint(_tipL.position);
                var r = _legR.InverseTransformPoint(_tipR.position);
                var slots = YapsSquash.Slots(_avatar);
                if (_rest)
                {
                    _restL += l; _restR += r;
                    if (_slotRest == null && slots != null) _slotRest = slots.Select(s => s != null ? _hips.InverseTransformPoint(s.position) : Vector3.zero).ToArray();
                }
                else
                {
                    float dl = (l - _restL).magnitude * _legL.lossyScale.x, dr = (r - _restR).magnitude * _legR.lossyScale.x;
                    PeakL = Mathf.Max(PeakL, dl); PeakR = Mathf.Max(PeakR, dr); Peak = Mathf.Max(PeakL, PeakR);
                    // Slots 2 and 3 are the second pair's, the thighs.
                    if (slots != null && _slotRest != null)
                        for (int s = 2; s < Mathf.Min(4, slots.Length); s++)
                            if (slots[s] != null) SlotPeak = Mathf.Max(SlotPeak, (_hips.InverseTransformPoint(slots[s].position) - _slotRest[s]).magnitude * _hips.lossyScale.x);
                }
                // 30 pictures a second of the 60 simulated.
                if (_taken++ % 2 == 0) Shot(Path.Combine(_dir, $"f{_taken / 2:0000}.png"));
            }

            public static Color32[] Shot(string file)
            {
                if (_cam == null) return null;
                _cam.transform.position = _hips.position + _hips.rotation * _offset;
                _cam.transform.LookAt((_l.position + _tipL.position + _r.position + _tipR.position) / 4f);
                return Render(_cam, file);
            }
        }

        // ---- pictures ----------------------------------------------------------------------

        static Camera ThighCamera(GameObject avatar, Transform l, Transform r, float yaw)
        {
            var go = new GameObject("Story camera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 1f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 50f;
            // The jiggle bones root at the hip; the flesh they move is down the chain.
            var mid = (l.position + Tip(l).position + r.position + Tip(r).position) / 4f;
            float span = Mathf.Max(Vector3.Distance(l.position, r.position), Vector3.Distance(l.position, Tip(l).position), 0.08f);
            var fwd = Vector3.ProjectOnPlane(avatar.transform.forward, Vector3.up);
            fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            var dir = Quaternion.AngleAxis(yaw, Vector3.up) * fwd;
            cam.transform.position = mid + dir * span * 3.5f + Vector3.up * span * 0.3f;
            cam.transform.LookAt(mid);
            return cam;
        }

        static Color32[] Render(Camera cam, string file)
        {
            var rt = RenderTexture.GetTemporary(ShotSize, ShotSize, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(ShotSize, ShotSize, TextureFormat.RGBA32, false);
            var was = RenderTexture.active;
            bool async = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, ShotSize, ShotSize), 0, 0);
                tex.Apply(false);
                if (file != null) File.WriteAllBytes(file, tex.EncodeToPNG());
                return tex.GetPixels32();
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
                RenderTexture.active = was;
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(tex);
            }
        }

        static Color32[] ThighPixels(GameObject avatar, Transform l, Transform r, string file = null)
        {
            var cam = ThighCamera(avatar, l, r, 0f);
            try { return Render(cam, file); }
            finally { Object.DestroyImmediate(cam.gameObject); }
        }

        static void ThighShot(GameObject avatar, Transform l, Transform r, string dir, string name, string _)
        {
            Directory.CreateDirectory(Path.Combine(dir, "pics"));
            foreach (var (yaw, side) in new[] { (0f, "front"), (40f, "three-quarter") })
            {
                var cam = ThighCamera(avatar, l, r, yaw);
                Render(cam, Path.Combine(dir, "pics", $"{name} {side}.png"));
                Object.DestroyImmediate(cam.gameObject);
            }
        }

        static void SceneShotSave(Camera cam, string dir, string name)
        {
            var px = SceneShot.Shoot(cam);
            if (px == null) return;
            var tex = new Texture2D(Picture.W, Picture.H, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply(false);
            Directory.CreateDirectory(Path.Combine(dir, "pics"));
            File.WriteAllBytes(Path.Combine(dir, "pics", name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static void SaveSheet(string dir, string name, Color32[] a, Color32[] b)
        {
            if (a == null || b == null) return;
            var tex = new Texture2D(ShotSize * 2, ShotSize, TextureFormat.RGBA32, false);
            tex.SetPixels32(0, 0, ShotSize, ShotSize, a);
            tex.SetPixels32(ShotSize, 0, ShotSize, ShotSize, b);
            tex.Apply(false);
            Directory.CreateDirectory(Path.Combine(dir, "pics"));
            File.WriteAllBytes(Path.Combine(dir, "pics", name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        // The bottom of a thigh picture, below the hem: pixels run from the bottom row up.
        const float Legs = 0.6f;

        static float Share(Color32[] a, Color32[] b, float rows = 1f)
        {
            if (a == null || b == null || a.Length != b.Length) return 0f;
            int n = (int)(a.Length * rows), moved = 0;
            for (int i = 0; i < n; i++)
                if (Math.Abs(a[i].r - b[i].r) > 12 || Math.Abs(a[i].g - b[i].g) > 12 || Math.Abs(a[i].b - b[i].b) > 12) moved++;
            return moved / (float)n;
        }

        // The window, scrolled to `at` with the cards around it open, in tiles.
        static void WindowShot(UiOffscreenProbe.Mounted m, string dir, string name, VisualElement at)
        {
            for (var p = at; p != null; p = p.parent)
                if (p is BridgeElements.Card card) card.Open = true;
            m.Pump(2);
            var scroll = m.Content.Q<ScrollView>();
            if (scroll != null)
            {
                if (at != null) scroll.ScrollTo(at);
                else scroll.scrollOffset = Vector2.zero;
            }
            m.Pump(2);
            var background = new Color(0.22f, 0.22f, 0.22f);
            Directory.CreateDirectory(Path.Combine(dir, "pics"));
            UiOffscreenProbe.Save(UiOffscreenProbe.Capture(m.Panel, Width, StoryHeight, background), Path.Combine(dir, "pics", name + ".png"), background);
        }

        // ---- the windows -------------------------------------------------------------------

        static IEnumerator PressNamed(Target t, UiOffscreenProbe.Mounted m, string label)
        {
            var button = m.Content.Query<Button>().ToList().FirstOrDefault(b => Label(b) == label && Shown(b, m.Content));
            if (button == null) { Step(t, label, false, "no such button showing"); yield break; }
            if (!button.enabledInHierarchy) { Step(t, label, false, "greyed out"); yield break; }
            string before = Notices(m.Content);
            Press(button, out bool handled);
            yield return Settle(m);
            string after = Notices(m.Content);
            Step(t, label, handled, after != before && after.Length > 0 ? Clip(after) : "pressed, no new notice");
        }

        static T Field<T>(VisualElement root, string label) where T : VisualElement =>
            root.Query<T>().ToList().FirstOrDefault(f => f.GetType().GetProperty("label")?.GetValue(f) as string == label);

        static string Notices(VisualElement root) =>
            string.Join(" | ", root.Query<BridgeElements.NoticeBox>().ToList().Where(n => Shown(n, root))
                .Select(n => string.Join(" ", n.Query<Label>().ToList().Select(l => l.text).Where(s => !string.IsNullOrEmpty(s)))));

        static string Clip(string s) => s.Length > 400 ? s.Substring(0, 400) + "..." : s;

        static bool Mentions(ReportEntry e, string what) =>
            (e.Category + " " + e.Subject + " " + e.Detail).IndexOf(what, StringComparison.OrdinalIgnoreCase) >= 0;

        static void Pairs(Target t, YapsSquashSettings s, string when) =>
            Note(t, "squash pairs " + when, s.pairs.Count == 0 ? "none painted (bone names)" : string.Join(", ", s.pairs.Select(p => $"{p.name} ({(p.a ?? new byte[0]).Count(x => x > 0)}/{(p.b ?? new byte[0]).Count(x => x > 0)} vertices)")));

        static int Painted(UiOffscreenProbe.Mounted painter, int channel, float least = 0.01f)
        {
            var w = painter.Window.GetType().GetField("_w", UiOffscreenProbe.Any)?.GetValue(painter.Window) as float[][];
            return w != null && channel < w.Length ? w[channel].Count(x => x >= least) : -1;
        }

        // A closed fold as a person opens it; the painter builds its folds closed on every rebuild.
        static IEnumerator Unfold(UiOffscreenProbe.Mounted m, string title)
        {
            var card = CardTitled(m.Content, title);
            if (card == null || card.Open) yield break;
            card.Open = true;
            yield return Settle(m);
        }

        static float Weight(UiOffscreenProbe.Mounted painter, int channel)
        {
            var w = painter.Window.GetType().GetField("_w", UiOffscreenProbe.Any)?.GetValue(painter.Window) as float[][];
            return w != null && channel < w.Length ? w[channel].Sum() : -1f;
        }

        static BridgeElements.Card CardTitled(VisualElement root, string title) =>
            root.Query<BridgeElements.Card>().ToList().FirstOrDefault(c => c.Title == title);

        static SkinnedMeshRenderer BodyOf(GameObject avatar) =>
            YapsSquash.SettingsOf(avatar.transform)?.body ?? avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null).OrderByDescending(r => r.sharedMesh.vertexCount).FirstOrDefault();

        // The front of the skin level with `bone`, where a person drops the hand on it.
        static Vector3 OnSkin(GameObject avatar, SkinnedMeshRenderer body, Transform bone)
        {
            var p = Surface(avatar, body, bone.position, Vector3.up, 0f, 1);
            return p.Count > 0 ? p[0] : bone.position;
        }

        static string Motion(Transform root)
        {
            var m = YapsSquash.Materials(root).FirstOrDefault();
            return m != null && m.HasProperty("_YAPS_SquashMotion") ? m.GetVector("_YAPS_SquashMotion").ToString("0.00") : "absent";
        }

        // Where a person lays a hand on the thigh: the front of the skin halfway down the jiggle
        // chain. The chain's root sits in the hip crease, where the region is barely painted.
        static Vector3 OnThigh(GameObject avatar, Transform jiggle)
        {
            var mid = (jiggle.position + Tip(jiggle).position) / 2f;
            var p = Surface(avatar, BodyOf(avatar), mid, Vector3.up, 0f, 1);
            return p.Count > 0 ? p[0] : mid;
        }

        // The wrist and how far it is from each part, so a dead press says which part it faced.
        static string HandNote(Transform root)
        {
            var wrist = root.TransformPoint((Vector3)(TypeNamed("AvatarBridge.YapsSquashPreview")?.GetField("_wrist", UiOffscreenProbe.Any)?.GetValue(null) ?? Vector3.zero));
            var slots = YapsSquash.Slots(root);
            if (slots == null) return "no slots";
            return string.Join(", ", Enumerable.Range(0, 4).Select(i => slots[i] == null || slots[i].lossyScale.sqrMagnitude <= 0f ? $"part {i} empty"
                : $"part {i} on {slots[i].parent.name} {Vector3.Distance(wrist, slots[i].position) * 100f:0.0} cm"));
        }

        // Where a person drags the test hand's handle: the wrist just in front of the skin at `at`.
        static void PutHand(Transform root, Vector3 at)
        {
            var preview = TypeNamed("AvatarBridge.YapsSquashPreview");
            var fwd = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
            preview?.GetField("_wrist", UiOffscreenProbe.Any)?.SetValue(null, root.InverseTransformPoint(at + fwd * 0.03f));
        }

        // Points on the body's skin nearest a line down from `from`, for a brush stroke.
        static List<Vector3> Surface(GameObject avatar, SkinnedMeshRenderer body, Vector3 from, Vector3 down, float length, int count)
        {
            body = body != null ? body : BodyOf(avatar);
            var points = new List<Vector3>();
            if (body == null) return points;
            var baked = new Mesh();
            body.BakeMesh(baked);
            var m = Matrix4x4.TRS(body.transform.position, body.transform.rotation, Vector3.one);
            var verts = baked.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray();
            Object.DestroyImmediate(baked);
            var fwd = Vector3.ProjectOnPlane(avatar.transform.forward, Vector3.up).normalized;
            for (int i = 0; i < count; i++)
            {
                var aim = from - down.normalized * length * (count > 1 ? i / (float)(count - 1) : 0f);
                // The front of the skin there, as the camera sees it.
                var best = verts.Where(v => Vector3.ProjectOnPlane(v - aim, fwd).sqrMagnitude < 0.0004f)
                    .OrderByDescending(v => Vector3.Dot(v - aim, fwd)).FirstOrDefault();
                if (best != Vector3.zero) points.Add(best);
            }
            return points;
        }

        static void UpperLegNote(Target t, GameObject avatar, Transform jiggle)
        {
            var animator = avatar.GetComponent<Animator>();
            var leg = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg) : null;
            var body = BodyOf(avatar);
            if (leg == null || body == null) return;
            int Under(Transform top)
            {
                var under = new HashSet<Transform>(top.GetComponentsInChildren<Transform>(true));
                var bones = body.bones;
                bool In(int i) => i >= 0 && i < bones.Length && bones[i] != null && under.Contains(bones[i]);
                return body.sharedMesh.boneWeights.Count(w => (In(w.boneIndex0) ? w.weight0 : 0f) + (In(w.boneIndex1) ? w.weight1 : 0f) +
                                                             (In(w.boneIndex2) ? w.weight2 : 0f) + (In(w.boneIndex3) ? w.weight3 : 0f) > 0.01f);
            }
            Note(t, "The selected bone on the leg bone instead", $"\"{leg.name}\" would paint {Under(leg)} vertices (the whole leg to the toes), \"{jiggle.name}\" paints {Under(jiggle)}");
        }

        static Transform Deep(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);

        static Transform Tip(Transform t)
        {
            var tip = t;
            int Depth(Transform x) { int d = 0; for (; x != null && x != t; x = x.parent) d++; return d; }
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (Depth(c) > Depth(tip)) tip = c;
            return tip;
        }

        // ---- the converter's saved settings ------------------------------------------------

        static void BackUpPrefs(string dir)
        {
            string key = CvrSetup.SettingsPrefsKey;
            string saved = EditorPrefs.HasKey(key) ? "1" + EditorPrefs.GetString(key) : "0";
            SessionState.SetString(PrefsBackupKey, saved);
            File.WriteAllText(Path.Combine(dir, "converter-settings-backup.json"), saved.Substring(1));
            // A new user's defaults.
            EditorPrefs.DeleteKey(key);
        }

        static void RestorePrefs()
        {
            string saved = SessionState.GetString(PrefsBackupKey, "");
            if (saved.Length == 0) return;
            if (saved[0] == '1') EditorPrefs.SetString(CvrSetup.SettingsPrefsKey, saved.Substring(1));
            else EditorPrefs.DeleteKey(CvrSetup.SettingsPrefsKey);
            SessionState.EraseString(PrefsBackupKey);
        }
    }
}
#endif
