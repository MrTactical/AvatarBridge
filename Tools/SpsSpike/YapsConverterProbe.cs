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
            public bool HasChannelSpace;
            public float ChannelSpace = -1f;
            public int SpareSyncFloats;
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
                if (material.HasProperty("_YAPS_ChannelSpace"))
                {
                    outcome.HasChannelSpace = true;
                    outcome.ChannelSpace = material.GetFloat("_YAPS_ChannelSpace");
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
                // "#YAPS0E" does not start with "YAPS", so these two never
                // overlap and must not be subtracted from one another.
                outcome.SyncedParams = controller.parameters.Count(p => p.name.StartsWith("YAPS"));
                outcome.LocalParams = controller.parameters.Count(p => p.name.StartsWith("#YAPS"));

                int used = controller.parameters
                    .Where(p => !p.name.StartsWith("#")
                                && p.type != UnityEngine.AnimatorControllerParameterType.Trigger)
                    .Sum(p => p.type == UnityEngine.AnimatorControllerParameterType.Bool ? 1 : 32);
                outcome.SpareSyncFloats = Mathf.Max(0, (3200 - used) / 32);
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
            //
            // The shape depends on what sync budget the avatar had left,
            // which is the point of the degradation: a full channel is four
            // values and four triggers, an engagement-only one is a single
            // value and a single trigger. Expecting the full shape on a
            // full avatar just reports the design as a fault.
            int plugs = Mathf.Max(1, on.PatchedMaterials);
            bool full = on.ChannelSpace > 0.5f;
            int values = full ? 4 : 1;
            int triggers = full ? 4 : 1;

            Line("### The channel");
            Line("");
            Line(full
                ? "The avatar had room for the **full channel** — engagement and the socket's offset."
                : "The avatar had no room for the offset, so this is the **engagement-only** channel. " +
                  "Position comes from the socket's own marker lights at close range and from " +
                  "ChilloutVR's player positions beyond that.");
            Line("");
            Line("| | YAPS off | YAPS on | expected | reads as |");
            Line("|---|---:|---:|---:|---|");
            Channel("Triggers on the plug", off.ChannelTriggers, on.ChannelTriggers, plugs * triggers);
            Channel("Material driver tasks", off.MaterialTasks, on.MaterialTasks, plugs * values);
            Channel("Animator driver slots", off.DriverSlots, on.DriverSlots, plugs * values);
            Channel("Local `#YAPS…` parameters", off.LocalParams, on.LocalParams, plugs * values);
            Channel("Synced `YAPS…` parameters", off.SyncedParams, on.SyncedParams, plugs * values);
            Line($"| `_YAPS_ChannelSpace` on the material | — | " +
                 $"{(on.HasChannelSpace ? on.ChannelSpace.ToString("0") : "**absent**")} | " +
                 $"{(full ? "1" : "0")} | {(on.HasChannelSpace ? "correct" : "**WRONG — the shader never declared it**")} |");
            Line("");
            Line($"Synced parameters cost 32 bits each against ChilloutVR's 3200-bit cap — " +
                 $"{on.SyncedParams * 32} bits here. The avatar has **{on.SpareSyncFloats} float(s)** " +
                 $"of headroom left; the full channel needs four per plug.");
            if (!full)
            {
                Line("");
                Line("**The full-channel shape is therefore not exercised by this avatar.** It needs " +
                     "one with sync budget to spare — the test props are the obvious candidate.");
            }
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
