// Plays a scene holding the shader squash test and films the press sources: the part at rest,
// then one arm bent so the hand lies across the nearest part, filmed with every source on, with
// the arms off, and with the bone push off too. Per frame, how far each part's follow sits off
// its rest and the press that makes, to see whether settled cloth reads as a press.
//
// -executeMethod AvatarBridge.Regression.SquashPlayProbe.Run -spScene <scene> -spOut <dir> [-spOther]
//
// -spOther leaves the arms alone and presses with another player's hand instead, through the
// client's player globals: one shot per hand shape (open, fist, pointing, two fingers), the palm
// again as a third client lays the arrays out, then the wrist sunk in.
//
// -spVideo films one continuous run instead, three views a frame at 30 fps into <dir>/frames,
// with segments.tsv naming each segment's first frame: another player's hand in each shape,
// pokes round the part, quick and deep, two players at once, hands between, squeezing and
// grabbing, a hand from below, the own arm, then the pair pulse. Other players' hands are drawn
// as glass capsules the size the shader presses with.
//
// Batch, never -nographics: the frames are rendered.

#if AVATARBRIDGE_YAPS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;

namespace AvatarBridge.Regression
{
    [InitializeOnLoad]
    public static class SquashPlayProbe
    {
        const string K = "SquashPlay.";
        const int Fps = 60, Off = 120, Bend = 180, Bent = 300, End = 420;
        struct Step { }
        struct Late { }
        static readonly Dictionary<int, float> late = new Dictionary<int, float>();

        static int frame;
        static float power = -1f;
        static Animator animator;
        static CVRMaterialDataProvider provider;
        static SkinnedMeshRenderer skin;
        static Vector3[] lastEnd;
        static Transform shoulder, elbow, wrist;
        static Quaternion shoulderRest, elbowRest;
        static Vector3 target;
        static Camera front, side;
        static List<string> rows;
        static string outDir;

        static SquashPlayProbe()
        {
            if (SessionState.GetInt(K + "on", 0) == 1) EditorApplication.update += Tick;
        }

