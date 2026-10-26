// Runs ChilloutVR's shader filter (Settings > Experimental, "Enable Shader Filtering") inside the
// editor, then hands over to SquashPlayProbe, so a squash avatar is drawn exactly as a client with
// the filter on draws it. The filter is a native plugin that hooks the D3D11 device's Create*Shader
// calls and rewrites each program's bytecode: one counter for the whole program, each loop iteration
// adds its body's instruction count, and every loop breaks once the counter passes the Loop Limit.
//
// Needs the client's own Plugins/x86_64/DxbcShaderFilter.dll copied into the project (anywhere under
// Assets, editor enabled). It is the game's binary: never commit it, delete it after the run.
//
// -executeMethod AvatarBridge.Regression.ShaderFilterProbe.Run -sfLimit <0|128|256|512|1024|2048>
//     -spScene <scene> -spOut <dir> [any SquashPlayProbe mode]
//
// -executeMethod AvatarBridge.Regression.ShaderFilterProbe.Plug -sfLimit <n> [-ghostAvatar ...]
//     runs PlugGhostProbe with the plug's shader compiled at run time, so its bends are filtered.
//
// -sfRuntime swaps each squash or plug material's shader for a copy compiled at run time, which the hook
// does reach in the editor (an imported shader never passes through it here).
//
// 0 leaves the filter off. Before handing over it draws a canary: a 1000-step loop whose result
// reads 0.632 unfiltered and far less when the hook is live, logged as "[ShaderFilter] canary".
// Batch, never -nographics.
//
// -executeMethod AvatarBridge.Regression.ShaderFilterProbe.Dump -spScene <scene> | -spPrefab <prefab> -sfDump <dir>
//     writes the D3D11 vertex program of every squash or plug material's passes in that scene, compiled with
//     its keywords (plus -sfKeywords "A B"), as <material>.<pass>.dxbc, to run through the filter
//     outside Unity.

#if AVATARBRIDGE_YAPS && CVR_CCK_EXISTS && VRC_SDK_VRCSDK3 && UNITY_EDITOR
using System;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class ShaderFilterProbe
    {
        [DllImport("DxbcShaderFilter")] static extern void SetFilteringState(bool loops, bool geometry, bool tessellation);
        [DllImport("DxbcShaderFilter")] static extern void SetLoopLimit(int limit);
        [DllImport("DxbcShaderFilter")] static extern void SetMaxTesselationPower(float power);
        [DllImport("DxbcShaderFilter")] static extern void SetGeometryLimit(int limit);

        const string Canary = @"Shader ""Hidden/ShaderFilterCanary"" {
Properties { _N (""N"", Float) = 1000 _Step (""Step"", Float) = 0.001 }
SubShader { Pass { ZTest Always Cull Off ZWrite Off
CGPROGRAM
#pragma vertex vert_img
#pragma fragment frag
#include ""UnityCG.cginc""
float _N, _Step;
float4 frag (v2f_img i) : SV_Target
{
    float c = 0;
    [loop] for (int k = 0; k < (int) _N; k++) c = c * (1 - _Step) + _Step;
    return c;
}
ENDCG } } }";

        // The same loop in a vertex program, since the squash runs there.
        const string VertexCanary = @"Shader ""Hidden/ShaderFilterVertexCanary"" {
