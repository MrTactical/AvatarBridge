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
using AvatarBridge.Yaps;
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
        // Grid lines 3.7 mm apart over the breasts, widening to 12 mm over 4 cm past them, so the
        // larger pair stays under the 16 bit index limit.
        const float Wide = 0.63f, Above = 0.34f, Below = 0.42f, PlateY = 1.17f, Back = 0.05f, Plate = 0.06f;
        const float Fine = 0.0037f, Coarse = 0.012f, Ramp = 0.04f;
        // Each breast's patch of the grid: its centre off the middle and its reach across, up and
        // down. The patch is cast onto the globe, so it only has to reach past where the skin
        // rejoins the chest; across it stops at the middle, where the other's starts.
        const float BreastY = 1.13f, Apart = 0.2317f, Upper = 0.33f, Lower = 0.31f;
        // Each breast a hyper pair's globe, perky and round, about 46 x 46 x 42 cm: an ellipsoid centred Out from the
        // chest and Sag below its patch, radius Across wide, Top above and Bottom below (the lower
        // pole the fuller), Front to the tip and Rear back into the chest, which widens the base.
        // 23 cm across on 23.17 apart leaves the inner skins under 3 mm apart, so a squeeze
        // presses one into the other with no overlap for the pair's plane to start from.
        const float Out = 0.19f, Sag = 0.0f, Across = 0.23f, Top = 0.22f, Bottom = 0.235f, Front = 0.235f, Rear = 0.25f;
        // The top half leans back by Slope, so the upper pole runs down to the tip as a slope rather
        // than a shelf, and the tip turns Turn degrees out. The globe joins the chest through a fillet
        // Fillet wide on top, no crease there, narrowing to Fold below, the under-fold.
        const float Slope = 0.08f, Turn = 7f, Fillet = 0.08f, Fold = 0.03f;

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
                var globe = new Globe(s);
                var tip = globe.centre + Front * globe.axis;
                chains[2 * k] = Bone($"Breast_{lr}", chest, Apart * s, BreastY, globe.wall - 0.01f);
                chains[2 * k + 1] = Bone($"Breast_{lr}_1", chains[2 * k], globe.centre.x, globe.centre.y, globe.centre.z);
                Bone($"Breast_{lr}_End", chains[2 * k + 1], tip.x, tip.y, tip.z);
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
            // which material the build made. A soft sheen and no glow: a glow of its own tone lit every
            // side alike and left the globes reading flat under the film's lights.
            var skin = new Color(0.93f, 0.74f, 0.64f);
            var look = new Material(Shader.Find(YapsNativeBuilder.SimpleLitName)) { color = skin };
            look.SetFloat("_Glossiness", 0.42f);
            look.SetColor("_EmissionColor", Color.black);
            body.sharedMaterial = YapsJelly.Save(look, Folder + "/Jelly_Breast_Skin.mat");
            // Shading alone shapes it: its own shadow rendered hard black over the skin, and the fold
            // and the cleavage only deepen that. It still casts on the board.
            body.receiveShadows = false;

            // The pair's cloth, as an avatar's breasts carry, which the worn jelly rides.
            var cloth = new GameObject("Breast Cloth");
            cloth.transform.SetParent(rig, false);
            cloth.SetActive(false);
            var sd = cloth.AddComponent<MagicaCloth2.MagicaCloth>().SerializeData;
            sd.clothType = MagicaCloth2.ClothProcess.ClothType.BoneCloth;
            sd.rootBones = new List<Transform> { chains[0], chains[2] };
            // Every viewer's copy, seen or not, as the jelly's probes.
            sd.cullingSettings.cameraCullingMode = MagicaCloth2.CullingSettings.CameraCullingMode.Off;
            // Heavy: 0.4 of the board's own movement, the measured heft lever; more damping would slow
            // the return too. A softer pull to rest (0.14 at the root to 0.04 at the tip, from 0.2 to
            // 0.04) swings it slower and wider, and less of its speed lost on the way back (0.65,
            // from 0.8) lets it overshoot and settle over a few swings. The chains are twice as long
            // now, so the same pull already lets gravity hang them further. Held within 40 degrees,
            // from 30, room to hang and swing; the chain, 13 and 0 degrees down at rest, still stops
            // over 30 degrees off the plate, so a board turned over does not fold it in.
            sd.inertiaConstraint.worldInertia = 0.4f;
            sd.angleRestorationConstraint.stiffness.SetValue(0.14f, 1f, 0.3f);
            sd.angleRestorationConstraint.velocityAttenuation = 0.65f;
            sd.angleLimitConstraint.useAngleLimit = true;
            sd.angleLimitConstraint.limitAngle.SetValue(40f);
            cloth.SetActive(true);

            // Held by its board alone, as the box prop learned: a box round the skin took every grab,
            // so a fist closing on it picked the prop up instead.
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Board";
            Object.DestroyImmediate(board.GetComponent<Collider>());
            board.transform.SetParent(prop.transform, false);
            board.transform.localPosition = new Vector3(0f, PlateY + 0.5f * (Above - Below), Back - 0.015f);
            board.transform.localScale = new Vector3(2f * Wide + 0.04f, Above + Below + 0.04f, 0.03f);
            var wood = new Material(Shader.Find("Standard")) { color = new Color(0.22f, 0.15f, 0.11f) };
            wood.SetFloat("_Glossiness", 0.6f);
            board.GetComponent<MeshRenderer>().sharedMaterial = YapsJelly.Save(wood, Folder + "/Jelly_Board.mat");
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

            // Soft heavy flesh on the pair's feel, as the inspector would set it: softer and looser
            // (firmness 0.8, taut 0.8), a hand moving less of the heavier whole (give 0.9), the whole
            // swinging further and springing back slower (swing 1.2, spring back 0.8), deeper dents
            // and a bigger slap ring for a part twice the size (depth 1.3, slap 1.3); the fill-back's
            // settle as tuned.
            var jelly = prop.GetComponentsInChildren<Transform>(true).First(t => t.name.StartsWith(YapsJelly.Worn + " "));
            var settings = YapsSquash.SettingsOf(prop.transform);
            var feel = settings.feels.First(f => f.part == jelly.name.Substring(YapsJelly.Worn.Length + 1));
            (feel.firmness, feel.settle, feel.give, feel.swing, feel.springBack, feel.taut, feel.depth, feel.slap) = (0.8f, 1f, 0.9f, 1.2f, 0.8f, 0.8f, 1.3f, 1.3f);
            feel.preset = YapsSquashSettings.Preset.Custom;
            EditorUtility.SetDirty(settings);
            YapsJelly.Retune(jelly, settings);

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

        // The plate's front, rounded off toward its rim.
        static float ChestZ(float x, float y) => Back + Plate * Round(x / Wide) * Round(y > PlateY ? (y - PlateY) / Above : (PlateY - y) / Below);

        // One breast as a surface: the chest and the sagged ellipsoid joined smooth, and the point
        // the mesh is cast from. That point sits just behind the plate inside the globe, where both
        // the globe's underside and the chest it overhangs are in sight, so each ray meets the skin
        // once; cast from the globe's centre, the chest under the fold was hidden behind the globe.
        readonly struct Globe
        {
            const float Sink = 0.012f, Step = 0.003f, Reach = 0.6f;
            public readonly Vector3 centre, from, axis, across;
            public readonly float wall;

            public Globe(float s)
            {
                wall = ChestZ(s * Apart, BreastY);
                centre = new Vector3(s * Apart, BreastY - Sag, wall + Out);
                from = new Vector3(s * Apart, BreastY - Sag, wall - Sink);
                float t = Turn * Mathf.Deg2Rad;
                axis = new Vector3(s * Mathf.Sin(t), 0f, Mathf.Cos(t));
                across = new Vector3(s * Mathf.Cos(t), 0f, -Mathf.Sin(t));
            }

            // Under 0 inside the flesh: the chest and the ellipsoid as distances, smooth-joined.
            float Inside(Vector3 p)
            {
                var q = p - centre;
                float qx = Vector3.Dot(q, across), qz = Vector3.Dot(q, axis), qy = q.y;
                qy += Slope * qz * Mathf.Clamp01(qy / Top);
                float ry = qy > 0f ? Top : Bottom, rz = qz > 0f ? Front : Rear;
                // An ellipsoid's distance, near enough: its level set over its gradient.
                float k0 = new Vector3(qx / Across, qy / ry, qz / rz).magnitude;
                float k1 = Mathf.Max(new Vector3(qx / (Across * Across), qy / (ry * ry), qz / (rz * rz)).magnitude, 1e-9f);
                float globe = k0 * (k0 - 1f) / k1, chest = p.z - ChestZ(p.x, p.y);
                float k = Mathf.Lerp(Fold, Fillet, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(qy / Top)));
                float h = Mathf.Max(k - Mathf.Abs(chest - globe), 0f) / k;
                return Mathf.Min(chest, globe) - h * h * k * 0.25f;
            }

            // Where a ray from the cast point leaves the flesh: marched, then halved down to a micron.
            public Vector3 Hit(Vector3 dir)
            {
                float lo = 0f, hi = Step;
                while (hi < Reach && Inside(from + hi * dir) <= 0f) { lo = hi; hi += Step; }
                for (int i = 0; i < 24; i++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (Inside(from + mid * dir) > 0f) hi = mid; else lo = mid;
                }
                return from + 0.5f * (lo + hi) * dir;
            }

            // The patch's rim at angle phi, on the chest: the last ray toward it lands on it exactly,
            // so the cast skin meets the flat plate with no seam.
            public Vector3 Rim(float phi)
            {
                float x = centre.x + Apart * Mathf.Cos(phi), y = BreastY + Mathf.Sin(phi) * (phi > 0f ? Upper : Lower);
                return new Vector3(x, y, ChestZ(x, y));
            }

            public Vector3 Ray(float phi, float share) => Vector3.Slerp(axis, (Rim(phi) - from).normalized, share);
        }

        // Grid lines from lo to hi, Fine apart over a to b and widening to Coarse past it, so the
        // breasts take most of the index budget and the flat plate little.
        static List<float> Lines(float lo, float hi, float a, float b)
        {
            float Step(float x) => Mathf.Lerp(Fine, Coarse, Mathf.Max(Mathf.Max(a - x, x - b), 0f) / Ramp);
            float c = 0.5f * (a + b);
            var lines = new List<float>();
            for (float x = c - Step(c); x > lo + 0.5f * Step(x); x -= Step(x)) lines.Add(x);
            lines.Add(lo);
            lines.Reverse();
            lines.Add(c);
            for (float x = c + Step(c); x < hi - 0.5f * Step(x); x += Step(x)) lines.Add(x);
            lines.Add(hi);
            return lines;
        }

        // How far each ray turns from the tip toward the rim for an even share of the length along
        // the skin, per angle round the patch: rays turned evenly bunch on the front and spread
        // over the sides, which run back to the chest.
        const int Turns = 96, Steps = 384, Shares = 129;
        static float[,] Spacing(Globe globe)
        {
            var share = new float[Turns + 1, Shares];
            var arc = new float[Steps + 1];
            for (int i = 0; i <= Turns; i++)
            {
                float phi = Mathf.PI * (2f * i / Turns - 1f);
                var last = Vector3.zero;
                for (int n = 0; n <= Steps; n++)
                {
                    var p = globe.Hit(globe.Ray(phi, (float) n / Steps));
                    arc[n] = n == 0 ? 0f : arc[n - 1] + Vector3.Distance(p, last);
                    last = p;
                }
                for (int f = 0, n = 0; f < Shares; f++)
                {
                    float want = arc[Steps] * f / (Shares - 1);
                    while (n < Steps - 1 && arc[n + 1] < want) n++;
                    share[i, f] = (n + Mathf.Clamp01(Mathf.InverseLerp(arc[n], arc[n + 1], want))) / Steps;
                }
            }
            return share;
        }

        // The torso front as one sheet, its rim rolled back onto the board: the plate, and a breast
        // each side, its patch of the grid cast out onto the globe. Weighted to the chest off the
        // breasts, and on each to its root at the base and its middle bone toward the tip.
        static Mesh Torso()
        {
            var xs = Lines(-Wide, Wide, -2f * Apart - 0.01f, 2f * Apart + 0.01f);
            var ys = Lines(PlateY - Below, PlateY + Above, BreastY - Lower - 0.01f, BreastY + Upper + 0.01f);
            int nx = xs.Count, ny = ys.Count;
            var globes = new[] { new Globe(-1f), new Globe(1f) };
            var spacing = new[] { Spacing(globes[0]), Spacing(globes[1]) };
            var at = new Vector3[nx * ny];
            var uv = new Vector2[nx * ny];
            var weights = new BoneWeight[nx * ny];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = xs[i], y = ys[j];
                    var w = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                    var p = new Vector3(x, y, ChestZ(x, y));
                    // The near breast's alone: their weighted rims overlap in the cleavage.
                    int k = x < 0f ? 0 : 1;
                    float s = k == 0 ? -1f : 1f;
                    float u = (x - s * Apart) / Apart, v = (y - BreastY) / (y > BreastY ? Upper : Lower);
                    float d = Mathf.Sqrt(u * u + v * v);
                    if (d < 1f)
                    {
                        // The patch's distance out from its middle is the share of the length along
                        // the skin from the tip, read off the angle's spacing either side.
                        float phi = Mathf.Atan2(v, u), fa = (phi + Mathf.PI) / (2f * Mathf.PI) * Turns, ff = d * (Shares - 1);
                        int a0 = Mathf.Clamp((int) fa, 0, Turns - 1), f0 = Mathf.Clamp((int) ff, 0, Shares - 2);
                        var sp = spacing[k];
                        float share = Mathf.Lerp(Mathf.Lerp(sp[a0, f0], sp[a0, f0 + 1], ff - f0), Mathf.Lerp(sp[a0 + 1, f0], sp[a0 + 1, f0 + 1], ff - f0), fa - a0);
                        p = globes[k].Hit(globes[k].Ray(phi, share));
                    }
                    if (d < 1.1f)
                    {
                        // The breast's own bones all of it to 0.85 across, the chest's past 1.1.
                        float part = Mathf.SmoothStep(0f, 1f, (1.1f - d) / 0.25f), toTip = Mathf.SmoothStep(0f, 1f, (1f - d) / 0.6f);
                        w = new BoneWeight
                        {
                            boneIndex0 = 0, weight0 = 1f - part,
                            boneIndex1 = 1 + 2 * k, weight1 = part * (1f - toTip),
                            boneIndex2 = 2 + 2 * k, weight2 = part * toTip,
                        };
                    }
                    at[j * nx + i] = p;
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
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
#endif
