// S2 of the portal plan: does the margin directory carry an exit exactly, at
// every target size, and leave every cell alone?
//
//   -executeMethod AvatarBridge.Regression.PortalDirectoryProbe.Run
//
// Needs, deployed beside the scratch Dev/Probes/Portal/yaps_atlas.cginc:
//   YAPS/Atlas Exit Spike              the exit writer
//   Hidden/YapsPortalDirectoryReader   the scratch YapsPortalExit, once per key
// The cells come from the production YAPS/Atlas Socket and YAPS/Atlas Clear,
// so they are what ships.
//
// Per size and target format (8-bit is linear, as the whole chain must be),
// over an opaque scene colour, 40 random sockets:
//   A  clear, sockets
//   B  clear, exits, sockets      exits drawn first
//   C  clear, sockets, exits      exits drawn last; one queue, so either happens
//   D  clear, sockets, four writers that must draw nothing
// B and C for 20 live entries and again for 200, a crowd that puts entries on
// home B and loses some to clashes.
//
// Which entries the draw order leaves readable is predicted from the homes and
// tags the GPU reports per key, so the probe never re-derives the hash. Gates:
//   - every readable entry found, owner and number exact, from the predicted
//     home; every unreadable one not found (a clash fails safe);
//   - position within S/255 + 0.1 mm, S = 0.32 m; facing within 0.5 deg;
//   - 0 false exits in 10,000 random keys;
//   - every pixel outside the directory bit-identical in A, B and C, and D
//     identical to A everywhere;
//   - every directory pixel inside the clear's span, so alpha 0 in A.
// Below the smallest layout (238 square) nothing may change at all.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace AvatarBridge.Regression
{
    public static class PortalDirectoryProbe
    {
        const string Tag = "[PortalDirectory] ";
        const double Cell = 0.32, PosBound = Cell / 255 * 1000 + 0.1, FwdBound = 0.5;   // mm, degrees
        const int Strangers = 10000, SocketCount = 40, DirRows = 8, TooSmall = 238;
        static readonly (int w, int h)[] Sizes =
            { (239, 239), (256, 256), (512, 512), (932, 596), (1024, 1024), (1920, 1080), (TooSmall, TooSmall) };
        static readonly (string name, RenderTextureFormat format)[] Formats =
            { ("8-bit", RenderTextureFormat.ARGB32), ("half", RenderTextureFormat.ARGBHalf) };
        static readonly Color Scene = new Color(0.3f, 0.6f, 0.9f, 1);

        class Entry
        {
            public int owner, n;
            public Vector3 at, fwd, entrance;
            public Matrix4x4 m;
        }

        [MenuItem("Tools/AvatarBridge Dev/Probe: Portal directory")]
        public static void Run()
        {
            int code = 1;
            try { code = Measure() ? 0 : 1; }
            catch (Exception e) { Debug.LogError(Tag + "FAIL " + e); }
            Debug.Log(Tag + (code == 0 ? "RESULT PASS" : "RESULT FAIL"));
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static bool Measure()
        {
            var exitShader = Compiled("YAPS/Atlas Exit Spike");
            var readerShader = Compiled("Hidden/YapsPortalDirectoryReader");
            var socketShader = Compiled("YAPS/Atlas Socket");
            var clearShader = Compiled("YAPS/Atlas Clear");
            var exitMat = new Material(exitShader);
            var reader = new Material(readerShader);
            var socketMat = new Material(socketShader);
            var clearMat = new Material(clearShader);
            var levelQuads = Quads(8);
            var exitQuads = Quads(2);

            var rng = new Random(20261018);
            var sockets = Enumerable.Range(0, SocketCount).Select(_ =>
            {
                var p = new MaterialPropertyBlock();
                p.SetFloat("_YAPS_Kind", rng.Next(3));
                p.SetFloat("_YAPS_SocketTags", rng.Next(2) == 0 ? 0 : rng.Next(1 << 20));
                p.SetFloat("_YAPS_Owner", rng.Next(2) == 0 ? 0 : rng.Next(1, 1 << 24));
                p.SetFloat("_YAPS_SocketIndex", rng.Next(16));
                return (m: Matrix4x4.TRS(V(rng, 3), Rot(rng), Vector3.one), p);
            }).ToList();
            var sets = new[] { ("20 live", Live(rng, 20)), ("200 live", Live(rng, 200)) }
                .Select(s => (name: s.Item1, live: s.Item2, strangers: Others(rng, s.Item2))).ToList();
            var first = sets[0].live[0];
            // No owner, no number, number 16, and an owner of 2^24, which is 0 in 24 bits.
            var refused = new[] { (0, 3), (first.owner, 0), (first.owner, 16), (1 << 24, 3) }
                .Select(k => new Entry { owner = k.Item1, n = k.Item2, m = Matrix4x4.TRS(V(rng, 200), Rot(rng), Vector3.one) })
                .ToList();

            var go = new GameObject("__PortalDirectoryCamera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.cullingMask = 0;
            Log($"GPU {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}; S {Cell} m, position bound {PosBound:0.000} mm, " +
                $"facing bound {FwdBound} deg; {SocketCount} sockets, {Strangers} random keys a set");

            void Render(RenderTexture rt, Color background, CommandBuffer cb)
            {
                cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = background;
                cam.AddCommandBuffer(CameraEvent.AfterEverything, cb);
                cam.Render();
                cam.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
                cb.Release();
            }

            void DrawSockets(CommandBuffer cb)
            {
                foreach (var s in sockets)
                {
                    cb.DrawMesh(levelQuads, s.m, socketMat, 0, 0, s.p);
                    cb.DrawMesh(levelQuads, s.m, socketMat, 0, 1, s.p);
                }
            }

            void DrawExits(CommandBuffer cb, List<Entry> exits)
            {
                foreach (var e in exits)
                {
                    var p = new MaterialPropertyBlock();
                    p.SetFloat("_YAPS_Owner", e.owner);
                    p.SetFloat("_YAPS_EntranceIndex", e.n);
                    cb.DrawMesh(exitQuads, e.m, exitMat, 0, 0, p);
                }
            }

            float[] Atlas(RenderTexture rt, Action<CommandBuffer> draws)
            {
                var cb = new CommandBuffer();
                cb.DrawMesh(levelQuads, Matrix4x4.identity, clearMat, 0, 0);
                draws(cb);
                cb.DrawProcedural(Matrix4x4.identity, reader, 1, MeshTopology.Points, 1);
                Render(rt, Scene, cb);
                return Readback(rt);
            }

            float[] Read(RenderTexture atlas, RenderTexture output, List<Entry> keys)
            {
                var buf = new ComputeBuffer(keys.Count * 2, 16);
                buf.SetData(keys.SelectMany(k => new[] { new Vector4(k.owner, k.n, 0, 0), (Vector4) k.entrance }).ToArray());
                reader.SetBuffer("_Queries", buf);
                Shader.SetGlobalTexture("_YAPS_Atlas", atlas);
                var cb = new CommandBuffer();
                cb.DrawProcedural(Matrix4x4.identity, reader, 0, MeshTopology.Points, 4 + 4 * keys.Count);
                Render(output, new Color(0, 0, 0, 0), cb);
                var data = Readback(output);
                buf.Release();
                return data;
            }

            bool all = true;
            double worstPos8 = 0, worstPosHalf = 0, worstFwd8 = 0, worstFwdHalf = 0;
            int falseTotal = 0, taggedTotal = 0, fromBTotal = 0, lostTotal = 0;
            try
            {
                foreach (var (w, h) in Sizes)
                    foreach (var (fname, format) in Formats)
                    {
                        string at = $"{w}x{h} {fname}";
                        var atlas = new RenderTexture(w, h, 0, format, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point };
                        var output = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point };
                        atlas.Create();
                        output.Create();
                        Shader.SetGlobalVector("_YAPS_Atlas_TexelSize", new Vector4(1f / w, 1f / h, w, h));
                        try
                        {
                            var a = Atlas(atlas, DrawSockets);
                            bool atlasTop = TopDown(a, w, h, i => a[i] == 1 && a[i + 1] == 0 && a[i + 2] == 1 && a[i + 3] == 1, (w - 1, h - 1));
                            var d = Atlas(atlas, cb => { DrawSockets(cb); DrawExits(cb, refused); });

                            // The layout as the GPU works it out in this target.
                            var sum = Read(atlas, output, sets[0].live.Take(1).ToList());
                            bool outTop = TopDown(sum, w, h, i => sum[i + 3] == 7777, (0, 0));
                            Vector4 Px(float[] data, bool top, int p)
                            {
                                int x = p % w, y = p / w, i = ((top ? y : h - 1 - y) * w + x) * 4;
                                return new Vector4(data[i], data[i + 1], data[i + 2], data[i + 3]);
                            }
                            var s0 = Px(sum, outTop, 0);
                            bool fits = s0.x == 1;
                            int perRow = (int) Px(sum, outTop, 1).x, span = perRow * 4;
                            bool InDir(int x, int y) => fits && y < DirRows && x < span;
                            Log($"{at}: fits {fits}, rect {s0.y}x{s0.z}, directory {perRow} slots a row, {perRow * DirRows} in all, " +
                                $"x 0-{span - 1} of rows 0-{DirRows - 1}; readbacks {(atlasTop ? "top" : "bottom")} first");

                            if (fits)
                            {
                                int uncleared = 0;
                                for (int y = 0; y < DirRows; y++)
                                    for (int x = 0; x < span; x++)
                                        if (Px(a, atlasTop, y * w + x).w != 0) uncleared++;
                                bool pass = uncleared == 0 && span <= s0.y;
                                all &= pass;
                                Log($"{(pass ? "PASS" : "FAIL")} {at} clear span: {uncleared} of {span * DirRows} directory pixels kept alpha " +
                                    $"after the clear and the sockets; directory {span} px wide against a rect of {s0.y}");
                            }
                            {
                                int diff = Diff(a, d, w, h, atlasTop, (x, y) => false, out _);
                                bool pass = diff == 0;
                                all &= pass;
                                Log($"{(pass ? "PASS" : "FAIL")} {at} refused writers (owner 0, number 0, number 16, owner 2^24): " +
                                    $"{diff} pixels differ from the sockets alone");
                            }

                            foreach (var set in sets)
                            {
                                var keys = set.live.Concat(set.strangers).ToList();
                                foreach (bool exitsFirst in new[] { true, false })
                                {
                                    string run = $"{at} {set.name}, exits {(exitsFirst ? "first" : "last")}";
                                    var b = Atlas(atlas, cb =>
                                    {
                                        if (exitsFirst) DrawExits(cb, set.live);
                                        DrawSockets(cb);
                                        if (!exitsFirst) DrawExits(cb, set.live);
                                    });
                                    var r = Read(atlas, output, keys);
                                    Vector4 Q(int q, int k) => Px(r, outTop, 4 + 4 * q + k);

                                    // Outside the directory nothing may move; below the
                                    // smallest layout, nothing anywhere.
                                    int outside = Diff(a, b, w, h, atlasTop, InDir, out int inside);
                                    bool cells = outside == 0;
                                    all &= cells;
                                    Log($"{(cells ? "PASS" : "FAIL")} {run} cells: {outside} pixels outside the directory differ from " +
                                        $"the sockets alone ({inside} inside it changed)");

                                    // The directory as the draw order leaves it: each slot holds
                                    // the last entry drawn there, home A then home B.
                                    var live = set.live;
                                    var last = new Dictionary<(int, int), int>();
                                    (int, int) Home(int q, bool second)
                                    {
                                        var v = Q(q, 2);
                                        return second ? ((int) v.z, (int) v.w) : ((int) v.x, (int) v.y);
                                    }
                                    for (int i = 0; i < live.Count; i++) { last[Home(i, false)] = i; last[Home(i, true)] = i; }

                                    int found = 0, fromB = 0, lost = 0, wrong = 0, exact = 0;
                                    double worstPos = 0, worstFwd = 0;
                                    for (int i = 0; i < live.Count; i++)
                                    {
                                        var e = live[i];
                                        int onA = last[Home(i, false)];
                                        bool wantHit = fits && (onA == i || (Q(onA, 1).w != Q(i, 1).w && last[Home(i, true)] == i));
                                        int wantHome = onA == i ? 0 : 1;
                                        var r0 = Q(i, 0);
                                        var r1 = Q(i, 1);
                                        var r3 = Q(i, 3);
                                        bool hit = r0.w == 1;
                                        string why = null;
                                        if (hit != wantHit) why = hit ? "found, predicted lost" : "not found, predicted readable";
                                        else if (!hit) lost++;
                                        else
                                        {
                                            found++;
                                            if (wantHome == 1) fromB++;
                                            double pos = ((Vector3) r0 - e.at).magnitude * 1000.0;
                                            double fwd = Angle(r1, e.fwd);
                                            worstPos = Math.Max(worstPos, pos);
                                            worstFwd = Math.Max(worstFwd, fwd);
                                            bool keyOk = r3.x == e.owner && r3.y == e.n && r3.z == wantHome;
                                            if (keyOk) exact++;
                                            if (!keyOk) why = $"read back owner {r3.x} number {r3.y} from home {r3.z}, wanted home {wantHome}";
                                            else if (pos > PosBound || fwd > FwdBound) why = $"{pos:0.000} mm, {fwd:0.000} deg off";
                                        }
                                        if (why != null)
                                        {
                                            if (wrong++ < 8) Log($"  FAIL {run} entry {i} owner {e.owner} number {e.n}: {why}");
                                        }
                                    }
                                    bool entries = wrong == 0;
                                    all &= entries;
                                    Log($"{(entries ? "PASS" : "FAIL")} {run} entries: {found} found ({fromB} from home B), {lost} " +
                                        $"{(fits ? "lost to clashes" : "unpublished below the floor")} as predicted, {wrong} wrong; " +
                                        $"owner and number exact {exact} of {found}; position worst " +
                                        $"{worstPos:0.000} mm (bound {PosBound:0.000}), facing worst {worstFwd:0.000} deg (bound {FwdBound})");

                                    int falseExits = 0, tagged = 0;
                                    for (int j = live.Count; j < keys.Count; j++)
                                    {
                                        if (Q(j, 0).w == 1) falseExits++;
                                        if (Q(j, 3).z >= 0) tagged++;
                                    }
                                    bool strangers = falseExits == 0;
                                    all &= strangers;
                                    Log($"{(strangers ? "PASS" : "FAIL")} {run} random keys: {falseExits} false exits in {set.strangers.Count}, " +
                                        $"{tagged} passed a tag and so reached the number and owner check");

                                    if (fname == "8-bit") { worstPos8 = Math.Max(worstPos8, worstPos); worstFwd8 = Math.Max(worstFwd8, worstFwd); }
                                    else { worstPosHalf = Math.Max(worstPosHalf, worstPos); worstFwdHalf = Math.Max(worstFwdHalf, worstFwd); }
                                    if (set.live.Count == 20) falseTotal += falseExits;
                                    taggedTotal += tagged;
                                    fromBTotal += fromB;
                                    if (fits) lostTotal += lost;
                                }
                            }
                        }
                        finally
                        {
                            cam.targetTexture = null;
                            atlas.Release();
                            output.Release();
                        }
                    }
                Log($"worst over the six sizes: 8-bit {worstPos8:0.000} mm, {worstFwd8:0.000} deg; half {worstPosHalf:0.000} mm, " +
                    $"{worstFwdHalf:0.000} deg; false exits with 20 live {falseTotal}; {taggedTotal} random keys past the tag; " +
                    $"{fromBTotal} entries read from home B, {lostTotal} lost to clashes (both sets)");
                return all;
            }
            finally { Object.DestroyImmediate(go); }
        }

        static Shader Compiled(string name)
        {
            var s = Shader.Find(name);
            if (s == null) throw new InvalidOperationException(name + " not found");
            foreach (var m in ShaderUtil.GetShaderMessages(s))
                Log($"  {name}: {m.severity} {m.message} line {m.line}");
            if (!s.isSupported || ShaderUtil.ShaderHasError(s)) throw new InvalidOperationException(name + " did not compile");
            return s;
        }

        // Four numbers an owner, as avatars with several portals each.
        static List<Entry> Live(Random rng, int count)
        {
            var list = new List<Entry>();
            while (list.Count < count)
            {
                int owner = rng.Next(1, 1 << 24);
                if (list.Any(e => e.owner == owner)) continue;
                foreach (int n in Enumerable.Range(1, 15).OrderBy(_ => rng.Next()).Take(4).ToList())
                {
                    if (list.Count == count) break;
                    var at = V(rng, 200);
                    // Every fifth flat along Z, for the zero-scale guard.
                    var scale = new Vector3(F(rng, 0.2f, 3), F(rng, 0.2f, 3), list.Count % 5 == 4 ? 0 : F(rng, 0.2f, 3));
                    var m = Matrix4x4.TRS(at, Rot(rng), scale);
                    Vector3 z = m.GetColumn(2);
                    if (z.sqrMagnitude < 1e-12f) z = Vector3.Cross(m.GetColumn(0), m.GetColumn(1));
                    // Inside the +-41 m the low byte of the cell index can be unwrapped over.
                    list.Add(new Entry { owner = owner, n = n, m = m, at = m.GetColumn(3), fwd = z.normalized, entrance = at + V(rng, 35) });
                }
            }
            return list;
        }

        // Keys nobody published: every unused number of every live owner, so a
        // tag clash there reaches the number check, then random keys.
        static List<Entry> Others(Random rng, List<Entry> live)
        {
            var taken = new HashSet<long>(live.Select(e => (long) e.owner << 4 | (long) e.n));
            var list = new List<Entry>();
            void Add(int owner, int n)
            {
                if (list.Count < Strangers && taken.Add((long) owner << 4 | (long) n))
                    list.Add(new Entry { owner = owner, n = n, entrance = V(rng, 200) });
            }
            foreach (int owner in live.Select(e => e.owner).Distinct().ToList())
                for (int n = 1; n <= 15; n++) Add(owner, n);
            while (list.Count < Strangers) Add(rng.Next(1, 1 << 24), rng.Next(1, 16));
            return list;
        }

        // Bit for bit, so a -0 or a NaN cannot pass as equal.
        static int Diff(float[] x, float[] y, int w, int h, bool top, Func<int, int, bool> skip, out int skipped)
        {
            int diff = 0;
            skipped = 0;
            for (int i = 0; i < w * h; i++)
            {
                bool same = true;
                for (int c = 0; c < 4; c++)
                    same &= BitConverter.SingleToInt32Bits(x[i * 4 + c]) == BitConverter.SingleToInt32Bits(y[i * 4 + c]);
                if (same) continue;
                int row = i / w, yTop = top ? row : h - 1 - row;
                if (skip(i % w, yTop)) skipped++;
                else diff++;
            }
            return diff;
        }

        // Which way a readback runs, from a pixel drawn at a known place.
        static bool TopDown(float[] data, int w, int h, Func<int, bool> marked, (int x, int y) at)
        {
            if (marked((at.y * w + at.x) * 4)) return true;
            if (marked(((h - 1 - at.y) * w + at.x) * 4)) return false;
            throw new InvalidOperationException($"no sentinel at {at} in a {w}x{h} readback");
        }

        static float[] Readback(RenderTexture rt)
        {
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = was;
            var data = tex.GetRawTextureData<float>().ToArray();
            Object.DestroyImmediate(tex);
            return data;
        }

        static double Angle(Vector3 a, Vector3 b)
        {
            double ax = a.x, ay = a.y, az = a.z, bx = b.x, by = b.y, bz = b.z;
            double cx = ay * bz - az * by, cy = az * bx - ax * bz, cz = ax * by - ay * bx;
            return Math.Atan2(Math.Sqrt(cx * cx + cy * cy + cz * cz), ax * bx + ay * by + az * bz) * 180 / Math.PI;
        }

        // YapsAtlas.LevelQuads, in memory: corners in UV0, quad index in z, positions zero.
        static Mesh Quads(int count)
        {
            var corners = new List<Vector3>();
            var tris = new List<int>();
            for (int q = 0; q < count; q++)
            {
                int b = corners.Count;
                corners.Add(new Vector3(-0.5f, -0.5f, q));
                corners.Add(new Vector3(0.5f, -0.5f, q));
                corners.Add(new Vector3(-0.5f, 0.5f, q));
                corners.Add(new Vector3(0.5f, 0.5f, q));
                tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }
            var mesh = new Mesh();
            mesh.SetVertices(corners.Select(_ => Vector3.zero).ToList());
            mesh.SetUVs(0, corners);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000);
            return mesh;
        }

        static float F(Random rng, float lo, float hi) => lo + (float) rng.NextDouble() * (hi - lo);
        static Vector3 V(Random rng, float r) => new Vector3(F(rng, -r, r), F(rng, -r, r), F(rng, -r, r));
        static Quaternion Rot(Random rng) => Quaternion.Euler(F(rng, 0, 360), F(rng, 0, 360), F(rng, 0, 360));

        static void Log(string s) => Debug.Log(Tag + s);
    }
}