Properties { _N (""N"", Float) = 1000 _Step (""Step"", Float) = 0.001 }
SubShader { Pass { ZTest Always Cull Off ZWrite Off
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include ""UnityCG.cginc""
float _N, _Step;
struct v2f { float4 pos : SV_POSITION; float c : TEXCOORD0; };
v2f vert (appdata_img v)
{
    v2f o;
    o.pos = UnityObjectToClipPos(v.vertex);
    float c = 0;
    [loop] for (int k = 0; k < (int) _N; k++) c = c * (1 - _Step) + _Step;
    o.c = c;
    return o;
}
float4 frag (v2f i) : SV_Target { return i.c; }
ENDCG } } }";

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            Filter(args);
            SessionState.SetBool("ShaderFilterProbe.again", true);
            SessionState.SetBool("ShaderFilterProbe.runtime", args.Contains("-sfRuntime"));
            Again();
            SquashPlayProbe.Run();
        }

        // The plug ghost probe's scenes with the plug's shader compiled at run time, so a socket bend
        // is drawn as a client with the filter on draws it. -sfLimit as above, -ghost* as that probe.
        public static void Plug()
        {
            Filter(Environment.GetCommandLineArgs());
            PlugGhostProbe.Swap = m => IsYaps(m) ? new Material(m) { shader = RuntimeCopy(m.shader) } : m;
            PlugGhostProbe.Run();
        }

        static void Filter(string[] args)
        {
            int at = Array.IndexOf(args, "-sfLimit");
            int limit = at >= 0 && at + 1 < args.Length ? int.Parse(args[at + 1]) : 0;
            // The client's defaults beside the loop limit: tessellation 50 / 10, geometry 60.
            SetMaxTesselationPower(5f);
            SetGeometryLimit(60);
            if (limit > 0) SetLoopLimit(limit);
            SetFilteringState(limit > 0, limit > 0, limit > 0);
            Debug.Log($"[ShaderFilter] limit {limit}, canary {Draw(Canary):F4}, vertex canary {Draw(VertexCanary):F4}, asset canary {AssetCanary()} (0.6323 unfiltered)");
        }

        static Shader RuntimeCopy(Shader shader)
        {
            var copy = ShaderUtil.CreateShaderAsset(System.IO.File.ReadAllText(AssetDatabase.GetAssetPath(shader)), false);
            Debug.Log($"[ShaderFilter] runtime copy of {shader.name}: {(ShaderUtil.ShaderHasError(copy) ? "ERROR " + string.Join(" | ", ShaderUtil.GetShaderMessages(copy).Select(m => m.message).Take(3)) : "ok")}");
            return copy;
        }

        public static void Dump()
        {
            var args = Environment.GetCommandLineArgs();
            string dir = args[Array.IndexOf(args, "-sfDump") + 1];
            System.IO.Directory.CreateDirectory(dir);
            int ek = Array.IndexOf(args, "-sfKeywords");
            var extra = ek >= 0 ? args[ek + 1].Split(' ') : new string[0];
            int pf = Array.IndexOf(args, "-spPrefab");
            Renderer[] renderers;
            if (pf >= 0)
            {
                renderers = AssetDatabase.LoadAssetAtPath<GameObject>(args[pf + 1]).GetComponentsInChildren<Renderer>(true);
            }
            else
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(args[Array.IndexOf(args, "-spScene") + 1]);
                renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
            }
            var mats = renderers.SelectMany(r => r.sharedMaterials).Where(IsYaps).Distinct();
            foreach (var mat in mats)
            {
                var sub = ShaderUtil.GetShaderData(mat.shader).GetSubshader(0);
                for (int i = 0; i < sub.PassCount; i++)
                {
                    var pass = sub.GetPass(i);
                    var keywords = mat.shaderKeywords.Concat(extra).ToArray();
                    var info = pass.CompileVariant(UnityEditor.Rendering.ShaderType.Vertex, keywords,
                        UnityEditor.Rendering.ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
                    var bytes = info.ShaderData ?? new byte[0];
                    // The DXBC container sits inside Unity's own wrapper; its size is at offset 24.
                    int at = -1;
                    for (int k = 0; k + 28 < bytes.Length; k++)
                        if (bytes[k] == 'D' && bytes[k + 1] == 'X' && bytes[k + 2] == 'B' && bytes[k + 3] == 'C') { at = k; break; }
                    string name = $"{string.Concat(mat.name.Where(char.IsLetterOrDigit))}.{i}.{string.Concat(pass.Name.Where(char.IsLetterOrDigit))}";
                    if (!info.Success || at < 0)
                    {
                        Debug.Log($"[ShaderFilter] dump {name}: success {info.Success}, DXBC at {at}, {string.Join(" | ", info.Messages.Select(m => m.message))}");
                        continue;
                    }
                    int size = BitConverter.ToInt32(bytes, at + 24);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".dxbc"), bytes.Skip(at).Take(size).ToArray());
                    Debug.Log($"[ShaderFilter] dump {name}: {size} bytes, keywords {string.Join(" ", keywords)}");
                }
            }
            EditorApplication.Exit(0);
        }

        // A squash material, or a plug's: the plug's bake reads shape keys and walks its curve in
        // loops of its own.
        static bool IsYaps(Material m) => m != null && (m.HasProperty("_YAPS_DentPower") || m.HasProperty("_YAPS_Bake"));

        // Again once Play mode starts, whether or not it reloads the scripts.
        [InitializeOnLoadMethod]
        static void Again()
        {
            if (!SessionState.GetBool("ShaderFilterProbe.again", false)) return;
            EditorApplication.CallbackFunction check = null;
            check = () =>
            {
                if (!EditorApplication.isPlaying) return;
                EditorApplication.update -= check;
                if (SessionState.GetBool("ShaderFilterProbe.runtime", false)) Recompile();
                SessionState.SetBool("ShaderFilterProbe.again", false);
                Debug.Log($"[ShaderFilter] in Play mode: canary {Draw(Canary):F4}, vertex canary {Draw(VertexCanary):F4}, asset canary {AssetCanary()}");
            };
            EditorApplication.update += check;
        }

        // The editor never passes an imported shader through the hook, only one compiled at run time
        // (the canaries above, against the same shader saved as an asset). So -sfRuntime compiles each
        // squash material's shader again from its source once Play mode starts, and swaps it in for
        // the run. The materials are the test scene's own and get their shader back on quitting.
        static void Recompile()
        {
            var swapped = new System.Collections.Generic.Dictionary<Material, Shader>();
            foreach (var mat in UnityEngine.Object.FindObjectsOfType<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Where(IsYaps).Distinct())
            {
                string path = AssetDatabase.GetAssetPath(mat.shader);
                var copy = ShaderUtil.CreateShaderAsset(System.IO.File.ReadAllText(path), false);
                bool broken = ShaderUtil.ShaderHasError(copy);
                Debug.Log($"[ShaderFilter] runtime copy of {mat.name}: {(broken ? "ERROR " + string.Join(" | ", ShaderUtil.GetShaderMessages(copy).Select(m => m.message).Take(3)) : "ok")}");
                if (broken) continue;
                swapped[mat] = mat.shader;
                mat.shader = copy;
            }
            EditorApplication.quitting += () => { foreach (var kv in swapped) kv.Key.shader = kv.Value; };
        }

        // An imported copy, if the run put one in the project: Hidden/ShaderFilterAssetCanary.
        static string AssetCanary()
        {
            var shader = Shader.Find("Hidden/ShaderFilterAssetCanary");
            return shader == null ? "none" : Draw(shader).ToString("F4");
        }

        static float Draw(string source) => Draw(ShaderUtil.CreateShaderAsset(source));

        static float Draw(Shader shader)
        {
            var mat = new Material(shader);
            var rt = RenderTexture.GetTemporary(4, 4, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Graphics.Blit(null, rt, mat);
            var tex = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            return tex.GetPixel(1, 1).r;
        }
    }
}
#endif
