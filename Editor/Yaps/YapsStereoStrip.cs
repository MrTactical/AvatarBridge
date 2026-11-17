// Drops the double-wide stereo variants of YAPS patches from a build.
//
// Unity compiles every variant of a shader three times for a bundle: mono, single-pass
// instanced and double-wide. ChilloutVR always draws single-pass instanced, so the
// double-wide third of a patch is compiled on the first upload and shipped in every
// download, and never drawn. A patch's vertex variant costs seconds to compile, and an
// avatar with many patches waited minutes on that third alone. Patches only ever ship to
// ChilloutVR, so no other build loses anything; any other shader is left alone. The live
// tissue's step loses every stereo copy, as it is only blitted.
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

        // The live tissue's step is only ever blitted, never drawn into an eye, so it needs no
        // stereo copy at all. Its skip_variants pragma does not reach the ones Unity adds itself.
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
