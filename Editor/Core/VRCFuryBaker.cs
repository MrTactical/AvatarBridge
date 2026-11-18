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
    // Fury avatars only get their real layers, menus and PhysBones at build time, so this runs
    // Fury's own "Build a Test Copy" and the baked copy is converted. Reflection only: Fury may
    // be absent and its editor API is internal and changes between versions; a mismatch falls
    // back to a "build a test copy manually" instruction.
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

        public static GameObject TryBake(VRCAvatarDescriptor source, BridgeReport report)
        {
            if (!HasFuryComponents(source.gameObject))
            {
                return null;
            }

            ReportBrokenFuryReferences(source.gameObject, report);

            var bakeMethod = FindBakeMethod(out string methodDescription);
            if (bakeMethod == null)
            {
                report.Warning(Category, "VRCFury components detected but no compatible VRCFury builder was found",
                    "Fury features would be lost if converted as-is. " + ManualInstruction);
                return null;
            }

            var rootsBefore = GetSceneRoots();
            GameObject directResult = null;

            // Fury's ErrorDialogBoundary catches each feature's exception, logs and carries on, so
            // a bake with ten failures returns normally ("Errors: 0", every toggle dead). Count
            // what it swallowed from the log.
            int furyErrors = 0;
            // Every distinct message, uncut: Fury names the missing file, and that name is the fix.
            var furyMessages = new List<string>();
            // Unity's complaint about the controller while Fury reads it: not a Fury failure.
            var unityNotes = new List<string>();
            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type != LogType.Exception && type != LogType.Error)
                {
                    return;
                }
                if (condition != null && condition.StartsWith("Controller '", StringComparison.Ordinal)
                    && condition.Contains("which does not exist in controller"))
                {
                    if (!unityNotes.Contains(condition)) unityNotes.Add(condition);
                    return;
                }
                if ((stackTrace != null && (stackTrace.Contains("VF.") || stackTrace.Contains("VRCF")))
                    || (condition != null && condition.Contains("VRCFury")))
                {
                    furyErrors++;
                    if (condition != null && !furyMessages.Contains(condition))
                    {
                        furyMessages.Add(condition);
                    }
                }
            }

            Application.logMessageReceived += OnLog;
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
                report.Error(Category, "VRCFury bake failed", inner.Message + ". " + ManualInstruction);
                return null;
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            if (unityNotes.Count > 0)
            {
                report.Warning(Category,
                    $"{BridgeElements.Count(unityNotes.Count, "transition in the avatar's own controllers tests", "transitions in the avatar's own controllers test")} a parameter the controller never declares",
                    string.Join(" | ", unityNotes.Take(4).Select(m => "\"" + Truncate(Flatten(m), 300) + "\"")) +
                    ": Unity's message, not a VRCFury failure. Declare that parameter on that controller if the " +
                    "transition should work.");
            }

            if (furyErrors > 0)
            {
                var quoted = string.Join(" | ", furyMessages
                    .Take(4)
                    .Select(m => "\"" + Truncate(Flatten(m), 400) + "\""));
                if (furyMessages.Count > 4)
                {
                    quoted += $" | (+{furyMessages.Count - 4} more: see the Console)";
                }
                report.Error(Category, $"VRCFury reported {BridgeElements.Count(furyErrors, "error", "errors")} during its own build",
                    quoted + ": Fury ran, and those features are missing or half-built. If it names files you " +
                    "don't have, install their package or delete the component asking. Fix what errors in " +
                    "VRCFury > Build a Test Copy on the original, then convert again. Do not upload this one.");
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

        static void ReportBrokenFuryReferences(GameObject avatar, BridgeReport report)
        {
            var broken = new List<string>();
            foreach (var component in avatar.GetComponentsInChildren<Component>(true))
            {
                if (!IsFuryComponent(component))
                {
                    continue;
                }
                int missing = 0;
                var so = new SerializedObject(component);
                var it = so.GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType == SerializedPropertyType.ObjectReference
                        && it.objectReferenceInstanceIDValue != 0
                        && it.objectReferenceValue == null)
                    {
                        missing++;
                    }
                }
                if (missing > 0)
                {
                    string path = AnimationUtility.CalculateTransformPath(
                        component.transform, avatar.transform);
                    broken.Add($"\"{(string.IsNullOrEmpty(path) ? avatar.name : path)}\" " +
                        $"({component.GetType().Name}, {BridgeElements.Count(missing, "missing reference", "missing references")})");
                }
            }

            if (broken.Count > 0)
            {
                report.Warning(Category,
                    $"{BridgeElements.Count(broken.Count, "VRCFury component references", "VRCFury components reference")} assets that aren't in this project",
                    string.Join("; ", broken) + ": the cause of Fury's \"missing some files\" error, not a " +
                    "missing VRCFury. Install the package the files come from, or delete the component, then " +
                    "convert again.");
            }
        }

        static string Flatten(string s) =>
            s == null ? "" : s.Replace("\r", " ").Replace("\n", " ").Trim();

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

        static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "(no message captured)";
            }
            return text.Length <= max ? text : text.Substring(0, max) + "…";
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
