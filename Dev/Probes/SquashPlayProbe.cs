// Plays a scene holding the shader squash test and films the press sources: the part at rest,
// then one arm bent so the hand lies across the nearest part, filmed with every source on, with
// the arms off, and with the bone push off too. Per frame, how far each part's follow sits off
// its rest and the press that makes, to see whether settled cloth reads as a press.
//
// -executeMethod AvatarBridge.Regression.SquashPlayProbe.Run -spScene <scene> -spOut <dir> [-spOther]
//
// -spOther leaves the arms alone and presses with another player's hand instead, through the
// client's player globals: first a palm on the skin with the wrist outside, then the wrist sunk in.
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
        static Vector4 tune;
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
            if (frame == Bend - 1) Shoot("rest");
            if (SessionState.GetInt(K + "other", 0) == 1)
            {
                if (frame >= Bend) Other(frame < End - 15 ? 0.15f : 0f);
                if (frame == End - 21 || frame == End - 11) Debug.Log($"[SquashPlay] other: params1 {Shader.GetGlobalVector("CVRGlobalParams1")} " +
                    $"hand1 {(Vector3)Shader.GetGlobalVectorArray("_CVR_PlayerLeftHandPositions")[1]} target {target} part {slots[0].position} {slots[1].position} " +
                    $"wrists {slots[10].position} {slots[14].position} hands {mats[0].GetVector("_YAPS_SquashHands")} power {mats[0].GetFloat("_YAPS_DentPower")}");
                if (frame == End - 20) Shoot("other_palm");
                if (frame == End - 10) Shoot("other_wrist_in");
                // The same press with less bend and a deeper dent, then none and deeper still.
                if (frame == End - 9) { tune = mats[0].GetVector("_YAPS_SquashTune"); foreach (var m in mats) m.SetVector("_YAPS_SquashTune", new Vector4(tune.x, tune.y, 0.1f, 0.6f)); }
                if (frame == End - 7) Shoot("other_wrist_in_bend10_dent60");
                if (frame == End - 6) foreach (var m in mats) m.SetVector("_YAPS_SquashTune", new Vector4(tune.x, tune.y, 0f, 0.9f));
                if (frame == End - 5) { Shoot("other_wrist_in_bend0_dent90"); foreach (var m in mats) m.SetVector("_YAPS_SquashTune", tune); frame = End - 4; }
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
        static void Other(float back)
        {
            var forward = animator.transform.forward;
            var wristAt = target + forward * back;
            var hands = new Vector4[255];
            var chests = new Vector4[255];
            var roots = new Vector4[255];
            hands[1] = wristAt;
            chests[1] = wristAt + forward * 0.45f;
            roots[1] = new Vector4(chests[1].x, 0f, chests[1].z, 1.7f);
            Shader.SetGlobalVectorArray("_CVR_PlayerLeftHandPositions", hands);
            Shader.SetGlobalVectorArray("_CVR_PlayerRightHandPositions", new Vector4[255]);
            Shader.SetGlobalVectorArray("_CVR_PlayerChestPositions", chests);
            Shader.SetGlobalVectorArray("_CVR_PlayerRootPositions", roots);
            Shader.SetGlobalVector("CVRGlobalParams1", new Vector4(0f, 2f, 0f, 0f));
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
