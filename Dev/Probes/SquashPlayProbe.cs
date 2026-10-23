// Plays a scene holding the shader squash test and films the press sources: the part at rest,
// then one arm bent so the hand lies across the nearest part, filmed with every source on, with
// the arms off, and with the bone push off too. Per frame, how far each part's follow sits off
// its rest and the press that makes, to see whether settled cloth reads as a press.
//
// -executeMethod AvatarBridge.Regression.SquashPlayProbe.Run -spScene <scene> -spOut <dir> [-spOther]
//
// -spOther leaves the arms alone and presses with another player's hand instead, through the
// client's player globals: one shot per hand shape (open, fist, pointing, two fingers), the palm
// again as a third client lays the arrays out, then the wrist sunk in. -spPart 1 presses the
// pair's second part instead of the one nearest the left hand (the shader filter cut every part
// loop after its first part, so only a press on a later part shows it).
//
// -spVideo films one continuous run instead, three views a frame at 30 fps into <dir>/frames,
// with segments.tsv naming each segment's first frame: the avatar hopping with the stretch in
// motion off, as real flesh and as anime, a plug parting the pair, another player's hand in each shape,
// pokes round the part, quick and deep, two players at once, hands between, squeezing and
// grabbing, a hand from below, the own arm, then the pair pulse. Other players' hands are drawn
// as glass capsules the size the shader presses with.
//
// -spCost times the view rendered with the squash off, on with nobody near, with 64 players far
// off and with 64 players' hands on the part, alone and among nine copies of the avatar, into
// <dir>/cost.tsv.
//
// -spJump sweeps the own arm across the pair at five depths and heights instead, and reads back
// every vertex's squash move each frame, into <dir>/jump.tsv: the largest change in one frame, and
// which vertex. The read runs the project's own yaps_dent.cginc, not the patched copy, so an edit
// to it is measured without rebuilding the avatar. A snap is a change far past the arm's own step.
// The same sweep is read again with one stage at a time taken out of a copy of it, into
// jump_<stage>.tsv, so whichever copy loses a snap names where it comes from. Other copies read
// a value out in place of the move, as each vertex's share of it: the press, the bend, the
// shares, the dent's move, and the flatten's lines, amounts and pushes. -spJumpVertex N logs one
// vertex's move in every copy each frame, into jump_vertex.tsv.
//
// -spLook draws the pair with the project's own yaps_dent.cginc too, lit from the camera over a
// lightened texture so a black garment still shows its shape, from the front, from above and
// close: at rest, then squeezed together from outside at two strengths, into
// <dir>/look_<name>.png. For judging the shape where the pair meets.
//
// Batch, never -nographics: the frames are rendered.

