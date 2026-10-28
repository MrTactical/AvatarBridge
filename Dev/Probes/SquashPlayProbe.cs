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
// motion off and on, a plug parting the pair, another player's hand in each shape,
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
// <dir>/look_<name>.png. For judging the shape where the pair meets. Each shot is drawn again as
// the part shares in colour (_shares), as zebra stripes on the shader's normals (_zebra) and on
// the triangles themselves (_zebra_mesh): a kink in a stripe is a crease, and with the skinning
// alone, the squash left out (_raw). -spMeet turns the pair's
// contact on first. -spPreview squeezes with the inspector preview's hands instead (0, 0.5, 1, 1).
// -spSwing turns each part about the bone it hangs from instead, the cloth stopped, across any gap between them and
// on into an overlap: one into a still partner by 0.3 and 0.6 reaches, then both, half each, by
// 0.6 and 1.2.
// -spHand swings the pair to just touching the same way, then presses the first part from its
// outer side with another player's open hand, 0.4 and 0.8 reaches in, bending it into the other.
// Deeper presses less: a press stops at the hand's line, which by then nears the part's centre.
//
// -spDrag moves the first part's chain root into the other over two seconds, as the Move tool
// does in Play mode, then holds it. Each frame after the cloth, into <dir>/drag.tsv: where each
// follow sits off its rest along the pair's line and the lead the pair squash reads from that,
// with shots from the front and above. -spDragBoth moves both chain roots, half as far each.
// -spDragName <object> drags that object instead, found by name under the avatar, and -spMoveTool
// sets it between frames as the Move tool's handle does, not at the start of the frame's Update.
// -spGrab pulls with the Avatar Tester's grab instead: the cloth component named by -spDragName
// pushed each frame toward where the first part's follow bone would be, dragged. -spPush runs the
// Avatar Tester's squash preview and moves its Push one in from 0 to 1 instead. -spDragPair 1
// drags the second pair, shot from behind. -spDragScale f moves it f times as far.
// With -spLook each shot is drawn again in the look shader, every layer tinted its own colour, so
// one coming through another shows, and the squashed layers each alone (_look, _look_<layer>).
// At the drag's end every squashed layer's edges are read back, into tear.tsv: how many the
// squash stretches past two and three times, how many open by a centimetre, how many triangles
// it turns over; tear_edges.tsv lists the worst with where they lie, their shares and how far
// off the skin. -spLookStages draws and counts it again with each stage of the squash switched
// off in turn, in memory, each squashed layer alone too (_look_<layer>_<stage>); the branch stage
// colours the seam's choices in place of the shading. -spDent <file> draws and counts everything
// with that include in place of the live one, in memory: an experiment needs no build.
// -spSeamFilm <file>[,<file>] films the pair pressed into each other six ways in the look shader,
// the live include beside each file's, every column drawn from the same frame; "," films the live
// one alone. Mid hold it counts the tear and the volume the squash adds, as at the drag's end.
// -spReference with it films only the hands' presses, with the pair as real tissue beside them
// (SoftBodyReference), bare and clothed, and logs where each puts the volume. -spBasis adds the
// bare tissue's answers to single presses summed for the same hands, as a bake would store them;
// -spBasisCache <file> keeps those answers between runs, -spBare drops the clothed column.
// -spDestroy <name> destroys the prefab copy's objects so named before Play.
// -spPose <a,b> logs those bones' local moves at the drag's end against before Play.
// -spPrefab <asset path> plays that converted prefab in place of the scene's own avatar, logging
// each cloth's colliders; -spNoColliders <prefixes> takes colliders so named out of them first.
// -spRootScale <s> resizes the avatar before Play, through a parent, with any of the above.
// -spEach adds a front shot per renderer, each drawn alone, at every shot.
// -spRaw switches the squash off on copies of its materials, for skinning alone.
// -spGrabTimes N pushes N times a frame with -spGrab, as mouse drag events can come.
// -spNoContact switches off the converter's soft body spheres once a drag starts, on builds that
// still put them on "SoftBodyContact" children.
// -spRebuild builds the squash again on the played copy, into Assets/__SquashPlayRebuild, so the
// real materials run the deployed code; clear that folder and the new patched shaders after.
// -spSurface gives each part its own surface for the run, as a build with it would: measured from
// a copy of the avatar and set on its squash materials in memory, put back on quitting.
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
            // In a drag, -spLook only redraws the drag's shots.
            SessionState.SetInt(K + "look", Environment.GetCommandLineArgs().Contains("-spLook")
                && !Environment.GetCommandLineArgs().Contains("-spDrag") && !Environment.GetCommandLineArgs().Contains("-spDragBoth") ? 1 : 0);
            SessionState.SetInt(K + "drag", Environment.GetCommandLineArgs().Contains("-spDragBoth") ? 2
                : Environment.GetCommandLineArgs().Contains("-spDrag") ? 1 : 0);
            SessionState.SetString(K + "dragName", Arg("-spDragName") ?? "");
            SessionState.SetInt(K + "moveTool", Environment.GetCommandLineArgs().Contains("-spMoveTool") ? 1 : 0);
            SessionState.SetInt(K + "grab", Environment.GetCommandLineArgs().Contains("-spGrab") ? 1 : 0);
            SessionState.SetInt(K + "push", Environment.GetCommandLineArgs().Contains("-spPush") ? 1 : 0);
            SessionState.SetInt(K + "dragPair", Arg("-spDragPair") == "1" ? 2 : 0);
            if (Environment.GetCommandLineArgs().Contains("-spJump")) WriteJumpShaders();
            var scene = EditorSceneManager.OpenScene(Arg("-spScene"), OpenSceneMode.Single);
            foreach (var r in scene.GetRootGameObjects())
                if (r.GetComponentInChildren<CVRAvatar>(true) == null && r.GetComponentInChildren<Animator>(true) != null) r.SetActive(false);
            // -spPrefab: the avatar as last converted, in place of the scene's copy, which keeps
            // whatever conversion was in the scene when it was saved. Never saved.
            if (Arg("-spPrefab") is string prefabPath && !prefabPath.StartsWith("-"))
            {
                var old = scene.GetRootGameObjects().First(r => r.GetComponentInChildren<CVRAvatar>(true) != null);
                var made = (GameObject) PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), scene);
                made.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
                foreach (var r in scene.GetRootGameObjects())
                    if (r != made && r.GetComponentInChildren<CVRAvatar>(true) != null) r.SetActive(false);
                // Where the first pair's follows sit off their rests before Play, along the pair's line.
                var edit = YapsSquash.Slots(made.transform);
                if (edit != null && edit[0] != null && edit[1] != null)
                {
                    var line = (edit[1].position - edit[0].position).normalized;
                    Debug.Log($"[SquashPlay] edit mode: gap {100f * (edit[1].position - edit[0].position).magnitude:0.00} cm, follows off rest " +
                              $"{100f * Vector3.Dot(edit[0].position - edit[4].position, line):0.00} and {100f * Vector3.Dot(edit[5].position - edit[1].position, line):0.00} cm");
                }
