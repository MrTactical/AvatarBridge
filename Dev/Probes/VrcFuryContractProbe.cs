using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Asserts that every VRCFury SPS field the converter reads by name still exists, with the type
    // it is read as. Run it after a VRCFury update and before a release.
    //
    // The reads are reflection, so a rename never breaks the build: it reads as empty. Tags then
    // drop with no warning, and a plug whose shared-tag switch went missing answers only its own
    // rules. Only enableSps has a guard in the converter; this covers the rest.
    //
    // It names no AvatarBridge type, so it runs in a bare project with only VRCFury in it. It reads
    // the components the way Unity saves them, never VRCFury's source: the full serialized
    // property list of a fresh component is logged, so a renamed field shows up beside the gap.
    public static class VrcFuryContractProbe
    {
        enum Kind { Bool, Strings, Rules }

        static readonly (string Type, string Field, Kind Kind)[] Required =
        {
            ("VF.Component.VRCFuryHapticPlug", "enableSps", Kind.Bool),
            ("VF.Component.VRCFuryHapticPlug", "spsOverrun", Kind.Bool),
            ("VF.Component.VRCFuryHapticPlug", "useHipAvoidance", Kind.Bool),
            ("VF.Component.VRCFuryHapticPlug", "useSharedTag", Kind.Bool),
            ("VF.Component.VRCFuryHapticPlug", "includeTags", Kind.Rules),
            ("VF.Component.VRCFuryHapticPlug", "excludeTags", Kind.Rules),
            ("VF.Component.VRCFuryHapticSocket", "useSharedTag", Kind.Bool),
            ("VF.Component.VRCFuryHapticSocket", "tags", Kind.Strings),
        };

        // Fields of one include or exclude rule.
        static readonly (string Field, Type Type)[] RuleFields =
        {
            ("tag", typeof(string)), ("allowSelf", typeof(bool)), ("allowOthers", typeof(bool)),
        };

        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        [MenuItem("Tools/Avatar Bridge/Dev/Checks/VRCFury SPS contract")]
        public static void Run()
        {
            int fail = 0;
            void Check(string label, bool ok)
            {
                Debug.Log($"[VrcFuryContractProbe] {(ok ? "ok  " : "WRONG")} {label}");
                if (!ok) fail++;
            }

            var types = new Dictionary<string, Type>();
            foreach (var name in Required.Select(r => r.Type).Distinct())
            {
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
                Check($"{name} exists", type != null);
                if (type != null) types[name] = type;
            }

            foreach (var (typeName, fieldName, kind) in Required)
            {
                if (!types.TryGetValue(typeName, out var type)) continue;
                var field = type.GetField(fieldName, Any);
                string label = $"{type.Name}.{fieldName} as {kind}";
                if (field == null) { Check(label + ": missing", false); continue; }
                var element = Element(field.FieldType);
                switch (kind)
                {
                    case Kind.Bool:
                        Check(label, field.FieldType == typeof(bool));
                        break;
                    case Kind.Strings:
                        Check(label, element == typeof(string));
                        break;
                    case Kind.Rules:
                        Check(label + " (a list)", element != null);
                        if (element == null) break;
                        foreach (var (rule, ruleType) in RuleFields)
                        {
                            var f = element.GetField(rule, Any);
                            Check($"{type.Name}.{fieldName}[].{rule} as {ruleType.Name}", f != null && f.FieldType == ruleType);
                        }
                        break;
                }
            }

            // What Unity saves for each, for reading a rename off the log.
            var host = new GameObject("__VrcFuryContractProbe");
            try
            {
                foreach (var type in types.Values)
                {
                    var component = host.AddComponent(type);
                    var so = new SerializedObject(component);
                    var lines = new List<string>();
                    var it = so.GetIterator();
                    for (bool enter = true; it.Next(enter); enter = it.propertyType != SerializedPropertyType.String)
                    {
                        lines.Add($"{it.propertyPath} ({it.propertyType})");
                    }
                    Debug.Log($"[VrcFuryContractProbe] saved fields of {type.Name}:\n  " + string.Join("\n  ", lines));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }

            Debug.Log(fail == 0
                ? "[VrcFuryContractProbe] PASS: every SPS field the converter reads is there."
                : $"[VrcFuryContractProbe] FAIL: {fail} field(s) missing or retyped.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        static Type Element(Type t)
        {
            if (t.IsArray) return t.GetElementType();
            if (t.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(t)) return t.GetGenericArguments()[0];
            return null;
        }
    }
}
