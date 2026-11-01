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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using static AvatarBridge.YapsJelly;

namespace AvatarBridge.Regression
{
    [InitializeOnLoad]
    public static class JellySpikeBuilder
    {
        // A new folder from 2026-10-27: an editor that rebuilt the old one by delete and remake
        // could no longer load its materials at all, so its assets start again under new GUIDs.
        // The probe prop's in a folder of its own, so building it leaves a worn jelly's assets be. Kept
        // in the session, which a Play mode's reload keeps too.
        static string Home => SessionState.GetBool(K + "prop", false) ? "Assets/AvatarBridge_JellyProp" : "Assets/AvatarBridge_JellySpike";
        // The second pair's jelly is a chain of its own, its assets beside the first's and the
        // shaders shared: a copy each would compile every variant twice.
        static int chain;
        static string Folder => Home + (chain > 0 ? "_" + (chain + 1) : "");
        // The chain a worn jelly is, from where its step's materials sit.
        // A draw property as the chain's pair reads it: the second pair's slot ends in 2.
        static string Slot(string name) => chain > 0 ? name + (chain + 1) : name;
        static int ChainOf(Transform jelly)
        {
            string at = AssetDatabase.GetAssetPath(jelly.GetComponentsInChildren<CVRMaterialDataProvider>(true).First(q => q.transforms != null && q.transforms.Length > 0).materials[0]);
            for (int s = YapsSquash.JellySlots; s > 1; s--)
                if (at.StartsWith($"{Home}_{s}/")) return s - 1;
            return 0;
        }
        static string Prefab => Folder + "/Jelly_Spike.prefab";
        static string Scene => Folder + "/Jelly_Preview.unity";
        const string K = "JellySpike.";
        // One pair's tissue is about 3000 particles; this is 3072, 20 cm across at 1.33 cm.
        static readonly Vector3Int Size = new Vector3Int(16, 12, 16);
        // A coarse lattice the surface blends between, an eighth of the fine one's particles. The
        // default: it sags and turns within millimetres of the fine one and costs a slow GPU about
        // half; the menu's tick takes the fine one back.
        static readonly Vector3Int Mid = new Vector3Int(12, 9, 12), Coarse = new Vector3Int(8, 6, 8);
        // 20 cm deep whatever the lattice; the probe prop 40, as people's colliders are big against
        // 20 cm, on the fine lattice, which keeps the 20 cm coarse one's detail.
        static bool Probed => SessionState.GetBool(K + "prop", false);
        static float SpacingOf(Vector3Int n) => (Probed ? 0.4f : 0.2f) / (n.z - 1);
        const string CoarseKey = "AvatarBridge.JellySpike.Coarse";
        static Vector3Int Lattice => Probed || !EditorPrefs.GetBool(CoarseKey, true) ? Size : Coarse;

        [MenuItem("Tools/Avatar Bridge/Dev/Jelly/Coarse lattice")]
        static void ToggleCoarse() => EditorPrefs.SetBool(CoarseKey, !EditorPrefs.GetBool(CoarseKey, true));

        [MenuItem("Tools/Avatar Bridge/Dev/Jelly/Coarse lattice", true)]
        static bool ToggleCoarseShown()
        {
            Menu.SetChecked("Tools/Avatar Bridge/Dev/Jelly/Coarse lattice", EditorPrefs.GetBool(CoarseKey, true));
            return true;
        }
        const int Frames = 300, Load = 32, BreastFrames = 3680, ViewFrames = 720;
        // How far off the skin a collider pushes a probe: the probe cloth's radius.
        // The probe prop's twice, as its 24 sit twice as far apart over a face four times the size.
        static float ProbeReach => Probed ? 0.08f : 0.04f;
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

        [MenuItem("Tools/Avatar Bridge/Dev/Jelly/Early spike/Add to the selected avatar")]
        static void AddToSelected() => Add(withMeter: false);

        // To wear: no test cube, so one pass, its readback and its parser fewer.
        [MenuItem("Tools/Avatar Bridge/Dev/Jelly/Early spike/Add to wear, without the test cube")]
        static void AddToWear() => Add(withMeter: false, withRider: false, offFar: false);

        // The same with the pass and the parser kept for the far switch alone: dearer near, cheaper
        // far, so it pays on a slow GPU in a crowd.
        [MenuItem("Tools/Avatar Bridge/Dev/Jelly/Early spike/Add to wear, far copies switched off")]
        static void AddToWearOffFar() => Add(withMeter: false, withRider: false, offFar: true);

        // Three regions packed into one texture and one pair of passes: on the chest, behind the
        // hips and above the head, each on its own bone.
        [MenuItem("Tools/Avatar Bridge/Dev/Jelly/Early spike/Add three to wear, packed")]
        static void AddPacked() => Add(withMeter: false, withRider: false, offFar: false, regions: 3);

        [MenuItem("Tools/Avatar Bridge/Dev/Jelly/Early spike/Make a prop of three, packed")]
        static void MakePackedProp() => MakeProp(3);

        // The meter's load costs 32 passes every other half second, far more than the jelly, so it
        // goes on only when asked for.
        [MenuItem("Tools/Avatar Bridge/Dev/Jelly/Early spike/Add with the cost meter")]
        static void AddWithMeter() => Add(withMeter: true);

        // The jelly alone as a prop, its cost told apart from any avatar's.
        [MenuItem("Tools/Avatar Bridge/Dev/Jelly/Early spike/Make a prop in the open scene")]
        static void MakeOneProp() => MakeProp(1);

        static GameObject MakeProp(int regions, bool withRider = true, string id = null)
        {
            var prefab = Make(preview: false, withMeter: false, withRider: withRider, regions: regions);
            if (prefab == null) return null;
            var old = GameObject.Find("Jelly Prop");
            // The old prop's content id kept, so an upload replaces it: rebuilt without one, each
            // upload made another prop on the account.
            if (string.IsNullOrEmpty(id)) id = PropId(old);
            if (old != null) Undo.DestroyObjectImmediate(old);
            var prop = new GameObject("Jelly Prop");
            prop.AddComponent<CVRSpawnable>();
            if (!string.IsNullOrEmpty(id))
            {
                var info = prop.GetComponent<CVRAssetInfo>();
                if (info == null)
                {
                    info = prop.AddComponent<CVRAssetInfo>();
                    info.type = CVRAssetInfo.AssetType.Spawnable;
                }
                info.objectId = id;
            }
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
            Debug.Log("[Jelly] made Jelly Prop in the open scene, grabbable" + (string.IsNullOrEmpty(id) ? "" : $", keeping its content id {id}") + "; upload it from the CCK Control Panel");
            return prop;
        }

        static string PropId(GameObject prop)
        {
            var info = prop != null ? prop.GetComponent<CVRAssetInfo>() : null;
            return info != null ? info.objectId : null;
        }

#if AVATARBRIDGE_MAGICA
        // Two jellies on one prop, MagicaCloth probes on each one's front: the left feels the player
        // arrays' hands too, the right its probes alone, so a poke that dents the right one in game
        // is a collider's, the client's capsule on the hand.
        [MenuItem("Tools/Avatar Bridge/Dev/Jelly/Make a prop of two, the right one colliders only")]
        static void MakeProbePropMenu()
        {
            SessionState.SetBool(K + "prop", true);
            try { MakeProbeProp(); }
            finally { SessionState.SetBool(K + "prop", false); }
        }

        // The same with the right one's probes physics balls the world's solid colliders push, for
        // whether a wall or a table dents it in game. Its plate and skin orange.
        [MenuItem("Tools/Avatar Bridge/Dev/Jelly/Make a prop of two, the right one feeling the world")]
        static void MakeWorldPropMenu()
        {
            SessionState.SetBool(K + "prop", true);
            try { MakeProbeProp(world: true); }
            finally { SessionState.SetBool(K + "prop", false); }
        }

        // How far the prop's twitch moves at a full trigger, in metres.
        const float PropTwitch = 0.02f;

        // Batch, for an upload: the world prop with every feature on, in a scene of its own.
        public static void MakeWorldPropScene()
        {
            SessionState.SetBool(K + "prop", true);
            try
            {
                // The last build's content id, from its own scene: a new scene has no old prop to keep it from.
                string path = Folder + "/Jelly_Prop.unity", id = null;
                if (File.Exists(path))
                {
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    id = PropId(GameObject.Find("Jelly Prop"));
                }
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                bool made = MakeProbeProp(world: true, id: id) != null;
                EditorSceneManager.SaveScene(scene, path);
                Debug.Log(made ? $"[Jelly] made the world prop in {Folder}/Jelly_Prop.unity" : "[Jelly] FAIL the world prop did not build");
            }
            finally { SessionState.SetBool(K + "prop", false); }
            EditorApplication.Exit(0);
        }

