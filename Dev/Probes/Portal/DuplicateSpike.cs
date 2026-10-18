#if CVR_CCK_EXISTS && UNITY_EDITOR && AVATARBRIDGE_YAPS
// The duplicate's draw, option f (DS1).
//
//   -executeMethod AvatarBridge.Regression.DuplicateSpike.Run
//       -dupIncludes <Dev/Probes/Portal>   the spike copies; the project's props must be
//                                          the spike's with "#define YAPS_DUPLICATE" first
//       [-dupHosts Fade]                   only hosts whose name contains it (Simple Lit always)
//       [-dupSweep 71]                     steps across the second socket's engage edge
//       [-dupSweepFrom 1.0 -dupSweepTo 1.7] its range, in plug lengths from the base
//
// Build is the builder: a copy of every corner of the plug submesh's
// triangles whose three corners are all on the plug, appended after every
// vertex with its attributes, bone weights and blendshape deltas, and those
// triangles added to the same submesh over the copies. Always from the mesh
// under it, so copies never compound. The deform tells the copy apart by
// _YAPS_CopyStart, the original vertex count.
//
// The probe makes a skinned plug with a body behind it and runs the plan's
// order: split, duplicate, bake, readout. Then, through the scratch deform
// with YAPS_DUPLICATE on, with sockets drawn into a screen atlas in several
// arrangements, on Simple Lit, Poiyomi Fade, Poiyomi outlines and a lilToon
// outline, HDR and 8-bit:
//   a hidden copy: the frame with the copy's triangles is the frame without
//     them, pixel for pixel, and every copy vertex is one point
//   a drawn copy: the first copy's rings reach its socket and the second's
//     the next socket past the first's path
//   seams 0.00 mm in both copies
// Off the GPU: the copies' bake entries equal their originals' bit for bit,
// UInt32 indices past 65535 vertices, a rebuild that does not compound, and
// the readout after the copies.
//
// Reported, not gated: the sweep of a second socket across where the copy
// starts drawing, on a bent plug, counting triangles split between hidden and
// drawn corners and the pixels they throw outside the plug.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    public static class DuplicateSpike
    {
        public const string Suffix = " duplicate";

        // --- the builder ----------------------------------------------

        // from: which original each copy copies, in order; copy i is vertex
        // dupFrom.vertexCount + i.
        public static Mesh Build(Mesh dupFrom, int submesh, Transform[] bones, Transform root,
            out int[] from, out string why)
        {
            from = null;
            why = null;
            if (dupFrom == null || root == null || bones == null) { why = "no mesh, bones or root"; return null; }
            // Built on, the copies would be copied again.
            if (dupFrom.name.EndsWith(Suffix, StringComparison.Ordinal)) { why = "it is a duplicate already; build from the mesh under it"; return null; }
            if (submesh < 0 || submesh >= dupFrom.subMeshCount || dupFrom.GetTopology(submesh) != MeshTopology.Triangles)
            { why = "it has no triangle submesh " + submesh; return null; }
            int n = dupFrom.vertexCount;
            var weights = dupFrom.boneWeights;
            if (weights.Length != n) { why = "it has no bone weights"; return null; }
            var plugBones = new HashSet<int>();
            for (int b = 0; b < bones.Length; b++)
                if (bones[b] != null && bones[b].IsChildOf(root)) plugBones.Add(b);
            // Any weight at all, as the bake marks a vertex active. A corner off
            // the plug holds the copy's triangle to the body wherever it goes.
            bool On(int v) => YapsBaker.WeightOnPlug(weights[v], plugBones) > 0;
            var tris = dupFrom.GetTriangles(submesh);
            var kept = new List<int>();
            var copied = new SortedSet<int>();
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                if (!On(tris[i]) || !On(tris[i + 1]) || !On(tris[i + 2])) continue;
                for (int c = 0; c < 3; c++) { kept.Add(tris[i + c]); copied.Add(tris[i + c]); }
            }
            if (kept.Count == 0) { why = "no triangle has all three corners on the plug"; return null; }
            from = copied.ToArray();
            var copyOf = new Dictionary<int, int>();
            for (int i = 0; i < from.Length; i++) copyOf[from[i]] = n + i;
            int total = n + from.Length;

            var mesh = new Mesh
            {
                name = dupFrom.name + Suffix,
                // A copy past 65535 has no 16-bit index.
                indexFormat = total > 65535 ? IndexFormat.UInt32 : dupFrom.indexFormat,
            };
            mesh.vertices = Extend(dupFrom.vertices, from);
            mesh.normals = Extend(dupFrom.normals, from);
            mesh.tangents = Extend(dupFrom.tangents, from);
            mesh.colors = Extend(dupFrom.colors, from);
            var uv = new List<Vector4>();
            for (int channel = 0; channel < 8; channel++)
            {
                uv.Clear();
                dupFrom.GetUVs(channel, uv);
                if (uv.Count != n) continue;
                for (int i = 0; i < from.Length; i++) uv.Add(uv[from[i]]);
                mesh.SetUVs(channel, uv);
            }

            // The full list, as the readout copies it: more than four influences survive.
            var perVertex = dupFrom.GetBonesPerVertex();
            var all = dupFrom.GetAllBoneWeights();
            var starts = new int[n];
            for (int i = 0, run = 0; i < n; i++) { starts[i] = run; run += perVertex[i]; }
            int added = 0;
            foreach (int v in from) added += perVertex[v];
            var perOut = new NativeArray<byte>(total, Allocator.Temp);
            var allOut = new NativeArray<BoneWeight1>(all.Length + added, Allocator.Temp);
            try
            {
                NativeArray<byte>.Copy(perVertex, perOut, n);
                NativeArray<BoneWeight1>.Copy(all, allOut, all.Length);
                int at = all.Length;
                for (int i = 0; i < from.Length; i++)
                {
                    int count = perVertex[from[i]];
                    perOut[n + i] = (byte) count;
                    for (int k = 0; k < count; k++) allOut[at++] = all[starts[from[i]] + k];
                }
                mesh.SetBoneWeights(perOut, allOut);
            }
            finally
            {
                perOut.Dispose();
                allOut.Dispose();
            }
            mesh.bindposes = dupFrom.bindposes;

            mesh.subMeshCount = dupFrom.subMeshCount;
            for (int s = 0; s < dupFrom.subMeshCount; s++)
            {
                var indices = dupFrom.GetIndices(s);
                if (s == submesh) indices = indices.Concat(kept.Select(v => copyOf[v])).ToArray();
                mesh.SetIndices(indices, dupFrom.GetTopology(s), s);
            }

            var dv = new Vector3[n];
            var dn = new Vector3[n];
            var dt = new Vector3[n];
            for (int s = 0; s < dupFrom.blendShapeCount; s++)
                for (int f = 0; f < dupFrom.GetBlendShapeFrameCount(s); f++)
                {
                    dupFrom.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    mesh.AddBlendShapeFrame(dupFrom.GetBlendShapeName(s), dupFrom.GetBlendShapeFrameWeight(s, f),
                        Extend(dv, from), Extend(dn, from), Extend(dt, from));
                }
            mesh.bounds = dupFrom.bounds;
            return mesh;
        }

        static T[] Extend<T>(T[] source, int[] from)
        {
            if (source == null || source.Length == 0) return source;
            var to = new T[source.Length + from.Length];
            Array.Copy(source, to, source.Length);
            for (int i = 0; i < from.Length; i++) to[source.Length + i] = source[from[i]];
            return to;
        }

        // --- the probe ------------------------------------------------

        const string Tag = "[DuplicateSpike] ";
        const string Folder = "Assets/__DupSpike";
        const string Define = "#define YAPS_DUPLICATE";
        const string PoiyomiPath = "Assets/_PoiyomiShaders/Shaders/9.3/Toon/Poiyomi Toon.shader";
        static readonly string[] Parts = { "yaps_props", "yaps_atlas", "yaps_resolve", "yaps_deform" };

        // A tube along +Z: behind the base it is body, the first 2 cm feathered
        // onto the plug, and from the middle a second bone.
        const int Rings = 72, Sides = 24, Columns = Sides + 1, Verts = (Rings + 1) * Columns;
        const float Behind = -0.03f, TipZ = 0.33f, Feather = 0.02f, Radius = 0.03f, MidZ = 0.165f;
        const float RingStep = (TipZ - Behind) / Rings;
        const int Owner = 0x5A3C17, W = 1280, H = 800, Layer = 31, CaptureLayer = 30;
        static readonly Vector3 At = new Vector3(60.3f, 1.0f, 20.2f);

        static float RingZ(int k) => Behind + (TipZ - Behind) * k / Rings;

        class Host
        {
            public string name;
            public Material mat;
            public readonly long[] hiddenDiff = new long[2], drawnDiff = new long[2];
            public int failures;
        }

        struct Sock
        {
            public string name;
            public Vector3 offset;   // from the bake's origin
            public int kind, number;
        }

        class Case
        {
            public string name;
            public Sock[] socks = new Sock[0];
            public float enabled = 1, debug, bend;
            public bool preview;
            public string first, second;   // the sockets each copy lands in; second null when hidden
        }

        static readonly Sock A = new Sock { name = "A", offset = new Vector3(-0.07f, 0.02f, 0.24f), kind = 1, number = 1 };
        static readonly Sock B = new Sock { name = "B", offset = new Vector3(0.09f, -0.01f, 0.265f), kind = 1, number = 2 };
        static readonly Sock R = new Sock { name = "R", offset = new Vector3(0, 0, 0.10f), kind = 0, number = 3 };
        // Past 1.6 lengths, where the first copy would rest unengaged.
        static readonly Sock Far = new Sock { name = "B far", offset = new Vector3(0.12f, 0, 0.51f), kind = 1, number = 2 };

        static readonly Case[] Cases =
        {
            new Case { name = "no socket" },
            new Case { name = "one hole", socks = new[] { A }, first = "A" },
            new Case { name = "two holes, off", socks = new[] { A, B }, enabled = 0 },
            new Case { name = "two holes, bent, off", socks = new[] { A, B }, enabled = 0, bend = 20 },
            new Case { name = "two holes, a view", socks = new[] { A, B }, debug = 1 },
            new Case { name = "two holes, preview socket, atlas off", socks = new[] { A, B }, preview = true, first = "B" },
            new Case { name = "second hole unengaged", socks = new[] { A, Far }, first = "A" },
            new Case { name = "ring then hole", socks = new[] { R, A }, first = "A" },
            new Case { name = "the second hole alone", socks = new[] { B }, first = "B" },
            new Case { name = "two holes", socks = new[] { A, B }, first = "A", second = "B" },
            new Case { name = "ring, hole, hole", socks = new[] { R, A, B }, first = "A", second = "B" },
            new Case { name = "two holes, bent", socks = new[] { A, B }, bend = 20, first = "A", second = "B" },
        };

        [MenuItem("Tools/AvatarBridge Dev/Probe: Duplicate spike")]
        public static void Run()
        {
            int code = 1;
            bool async = ShaderUtil.allowAsyncCompilation;
            var before = Entries("Assets");
            try
            {
                // A host still compiling draws Unity's placeholder, unbent.
                ShaderUtil.allowAsyncCompilation = false;
                code = Measure() ? 0 : 1;
            }
            catch (Exception e) { Debug.LogError(Tag + "FAIL " + e); }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
                var made = Entries("Assets").Except(before).OrderBy(p => p.Length).ToList();
                var gone = new List<string>();
                foreach (string p in made)
                    if (!gone.Any(g => p.StartsWith(g + "/")) && AssetDatabase.DeleteAsset(p)) gone.Add(p);
                Log($"cleaned up {gone.Count} new assets: {string.Join(", ", gone.Take(12))}{(gone.Count > 12 ? ", ..." : "")}");
            }
            Debug.Log(Tag + (code == 0 ? "RESULT PASS" : "RESULT FAIL"));
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static bool all;

        static void Check(bool ok, string line)
        {
            all &= ok;
            Log((ok ? "PASS " : "FAIL ") + line);
        }

        static bool Measure()
        {
            all = true;
            string spike = Arg("-dupIncludes") ?? throw new ArgumentException("-dupIncludes is required");
            string only = Arg("-dupHosts");
            int sweep = int.Parse(Arg("-dupSweep") ?? "71");
            float sweepFrom = float.Parse(Arg("-dupSweepFrom") ?? "1.0", System.Globalization.CultureInfo.InvariantCulture);
            float sweepTo = float.Parse(Arg("-dupSweepTo") ?? "1.7", System.Globalization.CultureInfo.InvariantCulture);
            bool Wanted(string name) => only == null || name == "Simple Lit" || name.Contains(only);

            var socketShader = Compiled("YAPS/Atlas Socket");
            var clearShader = Compiled("YAPS/Atlas Clear");
            string shipped = Path.GetDirectoryName(AssetDatabase.GetAssetPath(socketShader)).Replace('\\', '/');
            foreach (string p in Parts)
            {
                string want = (p == "yaps_props" ? Define + "\n" : "") + Lf(File.ReadAllText($"{spike}/{p}.cginc"));
                if (Lf(File.ReadAllText($"{shipped}/{p}.cginc")) != want)
                    throw new InvalidOperationException($"{shipped}/{p}.cginc is not the copy in {spike}" +
                        (p == "yaps_props" ? " behind \"" + Define + "\"" : "") + "; swap it in before Unity starts");
            }
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__DupSpike");
            var capture = new Material(Template(spike, "DuplicateCapture", shipped));
            Log($"GPU {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}; includes {shipped} = {spike} with {Define}");

            var report = new BridgeReport();
            var hosts = Hosts(report, Wanted);
            foreach (var e in report.Entries.Where(e => e.Status == ReportStatus.Warning))
                Log($"  report warning: {e.Subject}: {e.Detail}");

            IndexFormatCheck();

            var made = new List<Object>();
            T Made<T>(T o) where T : Object { made.Add(o); return o; }
            GameObject Go(string name, Transform parent = null)
            {
                var go = Made(new GameObject(name) { hideFlags = HideFlags.DontSave });
                if (parent != null) go.transform.SetParent(parent, false);
                return go;
            }

            // The avatar: a root, hips, the plug's two bones, the body renderer.
            var avatar = Go("__DupAvatar");
            avatar.transform.position = At;
            avatar.AddComponent<CVRAvatar>();
            var hips = Go("Hips", avatar.transform).transform;
            var plugRoot = Go("PlugRoot", hips).transform;
            var plugMid = Go("PlugMid", plugRoot).transform;
            plugMid.localPosition = new Vector3(0, 0, MidZ);
            var bodyGo = Go("Body", avatar.transform);
            bodyGo.layer = Layer;
            var bones = new[] { hips, plugRoot, plugMid };
            var source = Made(PlugMesh());
            source.bindposes = bones.Select(b => b.worldToLocalMatrix * bodyGo.transform.localToWorldMatrix).ToArray();
            var skin = bodyGo.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = source;
            skin.bones = bones;
            skin.rootBone = hips;
            var bodyMat = Made(new Material(Shader.Find("Unlit/Color")) { color = new Color(0.4f, 0.35f, 0.3f, 1) });
            skin.sharedMaterials = new[] { bodyMat };
            skin.shadowCastingMode = ShadowCastingMode.Off;
            skin.receiveShadows = false;
            skin.lightProbeUsage = LightProbeUsage.Off;
            skin.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // Split, duplicate, bake, readout.
            var plain = hosts[0].mat;
            int slot = YapsSlotSplit.Split(skin, plugRoot, 0, YapsSlotSplit.Mask(skin, plugRoot), plain, Folder, out string why);
            Check(slot == 1, $"split: the plug's triangles on slot {slot} ({why ?? "ok"}), {skin.sharedMesh.subMeshCount} submeshes");
            if (slot < 0) return false;
            var dupFrom = skin.sharedMesh;
            int n = dupFrom.vertexCount;
            var dup = Made(Build(dupFrom, slot, bones, plugRoot, out int[] from, out why));
            if (dup == null) { Check(false, "duplicate: " + why); return false; }
            int total = dup.vertexCount;
            var copyOf = new Dictionary<int, int>();
            for (int i = 0; i < from.Length; i++) copyOf[from[i]] = n + i;
            int origTris = dupFrom.GetTriangles(slot).Length / 3, copyTris = dup.GetTriangles(slot).Length / 3 - origTris;
            Log($"duplicate: {n} vertices, {from.Length} copied ({total} in all, index format {dup.indexFormat}), " +
                $"{copyTris} of the plug slot's {origTris} triangles copied");
            AttributesCheck(dupFrom, dup, from);

            var again = Made(Build(dupFrom, slot, bones, plugRoot, out int[] from2, out _));
            bool sameAgain = again != null && again.vertexCount == total && from2.SequenceEqual(from)
                             && Enumerable.Range(0, dup.subMeshCount).All(s => again.GetIndices(s).SequenceEqual(dup.GetIndices(s)))
                             && again.vertices.SequenceEqual(dup.vertices);
            var compounded = Build(dup, slot, bones, plugRoot, out _, out string refused);
            Check(sameAgain && compounded == null, $"rebuild from dupFrom: identical {sameAgain}, {again?.vertexCount} vertices; " +
                $"built on the duplicate itself: {(compounded == null ? "refused (" + refused + ")" : compounded.vertexCount + " vertices")}");

            // The mesh as it was, baked under another name so the asset is its own.
            bodyGo.name = "BodyPlain";
            var plainBake = YapsBaker.Bake(skin, plugRoot, Folder, report, out string failure, new[] { "Thick" });
            if (plainBake == null) { Check(false, "bake of dupFrom: " + failure); return false; }
            var plainFloats = Floats(plainBake.Bake);
            bodyGo.name = "Body";
            skin.sharedMesh = dup;
            var result = YapsBaker.Bake(skin, plugRoot, Folder, report, out failure, new[] { "Thick" });
            if (result == null) { Check(false, "bake of the duplicate: " + failure); return false; }
            var baked = Floats(result.Bake);
            BakeCheck(baked, plainFloats, result, plainBake, from, n, total);

            // The readout after the copies: its quads past them, its anchor an original.
            var plugComp = plugRoot.gameObject.AddComponent<YapsPlug>();
            plugComp.readout = true;
            YapsDebugOverlayBuilder.Record(plugComp, result, skin.sharedMaterials[slot], "probe", report);
            YapsDebugOverlayBuilder.Build(skin, report);
            var withReadout = skin.sharedMesh;
            bool readoutOk = withReadout != dup && withReadout.vertexCount == total + 12
                             && withReadout.subMeshCount == dup.subMeshCount + 1
                             && plugComp.readoutAnchor >= 0 && plugComp.readoutAnchor < n && plugComp.readoutTip < n
                             && withReadout.vertices.Take(total).SequenceEqual(dup.vertices);
            string readoutName = withReadout.name;
            YapsDebugOverlayBuilder.Restore(skin);
            bool restored = skin.sharedMesh == dup && skin.sharedMaterials.Length == dup.subMeshCount;
            Check(readoutOk && restored, $"readout after the copies: \"{readoutName}\" {withReadout.vertexCount} vertices " +
                $"(the duplicate's {total} + 12), anchor {plugComp.readoutAnchor} tip {plugComp.readoutTip} (copies from {n}); " +
                $"restored to the duplicate {restored}");

            // The same vertices without the copies' triangles: what the frame must be when the copy hides.
            var lone = Made(Object.Instantiate(dup));
            lone.name = dup.name + " lone";
            lone.SetTriangles(dupFrom.GetTriangles(slot), slot);
            var copyTriangles = dup.GetTriangles(slot).Skip(origTris * 3).ToArray();
            // The same again as points, one pixel each, for the capture.
            var points = Made(Object.Instantiate(dup));
            points.name = dup.name + " points";
            points.subMeshCount = 1;
            points.SetIndices(Enumerable.Range(0, total).ToArray(), MeshTopology.Points, 0);
            var huge = new Bounds(Vector3.zero, Vector3.one * 1000);
            skin.updateWhenOffscreen = false;
            skin.localBounds = huge;
            // Without it every render of one editor frame reuses the skin
            // matrices of the first, and a bend set between renders is never seen.
            skin.forceMatrixRecalculationPerRender = true;
            var capGo = Go("Capture", avatar.transform);
            capGo.layer = CaptureLayer;
            var capSkin = capGo.AddComponent<SkinnedMeshRenderer>();
            capSkin.sharedMesh = points;
            capSkin.bones = bones;
            capSkin.rootBone = hips;
            capSkin.sharedMaterial = capture;
            capSkin.updateWhenOffscreen = false;
            capSkin.localBounds = huge;
            capSkin.forceMatrixRecalculationPerRender = true;

            foreach (var h in hosts) YapsBaker.Apply(result, h.mat, true);
            var block = new MaterialPropertyBlock();
            foreach (var kv in Defaults) block.SetFloat(kv.Key, kv.Value);
            block.SetFloat("_YAPS_VertexCount", result.VertexCount);
            block.SetFloat("_YAPS_Length", result.Length);
            block.SetFloat("_YAPS_ShapeCount", result.Shapes.Count);
            block.SetFloat("_YAPS_CopyStart", n);
            block.SetTexture("_YAPS_Bake", result.Bake);
            block.SetVector("_YAPS_Bake_TexelSize", new Vector4(1f / result.Bake.width, 1f / result.Bake.height, result.Bake.width, result.Bake.height));
            foreach (string v in new[] { "_YAPS_ShapeWeights", "_YAPS_ShapeWeights2", "_YAPS_ShapeWeights3", "_YAPS_ShapeWeights4",
                         "_YAPS_SocketFlags", "_YAPS_SocketPos", "_YAPS_SocketForward" })
                block.SetVector(v, Vector4.zero);
            float L = result.Length;
            var origin = result.Origin;
            Log($"bake: length {L:0.0000} m, origin {(origin - At) * 1000} mm from the avatar, sockets at " +
                string.Join(", ", new[] { A, B, R, Far }.Select(s => $"{s.name} {s.offset.magnitude / L:0.000} L")));

            // One light, the scene camera, the capture camera, the atlas camera.
            var lightGo = Go("__DupLight");
            lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << Layer;
            var camGo = Go("__DupCamera");
            camGo.transform.position = origin + new Vector3(0.75f, 0.3f, 0.05f);
            camGo.transform.LookAt(origin + new Vector3(0, 0, 0.17f));
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 50;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 50;
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.07f, 0.09f, 1);
            cam.renderingPath = RenderingPath.Forward;
            cam.allowMSAA = false;
            var capCamGo = Go("__DupCaptureCamera");
            capCamGo.transform.SetPositionAndRotation(camGo.transform.position + new Vector3(0.2f, 0.1f, -0.3f), Quaternion.Euler(10, -60, 0));
            var capCam = capCamGo.AddComponent<Camera>();
            capCam.enabled = false;
            capCam.cullingMask = 1 << CaptureLayer;
            capCam.clearFlags = CameraClearFlags.SolidColor;
            capCam.backgroundColor = new Color(0, 0, 0, 0);
            capCam.allowHDR = false;
            capCam.allowMSAA = false;
            capCam.nearClipPlane = 0.05f;
            capCam.farClipPlane = 50;
            var capPoint = capCamGo.transform.position - capCamGo.transform.forward;
            var atlasCam = Go("__DupAtlasCamera").AddComponent<Camera>();
            atlasCam.enabled = false;
            atlasCam.cullingMask = 0;
            atlasCam.clearFlags = CameraClearFlags.SolidColor;

            var levelQuads = Made(Quads(8));
            var socketMat = Made(new Material(socketShader));
            socketMat.SetFloat("_YAPS_Owner", Owner);
            var clearMat = Made(new Material(clearShader));

            double worstSeam = 0, worstLanding = 0, worstSkinning = 0;
            int seamSplits = 0, captureFails = 0;
            var landing = new List<string>();
            try
            {
                for (int f = 0; f < 2; f++)
                {
                    bool hdr = f == 0;
                    string fname = hdr ? "HDR" : "8-bit";
                    var format = hdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
                    var atlas = Made(new RenderTexture(W, H, 0, format, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point });
                    var target = Made(new RenderTexture(W, H, 24, format, RenderTextureReadWrite.Default));
                    var grab = Made(new RenderTexture(W, H, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point });
                    var read = Made(new Texture2D(W, H, hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, true));
                    var floats = Made(new Texture2D(W, H, TextureFormat.RGBAFloat, false, true));
                    Shader.SetGlobalVector("_YAPS_Atlas_TexelSize", new Vector4(1f / W, 1f / H, W, H));
                    cam.allowHDR = hdr;
                    cam.targetTexture = target;
                    capCam.targetTexture = grab;

                    void DrawAtlas(Sock[] socks)
                    {
                        var cb = new CommandBuffer();
                        cb.DrawMesh(levelQuads, Matrix4x4.identity, clearMat, 0, 0);
                        foreach (var s in socks)
                        {
                            var sb = new MaterialPropertyBlock();
                            sb.SetFloat("_YAPS_Kind", s.kind);
                            sb.SetFloat("_YAPS_SocketIndex", s.number);
                            var m = Matrix4x4.TRS(origin + s.offset, Quaternion.LookRotation(Vector3.back), Vector3.one);
                            cb.DrawMesh(levelQuads, m, socketMat, 0, 0, sb);
                            cb.DrawMesh(levelQuads, m, socketMat, 0, 1, sb);
                        }
                        atlasCam.targetTexture = atlas;
                        atlasCam.backgroundColor = new Color(0.3f, 0.6f, 0.9f, 1);
                        atlasCam.AddCommandBuffer(CameraEvent.AfterEverything, cb);
                        atlasCam.Render();
                        atlasCam.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
                        cb.Release();
                        Shader.SetGlobalTexture("_YAPS_Atlas", atlas);
                    }

                    var px = new ulong[W * H];
                    ulong[] Frame()
                    {
                        cam.Render();
                        RenderTexture.active = target;
                        read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                        RenderTexture.active = null;
                        if (hdr) read.GetRawTextureData<ulong>().CopyTo(px);
                        else { var raw = read.GetRawTextureData<uint>(); for (int i = 0; i < px.Length; i++) px[i] = raw[i]; }
                        return (ulong[]) px.Clone();
                    }
                    Vector3[] Capture()
                    {
                        capCam.Render();
                        RenderTexture.active = grab;
                        floats.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                        RenderTexture.active = null;
                        var data = floats.GetRawTextureData<float>();
                        var got = new Vector3[total];
                        int seen = 0;
                        for (int i = 0; i < W * H; i++)
                        {
                            float a = data[i * 4 + 3];
                            if (a < 100 || a >= 100 + total) continue;
                            got[(int) a - 100] = new Vector3(data[i * 4], data[i * 4 + 1], data[i * 4 + 2]);
                            seen++;
                        }
                        if (seen != total) { captureFails++; Log($"FAIL capture saw {seen} of {total} vertices"); }
                        return got;
                    }
                    // What the resolver handed the first copy, read at the tip ring's
                    // first four vertices, one chain entry each.
                    void Dump(Case c)
                    {
                        var rows = new Vector3[5][];
                        for (int m = 1; m <= 4; m++)
                        {
                            block.SetFloat("_DupDump", m);
                            capSkin.SetPropertyBlock(block);
                            rows[m] = Capture();
                        }
                        block.SetFloat("_DupDump", 0);
                        capSkin.SetPropertyBlock(block);
                        int t = result.TipVertex & ~3;
                        string Named(Vector3 p)
                        {
                            if (c.socks.Length == 0) return "";
                            var s = c.socks.OrderBy(x => (origin + x.offset - p).sqrMagnitude).First();
                            return $" ({s.name} {(origin + s.offset - p).magnitude * 1000:0.0} mm)";
                        }
                        var entries = Enumerable.Range(0, 4).Select(e =>
                            $"[{e}] kind {rows[2][t + e].x:0} at {(rows[1][t + e] - origin) * 1000:F1}{Named(rows[1][t + e])}");
                        Log($"  chain {c.name}: count {rows[2][t].y:0}, tier {rows[2][t].z:0}, engaged {rows[3][t].x:0.000}, " +
                            $"hits {rows[3][t].y:0} headers {rows[3][t].z:0}; aimed at {(rows[4][t] - origin) * 1000:F1}{Named(rows[4][t])}; " +
                            string.Join(" ", entries));
                    }
                    void Pose(Case c)
                    {
                        plugMid.localRotation = Quaternion.Euler(c.bend, 0, 0);
                        DrawAtlas(c.socks);
                        block.SetFloat("_YAPS_Enabled", c.enabled);
                        block.SetFloat("_YAPS_Debug", c.debug);
                        block.SetFloat("_YAPS_UseAtlas", c.preview ? 0 : 1);
                        var b = origin + B.offset;
                        block.SetVector("_YAPS_SocketFlags", c.preview ? new Vector4(1, 1, 0, 0) : Vector4.zero);
                        block.SetVector("_YAPS_SocketPos", c.preview ? new Vector4(b.x, b.y, b.z, 1) : Vector4.zero);
                        block.SetVector("_YAPS_SocketForward", c.preview ? new Vector4(0, 0, -1, 0) : Vector4.zero);
                        skin.SetPropertyBlock(block);
                        capSkin.SetPropertyBlock(block);
                    }

                    skin.enabled = false;
                    var empty = Frame();
                    skin.enabled = true;

                    foreach (var c in Cases)
                    {
                        Pose(c);
                        var got = Capture();
                        if (f == 0 && c.socks.Length > 0 && c.enabled > 0 && c.debug < 0.5) Dump(c);
                        bool hidden = c.second == null;
                        int atPoint = Enumerable.Range(n, total - n).Count(v => Same(got[v], got[n]));
                        double off = (got[n] - capPoint).magnitude;
                        bool shape = hidden ? atPoint == total - n && off < 1e-3 : Enumerable.Range(n, total - n).All(v => (got[v] - capPoint).magnitude > 1e-3);
                        // Each ring's two seam vertices, which only the UV tells apart.
                        int split = 0;
                        double seam = 0;
                        for (int k = 0; k <= Rings; k++)
                        {
                            int a = k * Columns, z = a + Sides;
                            var pairs = new List<(int, int)> { (a, z) };
                            if (copyOf.TryGetValue(a, out int ca) && copyOf.TryGetValue(z, out int cz)) pairs.Add((ca, cz));
                            foreach (var (x, y) in pairs)
                            {
                                if (!Same(got[x], got[y])) split++;
                                seam = Math.Max(seam, (got[x] - got[y]).magnitude * 1000.0);
                            }
                        }
                        seamSplits += split;
                        worstSeam = Math.Max(worstSeam, seam);

                        string where = "";
                        bool lands = true;
                        if (c.first != null)
                        {
                            Vector3 Of(string name) => origin + Cases.SelectMany(x => x.socks).First(s => s.name == name).offset;
                            double Nearest(bool second, Vector3 s)
                            {
                                double best = 1e9;
                                for (int k = 0; k <= Rings; k++)
                                {
                                    var ring = Enumerable.Range(k * Columns, Sides).Select(v => second ? (copyOf.TryGetValue(v, out int cv) ? cv : -1) : v).ToList();
                                    if (ring.Contains(-1)) continue;
                                    var centre = ring.Aggregate(Vector3.zero, (acc, v) => acc + got[v]) / Sides;
                                    best = Math.Min(best, (centre - s).magnitude);
                                }
                                return best * 1000.0;
                            }
                            double d1 = Nearest(false, Of(c.first));
                            lands = d1 <= RingStep * 1000;
                            where = $"first copy {d1:0.000} mm from {c.first}";
                            worstLanding = Math.Max(worstLanding, d1);
                            if (c.second != null)
                            {
                                double d2 = Nearest(true, Of(c.second));
                                double wrong1 = Nearest(false, Of(c.second)), wrong2 = Nearest(true, Of(c.first));
                                lands &= d2 <= RingStep * 1000 && wrong1 > 3 * RingStep * 1000 && wrong2 > 3 * RingStep * 1000;
                                worstLanding = Math.Max(worstLanding, d2);
                                where += $", second {d2:0.000} mm from {c.second} (first copy's nearest to {c.second} {wrong1:0.0} mm, second's to {c.first} {wrong2:0.0} mm)";
                            }
                            if (f == 0) landing.Add($"{c.name}: {where}");
                            if (f == 0)
                            {
                                int tip = Rings * Columns;
                                var t1 = Enumerable.Range(tip, Sides).Aggregate(Vector3.zero, (acc, v) => acc + got[v]) / Sides;
                                var t2 = Enumerable.Range(tip, Sides).Aggregate(Vector3.zero, (acc, v) => acc + got[copyOf[v]]) / Sides;
                                Log($"  tips {c.name}: first copy at {(t1 - origin) * 1000:F1}, second at {(t2 - origin) * 1000:F1} mm from the origin");
                            }
                        }

                        // Skinning ran: the first copy, untouched while off, is where Unity's own skinning puts it.
                        if (c.enabled <= 0)
                        {
                            var bakedMesh = new Mesh();
                            skin.sharedMesh = dup;
                            skin.BakeMesh(bakedMesh, true);
                            var bv = bakedMesh.vertices;
                            double worst = 0;
                            for (int v = 0; v < n; v++) worst = Math.Max(worst, (bodyGo.transform.TransformPoint(bv[v]) - got[v]).magnitude * 1000.0);
                            Object.DestroyImmediate(bakedMesh);
                            worstSkinning = Math.Max(worstSkinning, worst);
                            where += $"{(where.Length > 0 ? ", " : "")}first copy {worst:0.0000} mm from BakeMesh";
                        }

                        bool ok = shape && split == 0 && lands;
                        if (!ok) captureFails++;
                        Log($"{(ok ? "ok  " : "FAIL")} {fname} {c.name}: {(hidden ? "hidden" : "drawn")}, {atPoint} of {total - n} copy vertices on one point " +
                            $"({off * 1000:0.000} mm from a metre behind the capture camera), seam {split} split ({seam:0.0000} mm)" +
                            (where.Length > 0 ? "; " + where : ""));

                        foreach (var h in hosts)
                        {
                            skin.sharedMaterials = new[] { bodyMat, h.mat };
                            skin.sharedMesh = dup;
                            var withCopy = Frame();
                            skin.sharedMesh = lone;
                            var without = Frame();
                            skin.sharedMesh = dup;
                            long diff = 0, drawn = 0;
                            int first = -1;
                            for (int i = 0; i < px.Length; i++)
                            {
                                if (without[i] != empty[i]) drawn++;
                                if (withCopy[i] == without[i]) continue;
                                diff++;
                                if (first < 0) first = i;
                            }
                            if (hidden) h.hiddenDiff[f] += diff; else h.drawnDiff[f] += diff;
                            bool hostOk = drawn > 0 && (hidden ? diff == 0 : diff > 0);
                            if (!hostOk && h.failures++ < 12)
                                Log($"FAIL {h.name}, {fname}, {c.name}: {diff} pixels differ with the copy's triangles " +
                                    $"({(hidden ? "hidden, want 0" : "drawn, want some")}){(first >= 0 ? $", first at {first % W},{first / W}" : "")}; " +
                                    $"{drawn} drawn without them");
                        }
                    }

                    // Across where the second copy starts drawing, on a bent plug: its
                    // corners decide one by one, from roots the bend sets apart.
                    if (hdr && sweep > 0)
                    {
                        var simple = hosts[0];
                        skin.sharedMaterials = new[] { bodyMat, simple.mat };
                        var dir = B.offset.normalized;
                        int maxMixed = 0, mixedSteps = 0;
                        float lastDrawn = -1;
                        long maxStray = 0, strayAll = 0;
                        string worstAt = "";
                        for (int step = 0; step < sweep; step++)
                        {
                            float d = L * Mathf.Lerp(sweepFrom, sweepTo, sweep > 1 ? step / (sweep - 1f) : 0);
                            var c = new Case { name = "sweep", bend = 10,
                                socks = new[] { A, new Sock { name = "B", offset = dir * d, kind = 1, number = 2 } } };
                            Pose(c);
                            var got = Capture();
                            var hid = new bool[total];
                            for (int v = n; v < total; v++) hid[v] = (got[v] - capPoint).magnitude < 1e-3;
                            int mixed = 0, hiddenCount = hid.Count(x => x);
                            for (int t = 0; t + 2 < copyTriangles.Length; t += 3)
                            {
                                int k = (hid[copyTriangles[t]] ? 1 : 0) + (hid[copyTriangles[t + 1]] ? 1 : 0) + (hid[copyTriangles[t + 2]] ? 1 : 0);
                                if (k == 1 || k == 2) mixed++;
                            }
                            // The plug on screen, from every vertex not on the hidden point.
                            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                            for (int v = 0; v < total; v++)
                            {
                                if (hid[v]) continue;
                                var sp = cam.WorldToScreenPoint(got[v]);
                                if (sp.z <= 0) continue;
                                x0 = Math.Min(x0, sp.x); y0 = Math.Min(y0, sp.y); x1 = Math.Max(x1, sp.x); y1 = Math.Max(y1, sp.y);
                            }
                            const float margin = 32;
                            skin.sharedMesh = dup;
                            var withCopy = Frame();
                            skin.sharedMesh = lone;
                            var without = Frame();
                            skin.sharedMesh = dup;
                            long stray = 0, diff = 0;
                            for (int i = 0; i < px.Length; i++)
                            {
                                if (withCopy[i] == without[i]) continue;
                                diff++;
                                int x = i % W, y = i / W;
                                if (x < x0 - margin || x > x1 + margin || y < y0 - margin || y > y1 + margin) stray++;
                            }
                            if (mixed > 0) mixedSteps++;
                            if (hiddenCount < total - n) lastDrawn = d / L;
                            if (mixed > maxMixed || stray > maxStray) worstAt = $"{d / L:0.0000} L: {mixed} mixed, {stray} stray of {diff}, {hiddenCount} hidden";
                            maxMixed = Math.Max(maxMixed, mixed);
                            maxStray = Math.Max(maxStray, stray);
                            strayAll += stray;
                            if (step % 5 == 0 || mixed > 0 || (hiddenCount > 0 && hiddenCount < total - n))
                                Log($"sweep {d / L:0.00000} L: {hiddenCount} of {total - n} copy vertices hidden, {mixed} mixed triangles, " +
                                    $"{diff} pixels differ, {stray} outside the plug");
                        }
                        Log($"INFO sweep, bent 10 deg, second hole {sweepFrom} to {sweepTo} L in {sweep} steps: second copy drawn out to {lastDrawn:0.0000} L; {mixedSteps} steps with mixed triangles, " +
                            $"at most {maxMixed}; at most {maxStray} pixels outside the plug ({strayAll} over the sweep); worst {worstAt}");
                    }
                }
            }
            finally
            {
                cam.targetTexture = null;
                capCam.targetTexture = null;
                atlasCam.targetTexture = null;
                foreach (var o in made)
                {
                    // A bake may have saved one; the asset sweep takes those.
                    if (o == null || EditorUtility.IsPersistent(o)) continue;
                    if (o is RenderTexture rt) rt.Release();
                    Object.DestroyImmediate(o);
                }
            }

            foreach (var h in hosts)
                for (int f = 0; f < 2; f++)
                    Check(h.hiddenDiff[f] == 0 && h.failures == 0, $"{h.name}, {(f == 0 ? "HDR" : "8-bit")}: hidden copy {h.hiddenDiff[f]} pixels over " +
                        $"{Cases.Count(c => c.second == null)} cases; drawn copy {h.drawnDiff[f]} pixels over {Cases.Count(c => c.second != null)}");
            foreach (string l in landing) Log("  landing " + l);
            Check(captureFails == 0, $"captures: {captureFails} failed; landing within {worstLanding:0.000} mm, bound {RingStep * 1000:0.000} (a ring step)");
            Check(seamSplits == 0 && worstSeam == 0, $"seams: {seamSplits} split, at most {worstSeam:0.0000} mm, both copies");
            Check(worstSkinning <= 0.01, $"skinning ran: the first copy within {worstSkinning:0.0000} mm of BakeMesh while off");
            return all;
        }

        // Copies carry their originals' every attribute.
        static void AttributesCheck(Mesh src, Mesh dup, int[] from)
        {
            int n = src.vertexCount, bad = 0;
            var sv = src.vertices; var dv = dup.vertices;
            var sn = src.normals; var dn = dup.normals;
            var st = src.tangents; var dt = dup.tangents;
            var sc = src.colors; var dc = dup.colors;
            var su = new List<Vector2>(); var du = new List<Vector2>();
            src.GetUVs(0, su); dup.GetUVs(0, du);
            var sw = src.boneWeights; var dw = dup.boneWeights;
            var sd = new Vector3[n]; var sdn = new Vector3[n]; var sdt = new Vector3[n];
            var dd = new Vector3[dup.vertexCount]; var ddn = new Vector3[dup.vertexCount]; var ddt = new Vector3[dup.vertexCount];
            src.GetBlendShapeFrameVertices(0, 0, sd, sdn, sdt);
            dup.GetBlendShapeFrameVertices(0, 0, dd, ddn, ddt);
            for (int i = 0; i < from.Length; i++)
            {
                int o = from[i], c = n + i;
                if (dv[c] != sv[o] || dn[c] != sn[o] || dt[c] != st[o] || dc[c] != sc[o] || du[c] != su[o]
                    || !dw[c].Equals(sw[o]) || dd[c] != sd[o] || ddn[c] != sdn[o] || ddt[c] != sdt[o]) bad++;
            }
            bool originals = Enumerable.Range(0, n).All(v => dv[v] == sv[v] && dw[v].Equals(sw[v]));
            Check(bad == 0 && originals && dup.blendShapeCount == src.blendShapeCount && dup.bindposes.SequenceEqual(src.bindposes)
                  && dup.indexFormat == IndexFormat.UInt16,
                $"attributes: {bad} of {from.Length} copies differ from their originals (position, normal, tangent, colour, uv, " +
                $"bone weights, blendshape deltas); originals untouched {originals}; {dup.blendShapeCount} shapes; index format {dup.indexFormat} at {dup.vertexCount} vertices");
        }

        // Every copy's bake entry is its original's, bit for bit: ten floats and
        // each shape block's nine.
        static void BakeCheck(float[] baked, float[] plainFloats, YapsBaker.Result result, YapsBaker.Result plain,
            int[] from, int n, int total)
        {
            int shapes = result.Shapes.Count, bad = 0, inactive = 0;
            int Base(int v) => 1 + v * 10;
            int Shape(int s, int v) => 1 + total * 10 + s * total * 9 + v * 9;
            bool Bits(int a, int b, int len)
            {
                for (int i = 0; i < len; i++)
                    if (BitConverter.SingleToInt32Bits(baked[a + i]) != BitConverter.SingleToInt32Bits(baked[b + i])) return false;
                return true;
            }
            for (int i = 0; i < from.Length; i++)
            {
                int o = from[i], c = n + i;
                bool same = Bits(Base(c), Base(o), 10);
                for (int s = 0; s < shapes; s++) same &= Bits(Shape(s, c), Shape(s, o), 9);
                if (!same) bad++;
                if (baked[Base(c) + 9] <= 0 || baked[Base(c) + 2] < 0) inactive++;
            }
            bool sized = result.VertexCount == total && baked.Length >= 1 + total * 10 + shapes * total * 9;
            double drift = (result.Origin - plain.Origin).magnitude * 1000.0, turn = Quaternion.Angle(result.Rotation, plain.Rotation);
            Check(bad == 0 && sized && result.AnchorVertex < n && result.TipVertex < n,
                $"copies bake: {bad} of {from.Length} copy entries differ from their originals (10 floats and {shapes} shape block); " +
                $"{result.VertexCount} vertices baked; anchor {result.AnchorVertex} tip {result.TipVertex}, both originals (copies from {n})");
            Log($"INFO bake frame against dupFrom's own bake: origin {drift:0.000000} mm, axis {turn:0.000000} deg, " +
                $"length {(result.Length - plain.Length) * 1000:0.000000} mm; {inactive} copies read inactive at rest (behind the base), " +
                $"which ride on their originals while the copy draws");
        }

        // The UInt16 limit: copies that cross it get 32-bit indices, and every
        // copied triangle still names the copies of its originals.
        static void IndexFormatCheck()
        {
            const int count = 40000, plug = 30000;
            var hips = new GameObject("__DupBigHips") { hideFlags = HideFlags.DontSave }.transform;
            var root = new GameObject("__DupBigRoot") { hideFlags = HideFlags.DontSave }.transform;
            root.SetParent(hips, false);
            try
            {
                var pos = new Vector3[count];
                var w = new BoneWeight[count];
                for (int i = 0; i < count; i++)
                {
                    pos[i] = new Vector3(i % 200 * 0.01f, i / 200 * 0.01f, 0);
                    w[i] = new BoneWeight { boneIndex0 = i < plug ? 1 : 0, weight0 = 1 };
                }
                var tris = new List<int>();
                for (int i = 0; i + 2 < count; i++) tris.AddRange(new[] { i, i + 1, i + 2 });
                var mesh = new Mesh { name = "DupBig", vertices = pos, boneWeights = w, bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity } };
                mesh.SetTriangles(tris, 0);
                var dup = Build(mesh, 0, new[] { hips, root }, root, out int[] from, out string why);
                bool ok = dup != null && mesh.indexFormat == IndexFormat.UInt16 && dup.indexFormat == IndexFormat.UInt32;
                int bad = 0, maxIndex = 0, added = 0;
                if (dup != null)
                {
                    var got = dup.GetIndices(0);
                    var dv = dup.vertices;
                    added = got.Length - tris.Count;
                    for (int i = 0; i < tris.Count; i++) if (got[i] != tris[i]) bad++;
                    // The copied triangles, in order, are the all-plug ones of the source.
                    int at = tris.Count;
                    for (int t = 0; t + 2 < tris.Count; t += 3)
                    {
                        if (tris[t + 2] >= plug) continue;
                        for (int c = 0; c < 3; c++)
                        {
                            int v = got[at++];
                            maxIndex = Math.Max(maxIndex, v);
                            if (v < count || dv[v] != pos[tris[t + c]]) bad++;
                        }
                    }
                    ok &= dup.vertexCount == count + from.Length && at == got.Length && bad == 0 && maxIndex > 65535;
                }
                Check(ok, $"index format: {count} vertices on {mesh.indexFormat}, {from?.Length} copied, " +
                    (dup != null ? $"{dup.vertexCount} in all on {dup.indexFormat}" : why) +
                    $"; {added / 3} triangles added, highest index {maxIndex}, {bad} wrong");
                if (dup != null) Object.DestroyImmediate(dup);
                Object.DestroyImmediate(mesh);
            }
            finally
            {
                Object.DestroyImmediate(hips.gameObject);
            }
        }

        // The tube, its seam column the first again with u = 1, normals out,
        // tangents round. "Thick" swells the plug part.
        static Mesh PlugMesh()
        {
            var pos = new Vector3[Verts];
            var nrm = new Vector3[Verts];
            var tan = new Vector4[Verts];
            var uv = new Vector2[Verts];
            var col = new Color[Verts];
            var bw = new BoneWeight[Verts];
            var thick = new Vector3[Verts];
            var rng = new System.Random(20261018);
            var tint = Enumerable.Range(0, (Rings + 1) * Sides).Select(_ =>
                new Color(0.2f + 0.8f * (float) rng.NextDouble(), (float) rng.NextDouble(), (float) rng.NextDouble(), 0.2f + 0.8f * (float) rng.NextDouble())).ToArray();
            for (int v = 0; v < Verts; v++)
            {
                int k = v / Columns, j = v % Columns;
                float a = 2 * Mathf.PI * (j % Sides) / Sides, z = RingZ(k);
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                pos[v] = new Vector3(Radius * c, Radius * s, z);
                nrm[v] = new Vector3(c, s, 0);
                tan[v] = new Vector4(-s, c, 0, 1);
                uv[v] = new Vector2((float) j / Sides, (float) k / Rings);
                col[v] = tint[k * Sides + j % Sides];
                float plug = Mathf.Clamp01(z / Feather), mid = Mathf.Clamp01((z - (MidZ - 0.03f)) / 0.06f);
                var parts = new[] { (0, 1 - plug), (1, plug * (1 - mid)), (2, plug * mid) }
                    .Where(p => p.Item2 > 0).OrderByDescending(p => p.Item2).ToArray();
                bw[v] = new BoneWeight
                {
                    boneIndex0 = parts[0].Item1, weight0 = parts[0].Item2,
                    boneIndex1 = parts.Length > 1 ? parts[1].Item1 : 0, weight1 = parts.Length > 1 ? parts[1].Item2 : 0,
                };
                thick[v] = nrm[v] * (0.004f * plug);
            }
            var tris = new List<int>();
            for (int k = 0; k < Rings; k++)
                for (int j = 0; j < Sides; j++)
                {
                    int a = k * Columns + j, b = a + 1, c = a + Columns, e = c + 1;
                    tris.AddRange(new[] { a, c, b, b, c, e });
                }
            var mesh = new Mesh { name = "DupPlug", vertices = pos, normals = nrm, tangents = tan, uv = uv, colors = col, boneWeights = bw };
            mesh.SetTriangles(tris, 0);
            mesh.AddBlendShapeFrame("Thick", 100, thick, new Vector3[Verts], new Vector3[Verts]);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Simple Lit first: the opaque baseline, and the material the split is given.
        static List<Host> Hosts(BridgeReport report, Func<string, bool> wanted)
        {
            var hosts = new List<Host>();
            Shader Patched(Material m, string what)
            {
                var s = YapsShaderPatcher.Patch(m, report, out string refusal, out int shadows);
                Log($"{what}: {(s != null ? $"patched to \"{s.name}\"" : "refused: " + refusal)}" +
                    (shadows > 0 ? $", {shadows} shadow passes left straight" : ""));
                return s;
            }
            void Add(string name, Material source, Shader patched)
            {
                if (patched == null) { Check(false, $"{name}: no patched shader to draw"); return; }
                var m = new Material(YapsLock.Basis(source)) { name = name };
                m.shader = patched;
                m.renderQueue = source.renderQueue;
                AssetDatabase.CreateAsset(m, $"{Folder}/{Regex.Replace(name, "[^A-Za-z0-9]+", "-")}.mat");
                hosts.Add(new Host { name = name, mat = m });
            }

            var lit = new Material(Shader.Find("YAPS/Simple Lit")) { name = "Simple Lit" };
            Add("Simple Lit", lit, Patched(lit, "YAPS Simple Lit"));
            if (hosts.Count == 0) throw new InvalidOperationException("YAPS Simple Lit did not patch");

            var mask = Texture((x, y) => { float g = 0.2f + 0.8f * ((x * 7 + y * 13) % 64) / 63f; return new Color(g, g, g, 1); });
            AssetDatabase.CreateAsset(mask, $"{Folder}/Mask.asset");
            var poiSets = new (string name, Action<Material> set)[]
            {
                // Fade, as its preset writes it, at half alpha: a copy drawn twice shows.
                ("Poiyomi Fade", m =>
                {
                    m.SetFloat("_Mode", 2);
                    m.renderQueue = 3000;
                    m.SetOverrideTag("RenderType", "Transparent");
                    foreach (var (p, v) in new[] { ("_AlphaForceOpaque", 0f), ("_BlendOp", 0f), ("_BlendOpAlpha", 4f), ("_Cutoff", 0.002f),
                                 ("_SrcBlend", 5f), ("_DstBlend", 10f), ("_SrcBlendAlpha", 1f), ("_DstBlendAlpha", 1f), ("_AddSrcBlend", 5f),
                                 ("_AddDstBlend", 1f), ("_AddSrcBlendAlpha", 0f), ("_AddDstBlendAlpha", 1f), ("_AlphaToCoverage", 0f),
                                 ("_ZWrite", 0f), ("_ZTest", 4f), ("_AlphaPremultiply", 0f), ("_OutlineSrcBlend", 5f), ("_OutlineDstBlend", 10f) })
                        m.SetFloat(p, v);
                    m.SetColor("_Color", new Color(1, 1, 1, 0.5f));
                }),
                ("Poiyomi outline, mask and vertex red", m => Outline(m, mask)),
                ("Poiyomi outline, vertex colour normals, local", m => { Outline(m, mask); m.SetFloat("_OutlineUseVertexColorNormals", 1); }),
            };
            var poiMats = new List<(string name, Material m)>();
            var poi = AssetDatabase.LoadAssetAtPath<Shader>(PoiyomiPath);
            if (poiSets.Any(s => wanted(s.name)))
            {
                if (poi == null || YapsLock.ThryLocked(poi) != false) throw new InvalidOperationException("no unlocked Poiyomi Toon at " + PoiyomiPath);
                for (int i = 0; i < poiSets.Length; i++)
                {
                    if (!wanted(poiSets[i].name)) continue;
                    var m = new Material(poi);
                    poiSets[i].set(m);
                    AssetDatabase.CreateAsset(m, $"{Folder}/Poi{i}.mat");
                    poiMats.Add((poiSets[i].name, m));
                }
                YapsLock.Prepare(poiMats.Select(x => x.m));
                foreach (var (name, m) in poiMats)
                {
                    var basis = YapsLock.Basis(m);
                    if (YapsLock.ThryLocked(basis.shader) != true) { Check(false, $"{name}: Poiyomi's lock did not run"); continue; }
                    Add(name, m, Patched(m, name));
                }
            }

            const string lilName = "lilToon outline, mask and vertex red";
            if (wanted(lilName))
            {
                // Its drawing passes are UsePass into Hidden/ltspass_opaque, which the
                // shipped patch leaves unpatched; the pass shader itself, with lilToon's
                // own switches for the inputs the deform reads, is what draws here.
                var opaque = Shader.Find("Hidden/ltspass_opaque") ?? throw new InvalidOperationException("no Hidden/ltspass_opaque");
                string opaquePath = AssetDatabase.GetAssetPath(opaque);
                string forced = Regex.Replace(Lf(File.ReadAllText(opaquePath)), @"^(\s*)HLSLPROGRAM\s*$",
                    m => m.Value + "\n#define LIL_REQUIRE_APP_NORMAL\n#define LIL_REQUIRE_APP_TANGENT\n#define LIL_REQUIRE_APP_VERTEXID",
                    RegexOptions.Multiline);
                forced = new Regex("Shader\\s+\"[^\"]*\"").Replace(forced, "Shader \"Hidden/DupSpike/ltspass_opaque\"", 1);
                string forcedPath = Path.GetDirectoryName(opaquePath).Replace('\\', '/') + "/DupSpike_ltspass_opaque.shader";
                File.WriteAllText(forcedPath, forced);
                AssetDatabase.ImportAsset(forcedPath, ImportAssetOptions.ForceSynchronousImport);
                var pass = Compiled(AssetDatabase.LoadAssetAtPath<Shader>(forcedPath), forcedPath);
                var m = new Material(pass) { name = lilName };
                m.SetFloat("_UseOutline", 1);
                m.SetFloat("_OutlineWidth", 0.5f);
                m.SetTexture("_OutlineWidthMask", mask);
                m.SetFloat("_OutlineVertexR2Width", 1);
                Add(lilName, m, Patched(new Material(pass), "lilToon's opaque pass shader, inputs on"));
            }
            return hosts;
        }

        static void Outline(Material m, Texture mask)
        {
            m.SetFloat("_EnableOutlines", 1);
            m.SetFloat("_LineWidth", 1);
            m.SetTexture("_OutlineMask", mask);
            m.SetFloat("_OutlineVertexColorMask", 1);
        }

        // The patcher's defaults; the per-pose values ride the block too, so
        // nothing depends on what a material carried.
        static readonly Dictionary<string, float> Defaults = new Dictionary<string, float>
        {
            ["_YAPS_Overrun"] = 1, ["_YAPS_BakeScale"] = 1, ["_YAPS_BakeGirth"] = 1, ["_YAPS_FrameFromVertex"] = 1,
            ["_YAPS_SelfTag"] = -1, ["_YAPS_UseAtlas"] = 1, ["_YAPS_TaperStart"] = 0.1f, ["_YAPS_TaperEnd"] = 0.3f,
            ["_YAPS_IdleLength"] = 1, ["_YAPS_IdleWidth"] = 1, ["_YAPS_SqueezeDistance"] = 0.15f, ["_YAPS_BulgeDistance"] = 0.2f,
            ["_YAPS_PumpSpeed"] = 6, ["_YAPS_PumpWidth"] = 1, ["_YAPS_WriggleSpeed"] = 2, ["_YAPS_BezierSmoothness"] = 1,
            ["_YAPS_Owner"] = 0, ["_YAPS_Debug"] = 0, ["_YAPS_PortalSpan"] = 0, ["_YAPS_ChannelSpace"] = 0, ["_YAPS_Enabled"] = 1,
        };

        static float[] Floats(Texture2D bake)
        {
            var request = AsyncGPUReadback.Request(bake, 0);
            request.WaitForCompletion();
            if (request.hasError) throw new InvalidOperationException("the bake did not read back");
            return request.GetData<float>().ToArray();
        }

        // A probe shader from beside the spike copies, pointed at the project's own.
        static Shader Template(string spike, string file, string shipped)
        {
            string text = Lf(File.ReadAllText(Path.Combine(spike, file + ".shader")));
            text = Regex.Replace(text, "#include\\s+\"(yaps_\\w+\\.cginc)\"", m => $"#include \"{shipped}/{m.Groups[1].Value}\"");
            text = new Regex("Shader\\s+\"[^\"]*\"").Replace(text, $"Shader \"Hidden/DupSpike/{file}\"", 1);
            string path = $"{Folder}/{file}.shader";
            File.WriteAllText(path, text);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return Compiled(AssetDatabase.LoadAssetAtPath<Shader>(path), path);
        }

        static Shader Compiled(string name) => Compiled(Shader.Find(name), name);

        static Shader Compiled(Shader s, string name)
        {
            if (s == null) throw new InvalidOperationException(name + " not found");
            foreach (var m in ShaderUtil.GetShaderMessages(s))
                Log($"  {name}: {m.severity} {m.message} line {m.line}");
            if (!s.isSupported || ShaderUtil.ShaderHasError(s)) throw new InvalidOperationException(name + " did not compile");
            return s;
        }

        static Texture2D Texture(Func<int, int, Color> at)
        {
            var t = new Texture2D(64, 64, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    t.SetPixel(x, y, at(x, y));
            t.Apply(false, false);
            return t;
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

        static HashSet<string> Entries(string root) => new HashSet<string>(
            Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
                .Select(p => p.Replace('\\', '/')).Where(p => !p.EndsWith(".meta")));

        static bool Same(Vector3 x, Vector3 y) =>
            BitConverter.SingleToInt32Bits(x.x) == BitConverter.SingleToInt32Bits(y.x)
            && BitConverter.SingleToInt32Bits(x.y) == BitConverter.SingleToInt32Bits(y.y)
            && BitConverter.SingleToInt32Bits(x.z) == BitConverter.SingleToInt32Bits(y.z);

        static string Lf(string s) => s.Replace("\r\n", "\n");

        static void Log(string s) => Debug.Log(Tag + s);

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
#endif