#if AVATARBRIDGE_YAPS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
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
            SessionState.SetInt(K + "part", Environment.GetCommandLineArgs().Contains("-spPart") ? int.Parse(Arg("-spPart")) : -1);
            SessionState.SetInt(K + "video", Environment.GetCommandLineArgs().Contains("-spVideo") ? 1 : 0);
            SessionState.SetInt(K + "plug", Environment.GetCommandLineArgs().Contains("-spPlug") ? 1 : 0);
            SessionState.SetInt(K + "cost", Environment.GetCommandLineArgs().Contains("-spCost") ? 1 : 0);
            SessionState.SetInt(K + "jump", Environment.GetCommandLineArgs().Contains("-spJump") ? 1 : 0);
            SessionState.SetInt(K + "look", Environment.GetCommandLineArgs().Contains("-spLook") ? 1 : 0);
            if (Environment.GetCommandLineArgs().Contains("-spLook"))
            {
                File.WriteAllText(LookPath, LookShader);
                AssetDatabase.ImportAsset(LookPath, ImportAssetOptions.ForceUpdate);
            }
            if (Environment.GetCommandLineArgs().Contains("-spJump")) WriteJumpShaders();
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
                if (SessionState.GetInt(K + "part", -1) >= 0) left = SessionState.GetInt(K + "part", -1) == 0;
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
            if (SessionState.GetInt(K + "cost", 0) == 1) { if (frame == Bend) Cost(slots, mats); return; }
            if (SessionState.GetInt(K + "jump", 0) == 1) { if (frame >= Bend) JumpPose(slots, mats); return; }
            if (SessionState.GetInt(K + "look", 0) == 1) { if (frame >= Bend) LookPose(slots, mats); return; }
            if (SessionState.GetInt(K + "video", 0) == 1) { if (frame >= Bend) Film(slots, mats); return; }
            if (frame == Bend - 1) Shoot("rest");
            if (SessionState.GetInt(K + "plug", 0) == 1)
            {
                // A plug light from below, its shaft up through the socket between the pair: short of
                // the socket, then a third and two thirds of its length through.
                var shots = new (float through, string name)[] { (-0.05f, "plug_short"), (0.10f, "plug_in"), (0.20f, "plug_deep") };
                int at = frame - (End - 30);
                var s = shots[Mathf.Clamp(at, 0, shots.Length - 1)];
                if (frame >= Bend) Plug(slots[18], slots[0], slots[1], s.through);
                // The same frame again without the light, so the cloth cannot tell them apart.
                if (at >= 0 && at < shots.Length) { Shoot(s.name); plug.enabled = false; Shoot(s.name + "_off"); plug.enabled = true; }
                if (at == shots.Length - 1) frame = End - 21;
            }
            else if (SessionState.GetInt(K + "other", 0) == 1)
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

        static Light plug;
        // Long enough to read on a large pair: the shader sizes the shaft from the length alone.
        const float PlugLength = 0.30f;

        // A plug's protocol light: black, range ending in 8, intensity its length, at its base.
        static void Plug(Transform socket, Transform a, Transform b, float through)
        {
            if (plug == null)
            {
                plug = new GameObject("Test plug light").AddComponent<Light>();
                plug.type = LightType.Point;
                plug.color = Color.black;
                plug.renderMode = LightRenderMode.ForceVertex;
                plug.range = 0.483f;
                plug.intensity = PlugLength;
                // Where the pair meets, as a socket placed in the cleavage would be. The provider
                // reads the transform live, so moving it here is what the shader sees.
                socket.position = 0.5f * (a.position + b.position);
                Debug.Log($"[SquashPlay] plug: socket {socket.name} at {socket.position}, scale {socket.lossyScale.x}");
            }
            var dir = (Vector3.up + animator.transform.forward * 0.3f).normalized;
            plug.transform.position = socket.position - dir * (PlugLength - through);
        }

        // Each case rendered 40 times at 1920x1080, the first five dropped, a pixel read back after
        // each so the GPU has finished: wall time for culling, the draws and the GPU together.
        static void Cost(Transform[] slots, Material[] mats)
        {
            var root = animator.transform;
            var cam = new GameObject("SquashPlay cost").AddComponent<Camera>();
            cam.enabled = false;
            cam.targetTexture = new RenderTexture(1920, 1080, 24);
            cam.transform.position = root.position + root.forward * 5f + Vector3.up * 1.3f;
            cam.transform.LookAt(root.position + Vector3.up);
            var probe = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var part = slots[0].position;
            float power = mats[0].GetFloat("_YAPS_DentPower");
            var renderers = root.GetComponentsInChildren<Renderer>();
            var copies = new List<GameObject>();
            var lines = new List<string> { "avatars	case	median_ms	fastest_ms" };
            int verts = root.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.sharedMaterials.Any(mats.Contains)).Sum(r => r.sharedMesh.vertexCount);

            void Players(int n, bool near)
            {
                var hands = new Vector4[255];
                var chests = new Vector4[255];
                var feet = new Vector4[255];
                for (int i = 1; i <= n; i++)
                {
                    float a = i * 2.4f;
                    var at = near ? part + root.forward * 0.04f + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.03f
                                  : root.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (4f + 0.1f * i) + Vector3.up;
                    hands[i] = new Vector4(at.x, at.y, at.z, -1f);
                    chests[i] = at + root.forward * 0.45f;
                    feet[i] = new Vector4(chests[i].x, 0f, chests[i].z, 1.7f);
                }
                Shader.SetGlobalVectorArray("_CVR_PlayerLeftHandPositions", hands);
                Shader.SetGlobalVectorArray("_CVR_PlayerRightHandPositions", hands);
                Shader.SetGlobalVectorArray("_CVR_PlayerChestPositions", chests);
                Shader.SetGlobalVectorArray("_CVR_PlayerRootPositions", feet);
                Shader.SetGlobalVector("CVRGlobalParams1", new Vector4(0f, n + 1, 0f, 0f));
            }
            void Time(string name)
            {
                var ms = new List<double>();
                for (int k = 0; k < 40; k++)
                {
                    var w = System.Diagnostics.Stopwatch.StartNew();
                    cam.Render();
                    RenderTexture.active = cam.targetTexture;
                    probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                    RenderTexture.active = null;
                    w.Stop();
                    if (k >= 5) ms.Add(w.Elapsed.TotalMilliseconds);
                }
                ms.Sort();
                string row = $"{1 + copies.Count}	{name}	{ms[ms.Count / 2].ToString("0.000", CultureInfo.InvariantCulture)}	{ms[0].ToString("0.000", CultureInfo.InvariantCulture)}";
                lines.Add(row);
                Debug.Log("[SquashPlay] cost " + row);
            }
            void Cases()
            {
                foreach (var r in renderers) r.enabled = false;
                foreach (var c in copies) c.SetActive(false);
                Players(0, false);
                Time("no avatar");
                foreach (var r in renderers) r.enabled = true;
                foreach (var c in copies) c.SetActive(true);
                foreach (var m in mats) m.SetFloat("_YAPS_DentPower", 0f);
                Time("squash off");
                foreach (var m in mats) m.SetFloat("_YAPS_DentPower", power);
                Time("squash on, nobody else");
                Players(16, false);
                Time("16 players, far off");
                Players(64, false);
                Time("64 players, far off");
                Players(64, true);
                Time("64 players' hands on one part");
            }

            Cases();
            // A room: nine more copies behind and beside it, sharing its materials.
            for (int k = 0; k < 9; k++)
                copies.Add(UnityEngine.Object.Instantiate(root.gameObject,
                    root.position + root.right * ((k % 3 - 1) * 1.1f) - root.forward * (1.2f + 1.2f * (k / 3)), root.rotation));
            Cases();
            lines.Add($"squashed vertices per avatar	{verts}");
            File.WriteAllLines(Path.Combine(outDir, "cost.tsv"), lines);
            Players(0, false);
            SessionState.SetInt(K + "on", 0);
            Debug.Log("[SquashPlay] cost done " + outDir);
            EditorApplication.Exit(0);
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
        static YapsSquashSettings knobs;
        static bool stretchWas;
        static float styleWas;
        static Vector3 standing;

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
                if (knobs != null) { knobs.stretchInMotion = stretchWas; knobs.motionStyle = styleWas; YapsSquash.Apply(animator.transform); }
                animator.transform.position = standing;
                File.WriteAllLines(Path.Combine(outDir, "push.tsv"), rows);
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
            if (plug != null) plug.enabled = false;
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
            knobs = YapsSquash.SettingsOf(animator.transform);
            if (knobs != null) { stretchWas = knobs.stretchInMotion; styleWas = knobs.motionStyle; }
            // A stale copy of a renamed script can own the settings' GUID, and the knob shots then
            // film identical frames with nothing failing.
            else Debug.LogWarning("[SquashPlay] no squash settings under the avatar: the stretch and style shots will look the same");
            standing = animator.transform.position;
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
            // Pressed in, then swept side to side while held: two sweeps a shot, wider as it goes in.
            Action<float, float> Front(float g, float deep) => (s, u) =>
            {
                var (wrist, dir, shape) = Tip(front, -f, g, Mathf.Lerp(-0.04f, deep, s));
                Press((wrist + outP * (0.07f * s * Mathf.Sin(4f * Mathf.PI * u)), dir, shape));
            };
            // Five hops, landing hard, with the motion knobs set as named. Once a hop in the
            // anime one, the same frame rendered with the stretch off as well.
            Action<float, float> Hops(bool stretch, float style, string tag) => (s, u) =>
            {
                if (knobs != null && (knobs.stretchInMotion != stretch || knobs.motionStyle != style))
                {
                    knobs.stretchInMotion = stretch;
                    knobs.motionStyle = style;
                    YapsSquash.Apply(root);
                }
                root.position = standing + up * (0.12f * Mathf.Abs(Mathf.Sin(Mathf.PI * u * 5f)));
                if (tag != null && knobs != null && (frame - shotStart) % 48 == 24)
                {
                    Shoot($"{tag}_{frame}");
                    knobs.stretchInMotion = false; YapsSquash.Apply(root);
                    Shoot($"{tag}_{frame}_off");
                    knobs.stretchInMotion = true; YapsSquash.Apply(root);
                }
            };
            float shaft = Mathf.Clamp(0.1f * PlugLength, 0.012f, 0.06f);
            film = new List<Shot>
            {
                new Shot { name = "Hopping: the physics alone", frames = 240, at = mid + f * 0.05f + up * 0.06f, width = 1f, profile = true, pose = Hops(false, 0f, null) },
                new Shot { name = "Hopping: stretch in motion, real flesh", frames = 240, at = mid + f * 0.05f + up * 0.06f, width = 1f, profile = true, pose = Hops(true, 0f, "real") },
                new Shot { name = "Hopping: stretch in motion, anime", frames = 240, at = mid + f * 0.05f + up * 0.06f, width = 1f, profile = true, pose = Hops(true, 1f, "anime") },
                new Shot { name = "A plug in a socket between them", frames = 300, at = mid + f * 0.05f, width = 0.8f, pose = (s, u) =>
                {
                    root.position = standing;
                    Plug(slots[18], slots[0], slots[1], Mathf.Lerp(-0.05f, 0.20f, s));
                    plug.enabled = true;
                    var dir = (Vector3.up + f * 0.3f).normalized;
                    ghost[0].gameObject.SetActive(true);
                    Capsule(ghost[0], plug.transform.position, plug.transform.position + dir * PlugLength, shaft);
                } },
                new Shot { name = "Another player: open hand", frames = 180, pose = Front(-1f, 0.10f), at = front, width = 0.45f, profile = true },
                new Shot { name = "Another player: fist", frames = 180, pose = Front(1f, 0.10f), at = front, width = 0.45f, profile = true },
                new Shot { name = "Another player: pointing finger", frames = 180, pose = Front(4f, 0.12f), at = front, width = 0.45f, profile = true },
                new Shot { name = "Another player: two fingers", frames = 180, pose = Front(5f, 0.12f), at = front, width = 0.45f, profile = true },
                new Shot { name = "Poking around it: top, outside, underneath, inside", frames = 360, at = P + f * 0.04f, width = 0.6f, pose = (s, u) =>
                {
                    int k = Mathf.Min((int)(u * 4f), 3);
                    var n = (spots[k] - P).normalized;
                    Press(Tip(spots[k], -n, 4f, Mathf.Lerp(-0.04f, 0.06f, Beats(u * 4f - k, 1))));
                } },
                new Shot { name = "Quick pokes", frames = 180, at = front, width = 0.45f, profile = true, pose = (s, u) => Press(Tip(front, -f, 4f, Mathf.Lerp(-0.03f, 0.05f, Beats(u, 5)))) },
                new Shot { name = "A deep poke", frames = 240, pose = Front(4f, 0.16f), at = front, width = 0.45f, profile = true },
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

        const string JumpDir = "Assets/AvatarBridgeOutput/SquashTest/";
        const string LiveDent = "Assets/AvatarBridge/Editor/Yaps/yaps_dent.cginc";
        // Each stage the sweep is read without, by the lines that switch it off in a copy, in
        // pairs of what to find and what to put. Press and bend read those vectors out instead,
        // as each vertex's share of them.
        const string Stop = "    world += mapped;\n";
        const string StopTo = "    position = mul(unity_WorldToObject, float4(start + mapped, 1)).xyz;\n    return;\n";
        static readonly (string name, string[] swaps)[] JumpStages =
        {
            ("full", new string[0]),
            ("nodent", new[] { "float local = deepest > 0 ? deepest * (1 - exp(-in0 / deepest)) : 0;", "float local = 0;" }),
            ("nobend", new[] { "if (p.framed && _YAPS_SquashTune.z > 0)", "if (false)" }),
            ("noflat", new[] { "squashed += ax[c3] * (dot(d, ax[c3]) * along[c3] + shift[c3]);", "squashed += ax[c3] * dot(d, ax[c3]);" }),
            ("noplane", new[] { "if (squashContact[c] <= 0) continue;", "continue;" }),
            ("nocapsule", new[] { "if (dist >= r + soft || dist < 1e-6) continue;", "continue;" }),
            ("press", new[] { "mapped += (moved - world) * shares[m];", "mapped += (hv[0] + hv[1] + hv[2] + hv[3]) * shares[m];", Stop, StopTo }),
            ("bend", new[] { "mapped += (moved - world) * shares[m];", "mapped += bend * shares[m];", Stop, StopTo }),
            ("shares", new[] { "mapped += (moved - world) * shares[m];", "mapped += float3(m == 0, m == 1, m >= 2) * shares[m];", Stop, StopTo }),
            ("dentmove", new[] { "float3 moved = p.centre + squashed + bend * lean;", "float3 moved = world;", Stop, StopTo }),
            ("nodentmove", new[] { "            moved -= d * sink;\n", "" }),
            ("ax0", new[] { "mapped += (moved - world) * shares[m];", "mapped += (ax[0]) * shares[m];", Stop, StopTo }),
            ("ax1", new[] { "mapped += (moved - world) * shares[m];", "mapped += (ax[1]) * shares[m];", Stop, StopTo }),
            ("along", new[] { "mapped += (moved - world) * shares[m];", "mapped += (float3(along[0], along[1], along[2])) * shares[m];", Stop, StopTo }),
            ("shift", new[] { "mapped += (moved - world) * shares[m];", "mapped += (float3(shift[0], shift[1], shift[2])) * shares[m];", Stop, StopTo }),
            ("sum", new[] { "mapped += (moved - world) * shares[m];", "mapped += (S) * shares[m];", Stop, StopTo }),
            ("online", new[] { "mapped += (moved - world) * shares[m];", "mapped += (float3(dot(ax[0], mul(M, ax[0])), dot(ax[1], mul(M, ax[1])), dot(ax[2], mul(M, ax[2])))) * shares[m];", Stop, StopTo }),
            ("lean", new[] { "mapped += (moved - world) * shares[m];", "mapped += (float3(lean, bendBy, 0)) * shares[m];", Stop, StopTo }),
        };

        static void WriteJumpShaders()
        {
            string live = File.ReadAllText(LiveDent);
            foreach (var s in JumpStages)
            {
                string include = LiveDent;
                if (s.swaps.Length > 0)
                {
                    string copy = live.Replace("\r\n", "\n");
                    for (int i = 0; i < s.swaps.Length; i += 2)
                    {
                        if (!copy.Contains(s.swaps[i])) Debug.LogError($"[SquashPlay] jump stage {s.name}: line {i / 2} not found");
                        copy = copy.Replace(s.swaps[i], s.swaps[i + 1]);
                    }
                    include = JumpDir + $"yaps_dent_{s.name}.cginc";
                    File.WriteAllText(include, copy);
                    AssetDatabase.ImportAsset(include, ImportAssetOptions.ForceUpdate);
                }
                string path = JumpDir + $"SquashJump_{s.name}.shader";
                File.WriteAllText(path, JumpShader.Replace("SquashJump\"", $"SquashJump_{s.name}\"").Replace(LiveDent, include));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }

        // Each vertex's move, drawn as one point at a pixel of its own, its index in alpha so the
        // read needs no row order. Rendered as a replacement, so every material keeps its values.
        const string JumpShader = @"Shader ""Hidden/AvatarBridge/SquashJump""
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            #pragma target 5.0
            #include ""UnityCG.cginc""
            #include ""Assets/AvatarBridge/Editor/Yaps/yaps_socket.cginc""
            #include ""Assets/AvatarBridge/Editor/Yaps/yaps_dent.cginc""
            float _JumpWidth, _JumpHeight;
            struct A { float4 vertex : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; uint id : SV_VertexID; };
            struct V { float4 moved : TEXCOORD0; };
            struct G { float4 pos : SV_POSITION; float4 moved : TEXCOORD0; };
            V vert(A v)
            {
                float3 p = v.vertex.xyz, n = v.normal, t = v.tangent.xyz;
                YapsDentDeform(p, n, t, v.id);
                V o;
                o.moved = float4(mul((float3x3)unity_ObjectToWorld, p - v.vertex.xyz), v.id + 1);
                return o;
            }
            [maxvertexcount(3)]
            void geom(triangle V i[3], inout PointStream<G> s)
            {
                [unroll] for (int k = 0; k < 3; k++)
                {
                    uint id = (uint) i[k].moved.w - 1, w = (uint) _JumpWidth;
                    G o;
                    o.pos = float4((float2(id % w, id / w) + 0.5) / float2(_JumpWidth, _JumpHeight) * 2 - 1, 0.5, 1);
                    o.moved = i[k].moved;
                    s.Append(o);
                }
            }
            float4 frag(G i) : SV_Target { return i.moved; }
            ENDCG
        }
    }
}
";
        const int JumpWidth = 1024, JumpSegment = 360;
        // Depth in front of the near part's centre and height above it, in reaches: shallow, half,
        // deep, over the top, underneath.
        static readonly (float depth, float height)[] JumpPaths = { (0.95f, 0f), (0.7f, 0f), (0.45f, 0f), (0.7f, 0.4f), (0.7f, -0.4f) };
        static Camera jumpCam;
        static Texture2D jumpTex;
        static Vector3[][] jumpWas;
        static Vector3 jumpFrom, jumpTo, jumpFront, jumpUp, wristWas, elbowWas;
        // -spJumpVertex N: that vertex's move every frame in every copy, into jump_vertex.tsv.
        static int jumpVertex = -1;
        static List<string> jumpVertexRows;
        static float jumpReach;
        static List<string>[] jumpRows;
        static List<(float jump, int frame)>[] jumpWorst;

        // The left wrist from outside the near part, across its front, to past the far one's centre
        // and back, once per path, the depth and height easing over between paths.
        static void JumpPose(Transform[] slots, Material[] mats)
        {
            if (jumpCam == null)
            {
                int p = (slots[0].position - target).sqrMagnitude < (slots[1].position - target).sqrMagnitude ? 0 : 1;
                Vector3 P = slots[p].position, Q = slots[1 - p].position;
                var root = animator.transform;
                jumpReach = mats[0].GetVector("_YAPS_SquashReach")[p] * slots[p].lossyScale.x;
                var outP = Vector3.ProjectOnPlane(P - Q, Vector3.up).normalized;
                jumpFrom = P + outP * 1.3f * jumpReach;
                jumpTo = Q - outP * 0.3f * jumpReach;
                jumpFront = root.forward;
                jumpUp = root.up;
                var smr = animator.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.sharedMaterials.Any(m => mats.Contains(m)));
                smr.gameObject.layer = 31;
                smr.updateWhenOffscreen = true;
                int height = (smr.sharedMesh.vertexCount + JumpWidth - 1) / JumpWidth;
                Shader.SetGlobalFloat("_JumpWidth", JumpWidth);
                Shader.SetGlobalFloat("_JumpHeight", height);
                jumpCam = new GameObject("SquashJump camera").AddComponent<Camera>();
                jumpCam.enabled = false;
                jumpCam.cullingMask = 1 << 31;
                jumpCam.clearFlags = CameraClearFlags.SolidColor;
                jumpCam.backgroundColor = Color.clear;
                jumpCam.allowHDR = false;
                jumpCam.allowMSAA = false;
                jumpCam.fieldOfView = 60f;
                jumpCam.transform.position = P + jumpFront * 1.5f;
                jumpCam.transform.LookAt(P);
                jumpCam.targetTexture = new RenderTexture(JumpWidth, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                jumpTex = new Texture2D(JumpWidth, height, TextureFormat.RGBAFloat, false, true);
                jumpWas = new Vector3[JumpStages.Length][];
                jumpRows = JumpStages.Select(_ => new List<string> { "frame\tpath\tjump_mm\tvertex\twas_mm\tnow_mm\tover_2mm\tmost_mm\twrist_step_mm\telbow_step_mm" }).ToArray();
                jumpWorst = JumpStages.Select(_ => new List<(float, int)>()).ToArray();
                var cli = Environment.GetCommandLineArgs();
                int vi = Array.IndexOf(cli, "-spJumpVertex");
                jumpVertex = vi >= 0 && vi + 1 < cli.Length ? int.Parse(cli[vi + 1], CultureInfo.InvariantCulture) : -1;
                jumpVertexRows = new List<string> { "frame\tstage\tx_mm\ty_mm\tz_mm" };
                foreach (var s in JumpStages)
                {
                    var shader = Shader.Find($"Hidden/AvatarBridge/SquashJump_{s.name}");
                    if (shader == null || ShaderUtil.ShaderHasError(shader))
                        Debug.LogError($"[SquashPlay] jump shader {s.name}: " + (shader == null ? "missing" : string.Join(" | ", ShaderUtil.GetShaderMessages(shader).Select(m => m.message))));
                }
                Debug.Log($"[SquashPlay] jump: {smr.name}, {smr.sharedMesh.vertexCount} vertices, reach {jumpReach * 100f:F1} cm");
            }
            int t = frame - Bend, seg = t / JumpSegment;
            if (seg >= JumpPaths.Length)
            {
                for (int s = 0; s < JumpStages.Length; s++)
                {
                    File.WriteAllLines(Path.Combine(outDir, s == 0 ? "jump.tsv" : $"jump_{JumpStages[s].name}.tsv"), jumpRows[s]);
                    Debug.Log($"[SquashPlay] jump worst {JumpStages[s].name}: " + string.Join(", ", jumpWorst[s].OrderByDescending(w => w.jump).Take(12).Select(w => $"{w.frame}:{w.jump:F1}")));
                }
                if (jumpVertex >= 0) File.WriteAllLines(Path.Combine(outDir, "jump_vertex.tsv"), jumpVertexRows);
                SessionState.SetInt(K + "on", 0);
                EditorApplication.Exit(0);
                return;
            }
            float u = (t % JumpSegment) / (float)JumpSegment;
            var was = JumpPaths[Mathf.Max(seg - 1, 0)];
            var now = JumpPaths[seg];
            float ease = Mathf.SmoothStep(0f, 1f, u / 0.15f);
            float depth = Mathf.Lerp(was.depth, now.depth, ease), up = Mathf.Lerp(was.height, now.height, ease);
            float across = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * u);
            target = Vector3.Lerp(jumpFrom, jumpTo, across) + (jumpFront * depth + jumpUp * up) * jumpReach;
            Reach(1f);
        }

        static void JumpRead()
        {
            for (int s = 0; s < JumpStages.Length; s++) JumpRead(s);
            wristWas = wrist.position;
            elbowWas = elbow.position;
        }

        static void JumpRead(int s)
        {
            jumpCam.RenderWithShader(Shader.Find($"Hidden/AvatarBridge/SquashJump_{JumpStages[s].name}"), "");
            RenderTexture.active = jumpCam.targetTexture;
            jumpTex.ReadPixels(new Rect(0, 0, jumpTex.width, jumpTex.height), 0, 0);
            RenderTexture.active = null;
            var px = jumpTex.GetPixels();
            var moved = new Vector3[jumpTex.width * jumpTex.height];
            var seen = new bool[moved.Length];
            foreach (var c in px)
            {
                int id = Mathf.RoundToInt(c.a) - 1;
                if (id < 0 || id >= moved.Length) continue;
                moved[id] = new Vector3(c.r, c.g, c.b);
                seen[id] = true;
            }
            if (jumpVertex >= 0 && jumpVertex < moved.Length && seen[jumpVertex])
            {
                string G(float x) => (x * 1000f).ToString("0.00", CultureInfo.InvariantCulture);
                var v = moved[jumpVertex];
                jumpVertexRows.Add(string.Join("\t", frame, JumpStages[s].name, G(v.x), G(v.y), G(v.z)));
            }
            if (jumpWas[s] != null)
            {
                float worst = 0f, most = 0f;
                int at = -1, over = 0;
                for (int i = 0; i < moved.Length; i++)
                {
                    if (!seen[i]) continue;
                    float j = (moved[i] - jumpWas[s][i]).magnitude;
                    most = Mathf.Max(most, moved[i].magnitude);
                    if (j > 0.002f) over++;
                    if (j > worst) { worst = j; at = i; }
                }
                float step = (wrist.position - wristWas).magnitude, elbowStep = (elbow.position - elbowWas).magnitude;
                string F(float x) => (x * 1000f).ToString("0.00", CultureInfo.InvariantCulture);
                jumpRows[s].Add(string.Join("\t", frame, (frame - Bend) / JumpSegment, F(worst), at, at < 0 ? "0" : F(jumpWas[s][at].magnitude),
                    at < 0 ? "0" : F(moved[at].magnitude), over, F(most), F(step), F(elbowStep)));
                jumpWorst[s].Add((worst * 1000f, frame));
            }
            jumpWas[s] = moved;
        }

        const string LookPath = "Assets/AvatarBridgeOutput/SquashTest/SquashLook.shader";
        const string LookShader = @"Shader ""Hidden/AvatarBridge/SquashLook""
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #include ""UnityCG.cginc""
            #include ""Assets/AvatarBridge/Editor/Yaps/yaps_socket.cginc""
            #include ""Assets/AvatarBridge/Editor/Yaps/yaps_dent.cginc""
            sampler2D _MainTex;
            float4 _MainTex_ST;
            struct A { float4 vertex : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; float2 uv : TEXCOORD0; uint id : SV_VertexID; };
            struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 w : TEXCOORD1; float2 uv : TEXCOORD2; };
            V vert(A v)
            {
                float3 p = v.vertex.xyz, n = v.normal, t = v.tangent.xyz;
                YapsDentDeform(p, n, t, v.id);
                V o;
                o.pos = UnityObjectToClipPos(float4(p, 1));
                o.w = mul(unity_ObjectToWorld, float4(p, 1)).xyz;
                o.n = UnityObjectToWorldNormal(n);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            float4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.n);
                float3 l = normalize(normalize(_WorldSpaceCameraPos - i.w) + float3(0, 0.6, 0));
                float k = 0.25 + 0.75 * saturate(dot(n, l));
                return float4((0.55 + 0.45 * tex2D(_MainTex, i.uv).rgb) * k, 1);
            }
            ENDCG
        }
    }
}
";
        // The squeeze from outside at each shot, and which part of its swing to shoot it at.
        static readonly (string name, float pulse, float phase)[] LookShots =
            { ("rest", 0f, 0f), ("squeeze_soft", 0.3f, 0.985f), ("squeeze_hard", 0.6f, 0.985f), ("squeeze_hard_half", 0.6f, 0.5f) };
        static Camera[] lookCams;
        static string lookShot;
        static int lookAt;
        static float lookWas;

        // Every renderer the squash moves, on a layer of its own, and three cameras on the pair's
        // middle. Each shot waits for the squeeze to reach its part of the swing, rising.
        static void LookPose(Transform[] slots, Material[] mats)
        {
            if (lookCams == null)
            {
                var root = animator.transform;
                Vector3 mid = 0.5f * (slots[0].position + slots[1].position);
                float reach = mats[0].GetVector("_YAPS_SquashReach")[0] * slots[0].lossyScale.x;
                foreach (var smr in animator.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.sharedMaterials.Any(m => mats.Contains(m))))
                {
                    smr.gameObject.layer = 31;
                    smr.updateWhenOffscreen = true;
                }
                Camera Make(Vector3 from)
                {
                    var cam = new GameObject("SquashLook camera").AddComponent<Camera>();
                    cam.enabled = false;
                    cam.cullingMask = 1 << 31;
                    cam.nearClipPlane = 0.01f;
                    cam.fieldOfView = 30f;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
                    cam.transform.position = from;
                    cam.transform.LookAt(mid);
                    var rt = new RenderTexture(700, 700, 24) { antiAliasing = 4 };
                    cam.targetTexture = rt;
                    return cam;
                }
                lookCams = new[]
                {
                    Make(mid + root.forward * reach * 6f),
                    Make(mid + root.up * reach * 4f + root.forward * reach * 2.5f),
                    Make(mid + root.forward * reach * 2.6f + root.up * reach * 0.8f - root.right * reach * 0.8f),
                };
                var shader = Shader.Find("Hidden/AvatarBridge/SquashLook");
                if (shader == null || ShaderUtil.ShaderHasError(shader))
                    Debug.LogError("[SquashPlay] look shader: " + (shader == null ? "missing" : string.Join(" | ", ShaderUtil.GetShaderMessages(shader).Select(m => m.message))));
                lookAt = 0;
                lookWas = -1f;
                foreach (var m in mats) m.SetFloat("_YAPS_SquashPulse", LookShots[0].pulse);
            }
            if (lookAt >= LookShots.Length)
            {
                Debug.Log("[SquashPlay] look done " + outDir);
                SessionState.SetInt(K + "on", 0);
                EditorApplication.Exit(0);
                return;
            }
            var shot = LookShots[lookAt];
            float phase = 0.5f - 0.5f * Mathf.Cos(2f * Time.timeSinceLevelLoad);
            bool due = shot.pulse <= 0f ? frame >= Bend + 30 : lookWas >= 0f && lookWas < shot.phase && phase >= shot.phase;
            lookWas = phase;
            if (!due) return;
            lookShot = shot.name;
            lookAt++;
        }

        static void LookShoot()
        {
            var shader = Shader.Find("Hidden/AvatarBridge/SquashLook");
            var sheet = new Texture2D(700 * lookCams.Length, 700, TextureFormat.RGB24, false);
            var one = new Texture2D(700, 700, TextureFormat.RGB24, false);
            for (int c = 0; c < lookCams.Length; c++)
            {
                lookCams[c].RenderWithShader(shader, "");
                RenderTexture.active = lookCams[c].targetTexture;
                one.ReadPixels(new Rect(0, 0, 700, 700), 0, 0);
                sheet.SetPixels(700 * c, 0, 700, 700, one.GetPixels());
            }
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(outDir, $"look_{lookShot}.png"), sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
            UnityEngine.Object.DestroyImmediate(one);
            Debug.Log($"[SquashPlay] look shot {lookShot}");
            lookShot = null;
            if (lookAt < LookShots.Length)
                foreach (var m in provider.materials) m.SetFloat("_YAPS_SquashPulse", LookShots[lookAt].pulse);
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
            if (jumpCam != null) JumpRead();
            if (lookShot != null) LookShoot();
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
