// Phase 2 verification. Runs a full conversion with YAPS switched on and
// then asks the result the questions that matter.
//
// The pass touches four things that can each fail quietly: what VRCFury's
// bake was allowed to produce, what the stripper was told to spare, what
// the plug's material became, and what the sockets now look like. A
// conversion that "succeeded" while the plug kept its old shader, or while
// the stripper deleted the socket objects a moment before this pass ran,
// looks identical in the report. So the checks here read the finished
// avatar, not the log.
//
// Runs the same avatar twice — once with YAPS off — because the useful
// question is not "did anything happen" but "what changed, and only that".
//
//   Unity.exe -batchmode -projectPath "<test project>" \
//     -executeMethod AvatarBridge.Spike.YapsConverterProbe.RunBatch -quit
#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Spike
{
    public static class YapsConverterProbe
    {
        const string SceneRelative = "Assets/Kemonoroo/AngelaFlux/Angela_PC_SPS.unity";
        static readonly StringBuilder Log = new StringBuilder();

        [MenuItem("AvatarBridge/Spike/Probe the YAPS converter pass (2)")]
        public static void RunBatch()
        {
            Log.Clear();
            Line("# Phase 2 — converter pass probe");
            Line("");
            Line($"Scene: `{SceneRelative}`  ");
            Line($"Run: {DateTime.Now:yyyy-MM-dd HH:mm}");
            Line("");

            try
            {
                var off = Convert(yaps: false);
                var on = Convert(yaps: true);
                Compare(off, on);
            }
            catch (Exception e)
            {
                Line("");
                Line($"**Probe threw:** `{e.GetType().Name}: {e.Message}`");
                Line("```");
                Line(e.StackTrace ?? "");
                Line("```");
            }

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                "YapsConverterProbe.md"));
            File.WriteAllText(path, Log.ToString());
            Debug.Log($"[YAPS] Converter probe written to {path}");
        }

        class Outcome
        {
            public bool Yaps;
            public int Plugs, Sockets, Lights, Pointers, ScreenMarkers, Resolvers;
            public int PatchedMaterials, BakeTextures;
            public List<string> LightRanges = new List<string>();
            public List<string> Errors = new List<string>();
            public List<string> YapsEntries = new List<string>();
            public bool SpsStillPatched;
            public int ChannelTriggers, MaterialTasks, DriverSlots, SyncedParams, LocalParams;
            public bool ChannelSpaceSet;
        }

        static Outcome Convert(bool yaps)
        {
            var scene = EditorSceneManager.OpenScene(SceneRelative, OpenSceneMode.Single);
            var descriptor = scene.GetRootGameObjects()
                .Select(go => go.GetComponentInChildren<
                    VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true))
                .FirstOrDefault(d => d != null);
            if (descriptor == null)
            {
                throw new Exception("no VRC avatar descriptor in the scene");
            }

            var settings = new BridgeSettings
            {
                stripSpsSystems = true,
                convertYapsSystems = yaps,
            };
            var report = BridgeConverter.Convert(descriptor, settings);

            var outcome = new Outcome { Yaps = yaps };
            foreach (var entry in report.Entries)
            {
                if (entry.Status == ReportStatus.Error)
                {
                    outcome.Errors.Add($"{entry.Category}: {entry.Subject}");
                }
                if (entry.Category == "YAPS")
                {
                    outcome.YapsEntries.Add($"{Severity(entry.Status)} {entry.Subject}");
                }
            }

            var root = report.ConvertedRoot;
            if (root == null)
            {
                outcome.Errors.Add("the conversion produced no avatar at all");
                return outcome;
            }

            outcome.Plugs = Count(root, "BakedSpsPlug");
            outcome.Sockets = Count(root, "BakedSpsSocket");
            outcome.ScreenMarkers = Count(root, "SpsScreenMarker");
            outcome.Resolvers = Count(root, "SpsResolver");
            outcome.Pointers = root.GetComponentsInChildren<ABI.CCK.Components.CVRPointer>(true).Length;

            var lights = root.GetComponentsInChildren<Light>(true);
            outcome.Lights = lights.Length;
            outcome.LightRanges = lights.Select(l => l.range.ToString("0.0000"))
                .GroupBy(v => v).OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key} ×{g.Count()}").ToList();

            foreach (var material in root.GetComponentsInChildren<Renderer>(true)
                         .SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct())
            {
                if (material.HasProperty("_YAPS_Bake") && material.GetTexture("_YAPS_Bake") != null)
                {
                    outcome.BakeTextures++;
                }
                if (material.shader != null && material.shader.name.Contains("YAPS"))
                {
                    outcome.PatchedMaterials++;
                }
                if (material.shader != null && material.shader.name.Contains("SPSPatched"))
                {
                    outcome.SpsStillPatched = true;
                }
                if (material.HasProperty("_YAPS_ChannelSpace") && material.GetFloat("_YAPS_ChannelSpace") > 0.5f)
                {
                    outcome.ChannelSpaceSet = true;
                }
            }

            // The channel is the half that can do nothing while every other
            // number still reads right — it did exactly that once, because
            // the pass was registered before the plugs existed.
            outcome.ChannelTriggers = root
                .GetComponentsInChildren<ABI.CCK.Components.CVRAdvancedAvatarSettingsTrigger>(true)
                .Count(t => t.name.StartsWith("YAPS Channel"));
            var materialDriver = root.GetComponentInChildren<ABI.CCK.Components.CVRMaterialDriver>(true);
            outcome.MaterialTasks = materialDriver != null ? materialDriver.tasks.Count : 0;
            var animatorDriver = root.GetComponentInChildren<ABI.CCK.Components.CVRAnimatorDriver>(true);
            outcome.DriverSlots = animatorDriver != null ? animatorDriver.animators.Count : 0;

            // The merged controller reaches the avatar through CVRAvatar's
            // override slot, not the Animator's — reading the Animator gave
            // a flat zero on a run where the sync diagnostic was plainly
            // counting the same parameters.
            var avatar = root.GetComponent<ABI.CCK.Components.CVRAvatar>();
            var controller = (avatar != null && avatar.overrides != null
                ? avatar.overrides.runtimeAnimatorController
                : null) as UnityEditor.Animations.AnimatorController;
            if (controller == null)
            {
                var animator = root.GetComponent<Animator>();
                controller = animator != null
                    ? animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController
                    : null;
            }
            if (controller != null)
            {
                outcome.SyncedParams = controller.parameters.Count(p => p.name.StartsWith("YAPS"));
                outcome.LocalParams = controller.parameters.Count(p => p.name.StartsWith("#YAPS"));
            }
            return outcome;
        }

        static void Compare(Outcome off, Outcome on)
        {
            Line("## What the flag changed");
            Line("");
            Line("| | YAPS off | YAPS on | reads as |");
            Line("|---|---:|---:|---|");
            Row("Plug objects kept", off.Plugs, on.Plugs, on.Plugs > 0 && off.Plugs == 0);
            Row("Socket objects kept", off.Sockets, on.Sockets, on.Sockets > 0 && off.Sockets == 0);
            Row("Marker lights", off.Lights, on.Lights, on.Lights > 0);
            Row("CVR pointers", off.Pointers, on.Pointers, on.Pointers >= off.Pointers);
            Row("Materials on a YAPS shader", off.PatchedMaterials, on.PatchedMaterials,
                on.PatchedMaterials > 0 && off.PatchedMaterials == 0);
            Row("Materials carrying a bake", off.BakeTextures, on.BakeTextures,
                on.BakeTextures > 0 && off.BakeTextures == 0);
            Row("Screen-atlas markers left", off.ScreenMarkers, on.ScreenMarkers,
                on.ScreenMarkers == 0);
            Row("Resolver renderers left", off.Resolvers, on.Resolvers, on.Resolvers == 0);
            Line("");

            // Every one of these was "correct" on a run where the channel
            // pass returned immediately without doing anything.
            int plugs = Mathf.Max(1, on.PatchedMaterials);
            Line("### The channel");
            Line("");
            Line("| | YAPS off | YAPS on | expected | reads as |");
            Line("|---|---:|---:|---:|---|");
            Channel("Axis triggers on the plug", off.ChannelTriggers, on.ChannelTriggers, plugs * 3);
            Channel("Material driver tasks", off.MaterialTasks, on.MaterialTasks, plugs * 4);
            Channel("Animator driver slots", off.DriverSlots, on.DriverSlots, plugs * 4);
            Channel("Local `#YAPS…` parameters", off.LocalParams, on.LocalParams, plugs * 4);
            Channel("Synced `YAPS…` parameters", off.SyncedParams,
                on.SyncedParams - on.LocalParams, plugs * 4);
            Line($"| Material told to read plug-local | — | {(on.ChannelSpaceSet ? "yes" : "no")} | yes | " +
                 $"{(on.ChannelSpaceSet ? "correct" : "**WRONG**")} |");
            Line("");
            Line($"Synced parameters cost 32 bits each against ChilloutVR's 3200-bit cap — " +
                 $"{(on.SyncedParams - on.LocalParams) * 32} bits here, " +
                 $"{(on.SyncedParams - on.LocalParams) * 32 * 100f / 3200f:0.#}% of the budget.");
            Line("");

            Line("| Check | Result |");
            Line("|---|---|");
            Line($"| VRChat's SPS shader kept off the avatar | " +
                 $"{(on.SpsStillPatched ? "**WRONG — a material is still on an SPS-patched shader**" : "correct")} |");
            Line($"| Conversion errors, YAPS off | {(off.Errors.Count == 0 ? "none" : "**" + off.Errors.Count + "**")} |");
            Line($"| Conversion errors, YAPS on | {(on.Errors.Count == 0 ? "none" : "**" + on.Errors.Count + "**")} |");
            Line("");

            foreach (var errors in new[] { off.Errors, on.Errors })
            {
                foreach (var error in errors.Take(6))
                {
                    Line($"- {error}");
                }
            }

            Line("### Marker light ranges after conversion");
            Line("");
            Line(on.LightRanges.Count == 0
                ? "None."
                : string.Join(", ", on.LightRanges.Select(r => $"`{r}`")));
            Line("");
            Line("Roots should read `0.4706` and fronts `0.4006`. Any `0.41`/`0.42`/`0.45` left " +
                 "means a socket was missed, and its front would evict its own root.");
            Line("");

            Line("### What the pass reported");
            Line("");
            if (on.YapsEntries.Count == 0)
            {
                Line("Nothing — the pass either did not run or found nothing to do.");
            }
            foreach (var entry in on.YapsEntries)
            {
                Line($"- {entry}");
            }
            Line("");
        }

        static void Row(string label, int off, int on, bool ok)
        {
            Line($"| {label} | {off} | {on} | {(ok ? "correct" : "**WRONG**")} |");
        }

        static void Channel(string label, int off, int on, int expected)
        {
            Line($"| {label} | {off} | {on} | {expected} | " +
                 $"{(on == expected && off == 0 ? "correct" : "**WRONG**")} |");
        }

        static int Count(GameObject root, string needle)
            => root.GetComponentsInChildren<Transform>(true).Count(t => t.name.Contains(needle));

        static string Severity(ReportStatus status)
            => status == ReportStatus.Error ? "**ERROR**"
                : status == ReportStatus.Warning ? "*warning*" : "";

        static void Line(string s) => Log.AppendLine(s);
    }
}
#endif
