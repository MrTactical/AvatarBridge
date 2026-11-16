#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AvatarBridge.Regression
{
    // Release check: one body material of each host patched with every role, the jelly's
    // included, and compiled fresh, timed against a limit well under Unity's compiler timeout.
    // Unity's shader cache survives a renamed copy, so a body taking 346 s a variant passed every
    // run for days until an include's line numbers moved; the nonce moves them every time.
    public static class FreshCompileTimeTest
    {
        const double LimitSeconds = 120;

        // Hosts by shader name; a locked Poiyomi first, as converted bodies wear.
        static readonly (string host, Regex name)[] Hosts =
        {
            ("Poiyomi Toon", new Regex(@"poiyomi.*toon", RegexOptions.IgnoreCase)),
            ("Poiyomi Pro", new Regex(@"poiyomi.*pro", RegexOptions.IgnoreCase)),
            ("lilToon", new Regex(@"liltoon", RegexOptions.IgnoreCase)),
            ("YAPS Standard", new Regex(@"^YAPS/Standard$")),
        };

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Patched bodies compile fresh in time")]
        public static void Run()
        {
            int failures = 0, timed = 0;
            var generated = GeneratedFiles.Snapshot();
            var made = new List<Material>();
            try
            {
                var materials = AssetDatabase.FindAssets("t:Material", new[] { "Assets/AvatarBridgeOutput" })
                    .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)))
                    // Not a copy the patcher made: it already carries YAPS.
                    .Where(m => m != null && m.shader != null && !AssetDatabase.GetAssetPath(m.shader).StartsWith(YapsNativeBuilder.OutputRoot + "/"))
                    .ToList();
                foreach (var (host, name) in Hosts)
                {
                    var pick = materials.Where(m => name.IsMatch(m.shader.name))
                        .OrderBy(m => m.shader.name.StartsWith("Hidden/Locked/") ? 0 : 1).FirstOrDefault();
                    if (pick == null && host == "YAPS Standard")
                    {
                        var standard = Shader.Find(YapsNativeBuilder.StandardName);
                        if (standard != null) made.Add(pick = new Material(standard) { name = "Fresh compile stand-in" });
                    }
                    if (pick == null)
                    {
                        Debug.Log($"[FreshCompileTimeTest] skip  {host}: no material in the project wears it");
                        continue;
                    }
                    YapsShaderPatcher.FreshCompileNonce = "fresh compile check " + System.Guid.NewGuid().ToString("N");
                    var clock = Stopwatch.StartNew();
                    var shader = YapsShaderPatcher.Patch(pick, null, out string refusal, out _, roles: YapsShaderPatcher.Roles.All | YapsShaderPatcher.Roles.Jelly);
                    double s = clock.Elapsed.TotalSeconds;
                    bool ok = shader != null && s < LimitSeconds;
                    if (!ok) failures++;
                    timed++;
                    Debug.Log($"[FreshCompileTimeTest] {(ok ? "ok   " : "WRONG")} {host} ({pick.name} on {pick.shader.name}): {s:0.0} s" +
                              (shader == null ? $", refused: {refusal}" : ""));
                }
            }
            finally
            {
                YapsShaderPatcher.FreshCompileNonce = null;
                foreach (var m in made) Object.DestroyImmediate(m);
                GeneratedFiles.DeleteNew(generated);
            }
            if (timed == 0) failures++;
            Debug.Log(failures == 0 ? $"[FreshCompileTimeTest] PASS ({timed} hosts under {LimitSeconds} s)" : $"[FreshCompileTimeTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }
}
#endif