        public static void Run()
        {
            SessionState.SetString(K + "out", Arg("-spOut"));
            SessionState.SetInt(K + "on", 1);
            SessionState.SetInt(K + "other", Environment.GetCommandLineArgs().Contains("-spOther") ? 1 : 0);
            SessionState.SetInt(K + "video", Environment.GetCommandLineArgs().Contains("-spVideo") ? 1 : 0);
            var scene = EditorSceneManager.OpenScene(Arg("-spScene"), OpenSceneMode.Single);
            foreach (var r in scene.GetRootGameObjects())
                if (r.GetComponentInChildren<CVRAvatar>(true) == null && r.GetComponentInChildren<Animator>(true) != null) r.SetActive(false);
            // Nothing is on screen in batch, and the cloth culls with the Animator.
            foreach (var a in UnityEngine.Object.FindObjectsOfType<Animator>()) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || provider != null || SessionState.GetInt(K + "on", 0) != 1) return;
            outDir = SessionState.GetString(K + "out", "");
            Directory.CreateDirectory(outDir);
            var providers = UnityEngine.Object.FindObjectsOfType<CVRMaterialDataProvider>();
            provider = providers.First(p => p.dataMode == CVRMaterialDataProvider.DataMode.TransformLocalToWorld);
            skin = providers.Where(p => p.dataMode == CVRMaterialDataProvider.DataMode.SkinnedMeshRendererPreviousVertexBuffer).Select(p => p.smr).FirstOrDefault();
            animator = provider.GetComponentInParent<CVRAvatar>().GetComponent<Animator>();
            rows = new List<string> { "frame\tpart\toff_rest_cm\tpush_cm\tprovider_writes\tend_of_frame_off_rest_cm\tskin_off_rest_cm\tskin_vs_last_end_cm\tskin_worst_slot_vs_last_end_cm" };
            Time.captureFramerate = Fps;
            frame = 0;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                var sys = loop.subSystemList[i];
                var list = sys.subSystemList.ToList();
                if (sys.type == typeof(UnityEngine.PlayerLoop.Update))
                    list.Insert(0, new PlayerLoopSystem { type = typeof(Step), updateDelegate = OnStep });
                // After the cloth has written its bones, which it puts back to rest at the next
                // frame's start, so a script reading in LateUpdate never sees them moved.
                else if (sys.type == typeof(UnityEngine.PlayerLoop.PostLateUpdate))
                    list.Add(new PlayerLoopSystem { type = typeof(Late), updateDelegate = OnLate });
                else continue;
                sys.subSystemList = list.ToArray();
                loop.subSystemList[i] = sys;
            }
            PlayerLoop.SetPlayerLoop(loop);
        }

        static void OnStep()
        {
            if (!EditorApplication.isPlaying) return;
            frame++;
            var slots = provider.transforms;
            var mats = provider.materials;
            Log(slots, mats[0]);

            if (frame == Off)
            {
                animator.enabled = false;
                // The arm on the side of the part nearest the left hand.
                bool left = Vector3.Distance(slots[0].position, animator.GetBoneTransform(HumanBodyBones.LeftHand).position)
                          < Vector3.Distance(slots[1].position, animator.GetBoneTransform(HumanBodyBones.LeftHand).position);
                var part = slots[left ? 0 : 1];
                shoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                elbow = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                wrist = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                shoulderRest = shoulder.rotation;
                elbowRest = elbow.rotation;
                // The wrist sunk into the front of the part, so the hand and forearm lie in it.
                var root = animator.transform;
                float reach = mats[0].GetVector("_YAPS_SquashReach")[left ? 0 : 1] * part.lossyScale.x;
                var chest = animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
                var outward = Vector3.ProjectOnPlane(part.position - chest.position, Vector3.up).normalized;
                target = part.position + root.forward * reach * 0.6f + outward * reach * 0.2f;
                SetUpCameras(part.position, reach);
                Shoot("rest_animated");
            }
            if (SessionState.GetInt(K + "video", 0) == 1) { if (frame >= Bend) Film(slots, mats); return; }
            if (frame == Bend - 1) Shoot("rest");
            if (SessionState.GetInt(K + "other", 0) == 1)
            {
                // One shot per hand shape, the gesture in the hand's w as the client sends it. The
                // third-player shot repeats the palm as a third client sees it: its own hands far
                // off at index 0, the wearer's own hands at index 2, which must change nothing.
                var shots = new (float back, float gesture, bool third, string name)[]
                {
                    // The skin sits about 7 cm past the target: 8.7 cm out, an open hand or a finger
                    // is 3 cm in and a fist falls short; with the wrist at the target the fist is in.
                    (0.087f, -1f, false, "other_palm"), (0.087f, -1f, true, "third_palm"), (0.087f, 1f, false, "other_fist"),
                    (0.087f, 4f, false, "other_point"), (0.087f, 5f, false, "other_victory"), (0f, 1f, false, "other_fist_in"),
                    (0f, -1f, false, "other_wrist_in"),
                };
                int at = frame - (End - 30);
                var s = shots[Mathf.Clamp(at, 0, shots.Length - 1)];
                if (frame >= Bend) Other(s.back, s.gesture, s.third);
                if (at == 0) Debug.Log($"[SquashPlay] other: params1 {Shader.GetGlobalVector("CVRGlobalParams1")} " +
                    $"hand1 {Shader.GetGlobalVectorArray("_CVR_PlayerLeftHandPositions")[1]} target {target} part {slots[0].position} {slots[1].position} " +
                    $"wrists {slots[10].position} {slots[14].position} hands {mats[0].GetVector("_YAPS_SquashHands")} power {mats[0].GetFloat("_YAPS_DentPower")}");
                if (at >= 0 && at < shots.Length) Shoot(s.name);
                if (at == shots.Length - 1) frame = End - 21;
            }
            else
            {
                if (frame >= Bend && frame <= Bent) Reach(Mathf.SmoothStep(0f, 1f, (frame - Bend) / (float)(Bent - Bend)));
                if (frame > Bent) Reach(1f);
            }
            if (frame == End - 20) Shoot("hand_all");
            if (frame == End - 15) Set(mats, push => new Vector4(push.x, push.y, push.z, 0f));
            if (frame == End - 10) Shoot("hand_no_arms");
            if (frame == End - 9) Set(mats, push => new Vector4(0f, push.y, push.z, 0f));
            if (frame == End - 4) Shoot("hand_nothing");
            // The shader off entirely, so what is left is the cloth and the arm.
            if (frame == End - 3) { power = mats[0].GetFloat("_YAPS_DentPower"); foreach (var m in mats) m.SetFloat("_YAPS_DentPower", 0f); }
            if (frame == End - 1) Shoot("hand_shader_off");
            if (frame == End)
            {
                if (power >= 0f) foreach (var m in mats) m.SetFloat("_YAPS_DentPower", power);
                Set(mats, push => new Vector4(1f, push.y, push.z, 1f));
                File.WriteAllLines(Path.Combine(outDir, "push.tsv"), rows);
                SessionState.SetInt(K + "on", 0);
                Debug.Log("[SquashPlay] done " + outDir);
                EditorApplication.Exit(0);
            }
        }

        // Another player, 1.7 m tall, reaching in from the front: the globals carry only a wrist,
        // set `back` metres out from where the own-arm test sinks its wrist, and the chest behind it.
        // `third` lays the arrays out as a third client has them, where the wearer is a remote
        // player too: that client's own player first, far off, and the wearer after the presser.
        static void Other(float back, float gesture, bool third)
        {
            var forward = animator.transform.forward;
            var wristAt = target + forward * back;
            var left = new Vector4[255];
            var right = new Vector4[255];
            var chests = new Vector4[255];
            var roots = new Vector4[255];
            left[1] = new Vector4(wristAt.x, wristAt.y, wristAt.z, gesture);
            chests[1] = wristAt + forward * 0.45f;
            roots[1] = new Vector4(chests[1].x, 0f, chests[1].z, 1.7f);
            if (third)
            {
                var far = animator.transform.position + Vector3.right * 10f;
                left[0] = far + Vector3.up; right[0] = far + Vector3.up + Vector3.right * 0.4f;
                chests[0] = far + Vector3.up * 1.3f; roots[0] = new Vector4(far.x, far.y, far.z, 1.6f);
                Vector3 l = animator.GetBoneTransform(HumanBodyBones.LeftHand).position, r = animator.GetBoneTransform(HumanBodyBones.RightHand).position;
                left[2] = new Vector4(l.x, l.y, l.z, -1f); right[2] = new Vector4(r.x, r.y, r.z, -1f);
                var chest = (animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine)).position;
                chests[2] = chest; roots[2] = new Vector4(animator.transform.position.x, animator.transform.position.y, animator.transform.position.z, 1.6f);
            }
            Shader.SetGlobalVectorArray("_CVR_PlayerLeftHandPositions", left);
            Shader.SetGlobalVectorArray("_CVR_PlayerRightHandPositions", right);
            Shader.SetGlobalVectorArray("_CVR_PlayerChestPositions", chests);
            Shader.SetGlobalVectorArray("_CVR_PlayerRootPositions", roots);
            Shader.SetGlobalVector("CVRGlobalParams1", new Vector4(0f, third ? 3f : 2f, 0f, 0f));
        }

        const float Tall = 1.7f;
        struct Shot { public string name; public int frames; public Action<float, float> pose; public Vector3 at; public float width; public bool profile; }
        static List<Shot> film;
        static List<string> segments;
        static Camera[] views;
        static Transform[] ghost;
        static Texture2D sheet;
        static MeshCollider body;
        static int shotIndex, shotStart, filmed;
        static float handRadius;
        static Vector3 outside;
        static Material[] glass;
        static float pulseWas;

        // Each shot eases in over its first third, holds, and eases out, so s runs 0, 1, 0.
        static void Film(Transform[] slots, Material[] mats)
        {
            if (film == null) StartFilm(slots, mats);
            bool cut = frame == Bend;
            while (shotIndex < film.Count && frame - shotStart >= film[shotIndex].frames) { shotStart += film[shotIndex++].frames; cut = true; }
            if (shotIndex == film.Count)
            {
                File.WriteAllLines(Path.Combine(outDir, "segments.tsv"), segments);
                // The pulse shot set it on the material assets, which outlive Play mode: the
                // build it filmed was left pulsing a little in game.
                foreach (var m in mats) m.SetFloat("_YAPS_SquashPulse", pulseWas);
                AssetDatabase.SaveAssets();
                SessionState.SetInt(K + "on", 0);
                Debug.Log($"[SquashPlay] filmed {filmed} frames into {outDir}");
                EditorApplication.Exit(0);
                return;
            }
            var shot = film[shotIndex];
            if (cut)
            {
                segments.Add($"{filmed}\t{shot.name}");
                float away = shot.width / 2f / Mathf.Tan(15f * Mathf.Deg2Rad);
                var root = animator.transform;
                var from = new[] { shot.profile ? outside : root.forward, (root.forward - root.right).normalized, (root.forward * 0.75f + root.up * 0.66f).normalized };
                for (int v = 0; v < 3; v++) { views[v].transform.position = shot.at + from[v] * away; views[v].transform.LookAt(shot.at); }
            }
            float u = (frame - shotStart) / (float)shot.frames;
            float s = u < 0.35f ? Mathf.SmoothStep(0f, 1f, u / 0.35f) : u < 0.65f ? 1f : Mathf.SmoothStep(1f, 0f, (u - 0.65f) / 0.35f);
            Press();
            shot.pose(s, u);
            if ((frame - Bend) % 2 != 0) return;
            for (int v = 0; v < 3; v++)
            {
                // Faint where it would hide the dent, plain from above.
                foreach (var g in ghost) g.GetComponent<Renderer>().sharedMaterial = glass[v == 2 ? 1 : 0];
                views[v].Render();
                RenderTexture.active = views[v].targetTexture;
                sheet.ReadPixels(new Rect(0, 0, 640, 640), v * 640, 0);
            }
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(outDir, "frames", $"f_{filmed++:00000}.jpg"), sheet.EncodeToJPG(92));
        }

        static void StartFilm(Transform[] slots, Material[] mats)
        {
            Directory.CreateDirectory(Path.Combine(outDir, "frames"));
            pulseWas = mats.Length > 0 ? mats[0].GetFloat("_YAPS_SquashPulse") : 0f;
            segments = new List<string>();
            shotStart = Bend;
            sheet = new Texture2D(1920, 640, TextureFormat.RGB24, false);
            handRadius = mats[0].GetVector("_YAPS_SquashHands").y;
            var root = animator.transform;
            Vector3 f = root.forward, up = root.up;
            views = Enumerable.Range(0, 3).Select(_ =>
            {
                var cam = new GameObject("SquashPlay view").AddComponent<Camera>();
                cam.enabled = false;
                cam.nearClipPlane = 0.01f;
                cam.fieldOfView = 30f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
                cam.targetTexture = new RenderTexture(640, 640, 24);
                return cam;
            }).ToArray();
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            // The latex reads black under one light: a key from either front side as well.
            RenderSettings.ambientLight = new Color(1f, 1f, 1f);
            foreach (var (from, power) in new[] { (f - root.right, 1f), (f + root.right, 0.7f) })
            {
                var fill = new GameObject("SquashPlay fill").AddComponent<Light>();
                fill.type = LightType.Directional;
                fill.intensity = power;
                fill.transform.rotation = Quaternion.LookRotation(-from + Vector3.down * 0.4f);
            }
            // The camera renders HDR under four lights, so the faint one is dimmed as well.
            glass = new[] { new Color(0.15f, 0.27f, 0.35f, 0.12f), new Color(0.45f, 0.8f, 1f, 0.3f) }
                .Select(c => new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse")) { color = c }).ToArray();
            ghost = Enumerable.Range(0, 12).Select(_ =>
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
                go.SetActive(false);
                return go.transform;
            }).ToArray();

            // The rest surface to press from, read off a baked copy of the body. The squash is in the
            // shader alone, so the bake is the shape before any press.
            var smr = animator.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.sharedMaterials.Any(m => mats.Contains(m)));
            if (smr != null)
            {
                var baked = new Mesh();
                smr.BakeMesh(baked);
                var go = new GameObject("SquashPlay body");
                go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                body = go.AddComponent<MeshCollider>();
                body.sharedMesh = baked;
            }
            Debug.Log($"[SquashPlay] film body {(smr != null ? smr.name : "none")}");

            int p = (slots[0].position - target).sqrMagnitude < (slots[1].position - target).sqrMagnitude ? 0 : 1;
            Vector3 P = slots[p].position, Q = slots[1 - p].position, mid = (P + Q) / 2f;
            float reach = mats[0].GetVector("_YAPS_SquashReach")[p] * slots[p].lossyScale.x;
            var outP = Vector3.ProjectOnPlane(P - Q, up).normalized;
            outside = outP;
            var front = Surface(target + f * 0.4f, -f, target - f * 0.07f);
            var between = Surface(mid + f * 0.4f, -f, mid + f * reach * 0.5f);
            var sideP = Surface(P + f * 0.02f + outP * 0.6f, -outP, P + outP * reach);
            var sideQ = Surface(Q + f * 0.02f - outP * 0.6f, outP, Q - outP * reach);
            var under = Surface(P + f * 0.06f - up * 0.6f, up, P - up * reach);
            float r = handRadius * Tall / 1.6f, hand = 0.11f * Tall, fist = 0.06f * Tall;

            // A hand pointing along `dir` with its tip `tip` past the skin, short of it when negative.
            // The fist presses with the knuckles, every other shape with the fingertips.
            (Vector3, Vector3, float) Tip(Vector3 skin, Vector3 dir, float g, float tip) =>
                (skin + dir * (tip - (g > 0.5f && g < 2.5f ? fist : hand)), dir, g);
            Vector3 On(Vector3 n) => Surface(P + n * 0.35f, -n, P + n * reach * 0.5f);
            var mirrored = target - P - 2f * outP * Vector3.Dot(target - P, outP);
            var frontQ = Surface(Q + mirrored + f * 0.4f, -f, Q + mirrored - f * 0.07f);
            var top = Surface(P + up * 0.35f + f * 0.05f, -up, P + up * reach * 0.6f);
            var spots = new[] { On((f + up * 0.8f).normalized), On((outP + f * 0.4f).normalized), On((f * 0.5f - up).normalized), On((f + up * 0.5f - outP * 0.5f).normalized) };
            Action<float, float> Front(float g, float deep) => (s, u) => Press(Tip(front, -f, g, Mathf.Lerp(-0.04f, deep, s)));
            film = new List<Shot>
            {
                new Shot { name = "Another player: open hand", frames = 180, pose = Front(-1f, 0.05f), at = front, width = 0.45f, profile = true },
                new Shot { name = "Another player: fist", frames = 180, pose = Front(1f, 0.05f), at = front, width = 0.45f, profile = true },
                new Shot { name = "Another player: pointing finger", frames = 180, pose = Front(4f, 0.07f), at = front, width = 0.45f, profile = true },
                new Shot { name = "Another player: two fingers", frames = 180, pose = Front(5f, 0.07f), at = front, width = 0.45f, profile = true },
                new Shot { name = "Poking around it: top, outside, underneath, inside", frames = 360, at = P + f * 0.04f, width = 0.6f, pose = (s, u) =>
                {
                    int k = Mathf.Min((int)(u * 4f), 3);
                    var n = (spots[k] - P).normalized;
                    Press(Tip(spots[k], -n, 4f, Mathf.Lerp(-0.04f, 0.06f, Beats(u * 4f - k, 1))));
                } },
                new Shot { name = "Quick pokes", frames = 180, at = front, width = 0.45f, profile = true, pose = (s, u) => Press(Tip(front, -f, 4f, Mathf.Lerp(-0.03f, 0.05f, Beats(u, 5)))) },
                new Shot { name = "A deep poke", frames = 240, pose = Front(4f, 0.11f), at = front, width = 0.45f, profile = true },
                new Shot { name = "A finger between them", frames = 210, at = between, width = 0.6f, pose = (s, u) => Press(Tip(between, -f, 4f, Mathf.Lerp(-0.04f, 0.09f, s))) },
                new Shot { name = "Two players poking at once", frames = 240, at = mid + f * 0.05f, width = 0.9f, pose = (s, u) =>
                    Press(Tip(front, -f, 4f, Mathf.Lerp(-0.03f, 0.06f, Beats(u, 3))), Tip(frontQ, -f, 4f, Mathf.Lerp(-0.03f, 0.06f, Beats(u + 1f / 6f, 3)))) },
                new Shot { name = "A hand pushed in between them", frames = 210, at = between, width = 0.7f, pose = (s, u) => Press(Tip(between, -f, -1f, Mathf.Lerp(-0.06f, 0.08f, s))) },
                new Shot { name = "Two hands squeezing them together", frames = 360, at = mid + f * 0.05f, width = 1f, pose = (s, u) =>
                {
                    float b = Beats(u, 2);
                    Press((sideP + f * 0.1f + outP * (r + Mathf.Lerp(0.03f, -0.13f, b)), -f, -1f), (sideQ + f * 0.1f - outP * (r + Mathf.Lerp(0.03f, -0.13f, b)), -f, -1f));
                } },
                new Shot { name = "Grabbed and squeezed, top and bottom", frames = 300, at = P + f * 0.05f, width = 0.6f, profile = true, pose = (s, u) =>
                {
                    float b = Beats(u, 2);
                    Press((top + f * 0.1f + up * (r + Mathf.Lerp(0.03f, -0.09f, b)), -f, -1f), (under + f * 0.1f - up * (r + Mathf.Lerp(0.03f, -0.09f, b)), -f, -1f));
                } },
                new Shot { name = "Both hands on both", frames = 300, at = mid + f * 0.05f, width = 0.9f, pose = (s, u) =>
                    Press(Tip(front, -f, -1f, Mathf.Lerp(-0.03f, 0.08f, Beats(u, 3))), Tip(frontQ, -f, -1f, Mathf.Lerp(-0.03f, 0.08f, Beats(u + 0.08f, 3)))) },
                new Shot { name = "A hand lifting from below", frames = 210, at = under + up * 0.05f, width = 0.5f, pose = (s, u) =>
                {
                    var w = under + f * 0.12f - up * (r + Mathf.Lerp(0.03f, -0.05f, s));
                    Press((w, (-f * 0.45f + up * 0.1f).normalized, -1f));
                } },
                new Shot { name = "Own hand", frames = 240, at = front, width = 0.6f, profile = true, pose = (s, u) => Reach(s) },
                new Shot { name = "Test pulse: squeezed together from outside", frames = 480, at = mid + f * 0.05f, width = 0.9f, pose = (s, u) =>
                {
                    foreach (var m in mats) m.SetFloat("_YAPS_SquashPulse", 0.3f * Mathf.Clamp01(Mathf.Min(u, 1f - u) / 0.1f));
                } },
            };
        }

        static float Beats(float u, int n) => Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Repeat(u * n, 1f)), 2f);

        static Vector3 Surface(Vector3 from, Vector3 dir, Vector3 fallback)
        {
            var h = default(RaycastHit);
            bool hit = body != null && body.Raycast(new Ray(from, dir), out h, 2f);
            Debug.Log($"[SquashPlay] surface from {from:F3} along {dir:F2}: {(hit ? h.point.ToString("F3") : "missed, using " + fallback.ToString("F3"))}");
            return hit ? h.point : fallback;
        }

        // Other players' hands, a player each so each points its own way: the shader takes a
        // hand's direction from its player's chest, set here behind the wrist. Drawn as the
        // capsules the shader presses with.
        static void Press(params (Vector3 wrist, Vector3 dir, float g)[] hands)
        {
            var left = new Vector4[255];
            var chests = new Vector4[255];
            var roots = new Vector4[255];
            for (int i = 0; i < ghost.Length / 3; i++)
            {
                bool on = i < hands.Length;
                for (int k = 0; k < 3; k++) ghost[i * 3 + k].gameObject.SetActive(on && (k != 1 || hands[i].g > 2.5f));
                if (!on) continue;
                var (at, dir, g) = hands[i];
                var chest = at - dir * 0.45f;
                left[i + 1] = new Vector4(at.x, at.y, at.z, g);
                chests[i + 1] = chest;
                roots[i + 1] = new Vector4(chest.x, 0f, chest.z, Tall);
                float rad = handRadius * Tall / 1.6f, fist = g > 1.5f ? 1f : Mathf.Clamp01(g);
                float rf = (g > 4.5f && g < 5.5f ? 0.35f : 0.2f) * rad;
                Capsule(ghost[i * 3], at, at + dir * Mathf.Max(Mathf.Lerp(0.11f, 0.06f, fist) * Tall - rad, 0f), rad);
                Capsule(ghost[i * 3 + 1], at + dir * (0.06f * Tall), at + dir * Mathf.Max(0.11f * Tall - rf, 0.06f * Tall), rf);
                Capsule(ghost[i * 3 + 2], at, at - dir * 0.25f, rad * 0.7f);
            }
            Shader.SetGlobalVectorArray("_CVR_PlayerLeftHandPositions", left);
            Shader.SetGlobalVectorArray("_CVR_PlayerRightHandPositions", new Vector4[255]);
            Shader.SetGlobalVectorArray("_CVR_PlayerChestPositions", chests);
            Shader.SetGlobalVectorArray("_CVR_PlayerRootPositions", roots);
            Shader.SetGlobalVector("CVRGlobalParams1", new Vector4(0f, hands.Length + 1, 0f, 0f));
        }

        static void Capsule(Transform t, Vector3 a, Vector3 b, float r)
        {
            var d = b - a;
            t.position = (a + b) / 2f;
            t.rotation = d.sqrMagnitude > 1e-10f ? Quaternion.FromToRotation(Vector3.up, d) : Quaternion.identity;
            t.localScale = new Vector3(2f * r, d.magnitude / 2f + r, 2f * r);
        }

        // The wrist toward the target: the elbow bends until the shoulder to wrist distance
        // matches, then the upper arm turns to point at it.
        static void Reach(float t)
        {
            shoulder.rotation = shoulderRest;
            elbow.rotation = elbowRest;
            float a = Vector3.Distance(shoulder.position, elbow.position), b = Vector3.Distance(elbow.position, wrist.position);
            var goal = Vector3.Lerp(wrist.position, target, t);
            float c = Mathf.Clamp(Vector3.Distance(shoulder.position, goal), Mathf.Abs(a - b) + 1e-4f, a + b - 1e-4f);
            var upper = elbow.position - shoulder.position;
            var lower = wrist.position - elbow.position;
            var axis = Vector3.Cross(upper, lower);
            if (axis.sqrMagnitude < 1e-10f) axis = Vector3.Cross(upper, animator.transform.forward);
            float now = Vector3.Angle(-upper, lower);
            float want = Mathf.Acos(Mathf.Clamp((a * a + b * b - c * c) / (2f * a * b), -1f, 1f)) * Mathf.Rad2Deg;
            elbow.rotation = Quaternion.AngleAxis(now - want, axis.normalized) * elbow.rotation;
            shoulder.rotation = Quaternion.FromToRotation(wrist.position - shoulder.position, goal - shoulder.position) * shoulder.rotation;
        }

        static void OnLate()
        {
            if (!EditorApplication.isPlaying || provider == null) return;
            var slots = provider.transforms;
            for (int p = 0; p < 4; p++) late[p] = (slots[p].position - slots[4 + p].position).magnitude;
            lastEnd = slots.Select(t => t.position).ToArray();
        }

        // What the skinned helper holds when the provider reads it: the last skinning pass.
        static Vector3[] SkinPositions()
        {
            if (skin == null) return null;
            try
            {
                using (var buffer = skin.GetVertexBuffer())
                {
                    if (buffer == null) return null;
                    var data = new float[buffer.count * buffer.stride / 4];
                    buffer.GetData(data);
                    int step = buffer.stride / 4;
                    return Enumerable.Range(0, 16).Select(i => new Vector3(data[i * step], data[i * step + 1], data[i * step + 2])).ToArray();
                }
            }
            catch (Exception e)
            {
                if (frame % 60 == 0) Debug.LogWarning("[SquashPlay] skin read: " + e.Message);
                return null;
            }
        }

        static void Log(Transform[] slots, Material m)
        {
            var written = m.GetMatrixArray("_YAPS_SquashFollow");
            bool writes = written != null && written.Length > 0 && (written[0].GetColumn(3) - (Vector4)slots[0].position).sqrMagnitude < 1e-8f;
            var reach = m.GetVector("_YAPS_SquashReach");
            var push = m.GetVector("_YAPS_SquashPush");
            var skinned = SkinPositions();
            string F(float x) => x.ToString("0.00", CultureInfo.InvariantCulture);
            string worst = skinned == null || lastEnd == null ? "-1" : F(Enumerable.Range(0, 16)
                .Where(i => slots[i].lossyScale.x > 0f).Max(i => (skinned[i] - lastEnd[i]).magnitude) * 100f);
            for (int p = 0; p < 4; p++)
            {
                if (slots[p].lossyScale.x <= 0f) continue;
                var off = slots[p].position - slots[4 + p].position;
                var lifted = new Vector3(off.x, Mathf.Max(off.y, 0f), off.z);
                float press = Mathf.Max(lifted.magnitude - push.y * reach[p] * slots[p].lossyScale.x, 0f) * push.x;
                rows.Add(string.Join("\t", frame, p, (off.magnitude * 100f).ToString("0.00", CultureInfo.InvariantCulture),
                    (press * 100f).ToString("0.00", CultureInfo.InvariantCulture), writes ? 1 : 0,
                    (late.TryGetValue(p, out float l) ? l * 100f : -1f).ToString("0.00", CultureInfo.InvariantCulture),
                    skinned == null ? "-1" : F((skinned[p] - skinned[4 + p]).magnitude * 100f),
                    skinned == null || lastEnd == null ? "-1" : F((skinned[p] - lastEnd[p]).magnitude * 100f), worst));
            }
        }

        static void Set(Material[] mats, Func<Vector4, Vector4> change)
        {
            foreach (var m in mats) m.SetVector("_YAPS_SquashPush", change(m.GetVector("_YAPS_SquashPush")));
        }

        static void SetUpCameras(Vector3 at, float reach)
        {
            var root = animator.transform;
            Camera Make(Vector3 from)
            {
                var cam = new GameObject("SquashPlay camera").AddComponent<Camera>();
                cam.enabled = false;
                cam.nearClipPlane = 0.01f;
                cam.fieldOfView = 30f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
                cam.transform.position = from;
                cam.transform.LookAt(at);
                cam.targetTexture = new RenderTexture(800, 800, 24);
                return cam;
            }
            float away = reach * 7f;
            front = Make(at + root.forward * away);
            side = Make(at + (root.forward - root.right).normalized * away);
            {
                var sun = new GameObject("SquashPlay light").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.LookRotation(-root.forward + Vector3.down * 0.5f);
            }
        }

        static void Shoot(string name)
        {
            var pixels = new Texture2D(800, 800, TextureFormat.RGB24, false);
            foreach (var (cam, view) in new[] { (front, "front"), (side, "side") })
            {
                cam.Render();
                RenderTexture.active = cam.targetTexture;
                pixels.ReadPixels(new Rect(0, 0, 800, 800), 0, 0);
                File.WriteAllBytes(Path.Combine(outDir, $"{name}_{view}.png"), pixels.EncodeToPNG());
            }
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(pixels);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
#endif
