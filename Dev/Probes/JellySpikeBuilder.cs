// The jelly spike: can ChilloutVR run a soft body on an avatar, every frame, and at what cost?
//
// A lattice of particles lives in a float render texture, where each particle is in its left half
// and where it was a pass before in its right. Two blitters run by a render controller step it from
// that texture into a twin and back, a pass each, no temporary and no copy: every particle is
// pulled toward where the rigid fit of its 3x3x3 neighbourhood puts it (shape matching), the back
// face is held on an anchor the material data provider hands in, and the hands the first pass
// found in reach push it out of the way. Past 6 m from the viewer's head it hangs rigid and does
// next to no work. A mesh draws the lattice's surface from the same texture. Nothing is baked,
// nothing is synced, and each client steps its own copy.
//
// A plain cube rides the front: the second pass writes where the front centre is, and how its
// neighbourhood has turned, into two texels of the state's last row, a third blitter copies them
// into a 2x1 texture, and a texture parser writes those into a transform's local position and
// Euler angles. Nothing on the cube's own shader knows
// the tissue, so its riding says whether anything attached to a bone could follow the tissue too.
//
// A cost meter floats above it: the frame time, and every half second a load of 32 more copies of
// the step, on a second lattice nobody draws, switched on and off, the difference read as what one
// pass costs on that machine.
//
//   -executeMethod AvatarBridge.Regression.JellySpikeBuilder.Build
//   -executeMethod AvatarBridge.Regression.JellySpikeBuilder.Bench -jellyOut <dir>
//   -executeMethod AvatarBridge.Regression.JellySpikeBuilder.Verify -jellyOut <dir>
//
// Bench times a frame the way a viewer pays for the prefab (its two passes, one texture into the
// other, and the rider's copy; the parser's readback apart) at 90 fps, where the first pass predicts and the second fits,
// and at 60, where each steps, for one pair's lattice and four times that, with no players, 64 far
// off, 64 with eight hands in reach, and the viewer 1 m and 20 m off; times chains of plain copies,
// for what a pass costs before it works; and checks the tissue settles and swings back sanely.
// Pass -force-device-index 1 to run it on a second GPU, the CPU's own, for a low-end machine. Verify plays the prefab in
// batch with the CCK's own render controller and a swinging anchor. Batch, never -nographics.
#if CVR_CCK_EXISTS && UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        const string Folder = "Assets/AvatarBridge_JellySpike";
        const string Prefab = Folder + "/Jelly_Spike.prefab";
        const string Scene = Folder + "/Jelly_Preview.unity";
        const string K = "JellySpike.";
        // One pair's tissue is about 3000 particles; this is 3072, 20 cm across at 1.33 cm.
        static readonly Vector3Int Size = new Vector3Int(16, 12, 16);
        const float Spacing = 0.2f / 15f;
        const int Row = 64, Passes = 2, Frames = 300, Load = 32;
        struct Late { }

        static int frame;
        static Camera eye;
        static Transform swing, jelly;
        static Matrix4x4 seen;

        static JellySpikeBuilder()
        {
            if (SessionState.GetInt(K + "on", 0) == 1) EditorApplication.update += Hook;
        }

        public static void Build() => EditorApplication.Exit(Make() != null ? 0 : 1);

        [MenuItem("Tools/AvatarBridge Dev/Jelly spike: add to the selected avatar")]
        static void AddToSelected() => Add(withMeter: false);

        // The meter's load costs 32 passes every other half second, far more than the jelly, so it
        // goes on only when asked for.
        [MenuItem("Tools/AvatarBridge Dev/Jelly spike: add with the cost meter")]
        static void AddWithMeter() => Add(withMeter: true);

        // The jelly alone as a prop, its cost told apart from any avatar's.
        [MenuItem("Tools/AvatarBridge Dev/Jelly spike: make a prop in the open scene")]
        static void MakeProp()
        {
            var prefab = Make(preview: false, withMeter: false);
            if (prefab == null) return;
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
            var span = (Vector3) (Size - Vector3Int.one) * Spacing;
            box.center = new Vector3(0f, 1.2f, 0.5f * span.z + 0.025f);
            box.size = span + new Vector3(0.06f, 0.06f, 0.11f);
            Undo.RegisterCreatedObjectUndo(prop, "Make the jelly prop");
            Selection.activeGameObject = prop;
            Debug.Log("[Jelly] made Jelly Prop in the open scene, grabbable; upload it from the CCK Control Panel");
        }

        static void Add(bool withMeter)
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
            var prefab = Make(preview: false, withMeter: withMeter);
            if (prefab == null) return;
            // Any earlier one on the avatar goes. An instance is named after the prefab's file,
            // Jelly_Spike; older builds left Jelly Spike.
            var stale = avatar.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == prefab.name || t.name == "Jelly Spike").Select(t => t.gameObject).ToList();
            foreach (var old in stale)
                if (old != null) Undo.DestroyObjectImmediate(old);
            var root = avatar.transform;
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            // Sized and placed for the avatar's height, so it hangs clear of the chest on anyone.
            float tall = head != null ? Mathf.Max(Vector3.Dot(head.position - root.position, root.up), 0.3f) : 1.5f;
            var made = (GameObject) PrefabUtility.InstantiatePrefab(prefab, chest);
            made.transform.SetPositionAndRotation(chest.position + root.forward * (0.35f * tall / 1.5f), root.rotation);
            made.transform.localScale = Vector3.one * (tall / 1.5f / Mathf.Max(chest.lossyScale.x, 1e-6f));
            Undo.RegisterCreatedObjectUndo(made, "Add the jelly spike");
            Selection.activeGameObject = made;
            Debug.Log($"[Jelly] added under {chest.name}, {0.35f * tall / 1.5f:0.00} m in front, scaled for a {tall:0.00} m head height; {stale.Count} earlier one(s) taken off");
        }

        static GameObject Make(bool preview = true, bool withMeter = true)
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
                Debug.Log("[Jelly] BUILD ABORT: a shader did not import or has errors");
                return null;
            }

            var state = Save(State(Size), "/Jelly_State.renderTexture");
            var twin = Save(State(Size), "/Jelly_State_B.renderTexture");
            var stepMat = Save(StepMaterial(step, Size, 0), "/Jelly_Step.mat");
            var stepMat2 = Save(StepMaterial(step, Size, 1), "/Jelly_Step_2.mat");
            var drawMat = new Material(draw);
            drawMat.SetTexture("_State", state);
            drawMat = Save(drawMat, "/Jelly_Draw.mat");
            var mesh = Save(Surface(Size), "/Jelly_Surface.asset");
            var where = Save(Strip(2), "/Jelly_Points.renderTexture");
            var pointsMat = Save(StepMaterial(step, Size, 2), "/Jelly_Points.mat");
            var plain = Save(new Material(Shader.Find("Standard")) { color = new Color(1f, 0.85f, 0.1f) }, "/Jelly_Rider.mat");
            var root = new GameObject("Jelly Spike");
            var tissue = new GameObject("Tissue");
            tissue.transform.SetParent(root.transform, false);
            var provider = tissue.AddComponent<CVRMaterialDataProvider>();
            provider.dataMode = CVRMaterialDataProvider.DataMode.TransformLocalToWorld;
            provider.transforms = new[] { root.transform };
            provider.shaderPropertyName = "_JellyAnchor";
            var controller = tissue.AddComponent<CVRRenderController>();
            // The anchor first, then the passes, in the order the client runs them.
            controller.cvrRenderComponents.Add(provider);
            for (int i = 0; i < Passes; i++)
            {
                var blitter = tissue.AddComponent<CVRBlitter>();
                // Into the twin and back: a texture in and out the same would make the client
                // render into a temporary and copy it back, every pass.
                blitter.originTexture = i == 0 ? state : twin;
                blitter.destinationTexture = i == 0 ? twin : state;
                blitter.blitMaterial = i == 0 ? stepMat : stepMat2;
                blitter.destinationTextures = new RenderTexture[0];
                controller.cvrRenderComponents.Add(blitter);
            }
            // Last, the rider's texels out of the stepped state, for the parser.
            var pointer = tissue.AddComponent<CVRBlitter>();
            pointer.originTexture = state;
            pointer.destinationTexture = where;
            pointer.blitMaterial = pointsMat;
            pointer.destinationTextures = new RenderTexture[0];
            controller.cvrRenderComponents.Add(pointer);
            provider.materials = new[] { stepMat, stepMat2 };

            if (withMeter)
            {
                // The cost meter: its clock, and the load, which steps a lattice nobody draws in the odd
                // phases and leaves it be in the even ones.
                var load = Save(State(Size), "/Jelly_Load.renderTexture");
                var loadMat = StepMaterial(step, Size, 0);
                loadMat.SetFloat("_Stress", 1f);
                // The step alone, without the first pass's look for hands.
                loadMat.DisableKeyword("JELLY_SCAN");
                loadMat = Save(loadMat, "/Jelly_Load.mat");
                provider.materials = new[] { stepMat, stepMat2, loadMat };
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

            // The rider, at rest on the front centre, and a plain cube on it.
            var rider = new GameObject("Rider");
            rider.transform.SetParent(root.transform, false);
            rider.transform.localPosition = new Vector3(0f, 0f, (Size.z - 1) * Spacing);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Plain cube";
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.transform.SetParent(rider.transform, false);
            cube.transform.localPosition = new Vector3(0f, 0f, 0.025f);
            cube.transform.localScale = Vector3.one * 0.05f;
            cube.GetComponent<MeshRenderer>().sharedMaterial = plain;
            var parser = tissue.AddComponent<CVRTexturePropertyParser>();
            parser.textureType = CVRTexturePropertyParser.TextureType.LocalTexture;
            parser.texture = where;
            // Vector3 properties are type 4, an element each; the texels hold each value between
            // its task's bounds, so a clamped remap and an unclamped one read the same.
            for (int i = 0; i < 3; i++)
                parser.tasks.Add(Task(rider, 0, i, "localPosition", -0.5f, 0.5f));
            for (int i = 0; i < 3; i++)
                parser.tasks.Add(Task(rider, 1, i, "localEulerAngles", -180f, 180f));
            var body = new GameObject("Surface");
            body.transform.SetParent(root.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = body.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = drawMat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

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

        // The row after the lattice's: the hands in reach, the rider and the far switch.
        static int Side(Vector3Int n) => (n.x * n.y * n.z + Row - 1) / Row;

        static RenderTexture State(Vector3Int n)
        {
            // Float, so positions keep their digits; point and no mips, so a texel is only ever itself.
            return new RenderTexture(2 * Row, Side(n) + 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
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

        static Material StepMaterial(Shader step, Vector3Int n, int pass)
        {
            var m = new Material(step);
            m.SetVector("_Lattice", new Vector4(n.x, n.y, n.z, Spacing));
            m.SetFloat("_Pass", pass);
            // The first pass looks for the hands in reach.
            if (pass == 0) m.EnableKeyword("JELLY_SCAN");
            return m;
        }

        // One lattice's textures and passes, wired as the prefab wires them.
        sealed class Rig
        {
            public RenderTexture State, Twin, Where;
            public Material[] Mats;
            readonly int side;

            public Rig(Vector3Int n)
            {
                State = JellySpikeBuilder.State(n);
                Twin = JellySpikeBuilder.State(n);
                Where = Strip(2);
                foreach (var t in new[] { State, Twin, Where }) t.Create();
                side = Side(n);
                var step = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/JellyStep.shader");
                Mats = new[] { StepMaterial(step, n, 0), StepMaterial(step, n, 1), StepMaterial(step, n, 2) };
            }

            public void Anchor(Matrix4x4 m)
            {
                foreach (var mat in Mats) mat.SetMatrixArray("_JellyAnchor", new[] { m });
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
                Graphics.Blit(State, Where, Mats[2]);
            }

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

            public void Release()
            {
                foreach (var t in new[] { State, Twin, Where }) t.Release();
            }
        }

        // The lattice's six faces as a grid of quads, each vertex naming its particle's texel.
        static Mesh Surface(Vector3Int n)
        {
            var at = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            var index = new Dictionary<int, int>();
            int Vertex(int i, int j, int k)
            {
                int p = (k * n.y + j) * n.x + i;
                if (index.TryGetValue(p, out int v)) return v;
                index[p] = at.Count;
                at.Add(new Vector3((i - 0.5f * (n.x - 1)) * Spacing, (j - 0.5f * (n.y - 1)) * Spacing, k * Spacing));
                uv.Add(new Vector2(p % Row + 0.5f, p / Row + 0.5f));
                return at.Count - 1;
            }
            void Face(int axis, int fixedAt)
            {
                int a = (axis + 1) % 3, b = (axis + 2) % 3;
                int na = n[a], nb = n[b];
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
                Face(axis, n[axis] - 1);
            }
            var mesh = new Mesh { name = "Jelly surface" };
            mesh.SetVertices(at);
            mesh.SetUVs(0, uv);
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
            // ponytail: the surface mesh is kept as it is; it only changes with the lattice's size.
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
            foreach (var n in new[] { Size, new Vector3Int(32, 24, 16) })
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
                // Past the far switch's 6 m the tissue hangs where the rigid shape puts it.
                Health(Read(rigs[c].State), n, still, out _, out float front);
                if (head > 6.5f) ok &= front < 1e-3f;
                var at = new[] { 90, 60 }.Select(fps => $"{Spread(frames[(c, fps)])} ms a frame at {fps} fps ({Median(submits[(c, fps)]):0.000} to submit)");
                lines.Add($"[Jelly] {n.x * n.y * n.z} particles, {players} players, {inReach} hands in reach{(head > 0 ? $", the viewer {head:0} m off" : "")}, " +
                          $"kept {kept[c]}, front {100f * front:0.0} cm off rigid: {string.Join(", ", at)}");
                rigs[c].Release();
            }
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
            foreach (int fps in new[] { 90, 60 })
            {
                var rig = new Rig(Size);
                rig.Dt(1f / fps);
                var view = Viewer(rig.State, out var shots);
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
                        var health = Health(Read(rig.State), Size, anchor, out string said, out _);
                        ok &= health;
                        if (f == -1) lines.Add($"[Jelly] at {fps} fps, settled after 2 s: {said}");
                        else trace.Add($"{f}: {said}");
                    }
                    if (fps == 90 && f >= 0 && shotsAt.Contains(f)) strip.Add(Shot(shots));
                }
                lines.Add($"[Jelly] at {fps} fps, turned 60 degrees in a sixth of a second then held, frame: " + string.Join(" | ", trace));
                Object.DestroyImmediate(view);
                rig.Release();
            }
            var sheet = new Texture2D(strip[0].width * 4, strip[0].height * 2, TextureFormat.RGB24, false);
            for (int i = 0; i < strip.Count && i < 8; i++)
                sheet.SetPixels((i % 4) * strip[0].width, (1 - i / 4) * strip[0].height, strip[0].width, strip[0].height, strip[i].GetPixels());
            File.WriteAllBytes(Path.Combine(dir, "turn.png"), sheet.EncodeToPNG());
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
            Vector3 Rest(int i, int j, int k) => new Vector3((i - 0.5f * (n.x - 1)) * Spacing, (j - 0.5f * (n.y - 1)) * Spacing, k * Spacing);
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
        static GameObject Viewer(RenderTexture state, out Camera cam)
        {
            var draw = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/JellyDraw.shader"));
            draw.SetTexture("_State", state);
            var go = new GameObject("Jelly bench") { hideFlags = HideFlags.HideAndDontSave };
            go.AddComponent<MeshFilter>().sharedMesh = Surface(Size);
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
            if (!EditorApplication.isPlaying || eye != null || SessionState.GetInt(K + "on", 0) != 1) return;
            eye = Object.FindObjectsOfType<Camera>().First();
            eye.targetTexture = new RenderTexture(480, 360, 24);
            swing = GameObject.Find("Swing").transform;
            jelly = Object.FindObjectsOfType<CVRBlitter>().First().transform.parent;
            Time.captureFramerate = 90;
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
            bool ok = Health(tissueNow, Size, seen, out string said, out _);
            // The rider's texels, decoded as the parser's tasks would, against the front centre.
            var where = Read(AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + "/Jelly_Points.renderTexture"));
            int front = ((Size.z - 1) * Size.y + Size.y / 2) * Size.x + Size.x / 2;
            var frontAt = seen.inverse.MultiplyPoint3x4((Vector3) tissueNow[(front / Row) * 2 * Row + front % Row]);
            var read = (Vector3) where[0] - Vector3.one * 0.5f;
            var turn = ((Vector3) where[1] - Vector3.one * 0.5f) * 360f;
            ok &= (read - frontAt).magnitude < 1e-3f && where[0].w == 1f;
            var clockNow = Read(AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + "/Jelly_Clock.renderTexture"));
            said += $"; the meter's clock: {clockNow[0].x:0.00} ms now, last phases off {clockNow[2].x:0.00} and on {clockNow[2].y:0.00} ({clockNow[2].z:0}/{clockNow[2].w:0})";
            ok &= Mathf.Abs(clockNow[0].x - 1000f / 90f) < 0.5f;
            int dead = jelly.GetComponentsInChildren<Renderer>(true).Count(r => r.sharedMaterial == null)
                + jelly.GetComponentsInChildren<CVRBlitter>(true).Count(b => b.blitMaterial == null || b.originTexture == null || b.destinationTexture == null);
            ok &= dead == 0;
            said += $"; built twice, {dead} missing references";
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

        // ---------------------------------------------------------------- shaders

        const string StepShader = @"// The jelly spike's step. Written by JellySpikeBuilder; edits here are overwritten.
//
// The frame's two passes, from one texture into its twin and back. Left half: where each particle
// is, w the _Time.y of the last pass (negative while no anchor arrives). Right half: where it was a
// pass before, w the h that pass took, so a velocity is only ever read off the two. The row after
// the lattice: the hands in reach (texels 0 to 15), the rider (16, 17) and the far switch (18).
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
        _Hand (""Hand radius at a 1.6 m viewpoint"", Float) = 0.05
        _Far (""Hangs rigid past this many metres from the viewer's head"", Float) = 6
        _Pass (""Which of the frame's passes this is: 2 copies the rider out"", Float) = 0
        _FixedDt (""Fixed frame time, 0 for the real one"", Float) = 0
        _Stress (""The cost meter's load: steps only in odd phases"", Float) = 0
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
            #include ""UnityCG.cginc""
            #include ""JellyCommon.cginc""

            float _Stiff, _Hz, _Hand, _Far, _Pass, _FixedDt, _Stress;
            // The client's first is the local player's head; only it is read.
            float4 _CVR_PlayerHeadPositions[1];

#ifdef JELLY_SCAN
            float4 _CVR_PlayerLeftHandPositions[255];
            float4 _CVR_PlayerRightHandPositions[255];
            float4 _CVR_PlayerChestPositions[255];
            float4 _CVR_PlayerRootPositions[255];
            float4 CVRGlobalParams1;

            // Hand slot texel x of the eight, two texels each, (wrist, radius) then (tip, 1), an
            // empty one 0. Each looks through every player's hands and keeps the one it is due, so a
            // pass reads eight however many players there are.
            float4 Scan(int x, float4x4 m, float scale)
            {
                int slot = x >> 1;
                int3 n = Count();
                // A sphere round the lattice at rest, with room for it to bulge.
                float3 centre = mul(m, float4(0, 0, 0.5 * (n.z - 1) * _Lattice.w, 1)).xyz;
                float reach = (0.5 * length(float3(n - 1)) * _Lattice.w + 0.05) * scale;
                int players = min((int) round(CVRGlobalParams1.y), 64);
                int found = 0;
                float4 kept = 0;
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
                                    float3 wrist = hand == 0 ? _CVR_PlayerLeftHandPositions[who].xyz : _CVR_PlayerRightHandPositions[who].xyz;
                                    float3 arm = wrist - chest;
                                    float3 tip = wrist + (dot(arm, arm) > 1e-6 ? normalize(arm) : 0) * max(0.11 * tall - r, 0);
                                    float3 seg = tip - wrist;
                                    float3 closest = wrist + seg * saturate(dot(centre - wrist, seg) / max(dot(seg, seg), 1e-9));
                                    // ponytail: eight hands at most, the first in player order; a crowd round one
                                    // jelly needs more slots.
                                    bool hit = dot(wrist, wrist) >= 1e-6 && distance(centre, closest) <= reach + r;
                                    // Kept, not returned: a return inside the unrolled loop fails D3D11's validation.
                                    kept = hit && found == slot ? ((x & 1) == 0 ? float4(wrist, r) : float4(tip, 1)) : kept;
                                    found += hit;
                                }
                            }
                        }
                    }
                }
                return kept;
            }
