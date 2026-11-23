using System;
using System.Collections.Generic;
using System.IO;
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
        static readonly List<string> Timed = new List<string>();
        static bool _pending;
        static string _tool = "AvatarBridge";
        // Every console warning and error since the conversion started, its own or anyone's.
        static int _warnings, _errors;
        static long _startMemory;

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
            if (bad) System.Threading.Interlocked.Increment(ref _errors);
            else if (type == LogType.Warning) System.Threading.Interlocked.Increment(ref _warnings);
            // Each step's time, for Diagnostics.md: where a slow conversion spent it. The step logs
            // that end in " (ms): " stay for this; a reworded one drops out of Diagnostics.md.
            if (ours && condition.Contains(" (ms): "))
                lock (Timed)
                {
                    Timed.Add(condition.Replace("[AvatarBridge] ", ""));
                    if (Timed.Count > 50) Timed.RemoveAt(0);
                }
            if (!ours && !bad) return;
            lock (Recent)
            {
                Recent.Add($"{type}: {condition}" + (bad && !string.IsNullOrEmpty(stack) ? "\n" + Trim(stack, 1500) : ""));
                if (Recent.Count > Kept) Recent.RemoveAt(0);
            }
            if (!ours || !bad) return;
            lock (Problems)
            {
                if (Problems.Count < 20) Problems.Add(Trim(condition, 400));
                if (condition.StartsWith("[YAPS]") || stack != null && stack.Contains("AvatarBridge.Yaps")) _tool = "YAPS";
            }
            if (Application.isBatchMode || !Ask) return;
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
                    "lines and the last run's reports, with your PC's processor, memory and graphics card. It holds your project's file paths and object names, " +
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
            scroll.Add(BridgeElements.Footer(_tool == "YAPS" ? BridgeLinks.YapsHelp : BridgeLinks.Repo + "#reporting-a-bug", OpenIssue));
        }

        // A log line as a person reads it: the tool's tag and the level off the front.
        static string Plain(string line) => Regex.Replace(line, @"^\[(AvatarBridge|YAPS)\]\s*(Error:\s*)?(✗\s*)?", "");

        internal static string Tool => _tool;

        void Dismiss()
        {
            lock (Problems) Problems.Clear();
            _tool = "AvatarBridge";
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

            var files = new List<(string name, string text)> { ("What happened.txt", sb.ToString()), ("Console.txt", console) };
            if (reports.Count > 0)
                files.AddRange(Directory.GetFiles(folder, "*.md").Select(md => (Path.GetFileName(md), File.ReadAllText(md))));
            File.WriteAllBytes(zip, StoredZip(files));
            AssetDatabase.Refresh();
            return zip;
        }

        // A zip of text files, stored rather than compressed: System.IO.Compression's ZipArchive is
        // missing from a project on the .NET Framework profile, and the tool would not compile there.
        static byte[] StoredZip(List<(string name, string text)> files)
        {
            var output = new MemoryStream();
            var w = new BinaryWriter(output);
            var central = new MemoryStream();
            var c = new BinaryWriter(central);
            foreach (var (name, text) in files)
            {
                byte[] n = Encoding.UTF8.GetBytes(name), data = new UTF8Encoding(false).GetBytes(text);
                uint crc = Crc32(data);
                uint at = (uint) output.Position;
                // Local header: version 2.0, the UTF-8 name flag, stored, no date.
                w.Write(0x04034b50u); w.Write((ushort) 20); w.Write((ushort) 0x0800); w.Write((ushort) 0);
                w.Write(0u); w.Write(crc); w.Write((uint) data.Length); w.Write((uint) data.Length);
                w.Write((ushort) n.Length); w.Write((ushort) 0); w.Write(n); w.Write(data);
                c.Write(0x02014b50u); c.Write((ushort) 20); c.Write((ushort) 20); c.Write((ushort) 0x0800); c.Write((ushort) 0);
                c.Write(0u); c.Write(crc); c.Write((uint) data.Length); c.Write((uint) data.Length);
                c.Write((ushort) n.Length); c.Write((ushort) 0); c.Write((ushort) 0); c.Write((ushort) 0); c.Write((ushort) 0);
                c.Write(0u); c.Write(at); c.Write(n);
            }
            uint start = (uint) output.Position;
            c.Flush();
            w.Write(central.ToArray());
            w.Write(0x06054b50u); w.Write((ushort) 0); w.Write((ushort) 0);
            w.Write((ushort) files.Count); w.Write((ushort) files.Count);
            w.Write((uint) central.Length); w.Write(start); w.Write((ushort) 0);
            w.Flush();
            return output.ToArray();
        }

        static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++) crc = (crc >> 1) ^ (0xEDB88320u & (uint) -(int) (crc & 1));
            }
            return ~crc;
        }

        // The converter's output folder as last saved, without needing the CCK to read the settings.
        static string OutputRoot()
        {
            var m = Regex.Match(EditorPrefs.GetString("AvatarBridge.Settings", ""), "\"outputFolder\"\\s*:\\s*\"([^\"]+)\"");
            string root = m.Success ? m.Groups[1].Value : "";
            return root.StartsWith("Assets") ? root : "Assets/AvatarBridgeOutput";
        }

        internal static void StartRun()
        {
            _warnings = 0;
            _errors = 0;
            _startMemory = EditorMemory().now;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct ProcessMemory
        {
            public uint Size, PageFaults;
            public UIntPtr PeakWorkingSet, WorkingSet, PeakPagedPool, PagedPool, PeakNonPagedPool, NonPagedPool,
                PageFile, PeakPageFile;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool K32GetProcessMemoryInfo(IntPtr process, ref ProcessMemory counters, uint size);

        // The editor's working set and its peak. Process.WorkingSet64 reads 0 under Unity's Mono.
        internal static (long now, long peak) EditorMemory()
        {
            try
            {
                var m = new ProcessMemory { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<ProcessMemory>() };
                return K32GetProcessMemoryInfo(GetCurrentProcess(), ref m, m.Size)
                    ? ((long)m.WorkingSet.ToUInt64(), (long)m.PeakWorkingSet.ToUInt64()) : (0, 0);
            }
            catch
            {
                return (0, 0);
            }
        }

        internal static (int warnings, int errors, long startMemory) RunCounts() => (_warnings, _errors, _startMemory);

        // The step times logged since the last call.
        internal static List<string> TakeTimings()
        {
            lock (Timed)
            {
                var taken = Timed.ToList();
                Timed.Clear();
                return taken;
            }
        }

        internal static string RecentText()
        {
            lock (Recent) return string.Join("\n\n", Recent);
        }

        static string Trim(string s, int max) => s == null ? "" : s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
