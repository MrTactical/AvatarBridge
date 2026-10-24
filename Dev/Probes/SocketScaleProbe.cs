#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarBridge.Regression
{
    // Does a socket mesh open to the same shape on a resized avatar? The opening is baked shape
    // deltas added in the vertex stage, and a skinned mesh reaches that stage in world space, so
    // deltas baked at one size may stay that size at any other. The socket slot is drawn through
    // Hidden/YapsBendCapture with the contact channel's depth forced, closed then open, at each
    // scale, and both the mesh and its opening measured in the avatar's own units. Then the
    // avatar turned a quarter round: an opening that turns with it moves each vertex the same
    // way, turned. One scale per process, -sockScale: re-scaling the root in the same process
    // left the skin at the first size (measured: the mesh read 1.606 m across at all three),
    // so compare each run's "avatar units" against a run at 1.
    //
    // Run: -executeMethod AvatarBridge.Regression.SocketScaleProbe.Run [-sockAvatar <prefab path>]
    //      [-sockScale <s>] [-sockMaterial <baked socket material>]  put back on the slot it was baked from, for an
    //                                               avatar whose socket bake was later taken off
    public static class SocketScaleProbe
    {
        const string DefaultAvatar = "Assets/AvatarBridgeOutput/SootiePoopie SPS Version/SootiePoopie SPS Version (ChilloutVR).prefab";

        public static void Run()
        {
            void Log(string s) => Debug.Log("[SocketScale] " + s);
            string F(float x) => x.ToString("0.0000", CultureInfo.InvariantCulture);
            int code = 1;
            var cleanup = new List<Action>();
            try
            {
                string path = Arg("-sockAvatar") ?? DefaultAvatar;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var shader = Shader.Find("Hidden/YapsBendCapture");
                if (prefab == null) throw new Exception("no prefab at " + path);
                if (shader == null || !shader.isSupported) throw new Exception("Hidden/YapsBendCapture did not compile");

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var avatar = (GameObject) PrefabUtility.InstantiatePrefab(prefab);
                avatar.transform.position = Vector3.zero;
                if (Arg("-sockMaterial") is string matPath)
                {
                    var baked = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (!IsSocket(baked)) throw new Exception("no baked socket material at " + matPath);
                    string from = baked.name.Replace("_YAPS_", "").Trim();
                    int count = Mathf.RoundToInt(baked.GetFloat("_YAPS_VertexCount"));
                    foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
                    {
                        // Not ?.: a missing MeshFilter is Unity's fake null.
                        var mf = r.GetComponent<MeshFilter>();
                        var m = r is SkinnedMeshRenderer sr ? sr.sharedMesh : mf != null ? mf.sharedMesh : null;
                        var slots = r.sharedMaterials;
                        int at = Array.FindIndex(slots, x => x != null && x.name == from);
                        if (m == null || at < 0 || m.vertexCount != count) continue;
                        slots[at] = baked;
                        r.sharedMaterials = slots;
                        Log($"\"{baked.name}\" put back on \"{r.name}\" slot {at}");
                        break;
                    }
                }
                var socket = avatar.GetComponentsInChildren<Renderer>(true).FirstOrDefault(x => x.sharedMaterials.Any(IsSocket));
                if (socket == null) throw new Exception("no socket mesh on " + path);
                for (var t = socket.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
                if (socket is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
                // Nothing else drawn and no marker light anywhere: the depth is the channel's alone.
                foreach (var other in avatar.GetComponentsInChildren<Renderer>(true)) if (other != socket) other.enabled = false;
                foreach (var light in avatar.GetComponentsInChildren<Light>(true)) light.enabled = false;

                // A copy on the capture shader carrying the slot's whole YAPS block, so the
                // converted avatar's own material is never written.
                var mats = socket.sharedMaterials;
                int slot = Array.FindIndex(mats, IsSocket);
                var src = mats[slot];
                var capture = new Material(shader) { name = src.name + " (capture)" };
                for (int i = 0; i < src.shader.GetPropertyCount(); i++)
                {
                    string name = src.shader.GetPropertyName(i);
                    if (!name.StartsWith("_YAPS_", StringComparison.Ordinal)) continue;
                    switch (src.shader.GetPropertyType(i))
                    {
                        case ShaderPropertyType.Texture: capture.SetTexture(name, src.GetTexture(name)); break;
                        case ShaderPropertyType.Vector:
                        case ShaderPropertyType.Color: capture.SetVector(name, src.GetVector(name)); break;
                        default: capture.SetFloat(name, src.GetFloat(name)); break;
                    }
                }
                capture.SetFloat("_YapsCaptureOn", 1f);
                mats[slot] = capture;
                socket.sharedMaterials = mats;
                var mesh = socket is SkinnedMeshRenderer s ? s.sharedMesh : socket.GetComponent<MeshFilter>().sharedMesh;
                int n = mesh.vertexCount;
                Log($"avatar {path}; socket \"{socket.name}\" ({socket.GetType().Name}), slot {slot}, {n} vertices, " +
                    $"{capture.GetFloat("_YAPS_ShapeCount"):0} shapes");

                var camGo = new GameObject("__Camera");
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.fieldOfView = 100f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 100f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.renderingPath = RenderingPath.Forward;
                var rt = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                var buffer = new ComputeBuffer(n, 16);
                cleanup.Add(() => { cam.targetTexture = null; rt.Release(); buffer.Release(); });
                var zero = new Vector4[n];

                Vector4[] Capture(float depth)
                {
                    capture.SetFloat("_YAPS_SocketDepth", depth);
                    buffer.SetData(zero);
                    Graphics.ClearRandomWriteTargets();
                    Graphics.SetRandomWriteTarget(1, buffer, false);
                    cam.Render();
                    Graphics.ClearRandomWriteTargets();
                    var got = new Vector4[n];
                    buffer.GetData(got);
                    return got;
                }

                float scale = float.TryParse(Arg("-sockScale"), NumberStyles.Float, CultureInfo.InvariantCulture, out float sa) ? sa : 1f;
                bool ok = true;
                {
                    avatar.transform.localScale = Vector3.one * scale;
                    // Twice: the first render re-skins at the new size and moves the bounds.
                    cam.transform.position = socket.bounds.center + Vector3.back * (3f * socket.bounds.extents.magnitude);
                    cam.transform.LookAt(socket.bounds.center);
                    Capture(-1f);
                    cam.transform.position = socket.bounds.center + Vector3.back * (3f * socket.bounds.extents.magnitude);
                    cam.transform.LookAt(socket.bounds.center);
                    var closed = Capture(-1f);
                    var open = Capture(1f);

                    var drawn = Enumerable.Range(0, n).Where(i => closed[i].w > 0.5f && open[i].w > 0.5f).ToList();
                    if (drawn.Count == 0) throw new Exception($"nothing captured at scale {scale}");
                    var b = new Bounds(closed[drawn[0]], Vector3.zero);
                    foreach (int i in drawn) b.Encapsulate(closed[i]);
                    float worst = 0f, sum = 0f;
                    int moved = 0;
                    foreach (int i in drawn)
                    {
                        float d = Vector3.Distance(open[i], closed[i]);
                        worst = Mathf.Max(worst, d);
                        if (d > 1e-5f) { moved++; sum += d; }
                    }
                    // In the avatar's units: a socket that scales with its avatar reads the same at every size.
                    float size = b.size.magnitude / scale, opening = worst / scale;
                    ok &= moved > 0;
                    Log($"scale {scale}: mesh {F(size)} avatar units across, {drawn.Count} drawn; " +
                        $"opening widest {F(opening)} avatar units, {moved} vertices move, mean {F(moved > 0 ? sum / moved / scale : 0f)}");
                }

                // A quarter turn about up: each vertex's move against its unturned move, turned.
                Vector3[] Moves()
                {
                    cam.transform.position = socket.bounds.center + Vector3.back * (3f * socket.bounds.extents.magnitude);
                    cam.transform.LookAt(socket.bounds.center);
                    Capture(-1f);
                    var closed = Capture(-1f);
                    var open = Capture(1f);
                    return Enumerable.Range(0, n).Select(i => closed[i].w > 0.5f && open[i].w > 0.5f ? (Vector3) (open[i] - closed[i]) : Vector3.zero).ToArray();
                }
                avatar.transform.rotation = Quaternion.identity;
                var straight = Moves();
                var quarter = Quaternion.AngleAxis(90f, Vector3.up);
                avatar.transform.rotation = quarter;
                var turned = Moves();
                avatar.transform.rotation = Quaternion.identity;
                float worstAngle = 0f, angleSum = 0f;
                int counted = 0;
                for (int i = 0; i < n; i++)
                {
                    if (straight[i].magnitude < 1e-4f || turned[i].magnitude < 1e-4f) continue;
                    float a = Vector3.Angle(quarter * straight[i], turned[i]);
                    worstAngle = Mathf.Max(worstAngle, a);
                    angleSum += a;
                    counted++;
                }
                bool turns = counted > 0 && angleSum / counted < 1f;
                ok &= turns;
                Log($"{(turns ? "PASS" : "FAIL")} turned 90°: {counted} vertices compared, their move off the turned unturned move by " +
                    $"{(counted > 0 ? angleSum / counted : 0f):0.0}° on average, {worstAngle:0.0}° at worst");
                code = ok ? 0 : 1;
            }
            catch (Exception e) { Debug.LogException(e); code = 2; }
            finally { foreach (var c in cleanup) c(); }
            Log(code == 0 ? "RESULT PASS" : "RESULT FAIL");
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static bool IsSocket(Material m) =>
            m != null && m.HasProperty("_YAPS_SocketPower") && m.GetFloat("_YAPS_SocketPower") > 0f;

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
#endif