#if AVATARBRIDGE_MAGICA
                // What each soft part collides with, and -spNoColliders <a,b> takes colliders whose
                // names start so out of every list before Play, so the cloth never registers them.
                string[] drop = (Arg("-spNoColliders") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                // -spDestroy <name>: objects so named removed outright, from the bone tree as well.
                if (Arg("-spDestroy") is string gone && !gone.StartsWith("-"))
                    foreach (var t in made.GetComponentsInChildren<Transform>(true).Where(x => x.name == gone).ToList())
                        UnityEngine.Object.DestroyImmediate(t.gameObject);
                foreach (var cloth in made.GetComponentsInChildren<MagicaCloth2.MagicaCloth>(true))
                {
                    var list = cloth.SerializeData.colliderCollisionConstraint.colliderList;
                    list.RemoveAll(c => c == null || drop.Any(d => c.name.StartsWith(d)));
                    // -spNoCloth: the cloth off, so only the animator poses the bones.
                    if (Environment.GetCommandLineArgs().Contains("-spNoCloth")) cloth.enabled = false;
                    Debug.Log($"[SquashPlay] cloth {cloth.name}: radius {cloth.SerializeData.radius.value:0.###}, colliders " +
                              string.Join(", ", list.Select(c => $"{c.name} on {c.transform.parent?.name}")));
                }
#endif
            }
            // -spRootScale: the avatar resized, as a scaler or a height slider does. Set on a parent:
            // an avatar's own animator can write its root's scale back every frame. The look
            // cameras and the swing read are in reaches, so a reach that scales reads the same.
            if (float.TryParse(Arg("-spRootScale"), NumberStyles.Float, CultureInfo.InvariantCulture, out float rootScale))
                foreach (var r in scene.GetRootGameObjects())
                    if (r.GetComponentInChildren<CVRAvatar>(true) != null)
                    {
                        var parent = new GameObject("SquashPlay scale").transform;
                        parent.localScale = Vector3.one * rootScale;
                        r.transform.SetParent(parent, false);
                    }
            // -spPose: the named bones' local poses before Play, logged against Play at the drag's end.
            if (Arg("-spPose") is string poseNames && !poseNames.StartsWith("-"))
                SessionState.SetString(K + "pose", string.Join("|", poseNames.Split(',').Select(n =>
                {
                    var t = scene.GetRootGameObjects().Where(r => r.activeSelf).SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(x => x.name == n);
                    return t == null ? "" : string.Join(";", n, JsonUtility.ToJson(t.localPosition), JsonUtility.ToJson(t.localEulerAngles), JsonUtility.ToJson(t.localScale));
                }).Where(x => x.Length > 0)));
            // -spRebuild: the squash built again on the played copy, into a folder of its own, so its
            // real materials run the deployed code, which a patched shader carries inlined. Never
            // saved; the folder and the shaders the patch wrote are cleared from outside after the run.
            if (Environment.GetCommandLineArgs().Contains("-spRebuild"))
            {
                var played = scene.GetRootGameObjects().First(r => r.activeSelf && r.GetComponentInChildren<CVRAvatar>(true) != null);
                if (PrefabUtility.IsPartOfPrefabInstance(played))
                    PrefabUtility.UnpackPrefabInstance(played, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                Directory.CreateDirectory(Arg("-spOut"));
                string bakedFile = Path.Combine(Arg("-spOut"), "edit-soft.f32");
                YapsSquash.SoftBakedForTest = (name, at, shares, moves, frame) =>
                {
                    if (File.Exists(bakedFile)) return;
                    WriteFloats(bakedFile, at.Concat(moves.SelectMany(m => m)).Concat(frame).SelectMany(v => new[] { v.x, v.y, v.z }));
                    WriteFloats(Path.ChangeExtension(bakedFile, ".shares.f32"), shares.SelectMany(v => new[] { v.x, v.y }));
                    Debug.Log($"[SoftBody] the build baked {name}: centres {Floats(frame[0], frame[1])}, across {Floats(frame[2])}, out {Floats(frame[3])}, reach {frame[4].x:0.000}, unit {frame[4].y:0.000}");
                };
                string said = YapsSquash.Add(played.GetComponentInChildren<CVRAvatar>(true).transform, "Assets/__SquashPlayRebuild", out bool built);
                YapsSquash.SoftBakedForTest = null;
                YapsShaderPatcher.EndPass(null, null);
                Debug.Log($"[SquashPlay] rebuilt {built}: {said}");
                // The skin as the build baked it, posed as the scene stands, for the bake against
                // the pose the game plays.
                Directory.CreateDirectory(Arg("-spOut"));
                foreach (var smr in played.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var posed = new Mesh();
                    smr.BakeMesh(posed);
                    var t = smr.transform;
                    var drawn = posed.vertices.Select(v => t.position + t.rotation * v).ToArray();
                    WriteFloats(Path.Combine(Arg("-spOut"), $"edit-{smr.name}.f32"), drawn.SelectMany(v => new[] { v.x, v.y, v.z }));
                    UnityEngine.Object.DestroyImmediate(posed);
                    // Skinned by hand as the build's Skins does, from the mesh's own vertices: no blend shapes.
                    var mesh = smr.sharedMesh;
                    var bind = mesh.bindposes;
                    var skin = smr.bones.Select((b, i) => b != null && i < bind.Length ? b.localToWorldMatrix * bind[i] : Matrix4x4.zero).ToArray();
                    var bw = mesh.boneWeights;
                    var verts = mesh.vertices;
                    var hand = new Vector3[Math.Min(bw.Length, verts.Length)];
                    for (int v = 0; v < hand.Length; v++)
                    {
                        var w = bw[v];
                        hand[v] = skin[w.boneIndex0].MultiplyPoint3x4(verts[v]) * w.weight0 + skin[w.boneIndex1].MultiplyPoint3x4(verts[v]) * w.weight1 +
                                  skin[w.boneIndex2].MultiplyPoint3x4(verts[v]) * w.weight2 + skin[w.boneIndex3].MultiplyPoint3x4(verts[v]) * w.weight3;
                    }
                    WriteFloats(Path.Combine(Arg("-spOut"), $"edit-hand-{smr.name}.f32"), hand.SelectMany(v => new[] { v.x, v.y, v.z }));
                    if (hand.Length == drawn.Length && hand.Length > 0)
                    {
                        var shapes = Enumerable.Range(0, mesh.blendShapeCount).Where(i => smr.GetBlendShapeWeight(i) != 0f).Select(i => $"{mesh.GetBlendShapeName(i)} {smr.GetBlendShapeWeight(i):0.#}");
                        var off = hand.Select((h, i) => (h - drawn[i]).magnitude).ToArray();
                        Debug.Log($"[SquashPlay] {smr.name} skinned by hand against drawn: {100f * Math.Sqrt(off.Average(x => (double) x * x)):0.00} cm rms, {100f * off.Max():0.0} worst, " +
                                  $"{off.Count(x => x > 0.005f)} vertices past 5 mm; shapes on: {string.Join(", ", shapes)}");
                    }
                }
            }
            if (Environment.GetCommandLineArgs().Contains("-spSurface"))
                Surface(scene.GetRootGameObjects().First(r => r.activeSelf && r.GetComponentInChildren<CVRAvatar>(true) != null));
            // Nothing is on screen in batch, and the cloth culls with the Animator.
            foreach (var a in UnityEngine.Object.FindObjectsOfType<Animator>()) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        // Measured on a copy, since a build strips the squash it measures, in the same part order
        // the build writes. The materials are the project's own: put back on quitting, as batch Unity
        // saves what is left changed on them.
        static void Surface(GameObject avatar)
        {
            var copy = UnityEngine.Object.Instantiate(avatar);
            var surfaces = new List<float[]>();
            var said = new List<string>();
            try
            {
                YapsSquash.MeasuredForTest = (name, centre, toward, o, points, reaches, unit) =>
                {
                    surfaces.Add(YapsSquash.Surface(points, centre, toward, o, reaches, unit, out float mapOff, out float ellipsoidOff));
                    said.Add($"{name} {(surfaces[surfaces.Count - 1] != null ? "own" : "ellipsoid")} {mapOff * 100f:0.00}/{ellipsoidOff * 100f:0.00} cm");
                };
                YapsSquash.Add(copy.GetComponentInChildren<CVRAvatar>(true).transform, "Assets/__SquashPlaySurface", out _);
            }
            finally
            {
                YapsSquash.MeasuredForTest = null;
                UnityEngine.Object.DestroyImmediate(copy);
            }
            var tex = YapsSquash.SurfaceTexture(surfaces, null, null);
            tex.hideFlags = HideFlags.DontSave;
            var on = Vector4.zero;
            for (int m = 0; m < surfaces.Count && m < 4; m++) on[m] = surfaces[m] != null ? 1f : 0f;
            var mats = avatar.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Where(m => m != null && m.HasProperty("_YAPS_DentPower")).Distinct().ToList();
            // A build from before the surface never declared it: nothing to keep.
            var was = mats.ToDictionary(m => m, m => (
                m.HasProperty("_YAPS_SquashSurface") ? m.GetTexture("_YAPS_SquashSurface") : null,
                m.HasProperty("_YAPS_SquashSurfaceOn") ? m.GetVector("_YAPS_SquashSurfaceOn") : Vector4.zero));
            foreach (var m in mats)
            {
                m.SetTexture("_YAPS_SquashSurface", tex);
                m.SetVector("_YAPS_SquashSurfaceOn", on);
            }
            EditorApplication.quitting += () =>
            {
                foreach (var kv in was)
                {
                    kv.Key.SetTexture("_YAPS_SquashSurface", kv.Value.Item1);
                    kv.Key.SetVector("_YAPS_SquashSurfaceOn", kv.Value.Item2);
                }
            };
            Debug.Log($"[SquashPlay] surfaces on {mats.Count} materials: {string.Join(", ", said)}");
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || provider != null || SessionState.GetInt(K + "on", 0) != 1) return;
            outDir = SessionState.GetString(K + "out", "");
            Directory.CreateDirectory(outDir);
            var providers = UnityEngine.Object.FindObjectsOfType<CVRMaterialDataProvider>();
            provider = providers.First(p => p.dataMode == CVRMaterialDataProvider.DataMode.TransformLocalToWorld);
            // -spRaw: the squash off, to tell what skinning alone does from what the squash adds. On
            // copies: switched off on the assets, the batch editor saved them on exit and the squash
            // stayed off in the project.
            if (Environment.GetCommandLineArgs().Contains("-spRaw"))
            {
                var raw = new Dictionary<Material, Material>();
                foreach (var r in provider.GetComponentInParent<CVRAvatar>().GetComponentsInChildren<Renderer>(true))
                    r.sharedMaterials = r.sharedMaterials.Select(m =>
                    {
                        if (m == null || !m.HasProperty("_YAPS_DentPower")) return m;
                        if (!raw.TryGetValue(m, out var off))
                        {
                            raw[m] = off = new Material(m);
                            off.SetFloat("_YAPS_DentPower", 0f);
                        }
                        return off;
                    }).ToArray();
            }
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
                float reach = mats[0].GetVector("_YAPS_SquashReach")[left ? 0 : 1] * Unit(part);
                var chest = animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
                var outward = Vector3.ProjectOnPlane(part.position - chest.position, Vector3.up).normalized;
                target = part.position + root.forward * reach * 0.6f + outward * reach * 0.2f;
                SetUpCameras(part.position, reach);
                Shoot("rest_animated");
            }
            if (SessionState.GetInt(K + "cost", 0) == 1) { if (frame == Bend) Cost(slots, mats); return; }
            if (SessionState.GetInt(K + "jump", 0) == 1) { if (frame >= Bend) JumpPose(slots, mats); return; }
            if (PivotOn) { if (frame >= Bend) PivotShake(slots); return; }
            if (SeamFilm != null) { if (frame >= Bend) SeamPose(slots, mats); return; }
            if (SessionState.GetInt(K + "look", 0) == 1) { if (frame >= Bend) LookPose(slots, mats); return; }
            if (SessionState.GetInt(K + "video", 0) == 1) { if (frame >= Bend) Film(slots, mats); return; }
            if (SessionState.GetInt(K + "drag", 0) > 0) { if (frame >= Bend) Drag(slots, mats); return; }
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
                // The same hands read as they were before the soft body: unbaked, the shader
                // presses with its own hand reading. Taken in turns, so neither gets the warm GPU.
                var soft = mats.Select(m => m.GetVector("_YAPS_SoftDepth")).ToArray();
                for (int round = 0; round < 3; round++)
                {
                    foreach (var m in mats) m.SetVector("_YAPS_SoftDepth", Vector4.zero);
                    Time("64 players' hands on one part, soft body off");
                    for (int i = 0; i < mats.Length; i++) mats[i].SetVector("_YAPS_SoftDepth", soft[i]);
                    Time("64 players' hands on one part, soft body on");
                }
                Players(0, false);
                foreach (var m in mats) m.SetVector("_YAPS_SoftDepth", Vector4.zero);
                Time("nobody else, soft body off");
                for (int i = 0; i < mats.Length; i++) mats[i].SetVector("_YAPS_SoftDepth", soft[i]);
                Time("nobody else, soft body on");
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
        static Vector4[] shapeWas;
        static YapsSquashSettings knobs;
        static bool stretchWas;
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
                if (knobs != null) { knobs.stretchInMotion = stretchWas; YapsSquash.Apply(animator.transform); }
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
            if (knobs != null) { stretchWas = knobs.stretchInMotion; }
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
            float reach = mats[0].GetVector("_YAPS_SquashReach")[p] * Unit(slots[p]);
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
            // Five hops, landing hard, the stretch in motion on or off. Given a tag, once a hop the
            // same frame rendered with the stretch off as well.
            Action<float, float> Hops(bool stretch, string tag) => (s, u) =>
            {
                if (knobs != null && knobs.stretchInMotion != stretch)
                {
                    knobs.stretchInMotion = stretch;
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
                new Shot { name = "Hopping: the physics alone", frames = 240, at = mid + f * 0.05f + up * 0.06f, width = 1f, profile = true, pose = Hops(false, null) },
                new Shot { name = "Hopping: stretch in motion, real flesh", frames = 240, at = mid + f * 0.05f + up * 0.06f, width = 1f, profile = true, pose = Hops(true, "real") },
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
                // One side only: the part pushed leads, and the other gives a little where they meet.
                new Shot { name = "One hand pushing one into the other", frames = 360, at = mid + f * 0.05f, width = 1f, pose = (s, u) =>
                    Press((sideP + f * 0.1f + outP * (r + Mathf.Lerp(0.03f, -0.13f, Beats(u, 2))), -f, -1f)) },
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
        // The leader's give, where it is inside the other, and how the colour stage takes a vertex.
        const string Cut = "            v = lerp(v, min(v, inside * abs(dot(N, facing)) * rsqrt(max(dot(N, N), 1e-12))), -bow);\n";
        const string Pick = "        if (shares[c] * squashContact[c] > yapsLookHeld) { yapsLookHeld = shares[c] * squashContact[c]; yapsLookDebug = ";
        static readonly (string name, string[] swaps)[] JumpStages =
        {
            ("full", new string[0]),
            ("nodent", new[] { "float local = deepest > 0 ? deepest * (1 - exp(-in0 / deepest)) : 0;", "float local = 0;" }),
            ("nobend", new[] { "if (p.framed && _YAPS_SquashTune.z > 0)", "if (false)" }),
            ("noflat", new[] { "squashed += ax[c3] * (dot(d, ax[c3]) * along[c3] + shift[c3]);", "squashed += ax[c3] * dot(d, ax[c3]);" }),
            ("noplane", new[] { "if (squashContact[c] <= 0) continue;", "continue;" }),
            ("nocapsule", new[] { "if (dist >= r + soft || dist < 1e-6) continue;", "continue;" }),
            ("press", new[] { "mapped += (moved - world) * shares[m] + shunt * swings[m] * swingOut;", "mapped += (hv[0] + hv[1] + hv[2] + hv[3]) * shares[m];", Stop, StopTo }),
            ("bend", new[] { "mapped += (moved - world) * shares[m] + shunt * swings[m] * swingOut;", "mapped += bend * shares[m];", Stop, StopTo }),
            ("shares", new[] { "mapped += (moved - world) * shares[m] + shunt * swings[m] * swingOut;", "mapped += float3(m == 0, m == 1, m >= 2) * shares[m];", Stop, StopTo }),
            ("dentmove", new[] { "float3 moved = p.centre + squashed + bend * lean;", "float3 moved = world;", Stop, StopTo }),
            ("nodentmove", new[] { "            moved -= d * sink;\n", "" }),
            ("ax0", new[] { "mapped += (moved - world) * shares[m] + shunt * swings[m] * swingOut;", "mapped += (ax[0]) * shares[m];", Stop, StopTo }),
            ("ax1", new[] { "mapped += (moved - world) * shares[m] + shunt * swings[m] * swingOut;", "mapped += (ax[1]) * shares[m];", Stop, StopTo }),
            ("along", new[] { "mapped += (moved - world) * shares[m] + shunt * swings[m] * swingOut;", "mapped += (float3(along[0], along[1], along[2])) * shares[m];", Stop, StopTo }),
            ("shift", new[] { "mapped += (moved - world) * shares[m] + shunt * swings[m] * swingOut;", "mapped += (float3(shift[0], shift[1], shift[2])) * shares[m];", Stop, StopTo }),
            ("sum", new[] { "mapped += (moved - world) * shares[m] + shunt * swings[m] * swingOut;", "mapped += (S) * shares[m];", Stop, StopTo }),
            ("online", new[] { "mapped += (moved - world) * shares[m] + shunt * swings[m] * swingOut;", "mapped += (float3(dot(ax[0], mul(M, ax[0])), dot(ax[1], mul(M, ax[1])), dot(ax[2], mul(M, ax[2])))) * shares[m];", Stop, StopTo }),
            ("lean", new[] { "mapped += (moved - world) * shares[m] + shunt * swings[m] * swingOut;", "mapped += (float3(lean, bendBy, 0)) * shares[m];", Stop, StopTo }),
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
            // Written by the look's branch stage, which this compiles too.
            static float4 yapsLookDebug;
            static float yapsLookHeld, yapsLeaveFit;
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
                jumpReach = mats[0].GetVector("_YAPS_SquashReach")[p] * Unit(slots[p]);
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

        // Stages the drag's look is drawn again without, with -spLookStages: find-and-put pairs as
        // in the sweep's.
        // -spLookStages alone draws every stage; followed by names, a comma list, only those.
        static IEnumerable<(string name, string[] swaps)> ChosenStages()
        {
            string only = Arg("-spLookStages");
            if (only == null || only.StartsWith("-")) return LookStages;
            var names = only.Split(',');
            return LookStages.Concat(ProbeStages).Where(x => names.Contains(x.name));
        }

        static readonly (string name, string[] swaps)[] LookStages =
        {
            ("noplane", new[] { "if (squashContact[c] <= 0) continue;", "continue;" }),
            ("noshunt", new[] { "shunt = -axis * min(max(meet, 0) * max(lead, 0), reach);", "shunt = 0;" }),
            ("bulk0", new[] { "#define YAPS_SQUASH_BULK 0.6", "#define YAPS_SQUASH_BULK 0" }),
            ("bulk3", new[] { "#define YAPS_SQUASH_BULK 0.6", "#define YAPS_SQUASH_BULK 0.3" }),
            ("bulk9", new[] { "#define YAPS_SQUASH_BULK 0.6", "#define YAPS_SQUASH_BULK 0.9" }),
            ("fullheld", new[] { "float held = shares[c] * squashContact[c];", "float held = (shares[c] > 0) * squashContact[c];" }),
            ("nobow", new[] { "float bow = squashBow[c];", "float bow = 0;" }),
            ("noswell", new[] { "float3 away = side / max(length(side), ring);", "float3 away = 0;" }),
            ("leanall", new[] { "float lean = saturate((dot(squashed, p.o) + bendFrom) / bendSpan);", "float lean = 1;" }),
            ("nostretch", new[] { "stretch = clamp(1 + by, 1 / _YAPS_SquashMotion.z, _YAPS_SquashMotion.z);", "stretch = 1;" }),
            ("noflat", new[] { "squashed += ax[c3] * (dot(d, ax[c3]) * along[c3] + shift[c3]);", "squashed += ax[c3] * dot(d, ax[c3]);" }),
            ("nopart", new[] { "mapped += (moved - world) * shares[m] + shunt * swings[m] * swingOut;", "mapped += 0;" }),
            ("nocut", new[] { Cut, "" }),
        };
        // Per part out, in, front, across and back, with the squeeze after the first and the lower
        // depth after the second; then the skin's reach each of those ways, the upper depth, and
        // how many sources were read; then the slanted ways, out in front and in front from below,
        // per part. _SoftPick 10 gives the pair each vertex rides, plus one.
        static readonly string[] SoftAmountSwaps =
        {
            "void YapsSoftMove(float3 row", "float _SoftPick;\nvoid YapsSoftMove(float3 row",
            "    float most = 1.6 * d1;\n",
            "    float picked[30] = { amount[0], amount[1], amount[2], amount[3], amount[4], squeeze, amount[5], amount[6], amount[7], amount[8], amount[9], d0,\n" +
            "        ext[0], ext[1], ext[2], ext[3], ext[4], ext[5], ext[6], ext[7], ext[8], ext[9], d1, (float) (arms + hands + plugs),\n" +
            "        amount[10], amount[11], amount[12], amount[13], 0, 0 };\n" +
            "    int pk = 3 * (int) round(_SoftPick);\n    if (pk >= 30) { world += float3(k + 1, 0, 0); return; }\n    world += (k == 0) * float3(picked[pk], picked[pk + 1], picked[pk + 2]);\n    return;\n" +
            "    float most = 1.6 * d1;\n",
            "    YapsSoftMove(softRow, world, n, hasNormal);\n", "    YapsSoftMove(softRow, world, n, hasNormal);\n    position = mul(unity_WorldToObject, float4(world, 1)).xyz;\n    return;\n",
        };

        // Read out in place of the move, for the edges the tear finds: each vertex's shares (the
        // first part, the second, the rest), and the second part's lean, bend and how far out of
        // its shape, in its reaches.
        static readonly (string name, string[] swaps)[] ProbeStages =
        {
            ("share", new[] { "    yapsSkin = YapsSkinState();\n", "    yapsSkin = YapsSkinState();\n    position = mul(unity_WorldToObject, float4(mul(unity_ObjectToWorld, float4(position, 1)).xyz + float3(shares[0], shares[1], 0), 1)).xyz;\n    return;\n" }),
            ("lean", new[] { "mapped += (moved - world) * shares[m] + shunt * swings[m] * swingOut;", "mapped += float3(onSeam, squashContact[m], length(world - p.centre) / YapsExtent(p, normalize(world - p.centre))) * ((m & 1) == 1);", Stop, StopTo }),
            // The baked soft body's move alone, before the parts meet each other.
            ("softonly", new[] { "    YapsSoftMove(softRow, world, n, hasNormal);\n", "    YapsSoftMove(softRow, world, n, hasNormal);\n    position = mul(unity_WorldToObject, float4(world, 1)).xyz;\n    return;\n" }),
            // What the soft body read of the first pair's hands, three numbers at a time by
            // _SoftPick (SoftAmounts), in place of its move; and again without the own arms.
            ("softamt", SoftAmountSwaps),
            ("softnoarm", SoftAmountSwaps.Concat(new[] { "    YapsSources(k, arms, hands, plugs);\n", "    YapsSources(k, arms, hands, plugs);\n    arms = 0;\n" }).ToArray()),
            // One baked answer as the shader applies it, way _SoftWay at the first depth or, with
            // _SoftDeep, the second, whatever the hands: the bake itself against the probe's.
            ("softway", new[] {
                "void YapsSoftMove(float3 row", "float _SoftWay, _SoftDeep;\nvoid YapsSoftMove(float3 row",
                "        float d = min((e == 10 ? squeeze : amount[min(e < 10 ? e : e - 1, 13)]) * _YAPS_SquashTune.y, most);\n",
                "        float d = e == (int) round(_SoftWay) ? (_SoftDeep > 0.5 ? d1 : d0) : 0;\n",
                "    world += (P0.a * move.x + P0.o * move.y + P0.u * move.z) * (unit0 * row.y);\n",
                "    world += (P0.a * move.x + P0.o * move.y + P0.u * move.z) * (unit0 * row.y * (k == 0));\n",
                "    YapsSoftMove(softRow, world, n, hasNormal);\n", "    YapsSoftMove(softRow, world, n, hasNormal);\n    position = mul(unity_WorldToObject, float4(world, 1)).xyz;\n    return;\n" }),
            ("slots", new[] { "    yapsSkin = YapsSkinState();\n", "    yapsSkin = YapsSkinState();\n    position = mul(unity_WorldToObject, float4(mul(unity_ObjectToWorld, float4(position, 1)).xyz + float3(yapsSkin, 100 * distance(YapsSlot(0), _YAPS_SquashFollow[0]._m03_m13_m23), 100 * distance(YapsSlot(0), YapsSlot(4))), 1)).xyz;\n    return;\n" }),
            ("swing", new[] { "mapped += (moved - world) * shares[m] + shunt * swings[m] * swingOut;", "mapped += float3(swings[m], shares[m], length(shunt)) * ((m & 1) == 1);", Stop, StopTo }),
            // How far the seam cuts: how far past it a vertex it holds lies, the depth the part pass
            // left it to press, which the swell sizes what it gives back by, and how far off the
            // line between the pair the vertex lies.
            ("cutv", new[] {
                "#define YAPS_DENT_INCLUDED\n", "#define YAPS_DENT_INCLUDED\nstatic float3 yapsCutRead;\n",
                "    yapsSkin = YapsSkinState();\n", "    yapsSkin = YapsSkinState();\n    yapsCutRead = 0;\n",
                "        world += away * swell - seamN * (dot(seamN, facing) * back);\n",
                "        if (held > 0.5 && v > yapsCutRead.x) yapsCutRead = float3(v, deep, length(side));\n        world += away * swell - seamN * (dot(seamN, facing) * back);\n",
                "    // The own arms and hands, a capsule per bone from one joint to the next.",
                "    position = mul(unity_WorldToObject, float4(start + yapsCutRead, 1)).xyz;\n    return;\n    // The own arms and hands, a capsule per bone from one joint to the next." }),
            // Coloured in place of the shading, by the contact holding the vertex most: on the
            // leader, red where its depth in the other won over the seam, green bands a quarter
            // width apart of where it is pressed to; grey on the part pressed; bluer on either
            // where the last surface solve eased back to the fitted shape.
            ("branch", new[] {
                "    return lerp(s, s1, saturate(2 - abs(g1) / (0.05 * p.reach)));",
                "    yapsLeaveFit = saturate(2 - abs(g1) / (0.05 * p.reach));\n    return lerp(s, s1, yapsLeaveFit);",
                "        if (bow < 0)\n        {\n            YapsPart P", "        float dbgV = v;\n        if (bow < 0)\n        {\n            YapsPart P",
                "        if (v <= -10 * width) continue;\n",
                Pick + "bow < 0 ? float4(v < dbgV - 1e-5, frac(v / width * 4), 0.3 + 0.7 * (1 - yapsLeaveFit), 1) : float4(0.4, 0.4, 0.4 + 0.6 * (1 - yapsLeaveFit), 1); }\n" +
                "        if (v <= -10 * width) continue;\n" }),
        };

        // Made in memory, so nothing is written into the project. A stage inlines its copy of the
        // deform in place of the include.
        static readonly Dictionary<string, Shader> lookShaders = new Dictionary<string, Shader>();
        static Shader Look(string stage = "") => Staged(lookShaders, LookShader, "SquashLook", stage);

        static Shader Staged(Dictionary<string, Shader> made, string source, string name, string stage)
        {
            if (made.TryGetValue(stage, out var shader) && shader != null) return shader;
            source = source.Replace(name + "\"", $"{name}_{stage}\"");
            var swaps = LookStages.Concat(ProbeStages).FirstOrDefault(s => s.name == stage).swaps;
            string dent = Arg("-spDent");
            if (swaps != null || dent != null)
            {
                string copy = File.ReadAllText(dent ?? LiveDent).Replace("\r\n", "\n");
                for (int i = 0; i < (swaps?.Length ?? 0); i += 2)
                {
                    if (!copy.Contains(swaps[i])) Debug.LogError($"[SquashPlay] look stage {stage}: line {i / 2} not found");
                    copy = copy.Replace(swaps[i], swaps[i + 1]);
                }
                source = source.Replace($"#include \"{LiveDent}\"", copy);
            }
            return made[stage] = ShaderUtil.CreateShaderAsset(source);
        }
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
            // Written by the branch stage in place of the shading, alpha 1 where set.
            static float4 yapsLookDebug;
            static float yapsLookHeld, yapsLeaveFit;
            #include ""Assets/AvatarBridge/Editor/Yaps/yaps_socket.cginc""
            #include ""Assets/AvatarBridge/Editor/Yaps/yaps_dent.cginc""
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _LookMode;
            float _LookRaw;
            float4 _LookTint;
            struct A { float4 vertex : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; float2 uv : TEXCOORD0; uint id : SV_VertexID; };
            struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 w : TEXCOORD1; float2 uv : TEXCOORD2; float4 share : TEXCOORD3; float4 debug : TEXCOORD4; };
            V vert(A v)
            {
                float3 p = v.vertex.xyz, n = v.normal, t = v.tangent.xyz;
                yapsLookDebug = 0;
                yapsLookHeld = 0;
                if (_LookRaw < 0.5) YapsDentDeform(p, n, t, v.id);
                V o;
                o.debug = yapsLookDebug;
                o.pos = UnityObjectToClipPos(float4(p, 1));
                o.w = mul(unity_ObjectToWorld, float4(p, 1)).xyz;
                o.n = UnityObjectToWorldNormal(n);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                uint width = (uint) round(abs(1.0 / _YAPS_DentOwn_TexelSize.x));
                o.share = _YAPS_DentOwn.Load(int3(v.id % width, v.id / width, 0));
                return o;
            }
            // Zebra stripes: a reflection of horizontal bands. A kink in a stripe is a crease, a
            // wobble a lump, where plain shading hides both.
            float Zebra(float3 n, float3 w)
            {
                float3 r = reflect(-normalize(_WorldSpaceCameraPos - w), n);
                return step(0.5, frac(r.y * 6));
            }
            float4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.n);
                if (i.debug.a > 0.5) return float4(i.debug.rgb, 1);
                // 1 the part shares, 2 stripes on the shader's normals, 3 stripes on the drawn
                // triangles themselves, so a normal that disagrees with the shape shows.
                if (_LookMode > 0.5 && _LookMode < 1.5)
                    return float4(i.share.r + i.share.b, i.share.g + i.share.a, 0.25 + 0.5 * (i.share.b + i.share.a), 1);
                if (_LookMode > 2.5) n = normalize(cross(ddy(i.w), ddx(i.w)));
                if (_LookMode > 1.5) { float z = Zebra(n, i.w); return float4(z, z, z * 0.9 + 0.1, 1); }
                float3 l = normalize(normalize(_WorldSpaceCameraPos - i.w) + float3(0, 0.6, 0));
                float k = 0.25 + 0.75 * saturate(dot(n, l));
                float3 base = _LookTint.a > 0 ? _LookTint.rgb : 0.55 + 0.45 * tex2D(_MainTex, i.uv).rgb;
                return float4(base * k, 1);
            }
            ENDCG
        }
    }
}
";
        // The squeeze from outside at each shot, and which part of its swing to shoot it at.
        static readonly (string name, float pulse, float phase)[] LookShots =
            { ("rest", 0f, 0f), ("squeeze_soft", 0.3f, 0.985f), ("squeeze_hard", 0.6f, 0.985f), ("squeeze_hard_half", 0.6f, 0.5f) };
        static readonly bool LookPreview = Environment.GetCommandLineArgs().Contains("-spPreview");
        // -spSwing: the parts' own bones turned toward each other instead, as a spin swings them,
        // across any gap and on into an overlap of so many reaches: by the first alone into a still
        // partner, then by both, half each.
        static readonly (string name, float overlap, float first)[] SwingShots =
            { ("rest", 0f, 1f), ("swing_one_soft", 0.3f, 1f), ("swing_one_hard", 0.6f, 1f), ("swing_both_hard", 0.6f, 0.5f), ("swing_both_deep", 1.2f, 0.5f) };
        static readonly bool LookSwing = Environment.GetCommandLineArgs().Contains("-spSwing");
        static readonly (string name, float depth)[] HandShots = { ("hand_rest", 0f), ("hand_soft", 0.4f), ("hand_in", 0.8f) };
        static readonly bool LookHand = Environment.GetCommandLineArgs().Contains("-spHand");
        static Quaternion[] swingFrom;
        static Transform[] swingHinge;
        static int swingWait;

        // -spPivot: the avatar shaken side to side, up and down, then back and forth, the cloth
        // running, and the point each part of the first pair turns about fitted from its follow's
        // pose against its rest every frame: whatever stays put while the follow swings. Logged in
        // the part's frame beside the hang the build wrote, with how far each bone between follow
        // and rest moved, so the hang can be checked against what the physics really do.
        static readonly bool PivotOn = Environment.GetCommandLineArgs().Contains("-spPivot");
        const int PivotFrames = 360;
        static Vector3 pivotStanding;
        static Vector3[] pivotC0;
        static Quaternion[] pivotQ0;
        static double[][] pivotM, pivotB;
        static Dictionary<Transform, (Vector3 at, float moved, float turned, Quaternion q)> pivotBones;

        static void PivotShake(Transform[] slots)
        {
            var root = animator.transform;
            int k = frame - Bend;
            if (k == 0) pivotStanding = root.position;
            var ways = new[] { root.right, Vector3.up, root.forward };
            float u = k / (float)(PivotFrames / 3);
            int w = Mathf.Min((int)u, 2);
            root.position = pivotStanding + ways[w] * (0.08f * Mathf.Sin(2f * Mathf.PI * 2f * (u - w)));
            if (k < PivotFrames) return;
            for (int p = 0; p < 2; p++)
            {
                var m = new Matrix4x4();
                for (int i = 0; i < 9; i++) m[i / 3, i % 3] = (float)pivotM[p][i];
                m[3, 3] = 1;
                var local = m.inverse.MultiplyVector(new Vector3((float)pivotB[p][0], (float)pivotB[p][1], (float)pivotB[p][2]));
                var rest = slots[4 + p];
                var at = rest.position + rest.rotation * local;
                var mat = provider.materials[0];
                float reach = mat.GetVector("_YAPS_SquashReach")[p] * Unit(slots[p]);
                Vector3 r0 = slots[4 + p].position, r1 = slots[4 + (p ^ 1)].position, ta = (r1 - r0).normalized;
                Vector3 to = Vector3.ProjectOnPlane(0.5f * (r0 + r1) - slots[16].position, ta).normalized, tu = Vector3.Cross(ta, to);
                Vector3 pv = at - r0;
                float R = reach / Unit(slots[4 + p]);
                Debug.Log($"[SquashPlay] pivot part {p}: fitted {Vector3.Dot(pv, ta) / reach:0.00}/{Vector3.Dot(pv, to) / reach:0.00}/{Vector3.Dot(pv, tu) / reach:0.00} reach, " +
                          $"hang written {mat.GetVector("_YAPS_SquashHangToward")[p] / R:0.00}/{mat.GetVector("_YAPS_SquashHangOut")[p] / R:0.00}/{mat.GetVector("_YAPS_SquashHangAcross")[p] / R:0.00}; " +
                          string.Join(", ", pivotBones.Where(b => b.Key.IsChildOf(slots[4 + p].parent) && slots[p].IsChildOf(b.Key))
                              .Select(b => (b, at: rest.rotation * b.Value.at))
                              .Select(x => $"{x.b.Key.name} at {Vector3.Dot(x.at, ta) / reach:0.00}/{Vector3.Dot(x.at, to) / reach:0.00}/{Vector3.Dot(x.at, tu) / reach:0.00} moved {x.b.Value.moved / reach:0.00} reach, turned {x.b.Value.turned:0} deg")));
            }
            EditorApplication.Exit(0);
        }

        // After the cloth: the follow's pose in its rest's frame, against the first frame's. A
        // turn R about a point P takes c0 to P + R (c0 - P), so (I - R) P = c - R c0, summed
        // over the frames in the least squares.
        static void PivotRead(Transform[] slots)
        {
            int k = frame - Bend;
            if (k < 0 || k >= PivotFrames) return;
            if (k == 0)
            {
                pivotC0 = new Vector3[2];
                pivotQ0 = new Quaternion[2];
                pivotM = new[] { new double[9], new double[9] };
                pivotB = new[] { new double[3], new double[3] };
                pivotBones = new Dictionary<Transform, (Vector3, float, float, Quaternion)>();
            }
            for (int p = 0; p < 2; p++)
            {
                var rest = slots[4 + p];
                var inv = Quaternion.Inverse(rest.rotation);
                var c = inv * (slots[p].position - rest.position);
                var q = inv * slots[p].rotation;
                for (var b = slots[p].parent; b != null && b != rest.parent; b = b.parent)
                {
                    var bAt = inv * (b.position - rest.position);
                    var bq = inv * b.rotation;
                    if (k == 0) pivotBones[b] = (bAt, 0f, 0f, bq);
                    else
                    {
                        var was = pivotBones[b];
                        pivotBones[b] = (was.at, Mathf.Max(was.moved, (bAt - was.at).magnitude), Mathf.Max(was.turned, Quaternion.Angle(bq, was.q)), was.q);
                    }
                }
                if (k == 0) { pivotC0[p] = c; pivotQ0[p] = q; continue; }
                var R = Matrix4x4.Rotate(q * Quaternion.Inverse(pivotQ0[p]));
                var rhs = c - R.MultiplyVector(pivotC0[p]);
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        // (I - R)^T (I - R), and (I - R)^T rhs
                        double s = 0;
                        for (int r = 0; r < 3; r++) s += ((r == i ? 1 : 0) - R[r, i]) * ((r == j ? 1 : 0) - R[r, j]);
                        pivotM[p][i * 3 + j] += s;
                    }
                    for (int r = 0; r < 3; r++) pivotB[p][i] += ((r == i ? 1 : 0) - R[r, i]) * rhs[r];
                }
            }
        }
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
                float reach = mats[0].GetVector("_YAPS_SquashReach")[0] * Unit(slots[0]);
                Debug.Log($"[SquashPlay] look reach {reach * 100f:0.000} cm in the world, avatar scale in the world {root.lossyScale.x:0.###}");
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
                var shader = Look();
                if (shader == null || ShaderUtil.ShaderHasError(shader))
                    Debug.LogError("[SquashPlay] look shader: " + (shader == null ? "missing" : string.Join(" | ", ShaderUtil.GetShaderMessages(shader).Select(m => m.message))));
                // The shots below write to the material assets, which outlive Play mode and are saved
                // on exit: a look run left a built avatar pulsing.
                pulseWas = mats.Length > 0 ? mats[0].GetFloat("_YAPS_SquashPulse") : 0f;
                shapeWas = mats.Select(m => m.GetVector("_YAPS_SquashShape")).ToArray();
                // -spMeet: the pair's contact on, which a fresh build leaves off.
                if (Environment.GetCommandLineArgs().Contains("-spMeet"))
                    foreach (var m in mats) { var s = m.GetVector("_YAPS_SquashShape"); m.SetVector("_YAPS_SquashShape", new Vector4(s.x, s.y, s.z, 1f)); }
                // -spNoMeet: off, for a build saved with it on.
                if (Environment.GetCommandLineArgs().Contains("-spNoMeet"))
                    foreach (var m in mats) { var s = m.GetVector("_YAPS_SquashShape"); m.SetVector("_YAPS_SquashShape", new Vector4(s.x, s.y, s.z, 0f)); }
                lookAt = 0;
                lookWas = -1f;
                foreach (var m in mats) m.SetFloat("_YAPS_SquashPulse", LookPreview ? 0f : LookShots[0].pulse);
                if (LookPreview) YapsSquashPreview.Start(YapsSquash.SettingsOf(animator.transform));
            }
            // -spPreview: the inspector's Squeeze, its hands, in place of the test pulse.
            if (LookPreview && lookAt < LookShots.Length)
            {
                YapsSquashPreview.Squeeze = LookShots[lookAt].pulse / 0.6f;
                typeof(YapsSquashPreview).GetMethod("Tick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);
            }
            if (LookSwing && lookAt < SwingShots.Length)
            {
                var (swingName, overlap, first) = SwingShots[lookAt];
                Swing(slots, mats, overlap, first);
                // The provider and the skinning catch up over a few frames.
                if (++swingWait < 8) return;
                swingWait = 0;
                lookShot = swingName;
                lookAt++;
                return;
            }
            if (LookHand && lookAt < HandShots.Length)
            {
                Swing(slots, mats, 0.001f, 0.5f);
                HandOn(slots, mats, HandShots[lookAt].depth);
                if (++swingWait < 8) return;
                swingWait = 0;
                lookShot = HandShots[lookAt].name;
                lookAt++;
                return;
            }
            if (lookAt >= (LookSwing ? SwingShots.Length : LookHand ? HandShots.Length : LookShots.Length))
            {
                for (int i = 0; i < mats.Length; i++) { mats[i].SetFloat("_YAPS_SquashPulse", pulseWas); mats[i].SetVector("_YAPS_SquashShape", shapeWas[i]); }
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
            var shader = Look();
            var sheet = new Texture2D(700 * lookCams.Length, 700, TextureFormat.RGB24, false);
            var one = new Texture2D(700, 700, TextureFormat.RGB24, false);
            string[] modes = { "", "_shares", "_zebra", "_zebra_mesh" };
            for (int mode = 0; mode < modes.Length; mode++)
            {
                Shader.SetGlobalFloat("_LookMode", mode);
                for (int c = 0; c < lookCams.Length; c++)
                {
                    lookCams[c].RenderWithShader(shader, "");
                    RenderTexture.active = lookCams[c].targetTexture;
                    one.ReadPixels(new Rect(0, 0, 700, 700), 0, 0);
                    sheet.SetPixels(700 * c, 0, 700, 700, one.GetPixels());
                }
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, $"look_{lookShot}{modes[mode]}.png"), sheet.EncodeToPNG());
            }
            // The skinning alone, the squash left out, to tell what the shader adds.
            Shader.SetGlobalFloat("_LookMode", 0f);
            Shader.SetGlobalFloat("_LookRaw", 1f);
            for (int c = 0; c < lookCams.Length; c++)
            {
                lookCams[c].RenderWithShader(shader, "");
                RenderTexture.active = lookCams[c].targetTexture;
                one.ReadPixels(new Rect(0, 0, 700, 700), 0, 0);
                sheet.SetPixels(700 * c, 0, 700, 700, one.GetPixels());
            }
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(outDir, $"look_{lookShot}_raw.png"), sheet.EncodeToPNG());
            Shader.SetGlobalFloat("_LookRaw", 0f);
            UnityEngine.Object.DestroyImmediate(sheet);
            UnityEngine.Object.DestroyImmediate(one);
            Debug.Log($"[SquashPlay] look shot {lookShot}");
            lookShot = null;
            if (lookAt < LookShots.Length && !LookPreview && !LookSwing && !LookHand)
                foreach (var m in provider.materials) m.SetFloat("_YAPS_SquashPulse", LookShots[lookAt].pulse);
        }

        // -spSeamFilm <file>[,<file>]: the pair pressed into each other six ways, each eased in, held
        // and let go, drawn from the front and from above in the look shader once with the live
        // include and once with each file, side by side from the same frame, into <dir>/frames
        // with segments.tsv. Comparing includes in separate runs would compare two cloth runs too.
        static readonly string SeamFilm = Arg("-spSeamFilm");
        static readonly (string name, int kind, float amount)[] SeamShots =
        {
            ("One dragged into the other, the cloth running", 0, 1f),
            ("Both dragged into each other, the cloth running", 1, 1f),
            ("One swung into a still partner", 2, 0.6f),
            ("Both swung together, deep", 3, 1.2f),
            // Past about half a reach a hand's line nears the part's centre, where its press lets go.
            ("A hand pressing one from outside", 4, 0.45f),
            ("Two hands squeezing them together", 5, 0.45f),
            // Held short of the centres, where a press still says which way it came in.
            ("Two hands squeezing them halfway", 5, 0.25f),
        };
        // The reference has no bones to swing, so it films the hands alone, each eased in from rest.
        static readonly bool Reference = Environment.GetCommandLineArgs().Contains("-spReference");
        // And more ways to press, so the summing is tuned on more than one squeeze: each hand a
        // part, the way it comes from (out of the pair, up, forward), and how deep in reaches.
        static readonly (string name, (int part, Vector3 way, float depth)[] hands)[] Varied =
        {
            ("A hand from the front", new[] { (0, new Vector3(0f, 0f, 1f), 0.3f) }),
            ("A hand lifting from below", new[] { (0, new Vector3(0f, -1f, 0f), 0.3f) }),
            ("A hand pressing down from above", new[] { (0, new Vector3(0f, 1f, 0f), 0.3f) }),
            ("A hand from outside and in front", new[] { (0, new Vector3(1f, 0f, 1f), 0.35f) }),
            ("A hand from outside, held high", new[] { (0, new Vector3(1f, 0.6f, 0.3f), 0.35f) }),
            ("One pressed deep, the other light", new[] { (0, new Vector3(1f, 0f, 0f), 0.4f), (1, new Vector3(1f, 0f, 0f), 0.15f) }),
            ("A light squeeze", new[] { (0, new Vector3(1f, 0f, 0f), 0.15f), (1, new Vector3(1f, 0f, 0f), 0.15f) }),
            ("Both lifted from below in front", new[] { (0, new Vector3(0f, -1f, 1f), 0.3f), (1, new Vector3(0f, -1f, 1f), 0.3f) }),
            // Harder, past the depths baked: how far the answers carry on, and where they let go.
            ("Squeezed hard together", new[] { (0, new Vector3(1f, 0f, 0f), 0.55f), (1, new Vector3(1f, 0f, 0f), 0.55f) }),
            ("One shoved hard into the other", new[] { (0, new Vector3(1f, 0f, 0f), 0.6f) }),
            ("A hand deep from the front", new[] { (0, new Vector3(0f, 0f, 1f), 0.5f) }),
            ("Both pushed up hard from below", new[] { (0, new Vector3(0f, -1f, 0.3f), 0.45f), (1, new Vector3(0f, -1f, 0.3f), 0.45f) }),
            ("Both pressed hard from the front", new[] { (0, new Vector3(0f, 0f, 1f), 0.45f), (1, new Vector3(0f, 0f, 1f), 0.45f) }),
            ("One grabbed from above and below", new[] { (0, new Vector3(0f, 1f, 0.2f), 0.3f), (0, new Vector3(0f, -1f, 0.2f), 0.3f) }),
        };
        static readonly (string name, int kind, float amount)[] SeamOn = Reference
            ? SeamShots.Where(x => x.kind >= 4).Concat(Varied.Select((v, i) => (v.name, 6, (float) i))).ToArray()
            : SeamShots;
        static SoftBodyReference[] refs;
        static List<Renderer>[] refShown;
        static SkinnedMeshRenderer refBody;
        static Vector3[] refRest;
        const int RefLayer = 30;
        const int SeamFrames = 360, SeamSize = 900;
        static Camera[] seamCams;
        static Shader[] seamLooks;
        static Texture2D seamSheet;
        static List<string> seamSegments;
        static int seamFilmed;
        static bool seamShoot;
        static string seamRead;

        static void SeamPose(Transform[] slots, Material[] mats)
        {
            int t = frame - Bend - (Reference ? 30 : 0), at = t / SeamFrames;
            // Turned off, the cloth lets its bones back to their own pose: the reference is built
            // on that, so the shader starts from the pose the tissue does.
            if (t < 0) { Swing(slots, mats, 0f, 0.5f); HandOn(slots, mats, 0f); return; }
            if (seamCams == null)
            {
                var root = animator.transform;
                Vector3 mid = 0.5f * (slots[0].position + slots[1].position);
                float reach = mats[0].GetVector("_YAPS_SquashReach")[0] * Unit(slots[0]);
                Camera Make(Vector3 from, Vector3 up)
                {
                    var cam = new GameObject("SquashSeam camera").AddComponent<Camera>();
                    cam.enabled = false;
                    cam.nearClipPlane = 0.01f;
                    cam.fieldOfView = 30f;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
                    cam.cullingMask = ~(1 << RefLayer);
                    cam.transform.position = from;
                    cam.transform.LookAt(mid, up);
                    cam.targetTexture = new RenderTexture(SeamSize, SeamSize, 24) { antiAliasing = 8 };
                    return cam;
                }
                seamCams = new[] { Make(mid + root.forward * reach * 6f, root.up), Make(mid + root.up * reach * 5.5f, root.forward) };
                seamLooks = new[] { Look() }.Concat(SeamFilm.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select((f, i) => ShaderUtil.CreateShaderAsset(LookShader
                    .Replace("SquashLook\"", $"SquashLook_film{i}\"")
                    .Replace($"#include \"{LiveDent}\"", File.ReadAllText(f).Replace("\r\n", "\n"))))).ToArray();
                foreach (var shader in seamLooks.Where(x => x == null || ShaderUtil.ShaderHasError(x)))
                    Debug.LogError("[SquashPlay] seam film shader: " + (shader == null ? "missing" : string.Join(" | ", ShaderUtil.GetShaderMessages(shader).Select(m => m.message))));
                // Every layer its own colour, as the drag's looks, so one coming through another shows.
                var block = new MaterialPropertyBlock();
                var shown = animator.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToList();
                for (int i = 0; i < shown.Count; i++)
                {
                    shown[i].GetPropertyBlock(block);
                    block.SetColor("_LookTint", Tints[i % Tints.Length]);
                    shown[i].SetPropertyBlock(block);
                }
                if (Reference) StartReference(slots, reach);
                // -spReferenceSetup, or -spBakeTest: built, logged and left, to tune the build without a film.
                if (Environment.GetCommandLineArgs().Contains("-spReferenceSetup") || Arg("-spBakeTest") != null) { SessionState.SetInt(K + "on", 0); EditorApplication.Exit(0); return; }
                seamSheet = new Texture2D(SeamSize * (seamLooks.Length + (refs?.Length ?? 0)), 2 * SeamSize, TextureFormat.RGB24, false);
                seamSegments = new List<string>();
                Directory.CreateDirectory(Path.Combine(outDir, "frames"));
                // Measured once at rest: the second drag starts while the first's cloth still sways.
                dragBy = (slots[1].position - slots[0].position) * 0.5f;
            }
            if (at >= SeamOn.Length)
            {
                File.WriteAllLines(Path.Combine(outDir, "segments.tsv"), seamSegments);
                Debug.Log($"[SquashPlay] seam film {seamFilmed} frames into {outDir}");
                SessionState.SetInt(K + "on", 0);
                EditorApplication.Exit(0);
                return;
            }
            var (name, kind, amount) = SeamOn[at];
            float u = (t % SeamFrames) / (float)SeamFrames;
            float s = u < 0.35f ? Mathf.SmoothStep(0f, 1f, u / 0.35f) : u < 0.65f ? 1f : Mathf.SmoothStep(1f, 0f, (u - 0.65f) / 0.35f);
            if (t % SeamFrames == 0)
            {
                seamSegments.Add($"{seamFilmed}\t{name}");
                if (kind <= 1)
                {
                    dragged = ChainRoot(slots[0], slots[4]);
                    dragLocal = dragged.localPosition;
                    dragged2 = kind == 1 ? ChainRoot(slots[1], slots[5]) : null;
                    if (dragged2 != null) dragLocal2 = dragged2.localPosition;
                }
            }
            if (kind <= 1) { dragSet = s; Place(); }
            else if (kind <= 3) Swing(slots, mats, amount, kind == 2 ? 1f : 0.5f, s);
            else if (kind == 6)
            {
                Swing(slots, mats, 0.001f, 0.5f, s);
                HandsOn(slots, mats, Varied[(int) amount].hands, s);
            }
            else
            {
                // Two hands push the pair into each other as well as denting it; beside the tissue,
                // which has no bones to swing, the hands alone, or the shader is pressed twice.
                Swing(slots, mats, kind == 5 && !Reference ? 0.001f + 0.6f * s : 0.001f, 0.5f, Reference ? s : 1f);
                HandOn(slots, mats, amount * s, kind == 5);
            }
            if (refs != null)
            {
                if (t % SeamFrames == 0) foreach (var r in refs) r.Reset();
                var hands = Hands();
                foreach (var r in refs) if (r != summed) r.Step(hands);
                if (summed != null)
                {
                    summed.PushOut = hands;
                    summed.Show(Summed(hands));
                }
            }
            // Mid hold, the tear and the volume the squash adds, with the live include or -spDent's.
            if (t % SeamFrames == SeamFrames / 2) seamRead = name;
            // At rest too, so what the squash does to the pose as built is told from what pressing adds.
            if (t == 0) seamRead = "at rest";
            seamShoot = t % 2 == 0;
        }

        static void SeamShoot()
        {
            seamShoot = false;
            Shader.SetGlobalFloat("_LookMode", 0f);
            Shader.SetGlobalFloat("_LookRaw", 0f);
            for (int c = 0; c < 2; c++)
                for (int k = 0; k < seamLooks.Length; k++)
                {
                    seamCams[c].RenderWithShader(seamLooks[k], "");
                    RenderTexture.active = seamCams[c].targetTexture;
                    seamSheet.ReadPixels(new Rect(0, 0, SeamSize, SeamSize), SeamSize * k, c == 0 ? SeamSize : 0);
                }
            if (refs != null)
            {
                // The tissue drawn unsquashed, on its own layer with the hands moved onto it.
                Shader.SetGlobalFloat("_LookRaw", 1f);
                foreach (var g in ghost) g.gameObject.layer = RefLayer;
                for (int r = 0; r < refs.Length; r++)
                {
                    for (int q = 0; q < refs.Length; q++) foreach (var shown in refShown[q]) shown.enabled = q == r;
                    for (int c = 0; c < 2; c++)
                    {
                        int mask = seamCams[c].cullingMask;
                        seamCams[c].cullingMask = 1 << RefLayer;
                        seamCams[c].RenderWithShader(seamLooks[0], "");
                        seamCams[c].cullingMask = mask;
                        RenderTexture.active = seamCams[c].targetTexture;
                        seamSheet.ReadPixels(new Rect(0, 0, SeamSize, SeamSize), SeamSize * (seamLooks.Length + r), c == 0 ? SeamSize : 0);
                    }
                }
                foreach (var g in ghost) g.gameObject.layer = 0;
                Shader.SetGlobalFloat("_LookRaw", 0f);
            }
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(outDir, "frames", $"f_{seamFilmed++:00000}.jpg"), seamSheet.EncodeToJPG(92));
            if (seamRead == null) return;
            Debug.Log($"[SquashPlay] seam film hold: {seamRead}");
            if (refs != null)
                foreach (var r in refs)
                    Debug.Log($"[SoftBody] {r.label} {seamRead}: tissue volume {100f * r.VolumeChange:+0.00;-0.00}%, skin {r.Where(r.Rest(0), r.Now(0), refBody.sharedMesh.triangles)}");
            if (summed != null)
            {
                // How far the summed answers land from the tissue solving the same hands.
                Vector3[] solved = refs[0].Now(0), sum = summed.Now(0), rest = refs[0].Rest(0);
                double se = 0;
                int count = 0;
                float worst = 0f, most = 0f;
                for (int v = 0; v < rest.Length; v++)
                {
                    float moved = (solved[v] - rest[v]).magnitude;
                    if (moved < 1e-4f && (sum[v] - rest[v]).sqrMagnitude < 1e-8f) continue;
                    float e = (solved[v] - sum[v]).magnitude;
                    se += e * e;
                    count++;
                    worst = Mathf.Max(worst, e);
                    most = Mathf.Max(most, moved);
                }
                Debug.Log($"[SoftBody] summed against solved, {seamRead}: {count} vertices moved, {100f * most:0.0} cm at most; off by {100f * Math.Sqrt(se / Math.Max(count, 1)):0.00} cm rms, {100f * worst:0.0} cm at worst; {summedRead}; seam: {summed.SeamRead}");
                if (Dump != null)
                {
                    File.WriteAllLines(Path.Combine(Dump, $"shot{dumped}.txt"), new[] { seamRead }.Concat(Hands().Select(c => Floats(c.a, c.b) + " " + c.r.ToString("R", CultureInfo.InvariantCulture))));
                    WriteFloats(Path.Combine(Dump, $"shot{dumped}.f32"), solved.SelectMany(v => new[] { v.x, v.y, v.z }));
                    dumped++;
                }
            }
            DragTear();
            seamRead = null;
        }

        // The reference built on the pose the film starts from: once bare and once under whatever
        // clothing lies over the pair, each drawn by copies of the body and that clothing on their
        // own layer, tinted as the avatar's are.
        static void StartReference(Transform[] slots, float reach)
        {
            var root = animator.transform;
            var skins = animator.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.enabled).ToList();
            refBody = skins.Where(r => r.sharedMaterials.Any(m => m != null && m.HasProperty("_YAPS_DentOwn"))).OrderByDescending(r => r.sharedMesh.vertexCount).First();
            Vector3[] World(SkinnedMeshRenderer r)
            {
                var m = new Mesh();
                r.BakeMesh(m);
                var at = m.vertices.Select(v => r.transform.position + r.transform.rotation * v).ToArray();
                UnityEngine.Object.DestroyImmediate(m);
                return at;
            }
            refRest = World(refBody);
            var share = ReadStage(refBody, "share").Select(s => new Vector2(s.x, s.y)).ToArray();
            Vector3 c0 = slots[0].position, c1 = slots[1].position;
            var cloth = skins.Where(r => r != refBody).Select(r => (r, at: World(r)))
                .Where(c => c.at.Count(v => Mathf.Min((v - c0).magnitude, (v - c1).magnitude) < 1.2f * reach) >= 50).ToList();
            Debug.Log($"[SoftBody] clothing over the pair: {(cloth.Count > 0 ? string.Join(", ", cloth.Select(c => c.r.name)) : "none")}");
            // Every layer as one surface: the tissue goes out only where none lies over it.
            var clothAt = cloth.SelectMany(c => c.at).ToArray();
            var clothTris = new List<int>();
            int offset = 0;
            foreach (var c in cloth) { clothTris.AddRange(c.r.sharedMesh.triangles.Select(i => i + offset)); offset += c.at.Length; }
            var block = new MaterialPropertyBlock();
            Renderer Copy(SkinnedMeshRenderer from, Vector3[] at, out Mesh mesh)
            {
                var src = from.sharedMesh;
                mesh = new Mesh { name = from.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, vertices = at, uv = src.uv };
                mesh.subMeshCount = src.subMeshCount;
                for (int i = 0; i < src.subMeshCount; i++) mesh.SetTriangles(src.GetTriangles(i), i);
                mesh.RecalculateNormals();
                var go = new GameObject("SoftBody " + from.name) { layer = RefLayer };
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = from.sharedMaterials;
                from.GetPropertyBlock(block);
                r.SetPropertyBlock(block);
                return r;
            }
            // -spBare: the bare tissue alone, to check the basis sooner.
            refs = new SoftBodyReference[cloth.Count > 0 && !Environment.GetCommandLineArgs().Contains("-spBare") ? 2 : 1];
            refShown = new List<Renderer>[refs.Length];
            for (int k = 0; k < refs.Length; k++)
            {
                refs[k] = new SoftBodyReference(k == 0 ? "bare" : "clothed", refRest, refBody.sharedMesh.triangles, share, c0, c1, reach, root.up, root.forward,
                    k == 0 ? null : ((Vector3[], int[])?) (clothAt, clothTris.ToArray()), reach / 16f);
                refShown[k] = new List<Renderer> { Copy(refBody, refRest, out var bodyMesh) };
                refs[k].Embed(bodyMesh, refRest);
                if (k == 0) continue;
                foreach (var c in cloth)
                {
                    refShown[k].Add(Copy(c.r, c.at, out var mesh));
                    refs[k].Follow(mesh, c.at);
                }
            }
            // -spBasis: a column more, the bare tissue's answers to single presses summed for the
            // hands each frame, as a bake would store and the shader add, beside it solving them.
            // -spBasisBake "<cube divisor>,<substeps>,<frames>,<hold>[,<smoothing passes>[,<deeper depths>...]]": baked as a build would, on
            // its own coarser tissue, to a quarter and a half of the reach, the cache unused.
            var bake = Arg("-spBasisBake")?.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            if (Environment.GetCommandLineArgs().Contains("-spBasis"))
            {
                summed = new SoftBodyReference("summed", refRest, refBody.sharedMesh.triangles, share, c0, c1, reach, root.up, root.forward, null, reach / (bake?[0] ?? 16f))
                    { Substeps = bake != null ? (int) bake[1] : 12, Clamp = bake != null, Seam = true, Smooth = bake != null && bake.Length > 4 ? (int) bake[4] : 0 };
                refShown = refShown.Append(new List<Renderer> { Copy(refBody, refRest, out var sumMesh) }).ToArray();
                summed.Embed(sumMesh, refRest);
                refs = refs.Append(summed).ToArray();
                Basis(bake != null ? summed : refs[0], share, c0, c1, reach, bake);
                if (Arg("-spBakeTest") != null) BakeTest(share, c0, c1, reach);
            }
        }

        static SoftBodyReference summed;
        static string summedRead, summedHands;
        static bool waysCompared;
        // The build's baked answers as the shader applies them, per way and depth (way * 2 + depth),
        // and the probe's reading of the last shot's hands in the shader's order of ways.
        static Vector3[][] gameBasis, extentBasis, posedBasis, handBasis, buildBasis;
        static float[] summedAmounts;
        static Vector2[] basisShare;
        static float basisReach;
        // -spDump <dir>: the summing's inputs and the solved skin at each shot, to try other sums
        // offline: the answers as skin moves, the skin at rest, its faces and welds, which of it
        // rides the tissue, each part's share, and each shot's hands.
        static readonly string Dump = Arg("-spDump");
        static int dumped;

        static string Floats(params Vector3[] vs) => string.Join(" ", vs.SelectMany(v => new[] { v.x, v.y, v.z }).Select(f => f.ToString("R", CultureInfo.InvariantCulture)));

        static void WriteFloats(string path, IEnumerable<float> values)
        {
            using (var w = new BinaryWriter(File.Create(path)))
                foreach (var f in values) w.Write(f);
        }

        static void DumpBasis(Vector2[] share)
        {
            Directory.CreateDirectory(Dump);
            File.WriteAllLines(Path.Combine(Dump, "meta.txt"), new[]
            {
                $"vertices {refRest.Length}",
                "depths " + string.Join(" ", basisDepths.Select(d => d.ToString("R", CultureInfo.InvariantCulture))),
                "parts " + Floats(partAt[0], partAt[1]),
                "out " + Floats(partOut[0], partOut[1]),
            }.Concat(basisWays.Select(w => $"way {w.part} " + Floats(w.dir))));
            WriteFloats(Path.Combine(Dump, "rest.f32"), refRest.SelectMany(v => new[] { v.x, v.y, v.z }));
            WriteFloats(Path.Combine(Dump, "share.f32"), share.SelectMany(v => new[] { v.x, v.y }));
            File.WriteAllBytes(Path.Combine(Dump, "tris.i32"), refBody.sharedMesh.triangles.SelectMany(BitConverter.GetBytes).ToArray());
            File.WriteAllBytes(Path.Combine(Dump, "riding.u8"), summed.Riding.Select(b => (byte) (b ? 1 : 0)).ToArray());
            File.WriteAllLines(Path.Combine(Dump, "welds.txt"), summed.SkinWelds.Select(g => string.Join(" ", g)));
            WriteFloats(Path.Combine(Dump, "basis.f32"), basis.SelectMany(b => b.at).SelectMany(f => summed.SkinMoves(f)).SelectMany(v => float.IsNaN(v.x) ? new[] { 0f, 0f, 0f } : new[] { v.x, v.y, v.z }));
        }
        // The bare tissue's answers: which part a palm pressed (2 both, squeezed), from which way, and
        // its moves at each depth.
        static List<(int part, Vector3 dir, Vector3[][] at)> basis;
        static float[] basisDepths;
        static List<Vector3>[] partSkin;
        static Vector3[] partAt, partOut;

        // The bare tissue pressed one way at a time, as a bake would: a palm on each part from
        // outside, from its partner's side, the front, above and below, and the pair squeezed from
        // both outsides at once, each to a quarter, a half and four fifths of its reach. Summed from one side
        // each, a squeeze counted the partner shoved away twice, once from either side.
        static void Basis(SoftBodyReference tissue, Vector2[] share, Vector3 c0, Vector3 c1, float reach, float[] bake)
        {
            partAt = new[] { c0, c1 };
            basisShare = share;
            basisReach = reach;
            partSkin = new[] { 0, 1 }.Select(j => refRest.Where((v, i) => (j == 0 ? share[i].x : share[i].y) >= 0.5f).ToList()).ToArray();
            var root = animator.transform;
            Vector3 across = Vector3.ProjectOnPlane(c1 - c0, root.up).normalized, up = root.up;
            partOut = new[] { -across, across };
            basisDepths = bake != null ? new[] { 0.25f, 0.5f }.Concat(bake.Skip(5)).Select(d => d * reach).ToArray() : new[] { 0.25f * reach, 0.5f * reach, 0.8f * reach };
            basis = new List<(int, Vector3, Vector3[][])>();
            basisWays = new List<(int part, Vector3 dir)>();
            for (int j = 0; j < 2; j++)
                foreach (var dir in new[] { partOut[j], -partOut[j], root.forward, up, -up }) basisWays.Add((j, dir));
            basisWays.Add((2, Vector3.zero));
            // Slanted, as the build bakes them: outside in front, in front from below.
            for (int j = 0; j < 2; j++)
                foreach (var dir in new[] { (partOut[j] + root.forward).normalized, (root.forward - up).normalized }) basisWays.Add((j, dir));
            // -spBasisCache <file>: the answers kept between runs, the presses being slow; read back
            // only when the tissue and the depths match.
            string cache = bake != null ? null : Arg("-spBasisCache");
            int count = tissue.Particles.Length;
            if (cache != null && File.Exists(cache))
                using (var r = new BinaryReader(File.OpenRead(cache)))
                    if (r.ReadInt32() == count && r.ReadInt32() == basisDepths.Length && r.ReadInt32() == basisWays.Count && basisDepths.All(d => Mathf.Approximately(r.ReadSingle(), d)))
                    {
                        foreach (var (part, dir) in basisWays)
                            basis.Add((part, dir, basisDepths.Select(_ => Enumerable.Range(0, count).Select(i => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle())).ToArray()).ToArray()));
                        Debug.Log($"[SoftBody] the presses' answers read from {cache}");
                        return;
                    }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (var (part, dir) in basisWays)
                basis.Add((part, dir, tissue.Respond(Palms(part, dir), basisDepths, bake != null ? (int) bake[2] : 40, bake != null ? (int) bake[3] : 30)));
            Debug.Log($"[SoftBody] the basis baked in {clock.Elapsed.TotalSeconds:0.0} s, {basisWays.Count} ways to {basisDepths.Length} depths ({tissue.Timing})");
            if (Dump != null) DumpBasis(share);
            if (cache == null) return;
            using (var w = new BinaryWriter(File.Create(cache)))
            {
                w.Write(count);
                w.Write(basisDepths.Length);
                w.Write(basisWays.Count);
                foreach (var d in basisDepths) w.Write(d);
                foreach (var (_, _, at) in basis)
                    foreach (var field in at)
                        foreach (var v in field) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
            }
        }

        static List<(int part, Vector3 dir)> basisWays;
        // Whether the shader's across, toward cross out, points down, so it is the probe's below.
        static bool AcrossIsDown => Vector3.Dot(Vector3.Cross(partAt[1] - partAt[0], animator.transform.forward), animator.transform.up) < 0f;

        // The palms of one way: upright on the sides and front, flat under or over the part; both
        // outsides for the squeeze.
        static (Vector3, Vector3, float, Vector3)[] Palms(int part, Vector3 dir)
        {
            if (part == 2) return new[] { Palms(0, partOut[0])[0], Palms(1, partOut[1])[0] };
            var up = animator.transform.up;
            float rad = handRadius * Tall / 1.6f, len = 0.11f * Tall - rad;
            var axis = Mathf.Abs(Vector3.Dot(dir, up)) > 0.7f ? partOut[1] : up;
            var touch = partAt[part] + dir * (Extent(part, dir) + rad);
            return new[] { (touch - axis * (len / 2f), touch + axis * (len / 2f), rad, -dir) };
        }

        // -spBakeTest "<cube divisor>,<substeps>,<frames>,<hold>,<settle mm>[,<threads 0/1>[,<grip 0/1>]]_...":
        // the basis's presses again on a tissue so built, for each setting, timed, its skin's moves
        // set against the reference's answers; -spBakeWays "<i>,..." only those ways. -spBakeNudge
        // "<mm>" presses each way again with its palms that far either side across, and logs how far
        // the two answers part: a press whose answer turns on a millimetre bakes as chance.
        static void BakeTest(Vector2[] share, Vector3 c0, Vector3 c1, float reach)
        {
            var root = animator.transform;
            var ways = Arg("-spBakeWays")?.Split(',').Select(int.Parse).ToArray() ?? Enumerable.Range(0, basisWays.Count).ToArray();
            var slow = ways.Select(w => basis[w].at.Select(refs[0].SkinMoves).ToArray()).ToArray();
            foreach (var spec in Arg("-spBakeTest").Split('_'))
            {
                var f = spec.Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                var build = System.Diagnostics.Stopwatch.StartNew();
                var fast = new SoftBodyReference($"fast {spec}", refRest, refBody.sharedMesh.triangles, share, c0, c1, reach, root.up, root.forward, null, reach / f[0])
                    { Substeps = (int) f[1], Threads = f.Length < 6 || f[5] != 0f, Grip = f.Length < 7 || f[6] != 0f };
                fast.Embed(new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, vertices = refRest, triangles = refBody.sharedMesh.triangles }, refRest);
                build.Stop();
                var press = System.Diagnostics.Stopwatch.StartNew();
                var rows = new List<string>();
                double all = 0;
                int allCount = 0;
                for (int i = 0; i < ways.Length; i++)
                {
                    var fields = fast.Respond(Palms(basisWays[ways[i]].part, basisWays[ways[i]].dir), basisDepths, (int) f[2], (int) f[3], f[4] / 1000f);
                    var cells = new List<string>();
                    for (int k = 0; k < basisDepths.Length; k++)
                    {
                        var got = fast.SkinMoves(fields[k]);
                        double sum = 0, worst = 0, most = 0;
                        int count = 0, lost = 0;
                        for (int v = 0; v < got.Length; v++)
                        {
                            var want = slow[i][k][v];
                            if (float.IsNaN(want.x) || want.magnitude < 1e-3f) continue;
                            most = Math.Max(most, want.magnitude);
                            if (float.IsNaN(got[v].x)) { lost++; continue; }
                            double e = (got[v] - want).magnitude;
                            sum += e * e;
                            worst = Math.Max(worst, e);
                            count++;
                        }
                        all += sum;
                        allCount += count;
                        cells.Add($"{100 * Math.Sqrt(sum / Math.Max(count, 1)):0.00}/{100 * worst:0.0} of {100 * most:0.0}" + (lost > 0 ? $" ({lost} lost)" : ""));
                    }
                    if (Arg("-spBakeNudge") != null)
                    {
                        float mm = float.Parse(Arg("-spBakeNudge"), CultureInfo.InvariantCulture) / 1000f;
                        Vector3[][] Nudged(float by) => fast.Respond(Palms(basisWays[ways[i]].part, basisWays[ways[i]].dir)
                            .Select(c => (c.Item1 + partOut[1] * by, c.Item2 + partOut[1] * by, c.Item3, c.Item4)).ToArray(), basisDepths, (int) f[2], (int) f[3], f[4] / 1000f);
                        var (left, right) = (Nudged(-mm), Nudged(mm));
                        for (int k = 0; k < basisDepths.Length; k++)
                        {
                            Vector3[] l = fast.SkinMoves(left[k]), r = fast.SkinMoves(right[k]);
                            double sum = 0, worst = 0;
                            int count = 0;
                            for (int v = 0; v < l.Length; v++)
                            {
                                if (float.IsNaN(l[v].x) || float.IsNaN(r[v].x) || Mathf.Max(l[v].magnitude, r[v].magnitude) < 1e-3f) continue;
                                double e = (l[v] - r[v]).magnitude;
                                sum += e * e;
                                worst = Math.Max(worst, e);
                                count++;
                            }
                            cells[k] += $" [{1000f * mm:0.#} mm either side parts {100 * Math.Sqrt(sum / Math.Max(count, 1)):0.00}/{100 * worst:0.0}]";
                        }
                    }
                    rows.Add($"way {ways[i]} " + string.Join(", ", cells));
                }
                press.Stop();
                Debug.Log($"[SoftBody] bake test {spec}: built in {build.Elapsed.TotalSeconds:0.0} s, {ways.Length} ways pressed in {press.Elapsed.TotalSeconds:0.0} s ({fast.Timing}); " +
                          $"off by {100 * Math.Sqrt(all / Math.Max(allCount, 1)):0.00} cm rms over all, by way and depth rms/worst of the most, cm: {string.Join("; ", rows)}");
            }
        }

        // How far a part's skin reaches from its centre toward dir: its furthest within 20 degrees.
        static float Extent(int j, Vector3 dir)
        {
            float best = 0f;
            foreach (var v in partSkin[j])
            {
                var d = v - partAt[j];
                float along = Vector3.Dot(d, dir);
                if (along > 0.94f * d.magnitude) best = Mathf.Max(best, along);
            }
            return best;
        }

        // The basis summed for these hands. Each capsule presses each part toward it by how far it
        // is in, shared over the five ways by the square of how near it points to each, which sums
        // to one, the slanted ways taking their part of it; the deepest capsule each way counts, as a palm and its fingers press one dent. What both outsides are pressed alike is the squeeze;
        // the rest each part's own. Each answer read at its depth, straight between the depths
        // baked and on past the last.
        static Vector3[] Summed(IList<(Vector3 a, Vector3 b, float r)> hands)
        {
            var amount = new float[2, 5];
            var slanted = new float[2, 2];
            summedHands = "";
            foreach (var (a, b, r) in hands)
                for (int j = 0; j < 2; j++)
                {
                    // How far the capsule is in past the part's skin at rest, uncapped: capped, a
                    // hand pushing the part ahead of it read as a touch. Square to the capsule, as
                    // a palm presses along its normal: measured to its nearest end, a palm held
                    // above the centre read as pressing from above. Each way's depth is taken on
                    // that way's own line, as its answer was baked.
                    var ab = b - a;
                    var to = a + ab * Mathf.Clamp01(Vector3.Dot(partAt[j] - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-10f)) - partAt[j];
                    // Touching at all first, by the true distance: square to it, a palm lying under
                    // one part read as lifting its partner, its line running on under both.
                    if (to.magnitude < 1e-6f || Extent(j, to.normalized) + r - to.magnitude <= 0f) continue;
                    var along = ab.normalized;
                    to -= along * Vector3.Dot(to, along);
                    float dist = to.magnitude;
                    if (dist < 1e-6f) continue;
                    var w = to / dist;
                    if (Extent(j, w) + r - dist <= 0f) continue;
                    summedHands += $" [{j}: {100f * dist:0.0} from the centre, skin at {100f * Extent(j, w):0.0}, radius {100f * r:0.0}]";
                    var share = new float[5];
                    var press = new float[5];
                    for (int k = 0; k < 5; k++)
                    {
                        var dir = basis[5 * j + k].dir;
                        float c = Mathf.Max(Vector3.Dot(w, dir), 0f);
                        share[k] = c * c;
                        press[k] = Mathf.Min(Extent(j, dir) + r - Vector3.Dot(to, dir), (Extent(j, w) + r - dist) / Mathf.Max(c, 1e-3f));
                    }
                    // As the shader shares them: each slanted way takes what the lesser of its two
                    // gives, the front's split between the two by the others' shares.
                    float split = share[0] / Mathf.Max(share[0] + share[4], 1e-6f);
                    float toOut = Mathf.Min(share[2] * split, share[0]), toBelow = Mathf.Min(share[2] * (1f - split), share[4]);
                    share[0] -= toOut;
                    share[4] -= toBelow;
                    share[2] -= toOut + toBelow;
                    for (int k = 0; k < 5; k++)
                        if (press[k] > 0f) amount[j, k] = Mathf.Max(amount[j, k], share[k] * press[k]);
                    slanted[j, 0] = Mathf.Max(slanted[j, 0], 2f * toOut * (Extent(j, w) + r - dist));
                    slanted[j, 1] = Mathf.Max(slanted[j, 1], 2f * toBelow * (Extent(j, w) + r - dist));
                }
            float squeeze = Mathf.Min(amount[0, 0], amount[1, 0]);
            amount[0, 0] -= squeeze;
            amount[1, 0] -= squeeze;
            int across = AcrossIsDown ? 4 : 3;
            summedAmounts = new[] { amount[0, 0], amount[0, 1], amount[0, 2], amount[0, across], amount[0, 7 - across],
                amount[1, 0], amount[1, 1], amount[1, 2], amount[1, across], amount[1, 7 - across], squeeze,
                slanted[0, 0], slanted[0, 1], slanted[1, 0], slanted[1, 1] };
            summedRead = $"squeezed {100f * squeeze:0.0} cm, then out, in, front, up, down, out in front, in front from below: " +
                         string.Join(" | ", new[] { 0, 1 }.Select(j => string.Join(" ", Enumerable.Range(0, 5).Select(k => amount[j, k]).Concat(new[] { slanted[j, 0], slanted[j, 1] })
                             .Select(a => (100f * a).ToString("0.0"))))) + ";" + summedHands;
            var at = (Vector3[]) summed.Particles.Clone();
            void Add(Vector3[][] fields, float d)
            {
                if (d <= 0f) return;
                int k = 0;
                while (k < basisDepths.Length - 1 && d > basisDepths[k]) k++;
                float d0 = k == 0 ? 0f : basisDepths[k - 1], t = (d - d0) / (basisDepths[k] - d0);
                for (int p = 0; p < at.Length; p++)
                    at[p] += k == 0 ? fields[0][p] * t : fields[k - 1][p] + (fields[k][p] - fields[k - 1][p]) * t;
            }
            for (int j = 0; j < 2; j++)
                for (int k = 0; k < 5; k++) Add(basis[5 * j + k].at, amount[j, k]);
            Add(basis[10].at, squeeze);
            for (int j = 0; j < 2; j++)
                for (int k = 0; k < 2; k++) Add(basis[11 + 2 * j + k].at, slanted[j, k]);
            return at;
        }

        // One stage of the jump shader read back per vertex, as the drag's tear reads them.
        static Vector3[] ReadStage(SkinnedMeshRenderer smr, string stage)
        {
            var cam = new GameObject("SquashRead camera").AddComponent<Camera>();
            cam.enabled = false;
            cam.cullingMask = 1 << 31;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.transform.SetPositionAndRotation(front.transform.position, front.transform.rotation);
            int n = smr.sharedMesh.vertexCount, height = (n + JumpWidth - 1) / JumpWidth;
            Shader.SetGlobalFloat("_JumpWidth", JumpWidth);
            Shader.SetGlobalFloat("_JumpHeight", height);
            var rt = new RenderTexture(JumpWidth, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var tex = new Texture2D(JumpWidth, height, TextureFormat.RGBAFloat, false, true);
            cam.targetTexture = rt;
            int layer = smr.gameObject.layer;
            smr.gameObject.layer = 31;
            cam.RenderWithShader(Staged(tearShaders, JumpShader, "SquashJump", stage), "");
            smr.gameObject.layer = layer;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, JumpWidth, height), 0, 0);
            RenderTexture.active = null;
            var into = new Vector3[n];
            foreach (var c in tex.GetPixels())
            {
                int id = Mathf.RoundToInt(c.a) - 1;
                if (id >= 0 && id < n) into[id] = new Vector3(c.r, c.g, c.b);
            }
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
            return into;
        }

        // The ghost capsules as drawn: each a shape of a hand the shader presses with.
        static List<(Vector3 a, Vector3 b, float r)> Hands()
        {
            var list = new List<(Vector3 a, Vector3 b, float r)>();
            foreach (var g in ghost ?? new Transform[0])
            {
                if (!g.gameObject.activeSelf) continue;
                float r = g.localScale.x / 2f, half = Mathf.Max(g.localScale.y - r, 0f);
                list.Add((g.position - g.up * half, g.position + g.up * half, r));
            }
            return list;
        }

        // Each part's own bone turned so its follow moves toward where the other stood, together
        // closing the gap to where the pair touches and then the overlap, the first taking that
        // share. Both from their first pose. The cloth is stopped, or it turns them back. A scale
        // under 1 goes that share of the way, so a film moves in from rest.
        static void Swing(Transform[] slots, Material[] mats, float overlap, float first, float scale = 1f)
        {
            if (swingFrom == null)
            {
                foreach (var c in animator.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null && c.GetType().Name.Contains("MagicaCloth")))
                    c.enabled = false;
                swingHinge = new[] { Hinge(0), Hinge(1) };
                swingFrom = new[] { swingHinge[0].localRotation, swingHinge[1].localRotation };
            }
            swingHinge[0].localRotation = swingFrom[0];
            swingHinge[1].localRotation = swingFrom[1];
            float reach = mats[0].GetVector("_YAPS_SquashReach")[0] * Unit(slots[0]);
            Vector3 p0 = slots[0].position, p1 = slots[1].position;
            float touch = mats[0].GetVector("_YAPS_SquashPair").x * Unit(slots[0]);
            float total = (overlap > 0f ? (p1 - p0).magnitude - touch + overlap * reach : 0f) * scale;
            float a = total * first / reach, b = total * (1f - first) / reach;
            for (int i = 0; i < 2; i++)
            {
                float by = (i == 0 ? a : b) * reach;
                if (by <= 0f) continue;
                var mover = swingHinge[i];
                Vector3 from = i == 0 ? p0 : p1, to = (i == 0 ? p1 - p0 : p0 - p1).normalized;
                Vector3 r = from - mover.position;
                mover.rotation = Quaternion.FromToRotation(r, r + to * by) * mover.rotation;
            }
            if (swingWait == 0)
                Debug.Log($"[SquashPlay] swing {a:0.00}/{b:0.00} reach, touching at {touch / reach:0.00}: moved {(slots[0].position - p0).magnitude / reach:0.00}/{(slots[1].position - p1).magnitude / reach:0.00}, apart {(p1 - p0).magnitude / reach:0.00} -> {(slots[1].position - slots[0].position).magnitude / reach:0.00}, turned {Quaternion.Angle(swingFrom[0], swingHinge[0].localRotation):0}/{Quaternion.Angle(swingFrom[1], swingHinge[1].localRotation):0} deg about {swingHinge[0].name}, lever {(p0 - swingHinge[0].position).magnitude / reach:0.00} reach, pivot {Pivot()}");
            // The bone the physics turn it about: of those between the follow and its rest, the one
            // nearest the hang the build wrote. Measured with -spPivot, the cloth holds it in place
            // and turns it. With no hang written, the follow's own bone.
            Transform Hinge(int i)
            {
                Vector3 r0 = slots[4 + i].position, r1 = slots[4 + (i ^ 1)].position, ta = (r1 - r0).normalized;
                Vector3 to = Vector3.ProjectOnPlane(0.5f * (r0 + r1) - slots[16].position, ta).normalized, tu = Vector3.Cross(ta, to);
                var hang = new Vector3(mats[0].GetVector("_YAPS_SquashHangToward")[i], mats[0].GetVector("_YAPS_SquashHangOut")[i], mats[0].GetVector("_YAPS_SquashHangAcross")[i]);
                var at = r0 + (ta * hang.x + to * hang.y + tu * hang.z) * Unit(slots[4 + i]);
                var best = slots[i].parent;
                if (hang == Vector3.zero) return best;
                for (var b = slots[i].parent; b != null && b != slots[4 + i].parent; b = b.parent)
                    if ((b.position - at).sqrMagnitude < (best.position - at).sqrMagnitude) best = b;
                return best;
            }
            string Pivot()
            {
                // The bone it turns about, in the part's frame: toward the partner, out of the body, across.
                Vector3 r0 = slots[4].position, r1 = slots[5].position, ta = (r1 - r0).normalized;
                Vector3 to = Vector3.ProjectOnPlane(0.5f * (r0 + r1) - slots[16].position, ta).normalized, tu = Vector3.Cross(ta, to);
                Vector3 pv = swingHinge[0].position - r0;
                return $"{Vector3.Dot(pv, ta) / reach:0.00}/{Vector3.Dot(pv, to) / reach:0.00}/{Vector3.Dot(pv, tu) / reach:0.00}";
            }
            // What the shader reads: each follow off its rest, from the matrices the provider wrote.
            if (swingWait == 7)
            {
                var w = mats[0].GetMatrixArray("_YAPS_SquashFollow");
                Vector3 At(int k) => w[k].GetColumn(3);
                var axis = (At(1) - At(0)).normalized;
                Debug.Log($"[SquashPlay] swing read: follow off rest {(At(0) - At(4)).magnitude / reach:0.00}/{(At(1) - At(5)).magnitude / reach:0.00} " +
                          $"(toward the other {Vector3.Dot(At(0) - At(4), axis) / reach:0.00}/{Vector3.Dot(At(5) - At(1), axis) / reach:0.00}, down {-(At(0) - At(4)).y / reach:0.00}/{-(At(1) - At(5)).y / reach:0.00}), " +
                          $"overlap {(touch - (At(1) - At(0)).magnitude) / reach:0.00}, apart {(At(1) - At(0)).magnitude / reach:0.00}; " +
                          $"rest under {slots[4].parent.name}/{slots[5].parent.name}, movers {slots[0].parent.name}/{slots[1].parent.name}");
            }
        }

        // An open hand upright against the first part's outer side, so deep into it that the
        // shader's press is that many reaches: the capsule line sits the part's away reach plus the
        // hand's radius out, less the depth. Its ghost capsules stay off the look cameras' layer.
        // Both puts the second part's hand on its outer side too, squeezing the pair together.
        static void HandOn(Transform[] slots, Material[] mats, float depth, bool both = false)
        {
            if (ghost == null)
            {
                handRadius = mats[0].GetVector("_YAPS_SquashHands").y;
                ghost = Enumerable.Range(0, 12).Select(_ =>
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
                    go.SetActive(false);
                    return go.transform;
                }).ToArray();
            }
            if (depth <= 0f) { Press(); return; }
            float unit = Unit(slots[0]), reach = mats[0].GetVector("_YAPS_SquashReach")[0] * unit;
            float away = mats[0].GetVector("_YAPS_SquashAway")[0] * unit, rad = handRadius * Tall / 1.6f;
            Vector3 up = animator.transform.up;
            float len = 0.11f * Tall - rad;
            (Vector3, Vector3, float) On(int i)
            {
                Vector3 P = slots[i].position;
                var outP = Vector3.ProjectOnPlane(P - slots[i ^ 1].position, up).normalized;
                float r = mats[0].GetVector("_YAPS_SquashReach")[i] * unit, a = mats[0].GetVector("_YAPS_SquashAway")[i] * unit;
                return (P + outP * (a + rad - depth * r) + up * (len / 2f), -up, -1f);
            }
            if (both) Press(On(0), On(1));
            else Press(On(0));
            if (swingWait == 0) Debug.Log($"[SquashPlay] hand {depth:0.0} reach in, line {(away + rad - depth * reach) / reach:0.00} reach out, radius {rad / reach:0.00}");
        }

        // Open hands with the palm flat to its part from each way, in so deep that the palm's line
        // sits the skin's reach there plus the hand's radius out, less the depth, eased in by s.
        // Upright from the sides and front, lying across under or over the part.
        static void HandsOn(Transform[] slots, Material[] mats, (int part, Vector3 way, float depth)[] hands, float s)
        {
            if (ghost == null) HandOn(slots, mats, 0f);
            float unit = Unit(slots[0]), rad = handRadius * Tall / 1.6f, len = 0.11f * Tall - rad;
            Vector3 up = animator.transform.up, forward = animator.transform.forward;
            Press(hands.Select(h =>
            {
                Vector3 P = slots[h.part].position;
                var outP = Vector3.ProjectOnPlane(P - slots[h.part ^ 1].position, up).normalized;
                var w = (outP * h.way.x + up * h.way.y + forward * h.way.z).normalized;
                float reach = mats[0].GetVector("_YAPS_SquashReach")[h.part] * unit;
                float skin = partSkin != null ? Extent(h.part, w) : mats[0].GetVector("_YAPS_SquashAway")[h.part] * unit;
                var centre = P + w * (skin + rad - h.depth * s * reach);
                var axis = Mathf.Abs(Vector3.Dot(w, up)) > 0.7f ? outP : up;
                return (centre + axis * (len / 2f), -axis, -1f);
            }).ToArray());
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
            if (PivotOn) PivotRead(slots);
            if (jumpCam != null) JumpRead();
            if (lookShot != null) LookShoot();
            if (seamShoot) SeamShoot();
            if (dragRows != null) DragRead();
        }

        static Transform dragged, dragged2, grabBone;
        static Dictionary<SkinnedMeshRenderer, Vector3[]> startAt;
        static Vector3 grabFrom;
        static Vector3 dragLocal, dragLocal2, dragBy, dragSetAt;
        static float dragSet;
        static List<string> dragRows;

        // The dragged pair's slots where the first pair's sit, so the drag reads either alike.
        static Transform[] Paired(Transform[] s)
        {
            int dp = SessionState.GetInt(K + "dragPair", 0);
            if (dp == 0) return s;
            var r = (Transform[]) s.Clone();
            r[0] = s[dp]; r[1] = s[dp + 1]; r[4] = s[4 + dp]; r[5] = s[5 + dp];
            return r;
        }

        static Vector4 PairedReach(Vector4 reach)
        {
            int dp = SessionState.GetInt(K + "dragPair", 0);
            return dp == 0 ? reach : new Vector4(reach[dp], reach[dp + 1], reach[dp], reach[dp + 1]);
        }

        // The bone under the rest's on the way to the follow: the chain root. Follow and rest on
        // one bone leave the bone itself.
        static Transform ChainRoot(Transform follow, Transform rest)
        {
            var b = follow;
            while (b.parent != null && b.parent != rest.parent) b = b.parent;
            return b == follow || b.parent == null ? follow.parent : b;
        }

        static void Drag(Transform[] slots, Material[] mats)
        {
            slots = Paired(slots);
            int t = frame - Bend;
            if (t == 0)
            {
                dragged = ChainRoot(slots[0], slots[4]);
                string named = SessionState.GetString(K + "dragName", "");
                if (named.Length > 0)
                    dragged = provider.GetComponentInParent<CVRAvatar>().GetComponentsInChildren<Transform>(true).First(x => x.name == named);
                if (SessionState.GetInt(K + "moveTool", 0) == 1) EditorApplication.update += () => { if (EditorApplication.isPlaying) Place(); };
                if (SessionState.GetInt(K + "push", 0) == 1) YapsSquashPreview.Start(YapsSquash.SettingsOf(provider.GetComponentInParent<CVRAvatar>().transform));
                // The converter's soft body spheres switched off, to see what they do at rest.
                if (Environment.GetCommandLineArgs().Contains("-spNoContact"))
                    foreach (var sphere in provider.GetComponentInParent<CVRAvatar>().GetComponentsInChildren<Transform>(true).Where(x => x.name == "SoftBodyContact"))
                        sphere.gameObject.SetActive(false);
                grabBone = slots[0].parent;
                grabFrom = grabBone.position;
                dragLocal = dragged.localPosition;
                dragBy = (slots[1].position - slots[0].position) * 0.5f;
                if (float.TryParse(Arg("-spDragScale"), NumberStyles.Float, CultureInfo.InvariantCulture, out float dragScale)) dragBy *= dragScale;
                if (SessionState.GetInt(K + "drag", 0) == 2)
                {
                    dragged2 = ChainRoot(slots[1], slots[5]);
                    dragLocal2 = dragged2.localPosition;
                }
                var mid = 0.5f * (slots[0].position + slots[1].position);
                float away = PairedReach(mats[0].GetVector("_YAPS_SquashReach"))[0] * slots[0].lossyScale.x * 7f;
                front.transform.position = mid + animator.transform.forward * (SessionState.GetInt(K + "dragPair", 0) == 0 ? away : -away);
                front.transform.LookAt(mid);
                side.transform.position = mid + Vector3.up * away;
                side.transform.LookAt(mid, animator.transform.forward);
                // The skin before the drag, for the tear's count against where the skin started:
                // against the dragged pose alone, a squash undoing what the drag's skinning
                // pressed together read as tearing.
                startAt = new Dictionary<SkinnedMeshRenderer, Vector3[]>();
                foreach (var r in animator.GetComponentsInChildren<SkinnedMeshRenderer>()
                             .Where(r => r.enabled && r.sharedMaterials.Any(m => m != null && m.HasProperty("_YAPS_DentOwn"))))
                {
                    var bm = new Mesh();
                    r.BakeMesh(bm);
                    startAt[r] = bm.vertices.Select(v => r.transform.rotation * v).ToArray();
                    UnityEngine.Object.DestroyImmediate(bm);
                }
                dragRows = new List<string> { "frame	set_cm	dragged_off_set_cm	self_cm	other_cm	lead_dragged	gap_cm	over_cm	lead_over" };
                // Compiled before the drag moves anything, as each takes seconds.
                if (Environment.GetCommandLineArgs().Contains("-spLookStages"))
                    foreach (var s in ChosenStages()) Look(s.name);
                Debug.Log($"[SquashPlay] drag: {dragged.name} under {dragged.parent.name}; follow on {slots[0].parent.name}, rest on {slots[4].parent.name}; " +
                          $"partner follow on {slots[1].parent.name}, rest on {slots[5].parent.name}; by {dragBy.magnitude * 100f:0.0} cm");
            }
            dragSet = Mathf.SmoothStep(0f, 1f, t / 120f);
            if (SessionState.GetInt(K + "push", 0) == 1) YapsSquashPreview.Push = dragSet;
            else
#if AVATARBRIDGE_MAGICA
            if (SessionState.GetInt(K + "grab", 0) == 1)
            {
                var pull = grabFrom + dragBy * dragSet - grabBone.position;
                // The tester pushes on every mouse drag event, which can come several times a frame.
                int.TryParse(Arg("-spGrabTimes") ?? "1", out int times);
                for (int i = 0; i < Mathf.Max(times, 1); i++)
                    CckAnimatorTester.Push(dragged.GetComponent<MagicaCloth2.MagicaCloth>(), pull, Mathf.Min(pull.magnitude * 30f, 18f));
                dragSetAt = dragged.position;
            }
            else
#endif
            if (SessionState.GetInt(K + "moveTool", 0) == 0) Place();
            if (t == 240)
            {
                File.WriteAllLines(Path.Combine(outDir, "drag.tsv"), dragRows);
                Debug.Log("[SquashPlay] drag done");
                EditorApplication.Exit(0);
            }
        }

        static void Place()
        {
            float mine = dragged2 != null ? 0.5f : 1f;
            dragged.localPosition = dragLocal + dragged.parent.InverseTransformVector(dragBy * dragSet * mine);
            if (dragged2 != null) dragged2.localPosition = dragLocal2 - dragged2.parent.InverseTransformVector(dragBy * dragSet * mine);
            dragSetAt = dragged.position;
        }

        static void DragRead()
        {
            var slots = Paired(provider.transforms);
            var axis = (slots[1].position - slots[0].position).normalized;
            float self = Vector3.Dot(slots[0].position - slots[4].position, axis);
            float other = Vector3.Dot(slots[5].position - slots[1].position, axis);
            float lead = Mathf.Clamp((other - self) / Mathf.Max(Mathf.Abs(other) + Mathf.Abs(self), 1e-6f), -1f, 1f);
            // The overlap from the straight reaches, which the shader turns with the swing.
            var reach = PairedReach(provider.materials[0].GetVector("_YAPS_SquashReach"));
            float over = reach[0] * slots[0].lossyScale.x + reach[1] * slots[1].lossyScale.x - Vector3.Distance(slots[0].position, slots[1].position);
            float leadOver = Mathf.Clamp((other - self) / Mathf.Max(over, 0.1f * reach[0] * slots[0].lossyScale.x), -1f, 1f);
            int t = frame - Bend;
            string F(float x) => x.ToString("0.00", CultureInfo.InvariantCulture);
            dragRows.Add(string.Join("	", frame, F(dragSet * dragBy.magnitude * 100f), F((dragged.position - dragSetAt).magnitude * 100f),
                F(self * 100f), F(other * 100f), F(lead), F(Vector3.Distance(slots[0].position, slots[1].position) * 100f), F(over * 100f), F(leadOver)));
            if (t == 0 || t == 60 || t == 120 || t == 239)
            {
                Shoot($"drag_{t}");
                if (Environment.GetCommandLineArgs().Contains("-spLook")) DragLook($"drag_{t}");
            }
            if (t == 239)
            {
                foreach (var entry in SessionState.GetString(K + "pose", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var f = entry.Split(';');
                    var bone = animator.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == f[0]);
                    if (bone == null) continue;
                    var p0 = JsonUtility.FromJson<Vector3>(f[1]);
                    var r0 = Quaternion.Euler(JsonUtility.FromJson<Vector3>(f[2]));
                    var s0 = JsonUtility.FromJson<Vector3>(f[3]);
                    Debug.Log($"[SquashPlay] pose {f[0]}: moved {100f * bone.parent.TransformVector(bone.localPosition - p0).magnitude:0.00} cm, turned {Quaternion.Angle(r0, bone.localRotation):0.0} deg, scale {s0} -> {bone.localScale}");
                }
                DragTear();
            }
        }

        // At the drag's end, how far the squash stretches each squashed layer's edges and how many
        // of its triangles it turns over, into tear.tsv. A tear is skin either side of an edge
        // sent opposite ways, a fold skin turned back over itself; a picture shows either only as
        // a slit. Read with the sweep's shader, made in memory, and with -spLookStages without each
        // stage too.
        static readonly Dictionary<string, Shader> tearShaders = new Dictionary<string, Shader>();
        static List<string> tearEdges;
        static Vector3[] bodyAt;
        static readonly Dictionary<string, double> pairVolume = new Dictionary<string, double>();
        static void DragTear()
        {
            var rows = new List<string> { "stage\trenderer\tedges_moved\tover_2x\tover_3x\tworst_x\tturned_over\topened_1cm\twidest_cm\tskin_opened_from_start\topened_from_start\twidest_from_start_cm\tvolume_added_cm3" };
            tearEdges = new List<string> { "renderer\ta\tb\tstretch_x\toff_skin_cm\tx_cm\ty_cm\tz_cm\tmove_a_x\tmove_a_y\tmove_a_z\tmove_b_x\tmove_b_y\tmove_b_z\tfirst_a\tsecond_a\t_\tfirst_b\tsecond_b\t_\tlean_a\tbend_a_cm\tout_a\tlean_b\tbend_b_cm\tout_b\tskin_a_x\tskin_a_y\tskin_a_z\tskin_b_x\tskin_b_y\tskin_b_z\tswing_a\tswing_b\tshunt_cm" };
            DragTear("", rows);
            if (Environment.GetCommandLineArgs().Contains("-spLookStages"))
                foreach (var s in ChosenStages()) DragTear(s.name, rows);
            File.WriteAllLines(Path.Combine(outDir, "tear.tsv"), rows);
            File.WriteAllLines(Path.Combine(outDir, "tear_edges.tsv"), tearEdges);
        }

        // How much is inside the skin, through a square half wide each way round the pair's middle:
        // n by n columns from the front, the length along each where the skin wraps it at least
        // once. Unity winds front faces clockwise, so the outward normal is the cross negated.
        static double SeenVolume(Vector3[] at, int[] tris, Vector3 mid, Vector3 right, Vector3 up, Vector3 fwd, float half, int n)
        {
            float cell = 2f * half / n;
            var bins = new List<int>[n * n];
            for (int i = 0; i < tris.Length; i += 3)
            {
                float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                for (int k = 0; k < 3; k++)
                {
                    var p = at[tris[i + k]] - mid;
                    float x = Vector3.Dot(p, right), y = Vector3.Dot(p, up);
                    x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
                }
                int gx0 = Mathf.Max(0, Mathf.FloorToInt((x0 + half) / cell - 0.5f)), gx1 = Mathf.Min(n - 1, Mathf.CeilToInt((x1 + half) / cell - 0.5f));
                int gy0 = Mathf.Max(0, Mathf.FloorToInt((y0 + half) / cell - 0.5f)), gy1 = Mathf.Min(n - 1, Mathf.CeilToInt((y1 + half) / cell - 0.5f));
                for (int gx = gx0; gx <= gx1; gx++)
                    for (int gy = gy0; gy <= gy1; gy++)
                        (bins[gx * n + gy] ??= new List<int>()).Add(i);
            }
            double volume = 0;
            var hits = new List<(float d, int s)>();
            for (int g = 0; g < n * n; g++)
            {
                if (bins[g] == null) continue;
                float px = -half + (g / n + 0.5f) * cell, py = -half + (g % n + 0.5f) * cell;
                hits.Clear();
                foreach (int i in bins[g])
                {
                    Vector3 a = at[tris[i]] - mid, b = at[tris[i + 1]] - mid, c = at[tris[i + 2]] - mid;
                    float ax = Vector3.Dot(a, right) - px, ay = Vector3.Dot(a, up) - py;
                    float bx = Vector3.Dot(b, right) - px, by = Vector3.Dot(b, up) - py;
                    float cx = Vector3.Dot(c, right) - px, cy = Vector3.Dot(c, up) - py;
                    float wa = bx * cy - by * cx, wb = cx * ay - cy * ax, wc = ax * by - ay * bx;
                    if (!((wa >= 0 && wb >= 0 && wc >= 0) || (wa <= 0 && wb <= 0 && wc <= 0))) continue;
                    float sum = wa + wb + wc;
                    if (Mathf.Abs(sum) < 1e-12f) continue;
                    float d = (wa * Vector3.Dot(a, fwd) + wb * Vector3.Dot(b, fwd) + wc * Vector3.Dot(c, fwd)) / sum;
                    // Walked from the front backward: in where the skin faces forward.
                    hits.Add((d, Vector3.Dot(-Vector3.Cross(b - a, c - a), fwd) > 0 ? 1 : -1));
                }
                hits.Sort((p, q) => q.d.CompareTo(p.d));
                int wraps = 0;
                float last = 0f, inside = 0f;
                foreach (var h in hits)
                {
                    if (wraps >= 1) inside += last - h.d;
                    wraps += h.s;
                    last = h.d;
                }
                volume += inside * cell * cell;
            }
            return volume;
        }

        static void DragTear(string stage, List<string> rows)
        {
            var tearShader = Staged(tearShaders, JumpShader, "SquashJump", stage);
            var cam = new GameObject("SquashTear camera").AddComponent<Camera>();
            cam.enabled = false;
            cam.cullingMask = 1 << 31;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.transform.SetPositionAndRotation(front.transform.position, front.transform.rotation);
            foreach (var smr in animator.GetComponentsInChildren<SkinnedMeshRenderer>()
                         .Where(r => r.enabled && r.sharedMaterials.Any(m => m != null && m.HasProperty("_YAPS_DentOwn"))))
            {
                var mesh = smr.sharedMesh;
                int height = (mesh.vertexCount + JumpWidth - 1) / JumpWidth;
                Shader.SetGlobalFloat("_JumpWidth", JumpWidth);
                Shader.SetGlobalFloat("_JumpHeight", height);
                var rt = new RenderTexture(JumpWidth, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                var tex = new Texture2D(JumpWidth, height, TextureFormat.RGBAFloat, false, true);
                cam.targetTexture = rt;
                Vector3[] Read(Shader with, out bool[] got)
                {
                    int layer = smr.gameObject.layer;
                    smr.gameObject.layer = 31;
                    cam.RenderWithShader(with, "");
                    smr.gameObject.layer = layer;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, JumpWidth, height), 0, 0);
                    RenderTexture.active = null;
                    var into = new Vector3[mesh.vertexCount];
                    got = new bool[into.Length];
                    foreach (var c in tex.GetPixels())
                    {
                        int id = Mathf.RoundToInt(c.a) - 1;
                        if (id < 0 || id >= into.Length) continue;
                        into[id] = new Vector3(c.r, c.g, c.b);
                        got[id] = true;
                    }
                    return into;
                }
                var moved = Read(tearShader, out var seen);
                if (stage == "slots")
                {
                    var v = moved.Where((x, i) => seen[i] && x != Vector3.zero).DefaultIfEmpty(Vector3.zero).First();
                    var ends = provider.transforms;
                    var mats = smr.sharedMaterials.Where(m => m != null && m.HasProperty("_YAPS_DentOwn")).ToArray();
                    var held = mats.Length > 0 ? mats[0].GetMatrixArray("_YAPS_SquashFollow") : null;
                    string matrix = held != null && held.Length > 4 ? (100f * ((Vector3) held[0].GetColumn(3) - (Vector3) held[4].GetColumn(3)).magnitude).ToString("0.0", CultureInfo.InvariantCulture) : "none";
                    Debug.Log($"[SquashPlay] slots {smr.name}: source {v.x:0}, skinned slot off the matrix {v.y:0.0} cm, shader's follow off rest {v.z:0.0} cm, " +
                              $"transforms at frame end {100f * (ends[0].position - ends[4].position).magnitude:0.0} cm, material matrices {matrix} cm");
                }
                Vector3[] share = null, lean = null, swingW = null;
                if (stage == "")
                {
                    share = Read(Staged(tearShaders, JumpShader, "SquashJump", "share"), out _);
                    swingW = Read(Staged(tearShaders, JumpShader, "SquashJump", "swing"), out _);
                    lean = Read(Staged(tearShaders, JumpShader, "SquashJump", "lean"), out _);
                    // How far out of the second part's shape the vertices it moves lie.
                    float[] edges = { 1f, 1.25f, 1.5f, 2f, 3f, float.MaxValue };
                    var bins = new int[edges.Length];
                    for (int v = 0; v < share.Length; v++)
                        if (share[v].y >= 0.05f && lean[v].z > 0f) bins[Array.FindIndex(edges, e => lean[v].z < e)]++;
                    var cut = Read(Staged(tearShaders, JumpShader, "SquashJump", "cutv"), out _);
                    var past = Enumerable.Range(0, cut.Length).Where(x => cut[x].x > 0f).OrderByDescending(x => cut[x].x).ToList();
                    if (past.Count > 0)
                        Debug.Log($"[SquashPlay] cut {smr.name}: {past.Count} vertices past the seam, deepest {100f * cut[past[0]].x:0.00} cm, {100f * cut[past[0]].z:0.00} cm off the line, " +
                                  $"the part pass left {100f * cut.Max(x => x.y):0.00} cm to press");
                    Debug.Log($"[SquashPlay] out {smr.name}: under 1 {bins[0]}, 1-1.25 {bins[1]}, 1.25-1.5 {bins[2]}, 1.5-2 {bins[3]}, 2-3 {bins[4]}, over 3 {bins[5]}");
                }
                // Baked in world units in the renderer's axes (the flag to scale it left a 0.12 body
                // eight times too big); turned into the world the moves are in.
                var baked = new Mesh();
                smr.BakeMesh(baked);
                var rest = baked.vertices.Select(v => smr.transform.rotation * v).ToArray();
                // Edges under 3 mm left out of the ratios and the turned count: a millimetre edge
                // stretched twenty times by a centimetre of move is no gap anyone sees. How far an
                // edge opens is counted whatever its length.
                const float Least = 0.003f;
                int count = 0, over2 = 0, over3 = 0, turned = 0, opened = 0, skinOpened = 0, openedStart = 0;
                float worst = 1f, widest = 0f, widestStart = 0f;
                Vector3[] start = startAt != null && startAt.TryGetValue(smr, out var st) && st.Length == rest.Length ? st : null;
                // How far the first part's own skin moved, against how far its follow did.
                if (stage == "slots" && start != null)
                {
                    var sh = Read(Staged(tearShaders, JumpShader, "SquashJump", "share"), out _);
                    var at = provider.transforms[4].position - smr.transform.position;
                    var near = Enumerable.Range(0, rest.Length).Where(v => sh[v].x >= 0.9f && (start[v] - at).magnitude < 0.3f).ToList();
                    if (near.Count > 0)
                        Debug.Log($"[SquashPlay] slots {smr.name}: the first part's skin moved {100f * near.Average(v => (rest[v] - start[v]).magnitude):0.0} cm on average, " +
                                  $"{100f * near.Max(v => (rest[v] - start[v]).magnitude):0.0} cm at most, over {near.Count} vertices");
                }
                var tris = mesh.triangles;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int t0 = tris[i], t1 = tris[i + 1], t2 = tris[i + 2];
                    if (seen[t0] && seen[t1] && seen[t2] && moved[t0] + moved[t1] + moved[t2] != Vector3.zero
                        && Mathf.Max((rest[t1] - rest[t0]).sqrMagnitude, (rest[t2] - rest[t1]).sqrMagnitude, (rest[t0] - rest[t2]).sqrMagnitude) >= Least * Least)
                    {
                        var was = Vector3.Cross(rest[t1] - rest[t0], rest[t2] - rest[t0]);
                        var now = Vector3.Cross(rest[t1] + moved[t1] - rest[t0] - moved[t0], rest[t2] + moved[t2] - rest[t0] - moved[t0]);
                        if (Vector3.Dot(was, now) < 0f) turned++;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        int a = tris[i + k], b = tris[i + (k + 1) % 3];
                        if (!seen[a] || !seen[b] || (moved[a].sqrMagnitude < 1e-10f && moved[b].sqrMagnitude < 1e-10f)) continue;
                        float was = (rest[a] - rest[b]).magnitude;
                        if (was < 1e-6f) continue;
                        float now = (rest[a] + moved[a] - rest[b] - moved[b]).magnitude;
                        if (now - was > 0.01f) opened++;
                        widest = Mathf.Max(widest, now - was);
                        if (start != null)
                        {
                            float was0 = (start[a] - start[b]).magnitude;
                            if (was - was0 > 0.01f) skinOpened++;
                            if (now - was0 > 0.01f) openedStart++;
                            widestStart = Mathf.Max(widestStart, now - was0);
                        }
                        if (was < Least) continue;
                        float x = now / was;
                        count++;
                        if ((x > 3f || now - was > 0.01f) && stage == "")
                        {
                            // Where, off the partner's follow in the avatar's axes (x out to the
                            // partner's side, y up, z forward), and how each end moved, in cm.
                            var root = animator.transform;
                            var slots = Paired(provider.transforms);
                            string C(Vector3 v) => string.Join("\t", (100f * v).x.ToString("0.0", CultureInfo.InvariantCulture),
                                (100f * v).y.ToString("0.0", CultureInfo.InvariantCulture), (100f * v).z.ToString("0.0", CultureInfo.InvariantCulture));
                            Vector3 L(Vector3 v)
                            {
                                var l = root.InverseTransformVector(v);
                                l.x *= Mathf.Sign(Vector3.Dot(slots[1].position - slots[0].position, root.right));
                                return l;
                            }
                            string S(Vector3 v) => string.Join("\t", v.x.ToString("0.00", CultureInfo.InvariantCulture),
                                v.y.ToString("0.00", CultureInfo.InvariantCulture), v.z.ToString("0.00", CultureInfo.InvariantCulture));
                            // How far off the body skin, in cm: the build copies the skin's shares
                            // onto a layer within 4 cm of it.
                            if (bodyAt == null)
                            {
                                var body = animator.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.enabled && r.sharedMaterials.Any(m => m != null && m.HasProperty("_YAPS_DentOwn"))).OrderByDescending(r => r.sharedMesh.vertexCount).First();
                                var bm = new Mesh();
                                body.BakeMesh(bm);
                                bodyAt = bm.vertices.Select(v => body.transform.position + body.transform.rotation * v).ToArray();
                                UnityEngine.Object.DestroyImmediate(bm);
                            }
                            var wa = smr.transform.position + rest[a];
                            float off = smr.sharedMesh.vertexCount == bodyAt.Length ? 0f : 100f * Mathf.Sqrt(bodyAt.Min(v => (v - wa).sqrMagnitude));
                            tearEdges.Add(string.Join("\t", smr.name, a, b, x.ToString("0.00", CultureInfo.InvariantCulture), off.ToString("0.0", CultureInfo.InvariantCulture),
                                C(L(smr.transform.position + rest[a] - slots[1].position)), C(L(moved[a])), C(L(moved[b])),
                                S(share[a]), S(share[b]), S(new Vector3(lean[a].x, lean[a].y * 100f, lean[a].z)),
                                S(new Vector3(lean[b].x, lean[b].y * 100f, lean[b].z)),
                                start != null ? C(L(rest[a] - start[a])) : "", start != null ? C(L(rest[b] - start[b])) : "",
                                S(new Vector3(swingW[a].x, swingW[b].x, 100f * Mathf.Max(swingW[a].z, swingW[b].z)))));
                        }
                        if (x > 2f) over2++;
                        if (x > 3f) over3++;
                        worst = Mathf.Max(worst, x);
                    }
                }
                // The volume the squash adds to the skinned shape: flesh pressed together keeps its
                // mass, so a loss here is mass deleted where they meet. Summed from the pair's
                // middle, which cancels as long as the mesh's open edges stay put. The pair's own
                // volume, each part's skin coned to its follow, is read with the shares. Unity winds
                // front faces clockwise, so both sums come out negated.
                var mid = 0.5f * (provider.transforms[0].position + provider.transforms[1].position) - smr.transform.position;
                double gained = 0;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 Was(int k) => rest[tris[i + k]] - mid;
                    Vector3 Now(int k) => Was(k) + (seen[tris[i + k]] ? moved[tris[i + k]] : Vector3.zero);
                    gained -= (Vector3.Dot(Now(0), Vector3.Cross(Now(1), Now(2))) - Vector3.Dot(Was(0), Vector3.Cross(Was(1), Was(2)))) / 6.0;
                }
                if (share != null)
                {
                    double pair = 0;
                    for (int i = 0; i < tris.Length; i += 3)
                        for (int p = 0; p < 2; p++)
                        {
                            if (new[] { tris[i], tris[i + 1], tris[i + 2] }.Any(t => (p == 0 ? share[t].x : share[t].y) < 0.5f)) continue;
                            var c = provider.transforms[p].position - smr.transform.position;
                            pair -= Vector3.Dot(rest[tris[i]] - c, Vector3.Cross(rest[tris[i + 1]] - c, rest[tris[i + 2]] - c)) / 6.0;
                        }
                    pairVolume[smr.name] = pair;
                }
                pairVolume.TryGetValue(smr.name, out double pairV);
                Debug.Log($"[SquashPlay] volume {stage} {smr.name}: the squash adds {gained * 1e6:0} cm3, {100 * gained / Math.Max(Math.Abs(pairV), 1e-9):+0.0;-0.0}% of the pair's {pairV * 1e6:0} cm3");
                // Against the pose the reference started from, so the bones' swing counts as the
                // tissue's own move does.
                if (stage == "" && refs != null && smr == refBody)
                {
                    var drawn = rest.Select((x, i) => smr.transform.position + x + (seen[i] ? moved[i] : Vector3.zero)).ToArray();
                    Debug.Log($"[SquashPlay] where {smr.name}: skin {refs[0].Where(refRest, drawn, tris)}");
                    // How far the shader lands from the tissue solving the same hands, as the
                    // summed answers are measured; the baked soft body's move alone; and the
                    // skinning alone, which the cloth moves and the tissue never sees.
                    var solved = refs[0].Now(0);
                    // The second pair's skin, which the tissue solved here leaves out.
                    Shader.SetGlobalFloat("_SoftPick", 10);
                    var pairOf = Read(Staged(tearShaders, JumpShader, "SquashJump", "softamt"), out var pairSeen);
                    var otherPair = pairOf.Select((p, i) => pairSeen[i] && p.x > 1.5f).ToArray();
                    string Off(Vector3[] shown)
                    {
                        double se = 0;
                        int moving = 0;
                        float far = 0f;
                        for (int v = 0; v < solved.Length && v < shown.Length; v++)
                        {
                            if (otherPair[v] || (solved[v] - refRest[v]).sqrMagnitude < 1e-8f && (shown[v] - refRest[v]).sqrMagnitude < 1e-8f) continue;
                            float e = (solved[v] - shown[v]).magnitude;
                            se += e * e;
                            moving++;
                            far = Mathf.Max(far, e);
                        }
                        return $"{moving} vertices, {100f * Math.Sqrt(se / Math.Max(moving, 1)):0.00} cm rms, {100f * far:0.0} worst";
                    }
                    var soft = Read(Staged(tearShaders, JumpShader, "SquashJump", "softonly"), out var softSeen);
                    var skinned = rest.Select(x => smr.transform.position + x).ToArray();
                    Debug.Log($"[SoftBody] shader against solved, {seamRead}: {Off(drawn)}; soft body alone {Off(rest.Select((x, i) => smr.transform.position + x + (softSeen[i] ? soft[i] : Vector3.zero)).ToArray())}; " +
                              $"skinning alone {Off(skinned)}");
                    // How far into the hands the skin goes: the tissue solved stays a few
                    // millimetres off them.
                    var palms = Hands();
                    string InPalms(Vector3[] shown)
                    {
                        int palmed = 0;
                        float deepestIn = 0f;
                        for (int v = 0; v < shown.Length; v++)
                        {
                            if (otherPair[v]) continue;
                            foreach (var (palmA, palmB, palmR) in palms)
                            {
                                var palmAB = palmB - palmA;
                                var onPalm = palmA + palmAB * Mathf.Clamp01(Vector3.Dot(shown[v] - palmA, palmAB) / Mathf.Max(palmAB.sqrMagnitude, 1e-10f));
                                float inPalm = palmR - (shown[v] - onPalm).magnitude;
                                if (inPalm <= 0f) continue;
                                palmed++;
                                deepestIn = Mathf.Max(deepestIn, inPalm);
                                break;
                            }
                        }
                        return $"{palmed} vertices in a palm, {100f * deepestIn:0.0} cm the deepest";
                    }
                    Debug.Log($"[SoftBody] skin in the hands, {seamRead}: shader {InPalms(drawn)}; solved {InPalms(solved)}");
                    // Every vertex of the first pair reads the same hands, so the largest is the reading.
                    float[] SoftPicks(string with)
                    {
                        var p = new float[30];
                        for (int k = 0; k < 10; k++)
                        {
                            Shader.SetGlobalFloat("_SoftPick", k);
                            var got = Read(Staged(tearShaders, JumpShader, "SquashJump", with), out var gotSeen);
                            for (int c = 0; c < 3; c++) p[3 * k + c] = got.Where((x, i) => gotSeen[i]).Select(x => x[c]).DefaultIfEmpty(0f).Max();
                        }
                        return p;
                    }
                    var shaderPicks = SoftPicks("softamt");
                    string SoftAmounts(string with)
                    {
                        var p = with == "softamt" ? shaderPicks : SoftPicks(with);
                        string Cm(params int[] at) => string.Join(" ", at.Select(i => (100f * p[i]).ToString("0.0")));
                        return $"squeezed {100f * p[5]:0.0} cm, then out, in, front, across, back: {Cm(0, 1, 2, 3, 4)} | {Cm(6, 7, 8, 9, 10)}; " +
                               $"skin {Cm(12, 13, 14, 15, 16)} | {Cm(17, 18, 19, 20, 21)}; depths {Cm(11, 22)}; {p[23]:0} sources";
                    }
                    Debug.Log($"[SoftBody] shader read {SoftAmounts("softamt")}; without the own arms {SoftAmounts("softnoarm")}; " +
                              $"the probe's skin {string.Join(" ", basis.Take(10).Select(b => (100f * Extent(b.part, b.dir)).ToString("0.0")))}; the probe read {summedRead}");
                    // Which half is short: the build's answers or the probe's, each summed as the shader
                    // sums them for the shader's reading of these hands and for the probe's.
                    if (gameBasis != null && summedAmounts != null && basis != null)
                    {
                        bool down = AcrossIsDown;
                        var probeBasis = Enumerable.Range(0, 2 * YapsSoftBody.Ways).Select(e =>
                        {
                            int w = e / 2, pw = down && w % 5 >= 3 && w < 10 ? w + (w % 5 == 3 ? 1 : -1) : w;
                            return summed.SkinMoves(basis[pw].at[e % 2]).Select(m => float.IsNaN(m.x) ? Vector3.zero : m).ToArray();
                        }).ToArray();
                        var sp = shaderPicks;
                        var shaderAmounts = new[] { sp[0], sp[1], sp[2], sp[3], sp[4], sp[6], sp[7], sp[8], sp[9], sp[10], sp[5], sp[24], sp[25], sp[26], sp[27] };
                        float d0 = sp[11], d1 = sp[22];
                        Vector3[] Sum(Vector3[][] b, float[] a)
                        {
                            var m = new Vector3[rest.Length];
                            if (d0 > 0f && d1 > d0)
                                for (int e = 0; e < YapsSoftBody.Ways; e++)
                                {
                                    float d = Mathf.Min(a[e], 1.6f * d1);
                                    if (d <= 0f) continue;
                                    for (int v = 0; v < m.Length && v < b[2 * e].Length; v++)
                                        m[v] += d <= d0 ? b[2 * e][v] * (d / d0) : Vector3.LerpUnclamped(b[2 * e][v], b[2 * e + 1][v], (d - d0) / (d1 - d0));
                                }
                            return rest.Select((r, i) => smr.transform.position + r + m[i]).ToArray();
                        }
                        Debug.Log($"[SoftBody] answers by reading, {seamRead}: the build's by the shader {Off(Sum(gameBasis, shaderAmounts))}; the build's by the probe {Off(Sum(gameBasis, summedAmounts))}; " +
                                  $"the probe's by the shader {Off(Sum(probeBasis, shaderAmounts))}; the probe's by the probe {Off(Sum(probeBasis, summedAmounts))}" +
                                  (extentBasis != null ? $"; the probe's inputs with the shader's skin, by the shader {Off(Sum(extentBasis, shaderAmounts))}" : "") +
                                  (posedBasis != null ? $"; as the build poses it, by the shader {Off(Sum(posedBasis, shaderAmounts))}" : "") +
                                  (handBasis != null ? $"; and skinned by hand, by the shader {Off(Sum(handBasis, shaderAmounts))}" : "") +
                                  (buildBasis != null ? $"; the build's as baked, by the shader {Off(Sum(buildBasis, shaderAmounts))}" : ""));
                    }
                    if (!waysCompared && basis != null)
                    {
                        waysCompared = true;
                        // Each baked way against the probe's way pressing the same side, both its
                        // ups: the shader's across is the cross of toward and out, either way up.
                        var cells = new List<string>();
                        for (int w = 0; w < YapsSoftBody.Ways; w++)
                            for (int k = 0; k < 2; k++)
                            {
                                Shader.SetGlobalFloat("_SoftWay", w);
                                Shader.SetGlobalFloat("_SoftDeep", k);
                                var got = Read(Staged(tearShaders, JumpShader, "SquashJump", "softway"), out var gotSeen);
                                gameBasis ??= new Vector3[2 * YapsSoftBody.Ways][];
                                gameBasis[2 * w + k] = got.Select((g, i) => gotSeen[i] ? g : Vector3.zero).ToArray();
                                string best = "";
                                double bestOff = double.MaxValue;
                                foreach (int pw in w % 5 >= 3 && w < 10 ? new[] { w - w % 5 + 3, w - w % 5 + 4 } : new[] { w })
                                {
                                    var want = summed.SkinMoves(basis[pw].at[k]);
                                    double se = 0, gs = 0, ws = 0, dot = 0;
                                    int vertices = 0;
                                    for (int v = 0; v < want.Length && v < got.Length; v++)
                                    {
                                        var g = gotSeen[v] ? got[v] : Vector3.zero;
                                        var x = float.IsNaN(want[v].x) ? Vector3.zero : want[v];
                                        if (otherPair[v] || g.sqrMagnitude < 1e-8f && x.sqrMagnitude < 1e-8f) continue;
                                        se += (g - x).sqrMagnitude;
                                        gs += g.sqrMagnitude;
                                        ws += x.sqrMagnitude;
                                        dot += Vector3.Dot(g, x);
                                        vertices++;
                                    }
                                    double off = Math.Sqrt(se / Math.Max(vertices, 1));
                                    if (off >= bestOff) continue;
                                    bestOff = off;
                                    best = $"way {w}.{k} as {pw}: baked {100 * Math.Sqrt(gs / Math.Max(vertices, 1)):0.00} probe {100 * Math.Sqrt(ws / Math.Max(vertices, 1)):0.00} off {100 * off:0.00} cm rms, " +
                                           $"cos {dot / Math.Max(Math.Sqrt(gs * ws), 1e-12):0.00}, {vertices} vertices";
                                }
                                cells.Add(best);
                            }
                        Debug.Log($"[SoftBody] baked against the probe's answers, the probe's depths {100f * basisDepths[0]:0.0} {100f * basisDepths[1]:0.0} cm: {string.Join("; ", cells)}");
                        // The build's bake on the probe's own inputs, either way up: the port against
                        // the probe's tissue, apart from what the build feeds it.
                        Vector3[][] upright = null;
                        string editFile = Path.Combine(SessionState.GetString(K + "out", ""), $"edit-{refBody.name}.f32");
                        Vector3[] editAt = null;
                        if (File.Exists(editFile))
                        {
                            var raw = File.ReadAllBytes(editFile);
                            var f = new float[raw.Length / 4];
                            Buffer.BlockCopy(raw, 0, f, 0, raw.Length);
                            editAt = Enumerable.Range(0, f.Length / 3).Select(i => new Vector3(f[3 * i], f[3 * i + 1], f[3 * i + 2])).ToArray();
                            if (editAt.Length != refRest.Length) editAt = null;
                        }
                        Vector3[] handAt = null;
                        string handFile = Path.Combine(SessionState.GetString(K + "out", ""), $"edit-hand-{refBody.name}.f32");
                        if (editAt != null && File.Exists(handFile))
                        {
                            var raw = File.ReadAllBytes(handFile);
                            var f = new float[raw.Length / 4];
                            Buffer.BlockCopy(raw, 0, f, 0, raw.Length);
                            handAt = Enumerable.Range(0, f.Length / 3).Select(i => new Vector3(f[3 * i], f[3 * i + 1], f[3 * i + 2])).ToArray();
                            if (handAt.Length != refRest.Length) handAt = null;
                        }
                        if (editAt != null)
                        {
                            // How far the pair's skin stands from where it plays, the pair's own shift taken out.
                            var on = Enumerable.Range(0, refRest.Length).Where(i => (basisShare[i].x >= 0.5f || basisShare[i].y >= 0.5f) && (refRest[i] - 0.5f * (partAt[0] + partAt[1])).magnitude < 2f * basisReach).ToArray();
                            var shift = on.Aggregate(Vector3.zero, (a, i) => a + editAt[i] - refRest[i]) / Math.Max(on.Length, 1);
                            double pe = on.Sum(i => (double) (editAt[i] - refRest[i] - shift).sqrMagnitude);
                            float pw = on.Length == 0 ? 0f : on.Max(i => (editAt[i] - refRest[i] - shift).magnitude);
                            Debug.Log($"[SoftBody] the pair's skin as the build posed it against Play: {on.Length} vertices, shifted {100f * shift.magnitude:0.0} cm, then {100 * Math.Sqrt(pe / Math.Max(on.Length, 1)):0.00} cm rms, {100f * pw:0.0} worst");
                        }
                        // The skin each way as the shader measures it, the material's reaches: out, in,
                        // front, across and back, for the palms of a bake on the probe's inputs.
                        float ShaderSkin(int j, Vector3 w)
                        {
                            var up = animator.transform.up;
                            var t = Vector3.ProjectOnPlane(partAt[1 - j] - partAt[j], up).normalized;
                            var across = Vector3.Cross(Vector3.ProjectOnPlane(partAt[1] - partAt[0], up).normalized, animator.transform.forward).normalized;
                            var ways = new[] { -t, t, animator.transform.forward, across, -across };
                            int best = Enumerable.Range(0, 5).OrderByDescending(i => Vector3.Dot(w, ways[i])).First();
                            return shaderPicks[12 + 5 * j + best];
                        }
                        // 4: the build's own pose: the skin as the scene stands, each part's centre the middle
                        // of its skin there, as the build takes it, and the shader's skin.
                        Vector3[] Middles(Vector3[] at) => at == null ? null : new[] { 0, 1 }.Select(j =>
                        {
                            // The probe's shares hold both pairs: only the skin by this part.
                            var on = Enumerable.Range(0, at.Length).Where(i => (j == 0 ? basisShare[i].x : basisShare[i].y) >= 0.5f && (refRest[i] - partAt[j]).magnitude < 2f * basisReach).ToArray();
                            return on.Aggregate(Vector3.zero, (a, i) => a + at[i]) / Math.Max(on.Length, 1);
                        }).ToArray();
                        var posedAt = Middles(editAt);
                        // 5: as 4, on the skin skinned by hand as the build does, without blend shapes.
                        var handMiddles = Middles(handAt);
                        foreach (var sign in (editAt != null ? new[] { 1f, -1f, 2f } : new[] { 1f, -1f }).Append(3f).Concat(editAt != null ? new[] { 4f } : new float[0]).Concat(handAt != null ? new[] { 5f } : new float[0]))
                        {
                            var root = animator.transform;
                            // 2: upright, on the skin as the build posed it.
                            // 3: upright, the palms placed by the shader's skin.
                            var (moves, _, said) = YapsSoftBody.Bake(sign == 5f ? handAt : sign == 2f || sign == 4f ? editAt : refRest, refBody.sharedMesh.triangles, YapsSquash.Welds(refBody.sharedMesh), basisShare,
                                sign == 5f ? handMiddles[0] : sign == 4f ? posedAt[0] : partAt[0], sign == 5f ? handMiddles[1] : sign == 4f ? posedAt[1] : partAt[1],
                                basisReach, (sign > 1f ? 1f : sign) * root.up, root.forward, -root.up, (j, w) => sign >= 3f ? ShaderSkin(j, w) : Extent(j, w));
                            if (sign == 4f && moves != null) Debug.Log($"[SoftBody] the build's centres as posed, against Play: {100f * (posedAt[0] - partAt[0]).magnitude:0.0} and {100f * (posedAt[1] - partAt[1]).magnitude:0.0} cm");
                            if (sign >= 3f && moves != null)
                            {
                                var inOrder = Enumerable.Range(0, 2 * YapsSoftBody.Ways).Select(e =>
                                {
                                    int w = e / 2, pw = AcrossIsDown && w % 5 >= 3 && w < 10 ? w + (w % 5 == 3 ? 1 : -1) : w;
                                    return moves[2 * pw + e % 2];
                                }).ToArray();
                                if (sign == 3f) extentBasis = inOrder;
                                else if (sign == 4f) posedBasis = inOrder;
                                else handBasis = inOrder;
                            }
                            var rowsOff = new List<string>();
                            for (int w = 0; w < YapsSoftBody.Ways && moves != null; w++)
                                for (int k = 0; k < 2; k++)
                                {
                                    int pw = w % 5 >= 3 && w < 10 && sign < 0f ? w + (w % 5 == 3 ? 1 : -1) : w;
                                    var want = summed.SkinMoves(basis[pw].at[k]);
                                    double se = 0, gs = 0, ws = 0, dot = 0;
                                    int vertices = 0;
                                    for (int v = 0; v < want.Length; v++)
                                    {
                                        var g = moves[2 * w + k][v];
                                        var x = float.IsNaN(want[v].x) ? Vector3.zero : want[v];
                                        if (otherPair[v] || g.sqrMagnitude < 1e-8f && x.sqrMagnitude < 1e-8f) continue;
                                        se += (g - x).sqrMagnitude;
                                        gs += g.sqrMagnitude;
                                        ws += x.sqrMagnitude;
                                        dot += Vector3.Dot(g, x);
                                        vertices++;
                                    }
                                    string flipped = "";
                                    if (upright != null)
                                    {
                                        // Against the same press baked the other way up.
                                        var u = upright[2 * pw + k];
                                        double ue = 0, uu = 0, ug = 0, ud = 0;
                                        for (int v = 0; v < u.Length; v++)
                                        {
                                            var g = moves[2 * w + k][v];
                                            ue += (g - u[v]).sqrMagnitude;
                                            uu += u[v].sqrMagnitude;
                                            ug += g.sqrMagnitude;
                                            ud += Vector3.Dot(g, u[v]);
                                        }
                                        flipped = $" (as upright cos {ud / Math.Max(Math.Sqrt(uu * ug), 1e-12):0.00}, {100 * Math.Sqrt(ue / Math.Max(vertices, 1)):0.00})";
                                    }
                                    rowsOff.Add($"{w}.{k} cos {dot / Math.Max(Math.Sqrt(gs * ws), 1e-12):0.00} off {100 * Math.Sqrt(se / Math.Max(vertices, 1)):0.00}{flipped}");
                                }
                            Debug.Log($"[SoftBody] the build's bake on the probe's inputs, up {sign:+0;-0}: {said}; {string.Join(", ", rowsOff)}");
                            upright ??= moves;
                        }
                        string bakedAt = Path.Combine(SessionState.GetString(K + "out", ""), "edit-soft.f32");
                        if (File.Exists(bakedAt) && gameBasis != null)
                        {
                            var raw = File.ReadAllBytes(bakedAt);
                            var f = new float[raw.Length / 4];
                            Buffer.BlockCopy(raw, 0, f, 0, raw.Length);
                            int n = refRest.Length;
                            int bakedFields = 2 * YapsSoftBody.Ways + 1;
                            if (f.Length == 3 * (bakedFields * n + 5))
                            {
                                Vector3 V(int i) => new Vector3(f[3 * i], f[3 * i + 1], f[3 * i + 2]);
                                buildBasis = Enumerable.Range(0, 2 * YapsSoftBody.Ways).Select(e => Enumerable.Range(0, n).Select(v => V(n * (e + 1) + v)).ToArray()).ToArray();
                                Vector3 c0 = V(bakedFields * n), c1 = V(bakedFields * n + 1), across = V(bakedFields * n + 2), outward = V(bakedFields * n + 3), reach = V(bakedFields * n + 4);
                                string Against(Vector3[][] other)
                                {
                                    var listed = new List<string>();
                                    for (int e = 0; e < 22; e++)
                                    {
                                        double se = 0, gs = 0, os = 0, dot = 0;
                                        int counted = 0;
                                        for (int v = 0; v < n; v++)
                                        {
                                            if (otherPair[v]) continue;
                                            Vector3 g = buildBasis[e][v], o = other[e][v];
                                            if (g.sqrMagnitude < 1e-8f && o.sqrMagnitude < 1e-8f) continue;
                                            se += (g - o).sqrMagnitude;
                                            gs += g.sqrMagnitude;
                                            os += o.sqrMagnitude;
                                            dot += Vector3.Dot(g, o);
                                            counted++;
                                        }
                                        listed.Add($"{e / 2}.{e % 2} cos {dot / Math.Max(Math.Sqrt(gs * os), 1e-12):0.00} off {100 * Math.Sqrt(se / Math.Max(counted, 1)):0.00} of {100 * Math.Sqrt(gs / Math.Max(counted, 1)):0.00}");
                                    }
                                    return string.Join(", ", listed);
                                }
                                Debug.Log($"[SoftBody] the build's inputs: centres off the probe's {100f * (c0 - partAt[0]).magnitude:0.0} and {100f * (c1 - partAt[1]).magnitude:0.0} cm" +
                                          (handMiddles != null ? $", off its middles skinned by hand {100f * (c0 - handMiddles[0]).magnitude:0.0} and {100f * (c1 - handMiddles[1]).magnitude:0.0}" : "") +
                                          $"; across {Vector3.Angle(across, animator.transform.up):0.0} degrees off up, out {Vector3.Angle(outward, animator.transform.forward):0.0} off forward; reach {100f * reach.x:0.0} cm against {100f * basisReach:0.0}");
                                Debug.Log($"[SoftBody] the build's answers as baked, against what the shader applies: {Against(gameBasis)}");
                                if (handBasis != null) Debug.Log($"[SoftBody] the build's answers as baked, against the probe's bake on the skin skinned by hand: {Against(handBasis)}");
                                // The build's own inputs baked again here: exactly, then with one changed at a time.
                                string sharesAt = Path.ChangeExtension(bakedAt, ".shares.f32");
                                if (File.Exists(sharesAt))
                                {
                                    var sr = File.ReadAllBytes(sharesAt);
                                    var sf = new float[sr.Length / 4];
                                    Buffer.BlockCopy(sr, 0, sf, 0, sr.Length);
                                    var buildShare = Enumerable.Range(0, sf.Length / 2).Select(i => new Vector2(sf[2 * i], sf[2 * i + 1])).ToArray();
                                    var buildAt = Enumerable.Range(0, n).Select(V).ToArray();
                                    if (editAt != null && posedAt != null)
                                    {
                                        var near = Enumerable.Range(0, n).Where(i => (buildShare[i].x >= 0.5f || buildShare[i].y >= 0.5f)).ToArray();
                                        double se = near.Sum(i => (double) (buildAt[i] - editAt[i]).sqrMagnitude);
                                        Debug.Log($"[SoftBody] the build's skin against Unity's in the scene pose: {near.Length} vertices of the pair, {100 * Math.Sqrt(se / Math.Max(near.Length, 1)):0.00} cm rms, " +
                                                  $"{100f * near.Select(i => (buildAt[i] - editAt[i]).magnitude).DefaultIfEmpty(0f).Max():0.0} worst; centres off the probe's middles there {100f * (c0 - posedAt[0]).magnitude:0.0} and {100f * (c1 - posedAt[1]).magnitude:0.0} cm");
                                    }
                                    var runs = new List<(string, Vector3[], Vector2[], Vector3, Vector3, Vector3, Vector3)>
                                    {
                                        ("exactly as the build", buildAt, buildShare, c0, c1, across, outward),
                                        ("with the probe's shares", buildAt, basisShare, c0, c1, across, outward),
                                    };
                                    if (editAt != null) runs.Add(("on the skin as Unity draws it", editAt, buildShare, c0, c1, across, outward));
                                    if (posedAt != null) runs.Add(("with the probe's middles", buildAt, buildShare, posedAt[0], posedAt[1], across, outward));
                                    foreach (var (label, skinAt, sh, ca, cb, up, fwd) in runs)
                                    {
                                        var (again, _, _) = YapsSoftBody.Bake(skinAt, refBody.sharedMesh.triangles, YapsSquash.Welds(refBody.sharedMesh), sh, ca, cb, reach.x, up, fwd, -animator.transform.up,
                                            (j, w) => ShaderSkin(j, w));
                                        if (again == null) continue;
                                        bool flip = Vector3.Dot(up, across) < 0f;
                                        var inOrder = Enumerable.Range(0, 2 * YapsSoftBody.Ways).Select(e =>
                                        {
                                            int w = e / 2, pw = flip && w % 5 >= 3 && w < 10 ? w + (w % 5 == 3 ? 1 : -1) : w;
                                            return again[2 * pw + e % 2];
                                        }).ToArray();
                                        Debug.Log($"[SoftBody] the build's answers as baked, against its inputs baked here {label}: {Against(inOrder)}");
                                    }
                                }
                            }
                        }
                    }
                }
                // The same counted once where the skin overlaps itself, as the eye sees it: two
                // sides of a cleavage already crossing at rest lose that overlap to the seam, which
                // the sum above reads as volume gone and no one sees.
                {
                    var root = animator.transform;
                    float half = 1.5f * (provider.transforms[0].position - provider.transforms[1].position).magnitude;
                    var after = rest.Select((x, i) => x + (seen[i] ? moved[i] : Vector3.zero)).ToArray();
                    double seenBefore = SeenVolume(rest, tris, mid, root.right, root.up, root.forward, half, 160);
                    double seenAfter = SeenVolume(after, tris, mid, root.right, root.up, root.forward, half, 160);
                    Debug.Log($"[SquashPlay] seen {stage} {smr.name}: the squash adds {(seenAfter - seenBefore) * 1e6:0} cm3, {100 * (seenAfter - seenBefore) / Math.Max(Math.Abs(pairV), 1e-9):+0.0;-0.0}% of the pair");
                }
                rows.Add(string.Join("\t", stage == "" ? "full" : stage, smr.name, count, over2, over3, worst.ToString("0.00", CultureInfo.InvariantCulture), turned,
                    opened, (100f * widest).ToString("0.0", CultureInfo.InvariantCulture), skinOpened, openedStart, (100f * widestStart).ToString("0.0", CultureInfo.InvariantCulture),
                    (gained * 1e6).ToString("0", CultureInfo.InvariantCulture)));
                Debug.Log($"[SquashPlay] tear {stage} {smr.name}: {count} edges moved, {over2} over 2x, {over3} over 3x, worst {worst:0.00}x, {turned} turned over, " +
                          $"{opened} opened over 1 cm, widest {100f * widest:0.0} cm; from the start: skin {skinOpened}, squashed {openedStart}, widest {100f * widestStart:0.0} cm");
                UnityEngine.Object.DestroyImmediate(baked);
                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
        }

        static readonly Color[] Tints =
        {
            new Color(1f, 0.6f, 0.4f, 1f), new Color(0.4f, 0.65f, 1f, 1f), new Color(0.5f, 0.95f, 0.5f, 1f),
            new Color(1f, 0.95f, 0.45f, 1f), new Color(0.85f, 0.55f, 1f, 1f), new Color(0.5f, 0.95f, 0.95f, 1f),
            new Color(1f, 0.5f, 0.75f, 1f), new Color(0.75f, 0.75f, 0.75f, 1f),
        };

        // The drag's front and top views in the look shader: every layer tinted its own colour, then
        // each squashed layer alone.
        static void DragLook(string name)
        {
            var shown = animator.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToList();
            var squashed = shown.Where(r => r.sharedMaterials.Any(m => m != null && m.HasProperty("_YAPS_DentOwn"))).ToList();
            var block = new MaterialPropertyBlock();
            void Tint(Renderer r, Color c)
            {
                r.GetPropertyBlock(block);
                block.SetColor("_LookTint", c);
                r.SetPropertyBlock(block);
            }
            if (name.EndsWith("_0"))
                Debug.Log("[SquashPlay] look tints: " + string.Join(", ", shown.Select((r, i) => $"{r.name} {Tints[i % Tints.Length]}")));
            var pixels = new Texture2D(800, 800, TextureFormat.RGB24, false);
            Shader.SetGlobalFloat("_LookMode", 0f);
            Shader.SetGlobalFloat("_LookRaw", 0f);
            void Grab(string file, string stage = "")
            {
                foreach (var (cam, view) in new[] { (front, "front"), (side, "top") })
                {
                    cam.RenderWithShader(Look(stage), "");
                    RenderTexture.active = cam.targetTexture;
                    pixels.ReadPixels(new Rect(0, 0, 800, 800), 0, 0);
                    File.WriteAllBytes(Path.Combine(outDir, $"{file}_{view}.png"), pixels.EncodeToPNG());
                }
                RenderTexture.active = null;
            }
            for (int i = 0; i < shown.Count; i++) Tint(shown[i], Tints[i % Tints.Length]);
            Grab($"{name}_look");
            if (name.EndsWith("_239") && Environment.GetCommandLineArgs().Contains("-spLookStages"))
                foreach (var s in ChosenStages()) Grab($"{name}_look_{s.name}", s.name);
            foreach (var only in squashed)
            {
                foreach (var r in shown) r.enabled = r == only;
                string layer = string.Concat(only.name.Split(Path.GetInvalidFileNameChars()));
                Grab($"{name}_look_{layer}");
                // A layer under another shows its stages only alone.
                if (name.EndsWith("_239") && Environment.GetCommandLineArgs().Contains("-spLookStages"))
                    foreach (var s in ChosenStages()) Grab($"{name}_look_{layer}_{s.name}", s.name);
            }
            foreach (var r in shown)
            {
                r.enabled = true;
                Tint(r, Color.clear);
            }
            UnityEngine.Object.DestroyImmediate(pixels);
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
                float press = Mathf.Max(lifted.magnitude - push.y * reach[p] * Unit(slots[p]), 0f) * push.x;
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
            // -spEach: the front view again with one renderer at a time, to name what draws where.
            if (Environment.GetCommandLineArgs().Contains("-spEach"))
            {
                var shown = animator.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToList();
                foreach (var only in shown)
                {
                    foreach (var r in shown) r.enabled = r == only;
                    front.Render();
                    RenderTexture.active = front.targetTexture;
                    pixels.ReadPixels(new Rect(0, 0, 800, 800), 0, 0);
                    string file = string.Concat(only.name.Split(Path.GetInvalidFileNameChars()));
                    File.WriteAllBytes(Path.Combine(outDir, $"{name}_front_{file}.png"), pixels.EncodeToPNG());
                }
                foreach (var r in shown) r.enabled = true;
            }
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(pixels);
        }

        // A follow's unit as the shader reads it: its matrix's first column, which lossyScale is
        // not under a turned, unevenly scaled parent.
        static float Unit(Transform t) => ((Vector3)t.localToWorldMatrix.GetColumn(0)).magnitude;

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
#endif
