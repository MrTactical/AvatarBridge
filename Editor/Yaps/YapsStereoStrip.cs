// Drops the double-wide stereo variants of YAPS patches from a build.
//
// ChilloutVR always draws single-pass instanced, so the double-wide copy Unity compiles
// for a bundle is never drawn, and on an avatar with many patches it cost minutes of
// compile. Patches only ship to ChilloutVR; other shaders are left alone.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarBridge
{
    class YapsStereoStrip : IPreprocessShaders
    {
        static readonly ShaderKeyword DoubleWide = new ShaderKeyword("UNITY_SINGLE_PASS_STEREO");

        public int callbackOrder => 0;

        // The live tissue step is only blitted, so it needs no stereo copy. Its
        // skip_variants pragma misses the ones Unity adds itself.
        static readonly ShaderKeyword[] Stereo =
        {
            DoubleWide, new ShaderKeyword("STEREO_INSTANCING_ON"), new ShaderKeyword("STEREO_MULTIVIEW_ON"),
            new ShaderKeyword("STEREO_CUBEMAP_RENDER_ON"),
        };

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (shader.name == "Hidden/AvatarBridge/JellyStep")
            {
                for (int i = data.Count - 1; i >= 0; i--)
                    if (Array.Exists(Stereo, k => data[i].shaderKeywordSet.IsEnabled(k))) data.RemoveAt(i);
                return;
            }
            if (!AssetDatabase.GetAssetPath(shader).StartsWith(YapsShaderPatcher.SharedFolder + "/", StringComparison.OrdinalIgnoreCase)) return;
            for (int i = data.Count - 1; i >= 0; i--)
                if (data[i].shaderKeywordSet.IsEnabled(DoubleWide)) data.RemoveAt(i);
        }
    }
}
#endif
