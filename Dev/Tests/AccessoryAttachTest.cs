#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // The Toolkit's "VRChat accessories" card on a converted avatar in a new scene, never saved: a
    // VRCFury accessory with no Armature Link dropped on the Head and one with plain PhysBones on the
    // Chest. Both convert in place, the avatar's own objects stay as they were, nothing points at a
    // destroyed object, and Ctrl+Z puts the accessories back as dropped. Needs the project's assets
    // named below; output in Assets/__AccessoryAttachTest, NDMF's and VRCFury's temp folders named
    // "Accessories" cleared from outside after.
    // -executeMethod AvatarBridge.Regression.AccessoryAttachTest.Run [-aaTarget <CVR prefab>] [-aaHead <prefab>] [-aaChest <prefab>]
    // [-aaShot <dir>]: the head from the front and the back, as dropped beside as converted, one JPEG each.
    public static class AccessoryAttachTest
    {
        const string Dir = "Assets/__AccessoryAttachTest";

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/VRChat accessories convert in place")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[AccessoryAttachTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            AssetDatabase.DeleteAsset(Dir);
            AssetDatabase.CreateFolder("Assets", "__AccessoryAttachTest");
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var target = (GameObject) PrefabUtility.InstantiatePrefab(Load(Arg("-aaTarget", "Assets/AvatarBridgeOutput/Ampwave/Ampwave (ChilloutVR).prefab")));
                var animator = target.GetComponent<Animator>();
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                fail += Check("a converted avatar alone carries no accessories", AccessoryAttach.Find(target).Count == 0);
                // One of the avatar's own objects aimed at an accessory: it must follow the conversion.
                var aim = new GameObject("Aimed at the glasses").AddComponent<UnityEngine.Animations.ParentConstraint>();
                aim.transform.SetParent(target.transform, false);
                var own = Snapshot(target);
                int missingBefore = Missing(target), clothBefore = Cloth(target);
                var doomedBefore = Doomed(target);

                var glasses = (GameObject) PrefabUtility.InstantiatePrefab(Load(Arg("-aaHead", "Assets/!Plague Doctor/Blackstar Store/C&D - Glasses Pack/Prefabs/Glasses - Academic.prefab")), head);
                var wings = (GameObject) PrefabUtility.InstantiatePrefab(Load(Arg("-aaChest", "Assets/ZealotDKD/Horse Male Anthro/Nightmare Wings/Nightmare Wings AV3.prefab")), chest);
                var lens = glasses.GetComponentsInChildren<Transform>(true).Last();
                string lensName = lens.name;
                aim.AddSource(new UnityEngine.Animations.ConstraintSource { sourceTransform = lens, weight = 1 });
                var dropped = Snapshot(target);
                var placed = Poses(target, own);
                var found = AccessoryAttach.Find(target);
                fail += Check($"both accessories found at their roots ({string.Join(", ", found.Select(f => f.name))})",
                    found.Count == 2 && found.Contains(glasses) && found.Contains(wings));
                string shots = Arg("-aaShot", null);
                var before = shots == null ? null : Views(target, animator);

                // The CCK adds its own object to a scene when it opens, so compare, never count.
                var roots = target.scene.GetRootGameObjects();
                var avatar = target.GetComponent<CVRAvatar>();
                var overBefore = avatar.overrides;
                var ownController = (AnimatorController) overBefore.runtimeAnimatorController;
                int ownLayers = ownController.layers.Length, entriesBefore = avatar.avatarSettings.settings.Count;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var report = AccessoryAttach.Run(target, new BridgeSettings(), r => Dir);
                Debug.Log($"[AccessoryAttachTest] ran in {clock.Elapsed.TotalSeconds:0} s");
                foreach (var e in report.Entries) Debug.Log($"[AccessoryAttachTest]   {e.Status} [{e.Category}] {e.Subject}: {e.Detail}");

                fail += Check("no VRChat, VRCFury or Modular Avatar component is left", AccessoryAttach.Find(target).Count == 0);
                fail += Check("the glasses are on the Head again", glasses == null && head.Find("Glasses - Academic") != null);
                fail += Check("the wings are on the Chest again", wings == null && chest.Find("Nightmare Wings AV3") != null);
                fail += Check($"three new cloths ({Cloth(target) - clothBefore})", Cloth(target) - clothBefore == 3);
                fail += Check("every reference found its match", !report.Entries.Any(e => e.Subject.Contains("had no match")));
                fail += Check($"nothing points at a destroyed object ({Missing(target) - missingBefore} new)", Missing(target) == missingBefore);
                var after = Snapshot(target);
                var aimedAt = aim.GetSource(0).sourceTransform;
                fail += Check($"the avatar's own reference follows the conversion ({(aimedAt == null ? "nothing" : aimedAt.name)})",
                    aimedAt != null && aimedAt.IsChildOf(head) && aimedAt.name == lensName);
                var poses = Poses(target, own);
                var moved = placed.Keys.Where(k => poses.TryGetValue(k, out var q) && (Vector3.Distance(q.p, placed[k].p) > 1e-4f || Quaternion.Angle(q.r, placed[k].r) > 0.01f)).ToList();
                fail += Check($"every carried object sits where it was dropped ({poses.Keys.Count(placed.ContainsKey)} compared; {string.Join(", ", moved.Take(3))})",
                    moved.Count == 0 && poses.Keys.Count(placed.ContainsKey) > 50);
                var changed = own.Keys.Where(p => !after.TryGetValue(p, out var t) || t != own[p]).ToList();
                fail += Check($"the avatar's own objects are as they were ({string.Join(", ", changed.Take(5))})", changed.Count == 0);
                var extra = target.scene.GetRootGameObjects().Except(roots).Select(r => r.name).ToList();
                fail += Check($"the stand-in and its output are gone ({string.Join(", ", extra)})", extra.Count == 0);
                fail += Check("the report names both glasses toggles as carried",
                    report.Entries.Any(e => e.Subject.StartsWith("Carried their toggles") && e.Detail.Contains("Glasses Toggle") && e.Detail.Contains("Glasses Arms")));
                var wired = avatar.overrides != null ? avatar.overrides.runtimeAnimatorController as AnimatorController : null;
                string wiredPath = wired == null ? "" : AssetDatabase.GetAssetPath(wired);
                fail += Check($"the avatar ships a copy of its controller from the accessories' folder ({wiredPath})", wiredPath.StartsWith(Dir + "/"));
                fail += Check("its own controller is not edited", ownController.layers.Length == ownLayers && avatar.overrides != overBefore);
                var layerNames = wired == null ? new List<string>() : wired.layers.Select(l => l.name).ToList();
                fail += Check($"the copy has both toggle layers ({string.Join(", ", layerNames.Skip(ownLayers))})",
                    layerNames.Contains("Toggle Glasses Toggle") && layerNames.Contains("Toggle Glasses Arms"));
                fail += Check("the copy has both toggle parameters", wired != null
                    && wired.parameters.Any(x => x.name == "GlassesToggle") && wired.parameters.Any(x => x.name == "GlassesArms"));
                var machine = avatar.avatarSettings.settings.Select(e => e.machineName).ToList();
                fail += Check($"the menu gained both entries ({entriesBefore} to {machine.Count})", machine.Contains("GlassesToggle") && machine.Contains("GlassesArms"));
                var doomedFiles = wiredPath == "" ? new List<string>() : AssetDatabase.GetDependencies(wiredPath, true).Where(AnimatorMerger.IsDoomedGeneratedPath).ToList();
                fail += Check($"the copy uses no file VRCFury or NDMF deletes ({string.Join(", ", doomedFiles.Take(3))})", doomedFiles.Count == 0);
                fail += Check("no errors", report.CountOf(ReportStatus.Error) == 0);
                var left = AssetDatabase.FindAssets("", new[] { Dir }).Select(AssetDatabase.GUIDToAssetPath).ToList();
                Debug.Log($"[AccessoryAttachTest] {left.Count} assets left: {string.Join(", ", left.Take(12))}");
                var reach = new HashSet<string>(AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(avatar.overrides), true));
                fail += Check("the copy's own prefab and reports are gone, and every controller left is wired in",
                    !left.Any(p => p.EndsWith(".prefab") || p.Contains("ConversionReport") || p.EndsWith("Diagnostics.md")
                        || (p.EndsWith(".controller") || p.EndsWith(".overrideController")) && !reach.Contains(p)));
                var doomed = Doomed(target).Except(doomedBefore).ToList();
                fail += Check($"nothing uses a file VRCFury or NDMF deletes ({string.Join(", ", doomed.Take(3))})", doomed.Count == 0);
                foreach (var r in target.GetComponentsInChildren<Renderer>(true).Where(r => r.transform.IsChildOf(head.Find("Glasses - Academic") ?? head)))
                    Debug.Log($"[AccessoryAttachTest] glasses renderer {r.name}: active {r.gameObject.activeInHierarchy}, enabled {r.enabled}, " +
                        $"centre {(r.bounds.center - head.position) * 100:0.0} cm from the head, size {r.bounds.size * 100:0.0} cm");
                if (shots != null) Save(before, Views(target, animator), shots);

                // The toggle itself, run by the controller ChilloutVR ships. Last: it moves the avatar.
                var copyRoot = head.Find("Glasses - Academic");
                bool Shown() => copyRoot.GetComponentsInChildren<Renderer>(true).Any(r => r.gameObject.activeInHierarchy);
                animator.runtimeAnimatorController = avatar.overrides;
                animator.Rebind();
                void Toggle(bool on)
                {
                    animator.SetBool("GlassesToggle", on);
                    for (int i = 0; i < 20; i++) animator.Update(0.1f);
                }
                Toggle(true);
                bool shownOn = Shown();
                Toggle(false);
                bool shownOff = Shown();
                fail += Check($"GlassesToggle shows the glasses and hides them again (on {shownOn}, off {shownOff})", shownOn && !shownOff);

                // The record: Detach removes what the press added, Re-attach brings it back.
                var records = AccessoryRecord.FindIn(Dir);
                fail += Check($"the press wrote a record ({records.Count})", records.Count == 1 && records[0].accessories.Count == 2 && records[0].copies.Count > 0);
                var detached = AccessoryRecord.Detach(target, records[0]);
                foreach (var e in detached.Entries) Debug.Log($"[AccessoryAttachTest]   {e.Status} [{e.Category}] {e.Subject}: {e.Detail}");
                fail += Check("Detach removes the carried objects", head.Find("Glasses - Academic") == null && chest.Find("Nightmare Wings AV3") == null);
                fail += Check($"Detach leaves only the avatar's own cloth ({Cloth(target) - clothBefore} extra)", Cloth(target) == clothBefore);
                fail += Check("Detach puts the avatar's own controller and menu back", avatar.overrides == overBefore && avatar.avatarSettings.settings.Count == entriesBefore);
                var again = AccessoryRecord.Reattach(target, records[0], new BridgeSettings(), r => Dir);
                foreach (var e in again.Entries) Debug.Log($"[AccessoryAttachTest]   {e.Status} [{e.Category}] {e.Subject}: {e.Detail}");
                fail += Check("Re-attach brings both back converted", head.Find("Glasses - Academic") != null && chest.Find("Nightmare Wings AV3") != null
                    && AccessoryAttach.Find(target).Count == 0 && Cloth(target) - clothBefore == 3);
                fail += Check("Re-attach carries the toggles again", avatar.overrides != overBefore
                    && avatar.avatarSettings.settings.Any(e => e.machineName == "GlassesToggle") && again.CountOf(ReportStatus.Error) == 0);
                fail += Check("Re-attach wrote a second record, newest first", AccessoryRecord.FindIn(Dir).Count == 2 && AccessoryRecord.FindIn(Dir)[0] != records[0]);
                Undo.PerformUndo();
                fail += Check("Ctrl+Z on Re-attach leaves it detached", head.Find("Glasses - Academic") == null && avatar.overrides == overBefore);
                Undo.PerformUndo();
                fail += Check("Ctrl+Z on Detach brings the converted objects back", head.Find("Glasses - Academic") != null);

                Undo.PerformUndo();
                var undone = Snapshot(target);
                var differ = dropped.Keys.Union(undone.Keys).Where(p => !dropped.TryGetValue(p, out var a) || !undone.TryGetValue(p, out var b) || a != b).ToList();
                fail += Check($"Ctrl+Z puts it back as dropped ({string.Join(", ", differ.Take(5))})", differ.Count == 0);
                fail += Check("Ctrl+Z finds both accessories again", AccessoryAttach.Find(target).Count == 2);
                fail += Check("Ctrl+Z aims the avatar's reference at the original again", aim.GetSource(0).sourceTransform != null && aim.GetSource(0).sourceTransform != aimedAt);
                fail += Check("Ctrl+Z puts the avatar's controller and menu back", avatar.overrides == overBefore && avatar.avatarSettings.settings.Count == entriesBefore);
            }
            catch (Exception e)
            {
                fail += Check("threw " + e, false);
            }
            finally
            {
                AssetDatabase.DeleteAsset(Dir);
            }
            Debug.Log(fail == 0
                ? "[AccessoryAttachTest] PASS: accessories convert in place and Ctrl+Z puts them back."
                : $"[AccessoryAttachTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        // The glasses from the front, the wings from behind, at the avatar's own scale.
        static Texture2D[] Views(GameObject target, Animator animator)
        {
            var head = animator.GetBoneTransform(HumanBodyBones.Head).position;
            var chest = animator.GetBoneTransform(HumanBodyBones.Chest).position;
            float d = Vector3.Distance(head, animator.GetBoneTransform(HumanBodyBones.Hips).position);
            var f = target.transform.forward;
            return new[] { Shot(head, head + f * d * 1.3f), Shot(chest, chest - f * d * 4 + Vector3.up * d * 0.6f) };
        }

        static Texture2D Shot(Vector3 at, Vector3 from)
        {
            const int Size = 600;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.45f);
            var light = new GameObject("Shot light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.LookRotation(at - from) * Quaternion.Euler(25, 25, 0);
            var cam = new GameObject("Shot camera").AddComponent<Camera>();
            cam.transform.position = from;
            cam.transform.LookAt(at);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.3f, 0.32f, 0.36f);
            cam.nearClipPlane = 0.01f;
            cam.fieldOfView = 45;
            var rt = RenderTexture.GetTemporary(Size, Size, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
            UnityEngine.Object.DestroyImmediate(light.gameObject);
            return tex;
        }

        static void Save(Texture2D[] before, Texture2D[] after, string dir)
        {
            Directory.CreateDirectory(dir);
            for (int i = 0; i < before.Length; i++)
            {
                int w = before[i].width, h = before[i].height;
                var both = new Texture2D(w * 2, h, TextureFormat.RGB24, false);
                both.SetPixels(0, 0, w, h, before[i].GetPixels());
                both.SetPixels(w, 0, w, h, after[i].GetPixels());
                both.Apply();
                File.WriteAllBytes(Path.Combine(dir, $"accessories-{i + 1}.jpg"), both.EncodeToJPG(88));
                Debug.Log($"[AccessoryAttachTest] wrote {Path.Combine(dir, $"accessories-{i + 1}.jpg")}");
            }
        }

        static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new Exception("missing " + path);

        // Every object by path, with its components' types, the way the card compares them.
        static Dictionary<string, string> Snapshot(GameObject root) =>
            root.GetComponentsInChildren<Transform>(true).GroupBy(t => t == root.transform ? "" : AnimationUtility.CalculateTransformPath(t, root.transform))
                .ToDictionary(g => g.Key, g => string.Join(" ", g.First().GetComponents<Component>().Select(c => c == null ? "missing" : c.GetType().Name)));

        static List<string> Doomed(GameObject root) => root.GetComponentsInChildren<Component>(true).Where(c => c != null)
            .SelectMany(Used).Where(AnimatorMerger.IsDoomedGeneratedPath).Distinct().ToList();

        static IEnumerable<string> Used(Component c)
        {
            var it = new SerializedObject(c).GetIterator();
            while (it.Next(true))
                if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null)
                    yield return AssetDatabase.GetAssetPath(it.objectReferenceValue);
        }

        // World pose of every object the avatar did not have before.
        static Dictionary<string, (Vector3 p, Quaternion r)> Poses(GameObject root, Dictionary<string, string> own) =>
            root.GetComponentsInChildren<Transform>(true).Where(t => t != root.transform)
                .Select(t => (path: AnimationUtility.CalculateTransformPath(t, root.transform), t)).Where(x => !own.ContainsKey(x.path))
                .GroupBy(x => x.path).ToDictionary(g => g.Key, g => (g.First().t.position, g.First().t.rotation));

        static int Cloth(GameObject root) => root.GetComponentsInChildren<Component>(true).Count(c => c != null && c.GetType().Name == "MagicaCloth");

        static int Missing(GameObject root)
        {
            int n = 0;
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var it = new SerializedObject(c).GetIterator();
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null && it.objectReferenceInstanceIDValue != 0)
                        n++;
            }
            return n;
        }

        static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
    }
}
#endif
