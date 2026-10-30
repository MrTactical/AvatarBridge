// The jelly spike: can ChilloutVR run a soft body on an avatar, every frame, and at what cost?
//
// A lattice of particles lives in a float render texture, where each particle is in its left half
// and where it was a pass before in its right. Two blitters run by a render controller step it from
// that texture into a twin and back, a pass each, no temporary and no copy: every particle is
// pulled toward where the rigid fit of its 3x3x3 neighbourhood puts it (shape matching), the back
// face is held on an anchor the material data provider hands in, and the hands the first pass
// found in reach push it out of the way. Past 6 m from the viewer's head it hangs rigid and does
// next to no work; as a prop, settled, untouched and on an anchor that has not moved, it sleeps,
// the same, until a hand or the anchor wakes it. Several regions can share the texture and the two
// passes, each a block of rows on an anchor of its own. A mesh draws the lattice's surface from the same texture. Nothing is baked,
// nothing is synced, and each client steps its own copy.
//
// A plain cube rides the front: the second pass writes where the front centre is, and how its
// neighbourhood has turned, into two texels of the state's last row, a third blitter copies them
// into a 3x1 texture, and a texture parser writes those into a transform's local position and
// Euler angles. The third texel is the far switch, which the same parser hands to the two steps'
// blitters, so far off they stop altogether. The wear build leaves the cube, its pass and its
// parser out. Nothing on the cube's own shader knows
// the tissue, so its riding says whether anything attached to a bone could follow the tissue too.
//
// On an avatar's own breasts (needs the YAPS squash built): one lattice over the pair, fitted to
// their skin with its back face on the chest wall, the body's squash materials patched with the
// YAPS Jelly role to move each vertex by it, the pair's bone physics switched off. Its tissue rests
// lifted by the sag gravity gives it, so upright it keeps the modelled shape.
//
// A cost meter floats above it: the frame time, and every half second a load of 32 more copies of
// the step, on a second lattice nobody draws, switched on and off, the difference read as what one
// pass costs on that machine.
//
//   -executeMethod AvatarBridge.Regression.JellySpikeBuilder.Build
//   -executeMethod AvatarBridge.Regression.JellySpikeBuilder.Bench -jellyOut <dir>
//   -executeMethod AvatarBridge.Regression.JellySpikeBuilder.Verify -jellyOut <dir>
//   -executeMethod AvatarBridge.Regression.JellySpikeBuilder.VerifyBreasts -jellyScene <scene> [-jellyPrefab <prefab> [-jellySquash]] -jellyOut <dir>
//   -executeMethod AvatarBridge.Regression.JellySpikeBuilder.TuneBreasts -jellyScene <scene> -jellyPrefab <prefab> -jellyOut <dir>
//   -executeMethod AvatarBridge.Regression.JellySpikeBuilder.FilmBreasts -jellyScene <scene> -jellyPrefab <prefab>
//
// Bench times a frame the way a viewer pays for the prefab (its two passes, one texture into the
// other, and the rider's copy; the parser's readback apart) at 90 fps, where the first pass predicts and the second fits,
// and at 60, where each steps, for one pair's lattice and four times that, with no players, 64 far
// off, 64 with eight hands in reach, and the viewer 1 m and 20 m off, and asleep; times chains of plain copies,
// for what a pass costs before it works; and checks the tissue settles and swings back sanely.
// Pass -force-device-index 1 to run it on a second GPU, the CPU's own, for a low-end machine. Verify plays the prefab in
// batch with the CCK's own render controller and a swinging anchor. Batch, never -nographics.
#if CVR_CCK_EXISTS && UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    [InitializeOnLoad]
    public static class JellySpikeBuilder
    {
        // A new folder from 2026-10-27: an editor that rebuilt the old one by delete and remake
        // could no longer load its materials at all, so its assets start again under new GUIDs.
        // The probe prop's in a folder of its own, so building it leaves a worn jelly's assets be. Kept
        // in the session, which a Play mode's reload keeps too.
        static string Folder => SessionState.GetBool(K + "prop", false) ? "Assets/AvatarBridge_JellyProp" : "Assets/AvatarBridge_JellySpike";
        static string Prefab => Folder + "/Jelly_Spike.prefab";
        static string Scene => Folder + "/Jelly_Preview.unity";
        const string K = "JellySpike.";
        // One pair's tissue is about 3000 particles; this is 3072, 20 cm across at 1.33 cm.
        static readonly Vector3Int Size = new Vector3Int(16, 12, 16);
        // A coarse lattice the surface blends between, an eighth of the fine one's particles. The
        // default: it sags and turns within millimetres of the fine one and costs a slow GPU about
        // half; the menu's tick takes the fine one back.
        static readonly Vector3Int Mid = new Vector3Int(12, 9, 12), Coarse = new Vector3Int(8, 6, 8);
        // 20 cm deep whatever the lattice.
        static float SpacingOf(Vector3Int n) => 0.2f / (n.z - 1);
        const string CoarseKey = "AvatarBridge.JellySpike.Coarse";
        static Vector3Int Lattice => EditorPrefs.GetBool(CoarseKey, true) ? Coarse : Size;

        [MenuItem("Tools/AvatarBridge Dev/Jelly spike: coarse lattice")]
        static void ToggleCoarse() => EditorPrefs.SetBool(CoarseKey, !EditorPrefs.GetBool(CoarseKey, true));

        [MenuItem("Tools/AvatarBridge Dev/Jelly spike: coarse lattice", true)]
        static bool ToggleCoarseShown()
        {
            Menu.SetChecked("Tools/AvatarBridge Dev/Jelly spike: coarse lattice", EditorPrefs.GetBool(CoarseKey, true));
            return true;
        }
        const int Row = 64, Passes = 2, Frames = 300, Load = 32, BreastFrames = 3300, ViewFrames = 540, ProbesPer = 24;
        // How far off the skin a collider pushes a probe: the probe cloth's radius.
        const float ProbeReach = 0.04f;
        struct Late { }

        static int frame;
        static Camera eye;
        static Transform swing, jelly;
        static Transform[] breastRegions, breastBones;
        static Matrix4x4[] breastOn;
        static readonly List<string> breastTrace = new List<string>();
        static Matrix4x4 seen;

        static JellySpikeBuilder()
        {
            if (SessionState.GetInt(K + "on", 0) != 0) EditorApplication.update += Hook;
        }

        public static void Build() => EditorApplication.Exit(Make() != null ? 0 : 1);

        [MenuItem("Tools/AvatarBridge Dev/Jelly spike: add to the selected avatar")]
        static void AddToSelected() => Add(withMeter: false);

        // To wear: no test cube, so one pass, its readback and its parser fewer.
        [MenuItem("Tools/AvatarBridge Dev/Jelly spike: add to wear, without the test cube")]
        static void AddToWear() => Add(withMeter: false, withRider: false, offFar: false);

        // The same with the pass and the parser kept for the far switch alone: dearer near, cheaper
        // far, so it pays on a slow GPU in a crowd.
        [MenuItem("Tools/AvatarBridge Dev/Jelly spike: add to wear, far copies switched off")]
        static void AddToWearOffFar() => Add(withMeter: false, withRider: false, offFar: true);

        // Three regions packed into one texture and one pair of passes: on the chest, behind the
        // hips and above the head, each on its own bone.
        [MenuItem("Tools/AvatarBridge Dev/Jelly spike: add three to wear, packed")]
        static void AddPacked() => Add(withMeter: false, withRider: false, offFar: false, regions: 3);

        [MenuItem("Tools/AvatarBridge Dev/Jelly spike: make a prop of three, packed")]
        static void MakePackedProp() => MakeProp(3);

        // The meter's load costs 32 passes every other half second, far more than the jelly, so it
        // goes on only when asked for.
        [MenuItem("Tools/AvatarBridge Dev/Jelly spike: add with the cost meter")]
        static void AddWithMeter() => Add(withMeter: true);

        // The jelly alone as a prop, its cost told apart from any avatar's.
        [MenuItem("Tools/AvatarBridge Dev/Jelly spike: make a prop in the open scene")]
        static void MakeOneProp() => MakeProp(1);

        static GameObject MakeProp(int regions, bool withRider = true)
        {
            var prefab = Make(preview: false, withMeter: false, withRider: withRider, regions: regions);
            if (prefab == null) return null;
            var old = GameObject.Find("Jelly Prop");
            if (old != null) Undo.DestroyObjectImmediate(old);
            var prop = new GameObject("Jelly Prop");
            prop.AddComponent<CVRSpawnable>();
            var made = (GameObject) PrefabUtility.InstantiatePrefab(prefab, prop.transform);
            // At chest height above the floor it is placed on.
            made.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            // Grabbable. The pickup goes on the spawnable itself, which the client syncs as the
            // prop's own place, and its collider on the same object, round the jelly and the cube.
            // With no rigidbody the client adds a kinematic one, so a jelly let go hangs where it
            // was left.
            prop.AddComponent<CVRPickupObject>();
            var box = prop.AddComponent<BoxCollider>();
            var span = (Vector3) (Lattice - Vector3Int.one) * SpacingOf(Lattice);
            box.center = new Vector3(0f, 1.2f, 0.5f * span.z + 0.025f);
            box.size = span + new Vector3(0.06f + 0.3f * (regions - 1), 0.06f, 0.11f);
            // A trigger, so it still takes the grab but never pushes a player away.
            box.isTrigger = true;
            Undo.RegisterCreatedObjectUndo(prop, "Make the jelly prop");
            Selection.activeGameObject = prop;
            Debug.Log("[Jelly] made Jelly Prop in the open scene, grabbable; upload it from the CCK Control Panel");
            return prop;
        }

#if AVATARBRIDGE_MAGICA
        // Two jellies on one prop, MagicaCloth probes on each one's front: the left feels the player
        // arrays' hands too, the right its probes alone, so a poke that dents the right one in game
        // is a collider's, the client's capsule on the hand.
        [MenuItem("Tools/AvatarBridge Dev/Jelly spike: make a prop of two, the right one colliders only")]
        static void MakeProbePropMenu()
        {
            SessionState.SetBool(K + "prop", true);
            try { MakeProbeProp(); }
            finally { SessionState.SetBool(K + "prop", false); }
        }

        // The same with the right one's probes physics balls the world's solid colliders push, for
        // whether a wall or a table dents it in game. Its plate and skin orange.
        [MenuItem("Tools/AvatarBridge Dev/Jelly spike: make a prop of two, the right one feeling the world")]
        static void MakeWorldPropMenu()
        {
            SessionState.SetBool(K + "prop", true);
            try { MakeProbeProp(world: true); }
            finally { SessionState.SetBool(K + "prop", false); }
        }

        static GameObject MakeProbeProp(bool world = false)
        {
            // No rider: it sat on the left one's front, where the pokes go.
            var prop = MakeProp(2, withRider: false);
            if (prop == null) return null;
            var made = prop.transform.GetChild(0).gameObject;
            PrefabUtility.UnpackPrefabInstance(made, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            var n = Lattice;
            float spacing = SpacingOf(n);
            var span = (Vector3) (n - Vector3Int.one) * spacing;
            var provider = made.GetComponentInChildren<CVRMaterialDataProvider>(true);
            var regions = new[] { made.transform.Find("Jelly Region 0"), made.transform.Find("Jelly Region 1") };
            // Rigid, so each region's still copy sits on it.
            var hangs = new Transform[2];
            for (int r = 0; r < 2; r++)
            {
                hangs[r] = new GameObject($"Jelly Hang {r}").transform;
                hangs[r].SetParent(regions[r].parent, false);
                hangs[r].localPosition = regions[r].localPosition;
                hangs[r].localRotation = regions[r].localRotation;
                hangs[r].localScale = regions[r].localScale;
            }
            provider.transforms = new[] { regions[0], regions[1], hangs[0], hangs[1] };
            // The front half of each surface at rest, farthest first, as on a breast.
            var spots = new List<Vector3>[2];
            for (int r = 0; r < 2; r++)
            {
                var surface = regions[r].Find(r == 0 ? "Surface" : $"Surface {r}");
                var region = regions[r];
                var front = surface.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v => surface.TransformPoint(v)).ToList();
                float deepest = front.Max(w => region.InverseTransformPoint(w).z);
                front = front.Where(w => region.InverseTransformPoint(w).z >= 0.5f * deepest).ToList();
                var picked = new List<Vector3> { front.OrderByDescending(w => region.InverseTransformPoint(w).z).First() };
                while (picked.Count < ProbesPer && picked.Count < front.Count)
                    picked.Add(front.OrderByDescending(w => picked.Min(q => (q - w).sqrMagnitude)).First());
                spots[r] = picked;
            }
            AddSkin(made, provider, hangs, spots, 0.5f * span.z, "Make the jelly prop", world ? new[] { false, true } : null);
            foreach (var mat in provider.materials)
            {
                mat.SetVector("_JellyArrays", new Vector4(1f, 0f, 1f, 1f));
                // The plates' tops, which each rests on.
                mat.SetVector("_Floor", new Vector4(1f, -0.5f * span.y - 0.015f, 0f, 0f));
                EditorUtility.SetDirty(mat);
            }
            // The colliders-only jelly drawn blue itself, not just its plate: both pink, the one a
            // hand passes through read as broken.
            var blueSkin = regions[1].Find("Surface 1").GetComponent<MeshRenderer>();
            var blueDraw = Save(new Material(blueSkin.sharedMaterial), "/Jelly_Draw_Colliders.mat");
            blueDraw.SetColor("_Color", world ? new Color(1f, 0.72f, 0.42f) : new Color(0.5f, 0.68f, 1f));
            blueSkin.sharedMaterial = blueDraw;
            // Green under the one that feels both, blue under the colliders' alone.
            for (int r = 0; r < 2; r++)
            {
                var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plate.name = r == 0 ? "Plate, hands and colliders" : "Plate, colliders only";
                Object.DestroyImmediate(plate.GetComponent<Collider>());
                plate.transform.SetParent(regions[r], false);
                plate.transform.localPosition = new Vector3(0f, -0.5f * span.y - 0.02f, 0.5f * span.z);
                plate.transform.localScale = new Vector3(span.x + 0.06f, 0.01f, span.z + 0.06f);
                var tint = r == 0 ? new Color(0.2f, 0.8f, 0.3f) : world ? new Color(0.95f, 0.5f, 0.1f) : new Color(0.2f, 0.5f, 1f);
                plate.GetComponent<MeshRenderer>().sharedMaterial = Save(new Material(Shader.Find("Standard")) { color = tint }, r == 0 ? "/Jelly_Plate_Both.mat" : "/Jelly_Plate_Colliders.mat");
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Jelly] made the probe prop, {spots[0].Count} probes a jelly: green feels hands and colliders, {(world ? "orange the world's solids" : "blue colliders only")}; upload it from the CCK Control Panel");
            return prop;
        }

        // The probe prop in a scene of its own, played: settled, then a player's hand from the arrays
        // 3 cm into each jelly's front, then away, then a MagicaCloth ball 3 cm into each. Passes when
        // the hands dent the green one alone and the balls dent both.
        public static void VerifyProbeProp()
        {
            SessionState.SetBool(K + "prop", true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (MakeProbeProp() == null)
            {
                SessionState.SetBool(K + "prop", false);
                Debug.Log("[Jelly] FAIL prop: it did not build");
                EditorApplication.Exit(1);
                return;
            }
            // -jellyFilm: the demonstration instead, two cameras side by side.
            bool film = Environment.GetCommandLineArgs().Contains("-jellyFilm");
            SessionState.SetBool(K + "propFilm", film);
            SessionState.SetBool(K + "propWorld", false);
            SessionState.SetBool(K + "propWorldFilm", false);
            var cam = new GameObject("Eye").AddComponent<Camera>();
            cam.transform.position = film ? new Vector3(0.55f, 1.45f, 0.75f) : new Vector3(0f, 1.3f, 1.2f);
            cam.transform.LookAt(new Vector3(0f, 1.22f, film ? 0.12f : 0.1f));
            if (film)
            {
                cam.fieldOfView = 34f;
                var low = new GameObject("Eye 2").AddComponent<Camera>();
                low.transform.position = new Vector3(-0.6f, 1.3f, 0.7f);
                low.transform.LookAt(new Vector3(0f, 1.22f, 0.12f));
                low.fieldOfView = 34f;
            }
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Folder + "/Jelly_PropCheck.unity");
            SessionState.SetString(K + "out", Out());
            SessionState.SetInt(K + "on", 4);
            EditorApplication.update += Hook;
            EditorApplication.EnterPlaymode();
        }

        // The world-feeling prop in a scene of its own, played: settled, then a wall slid 3 cm into
        // the orange one's front, held, and out. Passes when the wall dents the orange one, leaves
        // the green be, and the dent springs back once it goes.
        public static void VerifyWorldProp()
        {
            SessionState.SetBool(K + "prop", true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (MakeProbeProp(world: true) == null)
            {
                SessionState.SetBool(K + "prop", false);
                Debug.Log("[Jelly] FAIL world prop: it did not build");
                EditorApplication.Exit(1);
                return;
            }
            SessionState.SetBool(K + "propFilm", false);
            SessionState.SetBool(K + "propWorld", true);
            // -jellyFilm: filmed instead, two cameras side by side as the prop's film, the wall drawn
            // as glass and slapped on as well.
            bool film = Environment.GetCommandLineArgs().Contains("-jellyFilm");
            SessionState.SetBool(K + "propWorldFilm", film);
            var cam = new GameObject("Eye").AddComponent<Camera>();
            cam.transform.position = film ? new Vector3(0.55f, 1.45f, 0.75f) : new Vector3(0f, 1.3f, 1.2f);
            cam.transform.LookAt(new Vector3(0f, 1.22f, film ? 0.12f : 0.1f));
            if (film)
            {
                cam.fieldOfView = 34f;
                var low = new GameObject("Eye 2").AddComponent<Camera>();
                low.transform.position = new Vector3(0.75f, 1.25f, 0.45f);
                low.transform.LookAt(new Vector3(0.05f, 1.22f, 0.12f));
                low.fieldOfView = 34f;
            }
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Folder + "/Jelly_WorldCheck.unity");
            SessionState.SetString(K + "out", Out());
            SessionState.SetInt(K + "on", 4);
            EditorApplication.update += Hook;
            EditorApplication.EnterPlaymode();
        }

        static Vector4[] worldRest;
        static readonly float[] worldDent = new float[3];
        static float worldThrough;
        static GameObject worldWall;

        static void OnLatePropWorld()
        {
            var n = Lattice;
            var span = (Vector3) (n - Vector3Int.one) * SpacingOf(n);
            var all = Object.FindObjectsOfType<Transform>();
            var regions = new[] { 0, 1 }.Select(r => all.First(t => t.name == $"Jelly Region {r}")).ToArray();
            var state = AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + "/Jelly_State.renderTexture");
            // Over the orange one's front alone, a kinematic body as a moving world solid is, drawn
            // as a pane of glass.
            bool film = SessionState.GetBool(K + "propWorldFilm", false);
            if (worldWall == null)
            {
                worldWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                worldWall.name = "Wall";
                // 6 cm past the face all round, as a pane is wider than what it presses: 1 cm past,
                // the probes on the face's rim slipped round its edge and the corners stood through.
                worldWall.transform.localScale = new Vector3(span.x + 0.12f, span.y + 0.12f, 0.1f);
                worldWall.AddComponent<Rigidbody>().isKinematic = true;
                worldWall.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.75f, 0.9f, 1f, 0.3f) };
            }
            float gap = frame < 150 ? 0.15f : frame < 210 ? Mathf.Lerp(0.15f, -0.03f, (frame - 150) / 60f) : frame < 300 ? -0.03f : Mathf.Lerp(-0.03f, 0.15f, Mathf.Clamp01((frame - 300) / 30f));
            // Filmed, then slapped on in six frames, held, and taken away.
            if (film && frame >= 450) gap = frame < 480 ? 0.15f : frame < 486 ? Mathf.Lerp(0.15f, -0.03f, (frame - 480) / 6f) : frame < 520 ? -0.03f : Mathf.Lerp(-0.03f, 0.15f, Mathf.Clamp01((frame - 520) / 20f));
            worldWall.transform.SetPositionAndRotation(regions[1].TransformPoint(0f, 0f, span.z + 0.05f + gap), regions[1].rotation);
            eye.Render();
            if (film)
            {
                eye2.Render();
                if (frame % 2 == 0)
                {
                    var was = RenderTexture.active;
                    int w = eye.targetTexture.width, h = eye.targetTexture.height;
                    var sheet = new Texture2D(2 * w, h, TextureFormat.RGB24, false);
                    RenderTexture.active = eye.targetTexture;
                    sheet.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    RenderTexture.active = eye2.targetTexture;
                    sheet.ReadPixels(new Rect(0, 0, w, h), w, 0);
                    RenderTexture.active = was;
                    string frames = Path.Combine(SessionState.GetString(K + "out", "Jelly"), "frames");
                    Directory.CreateDirectory(frames);
                    File.WriteAllBytes(Path.Combine(frames, $"f_{propFilmed++:00000}.jpg"), sheet.EncodeToJPG(92));
                    Object.DestroyImmediate(sheet);
                }
            }
            int rows = Side(n), count = n.x * n.y * n.z;
            if (frame == 140) worldRest = Read(state);
            float Dent(Vector4[] now, int r)
            {
                float most = 0f;
                for (int i = 0; i < count; i++)
                {
                    int at = (r * (rows + 1) + i / Row) * 2 * Row + i % Row;
                    most = Mathf.Max(most, Vector3.Distance(now[at], worldRest[at]));
                }
                return most;
            }
            if (frame >= 240 && frame < 300 || frame >= 420 && frame < 450)
            {
                var now = Read(state);
                if (frame < 300)
                {
                    worldDent[0] = Mathf.Max(worldDent[0], Dent(now, 1));
                    worldDent[1] = Mathf.Max(worldDent[1], Dent(now, 0));
                    // How far its skin stands past the wall's face: pressed flat, none.
                    for (int i = 0; i < count; i++)
                        worldThrough = Mathf.Max(worldThrough, regions[1].InverseTransformPoint(now[(rows + 1 + i / Row) * 2 * Row + i % Row]).z - (span.z - 0.03f));
                }
                else worldDent[2] = Mathf.Max(worldDent[2], Dent(now, 1));
            }
            if (++frame < (film ? 700 : 450)) return;
            if (film)
            {
                string dir = SessionState.GetString(K + "out", "Jelly");
                File.WriteAllLines(Path.Combine(dir, "segments.txt"), new[] { "0\t150\tAt rest", "150\t450\tA pane of glass, a world solid, pressed 3 cm in, held, taken away", "450\t700\tThe pane slapped on" });
                File.WriteAllText(Path.Combine(dir, "legend.txt"), "Green plate: hands and MagicaCloth colliders.   Orange plate: the world's solids.");
                Debug.Log($"[Jelly] PASS world prop film: {propFilmed} shots of {frame} frames; the pane dented the orange one {1000f * worldDent[0]:0.0} mm");
                SessionState.SetInt(K + "on", 0);
                SessionState.SetBool(K + "prop", false);
                EditorApplication.Exit(0);
                return;
            }
            bool ok = worldDent[0] > 0.01f && worldDent[1] < 0.003f && worldDent[2] < 0.005f && worldThrough < 0.008f;
            Debug.Log($"[Jelly] {(ok ? "PASS" : "FAIL")} world prop after {frame} frames: a wall 3 cm into the orange one's front dented it {1000f * worldDent[0]:0.0} mm " +
                      $"(its skin {1000f * worldThrough:0.0} mm past the wall) and the green {1000f * worldDent[1]:0.0} mm; the wall gone, the orange one {1000f * worldDent[2]:0.0} mm off its rest");
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static Vector4[] propRest;
        static readonly float[] propDent = new float[4], propPushed = new float[4];
        static readonly int[] propSlots = new int[4];
        static readonly float[] propDragDent = new float[2];
        static float propDragWorst, propDragJump;
        static readonly float[] propThrough = new float[3];
        static float propTwo;
        static readonly float[] propDropRing = new float[2];
        static float propDropLow = 1f;
        static readonly float[] propDragLast = { float.NaN, float.NaN };
        static int propDragFrames, propDragDeep, propDragLost;
        static GameObject[] propBalls, propHands;
        static Camera eye2;
        static int propFilmed;

        // See-through, so the dent under a hand or ball shows.
        static Material Glass(Color c)
        {
            var m = new Material(Shader.Find("Standard")) { color = c };
            m.SetFloat("_Mode", 2f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int) BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int) BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = 3000;
            return m;
        }

        // Players' left hands from the arrays, one player a hand, each straight into its jelly's front
        // from dx along it, its far side depth past the skin. Shown as see-through capsules.
        static void PropHands(Transform[] regions, Vector3[] fronts, float hand, params (int r, float depth, float dx)[] hands)
        {
            const float tall = 1.6f;
            var left = Enumerable.Repeat(new Vector4(50f, 50f, 50f, 0f), 255).ToArray();
            var right = (Vector4[]) left.Clone();
            var chest = (Vector4[]) left.Clone();
            var root = Enumerable.Repeat(new Vector4(50f, 0f, 50f, tall), 255).ToArray();
            for (int i = 0; i < hands.Length; i++)
            {
                var (r, depth, dx) = hands[i];
                var wrist = fronts[r] + regions[r].right * dx + regions[r].forward * (0.11f * tall - depth);
                left[i] = wrist;
                chest[i] = wrist + regions[r].forward * 0.3f;
                root[i] = new Vector4(chest[i].x, 0f, chest[i].z, tall);
                if (propHands == null || i >= propHands.Length) continue;
                var tip = wrist - regions[r].forward * Mathf.Max(0.11f * tall - hand, 0f);
                propHands[i].transform.SetPositionAndRotation(0.5f * (wrist + tip), Quaternion.FromToRotation(Vector3.up, tip - wrist));
                propHands[i].transform.localScale = new Vector3(2f * hand, 0.5f * (tip - wrist).magnitude + hand, 2f * hand);
            }
            if (propHands != null)
                for (int i = 0; i < propHands.Length; i++) propHands[i].SetActive(i < hands.Length);
            Shader.SetGlobalVectorArray("_CVR_PlayerLeftHandPositions", left);
            Shader.SetGlobalVectorArray("_CVR_PlayerRightHandPositions", right);
            Shader.SetGlobalVectorArray("_CVR_PlayerChestPositions", chest);
            Shader.SetGlobalVectorArray("_CVR_PlayerRootPositions", root);
            Shader.SetGlobalVectorArray("_CVR_PlayerHeadPositions", new Vector4[255]);
            Shader.SetGlobalVector("CVRGlobalParams1", new Vector4(0f, hands.Length, 0f, 0f));
        }

        // MagicaCloth balls given to the probes' cloth as a player's colliders are, made on first use:
        // each into its jelly's front from dx, dy along it, its far side depth past the skin; the rest
        // parked far below.
        static void PropBalls(Transform[] regions, Vector3[] fronts, bool seen, params (int r, float depth, float dx, float dy)[] balls)
        {
            const float radius = 0.045f;
            if (propBalls == null)
            {
                var net = Object.FindObjectsOfType<MagicaCloth2.MagicaCloth>().First(c => c.name == "Jelly Probes");
                propBalls = Enumerable.Range(0, 4).Select(i =>
                {
                    var b = new GameObject($"Test ball {i}");
                    var ball = b.AddComponent<MagicaCloth2.MagicaSphereCollider>();
                    ball.SetSize(radius);
                    net.SerializeData.colliderCollisionConstraint.colliderList.Add(ball);
                    if (seen)
                    {
                        var shown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        Object.DestroyImmediate(shown.GetComponent<Collider>());
                        shown.transform.SetParent(b.transform, false);
                        shown.transform.localScale = Vector3.one * 2f * radius;
                        shown.GetComponent<MeshRenderer>().sharedMaterial = Glass(new Color(0.6f, 0.8f, 1f, 0.45f));
                    }
                    return b;
                }).ToArray();
                net.SetParameterChange();
            }
            for (int i = 0; i < propBalls.Length; i++)
            {
                if (i >= balls.Length)
                {
                    propBalls[i].transform.position = Vector3.down * 50f;
                    continue;
                }
                var (r, depth, dx, dy) = balls[i];
                propBalls[i].transform.position = fronts[r] + regions[r].right * dx + regions[r].up * dy + regions[r].forward * (radius - depth);
            }
        }

        static void OnLateProp()
        {
            var n = Lattice;
            float spacing = SpacingOf(n);
            var all = Object.FindObjectsOfType<Transform>();
            var regions = new[] { 0, 1 }.Select(r => all.First(t => t.name == $"Jelly Region {r}")).ToArray();
            var fronts = regions.Select(t => t.TransformPoint(0f, 0f, (n.z - 1) * spacing)).ToArray();
            var state = AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + "/Jelly_State.renderTexture");
            float hand = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Jelly_Step.mat").GetFloat("_Hand");
            // A drag as the film's: in over 0.6 s while moving 7 cm each way across a front, out again.
            (float d, float x) Dragged(int g) => (Mathf.Lerp(-0.05f, 0.025f, Mathf.Clamp01(g / 54f) * Mathf.Clamp01((450 - g) / 54f)), 0.07f * Mathf.Sin(2f * Mathf.PI * 0.6f * g / 90f));
            // Two players, each a left hand straight into one jelly, its far side 3 cm in.
            float s = Mathf.Clamp01((frame - 150) / 30f);
            if (frame >= 150 && frame < 270) PropHands(regions, fronts, hand, (0, 0.08f * s - 0.05f, 0f), (1, 0.08f * s - 0.05f, 0f));
            else if (frame >= 1450 && frame < 1910)
            {
                int t = frame - 1450;
                PropHands(regions, fronts, hand, (0, t < 270 ? Mathf.Lerp(-0.05f, 0.32f, t / 270f) : t < 360 ? 0.32f : Mathf.Lerp(0.32f, -0.05f, (t - 360) / 90f), 0f));
            }
            else if (frame >= 1000)
            {
                var (hd, hx) = Dragged(frame - 1000);
                PropHands(regions, fronts, hand, (0, hd, hx));
            }
            else PropHands(regions, fronts, hand);
            // Then a 4.5 cm ball into each, given to the probes' cloth as a player's collider is, out,
            // and dragged. Last a hand from the arrays dragged the same way over the green, which
            // needs no probes: what a drag should dent.
            var (bd, bx) = frame < 540 ? (frame < 450 ? Mathf.Lerp(-0.055f, 0.03f, Mathf.Clamp01((frame - 330) / 30f)) : Mathf.Lerp(0.03f, -0.05f, (frame - 450) / 60f), 0f) : Dragged(frame - 540);
            // Last lifted 12 cm and dropped onto a stop: the landing alone should ring each.
            var prop = all.First(t => t.name == "Jelly Prop");
            if (frame >= 2010) prop.position = new Vector3(0f, frame < 2040 ? 0.12f * Mathf.SmoothStep(0f, 1f, (frame - 2010) / 30f) : frame < 2060 ? 0.12f : 0.12f * Mathf.Clamp01((2069 - frame) / 9f), 0f);
            // Then two balls 12 cm apart into each, which should take a slot each, not share them.
            float two = Mathf.Lerp(-0.06f, 0.03f, (frame - 1910) / 40f);
            if (frame >= 1910 && frame < 2010) PropBalls(regions, fronts, false, (0, two, -0.06f, 0.03f), (0, two, 0.06f, -0.03f), (1, two, -0.06f, 0.03f), (1, two, 0.06f, -0.03f));
            else if (frame >= 990) PropBalls(regions, fronts, false);
            else if (frame >= 330) PropBalls(regions, fronts, false, (0, bd, bx, 0f), (1, bd, bx, 0f));
            eye.Render();
            int rows = Side(n), count = n.x * n.y * n.z;
            if (frame == 140) propRest = Read(state);
            float Dent(Vector4[] now, int r)
            {
                float most = 0f;
                for (int i = 0; i < count; i++)
                {
                    int at = (r * (rows + 1) + i / Row) * 2 * Row + i % Row;
                    most = Mathf.Max(most, Vector3.Distance(now[at], propRest[at]));
                }
                return most;
            }
            // A sliding collider sweeps probes along, which read far more than it is in: the ball
            // rebuilt from them, its deepest point along the front against the true ball's.
            if (frame >= 540 && frame < 990 || frame >= 1000 && frame < 1450)
            {
                var now = Read(state);
                propDragDent[frame < 990 ? 0 : 1] = Mathf.Max(propDragDent[frame < 990 ? 0 : 1], Dent(now, 0));
                for (int r = 0; r < 2 && frame < 990; r++)
                {
                    if (bd < 0.02f) continue;
                    int side = (r * (rows + 1) + rows) * 2 * Row;
                    float truth = regions[r].InverseTransformPoint(propBalls[r].transform.position).z - 0.045f;
                    var deep = Enumerable.Range(0, 8).Where(e => now[side + 2 * e + 1].w > 0.5f).Select(e => regions[r].InverseTransformPoint(now[side + 2 * e]).z - hand - truth).ToList();
                    propDragFrames++;
                    if (deep.Count == 0) propDragLost++;
                    else
                    {
                        propDragWorst = Mathf.Min(propDragWorst, deep.Min());
                        if (deep.Min() < -0.015f) propDragDeep++;
                        // A jump the ball never made slams the skin: the spasm in the film.
                        if (!float.IsNaN(propDragLast[r])) propDragJump = Mathf.Max(propDragJump, Mathf.Abs(deep.Min() - propDragLast[r]));
                        propDragLast[r] = deep.Min();
                    }
                }
            }
            if (frame >= 200 && frame < 270 || frame >= 390 && frame < 450)
            {
                var now = Read(state);
                int phase = frame < 300 ? 0 : 2;
                for (int r = 0; r < 2; r++)
                {
                    int side = (r * (rows + 1) + rows) * 2 * Row;
                    propDent[phase + r] = Mathf.Max(propDent[phase + r], Dent(now, r));
                    propSlots[phase + r] = Mathf.Max(propSlots[phase + r], Enumerable.Range(0, 8).Count(e => now[side + 2 * e + 1].w > 0.5f));
                    propPushed[phase + r] = Mathf.Max(propPushed[phase + r], Enumerable.Range(0, ProbesPer).Max(k => now[side + 88 + k].w));
                }
            }
            // Pushed on through: the dent held at the deepest, nothing torn, let go once through.
            if (frame >= 1450 && frame < 1900)
            {
                var now = Read(state);
                float dent = Dent(now, 0);
                propThrough[0] = Mathf.Max(propThrough[0], dent);
                propThrough[1] = Mathf.Max(propThrough[1], Enumerable.Range(0, count).Count(i => Vector3.Distance(now[i / Row * 2 * Row + i % Row], propRest[i / Row * 2 * Row + i % Row]) > 0.1f));
                if (frame >= 1750 && frame < 1810) propThrough[2] = Mathf.Max(propThrough[2], dent);
            }
            // On the blue, which only the probes feel: each ball's nearest slot.
            if (frame >= 1960 && frame < 2010)
            {
                var now = Read(state);
                int sd = (2 * rows + 1) * 2 * Row;
                var held = Enumerable.Range(0, 8).Where(e => now[sd + 2 * e + 1].w > 0.5f).Select(e => (Vector3) now[sd + 2 * e]).ToList();
                for (int q = 2; q < 4; q++) propTwo = Mathf.Max(propTwo, held.Count == 0 ? 1f : held.Min(x => Vector3.Distance(x, propBalls[q].transform.position)));
            }
            // Landed, nothing below its board: the lowest particle against the plate's top.
            if (frame >= 2060 && frame < 2110)
            {
                var now = Read(state);
                float board = -0.5f * (n.y - 1) * spacing - 0.015f;
                for (int r = 0; r < 2; r++)
                    for (int i = 0; i < count; i++)
                        propDropLow = Mathf.Min(propDropLow, regions[r].InverseTransformPoint(now[(r * (rows + 1) + i / Row) * 2 * Row + i % Row]).y - board);
            }
            // A ring fired in the frames since the landing, as deep as a slap at its speed.
            if (frame == 2075)
            {
                var now = Read(state);
                for (int r = 0; r < 2; r++)
                {
                    int side = (r * (rows + 1) + rows) * 2 * Row;
                    for (int k = 0; k < 2; k++)
                        if (Time.timeSinceLevelLoad - now[side + 80 + 2 * k].w < 0.1f) propDropRing[r] = Mathf.Max(propDropRing[r], now[side + 81 + 2 * k].w);
                }
            }
            if (++frame < 2110) return;
            bool ok = propDent[0] > 0.01f && propDent[1] < 0.003f && propDent[2] > 0.01f && propDent[3] > 0.01f && propSlots[1] == 0 &&
                      propDragLost == 0 && propDragWorst > -0.03f && propDragDeep <= 0.15f * propDragFrames && propDragJump < 0.015f &&
                      propThrough[0] < 0.06f && propThrough[1] == 0 && propThrough[2] < 0.01f && propTwo < 0.025f && propDropRing[0] > 0.005f && propDropRing[1] > 0.005f && propDropLow > -0.001f;
            Debug.Log($"[Jelly] {(ok ? "PASS" : "FAIL")} prop after {frame} frames: a hand from the arrays 3 cm in dented the green {1000f * propDent[0]:0.0} mm ({propSlots[0]} slots) " +
                      $"and the blue {1000f * propDent[1]:0.0} mm ({propSlots[1]} slots, 0 wanted); a MagicaCloth ball 3 cm in dented the green {1000f * propDent[2]:0.0} mm " +
                      $"({propSlots[2]} slots, probes pushed {1000f * propPushed[2]:0.0} mm) and the blue {1000f * propDent[3]:0.0} mm ({propSlots[3]} slots, {1000f * propPushed[3]:0.0} mm); hand radius {1000f * hand:0} mm; " +
                      $"dragged 7 cm each way, the ball rebuilt from the probes at worst {-100f * propDragWorst:0.0} cm too deep, {propDragDeep} of {propDragFrames} frames over 1.5 cm, its depth jumping {100f * propDragJump:0.0} cm in a frame at most, " +
                      $"and {propDragLost} without one; the green's dent {1000f * propDragDent[0]:0.0} mm against {1000f * propDragDent[1]:0.0} mm for a hand dragged the same way; " +
                      $"a hand pushed on through dented it {1000f * propThrough[0]:0.0} mm at most, {propThrough[1]:0} particles over 10 cm, {1000f * propThrough[2]:0.0} mm once through; two balls 12 cm apart, each slot within {100f * propTwo:0.0} cm of its ball; dropped 12 cm onto a stop, the landing rang the green {1000f * propDropRing[0]:0.0} mm and the blue {1000f * propDropRing[1]:0.0} mm, its lowest particle {1000f * propDropLow:0.0} mm off its board");
            SessionState.SetInt(K + "on", 0);
            SessionState.SetBool(K + "prop", false);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        // The demonstration, each shot's length in frames at 90 a second and its caption, which goes
        // to segments.txt for the encode. Four seconds and up, each move done more than once: at two
        // seconds a shot the captions went by too fast to read.
        static readonly (int frames, string say)[] PropShots =
        {
            (360, "The prop at rest"),
            (405, "Carried, shaken side to side"),
            (450, "Lifted, then dropped onto a stop, twice"),
            (405, "Turned quickly back and forth"),
            (450, "A player's hand pressed in slowly: the blue ignores hands"),
            (450, "A hand dragged across the skin"),
            (405, "Quick pokes: a hand on the green, a collider on the blue"),
            (450, "Hand slaps: the green rings"),
            (450, "A MagicaCloth collider pressed in: both dent"),
            (450, "A collider dragged across"),
            (450, "Collider slaps: both ring"),
            (450, "Two colliders on one jelly"),
            (450, "Shaken with a collider resting on it"),
            (450, "A hand pushed right through: the skin holds at 5 cm, then lets go"),
            (270, "Let go"),
        };

        static void OnLatePropFilm()
        {
            var n = Lattice;
            float spacing = SpacingOf(n);
            var all = Object.FindObjectsOfType<Transform>();
            var prop = all.First(t => t.name == "Jelly Prop");
            var regions = new[] { 0, 1 }.Select(r => all.First(t => t.name == $"Jelly Region {r}")).ToArray();
            float hand = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Jelly_Step.mat").GetFloat("_Hand");
            string dir = SessionState.GetString(K + "out", "Jelly");
            if (frame == 0)
            {
                // Asleep shows as stillness: blue on the plate already means colliders only.
                // A ring at its own speed crosses the prop in five frames of the film: slowed to a
                // third and held twice as long, so it can be seen at all, and a longer wave, which its
                // steepness cap lets stand higher.
                foreach (var m in Object.FindObjectsOfType<Renderer>().SelectMany(x => x.sharedMaterials).Where(m => m && m.HasProperty("_SleepTint")))
                {
                    m.SetFloat("_SleepTint", 0f);
                    m.SetVector("_Ring", new Vector4(1.5f, 0.35f, 0.16f, 0.8f));
                }
                propHands = Enumerable.Range(0, 2).Select(i =>
                {
                    var c = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    Object.DestroyImmediate(c.GetComponent<Collider>());
                    c.GetComponent<MeshRenderer>().sharedMaterial = Glass(new Color(1f, 0.8f, 0.6f, 0.45f));
                    return c;
                }).ToArray();
                int from = 0;
                File.WriteAllLines(Path.Combine(dir, "segments.txt"), PropShots.Select(s => { from += s.frames; return $"{from - s.frames}\t{from}\t{s.say}"; }));
            }
            // Which shot, and how far into it.
            int shot = 0, g = frame;
            while (shot < PropShots.Length - 1 && g >= PropShots[shot].frames) g -= PropShots[shot++].frames;
            int len = PropShots[shot].frames;
            float T(int a, int b) => Mathf.Clamp01((g - a) / (float) (b - a));
            // Up over ramp seconds at the shot's start, down over its last.
            float Env(float ramp) => Mathf.Clamp01(g / 90f / ramp) * Mathf.Clamp01((len - g) / 90f / ramp);
            float Wave(float hz) => Mathf.Sin(2f * Mathf.PI * hz * g / 90f);
            // A slap seen coming: drifting in from 25 cm over half a second, then from 12 cm in four
            // frames (3.4 m/s), held, and back out slowly. In three frames from where it waited, the
            // hit was too quick to see and only the way out showed, a slap that left the jelly.
            float Slap(int start) { int p = g - start; return p < -45 ? -0.25f : p < 0 ? Mathf.Lerp(-0.25f, -0.12f, Mathf.SmoothStep(0f, 1f, (p + 45) / 45f)) : p < 4 ? Mathf.Lerp(-0.12f, 0.03f, (p + 1) / 4f) : p < 24 ? 0.03f : p < 70 ? Mathf.Lerp(0.03f, -0.25f, Mathf.SmoothStep(0f, 1f, (p - 24) / 46f)) : -0.25f; }
            float Slaps() => g < 125 ? Slap(45) : g < 260 ? Slap(180) : Slap(315);
            // In slowly over a second, held, out over the last second.
            float Press(float deep) => Mathf.Lerp(-0.05f, deep, Mathf.SmoothStep(0f, 1f, T(20, 110))) - Mathf.Lerp(0f, deep + 0.05f, T(len - 100, len - 20));
            float Drag(float deep) => Mathf.Lerp(-0.05f, deep, Env(0.6f));

            // The prop as a player holding it moves it.
            var at = Vector3.zero;
            float turn = 0f;
            if (shot == 1) at.x = 0.1f * Wave(1.6f) * Env(0.4f);
            if (shot == 2)
            {
                int h = g % 225;
                at.y = h < 45 ? 0.12f * Mathf.SmoothStep(0f, 1f, h / 45f) : h < 80 ? 0.12f : 0.12f * (1f - Mathf.Clamp01((h - 80) / 9f));
            }
            if (shot == 3) turn = 35f * Wave(1.2f) * Env(0.4f);
            if (shot == 12) at.x = 0.06f * Wave(1.6f) * Env(0.6f);
            prop.SetPositionAndRotation(at, Quaternion.Euler(0f, turn, 0f));
            var fronts = regions.Select(t => t.TransformPoint(0f, 0f, (n.z - 1) * spacing)).ToArray();

            // The blue feels colliders only, so a collider pokes it beside the green's hand.
            float beat = g / 90f, poke = -0.04f + 0.075f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 2f * beat)), 2f) * Env(0.3f), across = (int) (beat * 2f) % 2 == 0 ? 0.04f : -0.04f;
            if (shot == 4) { float d = Press(0.04f); PropHands(regions, fronts, hand, (0, d, 0f), (1, d, 0f)); }
            else if (shot == 5) { float d = Drag(0.025f), x = 0.07f * Wave(0.6f); PropHands(regions, fronts, hand, (0, d, x), (1, d, x)); }
            else if (shot == 6) PropHands(regions, fronts, hand, (0, poke, across));
            else if (shot == 7) { float d = Slaps(); PropHands(regions, fronts, hand, (0, d, 0f), (1, d, 0f)); }
            else if (shot == 13) { float d = g < 270 ? Mathf.Lerp(-0.05f, 0.32f, g / 270f) : g < 330 ? 0.32f : Mathf.Lerp(0.32f, -0.05f, (g - 330) / 90f); PropHands(regions, fronts, hand, (0, d, 0f), (1, d, 0f)); }
            else PropHands(regions, fronts, hand);

            if (shot == 8) { float d = Press(0.035f); PropBalls(regions, fronts, true, (0, d, 0f, 0f), (1, d, 0f, 0f)); }
            else if (shot == 9) { float d = Drag(0.025f), x = 0.07f * Wave(0.6f); PropBalls(regions, fronts, true, (0, d, x, 0f), (1, d, x, 0f)); }
            else if (shot == 10) { float d = Slaps(); PropBalls(regions, fronts, true, (0, d, 0f, 0f), (1, d, 0f, 0f)); }
            else if (shot == 11)
            {
                float d1 = Mathf.Lerp(-0.06f, 0.03f, Mathf.SmoothStep(0f, 1f, T(20, 80))) - Mathf.Lerp(0f, 0.09f, T(len - 90, len - 40));
                float d2 = Mathf.Lerp(-0.06f, 0.03f, Mathf.SmoothStep(0f, 1f, T(130, 190))) - Mathf.Lerp(0f, 0.09f, T(len - 70, len - 20));
                PropBalls(regions, fronts, true, (0, d1, -0.06f, 0.03f), (0, d2, 0.06f, -0.03f), (1, d1, -0.06f, 0.03f), (1, d2, 0.06f, -0.03f));
            }
            else if (shot == 12) { float d = Drag(0.02f); PropBalls(regions, fronts, true, (0, d, 0f, 0f), (1, d, 0f, 0f)); }
            else if (shot == 6) PropBalls(regions, fronts, true, (1, poke, across, 0f));
            else if (propBalls != null) PropBalls(regions, fronts, true);

            eye.Render();
            eye2.Render();
            if (frame % 2 == 0)
            {
                var was = RenderTexture.active;
                int w = eye.targetTexture.width, h = eye.targetTexture.height;
                var sheet = new Texture2D(2 * w, h, TextureFormat.RGB24, false);
                RenderTexture.active = eye.targetTexture;
                sheet.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                RenderTexture.active = eye2.targetTexture;
                sheet.ReadPixels(new Rect(0, 0, w, h), w, 0);
                RenderTexture.active = was;
                string frames = Path.Combine(dir, "frames");
                Directory.CreateDirectory(frames);
                File.WriteAllBytes(Path.Combine(frames, $"f_{propFilmed++:00000}.jpg"), sheet.EncodeToJPG(92));
                Object.DestroyImmediate(sheet);
            }
            if (++frame < PropShots.Sum(x => x.frames)) return;
            Debug.Log($"[Jelly] PASS prop film: {propFilmed} shots of {frame} frames");
            SessionState.SetInt(K + "on", 0);
            SessionState.SetBool(K + "prop", false);
            EditorApplication.Exit(0);
        }
#endif

        static void Add(bool withMeter, bool withRider = true, bool offFar = true, int regions = 1)
        {
            var avatar = Selection.activeGameObject;
            var animator = avatar != null ? avatar.GetComponentInChildren<Animator>() : null;
            var chest = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest)
                : null;
            if (chest == null)
            {
                EditorUtility.DisplayDialog("Jelly spike", "Select a humanoid avatar in the scene first.", "OK");
                return;
            }
            // Rebuilt without the preview scene, which would replace the open one, and any earlier one
            // on the avatar taken off, so the avatar always carries the newest.
            var prefab = Make(preview: false, withMeter: withMeter, withRider: withRider, offFar: offFar, sleeps: false, regions: regions);
            if (prefab == null) return;
            int stale = Unworn(avatar.transform);
            var root = avatar.transform;
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            // Sized and placed for the avatar's height, so it hangs clear of the chest on anyone.
            float tall = head != null ? Mathf.Max(Vector3.Dot(head.position - root.position, root.up), 0.3f) : 1.5f;
            var made = (GameObject) PrefabUtility.InstantiatePrefab(prefab, chest);
            made.transform.SetPositionAndRotation(chest.position + root.forward * (0.35f * tall / 1.5f), root.rotation);
            made.transform.localScale = Vector3.one * (tall / 1.5f / Mathf.Max(chest.lossyScale.x, 1e-6f));
            if (regions > 1)
            {
                // Each region onto its own bone, the lattice standing out of it: in front of the
                // chest, behind the hips, above the head. Unpacked, so the regions can leave it.
                PrefabUtility.UnpackPrefabInstance(made, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var bones = new[] { chest, animator.GetBoneTransform(HumanBodyBones.Hips), head };
                var out_ = new[] { root.forward * 0.35f, -root.forward * 0.3f, root.up * 0.3f };
                var turn = new[] { Quaternion.identity, Quaternion.Euler(0f, 180f, 0f), Quaternion.Euler(-90f, 0f, 0f) };
                for (int r = 0; r < regions && r < bones.Length; r++)
                {
                    var region = made.transform.Find($"Jelly Region {r}");
                    if (region == null || bones[r] == null) continue;
                    region.SetParent(bones[r], true);
                    region.SetPositionAndRotation(bones[r].position + out_[r] * (tall / 1.5f), root.rotation * turn[r]);
                }
            }
            Undo.RegisterCreatedObjectUndo(made, "Add the jelly spike");
            Selection.activeGameObject = made;
            Debug.Log($"[Jelly] added under {chest.name}, {0.35f * tall / 1.5f:0.00} m in front, scaled for a {tall:0.00} m head height; {stale} earlier one(s) taken off");
        }

        // Any earlier one on the avatar goes; how many. An instance is named after the prefab's file,
        // Jelly_Spike; older builds left Jelly Spike.
        static int Unworn(Transform avatar)
        {
#if AVATARBRIDGE_YAPS
            // The colliders that pressed instead, back in their pair's cloth: their ends mark them and
            // name the pair, the breasts when they name none, as before any other pair.
            var ends = avatar.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Jelly collider ") && t.parent != null).ToList();
            foreach (var pair in ends.GroupBy(t => t.name.Split(new[] { ' ' }, 5).ElementAtOrDefault(4)))
                PairCollide(avatar, pair.Key != null ? PartOf(pair.Key) : Breast, pair.Select(t => t.parent).Distinct().SelectMany(t => t.GetComponents<Component>()).Where(IsCollider), with: true);
            foreach (var end in ends) Undo.DestroyObjectImmediate(end.gameObject);
#endif
            var stale = avatar.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == Path.GetFileNameWithoutExtension(Prefab) || t.name == "Jelly Spike" || t.name.StartsWith("Jelly Region")).Select(t => t.gameObject).ToList();
            foreach (var old in stale)
                if (old != null) Undo.DestroyObjectImmediate(old);
            return stale.Count;
        }

#if AVATARBRIDGE_YAPS
        static readonly Regex Breast = new Regex("breast|boob", RegexOptions.IgnoreCase);
        static readonly Regex Other = new Regex("^(?!.*(breast|boob))", RegexOptions.IgnoreCase);

        // A pair's bones, cloth and colliders by the name the squash gives it.
        static Regex PartOf(string name) => new Regex(Regex.Escape(name), RegexOptions.IgnoreCase);
        const float BreastCells = 600f;
        // A slap's ring as the draw spreads it: how much of the step's depth, speed (m/s),
        // wavelength (m), fade (s).
        static readonly Vector4 Slapped = new Vector4(1f, 1f, 0.1f, 0.4f);

        [MenuItem("Tools/AvatarBridge Dev/Jelly spike: put the selected avatar's breasts on the jelly")]
        static void BreastsOnSelected()
        {
            string said = BreastsOn(Selection.activeGameObject != null ? Selection.activeGameObject.transform : null);
            Debug.Log("[Jelly] " + said);
            EditorUtility.DisplayDialog("Jelly spike", said, "OK");
        }

        // Its probes physics balls the world's solids push, as the world prop's right one: walls,
        // doors, tables and dropped props, where the client lets an avatar's bodies meet them.
        [MenuItem("Tools/AvatarBridge Dev/Jelly spike: put the selected avatar's breasts on the jelly, feeling the world")]
        static void BreastsWorldOnSelected()
        {
            string said = PairOn(Selection.activeGameObject != null ? Selection.activeGameObject.transform : null, Breast, world: true);
            Debug.Log("[Jelly] " + said);
            EditorUtility.DisplayDialog("Jelly spike", said, "OK");
        }

        [MenuItem("Tools/AvatarBridge Dev/Jelly spike: put the selected avatar's other pair on the jelly")]
        static void OtherOnSelected()
        {
            string said = PairOn(Selection.activeGameObject != null ? Selection.activeGameObject.transform : null, Other);
            Debug.Log("[Jelly] " + said);
            EditorUtility.DisplayDialog("Jelly spike", said, "OK");
        }

        [MenuItem("Tools/AvatarBridge Dev/Jelly spike: take the jelly off the selected avatar")]
        static void BreastsOffSelected()
        {
            string said = BreastsOff(Selection.activeGameObject != null ? Selection.activeGameObject.transform : null);
            Debug.Log("[Jelly] " + said);
            EditorUtility.DisplayDialog("Jelly spike", said, "OK");
        }

        // A point off a box's corner in grid cells across and up, and in metres out.
        static Vector3 Cell(Vector3 p, Vector3 span, int grid) => new Vector3(p.x / span.x * grid, p.y / span.y * grid, p.z);

        static Transform AvatarOf(Transform picked)
        {
            var avatar = picked != null ? picked.GetComponentInParent<CVRAvatar>() : null;
            return avatar != null ? avatar.transform : picked;
        }

        // A lattice a breast, packed into one texture and one pair of passes: each swings on its root
        // as one piece and keeps off the other, two bodies. Each fitted to the skin the squash gives
        // that breast at least half of, a centimetre of room round it, its back face half a
        // centimetre into the chest wall; both the larger's size, about BreastCells cells each, as
        // near cubes as the box allows. The body's squash materials gain the Jelly role, which moves
        // each vertex by the squash's own share of each breast, and the squash leaves the pair to it.
        // The cloth or dynamic bones swinging the pair are switched off, or they and the lattice
        // would both swing it.
        internal static string BreastsOn(Transform picked) => PairOn(picked, Breast);

        // The same for the first of the squash's pairs whose name want matches, hung from the trunk
        // bone its skin's bones hang from.
        internal static string PairOn(Transform picked, Regex want, bool world = false)
        {
            var root = AvatarOf(picked);
            var animator = root != null ? root.GetComponentInChildren<Animator>() : null;
            if (animator == null || !animator.isHuman) return "Select a humanoid avatar in the scene first.";
            var mats = YapsSquash.Materials(root);
            if (mats.Count == 0) return $"{root.name} has no YAPS squash built. The jelly moves each vertex by the squash's own part shares, so build the squash first.";
            // Painted regions first, as the squash takes them; else the pairs found by name or picked.
            var painted = YapsSquash.RegionsOf(root, out string unpainted);
            var body = painted != null ? painted.body : YapsSquash.NamedBody(root);
            List<string> names = null;
            string none = unpainted ?? "no skin carries both parts";
            Vector4[] shares = null;
            if (painted != null)
            {
                none = null;
                names = painted.pairs.Take(2).Select(x => x.name).ToList();
                var p0 = painted.pairs[0];
                var p1 = painted.pairs.Count > 1 ? painted.pairs[1] : null;
                shares = Enumerable.Range(0, painted.vertexCount).Select(v => new Vector4(p0.a[v], p0.b[v], p1 != null ? p1.a[v] : 0, p1 != null ? p1.b[v] : 0) / 255f).ToArray();
            }
            else if (body != null) shares = YapsSquash.NamedShares(root, body, out names, out none);
            int pair = names != null ? names.FindIndex(want.IsMatch) : -1;
            if (shares == null || pair < 0 || pair > 1)
                return $"No such pair in {root.name}'s squash: {none ?? "its pairs are " + string.Join(", ", names)}.";
            var part = PartOf(names[pair]);
            var trunk = new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest }.Select(animator.GetBoneTransform).Where(t => t != null).ToList();
            var chest = Carrier(body, shares, 2 * pair, root);
            while (chest != null && !trunk.Contains(chest)) chest = chest.parent;
            if (chest == null) return $"{root.name}'s {names[pair]} hangs from no bone of the trunk.";

            var baked = new Mesh();
            body.BakeMesh(baked);
            var at = baked.vertices;
            Object.DestroyImmediate(baked);
            var pairSkin = Enumerable.Range(0, Mathf.Min(at.Length, shares.Length))
                .Where(v => shares[v][2 * pair] >= 0.5f || shares[v][2 * pair + 1] >= 0.5f)
                .Select(v => body.transform.position + body.transform.rotation * at[v]).ToArray();
            // A pair behind its bone, as a seat, faces back: the lattice's front is always out of the body.
            bool back = pairSkin.Length > 0 && Vector3.Dot(pairSkin.Aggregate(Vector3.zero, (sum, v) => sum + v) / pairSkin.Length - chest.position, root.forward) < 0f;
            var turn = back ? root.rotation * Quaternion.Euler(0f, 180f, 0f) : root.rotation;
            var undo = Quaternion.Inverse(turn);
            var los = new Vector3[2];
            var his = new Vector3[2];
            for (int side = 0; side < 2; side++)
            {
                var skin = new List<Vector3>();
                for (int v = 0; v < at.Length && v < shares.Length; v++)
                    if (shares[v][2 * pair + side] >= 0.5f)
                        skin.Add(undo * (body.transform.position + body.transform.rotation * at[v] - chest.position));
                if (skin.Count < 20) return $"Only {skin.Count} vertices of {body.name} belong to one of the pair; too few to fit a lattice to.";
                Vector3 lo = skin[0], hi = skin[0];
                foreach (var x in skin)
                {
                    lo = Vector3.Min(lo, x);
                    hi = Vector3.Max(hi, x);
                }
                los[side] = lo - new Vector3(0.01f, 0.01f, 0.005f);
                his[side] = hi + new Vector3(0.01f, 0.01f, 0.01f);
            }
            var size = Vector3.Max(his[0] - los[0], his[1] - los[1]);
            // -jellyCells: another count, for weighing a coarser lattice's look against its cost.
            var given = Environment.GetCommandLineArgs();
            int ci = Array.IndexOf(given, "-jellyCells");
            float cells = ci >= 0 && ci + 1 < given.Length && float.TryParse(given[ci + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float asked) ? asked : BreastCells;
            float cell = Mathf.Pow(size.x * size.y * size.z / cells, 1f / 3f);
            // Each breast's volume, blend shapes as set: from the plane of its base ring, where its own
            // skin ends on the body, out to its frontmost skin, over a grid of a hundredth of the box.
            // Its skin can be a shell, its inner faces turned in under its outer ones (a signed sum
            // over Kar's left a centimetre-thick shell, 1 litre), so only the frontmost surface over
            // each spot counts; from the chest bone's plane it took in the ribs (35 litres).
            var tris = body.sharedMesh.triangles;
            var volume = new float[2];
            for (int side = 0; side < 2; side++)
            {
                int k = 2 * pair + side;
                var lo = los[side];
                var span = his[side] - lo;
                const int Grid = 100;
                var front = new float[Grid * Grid];
                // The ring: edges only one of the part's triangles has.
                var edges = new Dictionary<(int, int), int>();
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    if (a >= shares.Length || b >= shares.Length || c >= shares.Length || shares[a][k] < 0.5f || shares[b][k] < 0.5f || shares[c][k] < 0.5f) continue;
                    foreach (var (e0, e1) in new[] { (a, b), (b, c), (c, a) })
                    {
                        var key = e0 < e1 ? (e0, e1) : (e1, e0);
                        edges[key] = edges.TryGetValue(key, out int seen) ? seen + 1 : 1;
                    }
                }
                var ring = edges.Where(e => e.Value == 1).SelectMany(e => new[] { e.Key.Item1, e.Key.Item2 }).Distinct().ToList();
                float baseZ = ring.Count > 0 ? ring.Average(v => (undo * (body.transform.position + body.transform.rotation * at[v] - chest.position)).z) - lo.z : 0f;
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    if (a >= shares.Length || b >= shares.Length || c >= shares.Length || shares[a][k] < 0.5f || shares[b][k] < 0.5f || shares[c][k] < 0.5f) continue;
                    var A = Cell(undo * (body.transform.position + body.transform.rotation * at[a] - chest.position) - lo, span, Grid);
                    var B = Cell(undo * (body.transform.position + body.transform.rotation * at[b] - chest.position) - lo, span, Grid);
                    var C = Cell(undo * (body.transform.position + body.transform.rotation * at[c] - chest.position) - lo, span, Grid);
                    float area = (B.x - A.x) * (C.y - A.y) - (C.x - A.x) * (B.y - A.y);
                    if (Mathf.Abs(area) < 1e-9f) continue;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.x, Mathf.Min(B.x, C.x)))), x1 = Mathf.Min(Grid - 1, Mathf.CeilToInt(Mathf.Max(A.x, Mathf.Max(B.x, C.x))));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.y, Mathf.Min(B.y, C.y)))), y1 = Mathf.Min(Grid - 1, Mathf.CeilToInt(Mathf.Max(A.y, Mathf.Max(B.y, C.y))));
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            float px = x + 0.5f, py = y + 0.5f;
                            float u = ((B.x - px) * (C.y - py) - (C.x - px) * (B.y - py)) / area, v = ((C.x - px) * (A.y - py) - (A.x - px) * (C.y - py)) / area;
                            if (u < 0f || v < 0f || u + v > 1f) continue;
                            front[y * Grid + x] = Mathf.Max(front[y * Grid + x], u * A.z + v * B.z + (1f - u - v) * C.z);
                        }
                }
                volume[side] = front.Sum(z => Mathf.Max(z - baseZ, 0f)) * (span.x / Grid) * (span.y / Grid);
            }
            float kilos = 950f * 0.5f * (volume[0] + volume[1]);
            // The same tissue at another size: about 2 Hz at half a kilogram, going as one over the
            // size, the cube root of the mass; damped at a quarter of critical; the heavier, the less
            // of a hand's push moves the whole. ponytail: one reference breast, not measured flesh;
            // the inspector's knobs take it from there.
            float hz = 2f * Mathf.Pow(0.5f / Mathf.Max(kilos, 1e-3f), 1f / 3f);
            // For now the pair's own cloth swings the breast bones and each region rides its bone, the
            // jelly the flesh on them: dents, the pair's squash, a viscous fill-back (Joe). Its own mass
            // is then the whole's give to a hand alone, carried on the bone: stiff and fully damped, so
            // a press moves the breast as one piece and it settles back, with no swing of its own on top
            // of the cloth's (Joe: "the whole breast is not moving like one mass, like the squash does
            // it"). The weight is the cloth's: left free at the breast's own 0.75 Hz, the mass trailed
            // every hop and drew the breast out like taffy. -jellyMass puts the swinging mass back in
            // the cloth's place, the cloth off; -jellyNoMass leaves the lattices alone, for telling the
            // mass's doing from theirs.
            bool cloth = !Environment.GetCommandLineArgs().Contains("-jellyMass");
            if (cloth) hz = 2.5f;
            if (Environment.GetCommandLineArgs().Contains("-jellyNoMass")) hz = 0f;
            // How much of a held press moves the whole: a tenth, so a hand dents the flesh and the
            // body's own moves swing the mass, as the soft body does (Joe). From the tissue's stiffness
            // under a palm against the spring the breast hangs on, it was 0.4 to 0.56, and every
            // press shoved the whole breast: "far too volatile".
            // Carried on the cloth, 0.35, held within 0.2 of its length. At 0.6 within 0.35 a squeeze
            // from both sides drove the pair together until their masses met, and the hands' push
            // then took them up toward the chin.
            float give = cloth ? 0.35f : 0.1f;
            // The whole's damping, 0.6 of critical: heavy flesh settles in about a swing, where at a
            // quarter one hit swung it back and forth for seconds. -jellyGive and -jellyZeta set
            // either, for tuning.
            var said = Environment.GetCommandLineArgs();
            int givenAt = Array.IndexOf(said, "-jellyGive"), zetaAt = Array.IndexOf(said, "-jellyZeta");
            if (givenAt >= 0 && givenAt + 1 < said.Length) give = float.Parse(said[givenAt + 1], System.Globalization.CultureInfo.InvariantCulture);
            float zeta = zetaAt >= 0 && zetaAt + 1 < said.Length ? float.Parse(said[zetaAt + 1], System.Globalization.CultureInfo.InvariantCulture) : cloth ? 1f : 0.6f;
            var n = new Vector3Int(Mathf.Max(3, Mathf.CeilToInt(size.x / cell) + 1), Mathf.Max(3, Mathf.CeilToInt(size.y / cell) + 1), Mathf.Max(3, Mathf.CeilToInt(size.z / cell) + 1));
            float spacing = Mathf.Max(size.x / (n.x - 1), size.y / (n.y - 1), size.z / (n.z - 1));

            var prefab = Make(preview: false, withMeter: false, withRider: false, offFar: true, sleeps: false, regions: 2, lattice: n, spacing: spacing, surface: false, lift: 1f);
            if (prefab == null) return "The jelly did not build; the console says why.";
            float radius = (size.x + size.y + size.z) / 6f - 0.01f;
            // How far each breast's own skin reaches toward the other off its centre: where the two
            // actually meet, for the draw's last guard. The box's half width sits past the skin, 7 cm
            // into the other's box on Kar.
            var across = new Vector3(0.5f * (los[1].x + his[1].x - los[0].x - his[0].x), 0.5f * (los[1].y + his[1].y - los[0].y - his[0].y), los[1].z - los[0].z).normalized;
            var skinReach = new float[2];
            for (int side = 0; side < 2; side++)
            {
                var c = new Vector3(0.5f * (los[side].x + his[side].x), 0.5f * (los[side].y + his[side].y), los[side].z + 0.5f * (n.z - 1) * spacing);
                var way = side == 0 ? across : -across;
                for (int v = 0; v < Mathf.Min(at.Length, shares.Length); v++)
                    if (shares[v][2 * pair + side] >= 0.5f)
                        skinReach[side] = Mathf.Max(skinReach[side], Vector3.Dot(undo * (body.transform.position + body.transform.rotation * at[v] - chest.position) - c, way));
            }
            // The squash's stretch in motion, as the conversion set it; none for a pair no cloth of its
            // own swings, as thighs: the bone it rides is the limb, and a seated thigh would read as
            // swung a quarter turn off where it hangs.
            var motion = mats[0].GetVector("_YAPS_SquashMotion");
            bool clothed = PairCloth(root, part).Count > 0;
            if (!clothed) motion.x = 0f;
            // The third pass too, which judges the same reach to wake a rest.
            foreach (var step in new[] { "/Jelly_Step.mat", "/Jelly_Step_2.mat", "/Jelly_Points.mat" })
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(Folder + step);
                if (mat == null) continue;
                mat.SetFloat("_MassHz", hz);
                mat.SetFloat("_MassDamp", 2f * zeta * 2f * Mathf.PI * hz);
                mat.SetFloat("_Give", give);
                mat.SetFloat("_MassCarried", cloth ? 1f : 0f);
                mat.SetFloat("_Limit", cloth ? 0.2f : 0.6f);
                mat.SetFloat("_MassRadius", radius);
                // A little more pillow (Joe): the tissue's pull to its rest 2.4 a second, from 3.
                mat.SetFloat("_Hz", 2.4f);
                mat.SetFloat("_Pair", 1f);
                var apart = new Vector3(0.5f * (los[1].x + his[1].x - los[0].x - his[0].x), 0.5f * (los[1].y + his[1].y - los[0].y - his[0].y), los[1].z - los[0].z);
                mat.SetVector("_PairOffset", apart);
                // Each one's half width across, and any gap between them as built: where the plane
                // they meet at starts to press.
                float wide0 = 0.5f * (his[0].x - los[0].x), wide1 = 0.5f * (his[1].x - los[1].x);
                // -jellyNoPlane leaves the plane out, for telling its doing from the rest's.
                bool plane = !Environment.GetCommandLineArgs().Contains("-jellyNoPlane");
                mat.SetVector("_PairReach", plane ? new Vector4(wide0, wide1, Mathf.Max(apart.magnitude - wide0 - wide1, 0f), 0f) : Vector4.zero);
                mat.SetVector("_PairSkin", plane ? new Vector4(skinReach[0], skinReach[1], 0f, 0f) : Vector4.zero);
                // Each breast's skin on its region's frame, what the own capsules and hands are kept
                // in reach of: the lattice is the larger's box, past the smaller's skin.
                for (int side = 0; side < 2; side++)
                    mat.SetVector($"_JellyBox{side}", new Vector4(0.5f * (his[side].x - los[side].x), 0.5f * (his[side].y - los[side].y), his[side].z - los[side].z, 0f));
                mat.SetVector("_JellyOffsetAt0", Vector4.zero);
                mat.SetVector("_JellyOffsetAt1", Vector4.zero);
                mat.SetFloat("_JellySkinStride", 0f);
                // Flesh barely changes volume but gives easily in shear, so nearly all of each
                // neighbourhood's own squash is kept: half kept, it sprang back like rubber.
                mat.SetFloat("_Squish", 0.9f);
                // About 0.7 of critical at the tissue's own 3 Hz: a dent fills back, no wobble.
                mat.SetFloat("_Viscous", 25f);
                // Solid: the homes take 0.7 of the whole's own squash at constant volume, so a squeeze
                // bulges it the other ways and it comes back when let go.
                mat.SetFloat("_Solid", 0.7f);
                mat.SetVector("_Motion", motion);
                EditorUtility.SetDirty(mat);
            }
            int stale = Unworn(root);
            var made = (GameObject) PrefabUtility.InstantiatePrefab(prefab, chest);
            made.transform.SetPositionAndRotation(chest.position, turn);
            made.transform.localScale = Vector3.one / Mathf.Max(chest.lossyScale.x, 1e-6f);
            for (int side = 0; side < 2; side++)
                made.transform.Find($"Jelly Region {side}").SetPositionAndRotation(chest.position + turn * new Vector3(0.5f * (los[side].x + his[side].x), 0.5f * (los[side].y + his[side].y), los[side].z), turn);
            Undo.RegisterCreatedObjectUndo(made, "Put the breasts on the jelly");
            // Each region rides the bone that carries most of its breast's skin, which the cloth
            // swings: the anchor handed the bone, the region's place on it in the step's materials.
            // The region stays out of the bone's tree, where the cloth would take it for a bone of
            // its own and bend the chain toward it.
            var carriers = new string[2];
            if (cloth)
            {
                PrefabUtility.UnpackPrefabInstance(made, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                var provider = made.GetComponentInChildren<CVRMaterialDataProvider>(true);
                Undo.RecordObject(provider, "Put the breasts on the jelly");
                for (int side = 0; side < 2; side++)
                {
                    var bone = Carrier(body, shares, 2 * pair + side, chest, clothed);
                    if (bone == null) continue;
                    var region = made.transform.Find($"Jelly Region {side}");
                    var placed = bone.InverseTransformPoint(region.position);
                    var turnOn = Quaternion.Inverse(bone.rotation) * region.rotation;
                    float sized = region.lossyScale.x / Mathf.Max(bone.lossyScale.x, 1e-9f);
                    var carried = bone.localToWorldMatrix * Matrix4x4.TRS(placed, turnOn, Vector3.one * sized);
                    float drift = 0f;
                    for (int e = 0; e < 16; e++) drift = Mathf.Max(drift, Mathf.Abs(carried[e] - region.localToWorldMatrix[e]));
                    if (drift > 1e-3f) Debug.LogWarning($"[Jelly] {bone.name} is scaled unevenly: the region's place on it is {drift:0.0000} off");
                    foreach (var mat in provider.materials)
                    {
                        mat.SetVector($"_JellyOffsetAt{side}", new Vector4(placed.x, placed.y, placed.z, sized));
                        mat.SetVector($"_JellyOffsetTurn{side}", new Vector4(turnOn.x, turnOn.y, turnOn.z, turnOn.w));
                        EditorUtility.SetDirty(mat);
                    }
                    provider.transforms[side] = bone;
                    carriers[side] = bone.name;
                }
                // And each region held still where it hangs on the chest, for how far the cloth has
                // swung it: the step stretches it by that, as the squash does.
                var hangs = new Transform[2];
                for (int side = 0; side < 2; side++)
                {
                    var region = made.transform.Find($"Jelly Region {side}");
                    hangs[side] = new GameObject($"Jelly Hang {side}").transform;
                    hangs[side].SetParent(region.parent, false);
                    hangs[side].localPosition = region.localPosition;
                    hangs[side].localRotation = region.localRotation;
                    hangs[side].localScale = region.localScale;
                    Undo.RegisterCreatedObjectUndo(hangs[side].gameObject, "Put the breasts on the jelly");
                }
                provider.transforms = new[] { provider.transforms[0], provider.transforms[1], hangs[0], hangs[1] };
                // -jellyProbes: MagicaCloth's colliders, other players' among them, as presses. The
                // client adds every other player's colliders to every MagicaCloth an avatar carries
                // (CVRDynamicBoneManager.UpdateComponents), hands only unless the viewer chose more,
                // and a hand without one is given a capsule sized from its fingers. So a spring cloth
                // of loose points on each breast's skin at rest is pushed by them, and where a point
                // was pushed to says where a collider is. On the chest, not under the breast's bone:
                // there the breast's own cloth would take each for a bone of its chain.
                // MagicaCloth only: the same hand gets a DynamicBone collider too, so DynamicBone probes
                // saw nothing more, and dented a quarter less for the same press.
                // -jellyWorld: the probes physics balls instead, as the world-feeling menu makes them.
                world = world || Array.IndexOf(given, "-jellyWorld") >= 0;
                bool probing = Array.IndexOf(given, "-jellyProbes") >= 0 || world;
                var spotsOf = new List<Vector3>[2];
                if (probing)
                    for (int side = 0; side < 2; side++)
                    {
                        var region = made.transform.Find($"Jelly Region {side}");
                        var front = Enumerable.Range(0, Mathf.Min(at.Length, shares.Length)).Where(v => shares[v][2 * pair + side] >= 0.5f)
                            .Select(v => body.transform.position + body.transform.rotation * at[v]).ToList();
                        float deepest = front.Max(w => region.InverseTransformPoint(w).z);
                        front = front.Where(w => region.InverseTransformPoint(w).z >= 0.5f * deepest).ToList();
                        // ponytail: twenty-four a breast, the state row's room, spread farthest first from
                        // its frontmost skin. At eight a palm passed between them, at sixteen a 4.5 cm ball
                        // 3 cm in; a finger's collider still can.
                        var spots = new List<Vector3> { front.OrderByDescending(w => region.InverseTransformPoint(w).z).First() };
                        while (spots.Count < ProbesPer && spots.Count < front.Count)
                            spots.Add(front.OrderByDescending(w => spots.Min(q => (q - w).sqrMagnitude)).First());
                        spotsOf[side] = spots;
                    }
                AddSkin(made, provider, hangs, probing ? spotsOf : null, 0.5f * (n.z - 1) * spacing, "Put the breasts on the jelly", world ? new[] { true, true } : null);
            }

            // The wearer's own arms, hands and thighs press the breasts as other players' hands do:
            // eight capsules bone to bone, the bones after the four anchors in the provider's
            // transforms, each as thick as the body's skin round it (the median distance of the skin
            // its bones carry). Other players' hands come from the client's arrays; the wearer's arm
            // went through the breast in the film, as the arrays hold hands alone.
            var ownBones = new[]
            {
                HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleDistal, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.RightMiddleDistal, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
            }.Select(b => animator.GetBoneTransform(b)).ToArray();
            var ownRadii = new float[8];
            var weights = body.sharedMesh.boneWeights;
            for (int c = 0; c < 8; c++)
            {
                int s0 = (c >> 2) * 6 + ((c & 3) < 3 ? (c & 3) : 4);
                Transform from = ownBones[s0], to = ownBones[s0 + 1];
                if (from == null || to == null) continue;
                // The skin whose strongest bone hangs from this one and not from the next (twist bones
                // and, for the hand, its fingers), round the middle of the bone alone: taken to the
                // ends it took in the shoulder and the hip, an upper arm 98 mm thick, a thigh 184.
                var near = new List<float>();
                for (int v = 0; v < Mathf.Min(at.Length, weights.Length); v++)
                {
                    var bone = body.bones[weights[v].boneIndex0];
                    if (bone == null || !bone.IsChildOf(from) || bone.IsChildOf(to)) continue;
                    var w = body.transform.position + body.transform.rotation * at[v];
                    var seg = to.position - from.position;
                    float along = Vector3.Dot(w - from.position, seg) / Mathf.Max(seg.sqrMagnitude, 1e-9f);
                    if (along > 0.2f && along < 0.8f) near.Add(Vector3.Distance(w, from.position + seg * along));
                }
                // ponytail: a low share, as a capsule is round and a limb is not: the median left Kar's
                // hand 56 mm thick and a thigh 236, pushing skin before they reached it. A limb a
                // little thin sinks in a touch first, which reads as soft.
                if (near.Count >= 8) ownRadii[c] = near.OrderBy(d => d).ElementAt(near.Count * 3 / 10);
            }
            // None reaching a particle of the pair's lattices as built, on the side it pushes them from:
            // there it would hold the tissue dented for good, as the thighs held a seat 18.7 mm in. Each
            // is thinned to clear them by half a centimetre, so it presses only past where it rests.
            var thinned = new List<string>();
            for (int c = 0; c < 8; c++)
            {
                int s0 = (c >> 2) * 6 + ((c & 3) < 3 ? (c & 3) : 4);
                if (!(ownRadii[c] > 0f)) continue;
                Vector3 a = ownBones[s0].position, s = ownBones[s0 + 1].position - a;
                float was = ownRadii[c];
                for (int side = 0; side < 2; side++)
                {
                    var region = made.transform.Find($"Jelly Region {side}");
                    var centre = region.TransformPoint(0f, 0f, 0.5f * (n.z - 1) * spacing);
                    for (int x = 0; x < n.x; x++)
                        for (int y = 0; y < n.y; y++)
                            for (int z = 0; z < n.z; z++)
                            {
                                var p = region.TransformPoint(new Vector3(x - 0.5f * (n.x - 1), y - 0.5f * (n.y - 1), z) * spacing);
                                var foot = a + s * Mathf.Clamp01(Vector3.Dot(p - a, s) / Mathf.Max(s.sqrMagnitude, 1e-12f));
                                if (Vector3.Dot(p - foot, centre - (a + 0.5f * s)) >= 0f) ownRadii[c] = Mathf.Min(ownRadii[c], Mathf.Max((p - foot).magnitude - 0.005f, 0f));
                            }
                }
                if (ownRadii[c] < was) thinned.Add($"{ownBones[s0].name} {1000f * was:0} to {1000f * ownRadii[c]:0} mm");
            }
            // The avatar's own MagicaCloth and DynamicBone colliders that its breasts' cloth collides
            // with press the breasts as its limbs do, and come out of that cloth's lists, so they dent
            // the skin and no longer move the bones (Joe: "deform the mesh, not move the bone"). Each
            // is handed as two ends, a child at either end of its capsule, so it rides its bone as the
            // limbs do. Not one within a cell of the breasts' skin as built: it moves the lattice
            // round that skin at rest and would dent it for good (a sphere in the cleavage left the
            // tissue 41 mm off its rest), so it stays in the cloth, moving the bones as before.
            var pressing = new List<(Component c, Vector3 a, Vector3 b, float r)>();
            var touching = new List<string>();
            foreach (var c in PairColliders(root, part).OrderBy(c => (c.transform.position - chest.position).sqrMagnitude))
            {
                if (!Capsule(c, out var ca, out var cb, out float cr)) continue;
                if (pairSkin.Any(p => Seg(p, ca, cb) < cr + spacing)) touching.Add(c.name);
                // ponytail: eight, the nearest the chest first; more need anchors past the 32 handed.
                else if (pressing.Count < 8) pressing.Add((c, ca, cb, cr));
            }
            PairCollide(root, part, pressing.Select(p => p.c), with: false);
            var colRadii = new float[8];
            var colEnds = new List<Transform>();
            for (int i = 0; i < pressing.Count; i++)
            {
                foreach (var (end, place) in new[] { ("a", pressing[i].a), ("b", pressing[i].b) })
                {
                    var t = new GameObject($"Jelly collider {i} {end} {names[pair]}").transform;
                    Undo.RegisterCreatedObjectUndo(t.gameObject, "Put the breasts on the jelly");
                    t.SetParent(pressing[i].c.transform, false);
                    t.position = place;
                    colEnds.Add(t);
                }
                colRadii[i] = pressing[i].r;
            }
            string collided = (pressing.Count > 0 ? $"{pressing.Count} of its colliders pressing instead of moving the bones ({string.Join(", ", pressing.Select(p => p.c.name))})" : "none of its colliders pressing") +
                              (touching.Count > 0 ? $", {touching.Count} left in the cloth as touching the breasts at rest ({string.Join(", ", touching)})" : "");
            var anchors = made.GetComponentsInChildren<CVRMaterialDataProvider>(true).First(q => q.transforms != null && q.transforms.Length > 0);
            Undo.RecordObject(anchors, "Put the breasts on the jelly");
            var handed = anchors.transforms.ToList();
            // Without the cloth the hang slots are never read; the regions fill them.
            while (handed.Count < 4) handed.Add(handed[handed.Count - 2]);
            handed.AddRange(ownBones.Select(b => b != null ? b : chest));
            handed.AddRange(colEnds);
            anchors.transforms = handed.ToArray();
            foreach (var mat in anchors.materials)
            {
                mat.SetVector("_JellyOwn0", new Vector4(ownRadii[0], ownRadii[1], ownRadii[2], ownRadii[3]));
                mat.SetVector("_JellyOwn1", new Vector4(ownRadii[4], ownRadii[5], ownRadii[6], ownRadii[7]));
                mat.SetVector("_JellyCol0", new Vector4(colRadii[0], colRadii[1], colRadii[2], colRadii[3]));
                mat.SetVector("_JellyCol1", new Vector4(colRadii[4], colRadii[5], colRadii[6], colRadii[7]));
                EditorUtility.SetDirty(mat);
            }

            var state = AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + "/Jelly_State.renderTexture");
            // Each part names its region, one past its index.
            var parts = Vector4.zero;
            parts[2 * pair] = 1f;
            parts[2 * pair + 1] = 2f;
            var report = new BridgeReport();
            var left = new List<string>();
            int patched = 0;
            foreach (var m in mats)
            {
                var shader = YapsShaderPatcher.WithRole(m, YapsShaderPatcher.Roles.Jelly, report);
                if (shader == null || ShaderUtil.ShaderHasError(shader))
                {
                    left.Add(m.name);
                    continue;
                }
                Undo.RecordObject(m, "Put the breasts on the jelly");
                YapsShaderPatcher.Wear(m, shader);
                m.SetTexture("_YAPS_JellyState", state);
                m.SetVector("_YAPS_JellyLattice", new Vector4(n.x, n.y, n.z, spacing));
                m.SetVector("_YAPS_JellySlap", Slapped);
                m.SetVector("_YAPS_JellyParts", parts);
                EditorUtility.SetDirty(m);
                patched++;
            }
            AssetDatabase.SaveAssets();
            // On with the cloth, off with the mass. !cloth here switched the cloth off while it was
            // meant to carry the regions, and the pair hung still on the chest.
            var off = PairPhysics(root, part, on: cloth);
            return $"On {root.name}: {names[pair]} on two lattices of {n.x}x{n.y}x{n.z} ({n.x * n.y * n.z} particles each, {100f * spacing:0.0} cm apart, " +
                   $"{100f * (n.x - 1) * spacing:0} x {100f * (n.y - 1) * spacing:0} x {100f * (n.z - 1) * spacing:0} cm) under {chest.name}; " +
                   (cloth ? $"{1000f * volume[0]:0.0} and {1000f * volume[1]:0.0} litres, riding {carriers[0] ?? "nothing"} and {carriers[1] ?? "nothing"}, which the cloth swings, a hand moving the whole by {give:0.00} of its press; "
                       : $"{1000f * volume[0]:0.0} and {1000f * volume[1]:0.0} litres, {kilos:0.0} kg each, so it swings at {hz:0.00} Hz and a hand moves the whole by {give:0.00} of its push; ") +
                   $"{patched} of {mats.Count} squash materials read it{(left.Count > 0 ? " (left as they were: " + string.Join(", ", left) + ")" : "")}; " +
                   (cloth ? (off.Count > 0 ? "switched back on: " + string.Join(", ", off) : "its cloth left on")
                       : off.Count > 0 ? "switched off: " + string.Join(", ", off) : "no cloth or dynamic bones found on the pair, so if anything else swings it, switch that off") +
                   $"; {collided}; {(thinned.Count > 0 ? "thinned to clear it as built: " + string.Join(", ", thinned) : "none of the wearer's limbs reaching it as built")}; {stale} earlier jelly taken off. Upload as it is.";
        }

        internal static string BreastsOff(Transform picked)
        {
            var root = AvatarOf(picked);
            if (root == null) return "Select the avatar first.";
            int cleared = 0;
            foreach (var m in YapsSquash.Materials(root))
            {
                if (!m.HasProperty("_YAPS_JellyLattice") || m.GetVector("_YAPS_JellyLattice") == Vector4.zero) continue;
                Undo.RecordObject(m, "Take the breasts off the jelly");
                m.SetVector("_YAPS_JellyLattice", Vector4.zero);
                m.SetVector("_YAPS_JellyParts", Vector4.zero);
                EditorUtility.SetDirty(m);
                cleared++;
            }
            AssetDatabase.SaveAssets();
            var body = YapsSquash.NamedBody(root);
            List<string> names = null;
            if (body != null) YapsSquash.NamedShares(root, body, out names, out _);
            var on = (names ?? new List<string>()).SelectMany(x => PairPhysics(root, PartOf(x), on: true)).ToList();
            int stale = Unworn(root);
            return $"Off {root.name}: {cleared} squash materials back on the squash alone, {stale} jelly taken off, " +
                   (on.Count > 0 ? "switched back on: " + string.Join(", ", on) : "no pair's cloth or dynamic bones were off");
        }

        // The cloth and dynamic bones whose roots are on the pair's bone chains, switched on or off;
        // what was switched.
        static List<string> PairPhysics(Transform root, Regex part, bool on)
        {
            var said = new List<string>();
            foreach (var b in PairCloth(root, part))
            {
                if (b.enabled == on) continue;
                Undo.RecordObject(b, on ? "Take the breasts off the jelly" : "Put the breasts on the jelly");
                b.enabled = on;
                said.Add($"{b.GetType().Name} on {b.name}");
            }
            return said;
        }

        // The cloth and dynamic bones whose roots are on the pair's bone chains, found as the squash
        // finds the chains, by name and side.
        static List<Behaviour> PairCloth(Transform root, Regex part)
        {
            var side = new Regex(@"left|right|[._ ]?[lr](?=$|[._ ])", RegexOptions.IgnoreCase);
            bool Sided(Transform t) => t != null && part.IsMatch(t.name) && side.IsMatch(t.name);
            var chains = new HashSet<Transform>(root.GetComponentsInChildren<Transform>(true)
                .Where(t => Sided(t) && !Sided(t.parent)).SelectMany(t => t.GetComponentsInChildren<Transform>(true)));
            var found = new List<Behaviour>();
            foreach (var b in root.GetComponentsInChildren<Behaviour>(true))
            {
                string type = b != null ? b.GetType().Name : "";
                if (type != "MagicaCloth" && type != "DynamicBone") continue;
                var so = new SerializedObject(b);
                var roots = new List<Transform>();
                foreach (var path in new[] { "serializeData.rootBones", "m_Roots" })
                {
                    var list = so.FindProperty(path);
                    if (list != null && list.isArray)
                        for (int i = 0; i < list.arraySize; i++) roots.Add(list.GetArrayElementAtIndex(i).objectReferenceValue as Transform);
                }
                if (so.FindProperty("m_Root")?.objectReferenceValue is Transform one) roots.Add(one);
                if (roots.Any(t => t != null && chains.Contains(t))) found.Add(b);
            }
            return found;
        }

        static bool IsCollider(Component c) => c != null && (c.GetType().Name == "MagicaCapsuleCollider" || c.GetType().Name == "MagicaSphereCollider" || c.GetType().Name == "DynamicBoneCollider");

        // The colliders the pair's cloth and dynamic bones collide with, read off their lists.
        static List<Component> PairColliders(Transform root, Regex part)
        {
            var found = new List<Component>();
            foreach (var b in PairCloth(root, part))
            {
                var list = new SerializedObject(b).FindProperty(b.GetType().Name == "MagicaCloth" ? "serializeData.colliderCollisionConstraint.colliderList" : "m_Colliders");
                if (list == null || !list.isArray) continue;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue is Component c && IsCollider(c) && !found.Contains(c)) found.Add(c);
            }
            return found;
        }

        // The pair's cloth and dynamic bones made to collide with these or not, each with its own
        // kind. ponytail: put back into every cloth of the pair of its kind, not only the ones it was in.
        static void PairCollide(Transform root, Regex part, IEnumerable<Component> colliders, bool with)
        {
            var set = colliders.ToList();
            if (set.Count == 0) return;
            foreach (var b in PairCloth(root, part))
            {
                bool magica = b.GetType().Name == "MagicaCloth";
                var so = new SerializedObject(b);
                var list = so.FindProperty(magica ? "serializeData.colliderCollisionConstraint.colliderList" : "m_Colliders");
                if (list == null || !list.isArray) continue;
                var have = Enumerable.Range(0, list.arraySize).Select(i => list.GetArrayElementAtIndex(i).objectReferenceValue as Component).ToList();
                var want = with ? have.Concat(set.Where(c => !have.Contains(c) && c.GetType().Name.StartsWith("Magica") == magica)).ToList() : have.Where(c => !set.Contains(c)).ToList();
                if (want.Count == have.Count) continue;
                list.arraySize = want.Count;
                for (int i = 0; i < want.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = want[i];
                so.ApplyModifiedProperties();
            }
        }

        // A collider as a capsule in the world, its ends and radius: MagicaCloth's spheres and
        // capsules, DynamicBone's outside ones. Planes and inside bounds are not a shape to press with.
        static bool Capsule(Component c, out Vector3 a, out Vector3 b, out float r)
        {
            a = b = Vector3.zero;
            r = 0f;
#if AVATARBRIDGE_MAGICA
            if (c is MagicaCloth2.MagicaSphereCollider sphere)
            {
                a = b = sphere.transform.TransformPoint(sphere.center);
                r = sphere.GetSize().x * Mathf.Abs(sphere.transform.lossyScale.x);
                return r > 0f;
            }
            if (c is MagicaCloth2.MagicaCapsuleCollider cap)
            {
                // As MagicaCloth draws it: centred, or from the centre back along its direction.
                var size = cap.GetSize();
                var dir = cap.GetLocalDir();
                float from = cap.alignedOnCenter ? 0.5f * size.z : 0f, to = cap.alignedOnCenter ? 0.5f * size.z : size.z - size.x;
                a = cap.transform.TransformPoint(cap.center + dir * Mathf.Max(from - size.x, 0f));
                b = cap.transform.TransformPoint(cap.center - dir * Mathf.Max(to - size.y, 0f));
                // ponytail: a cone as a cylinder of its larger end.
                r = Mathf.Max(size.x, size.y) * Mathf.Abs(cap.transform.lossyScale.x);
                return r > 0f;
            }
#endif
#if AVATARBRIDGE_DYNBONE
            if (c is DynamicBoneCollider db && db.m_Bound != DynamicBoneColliderBase.Bound.Inside)
            {
                var dir = db.m_Direction == DynamicBoneColliderBase.Direction.X ? Vector3.right : db.m_Direction == DynamicBoneColliderBase.Direction.Y ? Vector3.up : Vector3.forward;
                float half = Mathf.Max(0.5f * db.m_Height - db.m_Radius, 0f);
                a = db.transform.TransformPoint(db.m_Center - dir * half);
                b = db.transform.TransformPoint(db.m_Center + dir * half);
                r = db.m_Radius * Mathf.Abs(db.transform.lossyScale.x);
                return r > 0f;
            }
#endif
            return false;
        }

        static float Seg(Vector3 p, Vector3 a, Vector3 b)
        {
            var s = b - a;
            return Vector3.Distance(p, a + s * Mathf.Clamp01(Vector3.Dot(p - a, s) / Mathf.Max(s.sqrMagnitude, 1e-12f)));
        }

        // The bone carrying most of part k's skin, below the chest: the one its cloth swings.
        static Transform Carrier(SkinnedMeshRenderer body, Vector4[] shares, int k, Transform chest, bool clothed = true)
        {
            var weights = body.sharedMesh.boneWeights;
            var bones = body.bones;
            var sum = new float[bones.Length];
            for (int v = 0; v < weights.Length && v < shares.Length; v++)
            {
                if (shares[v][k] < 0.5f) continue;
                var w = weights[v];
                sum[w.boneIndex0] += w.weight0;
                sum[w.boneIndex1] += w.weight1;
                sum[w.boneIndex2] += w.weight2;
                sum[w.boneIndex3] += w.weight3;
            }
            // The most weighted one hanging from another of them, as the squash's follow: the cloth
            // holds a chain's root still, and the regions on it never swung. Else the most weighted.
            float total = sum.Sum();
            var carrying = new HashSet<Transform>(Enumerable.Range(0, bones.Length)
                .Where(b => bones[b] != null && sum[b] >= 0.03f * total && !chest.IsChildOf(bones[b])).Select(b => bones[b]));
            Transform best = null, hung = null;
            float most = 0f, mostHung = 0f;
            for (int b = 0; b < bones.Length; b++)
            {
                if (bones[b] == null || !carrying.Contains(bones[b])) continue;
                if (sum[b] > most) { most = sum[b]; best = bones[b]; }
                if (sum[b] > mostHung && carrying.Contains(bones[b].parent)) { mostHung = sum[b]; hung = bones[b]; }
            }
            // With no cloth there is no still root to pass over: a thigh rides its leg, not the shin
            // hanging from it.
            return clothed ? hung ?? best : best;
        }
#endif

        // Region r's probes (spots[r], none for null) and the skinned helper the step reads the regions'
        // frames and the probes through: each region carried by provider.transforms[r], held still by
        // hangs[r], depth its lattice's middle off its anchor.
        static void AddSkin(GameObject made, CVRMaterialDataProvider provider, Transform[] hangs, List<Vector3>[] spots, float depth, string undo, bool[] solid = null)
        {
            var probes = new List<Transform>[] { new List<Transform>(), new List<Transform>() };
            // On a solid side, a ball the world's colliders push, sprung to its rest on the hang: the
            // step reads it as it reads a cloth probe, through a tip on the skin it carries. The ball
            // sits ProbeReach less its radius out, so a surface is felt from ProbeReach off the skin
            // as a collider is by the cloth's; one that size on the skin would shove its neighbours.
            // Frictionless, so a surface slides past rather than dragging it along.
            PhysicMaterial slick = null;
            Transform Solid(int side, int k, Vector3 w, Vector3 outward)
            {
                const float radius = 0.01f;
                slick = slick ?? Save(new PhysicMaterial("Jelly probe") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicMaterialCombine.Minimum, bounciness = 0f }, "/Jelly_Probe.physicMaterial");
                var rest = new GameObject($"Jelly Probe Rest {side} {k}");
                rest.transform.SetParent(hangs[side], false);
                rest.transform.position = w + outward * (ProbeReach - radius);
                rest.AddComponent<Rigidbody>().isKinematic = true;
                var ball = new GameObject($"Jelly Probe Ball {side} {k}");
                ball.transform.SetParent(hangs[side], false);
                ball.transform.position = rest.transform.position;
                var body = ball.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.mass = 0.05f;
                body.constraints = RigidbodyConstraints.FreezeRotation;
                // Drawn where its speed says it is now: interpolated, it drew a physics step behind a
                // rest that moves every frame, and a body turning read as pushed.
                body.interpolation = RigidbodyInterpolation.Extrapolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var sphere = ball.AddComponent<SphereCollider>();
                sphere.radius = radius;
                sphere.sharedMaterial = slick;
                // 32 Hz and critical: at 7 Hz a body turning lagged each ball a centimetre behind its
                // rest, read as a push, and the breasts swung half again as far turning.
                var joint = ball.AddComponent<ConfigurableJoint>();
                joint.connectedBody = rest.GetComponent<Rigidbody>();
                joint.autoConfigureConnectedAnchor = false;
                joint.anchor = Vector3.zero;
                joint.connectedAnchor = Vector3.zero;
                joint.xDrive = joint.yDrive = joint.zDrive = new JointDrive { positionSpring = 2000f, positionDamper = 20f, maximumForce = float.MaxValue };
                var tip = new GameObject($"Jelly Probe Tip {side} {k}").transform;
                tip.SetParent(ball.transform, false);
                tip.position = w;
                return tip;
            }
            // Each probe a bone lying 10 cm along the skin to its spot, which a collider tips in: a
            // loose spring point takes every collider as soft and gave back 85% of each push past its
            // radius, 12 mm read for 30 in. Long enough to swing in by its reach and a deep press.
            List<Transform> Bones()
            {
                var bases = new List<Transform>();
                for (int side = 0; side < 2; side++)
                {
                    var region = made.transform.Find($"Jelly Region {side}");
                    var mid = region.TransformPoint(0f, 0f, depth);
                    for (int k = 0; k < spots[side].Count; k++)
                    {
                        var w = spots[side][k];
                        var outward = (w - mid).normalized;
                        if (solid != null && solid[side])
                        {
                            probes[side].Add(Solid(side, k, w, outward));
                            continue;
                        }
                        var along = Vector3.Cross(outward, region.right);
                        if (along.sqrMagnitude < 0.01f) along = Vector3.Cross(outward, region.up);
                        along.Normalize();
                        var b = new GameObject($"Jelly Probe Base {side} {k}").transform;
                        b.SetParent(hangs[side], false);
                        b.position = w - along * 0.1f;
                        var tip = new GameObject($"Jelly Probe Tip {side} {k}").transform;
                        tip.SetParent(b, false);
                        tip.position = w;
                        bases.Add(b);
                        probes[side].Add(tip);
                    }
                }
                return bases;
            }
#if AVATARBRIDGE_MAGICA
            var bases = spots != null ? Bones() : new List<Transform>();
            if (bases.Count > 0)
            {
                var net = new GameObject("Jelly Probes");
                net.transform.SetParent(made.transform, false);
                Undo.RegisterCreatedObjectUndo(net, undo);
                net.SetActive(false);
                var sd = net.AddComponent<MagicaCloth2.MagicaCloth>().SerializeData;
                sd.clothType = MagicaCloth2.ClothProcess.ClothType.BoneCloth;
                sd.rootBones = bases;
                // A collider is felt from ProbeReach off the skin: a palm between two probes reaches
                // one, and the push crosses the skin at speed, as a palm crosses the box.
                sd.radius = new MagicaCloth2.CurveSerializeData(ProbeReach);
                sd.gravity = 0f;
                // Back within a few frames once a collider lets go, and settled, not bouncing: at the
                // default damping it still read 2.6 mm a moment after, past the 2 mm a press takes,
                // and at 0.5 the 10 cm bone swung back out to 5 mm.
                sd.damping = new MagicaCloth2.CurveSerializeData(0.8f);
                sd.angleRestorationConstraint.useAngleRestoration = true;
                sd.angleRestorationConstraint.stiffness = new MagicaCloth2.CurveSerializeData(1f);
                // Every viewer's copy, seen or not: it has no renderer of its own to be culled by.
                sd.cullingSettings.cameraCullingMode = MagicaCloth2.CullingSettings.CameraCullingMode.Off;
                // Carried rigidly by the chest, or a hop would read as a press.
                sd.inertiaConstraint.worldInertia = 0f;
                sd.inertiaConstraint.localInertia = 0f;
                // Each where its collider puts it: the default 4 m/s cap would hold back a faster slap's.
                sd.inertiaConstraint.particleSpeedLimit = new MagicaCloth2.CheckSliderSerializeData(false, 4f);
                net.SetActive(true);
            }
#endif
            // The bones as the cloth swings them reach the step only through skinning, as the
            // squash's slots do: a helper mesh skinned four points to each region's bone and four to
            // its still copy. Its triangles have no area, so it draws nothing. "[FPR]" keeps the
            // client's first-person copy off it, which stops skinning the wearer's renderers
            // offscreen and froze the squash's.
            var skin = new GameObject("Jelly skin [FPR]").AddComponent<SkinnedMeshRenderer>();
            skin.transform.SetParent(made.transform, false);
            Undo.RegisterCreatedObjectUndo(skin.gameObject, undo);
            var skinBones = new List<Transform>();
            var points = new List<Vector3>();
            var skinWeights = new List<BoneWeight>();
            for (int side = 0; side < 2; side++)
                foreach (var on in new[] { provider.transforms[side], hangs[side] })
                {
                    int b = skinBones.IndexOf(on);
                    if (b < 0) { b = skinBones.Count; skinBones.Add(on); }
                    foreach (var p in new[] { Vector3.zero, Vector3.right * 0.1f, Vector3.up * 0.1f, Vector3.forward * 0.1f })
                    {
                        points.Add(skin.transform.InverseTransformPoint(hangs[side].TransformPoint(p)));
                        skinWeights.Add(new BoneWeight { boneIndex0 = b, weight0 = 1f });
                    }
                }
            // Then each probe twice, on itself and at its rest on the hang, from 16: a region's
            // pairs after the last's.
            int probed = Mathf.Min(probes[0].Count, probes[1].Count);
            for (int side = 0; side < 2; side++)
                for (int k = 0; k < probed; k++)
                    foreach (var on in new[] { probes[side][k], hangs[side] })
                    {
                        int b = skinBones.IndexOf(on);
                        if (b < 0) { b = skinBones.Count; skinBones.Add(on); }
                        points.Add(skin.transform.InverseTransformPoint(probes[side][k].position));
                        skinWeights.Add(new BoneWeight { boneIndex0 = b, weight0 = 1f });
                    }
            // Kept in place when it exists: remade at the same path, scene copies read it Missing.
            var skinMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Jelly_Skin.asset");
            if (skinMesh == null)
            {
                skinMesh = new Mesh();
                AssetDatabase.CreateAsset(skinMesh, Folder + "/Jelly_Skin.asset");
            }
            skinMesh.Clear();
            skinMesh.name = "Jelly skin";
            skinMesh.vertices = points.ToArray();
            skinMesh.boneWeights = skinWeights.ToArray();
            skinMesh.bindposes = skinBones.Select(b => b.worldToLocalMatrix * skin.transform.localToWorldMatrix).ToArray();
            skinMesh.triangles = Enumerable.Range(0, 3 * points.Count).Select(i => i / 3).ToArray();
            EditorUtility.SetDirty(skinMesh);
            skin.sharedMesh = skinMesh;
            skin.bones = skinBones.ToArray();
            skin.sharedMaterial = Save(new Material(Shader.Find("Standard")), "/Jelly_Skin.mat");
            skin.updateWhenOffscreen = true;
            skin.shadowCastingMode = ShadowCastingMode.Off;
            skin.receiveShadows = false;
            // A world probe's face reaches half again as far as the probes sit apart, so a pane's
            // faces overlap and leave no gap for the skin to show through.
            float patch = 0f;
            for (int ps = 0; ps < 2; ps++)
                if (solid != null && solid[ps] && spots != null && spots[ps].Count > 1)
                    patch = Mathf.Max(patch, 1.5f * spots[ps].Average(pa => spots[ps].Where(pq => pq != pa).Min(pq => (pa - pq).magnitude)));
            foreach (var mat in provider.materials)
            {
                mat.SetFloat("_JellySkinStride", skinMesh.GetVertexBufferStride(0));
                mat.SetFloat("_JellyProbes", probed);
                mat.SetFloat("_JellyProbeReach", ProbeReach);
                mat.SetFloat("_JellySolid", solid == null ? 0 : (solid[0] ? 1 : 0) + (solid[1] ? 2 : 0));
                mat.SetFloat("_JellyProbePatch", patch);
                EditorUtility.SetDirty(mat);
            }
            // The client disposes the skinned buffer it fetched right after binding it, so the
            // squash read zeros; the previous-buffer mode binds a copy the provider owns.
            var skinned = provider.gameObject.AddComponent<CVRMaterialDataProvider>();
            Undo.RegisterCreatedObjectUndo(skinned, undo);
            skinned.materials = provider.materials.ToArray();
            skinned.dataMode = CVRMaterialDataProvider.DataMode.SkinnedMeshRendererPreviousVertexBuffer;
            skinned.smr = skin;
            skinned.shaderPropertyName = "_JellySkin";
            // After the anchors, before the passes.
            var renders = provider.GetComponent<CVRRenderController>();
            Undo.RecordObject(renders, undo);
            renders.cvrRenderComponents.Insert(renders.cvrRenderComponents.IndexOf(provider) + 1, skinned);
        }

        static GameObject Make(bool preview = true, bool withMeter = true, bool withRider = true, bool offFar = true, bool sleeps = true, int regions = 1,
            Vector3Int? lattice = null, float spacing = 0f, bool surface = true, float lift = 0f)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            File.WriteAllText(Folder + "/JellyCommon.cginc", CommonInclude);
            File.WriteAllText(Folder + "/JellyStep.shader", StepShader);
            File.WriteAllText(Folder + "/JellyDraw.shader", DrawShader);
            File.WriteAllText(Folder + "/JellyClock.shader", ClockShader);
            File.WriteAllText(Folder + "/JellyMeter.shader", MeterShader);
            File.WriteAllText(Folder + "/README.md", Readme);
            AssetDatabase.Refresh();
            // The near pass is part of the first step now; what it left goes to the bin, out of the way.
            foreach (var gone in new[] { "/JellyNear.shader", "/JellyPoints.shader", "/Jelly_Near.renderTexture", "/Jelly_Near.mat" })
                if (AssetDatabase.LoadMainAssetAtPath(Folder + gone) != null) AssetDatabase.MoveAssetToTrash(Folder + gone);
            var step = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/JellyStep.shader");
            var draw = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/JellyDraw.shader");
            var clock = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/JellyClock.shader");
            var meter = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/JellyMeter.shader");
            var shaders = new[] { step, draw, clock, meter };
            if (shaders.Any(x => x == null || ShaderUtil.ShaderHasError(x)))
            {
                Debug.Log("[Jelly] BUILD ABORT: a shader did not import or has errors: " + string.Join(" | ", shaders.Where(x => x != null)
                    .SelectMany(x => ShaderUtil.GetShaderMessages(x)).Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    .Select(m => $"{m.message} line {m.line}")));
                return null;
            }

            var n = lattice ?? Lattice;
            if (spacing <= 0f) spacing = SpacingOf(n);
            var state = Save(State(n, regions), "/Jelly_State.renderTexture");
            var twin = Save(State(n, regions), "/Jelly_State_B.renderTexture");
            var stepMat = Save(StepMaterial(step, n, 0, spacing), "/Jelly_Step.mat");
            var stepMat2 = Save(StepMaterial(step, n, 1, spacing), "/Jelly_Step_2.mat");
            // Only a prop sleeps: worn, the avatar's own sway keeps it awake, and the test alone costs
            // a slow GPU a few percent. Set on the saved materials, which a rebuild keeps.
            foreach (var mat in new[] { stepMat, stepMat2 })
            {
                if (sleeps) mat.EnableKeyword("JELLY_SLEEP");
                else mat.DisableKeyword("JELLY_SLEEP");
                // The rider's texels are read only by the parser's pass and the sleep: worn without
                // either, stepping them cost a particle's whole step on two texels a region.
                mat.SetFloat("_Rider", withRider || sleeps ? 1f : 0f);
                mat.SetFloat("_Lift", lift);
                mat.SetFloat("_Regions", regions);
                EditorUtility.SetDirty(mat);
            }
            var drawMat = new Material(draw);
            drawMat.SetTexture("_State", state);
            drawMat.SetVector("_Lattice", new Vector4(n.x, n.y, n.z, spacing));
            drawMat = Save(drawMat, "/Jelly_Draw.mat");
            var root = new GameObject("Jelly Spike");
            // Each region an anchor of its own. Packed, they sit side by side to be moved; alone, the
            // root is the anchor.
            var anchors = new List<Transform> { root.transform };
            if (regions > 1)
            {
                anchors.Clear();
                for (int r = 0; r < regions; r++)
                {
                    var anchor = new GameObject($"Jelly Region {r}").transform;
                    anchor.SetParent(root.transform, false);
                    anchor.localPosition = new Vector3((r - 0.5f * (regions - 1)) * 0.3f, 0f, 0f);
                    anchors.Add(anchor);
                }
            }
            var tissue = new GameObject("Tissue");
            tissue.transform.SetParent(root.transform, false);
            var provider = tissue.AddComponent<CVRMaterialDataProvider>();
            provider.dataMode = CVRMaterialDataProvider.DataMode.TransformLocalToWorld;
            provider.transforms = anchors.ToArray();
            provider.shaderPropertyName = "_JellyAnchor";
            var controller = tissue.AddComponent<CVRRenderController>();
            // The anchor first, then the passes, in the order the client runs them.
            controller.cvrRenderComponents.Add(provider);
            var steps = new List<CVRBlitter>();
            var fed = new List<Material> { stepMat, stepMat2 };
            for (int i = 0; i < Passes; i++)
            {
                var blitter = tissue.AddComponent<CVRBlitter>();
                steps.Add(blitter);
                // Into the twin and back: a texture in and out the same would make the client
                // render into a temporary and copy it back, every pass.
                blitter.originTexture = i == 0 ? state : twin;
                blitter.destinationTexture = i == 0 ? twin : state;
                blitter.blitMaterial = i == 0 ? stepMat : stepMat2;
                blitter.destinationTextures = new RenderTexture[0];
                controller.cvrRenderComponents.Add(blitter);
            }
            // Last, the rider's texels and the far switch out of the stepped state, for the parser.
            RenderTexture where = null;
            if (withRider || offFar)
            {
                where = Save(Strip(3), "/Jelly_Points.renderTexture");
                var pointsMat = StepMaterial(step, n, 2, spacing);
                pointsMat.SetFloat("_Regions", regions);
                pointsMat = Save(pointsMat, "/Jelly_Points.mat");
                fed.Add(pointsMat);
                var pointer = tissue.AddComponent<CVRBlitter>();
                pointer.originTexture = state;
                pointer.destinationTexture = where;
                pointer.blitMaterial = pointsMat;
                pointer.destinationTextures = new RenderTexture[0];
                controller.cvrRenderComponents.Add(pointer);
            }

            if (withMeter)
            {
                // The cost meter: its clock, and the load, which steps a lattice nobody draws in the odd
                // phases and leaves it be in the even ones.
                var load = Save(State(n), "/Jelly_Load.renderTexture");
                var loadMat = StepMaterial(step, n, 0, spacing);
                loadMat.SetFloat("_Stress", 1f);
                // The step alone, without the first pass's look for hands.
                loadMat.DisableKeyword("JELLY_SCAN");
                loadMat = Save(loadMat, "/Jelly_Load.mat");
                fed.Add(loadMat);
                var ticks = Save(new RenderTexture(4, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false,
                }, "/Jelly_Clock.renderTexture");
                var clockMat = Save(new Material(clock), "/Jelly_Clock.mat");
                var meterMat = new Material(meter);
                meterMat.SetTexture("_Clock", ticks);
                meterMat.SetFloat("_Load", Load);
                meterMat = Save(meterMat, "/Jelly_Meter.mat");
                var ticker = tissue.AddComponent<CVRBlitter>();
                ticker.originTexture = ticks;
                ticker.destinationTexture = ticks;
                ticker.blitMaterial = clockMat;
                ticker.destinationTextures = new RenderTexture[0];
                controller.cvrRenderComponents.Add(ticker);
                var loader = new GameObject("Load");
                loader.transform.SetParent(root.transform, false);
                var loadController = loader.AddComponent<CVRRenderController>();
                for (int i = 0; i < Load; i++)
                {
                    var blitter = loader.AddComponent<CVRBlitter>();
                    blitter.originTexture = load;
                    blitter.destinationTexture = load;
                    blitter.blitMaterial = loadMat;
                    blitter.destinationTextures = new RenderTexture[0];
                    loadController.cvrRenderComponents.Add(blitter);
                }
                var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
                face.name = "Meter";
                Object.DestroyImmediate(face.GetComponent<Collider>());
                face.transform.SetParent(root.transform, false);
                face.transform.localPosition = new Vector3(0f, 0.15f, 0.1f);
                var faceRenderer = face.GetComponent<MeshRenderer>();
                faceRenderer.sharedMaterial = meterMat;
                faceRenderer.shadowCastingMode = ShadowCastingMode.Off;
                faceRenderer.receiveShadows = false;
            }

            provider.materials = fed.ToArray();

            if (where != null)
            {
                var parser = tissue.AddComponent<CVRTexturePropertyParser>();
                parser.textureType = CVRTexturePropertyParser.TextureType.LocalTexture;
                parser.texture = where;
                if (withRider)
                {
                    // The rider, at rest on the front centre, and a plain cube on it.
                    var plain = Save(new Material(Shader.Find("Standard")) { color = new Color(1f, 0.85f, 0.1f) }, "/Jelly_Rider.mat");
                    var rider = new GameObject("Rider");
                    rider.transform.SetParent(anchors[0], false);
                    rider.transform.localPosition = new Vector3(0f, 0f, (n.z - 1) * SpacingOf(n));
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = "Plain cube";
                    Object.DestroyImmediate(cube.GetComponent<Collider>());
                    cube.transform.SetParent(rider.transform, false);
                    cube.transform.localPosition = new Vector3(0f, 0f, 0.025f);
                    cube.transform.localScale = Vector3.one * 0.05f;
                    cube.GetComponent<MeshRenderer>().sharedMaterial = plain;
                    // Vector3 properties are type 4, an element each; the texels hold each value between
                    // its task's bounds, so a clamped remap and an unclamped one read the same.
                    for (int i = 0; i < 3; i++)
                        parser.tasks.Add(Task(rider, 0, i, "localPosition", -0.5f, 0.5f));
                    for (int i = 0; i < 3; i++)
                        parser.tasks.Add(Task(rider, 1, i, "localEulerAngles", -180f, 180f));
                }
                // The far switch: the client binds any public property, so the third texel turns both
                // steps' blitters off (a bool, set at 0.5 and up) far from the viewer, and the pass
                // that writes it, which stays on, turns them back on.
                if (offFar)
                    foreach (var blitter in steps)
                        parser.tasks.Add(new CVRTexturePropertyParserTask
                        {
                            x = 2,
                            y = 0,
                            channel = CVRTexturePropertyParserTask.Channel.r,
                            minValue = 0f,
                            maxValue = 1f,
                            target = tissue,
                            component = blitter,
                            propertyName = "enabled",
                            typeIndex = 2,
                        });
            }
            // A surface a region, on its anchor, so past the far switch each draws its own rest shape.
            for (int r = 0; r < regions && surface; r++)
            {
                var body = new GameObject(r == 0 ? "Surface" : $"Surface {r}");
                body.transform.SetParent(anchors[r], false);
                body.AddComponent<MeshFilter>().sharedMesh = Save(Surface(n, r), r == 0 ? "/Jelly_Surface.asset" : $"/Jelly_Surface_{r}.asset");
                var renderer = body.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = drawMat;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            Object.DestroyImmediate(root);
            if (prefab == null)
            {
                Debug.Log("[Jelly] BUILD FAILED: the prefab did not save");
                return null;
            }

            if (!preview)
            {
                AssetDatabase.SaveAssets();
                return prefab;
            }
            // A scene to press Play in: the spike on a stand that swings, and a camera on it.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var stand = new GameObject("Swing");
            SceneManager_Move(stand, scene);
            stand.transform.position = new Vector3(0f, 1.3f, 0f);
            var placed = (GameObject) PrefabUtility.InstantiatePrefab(prefab, scene);
            placed.transform.SetParent(stand.transform, false);
            placed.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            var look = new GameObject("Main Camera") { tag = "MainCamera" };
            SceneManager_Move(look, scene);
            look.transform.position = new Vector3(0.55f, 1.45f, 0.75f);
            look.transform.LookAt(new Vector3(0f, 1.25f, 0.4f));
            var cam = look.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.2f, 0.2f, 0.24f);
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 40f;
            EditorSceneManager.SaveScene(scene, Scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Jelly] BUILD ok: {Prefab}, {Scene}");
            return prefab;
        }

        static void SceneManager_Move(GameObject go, UnityEngine.SceneManagement.Scene scene) =>
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);

        // The row after the lattice's: the hands in reach, the rider and the far switch. Its rows
        // padded so each region is a whole number of eight: a GPU runs pixels in blocks of rows,
        // and a block with the side row and particles in it ran both their work one after the other.
        static int Side(Vector3Int n) => ((n.x * n.y * n.z + Row - 1) / Row + 8) / 8 * 8 - 1;

        // An arm's two bones turned so the wrist reaches goal, bending about the plane it was in.
        static void TwoBone(Transform shoulder, Transform elbow, Transform wrist, Vector3 goal, Vector3 hint)
        {
            float a = Vector3.Distance(shoulder.position, elbow.position), b = Vector3.Distance(elbow.position, wrist.position);
            float c = Mathf.Clamp(Vector3.Distance(shoulder.position, goal), Mathf.Abs(a - b) + 1e-4f, a + b - 1e-4f);
            var upper = elbow.position - shoulder.position;
            var lower = wrist.position - elbow.position;
            var axis = Vector3.Cross(upper, lower);
            if (axis.sqrMagnitude < 1e-10f) axis = Vector3.Cross(upper, hint);
            float now = Vector3.Angle(-upper, lower);
            float want = Mathf.Acos(Mathf.Clamp((a * a + b * b - c * c) / (2f * a * b), -1f, 1f)) * Mathf.Rad2Deg;
            elbow.rotation = Quaternion.AngleAxis(now - want, axis.normalized) * elbow.rotation;
            shoulder.rotation = Quaternion.FromToRotation(wrist.position - shoulder.position, goal - shoulder.position) * shoulder.rotation;
        }

        static RenderTexture State(Vector3Int n, int regions = 1)
        {
            // Float, so positions keep their digits; point and no mips, so a texel is only ever itself.
            return new RenderTexture(2 * Row, (Side(n) + 1) * regions, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
            };
        }

        static CVRTexturePropertyParserTask Task(GameObject target, int x, int element, string property, float min, float max) =>
            new CVRTexturePropertyParserTask
            {
                x = x,
                y = 0,
                channel = (CVRTexturePropertyParserTask.Channel) element,
                minValue = min,
                maxValue = max,
                target = target,
                component = target.transform,
                propertyName = property,
                typeIndex = 4,
                targetIndex = element,
            };

        // A row of texels for a pass's few answers.
        static RenderTexture Strip(int width) => new RenderTexture(width, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false,
        };

        static Material StepMaterial(Shader step, Vector3Int n, int pass, float spacing = 0f)
        {
            var m = new Material(step);
            m.SetVector("_Lattice", new Vector4(n.x, n.y, n.z, spacing > 0f ? spacing : SpacingOf(n)));
            m.SetFloat("_Pass", pass);
            // The first pass looks for the hands in reach, the third for whatever wakes a rest.
            if (pass == 0) m.EnableKeyword("JELLY_SCAN");
            if (pass == 2) m.EnableKeyword("JELLY_POINTS");
            return m;
        }

        // One lattice's textures and passes, wired as the prefab wires them: with the test cube's
        // third pass, or as worn, without.
        sealed class Rig
        {
            public RenderTexture State, Twin, Where;
            public Material[] Mats;
            readonly int side;
            readonly bool rider;

            public Rig(Vector3Int n, bool rider = true, int regions = 1)
            {
                this.rider = rider;
                State = JellySpikeBuilder.State(n, regions);
                Twin = JellySpikeBuilder.State(n, regions);
                Where = Strip(3);
                foreach (var t in new[] { State, Twin, Where }) t.Create();
                side = Side(n);
                var step = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/JellyStep.shader");
                Mats = new[] { StepMaterial(step, n, 0), StepMaterial(step, n, 1), StepMaterial(step, n, 2) };
                // As worn, so a row times the work; the sleep's own rows compile it in.
                Sleep(0f, false);
            }

            // A sleep time, 0 for never; compiled in or out, as a prop or worn.
            public void Sleep(float after, bool compiled)
            {
                foreach (var mat in Mats)
                {
                    mat.SetFloat("_Sleep", after);
                    if (compiled) mat.EnableKeyword("JELLY_SLEEP");
                    else mat.DisableKeyword("JELLY_SLEEP");
                }
            }

            public bool Asleep() => Read(State)[side * 2 * Row + 18].y > 0.5f;

            public void Anchor(params Matrix4x4[] m)
            {
                foreach (var mat in Mats) mat.SetMatrixArray("_JellyAnchor", m);
            }

            public void Dt(float dt)
            {
                foreach (var mat in Mats) mat.SetFloat("_FixedDt", dt);
            }

            // The passes in the order the client runs them.
            public void Passes()
            {
                Graphics.Blit(State, Twin, Mats[0]);
                Graphics.Blit(Twin, State, Mats[1]);
                if (rider) Third();
            }

            // The test cube's pass alone, all a copy runs once the parser has switched its steps off.
            public void Third() => Graphics.Blit(State, Where, Mats[2]);

            // A frame as a viewer pays for it: the passes, then the parser's readback, which asks for
            // RGBA32 and so converts on the GPU.
            public void Frame()
            {
                Passes();
                AsyncGPUReadback.Request(Where, 0, TextureFormat.RGBA32);
            }

            // Each kept hand's second texel is its tip, w 1.
            public int Kept()
            {
                var s = Read(State);
                return Enumerable.Range(0, 8).Count(e => s[side * 2 * Row + 2 * e + 1].w == 1f);
            }

            // The far switch, as the steps left it and as the third pass hands it to the parser.
            public bool Far() => Read(State)[side * 2 * Row + 18].x > 0.5f;

            public bool Run() => Read(Where)[2].x > 0.5f;

            public void Release()
            {
                foreach (var t in new[] { State, Twin, Where }) t.Release();
            }
        }

        // The lattice's six faces as a grid of quads, each vertex naming its particle's texel.
        // As fine as the fine lattice whatever the lattice: a coarse one's surface puts f vertices
        // to a particle's spacing, each blended from the eight particles round it. Each vertex names
        // where on the lattice it sits, in particles, and its region's row of switches.
        static Mesh Surface(Vector3Int n, int region = 0)
        {
            int rows = Side(n) + 1, sideRow = region * rows + rows - 1;
            int f = Mathf.Max(1, Mathf.RoundToInt(15f / (n.z - 1)));
            var g = (n - Vector3Int.one) * f + Vector3Int.one;
            float spacing = SpacingOf(n);
            var at = new List<Vector3>();
            var uv = new List<Vector2>();
            var uv2 = new List<Vector2>();
            var tris = new List<int>();
            var index = new Dictionary<int, int>();
            int Vertex(int i, int j, int k)
            {
                int p = (k * g.y + j) * g.x + i;
                if (index.TryGetValue(p, out int v)) return v;
                index[p] = at.Count;
                var c = new Vector3(i, j, k) / f;
                at.Add(new Vector3((c.x - 0.5f * (n.x - 1)) * spacing, (c.y - 0.5f * (n.y - 1)) * spacing, c.z * spacing));
                uv.Add(new Vector2(c.x, c.y));
                uv2.Add(new Vector2(c.z, sideRow + 0.5f));
                return at.Count - 1;
            }
            void Face(int axis, int fixedAt)
            {
                int a = (axis + 1) % 3, b = (axis + 2) % 3;
                int na = g[a], nb = g[b];
                for (int u = 0; u < na - 1; u++)
                    for (int w = 0; w < nb - 1; w++)
                    {
                        int Corner(int du, int dw)
                        {
                            var l = new int[3];
                            l[axis] = fixedAt;
                            l[a] = u + du;
                            l[b] = w + dw;
                            return Vertex(l[0], l[1], l[2]);
                        }
                        int v00 = Corner(0, 0), v10 = Corner(1, 0), v11 = Corner(1, 1), v01 = Corner(0, 1);
                        tris.AddRange(new[] { v00, v10, v11, v00, v11, v01 });
                    }
            }
            for (int axis = 0; axis < 3; axis++)
            {
                Face(axis, 0);
                Face(axis, g[axis] - 1);
            }
            var mesh = new Mesh { name = "Jelly surface" };
            mesh.SetVertices(at);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, uv2);
            mesh.SetTriangles(tris, 0);
            // The shader puts every vertex where the tissue is, so the renderer's box has to hold
            // wherever that goes.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return mesh;
        }

        // Kept where it already exists, its values brought up to date in place: an asset deleted and
        // made again at the same path in one session leaves the scene's jelly pointing at a dead
        // object (Missing), and copying a whole new object over it does the same.
        static T Save<T>(T asset, string file) where T : Object
        {
            var old = AssetDatabase.LoadAssetAtPath<T>(Folder + file);
            if (old == null)
            {
                AssetDatabase.DeleteAsset(Folder + file);
                AssetDatabase.CreateAsset(asset, Folder + file);
                return AssetDatabase.LoadAssetAtPath<T>(Folder + file);
            }
            if (old is Material m && asset is Material fresh)
            {
                m.shader = fresh.shader;
                m.CopyPropertiesFromMaterial(fresh);
                EditorUtility.SetDirty(m);
            }
            if (old is RenderTexture rt && asset is RenderTexture want && (rt.width != want.width || rt.height != want.height || rt.format != want.format))
            {
                rt.Release();
                rt.width = want.width;
                rt.height = want.height;
                rt.format = want.format;
                EditorUtility.SetDirty(rt);
            }
            // A surface rebuilt in place, so instances keep pointing at it: its uvs name the region.
            if (old is Mesh keep && asset is Mesh made)
            {
                keep.Clear();
                keep.vertices = made.vertices;
                keep.uv = made.uv;
                keep.uv2 = made.uv2;
                keep.triangles = made.triangles;
                keep.bounds = made.bounds;
                EditorUtility.SetDirty(keep);
            }
            return old;
        }

        static string Out()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-jellyOut");
            var dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : "Jelly";
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ---------------------------------------------------------------- bench

        public static void Bench()
        {
            if (Make() == null)
            {
                EditorApplication.Exit(1);
                return;
            }
            string dir = Out();
            var copy = new Material(Shader.Find("Hidden/BlitCopy"));
            var lines = new List<string> { $"[Jelly] bench on {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}" };
            bool ok = true;
            var still = Matrix4x4.TRS(new Vector3(0f, 1.3f, 0.3f), Quaternion.identity, Vector3.one);

            // Every row timed five times, the rows interleaved, so a clock ramp or a hitch widens one
            // row's range instead of moving its median. The head is the viewer's, metres in front of
            // the tissue, 0 for none.
            const int Reps = 5;
            var configs = new List<(Vector3Int n, int players, int inReach, float head)>();
            foreach (var n in new[] { Size, Mid, Coarse, new Vector3Int(32, 24, 16) })
                foreach (var (players, inReach, head) in new[] { (0, 0, 0f), (64, 0, 0f), (64, 8, 0f), (64, 8, 1f), (64, 0, 20f) })
                    configs.Add((n, players, inReach, head));
            var rigs = configs.Select(c => new Rig(c.n)).ToList();
            foreach (var r in rigs) r.Anchor(still);
            var frames = new Dictionary<(int, int), List<double>>();
            var submits = new Dictionary<(int, int), List<double>>();
            // K copies in a row, each reading the last one's output, as a jelly's passes do: the
            // slope is what a pass costs before it does any work.
            int[] chain = { 1, 2, 4, 8 };
            var copies = chain.ToDictionary(k => k, k => new List<double>());
            var kept = new int[configs.Count];
            for (int rep = 0; rep < Reps; rep++)
            {
                for (int c = 0; c < configs.Count; c++)
                {
                    Players(configs[c].players, configs[c].inReach, configs[c].head);
                    foreach (int fps in new[] { 90, 60 })
                    {
                        rigs[c].Dt(1f / fps);
                        double frame = Timed(rigs[c].Passes, rigs[c].State, 600, out double cpu);
                        Add(frames, (c, fps), frame);
                        Add(submits, (c, fps), cpu);
                    }
                    kept[c] = rigs[c].Kept();
                }
                var a = rigs[0].State;
                var b = rigs[0].Twin;
                foreach (int k in chain)
                    copies[k].Add(Timed(() =>
                    {
                        for (int i = 0; i < k; i++) Graphics.Blit(i % 2 == 0 ? a : b, i % 2 == 0 ? b : a, copy);
                    }, a, 600, out _));
            }
            // The parser's readback on its own: thousands of them in flight inside the rows above
            // would time Unity draining its queue, not the GPU.
            var bare = new List<double>();
            var read = new List<double>();
            Players(0);
            rigs[0].Dt(1f / 90f);
            for (int rep = 0; rep < Reps; rep++)
            {
                bare.Add(Timed(rigs[0].Passes, rigs[0].State, 300, out _));
                read.Add(Timed(rigs[0].Frame, rigs[0].State, 300, out _));
            }
            for (int c = 0; c < configs.Count; c++)
            {
                var (n, players, inReach, head) = configs[c];
                ok &= kept[c] == inReach;
                // Past the far switch's 6 m: the switch on, the parser told to stop the steps.
                Health(Read(rigs[c].State), n, still, out _, out float front);
                bool far = rigs[c].Far(), run = rigs[c].Run();
                ok &= far == head > 6.5f && run == !far;
                var at = new[] { 90, 60 }.Select(fps => $"{Spread(frames[(c, fps)])} ms a frame at {fps} fps ({Median(submits[(c, fps)]):0.000} to submit)");
                lines.Add($"[Jelly] {n.x * n.y * n.z} particles, {players} players, {inReach} hands in reach{(head > 0 ? $", the viewer {head:0} m off" : "")}, " +
                          $"kept {kept[c]}, front {100f * front:0.0} cm off rigid{(far ? ", far" : "")}: {string.Join(", ", at)}");
                rigs[c].Release();
            }

            // As worn, without the test cube, and what a far copy still costs once the parser has
            // switched its steps off; and one pass stepping in place, the client's route for a texture
            // in and out the same (a temporary, then a copy back), against the two into the twin.
            // That pass takes a whole step at a 60th, which costs what one at a 90th does.
            var worn = new Rig(Size, rider: false);
            var test = new Rig(Size);
            var far20 = new Rig(Size);
            var whole = StepMaterial(AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/JellyStep.shader"), Size, 0);
            foreach (var r in new[] { worn, test, far20 })
            {
                r.Anchor(still);
                r.Dt(1f / 90f);
            }
            whole.SetMatrixArray("_JellyAnchor", new[] { still });
            whole.SetFloat("_FixedDt", 1f / 60f);
            void InPlace()
            {
                var temporary = RenderTexture.GetTemporary(worn.State.descriptor);
                Graphics.Blit(worn.State, temporary, whole);
                Graphics.CopyTexture(temporary, worn.State);
                RenderTexture.ReleaseTemporary(temporary);
            }
            var builds = new Dictionary<string, List<double>>();
            for (int rep = 0; rep < Reps; rep++)
            {
                Players(64, 8);
                Add(builds, "worn", Timed(worn.Passes, worn.State, 600, out _));
                Add(builds, "test", Timed(test.Passes, test.State, 600, out _));
                Add(builds, "one", Timed(InPlace, worn.State, 600, out _));
                Players(64, 0, 20f);
                Add(builds, "far", Timed(far20.Passes, far20.State, 600, out _));
                Add(builds, "off", Timed(far20.Third, far20.State, 600, out _));
            }
            lines.Add($"[Jelly] 3072 particles at 90 fps, 64 players, 8 hands in reach: worn, two passes, {Spread(builds["worn"])} ms a frame; " +
                      $"with the third pass, the test cube's or the far switch's, {Spread(builds["test"])}; one pass in place instead of two {Spread(builds["one"])}");
            lines.Add($"[Jelly] the viewer 20 m off: {Spread(builds["far"])} ms a frame with the steps running at rest, " +
                      $"{Spread(builds["off"])} once the parser has switched them off");
            foreach (var r in new[] { worn, test, far20 }) r.Release();

            // Asleep: four seconds still beside a twin that never sleeps, the same sag on both; what a
            // frame costs asleep; then woken by the anchor moving a millimetre, and again by a hand.
            var sleeper = new Rig(Size, rider: false);
            var awake = new Rig(Size, rider: false);
            var prop = new Rig(Size, rider: false);
            foreach (var r in new[] { sleeper, awake, prop })
            {
                r.Anchor(still);
                r.Dt(1f / 90f);
            }
            sleeper.Sleep(0.5f, true);
            prop.Sleep(0f, true);
            Players(0);
            int fellAt = -1;
            for (int i = 0; i < 360; i++)
            {
                sleeper.Passes();
                awake.Passes();
                if (fellAt < 0 && sleeper.Asleep()) fellAt = i;
            }
            Health(Read(sleeper.State), Size, still, out _, out float sagAsleep);
            Health(Read(awake.State), Size, still, out _, out float sagAwake);
            var naps = new Dictionary<string, List<double>>();
            for (int rep = 0; rep < Reps; rep++)
            {
                foreach (int players in new[] { 0, 64 })
                {
                    Players(players, 0);
                    Add(naps, $"worn {players}", Timed(awake.Passes, awake.State, 600, out _));
                    Add(naps, $"prop {players}", Timed(prop.Passes, prop.State, 600, out _));
                    Add(naps, $"asleep {players}", Timed(sleeper.Passes, sleeper.State, 600, out _));
                }
            }
            bool slept = fellAt >= 0 && sleeper.Asleep();
            var nudged = Matrix4x4.TRS(new Vector3(0.001f, 1.3f, 0.3f), Quaternion.identity, Vector3.one);
            sleeper.Anchor(nudged);
            sleeper.Passes();
            bool wokeMoved = !sleeper.Asleep();
            sleeper.Passes();
            bool followed = Health(Read(sleeper.State), Size, nudged, out _, out _);
            Players(0);
            for (int i = 0; i < 360; i++) sleeper.Passes();
            bool sleptAgain = sleeper.Asleep();
            Players(1, 2);
            sleeper.Passes();
            bool wokeHand = !sleeper.Asleep();
            // Frames as uneven as a game's: the step must hold, since where the tissue rests moves
            // with it, or it never sleeps. The real frame time, not the bench's fixed one.
            var uneven = new Rig(Lattice, rider: false);
            uneven.Anchor(still);
            uneven.Sleep(0.5f, true);
            uneven.Dt(0f);
            Players(0);
            var jitter = new System.Random(7);
            for (int i = 0; i < 920; i++)
            {
                Shader.SetGlobalVector("unity_DeltaTime", new Vector4(1f / 230f * (1f + 0.4f * (float) (jitter.NextDouble() * 2 - 1)), 0f, 0f, 0f));
                uneven.Passes();
            }
            bool sleptUneven = uneven.Asleep();
            uneven.Release();
            ok &= slept && Mathf.Abs(sagAsleep - sagAwake) < 5e-4f && wokeMoved && followed && sleptAgain && wokeHand && sleptUneven;
            lines.Add($"[Jelly] asleep after {(fellAt + 1) / 90f:0.00} s still, front {100f * sagAsleep:0.00} cm off rigid against {100f * sagAwake:0.00} awake; " +
                      $"a frame at 90 fps, alone and with 64 players: asleep {Spread(naps["asleep 0"])} and {Spread(naps["asleep 64"])} ms, " +
                      $"awake as a prop {Spread(naps["prop 0"])} and {Spread(naps["prop 64"])}, as worn {Spread(naps["worn 0"])} and {Spread(naps["worn 64"])}; " +
                      $"woken by the anchor moving 1 mm: {wokeMoved}, back face on it the frame after: {followed}; asleep again: {sleptAgain}, woken by a hand: {wokeHand}; " +
                      $"asleep at 230 fps with frame times 40% uneven: {sleptUneven}");
            foreach (var r in new[] { sleeper, awake, prop }) r.Release();

            // Packed: three regions in one texture and one pair of passes, against three apart, the
            // hands in reach of the first alone. From fresh, each packed region must step exactly as
            // its lone twin does.
            var places = Enumerable.Range(0, 3).Select(r => Matrix4x4.TRS(new Vector3(0.6f * r, 1.3f, 0.3f), Quaternion.Euler(0f, 0f, 10f * r), Vector3.one)).ToArray();
            var packed = new Rig(Size, rider: false, regions: 3);
            var apart = places.Select(_ => new Rig(Size, rider: false)).ToArray();
            packed.Anchor(places);
            packed.Dt(1f / 90f);
            for (int r = 0; r < 3; r++)
            {
                apart[r].Anchor(places[r]);
                apart[r].Dt(1f / 90f);
            }
            Players(64, 8);
            for (int i = 0; i < 200; i++)
            {
                packed.Passes();
                foreach (var r in apart) r.Passes();
            }
            int block = (Side(Size) + 1) * 2 * Row, lattice = Side(Size) * 2 * Row;
            var packedState = Read(packed.State);
            float apartBy = 0f;
            var keptBy = new int[3];
            for (int r = 0; r < 3; r++)
            {
                var alone = Read(apart[r].State);
                for (int i = 0; i < lattice; i++)
                    apartBy = Mathf.Max(apartBy, ((Vector3) packedState[r * block + i] - (Vector3) alone[i]).magnitude);
                keptBy[r] = Enumerable.Range(0, 8).Count(e => packedState[r * block + lattice + 2 * e + 1].w == 1f);
            }
            var packs = new Dictionary<string, List<double>>();
            for (int rep = 0; rep < Reps; rep++)
                foreach (var (players, inReach) in new[] { (0, 0), (64, 0), (64, 8) })
                {
                    Players(players, inReach);
                    bool flip = (rep & 1) == 1;
                    void Packed() => Add(packs, $"packed {players} {inReach}", Timed(packed.Passes, packed.State, 600, out _));
                    void Apart() => Add(packs, $"apart {players} {inReach}", Timed(() => { foreach (var r in apart) r.Passes(); }, apart[2].State, 600, out _));
                    if (flip) { Apart(); Packed(); }
                    else { Packed(); Apart(); }
                }
            ok &= apartBy == 0f && keptBy[0] == 8 && keptBy[1] == 0 && keptBy[2] == 0;
            lines.Add($"[Jelly] three regions packed against apart, 3072 particles each, 90 fps: alone {Spread(packs["packed 0 0"])} ms against {Spread(packs["apart 0 0"])}, " +
                      $"64 players {Spread(packs["packed 64 0"])} against {Spread(packs["apart 64 0"])}, 8 hands on the first {Spread(packs["packed 64 8"])} against {Spread(packs["apart 64 8"])}; " +
                      $"packed and apart {1000f * apartBy:0.000} mm apart after 200 frames, hands kept {string.Join("/", keptBy)}");
            packed.Release();
            foreach (var r in apart) r.Release();
            lines.Add("[Jelly] chained copies, a frame: " + string.Join(", ", chain.Select(k => $"{k}: {Spread(copies[k])}")) +
                      $"; so a pass costs {(Median(copies[8]) - Median(copies[1])) / 7:0.0000} ms before it works");
            lines.Add($"[Jelly] the parser's readback, at 90 fps: {Spread(bare)} ms a frame without, {Spread(read)} with");

            // Behaviour, on one pair's lattice: settle under gravity, then the anchor turns 60 degrees
            // in a sixth of a second and holds. At 90 fps, where the first pass predicts and the
            // second fits, with pictures; then at 60, where each pass takes a step.
            Players(0);
            var strip = new List<Texture2D>();
            int[] shotsAt = { 0, 6, 12, 20, 30, 45, 70, 100 };
            Vector3 hang = new Vector3(0f, 1.3f, 0.3f);
            // The coarse lattices at 90 fps beside the fine one, each with its own pictures.
            foreach (var (n, fps) in new[] { (Size, 90), (Size, 60), (Mid, 90), (Coarse, 90) })
            {
                var rig = new Rig(n);
                rig.Dt(1f / fps);
                var view = Viewer(rig.State, n, out var shots);
                var trace = new List<string>();
                for (int f = -2 * fps; f <= 120 * fps / 90; f++)
                {
                    float turn = f < 0 ? 0f : 60f * Mathf.SmoothStep(0f, 1f, Mathf.Min(f / (fps / 6f), 1f));
                    var anchor = Matrix4x4.TRS(hang, Quaternion.Euler(0f, turn, 0f), Vector3.one);
                    rig.Anchor(anchor);
                    view.transform.SetPositionAndRotation(hang, Quaternion.Euler(0f, turn, 0f));
                    rig.Passes();
                    if (f == -1 || f >= 0 && (f % 5 == 0 || fps == 90 && shotsAt.Contains(f)))
                    {
                        var health = Health(Read(rig.State), n, anchor, out string said, out _);
                        ok &= health;
                        if (f == -1) lines.Add($"[Jelly] {n.x}x{n.y}x{n.z} at {fps} fps, settled after 2 s: {said}");
                        else trace.Add($"{f}: {said}");
                    }
                    if (fps == 90 && f >= 0 && shotsAt.Contains(f)) strip.Add(Shot(shots));
                }
                lines.Add($"[Jelly] {n.x}x{n.y}x{n.z} at {fps} fps, turned 60 degrees in a sixth of a second then held, frame: " + string.Join(" | ", trace));
                if (fps == 90)
                {
                    // The surface drawn alone, by a camera that also clears: the blend's cost shows
                    // against the fine lattice's one load a vertex.
                    var draws = Enumerable.Range(0, Reps).Select(i => Timed(() => shots.Render(), rig.State, 300, out _)).ToList();
                    lines.Add($"[Jelly] {n.x}x{n.y}x{n.z}: a camera drawing the surface {Spread(draws)} ms");
                }
                Object.DestroyImmediate(view);
                rig.Release();
            }
            // A row a lattice, fine at the top, the frames across.
            int lattices = strip.Count / shotsAt.Length;
            var sheet = new Texture2D(strip[0].width * shotsAt.Length, strip[0].height * lattices, TextureFormat.RGB24, false);
            for (int i = 0; i < strip.Count; i++)
                sheet.SetPixels((i % shotsAt.Length) * strip[0].width, (lattices - 1 - i / shotsAt.Length) * strip[0].height, strip[0].width, strip[0].height, strip[i].GetPixels());
            File.WriteAllBytes(Path.Combine(dir, "turn.png"), sheet.EncodeToPNG());
            // The two wear builds, last, since each replaces the prefab: two blitters and no parser, or
            // the third pass and a parser that only switches the steps.
            int Count<T>(GameObject go) where T : Component => go.GetComponentsInChildren<T>(true).Length;
            var wear = Make(preview: false, withMeter: false, withRider: false, offFar: false, sleeps: false);
            int wearBlits = Count<CVRBlitter>(wear), wearParsers = Count<CVRTexturePropertyParser>(wear);
            var wearOff = Make(preview: false, withMeter: false, withRider: false, offFar: true, sleeps: false);
            var switches = wearOff.GetComponentsInChildren<CVRTexturePropertyParser>(true).SelectMany(p => p.tasks).ToList();
            // Counted now: the packed build below replaces the same prefab.
            int offBlits = Count<CVRBlitter>(wearOff);
            bool sleepless = wear.GetComponentsInChildren<CVRBlitter>(true).All(b => !b.blitMaterial.IsKeywordEnabled("JELLY_SLEEP"));
            bool built = sleepless && wearBlits == 2 && wearParsers == 0 && offBlits == 3 && switches.Count == 2
                && switches.All(t => t.propertyName == "enabled" && t.component is CVRBlitter) && wearOff.transform.Find("Rider") == null;
            var three = Make(preview: false, withMeter: false, withRider: false, offFar: false, sleeps: false, regions: 3);
            var feeds = three.GetComponentsInChildren<CVRMaterialDataProvider>(true).Single().transforms;
            int surfaces = Count<MeshRenderer>(three), stateRows = AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + "/Jelly_State.renderTexture").height;
            bool packs3 = feeds.Length == 3 && surfaces == 3 && Count<CVRBlitter>(three) == 2 && stateRows == 3 * (Side(Lattice) + 1)
                && Enumerable.Range(0, 3).All(r => three.transform.Find($"Jelly Region {r}/{(r == 0 ? "Surface" : $"Surface {r}")}") != null);
            built &= packs3;
            ok &= built;
            lines.Add($"[Jelly] packed three: {feeds.Length} anchors, {surfaces} surfaces, {Count<CVRBlitter>(three)} blitters, {stateRows} state rows, {(packs3 ? "as they should be" : "WRONG")}");
            lines.Add($"[Jelly] the wear builds: {wearBlits} blitters and {wearParsers} parsers; far copies switched off, " +
                      $"{offBlits} blitters and {switches.Count} parser tasks, {(built ? "as they should be" : "WRONG")}");
            lines.Add($"[Jelly] {(ok ? "PASS" : "FAIL")}, frames at {string.Join(", ", shotsAt)} in {Path.Combine(dir, "turn.png")}");
            foreach (var l in lines) Debug.Log(l);
            File.WriteAllLines(Path.Combine(dir, "bench.txt"), lines);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        // Every player's hands somewhere far off, so the first pass looks at them all and keeps none;
        // the first inReach of them poking the bench tissue's front, from in front of it. The local
        // player's head, the client's first, head metres in front of the tissue, or unset.
        static void Players(int count, int inReach = 0, float head = 0f)
        {
            var left = Enumerable.Repeat(new Vector4(50f, 50f, 50f, 0f), 255).ToArray();
            var right = (Vector4[]) left.Clone();
            var chest = (Vector4[]) left.Clone();
            var root = Enumerable.Repeat(new Vector4(50f, 0f, 50f, 1.6f), 255).ToArray();
            var heads = new Vector4[255];
            if (head > 0f) heads[0] = new Vector4(0f, 1.3f, 0.3f + head, 0f);
            for (int e = 0; e < inReach; e++)
            {
                var at = new Vector4(-0.07f + 0.02f * e, 1.3f, 0.55f, 0f);
                if ((e & 1) == 0) left[e >> 1] = at;
                else right[e >> 1] = at;
                chest[e >> 1] = new Vector4(0f, 1.3f, 1f, 0f);
                root[e >> 1] = new Vector4(0f, 0f, 1f, 1.6f);
            }
            Shader.SetGlobalVectorArray("_CVR_PlayerLeftHandPositions", left);
            Shader.SetGlobalVectorArray("_CVR_PlayerRightHandPositions", right);
            Shader.SetGlobalVectorArray("_CVR_PlayerChestPositions", chest);
            Shader.SetGlobalVectorArray("_CVR_PlayerRootPositions", root);
            Shader.SetGlobalVectorArray("_CVR_PlayerHeadPositions", heads);
            Shader.SetGlobalVector("CVRGlobalParams1", new Vector4(0f, count, 0f, 0f));
        }

        // Per call, with the GPU waited on at the end, and the part that is only submitting. Warmed
        // for a third of a second first, so the GPU's clock has come up.
        static double Timed(Action run, RenderTexture rt, int runs, out double submit)
        {
            var warm = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 30 || warm.ElapsedMilliseconds < 330; i++) run();
            AsyncGPUReadback.Request(rt).WaitForCompletion();
            AsyncGPUReadback.WaitAllRequests();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < runs; i++) run();
            submit = clock.Elapsed.TotalMilliseconds / runs;
            AsyncGPUReadback.Request(rt).WaitForCompletion();
            AsyncGPUReadback.WaitAllRequests();
            return clock.Elapsed.TotalMilliseconds / runs;
        }

        static void Add<TKey>(Dictionary<TKey, List<double>> to, TKey key, double value)
        {
            if (!to.TryGetValue(key, out var list)) to[key] = list = new List<double>();
            list.Add(value);
        }

        static double Median(List<double> xs) => xs.OrderBy(x => x).ElementAt(xs.Count / 2);

        static string Spread(List<double> xs) => $"{Median(xs):0.000} ({xs.Min():0.000} to {xs.Max():0.000})";

        static Vector4[] Read(RenderTexture rt)
        {
            var req = AsyncGPUReadback.Request(rt);
            req.WaitForCompletion();
            return req.GetData<Vector4>().ToArray();
        }

        // No NaN, the back face on the anchor, how far the front centre sits from where the rigid
        // shape would put it, and the fastest particle.
        static bool Health(Vector4[] s, Vector3Int n, Matrix4x4 anchor, out string said, out float frontOff)
        {
            int width = 2 * Row, bad = 0;
            float held = 0f, fastest = 0f, worst = 0f;
            var worstAt = Vector3Int.zero;
            float spacing = SpacingOf(n);
            Vector3 Rest(int i, int j, int k) => new Vector3((i - 0.5f * (n.x - 1)) * spacing, (j - 0.5f * (n.y - 1)) * spacing, k * spacing);
            for (int k = 0; k < n.z; k++)
                for (int j = 0; j < n.y; j++)
                    for (int i = 0; i < n.x; i++)
                    {
                        int p = (k * n.y + j) * n.x + i;
                        var x = s[(p / Row) * width + p % Row];
                        var was = s[(p / Row) * width + Row + p % Row];
                        if (float.IsNaN(x.x + x.y + x.z + was.x + was.y + was.z + was.w)) { bad++; continue; }
                        float away = ((Vector3) x - anchor.MultiplyPoint3x4(Rest(i, j, k))).magnitude;
                        if (k == 0) held = Mathf.Max(held, away);
                        if (away > worst) { worst = away; worstAt = new Vector3Int(i, j, k); }
                        fastest = Mathf.Max(fastest, ((Vector3) x - (Vector3) was).magnitude / Mathf.Max(was.w, 1e-4f));
                    }
            int front = ((n.z - 1) * n.y + n.y / 2) * n.x + n.x / 2;
            var tip = (Vector3) s[(front / Row) * width + front % Row];
            var off = tip - anchor.MultiplyPoint3x4(Rest(n.x / 2, n.y / 2, n.z - 1));
            frontOff = off.magnitude;
            said = $"front off its rigid place {100f * off.magnitude:0.0} cm ({100f * off.x:0.0}/{100f * off.y:0.0}/{100f * off.z:0.0}), " +
                   $"worst {100f * worst:0.0} cm at {worstAt}, back face {1000f * held:0.0} mm, fastest {fastest:0.00} m/s{(bad > 0 ? $", {bad} NaN" : "")}";
            return bad == 0 && held < 1e-3f && fastest < 20f && off.magnitude < 0.15f;
        }

        // A camera on the tissue for the bench's pictures, drawing the surface from its state.
        static GameObject Viewer(RenderTexture state, Vector3Int n, out Camera cam)
        {
            var draw = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/JellyDraw.shader"));
            draw.SetTexture("_State", state);
            draw.SetVector("_Lattice", new Vector4(n.x, n.y, n.z, SpacingOf(n)));
            var go = new GameObject("Jelly bench") { hideFlags = HideFlags.HideAndDontSave };
            go.AddComponent<MeshFilter>().sharedMesh = Surface(n);
            go.AddComponent<MeshRenderer>().sharedMaterial = draw;
            var look = new GameObject("Jelly bench camera") { hideFlags = HideFlags.HideAndDontSave };
            look.transform.position = new Vector3(0.55f, 1.45f, 0.75f);
            look.transform.LookAt(new Vector3(0f, 1.25f, 0.4f));
            cam = look.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.2f, 0.2f, 0.24f);
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 40f;
            cam.targetTexture = new RenderTexture(320, 240, 24);
            return go;
        }

        static Texture2D Shot(Camera cam)
        {
            cam.Render();
            var was = RenderTexture.active;
            RenderTexture.active = cam.targetTexture;
            var png = new Texture2D(cam.targetTexture.width, cam.targetTexture.height, TextureFormat.RGB24, false);
            png.ReadPixels(new Rect(0, 0, png.width, png.height), 0, 0);
            png.Apply();
            RenderTexture.active = was;
            return png;
        }

        // ---------------------------------------------------------------- verify

        public static void Verify()
        {
            if (Make() == null)
            {
                EditorApplication.Exit(1);
                return;
            }
            // Built again in the same session, as the menu item does on every add: the scene's jelly must
            // still reach every asset after it.
            Make(preview: false);
            SessionState.SetString(K + "out", Out());
            SessionState.SetInt(K + "on", 1);
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            EditorApplication.update += Hook;
            EditorApplication.EnterPlaymode();
        }

        static void Hook()
        {
            if (!EditorApplication.isPlaying || eye != null || SessionState.GetInt(K + "on", 0) == 0) return;
#if AVATARBRIDGE_MAGICA
            if (SessionState.GetInt(K + "on", 0) == 4)
            {
                bool film = SessionState.GetBool(K + "propFilm", false) || SessionState.GetBool(K + "propWorldFilm", false);
                eye = Object.FindObjectsOfType<Camera>().First(c => c.name == "Eye");
                eye.targetTexture = film ? new RenderTexture(960, 540, 24) : new RenderTexture(480, 360, 24);
                eye2 = film ? Object.FindObjectsOfType<Camera>().First(c => c.name == "Eye 2") : null;
                if (eye2 != null) eye2.targetTexture = new RenderTexture(960, 540, 24);
            }
            else
#endif
#if AVATARBRIDGE_YAPS
            if (SessionState.GetInt(K + "on", 0) == 2) HookBreasts();
            else if (SessionState.GetInt(K + "on", 0) == 3) HookTune();
            else
#endif
            {
                eye = Object.FindObjectsOfType<Camera>().First();
                eye.targetTexture = new RenderTexture(480, 360, 24);
                swing = GameObject.Find("Swing").transform;
                jelly = Object.FindObjectsOfType<CVRBlitter>().First().transform.parent;
            }
            // -jellyFps: below 72 the step takes two whole steps a frame instead of a predicted one,
            // as a desktop player's 60 does.
            int fpsArg = Array.IndexOf(Environment.GetCommandLineArgs(), "-jellyFps");
            Time.captureFramerate = fpsArg >= 0 && fpsArg + 1 < Environment.GetCommandLineArgs().Length ? int.Parse(Environment.GetCommandLineArgs()[fpsArg + 1]) : 90;
            frame = 0;
            // Batch draws nothing on its own, so the camera is rendered by hand once a frame, after
            // every script's LateUpdate, where the player draws too.
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                var sys = loop.subSystemList[i];
                if (sys.type != typeof(UnityEngine.PlayerLoop.PostLateUpdate)) continue;
                var list = sys.subSystemList.ToList();
                list.Add(new PlayerLoopSystem { type = typeof(Late), updateDelegate = OnLate });
                sys.subSystemList = list.ToArray();
                loop.subSystemList[i] = sys;
            }
            PlayerLoop.SetPlayerLoop(loop);
        }

        static readonly List<Texture2D> verifyShots = new List<Texture2D>();

        static void OnLate()
        {
            if (!EditorApplication.isPlaying || eye == null) return;
#if AVATARBRIDGE_MAGICA
            if (SessionState.GetInt(K + "on", 0) == 4)
            {
                if (SessionState.GetBool(K + "propFilm", false)) OnLatePropFilm();
                else if (SessionState.GetBool(K + "propWorld", false)) OnLatePropWorld();
                else OnLateProp();
                return;
            }
#endif
#if AVATARBRIDGE_YAPS
            if (SessionState.GetInt(K + "on", 0) == 2)
            {
                OnLateBreasts();
                return;
            }
            if (SessionState.GetInt(K + "on", 0) == 3)
            {
                OnLateTune();
                return;
            }
#endif
            // The anchor the provider handed this frame's passes, before this frame's turn.
            seen = jelly.localToWorldMatrix;
            // Still for two seconds, then a quick turn and back, as a body turning on the spot.
            float t = Mathf.Max(frame - 180, 0) / 90f;
            swing.rotation = Quaternion.Euler(0f, 50f * Mathf.Sin(t * Mathf.PI * 2f * 0.7f) * Mathf.Min(t * 3f, 1f), 0f);
            eye.Render();
            if (frame >= 180 && frame % 12 == 0 && verifyShots.Count < 8)
            {
                var was = RenderTexture.active;
                RenderTexture.active = eye.targetTexture;
                var png = new Texture2D(eye.targetTexture.width, eye.targetTexture.height, TextureFormat.RGB24, false);
                png.ReadPixels(new Rect(0, 0, png.width, png.height), 0, 0);
                RenderTexture.active = was;
                verifyShots.Add(png);
            }
            if (++frame < Frames) return;

            string dir = SessionState.GetString(K + "out", "Jelly");
            var state = AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + "/Jelly_State.renderTexture");
            var tissueNow = Read(state);
            var n = Lattice;
            bool ok = Health(tissueNow, n, seen, out string said, out _);
            // The rider's texels, decoded as the parser's tasks would, against the front centre.
            var where = Read(AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + "/Jelly_Points.renderTexture"));
            int front = ((n.z - 1) * n.y + n.y / 2) * n.x + n.x / 2;
            var frontAt = seen.inverse.MultiplyPoint3x4((Vector3) tissueNow[(front / Row) * 2 * Row + front % Row]);
            var read = (Vector3) where[0] - Vector3.one * 0.5f;
            var turn = ((Vector3) where[1] - Vector3.one * 0.5f) * 360f;
            // The viewer here has no head, so the far switch must leave the steps running.
            ok &= (read - frontAt).magnitude < 1e-3f && where[0].w == 1f && where[2].x == 1f;
            var clockNow = Read(AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + "/Jelly_Clock.renderTexture"));
            said += $"; the meter's clock: {clockNow[0].x:0.00} ms now, last phases off {clockNow[2].x:0.00} and on {clockNow[2].y:0.00} ({clockNow[2].z:0}/{clockNow[2].w:0})";
            ok &= Mathf.Abs(clockNow[0].x - 1000f / 90f) < 0.5f;
            int dead = jelly.GetComponentsInChildren<Renderer>(true).Count(r => r.sharedMaterial == null)
                + jelly.GetComponentsInChildren<CVRBlitter>(true).Count(b => b.blitMaterial == null || b.originTexture == null || b.destinationTexture == null);
            ok &= dead == 0;
            said += $"; built twice, {dead} missing references";
            // The upload is the material file, so the surface's lattice must be in it: a value its
            // shader does not declare never reaches it, and in game every lookup missed the tissue.
            bool sized = File.ReadAllText(Folder + "/Jelly_Draw.mat").Contains($"_Lattice: {{r: {n.x}, g: {n.y}, b: {n.z}");
            ok &= sized;
            said += sized ? "; the surface's material file holds its lattice" : "; the surface's material file has NO lattice";
            said += $"; the rider's texels: at {100f * read.x:0.0}/{100f * read.y:0.0}/{100f * read.z:0.0} cm against the front centre's " +
                    $"{100f * frontAt.x:0.0}/{100f * frontAt.y:0.0}/{100f * frontAt.z:0.0}, turned {turn.x:0.0}/{turn.y:0.0}/{turn.z:0.0} degrees";
            if (verifyShots.Count > 0)
            {
                int w = verifyShots[0].width, h = verifyShots[0].height;
                var sheet = new Texture2D(w * 4, h * 2, TextureFormat.RGB24, false);
                for (int i = 0; i < verifyShots.Count; i++) sheet.SetPixels((i % 4) * w, (1 - i / 4) * h, w, h, verifyShots[i].GetPixels());
                File.WriteAllBytes(Path.Combine(dir, "verify.png"), sheet.EncodeToPNG());
            }
            Debug.Log($"[Jelly] {(ok ? "PASS" : "FAIL")} verify after {frame} frames with the CCK's render controller: {said}");
            SessionState.SetInt(K + "on", 0);
            EditorApplication.Exit(ok ? 0 : 1);
        }

#if AVATARBRIDGE_MAGICA
        // A MagicaCloth capsule from bone to bone, radius in metres, as an avatar's own.
        static List<MagicaCloth2.ColliderComponent> TestCapsules(Animator anim, float radius, params (HumanBodyBones from, HumanBodyBones to)[] bones)
        {
            var made = new List<MagicaCloth2.ColliderComponent>();
            foreach (var (from, to) in bones)
            {
                Transform f = anim.GetBoneTransform(from), t = anim.GetBoneTransform(to);
                var holder = new GameObject($"Test collider {f.name}").transform;
                holder.SetParent(f, false);
                holder.SetPositionAndRotation(0.5f * (f.position + t.position), Quaternion.FromToRotation(Vector3.up, t.position - f.position));
                var cap = holder.gameObject.AddComponent<MagicaCloth2.MagicaCapsuleCollider>();
                cap.direction = MagicaCloth2.MagicaCapsuleCollider.Direction.Y;
                cap.alignedOnCenter = true;
                float s0 = Mathf.Abs(holder.lossyScale.x);
                cap.SetSize(radius / s0, radius / s0, Vector3.Distance(f.position, t.position) / s0);
                made.Add(cap);
            }
            return made;
        }
#endif

#if AVATARBRIDGE_YAPS
        // The scene's avatar with a squash, its breasts put on the jelly, played with the CCK's own
        // render controller: settled, then a finger pushed 3 cm into one breast and taken away, then
        // the avatar turning on the spot. Passes when the anchor the step wrote is the rig's, the
        // tissue rests within 6 mm of its modelled shape (the lift), the finger moves it and the
        // body's own pixels with it well past the frame-to-frame noise, and the turn swings it.
        public static void VerifyBreasts()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-jellyScene");
            if (at < 0 || at + 1 >= args.Length)
            {
                Debug.Log("[Jelly] FAIL breasts: no -jellyScene");
                EditorApplication.Exit(1);
                return;
            }
            EditorSceneManager.OpenScene(args[at + 1], OpenSceneMode.Single);
            // -jellyPair: another of the squash's pairs, by a pattern its name matches.
            int pairAt = Array.IndexOf(args, "-jellyPair");
            SessionState.SetString(K + "pair", pairAt >= 0 && pairAt + 1 < args.Length ? args[pairAt + 1] : Breast.ToString());
            // A scene's avatar can be older than the conversion, so a prefab can stand in, alone.
            int from = Array.IndexOf(args, "-jellyPrefab");
            if (from >= 0 && from + 1 < args.Length)
            {
                foreach (var other in Object.FindObjectsOfType<Animator>()) other.gameObject.SetActive(false);
                var made = (GameObject) PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(args[from + 1]));
                // -jellySquash: the squash built on it first, into a folder of its own, cleared from
                // outside after the run.
                if (args.Contains("-jellySquash"))
                {
                    PrefabUtility.UnpackPrefabInstance(made, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    string squash = YapsSquash.Add(made.GetComponentInChildren<CVRAvatar>(true).transform, "Assets/__JellySquash", out bool built);
                    YapsShaderPatcher.EndPass(null, null);
                    Debug.Log($"[Jelly] squash built {built}: {squash}");
                }
            }
            var avatar = Object.FindObjectsOfType<CVRAvatar>().FirstOrDefault(a => YapsSquash.Has(a.transform));
            // Any other jelly in the scene, a prop, shares the step's materials in the editor, where
            // nothing instances them per copy as the client does: the last provider to run hands
            // its anchor to both.
            foreach (var other in Object.FindObjectsOfType<CVRRenderController>())
                if (avatar != null && !other.transform.IsChildOf(avatar.transform)) other.gameObject.SetActive(false);
#if AVATARBRIDGE_MAGICA
            // Three MagicaCloth colliders in the breasts' cloth, as an avatar's own: one round each
            // forearm, which the own-arm stage brings in, and a small one in the cleavage, clear of the
            // skin and the cloth at rest, which must not move it (at 6 cm it was left out, in the cloth).
            if (avatar != null)
            {
                var anim = avatar.GetComponentInChildren<Animator>();
                var tested = TestCapsules(anim, 0.04f, (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand), (HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand));
                var chestBone = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
                var inChest = new GameObject("Test collider in the chest").transform;
                inChest.SetParent(chestBone, false);
                inChest.position = chestBone.position + avatar.transform.forward * 0.12f - avatar.transform.up * 0.05f;
                var sphere = inChest.gameObject.AddComponent<MagicaCloth2.MagicaSphereCollider>();
                sphere.SetSize(0.015f / Mathf.Abs(inChest.lossyScale.x));
                tested.Add(sphere);
                foreach (var cloth in PairCloth(avatar.transform, Wanted()).OfType<MagicaCloth2.MagicaCloth>())
                    cloth.SerializeData.colliderCollisionConstraint.colliderList.AddRange(tested);
            }
#endif
            string said = avatar != null ? PairOn(avatar.transform, Wanted()) : "no avatar with a squash in the scene";
            Debug.Log("[Jelly] breasts: " + said);
            if (avatar == null || !said.StartsWith("On "))
            {
                EditorApplication.Exit(1);
                return;
            }
            SessionState.SetString(K + "out", Out());
            SessionState.SetInt(K + "on", 2);
            EditorApplication.update += Hook;
            EditorApplication.EnterPlaymode();
        }

        static Regex Wanted() => new Regex(SessionState.GetString(K + "pair", Breast.ToString()), RegexOptions.IgnoreCase);

        // For the squash probe's film (-spVideo), which presses with the same player globals the
        // jelly reads: the scene without its other jellies, and the avatar's prefab with its breasts
        // on the jelly, both saved under Assets/__JellyFilm, cleared from outside after.
        public static void FilmBreasts()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-jellyScene"), from = Array.IndexOf(args, "-jellyPrefab");
            if (at < 0 || from < 0 || from + 1 >= args.Length)
            {
                Debug.Log("[Jelly] FAIL film: needs -jellyScene and -jellyPrefab");
                EditorApplication.Exit(1);
                return;
            }
            var scene = EditorSceneManager.OpenScene(args[at + 1], OpenSceneMode.Single);
            // A jelly prop shares the step's materials in the editor and would hand them its anchor.
            foreach (var other in Object.FindObjectsOfType<CVRRenderController>(true))
                if (other != null && other.GetComponentInParent<CVRAvatar>(true) == null) Object.DestroyImmediate(other.transform.root.gameObject);
            var made = (GameObject) PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(args[from + 1]), scene);
            // -jellyPick <left bone>,<right bone> or -jellyThighs: the squash rebuilt on this copy with
            // that pair alone, picked or the thighs painted, so it is the first pair, the one the squash
            // probe films.
            int pick = Array.IndexOf(args, "-jellyPick");
            bool thighs = args.Contains("-jellyThighs");
            PrefabUtility.UnpackPrefabInstance(made, pick >= 0 || thighs ? PrefabUnpackMode.Completely : PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            const string Film = "Assets/__JellyFilm";
            if (!AssetDatabase.IsValidFolder(Film)) AssetDatabase.CreateFolder("Assets", "__JellyFilm");
            if (pick >= 0 || thighs)
            {
                var avatar = made.GetComponentInChildren<CVRAvatar>(true).transform;
                var settings = YapsSquash.SettingsOf(avatar) ?? avatar.gameObject.AddComponent<Yaps.YapsSquashSettings>();
                settings.partBones.Clear();
                settings.pairs.Clear();
                if (thighs) PaintThighs(avatar, settings);
                else settings.partBones.AddRange(args[pick + 1].Split(',').Select(x => avatar.GetComponentsInChildren<Transform>(true).First(t => t.name == x)));
                if (!AssetDatabase.IsValidFolder(Film + "/Squash")) AssetDatabase.CreateFolder(Film, "Squash");
                string squash = YapsSquash.Add(avatar, Film + "/Squash", out bool built);
                YapsShaderPatcher.EndPass(null, null);
                Debug.Log($"[Jelly] film: squash built {built}: {squash}");
            }
            // -jellyHandColliders after|before: a MagicaCloth capsule on each hand in the breasts'
            // cloth, as a PhysBones conversion lists them; after, put on first so the jelly presses
            // with them instead, before, put on last so they stay in the cloth and move the bones.
            int hc = Array.IndexOf(args, "-jellyHandColliders");
            string hands = hc >= 0 && hc + 1 < args.Length ? args[hc + 1] : null;
            void HandColliders()
            {
#if AVATARBRIDGE_MAGICA
                var cols = TestCapsules(made.GetComponentInChildren<Animator>(), 0.045f, (HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleDistal), (HumanBodyBones.RightHand, HumanBodyBones.RightMiddleDistal));
                foreach (var cloth in PairCloth(made.transform, Breast).OfType<MagicaCloth2.MagicaCloth>())
                    cloth.SerializeData.colliderCollisionConstraint.colliderList.AddRange(cols);
#endif
            }
            if (hands == "after") HandColliders();
            // -jellyPair: the pair by a pattern its name matches, the breasts unless given; -jellyNone:
            // the squash alone.
            int pairAt = Array.IndexOf(args, "-jellyPair");
            bool bare = args.Contains("-jellyNone");
            string said = bare ? "On the squash alone" : PairOn(made.transform, pairAt >= 0 && pairAt + 1 < args.Length ? new Regex(args[pairAt + 1], RegexOptions.IgnoreCase) : Breast);
            if (hands == "before") HandColliders();
            Debug.Log("[Jelly] film: " + said);
            if (!said.StartsWith("On "))
            {
                EditorApplication.Exit(1);
                return;
            }
            string path = $"{Film}/{made.name} {(bare ? "squash" : "jelly")}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(made, path);
            Object.DestroyImmediate(made);
            bool kept = EditorSceneManager.SaveScene(scene, $"{Film}/Film.unity");
            Debug.Log($"[Jelly] film: {(saved != null && kept ? "ready" : "FAIL")}, {path} and {Film}/Film.unity");
            EditorApplication.Exit(saved != null && kept ? 0 : 1);
        }

        // Thighs painted as a pair for the squash: each vertex's weight on an upper leg and the bones
        // under it short of the knee, the avatar's left A.
        static void PaintThighs(Transform avatar, Yaps.YapsSquashSettings settings)
        {
            var animator = avatar.GetComponentInChildren<Animator>();
            var legs = new[] { (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg), (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg) }
                .Select(x => (thigh: animator.GetBoneTransform(x.Item1), knee: animator.GetBoneTransform(x.Item2))).ToArray();
            var body = YapsSquash.NamedBody(avatar) ?? avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null).OrderByDescending(r => r.sharedMesh.vertexCount).First();
            var mesh = body.sharedMesh;
            var weights = mesh.boneWeights;
            var pair = new Yaps.YapsSquashSettings.Pair { name = "Thighs", a = new byte[mesh.vertexCount], b = new byte[mesh.vertexCount] };
            for (int v = 0; v < weights.Length; v++)
            {
                var w = weights[v];
                var on = new float[2];
                foreach (var (i, weight) in new[] { (w.boneIndex0, w.weight0), (w.boneIndex1, w.weight1), (w.boneIndex2, w.weight2), (w.boneIndex3, w.weight3) })
                    for (int side = 0; side < 2; side++)
                        if (weight > 0f && body.bones[i] != null && body.bones[i].IsChildOf(legs[side].thigh) && !body.bones[i].IsChildOf(legs[side].knee)) on[side] += weight;
                pair.a[v] = (byte) Mathf.RoundToInt(255f * Mathf.Clamp01(on[0]));
                pair.b[v] = (byte) Mathf.RoundToInt(255f * Mathf.Clamp01(on[1]));
            }
            settings.body = body;
            settings.vertexCount = mesh.vertexCount;
            settings.fingerprint = YapsSquash.Fingerprint(mesh);
            settings.pairs.Add(pair);
        }

        // The jelly against the avatar's own breast cloth: two copies side by side, one swinging its
        // breasts with its cloth, one on the jelly, moved alike (turned, hopped twice, walked, laid
        // on its back and stood up), once per setting of the jelly's springs. For each breast, where
        // its front skin goes off where it stood before each move, in the chest's frame. Physics
        // matches the source, so the setting nearest the cloth is the one to keep.
        //   -executeMethod AvatarBridge.Regression.JellySpikeBuilder.TuneBreasts -jellyScene <scene> -jellyPrefab <prefab> -jellyOut <dir>
        public static void TuneBreasts()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-jellyScene"), from = Array.IndexOf(args, "-jellyPrefab");
            if (at < 0 || from < 0 || from + 1 >= args.Length)
            {
                Debug.Log("[Jelly] FAIL tune: needs -jellyScene and -jellyPrefab");
                EditorApplication.Exit(1);
                return;
            }
            EditorSceneManager.OpenScene(args[at + 1], OpenSceneMode.Single);
            foreach (var other in Object.FindObjectsOfType<Animator>()) other.gameObject.SetActive(false);
            foreach (var other in Object.FindObjectsOfType<CVRRenderController>()) other.gameObject.SetActive(false);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(args[from + 1]);
            var cloth = (GameObject) PrefabUtility.InstantiatePrefab(prefab);
            var tissue = (GameObject) PrefabUtility.InstantiatePrefab(prefab);
            cloth.name = "Tune cloth";
            tissue.name = "Tune jelly";
            cloth.transform.position = new Vector3(-4f, 0f, 0f);
            tissue.transform.position = new Vector3(4f, 0f, 0f);
            string said = BreastsOn(tissue.transform);
            Debug.Log("[Jelly] tune: " + said);
            if (!said.StartsWith("On "))
            {
                EditorApplication.Exit(1);
                return;
            }
            SessionState.SetString(K + "out", Out());
            SessionState.SetInt(K + "on", 3);
            EditorApplication.update += Hook;
            EditorApplication.EnterPlaymode();
        }

        // One copy's breasts as measured: per side, its front skin vertex, that vertex in the mesh's
        // own space with the blend shapes on, its skin weights, and where it rests in the chest's
        // unscaled frame and, on the jelly, in the lattice's.
        sealed class Fronts
        {
            public Transform root, chest, rig;
            public SkinnedMeshRenderer body;
            public readonly Vector3[] bind = new Vector3[2], rest = new Vector3[2], onRig = new Vector3[2];
            public readonly BoneWeight[] weight = new BoneWeight[2];
            public readonly float[] share = new float[2];
            public readonly int[] region = new int[2];
            public readonly Transform[] anchor = new Transform[2];
            public Matrix4x4[] bindposes;
            public Vector3 start;
        }

        static Fronts tuneCloth, tuneJelly;
        static Material[] tuneSteps;
        const int TunePer = 1080;
        // The mass's _MassHz, _MassDamp and _Limit; BreastsOn's own first. The particles' springs,
        // swept alone against the cloth, moved too little at any setting to follow its swing.
        static readonly Vector3[] TuneSettings = new[] { Vector3.zero }.Concat(new[] { 0.8f, 1f, 1.25f, 1.5f, 2f }
            .SelectMany(hz => new[] { 2f, 3.5f, 6f }.SelectMany(damp => new[] { 0.3f, 0.6f }.Select(limit => new Vector3(hz, damp, limit))))).ToArray();
        // Per setting, per frame: the cloth's and the jelly's two fronts, centimetres off where they
        // stood before the move.
        static readonly List<Vector3[]> tuneTrace = new List<Vector3[]>();
        static readonly List<string> tuneLines = new List<string>();
        static readonly List<(Vector3 setting, float score)> tuneScores = new List<(Vector3, float)>();

        static Fronts Measured(Transform root, bool jellied)
        {
            var f = new Fronts { root = root, start = root.position };
            var animator = root.GetComponentInChildren<Animator>();
            animator.enabled = false;
            f.chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
            f.body = YapsSquash.NamedBody(root);
            var shares = YapsSquash.NamedShares(root, f.body, out var names, out _);
            int pair = names.FindIndex(x => Breast.IsMatch(x));
            var mesh = f.body.sharedMesh;
            var baked = new Mesh();
            f.body.BakeMesh(baked);
            var at = baked.vertices;
            Object.DestroyImmediate(baked);
            var verts = mesh.vertices;
            var weights = mesh.boneWeights;
            f.bindposes = mesh.bindposes;
            var frame = Matrix4x4.TRS(f.chest.position, f.chest.rotation, Vector3.one).inverse;
            if (jellied) f.rig = f.chest.GetComponentsInChildren<Transform>(true).First(t => t.name == Path.GetFileNameWithoutExtension(Prefab));
            var regions = jellied ? new[] { f.rig.Find("Jelly Region 0"), f.rig.Find("Jelly Region 1") } : null;
            for (int s = 0; s < 2; s++)
            {
                int best = -1;
                float most = float.MinValue;
                for (int v = 0; v < at.Length && v < shares.Length; v++)
                {
                    if (shares[v][2 * pair + s] < 0.8f) continue;
                    float ahead = Vector3.Dot(f.body.transform.position + f.body.transform.rotation * at[v] - f.chest.position, root.forward);
                    if (ahead > most) { most = ahead; best = v; }
                }
                var world = f.body.transform.position + f.body.transform.rotation * at[best];
                f.rest[s] = frame.MultiplyPoint3x4(world);
                f.weight[s] = weights[best];
                f.share[s] = Mathf.Clamp01(shares[best][2 * pair + s]);
                // The vertex as the renderer skins it: the mesh's own place with its blend shapes on.
                var shaped = verts[best];
                var delta = new Vector3[verts.Length];
                for (int b = 0; b < mesh.blendShapeCount; b++)
                {
                    float w = f.body.GetBlendShapeWeight(b);
                    if (w == 0f || mesh.GetBlendShapeFrameCount(b) == 0) continue;
                    int last = mesh.GetBlendShapeFrameCount(b) - 1;
                    mesh.GetBlendShapeFrameVertices(b, last, delta, null, null);
                    shaped += delta[best] * (w / mesh.GetBlendShapeFrameWeight(b, last));
                }
                f.bind[s] = shaped;
                // The region whose anchor is nearer carries it.
                if (jellied)
                {
                    f.region[s] = Vector3.Distance(world, regions[0].position) <= Vector3.Distance(world, regions[1].position) ? 0 : 1;
                    f.anchor[s] = regions[f.region[s]];
                    f.onRig[s] = f.anchor[s].InverseTransformPoint(world);
                }
            }
            return f;
        }

        static void HookTune()
        {
            tuneCloth = Measured(GameObject.Find("Tune cloth").transform, false);
            tuneJelly = Measured(GameObject.Find("Tune jelly").transform, true);
            jelly = tuneJelly.rig;
            tuneSteps = jelly.GetComponentInChildren<CVRRenderController>(true).cvrRenderComponents.OfType<CVRBlitter>().Select(b => b.blitMaterial).ToArray();
            var lattice = YapsSquash.Materials(tuneJelly.root).First(m => m.GetVector("_YAPS_JellyLattice").x > 0).GetVector("_YAPS_JellyLattice");
            breastN = new Vector3Int((int) lattice.x, (int) lattice.y, (int) lattice.z);
            breastSpacing = lattice.w;
            // Never rendered; it only opens OnLate's gate.
            eye = new GameObject("Jelly tune camera").AddComponent<Camera>();
            eye.enabled = false;
            Shader.SetGlobalVectorArray("_CVR_PlayerHeadPositions", new Vector4[255]);
            Shader.SetGlobalVector("CVRGlobalParams1", Vector4.zero);
        }

        // Where the cloth copy's front skin is, skinned on the CPU as the renderer skins it.
        static Vector3 Skinned(Fronts f, int s)
        {
            var w = f.weight[s];
            var bones = f.body.bones;
            Vector3 One(int i, float k) => k > 0f ? (bones[i].localToWorldMatrix * f.bindposes[i]).MultiplyPoint3x4(f.bind[s]) * k : Vector3.zero;
            return One(w.boneIndex0, w.weight0) + One(w.boneIndex1, w.weight1) + One(w.boneIndex2, w.weight2) + One(w.boneIndex3, w.weight3);
        }

        // How far region r's lattice round a point resting at local, in its anchor's frame, has moved
        // off its rest.
        static Vector3 Moved(Vector4[] s, int r, Vector3 local, Matrix4x4 rig)
        {
            var n = breastN;
            float sp = breastSpacing;
            var c = new Vector3(local.x / sp + 0.5f * (n.x - 1), local.y / sp + 0.5f * (n.y - 1), local.z / sp);
            c = Vector3.Max(Vector3.zero, Vector3.Min(c, (Vector3) (n - Vector3Int.one)));
            var c0 = Vector3Int.Min(Vector3Int.FloorToInt(c), n - new Vector3Int(2, 2, 2));
            var t = c - c0;
            var move = Vector3.zero;
            for (int k = 0; k < 8; k++)
            {
                var d = new Vector3Int(k & 1, (k >> 1) & 1, k >> 2);
                var l = c0 + d;
                int p = (l.z * n.y + l.y) * n.x + l.x;
                var rest = new Vector3((l.x - 0.5f * (n.x - 1)) * sp, (l.y - 0.5f * (n.y - 1)) * sp, l.z * sp);
                var off = (Vector3) Particle(s, r, p) - rig.MultiplyPoint3x4(rest);
                move += (d.x == 1 ? t.x : 1 - t.x) * (d.y == 1 ? t.y : 1 - t.y) * (d.z == 1 ? t.z : 1 - t.z) * off;
            }
            return move;
        }

        // The move at t seconds into a setting's run: still, turned, hopped twice, walked, laid on its
        // back and stood up. Each setting walks on from where the last one stopped.
        static void Posed(Fronts f, int run, float t)
        {
            float walk = Mathf.Clamp(t - 5.5f, 0f, 2f) * 1.3f;
            float hop = 0f;
            foreach (float start in new[] { 3.5f, 4.1f })
                if (t >= start && t < start + 0.45f) hop = 0.25f * Mathf.Sin(Mathf.PI * (t - start) / 0.45f);
            float bob = t >= 5.5f && t < 7.5f ? 0.03f * Mathf.Sin(2f * Mathf.PI * 2f * (t - 5.5f)) : 0f;
            float yaw = t >= 1f && t < 2.2f ? 50f * Mathf.Sin(2f * Mathf.PI * 0.7f * (t - 1f)) * Mathf.Min((t - 1f) * 3f, 1f) * Mathf.Min((2.2f - t) * 3f, 1f) : 0f;
            float back = t < 8.5f ? 0f : t < 9f ? (t - 8.5f) / 0.5f : t < 10.5f ? 1f : t < 11f ? 1f - (t - 10.5f) / 0.5f : 0f;
            f.root.SetPositionAndRotation(f.start + Vector3.forward * (run * 2.6f + walk) + Vector3.up * (hop + bob + 0.9f * back),
                Quaternion.Euler(-90f * Mathf.SmoothStep(0f, 1f, back), yaw, 0f));
        }

        static void OnLateTune()
        {
            int run = frame / TunePer, f = frame % TunePer;
            if (f == 0)
            {
                // The first run keeps what BreastsOn set from the mass.
                if (run == 0) TuneSettings[0] = new Vector3(tuneSteps[0].GetFloat("_MassHz"), tuneSteps[0].GetFloat("_MassDamp"), tuneSteps[0].GetFloat("_Limit"));
                foreach (var m in tuneSteps)
                {
                    m.SetFloat("_MassHz", TuneSettings[run].x);
                    m.SetFloat("_MassDamp", TuneSettings[run].y);
                    m.SetFloat("_Limit", TuneSettings[run].z);
                }
                tuneTrace.Clear();
            }
            Shader.SetGlobalVector("CVRGlobalParams1", Vector4.zero);
            var state = Read(AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + "/Jelly_State.renderTexture"));
            var now = new Vector3[4];
            for (int s = 0; s < 2; s++)
            {
                var clothFrame = Matrix4x4.TRS(tuneCloth.chest.position, tuneCloth.chest.rotation, Vector3.one).inverse;
                now[s] = 100f * (clothFrame.MultiplyPoint3x4(Skinned(tuneCloth, s)) - tuneCloth.rest[s]);
                now[2 + s] = 100f * (Quaternion.Inverse(tuneJelly.chest.rotation) * Moved(state, tuneJelly.region[s], tuneJelly.onRig[s], tuneJelly.anchor[s].localToWorldMatrix) * tuneJelly.share[s]);
            }
            tuneTrace.Add(now);
            float t = (f + 1) / 90f;
            Posed(tuneCloth, run, t);
            Posed(tuneJelly, run, t);
            if (f == TunePer - 1) Scored(run);
            if (++frame < TunePer * TuneSettings.Length) return;

            var best = tuneScores.OrderBy(x => x.score).First();
            tuneLines.Add($"[Jelly] tune: best _MassHz {best.setting.x}, _MassDamp {best.setting.y}, _Limit {best.setting.z}, {best.score:0.00} cm rms off the cloth, against {tuneScores[0].score:0.00} for the defaults");
            string dir = SessionState.GetString(K + "out", "Jelly");
            File.WriteAllLines(Path.Combine(dir, "tune.txt"), tuneLines);
            foreach (var line in tuneLines.Skip(tuneLines.Count - 3)) Debug.Log(line);
            SessionState.SetInt(K + "on", 0);
            EditorApplication.Exit(0);
        }

        // Each move scored from where both stood just before it: turned (1 to 3.5 s), hopped (3.5 to
        // 5.5), walked (5.5 to 8.5), laid back and stood up (8.5 to 12). Per move, the rms of the
        // jelly's fronts off the cloth's, and each one's furthest; the score sums the moves' rms but
        // the walk's: walking, the cloth's chains, pointing forward and pushed back along their own
        // length, buckle 20 cm sideways, which tissue should not copy.
        static void Scored(int run)
        {
            var moves = new[] { ("turn", 1f, 3.5f), ("hop", 3.5f, 5.5f), ("walk", 5.5f, 8.5f), ("lie", 8.5f, 12f) };
            var said = new List<string>();
            float score = 0f;
            var csv = new List<string> { "t,clothL.x,clothL.y,clothL.z,clothR.x,clothR.y,clothR.z,jellyL.x,jellyL.y,jellyL.z,jellyR.x,jellyR.y,jellyR.z" };
            for (int i = 0; i < tuneTrace.Count; i++)
                csv.Add($"{i / 90f:0.000}," + string.Join(",", tuneTrace[i].SelectMany(v => new[] { v.x, v.y, v.z }).Select(x => x.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture))));
            foreach (var (name, from, to) in moves)
            {
                int a = Mathf.RoundToInt(from * 90f), b = Mathf.Min(Mathf.RoundToInt(to * 90f), tuneTrace.Count);
                var zero = tuneTrace[a];
                double sum = 0;
                float clothMost = 0f, jellyMost = 0f;
                for (int i = a; i < b; i++)
                    for (int s = 0; s < 2; s++)
                    {
                        var c = tuneTrace[i][s] - zero[s];
                        var j = tuneTrace[i][2 + s] - zero[2 + s];
                        sum += (c - j).sqrMagnitude;
                        clothMost = Mathf.Max(clothMost, c.magnitude);
                        jellyMost = Mathf.Max(jellyMost, j.magnitude);
                    }
                float rms = (float) Math.Sqrt(sum / (2 * (b - a)));
                if (name != "walk") score += rms;
                said.Add($"{name} {rms:0.00} (cloth {clothMost:0.0}, jelly {jellyMost:0.0})");
            }
            var setting = TuneSettings[run];
            tuneScores.Add((setting, score));
            tuneLines.Add($"_MassHz {setting.x}, _MassDamp {setting.y}, _Limit {setting.z}: {score:0.00} cm; " + string.Join("; ", said));
            Debug.Log("[Jelly] tune " + tuneLines.Last());
            string dir = SessionState.GetString(K + "out", "Jelly");
            File.WriteAllLines(Path.Combine(dir, $"trace_{setting.x}_{setting.y}_{setting.z}.csv"), csv);
        }

        static Vector3Int breastN;
        static float breastSpacing, restMost, pokeMost, swingMost, noise, poked;
        // Whether the squash still moves anything the jelly draws: the picture with the squash on
        // against the same frame with it off, the jelly on in both; and the same picture twice.
        static readonly List<string> squashOver = new List<string>();
        static Vector3[] innerOff, innerOn, regionsAt;
        static Vector3 secondAt;
        // The own arm's stage: the arm's rest, the state before it, and what it found.
        static Quaternion[] armRest;
        static Vector4[] ownBefore;
        static string ownSaid = "not reached";
        static Vector4[] throughBefore;
        static readonly float[] ownThrough = new float[3];
        static readonly float[] clap = new float[3];
        static float clapCell;
        static readonly float[] grab = new float[5];
        static int grabbed;
        static Vector3 grabFrom, grabMass;
        static int grabAt;
        static float ownShake, ownMost;
        static float handsApart, handsCloser, handsOuter, handsInner, handsVolume, slapMost, slapLater, solid0;
        static readonly float[] pressShake = new float[3], pressMost = new float[3], pressAnchor = new float[3], pressRise = new float[3];
        static float riseFrom;
        static Vector3[] pressWas;
        static Vector3 anchorWas;
        static bool anchorOk = true;
        static readonly List<Texture2D> breastShots = new List<Texture2D>();
        // The far switch as the client's parser works it, and the viewer's camera for the view test.
        static CVRTexturePropertyParser breastParser;
        static ComputeBuffer viewBuffer;
        static readonly List<string> viewSaid = new List<string>();
        static readonly List<bool> viewRan = new List<bool>();
        static float viewBack, halfMost, probeBefore, probeDent, probeMoved;
        static int halfOn, halfFrames, probeSlots, probeSlotsBefore, probePushed, bothSlots, ringsSlow, ringsProbe, ringsBoth;
        static readonly List<string> probeSaid = new List<string>(), probeSeries = new List<string>(), ringSaid = new List<string>();
        static bool probed, remoteHand;
        static Vector4[] wallRest;
        static readonly float[] wallDent = new float[4];
        static GameObject breastWall;
        static Transform remoteBall;
        static Vector3 remoteWrist;
        static int probePair;
        static string probeCost;
        static Vector2 ringWas;
        static List<Transform> probeTs;
        static List<Vector3> probeRest;
        static List<Transform> probeHang;
        // When the parser switched the steps off and on, as the rest and the view decide.
        static readonly List<string> restSaid = new List<string>();
        static bool wasStepping;

        static void HookBreasts()
        {
            var avatar = Object.FindObjectsOfType<CVRAvatar>().First(a => YapsSquash.Has(a.transform));
            swing = avatar.transform;
            var animator = avatar.GetComponentInChildren<Animator>();
            // Held in the scene's pose and the rest of its cloth still, so only the tissue moves the
            // pixels: all but the pair's own cloth, left on where it swings the regions' bones, and
            // the probes', which move only when a collider reaches them.
            animator.enabled = false;
            var swings = new HashSet<Behaviour>(PairCloth(avatar.transform, Wanted()).Where(b => b.enabled));
            swings.UnionWith(avatar.GetComponentsInChildren<Behaviour>(true).Where(b => b.name.StartsWith("Jelly Probes")));
            foreach (var b in avatar.GetComponentsInChildren<MonoBehaviour>(true))
                if ((b.GetType().Name.StartsWith("MagicaCloth") || b.GetType().Name == "DynamicBone") && !swings.Contains(b)) b.enabled = false;
            jelly = avatar.GetComponentsInChildren<Transform>(true).First(t => t.name == Path.GetFileNameWithoutExtension(Prefab));
            breastRegions = Enumerable.Range(0, 2).Select(r => avatar.GetComponentsInChildren<Transform>(true).First(t => t.name == $"Jelly Region {r}")).ToArray();
            // Riding bones, the step is handed the bones and the regions' places on them; the regions
            // themselves are put there each frame, as everything here measures from them.
            var anchors = jelly.GetComponentInChildren<CVRMaterialDataProvider>(true);
            breastBones = anchors.transforms;
            breastOn = Enumerable.Range(0, 2).Select(r =>
            {
                var at = anchors.materials[0].GetVector($"_JellyOffsetAt{r}");
                var q = anchors.materials[0].GetVector($"_JellyOffsetTurn{r}");
                return at.w > 0f ? Matrix4x4.TRS(at, new Quaternion(q.x, q.y, q.z, q.w), Vector3.one * at.w) : Matrix4x4.identity;
            }).ToArray();
            CarryRegions();
            breastParser = jelly.GetComponentInChildren<CVRTexturePropertyParser>(true);
            viewSaid.Clear();
            viewRan.Clear();
            viewBack = 0f;
            halfMost = 0f;
            halfOn = halfFrames = 0;
            probeBefore = probeDent = probeMoved = 0f;
            probeSlots = probeSlotsBefore = 0;
            probeSaid.Clear();
            probeSeries.Clear();
            probed = false;
            restSaid.Clear();
            wasStepping = true;
            secondAt = breastRegions[1].position;
            regionsAt = breastRegions.Select(r => swing.InverseTransformPoint(r.position)).ToArray();
            innerOff = innerOn = null;
            foreach (var rt in new[] { "/Jelly_State.renderTexture", "/Jelly_State_B.renderTexture" })
            {
                var was = RenderTexture.active;
                RenderTexture.active = AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + rt);
                GL.Clear(false, true, Color.clear);
                RenderTexture.active = was;
            }
            var lattice = YapsSquash.Materials(swing).First(m => m.GetVector("_YAPS_JellyLattice").x > 0).GetVector("_YAPS_JellyLattice");
            breastN = new Vector3Int((int) lattice.x, (int) lattice.y, (int) lattice.z);
            breastSpacing = lattice.w;
            var look = new GameObject("Jelly breasts camera");
            // Back by twice the pair's width, whatever the avatar's size.
            float wide = Vector3.Distance(breastRegions[0].position, breastRegions[1].position) + (breastN.x - 1) * breastSpacing;
            var middle = 0.5f * (breastRegions[0].position + breastRegions[1].position) + jelly.forward * (0.5f * (breastN.z - 1) * breastSpacing);
            look.transform.position = middle + jelly.forward * (2f * wide) + jelly.up * (0.3f * wide);
            look.transform.LookAt(middle);
            eye = look.AddComponent<Camera>();
            eye.enabled = false;
            eye.clearFlags = CameraClearFlags.SolidColor;
            eye.backgroundColor = new Color(0.2f, 0.2f, 0.24f);
            eye.nearClipPlane = 0.05f;
            eye.fieldOfView = 35f;
            eye.targetTexture = new RenderTexture(480, 360, 24);
        }

        // Particle p of region r in a read state, packed a block of rows a region.
        static Vector4 Particle(Vector4[] s, int r, int p) => s[(r * (Side(breastN) + 1) + p / Row) * 2 * Row + p % Row];

        // How far the furthest particle of any region is from its rest on its anchor, from column x0 up
        // to x1 if given.
        static float Furthest(Vector4[] s, Transform[] anchors, int x0 = 0, int x1 = int.MaxValue)
        {
            var n = breastN;
            float most = 0f;
            for (int r = 0; r < anchors.Length; r++)
                for (int k = 0; k < n.z; k++)
                    for (int j = 0; j < n.y; j++)
                        for (int i = x0; i < Mathf.Min(x1, n.x); i++)
                        {
                            int p = (k * n.y + j) * n.x + i;
                            var rest = new Vector3((i - 0.5f * (n.x - 1)) * breastSpacing, (j - 0.5f * (n.y - 1)) * breastSpacing, k * breastSpacing);
                            most = Mathf.Max(most, ((Vector3) Particle(s, r, p) - anchors[r].localToWorldMatrix.MultiplyPoint3x4(rest)).magnitude);
                        }
            return most;
        }

        // Region r's lattice volume, each cell's edges averaged; only ever read as a ratio.
        static float Volume(Vector4[] s, int r)
        {
            var n = breastN;
            float sum = 0f;
            Vector3 P(int i, int j, int k) => Particle(s, r, (k * n.y + j) * n.x + i);
            for (int k = 0; k + 1 < n.z; k++)
                for (int j = 0; j + 1 < n.y; j++)
                    for (int i = 0; i + 1 < n.x; i++)
                    {
                        var ex = P(i + 1, j, k) - P(i, j, k) + P(i + 1, j + 1, k) - P(i, j + 1, k) + P(i + 1, j, k + 1) - P(i, j, k + 1) + P(i + 1, j + 1, k + 1) - P(i, j + 1, k + 1);
                        var ey = P(i, j + 1, k) - P(i, j, k) + P(i + 1, j + 1, k) - P(i + 1, j, k) + P(i, j + 1, k + 1) - P(i, j, k + 1) + P(i + 1, j + 1, k + 1) - P(i + 1, j, k + 1);
                        var ez = P(i, j, k + 1) - P(i, j, k) + P(i + 1, j, k + 1) - P(i + 1, j, k) + P(i, j + 1, k + 1) - P(i, j + 1, k) + P(i + 1, j + 1, k + 1) - P(i + 1, j + 1, k);
                        sum += Vector3.Dot(ex, Vector3.Cross(ey, ez)) / 64f;
                    }
            return sum;
        }

        // The share of the pictures' pixels that differ by more than a few levels.
        static float Changed(Texture2D a, Texture2D b)
        {
            var x = a.GetPixels32();
            var y = b.GetPixels32();
            int moved = 0;
            for (int i = 0; i < x.Length; i++)
                if (Mathf.Abs(x[i].r - y[i].r) + Mathf.Abs(x[i].g - y[i].g) + Mathf.Abs(x[i].b - y[i].b) > 12) moved++;
            return moved / (float) x.Length;
        }

        static void CarryRegions()
        {
            for (int r = 0; r < 2; r++)
                if (breastBones[r] != breastRegions[r])
                {
                    var o = breastBones[r].localToWorldMatrix * breastOn[r];
                    breastRegions[r].SetPositionAndRotation(o.GetColumn(3), o.rotation);
                }
        }

        static void OnLateBreasts()
        {
            CarryRegions();
            var n = breastN;
            // The client's parser, by hand since the CCK's never runs: the far switch read back each
            // frame and the steps' blitters set by it.
            bool stepping = true;
            if (breastParser != null && breastParser.texture is RenderTexture switched)
            {
                var sw = Read(switched);
                foreach (var task in breastParser.tasks)
                    if (task.component is Behaviour b && task.propertyName == "enabled") stepping &= b.enabled = sw[task.x].x >= 0.5f;
                if (stepping != wasStepping && restSaid.Count < 40 && frame < BreastFrames - ViewFrames + 260) restSaid.Add($"{(stepping ? "on" : "off")} {frame}");
                wasStepping = stepping;
            }
            // Last, the viewer's camera 3 m in front of the pair: looking at it, away, back at it, and
            // then 1.2 m off looking away, within the near limit.
            if (frame >= BreastFrames - ViewFrames)
            {
                int v = frame - (BreastFrames - ViewFrames);
                // The view alone decides here: no rest.
                if (v == 0)
                    foreach (var c in jelly.GetComponentInChildren<CVRRenderController>(true).cvrRenderComponents.OfType<CVRBlitter>())
                        c.blitMaterial.SetFloat("_Rest", 0f);
                var middle = 0.5f * (breastRegions[0].position + breastRegions[1].position);
                bool close = v >= 200;
                var cam = middle + swing.forward * (close ? 1.2f : 3f);
                var q = Quaternion.LookRotation(v >= 60 && v < 120 || close ? cam - middle : middle - cam, Vector3.up);
                viewBuffer ??= new ComputeBuffer(1, 64);
                viewBuffer.SetData(new[] { 0f, 0f, 0f, cam.x, cam.y, cam.z, cam.x, cam.y, cam.z, q.x, q.y, q.z, q.w, 0f, 0f, 0f });
                Shader.SetGlobalBuffer("_CVRRenderingBuffer", viewBuffer);
                if (v == 59 || v == 119 || v == 199 || v == 259)
                {
                    viewRan.Add(stepping);
                    viewSaid.Add($"{(v < 60 ? "looking at it" : v < 120 ? "looking away" : v < 200 ? "looking back" : "1.2 m off, looking away")}, the steps {(stepping ? "on" : "off")}");
                }
                if (v >= 125 && v < 200) viewBack = Mathf.Max(viewBack, Furthest(Read(AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + "/Jelly_State.renderTexture")), breastRegions));
                // Then the viewer's head 3 m off, past the half rate's 2 m: the steps every other
                // frame, each taking the frame it missed.
                if (v >= 280 && v < 360)
                {
                    halfFrames++;
                    if (stepping) halfOn++;
                    var st = Read(AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + "/Jelly_State.renderTexture"));
                    halfMost = Mathf.Max(halfMost, st[Side(n) * 2 * Row + 25].w);
                }
                // Last, another player's collider: a ball of a hand's size put in the probes' cloth and
                // dynamic bones as the client puts it, from 7 cm off the first breast's frontmost probe to
                // 3 cm past it, with no player's hand in the arrays, so only the probes can dent it.
                // Then the same ball as a slap, 10 cm in three frames, and that slap again with a
                // player's hand on its path, as the client gives a hand both: a ring each, the second
                // the arrays', the probes' taken for it.
                // Or, its probes feeling the world (-jellyWorld), a wall slid 3 cm into the first's
                // front, held and taken away, a kinematic body as a moving world solid is: it should
                // dent the first, barely move the second, and the dent spring back once it goes.
                bool balls = jelly.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("Jelly Probe Ball"));
                if (balls && v >= 360 && v <= 535)
                {
                    var box = jelly.GetComponentsInChildren<CVRMaterialDataProvider>(true).First(q => q.transforms != null && q.transforms.Length > 0).materials[0].GetVector("_JellyBox0");
                    if (breastWall == null)
                    {
                        breastWall = new GameObject("Wall");
                        // 2 cm, a pane of glass, thinner than a probe ball travels.
                        breastWall.AddComponent<BoxCollider>().size = new Vector3(2f * box.x + 0.02f, 2f * box.y + 0.02f, 0.02f);
                        breastWall.AddComponent<Rigidbody>().isKinematic = true;
                    }
                    float gap = v < 380 ? 0.15f : v < 410 ? Mathf.Lerp(0.15f, -0.03f, (v - 380) / 30f) : v < 445 ? -0.03f : Mathf.Lerp(-0.03f, 0.15f, Mathf.Clamp01((v - 445) / 15f));
                    breastWall.transform.SetPositionAndRotation(breastRegions[0].TransformPoint(0f, 0f, box.z + 0.01f + gap), breastRegions[0].rotation);
                    var st = Read(AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + "/Jelly_State.renderTexture"));
                    if (v == 375) wallRest = st;
                    float Off(int r)
                    {
                        float most = 0f;
                        for (int q = 0; q < n.x * n.y * n.z; q++)
                            most = Mathf.Max(most, ((Vector3) Particle(st, r, q) - (Vector3) Particle(wallRest, r, q)).magnitude);
                        return most;
                    }
                    if (v >= 425 && v < 445)
                    {
                        wallDent[0] = Mathf.Max(wallDent[0], Off(0));
                        wallDent[1] = Mathf.Max(wallDent[1], Off(1));
                        // How far its skin stands past the wall's face where the wall reached it: the
                        // particle nearest each probe whose ball the wall pushed 2 cm or more. Over
                        // every particle, the lattice's corners in the air round a round breast, which
                        // no skin is near, read 25 mm through.
                        var wparts = jelly.GetComponentsInChildren<Transform>(true);
                        foreach (var wball in wparts.Where(wt => wt.name.StartsWith("Jelly Probe Ball 0 ")))
                        {
                            var wrest = wparts.First(wt => wt.name == wball.name.Replace("Ball", "Rest"));
                            var wtip = wball.Find(wball.name.Replace("Ball", "Tip"));
                            var wpush = wball.position - wrest.position;
                            if (wtip == null || wpush.magnitude < 0.02f) continue;
                            var wtipRest = wtip.position - wpush;
                            int wq = Enumerable.Range(0, n.x * n.y * n.z).OrderBy(wi => ((Vector3) Particle(wallRest, 0, wi) - wtipRest).sqrMagnitude).First();
                            wallDent[3] = Mathf.Max(wallDent[3], breastRegions[0].InverseTransformPoint(Particle(st, 0, wq)).z - (box.z - 0.03f));
                        }
                    }
                    if (v >= 520) wallDent[2] = Mathf.Max(wallDent[2], Off(0));
                    if (v == 535) Object.DestroyImmediate(breastWall);
                }
                var nets = jelly.GetComponentsInChildren<Behaviour>(true).Where(c => c.name.StartsWith("Jelly Probes")).ToList();
                if (nets.Count > 0 && v >= 360 && v <= 535)
                {
                    probed = true;
                    if (v == 360)
                    {
                        // In the helper's order, the first the first breast's frontmost, where its skin is
                        // (the lattice's front is a cube's, 20 cm past it).
                        probeTs = jelly.GetComponentsInChildren<Transform>(true).Where(t => Regex.IsMatch(t.name, @"^Jelly Probe Tip \d \d+$")).ToList();
                        probeHang = probeTs.Select(t => t.parent.parent).ToList();
                        probeRest = probeTs.Select((t, k) => probeHang[k].InverseTransformPoint(t.position)).ToList();
                        // Aimed between the frontmost and its nearest, so the ball pushes both and the
                        // scan must make them one hand.
                        probePair = Enumerable.Range(1, probeTs.Count / 2 - 1).OrderBy(k => (probeTs[k].position - probeTs[0].position).sqrMagnitude).First();
                        remoteBall = new GameObject("Remote hand").transform;
#if AVATARBRIDGE_MAGICA
                        foreach (var net in nets.OfType<MagicaCloth2.MagicaCloth>())
                        {
                            var ball = remoteBall.gameObject.AddComponent<MagicaCloth2.MagicaSphereCollider>();
                            ball.SetSize(new Vector3(0.045f, 0f, 0f));
                            net.SerializeData.colliderCollisionConstraint.colliderList.Add(ball);
                            net.SetParameterChange();
                        }
#endif
                        probeSaid.Add($"{string.Join(" and ", nets.Select(c => c.GetType().Name))}, {probeTs.Count} probes, the frontmost " +
                                      $"{100f * Vector3.Dot(probeTs[0].position - breastRegions[0].position, breastRegions[0].forward):0.0} cm out on its region");
                    }
                    var front = 0.5f * (probeHang[0].TransformPoint(probeRest[0]) + probeHang[probePair].TransformPoint(probeRest[probePair]));
                    // The second slap from 15 cm off, the hand with it: from 7 cm a palm already
                    // touches the box, so it closed on nothing and its ring was the frame it came.
                    float slapped = Mathf.Clamp01(1f - Mathf.Abs(v - 517) / 4f);
                    float pressed = Mathf.Clamp01((v - 370) / 30f) - Mathf.Clamp01((v - 430) / 30f) + Mathf.Clamp01(1f - Mathf.Abs(v - 487) / 3f);
                    float off = v >= 505 ? 0.15f - 0.18f * slapped : 0.07f - 0.1f * pressed;
                    remoteBall.position = front + breastRegions[0].forward * (0.045f + off);
                    remoteHand = v >= 505 && v < 535;
                    remoteWrist = front + breastRegions[0].forward * (0.126f + off);
                    var st = Read(AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + "/Jelly_State.renderTexture"));
                    // The first breast's rings: a slot's time new since last frame is one fired, unless
                    // under 0.2 s after its last, the same ring deepened.
                    var rings = new Vector2(st[Side(n) * 2 * Row + 80].w, st[Side(n) * 2 * Row + 82].w);
                    if (v > 360)
                        for (int e = 0; e < 2; e++)
                            if (rings[e] != ringWas[e])
                            {
                                bool deepened = ringWas[e] > 0 && rings[e] - ringWas[e] < 0.2f;
                                if (deepened) { } else if (v < 470) ringsSlow++; else if (v < 505) ringsProbe++; else ringsBoth++;
                                ringSaid.Add($"{v}: {(deepened ? "deepened to " : "")}{1000f * st[Side(n) * 2 * Row + 81 + 2 * e].w:0.0} mm deep, " +
                                             $"{100f * ((Vector3) st[Side(n) * 2 * Row + 80 + 2 * e] - front).magnitude:0.0} cm off the ball's spot");
                            }
                    ringWas = rings;
                    float most = Furthest(st, breastRegions);
                    int slots = 0;
                    for (int e = 0; e < 8; e++) if (st[Side(n) * 2 * Row + 2 * e + 1].w > 0.5f) slots++;
                    float moved = probeTs.Select((t, k) => (t.position - probeHang[k].TransformPoint(probeRest[k])).magnitude).Max();
                    // As the step reads them, the first breast's, a frame late through the helper's skin.
                    int mine = probeTs.Count / 2;
                    var read = Enumerable.Range(0, Mathf.Min(mine, 24)).Select(k => st[Side(n) * 2 * Row + 88 + k].w).ToList();
                    if (v == 365 || v == 410 || v == 450)
                        probeSaid.Add($"{v}: the ball {100f * (0.07f - 0.1f * pressed):0.0} cm off, the probes moved {1000f * moved:0.0} mm, the step reads them " +
                                      $"{string.Join(" ", read.Select(g => $"{1000f * g:0.0}"))} mm, {slots} slots held");
                    if (v < 370)
                    {
                        probeBefore = Mathf.Max(probeBefore, most);
                        probeSlotsBefore = Mathf.Max(probeSlotsBefore, slots);
                    }
                    if (v % 10 == 0) probeSeries.Add($"{v} {1000f * most:0}");
                    // The hand and its collider together: the arrays' slot, the probes' dropped for it.
                    // From once the first slap's probes have let go.
                    if (v >= 512 && v < 535) bothSlots = Mathf.Max(bothSlots, slots);
                    // What the chain costs while a collider presses two probes, against its cost at rest
                    // below: the state put back after, so the verify carries on from where it was.
                    if (v == 410)
                    {
                        var steps = jelly.GetComponentInChildren<CVRRenderController>(true).cvrRenderComponents.OfType<CVRBlitter>().ToList();
                        var held = steps.SelectMany(c => new[] { c.originTexture, c.destinationTexture }).Distinct().Select(tex =>
                        {
                            var copy = new RenderTexture(tex.descriptor);
                            copy.Create();
                            Graphics.CopyTexture(tex, copy);
                            return (tex, copy);
                        }).ToList();
                        var times = Enumerable.Range(0, 5).Select(round => Timed(() => { foreach (var c in steps) Graphics.Blit(c.originTexture, c.destinationTexture, c.blitMaterial); }, steps[0].destinationTexture, 600, out _)).ToList();
                        foreach (var (tex, copy) in held)
                        {
                            Graphics.CopyTexture(copy, tex);
                            copy.Release();
                            Object.DestroyImmediate(copy);
                        }
                        probeCost = Spread(times);
                    }
                    if (v >= 400 && v < 430)
                    {
                        probeDent = Mathf.Max(probeDent, most);
                        probeSlots = Mathf.Max(probeSlots, slots);
                        probeMoved = Mathf.Max(probeMoved, moved);
                        probePushed = Mathf.Max(probePushed, read.Count(g => g > 0.002f));
                    }
                    if (v == 535)
                    {
                        remoteHand = false;
#if AVATARBRIDGE_MAGICA
                        foreach (var net in nets.OfType<MagicaCloth2.MagicaCloth>())
                        {
                            net.SerializeData.colliderCollisionConstraint.colliderList.RemoveAll(c => c != null && c.transform == remoteBall);
                            net.SetParameterChange();
                        }
#endif
                        Object.Destroy(remoteBall.gameObject);
                    }
                }
            }
            // One player, its finger into the first region's breast from the front, 3 cm past the
            // lattice's front less its centimetre of room, between frames 120 and 210.
            var fwd = swing.forward;
            var spot = breastRegions[0].position + breastRegions[0].forward * ((n.z - 1) * breastSpacing - 0.01f);
            float into = frame < 120 || frame >= 210 ? -1f : Mathf.Min((frame - 120) / 30f, 1f);
            var hand = Enumerable.Repeat(new Vector4(50f, 50f, 50f, 0f), 255).ToArray();
            var far = (Vector4[]) hand.Clone();
            var chestAt = (Vector4[]) hand.Clone();
            var root = Enumerable.Repeat(new Vector4(50f, 0f, 50f, 1.6f), 255).ToArray();
            if (into >= 0f)
            {
                // The tip reaches 0.126 m past the wrist, away from the chest given here.
                var wrist = spot + fwd * (0.126f + 0.15f - 0.18f * into);
                hand[0] = wrist;
                chestAt[0] = wrist + fwd * 0.45f;
                root[0] = new Vector4(wrist.x, swing.position.y, wrist.z, 1.6f) + (Vector4) (fwd * 0.45f);
            }
            Shader.SetGlobalVectorArray("_CVR_PlayerLeftHandPositions", hand);
            Shader.SetGlobalVectorArray("_CVR_PlayerRightHandPositions", far);
            Shader.SetGlobalVectorArray("_CVR_PlayerChestPositions", chestAt);
            Shader.SetGlobalVectorArray("_CVR_PlayerRootPositions", root);
            var heads = new Vector4[255];
            if (frame >= BreastFrames - ViewFrames + 260 && frame < BreastFrames - ViewFrames + 360) heads[0] = 0.5f * (breastRegions[0].position + breastRegions[1].position) + swing.forward * 3f;
            Shader.SetGlobalVectorArray("_CVR_PlayerHeadPositions", heads);
            // Then, once the turn has rung out, the film's squeeze twice, the pair's contact off for the
            // first: two players, a hand each on the pair's outer sides, pointing back at the chest,
            // from 3 cm off the skin to 13 cm in (SquashPlayProbe's shot). The masses go alike both
            // times, so what differs is the contact's.
            float squeezing = frame >= 600 && frame < 750 ? Mathf.Clamp01((frame - 600) / 30f) - Mathf.Clamp01((frame - 690) / 30f)
                : frame >= 870 && frame < 1050 ? Mathf.Clamp01((frame - 870) / 30f) - Mathf.Clamp01((frame - 990) / 30f)
                // and once more in six frames, which claps the pair together
                : frame >= 2350 && frame < 2440 ? Mathf.Clamp01((frame - 2350) / 6f) - Mathf.Clamp01((frame - 2400) / 30f) : -1f;
            if (squeezing >= 0f)
            {
                float half = 0.5f * (n.x - 1) * breastSpacing - 0.01f, r = 0.05f;
                var outward = breastRegions[0].position - breastRegions[1].position;
                outward.y = 0f;
                outward.Normalize();
                for (int side = 0; side < 2; side++)
                {
                    var o = side == 0 ? outward : -outward;
                    var at = breastRegions[side].position + breastRegions[side].forward * (0.5f * (n.z - 1) * breastSpacing) + o * half;
                    var palm = at + fwd * 0.1f + o * (r + Mathf.Lerp(0.03f, -0.13f, squeezing));
                    hand[side] = palm;
                    chestAt[side] = palm + fwd * 0.45f;
                    root[side] = new Vector4(palm.x, swing.position.y, palm.z, 1.6f) + (Vector4) (fwd * 0.45f);
                }
            }
            // Then one slap: a palm 5 cm into the first breast's outer side and out again, a tenth of a
            // second, for how far the whole goes and how long it swings.
            if (frame >= 1060 && squeezing < 0f)
            {
                float slap = Mathf.Clamp01(1f - Mathf.Abs(frame - 1086) / 5f);
                var outward = breastRegions[0].position - breastRegions[1].position;
                outward.y = 0f;
                outward.Normalize();
                var at = breastRegions[0].position + breastRegions[0].forward * (0.5f * (n.z - 1) * breastSpacing) + outward * (0.5f * (n.x - 1) * breastSpacing - 0.01f);
                var palm = at + fwd * 0.1f + outward * (0.05f + Mathf.Lerp(0.03f, -0.05f, slap));
                hand[0] = palm;
                chestAt[0] = palm + fwd * 0.45f;
                root[0] = new Vector4(palm.x, swing.position.y, palm.z, 1.6f) + (Vector4) (fwd * 0.45f);
                hand[1] = new Vector4(50f, 50f, 50f, 0f);
            }
            // Then a palm pressed deep under the first, as the film's lift, three times and held: the
            // pressure on, off, and on with a rigid fit, for which of them shakes a held press.
            int lift = frame < 1300 ? -1 : (frame - 1300) / 180;
            if (lift >= 0 && lift < 3)
            {
                float lifted = Mathf.Clamp01((frame - 1300 - 180 * lift) / 40f);
                var up = breastRegions[0].up;
                var at = breastRegions[0].position + breastRegions[0].forward * (0.5f * (n.z - 1) * breastSpacing) - up * (0.5f * (n.y - 1) * breastSpacing - 0.01f);
                var palm = at + fwd * 0.12f - up * (0.05f + Mathf.Lerp(0.03f, lift == 2 ? -0.12f : -0.08f, lifted));
                hand[0] = palm;
                chestAt[0] = palm + fwd * 0.44f - up * 0.1f;
                root[0] = new Vector4(chestAt[0].x, swing.position.y, chestAt[0].z, 1.6f);
                hand[1] = new Vector4(50f, 50f, 50f, 0f);
            }
            // Then the wearer's own arm across the first's lower front, as the film's own hand: the
            // wrist taken to between the pair, held, put back.
            if (frame >= 1840)
            {
                var anim = swing.GetComponentInChildren<Animator>();
                bool left = Vector3.Dot(breastRegions[0].position - swing.position, swing.right) < 0f;
                var upperArm = anim.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                var lowerArm = anim.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                var wristBone = anim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (frame == 1840) armRest = new[] { upperArm.rotation, lowerArm.rotation };
                upperArm.rotation = armRest[0];
                lowerArm.rotation = armRest[1];
                float across = Mathf.Clamp01((frame - 1840) / 40f) - Mathf.Clamp01((frame - 2000) / 30f);
                var up = breastRegions[0].up;
                var under = breastRegions[0].position + breastRegions[0].forward * (0.6f * (n.z - 1) * breastSpacing) - up * (0.3f * (n.y - 1) * breastSpacing);
                if (across > 0f) TwoBone(upperArm, lowerArm, wristBone, Vector3.Lerp(wristBone.position, under + 0.5f * (secondAt - breastRegions[0].position), across), swing.forward);
            }
            // Then the wearer's own hand straight into the first and on through to the chest, held,
            // and back out: held at the deepest, nothing torn, let go once through.
            if (frame >= 2040 && frame < 2340)
            {
                var anim = swing.GetComponentInChildren<Animator>();
                bool left = Vector3.Dot(breastRegions[0].position - swing.position, swing.right) < 0f;
                var upperArm = anim.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                var lowerArm = anim.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                var wristBone = anim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                upperArm.rotation = armRest[0];
                lowerArm.rotation = armRest[1];
                int since = frame - 2040;
                float push = since < 150 ? since / 150f : since < 200 ? 1f : 1f - Mathf.Clamp01((since - 200) / 60f);
                var skin = jelly.GetComponentsInChildren<CVRMaterialDataProvider>(true).First(q => q.transforms != null && q.transforms.Length > 0).materials[0].GetVector("_JellyBox0");
                var outward = breastRegions[0].forward;
                var front = breastRegions[0].TransformPoint(0f, 0f, skin.z);
                TwoBone(upperArm, lowerArm, wristBone, Vector3.Lerp(front + outward * 0.08f, breastRegions[0].position - outward * 0.02f, push), swing.forward);
            }
            // Then a hand on the first's front, closed to a fist there, pulled 8 cm straight out over
            // a third of a second, held, and opened as it goes: what it gripped follows it, the whole
            // leans after, and let go it springs back with a ring. Its far side a centimetre into the
            // skin however closed, so closing neither presses nor lets go of it. Closed in one frame,
            // as a controller's grip button does: closed over five, it gripped only by the frame.
            if (frame >= 2450 && frame < 2740)
            {
                var box = jelly.GetComponentsInChildren<CVRMaterialDataProvider>(true).First(q => q.transforms != null && q.transforms.Length > 0).materials[0].GetVector("_JellyBox0");
                var outward = breastRegions[0].forward;
                var front = breastRegions[0].TransformPoint(0f, 0f, box.z);
                float pulled = frame < 2560 ? 0.08f * Mathf.SmoothStep(0f, 1f, (frame - 2480) / 30f) : 0.08f + 0.3f * Mathf.Clamp01((frame - 2560) / 20f);
                float closed = frame < 2470 ? 0.6f : frame < 2560 ? 1f : Mathf.Lerp(1f, 0f, (frame - 2560) / 5f);
                var wrist = front + outward * (Mathf.Lerp(0.11f, 0.06f, closed) * 1.6f - 0.01f + pulled);
                hand[0] = new Vector4(wrist.x, wrist.y, wrist.z, frame < 2565 ? closed : -1f);
                chestAt[0] = wrist + outward * 0.45f;
                root[0] = new Vector4(chestAt[0].x, swing.position.y, chestAt[0].z, 1.6f);
                hand[1] = new Vector4(50f, 50f, 50f, 0f);
            }
            if (remoteHand)
            {
                hand[0] = remoteWrist;
                chestAt[0] = remoteWrist + breastRegions[0].forward * 0.45f;
                root[0] = new Vector4(chestAt[0].x, swing.position.y, chestAt[0].z, 1.6f);
            }
            Shader.SetGlobalVectorArray("_CVR_PlayerLeftHandPositions", hand);
            Shader.SetGlobalVectorArray("_CVR_PlayerChestPositions", chestAt);
            Shader.SetGlobalVectorArray("_CVR_PlayerRootPositions", root);
            Shader.SetGlobalVector("CVRGlobalParams1", new Vector4(0f, frame >= 1060 ? 1f : squeezing >= 0f ? 2f : into >= 0f ? 1f : 0f, 0f, 0f));
            // Then the avatar turning on the spot, as Verify turns the jelly.
            float t = Mathf.Max(frame - 230, 0) / 90f;
            swing.rotation = Quaternion.Euler(0f, 50f * Mathf.Sin(t * Mathf.PI * 2f * 0.7f) * Mathf.Min(t * 3f, 1f) * Mathf.Clamp01((330 - frame) / 30f), 0f);
            var blits = jelly.GetComponentInChildren<CVRRenderController>(true).cvrRenderComponents.OfType<CVRBlitter>().Select(b => b.blitMaterial).ToList();
            if (frame == 580) foreach (var b in blits) b.SetFloat("_Pair", 0f);
            if (frame == 740) foreach (var b in blits) b.SetFloat("_Pair", 1f);
            if (frame == 0) solid0 = blits[0].GetFloat("_Solid");
            if (frame == 1480) foreach (var b in blits) b.SetFloat("_Pair", 0f);
            if (frame == 1660) foreach (var b in blits) b.SetFloat("_Pair", 1f);
            eye.Render();
            var stateRt = AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + "/Jelly_State.renderTexture");
            if (frame == 110 || frame == 119 || frame == 160 || frame == 205 || frame == 250 || frame == 262 || frame == 274 || frame == 286 || frame == 599 || frame == 689 || frame == 989)
            {
                var was = RenderTexture.active;
                RenderTexture.active = eye.targetTexture;
                var png = new Texture2D(eye.targetTexture.width, eye.targetTexture.height, TextureFormat.RGB24, false);
                png.ReadPixels(new Rect(0, 0, png.width, png.height), 0, 0);
                RenderTexture.active = was;
                breastShots.Add(png);
            }
            if (frame == 119)
            {
                var s = Read(stateRt);
                restMost = Furthest(s, breastRegions);
                int side = Side(n);
                for (int r = 0; r < 2; r++)
                {
                    var placed = breastRegions[r].localToWorldMatrix;
                    int at = (r * (side + 1) + side) * 2 * Row;
                    for (int c = 0; c < 4; c++)
                        anchorOk &= ((Vector3) s[at + 26 + c] - (Vector3) placed.GetColumn(c)).magnitude < 1e-4f && s[at + 26 + c].w == 1f;
                }
            }
            if (frame == 119)
            {
                // What runs the step, for a lattice that never moved.
                var s = Read(stateRt);
                int side = Side(n);
                var control = jelly.GetComponentInChildren<CVRRenderController>(true);
                var provider = jelly.GetComponentInChildren<CVRMaterialDataProvider>(true);
                var handed = control.cvrRenderComponents.OfType<CVRBlitter>().First().blitMaterial.GetMatrixArray("_JellyAnchor");
                Debug.Log($"[Jelly] the step: controller {control.isActiveAndEnabled}, provider {provider.isActiveAndEnabled} with {provider.materials.Length} materials " +
                          $"and {provider.transforms.Length} transforms, {Object.FindObjectsOfType<MonoBehaviour>().Count(b => b.GetType().Name == "LateEventsManager")} late events managers, " +
                          $"anchor handed {(handed != null ? handed[0].GetColumn(3).ToString("F3") : "none")} against {breastRegions[0].position.ToString("F3")}, particle 0 {s[0].ToString("F3")}, switches {s[side * 2 * Row + 18]}, " +
                          $"centres {s[side * 2 * Row + 30].ToString("F3")} and {s[(2 * side + 1) * 2 * Row + 30].ToString("F3")}, " +
                          $"plane (centres apart, as built, gap, scale) {s[side * 2 * Row + 40].ToString("F4")} and {s[(2 * side + 1) * 2 * Row + 40].ToString("F4")}, " +
                          $"own capsules in reach at rest {(int) s[side * 2 * Row + 41].x} and {(int) s[(2 * side + 1) * 2 * Row + 41].x} (bits), " +
                          $"the rest's count {s[side * 2 * Row + 35].x:0.000} and {s[(2 * side + 1) * 2 * Row + 35].x:0.000} s");
            }
            {
                var s = Read(stateRt);
                int side = Side(n);
                float zc = 0.5f * (n.z - 1) * breastSpacing;
                var line = new System.Text.StringBuilder($"{frame}");
                for (int r = 0; r < 2; r++)
                {
                    var off = (Vector3) s[(r * (side + 1) + side) * 2 * Row + 30] - breastRegions[r].TransformPoint(0f, 0f, zc);
                    line.Append($",{1000f * off.x:0.00},{1000f * off.y:0.00},{1000f * off.z:0.00}");
                }
                line.Append($",{1000f * Furthest(s, breastRegions):0.00}");
                line.Append($",bulk {s[side * 2 * Row + 32].x:0.000} {s[side * 2 * Row + 33].y:0.000} {s[side * 2 * Row + 34].z:0.000} volume {s[side * 2 * Row + 32].w:0.000}");
                if (frame < 6)
                {
                    // What the step was handed against where the regions are, and region 0's particle 0.
                    var handed = jelly.GetComponentInChildren<CVRRenderController>(true).cvrRenderComponents.OfType<CVRBlitter>().First().blitMaterial.GetMatrixArray("_JellyAnchor");
                    line.Append($",handed {(handed != null && handed.Length > 1 ? handed[0].GetColumn(3).ToString("F3") + " " + handed[1].GetColumn(3).ToString("F3") : "none")}");
                    line.Append($" regions {breastRegions[0].position.ToString("F3")} {breastRegions[1].position.ToString("F3")} p0 {Particle(s, 0, 0).ToString("F3")} time {Time.time:0.000}");
                }
                if (frame < 4 || frame % 20 == 0)
                    for (int r = 0; r < 2; r++)
                    {
                        float most = 0f;
                        int which = 0;
                        for (int p = 0; p < n.x * n.y * n.z; p++)
                        {
                            var l = new Vector3Int(p % n.x, p / n.x % n.y, p / (n.x * n.y));
                            var rest = new Vector3((l.x - 0.5f * (n.x - 1)) * breastSpacing, (l.y - 0.5f * (n.y - 1)) * breastSpacing, l.z * breastSpacing);
                            float d = ((Vector3) Particle(s, r, p) - breastRegions[r].TransformPoint(rest)).magnitude;
                            if (d > most) { most = d; which = p; }
                        }
                        var at = Particle(s, r, which);
                        line.Append($",r{r} p{which} ({which % n.x} {which / n.x % n.y} {which / (n.x * n.y)}) {1000f * most:0.0} mm w {at.w:0.000}");
                    }
                // How far region 0's particles moved since the last frame in its own frame, and its
                // anchor in the avatar's: a held press settles to nothing.
                var local = new Vector3[n.x * n.y * n.z];
                for (int p = 0; p < local.Length; p++) local[p] = breastRegions[0].InverseTransformPoint(Particle(s, 0, p));
                float shakeStep = 0f, shakeMost = 0f;
                int shakeAt = 0;
                if (pressWas != null)
                    for (int p = 0; p < local.Length; p++)
                    {
                        float d = (local[p] - pressWas[p]).magnitude;
                        shakeStep += d / local.Length;
                        if (d > shakeMost) { shakeMost = d; shakeAt = p; }
                    }
                var shakeBy = pressWas != null ? 1000f * (local[shakeAt] - pressWas[shakeAt]) : Vector3.zero;
                pressWas = local;
                var anchorNow = swing.InverseTransformPoint(breastRegions[0].position);
                float anchorStep = (anchorNow - anchorWas).magnitude;
                anchorWas = anchorNow;
                line.Append($",step {1000f * shakeStep:0.000} most {1000f * shakeMost:0.00} at ({shakeAt % n.x} {shakeAt / n.x % n.y} {shakeAt / (n.x * n.y)}) by ({shakeBy.x:0.00} {shakeBy.y:0.00} {shakeBy.z:0.00}) anchor {1000f * anchorStep:0.000}");
                if (frame >= 1900 && frame < 1990)
                {
                    ownShake += shakeStep / 90f;
                    ownMost = Mathf.Max(ownMost, shakeMost);
                }
                if (frame == 1299) riseFrom = s[side * 2 * Row + 30].y;
                int hold = (frame - 1300) / 180, inHold = (frame - 1300) % 180;
                if (frame >= 1300 && hold < 3 && inHold >= 60 && inHold < 170)
                {
                    pressShake[hold] += shakeStep / 110f;
                    pressMost[hold] = Mathf.Max(pressMost[hold], shakeMost);
                    pressAnchor[hold] += anchorStep / 110f;
                    pressRise[hold] = Mathf.Max(pressRise[hold], s[side * 2 * Row + 30].y - riseFrom);
                }
                breastTrace.Add(line.ToString());
            }
            if (frame == 205) pokeMost = Furthest(Read(stateRt), breastRegions);
            if (frame == 1839) ownBefore = Read(stateRt);
            if (frame == 2039) throughBefore = Read(stateRt);
            if (frame >= 2040 && frame < 2340)
            {
                var st = Read(stateRt);
                float moved = 0f;
                int torn = 0;
                for (int q = 0; q < n.x * n.y * n.z; q++)
                {
                    int i = q / Row * 2 * Row + q % Row;
                    float d = ((Vector3) st[i] - (Vector3) throughBefore[i]).magnitude;
                    moved = Mathf.Max(moved, d);
                    torn += d > 0.1f ? 1 : 0;
                }
                ownThrough[0] = Mathf.Max(ownThrough[0], moved);
                ownThrough[1] = Mathf.Max(ownThrough[1], torn);
                if (frame >= 2200 && frame < 2240) ownThrough[2] = Mathf.Max(ownThrough[2], moved);
            }
            // The clap: each one's ring started from between them, within 3 cm of the middle of their
            // centres; and how far apart their skins sat as it began, against a cell.
            if (frame >= 2350 && frame < 2440)
            {
                var st = Read(stateRt);
                int sr = Side(n);
                if (frame == 2350)
                {
                    var skins = blits[0].GetVector("_PairSkin");
                    float sized = breastRegions[0].lossyScale.x;
                    clap[0] = Vector3.Distance(st[sr * 2 * Row + 30], st[(2 * sr + 1) * 2 * Row + 30]) - (skins.x + skins.y) * sized;
                    clapCell = blits[0].GetVector("_Lattice").w * sized;
                }
                for (int r = 0; r < 2; r++)
                {
                    int mine = (r * (sr + 1) + sr) * 2 * Row, theirs = ((1 - r) * (sr + 1) + sr) * 2 * Row;
                    var middle = 0.5f * ((Vector3) st[mine + 30] + (Vector3) st[theirs + 30]);
                    for (int k = 0; k < 2; k++)
                        if (Time.timeSinceLevelLoad - st[mine + 80 + 2 * k].w < 0.05f && Vector3.Distance(st[mine + 80 + 2 * k], middle) < 0.03f)
                            clap[1 + r] = Mathf.Max(clap[1 + r], st[mine + 81 + 2 * k].w);
                }
            }
            // The grab: the first's front middle particle and its mass along the pull, against just
            // before the hand came; how long any slot gripped; the ring as it let go; and how far
            // the skin still was from where it began two seconds on.
            if (frame >= 2445 && frame < 2740)
            {
                var st = Read(stateRt);
                int sr = Side(n) * 2 * Row;
                var outward = breastRegions[0].forward;
                if (frame == 2445)
                {
                    // The particle nearest where the fist will hold: the lattice's front is a cube's,
                    // well past the skin.
                    var box = jelly.GetComponentsInChildren<CVRMaterialDataProvider>(true).First(q => q.transforms != null && q.transforms.Length > 0).materials[0].GetVector("_JellyBox0");
                    var held = breastRegions[0].TransformPoint(0f, 0f, box.z) - outward * 0.01f;
                    grabAt = Enumerable.Range(0, n.x * n.y * n.z).OrderBy(q => ((Vector3) Particle(st, 0, q) - held).sqrMagnitude).First();
                }
                Vector3 p = Particle(st, 0, grabAt), c = st[sr + 30];
                if (frame == 2445) { grabFrom = p; grabMass = c; }
                for (int e = 0; e < 8; e++)
                    if (st[sr + 112 + e].w > 0f) { grabbed++; break; }
                if (frame >= 2530 && frame < 2560)
                {
                    grab[0] = Mathf.Max(grab[0], Vector3.Dot(p - grabFrom, outward));
                    grab[1] = Mathf.Max(grab[1], Vector3.Dot(c - grabMass, outward));
                }
                if (frame >= 2560 && frame < 2600)
                    for (int k = 0; k < 2; k++)
                        if (Time.timeSinceLevelLoad - st[sr + 80 + 2 * k].w < 0.05f) grab[2] = Mathf.Max(grab[2], st[sr + 81 + 2 * k].w);
                // Off its modelled rest on the anchor, as the hand came and two seconds after it let go.
                if (frame == 2445 || frame == 2739)
                {
                    int gx = grabAt % n.x, gy = grabAt / n.x % n.y, gz = grabAt / (n.x * n.y);
                    float off = (p - breastRegions[0].localToWorldMatrix.MultiplyPoint3x4(new Vector3((gx - 0.5f * (n.x - 1)) * breastSpacing, (gy - 0.5f * (n.y - 1)) * breastSpacing, gz * breastSpacing))).magnitude;
                    grab[frame == 2445 ? 4 : 3] = off;
                }
            }
            if (frame == 1990)
            {
                var st = Read(stateRt);
                int sr = Side(n) * 2 * Row;
                float moved = 0f;
                for (int q = 0; q < n.x * n.y * n.z; q++)
                {
                    int i = q / Row * 2 * Row + q % Row;
                    moved = Mathf.Max(moved, ((Vector3) st[i] - (Vector3) ownBefore[i]).magnitude);
                }
                var anchorsHanded = jelly.GetComponentsInChildren<CVRMaterialDataProvider>(true).First(q => q.transforms != null && q.transforms.Length > 0);
                var stepMat = anchorsHanded.materials[0];
                ownSaid = $"in reach {(int) st[sr + 41].x} (bits), the provider handing {anchorsHanded.transforms.Length} transforms, radii {stepMat.GetVector("_JellyOwn0") * 1000f:F0} and {stepMat.GetVector("_JellyOwn1") * 1000f:F0} mm, " +
                          $"upper arm {st[sr + 42]:F3} to {st[sr + 43]:F3}, forearm {st[sr + 44]:F3} to {st[sr + 45]:F3}, the first collider {st[sr + 64]:F3} to {st[sr + 65]:F3} radii {stepMat.GetVector("_JellyCol0") * 1000f:F0}; its particles moved {1000f * moved:0.0} mm at most, " +
                          $"held, {1000f * ownShake:0.000} mm a frame on average, {1000f * ownMost:0.00} at most";
            }
            if (frame > 230 && frame <= 330)
            {
                swingMost = Mathf.Max(swingMost, Furthest(Read(stateRt), breastRegions));
                // Riding bones, the cloth swings the anchors: how far, in the avatar's own frame.
                for (int r = 0; r < 2; r++) swingMost = Mathf.Max(swingMost, (swing.InverseTransformPoint(breastRegions[r].position) - regionsAt[r]).magnitude);
            }
            // The first's two columns facing the second.
            int inner = Vector3.Dot(secondAt - breastRegions[0].position, breastRegions[0].right) > 0f ? n.x - 2 : 0;
            // Where those columns' particles are over each squeeze's held second half, averaged.
            if ((frame >= 660 && frame < 690) || (frame >= 960 && frame < 990))
            {
                var st = Read(stateRt);
                var held = frame < 700 ? (innerOff ??= new Vector3[2 * n.y * n.z]) : (innerOn ??= new Vector3[2 * n.y * n.z]);
                int q = 0;
                for (int k = 0; k < n.z; k++)
                    for (int j = 0; j < n.y; j++)
                        for (int i = inner; i < inner + 2; i++)
                            held[q++] += (Vector3) Particle(st, 0, (k * n.y + j) * n.x + i) / 30f;
            }
            // The hands' squeeze: how far the masses closed on each other, the outer side's dent and
            // the inner side's give, the first breast's, each from its rest on the anchor.
            if (frame == 869 || (frame >= 960 && frame < 990))
            {
                var st = Read(stateRt);
                int sideRow = Side(n);
                float apart = Vector3.Distance(st[sideRow * 2 * Row + 30], st[(2 * sideRow + 1) * 2 * Row + 30]);
                if (frame == 869) { handsApart = apart; handsVolume = Volume(st, 0); }
                else
                {
                    int outer = inner == 0 ? n.x - 2 : 0;
                    handsCloser += (handsApart - apart) / 30f;
                    handsOuter += Furthest(st, new[] { breastRegions[0] }, outer, outer + 2) / 30f;
                    handsInner += Furthest(st, new[] { breastRegions[0] }, inner, inner + 2) / 30f;
                    if (frame == 989) handsVolume = Volume(st, 0) / handsVolume;
                }
            }
            // The slap: the first's mass, furthest from its rest after it, and still a second after.
            if (frame >= 1080 && frame < 1290 && Read(stateRt)[Side(n) * 2 * Row + 30].w > 0)
            {
                var st = Read(stateRt);
                float zcm = 0.5f * (n.z - 1) * breastSpacing;
                float off = ((Vector3) st[Side(n) * 2 * Row + 30] - breastRegions[0].TransformPoint(0f, 0f, zcm)).magnitude;
                slapMost = Mathf.Max(slapMost, off);
                if (frame >= 1180) slapLater = Mathf.Max(slapLater, off);
            }
            // At rest, a finger in, turning, the hands' squeeze and the arm across.
            if (frame == 100 || frame == 160 || frame == 300 || frame == 975 || frame == 1990)
            {
                var dented = YapsSquash.Materials(swing).Where(mm => mm.HasProperty("_YAPS_DentPower") && mm.GetFloat("_YAPS_DentPower") > 0).ToList();
                var powers = dented.Select(mm => mm.GetFloat("_YAPS_DentPower")).ToList();
                Texture2D with = Shot(eye), again = Shot(eye);
                foreach (var mm in dented) mm.SetFloat("_YAPS_DentPower", 0f);
                var without = Shot(eye);
                for (int k = 0; k < dented.Count; k++) dented[k].SetFloat("_YAPS_DentPower", powers[k]);
                squashOver.Add($"frame {frame} {100f * Changed(with, without):0.000}% (twice alike {100f * Changed(with, again):0.000}%)");
                foreach (var shot in new[] { with, again, without }) Object.DestroyImmediate(shot);
            }
            if (++frame < BreastFrames) return;
            foreach (var b in blits) b.SetFloat("_Solid", solid0);
            // What the breasts cost a viewer at rest: the client's chain of passes timed alone, and
            // the same passes only copying, the floor any pass pays before it does any work.
            var chain = jelly.GetComponentInChildren<CVRRenderController>(true).cvrRenderComponents.OfType<CVRBlitter>().ToList();
            var bareCopy = new Material(Shader.Find("Hidden/BlitCopy"));
            var chainTimes = new List<double>();
            var chainSubmits = new List<double>();
            var copyTimes = new List<double>();
            for (int rep = 0; rep < 5; rep++)
            {
                chainTimes.Add(Timed(() => { foreach (var c in chain) Graphics.Blit(c.originTexture, c.destinationTexture, c.blitMaterial); }, chain[0].destinationTexture, 600, out double sent));
                chainSubmits.Add(sent);
                copyTimes.Add(Timed(() => { foreach (var c in chain) Graphics.Blit(c.originTexture, c.destinationTexture, bareCopy); }, chain[0].destinationTexture, 600, out _));
            }
            Object.DestroyImmediate(bareCopy);
            Debug.Log($"[Jelly] the wearer's own arm across one, held: {ownSaid}");
            Debug.Log($"[Jelly] a fist closed on the first's front and pulled 8 cm straight out: gripped {grabbed} frames; the skin it held came {1000f * grab[0]:0.0} mm, the whole {1000f * grab[1]:0.0} mm; let go, a ring {1000f * grab[2]:0.0} mm, the skin {1000f * grab[3]:0.0} mm off its rest two seconds on ({1000f * grab[4]:0.0} as the hand came)");
            Debug.Log($"[Jelly] the pair clapped together by two hands in six frames, their skins {1000f * clap[0]:0.0} mm apart (a cell {1000f * clapCell:0.0} mm): a ring from between them on the first {1000f * clap[1]:0.0} mm and the second {1000f * clap[2]:0.0} mm");
            Debug.Log($"[Jelly] the wearer's own hand pushed on through one: its particles moved {1000f * ownThrough[0]:0.0} mm at most, {ownThrough[1]:0} over 10 cm, {1000f * ownThrough[2]:0.0} mm with the hand at the chest");
            Debug.Log($"[Jelly] the picture's pixels the squash changes with the jelly on: {string.Join(", ", squashOver)}");
            Debug.Log($"[Jelly] breasts' cost at rest on {SystemInfo.graphicsDeviceName}: {chain.Count} passes, {Spread(chainTimes)} ms a frame ({Median(chainSubmits):0.000} to submit); " +
                      $"the same passes only copying {Spread(copyTimes)} ms");
            // -jellyCost: the chain at a 90th (one predicted step) and a 60th (two whole ones), far off,
            // the far switch's parser pass alone, against two copies; then 4 and 16 avatars' chains one
            // after another, as a crowd's run; then the avatar drawn reading the jelly and the squash,
            // the squash alone, and neither. Each round interleaved, so a clock change moves every row
            // alike.
            if (Environment.GetCommandLineArgs().Contains("-jellyCost"))
            {
                var mats = chain.Select(c => c.blitMaterial).Distinct().ToList();
                RenderTexture pa = chain[0].originTexture, pb = chain[0].destinationTexture;
                var copyMat = new Material(Shader.Find("Hidden/BlitCopy"));
                void Chain() { foreach (var c in chain) Graphics.Blit(c.originTexture, c.destinationTexture, c.blitMaterial); }
                // A crowd: each avatar its own state, twin and far switch's three texels, the same passes.
                var crowd = Enumerable.Range(0, 16).Select(k =>
                {
                    var x = new RenderTexture(pa.descriptor);
                    var y = new RenderTexture(pb.descriptor);
                    var z = Strip(3);
                    x.Create();
                    y.Create();
                    z.Create();
                    Graphics.CopyTexture(pa, x);
                    Graphics.CopyTexture(pb, y);
                    return (x, y, z);
                }).ToList();
                // Every other texture is the far switch's own: taken for the twin, a crowd paid a whole
                // state's worth of it per avatar; shared by all, each waited on the last.
                RenderTexture Mapped(RenderTexture t, int k) => t == pa ? crowd[k].x : t == pb ? crowd[k].y : crowd[k].z;
                // passes: the first that many of the chain, the far switch's left out below its three;
                // skip: that many left off the front, the far switch's alone past the steps.
                void Crowd(int many, int passes = 99, int skip = 0)
                {
                    for (int k = 0; k < many; k++)
                        foreach (var c in chain.Skip(skip).Take(passes))
                            Graphics.Blit(Mapped(c.originTexture, k), Mapped(c.destinationTexture, k), c.blitMaterial);
                }
                // The anchor straight from the provider, its skinned swing left out, for that swing's price.
                var stride = mats.Select(mm => mm.GetFloat("_JellySkinStride")).ToList();
                void Unskinned(bool off) { for (int k = 0; k < mats.Count; k++) mats[k].SetFloat("_JellySkinStride", off ? 0f : stride[k]); }
                // The far switch's rest test off, as for a copy awake.
                var rests = mats.Select(mm => mm.GetFloat("_Rest")).ToList();
                void Rested(bool on) { for (int k = 0; k < mats.Count; k++) mats[k].SetFloat("_Rest", on ? rests[k] : 0f); }
                // The parser's pass, as the far switch's build adds it: the second pass's material on its
                // third pass.
                var points = new Material(chain[chain.Count - 1].blitMaterial);
                points.SetFloat("_Pass", 2f);
                var strip = Strip(3);
                strip.Create();
                var drawn = YapsSquash.Materials(swing).Where(mm => mm.HasProperty("_YAPS_JellyLattice") && mm.GetVector("_YAPS_JellyLattice").x > 0).ToList();
                var lat = drawn.Select(mm => mm.GetVector("_YAPS_JellyLattice")).ToList();
                var power = drawn.Select(mm => mm.GetFloat("_YAPS_DentPower")).ToList();
                var parts = drawn.Select(mm => mm.GetVector("_YAPS_JellyParts")).ToList();
                // handed: the jelly's parts given back to the squash, as before there was a jelly.
                void Drawn(bool jelly, bool squash, bool handed = false)
                {
                    for (int k = 0; k < drawn.Count; k++)
                    {
                        drawn[k].SetVector("_YAPS_JellyLattice", jelly ? lat[k] : Vector4.zero);
                        drawn[k].SetFloat("_YAPS_DentPower", squash ? power[k] : 0f);
                        drawn[k].SetVector("_YAPS_JellyParts", handed ? Vector4.zero : parts[k]);
                        drawn[k].SetVector("_YAPS_JellySlap", Slapped);
                    }
                }
                // The draw's inputs written straight into the state it reads, as no step runs between
                // its renders: eight hands 40 cm out in front, in reach of every vertex's sums and
                // touching none, and each region a slap 50 ms old, so its ring is live. Off, both zeroed.
                var read = (drawn.Count > 0 ? drawn[0].GetTexture("_YAPS_JellyState") as RenderTexture : null) ?? pa;
                var fake = new Texture2D(84, 1, TextureFormat.RGBAFloat, false);
                void Fake(bool hands, bool ring)
                {
                    for (int r = 0; r < 2; r++)
                    {
                        var px = new Color[84];
                        Vector3 front = breastRegions[r].position + swing.forward * 0.4f;
                        for (int e = 0; hands && e < 8; e++)
                        {
                            Vector3 wrist = front + swing.right * (0.03f * e);
                            px[2 * e] = new Color(wrist.x, wrist.y, wrist.z, 0.04f);
                            Vector3 tip = wrist + swing.up * 0.15f;
                            px[2 * e + 1] = new Color(tip.x, tip.y, tip.z, 10f);
                        }
                        if (ring)
                        {
                            Vector3 hit = breastRegions[r].position + swing.forward * 0.15f;
                            px[80] = new Color(hit.x, hit.y, hit.z, Time.timeSinceLevelLoad - 0.05f);
                            px[81] = new Color(0f, 0f, 0f, 0.01f);
                        }
                        fake.SetPixels(px);
                        fake.Apply();
                        // Texels 16 to 79 are the step's own; only the hands' and the slap's are written.
                        int row = r * (Side(n) + 1) + Side(n);
                        Graphics.CopyTexture(fake, 0, 0, 0, 0, 16, 1, read, 0, 0, 0, row);
                        Graphics.CopyTexture(fake, 0, 0, 80, 0, 4, 1, read, 0, 0, 80, row);
                    }
                }
                // Far is the viewer's head past _Far; none reads as near.
                void Head(float off) => Shader.SetGlobalVectorArray("_CVR_PlayerHeadPositions", Enumerable.Repeat(off > 0 ? (Vector4) (swing.position + swing.forward * off) : Vector4.zero, 255).ToArray());
                void Near(float dt) { Head(0f); foreach (var mm in mats) mm.SetFloat("_FixedDt", dt); }
                // The half rate: every other frame the whole chain at twice the frame, the far switch's alone between.
                int halfTick = 0;
                var timed = new (string name, Action set, Action run, RenderTexture rt, int runs)[]
                {
                    ("the step at 90 fps", () => Near(1f / 90f), Chain, pb, 1000),
                    ("at 60 fps", () => Near(1f / 60f), Chain, pb, 1000),
                    ("4 avatars' at 90", () => Near(1f / 90f), () => Crowd(4), pb, 300),
                    ("16 avatars'", () => { Near(1f / 90f); Unskinned(false); }, () => Crowd(16), pb, 100),
                    ("16 avatars', no far switch pass", () => Near(1f / 90f), () => Crowd(16, 2), pb, 100),
                    ("16 avatars', awake (no rest test)", () => { Near(1f / 90f); Rested(false); }, () => Crowd(16), pb, 100),
                    ("16 avatars' at the half rate, 3 m off", () => { Near(1f / 45f); Rested(true); Unskinned(false); Head(3f); },
                        () => { if (halfTick++ % 2 == 0) Crowd(16); else Crowd(16, 99, chain.Count - 1); }, pb, 100),
                    ("16 avatars', the anchor unskinned", () => { Near(1f / 90f); Rested(true); Unskinned(true); }, () => Crowd(16), pb, 100),
                    ("20 m off", () => { Near(1f / 90f); Unskinned(false); Head(20f); }, Chain, pb, 1000),
                    ("16 avatars 20 m off", () => { Near(1f / 90f); Head(20f); }, () => Crowd(16), pb, 100),
                    ("the far switch's parser pass alone", () => Near(1f / 90f), () => Graphics.Blit(pa, strip, points), strip, 1000),
                    ("two copies", () => { }, () => { Graphics.Blit(pa, pb, copyMat); Graphics.Blit(pb, pa, copyMat); }, pb, 1000),
                    ($"the avatar drawn ({drawn.Count} materials) with the squash and the jelly", () => { Drawn(true, true); Fake(false, false); }, () => eye.Render(), eye.targetTexture, 200),
                    ("the jelly, 8 hands in reach", () => { Drawn(true, true); Fake(true, false); }, () => eye.Render(), eye.targetTexture, 200),
                    ("the jelly, 8 hands and a slap's ring", () => { Drawn(true, true); Fake(true, true); }, () => eye.Render(), eye.targetTexture, 200),
                    ("the jelly alone, no squash", () => { Drawn(true, false); Fake(false, false); }, () => eye.Render(), eye.targetTexture, 200),
                    ("the squash alone", () => { Drawn(false, true); Fake(false, false); }, () => eye.Render(), eye.targetTexture, 200),
                    ("the squash on every part, the breasts handed back to it", () => Drawn(false, true, true), () => eye.Render(), eye.targetTexture, 200),
                    ("neither", () => Drawn(false, false), () => eye.Render(), eye.targetTexture, 200),
                };
                var got = timed.Select(r => new List<double>()).ToArray();
                for (int round = 0; round < 7; round++)
                    for (int v = 0; v < timed.Length; v++)
                    {
                        timed[v].set();
                        got[v].Add(Timed(timed[v].run, timed[v].rt, timed[v].runs, out _));
                    }
                Near(0f);
                Drawn(true, true);
                Fake(false, false);
                Object.DestroyImmediate(fake);
                Object.DestroyImmediate(copyMat);
                Object.DestroyImmediate(points);
                strip.Release();
                foreach (var (x, y, z) in crowd) { x.Release(); y.Release(); z.Release(); }
                Debug.Log($"[Jelly] cost on {SystemInfo.graphicsDeviceName}, ms: " + string.Join("; ", timed.Select((r, v) => $"{r.name} {Spread(got[v])}")));
            }
            // The bodies' doing: how much further from the other the first's facing side was held
            // with them than without.
            var away = breastRegions[0].position - secondAt;
            away.y = 0f;
            away.Normalize();
            float bodies = 0f;
            for (int q = 0; q < innerOn.Length; q++) bodies = Mathf.Max(bodies, Vector3.Dot(innerOn[q] - innerOff[q], away));

            noise = Changed(breastShots[0], breastShots[1]);
            poked = Changed(breastShots[1], breastShots[3]);
            bool viewOk = viewRan.SequenceEqual(new[] { true, false, true, true }) && viewBack < 0.006f
                && halfFrames > 0 && Mathf.Abs((float) halfOn / halfFrames - 0.5f) < 0.15f && halfMost >= 2f && halfMost <= 3f;
            bool walled = wallRest != null;
            if (walled) Debug.Log($"[Jelly] its probes feeling the world, a wall 3 cm into the first's front: the first dented {1000f * wallDent[0]:0.0} mm, the second {1000f * wallDent[1]:0.0} mm; the wall gone two thirds of a second, the first {1000f * wallDent[2]:0.0} mm off its rest; its skin {1000f * wallDent[3]:0.0} mm past the wall");
            bool wallOk = !walled || wallDent[0] > 0.01f && wallDent[1] < 0.005f && wallDent[2] < 0.006f && wallDent[3] < 0.008f;
            bool probeOk = wallOk && (!probed || probeDent > probeBefore + 0.005f && probeSlots == probeSlotsBefore + 1 && probePushed >= 2 && bothSlots == 1 && ringsSlow == 0 && ringsProbe == 1 && ringsBoth == 1);
            bool ok = anchorOk && clap[1] > 0.005f && clap[2] > 0.005f && grabbed > 60 && grab[0] > 0.04f && grab[1] > 0.002f && grab[2] > 0.005f && grab[3] < 0.006f && restMost < 0.006f && pokeMost > 0.01f && poked > 3f * noise + 0.002f && swingMost > 0.005f && bodies > 0.003f && handsOuter > 0.05f && viewOk && probeOk && ownThrough[1] <= 20 && ownThrough[2] < 0.02f;
            string dir = SessionState.GetString(K + "out", "Jelly");
            int w = breastShots[0].width, h = breastShots[0].height;
            int rows = (breastShots.Count + 3) / 4;
            var sheet = new Texture2D(w * 4, h * rows, TextureFormat.RGB24, false);
            for (int i = 0; i < breastShots.Count; i++) sheet.SetPixels((i % 4) * w, (rows - 1 - i / 4) * h, w, h, breastShots[i].GetPixels());
            File.WriteAllBytes(Path.Combine(dir, "breasts.png"), sheet.EncodeToPNG());
            File.WriteAllLines(Path.Combine(dir, "centres.csv"), new[] { "frame,c0x,c0y,c0z,c1x,c1y,c1z,furthest (mm)" }.Concat(breastTrace));
            Debug.Log($"[Jelly] {(ok ? "PASS" : "FAIL")} breasts after {frame} frames with the CCK's render controller: the step's anchor {(anchorOk ? "is" : "is NOT")} the rig's; " +
                      $"settled, the furthest particle {1000f * restMost:0.0} mm off its modelled rest (6 at most); a finger 3 cm in moved it {1000f * pokeMost:0.0} mm " +
                      $"and {100f * poked:0.00}% of the picture's pixels against {100f * noise:0.00}% frame to frame; turning, {1000f * swingMost:0.0} mm; " +
                      $"squeezed together, the pair's contact held the side facing the other {1000f * bodies:0.0} mm further off than without");
            Debug.Log($"[Jelly] the steps switched by the parser: {(restSaid.Count > 0 ? string.Join(", ", restSaid) : "never")}");
            Debug.Log($"[Jelly] the viewer's camera, 3 m off: {string.Join("; ", viewSaid)}; woken, the furthest particle {1000f * viewBack:0.0} mm off its rest (6 at most); " +
                      $"its head 3 m off, the steps ran {halfOn} of {halfFrames} frames, the most a step took {halfMost:0}");
            if (probed)
                Debug.Log($"[Jelly] another player's collider, a 4.5 cm ball only the probes are given, 3 cm in between the first's frontmost probe and its nearest: the furthest particle " +
                          $"{1000f * probeBefore:0.0} mm off its rest before, {1000f * probeDent:0.0} mm pressed; {probeSlots} of the step's eight hand slots held a probe's press, {probePushed} probes pushed (two or more, one slot wanted); " +
                          $"the probes moved {1000f * probeMoved:0.0} mm at most; {probeSlotsBefore} slots held before it came (the slap's palm, parked 3 cm off the outer side)");
            if (probed) Debug.Log($"[Jelly] the probes: {string.Join("; ", probeSaid)}");
            if (probed) Debug.Log($"[Jelly] the probes' press, the furthest particle in mm by frame: {string.Join(", ", probeSeries)}");
            if (probed) Debug.Log($"[Jelly] breasts' cost with a collider pressing two probes: {probeCost} ms a frame");
            if (probed) Debug.Log($"[Jelly] the probes' slaps, rings fired: {ringsSlow} in the slow press (0 wanted), {ringsProbe} for the ball's slap alone (1), " +
                                  $"{ringsBoth} for it with a hand in the arrays (1), which held {bothSlots} slots (1); {(ringSaid.Count > 0 ? string.Join("; ", ringSaid) : "none")}");
            Debug.Log($"[Jelly] hands squeeze, 13 cm into each outer side: the masses {1000f * handsCloser:0.0} mm closer, the first's outer side {1000f * handsOuter:0.0} mm " +
                      $"and inner side {1000f * handsInner:0.0} mm off rest, its volume {handsVolume:0.000}");
            Debug.Log($"[Jelly] a slap, a palm 5 cm into one side for a tenth of a second: the whole went {1000f * slapMost:0.0} mm, still swinging {1000f * slapLater:0.0} mm a second on");
            for (int k = 0; k < 3; k++)
                Debug.Log($"[Jelly] a palm {(k == 2 ? 12 : 8)} cm up under one, held, {(k == 1 ? "the pair's contact off" : "as built")}: " +
                          $"its particles moved {1000f * pressShake[k]:0.000} mm a frame on average, {1000f * pressMost[k]:0.00} mm at most, its anchor {1000f * pressAnchor[k]:0.000} mm a frame, the whole rose {1000f * pressRise[k]:0.0} mm");
            SessionState.SetInt(K + "on", 0);
            EditorApplication.Exit(ok ? 0 : 1);
        }
#endif

        // ---------------------------------------------------------------- shaders

        const string StepShader = @"// The jelly spike's step. Written by JellySpikeBuilder; edits here are overwritten.
//
// The frame's two passes, from one texture into its twin and back. Left half: where each particle
// is, w the _Time.y of the last pass (negative while no anchor arrives). Right half: where it was a
// pass before, w the h that pass took, so a velocity is only ever read off the two. The row after
// the lattice: the hands in reach (texels 0 to 15), the rider (16, 17), the far switch and
// whether it sleeps (18), how long it has stood still (19), where the anchor was the last time it
// stepped (20 to 22), the rider a frame before (23, 24), the step held with the smoothed frame
// time (25), the anchor the second pass stepped on (26 to 29: its axes with its scale, then its
// place, w 1), for a body's own shader to find the lattice by (yaps_jelly.cginc), and the region's
// mass, as one piece (30, its centre, w the _Time.y it stepped; 31, where it was, w that step).
// Below 72 fps each pass takes a step. Above it the first only works out where each particle is
// headed and the second fits, so each neighbour is read once. The first pass also looks for the
// hands, so the second pushes against where they are this frame; below 72 fps the first pass's own
// step pushes against where they were the frame before.
Shader ""Hidden/AvatarBridge/JellyStep""
{
    Properties
    {
        _MainTex (""Last pass's state"", 2D) = ""black"" {}
        _Lattice (""Particles across, up and deep; spacing in metres"", Vector) = (16, 12, 16, 0.0133)
        _Stiff (""Pull toward the neighbourhood's shape over a 180th of a second"", Range(0, 1)) = 0.5
        _Hz (""Wobble a second, toward its rest on the chest"", Float) = 3
        _Damp (""Damping a second"", Float) = 4
        _Viscous (""Damping a second of what the tissue does apart from its home's own move: as _Damp a jelly, far more flesh"", Float) = 4
        _Hand (""Hand radius at a 1.6 m viewpoint"", Float) = 0.05
        _Far (""Hangs rigid past this many metres from the viewer's head"", Float) = 4
        _Behind (""Hangs rigid more than this many degrees off where the viewer's camera looks, 0 never"", Float) = 100
        _Near (""Never hangs rigid for being out of view within this many metres of the viewer's camera"", Float) = 2
        _Half (""Steps every other frame past this many metres from the viewer's head, short of _Far; 0 never"", Float) = 2
        _Pass (""Which of the frame's passes this is: 2 copies the rider out"", Float) = 0
        _Slap (""Slaps: metres a second to fire, depth per m/s, deepest, life in seconds"", Vector) = (0.5, 0.01, 0.03, 1.5)
        _FixedDt (""Fixed frame time, 0 for the real one"", Float) = 0
        _Stress (""The cost meter's load: steps only in odd phases"", Float) = 0
        _Rider (""Steps the rider's texels, for the parser and the sleep"", Float) = 1
        _Sleep (""Rests after standing still this many seconds, untouched and unmoved; 0 never"", Float) = 0.5
        _Rest (""Worn: stops the steps after lying still this many seconds with no hand near; 0 never"", Float) = 0.5
        _Regions (""How many regions the state packs"", Float) = 1
        _Lift (""1 lifts its rest by the sag gravity gives, along the anchor's up: modelled tissue hangs already"", Float) = 0
        _Gravity (""Share of gravity the tissue feels"", Float) = 1
        _MassHz (""The whole region as one piece, swinging on its root: wobbles a second, 0 off"", Float) = 0
        _MassCarried (""1: the mass rides the anchor's own move, a bone its cloth swings, and only hands move it on it"", Float) = 0
        _MassDamp (""Its damping a second"", Float) = 4
        _Give (""Share of how far a hand reaches into it that moves the whole; the particles dent the rest"", Range(0, 1)) = 0.5
        _MassRadius (""Its radius against hands, in the anchor's metres"", Float) = 0.1
        _Limit (""Furthest its centre swings off its rest, as a share of its length from the root"", Float) = 0.6
        _Motion (""Stretch in motion, the squash's own: x per reach of swing, y swing ignored below this share of the reach, z longest stretch; x 0 none"", Vector) = (0, 0, 1, 0)
        _Pair (""1: regions 0 and 1 are a pair, each keeping off the other"", Float) = 0
        _PairKeep (""Nearest the pair's centres come, as a share of their distance at rest"", Float) = 0.85
        _Squish (""Share of each neighbourhood's own squash and stretch it keeps, at constant volume; 0 rigid"", Range(0, 1)) = 0
        _PairOffset (""The second region's place in the first's frame at rest, both turned alike"", Vector) = (0, 0, 0, 0)
        _PairReach (""Each region's half width toward the other at rest, and the gap between them then; x 0 no plane"", Vector) = (0, 0, 0, 0)
        _PairSkin (""Each region's skin's reach toward the other off its centre at rest; x 0 none"", Vector) = (0, 0, 0, 0)
        _JellyOwn0 (""The wearer's own capsules' radii: the left upper arm, forearm, hand and thigh; 0 none"", Vector) = (0, 0, 0, 0)
        _JellyOwn1 (""The same for the right"", Vector) = (0, 0, 0, 0)
        _JellyCol0 (""The avatar's own colliders' radii, those pressing; 0 none"", Vector) = (0, 0, 0, 0)
        _JellyCol1 (""The next four"", Vector) = (0, 0, 0, 0)
        _JellyBox0 (""The first region's skin on its anchor: half width, half height, depth; 0 the lattice's box"", Vector) = (0, 0, 0, 0)
        _JellyBox1 (""The same for the second"", Vector) = (0, 0, 0, 0)
        _JellyOffsetAt0 (""The first region's place on the bone it rides, in the bone's units, w its scale on it; w 0 rides none"", Vector) = (0, 0, 0, 0)
        _JellyOffsetTurn0 (""The first region's turn on that bone"", Vector) = (0, 0, 0, 1)
        _JellyOffsetAt1 (""The second region's place on its bone"", Vector) = (0, 0, 0, 0)
        _JellyOffsetTurn1 (""The second region's turn on its bone"", Vector) = (0, 0, 0, 1)
        _JellySkinStride (""The skinned helper's vertex stride, 0 for none: the bones as the cloth swings them"", Float) = 0
        _JellyProbes (""MagicaCloth probes a region has in the skinned helper, after its frames, 0 none"", Float) = 0
        _JellyProbeReach (""How far off the skin a collider pushes a probe, the probes' radius, metres"", Float) = 0
        _JellySolid (""Regions whose probes are balls the world's solids push, a bit each: felt as flat faces, not hands"", Float) = 0
        _JellyProbePatch (""How far across a world probe's face reaches, metres"", Float) = 0
        _Deepest (""How deep a hand presses before the skin holds it, metres; past the tissue's middle it lets go"", Float) = 0.05
        _JellyArrays (""A component a region: 0 leaves the player arrays' hands out, its probes alone"", Vector) = (1, 1, 1, 1)
        _Floor (""A board under the tissue on its anchor: x 1 for one, y its height there in metres"", Vector) = (0, 0, 0, 0)
        _Solid (""Share of the whole's own squash its homes take at constant volume, so squeezed it bulges where nothing holds it; 0 none"", Range(0, 0.95)) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 4.5
            // Only the first pass looks for hands, so only it carries every player's positions.
            #pragma multi_compile_local __ JELLY_SCAN
            // Only a prop sleeps; worn, it is compiled out.
            #pragma multi_compile_local __ JELLY_SLEEP
            // The third pass, which wakes a resting jelly, reads the players' hands too.
            #pragma multi_compile_local __ JELLY_POINTS
            #include ""UnityCG.cginc""
            #include ""JellyCommon.cginc""

            float _Stiff, _Hz, _Hand, _Far, _Behind, _Near, _Half, _Pass, _FixedDt, _Stress, _Sleep, _Lift, _Rider, _Rest, _Regions;
            float _MassHz, _MassCarried, _MassDamp, _Give, _MassRadius, _Limit, _Pair, _PairKeep, _Squish, _Viscous, _Solid;
            float4 _Slap;
            // The frame's time, for how fast the homes move.
            static float FrameDt;
            float4 _Motion, _PairOffset, _PairReach, _PairSkin, _JellyOwn0, _JellyOwn1, _JellyCol0, _JellyCol1, _JellyBox0, _JellyBox1, _JellyOffsetAt0, _JellyOffsetTurn0, _JellyOffsetAt1, _JellyOffsetTurn1;
            // A helper mesh skinned four points to each region's bone and four to where it hangs on
            // the chest, as the squash's slots, bound as the provider's copy of its skinned buffer.
            ByteAddressBuffer _JellySkin;
            float _JellySkinStride, _JellyProbes, _JellyProbeReach, _Deepest, _JellySolid, _JellyProbePatch;
            float4 _JellyArrays;
            float4 _Floor;
            // The most probes a region has: its state row's readout, 88 on, has room for no more.
            #define PROBES 24
            // A world probe's ball radius, as the builder's Solid() makes it.
            #define BALL 0.01

            // The inverse of a turn, an even scale and a place.
            float4x4 Unrigid(float4x4 m)
            {
                float3x3 r = transpose((float3x3) m) / dot(m._m00_m10_m20, m._m00_m10_m20);
                float3 t = -mul(r, m._m03_m13_m23);
                return float4x4(float4(r[0], t.x), float4(r[1], t.y), float4(r[2], t.z), float4(0, 0, 0, 1));
            }

            // A frame through skinning, from four points at base: its place, then a tenth along each
            // of its axes, in the helper's own space (skinned with no root bone, not the world's).
            // false where the buffer reads empty or spoilt.
            bool Skinned(uint base, out float4x4 f)
            {
                f = 0;
                uint stride = (uint) _JellySkinStride;
                float3 p[4];
                [unroll] for (uint i = 0; i < 4; i++)
                {
                    uint3 raw = _JellySkin.Load3((base + i) * stride);
                    // NaN and infinity by their bits: the compiler folds a float test for NaN away.
                    if (any((raw & 0x7f800000) == 0x7f800000)) return false;
                    p[i] = asfloat(raw);
                }
                float3 x = (p[1] - p[0]) * 10, y = (p[2] - p[0]) * 10, z = (p[3] - p[0]) * 10;
                if (!(dot(x, x) > 1e-8)) return false;
                f = float4x4(x.x, y.x, z.x, p[0].x, x.y, y.y, z.y, p[0].y, x.z, y.z, z.z, p[0].z, 0, 0, 0, 1);
                return true;
            }

            // Region r's anchor. One riding a bone its cloth swings is handed the bone, and its place
            // on it is here: parented under the bone, it joined the cloth's chain as a bone of its own.
            // The bone as the cloth swings it comes only through skinning: the provider reads bones
            // in a script LateUpdate, after the cloth put them back to rest and before it writes them,
            // so every region it handed stayed still on the chest. The region's frame on its bone
            // against the same frame where it hangs, both as last skinned, is the swing, a frame late,
            // put on the chest as it is now, so the body's own move is not late.
            float4x4 AnchorOf(uint r)
            {
                float4x4 a = _JellyAnchor[min(r, 3)];
                float4 at = r == 0 ? _JellyOffsetAt0 : _JellyOffsetAt1, q = r == 0 ? _JellyOffsetTurn0 : _JellyOffsetTurn1;
                if (r > 1 || !(at.w > 0)) return a;
                float3x3 t = Turn(q) * at.w;
                a = mul(a, float4x4(float4(t[0], at.x), float4(t[1], at.y), float4(t[2], at.z), float4(0, 0, 0, 1)));
                // Both in the helper's space, which their difference does not see. Kept only at its own
                // scale and within half a metre: the squash's skinned slots reached remote copies
                // metres off.
                float4x4 on, still;
                [branch] if (_JellySkinStride > 0 && Skinned(8 * r, on) && Skinned(8 * r + 4, still))
                {
                    float4x4 swung = mul(Unrigid(still), on);
                    if (abs(length(swung._m00_m10_m20) - 1) < 0.2 && length(swung._m03_m13_m23) < 0.5)
                        return mul(_JellyAnchor[2 + r], swung);
                }
                return a;
            }

            // Whether region r's probes are world balls.
            bool SolidRegion(uint r) { return (((uint) round(_JellySolid) >> r) & 1) != 0; }

            // Whether any of a side's probes read pushed last frame.
            bool Pushed(uint side)
            {
                bool any = false;
                [branch] if (_JellyProbes > 0)
                    [unroll] for (uint k = 0; k < PROBES; k++) any = any || k < (uint) _JellyProbes && _MainTex.Load(int3(88 + k, side, 0)).w > 0.002;
                return any;
            }

            // MagicaCloth's probes on region r's hang, as the build put them: back takes the skinned
            // helper's space to the world through the hang as last skinned, put on the hang as it
            // is now. false where there are none or the hang reads spoilt.
            bool Probing(uint r, out float4x4 back)
            {
                float4x4 still = 0;
                bool ok = false;
                back = 0;
                // Branched: an avatar with no probes would still take the hang's sixteen reads.
                [branch] if (_JellyProbes > 0 && _JellySkinStride > 0)
                {
                    ok = Skinned(8 * r + 4, still);
                    back = ok ? mul(_JellyAnchor[2 + r], Unrigid(still)) : 0;
                }
                return ok;
            }

            // Probe k of region r's: where it was pushed to and which way, and how far, 0 for a spoilt
            // read. Each is two points in the helper from 16, itself then its rest on the hang.
            float Probe(uint r, uint k, float4x4 back, out float3 at, out float3 way)
            {
                uint stride = (uint) _JellySkinStride, i0 = 16 + 2 * (r * min((uint) _JellyProbes, (uint) PROBES) + k);
                uint3 rp = _JellySkin.Load3(i0 * stride), rr = _JellySkin.Load3((i0 + 1) * stride);
                float3 d = mul((float3x3) back, asfloat(rp) - asfloat(rr));
                float gone = length(d);
                at = mul(back, float4(asfloat(rp), 1)).xyz;
                way = gone > 1e-6 ? d / gone : 0;
                // A ball the world pushes is only ever pushed in: one out past its rest is caught on
                // a solid's far side, as a thin pane passed over it leaves one, and read as a press.
                [branch] if (SolidRegion(r) && gone > 1e-6)
                {
                    float3 mid = mul(_JellyAnchor[2 + r], float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1)).xyz;
                    gone = dot(way, normalize(mid - (at - d))) > 0 ? gone : 0;
                }
                return any((rp & 0x7f800000) == 0x7f800000) || any((rr & 0x7f800000) == 0x7f800000) || !(gone < 0.2) ? 0 : gone;
            }

            // Region r's probes as colliders: each one's ball and rest, and how far it was pushed, 0
            // for none and for any but the two leaders below, so a slot and a ring are each a
            // collider's, not a probe's: within two hands,
            // two probes 8 cm apart took two slots, and the one a collider met slantwise swings
            // along its bone twice as fast as it came, a slow press read as a slap. ponytail: each
            // against its neighbours, not whole clusters, so a capsule longer than two balls can
            // still take two.
            void Felt(uint r, float4x4 back, out float3 balls[PROBES], out float3 rests[PROBES], out float deep[PROBES])
            {
                // Out of the body from the middle of the lattice at rest, through each probe's rest.
                float3 mid = mul(_JellyAnchor[2 + r], float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1)).xyz;
                float reach = _Hand + _JellyProbeReach;
                float gone[PROBES];
                bool any = false;
                [unroll] for (uint k = 0; k < PROBES; k++)
                {
                    float3 at, way;
                    float g = Probe(r, k, back, at, way);
                    rests[k] = at - way * g;
                    // The tip for now; a leader's becomes its collider's centre below.
                    balls[k] = at;
                    // Pushed past most of a reach is swept along by a sliding collider, round its side
                    // and out past it, not pressed: one read 12 cm for a ball 3 cm in and rebuilt it 5 cm
                    // too deep in a frame. Held to 0.8 of a reach, a press reads at most about 3 cm deep.
                    // Solving from where the tips sit instead jumped up to 8 cm a frame as roots flipped.
                    gone[k] = k < (uint) _JellyProbes && g > 0.002 ? min(g, 0.8 * reach) : 0;
                    deep[k] = 0;
                    any = any || gone[k] > 0;
                }
                // Untouched, as nearly always, none of this is looked at: the slaps run it every frame.
                [branch] if (any)
                {
                    // The furthest pushed leads, and the furthest pushed whose rest is past 1.25 reaches
                    // of its rest leads a second; each takes the probes resting within 1.2 reaches of
                    // its own. By the tips, which a collider sweeps round it, two balls 12 cm apart
                    // shared the probes between them and each slot sat 3 to 5 cm off its ball, drawn
                    // toward the other; by the rests, 1.5 cm. Scored offline on dumped probes first: a
                    // lone ball dragged still rebuilds as well, and the second slot it now and then
                    // takes sits 5 cm shallower than it, outside the skin. Each against each, 24 by 24
                    // unrolled at the scan and the slaps, timed the shader compiler out. ponytail: two
                    // colliders a region; a third reads as one of those.
                    float best = 0, next = 0;
                    float3 lead = 0, lead2 = 0, from = 0, from2 = 0;
                    [unroll] for (uint k = 0; k < PROBES; k++)
                    {
                        bool more = gone[k] > best;
                        lead = more ? balls[k] : lead;
                        from = more ? rests[k] : from;
                        best = max(best, gone[k]);
                    }
                    [unroll] for (uint k = 0; k < PROBES; k++)
                    {
                        bool far = gone[k] > next && distance(rests[k], from) >= 1.25 * reach;
                        lead2 = far ? balls[k] : lead2;
                        from2 = far ? rests[k] : from2;
                        next = far ? gone[k] : next;
                    }
                    // Each one's collider over the rests of the probes it pushed, weighted by their
                    // pushes, a reach less its own push out along the skin: a probe pushed g had the
                    // collider's skin, a reach out from it, come g past its rest. A probe says how far,
                    // never where along the skin, so put out along one probe's normal through its tip,
                    // a ball over a face's middle, which pushes the probes round it sideways, sat 4 cm
                    // too far out to dent at all; through the tips' middle, a 5 mm error in the
                    // collider's size still moved it 3 cm.
                    float w = 0, w2 = 0;
                    float3 rest = 0, rest2 = 0;
                    [unroll] for (uint k = 0; k < PROBES; k++)
                    {
                        float g = distance(rests[k], from) < 1.2 * reach ? gone[k] : 0, g2 = next > 0 && distance(rests[k], from2) < 1.2 * reach ? gone[k] : 0;
                        w += g;
                        rest += rests[k] * g;
                        w2 += g2;
                        rest2 += rests[k] * g2;
                    }
                    rest /= max(w, 1e-6);
                    rest2 /= max(w2, 1e-6);
                    float3 ball = rest + normalize(rest - mid) * (reach - best), ball2 = rest2 + normalize(rest2 - mid) * (reach - next);
                    [unroll] for (uint k = 0; k < PROBES; k++)
                    {
                        bool one = gone[k] == best && all(balls[k] == lead), two = next > 0 && gone[k] == next && all(balls[k] == lead2);
                        deep[k] = one || two ? gone[k] : 0;
                        rests[k] = one ? rest : two ? rest2 : rests[k];
                        balls[k] = one ? ball : two ? ball2 : balls[k];
                    }
                }
            }

            // Worked out once a pixel, as each is read many times over: its own region's alone, since
            // through skinning each is sixteen reads, and only a few texels need the other's.
            static float4x4 Mine;
            static uint Region;
            float4x4 Anchor(uint r) { return r == Region ? Mine : AnchorOf(r); }

            // A world point in an anchor's rest frame, metres from it along its axes.
            float3 Into(float4x4 a, float3 x)
            {
                float3 d = x - a._m03_m13_m23, ax = a._m00_m10_m20;
                return float3(dot(d, ax), dot(d, a._m01_m11_m21), dot(d, a._m02_m12_m22)) / dot(ax, ax);
            }

            // A kept hand from its slot's texels a (wrist, radius) and b (an open hand's tip, 10 plus the
            // gesture, and 10 a millimetre the skin holds it back), as the squash reads one
            // (yaps_dent.cginc): the palm from a to pb, ending at the knuckles as it closes to a fist,
            // and a finger out, thin, from fa to fb of radius rf, 0
            // for none. A pointing finger read as a whole open hand shoved the breast away from it.
            void Hand(float4 a, float4 b, out float3 pb, out float3 fa, out float3 fb, out float rf)
            {
                float3 seg = b.xyz - a.xyz;
                float len = length(seg), g = fmod(b.w - 10, 10), r = a.w;
                pb = a.xyz;
                fa = fb = a.xyz;
                rf = 0;
                if (len < 1e-6) return;
                float3 dir = seg / len;
                float tall = (len + r) / 0.11, fist = g > 1.5 ? 1 : saturate(g);
                pb = a.xyz + dir * max(lerp(0.11, 0.06, fist) * tall - r, 0);
                rf = g > 2.5 ? (g > 4.5 && g < 5.5 ? 0.35 : 0.2) * r : 0;
                fa = a.xyz + dir * (0.06 * tall);
                fb = a.xyz + dir * max(0.11 * tall - rf, 0.06 * tall);
            }

            // Whether a slot's hand is a closed fist: its gesture's 0 to 1 past three quarters.
            bool Fist(float4 b) { float g = fmod(b.w - 10, 10); return b.w > 0.5 && g > 0.75 && g < 1.5; }

            // Where a fist meets what it closes on: its knuckles' far side.
            float3 Knuckles(float4 a, float4 b)
            {
                float3 pb, fa, fb;
                float rf;
                Hand(a, b, pb, fa, fb, rf);
                float3 seg = b.xyz - a.xyz;
                return pb + (dot(seg, seg) > 1e-12 ? normalize(seg) : 0) * a.w;
            }

            // Slot e's grip as last frame left it (texel 112 + e): where on the anchor its fist closed
            // on the skin, in at; and how far the hand has pulled that since, on the anchor, capped at
            // half the tissue's depth, w 1; raw, the pull uncapped. 0 with no grip or its hand gone.
            float4 Pulled(uint e, float4x4 m, uint side, out float3 at, out float raw)
            {
                float4 g = _MainTex.Load(int3(112 + e, side, 0)), a = _MainTex.Load(int3(2 * e, side, 0)), b = _MainTex.Load(int3(2 * e + 1, side, 0));
                at = g.xyz;
                raw = 0;
                if (!(g.w > 0 && b.w > 0.5)) return 0;
                float3 d = Into(m, Knuckles(a, b)) - g.xyz;
                raw = length(d);
                return float4(d * min(1, 0.5 * (Count().z - 1) * _Lattice.w / max(raw, 1e-6)), 1);
            }

            // next on ahead of a capsule from a to b of radius r toward centre, the way a hand
            // presses: pushed out of the hand's nearest side, a particle a little off its axis went
            // round it and the hand passed between them, 35 mm of tissue moved for 130 mm in. One
            // behind the hand's line is left be, as a hand pushes and never pulls. Pushed on through,
            // a particle its spring had flung past the line came out the far side and was flung back,
            // a deep press at 60 frames a second shaking 10 cm a frame; pushed back out behind, the
            // ones a withdrawing hand had passed were dragged out with it, 11 cm past their rest.
            float3 Plow(float3 next, float3 a, float3 b, float r, float3 centre)
            {
                float3 seg = b - a, in_ = centre - (a + 0.5 * seg);
                float3 off = next - (a + seg * saturate(dot(next - a, seg) / max(dot(seg, seg), 1e-9)));
                float d2 = dot(off, off);
                if (d2 >= r * r || !(dot(in_, in_) > 1e-8)) return next;
                float3 way = normalize(in_);
                float along = dot(off, way);
                if (along < 0) return next;
                return next + way * (sqrt(max(along * along - d2 + r * r, 0)) - along);
            }

            // next out of a capsule the nearest way, as the wearer's own body pushes. Plowed toward
            // the centre like a hand, a particle whose rest lay behind a held arm slid round it and
            // was thrown back over the top, 50 mm a frame, round and round. One behind the line from
            // the limb to centre is left be, as hands do: pushed out either way, an arm moving off
            // dragged the tissue it had passed with it, and the breast stuck to it.
            float3 Out(float3 next, float3 a, float3 b, float r, float3 centre)
            {
                float3 seg = b - a;
                float3 off = next - (a + seg * saturate(dot(next - a, seg) / max(dot(seg, seg), 1e-9)));
                float d2 = dot(off, off);
                if (d2 >= r * r || d2 < 1e-12 || dot(off, centre - (a + 0.5 * seg)) < 0) return next;
                return next + off * (r * rsqrt(d2) - 1);
            }

            // The face of the tissue a hand came in by, a local axis, and the tissue's half thickness
            // across it: the face the wrist is over while it is outside, else the one a point 30 cm
            // back along the hand is over, toward its player's chest or the wearer's elbow. By the
            // wrist alone, a hand through to the back read as pressing in from behind.
            float3 Facing(float3 wrist, float3 tip, float4x4 m, float scale, out float3 mid, out float across)
            {
                float4 box = Region == 0 ? _JellyBox0 : _JellyBox1;
                float3 half = box.z > 0 ? float3(box.xy, 0.5 * box.z) : 0.5 * (float3) (Count() - 1) * _Lattice.w;
                mid = float3(0, 0, half.z);
                float3 way = tip - wrist, q = (Into(m, wrist) - mid) / half;
                if (max(abs(q.x), max(abs(q.y), abs(q.z))) < 1 && dot(way, way) > 1e-12) q = (Into(m, wrist - normalize(way) * 0.3 * scale) - mid) / half;
                float3 face = abs(q.x) >= abs(q.y) && abs(q.x) >= abs(q.z) ? float3(sign(q.x), 0, 0) : abs(q.y) >= abs(q.z) ? float3(0, sign(q.y), 0) : float3(0, 0, sign(q.z));
                across = dot(abs(face), half);
                return face;
            }

            // A hand past _Deepest into the skin, or a third of the tissue, is held there, as skin
            // would stop a real one; in VR it goes on through, and once its tip is past the tissue's
            // middle it lets go over its radius, as a body a hand has passed into springs back.
            // Followed in, a hand 11 cm in dragged 44 particles over 10 cm and one 17 cm in 120, the
            // tissue torn, and again as it was pulled back out. hold is how far the skin keeps it
            // short of where it is, which the whole still feels (Unheld): skin that stops a hand is
            // pushed along by it, and with the mass held too a squeeze no longer met in the middle.
            void Held(inout float3 wrist, inout float3 tip, float r, float4x4 m, float scale, out float hold)
            {
                float3 mid;
                float across;
                float3 face = Facing(wrist, tip, m, scale, mid, across);
                hold = 0;
                if (!any(face) || !(r > 0)) return;
                float deep = (across - min(dot(Into(m, wrist) - mid, face), dot(Into(m, tip) - mid, face))) * scale + r;
                float most = min(_Deepest * scale, 2 * across * scale / 3), go = saturate((deep - r - across * scale) / r);
                float back = max(deep - most, 0) + most * go;
                hold = back - deep * go;
                float3 out_ = normalize(mul((float3x3) m, face));
                wrist += out_ * back;
                tip += out_ * back;
            }

            // A held hand back where it is, less only its letting go: what moves the whole.
            void Unheld(inout float3 wrist, inout float3 tip, float hold, float4x4 m, float scale)
            {
                float3 mid;
                float across;
                float3 face = Facing(wrist, tip, m, scale, mid, across);
                float3 in_ = any(face) ? normalize(mul((float3x3) m, face)) * hold : 0;
                wrist -= in_;
                tip -= in_;
            }

            // The wearer's own capsule c of the sixteen, end to end after the four anchors: the left
            // upper arm, forearm, hand and thigh, then the right's, bone to bone; then the avatar's
            // colliders, each between its two ends. r 0 for none.
            void Own(uint c, float scale, out float3 a, out float3 b, out float r)
            {
                uint s = c < 8 ? 4 + (c >> 2) * 6 + ((c & 3) < 3 ? (c & 3) : 4) : 2 * c;
                a = _JellyAnchor[s]._m03_m13_m23;
                b = _JellyAnchor[s + 1]._m03_m13_m23;
                r = (c < 4 ? _JellyOwn0[c & 3] : c < 8 ? _JellyOwn1[c & 3] : c < 12 ? _JellyCol0[c & 3] : _JellyCol1[c & 3]) * scale;
            }

            // How much of the pair's plane holds, from the turn between the two frames, the partner's
            // as its row last wrote it. Built side by side and turned alike, the plane is where they
            // press; a limb turned across the other, as a thigh crossed over its partner some 55 to 75
            // degrees off it, lay past the plane with nothing there to meet and was pressed into a
            // blade. All of it to 40 degrees, none from 55; knees pressed together turn 31.
            float Meet(float4x4 m, uint ps)
            {
                float4 x = _MainTex.Load(int3(26, ps, 0)), y = _MainTex.Load(int3(27, ps, 0)), z = _MainTex.Load(int3(28, ps, 0));
                if (!(x.w > 0.5)) return 1;
                float t = dot(normalize(m._m00_m10_m20), normalize(x.xyz)) + dot(normalize(m._m01_m11_m21), normalize(y.xyz)) + dot(normalize(m._m02_m12_m22), normalize(z.xyz));
                return smoothstep(0.5736, 0.7660, 0.5 * (t - 1));
            }

            // How far a capsule from a to b of radius r leans the mass's spring target at p: c of its
            // overlap with the mass's radius.
            float3 Lean(float3 p, float3 a, float3 b, float r, float radius, float c)
            {
                float3 seg = b - a;
                float3 off = p - (a + seg * saturate(dot(p - a, seg) / max(dot(seg, seg), 1e-9)));
                float d = length(off);
                return d < r + radius && d > 1e-6 ? off * (c * (r + radius - d) / d) : 0;
            }

            // How far a capsule from a to b of radius r is off the skin: off its box on the anchor (the
            // lattice's, where none is set), less its radius and however far the mass has swung off its
            // rest. Nine points along the capsule, each a sixteenth of it from the next at most.
            float Gap(float3 a, float3 b, float r, float4x4 m, float scale, uint side)
            {
                float4 box = Region == 0 ? _JellyBox0 : _JellyBox1;
                float3 half = box.z > 0 ? float3(box.xy, 0.5 * box.z) : 0.5 * (float3) (Count() - 1) * _Lattice.w, mid = float3(0, 0, half.z);
                float4 mass = _MainTex.Load(int3(30, side, 0));
                float swung = mass.w > 0 ? distance(mass.xyz, mul(m, float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1)).xyz) : 0;
                float most = 1e9;
                [unroll] for (int k = 0; k <= 8; k++) most = min(most, length(max(abs(Into(m, lerp(a, b, k / 8.0)) - mid) - half, 0)));
                return most * scale - r - swung - distance(a, b) / 16;
            }

            // Whether it can reach the skin: within 8 cm, for what a press bulges out of the box. A sphere
            // round the lattice took in the thighs below it, so every particle and breast vertex tested
            // them with the arms down.
            bool InReach(float3 a, float3 b, float r, float4x4 m, float scale, uint side) { return Gap(a, b, r, m, scale, side) <= 0.08 * scale; }

            // How far the wearer's capsules in reach have moved against the anchor since the state at
            // side was stepped: their ends as it kept them (texels 42 to 57 and 64 to 79) on its anchor
            // then (26 to 29), against where they are now on m. Fresh, the reach is judged again and
            // one newly in it counts as a metre; otherwise those in reach then, which the count, run
            // every frame, can afford.
            float Shifted(float4x4 m, float scale, uint side, bool fresh)
            {
                float4 x0 = _MainTex.Load(int3(26, side, 0)), y0 = _MainTex.Load(int3(27, side, 0)), z0 = _MainTex.Load(int3(28, side, 0)), o0 = _MainTex.Load(int3(29, side, 0));
                float4x4 last = float4x4(x0.x, y0.x, z0.x, o0.x, x0.y, y0.y, z0.y, o0.y, x0.z, y0.z, z0.z, o0.z, 0, 0, 0, 1);
                uint was = (uint) _MainTex.Load(int3(41, side, 0)).x;
                float most = 0;
                [unroll] for (uint c = 0; c < 16; c++)
                {
                    float3 a, b;
                    float r;
                    Own(c, scale, a, b, r);
                    bool kept = (was & (1u << c)) != 0;
                    [branch] if (r > 0 && (fresh ? InReach(a, b, r, m, scale, side) : kept))
                    {
                        uint t = c < 8 ? 42 + 2 * c : 48 + 2 * c;
                        float3 ka = _MainTex.Load(int3(t, side, 0)).xyz, kb = _MainTex.Load(int3(t + 1, side, 0)).xyz;
                        float moved = max(length(Into(m, a) - Into(last, ka)), length(Into(m, b) - Into(last, kb))) * scale;
                        most = max(most, kept ? moved : 1);
                    }
                }
                return most;
            }

            // The client's first is the local player's head; only it is read.
            float4 _CVR_PlayerHeadPositions[1];

            // The client's rendering buffer: where the local player's camera is and how it is turned,
            // a frame late. Unbound, as in the editor or on a client without it, it reads zeros and
            // nothing counts as out of view.
            struct CVRRenderingData
            {
                int renderingCamera;
                int renderingMirroredView;
                int renderingCullingMask;
                float3 renderingCameraPos;
                float3 renderingPlayerCameraPos;
                float4 renderingPlayerCameraRot;
                float3 padding;
            };
            StructuredBuffer<CVRRenderingData> _CVRRenderingBuffer;

            // Well behind the viewer: past _Near of the camera and more than _Behind degrees off where
            // it looks, less the angle size takes up there. Waking comes back through the parser's
            // readback a few frames late, so it takes ten degrees more to go than to come back.
            bool Unseen(float3 at, float size, bool was)
            {
                if (!(_Behind > 0)) return false;
                CVRRenderingData d = _CVRRenderingBuffer[0];
                float4 q = d.renderingPlayerCameraRot;
                if (abs(dot(q, q) - 1) > 0.01) return false;
                float3 look = float3(2 * (q.x * q.z + q.w * q.y), 2 * (q.y * q.z - q.w * q.x), 1 - 2 * (q.x * q.x + q.y * q.y));
                float3 to = at - d.renderingPlayerCameraPos;
                float gone = length(to);
                if (!(gone > _Near)) return false;
                float off = degrees(acos(clamp(dot(look, to / gone), -1, 1))) - degrees(asin(saturate(size / gone)));
                return off > _Behind + (was ? 0 : 10);
            }

            // Far from the viewer's head or well behind their camera, judged on the first region for
            // every region alike, half a metre's give on the distance so it does not flicker.
            bool Away(bool was)
            {
                float4x4 a = AnchorOf(0);
                float3 head = _CVR_PlayerHeadPositions[0].xyz, at = a._m03_m13_m23;
                return dot(head, head) > 1e-6 && distance(head, at) > _Far - (was ? 0.5 : 0)
                    || Unseen(at, 2 * length(float3(Count()) * _Lattice.w) * length(a._m00_m10_m20), was);
            }

            // Past _Half of the viewer's head the parser runs the steps every other frame, the
            // crowd's cost halved where a jiggle at half the rate does not show.
            bool Halved()
            {
                float3 head = _CVR_PlayerHeadPositions[0].xyz;
                return _Half > 0 && dot(head, head) > 1e-6 && distance(head, AnchorOf(0)._m03_m13_m23) > _Half;
            }

#if defined(JELLY_SCAN) || defined(JELLY_POINTS)
            // The first 64 of the client's 255, all the scan reads: the whole arrays made every first
            // pass upload 16 kB of constants.
            float4 _CVR_PlayerLeftHandPositions[64];
            float4 _CVR_PlayerRightHandPositions[64];
            float4 _CVR_PlayerChestPositions[64];
            float4 _CVR_PlayerRootPositions[64];
            float4 CVRGlobalParams1;

            // Hand slot texel x of the eight, two texels each, (wrist, radius) then (an open hand's
            // tip, 10 plus the gesture), an empty one 0. Each looks through every player's hands and keeps the one it is due, so a
            // pass reads eight however many players there are.
            float4 Scan(int x, float4x4 m, float scale, uint side)
            {
                int slot = x >> 1;
                int3 n = Count();
                // A sphere round the lattice at rest, with room for it to bulge.
                float3 centre = mul(m, float4(0, 0, 0.5 * (n.z - 1) * _Lattice.w, 1)).xyz;
                float reach = (0.5 * length(float3(n - 1)) * _Lattice.w + 0.05) * scale;
                int players = (Region == 0 ? _JellyArrays.x : _JellyArrays.y) > 0.5 ? min((int) round(CVRGlobalParams1.y), 64) : 0;
                int found = 0;
                float4 kept = 0, kw = 0, kt = 0;
                // Every player's hands as the squash reads them: from the wrist, away from that
                // player's chest, a ninth of their viewpoint height. Unrolled over 64 players, so
                // Shader Filtering leaves it whole. Every lane looks at the same players, so the
                // branches take them all the same way: eight players to a group, a group past the
                // player count skipped whole, and inside one the players' places read together.
                // Most players are passed over on where they stand, since a hand is never further
                // from its player's feet than half as much again as their height: the hands
                // themselves, read for everyone, cost 20 us a pass for 64 players on a fast GPU,
                // most of it waiting on the player arrays.
                [unroll] for (int g = 0; g < 8; g++)
                {
                    [branch] if (g * 8 < players)
                    {
                        [unroll] for (int k = 0; k < 8; k++)
                        {
                            int who = g * 8 + k;
                            float4 root = _CVR_PlayerRootPositions[who];
                            float tall = root.w > 0.1 ? root.w : 1.6;
                            float3 to = centre - root.xyz;
                            float around = reach + 1.5 * tall + 0.5;
                            [branch] if (who < players && dot(to, to) < around * around)
                            {
                                float r = _Hand * tall / 1.6;
                                float3 chest = _CVR_PlayerChestPositions[who].xyz;
                                [unroll] for (int hand = 0; hand < 2; hand++)
                                {
                                    float4 held = hand == 0 ? _CVR_PlayerLeftHandPositions[who] : _CVR_PlayerRightHandPositions[who];
                                    float3 wrist = held.xyz;
                                    float3 arm = wrist - chest;
                                    float3 tip = wrist + (dot(arm, arm) > 1e-6 ? normalize(arm) : 0) * max(0.11 * tall - r, 0);
                                    // ponytail: eight hands at most, the first in player order; a crowd round one
                                    // jelly needs more slots.
                                    // Twice the radius, the skin's rim the draw eases out round a hand.
                                    bool hit = dot(wrist, wrist) >= 1e-6 && InReach(wrist, tip, 2 * r, m, scale, side);
                                    // Kept, not returned: a return inside the unrolled loop fails D3D11's validation.
                                    kw = hit && found == slot ? float4(wrist, r) : kw;
                                    kt = hit && found == slot ? float4(tip, 10 + clamp(held.w, 0, 9.9)) : kt;
                                    found += hit;
                                }
                            }
                        }
                    }
                }
                // Held once, for the hand this slot keeps: inside the loop it was 128 copies, too many to unroll.
                [branch] if (kt.w > 0.5)
                {
                    float3 w = kw.xyz, t = kt.xyz, pb, fa, fb;
                    float rf, hold;
                    // Held by as far as the hand reaches as drawn, a fist's knuckles or a finger out:
                    // by the open fingertips, a fist closing on the skin sat 3 cm off it, and gripped
                    // only if the frame it closed on caught it still in.
                    Hand(kw, kt, pb, fa, fb, rf);
                    float3 reach = rf > 0 ? fb : pb, was = w;
                    Held(w, reach, kw.w, m, scale, hold);
                    t += w - was;
                    // How far it is held back rides on the gesture, 10 a millimetre: 10 + gesture + 10 k.
                    kept = (x & 1) == 0 ? float4(w, kw.w) : float4(t, kt.w + 10 * round(1000 * hold));
                }
                // Then where MagicaCloth's colliders pushed the probes, each a hand's ball whose side
                // is the point it pushed: pushed out its nearest way, a probe lies on the collider's
                // skin, the collider on past it the way it came from. Its push is on the hang, as
                // last skinned, put on the hang as it is now.
                // Only once one was pushed last frame, as the readout from 88 has it: a collider is
                // felt a reach off the skin, so the frame this starts late by is still short of it.
                // At rest this is most of the step's cost.
                // Each test branched: `&&` works out both sides, so the reads and InReach's nine
                // points ran for every probe, pushed or not.
                bool pushed = false;
                // World probes press as flat faces in the step, never as hands.
                [branch] if (_JellyProbes > 0 && !SolidRegion(Region))
                    [unroll] for (uint k = 0; k < PROBES; k++) pushed = pushed || k < (uint) _JellyProbes && _MainTex.Load(int3(88 + k, side, 0)).w > 0.001;
                float4x4 back;
                [branch] if (pushed)
                [branch] if (Probing(Region, back))
                {
                    float3 balls[PROBES], rests[PROBES];
                    float deep[PROBES];
                    Felt(Region, back, balls, rests, deep);
                    [unroll] for (uint k = 0; k < PROBES; k++)
                    {
                        bool hit = false;
                        [branch] if (deep[k] > 0)
                        {
                            hit = InReach(balls[k], balls[k], _Hand, m, scale, side);
                            // A collider on a hand the arrays gave last frame is that hand, which has
                            // its slot: the client puts a capsule on every player's hands. The ball
                            // sits up to a reach out from the collider, the way its probe was pushed.
                            [unroll] for (uint e = 0; e < 8; e++)
                            {
                                float4 ha = _MainTex.Load(int3(2 * e, side, 0)), hb = _MainTex.Load(int3(2 * e + 1, side, 0));
                                float3 seg = hb.xyz - ha.xyz;
                                float3 p = ha.xyz + seg * saturate(dot(balls[k] - ha.xyz, seg) / max(dot(seg, seg), 1e-12));
                                hit = hit && !(hb.w > 0.5 && dot(seg, seg) > 1e-12 && distance(balls[k], p) < ha.w + _Hand + _JellyProbeReach);
                            }
                        }
                        kept = hit && found == slot ? ((x & 1) == 0 ? float4(balls[k], _Hand) : float4(balls[k], 10)) : kept;
                        found += hit;
                    }
                }
                return kept;
            }
#endif

#ifdef JELLY_POINTS
            // How far point p is off region r's box on its anchor m, in metres.
            float BoxGap(float3 p, float4x4 m, float scale, uint r)
            {
                float4 box = r == 0 ? _JellyBox0 : _JellyBox1;
                float3 half = box.z > 0 ? float3(box.xy, 0.5 * box.z) : 0.5 * (float3) (Count() - 1) * _Lattice.w, mid = float3(0, 0, half.z);
                return length(max(abs(Into(m, p) - mid) - half, 0)) * scale;
            }

            // Every region resting by its own count, and nothing changed since its last step: the
            // anchor within 1 cm and 3 degrees of where it was, nothing of the wearer's in reach moved
            // a centimetre against it or come into reach, and no player's hand where the scan would
            // keep it, its wrist, middle or tip within 8 cm of either box. The steps' blitters are off
            // meanwhile, so this pass alone wakes them. The players walked as the scan walks them.
            bool Resting()
            {
                uint rows = Rows(), regions = (uint) round(_Regions);
                int players = min((int) round(CVRGlobalParams1.y), 64);
                bool resting = regions > 0;
                float4x4 on[2];
                float scales[2];
                float3 mid = 0;
                // ponytail: two regions, a pair; a second pair needs four here.
                [unroll] for (uint r = 0; r < 2; r++)
                {
                    float4x4 m = AnchorOf(min(r, regions - 1));
                    float scale = length(m._m00_m10_m20);
                    on[r] = m;
                    scales[r] = scale;
                    mid += 0.5 * m._m03_m13_m23;
                    [branch] if (r < regions)
                    {
                        uint side = r * (rows + 1) + rows;
                        float4 x0 = _MainTex.Load(int3(26, side, 0)), z0 = _MainTex.Load(int3(28, side, 0)), o0 = _MainTex.Load(int3(29, side, 0));
                        Region = r;
                        resting = resting && _MainTex.Load(int3(18, side, 0)).y > 0.5 && scale > 1e-6 && distance(m._m03_m13_m23, o0.xyz) < 0.01 * scale
                            && dot(normalize(m._m00_m10_m20), normalize(x0.xyz)) > 0.9986 && dot(normalize(m._m02_m12_m22), normalize(z0.xyz)) > 0.9986
                            && Shifted(m, scale, side, true) < 0.01 * scale;
                    }
                }
                float reach = (length(float3(Count() - 1)) * _Lattice.w + 0.3) * scales[0];
                bool near = false;
                [unroll] for (int g = 0; g < 8; g++)
                {
                    [branch] if (g * 8 < players)
                    {
                        [unroll] for (int k = 0; k < 8; k++)
                        {
                            int who = g * 8 + k;
                            float4 root = _CVR_PlayerRootPositions[who];
                            float tall = root.w > 0.1 ? root.w : 1.6;
                            float3 to = mid - root.xyz;
                            float around = reach + 1.5 * tall + 0.5;
                            [branch] if (who < players && dot(to, to) < around * around)
                            {
                                float hr = _Hand * tall / 1.6;
                                float3 chest = _CVR_PlayerChestPositions[who].xyz;
                                [unroll] for (int hand = 0; hand < 2; hand++)
                                {
                                    float3 wrist = (hand == 0 ? _CVR_PlayerLeftHandPositions[who] : _CVR_PlayerRightHandPositions[who]).xyz, arm = wrist - chest;
                                    float3 tip = wrist + (dot(arm, arm) > 1e-6 ? normalize(arm) : 0) * max(0.11 * tall - hr, 0);
                                    [unroll] for (uint r = 0; r < 2; r++)
                                    {
                                        float gap = min(min(BoxGap(wrist, on[r], scales[r], r), BoxGap(0.5 * (wrist + tip), on[r], scales[r], r)), BoxGap(tip, on[r], scales[r], r));
                                        near = near || dot(wrist, wrist) >= 1e-6 && gap < 2 * hr + 0.08 * scales[r];
                                    }
                                }
                            }
                        }
                    }
                }
                // Or a probe pushed, a collider's press.
                [unroll] for (uint q = 0; q < 2; q++)
                {
                    float4x4 back;
                    [branch] if (q < regions)
                    [branch] if (Probing(q, back))
                    {
                        [unroll] for (uint k = 0; k < PROBES; k++)
                        {
                            float3 at, way;
                            [branch] if (k < (uint) _JellyProbes) near = near || Probe(q, k, back, at, way) > 0.002;
                        }
                    }
                }
                return resting && !near;
            }
#endif

            // The whole's own squash the homes take, from texels 32 to 34, a column each: the identity
            // when there is none.
            float3x3 Shape(uint side)
            {
                float4 x = _MainTex.Load(int3(32, side, 0)), y = _MainTex.Load(int3(33, side, 0)), z = _MainTex.Load(int3(34, side, 0));
                if (!(_Solid > 0) || !(x.w > 0)) return float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
                return transpose(float3x3(x.xyz, y.xyz, z.xyz));
            }

            // Particle l's place on the anchor's rest frame with the whole's squash taken: none at the
            // back face, on the chest wall, all of it at the front.
            float3 Shaped(int3 l, float3x3 shape)
            {
                float3 r = Rest(l);
                return r + (float) l.z / max(Count().z - 1, 1) * (mul(shape, r) - r);
            }

            // How that stretches the rest round l: its gradient there.
            float3x3 ShapedAt(int3 l, float3x3 shape)
            {
                float zmax = max(Count().z - 1, 1);
                float3x3 grow = shape - float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
                float3 g = mul(grow, Rest(l)) / (zmax * _Lattice.w);
                return float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1) + (float) l.z / zmax * grow + float3x3(0, 0, g.x, 0, 0, g.y, 0, 0, g.z);
            }

            // The region's mass, as one piece: its centre, the lattice's at rest, swings on the root,
            // the anchor's place. Each particle's home turns with it, more the nearer the front, so the
            // back face stays on the chest wall, and follows its centre in and out at constant volume.
            // Then the squash's own stretch in motion (yaps_dent.cginc), as real flesh's: swung
            // away from where it hangs it stretches, toward it it squashes, by _Motion.x per reach of
            // swing past _Motion.y of the reach, within _Motion.z, at constant volume, about the side
            // it hangs from. Off, or not stepped this tenth of a second, home is the rest on the anchor
            // alone. swing is the turn the particle's home took.
            // How far region r's bone has carried its centre off where it hangs on the chest, as the
            // squash's swing is its follow off its rest: the provider's third and fourth transforms
            // hold each region still on its parent. 0 for a region riding no bone.
            float3 Hung(uint r)
            {
                float4 at = r == 0 ? _JellyOffsetAt0 : _JellyOffsetAt1;
                if (r > 1 || !(at.w > 0)) return 0;
                float4x4 h = _JellyAnchor[2 + r];
                if (!(dot(h._m00_m10_m20, h._m00_m10_m20) > 1e-12)) return 0;
                float4 c = float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1);
                return mul(Anchor(r), c).xyz - mul(h, c).xyz;
            }

            float3 HomeOn(int3 l, float4x4 m, float4 tip, bool swings, float3x3 shape, float3 hung, out float4 swing)
            {
                float3 local = mul((float3x3) m, Shaped(l, shape));
                float3 root = m._m03_m13_m23;
                swing = float4(0, 0, 0, 1);
                if (!swings) return root + local;
                int3 n = Count();
                float3 rest = mul((float3x3) m, float3(0, 0, 0.5 * (n.z - 1) * _Lattice.w)), now = tip.xyz - root;
                float len0 = max(length(rest), 1e-6), len = max(length(now), 1e-6);
                float3 a = rest / len0, b = now / len;
                float w = (float) l.z / max(n.z - 1, 1);
                swing = normalize(lerp(float4(0, 0, 0, 1), normalize(float4(cross(a, b), 1 + dot(a, b))), w));
                float s = lerp(1, len / len0, w), along = dot(local, a);
                local = a * (along * s) + (local - a * along) * rsqrt(s);
                float3 at = root + mul(Turn(swing), local);
                // The cloth's swing of the bone, as the squash's stretch reads it, and the mass's lag on
                // it.
                float3 sw = hung + tip.xyz - root - rest;
                float reach = _MassRadius * length(m._m00_m10_m20), sl = length(sw), off = sl - _Motion.y * reach;
                [branch] if (_Motion.x > 0 && off > 0)
                {
                    float3 u = sw / sl, up = normalize(m._m01_m11_m21);
                    float st = lerp(1, clamp(1 + _Motion.x * off / reach * -dot(u, up), 1 / _Motion.z, _Motion.z), w);
                    float3 hangAt = tip.xyz + up * (0.5 * (n.y - 1) * _Lattice.w * length(m._m01_m11_m21));
                    float3 r0 = at - hangAt;
                    float by = dot(r0, u);
                    at = hangAt + u * (by * st) + (r0 - u * by) * rsqrt(st);
                }
                return at;
            }

            // The solid, once a frame in the first pass, column k of the squash the homes take: the best
            // linear map from a 4 x 4 x 4 of the particles' rest places to where they are on the anchor,
            // its turn taken out and its volume made the rest's, _Solid of it, the rest the rest. Flesh
            // barely changes volume, so squeezed one way it bulges the others, most where nothing holds
            // it, and let go it comes back. A pressure in its place swelled the whole evenly, into the
            // squeezing hands too, as a balloon would. w is the volume the map had, kept for the trace.
            float4 Bulk(int k, float4x4 m, bool distant, uint side)
            {
                float4 none = float4(k == 0, k == 1, k == 2, 1);
                if (distant) return none;
                int3 n = Count();
                float3 pc = 0, qc = 0, qq = 0, c0 = 0, c1 = 0, c2 = 0;
                // Summed as each is loaded, not held, so the pass keeps few registers.
                [unroll] for (int i = 0; i < 64; i++)
                {
                    int3 l = int3(round(float3(i % 4, (i / 4) % 4, i / 16) * (n - 1) / 3.0));
                    float3 p = Into(m, _MainTex.Load(Texel((uint) Index(l), 0)).xyz), q = Rest(l);
                    pc += p;
                    qc += q;
                    qq += q * q;
                    c0 += p * q.x;
                    c1 += p * q.y;
                    c2 += p * q.z;
                }
                pc /= 64;
                qc /= 64;
                // Over a box of rest places the rest's own spread is diagonal, an axis a column.
                float3 spread = qq - 64 * qc * qc;
                if (!all(spread > 1e-12)) return none;
                c0 = (c0 - 64 * pc * qc.x) / spread.x;
                c1 = (c1 - 64 * pc * qc.y) / spread.y;
                c2 = (c2 - 64 * pc * qc.z) / spread.z;
                float4 q = float4(0, 0, 0, 1);
                [unroll] for (int it = 0; it < 4; it++)
                {
                    float3x3 t = Turn(q);
                    float3 t0 = float3(t[0][0], t[1][0], t[2][0]), t1 = float3(t[0][1], t[1][1], t[2][1]), t2 = float3(t[0][2], t[1][2], t[2][2]);
                    float3 spin = (cross(t0, c0) + cross(t1, c1) + cross(t2, c2)) / (abs(dot(t0, c0) + dot(t1, c1) + dot(t2, c2)) + 1e-12);
                    float w = length(spin);
                    if (w > 1e-9) q = normalize(Mul(float4(spin / w * sin(0.5 * w), cos(0.5 * w)), q));
                }
                float3x3 a = mul(transpose(Turn(q)), transpose(float3x3(c0, c1, c2)));
                a = 0.5 * (a + transpose(a));
                float volume = determinant(a);
                if (!(volume > 0.3 && volume < 3)) return none;
                float3x3 shape = (1 - _Solid) * float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1) + _Solid * a * pow(volume, -1.0 / 3.0);
                return float4(transpose(shape)[k], volume);
            }

            float3 Home(int3 l, float4x4 m, uint side, out float4 swing)
            {
                float4 tip = _MainTex.Load(int3(30, side, 0));
                // Stamped and recent: a cleared texel reads as stepped at time 0, and at a scene's first
                // frame that passed, swinging every home toward the world's origin.
                return HomeOn(l, m, tip, _MassHz > 0 && tip.w > 0 && abs(_Time.y - tip.w) < 0.1, Shape(side), Hung(Base / (side - Base + 1)), swing);
            }

            // How fast particle l's home moves: from last frame's anchor and mass (texels 26 to 29 and
            // 31) to this one's, over the frame. 0 before there was a last frame.
            // ponytail: the cloth's swing is this frame's for both, so the stretch's own rate is
            // missing; keep last frame's hang in the side row if the stretch lags.
            float3 HomeSpeed(int3 l, uint side, float3 home)
            {
                float4 x = _MainTex.Load(int3(26, side, 0)), y = _MainTex.Load(int3(27, side, 0)), z = _MainTex.Load(int3(28, side, 0));
                float4 o = _MainTex.Load(int3(29, side, 0)), tip = _MainTex.Load(int3(30, side, 0)), was = _MainTex.Load(int3(31, side, 0));
                if (!(o.w > 0.5) || !(FrameDt > 0)) return 0;
                float4x4 last = float4x4(x.x, y.x, z.x, o.x, x.y, y.y, z.y, o.y, x.z, y.z, z.z, o.z, 0, 0, 0, 1);
                // Carried, 31 holds last frame's mass moved on with the anchor: put back on last frame's.
                if (_MassCarried > 0) was.xyz = mul(last, float4(Into(Anchor(Base / (side - Base + 1)), was.xyz), 1)).xyz;
                float4 unused;
                return (home - HomeOn(l, last, was, _MassHz > 0 && tip.w > 0 && abs(_Time.y - tip.w) < 0.1, Shape(side), Hung(Base / (side - Base + 1)), unused)) / FrameDt;
            }

            // Where a particle is headed from s, where it was was (w that step's time): its home's own
            // move kept, damped by _Damp; what it does apart from that damped by _Viscous. Flesh is
            // viscous, so a dent fills back without ringing while the mass's swing carries the whole:
            // damped by _Damp alone, the lattice rang at 3 Hz after every press, a jelly.
            float3 Ahead(float4 s, float4 was, int3 l, uint side, float3 home, float h)
            {
                float3 vh = HomeSpeed(l, side, home), v = (s.xyz - was.xyz) / max(was.w, 1e-4);
                return s.xyz + (vh * exp(-_Damp * was.w) + (v - vh) * exp(-_Viscous * was.w)) * h;
            }

            // The mass's step, once a frame by the whole frame time, in the first pass: texel 30 its
            // centre (w the _Time.y it stepped), 31 where it was (w that step's time). Verlet under
            // gravity; a spring of _MassHz toward its rest, held up by the sag gravity gives when the
            // tissue was modelled hanging; pushed out of the hands in reach by _Give of how far they
            // reach into it, the particles left to dent the rest; kept off its partner's centre, each
            // taking half; its swing held within _Limit of its length, in and out within a tenth.
            // The hands are the slots the last frame's scan kept.
            float4 Mass(int x, float4x4 m, float scale, float dt, bool distant, uint side, uint region, uint partnerSide)
            {
                float4 tip = _MainTex.Load(int3(30, side, 0)), was = _MainTex.Load(int3(31, side, 0));
                // Carried, both are moved on with the anchor since last frame, so only the hands move
                // the mass on it: lagging a bone its cloth swings, it swung the whole a second time.
                [branch] if (_MassCarried > 0)
                {
                    float4 x0 = _MainTex.Load(int3(26, side, 0)), y0 = _MainTex.Load(int3(27, side, 0)), z0 = _MainTex.Load(int3(28, side, 0)), o0 = _MainTex.Load(int3(29, side, 0));
                    if (o0.w > 0.5)
                    {
                        float4x4 last = float4x4(x0.x, y0.x, z0.x, o0.x, x0.y, y0.y, z0.y, o0.y, x0.z, y0.z, z0.z, o0.z, 0, 0, 0, 1);
                        tip.xyz = mul(m, float4(Into(last, tip.xyz), 1)).xyz;
                        was.xyz = mul(m, float4(Into(last, was.xyz), 1)).xyz;
                    }
                }
                float3 root = m._m03_m13_m23;
                float zc = 0.5 * (Count().z - 1) * _Lattice.w;
                float3 rest = mul(m, float4(0, 0, zc, 1)).xyz;
                bool fresh = distant || !(tip.w > 0 && abs(_Time.y - tip.w) < 0.1) || !(was.w > 0 && was.w < 0.1) || !(distance(tip.xyz, rest) < 1);
                if (x == 31) return float4(fresh ? rest : tip.xyz, dt);
                if (fresh) return float4(rest, _Time.y);
                // Carried, the cloth gives the weight its sag, so the mass takes none of its own.
                float3 next = tip.xyz + (tip.xyz - was.xyz) * (exp(-_MassDamp * dt) * dt / max(was.w, 1e-4)) + (_MassCarried > 0 ? 0 : Gravity * dt * dt);
                float spring = 6.2831853 * _MassHz * dt;
                float3 lift = _Lift > 0 && !(_MassCarried > 0) ? normalize(m._m01_m11_m21) * 9.81 * _Gravity * (1 - spring * spring) / (39.478418 * _MassHz * _MassHz) : 0;
                // The hands move where the spring holds it, a force, not the mass itself: pushed out
                // a share of the way each frame, it left any hand within a few frames and the
                // particles nothing to dent.
                float radius = _MassRadius * scale;
                float3 pushed = 0;
                [branch] if (_MainTex.Load(int3(1, side, 0)).w > 0.5)
                {
                    [unroll] for (int e = 0; e < 8; e++)
                    {
                        float4 a = _MainTex.Load(int3(2 * e, side, 0)), b = _MainTex.Load(int3(2 * e + 1, side, 0));
                        if (!(b.w > 0.5)) continue;
                        if (_MainTex.Load(int3(112 + e, side, 0)).w > 0) continue;
                        float3 ua = a.xyz, ub = b.xyz;
                        Unheld(ua, ub, floor((b.w - 10) / 10) / 1000, m, scale);
                        a.xyz = ua;
                        b.xyz = ub;
                        // Shifted by give / (1 - give) of its overlap where it is, it settles at give of
                        // the overlap where it was. A finger out leans by as much less as it is thinner,
                        // as the tissue's stiffness under a contact goes with its radius.
                        float3 pb, fa, fb;
                        float rf, c = min(_Give, 0.95) / (1 - min(_Give, 0.95));
                        Hand(a, b, pb, fa, fb, rf);
                        pushed += Lean(tip.xyz, a.xyz, pb, a.w, radius, c);
                        if (rf > 0) pushed += Lean(tip.xyz, fa, fb, rf, radius, c * rf / a.w);
                    }
                    // Each grip pulls the whole by the share of a press it takes, the skin it holds
                    // following the hand the rest of the way.
                    [unroll] for (uint gi = 0; gi < 8; gi++)
                    {
                        float3 gat;
                        float graw;
                        float4 d = Pulled(gi, m, side, gat, graw);
                        pushed += mul((float3x3) m, d.xyz) * (d.w * min(_Give, 0.95));
                    }
                }
                // The wearer's own hands too; their arms and thighs only dent it, or an arm resting
                // against its side would shove the whole away.
                uint own = (uint) _MainTex.Load(int3(41, side, 0)).x & 0x44u;
                [branch] if (own != 0)
                {
                    float c = min(_Give, 0.95) / (1 - min(_Give, 0.95));
                    [unroll] for (uint k = 2; k < 8; k += 4)
                    {
                        [branch] if (own & (1u << k))
                        {
                            float3 a, b;
                            float r;
                            Own(k, scale, a, b, r);
                            float hold;
                            Held(a, b, r, m, scale, hold);
                            Unheld(a, b, hold, m, scale);
                            pushed += Lean(tip.xyz, a, b, r, radius, c);
                        }
                    }
                }
                next = lerp(next, rest + lift + pushed, min(spring * spring, 1));
                [branch] if (_Pair > 0)
                {
                    float4 other = _MainTex.Load(int3(30, partnerSide, 0));
                    // As far apart as they sat as built: from where the anchors are now, two breasts
                    // the cloth swung together read as resting that close.
                    float keep = _PairKeep * length(_PairOffset.xyz) * scale;
                    float3 apart = next - other.xyz;
                    float d = length(apart);
                    if (other.w > 0 && abs(_Time.y - other.w) < 0.1 && d < keep && d > 1e-6) next += apart * (0.5 * (keep - d) / d);
                }
                float len0 = zc * scale;
                float3 off = next - root;
                float len = max(length(off), 1e-6);
                next = root + off * (clamp(len, 0.9 * len0, 1.1 * len0) / len);
                float3 swung = next - rest;
                float most = _Limit * len0;
                if (dot(swung, swung) > most * most) next = rest + swung * (most / length(swung));
                return float4(next, _Time.y);
            }

            // Where particle p, at l, goes this pass, and its neighbourhood's turn: toward the rigid
            // fit of its neighbourhood, toward its rest on the anchor, and out of any hand in reach.
            // With predicted, where each particle is headed is the first pass's; otherwise it is
            // worked out here from both halves.
            float3 Moved(uint p, int3 l, float4x4 m, float scale, float h, float3 home, bool predicted, bool distant, uint side, float4 swing, out float4 q)
            {
                q = Mul(swing, Quat((float3x3) m / scale));
                // The back face rides the anchor.
                if (l.z == 0 || distant) return home;
                float4 s = _MainTex.Load(Texel(p, 0));
                float3 ahead, base;
                float kh = 0;
                if (predicted)
                {
                    // The first pass's mark: 0 for a particle it put back at rest.
                    if (!(s.w > 0.5)) return home;
                    ahead = base = s.xyz;
                }
                else
                {
                    float4 was = _MainTex.Load(Texel(p, 1));
                    if (Fresh(s, was, home)) return home;
                    // Every right-half w is the h of the same pass, so this holds for the neighbours too.
                    kh = exp(-_Damp * was.w) / max(was.w, 1e-4) * h;
                    base = Ahead(s, was, l, side, home, h);
                    ahead = base + Gravity * h * h;
                }
                float3 c, rc;
                float4 lin;
                // The rest round l as the homes have it, the whole's squash taken. Held to the plain
                // rest while the homes moved, each particle was pulled two ways, and the lattice's sides
                // flickered a frame apart under a held press.
                float3x3 sw = ShapedAt(l, Shape(side));
                [branch] if (predicted) q = Fit(p, l, ahead, base, kh, m, h, home, q, true, sw, c, rc, lin);
                else q = Fit(p, l, ahead, base, kh, m, h, home, q, false, sw, c, rc, lin);
                // Rigid, or keeping _Squish of its neighbourhood's own squash and stretch at constant
                // volume, so flesh pressed in one way bulges out the others.
                float3 rigid = mul(Turn(q), mul(sw, Rest(l) - rc) * scale);
                float3 goal = c + (lin.w > 0 ? lerp(rigid, lin.xyz, _Squish) : rigid);
                // _Stiff is the pull over a 180th of a second, so the tissue feels the same
                // whether a frame takes one pass or two.
                float3 next = lerp(ahead, goal, 1 - pow(1 - _Stiff, h * 180));
                // And the whole of it toward its rest on the anchor, as a spring of _Hz: the
                // neighbourhoods alone keep the shape near each particle but let a block 16
                // across fold under its own weight, 42 cm at the front.
                float spring = 6.2831853 * _Hz * h;
                // Upright, gravity hangs the tissue g (1 - spring^2) / w^2 below where this pull holds
                // it; tissue modelled on a body hangs already, so it is held as far above.
                float3 lift = 0;
                if (_Lift > 0) lift = _Lift * normalize(m._m01_m11_m21) * 9.81 * _Gravity * (1 - spring * spring) / (39.478418 * _Hz * _Hz);
                // The pair meet at a plane between them, moving and tilting with them, with no friction,
                // so they slide along it and never stick (Joe: ""not sticky ... they push together"").
                // Each home past it is eased back behind it in order, the face it meets flat, and the
                // whole grows across by the volume that takes, so what is pressed flat goes up, down and
                // forward, never into nothing (Joe: ""it can't disappear into a void""). Read from the
                // masses, where each breast is as a whole; only closer than they sat as built do they
                // press, each taking half. Before, each read the other's homes through its body's
                // distance field: the lens between them was pressed to half and lost, and a hard push
                // passed through.
                float3 held = home, pn = 0;
                float pd = 0, meet = 0;
                bool plane = false;
                [branch] if (_Pair > 0 && _PairReach.x > 0)
                {
                    uint rows = side - Base, other = (Base / (rows + 1)) ^ 1, ps = other * (rows + 1) + rows;
                    float zc = 0.5 * (Count().z - 1) * _Lattice.w;
                    float4 ta = _MainTex.Load(int3(30, side, 0)), tb = _MainTex.Load(int3(30, ps, 0));
                    float3 ca = ta.w > 0 ? ta.xyz : mul(m, float4(0, 0, zc, 1)).xyz;
                    float3 cb = tb.xyz;
                    [branch] if (!(tb.w > 0)) cb = mul(Anchor(other), float4(0, 0, zc, 1)).xyz;
                    float dist = distance(ca, cb), wa = (other == 1 ? _PairReach.x : _PairReach.y) * scale;
                    float e = 0.5 * max((length(_PairOffset.xyz) - _PairReach.z) * scale - dist, 0);
                    meet = e > 0 ? Meet(m, ps) : 0;
                    [branch] if (e > 0 && dist > 1e-4 && meet > 0)
                    {
                        plane = true;
                        pn = (cb - ca) / dist;
                        pd = dot(ca, pn) + wa - e;
                        // Across, the cap past the plane of a ball the breast's half width given back,
                        // none at the chest wall.
                        float cap = e * e * (3 * wa - e) / (4 * wa * wa * wa), w = (float) l.z / max(Count().z - 1, 1);
                        float3 from = held - ca;
                        held += (from - pn * dot(from, pn)) * ((rsqrt(max(1 - cap, 0.5)) - 1) * w);
                        // Behind the plane by a soft minimum with it, so layers keep their order and the
                        // face is flat to a twelfth of the half width; eased in as they meet.
                        float a = dot(held, pn) - pd, soft = 0.08 * wa;
                        float eased = min(a, 0) - soft * log(1 + exp(-abs(a) / soft));
                        held += pn * ((eased - a) * saturate(e / soft));
                        held = lerp(home, held, meet);
                    }
                }
                next = lerp(next, held + lift, min(spring * spring, 1));
                // The hands found in reach, each from the wrist to its tip with a radius. Slots fill in
                // order, so an empty first one means there are none. Unrolled, so Shader Filtering
                // leaves it whole.
                // Each goes on ahead of the palm and any finger out, toward the region's centre.
                [branch] if (_MainTex.Load(int3(1, side, 0)).w > 0.5)
                {
                    float4 mass = _MainTex.Load(int3(30, side, 0));
                    float3 centre = mass.w > 0 ? mass.xyz : mul(m, float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1)).xyz;
                    [unroll] for (int e = 0; e < 8; e++)
                    {
                        float4 a = _MainTex.Load(int3(2 * e, side, 0)), b = _MainTex.Load(int3(2 * e + 1, side, 0));
                        if (!(b.w > 0.5)) continue;
                        // A gripping hand carries the skin below rather than pushing it.
                        if (_MainTex.Load(int3(112 + e, side, 0)).w > 0) continue;
                        float3 pb, fa, fb;
                        float rf;
                        Hand(a, b, pb, fa, fb, rf);
                        next = Plow(next, a.xyz, pb, a.w, centre);
                        if (rf > 0) next = Plow(next, fa, fb, rf, centre);
                    }
                    // Each grip takes the skin it closed on along with the hand: all of it at the
                    // grip, none a hand's breadth off, the neighbourhoods' fit bending what lies
                    // between. Let go, the tissue's own spring brings it back.
                    [unroll] for (uint gi = 0; gi < 8; gi++)
                    {
                        float3 gat;
                        float graw;
                        float4 d = Pulled(gi, m, side, gat, graw);
                        [branch] if (d.w > 0)
                        {
                            float w = smoothstep(2 * _MainTex.Load(int3(2 * gi, side, 0)).w / scale, 0, distance(Rest(l), gat));
                            // Its rest on the anchor carried by the pull, so the skin under the fist
                            // sits at the fist: on its home, the whole's lean after the hand counted twice.
                            next = lerp(next, lerp(home, mul(m, float4(Rest(l) + d.xyz, 1)).xyz, w), w);
                        }
                    }
                }
                // The wearer's own arms, hands and thighs in reach. None in reach, one read.
                uint own = (uint) _MainTex.Load(int3(41, side, 0)).x;
                [branch] if (own != 0)
                {
                    float4 mass = _MainTex.Load(int3(30, side, 0));
                    float3 centre = mass.w > 0 ? mass.xyz : mul(m, float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1)).xyz;
                    [unroll] for (uint c = 0; c < 16; c++)
                    {
                        [branch] if (own & (1u << c))
                        {
                            float3 a, b;
                            float r;
                            Own(c, scale, a, b, r);
                            float hold;
                            if (c == 2 || c == 6) Held(a, b, r, m, scale, hold);
                            next = Out(next, a, b, r, centre);
                        }
                    }
                }
                // A particle the hands drive past the plane goes half the way back to its home's side of
                // it each pass, each to its own home's depth, so the layers keep their order: put on the
                // plane itself, a slab of the lattice lay flat there, skin and what is worn over it on
                // one plane, and the two drew through each other in patches.
                [branch] if (plane)
                {
                    // Never further back than its own home, eased by how hard they press: sent onto
                    // the plane outright, the edge moved 13.6 mm with the pair resting as built.
                    float a = dot(next, pn) - pd, hb = dot(held, pn) - pd;
                    if (a > max(hb, 0)) next -= pn * (0.5 * (a - hb) * meet);
                }
                // A world solid, felt through the probes it pushed: each a flat face where its ball
                // met it, facing the way it pushed and reaching _JellyProbePatch across, and the skin
                // in front of one put back on it. A pane pushes them all alike and flattens the whole
                // front at once; a ball dropped on it pushes a few and dents only there. Rebuilt as
                // two balls, as a hand is, a pane pressed two dents and the rest showed through it.
                // The face is where the ball's side touched, its centre out from the tip along the
                // skin's own outward way as the build set it: offset along the push instead, a ball
                // lagging sideways in a turn made a sideways wall that threw the tissue 59 cm. And only
                // past 2 cm: a frontal solid reaches the skin at 4, and a turn's lag is millimetres.
                float4x4 sback;
                [branch] if (SolidRegion(Region) && Pushed(side) && Probing(Region, sback))
                {
                    float3 smid = mul(_JellyAnchor[2 + Region], float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1)).xyz;
                    [unroll] for (uint fk = 0; fk < PROBES; fk++)
                    {
                        float3 fat, fway;
                        float fg = fk < (uint) _JellyProbes ? Probe(Region, fk, sback, fat, fway) : 0;
                        [branch] if (fg > 0.02)
                        {
                            float3 fout = normalize(fat - fway * fg - smid);
                            float3 fo = next - (fat + fout * (_JellyProbeReach - BALL) - fway * BALL);
                            float fa = dot(fo, fway), across = length(fo - fway * fa);
                            if (fa < 0) next -= fway * (fa * (1 - smoothstep(0.5 * _JellyProbePatch, _JellyProbePatch, across)));
                        }
                    }
                }
                // A board under it on its anchor, which it rests on: dropped, it sagged through.
                [branch] if (_Floor.x > 0)
                {
                    float3 lp = Into(m, next);
                    if (lp.y < _Floor.y) next = mul(m, float4(lp.x, _Floor.y, lp.z, 1)).xyz;
                }
                return next;
            }

            // The texture parser's two texels, each a range between its task's bounds: where the
            // front centre is in the anchor's own space, x, y and z each from -0.5 to 0.5; and how its
            // neighbourhood has turned against the anchor, as Unity's Euler angles, each from -180 to
            // 180. From the same step the front centre's own texel takes.
            float4 Rider(int x, float4x4 m, float scale, float h, bool predicted, bool distant, uint side)
            {
                int3 n = Count();
                int3 l = int3(n.x / 2, n.y / 2, n.z - 1);
                float3 home = mul(m, float4(Rest(l), 1)).xyz;
                float4 q;
                float3 at = Moved((uint) Index(l), l, m, scale, h, home, predicted, distant, side, float4(0, 0, 0, 1), q);
                float3x3 r = (float3x3) m / scale;
                if (x == 16) return float4(saturate(mul(transpose(r), at - m._m03_m13_m23) / scale + 0.5), 1);
                float3x3 t = mul(transpose(r), Turn(q));
                // Unity turns by z, then x, then y: t = Ry Rx Rz.
                float3 euler = degrees(float3(asin(clamp(-t[1][2], -1, 1)), atan2(t[0][2], t[2][2]), atan2(t[1][0], t[1][1])));
                return float4(saturate(euler / 360 + 0.5), 1);
            }

            // The anchor's move since it last stepped, at the lattice's far corner, about 0.2 of the
            // anchor's units out.
            float AnchorMoved(float4x4 m, uint side)
            {
                float3 x = _MainTex.Load(int3(20, side, 0)).xyz, y = _MainTex.Load(int3(21, side, 0)).xyz, at = _MainTex.Load(int3(22, side, 0)).xyz;
                return distance(at, m._m03_m13_m23) + 0.2 * (distance(x, m._m00_m10_m20) + distance(y, m._m01_m11_m21));
            }

            // How long the tissue has stood still, in summed frame times so it holds at any frame
            // rate: the rider's texels a frame apart, the front centre under 2 mm/s and its
            // neighbourhood's turn under a degree a second, and the anchor under 2 mm/s. Read off
            // the second pass's own row, since a stillness test in the first, beside the hand scan,
            // slowed every step a fast GPU took near other players.
            float4 Still(float4 here, float4x4 m, float scale, float dt, bool distant, uint side)
            {
                float3 front = _MainTex.Load(int3(16, side, 0)).xyz - _MainTex.Load(int3(23, side, 0)).xyz;
                float3 turn = _MainTex.Load(int3(17, side, 0)).xyz - _MainTex.Load(int3(24, side, 0)).xyz;
                bool still = !distant && AnchorMoved(m, side) < 0.002 * dt && length(front) * scale < 0.002 * dt && length(turn) * 360 < dt;
                return float4(still ? here.x + dt : 0, 0, 0, 1);
            }

            // How long the region has lain still with no hand near, in x, counted at _Time.y in y: no
            // hand kept, nothing of the wearer's in reach moving 2 cm a second against the anchor
            // (texel 37, the first pass's), and nine particles of the front moving under 1 cm a second
            // against it. Still, not at rest, so an arm resting on it rests with its dent. Counted
            // afresh after any gap, as when the parser switched the steps off, so a wake needs _Rest
            // of quiet before it rests again. Predicted, the first pass's places carry a step of
            // gravity the fit takes back, 1.2 mm a frame at 90: taken off here.
            float4 Settling(float4 here, float4x4 m, float scale, float dt, bool distant, uint side, float3 fall)
            {
                float4 x0 = _MainTex.Load(int3(26, side, 0)), y0 = _MainTex.Load(int3(27, side, 0)), z0 = _MainTex.Load(int3(28, side, 0)), o0 = _MainTex.Load(int3(29, side, 0));
                float4x4 last = float4x4(x0.x, y0.x, z0.x, o0.x, x0.y, y0.y, z0.y, o0.y, x0.z, y0.z, z0.z, o0.z, 0, 0, 0, 1);
                bool quiet = !distant && o0.w > 0.5 && !(_MainTex.Load(int3(1, side, 0)).w > 0.5) && !Pushed(side) && _MainTex.Load(int3(37, side, 0)).x < 0.02 * scale * dt;
                int3 n = Count();
                [unroll] for (int k = 0; k < 9; k++)
                {
                    uint p = (uint) Index(int3((k % 3) * (n.x - 1) / 2, (k / 3) * (n.y - 1) / 2, n.z - 1));
                    float3 now = _MainTex.Load(Texel(p, 0)).xyz - fall, was = _MainTex.Load(Texel(p, 1)).xyz;
                    quiet = quiet && length(Into(m, now) - Into(last, was)) * scale < 0.01 * scale * dt;
                }
                bool counting = here.y > 0 && _Time.y - here.y < 0.1;
                return float4(quiet ? (counting ? here.x : 0) + dt : 0, _Time.y, 0, 1);
            }

            // Whether both passes may leave the tissue be next frame: still long enough, no hand in
            // reach this frame, and the anchor within 0.1 mm of where it last stepped. Decided here,
            // a frame ahead, since a pass cannot read its own row.
            bool Sleepy(float4x4 m, bool distant, uint side)
            {
                // Any slot, a hand or a probe's ball: it was w == 1, from before a slot's w carried
                // 10 and the gesture, so a sleeping prop woke for no hand at all. Or a world solid on
                // its probes, which takes no slot: pressed and still, it slept through the press.
                bool hand = _MainTex.Load(int3(1, side, 0)).w > 0.5 || Pushed(side);
                return _Sleep > 0 && !(_Stress > 0) && !distant && !hand && AnchorMoved(m, side) < 1e-4 && _MainTex.Load(int3(19, side, 0)).x >= _Sleep;
            }

            // The slaps, texels 80 to 84, which the draw spreads over the skin as rings on top of the
            // lattice's own give. A slap is a hand reaching the skin faster than
            // _Slap.x metres a second, judged on the skin's box taken as an ellipsoid: 84 keeps the
            // nearest palm's distance off it from last frame, so a hand already resting in fires
            // nothing, nor does the first frame after a gap. Each lands as a ring for the draw (80 and
            // 81, 82 and 83: where and when, then its depth in w, _Slap.y a metre a second up to
            // _Slap.z), in the slot with less left of its own, never over a live stronger one. The
            // hands are the slots the scan wrote last frame, less the probes', which ring below.
            // How fast the anchor moved since the state was last stepped, in metres a second; 0 with
            // no anchor kept.
            float3 AnchorVelocity(float4x4 m, float dt, uint side)
            {
                float4 at = _MainTex.Load(int3(29, side, 0));
                return at.w > 0 ? (m._m03_m13_m23 - at.xyz) / max(dt, 1e-4) : 0;
            }

            float4 Slap(int x, float4x4 m, float scale, float dt, bool distant, uint side)
            {
                float4 box = Region == 0 ? _JellyBox0 : _JellyBox1;
                float3 half = box.z > 0 ? float3(box.xy, 0.5 * box.z) : 0.5 * (float3) (Count() - 1) * _Lattice.w, mid = float3(0, 0, half.z);
                float near = 1e9;
                float3 at = 0;
                [branch] if (!distant && _MainTex.Load(int3(1, side, 0)).w > 0.5)
                {
                    float3 centre = mul(m, float4(mid, 1)).xyz;
                    [unroll] for (int e = 0; e < 8; e++)
                    {
                        float4 a = _MainTex.Load(int3(2 * e, side, 0)), b = _MainTex.Load(int3(2 * e + 1, side, 0));
                        // A probe's ball has no length. Taken as a palm, its first frame read as the
                        // nearest palm closing from wherever another one was.
                        if (!(b.w > 0.5) || all(b.xyz == a.xyz)) continue;
                        float3 pb, fa, fb;
                        float rf;
                        Hand(a, b, pb, fa, fb, rf);
                        float3 seg = pb - a.xyz, p = a.xyz + seg * saturate(dot(centre - a.xyz, seg) / max(dot(seg, seg), 1e-9));
                        float3 q = (Into(m, p) - mid) / half;
                        float lq = max(length(q), 1e-6), d = (lq - 1) * min(half.x, min(half.y, half.z)) * scale - a.w;
                        if (d < near)
                        {
                            near = d;
                            at = mul(m, float4(mid + q / lq * half, 1)).xyz;
                        }
                    }
                }
                if (x == 84) return float4(near, 0, 0, _Time.y);
                float4 keep = _MainTex.Load(int3(x, side, 0)), last = _MainTex.Load(int3(84, side, 0));
                float4 r0 = _MainTex.Load(int3(80, side, 0)), s0 = _MainTex.Load(int3(81, side, 0));
                float4 r1 = _MainTex.Load(int3(82, side, 0)), s1 = _MainTex.Load(int3(83, side, 0));
                bool timed = last.w > 0 && _Time.y - last.w < 0.1;
                float speed = (last.x - near) / max(dt, 1e-4);
                bool fired = timed && last.x > 0 && last.x < 0.5 && near <= 0 && speed > _Slap.x;
                // A collider only the probes know of: a probe's push crossing its reach, the collider
                // reaching the skin, timed over the frame as a palm's crossing of the box is. Measured
                // from the skin itself, the slap's end, a film's slap read a third as fast. Last
                // frame's push is the readout's, from 88. A ring under 0.2 s old within two reaches
                // is the same collider's: a hand carrying one as well rang through the arrays a few
                // frames before the probes, and as a palm goes in the probe furthest in changes and
                // crosses faster. Such a one deepens that ring and never adds a second. Unrolled:
                // Shader Filtering cuts a [loop] short.
                float4x4 back;
                int into = -1;
                [branch] if (!fired && timed && !distant)
                [branch] if (Probing(Region, back))
                {
                    speed = _Slap.x;
                    float skin = max(_JellyProbeReach, 0.002);
                    float3 balls[PROBES], rests[PROBES];
                    float deep[PROBES];
                    Felt(Region, back, balls, rests, deep);
                    [unroll] for (uint k = 0; k < PROBES; k++)
                    [branch] if (deep[k] >= skin)
                    {
                        float gone = deep[k], was = _MainTex.Load(int3(88 + k, side, 0)).w;
                        float rate = (gone - was) / max(dt, 1e-4);
                        float3 rest = rests[k];
                        float apart = 2 * (_Hand + _JellyProbeReach);
                        int same = _Time.y - r0.w < 0.2 && distance(r0.xyz, rest) < apart ? 0 : _Time.y - r1.w < 0.2 && distance(r1.xyz, rest) < apart ? 1 : -1;
                        float had = same == 0 ? s0.w : same == 1 ? s1.w : 0;
                        [flatten] if (was >= 0 && was < skin && gone >= skin && rate > speed && min(_Slap.y * rate, _Slap.z) > had)
                        {
                            speed = rate;
                            at = rest;
                            fired = true;
                            into = same;
                        }
                    }
                }
                // The tissue's own impact: its anchor stopping short, dropped onto something or
                // landing, rings it from the face its own weight carries on into, as a slap into that
                // face would; so does a yank, the tissue lagging into the face it leaves by. Only the
                // slaps that hit it rang it, never it hitting anything. Fired by more than twice a
                // slap's speed changed in a frame, at that speed; a frame faster than 8 m/s is a
                // teleport, not a fall. 85 keeps the anchor's last velocity.
                [branch] if (!fired && timed && !distant)
                {
                    float4 was = _MainTex.Load(int3(85, side, 0));
                    float3 v = AnchorVelocity(m, dt, side);
                    float lost = length(was.xyz - v);
                    [branch] if (was.w > 0 && _Time.y - was.w < 0.1 && length(was.xyz) < 8 && lost > 2 * _Slap.x)
                    {
                        float3 q = Into(m, m._m03_m13_m23 + was.xyz - v) / half;
                        at = mul(m, float4(mid + q / max(length(q), 1e-6) * half, 1)).xyz;
                        speed = lost;
                        fired = true;
                    }
                }
                // A slap that drives one of a pair into the other, their skins within a cell, rings
                // both from where they meet, out from between them, as fast as it drove that way: a
                // ring either row started on the last step from its far side. Their masses close a
                // tenth as fast as the hands that squeeze them, so never struck hard enough to ring.
                [branch] if (!fired && timed && !distant && _Pair > 0 && _PairSkin.x > 0)
                {
                    uint ps = (Region ^ 1) * (side - Base + 1) + side - Base;
                    float4 ta = _MainTex.Load(int3(30, side, 0)), tb = _MainTex.Load(int3(30, ps, 0));
                    float self = (Region == 0 ? _PairSkin.x : _PairSkin.y) * scale, other = (Region == 0 ? _PairSkin.y : _PairSkin.x) * scale;
                    float dist = distance(ta.xyz, tb.xyz);
                    [branch] if (ta.w > 0 && tb.w > 0 && dist > 1e-4 && dist - self - other < _Lattice.w * scale)
                    {
                        float3 u = (tb.xyz - ta.xyz) / dist;
                        float drove = 0;
                        [unroll] for (uint k = 0; k < 4; k++)
                        {
                            uint row = k < 2 ? side : ps;
                            float4 hit = _MainTex.Load(int3(80 + 2 * (k & 1), row, 0));
                            float way = dot(normalize((k < 2 ? ta.xyz : tb.xyz) - hit.xyz), k < 2 ? u : -u);
                            if (hit.w == last.w) drove = max(drove, _MainTex.Load(int3(81 + 2 * (k & 1), row, 0)).w / _Slap.y * way);
                        }
                        [branch] if (drove > _Slap.x)
                        {
                            at = ta.xyz + (tb.xyz - ta.xyz) * (self / (self + other));
                            speed = drove;
                            fired = true;
                        }
                    }
                }
                // A grip let go, or slipped: the skin it held springs back from as far as it was
                // pulled, at the speed the tissue's own spring gives that far out, ringing from where
                // it was gripped. 120 on keep each one's last pull.
                [branch] if (!fired && timed && !distant)
                {
                    [unroll] for (uint gi = 0; gi < 8; gi++)
                    {
                        float3 gat;
                        float graw;
                        Pulled(gi, m, side, gat, graw);
                        float4 hb = _MainTex.Load(int3(2 * gi + 1, side, 0)), pull = _MainTex.Load(int3(120 + gi, side, 0));
                        bool gripped = _MainTex.Load(int3(112 + gi, side, 0)).w > 0;
                        float back = length(pull.xyz) * scale * 6.2831853 * _Hz;
                        [flatten] if (gripped && (!Fist(hb) || graw > (Count().z - 1) * _Lattice.w) && back > max(fired ? speed : 0, _Slap.x))
                        {
                            at = mul(m, float4(gat, 1)).xyz;
                            speed = back;
                            fired = true;
                        }
                    }
                }
                if (!fired) return keep;
                float depth = min(_Slap.y * speed, _Slap.z);
                float left0 = s0.w > 0 ? s0.w * saturate(1 - (_Time.y - r0.w) / _Slap.w) : 0;
                float left1 = s1.w > 0 ? s1.w * saturate(1 - (_Time.y - r1.w) / _Slap.w) : 0;
                int slot = into >= 0 ? into : left0 <= left1 ? 0 : 1;
                if (into < 0 && min(left0, left1) > depth || (x - 80) / 2 != slot) return keep;
                return (x & 1) == 0 ? float4(at, _Time.y) : float4(0, 0, 0, depth);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                int2 px = int2(i.pos.xy);
                int3 n = Count();
                uint count = n.x * n.y * n.z, side = Rows();
                // A third pass, where there is a rider, copies its two texels into a texture of their
                // own: the texture parser reads back a whole texture, and the whole state costs it
                // ten times two texels. Its third texel says whether the steps should run at all: the
                // parser switches their blitters off far from the viewer or well behind them, and on
                // again once near and in view. Off, the state keeps its last far switch, so the same
                // rule brings them back.
                if (_Pass > 1.5)
                {
                    if (px.x < 2) return _MainTex.Load(int3(16 + px.x, side, 0));
                    bool off = Away(_MainTex.Load(int3(18, side, 0)).x > 0.5);
#ifdef JELLY_POINTS
                    [branch] if (!off && _Rest > 0) off = Resting();
#endif
                    // Every other 90th of a second by the clock, not by frames, so the parser's few
                    // frames of lag only shift which. Never under 60 fps, where half is too few.
                    [branch] if (!off && Halved()) off = _MainTex.Load(int3(25, side, 0)).x < 1.0 / 60 && frac(_Time.y * 45 + 0.25) >= 0.5;
                    return float4(off ? 0 : 1, 0, 0, 1);
                }
                float4 here = _MainTex.Load(int3(px, 0));
                // The cost meter's load rests in even phases, as cheap as a pass can be.
                if (_Stress > 0 && fmod(floor(_Time.y / Window), 2) < 0.5) return here;
                // Packed regions share the passes, the scan's player arrays and the draw's texture:
                // each is a block of rows with its own anchor, and from here on side is its own side
                // row. The third pass reads the first region's.
                uint region = (uint) px.y / (side + 1);
                Base = region * (side + 1);
                side += Base;
                // The rows padding the region out, before any work.
                if ((uint) px.y != side && (uint) px.x % Row + ((uint) px.y - Base) * Row >= count) return 0;
                Region = region;
                Mine = AnchorOf(region);
                float4x4 m = Mine;
                float scale = length(m._m00_m10_m20);
                bool anchored = scale > 1e-6;
                // The step is held while the frame time, smoothed, stays within 15% of it: where the
                // tissue rests moves with the step, so stepping each uneven frame by its own time
                // kept a settled prop trembling, never asleep. The first pass decides; the second
                // reads its answer.
                float4 clock = _MainTex.Load(int3(25, side, 0));
                float real = clamp(unity_DeltaTime.x, 1.0 / 240, 1.0 / 30), smooth = clock.y > 0 ? lerp(clock.y, real, 0.05) : real;
                float held = _Pass > 0.5 ? max(clock.x, 1.0 / 240) : clock.x > 0 && abs(smooth - clock.x) < 0.15 * clock.x ? clock.x : smooth;
                // Skipped by the half rate, the step takes the frames it missed, counted against this
                // frame's own time so a slow frame is not taken twice. A longer gap is a wake and
                // steps one frame, as before. The first pass counts; the second reads its count.
                // Not asked whether it is halved: && runs both sides, and that alone cost every step 10%.
                float frames = clock.w;
                [branch] if (_Pass < 0.5)
                {
                    float f = clock.z > 0 ? round((_Time.y - clock.z) / real) : 1;
                    frames = f >= 2 && f <= 3 ? f : 1;
                }
                frames = max(frames, 1);
                float dt = min((_FixedDt > 0 ? _FixedDt : held) * frames, 1.0 / 30);
                FrameDt = dt;
                int passes = dt > 1.0 / 72 ? 2 : 1;
                float h = dt / passes;
                // The meter's load always takes a whole step, the cost it is there to show.
                bool predicted = passes == 1 && !(_Stress > 0);
                // Far from the viewer's head or well behind them the tissue hangs rigid. The first pass
                // decides and the second reads its answer. No head, as without a humanoid avatar, counts
                // as near.
                float4 switches = _MainTex.Load(int3(18, side, 0));
                bool wasDistant = switches.x > 0.5, distant = wasDistant;
                if (_Pass < 0.5) distant = !(_Stress > 0) && Away(wasDistant);
                // Asleep: settled, untouched, on an anchor that has not moved. The second pass decided
                // it last frame, so both passes take the same way; a moved anchor or a hand wakes it
                // the frame after.
#ifdef JELLY_SLEEP
                bool asleep = switches.y > 0.5 && !distant;
#else
                bool asleep = false;
#endif

                if ((uint) px.y == side)
                {
                    if (!anchored) return 0;
                    if (_Pass < 0.5)
                    {
                        if (px.x == 18) return float4(distant, asleep, 0, 1);
                        if (px.x == 25) return float4(held, smooth, _Time.y, frames);
#ifdef JELLY_SCAN
                        [branch] if (px.x < 16)
                        {
                            if (distant) return 0;
                            return Scan(px.x, m, scale, side);
                        }
#endif
                        [branch] if (px.x >= 32 && px.x <= 34 && _Solid > 0) return Bulk(px.x - 32, m, distant, side);
                        // How far the wearer's capsules in reach moved against the anchor since last frame,
                        // for the rest's count.
                        [branch] if (px.x == 37) return float4(Shifted(m, scale, side, false), 0, 0, 1);
                        // A readout for the verify: the plane's distances as the step reads them.
                        [branch] if (px.x == 40)
                        {
                            float zc = 0.5 * (Count().z - 1) * _Lattice.w;
                            uint ps = (region ^ 1) * (side - Base + 1) + side - Base;
                            float4 ta = _MainTex.Load(int3(30, side, 0)), tb = _MainTex.Load(int3(30, ps, 0));
                            float3 ca = ta.w > 0 ? ta.xyz : mul(m, float4(0, 0, zc, 1)).xyz;
                            float3 cb = tb.xyz;
                            [branch] if (!(tb.w > 0)) cb = mul(Anchor(region ^ 1), float4(0, 0, zc, 1)).xyz;
                            return float4(distance(ca, cb), length(_PairOffset.xyz) * scale, _PairReach.z * scale, scale);
                        }
                        // Each of the wearer's own capsules' ends for the draw, as the hands' slots are,
                        // and on its far end whether it is in reach: judged here side by side, the
                        // second pass gathers them into 41. One texel judging all sixteen in turn was a
                        // seventh of the step.
                        [branch] if ((px.x >= 42 && px.x <= 57) || (px.x >= 64 && px.x <= 79))
                        {
                            float3 a, b;
                            float r;
                            uint c = px.x < 64 ? (uint) (px.x - 42) >> 1 : 8 + ((uint) (px.x - 64) >> 1);
                            Own(c, scale, a, b, r);
                            bool reach = false;
                            [branch] if ((px.x & 1) && r > 0) reach = InReach(a, b, r, m, scale, side);
                            float hold;
                            [branch] if (c == 2 || c == 6) Held(a, b, r, m, scale, hold);
                            return (px.x & 1) == 0 ? float4(a, r) : float4(b, reach);
                        }
                        // The draw's last guard: the plane midway between the two skins where they reach
                        // toward each other, moved on by half however far they overlapped as built, and
                        // the give it eases skin past it into.
                        [branch] if (px.x == 58 || px.x == 59)
                        {
                            if (!(_Pair > 0 && _PairSkin.x > 0) || distant) return 0;
                            float zc = 0.5 * (Count().z - 1) * _Lattice.w;
                            uint ps = (region ^ 1) * (side - Base + 1) + side - Base;
                            float4 ta = _MainTex.Load(int3(30, side, 0)), tb = _MainTex.Load(int3(30, ps, 0));
                            float3 ca = ta.w > 0 ? ta.xyz : mul(m, float4(0, 0, zc, 1)).xyz;
                            float3 cb = tb.xyz;
                            [branch] if (!(tb.w > 0)) cb = mul(Anchor(region ^ 1), float4(0, 0, zc, 1)).xyz;
                            float dist = distance(ca, cb);
                            if (!(dist > 1e-4)) return 0;
                            float self = (region == 0 ? _PairSkin.x : _PairSkin.y) * scale, partner = (region == 0 ? _PairSkin.y : _PairSkin.x) * scale;
                            float overlap = max(self + partner - length(_PairOffset.xyz) * scale, 0);
                            float3 u = (cb - ca) / dist;
                            float meet = Meet(m, ps);
                            if (!(meet > 0)) return 0;
                            return px.x == 58 ? float4(u, dot(ca, u) + 0.5 * (self + dist - partner) + 0.5 * overlap) : float4(0.004 * scale, meet, 0, 1);
                        }
                        [branch] if ((px.x == 30 || px.x == 31) && _MassHz > 0)
                        {
                            uint side0 = side - Base;
                            return Mass(px.x, m, scale, dt, distant, side, region, (region ^ 1) * (side0 + 1) + side0);
                        }
                        if (px.x >= 80 && px.x <= 84) return Slap(px.x, m, scale, dt, distant, side);
                        if (px.x == 85) return float4(AnchorVelocity(m, dt, side), _Time.y);
                        // Each hand slot's grip (112 to 119), as Pulled reads it: a fist closed on the
                        // skin grips it where it closed, until it opens or has pulled twice as far as
                        // the skin follows; a fist closed off the skin, or that came in closed, never
                        // grips, as a punch is no grab. Its pull (120 to 127) kept for the ring when
                        // it lets go.
                        [branch] if (px.x >= 112)
                        {
                            uint e = ((uint) px.x - 112) & 7;
                            float4 a = _MainTex.Load(int3(2 * e, side, 0)), b = _MainTex.Load(int3(2 * e + 1, side, 0)), was = _MainTex.Load(int3(112 + e, side, 0));
                            float3 at;
                            float raw;
                            float4 d = Pulled(e, m, side, at, raw);
                            if (px.x >= 120) return float4(d.xyz, d.w > 0 ? _Time.y : 0);
                            if (distant || !Fist(b)) return 0;
                            if (was.w > 0) return raw > (Count().z - 1) * _Lattice.w ? float4(0, 0, 0, -1) : was;
                            float3 pb, fa, fb;
                            float rf;
                            Hand(a, b, pb, fa, fb, rf);
                            return was.w == 0 && Gap(a.xyz, pb, a.w, m, scale, side) <= 0.01 * scale ? float4(Into(m, Knuckles(a, b)), _Time.y) : float4(0, 0, 0, -1);
                        }
                        // Each probe as the scan reads it, where it is and how far pushed: the slaps' last
                        // frame, and a readout for the verify.
                        [branch] if (px.x >= 88 && px.x < 88 + PROBES)
                        {
                            float4x4 back;
                            float3 at, way;
                            float gone = Probing(Region, back) ? Probe(Region, px.x - 88, back, at, way) : -1;
                            return float4(at, gone);
                        }
                        return here;
                    }
                    if (px.x >= 26 && px.x <= 29)
                        return float4(px.x == 26 ? m._m00_m10_m20 : px.x == 27 ? m._m01_m11_m21 : px.x == 28 ? m._m02_m12_m22 : m._m03_m13_m23, 1);
                    // Which of the wearer's own capsules are in reach, a bit each, from the first pass's
                    // judgements. Read a frame late by the first pass, so the reach has room.
                    [branch] if (px.x == 41)
                    {
                        uint bits = 0;
                        [unroll] for (uint c = 0; c < 16; c++) bits |= _MainTex.Load(int3(c < 8 ? 43 + 2 * c : 49 + 2 * c, side, 0)).w > 0.5 ? 1u << c : 0;
                        return float4(distant ? 0 : bits, 0, 0, 1);
                    }
#ifdef JELLY_SLEEP
                    if (px.x == 18) return float4(distant, Sleepy(m, distant, side), 0, 1);
                    if (asleep) return here;
                    if (px.x == 19) return Still(here, m, scale, dt, distant, side);
                    if (px.x >= 20 && px.x <= 22) return float4(px.x == 20 ? m._m00_m10_m20 : px.x == 21 ? m._m01_m11_m21 : m._m03_m13_m23, 1);
                    if (px.x == 23 || px.x == 24) return _MainTex.Load(int3(px.x - 7, side, 0));
#else
                    // Worn, resting: the third pass then switches the steps off and the draw keeps the
                    // state as it lies. Only after a count stepped without a gap.
                    if (px.x == 18)
                    {
                        float4 rest = _MainTex.Load(int3(35, side, 0));
                        return float4(distant, _Rest > 0 && !distant && rest.y > 0 && _Time.y - rest.y < 0.1 && rest.x >= _Rest, 0, 1);
                    }
                    if (px.x == 35) return Settling(here, m, scale, dt, distant, side, predicted ? Gravity * h * h : 0);
#endif
                    [branch] if ((px.x == 16 || px.x == 17) && _Rider > 0) return Rider(px.x, m, scale, h, predicted, distant, side);
                    return here;
                }

                // Far off, both passes leave the state be, as cheap as a pass can be: the surface
                // draws the rest shape off the far switch, and a stale state starts again at rest.
                // The second discards, writing nothing. Compiled into its variant alone: a discard
                // anywhere in the first, which carries the hand scan, slowed a fast GPU's near steps,
                // even one it never reached.
                [branch] if (distant)
                {
#ifndef JELLY_SCAN
                    if (_Pass > 0.5) discard;
#endif
                    return here;
                }
                // Asleep, the same, but stamped, so it reads as stepping and wakes where it lay.
                [branch] if (asleep) return _Pass > 0.5 && (uint) px.x < Row ? float4(here.xyz, _Time.y) : here;
                uint p = (uint) px.x % Row + ((uint) px.y - Base) * Row;
                if (p >= count) return 0;
                int half_ = (uint) px.x >= Row;
                int3 l = Place(p);
                // No anchor yet: nothing to hang from. Marked, so the surface can say so.
                if (!anchored) return half_ == 0 ? float4(0, 0, 0, -1) : 0;
                // The fit's right half was settled in the first pass: no home to work out.
                if (predicted && _Pass > 0.5 && half_ == 1) return here;
                float4 swing;
                float3 home = Home(l, m, side, swing);
                float4 q;
                if (predicted && _Pass < 0.5)
                {
                    // Where each particle is headed, for the second pass to fit, w 0 for one put back
                    // at rest; the right half already takes where it was.
                    float4 s = _MainTex.Load(Texel(p, 0)), was = _MainTex.Load(Texel(p, 1));
                    bool rest = Fresh(s, was, home);
                    if (half_ == 1) return float4(rest ? home : s.xyz, h);
                    if (rest) return float4(home, 0);
                    if (l.z == 0) return float4(home, 1);
                    return float4(Ahead(s, was, l, side, home, h) + Gravity * h * h, 1);
                }
                if (predicted)
                {
                    // The fit, from the first pass's predictions.
                    return float4(Moved(p, l, m, scale, h, home, true, distant, side, swing, q), _Time.y);
                }
                // A whole step. The right half only moves along, so half the texture does next to no work.
                if (half_ == 1)
                {
                    float4 s = _MainTex.Load(Texel(p, 0));
                    return float4(Fresh(s, here, home) ? home : s.xyz, h);
                }
                return float4(Moved(p, l, m, scale, h, home, false, distant, side, swing, q), _Time.y);
            }
            ENDCG
        }
    }
    Fallback Off
}
";

        const string CommonInclude = @"// The jelly spike's lattice, shared by its passes. Written by JellySpikeBuilder; edits here are overwritten.
#ifndef JELLY_COMMON
#define JELLY_COMMON

Texture2D<float4> _MainTex;
float4 _Lattice;
// One anchor a region. Packed, each region is a block of rows of the state, its side row last.
// After the four, the wearer's own bones (JellySpikeBuilder's ownBones).
float4x4 _JellyAnchor[32];
float _Damp;
// The first row of this pixel's region, set once a pixel and added to every lattice texel.
static uint Base = 0;

static const uint Row = 64;
static const float Window = 0.5;
// The step's share of gravity: 1 hangs like flesh, 0 keeps the modelled shape however the body lies.
float _Gravity;
#define Gravity float3(0, -9.81 * _Gravity, 0)
// The rigid fit's rotation steps. Each starts from the anchor's turn, which a neighbourhood's own
// is never far from, so three land where six did.
static const int Turns = 3;

int3 Count() { return int3(_Lattice.xyz); }
// A region's lattice rows, padded so with its side row it is a whole number of eight: a block of
// rows the GPU runs together never holds both a side row's work and particles'.
uint Rows() { int3 n = Count(); return ((uint) (n.x * n.y * n.z + Row - 1) / Row + 8) / 8 * 8 - 1; }
int Index(int3 l) { int3 n = Count(); return (l.z * n.y + l.y) * n.x + l.x; }
// In floats, where a division is cheap and exact enough for any lattice a texture holds; integer
// division by a value only known at run time is emulated, a few dozen instructions.
int3 Place(uint p)
{
    int3 n = Count();
    float row = floor((p + 0.5) / n.x), slab = floor((row + 0.5) / n.y);
    return int3(p - row * n.x, row - slab * n.y, slab);
}
// Unsigned, so a neighbour's index wrapped below 0 lands outside the texture, where a load reads 0.
int3 Texel(uint p, int half_) { return int3(p % Row + half_ * Row, p / Row + Base, 0); }
float3 Rest(int3 l) { int3 n = Count(); return float3(l.x - 0.5 * (n.x - 1), l.y - 0.5 * (n.y - 1), l.z) * _Lattice.w; }

// A fresh texture, a new copy of the avatar, garbage, a teleport, or a state left still a tenth
// of a second, far off or switched off: start at rest.
bool Fresh(float4 s, float4 was, float3 home)
{
    return !(was.w > 0 && was.w < 0.1) || !(abs(s.x) + abs(s.y) + abs(s.z) + abs(was.x) + abs(was.y) + abs(was.z) < 1e6)
        || !(distance(s.xyz, home) < 1) || !(_Time.y - s.w < 0.1);
}

float4 Mul(float4 a, float4 b) { return float4(a.w * b.xyz + b.w * a.xyz + cross(a.xyz, b.xyz), a.w * b.w - dot(a.xyz, b.xyz)); }

float3x3 Turn(float4 q)
{
    float x = q.x, y = q.y, z = q.z, w = q.w;
    return float3x3(1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w),
                    2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w),
                    2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y));
}

// Of the anchor alone, so every lane takes the same way.
float4 Quat(float3x3 m)
{
    float t = m[0][0] + m[1][1] + m[2][2];
    float4 q;
    [branch] if (t > 0) { float s = sqrt(t + 1) * 2; q = float4((m[2][1] - m[1][2]) / s, (m[0][2] - m[2][0]) / s, (m[1][0] - m[0][1]) / s, 0.25 * s); }
    else
    {
        [branch] if (m[0][0] > m[1][1] && m[0][0] > m[2][2]) { float s = sqrt(1 + m[0][0] - m[1][1] - m[2][2]) * 2; q = float4(0.25 * s, (m[0][1] + m[1][0]) / s, (m[0][2] + m[2][0]) / s, (m[2][1] - m[1][2]) / s); }
        else
        {
            [branch] if (m[1][1] > m[2][2]) { float s = sqrt(1 + m[1][1] - m[0][0] - m[2][2]) * 2; q = float4((m[0][1] + m[1][0]) / s, 0.25 * s, (m[1][2] + m[2][1]) / s, (m[0][2] - m[2][0]) / s); }
            else { float s = sqrt(1 + m[2][2] - m[0][0] - m[1][1]) * 2; q = float4((m[0][2] + m[2][0]) / s, (m[1][2] + m[2][1]) / s, 0.25 * s, (m[1][0] - m[0][1]) / s); }
        }
    }
    return normalize(q);
}

// The best rigid fit of l's 3x3x3 neighbourhood, from where each of them is headed: its turn
// (Mueller and others, 2016, started from q, the anchor's), its centre c and its rest centre rc.
// Each neighbour is read once, at an offset the whole pass shares, and taken against where l is
// headed, so the sums keep their digits; one out of the lattice counts 0. With predicted, where
// each is headed is the first pass's, its texel; otherwise it is worked out here, with gravity,
// the same for all, left out of both sides of each difference. Behind a particle one in from the
// back face are the anchor's own places, since the back face rides it.
float4 Fit(uint p, int3 l, float3 ahead, float3 base, float kh, float4x4 m, float h, float3 home, float4 q, bool predicted, float3x3 sw, out float3 c, out float3 rc, out float4 lin)
{
    int3 n = Count();
    bool3 lo = l > 0, hi = l < n - 1;
    float s = _Lattice.w;
    float3 mx = m._m00_m10_m20 * s, my = m._m01_m11_m21 * s;
    float3 behind = home - mul((float3x3) m, mul(sw, float3(0, 0, s))) - Gravity * h * h;
    int sy = n.x, sz = n.x * n.y;
    // The differences summed, and summed signed by each axis's offset.
    float3 sd = 0, ax = 0, ay = 0, az = 0;
    [unroll] for (int dz = -1; dz <= 1; dz++)
    [unroll] for (int dy = -1; dy <= 1; dy++)
    [unroll] for (int dx = -1; dx <= 1; dx++)
    {
        // l itself adds nothing.
        if (dx != 0 || dy != 0 || dz != 0)
        {
            bool ok = (dx < 0 ? lo.x : dx > 0 ? hi.x : true) && (dy < 0 ? lo.y : dy > 0 ? hi.y : true) && (dz < 0 ? lo.z : dz > 0 ? hi.z : true);
            uint o = p + (uint) (dx + dy * sy + dz * sz);
            float3 d;
            if (predicted) d = _MainTex.Load(Texel(o, 0)).xyz - ahead;
            else
            {
                float3 x = _MainTex.Load(Texel(o, 0)).xyz, w = _MainTex.Load(Texel(o, 1)).xyz;
                d = x + (x - w) * kh - base;
                if (dz < 0) d = l.z == 1 ? behind + dx * mx + dy * my - base : d;
            }
            d = ok ? d : 0;
            sd += d;
            if (dx != 0) ax += dx * d;
            if (dy != 0) ay += dy * d;
            if (dz != 0) az += dz * d;
        }
    }
    // How many neighbours l has along each axis, and what their offsets sum to along it.
    float3 cnt = 1 + (float3) lo + (float3) hi, bal = (float3) hi - (float3) lo;
    float k = cnt.x * cnt.y * cnt.z;
    float3 sr = bal * float3(cnt.y * cnt.z, cnt.x * cnt.z, cnt.x * cnt.y) * s;
    c = ahead + sd / k;
    rc = Rest(l) + sr / k;
    // The spread of where they are headed against their rest places, about both centres, a column
    // for each rest axis.
    float3 a0 = ax * s - sd * (sr.x / k), a1 = ay * s - sd * (sr.y / k), a2 = az * s - sd * (sr.z / k);
    // Its own linear fit, A = Apq Aqq^-1, with its volume kept (Mueller and others, 2005): over a box
    // of offsets Aqq is diagonal, each axis's spread about the rest centre. Scaled to the anchor's
    // own scale cubed, l's place on it, and the volume kept the one the homes' stretch sw gives;
    // w 0 where it has folded or has no spread along an axis.
    float3 side2 = float3(cnt.y * cnt.z, cnt.x * cnt.z, cnt.x * cnt.y);
    float3 spread = s * s * ((float3) lo + (float3) hi - bal * bal * side2 / k) * side2;
    lin = 0;
    if (all(spread > 1e-12))
    {
        float3 c0 = a0 / spread.x, c1 = a1 / spread.y, c2 = a2 / spread.z;
        float sc = length(m._m00_m10_m20), vol = dot(c0, cross(c1, c2)) / (sc * sc * sc);
        float3 r = Rest(l) - rc;
        if (vol > 0.05) lin = float4((c0 * r.x + c1 * r.y + c2 * r.z) * pow(vol / determinant(sw), -1.0 / 3.0), 1);
    }
    [unroll] for (int it = 0; it < Turns; it++)
    {
        float3x3 t = Turn(q);
        float3 t0 = float3(t[0][0], t[1][0], t[2][0]), t1 = float3(t[0][1], t[1][1], t[2][1]), t2 = float3(t[0][2], t[1][2], t[2][2]);
        float3 spin = (cross(t0, a0) + cross(t1, a1) + cross(t2, a2)) / (abs(dot(t0, a0) + dot(t1, a1) + dot(t2, a2)) + 1e-12);
        float w = length(spin);
        if (w > 1e-9) q = normalize(Mul(float4(spin / w * sin(0.5 * w), cos(0.5 * w)), q));
    }
    return q;
}

#endif
";

        const string ClockShader = @"// The jelly spike's cost meter clock. Written by JellySpikeBuilder; edits here are overwritten.
//
// Texel 0: the frame time in ms, smoothed over about a second. Texel 1: this phase's running sum
// and count, its number and when it began. Texel 2: running means of the phases' averages with the
// load off (x) and on (y), z and w 1 once each is in. Phases are Window seconds, the load on in odd ones.
Shader ""Hidden/AvatarBridge/JellyClock""
{
    Properties
    {
        _MainTex (""Last frame's clock"", 2D) = ""black"" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 4.5
            #include ""UnityCG.cginc""
            #include ""JellyCommon.cginc""

            float4 frag(v2f_img i) : SV_Target
            {
                int x = int(i.pos.x);
                float t = _Time.y, ms = unity_DeltaTime.x * 1000;
                float4 a = _MainTex.Load(int3(0, 0, 0)), b = _MainTex.Load(int3(1, 0, 0)), c = _MainTex.Load(int3(2, 0, 0));
                float phase = floor(t / Window);
                bool fresh = !(a.w == 1) || !(b.x >= 0 && b.y >= 0);
                if (x == 0) return float4(fresh ? ms : lerp(a.x, ms, 0.02), 0, 0, 1);
                bool turned = fresh || b.z != phase;
                if (x == 1)
                {
                    if (turned) return float4(0, 0, phase, t);
                    // The first tenth of a second of a phase is left out: the GPU runs a frame or two
                    // behind, so the switch shows in the frame time late. So is a hitch, a frame half
                    // as long again as the usual, which says nothing about the load.
                    return t - b.w > 0.1 && ms < 1.5 * a.x ? float4(b.x + ms, b.y + 1, b.z, b.w) : b;
                }
                if (fresh) return 0;
                if (turned && b.y > 2)
                {
                    float mean = b.x / b.y;
                    // Each a running mean over about ten phases, so it moves every half second and
                    // settles over five.
                    return fmod(b.z, 2) < 0.5 ? float4(c.z > 0 ? lerp(c.x, mean, 0.1) : mean, c.y, 1, c.w)
                                              : float4(c.x, c.w > 0 ? lerp(c.y, mean, 0.1) : mean, c.z, 1);
                }
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
";

        const string MeterShader = @"// The jelly spike's cost meter. Written by JellySpikeBuilder; edits here are overwritten.
//
// Four rows, a number each: white the frame time now in ms; green its running mean with the load
// off; red with it on; yellow what one pass of the step costs, the difference over the load's
// passes. Dark red while the clock has never run.
Shader ""AvatarBridge/Jelly Meter""
{
    Properties
    {
        // Declared, not only set: the client points a material's render textures at the avatar
        // copy's own by walking its declared properties.
        _Clock (""The meter's clock"", 2D) = ""black"" {}
        _Load (""Passes in the load"", Float) = 32
        _Size (""Width in metres"", Float) = 0.12
    }
    SubShader
    {
        Tags { ""Queue"" = ""Overlay"" ""RenderType"" = ""Overlay"" ""IgnoreProjector"" = ""True"" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #include ""UnityCG.cginc""

            Texture2D<float4> _Clock;
            float _Load, _Size;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // A view-space billboard at the object, so it faces every eye and mirror.
                float3 centre = mul(UNITY_MATRIX_V, float4(mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz, 1)).xyz;
                float2 off = (v.uv - 0.5) * float2(_Size, _Size * 6.0 / 7.0);
                o.pos = mul(UNITY_MATRIX_P, float4(centre + float3(off, 0), 1));
                o.uv = v.uv;
                return o;
            }

            // Digits and the point in 3 by 5, a bit a pixel, the top left first.
            static const uint Font[11] = { 0x7B6F, 0x2C97, 0x73E7, 0x73CF, 0x5BC9, 0x79CF, 0x79EF, 0x7249, 0x7BEF, 0x7BCF, 0x0002 };
            static const int Cells = 7;
            // A table, not pow: pow(10, 3) can come out a hair under 1000 and drop a digit.
            static const float Ten[7] = { 1, 10, 100, 1000, 10000, 100000, 1000000 };

            // The glyph in cell `cell` of a number right aligned in Cells, blank before its first digit.
            uint GlyphOf(float value, int decimals, int cell)
            {
                int k = Cells - 1 - cell;
                if (k == decimals) return Font[10];
                int place = k < decimals ? k : k - 1;
                float whole = floor(max(value, 0) * Ten[decimals] + 0.5);
                if (k > decimals + 1 && whole < Ten[place]) return 0;
                return Font[(int) fmod(floor(whole / Ten[place]), 10)];
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 a = _Clock.Load(int3(0, 0, 0)), c = _Clock.Load(int3(2, 0, 0));
                if (!(a.w == 1)) return fixed4(0.3, 0.05, 0.05, 1);
                float down = (1 - i.uv.y) * 4;
                int row = (int) floor(down);
                int cell = (int) floor(i.uv.x * Cells);
                // A glyph is 3 by 5 in a 4 by 6 cell.
                int gx = (int) floor(frac(i.uv.x * Cells) * 4), gy = (int) floor(frac(down) * 6);
                float value = row == 0 ? a.x : row == 1 ? c.x : row == 2 ? c.y : (c.z > 0 && c.w > 0 ? max(c.y - c.x, 0) / max(_Load, 1) : 0);
                float3 colour = row == 0 ? float3(1, 1, 1) : row == 1 ? float3(0.3, 1, 0.4) : row == 2 ? float3(1, 0.35, 0.3) : float3(1, 0.85, 0.2);
                uint g = GlyphOf(value, row == 3 ? 3 : 2, cell);
                bool lit = gx < 3 && gy < 5 && ((g >> (14 - (gy * 3 + gx))) & 1) != 0;
                return fixed4(lit ? colour : float3(0.05, 0.05, 0.07), 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
";

        const string DrawShader = @"// The jelly spike's surface. Written by JellySpikeBuilder; edits here are overwritten.
//
// Every vertex goes where its particle is. Pink while the tissue steps, or at rest past the far
// switch; blue asleep; red once it has not stepped for a quarter second; purple while no anchor
// reaches it.
Shader ""AvatarBridge/Jelly Spike""
{
    Properties
    {
        // Declared, not only set: the client points a material's render textures at the avatar
        // copy's own by walking its declared properties, and an undeclared one would go on
        // reading the asset, which nothing writes.
        _State (""Tissue state"", 2D) = ""black"" {}
        _Color (""Colour"", Color) = (0.95, 0.55, 0.65, 1)
        _SleepTint (""Blue while asleep; 0 for a film, where sleep should read as stillness"", Float) = 1
        _Ring (""A slap's ring, as the avatar's _YAPS_JellySlap: how much (0 none), speed, wavelength, fade"", Vector) = (1, 1, 0.1, 0.4)
        // Declared too: an undeclared value never reaches the saved material, and without it every
        // lookup misses the tissue.
        _Lattice (""Particles across, up and deep; spacing in metres"", Vector) = (16, 12, 16, 0.0133)
    }
    SubShader
    {
        Tags { ""RenderType"" = ""Opaque"" ""Queue"" = ""Geometry"" }
        Pass
        {
            Tags { ""LightMode"" = ""ForwardBase"" }
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #include ""UnityCG.cginc""

            Texture2D<float4> _State;
            float4 _Color;
            float _SleepTint;
            float4 _Ring;
            float4 _Lattice;

            float4 Particle(int3 l, int3 n, int first)
            {
                int p = (l.z * n.y + l.y) * n.x + l.x;
                return _State.Load(int3(p % 64, first + p / 64, 0));
            }

            // A kept hand's palm and finger and how a capsule pushes skin, as yaps_jelly.cginc's
            // YapsJellyHand and YapsJellyPress, which the avatar draws with.
            void Hand(float4 a, float4 b, out float3 pb, out float3 fa, out float3 fb, out float rf)
            {
                float3 seg = b.xyz - a.xyz;
                float len = length(seg), g = fmod(b.w - 10, 10), r = a.w;
                pb = a.xyz;
                fa = fb = a.xyz;
                rf = 0;
                if (len < 1e-6) return;
                float3 dir = seg / len;
                float tall = (len + r) / 0.11, fist = g > 1.5 ? 1 : saturate(g);
                pb = a.xyz + dir * max(lerp(0.11, 0.06, fist) * tall - r, 0);
                rf = g > 2.5 ? (g > 4.5 && g < 5.5 ? 0.35 : 0.2) * r : 0;
                fa = a.xyz + dir * (0.06 * tall);
                fb = a.xyz + dir * max(0.11 * tall - rf, 0.06 * tall);
            }

            float3 Press(float3 at, float3 a, float3 b, float r, float3 centre)
            {
                float3 seg = b - a, in_ = centre - (a + 0.5 * seg);
                if (!(dot(in_, in_) > 1e-8) || !(r > 0)) return 0;
                float3 way = normalize(in_);
                float3 off = at - (a + seg * saturate(dot(at - a, seg) / max(dot(seg, seg), 1e-9)));
                float d2 = dot(off, off), along = dot(off, way);
                float ease = saturate(1 - sqrt(d2) / (2 * r));
                float through = d2 < r * r ? sqrt(max(along * along - d2 + r * r, 0)) - along : 0;
                return way * max(through, r * ease * ease);
            }

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                // z of the lattice place, the region's row of switches.
                float2 side : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 at : TEXCOORD0;
                nointerpolation float3 tint : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // On a particle, it alone; between, the eight round it, weighted by nearness.
                int3 n = int3(_Lattice.xyz);
                int row = int(v.side.y), first = row - (int) (((uint) (n.x * n.y * n.z + 63) / 64 + 8) / 8 * 8 - 1);
                float3 c = float3(v.uv, v.side.x);
                int3 c0 = min(int3(c + 1e-4), n - 1);
                float3 t = saturate(c - c0);
                float4 s = Particle(c0, n, first);
                float3 blend = s.xyz;
                [branch] if (any(t > 1e-4))
                {
                    blend = 0;
                    [unroll] for (int k = 0; k < 8; k++)
                    {
                        int3 d = int3(k & 1, (k >> 1) & 1, k >> 2);
                        float3 w = d ? t : 1 - t;
                        blend += w.x * w.y * w.z * Particle(min(c0 + d, n - 1), n, first).xyz;
                    }
                }
                // The far switch, texel 18 of the region's side row: far off nothing steps, so the
                // surface takes its rest shape on the anchor.
                float4 switches = _State.Load(int3(18, row, 0));
                float3 at = blend;
                o.tint = _Color.rgb;
                if (switches.x > 0.5) at = mul(unity_ObjectToWorld, v.vertex).xyz;
                else if (s.w < 0) o.tint = float3(0.6, 0.2, 0.9);
                else if (abs(_Time.y - s.w) > 0.25) o.tint = float3(0.9, 0.1, 0.1);
                else if (_SleepTint > 0.5 && switches.y > 0.5) o.tint = float3(0.35, 0.55, 1);
                // The hands in reach push the surface too, as the step reads them (texels 0 to 15
                // of the row): the particles sit a cell apart, so on a coarse lattice a deep hand
                // dragged whole cells and the surface between folded. Pushed as the avatar's skin is,
                // in toward the tissue's middle with the rim eased out: snapped out from the hand's
                // axis instead, neighbours went opposite ways round it and the dent's rim tore.
                [branch] if (!(switches.x > 0.5) && _State.Load(int3(1, row, 0)).w > 0.5)
                {
                    float4 mass = _State.Load(int3(30, row, 0));
                    float3 centre = mass.w > 0 ? mass.xyz : _State.Load(int3(29, row, 0)).xyz + _State.Load(int3(28, row, 0)).xyz * (0.5 * (n.z - 1) * _Lattice.w);
                    float3 move = 0;
                    [unroll] for (int e = 0; e < 8; e++)
                    {
                        float4 a = _State.Load(int3(2 * e, row, 0)), b = _State.Load(int3(2 * e + 1, row, 0));
                        if (!(b.w > 0.5)) continue;
                        if (_State.Load(int3(112 + e, row, 0)).w > 0) continue;
                        float3 pb, fa, fb;
                        float rf;
                        Hand(a, b, pb, fa, fb, rf);
                        move += Press(at, a.xyz, pb, a.w, centre);
                        if (rf > 0) move += Press(at, fa, fb, rf, centre);
                    }
                    at += move;
                }
                // A slap's ring rides out across the skin as the avatar's draw rides it
                // (yaps_jelly.cginc's YapsJellySlap), from texels 80 to 83: where and when, then how
                // deep. Without it the prop showed only the lattice's own wobble.
                float3 face = (float3) (c >= n - 1 - 1e-3) - (float3) (c <= 1e-3);
                [branch] if (!(switches.x > 0.5) && _Ring.x > 0 && any(face))
                {
                    float3 nw = normalize(mul((float3x3) unity_ObjectToWorld, face));
                    float len = max(_Ring.z, 1e-3), fade = max(_Ring.w, 1e-3), k2 = 6.2831853 / len;
                    [unroll] for (int k = 0; k < 2; k++)
                    {
                        float4 hit = _State.Load(int3(80 + 2 * k, row, 0)), how = _State.Load(int3(81 + 2 * k, row, 0));
                        float age = _Time.y - hit.w;
                        [branch] if (how.w > 0 && age >= 0 && age < 4 * fade)
                        {
                            float d = length(blend - hit.xyz), x = d - _Ring.y * age;
                            at -= nw * (min(_Ring.x * how.w, 0.08 * len) * exp(-age / fade - x * x / (len * len)) / (1 + d / len) * cos(k2 * x));
                        }
                    }
                }
                // Never written: drawn where the mesh rests, so it shows at all.
                if (!(dot(at, at) > 1e-8 && dot(at, at) < 1e8)) at = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.at = at;
                o.pos = UnityWorldToClipPos(at);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(cross(ddy(i.at), ddx(i.at)));
                float3 view = normalize(_WorldSpaceCameraPos - i.at);
                n = dot(n, view) < 0 ? -n : n;
                // A light fixed above and in front, so each face reads apart from its neighbours.
                float lit = 0.3 + 0.6 * saturate(dot(n, normalize(float3(0.4, 1, 0.6)))) + 0.15 * pow(1 - saturate(dot(n, view)), 2);
                return fixed4(i.tint * lit, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
";

        const string Readme = @"# Jelly spike

Does a soft body run on a ChilloutVR avatar, stepped every frame on the GPU, and what does it cost?

A block of jelly about 20 cm across hangs in front of the chest. Its 384 particles live in a
render texture that two blitters step every frame, into a twin and back; nothing is baked and
nothing is synced. The surface is finer than the particles: each of its points is blended from
the eight particles round it, so it stays smooth. Tools > AvatarBridge Dev > Jelly spike: coarse
lattice, ticked by default, untick it for the fine lattice of 3072 particles, which costs a slow
GPU about twice as much and looks the same within millimetres. The first pass also picks out the hands in reach, eight at most, so a full
instance costs no more than an empty one. Below 72 fps each pass takes a step; above it the first
works out where each particle is headed and the second fits. More than 6 m from you, another
player's jelly hangs rigid and costs next to nothing, and so does the prop left still and
untouched, asleep. It should sag a little, wobble when you turn or bounce, and dent and jiggle when any hand
pokes it, yours or another player's.

## Setting it up

1. Select the avatar in the scene and run Tools > AvatarBridge Dev > Jelly spike: add to the
   selected avatar. It goes under the chest bone, in front of the chest, sized for the avatar,
   replacing any earlier one. Move it if it sits inside anything. Jelly spike: add with the cost
   meter does the same with the meter above it, which costs far more than the jelly. Jelly spike:
   add to wear, without the test cube, leaves the yellow cube off, and with it a pass, a readback
   and a parser: the one to keep on. Jelly spike: add to wear, far copies switched off, keeps the
   pass and the parser for the far switch alone: each copy near you costs a little more, each far
   one less, so it only pays on a slow GPU in a crowd. Jelly spike: add three to wear, packed,
   puts three in one texture and one pair of passes, each on its own bone: in front of the chest,
   behind the hips and above the head. Jelly spike: make a prop of three, packed, sets three side
   by side on one prop.
2. Upload. Nothing else: no parameters, no menu, no sync.

On the avatar's own breasts: build the YAPS squash first, select the avatar and run Jelly spike:
put the selected avatar's breasts on the jelly. One lattice is fitted over the pair, standing on
the chest bone with its back face on the chest wall, and the body's squash materials are patched
to move each of the pair's vertices with it (the first time takes a minute or two a shader); the
squash leaves the pair to it, and the cloth or dynamic bones swinging the pair are switched off.
It rests on the modelled shape when upright. Jelly spike: take the jelly off the selected
avatar's breasts puts the squash and the physics back. Building the squash again takes the jelly
off its materials; run the menu again after.

Or as a prop on its own: Jelly spike: make a prop in the open scene, then upload it from the CCK
Control Panel. Grab it in game and shake it; let go and it hangs where you left it.

Before uploading, `Jelly_Preview.unity` shows it in the editor: open it and press Play.

## What the colour says

| Colour | Means |
| --- | --- |
| Pink | The tissue is stepping |
| Blue | The prop asleep: still for half a second, untouched, not moving; a hand or a move wakes it |
| Red | It has not stepped for a quarter second: the blitters are not running for this copy |
| Purple | The anchor never arrived: the material data provider is not running |

The meter above it has four rows: white, your frame time in ms; green, its average with the load
off; red, with it on; the load switching every half second; yellow, what one pass of
the step's own work costs, the difference over the load's 32 passes. The load steps in place, so
each of its runs also pays a copy back the jelly's own passes do not. The load only says something with the frame rate uncapped, on desktop: under VR or vsync the frame
time sits at the cap whatever the work. The load itself costs 32 passes every other half second,
so it, not the jelly, is most of the frame rate a viewer loses with the meter running. A yellow near zero on a CPU-bound frame means the GPU did
the work in time it was waiting anyway: no cost to that frame.

A plain yellow cube sits on the front, except in the wear build. Its shader knows nothing of the tissue: a texture parser
moves it, from where the front centre is. It should ride the front as the jelly wobbles and as it
is poked, tilting with it, a frame or two behind. If the jelly wobbles and the cube stays put, the
parser is not running.

Past 6 m it should go still and keep its rest shape, without the sag, and pick the wobble up again
once you are back within 5.5 m. With the cube, or worn with far copies switched off, the parser
switches its stepping off out there altogether; worn without, the steps still run but leave it be.

The prop, left still for half a second, untouched and not moving, goes to sleep and turns blue,
costing about what a far copy does; a hand in reach or any move wakes it the frame after, from
where it lay. Worn, it never sleeps: the avatar's own idle sway would keep it awake anyway, and the
test alone costs a slow GPU a few percent, so the worn builds leave it out.

## What to try

1. Wear it. Turn quickly, jump, poke it with either hand. Does it wobble and dent, and settle?
2. Your frame time with it worn against another avatar.
3. Have somebody look at you, or watch from a second client: pink and wobbling for them too?
   Can they poke it?
4. Switch to another avatar and back: pink again within a second?
5. In VR: both eyes the same?
6. Optional: somebody with the Cameras content filter off should see it red, frozen.
7. Have somebody walk more than 6 m away from you and back: still and rigid out there, wobbling
   again once close, with no jump as it switches.
8. Set the prop down and leave it: blue within a couple of seconds, pink the moment a hand comes
   near or it is picked up, with no jump either way.
9. Wear the packed three: each wobbles with its own bone, and a hand on one leaves the others be.
   Your frame time against wearing one should barely move.
10. The breasts on the jelly: still at rest when you stand still, the same shape as before; a
    jiggle when you turn, jump or walk; a dent that follows the finger, yours or another player's;
    one pushed into the other pushes it too. Somebody with the Cameras content filter off sees
    them still, never broken.
";
    }
}
#endif
