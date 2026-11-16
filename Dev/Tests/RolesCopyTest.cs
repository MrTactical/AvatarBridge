#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Collections.Generic;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A patch that compiles only some deforms carries _YAPS_Roles, a marker read from the
    // shader's default. Copying a plug's values onto a readout listed it as a setting the
    // readout could not hold, so every readout of a role-patched plug warned. A real setting
    // the target lacks must still be listed.
    public static class RolesCopyTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Roles copy")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[RolesCopyTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            var shader = ShaderUtil.CreateShaderAsset(
                "Shader \"Hidden/RolesCopyTest\" { Properties {\n" +
                "  [HideInInspector] _YAPS_Roles (\"YAPS deforms compiled in\", Float) = 1\n" +
                "  _YAPS_Length (\"Length\", Float) = 0.2\n" +
                "} SubShader { Pass { } } }");
            var from = new Material(shader);
            var to = new Material(Shader.Find("Unlit/Color"));
            try
            {
                var missing = new List<string>();
                YapsNativeBuilder.CopyYapsProperties(from, to, missing);
                string listed = string.Join(", ", missing);
                fail += Check($"the roles marker is not listed (\"{listed}\")", !missing.Contains("_YAPS_Roles"));
                fail += Check($"a real setting still is (\"{listed}\")", missing.Contains("_YAPS_Length"));
            }
            finally
            {
                Object.DestroyImmediate(from);
                Object.DestroyImmediate(to);
                Object.DestroyImmediate(shader);
            }

            Debug.Log($"[RolesCopyTest] {(fail == 0 ? "PASS" : fail + " WRONG")}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
