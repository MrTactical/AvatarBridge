// Phase 0a remainder, risk R12: if the plug's own SPS flag is turned off
// before the VRCFury bake, do the objects, contacts and baked mesh data
// still appear while the body shader is left alone? That is the whole
// premise of patching our own deform into a clean shader instead of
// retargeting theirs.
//
// Runs both ways on the same avatar and diffs the two bakes. Never saves
// the scene; every mutation happens on a throwaway copy.
//
//   Unity.exe -batchmode -projectPath "<test project>" \
//     -executeMethod AvatarBridge.Spike.SpsBakeProbe.RunBatch -quit
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Spike
{
    public static class SpsBakeProbe
    {
        const string SceneRelative = "Assets/Kemonoroo/AngelaFlux/Angela_PC_SPS.unity";
        static readonly StringBuilder Log = new StringBuilder();

        [MenuItem("AvatarBridge/Spike/Run SPS bake probe")]
        public static void RunBatch()
        {
            Log.Clear();
            Line("# Phase 0a — SPS bake probe");
            Line("");
            Line($"Scene: `{SceneRelative}`  ");
            Line($"Run: {DateTime.Now:yyyy-MM-dd HH:mm}");
            Line("");

            try
            {
                Probe(spsEnabled: true, heading: "Control — plugs left as authored (enableSps = true)");
                Probe(spsEnabled: false, heading: "Test — enableSps = false before the bake");
            }
            catch (Exception e)
            {
                Line("");
                Line($"**Probe threw:** `{e.GetType().Name}: {e.Message}`");
                Line("```");
                Line(e.StackTrace ?? "");
                Line("```");
            }

            string outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                "SpsBakeProbe.md"));
            File.WriteAllText(outPath, Log.ToString());
            Debug.Log($"[SpsSpike] Bake probe written to {outPath}");
        }

        static void Probe(bool spsEnabled, string heading)
        {
            Line($"## {heading}");
            Line("");

            var scene = EditorSceneManager.OpenScene(SceneRelative, OpenSceneMode.Single);
            var descriptor = scene.GetRootGameObjects()
                .Select(FindDescriptor)
                .FirstOrDefault(d => d != null);
            if (descriptor == null)
            {
                Line("No VRC avatar descriptor found in the scene. Aborting this pass.");
                Line("");
                return;
            }

            // Work on a copy so the scene asset is never dirtied in a way
            // that could be saved by accident.
            var working = UnityEngine.Object.Instantiate(descriptor.gameObject);
            working.name = descriptor.gameObject.name + " (probe)";
            working.SetActive(true);

            var plugs = ComponentsNamed(working, "VRCFuryHapticPlug");
            var sockets = ComponentsNamed(working, "VRCFuryHapticSocket");
            Line($"Found **{plugs.Count} plug(s)**, **{sockets.Count} socket(s)** before baking.");
            Line("");

            if (plugs.Count > 0)
            {
                Line("Plug configuration as authored:");
                Line("");
                Line("| Plug | enableSps | configureTps | spsOverrun | addDpsTipLight | spsAutorig |");
                Line("|---|---|---|---|---|---|");
                foreach (var plug in plugs)
                {
                    Line($"| `{PathOf(plug.transform, working.transform)}` "
                         + $"| {Field(plug, "enableSps")} | {Field(plug, "configureTps")} "
                         + $"| {Field(plug, "spsOverrun")} | {Field(plug, "addDpsTipLight")} "
                         + $"| {Field(plug, "spsAutorig")} |");
                }
                Line("");
            }
            if (sockets.Count > 0)
            {
                var lightModes = sockets
                    .Select(s => $"{Field(s, "addLight")}")
                    .GroupBy(v => v)
                    .Select(g => $"{g.Key} ×{g.Count()}");
                Line($"Socket `addLight` values: {string.Join(", ", lightModes)}. "
                     + $"`useLights`: {string.Join(", ", sockets.Select(s => Field(s, "useLights")).Distinct())}");
                Line("");
            }

            if (!spsEnabled)
            {
                int flipped = 0;
                foreach (var plug in plugs)
                {
                    if (SetField(plug, "enableSps", false)) flipped++;
                }
                Line($"Set `enableSps = false` on **{flipped}/{plugs.Count}** plug(s).");
                Line("");
            }

            var report = new BridgeReport();
            GameObject baked = null;
            try
            {
                baked = VRCFuryBaker.TryBake(FindDescriptor(working), report);
            }
            catch (Exception e)
            {
                Line($"**Bake threw:** `{e.GetType().Name}: {e.Message}`");
            }

            if (baked == null)
            {
                Line("Bake returned nothing — inspecting the working copy in place instead.");
                baked = working;
            }
            Line("");
            Inspect(baked);

            // Never save. Leave the scene as it was on disk.
            EditorSceneManager.OpenScene(SceneRelative, OpenSceneMode.Single);
            Line("");
        }

        static void Inspect(GameObject root)
        {
            Line("### What the bake produced");
            Line("");

            var named = new (string label, string needle)[]
            {
                ("BakedSpsPlug objects", "BakedSpsPlug"),
                ("BakedSpsSocket objects", "BakedSpsSocket"),
                ("SpsResolver renderers", "SpsResolver"),
                ("SpsScreenMarker renderers", "SpsScreenMarker"),
            };
            foreach (var (label, needle) in named)
            {
                int count = root.GetComponentsInChildren<Transform>(true)
                    .Count(t => t.name.Contains(needle));
                Line($"- {label}: **{count}**");
            }

            var lights = root.GetComponentsInChildren<Light>(true);
            Line($"- Lights: **{lights.Length}**"
                 + (lights.Length > 0
                     ? " — ranges " + string.Join(", ",
                         lights.Select(l => l.range.ToString("0.0000")).Distinct().OrderBy(v => v))
                     : ""));

            int senders = ComponentsNamed(root, "VRCContactSender").Count;
            int receivers = ComponentsNamed(root, "VRCContactReceiver").Count;
            Line($"- Contact senders: **{senders}**, receivers: **{receivers}**");

            // The two artifacts that decide R12.
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var materials = renderers.SelectMany(r => r.sharedMaterials)
                .Where(m => m != null).Distinct().ToList();

            var patched = materials
                .Where(m => m.shader != null && m.shader.name.Contains("SPSPatched"))
                .ToList();
            Line($"- Materials on an **SPS-patched shader**: **{patched.Count}** / {materials.Count}");
            foreach (var m in patched.Take(5))
            {
                Line($"    - `{m.name}` → `{m.shader.name}`");
            }

            var withBake = materials
                .Where(m => m.HasProperty("_SPS_Bake") && m.GetTexture("_SPS_Bake") != null)
                .ToList();
            Line($"- Materials carrying a **`_SPS_Bake` texture**: **{withBake.Count}**");
            foreach (var m in withBake.Take(5))
            {
                var tex = m.GetTexture("_SPS_Bake");
                Line($"    - `{m.name}` → `{tex.name}` ({tex.width}×{tex.height})");
            }

            Line("");
            Line("### Verdict for R12");
            Line("");
            bool objectsSurvive = root.GetComponentsInChildren<Transform>(true)
                .Any(t => t.name.Contains("BakedSps"));
            bool shaderClean = patched.Count == 0;
            bool bakeSurvives = withBake.Count > 0;
            Line($"- Baked objects present: **{(objectsSurvive ? "yes" : "no")}**");
            Line($"- Body shaders left unpatched: **{(shaderClean ? "yes" : "no")}**");
            Line($"- Mesh bake texture present: **{(bakeSurvives ? "yes" : "no")}**");
        }

        // --- helpers ---------------------------------------------------

        static VRC.SDK3.Avatars.Components.VRCAvatarDescriptor FindDescriptor(GameObject go)
            => go == null ? null : go.GetComponentInChildren<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true);

        static List<Component> ComponentsNamed(GameObject root, string typeName)
            => root.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && c.GetType().Name == typeName)
                .ToList();

        static string Field(Component c, string name)
        {
            var f = c.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f == null ? "—" : Convert.ToString(f.GetValue(c));
        }

        static bool SetField(Component c, string name, object value)
        {
            var f = c.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null)
            {
                return false;
            }
            f.SetValue(c, value);
            EditorUtility.SetDirty(c);
            return true;
        }

        static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            while (t != null && t != root)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }
            return string.Join("/", parts);
        }

        static void Line(string s) => Log.AppendLine(s);
    }
}
#endif
