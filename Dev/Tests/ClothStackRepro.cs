#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge.Regression
{
    // Field report 2026-09-16: reconverting the same avatar stacks MagicaCloth
    // holders inside ONE avatar, under "MagicaCloth Phys", with default settings.
    // Nothing in the conversion path writes to the source, so this asks each
    // candidate route directly and prints what actually happened.
    //
    // A synthetic avatar, not a corpus one: route D applies prefab overrides,
    // which writes to the prefab asset on disk, and no paid avatar is worth
    // that risk.
    public static class ClothStackRepro
    {
        const string Folder = "Assets/ClothStackRepro";
        const string PrefabPath = Folder + "/ReproAvatar.prefab";
        static string LastOutcome = "";

        [MenuItem("Tools/AvatarBridge Dev/Repro: stacking MagicaCloth")]
        public static void Run()
        {
            Prepare();
            var lines = new List<string>();
            try
            {
                lines.Add(RouteA());
                lines.Add(RouteB());
                lines.Add(RouteC());
                lines.Add(RouteD());
                lines.Add(RouteE());
                lines.Add(RouteF());
                lines.Add(RouteG());
            }
            finally
            {
                foreach (var line in lines) Debug.Log("[ClothStack] " + line);
                AssetDatabase.Refresh();
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // Convert the source twice, defaults, nothing deleted between runs.
        static string RouteA()
        {
            var source = Instance();
            var first = Convert(source);
            var second = Convert(source);
            string result = $"A: convert the source twice, defaults -> first output {Holders(first)}, " +
                            $"second output {Holders(second)}, source {Holders(source)}";
            Clear(source, first, second);
            return result;
        }

        // Convert, then try to convert the OUTPUT, which is what "took that
        // base, added stuff, reconverted it" would mean literally.
        static string RouteB()
        {
            var source = Instance();
            var first = Convert(source);
            var descriptor = first.GetComponentInChildren<VRCAvatarDescriptor>(true);
            string result;
            if (descriptor == null)
            {
                result = "B: the output has no VRCAvatarDescriptor, so it cannot be converted again";
            }
            else
            {
                var second = BridgeConverter.Convert(descriptor, new BridgeSettings()) != null
                    ? Selection.activeGameObject : null;
                result = $"B: the output KEPT its descriptor; converting it gave {Holders(second)} " +
                         $"(the output it came from has {Holders(first)})";
                Clear(second);
            }
            Clear(source, first);
            return result;
        }

        // "Make a copy" off: the source is converted in place.
        static string RouteC()
        {
            var source = Instance();
            var settings = new BridgeSettings { cloneAvatar = false };
            Convert(source, settings);
            string after = Holders(source);
            var descriptor = source.GetComponentInChildren<VRCAvatarDescriptor>(true);
            string second = descriptor == null
                ? "and it can no longer be converted: the descriptor is gone"
                : $"and converting it again gave {Holders(Convert(source, new BridgeSettings { cloneAvatar = false }))}";
            Clear(source);
            return $"C: convert in place -> source {after}, {second}";
        }

        // Overrides -> Apply All on the converted avatar, which is an instance
        // of the same prefab the source is.
        static string RouteD()
        {
            var source = Instance();
            var first = Convert(source);
            string linked = PrefabUtility.IsPartOfPrefabInstance(first)
                ? "the output IS a prefab instance" : "the output is NOT a prefab instance";
            if (PrefabUtility.IsPartOfPrefabInstance(first))
            {
                PrefabUtility.ApplyPrefabInstance(first, InteractionMode.AutomatedAction);
            }
            Clear(source, first);
            var fresh = Instance();
            string result = $"D: {linked}; after applying its overrides a fresh source instance has " +
                            $"{Holders(fresh)}, and converting it gives {Holders(Convert(fresh))}";
            Clear(fresh);
            return result;
        }

        // The outfit case: convert, then add a rig with a PhysBone to the
        // SOURCE and convert again, both outputs left in the scene.
        static string RouteE()
        {
            var source = Instance();
            var first = Convert(source);
            AddChain(source.transform.Find("Hips"), "Skirt_root");
            var second = Convert(source);
            string result = $"E: outfit added to the source between runs -> first output {Holders(first)}, " +
                            $"second output {Holders(second)}, source {Holders(source)}";
            Clear(source, first, second);
            return result;
        }

        // The innocent explanation: several outfits whose bones share a name,
        // converted ONCE. Suffixes then count outfits, not conversions.
        static string RouteF()
        {
            var source = Instance();
            var hips = source.transform.Find("Hips");
            for (int outfit = 1; outfit <= 4; outfit++)
            {
                var wrap = new GameObject($"Outfit_{outfit}");
                wrap.transform.SetParent(hips, false);
                AddChain(wrap.transform, "L_Ear");
            }
            var converted = Convert(source);
            string result = $"F: one conversion, four outfits each with an \"L_Ear\" chain -> " +
                            $"{Holders(converted)}: {Names(converted)}";
            Clear(source);
            return result;
        }

        // Two PhysBones on one root, which the converter writes as two cloths.
        static string RouteG()
        {
            var source = Instance();
            var root = source.transform.Find("Hips/Hair_root");
            for (int extra = 0; extra < 2; extra++)
            {
                var pb = root.gameObject.AddComponent<VRCPhysBone>();
                pb.rootTransform = root;
            }
            var converted = Convert(source);
            string result = $"G: one conversion, three PhysBones on one root -> {Holders(converted)}: " +
                            Names(converted);
            Clear(source);
            return result;
        }

        // ---------------------------------------------------------------- helpers

        static void Prepare()
        {
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.DeleteAsset("Assets/AvatarBridgeReproOutput");
            AssetDatabase.CreateFolder("Assets", "ClothStackRepro");
            var root = new GameObject("ReproAvatar");
            root.AddComponent<Animator>();
            root.AddComponent<VRCAvatarDescriptor>();
            var hips = new GameObject("Hips");
            hips.transform.SetParent(root.transform, false);
            AddChain(hips.transform, "Hair_root");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
        }

        static void AddChain(Transform parent, string name)
        {
            var chain = new GameObject(name);
            chain.transform.SetParent(parent, false);
            var link = chain;
            for (int i = 1; i <= 3; i++)
            {
                var next = new GameObject($"{name}_{i}");
                next.transform.SetParent(link.transform, false);
                next.transform.localPosition = new Vector3(0f, 0.1f, 0f);
                link = next;
            }
            var pb = chain.AddComponent<VRCPhysBone>();
            pb.rootTransform = chain.transform;
            Skin(chain.transform);
        }

        // A chain no mesh is weighted to is treated as a helper rig and skipped,
        // so every chain gets a renderer listing its bones. The mesh itself is
        // never drawn here; only SkinnedMeshRenderer.bones is read.
        static void Skin(Transform chain)
        {
            var holder = new GameObject(chain.name + "_Mesh");
            holder.transform.SetParent(chain.parent, false);
            var skin = holder.AddComponent<SkinnedMeshRenderer>();
            var bones = chain.GetComponentsInChildren<Transform>(true);
            skin.sharedMesh = new Mesh { name = chain.name + "_Mesh" };
            skin.bones = bones;
            skin.rootBone = chain;
        }

        static GameObject Instance() =>
            (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));

        static GameObject Convert(GameObject avatar, BridgeSettings settings = null)
        {
            var descriptor = avatar.GetComponentInChildren<VRCAvatarDescriptor>(true);
            if (descriptor == null) return null;
            settings = settings ?? new BridgeSettings();
            settings.outputFolder = "Assets/AvatarBridgeReproOutput";
            LastOutcome = Outcome(BridgeConverter.Convert(descriptor, settings));
            Debug.Log("[ClothStack] conversion: " + LastOutcome);
            return Selection.activeGameObject;
        }

        // What the video counts: children of the collection object. Searched
        // anywhere under the avatar, and the cloth components counted too, so
        // "nothing here" cannot be this method looking in the wrong place.
        static string Holders(GameObject avatar)
        {
            if (avatar == null) return "no avatar";
            int collections = 0, holders = 0;
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "MagicaCloth Phys") continue;
                collections++;
                holders += t.childCount;
            }
            int cloths = 0;
            foreach (var c in avatar.GetComponentsInChildren<Component>(true))
            {
                if (c != null && c.GetType().Name == "MagicaCloth") cloths++;
            }
            return $"{collections} collection(s), {holders} holder(s), {cloths} cloth component(s)";
        }

        static string Names(GameObject avatar)
        {
            var home = avatar == null ? null : avatar.transform.Find("MagicaCloth Phys");
            if (home == null) return "no collection";
            var names = new List<string>();
            foreach (Transform child in home) names.Add(child.name);
            return string.Join(", ", names);
        }

        static string Outcome(BridgeReport report)
        {
            if (report == null) return "no report";
            var physics = new List<string>();
            foreach (var e in report.Entries)
            {
                if (e.Category != null && (e.Category.StartsWith("PhysBones") || e.Category.Contains("rig")))
                {
                    physics.Add($"[{e.Category}] {e.Status} {e.Subject}");
                }
            }
            return $"{report.Entries.Count} entries | " + string.Join(" || ", physics);
        }

        static void Clear(params GameObject[] objects)
        {
            foreach (var go in objects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
        }
    }
}
#endif
