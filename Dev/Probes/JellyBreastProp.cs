// A breast pair as a prop: a torso front on a board, built from nothing of any avatar's, given the
// squash and the live tissue by the same calls an avatar's build makes, so a hand in game meets the
// worn path's skin, step and probes on something anyone can spawn and pass round.
//
// The worn build hangs the jelly from the humanoid trunk, so the stand carries a mannequin rig
// for the build alone: a humanoid Animator lends it the trunk and is removed once it has.
#if CVR_CCK_EXISTS && UNITY_EDITOR && AVATARBRIDGE_MAGICA
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    public static class JellyBreastProp
    {
        // A folder of its own, so building it leaves the worn jelly's and the box prop's assets be.
        const string Folder = "Assets/AvatarBridge_JellyBreastProp";
        const string Name = "Jelly Breast Prop";

        // The torso front, metres in the stand's frame: x across, y up, z out of the chest.
        const float Wide = 0.21f, Above = 0.17f, Below = 0.19f, PlateY = 1.19f, Back = 0.05f, Plate = 0.05f, Grid = 0.004f;
        // Each breast: its centre off the middle, its reach across, up and down, and how far it stands out.
        // 9 cm across leaves the 2 cm cleavage on a 42 cm plate; 13 cm would not fit two.
        const float BreastY = 1.17f, Apart = 0.10f, Radius = 0.09f, Upper = 0.11f, Lower = 0.085f, Out = 0.09f;
        // The tip's set: out and down by these shares of the height.
        const float SetOut = 0.12f, SetDown = 0.15f;

        [MenuItem("Tools/Avatar Bridge/Dev/Jelly/Make a breast pair prop")]
        static void MakeMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            YapsNativeBuilder.EnsureFolderPublic(Folder);
            string said = null;
            bool made = JellySpikeBuilder.InScene(Folder + "/Jelly_BreastProp.unity", Name, id => Make(id, out said));
            Debug.Log(made ? $"[Jelly] made the breast prop in {Folder}/Jelly_BreastProp.unity; upload it from the CCK Control Panel. {said}" : "[Jelly] FAIL the breast prop did not build: " + said);
        }

        // Batch: -executeMethod AvatarBridge.Regression.JellyBreastProp.Batch
        public static void Batch()
        {
            YapsNativeBuilder.EnsureFolderPublic(Folder);
            string said = null;
            bool made = JellySpikeBuilder.InScene(Folder + "/Jelly_BreastProp.unity", Name, id => Make(id, out said));
            Debug.Log(made ? $"[Jelly] made the breast prop in {Folder}/Jelly_BreastProp.unity. {said}" : "[Jelly] FAIL the breast prop did not build: " + said);
            EditorApplication.Exit(made ? 0 : 1);
        }

        static GameObject Make(string id, out string said)
        {
            // A rebuild's squash copies start over: the squash names each copy unique, so they would pile up.
            AssetDatabase.DeleteAsset(Folder + "/Squash");
            var prop = JellySpikeBuilder.Shell(Name, id);
            var rig = new GameObject("Stand").transform;
            rig.SetParent(prop.transform, false);
            Transform Bone(string name, Transform parent, float x, float y, float z)
            {
                var t = new GameObject(name).transform;
                t.SetParent(parent, false);
                t.position = rig.TransformPoint(x, y, z);
                return t;
            }
            // The fifteen bones a humanoid needs, and a chest, named as HumanTrait names them.
            var hips = Bone("Hips", rig, 0f, 0.95f, 0f);
            var chest = Bone("Chest", Bone("Spine", hips, 0f, 1.05f, 0f), 0f, 1.2f, 0f);
            Bone("Head", Bone("Neck", chest, 0f, 1.42f, 0f), 0f, 1.52f, 0f);
            var chains = new Transform[4];
            for (int k = 0; k < 2; k++)
            {
                float s = k == 0 ? -1f : 1f;
                string side = k == 0 ? "Left" : "Right";
                Bone(side + "Hand", Bone(side + "LowerArm", Bone(side + "UpperArm", chest, 0.18f * s, 1.38f, 0f), 0.45f * s, 1.38f, 0f), 0.7f * s, 1.38f, 0f);
                Bone(side + "Foot", Bone(side + "LowerLeg", Bone(side + "UpperLeg", hips, 0.09f * s, 0.9f, 0f), 0.09f * s, 0.5f, 0f), 0.09f * s, 0.08f, 0f);
                // Named as an avatar's, so the squash's name search pairs them; the root under the
                // skin's base, the middle carrying most of it, the end at the tip for the cloth.
                string lr = k == 0 ? "L" : "R";
                float tip = Back + Plate * Round(Apart / Wide) * Round((PlateY - BreastY) / Below) + Out;
                chains[2 * k] = Bone($"Breast_{lr}", chest, Apart * s, BreastY, 0.09f);
                chains[2 * k + 1] = Bone($"Breast_{lr}_1", chains[2 * k], (Apart + 0.5f * SetOut * Out) * s, BreastY - 0.5f * SetDown * Out, 0.14f);
                Bone($"Breast_{lr}_End", chains[2 * k + 1], (Apart + SetOut * Out) * s, BreastY - SetDown * Out, tip);
            }
            var avatar = Humanoid(rig);
            if (avatar == null)
            {
                Object.DestroyImmediate(prop);
                said = "the mannequin rig made no valid humanoid";
                return null;
            }
            var lender = rig.gameObject.AddComponent<Animator>();
            lender.avatar = avatar;

            var body = new GameObject("Body").AddComponent<SkinnedMeshRenderer>();
            body.transform.SetParent(rig, false);
            var bones = new[] { chest, chains[0], chains[1], chains[2], chains[3] };
            var mesh = Torso();
            mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * body.transform.localToWorldMatrix).ToArray();
            string meshAt = Folder + "/Jelly_Breast_Body.asset";
            // Remade, not rebuilt in place: Save keeps an old mesh's vertices but not its weights.
            AssetDatabase.DeleteAsset(meshAt);
            AssetDatabase.CreateAsset(mesh, meshAt);
            body.sharedMesh = mesh;
            body.bones = bones;
            body.rootBone = chest;
            // A shader the patcher reads straight: on Standard it would swap in YAPS Standard and hide
            // which material the build made.
            body.sharedMaterial = YapsJelly.Save(new Material(Shader.Find(YapsNativeBuilder.SimpleLitName)) { color = new Color(0.93f, 0.76f, 0.66f) }, Folder + "/Jelly_Breast_Skin.mat");

            // The pair's cloth, as an avatar's breasts carry, which the worn jelly rides.
            var cloth = new GameObject("Breast Cloth");
            cloth.transform.SetParent(rig, false);
            cloth.SetActive(false);
            var sd = cloth.AddComponent<MagicaCloth2.MagicaCloth>().SerializeData;
            sd.clothType = MagicaCloth2.ClothProcess.ClothType.BoneCloth;
            sd.rootBones = new List<Transform> { chains[0], chains[2] };
            // Every viewer's copy, seen or not, as the jelly's probes.
            sd.cullingSettings.cameraCullingMode = MagicaCloth2.CullingSettings.CameraCullingMode.Off;
            cloth.SetActive(true);

            // Held by its board alone, as the box prop learned: a box round the skin took every grab,
            // so a fist closing on it picked the prop up instead.
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Board";
            Object.DestroyImmediate(board.GetComponent<Collider>());
            board.transform.SetParent(prop.transform, false);
            board.transform.localPosition = new Vector3(0f, PlateY + 0.5f * (Above - Below), Back - 0.01f);
            board.transform.localScale = new Vector3(2f * Wide + 0.04f, Above + Below + 0.04f, 0.02f);
            board.GetComponent<MeshRenderer>().sharedMaterial = YapsJelly.Save(new Material(Shader.Find("Standard")) { color = new Color(0.35f, 0.3f, 0.27f) }, Folder + "/Jelly_Board.mat");
            var held = board.GetComponent<MeshRenderer>().bounds;
            var grab = prop.GetComponent<BoxCollider>();
            grab.center = prop.transform.InverseTransformPoint(held.center);
            grab.size = held.size + new Vector3(0.02f, 0.02f, 0.02f);

            // The squash, then the jelly on its pair, the calls a user's build makes; the twitch on,
            // and no avatar menu to wire.
            string worn = null;
            try
            {
                said = YapsSquash.Add(prop.transform, Folder + "/Squash", out bool built);
                if (built) worn = YapsJelly.PairOn(prop.transform, YapsJelly.Breast, Folder + "/Jelly", Folder + "/Jelly", new YapsJelly.Options { twitch = true, wire = false }, new BridgeReport());
            }
            finally { YapsShaderPatcher.EndPass(null, null); }
            if (worn == null || !worn.StartsWith("On "))
            {
                Object.DestroyImmediate(prop);
                said = worn ?? said;
                return null;
            }
            said = worn;

            var twitch = prop.GetComponentsInChildren<Transform>(true).First(t => t.name == "Jelly Twitch");
            var animator = JellySpikeBuilder.Twitched(prop, twitch, Folder);
            // The tells went to the lender, which plays no controller: the prop's own takes them.
            var controller = (AnimatorController) animator.runtimeAnimatorController;
            var driver = prop.GetComponentInChildren<CVRAnimatorDriver>(true);
            for (int i = 0; i < driver.animators.Count; i++)
            {
                driver.animators[i] = animator;
                if (!controller.parameters.Any(p => p.name == driver.animatorParameters[i]))
                    controller.AddParameter(driver.animatorParameters[i], AnimatorControllerParameterType.Float);
            }
            Object.DestroyImmediate(lender);
            Object.DestroyImmediate(avatar);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = prop;
            return prop;
        }

        // The rig as a humanoid, its bones by their names; null when Unity will not take it.
        static Avatar Humanoid(Transform rig)
        {
            var all = rig.GetComponentsInChildren<Transform>(true);
            var description = new HumanDescription
            {
                human = all.Where(t => HumanTrait.BoneName.Contains(t.name))
                    .Select(t => new HumanBone { boneName = t.name, humanName = t.name, limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
                skeleton = all.Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray(),
            };
            var avatar = AvatarBuilder.BuildHumanAvatar(rig.gameObject, description);
            if (avatar != null && avatar.isValid && avatar.isHuman) return avatar;
            if (avatar != null) Object.DestroyImmediate(avatar);
            return null;
        }

        static float Round(float t) => Mathf.Sqrt(Mathf.Max(0f, 1f - t * t * t * t));

        // The torso front as one sheet, its rim rolled back onto the board: the plate, and a breast
        // each side, fuller below than above, its tip set out and down. Weighted to the chest off the
        // breasts, and on each to its root at the base and its middle bone toward the tip.
        static Mesh Torso()
        {
            int nx = Mathf.RoundToInt(2f * Wide / Grid) + 1, ny = Mathf.RoundToInt((Above + Below) / Grid) + 1;
            var at = new Vector3[nx * ny];
            var uv = new Vector2[nx * ny];
            var weights = new BoneWeight[nx * ny];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = -Wide + i * Grid, y = PlateY - Below + j * Grid;
                    float z = Back + Plate * Round(x / Wide) * Round(y > PlateY ? (y - PlateY) / Above : (PlateY - y) / Below);
                    var w = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                    var set = Vector3.zero;
                    for (int k = 0; k < 2; k++)
                    {
                        float s = k == 0 ? -1f : 1f;
                        float u = (x - s * Apart) / Radius, v = (y - BreastY) / (y > BreastY ? Upper : Lower);
                        float d2 = u * u + v * v;
                        if (d2 >= 1.21f) continue;
                        float d = Mathf.Sqrt(d2);
                        // Round on top and meeting the plate with no crease.
                        float h = d < 1f ? Out * (1f - d2) * (1f - d2) * (1f + 1.2f * d2) : 0f;
                        z += h;
                        set = new Vector3(SetOut * h * s, -SetDown * h, 0f);
                        // The breast's own bones all of it to 0.85 across, the chest's past 1.1.
                        float part = Mathf.SmoothStep(0f, 1f, (1.1f - d) / 0.25f), toTip = Mathf.SmoothStep(0f, 1f, (1f - d) / 0.6f);
                        w = new BoneWeight
                        {
                            boneIndex0 = 0, weight0 = 1f - part,
                            boneIndex1 = 1 + 2 * k, weight1 = part * (1f - toTip),
                            boneIndex2 = 2 + 2 * k, weight2 = part * toTip,
                        };
                    }
                    at[j * nx + i] = new Vector3(x, y, z) + set;
                    uv[j * nx + i] = new Vector2((float) i / (nx - 1), (float) j / (ny - 1));
                    weights[j * nx + i] = w;
                }
            var tris = new List<int>();
            for (int j = 0; j + 1 < ny; j++)
                for (int i = 0; i + 1 < nx; i++)
                {
                    int a = j * nx + i;
                    // Facing out of the chest, +z.
                    tris.AddRange(new[] { a, a + 1, a + nx, a + 1, a + nx + 1, a + nx });
                }
            var mesh = new Mesh { name = "Jelly Breast Body", vertices = at, uv = uv, triangles = tris.ToArray(), boneWeights = weights };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
#endif
