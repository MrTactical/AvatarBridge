// Spike S2 rig. Builds the probe cube and the protocol lights with the
// exact ranges, because the whole test turns on the fourth decimal and
// typing 0.4106 by hand is how a spike lies to you.
//
// Two rigs:
//   Self-contained  cube + all four lights on one object. Answers "does
//                   the client populate the slots at all, and does the
//                   range survive the upload".
//   Lights only     the lights with no cube, to park on an avatar so the
//                   probe can be a separate prop. Answers the question
//                   that actually matters: does one piece of content see
//                   another piece of content's vertex lights.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Spike
{
    static class SpsSpikeRig
    {
        const string ShaderName = "AvatarBridge/SPS Light Probe";

        // Ranges we intend to author. The 0.41/0.42/0.45 triple is the
        // DPS protocol as the decoder reads it; 0.49 is the plug tip.
        static readonly (string name, float range)[] Protocol =
        {
            ("Probe Light Hole 0.41", 0.41f),
            ("Probe Light Ring 0.42", 0.42f),
            ("Probe Light Front 0.45", 0.45f),
            ("Probe Light Tip 0.49", 0.49f),
        };

        [MenuItem("AvatarBridge/Spike/Build light probe rig (cube + lights)")]
        static void BuildSelfContained()
        {
            var root = new GameObject("SPS Light Probe Rig");
            Undo.RegisterCreatedObjectUndo(root, "Build SPS probe rig");

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Probe Cube";
            cube.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
            cube.GetComponent<MeshRenderer>().sharedMaterial = ProbeMaterial();

            // Spread them so each occupies its own slot and the swatch
            // brightness reads differently per light.
            var spots = new[]
            {
                new Vector3(0.35f, 0.15f, 0f),
                new Vector3(-0.35f, 0.15f, 0f),
                new Vector3(0f, 0.15f, 0.35f),
                new Vector3(0f, 0.15f, -0.35f),
            };
            for (int i = 0; i < Protocol.Length; i++)
            {
                MakeLight(root.transform, Protocol[i].name, Protocol[i].range, spots[i]);
            }

            Selection.activeGameObject = root;
            Debug.Log("[SpsSpike] Built probe rig. Upload as a prop, then read the cube: " +
                      "four rows, top row is slot 0. Red/green/blue/magenta swatches mean the " +
                      "protocol decoded; white means an ordinary light took the slot; dark grey " +
                      "means the slot is empty.");
        }

        [MenuItem("AvatarBridge/Spike/Build protocol lights only (park on an avatar)")]
        static void BuildLightsOnly()
        {
            var root = new GameObject("SPS Protocol Lights");
            Undo.RegisterCreatedObjectUndo(root, "Build SPS protocol lights");
            for (int i = 0; i < Protocol.Length; i++)
            {
                MakeLight(root.transform, Protocol[i].name, Protocol[i].range,
                    new Vector3(0f, i * 0.05f, 0f));
            }
            Selection.activeGameObject = root;
            Debug.Log("[SpsSpike] Built protocol lights. Park under a bone, upload the avatar, " +
                      "and read a probe cube spawned as a separate prop.");
        }

        static Material ProbeMaterial()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[SpsSpike] Shader \"{ShaderName}\" not found. " +
                               "Copy SpsLightProbe.shader into the project first.");
                return null;
            }
            var material = new Material(shader) { name = "SPS Light Probe" };
            string dir = "Assets/SpsSpike";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets", "SpsSpike");
            }
            string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/SPS Light Probe.mat");
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void MakeLight(Transform parent, string name, float range, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            // The protocol is a black light: it carries position, never
            // illumination, and the decoder uses colour to tell protocol
            // lights from somebody's actual lighting.
            light.color = Color.black;
            light.intensity = 1f;
            light.bounceIntensity = 0f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForceVertex;
            // Everything, so a layer mismatch cannot be mistaken for the
            // client refusing to populate the slots.
            light.cullingMask = ~0;
        }
    }
}
#endif
