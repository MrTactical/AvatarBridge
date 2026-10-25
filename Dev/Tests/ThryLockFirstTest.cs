#if CVR_CCK_EXISTS
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // An unlocked Poiyomi is locked through Poiyomi first, on a stand-in, and YAPS patches the
    // locked shader. Needs Poiyomi in the project; it locks a real material, so it compiles.
    // Batch: -executeMethod AvatarBridge.Regression.ThryLockFirstTest.Run
    public static class ThryLockFirstTest
    {
        const string Dir = "Assets/ThryLockFirstTest";
        static int failures;

        [MenuItem("Tools/AvatarBridge Dev/Test: Poiyomi locked before the YAPS patch")]
        public static void Run()
        {
            failures = 0;
            var poiyomi = Shader.Find(".poiyomi/Poiyomi Toon");
            if (poiyomi == null || YapsLock.ThryLocked(poiyomi) != false)
            {
                Debug.Log("[ThryLockFirstTest] SKIP: no unlocked Poiyomi Toon in the project");
                if (Application.isBatchMode) EditorApplication.Exit(0);
                return;
            }
            var generated = GeneratedFiles.Snapshot();
            try
            {
                Directory.CreateDirectory(Dir);
                AssetDatabase.Refresh();
                var material = new Material(poiyomi) { name = "LockFirst" };
                material.SetColor("_Color", new Color(0.2f, 0.4f, 0.6f, 1f));
                AssetDatabase.CreateAsset(material, Dir + "/LockFirst.mat");

                // A clip that animates _Color, so the lock must keep it live.
                var clip = new AnimationClip();
                clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "material._Color.r", AnimationCurve.Constant(0, 1, 1));
                YapsLock.Animate(null, new[] { clip });

                var report = new BridgeReport();
                var patched = YapsShaderPatcher.Patch(material, report, out string refusal, out _);
                if (refusal != null) Debug.Log($"[ThryLockFirstTest]   refused: {refusal}");
                var basis = YapsLock.Basis(material);
                string basisPath = AssetDatabase.GetAssetPath(basis);

                Check(patched != null, "the unlocked material patches");
                Check(basis != material && basisPath.StartsWith(YapsLock.Folder + "/"), "it has a stand-in in the stand-in folder");
                Check(YapsLock.ThryLocked(basis.shader) == true, "the stand-in is locked by Poiyomi");
                Check(YapsLock.ThryLocked(material.shader) == false, "the user's material is left unlocked");
                Check(basis.shader != null && basis.shader.name.StartsWith("Hidden/Locked/"), "the stand-in wears a locked shader");
                string lockedText = basis.shader != null ? File.ReadAllText(AssetDatabase.GetAssetPath(basis.shader)) : "";
                Check(Regex.IsMatch(lockedText, @"float4\s+_Color\s*;"), "the animated _Color stays a uniform after the lock");
                Check(patched != null && YapsShaderPatcher.OriginalShaderOf(new Material(patched)) == basis.shader,
                    "the patch records the locked shader as its source");
                Check(patched != null && !ShaderUtil.ShaderHasError(patched), "the patch of the locked shader has no errors");
                Check(report.Entries.Any(e => e.Subject == material.name && e.Detail.Contains("locked through Poiyomi first")),
                    "the report says it was locked first, under the user's material name");
                Check(!report.Entries.Any(e => e.Status == ReportStatus.Warning), "no warning for a material that locked");

                // A second patch of the same material reuses the stand-in.
                int before = Directory.GetFiles(YapsLock.Folder, "*.mat").Length;
                var again = YapsShaderPatcher.Patch(material, null, out _, out _);
                Check(again == patched, "a second patch gives the same shader");
                Check(Directory.GetFiles(YapsLock.Folder, "*.mat").Length == before, "and locks nothing new");

                // An unsaved material cannot be locked by Poiyomi, and says so.
                var loose = new Material(poiyomi) { name = "Loose" };
                var looseReport = new BridgeReport();
                YapsShaderPatcher.Patch(loose, looseReport, out _, out _);
                Check(looseReport.Entries.Any(e => e.Status == ReportStatus.Warning && e.Subject.Contains("\"Loose\"")),
                    "an unsaved material patched unlocked gets the warning");
                Object.DestroyImmediate(loose);
            }
            finally
            {
                AssetDatabase.DeleteAsset(Dir);
                GeneratedFiles.DeleteNew(generated);
            }
            Debug.Log(failures == 0 ? "[ThryLockFirstTest] PASS" : $"[ThryLockFirstTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[ThryLockFirstTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
