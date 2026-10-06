using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;
using UnityEngine;

namespace AvatarBridge
{
    // Keeps AvatarBridge's optional-dependency scripting defines in sync with what is
    // actually installed in the project. This file must always compile, so it may not
    // reference any SDK, CCK, MagicaCloth2 or DynamicBone types directly.
    //
    // Defines managed here:
    //   AVATARBRIDGE_MAGICA   - MagicaCloth2 is present
    //   AVATARBRIDGE_DYNBONE  - DynamicBone (or the VRLabs stub) is present
    //   AVATARBRIDGE_YAPS     - the YAPS add-on is installed
    //
    // The VRChat SDK and the CCK manage their own defines (VRC_SDK_VRCSDK3 and
    // CVR_CCK_EXISTS) which the rest of this package is gated behind.
    [InitializeOnLoad]
    public static class BridgeDefines
    {
        public const string Version = "4.7.2";

        public const string MagicaDefine = "AVATARBRIDGE_MAGICA";
        public const string DynamicBoneDefine = "AVATARBRIDGE_DYNBONE";
        public const string YapsDefine = "AVATARBRIDGE_YAPS";

        // Retired define, still cleared from projects that carry it.
        public const string ContactsDefine = "AVATARBRIDGE_CONTACTS";

        static BridgeDefines()
        {
            // Delayed so defines never mutate mid-compilation.
            EditorApplication.delayCall += SyncDefines;
            // A removed package leaves its define behind, and the code it gates
            // compiles into this same assembly: the compile fails, nothing
            // reloads, and SyncDefines never runs again. The old domain stays
            // loaded after a failed compile, so it still hears this.
            CompilationPipeline.assemblyCompilationFinished += (_, messages) =>
            {
                if (messages.Any(m => m.type == CompilerMessageType.Error))
                {
                    EditorApplication.delayCall -= SyncAfterFailedCompile;
                    EditorApplication.delayCall += SyncAfterFailedCompile;
                }
            };
        }

        public static bool HasMagicaCloth2 => TypeExists("MagicaCloth2.MagicaCloth");
        public static bool HasDynamicBone => TypeExists("DynamicBone");
        // A RUNTIME type of the add-on. Its editor code compiles into this
        // same assembly, which is the thing the define gates, so asking for
        // an editor type would be asking whether this file compiled.
        public static bool HasYaps => TypeExists("AvatarBridge.Yaps.YapsPlug");
        public static bool HasVrcAvatarSdk => TypeExists("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
        public static bool HasCck => TypeExists("ABI.CCK.Components.CVRAvatar");

        static void SyncDefines() => SyncDefines(HasMagicaCloth2, HasDynamicBone, HasYaps);

        // After a failed compile the loaded types are stale, since assemblies
        // never unload. Unity names a MonoBehaviour's file after its class, so
        // the scripts the compile was given say what is really installed.
        static void SyncAfterFailedCompile()
        {
            var scripts = new HashSet<string>(CompilationPipeline.GetAssemblies()
                .SelectMany(a => a.sourceFiles).Select(f => Path.GetFileName(f)));
            SyncDefines(
                HasMagicaCloth2 && StillInstalled(scripts, "MagicaCloth.cs", "MagicaCloth2.MagicaCloth"),
                HasDynamicBone && StillInstalled(scripts, "DynamicBone.cs", "DynamicBone"),
                HasYaps && StillInstalled(scripts, "YapsPlug.cs", "AvatarBridge.Yaps.YapsPlug"));
        }

        static bool StillInstalled(HashSet<string> scripts, string script, string typeName)
        {
            if (scripts.Contains(script)) return true;
            // Shipped as a DLL there is no script, and the DLL itself is the
            // evidence. A compiled script assembly is not: it outlives its source.
            string dll = FindType(typeName)?.Assembly.Location.Replace('\\', '/');
            return !string.IsNullOrEmpty(dll) && !dll.Contains("/Library/ScriptAssemblies/") && File.Exists(dll);
        }

        static void SyncDefines(bool magica, bool dynamicBone, bool yaps)
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
            changed |= SetDefine(defines, MagicaDefine, magica);
            changed |= SetDefine(defines, DynamicBoneDefine, dynamicBone);
            changed |= SetDefine(defines, YapsDefine, yaps);
            // Always false. Passing it through SetDefine rather than dropping
            // the line is what clears it out of projects that still have it.
            changed |= SetDefine(defines, ContactsDefine, false);

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

        static bool TypeExists(string fullTypeName) => FindType(fullTypeName) != null;

        static Type FindType(string fullTypeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(fullTypeName, false);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch
                {
                    // Reflection-only or broken assemblies can throw; ignore them.
                }
            }
            return null;
        }
    }
}
