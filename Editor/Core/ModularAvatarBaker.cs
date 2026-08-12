#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge
{
    // Modular Avatar support. Like VRCFury, MA applies its features (merge armature, menus,
    // parameters, reactive toggles, mesh cutters...) only at build time, via NDMF's pipeline.
    // Converting an MA avatar directly would lose all of that.
    //
    // So this invokes NDMF's own manual bake (nadena.dev.ndmf.AvatarProcessor.ManualProcessAvatar),
    // which clones the avatar, runs the full NDMF/MA pipeline, and hands back a baked copy with
    // plain VRChat components. AvatarBridge then converts that copy exactly like any other.
    //
    // Note: a VRCFury "Build a Test Copy" already runs NDMF internally, so avatars that use MA
    // *and* VRCFury are handled by the Fury bake; this baker is for MA-only avatars. All
    // reflection-based: NDMF/MA may be absent and its editor API is internal.
    public static class ModularAvatarBaker
    {
        const string Category = "Modular Avatar";
        public const string ManualInstruction =
            "Bake it manually instead: Tools → Modular Avatar (or NDM Framework) → 'Manual bake avatar' " +
            "with the avatar selected, then run AvatarBridge on the baked copy.";

        public static bool HasModularAvatarComponents(GameObject avatar)
        {
            if (avatar == null)
            {
                return false;
            }
            return avatar.GetComponentsInChildren<Component>(true).Any(IsNdmfComponent);
        }

        static bool IsNdmfComponent(Component c)
        {
            string ns = c != null ? (c.GetType().Namespace ?? "") : "";
            return ns.StartsWith("nadena.dev.modular_avatar", StringComparison.Ordinal)
                   || ns.StartsWith("nadena.dev.ndmf", StringComparison.Ordinal);
        }

        public static GameObject TryBake(VRCAvatarDescriptor source, BridgeReport report)
        {
            if (!HasModularAvatarComponents(source.gameObject))
            {
                return null;
            }

            Type processor = FindType("nadena.dev.ndmf.AvatarProcessor");
            if (processor == null)
            {
                report.Warning(Category, "Modular Avatar components detected but NDMF wasn't found",
                    "MA features would be lost if converted as-is. " + ManualInstruction);
                return null;
            }

            // Gate: let NDMF confirm it actually has something to process on this avatar.
            var canProcess = processor.GetMethod("CanProcessObject",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject) }, null);
            if (canProcess != null && canProcess.Invoke(null, new object[] { source.gameObject }) is bool ok && !ok)
            {
                return null;
            }

            var bake = FindBakeMethod(processor, out string description);
            if (bake == null)
            {
                report.Warning(Category, "MA/NDMF present but no compatible manual-bake method was found",
                    "Your NDMF version may be newer/older than expected. " + ManualInstruction);
                return null;
            }

            GameObject baked;
            try
            {
                // ManualProcessAvatar(GameObject, INDMFPlatformProvider = null); null platform
                // resolves to the default (VRChat) platform, giving plain VRChat components to convert.
                object[] args = bake.GetParameters().Length == 1
                    ? new object[] { source.gameObject }
                    : new object[] { source.gameObject, null };
                baked = bake.Invoke(null, args) as GameObject;
            }
            catch (Exception e)
            {
                var inner = e.InnerException ?? e;
                report.Error(Category, "Modular Avatar bake failed", inner.Message + " — " + ManualInstruction);
                return null;
            }

            if (baked == null)
            {
                report.Warning(Category, "NDMF bake produced no avatar copy", ManualInstruction);
                return null;
            }

            report.Converted(Category, "Avatar baked with Modular Avatar / NDMF before conversion",
                $"via {description}");
            return baked;
        }

        // Prefer the current API; fall back to the obsolete single-arg one on older NDMF.
        static MethodInfo FindBakeMethod(Type processor, out string description)
        {
            var methods = processor.GetMethods(BindingFlags.Public | BindingFlags.Static);
            foreach (var name in new[] { "ManualProcessAvatar", "ProcessAvatarUI" })
            {
                var m = methods.FirstOrDefault(x => x.Name == name
                    && x.ReturnType == typeof(GameObject)
                    && x.GetParameters().Length >= 1
                    && x.GetParameters()[0].ParameterType == typeof(GameObject));
                if (m != null)
                {
                    description = $"{processor.FullName}.{m.Name}";
                    return m;
                }
            }
            description = null;
            return null;
        }

        static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try
                {
                    t = assembly.GetType(fullName);
                }
                catch
                {
                    continue;
                }
                if (t != null)
                {
                    return t;
                }
            }
            return null;
        }
    }
}
#endif
