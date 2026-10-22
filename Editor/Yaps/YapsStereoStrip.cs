// Drops the double-wide stereo variants of YAPS patches from a build.
//
// Unity compiles every variant of a shader three times for a bundle: mono, single-pass
// instanced and double-wide. ChilloutVR always draws single-pass instanced, so the
// double-wide third of a patch is compiled on the first upload and shipped in every
// download, and never drawn. A patch's vertex variant costs seconds to compile, and an
// avatar with many patches waited minutes on that third alone. Patches only ever ship to
// ChilloutVR, so no other build loses anything; any other shader is left alone.
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

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (!AssetDatabase.GetAssetPath(shader).StartsWith(YapsShaderPatcher.SharedFolder + "/", StringComparison.OrdinalIgnoreCase)) return;
            for (int i = data.Count - 1; i >= 0; i--)
                if (data[i].shaderKeywordSet.IsEnabled(DoubleWide)) data.RemoveAt(i);
        }
    }
}
#endif
