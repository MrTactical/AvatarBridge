#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge.Regression
{
    // The Toolkit's "PhysBones to cloth" card on a synthetic ChilloutVR avatar: converts in
    // place, rewires its animations into a copy of the controller and of its override, leaves
    // the user's own files byte-identical, undoes in one step, refuses a second set, a VRChat
    // avatar and an override that swaps in a physics clip.
    // Batch: -executeMethod AvatarBridge.Regression.PhysBonesInPlaceTest.Run
    public static class PhysBonesInPlaceTest
    {
        const string Dir = "Assets/PhysBonesInPlaceTest";
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/PhysBones to cloth card")]
        public static void Run()
        {
            failures = 0;
            var made = new List<GameObject>();
            try
            {
                AssetDatabase.DeleteAsset(Dir);
                AssetDatabase.CreateFolder("Assets", "PhysBonesInPlaceTest");
                var avatar = Fixture("CardAvatar");
                made.Add(avatar);
                var cvr = avatar.GetComponent<CVRAvatar>();
                var controller = AnimatorController.CreateAnimatorControllerAtPath(Dir + "/Card.controller");
                var drive = Clip("Hair physics off", "Hips/Hair_root", typeof(VRCPhysBone), "m_Enabled", 0f);
                drive.SetCurve("Hips/Hair_root", typeof(VRCPhysBone), "gravity", AnimationCurve.Constant(0, 1, 0.5f));
                var hide = Clip("Hair hidden", "Hips/Hair_root", typeof(GameObject), "m_IsActive", 0f);
                var chest = Clip("Chest collider off", "Hips/Chest", typeof(VRCPhysBoneCollider), "m_Enabled", 0f);
                var tree = new BlendTree { name = "Hair tree", blendParameter = "Hair", blendType = BlendTreeType.Simple1D };
                AssetDatabase.CreateAsset(tree, Dir + "/Hair tree.asset");
                tree.AddChild(hide, 0f);
                controller.AddParameter("Hair", AnimatorControllerParameterType.Float);
                controller.AddMotion(drive);
                controller.AddMotion(tree);
                controller.AddMotion(chest);
                // A swap on a clip nothing here touches, as the CCK's own are, has to survive the copy.
                var wave = Clip("Wave", "Hips", typeof(Transform), "m_LocalPosition.y", 0f);
                controller.AddMotion(wave);
                var over = new AnimatorOverrideController(controller) { name = "Card" };
                var waved = Clip("Wave swapped", "Hips", typeof(Transform), "m_LocalPosition.y", 1f);
                over[wave] = waved;
                AssetDatabase.CreateAsset(over, Dir + "/Card.overrideController");
                cvr.overrides = over;
                // As Create Controller leaves it: the CCK would offer to attach these again.
                cvr.avatarSettings = new ABI.CCK.Scripts.CVRAdvancedAvatarSettings { baseController = controller, overrides = over };
                avatar.GetComponent<Animator>().runtimeAnimatorController = over;
                AssetDatabase.SaveAssets();
                var files = Snapshot();

                var settings = new BridgeSettings
                {
                    physicsTarget = PhysicsTarget.MagicaCloth2, deleteConvertedPhysBones = true, fitCollidersToMesh = false,
                };
                var report = PhysBonesInPlace.Run(avatar, settings, _ => Dir);
                Log(report);
                AssetDatabase.SaveAssets();
                Check(!report.Entries.Any(e => e.Status == ReportStatus.Error), "no error");
                Check(Cloths(avatar) == 1, "one cloth made");
                Check(avatar.GetComponentsInChildren<VRCPhysBone>(true).Length == 0, "the PhysBone is deleted");
                Check(files.All(f => File.Exists(f.Key) && File.ReadAllText(f.Key) == f.Value),
                    "the user's controller, override, tree and clips are byte-identical");
                Check(cvr.overrides != over && avatar.GetComponent<Animator>().runtimeAnimatorController == cvr.overrides
                      && over.runtimeAnimatorController == controller, "the avatar runs a copy of the override, the user's still wraps theirs");
                Check(cvr.overrides[wave] == waved, "and the copy keeps the override's other swaps");

                var copy = AvatarSurvey.ShippedController(cvr);
                string copyPath = AssetDatabase.GetAssetPath(copy);
                Check(copy != null && copy != controller && copyPath.StartsWith(Dir + "/"), "the copy lives in the output folder");
                AnimationClip InCopy(string name) => copy.animationClips.FirstOrDefault(c => c != null && c.name == name);
                var drove = InCopy(drive.name);
                Check(drove != null && Bindings(drove).Any(b => b.type.Name == "MagicaCloth" && b.propertyName == "m_Enabled")
                      && !Bindings(drove).Any(b => b.type == typeof(VRCPhysBone)) && AssetDatabase.GetAssetPath(drove) == copyPath,
                    "the PhysBone switch now drives the cloth, from the copy's own clip");
                var hid = InCopy(hide.name);
                Check(hid != null && hid != hide && Bindings(hid).Any(b => b.type.Name == "MagicaCloth" && b.propertyName == "m_Enabled"),
                    "the hide toggle inside a tree also switches the cloth");
                var chested = InCopy(chest.name);
                Check(chested != null && Bindings(chested).Any(b => b.type == typeof(GameObject) && b.propertyName == "m_IsActive"
                                                                    && b.path.EndsWith("MagicaCollider_Chest"))
                      && !Bindings(chested).Any(b => b.type == typeof(VRCPhysBoneCollider)),
                    "the collider switch now toggles the converted collider's object");
                string text = File.ReadAllText(copyPath);
                Check(text.Contains("MagicaCloth Phys") && text.Contains("MagicaCollider_Chest"), "and the edits reached the copy on disk");
                Check(tree.children.Length == 1 && tree.children[0].motion == hide, "the user's tree still holds the user's clip");
                Check(Has(report, ReportStatus.Converted, "re-wired"), "the rewire is reported");
                Check(Has(report, ReportStatus.Converted, "collider on/off"), "the collider rewire is reported");
                Check(report.Entries.Any(e => e.Status == ReportStatus.Skipped && e.Detail.Contains("gravity")), "the animated gravity is named");
                Check(Has(report, ReportStatus.Converted, "rewired in a copy"), "the copy is reported");
                Check(cvr.avatarSettings.overrides == cvr.overrides && cvr.avatarSettings.baseController == copy
                      && !report.Entries.Any(e => e.Subject.Contains("Create Controller")),
                    "Create Controller's own slots point at the copies too, so nothing warns");

                Undo.PerformUndo();
                Check(avatar.GetComponentsInChildren<VRCPhysBone>(true).Length == 1, "one undo brings the PhysBone back");
                Check(Cloths(avatar) == 0 && avatar.transform.Find("MagicaCloth Phys") == null, "and takes the cloth and its holder");
                Check(cvr.overrides == over && avatar.GetComponent<Animator>().runtimeAnimatorController == over,
                    "and points the avatar back at the user's override");
                Undo.PerformRedo();
                Check(Cloths(avatar) == 1 && avatar.GetComponentsInChildren<VRCPhysBone>(true).Length == 0
                      && AvatarSurvey.ShippedController(cvr) == copy, "redo puts the cloth and the copy back");
                Undo.PerformUndo();

                // With Delete off the PhysBones stay, and a second press would stack cloth.
                settings.deleteConvertedPhysBones = false;
                PhysBonesInPlace.Run(avatar, settings, _ => Dir);
                var again = PhysBonesInPlace.Run(avatar, settings, _ => Dir);
                Check(again.Entries.Any(e => e.Status == ReportStatus.Warning && e.Subject.Contains("already carries")),
                    "a second press with cloth there refuses");
                Check(Cloths(avatar) == 1, "and adds nothing");

                // An override that swaps in a physics clip would keep playing it unrewired.
                var swapped = Fixture("SwappedAvatar");
                made.Add(swapped);
                var plain = AnimatorController.CreateAnimatorControllerAtPath(Dir + "/Swapped.controller");
                var idle = Clip("Idle", "Hips", typeof(Transform), "m_LocalPosition.y", 0f);
                plain.AddMotion(idle);
                plain.AddMotion(Clip("Hair hidden too", "Hips/Hair_root", typeof(GameObject), "m_IsActive", 0f));
                var swapper = new AnimatorOverrideController(plain) { name = "Swapped" };
                swapper[idle] = Clip("Hair physics off too", "Hips/Hair_root", typeof(VRCPhysBone), "m_Enabled", 0f);
                AssetDatabase.CreateAsset(swapper, Dir + "/Swapped.overrideController");
                swapped.GetComponent<CVRAvatar>().overrides = swapper;
                var refusal = PhysBonesInPlace.Run(swapped, settings, _ => Dir);
                Log(refusal);
                Check(refusal.Entries.Any(e => e.Subject == "Animations left as they are" && e.Detail.Contains("Hair physics off too")),
                    "an override swapping in a physics clip leaves the animations, and says which");
                Check(swapped.GetComponent<CVRAvatar>().overrides == swapper && Cloths(swapped) == 1,
                    "and still converts, on the user's own override");

                // Built by Create Controller: the upload is a copy of the base plus the menu's own
                // layers, and the next Create Controller rebuilds it from the base.
                var aas = Fixture("AasAvatar");
                made.Add(aas);
                var baseCtl = AnimatorController.CreateAnimatorControllerAtPath(Dir + "/Base.controller");
                var baseDrive = Clip("Base physics off", "Hips/Hair_root", typeof(VRCPhysBone), "m_Enabled", 0f);
                baseCtl.AddMotion(baseDrive);
                AssetDatabase.SaveAssets();
                AssetDatabase.CopyAsset(Dir + "/Base.controller", Dir + "/Base_aas.controller");
                var aasCtl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Dir + "/Base_aas.controller");
                aasCtl.AddMotion(Clip("Menu hair toggle", "Hips/Hair_root", typeof(GameObject), "m_IsActive", 0f));
                var aasOver = new AnimatorOverrideController(aasCtl) { name = "Base_aas" };
                AssetDatabase.CreateAsset(aasOver, Dir + "/Base_aas.overrideController");
                var aasCvr = aas.GetComponent<CVRAvatar>();
                aasCvr.overrides = aasOver;
                aas.GetComponent<Animator>().runtimeAnimatorController = aasOver;
                aasCvr.avatarSettings = new ABI.CCK.Scripts.CVRAdvancedAvatarSettings
                {
                    baseController = baseCtl, animator = aasCtl, overrides = aasOver,
                };
                AssetDatabase.SaveAssets();
                string baseText = File.ReadAllText(Dir + "/Base.controller");
                var rebuilt = PhysBonesInPlace.Run(aas, settings, _ => Dir);
                Log(rebuilt);
                AssetDatabase.SaveAssets();
                var baseCopy = aasCvr.avatarSettings.baseController as AnimatorController;
                var baseDrove = baseCopy != null ? baseCopy.animationClips.FirstOrDefault(c => c != null && c.name == baseDrive.name) : null;
                Check(baseCopy != null && baseCopy != baseCtl && AssetDatabase.GetAssetPath(baseCopy).StartsWith(Dir + "/")
                      && baseDrove != null && Bindings(baseDrove).Any(b => b.type.Name == "MagicaCloth" && b.propertyName == "m_Enabled"),
                    "a base controller Create Controller rebuilds from is rewired in its own copy");
                Check(File.ReadAllText(Dir + "/Base.controller") == baseText, "and the user's base is byte-identical");
                Check(rebuilt.Entries.Any(e => e.Subject == "Create Controller would undo part of the rewiring" && e.Detail.Contains("Menu hair toggle"))
                      && !rebuilt.Entries.Any(e => e.Subject == "Create Controller would undo the rewiring"),
                    "only the menu's own toggle is named as lost to a rebuild");
                Undo.PerformUndo();
                Check(aasCvr.avatarSettings.baseController == baseCtl && aasCvr.overrides == aasOver, "undo points the base back too");

                var vrchat = Fixture("VrcAvatar", false);
                made.Add(vrchat);
                vrchat.AddComponent<VRCAvatarDescriptor>();
                var refused = PhysBonesInPlace.Run(vrchat, settings, _ => Dir);
                Check(refused.Entries.Any(e => e.Subject == "This is a VRChat avatar"), "a VRChat-only avatar is refused");
                Check(Cloths(vrchat) == 0 && vrchat.GetComponentsInChildren<VRCPhysBone>(true).Length == 1, "and left as it was");

                // A port that kept its VRChat descriptor beside the CVRAvatar is what the card is for.
                var port = Fixture("PortAvatar");
                made.Add(port);
                port.AddComponent<VRCAvatarDescriptor>();
                settings.deleteConvertedPhysBones = true;
                var ported = PhysBonesInPlace.Run(port, settings, _ => Dir);
                Check(!ported.Entries.Any(e => e.Subject == "This is a VRChat avatar") && Cloths(port) == 1,
                    "a port that kept its VRChat descriptor converts");
                Check(ported.Entries.Any(e => e.Subject.Contains("VRChat avatar descriptor")),
                    "and warns that its VRChat upload loses the PhysBones");
            }
            finally
            {
                foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
                AssetDatabase.DeleteAsset(Dir);
            }
            Debug.Log(failures == 0 ? "[PhysBonesInPlaceTest] PASS" : $"[PhysBonesInPlaceTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        // One three-bone chain with a mesh listing its bones, so it is not read as a helper
        // rig, and a collider it uses on a chest bone. Not "Hand": hands take their own route.
        static GameObject Fixture(string name, bool chilloutVr = true)
        {
            var root = new GameObject(name);
            root.AddComponent<Animator>();
            if (chilloutVr) root.AddComponent<CVRAvatar>();
            var hips = new GameObject("Hips");
            hips.transform.SetParent(root.transform, false);
            var chain = new GameObject("Hair_root");
            chain.transform.SetParent(hips.transform, false);
            var link = chain.transform;
            for (int i = 1; i <= 3; i++)
            {
                var next = new GameObject("Hair_" + i).transform;
                next.SetParent(link, false);
                next.localPosition = new Vector3(0f, 0.1f, 0f);
                link = next;
            }
            var chestBone = new GameObject("Chest");
            chestBone.transform.SetParent(hips.transform, false);
            var collider = chestBone.AddComponent<VRCPhysBoneCollider>();
            var pb = chain.AddComponent<VRCPhysBone>();
            pb.rootTransform = chain.transform;
            pb.colliders = new List<VRCPhysBoneColliderBase> { collider };
            var holder = new GameObject("Hair_Mesh");
            holder.transform.SetParent(hips.transform, false);
            var skin = holder.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = new Mesh { name = "Hair_Mesh" };
            skin.bones = chain.GetComponentsInChildren<Transform>(true);
            skin.rootBone = chain.transform;
            return root;
        }

        // Saved as its own file, as a user's clip is.
        static AnimationClip Clip(string name, string path, System.Type type, string property, float value)
        {
            var clip = new AnimationClip { name = name };
            clip.SetCurve(path, type, property, AnimationCurve.Constant(0, 1, value));
            AssetDatabase.CreateAsset(clip, $"{Dir}/{name}.anim");
            return clip;
        }

        static Dictionary<string, string> Snapshot() =>
            Directory.GetFiles(Dir).Where(f => !f.EndsWith(".meta")).ToDictionary(f => f, File.ReadAllText);

        static EditorCurveBinding[] Bindings(AnimationClip clip) => AnimationUtility.GetCurveBindings(clip);

        static bool Has(BridgeReport report, ReportStatus status, string subject) =>
            report.Entries.Any(e => e.Status == status && e.Subject.Contains(subject));

        static void Log(BridgeReport report)
        {
            foreach (var e in report.Entries) Debug.Log($"[PhysBonesInPlaceTest]   {e.Status} {e.Subject}: {e.Detail}");
        }

        static int Cloths(GameObject avatar) =>
            avatar.GetComponentsInChildren<Component>(true).Count(c => c != null && c.GetType().Name == "MagicaCloth");

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[PhysBonesInPlaceTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
