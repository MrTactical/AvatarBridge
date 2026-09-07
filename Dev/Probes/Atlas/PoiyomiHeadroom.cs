// Can a patched Poiyomi vertex stage take the atlas read?
//
// Written for Joe. Dev only, pruned from public builds.
//
//   Tools > YAPS > Atlas: Poiyomi headroom
//
// Every atlas spike so far used a clean twenty-line shader. A real plug wears
// somebody's Poiyomi, already patched, already doing vertex work, and
// compiled across Poiyomi's own variants. Adding a hash and twenty-seven
// texture reads to THAT is a different proposition: instruction counts,
// register pressure, and interpolator budget are all unknown.
//
// This does not touch the shipped shader. It finds the patched shader a plug
// is actually wearing, copies it, injects the atlas block into the copy's
// vertex function, imports it, and reports whether it compiled. A copy that
// compiles is the answer; a copy that does not names the limit.
#if CVR_CCK_EXISTS
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

public static class PoiyomiHeadroom
{
    const string OutDir = "Assets/YapsSpike/Headroom";

    [MenuItem("Tools/YAPS/Atlas: Poiyomi headroom")]
    static void Run()
    {
        var plug = Object.FindObjectsOfType<YapsPlug>(true).FirstOrDefault(p => p != null && p.Target != null);
        if (plug == null) { Debug.LogError("[Headroom] no YapsPlug with a mesh in the scene"); return; }

        var mat = plug.Target.sharedMaterials.FirstOrDefault(m => m != null && m.HasProperty("_YAPS_Bake"));
        if (mat == null) { Debug.LogError("[Headroom] the plug's mesh carries no baked YAPS material"); return; }
        var shader = mat.shader;
        string src = AssetDatabase.GetAssetPath(shader);
        if (string.IsNullOrEmpty(src) || !src.EndsWith(".shader"))
        {
            Debug.LogError("[Headroom] the material's shader has no .shader on disk: " + shader.name
                           + ". Nothing to copy, so nothing to measure.");
            return;
        }

        string text = File.ReadAllText(src);
        Debug.Log("[Headroom] baseline: " + shader.name
                  + System.Environment.NewLine + "  file      " + src
                  + System.Environment.NewLine + "  bytes     " + text.Length.ToString("N0")
                  + System.Environment.NewLine + "  errors    " + ShaderUtil.ShaderHasError(shader)
                  + System.Environment.NewLine + "  messages  " + ShaderUtil.GetShaderMessageCount(shader));

        // The atlas block, written the way it would ship: a hash to find the
        // cell, then one read per neighbouring cell. The accumulator is folded
        // into a value the deform already uses, or a compiler removes the
        // whole thing and the test measures nothing.
        //
        // Reads through _YAPS_Bake, a Texture2D already declared and already
        // read with .Load in the deform. Same KIND of read the atlas needs, a
        // point read with no filtering, and it means this declares nothing:
        // the first attempt added a sampler and landed it in the middle of a
        // function signature.
        const string block = @"
            // --- YAPS atlas headroom probe ---
            {
                float3 yapsAtlasWorld = mul(unity_ObjectToWorld, float4(0,0,0,1)).xyz;
                float3 yapsAtlasScaled = yapsAtlasWorld / 0.5;
                int3 yapsCell = int3(floor(yapsAtlasScaled));
                float3 yapsAcc = 0;
                [loop] for (int yi = -1; yi <= 1; yi++)
                [loop] for (int yj = -1; yj <= 1; yj++)
                [loop] for (int yk = -1; yk <= 1; yk++)
                {
                    int3 yc = yapsCell + int3(yi, yj, yk);
                    int yh = yc.x * 73856093; yh ^= yc.y * 19349663; yh ^= yc.z * 83492791;
                    int yidx = yh % 64; if (yidx < 0) yidx += 64;
                    yapsAcc += _YAPS_Bake.Load(int3(yidx % 8, yidx / 8, 0)).rgb;
                }
                yapsPosition += yapsAcc * 1e-9;
            }
            // --- end YAPS atlas headroom probe ---
";
        // The CALL, not the definition. The definition reads
        // "void YapsSocketDeform(inout float3 position" and spans two lines,
        // and the first version of this matched it and injected into the
        // middle of the parameter list, which is what the two syntax errors
        // at lines 5845 and 5849 were.
        int call = text.IndexOf("YapsSocketDeform(yapsPosition", System.StringComparison.Ordinal);
        if (call < 0)
        {
            Debug.LogError("[Headroom] no YapsSocketDeform(yapsPosition call here, so the patcher has not been through this shader. Bake the plug first.");
            return;
        }
        int lineStart = text.LastIndexOf((char)10, call) + 1;
        string patched = text.Insert(lineStart, block);

        // A distinct name, or Unity resolves the copy to the original.
        patched = Regex.Replace(patched, "Shader\\s+\"([^\"]+)\"",
            m => "Shader \"" + m.Groups[1].Value + " HEADROOM\"", RegexOptions.None);

        Directory.CreateDirectory(OutDir);
        string dst = OutDir + "/" + Path.GetFileNameWithoutExtension(src) + "_headroom.shader";
        File.WriteAllText(dst, patched);
        AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceSynchronousImport);

        var copy = AssetDatabase.LoadAssetAtPath<Shader>(dst);
        if (copy == null) { Debug.LogError("[Headroom] the copy did not import: " + dst); return; }

        bool bad = ShaderUtil.ShaderHasError(copy);
        int msgs = ShaderUtil.GetShaderMessageCount(copy);
        var lines = new System.Text.StringBuilder();
        lines.AppendLine("[Headroom] with the atlas block: " + (bad ? "FAILED TO COMPILE" : "compiles"));
        lines.AppendLine("  copy      " + dst);
        lines.AppendLine("  bytes     " + patched.Length.ToString("N0")
                         + "   (+" + (patched.Length - text.Length).ToString("N0") + ")");
        lines.AppendLine("  messages  " + msgs);
        if (msgs > 0)
        {
            foreach (var m in ShaderUtil.GetShaderMessages(copy).Take(8))
                lines.AppendLine("    " + m.severity + ": " + m.message.Trim()
                                 + (m.line > 0 ? "  (line " + m.line + ")" : ""));
        }
        if (bad) Debug.LogError(lines.ToString()); else Debug.Log(lines.ToString());
    }
}
#endif