#endif

            // Where particle p, at l, goes this pass, and its neighbourhood's turn: toward the rigid
            // fit of its neighbourhood, toward its rest on the anchor, and out of any hand in reach.
            // With predicted, where each particle is headed is the first pass's; otherwise it is
            // worked out here from both halves.
            float3 Moved(uint p, int3 l, float4x4 m, float scale, float h, float3 home, bool predicted, bool distant, uint side, out float4 q)
            {
                q = Quat((float3x3) m / scale);
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
                    base = s.xyz + (s.xyz - was.xyz) * kh;
                    ahead = base + Gravity * h * h;
                }
                float3 c, rc;
                [branch] if (predicted) q = Fit(p, l, ahead, base, kh, m, h, home, q, true, c, rc);
                else q = Fit(p, l, ahead, base, kh, m, h, home, q, false, c, rc);
                float3 goal = mul(Turn(q), (Rest(l) - rc) * scale) + c;
                // _Stiff is the pull over a 180th of a second, so the tissue feels the same
                // whether a frame takes one pass or two.
                float3 next = lerp(ahead, goal, 1 - pow(1 - _Stiff, h * 180));
                // And the whole of it toward its rest on the anchor, as a spring of _Hz: the
                // neighbourhoods alone keep the shape near each particle but let a block 16
                // across fold under its own weight, 42 cm at the front.
                float spring = 6.2831853 * _Hz * h;
                next = lerp(next, home, min(spring * spring, 1));
                // The hands found in reach, each from the wrist to its tip with a radius. Slots fill in
                // order, so an empty first one means there are none. Unrolled, so Shader Filtering
                // leaves it whole.
                [branch] if (_MainTex.Load(int3(1, side, 0)).w > 0.5)
                {
                    [unroll] for (int e = 0; e < 8; e++)
                    {
                        float4 a = _MainTex.Load(int3(2 * e, side, 0)), b = _MainTex.Load(int3(2 * e + 1, side, 0));
                        if (!(b.w > 0.5)) continue;
                        float3 seg = b.xyz - a.xyz;
                        float3 closest = a.xyz + seg * saturate(dot(next - a.xyz, seg) / max(dot(seg, seg), 1e-9));
                        float3 off = next - closest;
                        float d2 = dot(off, off);
                        if (d2 < a.w * a.w && d2 > 1e-12) next = closest + off * (a.w * rsqrt(d2));
                    }
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
                float3 at = Moved((uint) Index(l), l, m, scale, h, home, predicted, distant, side, q);
                float3x3 r = (float3x3) m / scale;
                if (x == 16) return float4(saturate(mul(transpose(r), at - m._m03_m13_m23) / scale + 0.5), 1);
                float3x3 t = mul(transpose(r), Turn(q));
                // Unity turns by z, then x, then y: t = Ry Rx Rz.
                float3 euler = degrees(float3(asin(clamp(-t[1][2], -1, 1)), atan2(t[0][2], t[2][2]), atan2(t[1][0], t[1][1])));
                return float4(saturate(euler / 360 + 0.5), 1);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                int2 px = int2(i.pos.xy);
                int3 n = Count();
                uint count = n.x * n.y * n.z, side = (count + Row - 1) / Row;
                // A third pass copies the rider's two texels into a texture of their own: the texture
                // parser reads back a whole texture, and the whole state costs it ten times two texels.
                if (_Pass > 1.5) return _MainTex.Load(int3(16 + px.x, side, 0));
                float4 here = _MainTex.Load(int3(px, 0));
                // The cost meter's load rests in even phases, as cheap as a pass can be.
                if (_Stress > 0 && fmod(floor(_Time.y / Window), 2) < 0.5) return here;
                float4x4 m = _JellyAnchor[0];
                float scale = length(m._m00_m10_m20);
                bool anchored = scale > 1e-6;
                float dt = _FixedDt > 0 ? _FixedDt : clamp(unity_DeltaTime.x, 1.0 / 240, 1.0 / 30);
                int passes = dt > 1.0 / 72 ? 2 : 1;
                float h = dt / passes;
                // The meter's load always takes a whole step, the cost it is there to show.
                bool predicted = passes == 1 && !(_Stress > 0);
                // Far from the viewer's head the tissue hangs rigid. The first pass decides, with half
                // a metre's give so it does not flicker at the edge, and the second reads its answer.
                // No head, as without a humanoid avatar, counts as near.
                bool wasDistant = _MainTex.Load(int3(18, side, 0)).x > 0.5, distant = wasDistant;
                if (_Pass < 0.5)
                {
                    float3 head = _CVR_PlayerHeadPositions[0].xyz;
                    distant = !(_Stress > 0) && dot(head, head) > 1e-6 && distance(head, m._m03_m13_m23) > _Far - (wasDistant ? 0.5 : 0);
                }

                if ((uint) px.y == side)
                {
                    if (!anchored) return 0;
                    if (px.x == 18) return float4(distant, 0, 0, 1);
                    if (_Pass < 0.5)
                    {
#ifdef JELLY_SCAN
                        [branch] if (px.x < 16)
                        {
                            if (distant) return 0;
                            return Scan(px.x, m, scale);
                        }
#endif
                        return here;
                    }
                    [branch] if (px.x == 16 || px.x == 17) return Rider(px.x, m, scale, h, predicted, distant, side);
                    return here;
                }

                // Far off, the first pass leaves its twin be and the second puts the tissue at rest.
                [branch] if (distant && _Pass < 0.5) return here;
                uint p = (uint) px.x % Row + (uint) px.y * Row;
                if (p >= count) return 0;
                int half_ = (uint) px.x >= Row;
                int3 l = Place(p);
                // No anchor yet: nothing to hang from. Marked, so the surface can say so.
                if (!anchored) return half_ == 0 ? float4(0, 0, 0, -1) : 0;
                float3 home = mul(m, float4(Rest(l), 1)).xyz;
                [branch] if (distant) return float4(home, half_ == 1 ? h : _Time.y);
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
                    float kh = exp(-_Damp * was.w) / max(was.w, 1e-4) * h;
                    return float4(s.xyz + (s.xyz - was.xyz) * kh + Gravity * h * h, 1);
                }
                if (predicted)
                {
                    // The fit, from the first pass's predictions; the right half was settled there.
                    if (half_ == 1) return here;
                    return float4(Moved(p, l, m, scale, h, home, true, distant, side, q), _Time.y);
                }
                // A whole step. The right half only moves along, so half the texture does next to no work.
                if (half_ == 1)
                {
                    float4 s = _MainTex.Load(Texel(p, 0));
                    return float4(Fresh(s, here, home) ? home : s.xyz, h);
                }
                return float4(Moved(p, l, m, scale, h, home, false, distant, side, q), _Time.y);
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
float4x4 _JellyAnchor[1];
float _Damp;

