// Importing a shader without errors is not the same as compiling it —
// Unity defers variant compilation until something uses the shader, so a
// clean import log proves nothing. This forces the question with the same
// check the real patcher will use, and prints whatever the compiler says.
//
//   Unity.exe -batchmode -projectPath "<test project>" \
//     -executeMethod AvatarBridge.Spike.YapsShaderCheck.RunBatch -quit
#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Spike
{
    public static class YapsShaderCheck
    {
        const string ShaderName = "AvatarBridge/YAPS Test Plug";

        [MenuItem("AvatarBridge/Spike/Verify YAPS shader compiles")]
        public static void RunBatch()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[YAPS] Shader \"{ShaderName}\" not found.");
                return;
            }

            // Touching a material forces the variant to be built.
            var material = new Material(shader);
            material.SetFloat("_YAPS_VertexCount", 1);

            bool broken = ShaderUtil.ShaderHasError(shader);
            var report = new StringBuilder();
            report.AppendLine($"[YAPS] {ShaderName}: {(broken ? "HAS ERRORS" : "compiles clean")}");

            int count = ShaderUtil.GetShaderMessageCount(shader);
            if (count > 0)
            {
                var messages = ShaderUtil.GetShaderMessages(shader);
                foreach (var message in messages)
                {
                    report.AppendLine($"  [{message.severity}] line {message.line}: {message.message}");
                    if (!string.IsNullOrEmpty(message.messageDetails))
                    {
                        report.AppendLine($"      {message.messageDetails.Trim()}");
                    }
                }
            }
            else
            {
                report.AppendLine("  no compiler messages at all");
            }

            Object.DestroyImmediate(material);

            if (broken)
            {
                Debug.LogError(report.ToString());
            }
            else
            {
                Debug.Log(report.ToString());
            }
        }
    }
}
#endif
