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
    //   3. Of two sockets on one line, the nearer is taken.
    //   4. The mesh holds together while a socket walks in: seams, edges, pops.
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

                // Every numbered writer material made before the capture
                // materials exist: a material asset created after them left
                // every later capture reading nothing, twice.
                foreach (var (kind, oneWay) in new[] { (YapsSocket.SocketKind.Hole, false), (YapsSocket.SocketKind.Ring, false), (YapsSocket.SocketKind.Ring, true) })
                {
                    var warm = MakeSocket("__Warm", kind, oneWay, true, false);
                    foreach (var r in warm.GetComponentsInChildren<Renderer>(true))
                        for (int number = 1; number <= YapsOwner.MaxSelfSockets; number++) YapsAtlas.Indexed(r.sharedMaterial, number);
                    UnityEngine.Object.DestroyImmediate(warm);
                }

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
                    cam.renderingPath = RenderingPath.Forward;
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

                // 2. A socket found anywhere round the tip, for every kind a
                // plug meets: YAPS sockets by the atlas, with and without the
                // lights it must answer before, and DPS-style sockets by their
                // marker lights alone. A contact-only socket (older TPS) has
                // nothing a shader can read, so there is nothing to find.
                var plumbing = new GameObject("__Atlas");
                YapsAtlas.AddGrab(plumbing.transform);
                YapsAtlas.AddClear(plumbing.transform);
                var places = new List<(float angle, float turn, float reach)>();
                foreach (float reach in new[] { 0.8f, 1.1f })
                {
                    places.Add((0f, 0f, reach));
                    foreach (float angle in new[] { 30f, 60f, 90f })
                        for (float turn = 0f; turn < 360f; turn += 45f) places.Add((angle, turn, reach));
                }
                // nearer: metres short of the place, along the same line.
                void Aim(Transform t, (float angle, float turn, float reach) p, float nearer = 0f)
                {
                    var dir = Quaternion.AngleAxis(p.turn, fwd) * (Quaternion.AngleAxis(p.angle, up) * fwd);
                    var at = root + dir * (p.reach * length - nearer);
                    var roll = Mathf.Abs(Vector3.Dot(dir, up)) > 0.99f ? fwd : up;
                    t.SetPositionAndRotation(at, Quaternion.LookRotation(root - at, roll));
                }
                string Where((float angle, float turn, float reach) p) => $"{p.angle:0}°/{p.turn:0}° at {p.reach:0.0}L";

                var kinds = new (string label, YapsSocket.SocketKind kind, bool oneWay, bool atlas, bool lights, int want)[]
                {
                    ("YAPS hole, atlas only", YapsSocket.SocketKind.Hole, false, true, false, 3),
                    ("YAPS ring, atlas only", YapsSocket.SocketKind.Ring, false, true, false, 3),
                    ("YAPS one-way ring, atlas only", YapsSocket.SocketKind.Ring, true, true, false, 3),
                    ("YAPS hole, atlas and lights", YapsSocket.SocketKind.Hole, false, true, true, 3),
                    ("DPS hole, lights only", YapsSocket.SocketKind.Hole, false, false, true, 2),
                    ("DPS ring, lights only", YapsSocket.SocketKind.Ring, false, false, true, 2),
                };
                foreach (var k in kinds)
                {
                    var sock = MakeSocket("__Test " + k.label, k.kind, k.oneWay, k.atlas, k.lights);
                    Log($"{k.label}: {sock.GetComponentsInChildren<Light>(false).Length} marker light(s) on, " +
                        $"{sock.GetComponentsInChildren<Renderer>(true).Count(r => YapsAtlas.IsPlumbing(r.sharedMaterial))} atlas renderer(s)");
                    foreach (var cam in new[] { hdr, ldr })
                    {
                        var counts = new int[4];
                        var missed = new List<(float, float, float)>();
                        int unlit = 0;
                        foreach (var p in places)
                        {
                            Aim(sock.transform, p);
                            int tier = TierOf(Capture(cam, 1f));
                            counts[tier]++;
                            if (tier == k.want) continue;
                            // A marker light reaches a renderer only when its
                            // range meets the renderer's bounds, in game too,
                            // and the DPS range is the message, so past that
                            // is past the light tier's reach, not a miss.
                            if (!k.atlas && !RootLightMeets(sock, skin)) unlit++;
                            else missed.Add(p);
                        }
                        Log($"{k.label}, {cam.name}: atlas {counts[3]}, light {counts[2]}, preview {counts[1]}, " +
                            $"nobody {counts[0]} of {places.Count}");
                        foreach (var m in missed.Take(6)) Log($"  missed {Where(m)}");
                        if (unlit > 0) Log($"  {unlit} place(s) past the root light's range from the plug's bounds");
                        // The first miss through the shipped atlas views: target
                        // 0.1 too small, 0.4 another screen, 0.7 no cell, 1 live;
                        // taps 0.1 no cell, 0.33 tag refused, 0.66 thrown out, 1 found.
                        if (missed.Count > 0 && k.atlas)
                        {
                            Aim(sock.transform, missed[0]);
                            float ShownIn(float view) => Shown(Capture(cam, view));
                            Log($"  first miss: atlas target {ShownIn(6f):0.00}, taps {ShownIn(5f):0.00}, gap {ShownIn(2f):0.00}");
                        }
                        fail += Check($"{k.label}, {cam.name}: found by the {Tiers[k.want]} at all {places.Count} places",
                            missed.Count == 0);
                    }
                    UnityEngine.Object.DestroyImmediate(sock);
                }

                // 3. A neighbour. Two atlas holes on one line, the second
                // nearer: the plug must take the nearer. Another avatar's pair
                // is numbered and owned, as a wearer's sockets are; one object's
                // is numbered with no owner, as a prop's; two objects are each
                // numbered by their own root, as world sockets are. A bucket
                // shared in one cell lets the later draw hide the other. The gap
                // view saturates at a length, so the tier view tells a far answer
                // from none.
                var pairs = new (string what, int farNumber, int nearNumber, int owner, bool strict)[]
                {
                    ("another avatar's", 1, 2, 4242, true),
                    ("one object's, no owner", 1, 2, 0, false),
                    ("two objects, no owner", 0, 0, 0, false),
                };
                int NumberOf(GameObject go) => go.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r.sharedMaterial != null && r.sharedMaterial.HasProperty("_YAPS_SocketIndex"))
                    .Select(r => Mathf.RoundToInt(r.sharedMaterial.GetFloat("_YAPS_SocketIndex"))).FirstOrDefault();
                foreach (var pair in pairs)
                {
                    var outer = MakeSocket("__Neighbour far", YapsSocket.SocketKind.Hole, false, true, false, pair.farNumber);
                    var near = MakeSocket("__Neighbour near", YapsSocket.SocketKind.Hole, false, true, false, pair.nearNumber);
                    // Nothing hands a stray socket an owner, so a block carries it.
                    if (pair.owner != 0)
                        foreach (var r in outer.GetComponentsInChildren<Renderer>(true).Concat(near.GetComponentsInChildren<Renderer>(true)))
                        {
                            var block = new MaterialPropertyBlock();
                            r.GetPropertyBlock(block);
                            block.SetFloat("_YAPS_Owner", pair.owner);
                            r.SetPropertyBlock(block);
                        }
                    foreach (float apart in new[] { 0.06f, 0.25f })
                    {
                        int seen = 0, hidden = 0, none = 0, other = 0, tried = 0;
                        var wrongAt = new List<string>();
                        foreach (var p in places)
                        {
                            // Past the base it is no longer the nearer one on this line.
                            if (p.reach * length - apart < 0.02f) continue;
                            tried++;
                            Aim(outer.transform, p);
                            Aim(near.transform, p, apart);
                            float gap = Shown(Capture(hdr, 2f));
                            float wantNear = (p.reach * length - apart) / length, wantFar = Mathf.Min(p.reach, 1f);
                            if (Mathf.Abs(gap - wantNear) < 0.02f) { seen++; continue; }
                            if (TierOf(Capture(hdr, 1f)) == 0) { none++; wrongAt.Add($"{Where(p)}: nobody"); }
                            else if (Mathf.Abs(gap - wantFar) < 0.02f) { hidden++; wrongAt.Add($"{Where(p)}: the far one"); }
                            else { other++; wrongAt.Add($"{Where(p)}: reads {gap:0.000}, near {wantNear:0.000}"); }
                        }
                        string label = $"neighbour {apart * 100f:0} cm nearer, {pair.what} (#{NumberOf(outer)}, #{NumberOf(near)})";
                        Log($"{label}: nearer taken {seen}, far one {hidden}, nobody {none}, other {other} of {tried}");
                        foreach (var w in wrongAt.Take(4)) Log($"  {w}");
                        if (pair.strict) fail += Check($"{label} is always the one taken", seen == tried);
                    }
                    UnityEngine.Object.DestroyImmediate(outer);
                    UnityEngine.Object.DestroyImmediate(near);
                }

                // 4. The mesh holds together while a socket walks in from 1.8
                // lengths to 0.2. Vertices sharing a place at rest (UV and
                // normal seams) must share it bent, or the surface opens; no
                // edge may stretch or squash past a bound; and a small socket
                // move is a small mesh move, or the plug pops.
                var mesh = skin.sharedMesh;
                var edgeKeys = new HashSet<long>();
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    if (!capture.Any(c => c.mat == mats[s])) continue;
                    var t = mesh.GetTriangles(s);
                    for (int i = 0; i < t.Length; i += 3)
                        for (int e = 0; e < 3; e++)
                        {
                            int va = t[i + e], vb = t[i + (e + 1) % 3];
                            edgeKeys.Add(((long) Mathf.Min(va, vb) << 32) | (uint) Mathf.Max(va, vb));
                        }
                }
                var edges = new List<(int a, int b, float len)>();
                foreach (long key in edgeKeys)
                {
                    int va = (int) (key >> 32), vb = (int) (key & 0xffffffffL);
                    float len = Vector3.Distance(rest[va], rest[vb]);
                    if (rest[va].w > 0.5f && rest[vb].w > 0.5f && len > 1e-5f) edges.Add((va, vb, len));
                }
                // ponytail: a 0.01 mm grid can split a coincident pair across a
                // cell edge and miss it; a neighbour search if that ever matters.
                var seams = new List<(int a, int b)>();
                var firstAt = new Dictionary<Vector3Int, int>();
                for (int i = 0; i < n; i++)
                {
                    if (rest[i].w < 0.5f) continue;
                    var key = Vector3Int.RoundToInt((Vector3) rest[i] * 1e5f);
                    if (firstAt.TryGetValue(key, out int first)) seams.Add((first, i));
                    else firstAt[key] = i;
                }
                Log($"mesh: {edges.Count} edges, {seams.Count} seam pairs");

                var walks = new (string label, bool atlas, float angle)[]
                {
                    ("YAPS hole walking in at 0°", true, 0f),
                    ("YAPS hole walking in at 45°", true, 45f),
                    ("YAPS hole walking in at 90°", true, 90f),
                    ("DPS hole walking in at 0°", false, 0f),
                };
                foreach (var w in walks)
                {
                    var walker = MakeSocket("__Walk", YapsSocket.SocketKind.Hole, false, w.atlas, true);
                    float seam = 0f, stretch = 1f, squash = 1f, jump = 0f, seamAt = 0f, stretchAt = 0f, squashAt = 0f, jumpAt = 0f;
                    int crushedInside = 0, crushedOutside = 0;
                    float crushedAt = 0f;
                    const float stride = 0.02f;
                    Vector4[] before = null;
                    for (int k = 0; k <= 80; k++)
                    {
                        float reach = 1.8f - k * stride;
                        Aim(walker.transform, (w.angle, 0f, reach));
                        var bent = Capture(hdr, 0f);
                        foreach (var (va, vb) in seams)
                        {
                            float d = Vector3.Distance(bent[va], bent[vb]);
                            if (d > seam) { seam = d; seamAt = reach; }
                        }
                        var opening = walker.transform.position;
                        var outward = walker.transform.forward;
                        int outside = 0;
                        foreach (var (va, vb, len) in edges)
                        {
                            float r = Vector3.Distance(bent[va], bent[vb]) / len;
                            if (r > stretch) { stretch = r; stretchAt = reach; }
                            if (r < squash) { squash = r; squashAt = reach; }
                            if (r >= 0.2f) continue;
                            // Behind the opening, a millimetre of slack.
                            bool inside = Vector3.Dot((Vector3) bent[va] - opening, outward) < 1e-3f
                                          && Vector3.Dot((Vector3) bent[vb] - opening, outward) < 1e-3f;
                            if (inside) crushedInside++; else outside++;
                        }
                        if (outside > crushedOutside) { crushedOutside = outside; crushedAt = reach; }
                        if (before != null)
                        {
                            float d = 0f;
                            for (int i = 0; i < n; i++)
                                if (bent[i].w > 0.5f && before[i].w > 0.5f) d = Mathf.Max(d, Vector3.Distance(bent[i], before[i]));
                            if (d > jump) { jump = d; jumpAt = reach; }
                        }
                        before = bent;
                    }
                    Log($"{w.label}: seams open {seam * 1000f:0.00} mm at {seamAt:0.00}L; edges {squash:0.00}x at {squashAt:0.00}L " +
                        $"to {stretch:0.00}x at {stretchAt:0.00}L; biggest step {jump / (stride * length):0.0}x the socket's move at {jumpAt:0.00}L");
                    Log($"  edges crushed below 0.2x: {crushedInside} edge-steps behind the opening, " +
                        $"most outside it {crushedOutside} at {crushedAt:0.00}L");
                    // A pop or a steep ramp: walk the worst step again ten times
                    // finer. A ramp's biggest step shrinks with the stride; a
                    // jump keeps its size, so its ratio grows tenfold.
                    {
                        const float fine = stride / 10f;
                        float fineJump = 0f, fineAt = 0f;
                        Vector4[] last = null;
                        for (int k = 0; k <= 30; k++)
                        {
                            float reach = jumpAt + stride * 1.5f - k * fine;
                            Aim(walker.transform, (w.angle, 0f, reach));
                            var bent = Capture(hdr, 0f);
                            if (last != null)
                            {
                                float d = 0f;
                                for (int i = 0; i < n; i++)
                                    if (bent[i].w > 0.5f && last[i].w > 0.5f) d = Mathf.Max(d, Vector3.Distance(bent[i], last[i]));
                                if (d > fineJump) { fineJump = d; fineAt = reach; }
                            }
                            last = bent;
                        }
                        Log($"  ten times finer round {jumpAt:0.00}L: biggest step {fineJump / (fine * length):0.0}x the socket's move " +
                            $"({fineJump * 1000f:0.0} mm) at {fineAt:0.000}L; at the coarse stride {jump * 1000f:0.0} mm");
                    }
                    fail += Check($"{w.label}: no seam opens past 0.5 mm", seam < 0.5e-3f);
                    fail += Check($"{w.label}: no edge squashed below 0.2x or stretched past 5x", squash > 0.2f && stretch < 5f);
                    fail += Check($"{w.label}: no step past ten times the socket's move", jump < 10f * stride * length);
                    UnityEngine.Object.DestroyImmediate(walker);
                }
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

        // A socket built the way the toolkit builds one, then cut down to the
        // transport under test: no lights for atlas only, no writer for a
        // DPS-style socket that only a light can announce.
        static bool RootLightMeets(GameObject socket, Renderer plug) =>
            socket.GetComponentsInChildren<Light>(true)
                .Where(l => Mathf.Abs(l.range - YapsSocketBuilder.FrontRange) > 1e-4f)
                .All(l => plug.bounds.SqrDistance(l.transform.position) <= l.range * l.range);

        static GameObject MakeSocket(string name, YapsSocket.SocketKind kind, bool oneWay, bool atlas, bool lights, int number = 0)
        {
            var go = new GameObject(name);
            var socket = go.AddComponent<YapsSocket>();
            socket.kind = kind;
            socket.oneWay = oneWay;
            socket.emitLights = lights;
            YapsSocketBuilder.Build(socket);
            if (!atlas)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    if (YapsAtlas.IsPlumbing(r.sharedMaterial))
                        UnityEngine.Object.DestroyImmediate(r.gameObject);
            // A number, as YapsOwner.ApplySelf gives a wearer's sockets.
            if (number > 0)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var indexed = YapsAtlas.Indexed(r.sharedMaterial, number);
                    if (indexed != null) r.sharedMaterial = indexed;
                }
            return go;
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
