using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace AvatarBridge
{
    /// <summary>
    /// Keeps AvatarBridge's optional-dependency scripting defines in sync with what is
    /// actually installed in the project. This file must always compile, so it may not
    /// reference any SDK, CCK, MagicaCloth2 or DynamicBone types directly.
    ///
    /// Defines managed here:
    ///   AVATARBRIDGE_MAGICA  - MagicaCloth2 is present
    ///   AVATARBRIDGE_DYNBONE - DynamicBone (or the VRLabs stub) is present
    ///
    /// The VRChat SDK and the CCK manage their own defines (VRC_SDK_VRCSDK3 and
    /// CVR_CCK_EXISTS) which the rest of this package is gated behind.
    /// </summary>
    [InitializeOnLoad]
    public static class BridgeDefines
    {
        /// <summary>Tool version, shown in the converter window title.</summary>
        public const string Version = "0.1.9";

        public const string MagicaDefine = "AVATARBRIDGE_MAGICA";
        public const string DynamicBoneDefine = "AVATARBRIDGE_DYNBONE";

        static BridgeDefines()
        {
            // Delay so we never mutate defines mid-compilation.
            EditorApplication.delayCall += SyncDefines;
        }

        public static bool HasMagicaCloth2 => TypeExists("MagicaCloth2.MagicaCloth");
        public static bool HasDynamicBone => TypeExists("DynamicBone");
        public static bool HasVrcAvatarSdk => TypeExists("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
        public static bool HasCck => TypeExists("ABI.CCK.Components.CVRAvatar");

        static void SyncDefines()
        {
            var target = NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));

            List<string> defines;
            try
            {
                defines = PlayerSettings.GetScriptingDefineSymbols(target)
                    .Split(';')
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Select(d => d.Trim())
                    .ToList();
            }
            catch (ArgumentException)
            {
                return; // Unsupported build target; nothing to do.
            }

            bool changed = false;
            changed |= SetDefine(defines, MagicaDefine, HasMagicaCloth2);
            changed |= SetDefine(defines, DynamicBoneDefine, HasDynamicBone);

            if (changed)
            {
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
                Debug.Log("[AvatarBridge] Updated scripting defines: " + string.Join(";", defines));
            }
        }

        static bool SetDefine(List<string> defines, string define, bool shouldExist)
        {
            bool exists = defines.Contains(define);
            if (shouldExist && !exists)
            {
                defines.Add(define);
                return true;
            }
            if (!shouldExist && exists)
            {
                defines.Remove(define);
                return true;
            }
            return false;
        }

        static bool TypeExists(string fullTypeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.GetType(fullTypeName, false) != null)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Reflection-only or broken assemblies can throw; ignore them.
                }
            }
            return false;
        }
    }
}
