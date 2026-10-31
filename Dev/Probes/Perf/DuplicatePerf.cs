// What the duplicate's second copy costs (DS2).
//
//   -executeMethod AvatarBridge.Regression.DuplicatePerf.Run
//       -dupIncludes <Dev/Probes/Portal>   the spike copies, inlined per variant
//       [-dupRepeats 7]
//       [-dupCompare <log>]                an earlier run's log: every series here must
//                                          land within the larger spread of the two
//       [-dupNoSkin]                       the vertex stage only
//
// PortalDeformPerf's method: 16 draws of about 2^20 points into a 1024 square,
// best of eight renders after a warm one, the floor pass subtracted, cases of
// one content timed render by render in turn, medians and spread over the
// repeats, after one group timed and thrown away. Each draw is a whole number
// of meshes, so a duplicate draw holds both copies in the mesh's own
// proportion. Reported per pass of the whole mesh: 6433 vertices for the
// plug, 12866 with its copy.
//
// Gate: every check below, every number finite, skinning clear of its own
// spread, and with -dupCompare every series within the larger spread of the
// two runs. Whether a hidden copy costs more than half a drawn one is
// reported, not gated.
//
// Three materials on two variants of the spike's includes:
//   plain      YAPS_DUPLICATE off, the plug alone
//   copy off   YAPS_DUPLICATE on, the same mesh, _YAPS_CopyStart 0
//   duplicate  YAPS_DUPLICATE on, the copies appended and anchored as the
//              builder makes them, the bake extended with their originals' entries
//   plain, constant inputs   the plain plug on PortalDeformPerf's constant
//              inputs and count, which the compiler folds; a mesh's inputs are
//              data, so the others read theirs through a uniform. For comparison
// Each material is timed against its own floor.
// Two contents, each over an empty atlas and a full one (every cell header
// counting a socket whose tag never matches, every directory slot taken):
//   A    an owned and numbered hole just ahead of the root, every vertex past
//        it: the first copy enters it, the second has nothing past it and hides
//   AB   and a second hole past it, which the second copy bends into
//
// Before timing, every case is checked: the plain plug in A, in AB bit for bit
// as in A, and alone with the second hole as a reference; the first copy as
// the plain plug; the hidden copy on one point far behind the eye; the drawn
// copy vertex by vertex as the plain plug resolved to the second hole alone.
//
// Skinning, measured apart: the same plug geometry on a four-bone chain,
// 200 plugs to a mesh and four renderers, skinned against the same meshes
// drawn unskinned, so the difference is the skinning alone. A check first
// moves a bone between two renders and reads back what skinning wrote, so a
// skin reused from the last frame fails the run instead of timing nothing.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using P = AvatarBridge.Regression.PortalDeformPerf;

namespace AvatarBridge.Regression
{
    public static class DuplicatePerf
    {
        const string Tag = "[DuplicatePerf] ";
        const string Folder = "Assets/__DuplicatePerf";
        static readonly string[] Parts = { "yaps_props", "yaps_atlas", "yaps_resolve", "yaps_deform" };
        const string BaselineInclude = "#include \"../Yaps/yaps_deform.cginc\"";

        const int Plug = 6433, Points = 1 << 20, Draws = 16, Size = 1024;
        const float Length = 0.649f;
        const int Owner = 0x5A3C17;
        // Past 41 m from the origin, as PortalDeformPerf's.
        static readonly Vector3 Root = new Vector3(60.3f, 1.0f, 20.2f);
        static readonly Vector3 HoleA = Root + new Vector3(0, 0, 0.01f * Length);
        // Past A, inside full engagement (1.2 L) and the cell scan's reach.
        static readonly Vector3 HoleB = Root + new Vector3(0.35f, 0.1f, 0.5f) * Length;
        const int SkinPlugs = 200, SkinRenderers = 4, SkinBones = 4;

        enum Content { A, AB, B }

