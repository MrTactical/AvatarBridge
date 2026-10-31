#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // The conversion diff's comparison, on snapshots with known answers.
    public static class ConversionDiffTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Conversion diff")]
        public static void Run()
        {
            failures = 0;
            var before = new[]
            {
                "version AvatarBridge = 4.7.2", "setting stripGogoLoco = True", "count Warning = 2",
                "layer [FX] Hat · 2 states · weight 1 · Override", "menu Hat (Hat, Toggle)", "component Animator = 1",
            };
            var now = new List<string>
            {
                "version AvatarBridge = 5.0.0", "setting stripGogoLoco = False", "count Warning = 3",
                "layer [FX] Hat · 2 states · weight 1 · Override", "report Warning [Sync] over the limit", "component Animator = 2",
            };
            var report = new BridgeReport();
            ConversionDiff.Compare(report, before, now, new DateTime(2026, 10, 15));
            var entry = report.Entries.Single();
            Debug.Log($"[ConversionDiffTest]   {entry.Subject}: {entry.Detail}\n{report.ChangesCard}");
            Check(entry.Subject == "Since the last conversion: 1 component, 1 menu entry, 1 report line changed", "summary counts each kind once");
            Check(entry.Detail.Contains("stripGogoLoco: True to False") && entry.Detail.Contains("AvatarBridge: 4.7.2 to 5.0.0"), "names the settings and version that changed");
            Check(report.ChangesCard.Contains("- menu Hat (Hat, Toggle)") && report.ChangesCard.Contains("+ report Warning [Sync] over the limit"), "lists what went and what came");
            Check(!report.ChangesCard.Contains("layer [FX] Hat"), "an unchanged layer is not listed");

            report = new BridgeReport();
            ConversionDiff.Compare(report, before, before.ToList(), new DateTime(2026, 10, 15));
            Check(report.Entries.Single().Subject == "Nothing changed since the last conversion" && report.ChangesCard == null, "the same snapshot says nothing changed");

            report = new BridgeReport();
            var versionOnly = before.ToList();
            versionOnly[0] = "version AvatarBridge = 5.0.0";
            ConversionDiff.Compare(report, before, versionOnly, new DateTime(2026, 10, 15));
            Check(report.Entries.Single().Subject == "Since the last conversion: only settings or the version changed", "a new version alone is not called a count change");

            Debug.Log(failures == 0 ? "[ConversionDiffTest] PASS" : $"[ConversionDiffTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[ConversionDiffTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
