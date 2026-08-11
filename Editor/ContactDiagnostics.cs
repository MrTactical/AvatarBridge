#if CVR_CCK_EXISTS
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace AvatarBridge
{
    // Everything Unity will say about why ChilloutVR's native contact components can or can't be
    // authored, in one menu click and without touching the project.
    //
    // Exists because four separate explanations for this failure have now been wrong, each
    // plausible enough to ship, and because diagnosing it by conversion rewrites a folder of
    // assets to answer a single question. Reasoning from symptoms has a poor record here, so this
    // prints facts and leaves the reasoning until afterwards.
    //
    // The state to explain: the script assets exist and resolve to the right classes with the
    // right source, the types load from Assembly-CSharp with no duplicate or stale definition,
    // and yet a live component gets a MonoScript with no asset path and no source text; which is
    // what makes the CCK report a broken script reference and ChilloutVR report a missing one.
    public static class ContactDiagnostics
    {
        const string Menu = "Tools/Avatar Bridge/Diagnose native contacts";
        const string RuntimeDir = "Assets/AvatarBridge/Runtime";

        static readonly string[] Classes =
        {
            "NAK.Contacts.ContactBase",
            "NAK.Contacts.ContactReceiver",
            "NAK.Contacts.ContactSender",
            "NAK.Contacts.ContactAnimator",
        };

        [MenuItem(Menu)]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== AvatarBridge native contact diagnostics ===");
            sb.AppendLine("AvatarBridge " + BridgeDefines.Version + " · Unity " + Application.unityVersion);
            sb.AppendLine("isCompiling=" + EditorApplication.isCompiling +
                          "  isUpdating=" + EditorApplication.isUpdating);
            sb.AppendLine();

            Section1Assets(sb);
            Section2DuplicateSources(sb);
            Section3AssemblyMembership(sb);
            Section4TypeResolution(sb, out var receiver);
            Section5AllMonoScripts(sb);
            Section6LiveComponent(sb, receiver);

            sb.AppendLine();
            sb.AppendLine("Copy everything above when reporting.");
            Debug.Log(sb.ToString());
        }

        // ---------------------------------------------------------------- 1. the assets ----

        static void Section1Assets(StringBuilder sb)
        {
            sb.AppendLine("-- 1. generated declarations as assets --");
            foreach (var name in new[] { "NakContactTypes", "ContactBase", "ContactSender", "ContactReceiver", "ContactAnimator" })
            {
                string path = RuntimeDir + "/" + name + ".cs";
                bool onDisk = File.Exists(path);
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                string guid = AssetDatabase.AssetPathToGUID(path);
                sb.AppendLine($"  {name,-18} onDisk={onDisk} loaded={(ms != null)} guid={(string.IsNullOrEmpty(guid) ? "<none>" : guid)}");
                if (ms != null)
                {
                    sb.AppendLine($"  {"",-18}   class={(ms.GetClass()?.FullName ?? "<null>")} textLen={ms.text?.Length ?? 0} " +
                                  $"instanceID={ms.GetInstanceID()}");
                }
                // What the .meta actually says, which is not always what the database believes.
                string meta = path + ".meta";
                if (File.Exists(meta))
                {
                    var line = File.ReadAllLines(meta).FirstOrDefault(l => l.StartsWith("guid:", StringComparison.Ordinal));
                    sb.AppendLine($"  {"",-18}   meta {line ?? "<no guid line>"}");
                }
            }
            sb.AppendLine();
        }

        // ------------------------------------------------------ 2. competing definitions ----

        static void Section2DuplicateSources(StringBuilder sb)
        {
            sb.AppendLine("-- 2. any other source declaring these classes --");
            int found = 0;
            foreach (var path in Directory.EnumerateFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
            {
                string text;
                try { text = File.ReadAllText(path); } catch { continue; }
                if (!text.Contains("NAK.Contacts")) continue;
                string rel = "Assets" + path.Substring(Application.dataPath.Length).Replace('\\', '/');
                if (rel.StartsWith(RuntimeDir, StringComparison.Ordinal)) continue;   // the expected ones
                bool declares = text.Contains("class ContactReceiver") || text.Contains("class ContactBase")
                                || text.Contains("class ContactSender") || text.Contains("class ContactAnimator");
                sb.AppendLine($"  {(declares ? "DECLARES" : "mentions")} {rel}");
                found++;
            }
            if (found == 0) sb.AppendLine("  none — only the generated files reference NAK.Contacts");
            sb.AppendLine();
        }

        // ------------------------------------------------- 3. which assembly owns the files ----

        static void Section3AssemblyMembership(StringBuilder sb)
        {
            sb.AppendLine("-- 3. assembly Unity compiles the declarations into --");
            foreach (var asmType in new[] { AssembliesType.Editor, AssembliesType.Player })
            {
                UnityEditor.Compilation.Assembly[] asms;
                try { asms = CompilationPipeline.GetAssemblies(asmType); }
                catch (Exception e) { sb.AppendLine($"  {asmType}: unavailable ({e.GetType().Name})"); continue; }

                var owning = asms.Where(a => a.sourceFiles.Any(f => f.Replace('\\', '/').StartsWith(RuntimeDir, StringComparison.Ordinal))).ToArray();
                if (owning.Length == 0)
                {
                    sb.AppendLine($"  {asmType,-6} NO assembly claims {RuntimeDir}/*.cs");
                    continue;
                }
                foreach (var a in owning)
                {
                    int count = a.sourceFiles.Count(f => f.Replace('\\', '/').StartsWith(RuntimeDir, StringComparison.Ordinal));
                    sb.AppendLine($"  {asmType,-6} {a.name}  ({count} of our files)  out={a.outputPath}");
                }
            }
            sb.AppendLine();
        }

        // ------------------------------------------------------------ 4. type resolution ----

        static void Section4TypeResolution(StringBuilder sb, out Type receiver)
        {
            sb.AppendLine("-- 4. loaded types --");
            receiver = null;
            foreach (var typeName in Classes)
            {
                var hits = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => { try { return (asm: a, t: a.GetType(typeName, false)); } catch { return (asm: a, t: (Type)null); } })
                    .Where(x => x.t != null).ToArray();
                if (hits.Length == 0) { sb.AppendLine($"  {typeName,-32} NOT LOADED"); continue; }
                foreach (var hit in hits)
                {
                    string loc; try { loc = string.IsNullOrEmpty(hit.asm.Location) ? "(no file)" : hit.asm.Location; } catch { loc = "(unavailable)"; }
                    bool ours = hit.t.GetInterfaces().Any(i => i.FullName == "AvatarBridge.IGeneratedContactStub");
                    sb.AppendLine($"  {typeName,-32} {hit.asm.GetName().Name} ours={ours} mvid={SafeMvid(hit.asm)}");
                    sb.AppendLine($"  {"",-32}   {loc}");
                }
                if (typeName.EndsWith("ContactReceiver", StringComparison.Ordinal)) receiver = hits[0].t;
            }
            sb.AppendLine();
        }

        static string SafeMvid(System.Reflection.Assembly a)
        {
            try { return a.ManifestModule.ModuleVersionId.ToString("N").Substring(0, 8); } catch { return "?"; }
        }

        // ------------------------------------------ 5. every MonoScript for these classes ----

        static void Section5AllMonoScripts(StringBuilder sb)
        {
            sb.AppendLine("-- 5. every MonoScript Unity holds for these classes --");
            sb.AppendLine("   (more than one per class, or one with no asset path, is the answer)");
            MonoScript[] all;
            try { all = MonoImporter.GetAllRuntimeMonoScripts(); }
            catch (Exception e) { sb.AppendLine("  unavailable: " + e.Message); sb.AppendLine(); return; }

            sb.AppendLine($"  total runtime MonoScripts: {all.Length}");
            int matched = 0;
            foreach (var ms in all)
            {
                Type c; try { c = ms.GetClass(); } catch { continue; }
                if (c == null || !Classes.Contains(c.FullName)) continue;
                matched++;
                string path = AssetDatabase.GetAssetPath(ms);
                sb.AppendLine($"  {c.FullName,-32} textLen={ms.text?.Length ?? 0,-5} id={ms.GetInstanceID(),-12} " +
                              $"asm={c.Assembly.GetName().Name}");
                sb.AppendLine($"  {"",-32}   path=\"{(string.IsNullOrEmpty(path) ? "<none>" : path)}\"");
            }
            if (matched == 0) sb.AppendLine("  NONE — Unity holds no runtime MonoScript for any of these classes");
            sb.AppendLine();
        }

        // ------------------------------------------------------- 6. the live component ----

        static void Section6LiveComponent(StringBuilder sb, Type receiver)
        {
            sb.AppendLine("-- 6. live component test --");
            if (receiver == null) { sb.AppendLine("  skipped: ContactReceiver not loaded"); return; }

            bool anyUsable = false;
            foreach (var hidden in new[] { true, false })
            {
                var probe = new GameObject("AvatarBridge_ContactDiag");
                if (hidden) probe.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    var mb = probe.AddComponent(receiver) as MonoBehaviour;
                    string label = hidden ? "hidden" : "normal";
                    if (mb == null) { sb.AppendLine($"  [{label}] AddComponent gave null / non-MonoBehaviour"); continue; }
                    var ms = MonoScript.FromMonoBehaviour(mb);
                    int len = ms != null ? ms.text?.Length ?? 0 : 0;
                    sb.AppendLine($"  [{label},-7] instance={mb.GetType().FullName} " +
                                  $"MonoScript={(ms == null ? "<null>" : "id " + ms.GetInstanceID())} textLen={len}");
                    sb.AppendLine($"  {"",-10}  path=\"{(ms != null ? AssetDatabase.GetAssetPath(ms) : "")}\" " +
                                  $"class={(ms?.GetClass()?.FullName ?? "<null>")}");
                    anyUsable |= len > 0;
                }
                finally { UnityEngine.Object.DestroyImmediate(probe); }
            }

            // Is the Type the script asset reports the same object reflection returned? If they
            // differ, two definitions are live and only one has a script behind it.
            var assetScript = AssetDatabase.LoadAssetAtPath<MonoScript>(RuntimeDir + "/ContactReceiver.cs");
            var assetType = assetScript != null ? assetScript.GetClass() : null;
            sb.AppendLine("  asset type vs reflected type: " +
                          (assetType == null ? "asset class is null"
                           : ReferenceEquals(assetType, receiver) ? "SAME Type object"
                           : $"DIFFERENT — asset={assetType.AssemblyQualifiedName} reflected={receiver.AssemblyQualifiedName}"));
            if (assetScript != null)
            {
                sb.AppendLine($"  asset MonoScript id={assetScript.GetInstanceID()} textLen={assetScript.text?.Length ?? 0}");
            }

            sb.AppendLine("  VERDICT  " + (anyUsable
                ? "USABLE — native contacts should convert"
                : "UNUSABLE — conversion falls back to pointers/triggers"));
        }
    }
}
#endif