        [MenuItem("Tools/Avatar Bridge/Dev/Portal/Duplicate perf")]
        public static void Run()
        {
            int code = 1;
            try { code = Measure() ? 0 : 1; }
            catch (Exception e) { Debug.LogError(Tag + "FAIL " + e); }
            finally { AssetDatabase.DeleteAsset(Folder); }
            Debug.Log(Tag + (code == 0 ? "RESULT PASS" : "RESULT FAIL"));
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static bool Measure()
        {
            string spike = P.Arg("-dupIncludes");
            if (spike == null) throw new ArgumentException("-dupIncludes is required");
            int repeats = int.Parse(P.Arg("-dupRepeats") ?? "7");
            string compare = P.Arg("-dupCompare");
            bool skin = !Environment.GetCommandLineArgs().Contains("-dupNoSkin");

            var template0 = Shader.Find("Hidden/DuplicatePerf");
            var writer = Shader.Find("YAPS/Atlas Socket");
            var skinShader = Shader.Find("Hidden/DuplicatePerfSkin");
            if (template0 == null || !template0.isSupported || ShaderUtil.ShaderHasError(template0))
                throw new InvalidOperationException("Hidden/DuplicatePerf did not compile");
            if (writer == null) throw new InvalidOperationException("YAPS/Atlas Socket not found");
            if (skinShader == null || !skinShader.isSupported) throw new InvalidOperationException("Hidden/DuplicatePerfSkin did not compile");
            string template = P.Lf(File.ReadAllText(AssetDatabase.GetAssetPath(template0)));
            if (!template.Contains(BaselineInclude)) throw new InvalidOperationException("the template's include line moved");

            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__DuplicatePerf");
            string inlined = string.Concat(Parts.Select(p => P.Part(spike, p)));
            Shader Variant(string name, string define)
            {
                string path = $"{Folder}/{name}.shader";
                File.WriteAllText(path, template.Replace("Shader \"Hidden/DuplicatePerf\"", $"Shader \"Hidden/DuplicatePerf/{name}\"")
                    .Replace(BaselineInclude, define + inlined));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var s = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (s == null || !s.isSupported || ShaderUtil.ShaderHasError(s))
                {
                    foreach (var m in s != null ? ShaderUtil.GetShaderMessages(s) : new ShaderMessage[0])
                        Log($"  {m.severity} {m.message} line {m.line}");
                    throw new InvalidOperationException($"{name} did not compile");
                }
                return s;
            }
            var plainShader = Variant("Plain", "");
            var dupShader = Variant("Duplicate", "#define YAPS_DUPLICATE\n");
            var constShader = Variant("PlainConstant", "#define PERF_CONSTANT_INPUTS\n");

            var bake = P.Bake();
            var bake2 = Doubled(bake);
            string[] names = { "plain", "copy off", "duplicate", "plain, constant inputs" };
            int[] verts = { Plug, Plug, 2 * Plug, Plug };
            var mats = new[]
            {
                P.Plugged(new Material(plainShader), bake),
                P.Plugged(new Material(dupShader), bake),
                P.Plugged(new Material(dupShader), bake2),
                P.Plugged(new Material(constShader), bake),
            };
            mats[2].SetFloat("_YAPS_VertexCount", 2 * Plug);
            mats[2].SetFloat("_YAPS_CopyStart", Plug);
            mats[2].SetFloat("_PerfCopyStart", Plug);

            Material Socket(int number)
            {
                var m = new Material(writer);
                m.SetFloat("_YAPS_Kind", 1);
                m.SetFloat("_YAPS_SocketIndex", number);
                m.SetFloat("_YAPS_Owner", Owner);
                return m;
            }
            var sockA = Socket(3);
            var sockB = Socket(4);
            var atA = Matrix4x4.TRS(HoleA, Quaternion.LookRotation(Vector3.back), Vector3.one);
            var atB = Matrix4x4.TRS(HoleB, Quaternion.LookRotation(Root - HoleB), Vector3.one);
            var quads = P.LevelQuads();
            var atlas = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBHalf);
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBHalf);
            var check = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBFloat);
            var go = new GameObject("__DuplicatePerfCamera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.cullingMask = 0;
            var plugAt = Matrix4x4.TRS(Root, Quaternion.identity, Vector3.one);
            var sync = new Texture2D(1, 1, TextureFormat.RGBAHalf, false);
            var made = new List<Object>();
            Shader.SetGlobalVector("_YAPS_Atlas_TexelSize", new Vector4(1f / Size, 1f / Size, Size, Size));

            void Render(RenderTexture rt, CameraClearFlags clear, CommandBuffer cb)
            {
                cam.targetTexture = rt;
                cam.clearFlags = clear;
                cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.AddCommandBuffer(CameraEvent.AfterEverything, cb);
                cam.Render();
                cam.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
            }

            void Write(Content content, bool full, int mi)
            {
                var cb = new CommandBuffer();
                cb.ClearRenderTarget(false, true, new Color(0, 0, 0, full ? 1f / 255f : 0f));
                if (full) cb.DrawProcedural(Matrix4x4.identity, mats[mi], 4, MeshTopology.Triangles, 6);
                if (content != Content.B) { cb.DrawMesh(quads, atA, sockA, 0, 0); cb.DrawMesh(quads, atA, sockA, 0, 1); }
                if (content != Content.A) { cb.DrawMesh(quads, atB, sockB, 0, 0); cb.DrawMesh(quads, atB, sockB, 0, 1); }
                Render(atlas, CameraClearFlags.Nothing, cb);
                cb.Release();
                Shader.SetGlobalTexture("_YAPS_Atlas", atlas);
            }

            void Sync(RenderTexture rt)
            {
                // A readback waits for the GPU to finish everything before it.
                RenderTexture.active = rt;
                sync.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                RenderTexture.active = null;
            }

            Color[] ReadBack(RenderTexture rt)
            {
                var read = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                RenderTexture.active = null;
                var px = read.GetPixels();
                Object.DestroyImmediate(read);
                return px;
            }

            // Microseconds per 6433 vertices, before the floor comes off.
            double[] Time(List<(int mi, Content content, bool full)> group, int pass)
            {
                var counts = group.Select(r => (int) Math.Round((double) Points / verts[r.mi]) * verts[r.mi]).ToArray();
                var cbs = group.Select((r, k) =>
                {
                    var cb = new CommandBuffer();
                    for (int i = 0; i < Draws; i++) cb.DrawProcedural(plugAt, mats[r.mi], pass, MeshTopology.Points, counts[k]);
                    return cb;
                }).ToArray();
                var best = group.Select(_ => double.MaxValue).ToArray();
                for (int trial = 0; trial < 9; trial++)
                    for (int k = 0; k < group.Count; k++)
                    {
                        Write(group[k].content, group[k].full, group[k].mi);
                        Sync(atlas);
                        cam.targetTexture = target;
                        cam.clearFlags = CameraClearFlags.SolidColor;
                        cam.AddCommandBuffer(CameraEvent.AfterEverything, cbs[k]);
                        var watch = Stopwatch.StartNew();
                        cam.Render();
                        Sync(target);
                        if (trial > 0) best[k] = Math.Min(best[k], watch.Elapsed.TotalMilliseconds);
                        cam.RemoveCommandBuffer(CameraEvent.AfterEverything, cbs[k]);
                    }
                foreach (var cb in cbs) cb.Release();
                return best.Select((ms, k) => ms * 1e3 / ((double) Draws * counts[k]) * Plug).ToArray();
            }

            // Every deformed vertex in world space, and the summary pixels:
            // [0] tier, links, copy path found; [1] the socket; [2] the copy's
            // links and engagement; [3] the copy's socket.
            (Vector3[] at, Color[] sum, int seen) Draw(int mi, Content content, bool full)
            {
                Write(content, full, mi);
                var cb = new CommandBuffer();
                cb.DrawProcedural(plugAt, mats[mi], 2, MeshTopology.Points, verts[mi]);
                cb.DrawProcedural(plugAt, mats[mi], 3, MeshTopology.Points, 4);
                Render(check, CameraClearFlags.SolidColor, cb);
                cb.Release();
                var at = new Vector3[verts[mi]];
                var sum = new Color[4];
                int seen = 0;
                foreach (var c in ReadBack(check))
                {
                    if (c.a >= 100 && c.a < 100 + verts[mi]) { at[(int) c.a - 100] = new Vector3(c.r, c.g, c.b); seen++; }
                    else if (c.a == 10 || c.a == 20 || c.a == 30 || c.a == 40) sum[(int) c.a / 10 - 1] = c;
                }
                return (at, sum, seen);
            }

            bool Checks(bool full)
            {
                string fill = full ? "full" : "empty";
                bool ok = true;
                void Check(bool pass, string what)
                {
                    ok &= pass;
                    Log($"{(pass ? "PASS" : "FAIL")} check {fill}, {what}");
                }
                Vector3 Rgb(Color c) => new Vector3(c.r, c.g, c.b);
                double Off(Vector3[] a, int aFrom, Vector3[] b, int bFrom, int n)
                {
                    double worst = 0;
                    for (int i = 0; i < n; i++) worst = Math.Max(worst, (a[aFrom + i] - b[bFrom + i]).magnitude * 1000.0);
                    return worst;
                }
                int Differ(Vector3[] a, Vector3[] b) => Enumerable.Range(0, Plug).Count(i => !P.Same(a[i], b[i]));
                int Near(Vector3[] a, int from, Vector3 hole) => Enumerable.Range(from, Plug).Count(i => (a[i] - hole).magnitude <= Length * 1.05f);
                bool Resolved((Vector3[] at, Color[] sum, int seen) d, int n, Vector3 hole, out string note)
                {
                    float mm = (Rgb(d.sum[1]) - hole).magnitude * 1000;
                    note = $"{d.seen} of {n} vertices drawn, tier {d.sum[0].r}, {d.sum[0].g} links, the socket {mm:0.000} mm from the hole";
                    return d.seen == n && d.sum[0].r == 3 && d.sum[0].g == 1 && mm <= 2;
                }

                var pA = Draw(0, Content.A, full);
                var pAB = Draw(0, Content.AB, full);
                var pB = Draw(0, Content.B, full);
                var oA = Draw(1, Content.A, full);
                var oAB = Draw(1, Content.AB, full);
                var dA = Draw(2, Content.A, full);
                var dAB = Draw(2, Content.AB, full);
                string note;

                bool r = Resolved(pA, Plug, HoleA, out note);
                int near = Near(pA.at, 0, HoleA);
                Check(r && near == Plug, $"plain, A: {note}; {near} within 1.05 L of A");
                r = Resolved(pAB, Plug, HoleA, out note);
                int differ = Differ(pAB.at, pA.at);
                Check(r && differ == 0, $"plain, AB: {note}; {differ} vertices not bit for bit as in A, since the path ends at A");
                r = Resolved(pB, Plug, HoleB, out note);
                near = Near(pB.at, 0, HoleB);
                Check(r && near == Plug, $"plain, B alone (the drawn copy's reference): {note}; {near} within 1.05 L of B");

                double off = Off(oA.at, 0, pA.at, 0, Plug);
                Check(Resolved(oA, Plug, HoleA, out note) && off <= 0.01, $"copy off, A: {note}; {off:0.0000} mm from plain at most, bound 0.01");
                off = Off(oAB.at, 0, pAB.at, 0, Plug);
                Check(Resolved(oAB, Plug, HoleA, out note) && off <= 0.01, $"copy off, AB: {note}; {off:0.0000} mm from plain at most, bound 0.01");
                foreach (var c in new[] { Content.A, Content.AB })
                {
                    var k = Draw(3, c, full);
                    off = Off(k.at, 0, (c == Content.A ? pA : pAB).at, 0, Plug);
                    Check(Resolved(k, Plug, HoleA, out note) && off <= 0.01, $"plain, constant inputs, {c}: {note}; {off:0.0000} mm from plain at most, bound 0.01");
                }

                // Hidden: one point bit for bit, behind the eye and far from the plug.
                r = Resolved(dA, 2 * Plug, HoleA, out note);
                off = Off(dA.at, 0, pA.at, 0, Plug);
                var hide = dA.at[Plug];
                int apart = Enumerable.Range(Plug, Plug).Count(i => !P.Same(dA.at[i], hide));
                float away = (hide - Root).magnitude;
                Check(r && dA.sum[0].b == 0 && off <= 0.01 && apart == 0 && away > 10,
                    $"duplicate, A: {note}; copy path found {dA.sum[0].b}; first copy {off:0.0000} mm from plain at most; " +
                    $"second copy hidden: {apart} of {Plug} off one point, which is {away:0.0} m from the root");

                // Drawn: the second copy as the plain plug drew itself into B alone.
                r = Resolved(dAB, 2 * Plug, HoleA, out note);
                off = Off(dAB.at, 0, pAB.at, 0, Plug);
                double copyOff = Off(dAB.at, Plug, pB.at, 0, Plug);
                float copyMm = (Rgb(dAB.sum[3]) - HoleB).magnitude * 1000;
                near = Near(dAB.at, Plug, HoleB);
                Check(r && dAB.sum[0].b == 1 && dAB.sum[2].r == 1 && dAB.sum[2].g >= 0.999f && copyMm <= 2
                      && off <= 0.01 && copyOff <= 0.01 && near == Plug,
                    $"duplicate, AB: {note}; copy path found {dAB.sum[0].b}, {dAB.sum[2].r} links, engaged {dAB.sum[2].g:0.000}, " +
                    $"its socket {copyMm:0.000} mm from B; first copy {off:0.0000} mm from plain at most; " +
                    $"second copy {copyOff:0.0000} mm at most from the plain plug drawn into B alone, {near} within 1.05 L of B");
                return ok;
            }

            // Skinning: one rig, four bones along +Z, five layers for the cases.
            var rig = new List<(string name, int layer, int verts)>
            {
                ("plain skinned", 27, Plug), ("plain unskinned", 28, Plug),
                ("duplicate skinned", 29, 2 * Plug), ("duplicate unskinned", 30, 2 * Plug),
            };
            const int CheckLayer = 26;
            Transform[] bones = null;
            Material skinMat = null, skinCheck = null;

            Mesh SkinMesh(int plugs, bool copies)
            {
                int each = copies ? 2 * Plug : Plug, n = plugs * each;
                var pos = new Vector3[n];
                var nrm = new Vector3[n];
                var tan = new Vector4[n];
                var bw = new BoneWeight[n];
                for (int v = 0; v < n; v++)
                {
                    int i = v % each;
                    if (i >= Plug)
                    {
                        // The anchored copy: on the root, wholly on its bone.
                        pos[v] = Vector3.zero; nrm[v] = Vector3.up; tan[v] = new Vector4(0, 0, 1, 1);
                        bw[v] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
                        continue;
                    }
                    // PortalDeformPerf.Bake's tube.
                    float t = (i / 64) / 100f, angle = (i % 64) * Mathf.PI * 2 / 64;
                    float z = Mathf.Lerp(0.06f, 1, t) * Length, rad = 0.08f * Length * (1 - 0.5f * t);
                    float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
                    pos[v] = new Vector3(rad * cs, rad * sn, z); nrm[v] = new Vector3(cs, sn, 0); tan[v] = new Vector4(-sn, cs, 0, 1);
                    float f = z / (Length / (SkinBones - 1));
                    var w = Enumerable.Range(0, SkinBones).Select(b => (b, w: Mathf.Max(0, 1 - Mathf.Abs(f - b) / 1.5f)))
                        .OrderByDescending(x => x.w).ToArray();
                    float total = w.Sum(x => x.w);
                    bw[v] = new BoneWeight
                    {
                        boneIndex0 = w[0].b, weight0 = w[0].w / total, boneIndex1 = w[1].b, weight1 = w[1].w / total,
                        boneIndex2 = w[2].b, weight2 = w[2].w / total, boneIndex3 = w[3].b, weight3 = w[3].w / total,
                    };
                }
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                mesh.vertices = pos;
                mesh.normals = nrm;
                mesh.tangents = tan;
                mesh.boneWeights = bw;
                mesh.SetIndices(Enumerable.Range(0, n).ToArray(), MeshTopology.Points, 0);
                mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * bones[0].localToWorldMatrix).ToArray();
                made.Add(mesh);
                return mesh;
            }

