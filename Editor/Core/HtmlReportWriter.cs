#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    // The conversion report as a single self-contained web page, written beside the markdown.
    //
    // The markdown stays the artefact for bug reports; grep-able, diff-able, attachable. This
    // is the page a person actually reads: what happened, how much of it, and where to look,
    // with the numbers drawn instead of listed. Everything is inline; no CDN, no external
    // fonts, no script includes; because the file gets opened from disk and shared around,
    // and a report that needs the internet to render is a report that renders blank.
    //
    // Chart rules followed deliberately: outcome colours are STATUS colours and each slice is
    // also named and counted in text, so identity never rides on colour alone; the category
    // chart is one measure across categories, so it uses one hue rather than a rainbow; dark
    // mode is its own palette step, not an inversion. The palette validator could not run on
    // this machine (no Node), so the risky adjacency; amber beside orange; is avoided by
    // construction instead: "approximated" is blue.
    public static class HtmlReportWriter
    {
        public static void Write(BridgeContext ctx)
        {
            var report = ctx.Report;
            string avatar = ctx.Target != null ? ctx.Target.name : "avatar";

            int converted = report.CountOf(ReportStatus.Converted);
            int approximated = report.CountOf(ReportStatus.Approximated);
            int skipped = report.CountOf(ReportStatus.Skipped);
            int warnings = report.CountOf(ReportStatus.Warning);
            int errors = report.CountOf(ReportStatus.Error);
            int total = converted + approximated + skipped + warnings + errors;

            var byCategory = report.Entries
                .GroupBy(e => string.IsNullOrEmpty(e.Category) ? "General" : e.Category)
                .Select(g => (Name: g.Key, Count: g.Count(),
                    Bad: g.Count(e => e.Status == ReportStatus.Warning || e.Status == ReportStatus.Error)))
                .OrderByDescending(g => g.Count).ToList();

            CountParameters(ctx, out int syncedParams, out int localParams);
            GatherFacts(ctx, out int layers, out int states, out int clips,
                out int meshes, out int blendshapes, out int materials);
            float height = AvatarScalerInjector.MeasureHeight(ctx);

            var sb = new StringBuilder(160 * 1024);
            sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append("<title>").Append(H(avatar)).Append(" — AvatarBridge report</title>");
            sb.Append("<style>").Append(Css).Append("</style></head><body>");

            // ------------------------------------------------------------------ header ----
            sb.Append("<header><div class=\"wrap\">");
            sb.Append("<div class=\"crumb\">AvatarBridge conversion report</div>");
            sb.Append("<h1>").Append(H(avatar)).Append("</h1>");
            sb.Append("<div class=\"meta\">v").Append(H(BridgeDefines.Version))
              .Append(" · Unity ").Append(H(Application.unityVersion))
              .Append(" · ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("</div>");
            string verdict = errors > 0
                ? $"Finished with {errors} error{(errors == 1 ? "" : "s")} — start at the red entries below."
                : warnings > 0
                    ? $"Done. {warnings} thing{(warnings == 1 ? "" : "s")} may want a look before upload."
                    : "Done. Ready for the CCK's upload checks.";
            sb.Append("<div class=\"verdict ").Append(errors > 0 ? "bad" : warnings > 0 ? "warn" : "good")
              .Append("\">").Append(H(verdict)).Append("</div>");
            sb.Append("</div></header><main class=\"wrap\">");

            // ------------------------------------------------------------------- tiles ----
            sb.Append("<section class=\"tiles\">");
            Tile(sb, converted.ToString(), "converted", "good");
            Tile(sb, approximated.ToString(), "approximated", "info");
            Tile(sb, skipped.ToString(), "skipped", "mut");
            Tile(sb, warnings.ToString(), warnings == 1 ? "warning" : "warnings", "warn");
            Tile(sb, errors.ToString(), errors == 1 ? "error" : "errors", "bad");
            Tile(sb, layers.ToString(), "animator layers", "plain");
            Tile(sb, (syncedParams + localParams).ToString(),
                $"parameters · {syncedParams} synced", "plain");
            Tile(sb, blendshapes.ToString(), $"blendshapes · {meshes} meshes", "plain");
            Tile(sb, height.ToString("0.00") + " m", "eye height", "plain");
            sb.Append("</section>");

            // ------------------------------------------------------------------ charts ----
            sb.Append("<section class=\"charts\">");
            Donut(sb, "What happened", total, new[]
            {
                ("Converted", converted, "var(--good)"),
                ("Approximated", approximated, "var(--info)"),
                ("Skipped", skipped, "var(--mut)"),
                ("Warnings", warnings, "var(--warn)"),
                ("Errors", errors, "var(--bad)"),
            });
            Donut(sb, "Parameter sync", syncedParams + localParams, new[]
            {
                ("Synced", syncedParams, "var(--info)"),
                ("Local (#)", localParams, "var(--mut)"),
            });
            Bars(sb, "Where the work went", byCategory);
            sb.Append("</section>");

            // ----------------------------------------------------------------- entries ----
            sb.Append("<section class=\"list\"><div class=\"listhead\"><h2>Everything, in detail</h2>");
            sb.Append("<input id=\"q\" type=\"search\" placeholder=\"Filter entries…\" aria-label=\"Filter entries\">");
            sb.Append("<div class=\"chips\">");
            Chip(sb, "all", "All", total, true);
            Chip(sb, "Error", "Errors", errors, false);
            Chip(sb, "Warning", "Warnings", warnings, false);
            Chip(sb, "Approximated", "Approximated", approximated, false);
            Chip(sb, "Skipped", "Skipped", skipped, false);
            Chip(sb, "Converted", "Converted", converted, false);
            sb.Append("</div></div>");

            foreach (var entry in report.Entries
                .OrderBy(e => StatusRank(e.Status))
                .ThenBy(e => e.Category, StringComparer.Ordinal))
            {
                string cls = entry.Status.ToString();
                sb.Append("<article class=\"entry\" data-s=\"").Append(cls).Append("\">");
                sb.Append("<span class=\"stripe ").Append(cls).Append("\"></span><div class=\"body\">");
                sb.Append("<div class=\"head\"><span class=\"cat\">").Append(H(entry.Category)).Append("</span> ");
                sb.Append(H(entry.Subject ?? "")).Append("</div>");
                if (!string.IsNullOrEmpty(entry.Detail))
                {
                    sb.Append("<div class=\"detail\">").Append(H(entry.Detail)).Append("</div>");
                }
                sb.Append("</div></article>");
            }
            sb.Append("</section>");

            // ------------------------------------------------------------------ survey ----
            if (!string.IsNullOrEmpty(report.SurveyCard))
            {
                sb.Append("<section><h2>What this avatar does</h2><div class=\"appx\">");
                AppendMiniMarkdown(sb, report.SurveyCard);
                sb.Append("</div></section>");
            }

            // ------------------------------------------------------------------ weight ----
            if (!string.IsNullOrEmpty(report.WeightCard))
            {
                sb.Append("<section><h2>What this avatar costs</h2><div class=\"appx\">");
                AppendMiniMarkdown(sb, report.WeightCard);
                sb.Append("</div></section>");
            }

            // ---------------------------------------------------------------- appendix ----
            if (!string.IsNullOrEmpty(report.Appendix))
            {
                sb.Append("<section><details class=\"appendix\"><summary>Technical appendix — ")
                  .Append("the converted animator, measured</summary><div class=\"appx\">");
                AppendMiniMarkdown(sb, report.Appendix);
                sb.Append("</div></details></section>");
            }

            sb.Append("<footer>Generated by <a href=\"https://github.com/MrTactical/AvatarBridge\">AvatarBridge</a>. ")
              .Append("The markdown report beside this file is the one to attach to bug reports.</footer>");
            sb.Append("</main><script>").Append(Js).Append("</script></body></html>");

            string path = $"{ctx.OutputDir}/ConversionReport.html";
            System.IO.File.WriteAllText(
                System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", path)),
                sb.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
            report.SavedHtmlPath = path;
        }

        // --------------------------------------------------------------- chart pieces ----

        static void Tile(StringBuilder sb, string value, string label, string tone)
        {
            sb.Append("<div class=\"tile ").Append(tone).Append("\"><b>").Append(H(value))
              .Append("</b><span>").Append(H(label)).Append("</span></div>");
        }

        static void Chip(StringBuilder sb, string key, string label, int count, bool selected)
        {
            sb.Append("<button class=\"chip").Append(selected ? " sel" : "").Append(count == 0 ? " dead" : "")
              .Append("\" data-f=\"").Append(key).Append("\">").Append(label).Append(" ")
              .Append(count).Append("</button>");
        }

        static void Donut(StringBuilder sb, string title, int total, (string Name, int Count, string Colour)[] slices)
        {
            sb.Append("<div class=\"card\"><h2>").Append(H(title)).Append("</h2><div class=\"donutrow\">");
            sb.Append("<svg viewBox=\"0 0 120 120\" role=\"img\" aria-label=\"").Append(H(title)).Append("\">");
            const double r = 46, cx = 60, cy = 60, gapDeg = 2;
            double angle = -90;
            var live = slices.Where(s => s.Count > 0).ToArray();
            foreach (var s in live)
            {
                double sweep = total > 0 ? 360.0 * s.Count / total : 0;
                double a0 = angle + (live.Length > 1 ? gapDeg / 2 : 0);
                double a1 = angle + sweep - (live.Length > 1 ? gapDeg / 2 : 0);
                if (a1 <= a0) { a1 = a0 + 0.5; }
                sb.Append("<path d=\"M ").Append(P(cx, cy, r, a0)).Append(" A ").Append(F(r)).Append(' ')
                  .Append(F(r)).Append(" 0 ").Append(a1 - a0 > 180 ? 1 : 0).Append(" 1 ")
                  .Append(P(cx, cy, r, a1)).Append("\" fill=\"none\" stroke=\"").Append(s.Colour)
                  .Append("\" stroke-width=\"16\"><title>").Append(H($"{s.Name}: {s.Count}"))
                  .Append("</title></path>");
                angle += sweep;
            }
            sb.Append("<text x=\"60\" y=\"57\" class=\"dn\">").Append(total).Append("</text>");
            sb.Append("<text x=\"60\" y=\"72\" class=\"dl\">entries</text></svg>");
            sb.Append("<ul class=\"legend\">");
            foreach (var s in slices)
            {
                sb.Append("<li").Append(s.Count == 0 ? " class=\"dead\"" : "").Append("><i style=\"background:")
                  .Append(s.Colour).Append("\"></i>").Append(H(s.Name)).Append("<b>").Append(s.Count).Append("</b></li>");
            }
            sb.Append("</ul></div></div>");
        }

        static void Bars(StringBuilder sb, string title, List<(string Name, int Count, int Bad)> rows)
        {
            int max = Math.Max(1, rows.Count > 0 ? rows[0].Count : 1);
            sb.Append("<div class=\"card wide\"><h2>").Append(H(title)).Append("</h2><div class=\"bars\">");
            foreach (var row in rows.Take(12))
            {
                double pct = 100.0 * row.Count / max;
                sb.Append("<div class=\"bar\" title=\"").Append(H($"{row.Name}: {row.Count} entr{(row.Count == 1 ? "y" : "ies")}"))
                  .Append("\"><span class=\"bl\">").Append(H(row.Name)).Append("</span>")
                  .Append("<span class=\"track\"><span class=\"fill\" style=\"width:").Append(F(pct))
                  .Append("%\"></span></span><span class=\"bv\">").Append(row.Count);
                if (row.Bad > 0)
                {
                    sb.Append(" <em class=\"flag\">").Append(row.Bad).Append(" ⚠</em>");
                }
                sb.Append("</span></div>");
            }
            if (rows.Count > 12)
            {
                sb.Append("<div class=\"more\">and ").Append(rows.Count - 12).Append(" smaller categories</div>");
            }
            sb.Append("</div></div>");
        }

        static string P(double cx, double cy, double r, double deg)
        {
            double rad = deg * Math.PI / 180.0;
            return F(cx + r * Math.Cos(rad)) + " " + F(cy + r * Math.Sin(rad));
        }

        static string F(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        static int StatusRank(ReportStatus s) => s == ReportStatus.Error ? 0
            : s == ReportStatus.Warning ? 1 : s == ReportStatus.Approximated ? 2
            : s == ReportStatus.Skipped ? 3 : 4;

        // ---------------------------------------------------------------------- facts ----

        static void CountParameters(BridgeContext ctx, out int synced, out int local)
        {
            synced = 0; local = 0;
            var controller = ctx.MergedController;
            if (controller == null) return;
            foreach (var p in controller.parameters)
            {
                if (p.name.StartsWith("#", StringComparison.Ordinal)) local++; else synced++;
            }
        }

        static void GatherFacts(BridgeContext ctx, out int layers, out int states, out int clips,
            out int meshes, out int blendshapes, out int materials)
        {
            layers = 0; states = 0; clips = 0; meshes = 0; blendshapes = 0; materials = 0;
            var controller = ctx.MergedController;
            if (controller != null)
            {
                layers = controller.layers.Length;
                clips = controller.animationClips.Distinct().Count();
                foreach (var layer in controller.layers)
                {
                    states += CountStates(layer.stateMachine);
                }
            }
            if (ctx.Target != null)
            {
                var mats = new HashSet<Material>();
                foreach (var smr in ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    meshes++;
                    blendshapes += smr.sharedMesh != null ? smr.sharedMesh.blendShapeCount : 0;
                    foreach (var m in smr.sharedMaterials) { if (m != null) mats.Add(m); }
                }
                materials = mats.Count;
            }
        }

        static int CountStates(AnimatorStateMachine machine)
        {
            if (machine == null) return 0;
            int n = machine.states.Length;
            foreach (var sub in machine.stateMachines) { n += CountStates(sub.stateMachine); }
            return n;
        }

        // ------------------------------------------------------------- tiny markdown ----

        static void AppendMiniMarkdown(StringBuilder sb, string md)
        {
            bool inTable = false;
            foreach (var raw in md.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw;
                bool tableRow = line.TrimStart().StartsWith("|");
                if (inTable && !tableRow) { sb.Append("</table>"); inTable = false; }
                if (tableRow)
                {
                    var cells = line.Trim().Trim('|').Split('|');
                    if (cells.All(c => c.Trim().Length == 0 || c.Trim().All(ch => ch == '-' || ch == ':')))
                    {
                        continue;   // separator row
                    }
                    if (!inTable) { sb.Append("<table>"); inTable = true; }
                    sb.Append("<tr>");
                    foreach (var cell in cells)
                    {
                        sb.Append("<td>").Append(Inline(cell.Trim())).Append("</td>");
                    }
                    sb.Append("</tr>");
                    continue;
                }
                if (line.StartsWith("### ")) { sb.Append("<h4>").Append(Inline(line.Substring(4))).Append("</h4>"); }
                else if (line.StartsWith("## ")) { sb.Append("<h3>").Append(Inline(line.Substring(3))).Append("</h3>"); }
                else if (line.StartsWith("- ")) { sb.Append("<div class=\"li\">").Append(Inline(line.Substring(2))).Append("</div>"); }
                else if (line.Trim().Length == 0) { sb.Append("<div class=\"gap\"></div>"); }
                else { sb.Append("<p>").Append(Inline(line)).Append("</p>"); }
            }
            if (inTable) { sb.Append("</table>"); }
        }

        static string Inline(string text)
        {
            string s = H(text);
            // `code` then **bold**; on escaped text, so nothing here can open a tag itself.
            var code = new System.Text.RegularExpressions.Regex("`([^`]+)`");
            s = code.Replace(s, "<code>$1</code>");
            var bold = new System.Text.RegularExpressions.Regex(@"\*\*([^*]+)\*\*");
            s = bold.Replace(s, "<b>$1</b>");
            return s;
        }

        static string H(string s) => string.IsNullOrEmpty(s) ? "" :
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        // ----------------------------------------------------------------------- css ----

        const string Css = @"
:root{--bg:#f6f7f9;--card:#ffffff;--ink:#1b2430;--ink2:#5a6472;--line:#e3e7ec;
--good:#2e9c4f;--info:#3d74d6;--warn:#c98a1b;--bad:#d64545;--mut:#97a0af;--accent:#1b5e9e}
@media(prefers-color-scheme:dark){:root{--bg:#14181d;--card:#1d232b;--ink:#e8ecf1;--ink2:#98a2b0;
--line:#2b333d;--good:#3fbe66;--info:#6b9ef0;--warn:#edbe55;--bad:#f06a6a;--mut:#77808d;--accent:#5b9bd5}}
*{box-sizing:border-box;margin:0}body{background:var(--bg);color:var(--ink);
font:15px/1.55 system-ui,'Segoe UI',sans-serif}
.wrap{max-width:1060px;margin:0 auto;padding:0 20px}
header{background:linear-gradient(120deg,#1b5e9e,#c4400c);color:#fff;padding:34px 0 26px}
header .crumb{opacity:.85;font-size:12px;letter-spacing:.08em;text-transform:uppercase}
header h1{font-size:30px;margin:2px 0 4px}header .meta{opacity:.85;font-size:13px}
.verdict{display:inline-block;margin-top:12px;padding:6px 12px;border-radius:8px;
background:rgba(255,255,255,.14);font-weight:600;font-size:14px}
main{padding:26px 20px 60px}
.tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:10px;margin-bottom:22px}
.tile{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:12px 14px}
.tile b{display:block;font-size:24px;line-height:1.2}.tile span{color:var(--ink2);font-size:12.5px}
.tile.good b{color:var(--good)}.tile.info b{color:var(--info)}.tile.warn b{color:var(--warn)}
.tile.bad b{color:var(--bad)}.tile.mut b{color:var(--mut)}
.charts{display:grid;grid-template-columns:1fr 1fr;gap:14px;margin-bottom:26px}
.card{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:16px 18px}
.card.wide{grid-column:1/-1}.card h2{font-size:15px;margin-bottom:12px}
.donutrow{display:flex;align-items:center;gap:18px}.donutrow svg{width:150px;flex:none}
.dn{font-size:22px;font-weight:700;text-anchor:middle;fill:var(--ink)}
.dl{font-size:9px;text-anchor:middle;fill:var(--ink2)}
.legend{list-style:none}.legend li{display:flex;align-items:center;gap:8px;padding:3px 0;font-size:13.5px}
.legend li.dead{opacity:.45}.legend i{width:11px;height:11px;border-radius:3px;flex:none}
.legend b{margin-left:auto;font-variant-numeric:tabular-nums}
.bars .bar{display:flex;align-items:center;gap:10px;padding:3.5px 0}
.bl{width:170px;flex:none;font-size:13px;color:var(--ink2);text-align:right;overflow:hidden;
text-overflow:ellipsis;white-space:nowrap}
.track{flex:1;background:var(--line);border-radius:99px;height:10px;overflow:hidden}
.fill{display:block;height:100%;background:var(--info);border-radius:99px}
.bv{width:70px;font-size:13px;font-variant-numeric:tabular-nums}
.flag{color:var(--warn);font-style:normal;font-size:12px}
.more{color:var(--ink2);font-size:12.5px;padding-top:6px}
.listhead{display:flex;flex-wrap:wrap;align-items:center;gap:10px;margin:4px 0 12px}
.listhead h2{font-size:17px;margin-right:auto} #q{background:var(--card);
border:1px solid var(--line);border-radius:9px;color:var(--ink);
padding:7px 12px;font-size:14px;width:220px}
.chips{display:flex;flex-wrap:wrap;gap:6px}
.chip{background:var(--card);border:1px solid var(--line);border-radius:99px;color:var(--ink2);
padding:4px 12px;font-size:12.5px;cursor:pointer}
.chip.sel{background:var(--accent);border-color:var(--accent);color:#fff}
.chip.dead{opacity:.4;pointer-events:none}
.entry{display:flex;background:var(--card);border:1px solid var(--line);border-radius:10px;
margin-bottom:7px;overflow:hidden}
.stripe{width:4px;flex:none}.stripe.Converted{background:var(--good)}
.stripe.Approximated{background:var(--info)}.stripe.Skipped{background:var(--mut)}
.stripe.Warning{background:var(--warn)}.stripe.Error{background:var(--bad)}
.entry .body{padding:9px 14px}.entry .head{font-weight:600;font-size:14px}
.entry .cat{color:var(--ink2);font-weight:500}
.entry .detail{color:var(--ink2);font-size:13px;margin-top:3px;white-space:pre-wrap}
.appendix{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:14px 18px}
.appendix summary{cursor:pointer;font-weight:600}
.appx{margin-top:12px;font-size:13px}.appx h3{margin:16px 0 6px;font-size:15px}
.appx h4{margin:12px 0 4px;font-size:13.5px}
.appx table{border-collapse:collapse;margin:6px 0;max-width:100%;display:block;overflow-x:auto}
.appx td{border:1px solid var(--line);padding:3px 9px;white-space:nowrap}
.appx code{background:var(--bg);border-radius:4px;padding:1px 5px;
font:12px ui-monospace,Consolas,monospace}
.appx .li{padding-left:14px;position:relative}.appx .li:before{content:'–';position:absolute;left:0}
.appx .gap{height:6px}
footer{margin-top:34px;color:var(--ink2);font-size:12.5px}
footer a{color:var(--accent)}
@media(max-width:760px){.charts{grid-template-columns:1fr}.bl{width:110px}}";

        // ------------------------------------------------------------------------ js ----

        const string Js = @"
var f='all';var q='';
function apply(){document.querySelectorAll('.entry').forEach(function(e){
var okF=f==='all'||e.dataset.s===f;
var okQ=!q||e.textContent.toLowerCase().indexOf(q)>=0;
e.style.display=okF&&okQ?'':'none';});}
document.querySelectorAll('.chip').forEach(function(c){c.addEventListener('click',function(){
document.querySelectorAll('.chip').forEach(function(x){x.classList.remove('sel')});
c.classList.add('sel');f=c.dataset.f;apply();});});
document.getElementById('q').addEventListener('input',function(e){
q=e.target.value.toLowerCase();apply();});";
    }
}
#endif
