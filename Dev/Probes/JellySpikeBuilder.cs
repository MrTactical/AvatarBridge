// The jelly spike: can ChilloutVR run a soft body on an avatar, every frame, and at what cost?
//
// A lattice of particles lives in a float render texture, positions in its left half and
// velocities in its right. Blitters run by a render controller step it in place once a frame
// each: every particle is pulled toward where the rigid fit of its 3x3x3 neighbourhood puts it
// (shape matching), the back face is held on an anchor the material data provider hands in, and
// every player's hands push it out of the way. A mesh draws the lattice's surface from the same
// texture. Nothing is baked, nothing is synced, and each client steps its own copy.
//
//   -executeMethod AvatarBridge.Regression.JellySpikeBuilder.Build
//   -executeMethod AvatarBridge.Regression.JellySpikeBuilder.Bench -jellyOut <dir>
//   -executeMethod AvatarBridge.Regression.JellySpikeBuilder.Verify -jellyOut <dir>
//
// Bench times the step the way the client runs a self-updating blitter (temporary, blit, copy
// back) against a bare copy, for one pair's lattice and four times that, with no players and
// with 64, and checks the tissue settles and swings back sanely. Verify plays the prefab in
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
        const string Folder = "Assets/AvatarBridge_Jelly";
        const string Prefab = Folder + "/Jelly_Spike.prefab";
        const string Scene = Folder + "/Jelly_Preview.unity";
        const string K = "JellySpike.";
        // One pair's tissue is about 3000 particles; this is 3072, 20 cm across at 1.33 cm.
        static readonly Vector3Int Size = new Vector3Int(16, 12, 16);
        const float Spacing = 0.2f / 15f;
        const int Row = 64, Passes = 2, Frames = 300;
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
        static void AddToSelected()
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
            var prefab = Make();
            if (prefab == null) return;
            var root = avatar.transform;
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            // Sized and placed for the avatar's height, so it hangs clear of the chest on anyone.
            float tall = head != null ? Mathf.Max(Vector3.Dot(head.position - root.position, root.up), 0.3f) : 1.5f;
            var made = (GameObject) PrefabUtility.InstantiatePrefab(prefab, chest);
            made.transform.SetPositionAndRotation(chest.position + root.forward * (0.35f * tall / 1.5f), root.rotation);
            made.transform.localScale = Vector3.one * (tall / 1.5f / Mathf.Max(chest.lossyScale.x, 1e-6f));
            Undo.RegisterCreatedObjectUndo(made, "Add the jelly spike");
            Selection.activeGameObject = made;
            Debug.Log($"[Jelly] added under {chest.name}, {0.35f * tall / 1.5f:0.00} m in front, scaled for a {tall:0.00} m head height");
        }

        static GameObject Make()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "AvatarBridge_Jelly");
            File.WriteAllText(Folder + "/JellyStep.shader", StepShader);
            File.WriteAllText(Folder + "/JellyDraw.shader", DrawShader);
            File.WriteAllText(Folder + "/README.md", Readme);
            AssetDatabase.Refresh();
            var step = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/JellyStep.shader");
            var draw = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/JellyDraw.shader");
            if (step == null || draw == null || ShaderUtil.ShaderHasError(step) || ShaderUtil.ShaderHasError(draw))
            {
                Debug.Log("[Jelly] BUILD ABORT: a shader did not import or has errors");
                return null;
            }

            var state = Save(State(Size), "/Jelly_State.renderTexture");
            var stepMat = Save(StepMaterial(step, Size), "/Jelly_Step.mat");
            var drawMat = new Material(draw);
            drawMat.SetTexture("_State", state);
            drawMat = Save(drawMat, "/Jelly_Draw.mat");
            var mesh = Save(Surface(Size), "/Jelly_Surface.asset");

            var root = new GameObject("Jelly Spike");
            var tissue = new GameObject("Tissue");
            tissue.transform.SetParent(root.transform, false);
            var provider = tissue.AddComponent<CVRMaterialDataProvider>();
            provider.materials = new[] { stepMat };
            provider.dataMode = CVRMaterialDataProvider.DataMode.TransformLocalToWorld;
            provider.transforms = new[] { root.transform };
            provider.shaderPropertyName = "_JellyAnchor";
            var controller = tissue.AddComponent<CVRRenderController>();
            // The anchor first, then the passes, in the order the client runs them.
            controller.cvrRenderComponents.Add(provider);
            for (int i = 0; i < Passes; i++)
            {
                var blitter = tissue.AddComponent<CVRBlitter>();
                // The same texture in and out: the client renders into a temporary and copies it
                // back, its own route for a self-update.
                blitter.originTexture = state;
                blitter.destinationTexture = state;
                blitter.blitMaterial = stepMat;
                blitter.destinationTextures = new RenderTexture[0];
                controller.cvrRenderComponents.Add(blitter);
            }
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

        static RenderTexture State(Vector3Int n)
        {
            int count = n.x * n.y * n.z;
            // Float, so positions keep their digits; point and no mips, so a texel is only ever itself.
            return new RenderTexture(2 * Row, (count + Row - 1) / Row, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
            };
        }

        static Material StepMaterial(Shader step, Vector3Int n)
        {
            var m = new Material(step);
            m.SetVector("_Lattice", new Vector4(n.x, n.y, n.z, Spacing));
            m.SetFloat("_Passes", Passes);
            return m;
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

        static T Save<T>(T asset, string file) where T : Object
        {
            AssetDatabase.DeleteAsset(Folder + file);
            AssetDatabase.CreateAsset(asset, Folder + file);
            return AssetDatabase.LoadAssetAtPath<T>(Folder + file);
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
            var step = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/JellyStep.shader");
            var copy = new Material(Shader.Find("Hidden/BlitCopy"));
            var lines = new List<string> { $"[Jelly] bench on {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}" };
            bool ok = true;

            foreach (var n in new[] { Size, new Vector3Int(32, 24, 16) })
                foreach (int players in new[] { 0, 64 })
                {
                    Players(players);
                    var mat = StepMaterial(step, n);
                    mat.SetFloat("_FixedDt", 1f / 90f);
                    mat.SetFloat("_Passes", 1f);
                    var rt = State(n);
                    rt.Create();
                    var anchor = Matrix4x4.TRS(new Vector3(0f, 1.3f, 0.3f), Quaternion.identity, Vector3.one);
                    mat.SetMatrixArray("_JellyAnchor", new[] { anchor });
                    double sim = Timed(rt, mat, 600, out double simCpu);
                    double bare = Timed(rt, copy, 600, out double bareCpu);
                    lines.Add($"[Jelly] {n.x * n.y * n.z} particles, {players} players: a step {sim:0.000} ms a run ({simCpu:0.000} to submit), " +
                              $"a bare copy by the same route {bare:0.000} ({bareCpu:0.000}); the step's own work {sim - bare:0.000} ms");
                    rt.Release();
                }

            // Behaviour, on one pair's lattice at 90 fps with the real two passes a frame: settle
            // under gravity, then the anchor turns 60 degrees in a sixth of a second and holds.
            Players(0);
            var real = StepMaterial(step, Size);
            real.SetFloat("_FixedDt", 1f / 90f);
            var state = State(Size);
            state.Create();
            var view = Viewer(state, out var shots);
            var trace = new List<string>();
            var strip = new List<Texture2D>();
            int[] shotsAt = { 0, 6, 12, 20, 30, 45, 70, 100 };
            Vector3 hang = new Vector3(0f, 1.3f, 0.3f);
            for (int f = -180; f <= 120; f++)
            {
                float turn = f < 0 ? 0f : 60f * Mathf.SmoothStep(0f, 1f, Mathf.Min(f / 15f, 1f));
                var anchor = Matrix4x4.TRS(hang, Quaternion.Euler(0f, turn, 0f), Vector3.one);
                real.SetMatrixArray("_JellyAnchor", new[] { anchor });
                view.transform.SetPositionAndRotation(hang, Quaternion.Euler(0f, turn, 0f));
                for (int p = 0; p < Passes; p++) Run(state, real);
                if (f == -1 || f >= 0 && (f % 5 == 0 || shotsAt.Contains(f)))
                {
                    var s = Read(state);
                    var health = Health(s, Size, anchor, out string said);
                    ok &= health;
                    if (f == -1) lines.Add($"[Jelly] settled after 2 s: {said}");
                    else trace.Add($"{f}: {said}");
                }
                if (f >= 0 && shotsAt.Contains(f)) strip.Add(Shot(shots));
            }
            lines.Add("[Jelly] turned 60 degrees in 15 frames then held, frame: " + string.Join(" | ", trace));
            var sheet = new Texture2D(strip[0].width * 4, strip[0].height * 2, TextureFormat.RGB24, false);
            for (int i = 0; i < strip.Count && i < 8; i++)
                sheet.SetPixels((i % 4) * strip[0].width, (1 - i / 4) * strip[0].height, strip[0].width, strip[0].height, strip[i].GetPixels());
            File.WriteAllBytes(Path.Combine(dir, "turn.png"), sheet.EncodeToPNG());
            lines.Add($"[Jelly] {(ok ? "PASS" : "FAIL")}, frames at {string.Join(", ", shotsAt)} in {Path.Combine(dir, "turn.png")}");
            foreach (var l in lines) Debug.Log(l);
            File.WriteAllLines(Path.Combine(dir, "bench.txt"), lines);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        // Every player's hands somewhere far off, so the step pays for the loop and finds nothing.
        static void Players(int count)
        {
            var far = Enumerable.Repeat(new Vector4(50f, 50f, 50f, 0f), 255).ToArray();
            var root = Enumerable.Repeat(new Vector4(50f, 0f, 50f, 1.6f), 255).ToArray();
            Shader.SetGlobalVectorArray("_CVR_PlayerLeftHandPositions", far);
            Shader.SetGlobalVectorArray("_CVR_PlayerRightHandPositions", far);
            Shader.SetGlobalVectorArray("_CVR_PlayerChestPositions", far);
            Shader.SetGlobalVectorArray("_CVR_PlayerRootPositions", root);
            Shader.SetGlobalVector("CVRGlobalParams1", new Vector4(0f, count, 0f, 0f));
        }

        // The client's self-update, exactly: a temporary like the target, a blit, a copy back.
        static void Run(RenderTexture rt, Material mat)
        {
            var temp = RenderTexture.GetTemporary(rt.descriptor);
            Graphics.Blit(rt, temp, mat);
            Graphics.CopyTexture(temp, rt);
            RenderTexture.ReleaseTemporary(temp);
        }

        // Per run, with the GPU waited on at the end, and the part that is only submitting.
        static double Timed(RenderTexture rt, Material mat, int runs, out double submit)
        {
            for (int i = 0; i < 30; i++) Run(rt, mat);
            AsyncGPUReadback.Request(rt).WaitForCompletion();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < runs; i++) Run(rt, mat);
            submit = clock.Elapsed.TotalMilliseconds / runs;
            AsyncGPUReadback.Request(rt).WaitForCompletion();
            return clock.Elapsed.TotalMilliseconds / runs;
        }

        static Vector4[] Read(RenderTexture rt)
        {
            var req = AsyncGPUReadback.Request(rt);
            req.WaitForCompletion();
            return req.GetData<Vector4>().ToArray();
        }

        // No NaN, the back face on the anchor, how far the front centre sits from where the rigid
        // shape would put it, and the fastest particle.
        static bool Health(Vector4[] s, Vector3Int n, Matrix4x4 anchor, out string said)
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
                        var v = s[(p / Row) * width + Row + p % Row];
                        if (float.IsNaN(x.x + x.y + x.z + v.x + v.y + v.z)) { bad++; continue; }
                        float away = ((Vector3) x - anchor.MultiplyPoint3x4(Rest(i, j, k))).magnitude;
                        if (k == 0) held = Mathf.Max(held, away);
                        if (away > worst) { worst = away; worstAt = new Vector3Int(i, j, k); }
                        fastest = Mathf.Max(fastest, ((Vector3) v).magnitude);
                    }
            int front = ((n.z - 1) * n.y + n.y / 2) * n.x + n.x / 2;
            var tip = (Vector3) s[(front / Row) * width + front % Row];
            var off = tip - anchor.MultiplyPoint3x4(Rest(n.x / 2, n.y / 2, n.z - 1));
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
            bool ok = Health(Read(state), Size, seen, out string said);
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
// One run moves the lattice one pass on. Left half of the texture: positions, w the _Time.y of
// the last pass (negative while no anchor arrives). Right half: velocities, w 1 once started.
Shader ""Hidden/AvatarBridge/JellyStep""
{
    Properties
    {
        _MainTex (""Last pass's state"", 2D) = ""black"" {}
        _Lattice (""Particles across, up and deep; spacing in metres"", Vector) = (16, 12, 16, 0.0133)
        _Stiff (""Pull toward the neighbourhood's shape, a pass"", Range(0, 1)) = 0.5
        _Hz (""Wobble a second, toward its rest on the chest"", Float) = 3
        _Damp (""Damping a second"", Float) = 4
        _Passes (""Passes a frame"", Float) = 2
        _Hand (""Hand radius at a 1.6 m viewpoint"", Float) = 0.05
        _FixedDt (""Fixed frame time, 0 for the real one"", Float) = 0
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

            Texture2D<float4> _MainTex;
            float4 _Lattice;
            float _Stiff, _Hz, _Damp, _Passes, _Hand, _FixedDt;
            float4x4 _JellyAnchor[1];
            float4 _CVR_PlayerLeftHandPositions[255];
            float4 _CVR_PlayerRightHandPositions[255];
            float4 _CVR_PlayerChestPositions[255];
            float4 _CVR_PlayerRootPositions[255];
            float4 CVRGlobalParams1;

            static const int Row = 64;
            static const float3 Gravity = float3(0, -9.81, 0);

            int3 Count() { return int3(_Lattice.xyz); }
            int Index(int3 l) { int3 n = Count(); return (l.z * n.y + l.y) * n.x + l.x; }
            int3 Place(int p) { int3 n = Count(); return int3(p % n.x, (p / n.x) % n.y, p / (n.x * n.y)); }
            int3 Texel(int p, int half_) { return int3(p % Row + half_ * Row, p / Row, 0); }
            float3 Rest(int3 l) { int3 n = Count(); return float3(l.x - 0.5 * (n.x - 1), l.y - 0.5 * (n.y - 1), l.z) * _Lattice.w; }

            // Where particle l would go this pass with nothing pulling it; the back face rides the anchor.
            float3 Ahead(int3 l, float4x4 m, float h)
            {
                if (l.z == 0) return mul(m, float4(Rest(l), 1)).xyz;
                int p = Index(l);
                return _MainTex.Load(Texel(p, 0)).xyz + _MainTex.Load(Texel(p, 1)).xyz * h + Gravity * h * h;
            }

            float4 Mul(float4 a, float4 b) { return float4(a.w * b.xyz + b.w * a.xyz + cross(a.xyz, b.xyz), a.w * b.w - dot(a.xyz, b.xyz)); }

            float3x3 Turn(float4 q)
            {
                float x = q.x, y = q.y, z = q.z, w = q.w;
                return float3x3(1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w),
                                2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w),
                                2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y));
            }

            float4 Quat(float3x3 m)
            {
                float t = m[0][0] + m[1][1] + m[2][2];
                float4 q;
                if (t > 0) { float s = sqrt(t + 1) * 2; q = float4((m[2][1] - m[1][2]) / s, (m[0][2] - m[2][0]) / s, (m[1][0] - m[0][1]) / s, 0.25 * s); }
                else if (m[0][0] > m[1][1] && m[0][0] > m[2][2]) { float s = sqrt(1 + m[0][0] - m[1][1] - m[2][2]) * 2; q = float4(0.25 * s, (m[0][1] + m[1][0]) / s, (m[0][2] + m[2][0]) / s, (m[2][1] - m[1][2]) / s); }
                else if (m[1][1] > m[2][2]) { float s = sqrt(1 + m[1][1] - m[0][0] - m[2][2]) * 2; q = float4((m[0][1] + m[1][0]) / s, 0.25 * s, (m[1][2] + m[2][1]) / s, (m[0][2] - m[2][0]) / s); }
                else { float s = sqrt(1 + m[2][2] - m[0][0] - m[1][1]) * 2; q = float4((m[0][2] + m[2][0]) / s, (m[1][2] + m[2][1]) / s, 0.25 * s, (m[1][0] - m[0][1]) / s); }
                return normalize(q);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                int2 px = int2(i.pos.xy);
                int half_ = px.x / Row;
                int p = px.x % Row + px.y * Row;
                int3 n = Count();
                if (p >= n.x * n.y * n.z) return 0;
                int3 l = Place(p);
                float4x4 m = _JellyAnchor[0];
                float scale = length(m._m00_m10_m20);
                // No anchor yet: nothing to hang from. Marked, so the surface can say so.
                if (!(scale > 1e-6)) return half_ == 0 ? float4(0, 0, 0, -1) : 0;
                float3 home = mul(m, float4(Rest(l), 1)).xyz;
                float4 s = _MainTex.Load(Texel(p, 0));
                float4 u = _MainTex.Load(Texel(p, 1));
                // A fresh texture, a new copy of the avatar, garbage, or a teleport: start at rest.
                bool fresh = !(u.w == 1) || !(abs(s.x) + abs(s.y) + abs(s.z) + abs(u.x) + abs(u.y) + abs(u.z) < 1e6)
                    || !(distance(s.xyz, home) < 1);
                if (fresh) return half_ == 0 ? float4(home, _Time.y) : float4(0, 0, 0, 1);

                float h = (_FixedDt > 0 ? _FixedDt : clamp(unity_DeltaTime.x, 1.0 / 240, 1.0 / 30)) / max(_Passes, 1);
                float3 x = s.xyz;
                float3 ahead = Ahead(l, m, h);
                float3 next = ahead;
                if (l.z > 0)
                {
                    // The neighbourhood's best rigid fit, from where each of its particles is headed.
                    float3 c = 0, rc = 0;
                    float k = 0;
                    [unroll] for (int dz = -1; dz <= 1; dz++)
                    [unroll] for (int dy = -1; dy <= 1; dy++)
                    [unroll] for (int dx = -1; dx <= 1; dx++)
                    {
                        int3 o = l + int3(dx, dy, dz);
                        if (any(o < 0) || any(o >= n)) continue;
                        c += Ahead(o, m, h);
                        rc += Rest(o);
                        k += 1;
                    }
                    c /= k;
                    rc /= k;
                    float3x3 a = 0;
                    [unroll] for (int ez = -1; ez <= 1; ez++)
                    [unroll] for (int ey = -1; ey <= 1; ey++)
                    [unroll] for (int ex = -1; ex <= 1; ex++)
                    {
                        int3 o = l + int3(ex, ey, ez);
                        if (any(o < 0) || any(o >= n)) continue;
                        float3 d = Ahead(o, m, h) - c, r = Rest(o) - rc;
                        a += float3x3(d.x * r, d.y * r, d.z * r);
                    }
                    // Its rotation (Mueller and others, 2016), started from the anchor's.
                    float4 q = Quat((float3x3) m / scale);
                    [unroll] for (int it = 0; it < 6; it++)
                    {
                        float3x3 t = Turn(q);
                        float3 t0 = float3(t[0][0], t[1][0], t[2][0]), t1 = float3(t[0][1], t[1][1], t[2][1]), t2 = float3(t[0][2], t[1][2], t[2][2]);
                        float3 a0 = float3(a[0][0], a[1][0], a[2][0]), a1 = float3(a[0][1], a[1][1], a[2][1]), a2 = float3(a[0][2], a[1][2], a[2][2]);
                        float3 spin = (cross(t0, a0) + cross(t1, a1) + cross(t2, a2)) / (abs(dot(t0, a0) + dot(t1, a1) + dot(t2, a2)) + 1e-12);
                        float w = length(spin);
                        if (w > 1e-9) q = normalize(Mul(float4(spin / w * sin(0.5 * w), cos(0.5 * w)), q));
                    }
                    float3 goal = mul(Turn(q), (Rest(l) - rc) * scale) + c;
                    next = lerp(ahead, goal, _Stiff);
                    // And the whole of it toward its rest on the anchor, as a spring of _Hz: the
                    // neighbourhoods alone keep the shape near each particle but let a block 16
                    // across fold under its own weight, 42 cm at the front.
                    float spring = 6.2831853 * _Hz * h;
                    next = lerp(next, home, min(spring * spring, 1));

                    // Every player's hands, as the squash reads them: from the wrist, away from that
                    // player's chest, a ninth of their viewpoint height. Last, so a loop cut short
                    // by Shader Filtering only loses hands.
                    int hands = 2 * min((int) round(CVRGlobalParams1.y), 64);
                    [loop] for (int e = 0; e < hands; e++)
                    {
                        int who = e >> 1;
                        float3 wrist = (e & 1) == 0 ? _CVR_PlayerLeftHandPositions[who].xyz : _CVR_PlayerRightHandPositions[who].xyz;
                        if (dot(wrist, wrist) < 1e-6) continue;
                        float tall = _CVR_PlayerRootPositions[who].w;
                        tall = tall > 0.1 ? tall : 1.6;
                        float r = _Hand * tall / 1.6;
                        float3 arm = wrist - _CVR_PlayerChestPositions[who].xyz;
                        float3 tip = wrist + (dot(arm, arm) > 1e-6 ? normalize(arm) : 0) * max(0.11 * tall - r, 0);
                        float3 seg = tip - wrist;
                        float3 closest = wrist + seg * saturate(dot(next - wrist, seg) / max(dot(seg, seg), 1e-9));
                        float3 off = next - closest;
                        float d2 = dot(off, off);
                        if (d2 < r * r && d2 > 1e-12) next = closest + off * (r * rsqrt(d2));
                    }
                }
                if (half_ == 0) return float4(next, _Time.y);
                return float4((next - x) / h * exp(-_Damp * h), 1);
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
render texture that two blitters step every frame; nothing is baked and nothing is synced. It
should sag a little, wobble when you turn or bounce, and dent and jiggle when any hand pokes it,
yours or another player's.

## Setting it up

1. Select the avatar in the scene and run Tools > AvatarBridge Dev > Jelly spike: add to the
   selected avatar. It goes under the chest bone, in front of the chest, sized for the avatar.
   Move it if it sits inside anything.
2. Upload. Nothing else: no parameters, no menu, no sync.

Before uploading, `Jelly_Preview.unity` shows it in the editor: open it and press Play.

## What the colour says

| Colour | Means |
| --- | --- |
| Pink | The tissue is stepping |
| Red | It has not stepped for a quarter second: the blitters are not running for this copy |
| Purple | The anchor never arrived: the material data provider is not running |

## What to try

1. Wear it. Turn quickly, jump, poke it with either hand. Does it wobble and dent, and settle?
2. Your frame time with it worn against another avatar.
3. Have somebody look at you, or watch from a second client: pink and wobbling for them too?
   Can they poke it?
4. Switch to another avatar and back: pink again within a second?
5. In VR: both eyes the same?
6. Optional: somebody with the Cameras content filter off should see it red, frozen.
";
    }
}
#endif
