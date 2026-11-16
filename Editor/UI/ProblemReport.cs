using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge
{
    // When AvatarBridge or YAPS logs an error, or one of its own frames throws, a window asks what
    // the user was doing and packs a small zip into the output folder: what they wrote, the recent
    // console lines and the last run's reports, never the avatar's own files. Nothing is sent: it
    // opens a pre-filled GitHub issue and shows the zip to drag in.
    [InitializeOnLoad]
    public class ProblemReport : EditorWindow
    {
        const string AskKey = "AvatarBridge.AskToReport";
        const string MenuPath = "Tools/Avatar Bridge/Ask me to report problems";
        const int Kept = 200;

        static readonly List<string> Recent = new List<string>();
        static readonly List<string> Problems = new List<string>();
        static bool _pending;
        static string _tool = "AvatarBridge";

        string _what = "";
        string _zip;

        static ProblemReport() => Application.logMessageReceived += OnLog;

        static bool Ask => EditorPrefs.GetBool(AskKey, true);

        [MenuItem(MenuPath, false, 2000)]
        static void ToggleAsk() => EditorPrefs.SetBool(AskKey, !Ask);

        [MenuItem(MenuPath, true)]
        static bool ToggleAskCheck()
        {
            Menu.SetChecked(MenuPath, Ask);
            return true;
        }

        static void OnLog(string condition, string stack, LogType type)
        {
            bool ours = condition.StartsWith("[AvatarBridge]") || condition.StartsWith("[YAPS]")
                        || stack != null && stack.Contains("AvatarBridge.") && !stack.Contains("AvatarBridge.Regression");
            bool bad = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            if (!ours && !bad) return;
            lock (Recent)
            {
                Recent.Add($"{type}: {condition}" + (bad && !string.IsNullOrEmpty(stack) ? "\n" + Trim(stack, 1500) : ""));
                if (Recent.Count > Kept) Recent.RemoveAt(0);
            }
            if (!ours || !bad || Application.isBatchMode || !Ask) return;
            lock (Problems)
            {
                if (Problems.Count < 20) Problems.Add(Trim(condition, 400));
                if (condition.StartsWith("[YAPS]") || stack != null && stack.Contains("AvatarBridge.Yaps")) _tool = "YAPS";
            }
            // After whatever logged it has finished: a conversion logs many errors in one call.
            if (_pending) return;
            _pending = true;
            EditorApplication.delayCall += Open;
        }

        static void Open()
        {
            _pending = false;
            if (Problems.Count == 0) return;
            var window = HasOpenInstances<ProblemReport>() ? GetWindow<ProblemReport>() : CreateInstance<ProblemReport>();
            window.titleContent = new GUIContent("Report a problem");
            window.minSize = new Vector2(420, 360);
            window.ShowUtility();
            window.Rebuild();
        }

        void CreateGUI() => Rebuild();

        void Rebuild()
        {
            var root = BridgeElements.Root(rootVisualElement);
            root.Clear();
            var scroll = BridgeElements.Scroll();
            root.Add(scroll);
            string[] problems;
            lock (Problems) problems = Problems.ToArray();

            // A card and the footer, as every other window.
            var card = new BridgeElements.Card($"Something went wrong in {_tool}");
            scroll.Add(card);
            var b = card.Body;
            b.Add(BridgeElements.Notice(Tone.Bad, Plain(problems.FirstOrDefault() ?? "An error was logged."),
                problems.Length > 1 ? string.Join("\n", problems.Skip(1).Select(Plain)) : null));
            if (_zip == null)
            {
                b.Add(BridgeElements.Hint("What were you doing, and what did you expect to happen?"));
                var what = new TextField { multiline = true, value = _what };
                what.style.minHeight = 90;
                what.style.whiteSpace = WhiteSpace.Normal;
                what.RegisterValueChangedCallback(e => _what = e.newValue);
                b.Add(what);
                b.Add(BridgeElements.Hint(
                    "Make the report packs a small zip into your output folder: what you wrote, the recent console " +
                    "lines and the last conversion's report. It holds your project's file paths and object names, " +
                    "never the avatar's files. Nothing is sent: it opens a GitHub issue for you to check and submit."));
                b.Add(BridgeElements.ButtonRow(
                    BridgeElements.Btn("Make the report", MakeReport, "Writes the zip and opens a pre-filled GitHub issue.", ButtonKind.Strong),
                    BridgeElements.Btn("Not now", Dismiss)));
            }
            else
            {
                b.Add(BridgeElements.Notice(Tone.Good, $"Saved {Path.GetFileName(_zip)}. Drag it into the GitHub issue " +
                    "that opened, check the text, then press Submit."));
                b.Add(BridgeElements.ButtonRow(
                    BridgeElements.Btn("Show the zip", () => EditorUtility.RevealInFinder(_zip), _zip, ButtonKind.Strong),
                    BridgeElements.Btn("Open the issue again", OpenIssue),
                    BridgeElements.Btn("Close", Dismiss)));
            }
            b.Add(BridgeElements.Bind("Ask me when something goes wrong",
                "Off: errors stay in the Console. Turn it back on under Tools ▸ Avatar Bridge.",
                Ask, v => EditorPrefs.SetBool(AskKey, v)));
            scroll.Add(BridgeElements.Footer(BridgeLinks.Repo + "#reporting-a-bug", OpenIssue));
        }

        // A log line as a person reads it: the tool's tag and the level off the front.
        static string Plain(string line) => Regex.Replace(line, @"^\[(AvatarBridge|YAPS)\]\s*(Error:\s*)?", "");

        void Dismiss()
        {
            lock (Problems) Problems.Clear();
            _zip = null;
            _what = "";
            Close();
        }

        void MakeReport()
        {
            try
            {
                _zip = WriteZip(_tool, _what);
                OpenIssue();
                EditorUtility.RevealInFinder(_zip);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Report a problem", "The zip could not be written: " + e.Message +
                    "\nThe issue opens without it.", "OK");
                OpenIssue();
            }
            BridgeElements.Defer(rootVisualElement, Rebuild);
        }

        void OpenIssue()
        {
            string environment = (_tool == "YAPS" ? "Tool:          YAPS\n" : "") + BridgeLinks.BuildDiagnostics();
            string what = Trim(_what, 1500);
            string first;
            lock (Problems) first = Problems.FirstOrDefault() ?? "";
            Application.OpenURL(BridgeLinks.Repo + "/issues/new?template=bug_report.yml" +
                "&what_happened=" + Uri.EscapeDataString(what + (what.Length > 0 ? "\n\n" : "") + "Error: " + Trim(first, 400)) +
                "&environment=" + Uri.EscapeDataString(environment));
        }

        // The zip beside the last run's report, else in the output root. Only text: what the user
        // wrote, the console lines, and the .md reports written in the last hour.
        internal static string WriteZip(string tool, string what)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string outputRoot = OutputRoot();
            string full = Path.Combine(projectRoot, outputRoot);
            var reports = Directory.Exists(full)
                ? Directory.GetFiles(full, "*Report.md", SearchOption.AllDirectories)
                    .Where(f => File.GetLastWriteTime(f) > DateTime.Now.AddHours(-1))
                    .OrderByDescending(File.GetLastWriteTime).ToList()
                : new List<string>();
            string folder = reports.Count > 0 ? Path.GetDirectoryName(reports[0]) : full;
            Directory.CreateDirectory(folder);
            string zip = Path.Combine(folder, $"{tool} problem report {DateTime.Now:yyyy-MM-dd HHmm}.zip");

            var sb = new StringBuilder();
            sb.AppendLine("What happened, in the user's words:").AppendLine(string.IsNullOrWhiteSpace(what) ? "(nothing written)" : what).AppendLine();
            sb.AppendLine(BridgeLinks.BuildDiagnostics());
            lock (Problems) sb.AppendLine("Errors:").AppendLine(string.Join("\n", Problems));
            string console;
            lock (Recent) console = string.Join("\n\n", Recent);

            using (var stream = new FileStream(zip, FileMode.Create))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                void Add(string name, string text)
                {
                    using (var w = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false))) w.Write(text);
                }
                Add("What happened.txt", sb.ToString());
                Add("Console.txt", console);
                if (reports.Count > 0)
                    foreach (var md in Directory.GetFiles(folder, "*.md"))
                        Add(Path.GetFileName(md), File.ReadAllText(md));
            }
            AssetDatabase.Refresh();
            return zip;
        }

        // The converter's output folder as last saved, without needing the CCK to read the settings.
        static string OutputRoot()
        {
            var m = Regex.Match(EditorPrefs.GetString("AvatarBridge.Settings", ""), "\"outputFolder\"\\s*:\\s*\"([^\"]+)\"");
            string root = m.Success ? m.Groups[1].Value : "";
            return root.StartsWith("Assets") ? root : "Assets/AvatarBridgeOutput";
        }

        internal static string RecentText()
        {
            lock (Recent) return string.Join("\n\n", Recent);
        }

        static string Trim(string s, int max) => s == null ? "" : s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
