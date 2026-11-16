using System.IO;
using System.IO.Compression;
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

                Directory.CreateDirectory(full);
                File.WriteAllText(Path.Combine(full, "ConversionReport.md"), "# report");
                File.WriteAllText(Path.Combine(full, "Diagnostics.md"), "# diagnostics");
                File.WriteAllBytes(Path.Combine(full, "Body.png"), new byte[] { 1, 2, 3 });
                zip = ProblemReport.WriteZip("AvatarBridge", "The toggle stayed on.");
                Check(Path.GetDirectoryName(zip) == full.TrimEnd(Path.DirectorySeparatorChar), $"beside the newest report ({zip})");
                using (var archive = new ZipArchive(File.OpenRead(zip), ZipArchiveMode.Read))
                {
                    var names = archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToArray();
                    Check(names.SequenceEqual(new[] { "Console.txt", "ConversionReport.md", "Diagnostics.md", "What happened.txt" }),
                        "text only: " + string.Join(", ", names));
                    string what;
                    using (var r = new StreamReader(archive.GetEntry("What happened.txt").Open())) what = r.ReadToEnd();
                    Check(what.Contains("The toggle stayed on.") && what.Contains("AvatarBridge:"), "it holds the user's words and the versions");
                }
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
