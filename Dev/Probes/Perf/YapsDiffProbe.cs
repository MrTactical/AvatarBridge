#if UNITY_EDITOR
// Two copies of the YAPS includes, run on the same random squash states, compared vertex by
// vertex. A rewrite meant to compile faster must move no vertex, and the squash has no other
// readback: nothing in a scene presses every source at once.
//
//   -executeMethod AvatarBridge.Regression.YapsDiffProbe.Run
//       -ydOld <folder of .cginc> [-ydNew <folder>] [-ydScenes 400] [-ydSeed 1]
//
// -ydNew defaults to the project's own YAPS includes. Each scene randomises the slots, the
// reaches, the own arms, other players' hands in every gesture and plug lights, then runs
// YapsDentDeform over 4096 vertices round the parts in a compute shader built from each copy.
// Exit code 0 when no vertex moves 0.01 mm apart and no normal 0.001: float rounding from
// reordered arithmetic measured 0.002 mm. No -quit: it exits.
//
// -ydWild [-ydCap 1.5] instead feeds the skinned slot buffer and spoils it: frozen where the body
// stood a moment ago and turned since, the first rest slot right and the rest wherever, or one
// slot huge or NaN. Each copy's worst move is logged in reaches of the vertex's own parts, and
// the run passes when the new copy never moves a vertex past the cap and never writes a NaN.
// The client can bind a buffer nothing skinned in the wearer's view, and remote copies blew up
// along metres-long streaks.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = System.Random;

namespace AvatarBridge.Regression
{
    public static class YapsDiffProbe
    {
        const string Root = "Assets/__YD";
        const int Count = 4096;
        static void Log(string s) => Debug.Log("[YapsDiff] " + s);

        const string Kernel = @"#pragma kernel Main
#define UNITY_UV_STARTS_AT_TOP 1
// A compute shader leaves SHADER_TARGET unset, which compiled the skinned slots out.
#ifndef SHADER_TARGET
#define SHADER_TARGET 50
#endif
float4 _ScreenParams;
float4 _ProjectionParams;
float4 unity_4LightAtten0;
float4 unity_LightColor[8];
float4 unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0;
float4x4 unity_ObjectToWorld, unity_WorldToObject;
float4 _Time;
float3 _WorldSpaceCameraPos;
float4x4 unity_MatrixV;
#define UNITY_MATRIX_V unity_MatrixV
#include ""yaps_props.cginc""
#include ""yaps_atlas.cginc""
#include ""yaps_resolve.cginc""
#include ""yaps_dent.cginc""
StructuredBuffer<float4> _In;
RWStructuredBuffer<float4> _Out;
uint _Count;
[numthreads(64, 1, 1)]
void Main(uint id : SV_DispatchThreadID)
{
    if (id >= _Count) return;
    float3 p = _In[id * 3].xyz, n = _In[id * 3 + 1].xyz, t = _In[id * 3 + 2].xyz;
    YapsDentDeform(p, n, t, id);
    _Out[id * 3] = float4(p, 0);
    _Out[id * 3 + 1] = float4(n, 0);
    _Out[id * 3 + 2] = float4(t, 0);
}
";

        public static void Run()
        {
            int code = 2;
            try { code = Measure(); }
            catch (Exception e) { Debug.LogException(e); }
            finally { AssetDatabase.DeleteAsset(Root); }
            Log("DONE");
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static ComputeShader Build(string name, string from)
        {
            string dir = $"{Root}/{name}";
            Directory.CreateDirectory(dir);
            foreach (string f in Directory.GetFiles(from, "*.cginc")) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);
            File.WriteAllText($"{dir}/diff.compute", Kernel);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>($"{dir}/diff.compute");
            if (cs == null || !cs.HasKernel("Main")) throw new Exception($"{name}: the kernel did not compile");
            return cs;
        }

