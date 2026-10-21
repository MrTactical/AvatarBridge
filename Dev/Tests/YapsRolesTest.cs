#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Roles = AvatarBridge.YapsShaderPatcher.Roles;

namespace AvatarBridge.Regression
{
    // A patch compiles only the deforms its roles name, each set of roles is a shader of its
    // own, and a material that takes on a second role gets both rather than keeping a patch
    // that lacks one. Every patch here compiles, on YAPS Simple Lit.
    //
    // Run: -executeMethod AvatarBridge.Regression.YapsRolesTest.Run
    public static class YapsRolesTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test: YAPS patch roles")]
        public static void Run()
        {
            int failures = 0;
            void Check(bool ok, string what)
            {
                if (!ok) failures++;
                Debug.Log($"[YapsRolesTest] {(ok ? "ok   " : "WRONG")} {what}");
            }

            var lit = Shader.Find("YAPS/Simple Lit");
            Check(lit != null, "YAPS Simple Lit is in the project");
            if (lit == null) { Finish(failures); return; }
            var source = new Material(lit) { name = "RolesTest" };
            var made = new System.Collections.Generic.List<Material> { source };
            try
            {
                var names = new System.Collections.Generic.HashSet<string>();
                Shader squashOnly = null;
                foreach (var role in new[] { Roles.Plug, Roles.Socket, Roles.Squash, Roles.All })
                {
                    var shader = YapsShaderPatcher.Patch(source, null, out string why, out _, roles: role);
                    Check(shader != null, $"{role}: patched{(shader == null ? ": " + why : "")}");
                    if (shader == null) continue;
                    if (role == Roles.Squash) squashOnly = shader;
                    Check(YapsShaderPatcher.RolesOf(shader) == role, $"{role}: the shader says {YapsShaderPatcher.RolesOf(shader)}");
                    Check(names.Add(shader.name), $"{role}: a shader of its own");
                    string text = File.ReadAllText(AssetDatabase.GetAssetPath(shader));
                    foreach (var (one, define) in new[] { (Roles.Plug, "YAPS_NO_PLUG"), (Roles.Socket, "YAPS_NO_SOCKET"), (Roles.Squash, "YAPS_NO_SQUASH") })
                        Check(text.Contains("#define " + define) == ((role & one) == 0),
                            $"{role}: {define} {((role & one) == 0 ? "defined" : "not defined")}");
                    var m = new Material(shader);
                    made.Add(m);
                    Check(!ShaderUtil.GetShaderMessages(shader).Any(x => x.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),
                        $"{role}: compiles");
                    Check(!YapsShaderPatcher.IsStale(m), $"{role}: reads as current");
                }

                if (squashOnly != null)
                {
                    var body = new Material(squashOnly) { name = "RolesTest body" };
                    made.Add(body);
                    Check(YapsShaderPatcher.WithRole(body, Roles.Squash, null) == squashOnly, "a role it has keeps its shader");
                    var both = YapsShaderPatcher.WithRole(body, Roles.Plug, null);
                    Check(both != null && YapsShaderPatcher.RolesOf(both) == (Roles.Plug | Roles.Squash), "a plug on a squashed material gets both");
                    if (both != null)
                    {
                        body.shader = both;
                        Check(!YapsShaderPatcher.IsStale(body), "the pair reads as current");
                        Check(YapsShaderPatcher.Refresh(body, null) && body.shader == both, "a refresh keeps both roles");
                    }
                }
            }
            finally
            {
                foreach (var m in made) Object.DestroyImmediate(m);
            }
            Finish(failures);
        }

        static void Finish(int failures)
        {
            Debug.Log(failures == 0 ? "[YapsRolesTest] PASS" : $"[YapsRolesTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }
}
#endif
