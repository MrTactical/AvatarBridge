#if UNITY_EDITOR
// Logs every variant a build compiles for a YAPS-patched shader: pass, stage and
// keywords, one line each, so a bundle build shows which variants the YAPS
// vertex code is paid for. Strips nothing. Live only with -ycbLogVariants.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarBridge.Regression
{
    class YapsVariantLog : IPreprocessShaders
    {
        static readonly bool On = Environment.GetCommandLineArgs().Contains("-ycbLogVariants");
        public int callbackOrder => int.MaxValue;

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (!On || shader.name.IndexOf("YAPS", StringComparison.OrdinalIgnoreCase) < 0) return;
            foreach (var d in data)
            {
                var words = d.shaderKeywordSet.GetShaderKeywords().Select(k => k.name).OrderBy(k => k);
                Debug.Log($"[YapsVariant] {shader.name} | {snippet.passName} | {snippet.passType} | {snippet.shaderType} | {string.Join(" ", words)}");
            }
        }
    }
}
#endif
