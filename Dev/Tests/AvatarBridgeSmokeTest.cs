// Presses every button in the YAPS window, the component inspectors and
// the ChilloutVR Toolkit by calling what each button calls, on objects
// built for the purpose, and asserts what each says it does. One menu
// item, one report. Batch: -executeMethod AvatarBridge.Regression.SmokeTest.RunBatch
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge.Regression
{
    public static class SmokeTest
    {
        static readonly List<(string name, bool ok, string note)> Results = new List<(string, bool, string)>();
        const string ScratchDir = "Assets/AvatarBridgeOutput/SmokeTest";

        [MenuItem("Tools/Avatar Bridge/DevTools/Smoke test every button")]
        public static void Run()
        {
            Results.Clear();
            var created = new List<GameObject>();
            try
            {
                RunAll(created);
            }
            finally
            {
                foreach (var go in created) if (go != null) UnityEngine.Object.DestroyImmediate(go);
                if (AssetDatabase.IsValidFolder(ScratchDir)) AssetDatabase.DeleteAsset(ScratchDir);
                if (AssetDatabase.IsValidFolder("Assets/YAPS/Generated/Smoke Prop")) AssetDatabase.DeleteAsset("Assets/YAPS/Generated/Smoke Prop");
                if (AssetDatabase.IsValidFolder("Assets/YAPS/Generated/Smoke Avatar")) AssetDatabase.DeleteAsset("Assets/YAPS/Generated/Smoke Avatar");
                if (AssetDatabase.IsValidFolder("Assets/YAPS/Generated/Test Plug")) AssetDatabase.DeleteAsset("Assets/YAPS/Generated/Test Plug");
                AssetDatabase.SaveAssets();
            }
            int fails = Results.Count(r => !r.ok);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Smoke test  {DateTime.Now:yyyy-MM-dd HH:mm}  {Results.Count - fails} passed, {fails} failed");
            foreach (var r in Results) sb.AppendLine($"- {(r.ok ? "PASS" : "FAIL")}  {r.name}{(string.IsNullOrEmpty(r.note) ? "" : "  : " + r.note)}");
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "SmokeTest.md"));
            File.WriteAllText(path, sb.ToString());
            if (fails == 0) Debug.Log("[Smoke] " + sb); else Debug.LogError("[Smoke] " + sb);
        }

        public static void RunBatch()
        {
            Run();
            EditorApplication.Exit(Results.Any(r => !r.ok) ? 1 : 0);
        }

        static void Step(string name, Action act)
        {
            try { act(); Results.Add((name, true, "")); }
            catch (Exception e) { Results.Add((name, false, e.GetType().Name + ": " + e.Message)); Debug.LogException(e); }
        }

        static void Check(bool condition, string what)
        {
            if (!condition) throw new Exception("expected " + what);
        }

        static void RunAll(List<GameObject> created)
        {
            // --- prefabs -------------------------------------------------
            Step("YAPS > Create universal socket prefabs", () =>
            {
                YapsSocketBuilder.CreatePrefabs();
                foreach (var name in new[] { "YAPS Hole", "YAPS Ring" })
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/YAPS/Prefabs/" + name + ".prefab");
                    Check(prefab != null, name + ".prefab written");
                    Check(prefab.GetComponent<YapsSocket>() != null, name + " carries YapsSocket");
                    Check(prefab.GetComponentsInChildren<Light>(true).Count(YapsScanner.IsProtocolLight) == 2, name + " has 2 marker lights");
                    Check(prefab.GetComponentsInChildren<CVRPointer>(true).Length == 4, name + " has 4 pointers");
                }
            });

            // --- an avatar-shaped object -----------------------------------
            var avatar = new GameObject("Smoke Avatar");
            created.Add(avatar);
            avatar.AddComponent<Animator>();
            var cvr = avatar.AddComponent<CVRAvatar>();
            var hips = new GameObject("Hips"); hips.transform.SetParent(avatar.transform, false);
            var armature = new GameObject("Armature"); armature.transform.SetParent(avatar.transform, false);
            hips.transform.SetParent(armature.transform, false);

            // --- add a hole / add a ring ------------------------------------
            YapsSocket hole = null, ring = null;
            Step("YAPS > Add a hole (under a bone)", () =>
            {
                var go = new GameObject("YAPS Hole"); go.transform.SetParent(hips.transform, false);
                hole = go.AddComponent<YapsSocket>(); hole.kind = YapsSocket.SocketKind.Hole;
                YapsSocketBuilder.Build(hole);
                Check(YapsSocketEditor.IsBuilt(go.transform), "hole is built");
                Check(go.GetComponentsInChildren<Light>(true).Count(YapsScanner.IsProtocolLight) == 2, "2 lights");
            });
            Step("YAPS > Add a ring", () =>
            {
                var go = new GameObject("YAPS Ring"); go.transform.SetParent(avatar.transform, false);
                go.transform.position = new Vector3(2f, 0f, 0f);
                ring = go.AddComponent<YapsSocket>(); ring.kind = YapsSocket.SocketKind.Ring;
                YapsSocketBuilder.Build(ring);
                Check(YapsSocketEditor.IsBuilt(go.transform), "ring is built");
                Check(go.GetComponentsInChildren<CVRPointer>(true).Any(p => p.type == "SPSLL_Socket_Ring"), "ring pointer");
            });
            Step("YAPS Socket > Kind switch rebuilds the markers", () =>
            {
                ring.kind = YapsSocket.SocketKind.Hole;
                YapsSocketBuilder.ApplyKind(ring);
                Check(ring.GetComponentsInChildren<Light>(true).Any(l => YapsScanner.IsProtocolLight(l) && YapsScanner.LightDigit(l) == 1), "root light re-ranged to hole");
                Check(ring.GetComponentsInChildren<CVRPointer>(true).Any(p => p.type == "SPSLL_Socket_Hole"), "SPS pointer renamed");
                Check(ring.name == "YAPS Hole", "object renamed (got " + ring.name + ")");
                ring.kind = YapsSocket.SocketKind.Ring;
                YapsSocketBuilder.ApplyKind(ring);
                Check(ring.name == "YAPS Ring" && YapsScanner.Scan(avatar).Sockets.Count(s => !s.IsHole) == 1, "back to a ring, scanner agrees");
            });
            Step("YAPS Socket > Rebuild markers (idempotent)", () =>
            {
                int before = hole.GetComponentsInChildren<CVRPointer>(true).Length;
                YapsSocketBuilder.Build(hole);
                Check(hole.GetComponentsInChildren<CVRPointer>(true).Length == before, "no duplicate markers");
            });

            // --- preview with a test plug (spawns and removes its own) --------
            Step("YAPS Socket > Preview with a test plug", () =>
            {
                YapsPreview.Set(hole, true);
                var spawned = GameObject.Find(YapsPreview.PlugName);
                Check(spawned != null, "a preview plug spawned");
                var mr = spawned.GetComponent<MeshRenderer>();
                Check(mr != null && mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_YAPS_Bake"), "preview plug baked");
                var block = new MaterialPropertyBlock();
                mr.GetPropertyBlock(block, 0);
                Check(block.GetVector("_YAPS_SocketFlags").x > 0.5f, "socket written into the plug's property block");
                YapsPreview.Set(hole, false);
                Check(GameObject.Find(YapsPreview.PlugName) == null, "preview plug removed");
                Check(!hole.preview, "preview off");
            });

            // --- test plug, make it a prop, verify -----------------------------
            GameObject plugRoot = null;
            Step("YAPS > Test plug", () =>
            {
                plugRoot = YapsNativeBuilder.BuildTestPlug(null, select: false);
                created.Add(plugRoot);
                plugRoot.name = "Smoke Prop";
                var mr = plugRoot.GetComponent<MeshRenderer>();
                Check(mr.sharedMaterial.HasProperty("_YAPS_Bake") && mr.sharedMaterial.GetFloat("_YAPS_Length") > 0.2f, "baked with a length");
                var markers = plugRoot.transform.Find("YAPS Markers");
                Check(markers != null, "announced (markers)");
                Check(markers.GetComponentsInChildren<Light>(true).Any(l => YapsScanner.LightDigit(l) == 9), "tracker light");
                Check(markers.GetComponentsInChildren<CVRPointer>(true).Any(p => p.type == "TPS_Pen_Penetrating"), "tip pointer");
            });
            Step("YAPS Plug > knobs write through", () =>
            {
                var plug = plugRoot.GetComponent<YapsPlug>();
                plug.curvature = 0.7f;
                var m = plugRoot.GetComponent<MeshRenderer>().sharedMaterial;
                YapsNativeBuilder.WriteKnobs(plug, m);
                Check(Mathf.Approximately(m.GetFloat("_YAPS_Curvature"), 0.7f), "material took the knob");
                YapsNativeBuilder.SyncPlugsFrom(m);
                Check(Mathf.Approximately(plug.curvature, 0.7f), "component reads it back");
            });
            Step("YAPS Plug > Re-bake", () =>
            {
                var o = YapsNativeBuilder.Bake(plugRoot.GetComponent<YapsPlug>());
                Check(o.Ok, "re-bake ok: " + o.Message);
            });
            // A fresh prop is grabbable by anyone and finds sockets by their
            // lights. The channel is the separate choice below: it is what
            // lets a socket's owner write the prop's values, and therefore
            // what takes it out of someone's hand.
            Step("YAPS > Make selected object a prop", () =>
            {
                var o = YapsPropBuilder.MakeProp(plugRoot);
                Check(o.Ok, o.Message);
                var sp = plugRoot.GetComponent<CVRSpawnable>();
                Check(sp != null, "a spawnable");
                Check(sp.syncValues.Count == 0, "no channel until it is asked for");
                Check(plugRoot.GetComponent<CVRPickupObject>()?.disallowTheft == false, "theft allowed");
                // On the ROOT: CVRPickupObject cannot see a child's collider.
                Check(plugRoot.GetComponent<Collider>() != null, "a collider on the root to grab");
            });
            Step("YAPS > Add the synced channel", () =>
            {
                var o = YapsPropBuilder.AddChannel(plugRoot);
                Check(o.Ok, o.Message);
                var sp = plugRoot.GetComponent<CVRSpawnable>();
                Check(sp != null && sp.useAdditionalValues && sp.syncValues.Count == 8, "8 synced values");
                Check(plugRoot.GetComponentsInChildren<CVRSpawnableTrigger>(true).Length == 8, "8 triggers, one per host");
                var controller = plugRoot.GetComponent<Animator>().runtimeAnimatorController as AnimatorController;
                Check(controller != null && controller.layers.Length == 8, "8 layers");
                Check(controller.layers.All(l => l.stateMachine != null && l.stateMachine.states.Length == 1), "each layer has its state");
                Check(plugRoot.GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_YAPS_ChannelSpace") > 0.5f, "channel space on");
            });
            Step("YAPS > Verify prop (repairs a blanked name)", () =>
            {
                var sp = plugRoot.GetComponent<CVRSpawnable>();
                sp.syncValues[0].animatorParameterName = "";
                var o = YapsPropBuilder.Verify(plugRoot);
                Check(o.Ok, o.Message);
                Check(sp.syncValues[0].animatorParameterName == "E", "name restored");
            });
            Step("YAPS > Drop the contact channel", () =>
            {
                var o = YapsPropBuilder.DropChannel(plugRoot);
                Check(o.Ok, o.Message);
                var sp = plugRoot.GetComponent<CVRSpawnable>();
                Check(sp.syncValues.Count == 0, "the synced values are gone");
                Check(plugRoot.GetComponentsInChildren<CVRSpawnableTrigger>(true).Length == 0, "and their triggers");
                Check(plugRoot.GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_YAPS_ChannelSpace") < 0.5f, "channel space off");
            });

            // --- make a plug from a bone (skinned mesh) --------------------------
            Step("YAPS > Make a plug from bone", () =>
            {
                var shaft = new GameObject("Shaft"); shaft.transform.SetParent(hips.transform, false);
                var tip = new GameObject("ShaftTip"); tip.transform.SetParent(shaft.transform, false);
                tip.transform.localPosition = new Vector3(0, 0, 0.15f);
                var smrGo = new GameObject("Body"); smrGo.transform.SetParent(avatar.transform, false);
                var smr = smrGo.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = SkinnedShaft(new[] { hips.transform, shaft.transform, tip.transform });
                smr.bones = new[] { hips.transform, shaft.transform, tip.transform };
                smr.rootBone = hips.transform;
                smr.sharedMaterials = new[] { new Material(Shader.Find(YapsNativeBuilder.SimpleLitName)), new Material(Shader.Find(YapsNativeBuilder.SimpleLitName)) };
                int slot = YapsNativeBuilder.SlotWeightedTo(smr, shaft.transform);
                Check(slot == 1, "the shaft's slot is found (got " + slot + ")");
                var plug = shaft.AddComponent<YapsPlug>();
                plug.renderer = smr; plug.rootBone = shaft.transform; plug.materialSlot = slot;
                var o = YapsNativeBuilder.Bake(plug);
                Check(o.Ok, o.Message);
                Check(smr.sharedMaterials[1].HasProperty("_YAPS_Bake"), "skinned slot baked");
                Check(YapsNativeBuilder.GuessRootBone(smr, 1) == shaft.transform, "root bone guessed from the mesh");
            });

            // --- scan and quiet -----------------------------------------------
            Step("YAPS > Scan", () =>
            {
                var scan = YapsScanner.Scan(avatar);
                Check(scan.Sockets.Count == 2, "2 sockets found (got " + scan.Sockets.Count + ")");
                Check(scan.Plugs.Count == 1, "1 plug found (got " + scan.Plugs.Count + ")");
            });
            Step("YAPS > Quiet the scene view (on and off)", () =>
            {
                bool was = SceneQuiet.IsQuiet;
                SceneQuiet.Toggle(); Check(SceneQuiet.IsQuiet != was, "toggled");
                SceneQuiet.Toggle(); Check(SceneQuiet.IsQuiet == was, "restored");
            });

            // --- toolkit ------------------------------------------------------
            var report = new BridgeReport();
            var ctx = new BridgeContext { Settings = new BridgeSettings(), Report = report, Target = avatar, CvrAvatar = cvr, OutputDir = ScratchDir };
            Step("Toolkit > Check this avatar (no controller)", () =>
            {
                BridgeDiagnostics.CheckComponentWhitelist(ctx);
                BridgeDiagnostics.CheckStereoShaders(ctx);
            });
            Step("Toolkit > Stereo shaders", () =>
            {
                ShaderSpiPatcher.Patch(avatar, ScratchDir + "/RehomedAssets", null, report);
            });
            Step("Toolkit > Face: visemes and blink", () =>
            {
                var r = CvrSetup.WireFace(avatar, new BridgeSettings());
                Check(r.Entries.Count > 0, "reported something");
            });
            Step("Toolkit > Audio limits", () =>
            {
                var src = avatar.AddComponent<AudioSource>();
                src.minDistance = 0f; src.spatialBlend = 0f;
                AvatarHygiene.SanitizeAudioSources(ctx);
                Check(src.minDistance > 0f || src.spatialBlend > 0.99f, "source clamped");
            });
            Step("Toolkit > Mesh bounds", () => AvatarHygiene.NormalizeSkinnedBounds(ctx));
            Step("Toolkit > Height slider", () =>
            {
                if (!AssetDatabase.IsValidFolder(ScratchDir)) { AssetDatabase.CreateFolder("Assets/AvatarBridgeOutput", "SmokeTest"); }
                var controller = AnimatorController.CreateAnimatorControllerAtPath(ScratchDir + "/Smoke.controller");
                avatar.GetComponent<Animator>().runtimeAnimatorController = controller;
                cvr.avatarSettings = new CVRAdvancedAvatarSettings { settings = new List<CVRAdvancedSettingsEntry>() };
                ctx.MergedController = controller;
                AvatarScalerInjector.Inject(controller, ctx);
                Check(controller.layers.Length > 0, "a layer added");
            });
            Step("Toolkit > Store description", () =>
            {
                string text = AvatarDescription.Build(ctx);
                Check(!string.IsNullOrEmpty(text), "text built");
                var result = CckDescriptionFiller.Fill(text, overwrite: true);
                Check(!string.IsNullOrEmpty(CckDescriptionFiller.Explain(result)), "explained");
            });
            Step("Toolkit > Merge animators", () =>
            {
                var a = AnimatorController.CreateAnimatorControllerAtPath(ScratchDir + "/A.controller");
                var b = AnimatorController.CreateAnimatorControllerAtPath(ScratchDir + "/B.controller");
                a.AddParameter("Shared", AnimatorControllerParameterType.Float);
                b.AddParameter("Shared", AnimatorControllerParameterType.Bool);
                b.AddParameter("Only", AnimatorControllerParameterType.Int);
                b.AddLayer("Extra");
                var r = new BridgeReport();
                var merged = AnimatorMergeTool.Merge(a, new[] { b }, ScratchDir + "/A merged.controller", r);
                Check(merged != null && merged != a, "written to a copy");
                Check(merged.layers.Any(l => l.name.StartsWith("Extra")), "layer arrived");
                Check(merged.parameters.Any(p => p.name == "Only"), "parameter arrived");
                Check(r.Entries.Any(e => e.Status == ReportStatus.Warning), "type clash named");
                Check(a.layers.All(l => !l.name.StartsWith("Extra")), "target untouched");
            });

            // --- Setup mode on any avatar ---------------------------------------
            Step("AvatarBridge > Set up any avatar (Setup mode)", () =>
            {
                var plain = new GameObject("Smoke Setup Avatar");
                created.Add(plain);
                plain.AddComponent<Animator>();
                var r = CvrSetup.Run(plain, new BridgeSettings { cloneAvatar = false });
                Check(plain.GetComponent<CVRAvatar>() != null, "CVRAvatar added");
                Check(!r.Entries.Any(e => e.Status == ReportStatus.Error), "no errors: " +
                    string.Join("; ", r.Entries.Where(e => e.Status == ReportStatus.Error).Select(e => e.Subject)));
            });

#if VRC_SDK_VRCSDK3
            // --- the converter, on a VRChat avatar already in the scene -----------
            // Convert itself is covered by the corpus on every avatar; this
            // presses the window's buttons once on whatever is open.
            var descriptor = UnityEngine.Object.FindObjectsOfType<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>()
                .FirstOrDefault(d => d.gameObject.activeInHierarchy);
            if (descriptor == null)
            {
                Results.Add(("AvatarBridge > Analyse / Convert", true, "SKIPPED: no VRChat avatar in the open scene; the corpus covers Convert"));
            }
            else
            {
                Step("AvatarBridge > Analyse this avatar", () =>
                {
                    var advice = AvatarAdvisor.Analyse(descriptor, new BridgeSettings());
                    Check(advice != null, "advice list");
                });
                Step("AvatarBridge > Convert", () =>
                {
                    var settings = new BridgeSettings();
                    var r = BridgeConverter.Convert(descriptor, settings);
                    Check(!r.Entries.Any(e => e.Status == ReportStatus.Error), "no errors: " +
                        string.Join("; ", r.Entries.Where(e => e.Status == ReportStatus.Error).Select(e => e.Subject)));
                });
            }
#else
            Results.Add(("AvatarBridge > Analyse / Convert", true, "SKIPPED: VRChat SDK not in this project"));
#endif

            // --- windows open --------------------------------------------------
            foreach (var (name, menu) in new[]
            {
                ("Window > VRChat to ChilloutVR Converter", "Tools/Avatar Bridge/VRChat to ChilloutVR Converter"),
                ("Window > CCK Animator Tester", "Tools/Avatar Bridge/CCK Animator Tester"),
                ("Window > ChilloutVR Toolkit", "Tools/Avatar Bridge/ChilloutVR Toolkit"),
                ("Window > YAPS Setup", "Tools/YAPS/Setup"),
            })
            {
                Step(name, () =>
                {
                    Check(EditorApplication.ExecuteMenuItem(menu), "menu item ran");
                    // A window that caught its own build exception shows a failure
                    // card instead of throwing; read it.
                    foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                    {
                        if (w.rootVisualElement == null) continue;
                        var failed = w.rootVisualElement.Query<Label>().ToList()
                            .FirstOrDefault(l => l.text != null && l.text.Contains("failed to build"));
                        Check(failed == null, "no \"failed to build\" card in " + w.titleContent.text);
                    }
                });
            }

            // The tester's controls write animator parameters and need Play mode;
            // that half is a manual check. Its window building is covered above.
            Results.Add(("CCK Animator Tester > drive gestures/visemes/menu", true, "MANUAL: needs Play mode with a CVR avatar"));
        }

        // A tiny skinned shaft: eight vertices in two submeshes, one weighted
        // to the hips (slot 0), one to the shaft chain (slot 1).
        static Mesh SkinnedShaft(Transform[] bones)
        {
            var mesh = new Mesh { name = "Smoke Shaft" };
            var v = new List<Vector3>(); var w = new List<BoneWeight>();
            // slot 0: a body quad on the hips
            foreach (var p in new[] { new Vector3(-0.1f, -0.1f, 0), new Vector3(0.1f, -0.1f, 0), new Vector3(0.1f, 0.1f, 0), new Vector3(-0.1f, 0.1f, 0) })
            { v.Add(p); w.Add(new BoneWeight { boneIndex0 = 0, weight0 = 1f }); }
            // slot 1: a strip along +Z, base on the shaft, tip on the tip bone
            for (int i = 0; i < 6; i++)
            {
                float t = i / 5f;
                v.Add(new Vector3(-0.02f, 0, t * 0.15f)); v.Add(new Vector3(0.02f, 0, t * 0.15f));
                var bw = new BoneWeight { boneIndex0 = 1, weight0 = 1f - t, boneIndex1 = 2, weight1 = t };
                w.Add(bw); w.Add(bw);
            }
            mesh.SetVertices(v);
            mesh.boneWeights = w.ToArray();
            mesh.bindposes = bones.Select(b => b.worldToLocalMatrix).ToArray();
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            var tris = new List<int>();
            for (int i = 0; i < 5; i++) { int a = 4 + i * 2; tris.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
            mesh.SetTriangles(tris, 1);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
#endif
