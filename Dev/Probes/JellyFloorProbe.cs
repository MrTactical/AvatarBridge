// A MagicaCloth plane collider on a part's cloth as a floor the live tissue rests on. Builds the
// squash and the first part's tissue on an unpacked copy of the prefab, into Assets/__JellyFloor
// (cleared from outside after), with a plane in the part's cloth. In Play mode the plane is put
// just in front of the lattice facing the body, pushed a third of the way in and held, then backed off;
// then the same again with every collider's radius 0 on the step materials, the control.
// Logs per frame how far the deepest particle lies past the plane and the longest edge against
// the spacing. The menu and animator are left alone, so the prefab's controllers are not written.
//
// -executeMethod AvatarBridge.Regression.JellyFloorProbe.Run [-jfScene <scene>] -jfPrefab <prefab> -jfOut <tsv>
// Batch, never -nographics. No -quit: it exits itself, 0 on pass.

#if AVATARBRIDGE_YAPS && AVATARBRIDGE_MAGICA
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarBridge.Regression
{
    [InitializeOnLoad]
    public static class JellyFloorProbe
    {
        const string K = "JellyFloor.", Folder = "Assets/__JellyFloor", FloorName = "Jelly floor probe plane";
        // Per run: settle, raise, hold, lower, rest. The control repeats it from Second.
        const int Settle = 150, Raise = 120, Hold = 120, Lower = 60, Rest = 90, Run1 = Settle + Raise + Hold + Lower + Rest;
        static int frame = -1;
        static double startedAt;
        static Transform floor;
        static RenderTexture state;
        static Vector3Int n;
        static float spacing, startAt, topAt, restStretch, below1, below2, stretch1, back1;
        static int regions;
        static Vector3[] rest;
        static readonly List<string> rows = new List<string> { "frame\tphase\tplaneAt\tdeepestPast\tlongestEdge" };

        static JellyFloorProbe()
        {
            if (SessionState.GetInt(K + "on", 0) == 1) EditorApplication.update += Tick;
        }

        public static void Run()
        {
            SessionState.SetString(K + "out", Arg("-jfOut"));
            if (Arg("-jfScene") == "") EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            else EditorSceneManager.OpenScene(Arg("-jfScene"), OpenSceneMode.Single);
            foreach (var other in UnityEngine.Object.FindObjectsOfType<Animator>()) other.gameObject.SetActive(false);
            var made = (GameObject) PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Arg("-jfPrefab")));
            PrefabUtility.UnpackPrefabInstance(made, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var root = made.GetComponentInChildren<CVRAvatar>(true).transform;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__JellyFloor");
            Debug.Log("[JellyFloor] squash: " + YapsSquash.Add(root, Folder, out bool built));
            YapsShaderPatcher.EndPass(null, null);
            var parts = YapsSquash.PartsOf(root, out string none);
            if (!built || parts == null || parts.parts.Count == 0) { Quit("no squash parts: " + none, 2); return; }
            var name = parts.parts[0].name;
            var want = new Regex("^" + Regex.Escape(name) + "$", RegexOptions.IgnoreCase);

            // Facing the body from 2 m in front at the build, so it is no part of the skin at rest. It
            // presses the front toward the chest, as lying face down: raised from below, it crossed
            // the layer fixed to the body, which no floor reaches.
            floor = new GameObject(FloorName).transform;
            floor.SetParent(root, false);
            floor.SetPositionAndRotation(root.position + 2f * root.forward, Quaternion.FromToRotation(Vector3.up, -root.forward));
            var plane = floor.gameObject.AddComponent<MagicaCloth2.MagicaPlaneCollider>();
            int cloths = 0;
            foreach (var cloth in YapsJelly.PairCloth(root, YapsJelly.PartOf(name)).OfType<MagicaCloth2.MagicaCloth>())
            {
                cloth.SerializeData.colliderCollisionConstraint.colliderList.Add(plane);
                cloths++;
            }
            if (cloths == 0) { Quit($"{name} has no MagicaCloth to put the plane in", 2); return; }

            var report = new BridgeReport();
            string said = YapsJelly.PairOn(root, want, Folder + "/Jelly", YapsJelly.Shaders, new YapsJelly.Options { wire = false }, report);
            Debug.Log("[JellyFloor] tissue: " + said);
            if (!said.StartsWith("On ")) { Quit("the tissue refused: " + said, 2); return; }
            bool pressing = floor.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("Jelly collider "));
            bool listed = YapsJelly.PairCloth(root, YapsJelly.PartOf(name)).OfType<MagicaCloth2.MagicaCloth>().All(c => c.SerializeData.colliderCollisionConstraint.colliderList.Contains(plane));
            Debug.Log($"[JellyFloor] {(pressing ? "ok   " : "WRONG")} the plane presses the tissue");
            Debug.Log($"[JellyFloor] {(listed ? "ok   " : "WRONG")} and stays in every cloth of {name}");
            if (!pressing || !listed) { Quit("the plane was not taken as built", 1); return; }

            SessionState.SetInt(K + "on", 1);
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || SessionState.GetInt(K + "on", 0) != 1) return;
            if (frame < 0)
            {
                startedAt = EditorApplication.timeSinceStartup;
                floor = UnityEngine.Object.FindObjectsOfType<MagicaCloth2.MagicaPlaneCollider>().First(p => p.name == FloorName).transform;
                var root = floor.GetComponentInParent<CVRAvatar>().transform;
                var worn = root.GetComponentsInChildren<Transform>(true).First(t => t.name.StartsWith(YapsJelly.Worn + " "));
                var steps = worn.GetComponentsInChildren<CVRBlitter>(true);
                state = steps.Select(b => b.originTexture).First(t => t != null && t.name == "Jelly_State");
                var lattice = steps.Select(b => b.blitMaterial).First(m => m != null && m.HasProperty("_Lattice")).GetVector("_Lattice");
                n = new Vector3Int((int) lattice.x, (int) lattice.y, (int) lattice.z);
                spacing = lattice.w;
                regions = worn.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Jelly Region"));
                // Held in the scene's pose, so only the plane moves anything.
                foreach (var a in root.GetComponentsInChildren<Animator>(true)) a.enabled = false;
                Debug.Log($"[JellyFloor] lattice {n} at {spacing * 1000f:0.0} mm, {regions} regions");
                frame = 0;
            }
            if (EditorApplication.timeSinceStartup - startedAt > 400) { Quit("timed out", 3); return; }

            var at = Particles();
            int f = frame % Run1;
            bool control = frame >= Run1;
            // Depths along the plane's normal, toward the body: a particle under the plane is on its front.
            var ax = floor.up;
            float Depth(Vector3 p) => Vector3.Dot(p, ax);
            void Put(float d) => floor.position += ax * (d - Depth(floor.position));
            if (f == Settle - 10)
            {
                rest = at;
                startAt = at.Min(Depth) - 0.03f;
                topAt = startAt + 0.03f + (at.Max(Depth) - at.Min(Depth)) / 3f;
                restStretch = Longest(at);
                if (control) ZeroColliders();
            }
            if (f < Settle - 10) Put(Mathf.Min(Depth(floor.position), at.Min(Depth) - 0.5f));
            else if (f < Settle) Put(startAt);
            else if (f < Settle + Raise) Put(Mathf.Lerp(startAt, topAt, (f - Settle) / (float) Raise));
            else if (f < Settle + Raise + Hold) Put(topAt);
            else if (f < Settle + Raise + Hold + Lower) Put(Mathf.Lerp(topAt, startAt, (f - Settle - Raise - Hold) / (float) Lower));

            float planeAt = Depth(floor.position);
            float below = at.Max(p => planeAt - Depth(p)), longest = Longest(at);
            string phase = f < Settle ? "settle" : f < Settle + Raise ? "raise" : f < Settle + Raise + Hold ? "hold" : f < Settle + Raise + Hold + Lower ? "lower" : "rest";
            rows.Add($"{frame}\t{(control ? "control " : "")}{phase}\t{planeAt:0.0000}\t{below:0.0000}\t{longest:0.000}");
            if (phase == "hold" && f >= Settle + Raise + Hold / 2)
            {
                if (control) below2 = Mathf.Max(below2, below);
                else { below1 = Mathf.Max(below1, below); stretch1 = Mathf.Max(stretch1, longest); }
            }
            if (!control && f == Run1 - 1) back1 = rest.Zip(at, Vector3.Distance).Max();
            if (f == Settle + Raise + Hold / 2 || f == Settle + Raise + Hold - 1) Dump(control);

            if (++frame < 2 * Run1) return;
            int failures = 0;
            void Check(bool ok, string what) { if (!ok) failures++; Debug.Log($"[JellyFloor] {(ok ? "ok   " : "WRONG")} {what}"); }
            Check(below1 <= 0.003f, $"held a third in, no particle lies 3 mm past the plane ({below1 * 1000f:0.0} mm)");
            Check(below2 >= 0.01f, $"the control, no collider pressing, sinks 10 mm or more ({below2 * 1000f:0.0} mm)");
            Check(stretch1 <= restStretch + 1f, $"no edge stretches a spacing past its longest at rest ({stretch1:0.00} against {restStretch:0.00})");
            Check(back1 <= 0.005f, $"lowered, every particle back within 5 mm of rest ({back1 * 1000f:0.0} mm)");
            Quit(failures == 0 ? null : $"{failures} checks", failures == 0 ? 0 : 1);
        }

        // Every particle of every region, in the world: region r's rows, Row to a row, current half.
        static Vector3[] Particles()
        {
            var req = AsyncGPUReadback.Request(state);
            req.WaitForCompletion();
            var s = req.GetData<Vector4>().ToArray();
            int count = n.x * n.y * n.z, rows = YapsJelly.Side(n) + 1;
            var at = new Vector3[regions * count];
            for (int r = 0; r < regions; r++)
                for (int p = 0; p < count; p++)
                    at[r * count + p] = s[(r * rows + p / YapsJelly.Row) * state.width + p % YapsJelly.Row];
            return at;
        }

        // Each region's reach bits (41) and the avatar collider slots as the step has them (64 to
        // 79: end a and radius, end b and reach), beside the provider's ends for the same slots.
        static void Dump(bool control)
        {
            var req = AsyncGPUReadback.Request(state);
            req.WaitForCompletion();
            var s = req.GetData<Vector4>().ToArray();
            int side = YapsJelly.Side(n);
            for (int r = 0; r < regions; r++)
            {
                int row = r * (side + 1) + side;
                Vector4 At(int x) => s[row * state.width + x];
                var said = new List<string> { $"region {r} row {row}: 41 bits {(int) At(41).x} into {(int) At(41).y}, distant/sleep 18 {At(18)}" };
                for (int c = 8; c < 16; c++)
                {
                    Vector4 a = At(64 + 2 * (c - 8)), b = At(65 + 2 * (c - 8));
                    if (a.w != 0f || b.w != 0f) said.Add($"  slot {c}: a {(Vector3) a:F3} r {a.w:0.###}, b {(Vector3) b:F3} reach {b.w:0.#}");
                }
                Debug.Log($"[JellyFloor] {(control ? "control " : "")}" + string.Join("\n", said));
            }
            var anchors = floor.GetComponentInParent<CVRAvatar>().GetComponentsInChildren<CVRMaterialDataProvider>(true).FirstOrDefault(q => q.transforms != null && q.transforms.Length > 16);
            if (anchors != null)
                for (int i = 16; i < anchors.transforms.Length; i++)
                    if (anchors.transforms[i] != null && anchors.transforms[i].name.StartsWith("Jelly collider"))
                        Debug.Log($"[JellyFloor] provider {i}: {anchors.transforms[i].name} under {anchors.transforms[i].parent.name} at {anchors.transforms[i].position:F3}");
            Debug.Log($"[JellyFloor] plane at {floor.position:F3}, up {floor.up:F2}");
            // The longest edge: where it is, its ends' depth past the plane and how far each moved.
            var at = Particles();
            int count = n.x * n.y * n.z;
            float most = 0f, planeAt = Vector3.Dot(floor.position, floor.up);
            string where = "";
            for (int r = 0; r < regions; r++)
                for (int k = 0; k < n.z; k++)
                    for (int j = 0; j < n.y; j++)
                        for (int i = 0; i < n.x; i++)
                        {
                            int p = r * count + (k * n.y + j) * n.x + i;
                            foreach (var (q, axis) in new[] { (i + 1 < n.x ? p + 1 : -1, "x"), (j + 1 < n.y ? p + n.x : -1, "y"), (k + 1 < n.z ? p + n.x * n.y : -1, "z") })
                            {
                                if (q < 0) continue;
                                float d = Vector3.Distance(at[p], at[q]);
                                if (d <= most) continue;
                                most = d;
                                where = $"region {r} ({i},{j},{k}) along {axis}: {d / spacing:0.00} spacings; depths past the plane {(planeAt - Vector3.Dot(at[p], floor.up)) * 1000f:0} and {(planeAt - Vector3.Dot(at[q], floor.up)) * 1000f:0} mm, moved {Vector3.Distance(at[p], rest[p]) * 1000f:0} and {Vector3.Distance(at[q], rest[q]) * 1000f:0} mm, rest depths {(planeAt - Vector3.Dot(rest[p], floor.up)) * 1000f:0} and {(planeAt - Vector3.Dot(rest[q], floor.up)) * 1000f:0} mm";
                            }
                        }
            Debug.Log($"[JellyFloor] {(control ? "control " : "")}longest {where}");
        }

        // The longest edge between neighbours along any axis, in spacings.
        static float Longest(Vector3[] at)
        {
            int count = n.x * n.y * n.z;
            float most = 0f;
            for (int r = 0; r < regions; r++)
                for (int k = 0; k < n.z; k++)
                    for (int j = 0; j < n.y; j++)
                        for (int i = 0; i < n.x; i++)
                        {
                            int p = r * count + (k * n.y + j) * n.x + i;
                            if (i + 1 < n.x) most = Mathf.Max(most, Vector3.Distance(at[p], at[p + 1]));
                            if (j + 1 < n.y) most = Mathf.Max(most, Vector3.Distance(at[p], at[p + n.x]));
                            if (k + 1 < n.z) most = Mathf.Max(most, Vector3.Distance(at[p], at[p + n.x * n.y]));
                        }
            // Against the spacing as built: only ever compared with its own reading at rest.
            return most / Mathf.Max(spacing, 1e-6f);
        }

        // In place: the materials are this run's own build in the scratch folder. A copy is fed
        // nothing by the data providers, which list the originals, so its anchors froze.
        static void ZeroColliders()
        {
            foreach (var m in UnityEngine.Object.FindObjectsOfType<CVRBlitter>().Select(b => b.blitMaterial).Distinct())
            {
                if (m == null || !m.HasProperty("_JellyCol0")) continue;
                m.SetVector("_JellyCol0", Vector4.zero);
                m.SetVector("_JellyCol1", Vector4.zero);
            }
            Debug.Log("[JellyFloor] control: every collider's radius 0 on the step materials");
        }

        static void Quit(string why, int code)
        {
            SessionState.SetInt(K + "on", 0);
            EditorApplication.update -= Tick;
            var outFile = SessionState.GetString(K + "out", "");
            if (!string.IsNullOrEmpty(outFile)) File.WriteAllLines(outFile, rows);
            Debug.Log(why == null ? "[JellyFloor] PASS" : "[JellyFloor] FAIL: " + why);
            EditorApplication.Exit(code);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : "";
        }
    }
}
#endif
