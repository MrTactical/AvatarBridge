// The tester's physics card, run headless on a converted avatar in Play
// mode: standing still marks nothing as edited, a pick never offers a bone
// the solver holds, a settled chain reads healthy and one thrown far reads
// red, the label reads its settings off the component, a push moves a chain,
// walking swings chains more than standing still, Stop puts the avatar back,
// the overlay switches MagicaCloth's own drawing on and back off, the walk is
// a closed circle, and a kept gravity edit lands on the scene's component
// once Play mode ends with its references intact, then undoes.
//
// The tester is made before Play, as a user opens it, so it captures rest
// poses on the way in and watches from the first frame. Every change to a
// chain's tunable data is logged with the fields that moved.
//
// Run: -executeMethod AvatarBridge.Regression.PhysicsTesterProbe.Run -probePrefab "Assets/.../X (ChilloutVR).prefab"
// (no -quit: it leaves Play mode and exits itself)
#if CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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
        const string RefsKey = "PhysicsTesterProbe.refs.";
        const string GravityKey = "PhysicsTesterProbe.gravity.";
        const string KeptKey = "AvatarBridge.KeptPhysics";
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        static readonly string[] Phases = { "building", "standing still", "unpushed", "pushed", "flung", "held", "walking" };
        static int fail;
        static CckAnimatorTester tester;
        static float stepAt, stillSwing, walkSwing, peak, unpushed, keptGravity;
        static int frameAt, ticks, steppedAfterPush = -1, sends;
        static bool landed;
        static string keptPath;
        static double leftPlayAt;
        static Vector3 rootStart, tipBefore;
        static Quaternion[] restRotations;
        static Transform[] tips;
        static Transform pushedTip;
        static MagicaCloth pushed;
        static (bool always, bool enable)[] gizmosBefore;
        // What the watcher hashes, per cloth, and every change to it by phase.
        static readonly Dictionary<MagicaCloth, (string json, string pretty)> seen = new Dictionary<MagicaCloth, (string, string)>();
        static readonly Dictionary<MagicaCloth, int[]> changes = new Dictionary<MagicaCloth, int[]>();
        static readonly Dictionary<MagicaCloth, Dictionary<string, (string first, string last)>> fields =
            new Dictionary<MagicaCloth, Dictionary<string, (string, string)>>();

        static PhysicsTesterProbe()
        {
            if (SessionState.GetInt(Key, -1) >= 0) EditorApplication.update += Tick;
        }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string prefab = args[Array.IndexOf(args, "-probePrefab") + 1];
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefab));
            // The edit-mode state Keep must land on and must not damage.
            foreach (var c in instance.GetComponentsInChildren<MagicaCloth>(true))
            {
                SessionState.SetString(RefsKey + PathOf(c.transform), RefsOf(c));
                SessionState.SetFloat(GravityKey + PathOf(c.transform), c.SerializeData.gravity);
            }
            tester = ScriptableObject.CreateInstance<CckAnimatorTester>();
            tester.hideFlags = HideFlags.DontSave;
            SessionState.SetInt(Key, 0);
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            int step = SessionState.GetInt(Key, 0);
            try
            {
                if (step == 8)
                {
                    AfterPlay();
                    return;
                }
                if (!EditorApplication.isPlaying) return;
                if (tester == null) tester = Resources.FindObjectsOfTypeAll<CckAnimatorTester>().FirstOrDefault();
                var avatar = UnityEngine.Object.FindObjectOfType<CVRAvatar>();
                var all = avatar != null ? avatar.GetComponentsInChildren<MagicaCloth>(true) : new MagicaCloth[0];
                // A cloth on something switched off at rest never builds; only running ones can move.
                var cloths = all.Where(c => c.isActiveAndEnabled && c.IsValid()).ToArray();
                if (step <= 6) Record(all, step);
                switch (step)
                {
                    case 0: // let the cloth build and settle
                        if (Time.time < 1.5f) return;
                        Check(cloths.Length > 0, $"{cloths.Length} of {all.Length} cloth(s) built and running");
                        tips = cloths.SelectMany(c => c.SerializeData.rootBones.Where(r => r != null))
                            .Select(r => r.GetComponentsInChildren<Transform>(true).Last()).ToArray();
                        restRotations = tips.Select(t => Local(avatar, t)).ToArray();
                        // The longest chain, so a push has room to show.
                        pushed = cloths.OrderByDescending(c => c.SerializeData.rootBones.Where(r => r != null)
                            .Max(r => r.GetComponentsInChildren<Transform>(true).Length)).First();
                        pushedTip = pushed.SerializeData.rootBones.Where(r => r != null)
                            .Select(r => r.GetComponentsInChildren<Transform>(true).Last()).First();
                        Next(1);
                        return;
                    case 1: // standing still for two seconds: the noise floor, and nothing edited
                        if (Time.time - stepAt < 2f) return;
                        stillSwing = Swing();
                        Report(all, 0, 2);
                        var edited = Edited();
                        Check(edited.Count == 0, "standing still marks nothing as edited" +
                                                 (edited.Count > 0 ? $" ({edited.Count}: {string.Join(", ", edited.Select(e => e.name))})" : ""));
                        CheckPick(cloths);
                        CheckReadout(cloths);
                        Watch();
                        Next(2);
                        return;
                    case 2: // the same quarter second with no push: what a push has to beat
                        if (!Watched(out unpushed)) return;
                        Check(CckAnimatorTester.Push(pushed, Vector3.right, 3f), $"MagicaCloth takes a push on this version ({pushed.name})");
                        // The fling the card sends on let-go, through its own path: one push alone
                        // lands only on a frame that runs a step, so LetGo re-sends it for one.
                        Set("_held", pushed);
                        Set("_flingVelocity", Vector3.right * 25f);
                        Call("LetGo");
                        Watch();
                        Next(3);
                        return;
                    case 3:
                        if (ticks == 0) steppedAfterPush = MagicaManager.Team.GetTeamDataRef(pushed.Process.TeamId).updateCount;
                        if (!Watched(out float moved)) return;
                        Check(moved > 2f * unpushed && moved > unpushed + 0.003f,
                            $"a fling on let-go moved the chain's tip {moved * 1000f:F1} mm (peak {peak * 1000f:F1}; " +
                            $"{unpushed * 1000f:F1} mm with no push; {Fps():F0} fps)");
                        Watch();
                        Next(4);
                        return;
                    case 4: // the strongest fling LetGo sends, sent again until a stepped frame takes it
                        if (!landed)
                        {
                            if (ticks > 0 && MagicaManager.Team.GetTeamDataRef(pushed.Process.TeamId).updateCount > 0)
                            {
                                landed = true;
                                sends = ticks;
                            }
                            else CckAnimatorTester.Push(pushed, Vector3.right, 25f);
                        }
                        if (!Watched(out float flung)) return;
                        Debug.Log($"[PhysicsTesterProbe] info a fling of 25 that landed on a stepped frame (sent {sends} time(s)) " +
                                  $"moved the tip {flung * 1000f:F1} mm (peak {peak * 1000f:F1}; {unpushed * 1000f:F1} with no push)");
                        Watch();
                        Next(5);
                        return;
                    case 5: // the pull a drag sends, every frame
                        CckAnimatorTester.Push(pushed, Vector3.right, 18f);
                        if (!Watched(out float held)) return;
                        Check(held > 0.02f && held > 5f * unpushed,
                            $"a pull re-sent every frame, as a drag sends it, moved the tip {held * 1000f:F1} mm ({unpushed * 1000f:F1} with no push)");
                        rootStart = avatar.transform.position;
                        restRotations = tips.Select(t => Local(avatar, t)).ToArray();
                        Call("StartMotion", avatar, CckAnimatorTester.Moves.Walk);
                        Next(6);
                        return;
                    case 6: // walking for two seconds
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
                        Report(all, 2, 7);
                        // An Inspector edit, as the watcher sees one: through SerializeData.
                        float g = pushed.SerializeData.gravity;
                        keptGravity = g <= 19f ? g + 1f : g - 1f;
                        pushed.SerializeData.gravity = keptGravity;
                        Next(7);
                        return;
                    case 7: // two polls later
                        if (Time.time - stepAt < 0.6f) return;
                        Check(Edited().Contains(pushed), "a gravity edit in Play mode is listed under Edited this Play session");
                        typeof(CckAnimatorTester).GetMethod("Keep", AnyStatic).Invoke(null, new object[] { pushed });
                        Check(SessionState.GetString(KeptKey, "") != "", "Keep records it for when Play mode ends");
                        // Leaving Play mode destroys this component for the scene's own.
                        keptPath = PathOf(pushed.transform);
                        leftPlayAt = EditorApplication.timeSinceStartup;
                        SessionState.SetInt(Key, 8);
                        EditorApplication.ExitPlaymode();
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

        // Back in edit mode, once the tester's hook has applied the record.
        static void AfterPlay()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SessionState.GetString(KeptKey, "") != "" && EditorApplication.timeSinceStartup - leftPlayAt < 10.0) return;
            string path = keptPath ?? "None";
            var avatar = UnityEngine.Object.FindObjectOfType<CVRAvatar>();
            var cloth = avatar != null
                ? avatar.GetComponentsInChildren<MagicaCloth>(true).FirstOrDefault(c => PathOf(c.transform) == path)
                : null;
            float before = SessionState.GetFloat(GravityKey + path, float.NaN);
            string refs = SessionState.GetString(RefsKey + path, "");
            Check(cloth != null && Mathf.Approximately(cloth.SerializeData.gravity, keptGravity),
                $"Keep lands gravity {before:0.##} -> {keptGravity:0.##} on the scene's {path.Split('/').Last()} " +
                $"once Play mode ends (reads {(cloth != null ? cloth.SerializeData.gravity.ToString("0.##") : "no component")})");
            Check(cloth != null && RefsOf(cloth) == refs,
                $"and keeps every root bone and collider reference ({Count(refs, 0)} root(s), {Count(refs, 1)} collider(s))" +
                (cloth != null && RefsOf(cloth) != refs ? $": was {refs}, now {RefsOf(cloth)}" : ""));
            if (cloth != null)
            {
                Undo.PerformUndo();
                Check(Mathf.Approximately(cloth.SerializeData.gravity, before) && RefsOf(cloth) == refs,
                    $"undo puts it back (gravity {cloth.SerializeData.gravity:0.##})");
            }
            Finish();
        }

        // A pick takes only what Grabbable offers, so that list must hold no
        // bone the solver keeps still. With nothing painted, MagicaCloth fixes
        // a BoneCloth's roots and nothing else (ClothProcess, the simple
        // selection it makes when there is none), so painted data voids the test.
        static void CheckPick(MagicaCloth[] cloths)
        {
            var problems = new List<string>();
            int rootOnly = 0;
            foreach (var c in cloths)
            {
                var data2 = typeof(MagicaCloth).GetField("serializeData2", Any)?.GetValue(c) as ClothSerializeData2;
                if (data2 == null || data2.selectionData.IsValid() || data2.boneAttributeDict.Count > 0)
                {
                    problems.Add($"{c.name} has painted or per-bone attributes, so its fixed set is not its roots");
                    continue;
                }
                var roots = c.SerializeData.rootBones.Where(r => r != null).ToList();
                var offered = CckAnimatorTester.Grabbable(c);
                if (offered.All(roots.Contains))
                {
                    rootOnly++;
                    continue;
                }
                var fixedOffered = offered.Where(roots.Contains).ToList();
                if (fixedOffered.Count > 0) problems.Add($"{c.name} offers {string.Join(", ", fixedOffered.Select(t => t.name))}");
                if (offered.Count == 0) problems.Add($"{c.name} offers nothing");
            }
            Check(problems.Count == 0, $"a pick never offers a bone the solver holds fixed ({cloths.Length - rootOnly} chain(s) with free bones" +
                                       (rootOnly > 0 ? $", {rootOnly} root-only, where a root is the only handle" : "") + ")" +
                                       (problems.Count > 0 ? ": " + string.Join("; ", problems) : ""));
        }

        static void CheckReadout(MagicaCloth[] cloths)
        {
            int captured = ((List<string>)typeof(CckAnimatorTester).GetField("_restPaths", Any).GetValue(tester)).Count;
            Check(captured > 0, $"the tester captured {captured} rest pose(s) on entering Play");
            var alarms = cloths.Select(c => (c, h: tester.Health(c))).Where(x => x.h.rank != 2).ToList();
            Check(alarms.Count == 0, $"settled chains read healthy ({cloths.Length - alarms.Count} of {cloths.Length} green)" +
                                     string.Concat(alarms.Select(a => $"  {a.c.name} [{a.h.why}]")));
            foreach (var c in cloths)
            {
                var d = c.SerializeData;
                string restore = d.angleRestorationConstraint.useAngleRestoration
                    ? $"{d.angleRestorationConstraint.stiffness.value:0.##}" : "off";
                string limit = d.angleLimitConstraint.useAngleLimit ? $"{d.angleLimitConstraint.limitAngle.value:0}°" : "off";
                string expected = $"{d.clothType}  grav {d.gravity:0.##}  damp {d.damping.value:0.###}  restore {restore}  " +
                                  $"limit {limit}  r {d.radius.value:0.###}  col {d.colliderCollisionConstraint.colliderList.Count(x => x != null)}";
                string text = CckAnimatorTester.LabelText(c, null);
                Check(text.EndsWith("  " + expected), $"the label reads its settings off the component: \"{text}\"" +
                                                      (text.EndsWith("  " + expected) ? "" : $", expected \"{expected}\""));
            }
            // One free bone carried ten chain lengths off, put back before the solver sees it.
            var root = pushed.SerializeData.rootBones.First(r => r != null && r.childCount > 0);
            var bone = root.GetChild(0);
            var was = bone.position;
            bone.position = was + Vector3.right * 10f * Longest(root);
            var thrown = tester.Health(pushed);
            bone.position = was;
            var bad = (Color)typeof(CckAnimatorTester).GetField("HealthBad", AnyStatic).GetValue(null);
            Check(thrown.rank == 0 && thrown.colour == bad, $"a bone thrown 10 chain lengths reads red [{thrown.why}]");
        }

        // A quarter second of the pushed chain's tip, from the next tick on.
        static void Watch()
        {
            tipBefore = pushedTip.position;
            peak = 0f;
            ticks = 0;
            frameAt = Time.frameCount;
        }

        static bool Watched(out float moved)
        {
            ticks++;
            moved = Vector3.Distance(pushedTip.position, tipBefore);
            peak = Mathf.Max(peak, moved);
            return Time.time - stepAt >= 0.25f;
        }

        static float Fps() => (Time.frameCount - frameAt) / Mathf.Max(1e-3f, Time.time - stepAt);

        static float Longest(Transform t)
        {
            float most = 0f;
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                most = Mathf.Max(most, Vector3.Distance(t.position, child.position) + Longest(child));
            }
            return most;
        }

        // ---- the watcher's view ----------------------------------------------------------

        static string Json(Component c) =>
            (string)typeof(CckAnimatorTester).GetMethod("Json", AnyStatic).Invoke(null, new object[] { c });

        static List<Component> Edited() =>
            (List<Component>)typeof(CckAnimatorTester).GetField("_edited", Any).GetValue(tester);

        static void Record(MagicaCloth[] all, int phase)
        {
            foreach (var c in all)
            {
                string json = Json(c);
                if (!seen.TryGetValue(c, out var was))
                {
                    seen[c] = (json, EditorJsonUtility.ToJson(c.SerializeData, true));
                    changes[c] = new int[Phases.Length];
                    fields[c] = new Dictionary<string, (string, string)>();
                    continue;
                }
                if (json == was.json) continue;
                string pretty = EditorJsonUtility.ToJson(c.SerializeData, true);
                changes[c][phase]++;
                foreach (var (path, before, now) in Diff(was.pretty, pretty))
                {
                    fields[c][path] = fields[c].TryGetValue(path, out var f) ? (f.first, now) : (before, now);
                }
                seen[c] = (json, pretty);
            }
        }

        static void Report(MagicaCloth[] all, int from, int to)
        {
            string span = string.Join(" and ", Phases.Skip(from).Take(to - from));
            var moved = all.Where(c => changes.ContainsKey(c) && changes[c].Skip(from).Take(to - from).Sum() > 0).ToList();
            Debug.Log($"[PhysicsTesterProbe] info tunable data changed on {moved.Count} of {all.Length} cloth(s) while {span}");
            foreach (var c in moved)
            {
                var counts = string.Join(", ", Enumerable.Range(from, to - from).Select(p => $"{changes[c][p]} {Phases[p]}"));
                var what = string.Join("; ", fields[c].Take(12).Select(f => $"{f.Key} {f.Value.first} -> {f.Value.last}"));
                Debug.Log($"[PhysicsTesterProbe] info   {c.name}{(c.isActiveAndEnabled ? "" : " (off)")}: {counts}: {what}");
            }
        }

        // Pretty JSON flattened to one dotted path per value, so two
        // snapshots of the same data compare line for line.
        static List<(string path, string value)> Flatten(string pretty)
        {
            var flat = new List<(string, string)>();
            var stack = new List<string>();
            foreach (var raw in pretty.Split('\n'))
            {
                string line = raw.Trim().TrimEnd(',');
                if (line == "}" || line == "]")
                {
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                    continue;
                }
                var m = Regex.Match(line, "^\"([^\"]*)\":\\s*(.*)$");
                string key = m.Success ? m.Groups[1].Value : "[]";
                string value = m.Success ? m.Groups[2].Value : line;
                if (value == "{" || value == "[")
                {
                    stack.Add(stack.Count == 0 ? "" : key);
                    continue;
                }
                flat.Add((string.Join(".", stack.Where(s => s != "").Append(key)), value));
            }
            return flat;
        }

        static IEnumerable<(string path, string before, string now)> Diff(string a, string b)
        {
            var x = Flatten(a);
            var y = Flatten(b);
            if (x.Count != y.Count)
            {
                yield return ("(shape)", $"{x.Count} values", $"{y.Count} values");
                yield break;
            }
            for (int i = 0; i < x.Count; i++)
            {
                if (x[i] != y[i]) yield return (y[i].path, x[i].value, y[i].value);
            }
        }

        // ---- helpers ------------------------------------------------------------------------

        static string PathOf(Transform t) => t == null ? "None" : t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        static string RefsOf(MagicaCloth c) =>
            string.Join(";", c.SerializeData.rootBones.Select(PathOf)) + " | " +
            string.Join(";", c.SerializeData.colliderCollisionConstraint.colliderList
                .Select(x => x == null ? "None" : PathOf(x.transform) + ":" + x.GetType().Name));

        static int Count(string refs, int part)
        {
            var side = refs.Split('|').ElementAtOrDefault(part)?.Trim();
            return string.IsNullOrEmpty(side) ? 0 : side.Split(';').Length;
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

        static void Set(string field, object value) =>
            typeof(CckAnimatorTester).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(tester, value);

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
