using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // An AvatarBridge error lands in the console buffer, and the report zip holds what the user
    // wrote, that buffer and the run's reports, and nothing else.
    public static class ProblemReportTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Problem report zip")]
        public static void Run()
        {
            failures = 0;
            const string folder = "Assets/AvatarBridgeOutput/ProblemReportTest";
            string full = Path.GetFullPath(folder);
            string zip = null;
            try
            {
                Debug.LogError("[AvatarBridge] Error: [Test] a problem for the report test");
                Check(ProblemReport.RecentText().Contains("a problem for the report test"), "the error is kept for the report");
                Check(ProblemReport.Tool == "AvatarBridge", "an AvatarBridge error names AvatarBridge");
                Debug.LogError("[YAPS] ✗ Bake: a YAPS problem for the report test");
                Check(ProblemReport.Tool == "YAPS", "a failed YAPS step names YAPS");

                Directory.CreateDirectory(full);
                File.WriteAllText(Path.Combine(full, "ConversionReport.md"), "# report");
                File.WriteAllText(Path.Combine(full, "Diagnostics.md"), "# diagnostics");
                File.WriteAllBytes(Path.Combine(full, "Body.png"), new byte[] { 1, 2, 3 });
                zip = ProblemReport.WriteZip("AvatarBridge", "The toggle stayed on.");
                Check(Path.GetDirectoryName(zip) == full.TrimEnd(Path.DirectorySeparatorChar), $"beside the newest report ({zip})");
                // Read back with Windows' own unzip, as the person opening it would.
                string outDir = Path.Combine(Path.GetTempPath(), "ProblemReportTest");
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
                var unzip = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("tar", $"-xf \"{zip}\" -C \"{Directory.CreateDirectory(outDir).FullName}\"")
                    { UseShellExecute = false, CreateNoWindow = true });
                unzip.WaitForExit();
                var names = Directory.GetFiles(outDir).Select(Path.GetFileName).OrderBy(n => n).ToArray();
                Check(unzip.ExitCode == 0 && names.SequenceEqual(new[] { "Console.txt", "ConversionReport.md", "Diagnostics.md", "What happened.txt" }),
                    $"it unzips, text only (exit {unzip.ExitCode}): " + string.Join(", ", names));
                string what = File.Exists(Path.Combine(outDir, "What happened.txt")) ? File.ReadAllText(Path.Combine(outDir, "What happened.txt")) : "";
                Check(what.Contains("The toggle stayed on.") && what.Contains("AvatarBridge:"), "it holds the user's words and the versions");
                Directory.Delete(outDir, true);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                if (Directory.Exists(full)) Directory.Delete(full, true);
            }
            Debug.Log(failures == 0 ? "[ProblemReportTest] PASS" : $"[ProblemReportTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[ProblemReportTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
