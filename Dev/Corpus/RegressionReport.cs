// The corpus run as a page a tester can read. Parses the digest
// folders the harness writes and renders one self-contained HTML
// file: what needs eyes first, then what changed, then what held.
// No Unity types on purpose, so the same file compiles standalone
// under mono and inside the corpus project.
//
//   mono csc.exe /out:rep.exe RegressionReport.cs
//   mono rep.exe <baselineDir> <currentDir> <out.html> [runLabel]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AvatarBridge.Regression
{
    public static class RegressionReport
    {
        class Avatar
        {
            public string Name;
            public string File;
            public bool HasBaseline;
            public bool Changed;
            public int LinesRemoved, LinesAdded;
            public string Exception;
            public int Converted, Approximated, Skipped, Warnings, Errors;
            public List<string> WarningLines = new List<string>();
            public List<string> ErrorLines = new List<string>();
            public int[] Sweep;                       // params responded stuck refused invalid
            public List<string> SweepNames = new List<string>();
            public List<string> SweepNew = new List<string>();
            public bool SweepInvalidNew;

            public int Rank =>
                Exception != null ? 0 :
                Errors > 0 || ErrorLines.Count > 0 ? 1 :
                SweepNew.Count > 0 || SweepInvalidNew ? 2 :
                !HasBaseline ? 3 :
                Changed ? 4 : 5;

            public string Status =>
                Exception != null ? "threw" :
                Errors > 0 || ErrorLines.Count > 0 ? "errors" :
                SweepNew.Count > 0 || SweepInvalidNew ? "sweep worse" :
                !HasBaseline ? "no baseline" :
                Changed ? $"changed  −{LinesRemoved} +{LinesAdded}" : "unchanged";
        }

        public static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("usage: rep.exe <baselineDir> <currentDir> <out.html> [runLabel]");
                return 2;
            }
            string label = args.Length > 3 ? args[3] : Path.GetFileName(Path.GetDirectoryName(args[1] + "/"));
            Write(args[0], args[1], args[2], label);
            Console.WriteLine("wrote " + args[2]);
            return 0;
        }

        public static string Write(string baselineDir, string currentDir, string outPath, string label)
        {
            var avatars = new List<Avatar>();
            foreach (var file in Directory.GetFiles(currentDir, "*.txt").OrderBy(f => f))
            {
                var a = Parse(file);
                string basePath = Path.Combine(baselineDir, Path.GetFileName(file));
                a.HasBaseline = File.Exists(basePath);
                if (a.HasBaseline)
                {
                    string before = File.ReadAllText(basePath);
                    string now = File.ReadAllText(file);
                    if (before != now)
                    {
                        a.Changed = true;
                        var oldLines = new HashSet<string>(before.Split('\n'));
                        var newLines = new HashSet<string>(now.Split('\n'));
                        a.LinesRemoved = oldLines.Except(newLines).Count();
                        a.LinesAdded = newLines.Except(oldLines).Count();
                    }
                    var old = Parse(basePath);
                    a.SweepNew = a.SweepNames.Where(n => !old.SweepNames.Contains(n)).ToList();
                    a.SweepInvalidNew = a.Sweep != null && a.Sweep[4] != 0
                        && (old.Sweep == null || old.Sweep[4] == 0);
                }
                else
                {
                    a.SweepNew = a.SweepNames.ToList();
                }
                avatars.Add(a);
            }

            string info = Path.Combine(currentDir, "_run.info");
            string runInfo = File.Exists(info) ? File.ReadAllText(info).Trim().Replace("\n", " · ") : "";

            File.WriteAllText(outPath, Render(label, runInfo, avatars), new UTF8Encoding(false));
            return outPath;
        }

        static Avatar Parse(string file)
        {
            var a = new Avatar { File = Path.GetFileName(file), Name = Path.GetFileNameWithoutExtension(file) };
            bool inSweep = false;
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.TrimEnd();
                var m = Regex.Match(line, @"^avatar: (.+)$");
                if (m.Success) { a.Name = m.Groups[1].Value; continue; }

                m = Regex.Match(line, @"^converted=(\d+) approximated=(\d+) skipped=(\d+) warnings=(\d+) errors=(\d+)");
                if (m.Success)
                {
                    a.Converted = int.Parse(m.Groups[1].Value);
                    a.Approximated = int.Parse(m.Groups[2].Value);
                    a.Skipped = int.Parse(m.Groups[3].Value);
                    a.Warnings = int.Parse(m.Groups[4].Value);
                    a.Errors = int.Parse(m.Groups[5].Value);
                    continue;
                }
                m = Regex.Match(line, @"^  (WARNING|ERROR) (.+)$");
                if (m.Success)
                {
                    if (m.Groups[1].Value == "ERROR") a.ErrorLines.Add(m.Groups[2].Value);
                    else a.WarningLines.Add(m.Groups[2].Value);
                    continue;
                }
                m = Regex.Match(line, @"^EXCEPTION (.+)$");
                if (m.Success) { a.Exception = m.Groups[1].Value; continue; }

                m = Regex.Match(line, @"^\[sweep\] params=(\d+) responded=(\d+) stuck=(\d+) refused=(\d+) invalid=(\d+)");
                if (m.Success)
                {
                    a.Sweep = new[] { 1, 2, 3, 4, 5 }.Select(i => int.Parse(m.Groups[i].Value)).ToArray();
                    inSweep = true;
                    continue;
                }
                if (inSweep)
                {
                    if (line.StartsWith("  stuck ") || line.StartsWith("  refused ")) a.SweepNames.Add(line.Trim());
                    else inSweep = false;
                }
            }
            return a;
        }

        static string Render(string label, string runInfo, List<Avatar> avatars)
        {
            var eyes = avatars.Where(a => a.Rank <= 2).OrderBy(a => a.Rank).ThenBy(a => a.Name).ToList();
            var fresh = avatars.Where(a => a.Rank == 3).OrderBy(a => a.Name).ToList();
            var changed = avatars.Where(a => a.Rank == 4).OrderBy(a => a.Name).ToList();
            var clean = avatars.Where(a => a.Rank == 5).OrderBy(a => a.Name).ToList();
            var swept = avatars.Where(a => a.Sweep != null).ToList();

            var sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset='utf-8'>");
            sb.Append("<meta name='viewport' content='width=device-width,initial-scale=1'>");
            sb.Append("<title>Corpus ").Append(H(label)).Append("</title><style>");
            sb.Append(@"
:root{--bg:#f5f4f0;--card:#fff;--ink:#232120;--dim:#77716c;--line:#e2ded8;
--bad:#b3403a;--warn:#a06a1f;--ok:#3a7a4e;--chip:#eeebe6}
@media(prefers-color-scheme:dark){:root{--bg:#191817;--card:#211f1e;--ink:#e8e4df;
--dim:#95908a;--line:#37332f;--bad:#e07570;--warn:#d9a05a;--ok:#7dba90;--chip:#2c2926}}
body{margin:0;background:var(--bg);color:var(--ink);
font:15px/1.5 'Segoe UI',system-ui,sans-serif;padding:1.2rem}
main{max-width:64rem;margin:0 auto}
h1{font-size:1.35rem;margin:.2rem 0}h2{font-size:1.05rem;margin:1.6rem 0 .6rem}
.sub{color:var(--dim);font-size:.85rem}
.tiles{display:flex;gap:.6rem;flex-wrap:wrap;margin:1rem 0}
.tile{background:var(--card);border:1px solid var(--line);border-radius:.5rem;
padding:.5rem .9rem;min-width:5.2rem}
.tile b{display:block;font-size:1.25rem}.tile span{color:var(--dim);font-size:.78rem}
.tile.bad b{color:var(--bad)}.tile.warn b{color:var(--warn)}.tile.ok b{color:var(--ok)}
details{background:var(--card);border:1px solid var(--line);border-radius:.5rem;
margin:.4rem 0;overflow:hidden}
summary{padding:.55rem .9rem;cursor:pointer;display:flex;gap:.7rem;align-items:baseline}
summary::-webkit-details-marker{display:none}
.name{font-weight:600}.chip{background:var(--chip);border-radius:1rem;padding:.05rem .6rem;
font-size:.75rem;color:var(--dim);white-space:nowrap}
.chip.bad{color:var(--bad)}.chip.warn{color:var(--warn)}.chip.ok{color:var(--ok)}
.body{padding:.2rem .9rem .8rem;border-top:1px solid var(--line);overflow-x:auto}
.body p{margin:.45rem 0}.mono{font-family:Consolas,monospace;font-size:.82rem}
ul{margin:.3rem 0;padding-left:1.2rem}li{margin:.15rem 0}
.dim{color:var(--dim)}
</style></head><body><main>");

            sb.Append("<h1>Corpus run — ").Append(H(label)).Append("</h1>");
            sb.Append("<div class='sub'>").Append(H(runInfo)).Append("</div>");

            int stuck = swept.Sum(a => a.Sweep[2]);
            int refused = swept.Sum(a => a.Sweep[3]);
            int invalid = swept.Sum(a => a.Sweep[4]);
            sb.Append("<div class='tiles'>");
            Tile(sb, avatars.Count.ToString(), "avatars", "");
            Tile(sb, eyes.Count.ToString(), "need eyes", eyes.Count > 0 ? "bad" : "ok");
            Tile(sb, changed.Count.ToString(), "changed", changed.Count > 0 ? "warn" : "ok");
            Tile(sb, clean.Count.ToString(), "unchanged", "ok");
            Tile(sb, swept.Sum(a => a.Sweep[0]).ToString(), "params swept", "");
            Tile(sb, swept.Sum(a => a.Sweep[1]).ToString(), "responded", "");
            Tile(sb, stuck.ToString(), "stuck", stuck > 0 ? "warn" : "ok");
            Tile(sb, refused.ToString(), "refused", refused > 0 ? "warn" : "ok");
            Tile(sb, invalid.ToString(), "invalid", invalid > 0 ? "warn" : "ok");
            sb.Append("</div>");

            Section(sb, "Needs eyes", eyes,
                "A thrown conversion, an error, or a toggle newly stuck against the baseline.");
            Section(sb, "No baseline yet", fresh, "First run for these; nothing to compare.");
            Section(sb, "Changed", changed, "The digest moved. Same-shaped changes across many " +
                "avatars usually mean one intended fix; an odd one out is the thing to open.");
            Section(sb, "Held steady", clean, null);

            sb.Append("<p class='sub'>Generated ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
              .Append(" by AvatarBridge's regression reporter.</p></main></body></html>");
            return sb.ToString();
        }

        static void Tile(StringBuilder sb, string n, string label, string cls)
        {
            sb.Append("<div class='tile ").Append(cls).Append("'><b>").Append(n)
              .Append("</b><span>").Append(label).Append("</span></div>");
        }

        static void Section(StringBuilder sb, string title, List<Avatar> list, string note)
        {
            if (list.Count == 0) return;
            sb.Append("<h2>").Append(title).Append(" (").Append(list.Count).Append(")</h2>");
            if (note != null) sb.Append("<div class='sub'>").Append(H(note)).Append("</div>");
            foreach (var a in list) Card(sb, a);
        }

        static void Card(StringBuilder sb, Avatar a)
        {
            sb.Append("<details><summary><span class='name'>").Append(H(a.Name)).Append("</span>");
            string cls = a.Rank <= 1 ? "bad" : a.Rank == 2 ? "warn" : a.Rank == 5 ? "ok" : "";
            sb.Append("<span class='chip ").Append(cls).Append("'>").Append(H(a.Status)).Append("</span>");
            if (a.Warnings > 0)
                sb.Append("<span class='chip'>").Append(a.Warnings).Append(" warnings</span>");
            if (a.Sweep != null && (a.Sweep[2] > 0 || a.Sweep[3] > 0))
                sb.Append("<span class='chip warn'>").Append(a.Sweep[2]).Append(" stuck · ")
                  .Append(a.Sweep[3]).Append(" refused</span>");
            sb.Append("</summary><div class='body'>");

            if (a.Exception != null)
                sb.Append("<p class='mono'>EXCEPTION ").Append(H(a.Exception)).Append("</p>");
            sb.Append("<p class='dim mono'>converted ").Append(a.Converted)
              .Append(" · approximated ").Append(a.Approximated)
              .Append(" · skipped ").Append(a.Skipped).Append("</p>");
            if (a.SweepNew.Count > 0)
            {
                sb.Append("<p><b>Newly failing toggles</b></p><ul>");
                foreach (var n in a.SweepNew) sb.Append("<li class='mono'>").Append(H(n)).Append("</li>");
                sb.Append("</ul>");
            }
            if (a.SweepInvalidNew)
                sb.Append("<p>The sweep drove every parameter and nothing visibly responded.</p>");
            if (a.ErrorLines.Count > 0)
            {
                sb.Append("<p><b>Errors</b></p><ul>");
                foreach (var e in a.ErrorLines) sb.Append("<li class='mono'>").Append(H(e)).Append("</li>");
                sb.Append("</ul>");
            }
            if (a.WarningLines.Count > 0)
            {
                sb.Append("<p><b>Warnings</b></p><ul>");
                foreach (var w in a.WarningLines.Take(40))
                    sb.Append("<li class='mono'>").Append(H(w)).Append("</li>");
                if (a.WarningLines.Count > 40)
                    sb.Append("<li class='dim'>… and ").Append(a.WarningLines.Count - 40).Append(" more</li>");
                sb.Append("</ul>");
            }
            sb.Append("</div></details>");
        }

        static string H(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
