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

            // Fury wraps each feature in an ErrorDialogBoundary that catches the exception, shows
            // a dialog, logs, and CARRIES ON — so a bake can fail ten times and still return
            // normally. One real avatar did exactly that: ten feature failures, a half-built
            // result, and a conversion report reading "Errors: 0" while every toggle was dead.
            // Listen to the log during the bake and count what Fury swallowed.
            int furyErrors = 0;
            // Every DISTINCT message, not just the first. A tester's report said "2 error(s)" and
            // quoted one of them, cut off mid-path at "…/ControllersWD/GoLocoB" — so the filename
            // Fury was asking for was unreadable and the second failure was invisible. Fury names
            // the missing file, and that name IS the fix, so it has to survive into the report.
            var furyMessages = new List<string>();
            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type != LogType.Exception && type != LogType.Error)
                {
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
                report.Error(Category, "VRCFury bake failed", inner.Message + " — " + ManualInstruction);
                return null;
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            if (furyErrors > 0)
            {
                var quoted = string.Join(" | ", furyMessages
                    .Take(4)
                    .Select(m => "\"" + Truncate(Flatten(m), 400) + "\""));
                if (furyMessages.Count > 4)
                {
                    quoted += $" | (+{furyMessages.Count - 4} more — see the Console)";
                }
                report.Error(Category, $"VRCFury reported {furyErrors} error(s) during its own build",
                    quoted + " — this is VRCFury's OWN message, which means Fury ran: the fault is in " +
                    "what it was asked to build, not in Fury being absent. When it names files under a " +
                    "folder you don't have, that package isn't installed in this project — install it, " +
                    "or delete the VRCFury component asking for it. Fury catches each failing feature, " +
                    "shows a dialog and continues, so the bake \"completed\" — but the features that " +
                    "failed are missing or half-built, and everything derived from them will misbehave. " +
                    "Run VRCFury > Build a Test Copy on the original avatar, fix or remove what errors " +
                    "there, then convert again. This conversion continued so you can inspect it, but do " +
                    "not upload it.");
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

        /// <summary>
        /// Names the VRCFury components pointing at assets this project doesn't have, BEFORE the
        /// bake runs on them.
        ///
        /// Fury's own failure says which FILES are missing but never which component wants them,
        /// and it arrives as a modal dialog mid-bake — so the reader gets a path and no way back
        /// to the thing that asked for it. A tester hit exactly this and read the message as
        /// "VRCFury is not installed", when Fury was installed and running: the missing package
        /// was GoGo Loco, which the erroring component referenced.
        ///
        /// A missing asset keeps its GUID in the serialized data, so the property still holds an
        /// instance ID that resolves to nothing — that mismatch is the whole test. The GUID can't
        /// be turned back into a path (nothing in the project has it), which is why this reports
        /// the OBJECT and leaves the filenames to Fury's own message.
        /// </summary>
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
                        $"({component.GetType().Name}, {missing} missing reference(s))");
                }
            }

            if (broken.Count > 0)
            {
                report.Warning(Category,
                    $"{broken.Count} VRCFury component(s) reference assets that aren't in this project",
                    string.Join("; ", broken) + " — each points at a file this project doesn't have, " +
                    "which is what makes Fury's own build fail with \"you're missing some files needed " +
                    "for this VRCFury asset\". That error means Fury RAN and could not find what the " +
                    "component asked for; it does not mean VRCFury is missing. Install the package the " +
                    "component came from (Fury's message names the files, so the folder tells you which " +
                    "package), or delete that component if you don't want the feature — then convert " +
                    "again. Converting past this loses whatever the component would have built, and " +
                    "anything animating paths it would have created stays dead.");
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
            text = text.Replace('\n', ' ').Replace('\r', ' ');
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
