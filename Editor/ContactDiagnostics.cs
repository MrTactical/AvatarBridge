#if CVR_CCK_EXISTS
using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    /// <summary>
    /// One-click report on why ChilloutVR's native contact components can or can't be authored.
    ///
    /// Exists because diagnosing this by conversion is slow and destructive — a full run rewrites
    /// a folder of assets to answer one question — and because three separate explanations for the
    /// same failure have now been wrong. Rather than reason from symptoms again, this prints what
    /// Unity actually reports and asks for nothing but a menu click.
    /// </summary>
    public static class ContactDiagnostics
    {
        const string Menu = "Tools/Avatar Bridge/Diagnose native contacts";

        [MenuItem(Menu)]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== AvatarBridge native contact diagnostics ===");
            sb.AppendLine("AvatarBridge " + BridgeDefines.Version + " · Unity " + Application.unityVersion);
            sb.AppendLine();

            // 1. The generated declarations, as files.
            sb.AppendLine("-- generated declarations --");
            foreach (var name in new[] { "NakContactTypes", "ContactBase", "ContactSender", "ContactReceiver", "ContactAnimator" })
            {
                var found = AssetDatabase.FindAssets("t:MonoScript " + name)
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => p.EndsWith("/" + name + ".cs", StringComparison.Ordinal))
                    .ToArray();
                if (found.Length == 0)
                {
                    sb.AppendLine($"  {name,-18} NOT FOUND as an asset");
                    continue;
                }
                foreach (var path in found)
                {
                    var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                    sb.AppendLine($"  {name,-18} {path}");
                    sb.AppendLine($"  {"",-18}   guid={AssetDatabase.AssetPathToGUID(path)}" +
                                  $" class={(ms != null && ms.GetClass() != null ? ms.GetClass().FullName : "<null>")}" +
                                  $" textLen={(ms != null ? ms.text?.Length ?? 0 : 0)}");
                }
            }
            sb.AppendLine();

            // 2. Every assembly claiming to define the types, which is what catches a stale or
            //    duplicate definition.
            sb.AppendLine("-- type resolution --");
            foreach (var typeName in new[] { "NAK.Contacts.ContactBase", "NAK.Contacts.ContactReceiver",
                                             "NAK.Contacts.ContactSender", "NAK.Contacts.ContactAnimator" })
            {
                var hits = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => { try { return (asm: a, t: a.GetType(typeName, false)); } catch { return (asm: a, t: (Type)null); } })
                    .Where(x => x.t != null)
                    .ToArray();
                if (hits.Length == 0)
                {
                    sb.AppendLine($"  {typeName,-34} NOT LOADED");
                    continue;
                }
                foreach (var hit in hits)
                {
                    string loc;
                    try { loc = string.IsNullOrEmpty(hit.asm.Location) ? "(no file)" : hit.asm.Location; }
                    catch { loc = "(unavailable)"; }
                    bool ours = hit.t.GetInterfaces().Any(i => i.FullName == "AvatarBridge.IGeneratedContactStub");
                    sb.AppendLine($"  {typeName,-34} {hit.asm.GetName().Name}  ours={ours}");
                    sb.AppendLine($"  {"",-34}   {loc}");
                }
            }
            sb.AppendLine();

            // 3. The question that actually decides it: can a live component be tied to a script?
            sb.AppendLine("-- live component test --");
            var receiver = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => { try { return a.GetType("NAK.Contacts.ContactReceiver", false); } catch { return null; } })
                .FirstOrDefault(t => t != null);
            if (receiver == null)
            {
                sb.AppendLine("  skipped: ContactReceiver is not loaded");
            }
            else
            {
                var probe = new GameObject("AvatarBridge_ContactDiag") { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    var mb = probe.AddComponent(receiver) as MonoBehaviour;
                    if (mb == null)
                    {
                        sb.AppendLine("  AddComponent returned null or a non-MonoBehaviour");
                    }
                    else
                    {
                        var ms = MonoScript.FromMonoBehaviour(mb);
                        sb.AppendLine($"  AddComponent            ok, instance type {mb.GetType().FullName}");
                        sb.AppendLine($"  MonoScript              {(ms == null ? "<null>" : "returned")}");
                        if (ms != null)
                        {
                            sb.AppendLine($"  MonoScript.text length  {ms.text?.Length ?? 0}");
                            sb.AppendLine($"  MonoScript asset path   \"{AssetDatabase.GetAssetPath(ms)}\"");
                            sb.AppendLine($"  MonoScript.GetClass()   {(ms.GetClass() == null ? "<null>" : ms.GetClass().FullName)}");
                        }
                        sb.AppendLine("  VERDICT                 " +
                                      (ms != null && !string.IsNullOrEmpty(ms.text)
                                          ? "USABLE — native contacts should convert"
                                          : "UNUSABLE — conversion will fall back to pointers/triggers"));
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(probe);
                }
            }

            sb.AppendLine();
            sb.AppendLine("Copy everything above when reporting.");
            Debug.Log(sb.ToString());
        }
    }
}
#endif