            GameObject AddRenderer(Mesh mesh, bool skinned, int layer, Material mat)
            {
                var r = new GameObject("__DuplicatePerfSkin") { layer = layer };
                r.transform.SetPositionAndRotation(bones[0].position, bones[0].rotation);
                made.Add(r);
                if (skinned)
                {
                    var smr = r.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = mesh;
                    smr.bones = bones;
                    smr.rootBone = bones[0];
                    smr.quality = SkinQuality.Bone4;
                    smr.updateWhenOffscreen = false;
                    // Re-skinned on every render, not once an editor frame.
                    smr.forceMatrixRecalculationPerRender = true;
                    smr.localBounds = new Bounds(Vector3.zero, Vector3.one * 10);
                    smr.sharedMaterial = mat;
                    smr.shadowCastingMode = ShadowCastingMode.Off;
                }
                else
                {
                    r.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = r.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                }
                return r;
            }

            bool SkinSetup()
            {
                var root = new GameObject("__DuplicatePerfRig");
                made.Add(root);
                root.transform.position = new Vector3(0, 0, 5);
                bones = Enumerable.Range(0, SkinBones).Select(b =>
                {
                    if (b == 0) return root.transform;
                    var t = new GameObject("__DuplicatePerfBone" + b).transform;
                    t.SetParent(root.transform, false);
                    t.localPosition = new Vector3(0, 0, b * Length / (SkinBones - 1));
                    return t;
                }).ToArray();
                skinMat = new Material(skinShader);
                skinCheck = new Material(skinShader);
                skinCheck.SetFloat("_Check", 1);
                var plain = SkinMesh(SkinPlugs, false);
                var dup = SkinMesh(SkinPlugs, true);
                foreach (var c in rig)
                    for (int k = 0; k < SkinRenderers; k++)
                        AddRenderer(c.verts == Plug ? plain : dup, c.name.EndsWith(" skinned"), c.layer, skinMat);

                // Moved between two renders with no frame between them, the tip
                // must follow its bone and the copies must stay on the root.
                var one = SkinMesh(1, true);
                AddRenderer(one, true, CheckLayer, skinCheck);
                Vector3[] Skinned()
                {
                    cam.targetTexture = check;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0, 0, 0, 0);
                    cam.cullingMask = 1 << CheckLayer;
                    cam.Render();
                    cam.cullingMask = 0;
                    var at = new Vector3[2 * Plug];
                    foreach (var c in ReadBack(check))
                        if (c.a >= 100 && c.a < 100 + 2 * Plug) at[(int) c.a - 100] = new Vector3(c.r, c.g, c.b);
                    return at;
                }
                var before = Skinned();
                var tip = bones[SkinBones - 1];
                tip.localPosition += new Vector3(0.1f, 0, 0);
                var after = Skinned();
                tip.localPosition -= new Vector3(0.1f, 0, 0);
                var w = one.boneWeights[Plug - 1];
                float want = 0.1f * ((w.boneIndex0 == SkinBones - 1 ? w.weight0 : 0) + (w.boneIndex1 == SkinBones - 1 ? w.weight1 : 0)
                    + (w.boneIndex2 == SkinBones - 1 ? w.weight2 : 0) + (w.boneIndex3 == SkinBones - 1 ? w.weight3 : 0));
                float moved = after[Plug - 1].x - before[Plug - 1].x;
                double copyMoved = Enumerable.Range(Plug, Plug).Max(i => (after[i] - before[i]).magnitude) * 1000.0;
                double copyOff = Enumerable.Range(Plug, Plug).Max(i => (before[i] - bones[0].position).magnitude) * 1000.0;
                bool ok = Mathf.Abs(moved - want) <= 1e-4f && want > 0.05f && copyMoved == 0 && copyOff <= 0.01;
                Log($"{(ok ? "PASS" : "FAIL")} check skinning: GPU skinning {PlayerSettings.gpuSkinning}, compute {SystemInfo.supportsComputeShaders}, " +
                    $"skin weights {QualitySettings.skinWeights}; a bone moved 100 mm between two renders moved the tip {moved * 1000:0.000} mm, " +
                    $"want {want * 1000:0.000}; the copies moved {copyMoved:0.0000} mm and sit {copyOff:0.0000} mm from the root at most");
                return ok;
            }

            // Milliseconds a render, case by case in turn, best of eight after a warm one.
            double[] SkinTime()
            {
                var best = rig.Select(_ => double.MaxValue).ToArray();
                cam.targetTexture = target;
                cam.clearFlags = CameraClearFlags.SolidColor;
                for (int trial = 0; trial < 9; trial++)
                    for (int k = 0; k < rig.Count; k++)
                    {
                        cam.cullingMask = 1 << rig[k].layer;
                        Sync(target);
                        var watch = Stopwatch.StartNew();
                        cam.Render();
                        Sync(target);
                        if (trial > 0) best[k] = Math.Min(best[k], watch.Elapsed.TotalMilliseconds);
                    }
                cam.cullingMask = 0;
                return best;
            }

            try
            {
                Log($"GPU {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}; plug {Plug} vertices, duplicate {2 * Plug}, " +
                    $"L {Length}, {repeats} repeats");
                bool checks = Checks(false) & Checks(true);
                if (skin) checks &= SkinSetup();
                if (!checks) { Log("FAIL a case did not take the path it names; nothing timed"); return false; }

                // A group shares one content and fill, so the three materials are timed alike.
                var groups = new List<List<(int mi, Content content, bool full)>>();
                foreach (bool full in new[] { false, true })
                    foreach (var c in new[] { Content.A, Content.AB })
                        groups.Add(Enumerable.Range(0, mats.Length).Select(mi => (mi, c, full)).ToList());
                var series = new Dictionary<string, List<double>>();
                void Add(string name, double v) { if (!series.TryGetValue(name, out var l)) series[name] = l = new List<double>(); l.Add(v); }
                string Key((int mi, Content content, bool full) r) => $"{names[r.mi]}, {r.content}, {(r.full ? "full" : "empty")}";

                // Thrown away: the first timing of a run reads the GPU before its
                // clocks rise, a floor of 0.58 us against 0.22 after.
                Time(groups[0], 1);
                if (skin) SkinTime();
                for (int rep = 0; rep < repeats; rep++)
                {
                    var floor = Time(groups[0], 1);
                    for (int mi = 0; mi < mats.Length; mi++) Add($"floor per 6433 vertices, {names[mi]}", floor[mi]);
                    foreach (var g in groups)
                    {
                        var t = Time(g, 0);
                        // A pass of the whole mesh: the floor is per vertex, as is the time.
                        for (int k = 0; k < g.Count; k++) Add(Key(g[k]), (t[k] - floor[g[k].mi]) * verts[g[k].mi] / Plug);
                    }
                    if (skin)
                    {
                        var s = SkinTime();
                        double per = 1e3 / (SkinRenderers * SkinPlugs);
                        for (int k = 0; k < rig.Count; k++) Add($"render, {rig[k].name}", s[k] * per);
                        Add("skinning, plain", (s[0] - s[1]) * per);
                        Add("skinning, duplicate", (s[2] - s[3]) * per);
                        Add("skinning, the copies", ((s[2] - s[3]) - (s[0] - s[1])) * per);
                    }
                    Log($"repeat {rep + 1} of {repeats} done, floors {string.Join(" ", floor.Select(f => f.ToString("0.00")))} us");
                }

                // Paired repeat by repeat, which drift between repeats cannot reach.
                foreach (bool full in new[] { false, true })
                {
                    string fill = full ? "full" : "empty";
                    for (int rep = 0; rep < repeats; rep++)
                    {
                        double V(string k) => series[k][rep];
                        double pa = V($"plain, A, {fill}"), pab = V($"plain, AB, {fill}");
                        double hidden = V($"duplicate, A, {fill}") - pa, drawn = V($"duplicate, AB, {fill}") - pab;
                        Add($"define on with no copy, over plain, {fill}", V($"copy off, A, {fill}") - pa);
                        Add($"constant inputs, over plain, {fill}", V($"plain, constant inputs, A, {fill}") - pa);
                        Add($"hidden copy, over plain, {fill}", hidden);
                        Add($"drawn copy, over plain, {fill}", drawn);
                        Add($"hidden over drawn, {fill}", hidden / drawn);
                        Add($"ratio hidden, {fill}", V($"duplicate, A, {fill}") / pa);
                        Add($"ratio drawn, {fill}", V($"duplicate, AB, {fill}") / pab);
                    }
                }

                string Unit(string key) => key.StartsWith("ratio") || key.StartsWith("hidden over") ? "x" : "us";
                foreach (var kv in series)
                    Log($"time {kv.Key}: median {P.Median(kv.Value):0.000} {Unit(kv.Key)}, spread {P.Spread(kv.Value):0.000} " +
                        $"({string.Join(" ", kv.Value.Select(t => t.ToString("0.000")))})");

                bool all = series.Values.All(l => l.All(x => !double.IsNaN(x) && !double.IsInfinity(x)));
                foreach (bool full in new[] { false, true })
                {
                    string fill = full ? "full" : "empty";
                    double M(string k) => P.Median(series[k]);
                    double S(string k) => P.Spread(series[k]);
                    Log($"result {fill}: plain plug {M($"plain, A, {fill}"):0.00} us a pass (spread {S($"plain, A, {fill}"):0.00}); " +
                        $"YAPS_DUPLICATE on with no copy {M($"copy off, A, {fill}"):0.00} us, " +
                        $"{M($"define on with no copy, over plain, {fill}"):+0.00;-0.00} paired (spread {S($"define on with no copy, over plain, {fill}"):0.00}); " +
                        $"on constant inputs {M($"plain, constant inputs, A, {fill}"):0.00} us, {M($"constant inputs, over plain, {fill}"):+0.00;-0.00} paired");
                    Log($"result {fill}: copy 2 hidden {M($"duplicate, A, {fill}"):0.00} us a pass, {M($"ratio hidden, {fill}"):0.000}x the plain plug, " +
                        $"the copy {M($"hidden copy, over plain, {fill}"):+0.00;-0.00} us paired (spread {S($"hidden copy, over plain, {fill}"):0.00})");
                    Log($"result {fill}: copy 2 in the second socket {M($"duplicate, AB, {fill}"):0.00} us a pass, {M($"ratio drawn, {fill}"):0.000}x the plain plug " +
                        $"({M($"plain, AB, {fill}"):0.00}), the copy {M($"drawn copy, over plain, {fill}"):+0.00;-0.00} us paired (spread {S($"drawn copy, over plain, {fill}"):0.00})");
                    double h = M($"hidden over drawn, {fill}");
                    Log($"result {fill}: a hidden copy costs {h * 100:0.0}% of a drawn one (spread {S($"hidden over drawn, {fill}") * 100:0.0} points): " +
                        (h > 0.5 ? "FLAG, more than half: a hidden copy is not cheap" : "under half"));
                }
                if (skin)
                {
                    // Separated only if skinning a plug measures clear of zero.
                    double sm = P.Median(series["skinning, plain"]), ss = P.Spread(series["skinning, plain"]);
                    bool separated = sm > 0 && sm > ss;
                    all &= separated;
                    Log($"{(separated ? "PASS" : "FAIL")} skinning separated: a plug's skinning {sm:0.000} us against its spread {ss:0.000}");
                    Log($"result skinning, a frame: {P.Median(series["skinning, plain"]):0.000} us a plug of {Plug}, " +
                        $"{P.Median(series["skinning, duplicate"]):0.000} us with its copy; the copies {P.Median(series["skinning, the copies"]):+0.000;-0.000} us " +
                        $"(spread {P.Spread(series["skinning, the copies"]):0.000})");
                }

                if (compare != null)
                {
                    var before = new Dictionary<string, (double median, double spread)>();
                    var line = new Regex(@"\[DuplicatePerf\] time (.+): median ([-0-9.]+) (?:us|x), spread ([0-9.]+) \(");
                    foreach (var l in File.ReadLines(compare))
                    {
                        var m = line.Match(l);
                        if (m.Success) before[m.Groups[1].Value] = (double.Parse(m.Groups[2].Value), double.Parse(m.Groups[3].Value));
                    }
                    int matched = 0;
                    foreach (var kv in series)
                    {
                        if (!before.TryGetValue(kv.Key, out var b)) continue;
                        matched++;
                        double d = P.Median(kv.Value) - b.median, bound = Math.Max(P.Spread(kv.Value), b.spread);
                        bool pass = Math.Abs(d) <= bound;
                        all &= pass;
                        Log($"{(pass ? "PASS" : "FAIL")} again {kv.Key}: {P.Median(kv.Value):0.000} against {b.median:0.000}, " +
                            $"{d:+0.000;-0.000}, bound {bound:0.000} (the larger spread)");
                    }
                    bool some = matched == series.Count;
                    all &= some;
                    Log($"{(some ? "PASS" : "FAIL")} again: {matched} of {series.Count} series found in {Path.GetFileName(compare)}");
                }
                return all;
            }
            finally
            {
                cam.targetTexture = null;
                target.Release();
                atlas.Release();
                check.Release();
                Object.DestroyImmediate(go);
                foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            }
        }

        // The plain bake with every entry again after it, as the builder
        // extends dupFrom's bake: each copy takes its original's entry.
        static Texture2D Doubled(Texture2D plain)
        {
            var src = plain.GetRawTextureData();
            int entries = Plug * 10 * 4, width = plain.width;
            int floats = 1 + 2 * Plug * 10, height = (floats + width - 1) / width;
            var bytes = new byte[width * height * 4];
            Buffer.BlockCopy(src, 0, bytes, 0, 4 + entries);
            Buffer.BlockCopy(src, 4, bytes, 4 + entries, entries);
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0 };
            tex.SetPixelData(bytes, 0);
            tex.Apply(false, false);
            return tex;
        }

        static void Log(string s) => Debug.Log(Tag + s);
    }
}
