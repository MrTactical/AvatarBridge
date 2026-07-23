#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge
{
    /// <summary>
    /// VRCFury support. Fury avatars only get their real FX layers / parameters / menus /
    /// PhysBones at build time, so converting them directly would lose every Fury feature.
    ///
    /// Instead of reimplementing VRCFury, we invoke VRCFury's own "Build a Test Copy"
    /// pipeline: it produces a fully-baked avatar with all Fury components applied and
    /// stripped. AvatarBridge then converts that baked copy.
    ///
    /// Everything is reflection-based: VRCFury may not be installed, and its editor API
    /// is internal and changes between versions. Missing/incompatible versions degrade to
    /// a clear "build a test copy manually" instruction instead of a compile error.
    /// </summary>
    public static class VRCFuryBaker
    {
        const string Category = "VRCFury";
        public const string ManualInstruction =
            "Build the copy manually instead: right-click the avatar in the Hierarchy → " +
            "VRCFury → 'Build a Test Copy' (older versions: Tools → VRCFury), then run " +
            "AvatarBridge on that test copy.";

        public static bool HasFuryComponents(GameObject avatar)
        {
            if (avatar == null)
            {
                return false;
            }
            return avatar.GetComponentsInChildren<Component>(true).Any(IsFuryComponent);
        }

        static bool IsFuryComponent(Component c)
        {
            if (c == null)
            {
                return false;
            }
            var type = c.GetType();
            string ns = type.Namespace ?? "";
            return ns == "VF" || ns.StartsWith("VF.") || type.Name.StartsWith("VRCFury");
        }

        /// <summary>
        /// Bakes the avatar with VRCFury's own builder. Returns the baked scene copy,
        /// or null when there is nothing to bake or the bake failed (already reported).
        /// </summary>
        public static GameObject TryBake(VRCAvatarDescriptor source, BridgeReport report)
        {
            if (!HasFuryComponents(source.gameObject))
            {
                return null;
            }

            var bakeMethod = FindBakeMethod(out string methodDescription);
            if (bakeMethod == null)
            {
                report.Warning(Category, "VRCFury components detected but no compatible VRCFury builder was found",
                    "Fury features would be lost if converted as-is. " + ManualInstruction);
                return null;
            }

            var rootsBefore = GetSceneRoots();
            GameObject directResult = null;
            try
            {
                if (bakeMethod.GetParameters().Length == 0)
                {
                    // Selection-driven menu entry point.
                    Selection.activeGameObject = source.gameObject;
                    bakeMethod.Invoke(null, null);
                }
                else
                {
                    object arg = CoerceArgument(source.gameObject, bakeMethod.GetParameters()[0].ParameterType);
                    directResult = ExtractGameObject(bakeMethod.Invoke(null, new[] { arg }));
                }
            }
            catch (Exception e)
            {
                var inner = e.InnerException ?? e;
                report.Error(Category, "VRCFury bake failed", inner.Message + " — " + ManualInstruction);
                return null;
            }

            GameObject baked = directResult;
            if (baked == null)
            {
                // The menu-style entry points return void; find the new avatar root by diffing the scene.
                baked = GetSceneRoots()
                    .Where(go => !rootsBefore.Contains(go))
                    .FirstOrDefault(go => go.GetComponentInChildren<VRCAvatarDescriptor>(true) != null);
            }

            if (baked == null)
            {
                report.Warning(Category, "VRCFury bake produced no detectable avatar copy",
                    ManualInstruction);
                return null;
            }

            report.Converted(Category, "Avatar baked with VRCFury before conversion", $"via {methodDescription}");
            return baked;
        }

        static List<GameObject> GetSceneRoots()
        {
            var roots = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    roots.AddRange(scene.GetRootGameObjects());
                }
            }
            return roots;
        }

        static MethodInfo FindBakeMethod(out string description)
        {
            // Known entry points, newest first. All are static; some take the avatar
            // (as GameObject or VRCFury's VFGameObject wrapper), some use Selection.
            var candidates = new (string typeName, string methodName)[]
            {
                ("VRCFuryTestCopyMenuItem", "BuildTestCopy"),
                ("VRCFuryTestCopyMenuItem", "RunBuildTestCopy"),
                ("TestCopyMenuItem", "BuildTestCopy"),
                ("TestCopyMenuItem", "RunBuildTestCopy")
            };

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch
                {
                    continue;
                }
                foreach (var type in types)
                {
                    string ns = type.Namespace ?? "";
                    if (!(ns == "VF" || ns.StartsWith("VF.")))
                    {
                        continue;
                    }
                    foreach (var (typeName, methodName) in candidates)
                    {
                        if (type.Name != typeName)
                        {
                            continue;
                        }
                        var method = type
                            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                            .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length <= 1);
                        if (method != null)
                        {
                            description = $"{type.FullName}.{method.Name}";
                            return method;
                        }
                    }
                }
            }
            description = null;
            return null;
        }

        /// <summary>VRCFury wraps GameObject in its own VFGameObject; convert via its implicit operator.</summary>
        static object CoerceArgument(GameObject avatar, Type parameterType)
        {
            if (parameterType.IsInstanceOfType(avatar))
            {
                return avatar;
            }
            var implicitOp = parameterType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "op_Implicit" &&
                                     m.ReturnType == parameterType &&
                                     m.GetParameters().Length == 1 &&
                                     m.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(GameObject)));
            return implicitOp != null ? implicitOp.Invoke(null, new object[] { avatar }) : avatar;
        }

        static GameObject ExtractGameObject(object result)
        {
            if (result is GameObject go)
            {
                return go;
            }
            if (result == null)
            {
                return null;
            }
            // VFGameObject and similar wrappers convert back via implicit operator.
            var backOp = result.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "op_Implicit" &&
                                     m.ReturnType == typeof(GameObject) &&
                                     m.GetParameters().Length == 1 &&
                                     m.GetParameters()[0].ParameterType.IsInstanceOfType(result));
            return backOp != null ? backOp.Invoke(null, new[] { result }) as GameObject : null;
        }
    }
}
#endif