        static GameObject MakeProbeProp(bool world = false, string id = null)
        {
            // No rider: it sat on the left one's front, where the pokes go.
            var prop = MakeProp(2, withRider: false, id: id);
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
            // The world one's probes half cloth, half balls, as a breast's feeling the world: the
            // cloth's still answers players' colliders, which it alone feels.
            AddSkin(made, provider, hangs, spots, 0.5f * span.z, "Make the jelly prop", Folder, ProbeReach, world ? new[] { false, true } : null, world ? ProbesPer / 2 : 0);
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
            var held = new Bounds();
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
                var on = plate.GetComponent<MeshRenderer>().bounds;
                if (r == 0) held = on;
                else held.Encapsulate(on);
            }
            // Held by its boards alone: a box round the jellies took every grab, so a fist closing on
            // one picked the prop up instead of the skin. A little under the boards, none above.
            var grab = prop.GetComponent<BoxCollider>();
            grab.center = prop.transform.InverseTransformPoint(held.center) - new Vector3(0f, 0.015f, 0f);
            grab.size = held.size + new Vector3(0.02f, 0.03f, 0.02f);
            // Each whole swings on its root as a breast does, free of any cloth: a breast's rate for
            // the tissue's weight, about half its box, damped at 0.6 of critical.
            float hz = 2f * Mathf.Pow(0.5f / (950f * 0.5f * span.x * span.y * span.z), 1f / 3f);
            // The twitch: the holder's trigger, the prop's own synced value, moves a transform off the
            // first hang through the prop's animator, kicking both wholes as far as it moves.
            var twitch = new GameObject("Jelly Twitch").transform;
            twitch.SetParent(hangs[0], false);
            provider.transforms = provider.transforms.Append(twitch).ToArray();
            foreach (var step in new[] { "/Jelly_Step.mat", "/Jelly_Step_2.mat", "/Jelly_Points.mat" })
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(Folder + step);
                if (mat == null) continue;
                mat.SetFloat("_MassHz", hz);
                mat.SetFloat("_MassDamp", 2f * 0.6f * 2f * Mathf.PI * hz);
                mat.SetFloat("_Give", 0.1f);
                mat.SetFloat("_MassCarried", 0f);
                mat.SetFloat("_Limit", 0.6f);
                mat.SetFloat("_MassRadius", (span.x + span.y + span.z) / 6f - 0.01f);
                mat.SetFloat("_JellyTwitch", provider.transforms.Length - 1);
                // The prop shows every feature: its grips slide.
                mat.SetFloat("_GripSlip", 0.02f);
                EditorUtility.SetDirty(mat);
            }
            string path = AnimationUtility.CalculateTransformPath(twitch, prop.transform);
            AnimationClip Kick(string file, float y)
            {
                var clip = Save(new AnimationClip(), file);
                clip.ClearCurves();
                clip.SetCurve(path, typeof(Transform), "m_LocalPosition.y", AnimationCurve.Constant(0f, 0f, y));
                EditorUtility.SetDirty(clip);
                return clip;
            }
            var still = Kick("/Jelly_Twitch_Rest.anim", 0f);
            var kicked = Kick("/Jelly_Twitch_Up.anim", PropTwitch);
            // Kept once made, so a prop already in a scene keeps pointing at it.
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(Folder + "/Jelly_Prop.controller");
            if (controller == null)
            {
                controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(Folder + "/Jelly_Prop.controller");
                controller.AddParameter("Twitch", AnimatorControllerParameterType.Float);
                controller.CreateBlendTreeInController("Twitch", out var tree);
                tree.blendParameter = "Twitch";
                tree.AddChild(still, 0f);
                tree.AddChild(kicked, 1f);
            }
            var animator = prop.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            var spawnable = prop.GetComponent<CVRSpawnable>();
            spawnable.useAdditionalValues = true;
            spawnable.syncValues.Add(new CVRSpawnableValue
            {
                name = "Twitch",
                updatedBy = CVRSpawnableValue.UpdatedBy.OwnerCurrentTrigger,
                updateMethod = CVRSpawnableValue.UpdateMethod.Override,
                animator = animator,
                animatorParameterName = "Twitch",
            });
            // On the world one, a pane of glass to grab and press into it, a pickup of its own synced
            // to everyone: a world solid, as a wall or a table is. 4 cm thick and 6 cm past the face
            // all round, as the check's wall: thinner, a quick push slipped past the probes' balls.
            if (world)
            {
                var pane = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pane.name = "Glass Pane";
                pane.transform.SetParent(prop.transform, false);
                pane.transform.SetPositionAndRotation(regions[1].TransformPoint(0f, 0f, span.z + 0.1f), regions[1].rotation);
                pane.transform.localScale = new Vector3(span.x + 0.12f, span.y + 0.12f, 0.04f);
                pane.GetComponent<MeshRenderer>().sharedMaterial = Save(Glass(new Color(0.75f, 0.9f, 1f, 0.35f)), "/Jelly_Glass.mat");
                pane.AddComponent<Rigidbody>().isKinematic = true;
                pane.AddComponent<CVRPickupObject>();
                spawnable.subSyncs.Add(new CVRSpawnableSubSync { transform = pane.transform, syncBoundary = 2f });
            }
            // What the jelly tells the animator on an avatar, here three bars between the two, each
            // its height through the parser: yellow the strain, magenta a grip, red the last slap.
            var parser = made.GetComponentInChildren<CVRTexturePropertyParser>(true);
            var bars = new[] { ("Squeeze", new Color(1f, 0.85f, 0.1f)), ("Grab", new Color(0.9f, 0.2f, 0.9f)), ("Slap", new Color(0.9f, 0.1f, 0.1f)) };
            for (int i = 0; i < bars.Length; i++)
            {
                var bar = new GameObject($"Jelly {bars[i].Item1} Bar").transform;
                bar.SetParent(made.transform, false);
                bar.localPosition = new Vector3(0.03f * (i - 1), 0.5f * span.y + 0.02f, 0.02f);
                bar.localScale = new Vector3(1f, 0.001f, 1f);
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(cube.GetComponent<Collider>());
                cube.transform.SetParent(bar, false);
                cube.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                cube.transform.localScale = new Vector3(0.02f, 1f, 0.02f);
                cube.GetComponent<MeshRenderer>().sharedMaterial = Save(new Material(Shader.Find("Standard")) { color = bars[i].Item2 }, $"/Jelly_{bars[i].Item1}_Bar.mat");
                parser.tasks.Add(new CVRTexturePropertyParserTask
                {
                    x = 3 + i,
                    y = 0,
                    channel = CVRTexturePropertyParserTask.Channel.r,
                    minValue = 0.001f,
                    maxValue = 0.12f,
                    target = bar.gameObject,
                    component = bar,
                    propertyName = "localScale",
                    typeIndex = 4,
                    targetIndex = 1,
                });
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Jelly] made the probe prop, {spots[0].Count} probes a jelly, each swinging at {hz:0.00} Hz: green feels hands and colliders, {(world ? "orange colliders and the world's solids" : "blue colliders only")}; the holder's trigger twitches both; the bars between say strain, grip and slap; upload it from the CCK Control Panel");
            return prop;
        }

        // The probe prop in a scene of its own, played: settled, then a player's hand from the arrays
        // 3 cm into each jelly's front, then away, then a MagicaCloth ball 3 cm into each. Passes when
        // the hands dent the green one alone and the balls dent both.
        public static void VerifyProbeProp()
        {
            SessionState.SetBool(K + "prop", true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // -jellyWorldProp: the world one instead, for the steady check.
            if (MakeProbeProp(world: Environment.GetCommandLineArgs().Contains("-jellyWorldProp")) == null)
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
            SessionState.SetBool(K + "propSteady", Environment.GetCommandLineArgs().Contains("-jellySteady"));
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
            // Filmed, then slapped on and straight off, six frames each way: held on, its ring ran
            // out under it. 7 cm in, a hard slap.
            if (film && frame >= 450) gap = frame < 480 ? 0.15f : frame < 486 ? Mathf.Lerp(0.15f, -0.07f, (frame - 480) / 6f) : Mathf.Lerp(-0.07f, 0.15f, Mathf.Clamp01((frame - 486) / 6f));
            // Moved as physics moves a solid once it comes near, as the breasts' film pane is: set
            // outright, it pushed the balls 31 mm of a 70 mm press, never to the skin, and a slap
            // rang nothing.
            var wallAt = regions[1].TransformPoint(0f, 0f, span.z + 0.05f + gap);
            if (frame < 150) worldWall.transform.SetPositionAndRotation(wallAt, regions[1].rotation);
            else worldWall.GetComponent<Rigidbody>().MovePosition(wallAt);
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
            // Filmed, the slap's rings on the orange one, every fourth frame through it; and in the
            // held press, how far its balls really went against what its probes read.
            if (film && frame >= 476 && frame <= 512 && frame % 4 == 0 || frame == 270)
            {
                var rung = Read(state);
                int rs = (2 * rows + 1) * 2 * Row;
                float moved = regions[1].parent.GetComponentsInChildren<Transform>(true).Where(pbx => pbx.name.StartsWith("Jelly Probe Ball 1 "))
                    .Select(pbx => pbx.parent.Find(pbx.name.Replace("Ball", "Rest"))).Where(prr => prr != null)
                    .Max(prr => Vector3.Distance(prr.parent.Find(prr.name.Replace("Rest", "Ball")).position, prr.position));
                Debug.Log($"[Jelly] slap frame {frame}: rings {Time.timeSinceLevelLoad - rung[rs + 80].w:0.00} s old {1000f * rung[rs + 81].w:0.0} mm, " +
                          $"{Time.timeSinceLevelLoad - rung[rs + 82].w:0.00} s old {1000f * rung[rs + 83].w:0.0} mm; deepest probe {1000f * Enumerable.Range(0, 24).Max(k => rung[rs + 88 + k].w):0.0} mm, " +
                          $"furthest ball {1000f * moved:0.0} mm off its rest");
            }
            if (frame == 140) worldRest = Read(state);
            // A world's wind from here on, at MagicaCloth's default strength: it must move nothing.
            if (frame == 141) new GameObject("Wind").AddComponent<MagicaCloth2.MagicaWindZone>();
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
                File.WriteAllLines(Path.Combine(dir, "segments.txt"), new[] { "0\t150\tAt rest", "150\t450\tA pane of glass, a world solid, pressed 3 cm in, held, taken away", "450\t700\tThe pane slapped on and straight off" });
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
        // The first hand's gesture, 0 open to 1 a fist, -1 none; its reach shortens as it closes.
        static float propClosed;
        // What the strip told: strain, grip and slap, at most; how far the green's whole swung shaken
        // and twitched; frames gripped.
        static readonly float[] propTold = new float[3];
        static readonly float[] propMass = new float[2];
        static Vector3 propMassRest;
        static int propGripped;
        // The stroke: how far its grip's spot travelled, the frames it held while sliding, and its
        // spot when the fist closed.
        static float propStroke;
        static int propStrokeHeld;
        static Vector4 propStrokeFrom;
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
                float closed = i == 0 ? Mathf.Max(propClosed, 0f) : 0f;
                var wrist = fronts[r] + regions[r].right * dx + regions[r].forward * (Mathf.Lerp(0.11f, 0.06f, closed) * tall - depth);
                left[i] = wrist;
                if (i == 0) left[i].w = propClosed;
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
            // Then a fist closed on the green's front, a centimetre in, pulled 6 cm out and let go; the
            // prop shaken 5 cm side to side at 2 Hz; and its twitch pulled full for a ninth of a second.
            if (frame >= 2110 && frame < 2240)
            {
                int t = frame - 2110;
                propClosed = t < 20 ? 0.6f : t < 110 ? 1f : t < 115 ? Mathf.Lerp(1f, 0f, (t - 110) / 5f) : -1f;
                float pulled = t < 30 ? 0f : t < 110 ? 0.06f * Mathf.SmoothStep(0f, 1f, (t - 30) / 30f) : 0.06f + 0.3f * Mathf.Clamp01((t - 110) / 20f);
                PropHands(regions, fronts, hand, (0, 0.01f - pulled, 0f));
            }
            else
            {
                propClosed = 0f;
                if (frame >= 2240) PropHands(regions, fronts, hand);
            }
            // Last a fist closed a centimetre into the green's front and stroked 8 cm along it, held,
            // and let go: the grip should slide with it, not stay where it closed and let go.
            if (frame >= 2500 && frame < 2680)
            {
                int t = frame - 2500;
                propClosed = t < 20 ? 0.6f : t < 150 ? 1f : t < 155 ? Mathf.Lerp(1f, 0f, (t - 150) / 5f) : -1f;
                PropHands(regions, fronts, hand, (0, t < 155 ? 0.01f : -0.05f, 0.08f * Mathf.SmoothStep(0f, 1f, (t - 40) / 60f)));
            }
            if (frame >= 2300 && frame < 2390) prop.position += Vector3.right * (0.05f * Mathf.Sin(2f * Mathf.PI * 2f * (frame - 2300) / 90f));
            if (frame >= 2400) prop.GetComponent<Animator>().SetFloat("Twitch", frame < 2410 ? 1f : 0f);
            // Then two balls 12 cm apart into each, which should take a slot each, not share them; as
            // far apart in probes' reaches on the probe prop, whose reach is twice: closer, they read as one.
            float two = Mathf.Lerp(-0.06f, 0.03f, (frame - 1910) / 40f), wide = ProbeReach / 0.04f;
            if (frame >= 1910 && frame < 2010) PropBalls(regions, fronts, false, (0, two, -0.06f * wide, 0.03f * wide), (0, two, 0.06f * wide, -0.03f * wide), (1, two, -0.06f * wide, 0.03f * wide), (1, two, 0.06f * wide, -0.03f * wide));
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
            // The strip's feedback and the green's whole: off its rest on the green, as the prop moves.
            if (frame >= 2050)
            {
                var told = Read(AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + "/Jelly_Points.renderTexture"));
                for (int k = 0; k < 3; k++) propTold[k] = Mathf.Max(propTold[k], told[3 + k].x);
                var now = Read(state);
                int side = rows * 2 * Row;
                if (Enumerable.Range(0, 8).Any(e => now[side + 112 + e].w > 0f)) propGripped++;
                var mass = regions[0].InverseTransformPoint(now[side + 30]);
                if (frame == 2295 || frame == 2395) propMassRest = mass;
                if (frame > 2295 && frame < 2395) propMass[0] = Mathf.Max(propMass[0], (mass - propMassRest).magnitude);
                if (frame > 2395 && frame < 2500) propMass[1] = Mathf.Max(propMass[1], (mass - propMassRest).magnitude);
                if (frame >= 2520 && frame < 2650)
                {
                    var spot = Enumerable.Range(0, 8).Select(e => now[side + 112 + e]).FirstOrDefault(g => g.w > 0f);
                    if (spot.w > 0f && propStrokeFrom.w <= 0f) propStrokeFrom = spot;
                    if (spot.w > 0f && frame >= 2540) propStrokeHeld++;
                    if (spot.w > 0f && propStrokeFrom.w > 0f) propStroke = Mathf.Max(propStroke, Vector3.Distance(spot, propStrokeFrom));
                }
            }
            if (++frame < 2700) return;
            bool ok = propDent[0] > 0.01f && propDent[1] < 0.003f && propDent[2] > 0.01f && propDent[3] > 0.01f && propSlots[1] == 0 &&
                      propDragLost == 0 && propDragWorst > -0.03f && propDragDeep <= 0.15f * propDragFrames && propDragJump < 0.015f &&
                      propThrough[0] < 0.06f && propThrough[1] == 0 && propThrough[2] < 0.01f && propTwo < 0.025f * ProbeReach / 0.04f && propDropRing[0] > 0.005f && propDropRing[1] > 0.005f && propDropLow > -0.001f &&
                      propGripped > 30 && propStroke > 0.05f && propStrokeHeld > 90 && propTold[0] > 0.2f && propTold[1] > 0.5f && propTold[2] > 0.1f && propMass[0] > 0.005f && propMass[1] > 0.003f;
            Debug.Log($"[Jelly] {(ok ? "PASS" : "FAIL")} prop after {frame} frames: a hand from the arrays 3 cm in dented the green {1000f * propDent[0]:0.0} mm ({propSlots[0]} slots) " +
                      $"and the blue {1000f * propDent[1]:0.0} mm ({propSlots[1]} slots, 0 wanted); a MagicaCloth ball 3 cm in dented the green {1000f * propDent[2]:0.0} mm " +
                      $"({propSlots[2]} slots, probes pushed {1000f * propPushed[2]:0.0} mm) and the blue {1000f * propDent[3]:0.0} mm ({propSlots[3]} slots, {1000f * propPushed[3]:0.0} mm); hand radius {1000f * hand:0} mm; " +
                      $"dragged 7 cm each way, the ball rebuilt from the probes at worst {-100f * propDragWorst:0.0} cm too deep, {propDragDeep} of {propDragFrames} frames over 1.5 cm, its depth jumping {100f * propDragJump:0.0} cm in a frame at most, " +
                      $"and {propDragLost} without one; the green's dent {1000f * propDragDent[0]:0.0} mm against {1000f * propDragDent[1]:0.0} mm for a hand dragged the same way; " +
                      $"a fist on the green gripped {propGripped} frames; stroked 8 cm along it, its grip slid {1000f * propStroke:0.0} mm and held {propStrokeHeld} of 110 frames; the bars told strain {propTold[0]:0.00}, grip {propTold[1]:0.00}, slap {propTold[2]:0.00}; the green's whole swung {1000f * propMass[0]:0.0} mm shaken and {1000f * propMass[1]:0.0} mm twitched; a hand pushed on through dented it {1000f * propThrough[0]:0.0} mm at most, {propThrough[1]:0} particles over 10 cm, {1000f * propThrough[2]:0.0} mm once through; two balls {12f * ProbeReach / 0.04f:0} cm apart, each slot within {100f * propTwo:0.0} cm of its ball; dropped 12 cm onto a stop, the landing rang the green {1000f * propDropRing[0]:0.0} mm and the blue {1000f * propDropRing[1]:0.0} mm, its lowest particle {1000f * propDropLow:0.0} mm off its board");
            SessionState.SetInt(K + "on", 0);
            SessionState.SetBool(K + "prop", false);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        // -jellySteady: the prop at a headset's frame rate (-jellyFps 240), its camera riding with it:
        // settled, still for a second, then slid at a steady 0.3 m/s. Frame to frame the picture should
        // hardly change either way; a draw that guesses wrong between steps changes it at the steps'
        // cadence. -jellyRate sets the steps a second, 0 every frame.
        static readonly List<float> steadyStill = new List<float>(), steadyMoving = new List<float>();
        static Texture2D steadyLast;
        static Vector4 steadyRate;
        // -jellyLag: the client's parser as it really reads (CVRTexturePropertyParserManager): one GPU
        // readback in flight, the blitters set from the last that arrived, black until one has. With
        // none the steps here ran every frame whatever the switch said.
        static AsyncGPUReadbackRequest lagRead;
        static bool lagPending;
        static Color32[] lagGot;
        static readonly List<int> steadySteps = new List<int>();

        static void OnLatePropSteady()
        {
            var n = Lattice;
            var all = Object.FindObjectsOfType<Transform>();
            var regions = new[] { 0, 1 }.Select(r => all.First(t => t.name == $"Jelly Region {r}")).ToArray();
            PropHands(regions, regions.Select(t => t.TransformPoint(0f, 0f, (n.z - 1) * SpacingOf(n))).ToArray(), 0f);
            var blits = Object.FindObjectsOfType<CVRBlitter>().Select(b => b.blitMaterial).Where(m => m != null && m.HasProperty("_Rate")).Distinct().ToList();
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-jellyRate");
            if (frame == 0)
            {
                steadyRate = blits.Count > 0 ? blits[0].GetVector("_Rate") : Vector4.zero;
                if (at >= 0 && at + 1 < args.Length)
                    foreach (var m in blits) m.SetVector("_Rate", new Vector4(int.Parse(args[at + 1]), int.Parse(args[at + 1]), 0f, 0f));
            }
            bool lag = args.Contains("-jellyLag");
            var parser = Object.FindObjectOfType<CVRTexturePropertyParser>();
            if (lag && parser != null && parser.texture is RenderTexture switched)
            {
                if (lagPending && (lagRead.hasError || lagRead.done))
                {
                    if (!lagRead.hasError) lagGot = lagRead.GetData<Color32>().ToArray();
                    lagPending = false;
                }
                if (!lagPending)
                {
                    lagRead = AsyncGPUReadback.Request(switched, 0, TextureFormat.RGBA32);
                    lagPending = true;
                }
                bool on = true;
                foreach (var task in parser.tasks)
                    if (task.component is Behaviour b && task.propertyName == "enabled")
                        on &= b.enabled = lagGot != null && lagGot[task.y * switched.width + task.x].r >= 128;
                if (on) steadySteps.Add(frame);
            }
            float fps = Time.captureFramerate;
            int settle = Mathf.RoundToInt(2.5f * fps), still = Mathf.RoundToInt(fps), ramp = Mathf.RoundToInt(0.25f * fps), wait = Mathf.RoundToInt(0.5f * fps);
            int f = frame - settle - still;
            if (f > 0)
            {
                float move = 0.3f / fps * Mathf.Clamp01(f / (float) ramp);
                all.First(t => t.name == "Jelly Prop").position += Vector3.right * move;
                eye.transform.position += Vector3.right * move;
            }
            if (frame >= settle)
            {
                var shot = Shot(eye);
                if (steadyLast != null)
                {
                    float c = Changed(steadyLast, shot);
                    if (f <= 0) steadyStill.Add(c);
                    else if (f > ramp + wait) steadyMoving.Add(c);
                    Object.DestroyImmediate(steadyLast);
                }
                steadyLast = shot;
            }
            if (++frame < settle + still + ramp + wait + Mathf.RoundToInt(fps)) return;
            foreach (var m in blits) m.SetVector("_Rate", steadyRate);
            string Of(List<float> c) => $"{100f * c.Average():0.000}% a frame on average, {100f * c.Max():0.000}% at most, {c.Count(x => x > 0.002f)} of {c.Count} frames over 0.2%";
            Debug.Log($"[Jelly] steady at {fps:0} frames a second, the steps at {(at >= 0 ? args[at + 1] : $"{steadyRate.x:0}")} a second: still, the picture changed " +
                      $"{Of(steadyStill)}; slid at 0.3 m/s with the camera, {Of(steadyMoving)}; the slide's first 30 frames in thousandths: " +
                      string.Join(" ", steadyMoving.Take(30).Select(x => $"{1000f * x:0}")));
            if (lag)
            {
                // Each gap a step's wait in seconds, after the first second's settling: the draw turns
                // red past a quarter second.
                var gaps = steadySteps.Where(s => s > fps).Zip(steadySteps.Where(s => s > fps).Skip(1), (a, b) => (b - a) / fps).ToList();
                float seconds = (frame - fps) / fps;
                Debug.Log($"[Jelly] steady through the client's lagged parser: {steadySteps.Count(s => s > fps) / seconds:0.0} steps a second, " +
                          (gaps.Count > 0 ? $"waits {1000f * gaps.Average():0} ms on average, {1000f * gaps.Max():0} at most, {gaps.Count(g => g > 0.25f)} past a quarter second, " +
                          $"{steadySteps.Zip(steadySteps.Skip(1), (a, b) => b - a == 1).Count(x => x)} steps a frame after the last" : "no steps"));
            }
            SessionState.SetInt(K + "on", 0);
            SessionState.SetBool(K + "prop", false);
            EditorApplication.Exit(0);
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
            (450, "A fist closes on the green and pulls it out, twice: the magenta bar"),
            (360, "The holder's trigger twitches both, three times"),
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
            else if (shot == 14)
            {
                // In open, closed on the skin a centimetre in, pulled 6 cm out, opened, away; twice.
                int t = g % 225;
                propClosed = t < 40 ? 0f : t < 130 ? Mathf.Clamp01((t - 40) / 5f) : t < 135 ? Mathf.Lerp(1f, 0f, (t - 130) / 5f) : -1f;
                float d = t < 40 ? Mathf.Lerp(-0.05f, 0.01f, Mathf.SmoothStep(0f, 1f, t / 40f)) : t < 135 ? 0.01f - 0.06f * Mathf.SmoothStep(0f, 1f, (t - 50) / 40f) : -0.05f - 0.3f * Mathf.Clamp01((t - 135) / 20f);
                PropHands(regions, fronts, hand, (0, d, 0f));
            }
            else PropHands(regions, fronts, hand);
            prop.GetComponent<Animator>().SetFloat("Twitch", shot == 15 && g % 120 >= 30 && g % 120 < 40 ? 1f : 0f);

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

            // The bars as the client's parser sets them, which the editor never runs.
            var told = Read(AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + "/Jelly_Points.renderTexture"));
            string[] bars = { "Squeeze", "Grab", "Slap" };
            for (int k = 0; k < 3; k++) all.First(t => t.name == $"Jelly {bars[k]} Bar").localScale = new Vector3(1f, Mathf.Lerp(0.001f, 0.12f, Mathf.Clamp01(told[3 + k].x)), 1f);
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

#if AVATARBRIDGE_YAPS

        [MenuItem("Tools/Avatar Bridge/Dev/Jelly/Put the selected avatar's breasts on the jelly")]
        static void BreastsOnSelected()
        {
            string said = BreastsOn(Selection.activeGameObject != null ? Selection.activeGameObject.transform : null);
            Debug.Log("[Jelly] " + said);
            EditorUtility.DisplayDialog("Jelly spike", said, "OK");
        }

        // Its probes physics balls the world's solids push, as the world prop's right one: walls,
        // doors, tables and dropped props, where the client lets an avatar's bodies meet them.
        [MenuItem("Tools/Avatar Bridge/Dev/Jelly/Put the selected avatar's breasts on the jelly, feeling the world")]
        static void BreastsWorldOnSelected()
        {
            string said = PairOn(Selection.activeGameObject != null ? Selection.activeGameObject.transform : null, Breast, world: true);
            Debug.Log("[Jelly] " + said);
            EditorUtility.DisplayDialog("Jelly spike", said, "OK");
        }

        // Batch, for an upload: the squashed avatar in -jellyScene's breasts on the jelly feeling the
        // world and colliders, its twitch ticked, the menu and animator wired as the menu wires them,
        // the scene saved.
        public static void WearBreasts()
        {
            var args = Environment.GetCommandLineArgs();
            var scene = EditorSceneManager.OpenScene(args[Array.IndexOf(args, "-jellyScene") + 1], OpenSceneMode.Single);
            // -jellyPrefab: the conversion put in the scene, any avatar already there kept but switched off.
            int from = Array.IndexOf(args, "-jellyPrefab");
            if (from >= 0 && from + 1 < args.Length)
            {
                foreach (var other in Object.FindObjectsOfType<CVRAvatar>()) other.gameObject.SetActive(false);
                PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(args[from + 1]));
            }
            var avatar = Object.FindObjectsOfType<CVRAvatar>().FirstOrDefault(a => YapsSquash.Has(a.transform));
            if (avatar == null)
            {
                Debug.Log("[Jelly] FAIL worn: no avatar with the squash in the scene");
                EditorApplication.Exit(1);
                return;
            }
            var settings = YapsSquash.SettingsOf(avatar.transform);
            if (settings != null)
            {
                settings.twitch = true;
                // For trying in game: the upload carries the slide too.
                settings.gripSlides = true;
                EditorUtility.SetDirty(settings);
            }
            string said = PairOn(avatar.transform, Breast, world: true);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Jelly] worn on {avatar.name}: {said}");
            EditorApplication.Exit(0);
        }

        // -jellyPaint: the avatar's parts painted in place of the name search, as a user paints them:
        // its pairs found by name, then a belly alone, the thighs, and a back alone past the jelly's
        // four. A vertex's strongest bone decides the ones no name finds, skin a named pair has left out.
        static void PaintTest(Transform root)
        {
            var settings = YapsSquash.Ensure(root);
            var body = YapsSquash.NamedBody(root);
            var named = YapsSquash.NamedChannels(root, body, out var names, out _);
            var anim = root.GetComponentInChildren<Animator>();
            var mesh = body.sharedMesh;
            int n = mesh.vertexCount;
            var baked = new Mesh();
            body.BakeMesh(baked);
            var at = baked.vertices;
            Object.DestroyImmediate(baked);
            var weights = mesh.boneWeights;
            byte B(float x) => (byte) Mathf.Clamp(Mathf.RoundToInt(x * 255f), 0, 255);
            settings.pairs.Clear();
            settings.body = body;
            settings.vertexCount = n;
            settings.fingerprint = YapsSquash.Fingerprint(mesh);
            var taken = new float[n];
            for (int k = 0; k < names.Count; k++)
            {
                settings.pairs.Add(new AvatarBridge.Yaps.YapsSquashSettings.Pair { name = names[k], a = named[2 * k].Select(B).ToArray(), b = named[2 * k + 1].Select(B).ToArray() });
                for (int v = 0; v < n; v++) taken[v] += named[2 * k][v] + named[2 * k + 1][v];
            }
            Transform Strongest(int v) => body.bones[weights[v].boneIndex0];
            Vector3 World(int v) => body.transform.position + body.transform.rotation * at[v];
            var spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            var chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            var legs = new[] { anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg), anim.GetBoneTransform(HumanBodyBones.RightUpperLeg) };
            byte[] Where(Func<int, bool> on) => Enumerable.Range(0, n).Select(v => on(v) && taken[v] < 0.05f ? (byte) 255 : (byte) 0).ToArray();
            float Front(int v) => Vector3.Dot(World(v) - spine.position, root.forward);
            settings.pairs.Add(new AvatarBridge.Yaps.YapsSquashSettings.Pair { name = "Belly", a = Where(v => Strongest(v) == spine && Front(v) > 0f) });
            settings.pairs.Add(new AvatarBridge.Yaps.YapsSquashSettings.Pair { name = "Thighs", a = Where(v => Strongest(v) == legs[0]), b = Where(v => Strongest(v) == legs[1]) });
            settings.pairs.Add(new AvatarBridge.Yaps.YapsSquashSettings.Pair { name = "Back", a = Where(v => (Strongest(v) == spine || Strongest(v) == chest) && Front(v) < 0f) });
            EditorUtility.SetDirty(settings);
            Debug.Log("[Jelly] painted: " + string.Join(", ", settings.pairs.Select(p => $"{p.name} {p.a.Count(x => x > 127)}/{p.b.Count(x => x > 127)}")));
        }

        // Batch: the user's route on -jellyPrefab's avatar, alone in -jellyScene: the squash's own
        // Build, which puts the jelly on over it, twice, then its Strip. Each build one jelly a part,
        // each on its own slot, and the squash materials reading it; stripped, nothing of the jelly
        // left. With -jellyPaint the painted parts, four on and the fifth refused. It writes to the
        // conversion's own materials and controllers, which the script running it puts back.
        public static void BuildCheck()
        {
            var args = Environment.GetCommandLineArgs();
            EditorSceneManager.OpenScene(args[Array.IndexOf(args, "-jellyScene") + 1], OpenSceneMode.Single);
            foreach (var other in Object.FindObjectsOfType<Animator>()) other.gameObject.SetActive(false);
            var made = (GameObject) PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(args[Array.IndexOf(args, "-jellyPrefab") + 1]));
            PrefabUtility.UnpackPrefabInstance(made, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var root = made.GetComponentInChildren<CVRAvatar>(true).transform;
            bool paint = args.Contains("-jellyPaint");
            if (paint) PaintTest(root);
            bool ok = true;
            for (int round = 1; round <= 2; round++)
            {
                string said = YapsSquash.Build(root, out bool built);
                var worn = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(Worn + " ")).Select(t => t.name).ToList();
                var mats = YapsSquash.Materials(root);
                bool Lit(Material m, int s) => m.HasProperty(LatticeOf(s)) && m.GetVector(LatticeOf(s)).x > 0;
                int reading = mats.Count(m => Enumerable.Range(0, YapsSquash.JellySlots).Any(s => Lit(m, s)));
                var slots = Enumerable.Range(0, YapsSquash.JellySlots).Select(s => mats.Count(m => Lit(m, s))).ToList();
                int owned = mats.Count(m => m.HasProperty("_YAPS_JellyOwn") && m.GetTexture("_YAPS_JellyOwn") != null);
                var carried = mats.Select(m => m.GetVector("_YAPS_JellyParts")).FirstOrDefault(p => p != Vector4.zero);
                bool fine = built && worn.Count > 0 && worn.Distinct().Count() == worn.Count && reading > 0 && owned > 0 && slots.Take(worn.Count).All(c => c > 0)
                            && (!paint || worn.Count == YapsSquash.JellySlots && said.Contains("not on it"));
                ok &= fine;
                Debug.Log($"[Jelly] build {round} {(fine ? "ok" : "BAD")}: {said} Worn: {string.Join(", ", worn)}; {reading} of {mats.Count} squash materials read it, " +
                          $"per slot {string.Join("/", slots)}, {owned} with the parts texture, the squash's parts carried as {carried}");
            }
            YapsSquash.Strip(root);
            var left = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Jelly")).Select(t => t.name).ToList();
            ok &= left.Count == 0;
            Debug.Log($"[Jelly] {(ok ? "PASS" : "FAIL")} build route: stripped, {(left.Count == 0 ? "nothing of the jelly left" : "left: " + string.Join(", ", left))}");
            EditorApplication.Exit(ok ? 0 : 1);
        }

        [MenuItem("Tools/Avatar Bridge/Dev/Jelly/Put the selected avatar's other pair on the jelly")]
        static void OtherOnSelected()
        {
            string said = PairOn(Selection.activeGameObject != null ? Selection.activeGameObject.transform : null, Other);
            Debug.Log("[Jelly] " + said);
            EditorUtility.DisplayDialog("Jelly spike", said, "OK");
        }

        // The worn jelly as the add-on builds it, into this builder's folders, its options from the
        // switches: -jellyCells n, -jellyMass, -jellyNoMass, -jellyGive g, -jellyZeta z, -jellyNoPlane,
        // -jellyWorld, -jellyProbes, -jellyTwitch, -jellySlide. Batch checks leave the avatar's
        // controllers alone; a batch build for an upload (-jellyWire) wires them.
        internal static string BreastsOn(Transform picked) => PairOn(picked, Breast);

        internal static string PairOn(Transform picked, Regex want, bool world = false)
        {
            var a = Environment.GetCommandLineArgs();
            float Num(string name, float none)
            {
                int i = Array.IndexOf(a, name);
                return i >= 0 && i + 1 < a.Length && float.TryParse(a[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : none;
            }
            var o = new Options
            {
                cells = Num("-jellyCells", BreastCells),
                mass = a.Contains("-jellyMass"),
                noMass = a.Contains("-jellyNoMass"),
                give = Num("-jellyGive", float.NaN),
                zeta = Num("-jellyZeta", float.NaN),
                plane = !a.Contains("-jellyNoPlane"),
                world = world || a.Contains("-jellyWorld"),
                probes = a.Contains("-jellyProbes"),
                probeReach = ProbeReach,
                twitch = a.Contains("-jellyTwitch"),
                slide = a.Contains("-jellySlide"),
                wire = !Application.isBatchMode || a.Contains("-jellyWire"),
            };
            return YapsJelly.PairOn(picked, want, Home, Home, o, new BridgeReport());
        }

        [MenuItem("Tools/Avatar Bridge/Dev/Jelly/Take the jelly off the selected avatar")]
        static void BreastsOffSelected()
        {
            string said = Off(Selection.activeGameObject != null ? Selection.activeGameObject.transform : null);
            Debug.Log("[Jelly] " + said);
            EditorUtility.DisplayDialog("Jelly spike", said, "OK");
        }

#endif


        static GameObject Make(bool preview = true, bool withMeter = true, bool withRider = true, bool offFar = true, bool sleeps = true, int regions = 1,
            Vector3Int? lattice = null, float spacing = 0f, bool surface = true, float lift = 0f)
        {
            foreach (var folder in new[] { Home, Folder })
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            // The step and its lattice are the add-on's, its Core writing them into Home below; the
            // draw, clock and meter this builder's own beside them, the draw on the same lattice.
            File.WriteAllText(Home + "/JellyCommon.cginc", CommonInclude);
            File.WriteAllText(Home + "/JellyDraw.shader", DrawShader);
            File.WriteAllText(Home + "/JellyClock.shader", ClockShader);
            File.WriteAllText(Home + "/JellyMeter.shader", MeterShader);
            File.WriteAllText(Home + "/README.md", Readme);
            AssetDatabase.Refresh();
            // The near pass is part of the first step now; what it left goes to the bin, out of the way.
            foreach (var gone in new[] { "/JellyNear.shader", "/JellyPoints.shader", "/Jelly_Near.renderTexture", "/Jelly_Near.mat" })
                if (AssetDatabase.LoadMainAssetAtPath(Folder + gone) != null) AssetDatabase.MoveAssetToTrash(Folder + gone);
            var draw = AssetDatabase.LoadAssetAtPath<Shader>(Home + "/JellyDraw.shader");
            var clock = AssetDatabase.LoadAssetAtPath<Shader>(Home + "/JellyClock.shader");
            var meter = AssetDatabase.LoadAssetAtPath<Shader>(Home + "/JellyMeter.shader");
            var shaders = new[] { draw, clock, meter };
            if (shaders.Any(x => x == null || ShaderUtil.ShaderHasError(x)))
            {
                Debug.Log("[Jelly] BUILD ABORT: a shader did not import or has errors: " + string.Join(" | ", shaders.Where(x => x != null)
                    .SelectMany(x => ShaderUtil.GetShaderMessages(x)).Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    .Select(m => $"{m.message} line {m.line}")));
                return null;
            }

            var n = lattice ?? Lattice;
            if (spacing <= 0f) spacing = SpacingOf(n);
            var core = Core(Folder, Home, n, spacing, regions, sleeps, withRider, offFar, lift);
            if (core == null) return null;
            var (step, state, root, anchors, tissue, provider, controller) = (core.step, core.state, core.root, core.anchors, core.tissue, core.provider, core.controller);
            var drawMat = new Material(draw);
            drawMat.SetTexture("_State", state);
            drawMat.SetVector("_Lattice", new Vector4(n.x, n.y, n.z, spacing));
            drawMat = Save(drawMat, "/Jelly_Draw.mat");

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
                provider.materials = provider.materials.Append(loadMat).ToArray();
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

            if (withRider)
            {
                var parser = core.parser;
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
                State = YapsJelly.State(n, regions);
                Twin = YapsJelly.State(n, regions);
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


        static T Save<T>(T asset, string file) where T : Object => YapsJelly.Save(asset, Folder + file);

        static Material StepMaterial(Shader step, Vector3Int n, int pass, float spacing = 0f) => YapsJelly.StepMaterial(step, n, pass, spacing > 0f ? spacing : SpacingOf(n));

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

        // What the client's chain of passes costs as it is now, timed alone: the state put back after,
        // so a verify carries on from where it was.
        static string ChainCost(Component jelly, float dt = 1f / 90f)
        {
            var steps = jelly.GetComponentInChildren<CVRRenderController>(true).cvrRenderComponents.OfType<CVRBlitter>().ToList();
            var fixedWas = steps.Select(c => c.blitMaterial).Distinct().Select(mt => (mt, mt.GetFloat("_FixedDt"))).ToList();
            foreach (var (mt, _) in fixedWas) mt.SetFloat("_FixedDt", dt);
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
            foreach (var (mt, was) in fixedWas) mt.SetFloat("_FixedDt", was);
            return Spread(times);
        }

        static string Spread(List<double> xs) => $"{Median(xs):0.000} ({xs.Min():0.000} to {xs.Max():0.000})";

        static Vector4[] Read(RenderTexture rt)
        {
            var req = AsyncGPUReadback.Request(rt);
            req.WaitForCompletion();
            return req.GetData<Vector4>().ToArray();
        }

        // The first region's outer side, level: away from its partner, or its own right for a part
        // alone, whose zero left the slap's parked palm 10 cm in front of it, in reach.
        static Vector3 Outward()
        {
            var o = breastRegions[0] == breastRegions[1] ? breastRegions[0].right : breastRegions[0].position - breastRegions[1].position;
            o.y = 0f;
            return o.normalized;
        }

        // The probed jelly's state. A part alone has one region's rows: the partner checks read them twice.
        static Vector4[] ReadState()
        {
            var got = Read(AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + "/Jelly_State.renderTexture"));
            return breastRegions != null && breastRegions[0] == breastRegions[1] ? got.Concat(got).ToArray() : got;
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
                else if (SessionState.GetBool(K + "propSteady", false)) OnLatePropSteady();
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
                    if (args.Contains("-jellyPaint")) PaintTest(made.GetComponentInChildren<CVRAvatar>(true).transform);
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
            // -jellyAlso: another pair on a jelly of its own first, so the check runs with both on.
            int alsoAt = Array.IndexOf(args, "-jellyAlso");
            if (avatar != null && alsoAt >= 0 && alsoAt + 1 < args.Length)
            {
                string also = PairOn(avatar.transform, new Regex(args[alsoAt + 1], RegexOptions.IgnoreCase));
                Debug.Log("[Jelly] also: " + also);
                if (!also.StartsWith("On "))
                {
                    EditorApplication.Exit(1);
                    return;
                }
            }
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
            // Every other avatar off, as the check has it: one already wearing the jelly shares its
            // step's materials.
            foreach (var other in Object.FindObjectsOfType<Animator>()) other.gameObject.SetActive(false);
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
            if (jellied) f.rig = f.chest.GetComponentsInChildren<Transform>(true).First(t => t.name.StartsWith(Worn + " ") && Wanted().IsMatch(t.name.Substring(Worn.Length + 1)));
            if (jellied) chain = ChainOf(f.rig);
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
            var lattice = YapsSquash.Materials(tuneJelly.root).First(m => m.GetVector(Slot("_YAPS_JellyLattice")).x > 0).GetVector(Slot("_YAPS_JellyLattice"));
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
            var state = ReadState();
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
        // The strain the step reads (texel 86): as the hand came, and the most while it pulled.
        static readonly float[] taut = new float[2];
        // What the points strip told the animator: squeeze and grip as the hand came, both pulled,
        // and the clap's slap. The twitch: each mass's swing, then the furthest two seconds on.
        static readonly float[] told = new float[5], twitched = new float[3];
        static readonly Vector3[] twitchFrom = new Vector3[2];
        static bool twitchSeen;
        static int grabbed;
        static Vector3 grabFrom, grabMass;
        static int grabAt;
        static float ownShake, ownMost;
        static float handsApart, handsCloser, handsOuter, handsInner, handsVolume, slapMost, slapLater, solid0, pair0;
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
        static int halfOn, halfFrames, nearOn, nearFrames, lastStep, probeSlots, probeSlotsBefore, probePushed, bothSlots, ringsSlow, ringsProbe, ringsBoth;
        static readonly List<string> probeSaid = new List<string>(), probeSeries = new List<string>(), ringSaid = new List<string>();
        static bool probed, remoteHand;
        static Vector4[] wallRest;
        static readonly float[] wallDent = new float[4];
        static GameObject breastWall;
        static Transform remoteBall;
        static Vector3 remoteWrist;
        static int probePair;
        static string probeCost;
        // The chain's cost under each load the breasts check makes: a finger in, a wall's faces, a fist.
        static string pokeCost, wallCost, gripCost;
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
            jelly = avatar.GetComponentsInChildren<Transform>(true).First(t => t.name.StartsWith(Worn + " ") && Wanted().IsMatch(t.name.Substring(Worn.Length + 1)));
            chain = ChainOf(jelly);
            // A part alone stands in for both of a pair: the partner checks read it twice.
            breastRegions = Enumerable.Range(0, 2).Select(r => jelly.Find($"Jelly Region {r}") ?? jelly.Find("Jelly Region 0")).ToArray();
            bool alone = breastRegions[0] == breastRegions[1];
            // Riding bones, the step is handed the bones and the regions' places on them; the regions
            // themselves are put there each frame, as everything here measures from them.
            var anchors = jelly.GetComponentInChildren<CVRMaterialDataProvider>(true);
            breastBones = anchors.transforms;
            breastOn = Enumerable.Range(0, 2).Select(r =>
            {
                var at = anchors.materials[0].GetVector($"_JellyOffsetAt{(alone ? 0 : r)}");
                var q = anchors.materials[0].GetVector($"_JellyOffsetTurn{(alone ? 0 : r)}");
                return at.w > 0f ? Matrix4x4.TRS(at, new Quaternion(q.x, q.y, q.z, q.w), Vector3.one * at.w) : Matrix4x4.identity;
            }).ToArray();
            CarryRegions();
            breastParser = jelly.GetComponentInChildren<CVRTexturePropertyParser>(true);
            viewSaid.Clear();
            viewRan.Clear();
            viewBack = 0f;
            halfMost = 0f;
            halfOn = halfFrames = nearOn = nearFrames = lastStep = 0;
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
            var lattice = YapsSquash.Materials(swing).First(m => m.GetVector(Slot("_YAPS_JellyLattice")).x > 0).GetVector(Slot("_YAPS_JellyLattice"));
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
            {
                // Off the anchor the state was stepped on (26 to 29), as the draw reads it: between
                // steps the bones have moved on and the state has not.
                int sr = (r * (Side(n) + 1) + Side(n)) * 2 * Row;
                var on = s[sr + 29].w > 0.5f ? new Matrix4x4((Vector3) s[sr + 26], (Vector3) s[sr + 27], (Vector3) s[sr + 28], s[sr + 29]) : anchors[r].localToWorldMatrix;
                for (int k = 0; k < n.z; k++)
                    for (int j = 0; j < n.y; j++)
                        for (int i = x0; i < Mathf.Min(x1, n.x); i++)
                        {
                            int p = (k * n.y + j) * n.x + i;
                            var rest = new Vector3((i - 0.5f * (n.x - 1)) * breastSpacing, (j - 0.5f * (n.y - 1)) * breastSpacing, k * breastSpacing);
                            most = Mathf.Max(most, ((Vector3) Particle(s, r, p) - on.MultiplyPoint3x4(rest)).magnitude);
                        }
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
            }
            // At the rate the switch flips every frame or two, so on is having stepped in the last three.
            if (stepping) lastStep = frame;
            bool running = frame - lastStep < 3;
            if (breastParser != null && running != wasStepping && restSaid.Count < 40 && frame < BreastFrames - ViewFrames + 260) restSaid.Add($"{(running ? "on" : "off")} {frame}");
            wasStepping = running;
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
                    viewRan.Add(running);
                    viewSaid.Add($"{(v < 60 ? "looking at it" : v < 120 ? "looking away" : v < 200 ? "looking back" : "1.2 m off, looking away")}, the steps {(running ? "on" : "off")}");
                }
                // Looking at it with no head, near: the steps at _Rate.x.
                if (v < 60)
                {
                    nearFrames++;
                    if (stepping) nearOn++;
                }
                if (v >= 125 && v < 200) viewBack = Mathf.Max(viewBack, Furthest(ReadState(), breastRegions));
                // Then the viewer's head 3 m off, past _Half's 2 m: the steps at _Rate.y, each taking
                // the frames it missed.
                if (v >= 280 && v < 360)
                {
                    halfFrames++;
                    if (stepping) halfOn++;
                    var st = ReadState();
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
                // Both kinds on (the first half of the probes cloth, the second balls), the wall comes once
                // the collider has gone.
                bool cloth = jelly.GetComponentsInChildren<Behaviour>(true).Any(c => c.name.StartsWith("Jelly Probes"));
                int wv = cloth ? v - 180 : v;
                bool balls = jelly.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("Jelly Probe Ball"));
                if (balls && wv >= 360 && wv <= 535)
                {
                    var box = jelly.GetComponentsInChildren<CVRMaterialDataProvider>(true).First(q => q.transforms != null && q.transforms.Length > 0).materials[0].GetVector("_JellyBox0");
                    if (breastWall == null)
                    {
                        breastWall = new GameObject("Wall");
                        // 2 cm, a pane of glass, thinner than a probe ball travels.
                        breastWall.AddComponent<BoxCollider>().size = new Vector3(2f * box.x + 0.02f, 2f * box.y + 0.02f, 0.02f);
                        breastWall.AddComponent<Rigidbody>().isKinematic = true;
                    }
                    float gap = wv < 380 ? 0.15f : wv < 410 ? Mathf.Lerp(0.15f, -0.03f, (wv - 380) / 30f) : wv < 445 ? -0.03f : Mathf.Lerp(-0.03f, 0.15f, Mathf.Clamp01((wv - 445) / 15f));
                    breastWall.transform.SetPositionAndRotation(breastRegions[0].TransformPoint(0f, 0f, box.z + 0.01f + gap), breastRegions[0].rotation);
                    var st = ReadState();
                    if (wv == 375) wallRest = st;
                    float Off(int r)
                    {
                        float most = 0f;
                        for (int q = 0; q < n.x * n.y * n.z; q++)
                            most = Mathf.Max(most, ((Vector3) Particle(st, r, q) - (Vector3) Particle(wallRest, r, q)).magnitude);
                        return most;
                    }
                    if (wv >= 425 && wv < 445)
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
                    if (wv == 435) wallCost = ChainCost(jelly);
                    if (wv >= 520) wallDent[2] = Mathf.Max(wallDent[2], Off(0));
                    if (wv == 535) Object.DestroyImmediate(breastWall);
                }
                var nets = jelly.GetComponentsInChildren<Behaviour>(true).Where(c => c.name.StartsWith("Jelly Probes")).ToList();
                if (nets.Count > 0 && v >= 360 && v <= 535)
                {
                    probed = true;
                    if (v == 360)
                    {
                        // In the helper's order, the first the first breast's frontmost, where its skin is
                        // (the lattice's front is a cube's, 20 cm past it).
                        probeTs = jelly.GetComponentsInChildren<Transform>(true).Where(t => Regex.IsMatch(t.name, @"^Jelly Probe Tip \d \d+$") && t.parent.name.StartsWith("Jelly Probe Base")).ToList();
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
                    var st = ReadState();
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
                    if (v == 410) probeCost = ChainCost(jelly);
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
                var outward = Outward();
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
                var outward = Outward();
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
            // The pair's contact off for its A/B and back as built: a part alone has none.
            if (frame == 0) pair0 = blits[0].GetFloat("_Pair");
            if (frame == 580) foreach (var b in blits) b.SetFloat("_Pair", 0f);
            if (frame == 740) foreach (var b in blits) b.SetFloat("_Pair", pair0);
            if (frame == 0) solid0 = blits[0].GetFloat("_Solid");
            if (frame == 1480) foreach (var b in blits) b.SetFloat("_Pair", 0f);
            if (frame == 1660) foreach (var b in blits) b.SetFloat("_Pair", pair0);
            eye.Render();
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
                var s = ReadState();
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
                var s = ReadState();
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
                var s = ReadState();
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
                // What pushes the first's mass: the hands its slots hold (distance off its centre @ radius,
                // h when gripping), its load and strain, and the probes' readings.
                {
                    int sr0 = side * 2 * Row;
                    var held = Enumerable.Range(0, 8).Where(e => s[sr0 + 2 * e + 1].w > 0.5f)
                        .Select(e => $"{1000f * Vector3.Distance(s[sr0 + 2 * e], s[sr0 + 30]):0}@{1000f * s[sr0 + 2 * e].w:0}{(s[sr0 + 112 + e].w > 0 ? "h" : "")}");
                    line.Append($",hands [{string.Join(" ", held)}] on {s[sr0 + 1].w:0.0},load {s[sr0 + 86].y:0.0000} strain {s[sr0 + 86].x:0.000}");
                    line.Append(",probes " + string.Join(" ", Enumerable.Range(0, 12).Select(k => (1000f * s[sr0 + 88 + k].w).ToString("0.0"))));
                }
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
            if (frame == 205)
            {
                pokeMost = Furthest(ReadState(), breastRegions);
                pokeCost = ChainCost(jelly);
            }
            if (frame == 1839) ownBefore = ReadState();
            if (frame == 2039) throughBefore = ReadState();
            if (frame >= 2040 && frame < 2340)
            {
                var st = ReadState();
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
                var st = ReadState();
                int sr = Side(n);
                if (frame == 2350)
                {
                    var skins = blits[0].GetVector("_PairSkin");
                    float sized = breastRegions[0].lossyScale.x;
                    clap[0] = Vector3.Distance(st[sr * 2 * Row + 30], st[(2 * sr + 1) * 2 * Row + 30]) - (skins.x + skins.y) * sized;
                    clapCell = blits[0].GetVector("_Lattice").w * sized;
                }
                told[4] = Mathf.Max(told[4], Read((RenderTexture) jelly.GetComponentInChildren<CVRTexturePropertyParser>(true).texture)[5].x);
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
            // The twitch: its transform 2 cm up and back over a fifth of a second, as the menu's clip
            // moves it, and each mass's swing off where it was.
            if (frame >= 2745 && frame < 2960)
            {
                var twT = jelly.GetComponentsInChildren<Transform>(true).FirstOrDefault(twx => twx.name == "Jelly Twitch");
                if (twT != null)
                {
                    twitchSeen = true;
                    int twAt = frame - 2750;
                    float twUp = twAt < 0 ? 0f : twAt < 5 ? twAt / 5f : twAt < 11 ? 1f : twAt < 18 ? 1f - (twAt - 11) / 7f : 0f;
                    twT.localPosition = Vector3.up * (0.02f * twUp / Mathf.Abs(twT.lossyScale.y));
                    var twSt = ReadState();
                    int twSr = Side(n);
                    for (int tr = 0; tr < 2; tr++)
                    {
                        Vector3 twC = twSt[(tr * (twSr + 1) + twSr) * 2 * Row + 30];
                        if (frame == 2749) twitchFrom[tr] = twC;
                        else if (frame > 2749 && frame < 2850) twitched[tr] = Mathf.Max(twitched[tr], Vector3.Distance(twC, twitchFrom[tr]));
                        else if (frame == 2959) twitched[2] = Mathf.Max(twitched[2], Vector3.Distance(twC, twitchFrom[tr]));
                    }
                }
            }
            if (frame >= 2445 && frame < 2740)
            {
                var st = ReadState();
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
                    taut[1] = Mathf.Max(taut[1], st[sr + 86].x);
                }
                if (frame == 2445) taut[0] = st[sr + 86].x;
                if (frame == 2545) gripCost = ChainCost(jelly);
                var toldNow = Read((RenderTexture) jelly.GetComponentInChildren<CVRTexturePropertyParser>(true).texture);
                if (frame == 2445) { told[0] = toldNow[3].x; told[1] = toldNow[4].x; }
                if (frame >= 2530 && frame < 2560) { told[2] = Mathf.Max(told[2], toldNow[3].x); told[3] = Mathf.Max(told[3], toldNow[4].x); }
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
                var st = ReadState();
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
                swingMost = Mathf.Max(swingMost, Furthest(ReadState(), breastRegions));
                // Riding bones, the cloth swings the anchors: how far, in the avatar's own frame.
                for (int r = 0; r < 2; r++) swingMost = Mathf.Max(swingMost, (swing.InverseTransformPoint(breastRegions[r].position) - regionsAt[r]).magnitude);
            }
            // The first's two columns facing the second.
            int inner = Vector3.Dot(secondAt - breastRegions[0].position, breastRegions[0].right) > 0f ? n.x - 2 : 0;
            // Where those columns' particles are over each squeeze's held second half, averaged.
            if ((frame >= 660 && frame < 690) || (frame >= 960 && frame < 990))
            {
                var st = ReadState();
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
                var st = ReadState();
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
            if (frame >= 1080 && frame < 1290 && ReadState()[Side(n) * 2 * Row + 30].w > 0)
            {
                var st = ReadState();
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
            // First, while the state is as the run left it: -jellyCost below leaves it switched far.
            // A plug through the pair's socket, from below and a little out, 10 cm past it: the share
            // of the picture it moves with the jelly on, against the squash alone and twice alike.
            // With none in the gap (the squash's empty node, scaled to zero), one between the two
            // regions' middles, straight into the slot the draw reads: the provider writes its
            // matrices once a frame, and this is one frame. Its skinned copy, left on the host, then
            // disagrees, so the shader reads every slot from the matrices.
            string plugSaid = "no squash slots, so no plug";
            bool plugOk = true;
            var follow = swing.GetComponentsInChildren<CVRMaterialDataProvider>(true).FirstOrDefault(q => q.shaderPropertyName == "_YAPS_SquashFollow");
            int slot = 18 + JellySpikeBuilder.chain;
            var socket = follow != null && follow.transforms.Length > slot ? follow.transforms[slot] : null;
            Transform testSocket = null;
            if (follow != null && socket != null && !(socket.lossyScale.x > 0f))
            {
                testSocket = socket = new GameObject("Test socket").transform;
                var mid = new Vector3(0f, 0f, 0.5f * (breastN.z - 1) * breastSpacing);
                testSocket.position = 0.5f * (breastRegions[0].TransformPoint(mid) + breastRegions[1].TransformPoint(mid));
                foreach (var mm in follow.materials.Where(mm => mm != null))
                {
                    var slots = mm.GetMatrixArray("_YAPS_SquashFollow");
                    if (slots == null || slots.Length <= slot) continue;
                    slots[slot] = testSocket.localToWorldMatrix;
                    mm.SetMatrixArray("_YAPS_SquashFollow", slots);
                }
            }
            if (socket != null && socket.lossyScale.x > 0f)
            {
                var lamp = new GameObject("Test plug light").AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.color = Color.black;
                lamp.renderMode = LightRenderMode.ForceVertex;
                lamp.range = 0.483f;
                lamp.intensity = 0.3f;
                lamp.transform.position = socket.position - (swing.up - 0.3f * breastRegions[0].forward).normalized * 0.2f;
                var drawn = YapsSquash.Materials(swing).Where(mm => mm.HasProperty("_YAPS_JellyParts")).ToList();
                var carried = drawn.Select(mm => mm.GetVector("_YAPS_JellyParts")).ToList();
                var lit = drawn.Select(mm => mm.GetVector(Slot("_YAPS_JellyLattice"))).ToList();
                var plugShare = new float[3];
                for (int alone = 0; alone < 2; alone++)
                {
                    for (int k = 0; k < drawn.Count; k++)
                    {
                        drawn[k].SetVector("_YAPS_JellyParts", alone == 1 ? Vector4.zero : carried[k]);
                        drawn[k].SetVector(Slot("_YAPS_JellyLattice"), alone == 1 ? Vector4.zero : lit[k]);
                    }
                    lamp.enabled = true;
                    Texture2D with = Shot(eye), again = Shot(eye);
                    lamp.enabled = false;
                    var without = Shot(eye);
                    plugShare[alone] = Changed(with, without);
                    if (alone == 0) plugShare[2] = Changed(with, again);
                    foreach (var shot in new[] { with, again, without }) Object.DestroyImmediate(shot);
                }
                for (int k = 0; k < drawn.Count; k++)
                {
                    drawn[k].SetVector("_YAPS_JellyParts", carried[k]);
                    drawn[k].SetVector(Slot("_YAPS_JellyLattice"), lit[k]);
                }
                // Whether the pair's jelly draws at all just now: the plug in, its lattice off against on.
                lamp.enabled = true;
                var latticed = drawn.Select(mm => mm.GetVector(Slot("_YAPS_JellyLattice"))).ToList();
                Texture2D drawnOn = Shot(eye);
                foreach (var mm in drawn) mm.SetVector(Slot("_YAPS_JellyLattice"), Vector4.zero);
                Texture2D drawnOff = Shot(eye);
                for (int k = 0; k < drawn.Count; k++) drawn[k].SetVector(Slot("_YAPS_JellyLattice"), latticed[k]);
                float drawsShare = Changed(drawnOn, drawnOff);
                Object.DestroyImmediate(drawnOn);
                Object.DestroyImmediate(drawnOff);
                Object.DestroyImmediate(lamp.gameObject);
                var stNow = ReadState();
                int sideN = Side(breastN);
                string regionsNow = string.Join(" and ", Enumerable.Range(0, 2).Select(r =>
                {
                    int row = r * (sideN + 1) + sideN;
                    var placeNow = stNow[row * 2 * Row + 29];
                    var offNow = stNow[row * 2 * Row + 18];
                    return $"placed {placeNow.w:0}, far {offNow.x:0}, resting {offNow.y:0}, stepped {Time.timeSinceLevelLoad - stNow[r * (sideN + 1) * 2 * Row].w:0.000} s ago";
                }));
                var m0 = drawn.FirstOrDefault();
                var reaches = m0 != null ? m0.GetVector("_YAPS_SquashReach") : Vector4.zero;
                regionsNow += "; the squash's parts " + string.Join(" and ", Enumerable.Range(0, 2).Select(r =>
                {
                    var part = follow.transforms[2 * JellySpikeBuilder.chain + r];
                    return part == null ? "none" : $"{1000f * Vector3.Distance(part.position, socket.position):0} mm off the socket, reaching {1000f * reaches[2 * JellySpikeBuilder.chain + r] * part.lossyScale.x:0} mm toward the other";
                }));
                Debug.Log($"[Jelly] the plug's pair: its jelly drawing {100f * drawsShare:0.000}% of the picture with the plug in; regions {regionsNow}" +
                          (m0 != null ? $"; plug gain {m0.GetFloat("_YAPS_SquashPlug"):0.00}, tune {m0.GetVector("_YAPS_SquashTune")}, out {m0.GetVector("_YAPS_SquashOut")}, parts {m0.GetVector("_YAPS_JellyParts")}, " +
                                        $"lattice {m0.GetVector(Slot("_YAPS_JellyLattice"))}, state {(m0.GetTexture(Slot("_YAPS_JellyState")) is Texture tx ? tx.name : "none")}, shader {m0.shader.name}" : ""));
                plugOk = plugShare[0] > 3f * plugShare[2] + 1e-4f && plugShare[0] > 0.3f * plugShare[1];
                plugSaid = $"a plug through {(testSocket != null ? "a socket put between the pair" : socket.name)} moved {100f * plugShare[0]:0.000}% of the picture with the jelly on, " +
                           $"{100f * plugShare[1]:0.000}% on the squash alone (twice alike {100f * plugShare[2]:0.000}%)";
            }
            if (testSocket != null) Object.DestroyImmediate(testSocket.gameObject);
            Debug.Log($"[Jelly] {plugSaid}");
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
            Debug.Log($"[Jelly] a fist closed on the first's front and pulled 8 cm straight out: gripped {grabbed} frames; the skin it held came {1000f * grab[0]:0.0} mm, the whole {1000f * grab[1]:0.0} mm; let go, a ring {1000f * grab[2]:0.0} mm, the skin {1000f * grab[3]:0.0} mm off its rest two seconds on ({1000f * grab[4]:0.0} as the hand came); the strain {taut[0]:0.00} as it came, {taut[1]:0.00} pulled");
            Debug.Log($"[Jelly] told the animator: squeeze {told[0]:0.00} and grab {told[1]:0.00} as the hand came, {told[2]:0.00} and {told[3]:0.00} pulled, the clap's slap {told[4]:0.00}; " +
                      (twitchSeen ? $"a 2 cm twitch swung the masses {1000f * twitched[0]:0.0} and {1000f * twitched[1]:0.0} mm, {1000f * twitched[2]:0.0} mm off two seconds on" : "no twitch"));
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
                var drawn = YapsSquash.Materials(swing).Where(mm => mm.HasProperty(Slot("_YAPS_JellyLattice")) && mm.GetVector(Slot("_YAPS_JellyLattice")).x > 0).ToList();
                var lat = drawn.Select(mm => mm.GetVector(Slot("_YAPS_JellyLattice"))).ToList();
                var power = drawn.Select(mm => mm.GetFloat("_YAPS_DentPower")).ToList();
                var parts = drawn.Select(mm => mm.GetVector("_YAPS_JellyParts")).ToList();
                // handed: the jelly's parts given back to the squash, as before there was a jelly.
                void Drawn(bool jelly, bool squash, bool handed = false)
                {
                    for (int k = 0; k < drawn.Count; k++)
                    {
                        drawn[k].SetVector(Slot("_YAPS_JellyLattice"), jelly ? lat[k] : Vector4.zero);
                        drawn[k].SetFloat("_YAPS_DentPower", squash ? power[k] : 0f);
                        drawn[k].SetVector("_YAPS_JellyParts", handed ? Vector4.zero : parts[k]);
                        drawn[k].SetVector("_YAPS_JellySlap", Slapped);
                    }
                }
                // The draw's inputs written straight into the state it reads, as no step runs between
                // its renders: eight hands 40 cm out in front, in reach of every vertex's sums and
                // touching none, and each region a slap 50 ms old, so its ring is live. Off, both zeroed.
                var read = (drawn.Count > 0 ? drawn[0].GetTexture(Slot("_YAPS_JellyState")) as RenderTexture : null) ?? pa;
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
                // Between steps: particle 0's stamp a 90th of a second old, so the draw carries every
                // particle on as it does on a frame the rate skips; stepped, this frame's.
                var at0 = Read(read);
                var stamp = new Texture2D(1, 1, TextureFormat.RGBAFloat, false);
                void Between(bool on)
                {
                    for (int r = 0; r < 2; r++)
                    {
                        var p0 = at0[r * (Side(n) + 1) * 2 * Row];
                        stamp.SetPixel(0, 0, new Color(p0.x, p0.y, p0.z, Time.timeSinceLevelLoad - (on ? 1f / 90f : 0f)));
                        stamp.Apply();
                        Graphics.CopyTexture(stamp, 0, 0, 0, 0, 1, 1, read, 0, 0, 0, r * (Side(n) + 1));
                    }
                }
                // Far is the viewer's head past _Far; none reads as near.
                void Head(float off) => Shader.SetGlobalVectorArray("_CVR_PlayerHeadPositions", Enumerable.Repeat(off > 0 ? (Vector4) (swing.position + swing.forward * off) : Vector4.zero, 255).ToArray());
                void Near(float dt) { Head(0f); foreach (var mm in mats) mm.SetFloat("_FixedDt", dt); }
                // Past _Half: every third frame the whole chain at three times the frame, the far switch's alone between.
                int halfTick = 0;
                var timed = new (string name, Action set, Action run, RenderTexture rt, int runs)[]
                {
                    ("the step at 90 fps", () => Near(1f / 90f), Chain, pb, 1000),
                    ("at 60 fps", () => Near(1f / 60f), Chain, pb, 1000),
                    ("4 avatars' at 90", () => Near(1f / 90f), () => Crowd(4), pb, 300),
                    ("16 avatars'", () => { Near(1f / 90f); Unskinned(false); }, () => Crowd(16), pb, 100),
                    ("16 avatars', no far switch pass", () => Near(1f / 90f), () => Crowd(16, 2), pb, 100),
                    ("16 avatars', awake (no rest test)", () => { Near(1f / 90f); Rested(false); }, () => Crowd(16), pb, 100),
                    ("16 avatars' at 30 a second, 3 m off", () => { Near(1f / 30f); Rested(true); Unskinned(false); Head(3f); },
                        () => { if (halfTick++ % 3 == 0) Crowd(16); else Crowd(16, 99, chain.Count - 1); }, pb, 100),
                    ("16 avatars', the anchor unskinned", () => { Near(1f / 90f); Rested(true); Unskinned(true); }, () => Crowd(16), pb, 100),
                    ("20 m off", () => { Near(1f / 90f); Unskinned(false); Head(20f); }, Chain, pb, 1000),
                    ("16 avatars 20 m off", () => { Near(1f / 90f); Head(20f); }, () => Crowd(16), pb, 100),
                    ("the far switch's parser pass alone", () => Near(1f / 90f), () => Graphics.Blit(pa, strip, points), strip, 1000),
                    ("two copies", () => { }, () => { Graphics.Blit(pa, pb, copyMat); Graphics.Blit(pb, pa, copyMat); }, pb, 1000),
                    ($"the avatar drawn ({drawn.Count} materials) with the squash and the jelly", () => { Drawn(true, true); Fake(false, false); }, () => eye.Render(), eye.targetTexture, 200),
                    ("the jelly, 8 hands in reach", () => { Drawn(true, true); Fake(true, false); }, () => eye.Render(), eye.targetTexture, 200),
                    ("the jelly, 8 hands and a slap's ring", () => { Drawn(true, true); Fake(true, true); }, () => eye.Render(), eye.targetTexture, 200),
                    ("the jelly alone, no squash", () => { Drawn(true, false); Fake(false, false); Between(false); }, () => eye.Render(), eye.targetTexture, 200),
                    ("the jelly alone, between steps", () => { Drawn(true, false); Fake(false, false); Between(true); }, () => eye.Render(), eye.targetTexture, 200),
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
                Between(false);
                Object.DestroyImmediate(fake);
                Object.DestroyImmediate(stamp);
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
            var rate = jelly.GetComponentInChildren<CVRRenderController>(true).cvrRenderComponents.OfType<CVRBlitter>().First().blitMaterial.GetVector("_Rate");
            float fps = Time.captureFramerate;
            // A rate of 0 steps every frame.
            float Share(float r) => r > 0f ? Mathf.Min(1f, r / fps) : 1f;
            bool viewOk = viewRan.SequenceEqual(new[] { true, false, true, true }) && viewBack < 0.006f
                && nearFrames > 0 && Mathf.Abs((float) nearOn / nearFrames - Share(rate.x)) < 0.1f
                && halfFrames > 0 && Mathf.Abs((float) halfOn / halfFrames - Share(rate.y)) < 0.1f && Mathf.Abs(halfMost - 1f / Share(rate.y)) < 1f;
            bool walled = wallRest != null;
            if (walled) Debug.Log($"[Jelly] its probes feeling the world, a wall 3 cm into the first's front: the first dented {1000f * wallDent[0]:0.0} mm, the second {1000f * wallDent[1]:0.0} mm; the wall gone two thirds of a second, the first {1000f * wallDent[2]:0.0} mm off its rest; its skin {1000f * wallDent[3]:0.0} mm past the wall");
            bool wallOk = !walled || wallDent[0] > 0.01f && wallDent[1] < 0.005f && wallDent[2] < 0.006f && wallDent[3] < 0.008f;
            // Split, the cloth's twelve probes sit further apart than the 1.25 reaches that make a
            // second collider, so a ball between two can lead both: a slot more, never one lost.
            int oneBall = walled ? 2 : 1;
            bool probeOk = wallOk && (!probed || probeDent > probeBefore + 0.005f && probeSlots > probeSlotsBefore && probeSlots <= probeSlotsBefore + oneBall && probePushed >= 2 && bothSlots >= 1 && bothSlots <= oneBall && ringsSlow == 0 && ringsProbe == 1 && ringsBoth == 1);
            // Each by name, so a FAIL says which.
            var passes = new (string name, bool pass)[]
            {
                ("plug", plugOk), ("anchor", anchorOk), ("told", told[0] < 0.05f && told[1] < 0.5f && told[2] > 0.3f && told[3] > 0.5f && told[4] > 0.1f),
                ("twitch", !twitchSeen || twitched[0] > 0.004f && twitched[1] > 0.004f && twitched[2] < 0.002f), ("clap", clap[1] > 0.005f && clap[2] > 0.005f),
                ("grab", grabbed > 60 && grab[0] > 0.04f && grab[1] > 0.002f && grab[2] > 0.005f && grab[3] < 0.006f), ("strain", taut[0] < 0.05f && taut[1] > 0.3f),
                ("rest", restMost < 0.006f), ("poke", pokeMost > 0.01f && poked > 3f * noise + 0.002f), ("swing", swingMost > 0.005f), ("contact", bodies > 0.003f),
                ("hands outer", handsOuter > 0.05f), ("view", viewOk), ("probes", probeOk), ("own through", ownThrough[1] <= 20 && ownThrough[2] < 0.02f),
            };
            bool ok = passes.All(q => q.pass);
            if (!ok) Debug.Log($"[Jelly] failed: {string.Join(", ", passes.Where(q => !q.pass).Select(q => q.name))}");
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
                      $"near, the steps ran {nearOn} of {nearFrames} frames; its head 3 m off, {halfOn} of {halfFrames}, the most a step took {halfMost:0}");
            if (probed)
                Debug.Log($"[Jelly] another player's collider, a 4.5 cm ball only the probes are given, 3 cm in between the first's frontmost probe and its nearest: the furthest particle " +
                          $"{1000f * probeBefore:0.0} mm off its rest before, {1000f * probeDent:0.0} mm pressed; {probeSlots} of the step's eight hand slots held a probe's press, {probePushed} probes pushed (two or more, {(oneBall == 1 ? "one slot" : "one or two slots")} wanted); " +
                          $"the probes moved {1000f * probeMoved:0.0} mm at most; {probeSlotsBefore} slots held before it came (the slap's palm, parked 3 cm off the outer side)");
            if (probed) Debug.Log($"[Jelly] the probes: {string.Join("; ", probeSaid)}");
            if (probed) Debug.Log($"[Jelly] the probes' press, the furthest particle in mm by frame: {string.Join(", ", probeSeries)}");
            if (probed) Debug.Log($"[Jelly] breasts' cost with a collider pressing two probes: {probeCost} ms a frame");
            Debug.Log($"[Jelly] breasts' cost loaded: a finger 3 cm in {pokeCost}, a wall's faces {wallCost ?? "(no world probes)"}, a fist gripping {gripCost} ms a frame");
            if (probed) Debug.Log($"[Jelly] the probes' slaps, rings fired: {ringsSlow} in the slow press (0 wanted), {ringsProbe} for the ball's slap alone (1), " +
                                  $"{ringsBoth} for it with a hand in the arrays (1), which held {bothSlots} slots ({oneBall} at most); {(ringSaid.Count > 0 ? string.Join("; ", ringSaid) : "none")}");
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
the eight particles round it, so it stays smooth. Tools > Avatar Bridge > Dev > Jelly >
Coarse lattice, ticked by default, untick it for the fine lattice of 3072 particles, which costs a slow
GPU about twice as much and looks the same within millimetres. The first pass also picks out the hands in reach, eight at most, so a full
instance costs no more than an empty one. Below 72 fps each pass takes a step; above it the first
works out where each particle is headed and the second fits. More than 6 m from you, another
player's jelly hangs rigid and costs next to nothing, and so does the prop left still and
untouched, asleep. It should sag a little, wobble when you turn or bounce, and dent and jiggle when any hand
pokes it, yours or another player's.

## Setting it up

1. Select the avatar in the scene and run Tools > Avatar Bridge > Dev > Jelly > Early spike >
   Add to the selected avatar. It goes under the chest bone, in front of the chest, sized for the avatar,
   replacing any earlier one. Move it if it sits inside anything. Early spike > Add with the cost
   meter does the same with the meter above it, which costs far more than the jelly. Early spike >
   Add to wear, without the test cube, leaves the yellow cube off, and with it a pass, a readback
   and a parser: the one to keep on. Early spike > Add to wear, far copies switched off, keeps the
   pass and the parser for the far switch alone: each copy near you costs a little more, each far
   one less, so it only pays on a slow GPU in a crowd. Early spike > Add three to wear, packed,
   puts three in one texture and one pair of passes, each on its own bone: in front of the chest,
   behind the hips and above the head. Early spike > Make a prop of three, packed, sets three side
   by side on one prop.
2. Upload. Nothing else: no parameters, no menu, no sync.

On the avatar's own breasts: build the YAPS squash first, select the avatar and run Jelly >
Put the selected avatar's breasts on the jelly. One lattice is fitted over the pair, standing on
the chest bone with its back face on the chest wall, and the body's squash materials are patched
to move each of the pair's vertices with it (the first time takes a minute or two a shader); the
squash leaves the pair to it, and the cloth or dynamic bones swinging the pair are switched off.
It rests on the modelled shape when upright. Jelly > Take the jelly off the selected
avatar puts the squash and the physics back. Building the squash again takes the jelly
off its materials; run the menu again after.

Or as a prop on its own: Jelly > Early spike > Make a prop in the open scene, then upload it from the CCK
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
