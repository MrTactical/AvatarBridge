#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarBridge.Regression
{
    // The runtime tester, step two: a real converted avatar, its plug drawn
    // through Hidden/YapsBendCapture, and the bend read back as numbers.
    //   1. No socket in reach: no vertex moves from where skinning put it.
    //   2. A socket anywhere round the tip is found, on an HDR and an 8-bit camera.
    // Which route found it comes from the shipped "Resolved by" view, which
    // straightens the plug to a quarter, half, three quarters or all of its
    // length, so no resolve code is copied here to drift from the shader's.
    //
    // Run: -executeMethod AvatarBridge.Regression.YapsBendTester.Run [-yapsAvatar <prefab path>]
    public static class YapsBendTester
    {
        const string DefaultAvatar = "Assets/AvatarBridgeOutput/Alexa/Alexa (ChilloutVR).prefab";
        static readonly string[] Tiers = { "nobody", "preview", "light", "atlas" };

        public static void Run()
        {
            int fail = 0;
            void Log(string s) => Debug.Log("[YapsBendTester] " + s);
            int Check(string label, bool ok) { Log($"{(ok ? "ok  " : "FAIL")} {label}"); return ok ? 0 : 1; }
            var cleanup = new List<Action>();
            try
            {
                string path = Arg("-yapsAvatar") ?? DefaultAvatar;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var shader = Shader.Find("Hidden/YapsBendCapture");
                if (prefab == null) throw new Exception("no prefab at " + path);
                if (shader == null || !shader.isSupported) throw new Exception("Hidden/YapsBendCapture did not compile");

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var avatar = (GameObject) PrefabUtility.InstantiatePrefab(prefab);
                avatar.transform.position = Vector3.zero;
                Log($"avatar {path}");

                var plug = avatar.GetComponentsInChildren<YapsPlug>(true).FirstOrDefault(p => p.Target is SkinnedMeshRenderer);
                var skin = plug != null ? (SkinnedMeshRenderer) plug.Target
                    : avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.sharedMaterials.Any(IsPlug));
                if (skin == null) throw new Exception("no skinned plug on the avatar");
                // Plugs often ship switched off and a toggle brings them in.
                for (var t = skin.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
                skin.updateWhenOffscreen = true;
                var marker = avatar.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "YAPS Markers")
                             ?? (plug != null ? plug.transform : skin.transform);
                Vector3 root = marker.position, fwd = marker.forward, up = marker.up;

                // Every plug slot onto the capture shader, carrying the patched
                // material's whole YAPS block by name.
                var mats = skin.sharedMaterials;
                var capture = new List<(Material mat, float count)>();
                float length = 0f;
                for (int s = 0; s < mats.Length; s++)
                {
                    if (!IsPlug(mats[s])) continue;
                    var src = mats[s];
                    var h = new Material(shader) { name = src.name + " (capture)" };
                    for (int i = 0; i < src.shader.GetPropertyCount(); i++)
                    {
                        string name = src.shader.GetPropertyName(i);
                        if (!name.StartsWith("_YAPS_", StringComparison.Ordinal)) continue;
                        switch (src.shader.GetPropertyType(i))
                        {
                            case ShaderPropertyType.Texture: h.SetTexture(name, src.GetTexture(name)); break;
                            case ShaderPropertyType.Vector:
                            case ShaderPropertyType.Color: h.SetVector(name, src.GetVector(name)); break;
                            default: h.SetFloat(name, src.GetFloat(name)); break;
                        }
                    }
                    h.SetFloat("_YAPS_Enabled", 1f);
                    capture.Add((h, h.GetFloat("_YAPS_VertexCount")));
                    length = Mathf.Max(length, src.GetFloat("_YAPS_Length"));
                    mats[s] = h;
                }
                skin.sharedMaterials = mats;
                Log($"plug \"{skin.name}\", {capture.Count} slot(s), length {length:0.000} m, " +
                    $"{skin.sharedMesh.vertexCount} vertices");

                var own = avatar.GetComponentsInChildren<YapsSocket>(true).Where(s => s.gameObject.activeSelf).ToList();
                foreach (var s in own) s.gameObject.SetActive(false);

                // Off to the side at 2.5 lengths, wide, so the plug and every
                // test place are in frame: a plug culled out never draws.
                Camera MakeCamera(string name, bool halfFloat)
                {
                    var go = new GameObject("__Camera " + name);
                    var cam = go.AddComponent<Camera>();
                    var rt = new RenderTexture(1024, 1024, 24, halfFloat ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32);
                    cam.targetTexture = rt;
                    cam.allowHDR = halfFloat;
                    cam.allowMSAA = false;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = Color.black;
                    cam.fieldOfView = 100f;
                    cam.nearClipPlane = 0.01f;
                    var mid = root + fwd * (0.5f * length);
                    var side = Vector3.Cross(fwd, up).normalized;
                    go.transform.position = mid + side * (2.5f * length) + up * (0.5f * length);
                    go.transform.LookAt(mid, up);
                    cleanup.Add(() => { cam.targetTexture = null; rt.Release(); });
                    return cam;
                }
                var hdr = MakeCamera("HDR", true);
                var ldr = MakeCamera("8-bit", false);

                int n = skin.sharedMesh.vertexCount;
                var buffer = new ComputeBuffer(n, 16);
                cleanup.Add(buffer.Release);
                var zero = new Vector4[n];

                // yapsOff: the vertex count set to 0, so the deform returns at
                // once and the capture is skinning alone, from the same pipeline.
                Vector4[] Capture(Camera cam, float debug, bool yapsOff = false)
                {
                    foreach (var (mat, count) in capture)
                    {
                        mat.SetFloat("_YAPS_Debug", debug);
                        mat.SetFloat("_YAPS_VertexCount", yapsOff ? 0f : count);
                        mat.SetFloat("_YapsCaptureOn", 1f);
                    }
                    YapsOwnerStandIn.Apply();
                    buffer.SetData(zero);
                    Graphics.ClearRandomWriteTargets();
                    Graphics.SetRandomWriteTarget(1, buffer, false);
                    cam.Render();
                    Graphics.ClearRandomWriteTargets();
                    var got = new Vector4[n];
                    buffer.GetData(got);
                    return got;
                }

                var rest = Capture(hdr, 0f, yapsOff: true);
                int written = rest.Count(v => v.w > 0.5f);
                fail += Check($"the plug drew and came back ({written} of {n} vertices)", written > 0);
                if (written == 0) throw new Exception("nothing captured: the plug is not being drawn");

                float Moved(Vector4[] got)
                {
                    float worst = 0f;
                    for (int i = 0; i < n; i++)
                        if (got[i].w > 0.5f && rest[i].w > 0.5f) worst = Mathf.Max(worst, Vector3.Distance(got[i], rest[i]));
                    return worst;
                }
                // 1. Nothing in reach: no bend at all.
                float moved = Moved(Capture(hdr, 0f));
                fail += Check($"no socket in reach, no bend (worst vertex {moved * 1000f:0.00} mm)", moved < 0.5e-3f);

                // A view draws each vertex at root + side + forward * z * shown.
                // With nothing resolved two views are known: the gap view reads
                // all of the length, "Resolved by" a quarter. That pins the root
                // and the forward step of every vertex, and any capture after
                // gives shown back. An extent measured along the marker's axis
                // did not: the plug's width leaked in and a full length read 0.33.
                var full = Capture(hdr, 2f);
                var quarter = Capture(hdr, 1f);
                var step = new Vector3[n];
                var origin = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    step[i] = ((Vector3) full[i] - (Vector3) quarter[i]) / 0.75f;
                    origin[i] = (Vector3) quarter[i] - 0.25f * step[i];
                }
                // The far tenth of the shaft, where the step is longest.
                float longest = step.Max(s => s.magnitude);
                var far = Enumerable.Range(0, n)
                    .Where(i => full[i].w > 0.5f && step[i].magnitude > 0.9f * longest).ToArray();
                float Shown(Vector4[] got)
                {
                    var each = far.Where(i => got[i].w > 0.5f)
                        .Select(i => Vector3.Dot((Vector3) got[i] - origin[i], step[i]) / step[i].sqrMagnitude)
                        .OrderBy(v => v).ToArray();
                    return each.Length > 0 ? each[each.Length / 2] : -1f;
                }
                Log($"calibrated on {far.Length} vertices, longest step {longest:0.000} m; " +
                    $"check: gap view reads {Shown(Capture(hdr, 2f)):0.00} (want 1.00)");
                // A quarter, half, three quarters or all of the length.
                int TierOf(Vector4[] got) => Mathf.Clamp(Mathf.RoundToInt(Shown(got) / 0.25f) - 1, 0, 3);

                // The avatar's own sockets back on: whatever it does, it is
                // what the game would do with them, owner stand-in and all.
                foreach (var s in own) s.gameObject.SetActive(true);
                var withOwn = Capture(hdr, 1f);
                Log($"info: {own.Count} own socket(s) switched on: resolved by {Tiers[TierOf(withOwn)]}, " +
                    $"worst vertex {Moved(Capture(hdr, 0f)) * 1000f:0.0} mm");
                foreach (var s in own) s.gameObject.SetActive(false);

                // 2. A socket found anywhere round the tip. Atlas only: a light
                // covers for whatever else is broken.
                var hole = YapsSocketBuilder.BuildPreviewSocket("__TestHole", YapsSocket.SocketKind.Hole, withLights: false);
                YapsAtlas.AddGrab(hole.transform);
                YapsAtlas.AddClear(hole.transform);
                var places = new List<(float angle, float turn, float reach)>();
                foreach (float reach in new[] { 0.8f, 1.1f })
                {
                    places.Add((0f, 0f, reach));
                    foreach (float angle in new[] { 30f, 60f, 90f })
                        for (float turn = 0f; turn < 360f; turn += 45f) places.Add((angle, turn, reach));
                }
                foreach (var cam in new[] { hdr, ldr })
                {
                    var counts = new int[4];
                    var missed = new List<string>();
                    var firstMiss = ((float, float, float)?) null;
                    foreach (var (angle, turn, reach) in places)
                    {
                        var dir = Quaternion.AngleAxis(turn, fwd) * (Quaternion.AngleAxis(angle, up) * fwd);
                        var at = root + dir * (reach * length);
                        var roll = Mathf.Abs(Vector3.Dot(dir, up)) > 0.99f ? fwd : up;
                        hole.transform.SetPositionAndRotation(at, Quaternion.LookRotation(root - at, roll));
                        int tier = TierOf(Capture(cam, 1f));
                        counts[tier]++;
                        if (tier != 3 && firstMiss == null) firstMiss = (angle, turn, reach);
                        if (tier != 3) missed.Add($"{angle:0}°/{turn:0}° at {reach:0.0}L: {Tiers[tier]}");
                    }
                    Log($"{cam.name}: {places.Count} places, atlas {counts[3]}, light {counts[2]}, " +
                        $"preview {counts[1]}, nobody {counts[0]}");
                    foreach (var m in missed.Take(12)) Log($"  missed {m}");
                    // The first miss through the shipped atlas views, each a
                    // length: target 0.1 too small, 0.4 another screen, 0.7 no
                    // cell, 1 live; taps 0.1 no cell, 0.33 tag refused, 0.66
                    // thrown out, 1 a socket.
                    if (missed.Count > 0)
                    {
                        var (angle, turn, reach) = firstMiss.Value;
                        var dir = Quaternion.AngleAxis(turn, fwd) * (Quaternion.AngleAxis(angle, up) * fwd);
                        var at = root + dir * (reach * length);
                        hole.transform.SetPositionAndRotation(at, Quaternion.LookRotation(root - at, up));
                        float ShownIn(float view) => Shown(Capture(cam, view));
                        Log($"  first miss: atlas target {ShownIn(6f):0.00}, atlas taps {ShownIn(5f):0.00}, " +
                            $"gap {ShownIn(2f):0.00}, use atlas {capture[0].mat.GetFloat("_YAPS_UseAtlas")}, " +
                            $"grab {Shader.GetGlobalTexture("_YAPS_Atlas")?.width}x{Shader.GetGlobalTexture("_YAPS_Atlas")?.height}");
                    }
                    fail += Check($"{cam.name}: a socket anywhere round the tip is found by the atlas", missed.Count == 0);
                }
                UnityEngine.Object.DestroyImmediate(hole);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                fail++;
            }
            finally
            {
                foreach (var c in cleanup) c();
            }

            Log(fail == 0 ? "PASS" : $"FAIL: {fail} check(s)");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        // A plug's slot, not a socket's: the patcher emits both ends into
        // every material and the properties say which it is.
        static bool IsPlug(Material m)
        {
            if (m == null || m.shader == null) return false;
            if (m.shader.FindPropertyIndex("_YAPS_VertexCount") < 0 || m.GetFloat("_YAPS_VertexCount") <= 0f) return false;
            return m.shader.FindPropertyIndex("_YAPS_SocketPower") < 0 || m.GetFloat("_YAPS_SocketPower") <= 0f;
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