        static int Measure()
        {
            string oldDir = Arg("-ydOld") ?? throw new Exception("-ydOld missing");
            string newDir = Arg("-ydNew") ?? Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(
                AssetDatabase.FindAssets("yaps_dent").First(g => AssetDatabase.GUIDToAssetPath(g).EndsWith("yaps_dent.cginc"))));
            int scenes = int.TryParse(Arg("-ydScenes"), out int s) ? s : 400;
            var rng = new Random(int.TryParse(Arg("-ydSeed"), out int seed) ? seed : 1);
            bool wild = Environment.GetCommandLineArgs().Contains("-ydWild");
            float cap = float.TryParse(Arg("-ydCap"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float cp) ? cp : 1.5f;
            Log($"old {oldDir}, new {newDir}, {scenes} scenes of {Count} vertices" + (wild ? $", wild slots, cap {cap} reaches" : ""));
            var shaders = new[] { Build("old", oldDir), Build("new", newDir) };

            var input = new ComputeBuffer(Count * 3, 16);
            var output = new ComputeBuffer(Count * 3, 16);
            var skin = new ComputeBuffer(60, 4, ComputeBufferType.Raw);
            var own = new Texture2D(64, Count / 64, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
            var got = new Vector4[2][] { new Vector4[Count * 3], new Vector4[Count * 3] };
            double worstP = 0, worstN = 0;
            long moved = 0, nanMismatch = 0, bad = 0;
            var farthest = new double[2];
            var nans = new long[2];
            var byKind = new System.Collections.Generic.SortedDictionary<string, double>[] { new System.Collections.Generic.SortedDictionary<string, double>(), new System.Collections.Generic.SortedDictionary<string, double>() };
            var clean = new Vector4[Count * 3];
            try
            {
                for (int scene = 0; scene < scenes; scene++)
                {
                    var verts = Scene(rng, shaders, own, skin, wild, out float[] lim, out float[] cleanSlots, out float[] spoiltSlots, out string kind, out string what);
                    input.SetData(verts);
                    for (int k = 0; k < 2; k++)
                    {
                        var cs = shaders[k];
                        cs.SetBuffer(0, "_In", input);
                        cs.SetBuffer(0, "_Out", output);
                        cs.SetBuffer(0, "_YAPS_SquashSkin", skin);
                        cs.SetTexture(0, "_YAPS_DentOwn", own);
                        cs.SetInt("_Count", Count);
                        // Spoilt, measured against the same scene with its slots as they should be:
                        // the scenes alone press harder than any avatar does.
                        if (wild)
                        {
                            skin.SetData(cleanSlots);
                            cs.Dispatch(0, Count / 64, 1, 1);
                            output.GetData(clean);
                            skin.SetData(spoiltSlots);
                        }
                        cs.Dispatch(0, Count / 64, 1, 1);
                        output.GetData(got[k]);
                        double sceneFar = 0;
                        for (int i = 0; i < Count; i++)
                        {
                            Vector3 by = (Vector3) got[k][i * 3] - (Vector3) (wild ? clean[i * 3] : verts[i * 3]);
                            if (float.IsNaN(by.x + by.y + by.z) || float.IsInfinity(by.x + by.y + by.z)) { nans[k]++; continue; }
                            if (lim[i] > 0) sceneFar = Math.Max(sceneFar, by.magnitude / lim[i]);
                        }
                        farthest[k] = Math.Max(farthest[k], sceneFar);
                        byKind[k][kind] = Math.Max(byKind[k].TryGetValue(kind, out double was) ? was : 0, sceneFar);
                        if (wild && k == 1 && kind != "frozen" && sceneFar > 0.01) Log($"scene {scene} {kind} moved {sceneFar:0.000}: {what}");
                    }
                    if (wild) continue;
                    double sceneP = 0, sceneN = 0;
                    for (int i = 0; i < Count; i++)
                    {
                        for (int c = 0; c < 2; c++)
                        {
                            Vector4 a = got[0][i * 3 + c], b = got[1][i * 3 + c];
                            bool na = float.IsNaN(a.x + a.y + a.z), nb = float.IsNaN(b.x + b.y + b.z);
                            if (na != nb) { nanMismatch++; continue; }
                            if (na) continue;
                            double d = ((Vector3) a - (Vector3) b).magnitude;
                            if (c == 0) sceneP = Math.Max(sceneP, d); else sceneN = Math.Max(sceneN, d);
                        }
                        if (((Vector3) got[0][i * 3] - (Vector3) verts[i * 3]).sqrMagnitude > 1e-14f) moved++;
                    }
                    worstP = Math.Max(worstP, sceneP);
                    worstN = Math.Max(worstN, sceneN);
                    if (sceneP > 1e-5 || sceneN > 1e-3)
                    {
                        bad++;
                        if (bad <= 20) Log($"scene {scene} DIFFERS: position {sceneP * 1000:0.000000} mm, normal {sceneN:0.000000}; {what}");
                    }
                }
            }
            finally
            {
                input.Release();
                output.Release();
                skin.Release();
                UnityEngine.Object.DestroyImmediate(own);
            }
            for (int k = 0; k < 2; k++)
                Log($"{(k == 0 ? "old" : "new")}: farthest move {farthest[k]:0.000} reaches" + (wild ? " off the clean result (" + string.Join(", ", byKind[k].Select(e => $"{e.Key} {e.Value:0.000}")) + ")" : "") + $", {nans[k]} vertices NaN or infinite");
            if (wild)
            {
                // Frozen slots read as a body standing still a moment ago, which no shader can
                // tell from a real one: logged, fixed where the buffer stops being skinned.
                double spoiled = byKind[1].Where(e => e.Key != "frozen").Select(e => e.Value).DefaultIfEmpty(0).Max();
                bool held = spoiled <= cap + 1e-4 && nans[1] == 0;
                Log((held ? "PASS" : "FAIL") + $": the new copy {(held ? "held" : "broke")} the {cap} reach cap on spoilt slots");
                return held ? 0 : 1;
            }
            Log($"vertices moved by the squash: {moved} of {(long) scenes * Count} ({100.0 * moved / ((long) scenes * Count):0.0}%)");
            Log($"worst position difference {worstP * 1000:0.000000} mm, worst normal difference {worstN:0.000000}, NaN mismatches {nanMismatch}");
            bool pass = bad == 0 && nanMismatch == 0 && moved > 0;
            Log((pass ? "PASS" : "FAIL") + $": {bad} of {scenes} scenes differ");
            return pass ? 0 : 1;
        }

        static float F(Random r, float a, float b) => a + (float) r.NextDouble() * (b - a);
        static bool P(Random r, double p) => r.NextDouble() < p;
        static Vector3 V(Random r, float s) => new Vector3(F(r, -s, s), F(r, -s, s), F(r, -s, s));
        static Vector3 Dir(Random r)
        {
            Vector3 v;
            do v = V(r, 1); while (v.sqrMagnitude < 0.01f || v.sqrMagnitude > 1);
            return v.normalized;
        }

        // One random state for every uniform the squash reads, and vertices round its parts.
        static Vector4[] Scene(Random r, ComputeShader[] shaders, Texture2D own, ComputeBuffer skin, bool wild, out float[] lim, out float[] cleanSlots, out float[] spoiltSlots, out string spoilt, out string what)
        {
            var o2w = P(r, 0.5) ? Matrix4x4.identity
                : Matrix4x4.TRS(V(r, 1), Quaternion.LookRotation(Dir(r), Dir(r)), Vector3.one * F(r, 0.5f, 2f));
            var slots = new Matrix4x4[20];
            var centres = new Vector3[4];
            float[] R = new float[4];
            var units = new float[4];
            var reach = new Vector4[6];
            bool second = P(r, 0.5);
            for (int m = 0; m < 4; m++)
            {
                R[m] = F(r, 0.04f, 0.1f);
                float side = (m & 1) == 0 ? -1 : 1;
                centres[m] = new Vector3(side * F(r, 0.04f, 0.08f), (m < 2 ? 1.3f : 0.9f) + F(r, -0.02f, 0.02f), 0.1f + F(r, -0.02f, 0.02f));
            }
            for (int m = 0; m < 4; m++)
            {
                bool present = m < 2 || second;
                float unit = units[m] = F(r, 0.5f, 2f);
                if (present && !P(r, 0.05))
                    slots[m] = Matrix4x4.TRS(centres[m], Quaternion.LookRotation(Dir(r), Dir(r)), Vector3.one * unit);
                // The rest slot: near the centre, up axis toward where the part hangs from.
                if (present && !P(r, 0.1))
                {
                    var hangDir = (Vector3.up + V(r, 0.3f)).normalized;
                    slots[4 + m] = Matrix4x4.TRS(centres[m] + V(r, P(r, 0.5) ? 0.03f : 0.002f),
                        Quaternion.LookRotation(Vector3.Cross(Dir(r), hangDir).normalized, hangDir), Vector3.one * unit);
                }
                float[] k = { 1, 1, 1, 1, 1, 1 };
                for (int j = 0; j < 6; j++)
                {
                    k[j] = F(r, 0.6f, 1.4f) * R[m] / unit;
                    if (P(r, 0.03)) k[j] = 0;
                    reach[j][m] = k[j];
                }
            }
            // Arms: shoulder, elbow, hand, finger each side, the hand often on a part.
            for (int side = 0; side < 2; side++)
            {
                var shoulder = new Vector3(side == 0 ? -0.2f : 0.2f, 1.45f, 0);
                var hand = P(r, 0.6) ? centres[r.Next(second ? 4 : 2)] + Dir(r) * F(r, 0, 0.12f) : shoulder + V(r, 0.6f);
                var elbow = Vector3.Lerp(shoulder, hand, 0.5f) + V(r, 0.1f);
                var finger = hand + Dir(r) * 0.08f;
                var pts = new[] { shoulder, elbow, hand, finger };
                for (int j = 0; j < 4; j++)
                    if (!P(r, 0.05)) slots[8 + side * 4 + j] = Matrix4x4.TRS(pts[j], Quaternion.identity, Vector3.one);
            }
            for (int pair = 0; pair < 2; pair++)
            {
                var mid = 0.5f * (centres[pair * 2] + centres[pair * 2 + 1]);
                if (!P(r, 0.05)) slots[16 + pair] = Matrix4x4.TRS(mid + new Vector3(0, F(r, -0.03f, 0.03f), -0.1f), Quaternion.identity, Vector3.one);
                if (P(r, 0.6)) slots[18 + pair] = Matrix4x4.TRS(mid + V(r, 0.01f), Quaternion.identity, Vector3.one);
            }

            // Other players: hands near the parts in every gesture.
            int players = r.Next(0, 9);
            var lh = new Vector4[255];
            var rh = new Vector4[255];
            var chest = new Vector4[255];
            var root = new Vector4[255];
            float[] gestures = { -1, 0, 0.3f, 1, 2, 3, 4, 5, 6 };
            for (int i = 0; i < players; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    Vector3 h = P(r, 0.7) ? centres[r.Next(second ? 4 : 2)] + Dir(r) * F(r, 0, 0.15f) : V(r, 2);
                    if (P(r, 0.05)) h = Vector3.zero;
                    var v = new Vector4(h.x, h.y, h.z, gestures[r.Next(gestures.Length)]);
                    if (j == 0) lh[i] = v; else rh[i] = v;
                }
                Vector3 c = (Vector3) lh[i] + new Vector3(F(r, -0.2f, 0.2f), -0.2f, -0.3f);
                chest[i] = P(r, 0.05) ? Vector4.zero : new Vector4(c.x, c.y, c.z, 0);
                root[i] = new Vector4(0, 0, 0, P(r, 0.1) ? 0.05f : F(r, 1, 2));
            }
            // Lights: some black protocol lights with digit 8 or 9 at the sockets.
            var colour = new Vector4[8];
            Vector4 px = default, py = default, pz = default, atten = default;
            for (int i = 0; i < 4; i++)
            {
                float range = P(r, 0.5) ? (P(r, 0.5) ? 0.08f : 0.19f) : F(r, 0.5f, 5f);
                atten[i] = 25f / (range * range);
                bool black = P(r, 0.7);
                colour[i] = black ? new Vector4(0, 0, 0, F(r, 0.05f, 0.4f)) : new Vector4(1, 0.5f, 0.2f, 1);
                Vector3 at = (Vector3) slots[18 + r.Next(2)].GetColumn(3) + Dir(r) * F(r, 0, 0.3f);
                px[i] = at.x; py[i] = at.y; pz[i] = at.z;
            }

            var reachNames = new[] { "_YAPS_SquashReach", "_YAPS_SquashAway", "_YAPS_SquashOut", "_YAPS_SquashIn", "_YAPS_SquashSideA", "_YAPS_SquashSideB" };
            var push = new Vector4(F(r, 0, 1), F(r, 0, 0.5f), F(r, 0.2f, 0.5f), P(r, 0.7) ? 1 : 0);
            var hands = new Vector4(F(r, 0, 2), F(r, 0.03f, 0.08f), P(r, 0.8) ? F(r, 0.2f, 2) : 0, wild ? 12 : 0);
            var shape = new Vector4(F(r, 0.2f, 0.6f), F(r, 0.5f, 1), F(r, 1.5f, 3), P(r, 0.7) ? 1 : 0);
            var tune = new Vector4(F(r, 0, 1), F(r, 0.5f, 1.5f), F(r, 0, 1), F(r, 0.2f, 0.8f));
            var motion = new Vector4(P(r, 0.6) ? F(r, 0.2f, 2) : 0, F(r, 0, 0.3f), F(r, 1.2f, 2), 0);
            var pairs = new Vector4(F(r, 0.05f, 0.25f), F(r, 0, 1), F(r, 0.05f, 0.25f), F(r, 0, 1));
            float plug = P(r, 0.7) ? F(r, 0.2f, 2) : 0;
            var limbL = new Vector4(F(r, 0.1f, 0.4f), F(r, 0.1f, 0.4f), F(r, 0.1f, 0.4f), 0);
            var limbR = new Vector4(F(r, 0.1f, 0.4f), F(r, 0.1f, 0.4f), F(r, 0.1f, 0.4f), 0);
            var time = new Vector4(0, F(r, 0, 100), 0, 0);
            // The skinned slots: where the matrices put them, an empty one too (both on the host in
            // a build), then spoilt.
            var skinAt = new Vector3[20];
            for (int i = 0; i < 20; i++) skinAt[i] = slots[i].GetColumn(3);
            cleanSlots = Raw(skinAt);
            spoilt = "clean";
            string what0 = "";
            if (wild)
            {
                Vector3 pivot = skinAt[4];
                switch (r.Next(3))
                {
                    case 0:
                        var turn = Quaternion.Euler(0, F(r, -60, 60), 0);
                        var off = Dir(r) * F(r, 0.05f, 0.4f);
                        for (int i = 0; i < 20; i++) skinAt[i] = pivot + turn * (skinAt[i] - pivot) + off;
                        spoilt = "frozen";
                        break;
                    case 1:
                        for (int i = 0; i < 20; i++) if (i != 4) skinAt[i] = pivot + V(r, 3);
                        spoilt = "garbage";
                        break;
                    default:
                        int j = r.Next(19);
                        bool nan = P(r, 0.5);
                        skinAt[j < 4 ? j : j + 1] = Vector3.one * (nan ? float.NaN : 1e20f);
                        spoilt = nan ? "NaN" : "huge"; what0 = $"slot {(j < 4 ? j : j + 1)}";
                        break;
                }
            }
            spoiltSlots = Raw(skinAt);
            skin.SetData(spoiltSlots);
            // Both copies get the same values: no draw from the generator below this line.
            foreach (var cs in shaders)
            {
                cs.SetMatrix("unity_ObjectToWorld", o2w);
                cs.SetMatrix("unity_WorldToObject", o2w.inverse);
                cs.SetMatrixArray("_YAPS_SquashFollow", slots);
                for (int j = 0; j < 6; j++) cs.SetVector(reachNames[j], reach[j]);
                cs.SetVector("_YAPS_SquashPair", pairs);
                cs.SetVector("_YAPS_SquashShape", shape);
                cs.SetVector("_YAPS_SquashPush", push);
                cs.SetVector("_YAPS_SquashLimbL", limbL);
                cs.SetVector("_YAPS_SquashLimbR", limbR);
                cs.SetFloat("_YAPS_SquashPulse", 0);
                cs.SetVector("_YAPS_SquashHands", hands);
                cs.SetVector("_YAPS_SquashTune", tune);
                cs.SetVector("_YAPS_SquashMotion", motion);
                cs.SetFloat("_YAPS_SquashPlug", plug);
                cs.SetFloat("_YAPS_DentPower", 1);
                cs.SetFloat("_YAPS_DentSpill", 0.3f);
                cs.SetVector("_YAPS_DentOwn_TexelSize", new Vector4(1f / own.width, 1f / own.height, own.width, own.height));
                cs.SetVector("CVRGlobalParams1", new Vector4(0, players, 0, 0));
                cs.SetVectorArray("_CVR_PlayerLeftHandPositions", lh);
                cs.SetVectorArray("_CVR_PlayerRightHandPositions", rh);
                cs.SetVectorArray("_CVR_PlayerChestPositions", chest);
                cs.SetVectorArray("_CVR_PlayerRootPositions", root);
                cs.SetVectorArray("unity_LightColor", colour);
                cs.SetVector("unity_4LightPosX0", px);
                cs.SetVector("unity_4LightPosY0", py);
                cs.SetVector("unity_4LightPosZ0", pz);
                cs.SetVector("unity_4LightAtten0", atten);
                cs.SetVector("_Time", time);
            }

            // Vertices round the parts, in object space, with their shares of each part.
            var shares = new Color[Count];
            var verts = new Vector4[Count * 3];
            lim = new float[Count];
            var w2o = o2w.inverse;
            for (int i = 0; i < Count; i++)
            {
                int m = r.Next(second ? 4 : 2);
                Vector3 world = centres[m] + Dir(r) * R[m] * F(r, 0, 2.2f);
                var c = new Color(0, 0, 0, 0);
                if (!P(r, 0.2))
                {
                    c[m] = F(r, 0.3f, 1);
                    if (P(r, 0.3)) c[m ^ 1] = F(r, 0, 0.5f);
                }
                shares[i] = c;
                // The vertex's own size to measure a move in: its largest part's reach.
                for (int q = 0; q < 4; q++)
                    if (c[q] > 0 && slots[q].GetColumn(0).sqrMagnitude > 0) lim[i] = Mathf.Max(lim[i], reach[0][q] * units[q]);
                Vector3 n = P(r, 0.05) ? Vector3.zero : w2o.MultiplyVector(Dir(r)).normalized;
                verts[i * 3] = w2o.MultiplyPoint3x4(world);
                verts[i * 3 + 1] = n;
                verts[i * 3 + 2] = w2o.MultiplyVector(Dir(r)).normalized;
            }
            own.SetPixels(shares);
            own.Apply(false);
            what = $"players {players}, second pair {second}, arms {push.w}, hands {hands.z:0.00}, plug {plug:0.00}, contact {shape.w}" + (wild ? $", slots {spoilt} {what0}" : "");
            return verts;
        }

        static float[] Raw(Vector3[] at)
        {
            var raw = new float[at.Length * 3];
            for (int i = 0; i < at.Length; i++) { raw[i * 3] = at[i].x; raw[i * 3 + 1] = at[i].y; raw[i * 3 + 2] = at[i].z; }
            return raw;
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