static const uint Row = 64;
static const float Window = 0.5;
static const float3 Gravity = float3(0, -9.81, 0);
// The rigid fit's rotation steps. Each starts from the anchor's turn, which a neighbourhood's own
// is never far from, so three land where six did.
static const int Turns = 3;

int3 Count() { return int3(_Lattice.xyz); }
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
int3 Texel(uint p, int half_) { return int3(p % Row + half_ * Row, p / Row, 0); }
float3 Rest(int3 l) { int3 n = Count(); return float3(l.x - 0.5 * (n.x - 1), l.y - 0.5 * (n.y - 1), l.z) * _Lattice.w; }

// A fresh texture, a new copy of the avatar, garbage, or a teleport: start at rest.
bool Fresh(float4 s, float4 was, float3 home)
{
    return !(was.w > 0 && was.w < 0.1) || !(abs(s.x) + abs(s.y) + abs(s.z) + abs(was.x) + abs(was.y) + abs(was.z) < 1e6)
        || !(distance(s.xyz, home) < 1);
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
float4 Fit(uint p, int3 l, float3 ahead, float3 base, float kh, float4x4 m, float h, float3 home, float4 q, bool predicted, out float3 c, out float3 rc)
{
    int3 n = Count();
    bool3 lo = l > 0, hi = l < n - 1;
    float s = _Lattice.w;
    float3 mx = m._m00_m10_m20 * s, my = m._m01_m11_m21 * s;
    float3 behind = home - m._m02_m12_m22 * s - Gravity * h * h;
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
// Every vertex goes where its particle is. Pink while the tissue steps; red once it has not
// stepped for a quarter second; purple while no anchor reaches it.
Shader ""AvatarBridge/Jelly Spike""
{
    Properties
    {
        // Declared, not only set: the client points a material's render textures at the avatar
        // copy's own by walking its declared properties, and an undeclared one would go on
        // reading the asset, which nothing writes.
        _State (""Tissue state"", 2D) = ""black"" {}
        _Color (""Colour"", Color) = (0.95, 0.55, 0.65, 1)
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

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
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
                float4 s = _State.Load(int3(int2(v.uv), 0));
                float3 at = s.xyz;
                o.tint = _Color.rgb;
                if (s.w < 0) o.tint = float3(0.6, 0.2, 0.9);
                else if (abs(_Time.y - s.w) > 0.25) o.tint = float3(0.9, 0.1, 0.1);
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

A block of jelly about 20 cm across hangs in front of the chest. Its 3072 particles live in a
render texture that two blitters step every frame, into a twin and back; nothing is baked and
nothing is synced. The first pass also picks out the hands in reach, eight at most, so a full
instance costs no more than an empty one. Below 72 fps each pass takes a step; above it the first
works out where each particle is headed and the second fits. More than 6 m from you, another
player's jelly hangs rigid and costs next to nothing. It should sag a little, wobble when you
turn or bounce, and dent and jiggle when any hand pokes it, yours or another player's.

## Setting it up

1. Select the avatar in the scene and run Tools > AvatarBridge Dev > Jelly spike: add to the
   selected avatar. It goes under the chest bone, in front of the chest, sized for the avatar,
   replacing any earlier one. Move it if it sits inside anything. Jelly spike: add with the cost
   meter does the same with the meter above it, which costs far more than the jelly.
2. Upload. Nothing else: no parameters, no menu, no sync.

Or as a prop on its own: Jelly spike: make a prop in the open scene, then upload it from the CCK
Control Panel. Grab it in game and shake it; let go and it hangs where you left it.

Before uploading, `Jelly_Preview.unity` shows it in the editor: open it and press Play.

## What the colour says

| Colour | Means |
| --- | --- |
| Pink | The tissue is stepping |
| Red | It has not stepped for a quarter second: the blitters are not running for this copy |
| Purple | The anchor never arrived: the material data provider is not running |

The meter above it has four rows: white, your frame time in ms; green, its average with the load
off; red, with it on; the load switching every half second; yellow, what one pass of
the step's own work costs, the difference over the load's 32 passes. The load steps in place, so
each of its runs also pays a copy back the jelly's own passes do not. The load only says something with the frame rate uncapped, on desktop: under VR or vsync the frame
time sits at the cap whatever the work. The load itself costs 32 passes every other half second,
so it, not the jelly, is most of the frame rate a viewer loses with the meter running. A yellow near zero on a CPU-bound frame means the GPU did
the work in time it was waiting anyway: no cost to that frame.

A plain yellow cube sits on the front. Its shader knows nothing of the tissue: a texture parser
moves it, from where the front centre is. It should ride the front as the jelly wobbles and as it
is poked, tilting with it, a frame or two behind. If the jelly wobbles and the cube stays put, the
parser is not running.

Past 6 m it should go still and keep its rest shape, without the sag, and pick the wobble up again
once you are back within 5.5 m.

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
";
    }
}
#endif
