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
using System;
using System.Collections.Generic;
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
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
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

        // The unambiguous cross-content test. A cube carrying no lights of
        // its own can only ever show somebody else's, so any row that is
        // not dark grey is proof that one piece of content sees another's
        // vertex lights. Everything up to here could be explained by a
        // rig lighting itself.
        [MenuItem("AvatarBridge/Spike/Build probe cube only (no lights, pure receiver)")]
        static void BuildReceiverOnly()
        {
            var root = new GameObject("SPS Light Probe (receiver only)");
            Undo.RegisterCreatedObjectUndo(root, "Build SPS probe receiver");

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Probe Cube";
            cube.transform.SetParent(root.transform, false);
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
            cube.GetComponent<MeshRenderer>().sharedMaterial = ProbeMaterial();

            Selection.activeGameObject = root;
            Debug.Log("[SpsSpike] Built a receiver-only probe. It has no lights, so every " +
                      "coloured row it shows came from somewhere else. Pair it with the " +
                      "lights-only rig on an avatar.");
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

        // The one topology never tested: shader on avatar A reading lights
        // on avatar B. Everything so far has been avatar-to-prop, and props
        // and avatars sit on different layers, so this is not a formality.
        // Upload it, share it, and have the other person wear it while you
        // wear the lights.
        [MenuItem("AvatarBridge/Spike/Build probe AVATAR (wearable, no lights)")]
        static void BuildProbeAvatar()
        {
            var root = new GameObject("SPS Probe Avatar");
            Undo.RegisterCreatedObjectUndo(root, "Build SPS probe avatar");

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Probe Cube";
            cube.transform.SetParent(root.transform, false);
            cube.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            cube.transform.localScale = Vector3.one * 0.6f;
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
            cube.GetComponent<MeshRenderer>().sharedMaterial = ProbeMaterial();

            var avatarType = FindType("ABI.CCK.Components.CVRAvatar");
            if (avatarType != null)
            {
                var avatar = root.AddComponent(avatarType);
                var viewpoint = avatarType.GetField("viewPosition");
                viewpoint?.SetValue(avatar, new Vector3(0f, 0.9f, 0.15f));
                var voice = avatarType.GetField("voicePosition");
                voice?.SetValue(avatar, new Vector3(0f, 0.85f, 0.15f));
            }
            else
            {
                Debug.LogWarning("[SpsSpike] CVRAvatar type not found — add the component by hand " +
                                 "before uploading.");
            }

            Selection.activeGameObject = root;
            Debug.Log("[SpsSpike] Built a wearable probe avatar with no lights of its own. " +
                      "Upload it, share it with your tester, and have them wear it while you " +
                      "wear the protocol lights. Any colour it shows is avatar-to-avatar.");
        }

        // A real partner carries a dozen sockets, so twenty four lights
        // contend for four slots. This asks the only question that matters
        // for pairing: when there are far too many, does the nearest pair
        // win, and do a root and its front light arrive together?
        [MenuItem("AvatarBridge/Spike/Build socket stress lights (12 sockets, 24 lights)")]
        static void BuildStressLights()
        {
            var root = new GameObject("SPS Protocol Lights (stress, 12 sockets)");
            Undo.RegisterCreatedObjectUndo(root, "Build SPS stress lights");

            for (int i = 0; i < 12; i++)
            {
                // Spread over a torso-sized volume so distance ordering is
                // meaningful rather than every socket sitting on one spot.
                float angle = i * Mathf.PI * 2f / 12f;
                var at = new Vector3(Mathf.Cos(angle) * 0.25f,
                                     0.1f + i * 0.04f,
                                     Mathf.Sin(angle) * 0.25f);
                var socket = new GameObject($"Socket {i:00}");
                socket.transform.SetParent(root.transform, false);
                socket.transform.localPosition = at;

                // Matches what the VRCFury bake actually emits: root ring or
                // hole, front 1 cm along +Z.
                MakeLight(socket.transform, "Root", (i % 2 == 0) ? 0.4106f : 0.4206f, Vector3.zero);
                MakeLight(socket.transform, "Front", 0.4506f, Vector3.forward * 0.01f);
            }

            Selection.activeGameObject = root;
            Debug.Log("[SpsSpike] Built 12 sockets / 24 protocol lights. Park on an avatar. " +
                      "Watch whether the probe's four slots hold a matched root+front pair " +
                      "for the nearest socket, or a scatter across several.");
        }

        // S1. Builds a mesh of little cubes whose only job is to be moved,
        // in the vertex shader, onto each player's hip, chest and head as
        // reported by the globals. Spawn it as a prop and look at people:
        // the markers should sit on their bodies and stay there.
        [MenuItem("AvatarBridge/Spike/Build GLOBALS probe (player position markers)")]
        static void BuildGlobalsProbe()
        {
            const int players = 8;
            const int channels = 3;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // One unit cube per (player, channel). Every cube is identical
            // and centred on the origin; the shader supplies the position.
            Vector3[] corners =
            {
                new Vector3(-1, -1, -1), new Vector3(1, -1, -1),
                new Vector3(1, 1, -1),   new Vector3(-1, 1, -1),
                new Vector3(-1, -1, 1),  new Vector3(1, -1, 1),
                new Vector3(1, 1, 1),    new Vector3(-1, 1, 1),
            };
            int[] cubeTris =
            {
                0,2,1, 0,3,2,  1,2,6, 1,6,5,  5,6,7, 5,7,4,
                4,7,3, 4,3,0,  3,7,6, 3,6,2,  4,0,1, 4,1,5,
            };

            for (int p = 0; p < players; p++)
            {
                for (int c = 0; c < channels; c++)
                {
                    int baseIndex = verts.Count;
                    foreach (var corner in corners)
                    {
                        verts.Add(corner * 0.5f);
                        uvs.Add(new Vector2(p, c));
                    }
                    foreach (int t in cubeTris)
                    {
                        tris.Add(baseIndex + t);
                    }
                }
            }

            var mesh = new Mesh { name = "SPS Globals Markers" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            // The shader throws these anywhere in the world, so a tight
            // bounds would let Unity cull the whole thing off-screen.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);

            string dir = "Assets/SpsSpike";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets", "SpsSpike");
            }
            AssetDatabase.CreateAsset(mesh,
                AssetDatabase.GenerateUniqueAssetPath(dir + "/SPS Globals Markers.asset"));

            var shader = Shader.Find("AvatarBridge/SPS Globals Probe");
            if (shader == null)
            {
                Debug.LogError("[SpsSpike] Globals probe shader not found.");
                return;
            }
            var material = new Material(shader) { name = "SPS Globals Probe" };
            AssetDatabase.CreateAsset(material,
                AssetDatabase.GenerateUniqueAssetPath(dir + "/SPS Globals Probe.mat"));

            var root = new GameObject("SPS Globals Probe");
            Undo.RegisterCreatedObjectUndo(root, "Build SPS globals probe");
            var filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            Selection.activeGameObject = root;
            Debug.Log("[SpsSpike] Built the globals probe. Spawn it as a prop and look at " +
                      "people: red marks hips, green chests, blue heads, and the bright set " +
                      "is player index 0, which should be you. Check a mirror too — these " +
                      "are global uniforms, so the mirror must agree with the direct view.");
        }

        // The stress failure was structural: the DPS protocol puts the
        // front light at a longer range than its own root, and Unity ranks
        // vertex lights by range, so fronts evict roots. For our own
        // avatars we are free to invert the ordering. Same decode, second
        // decimal, just remapped.
        [MenuItem("AvatarBridge/Spike/Build INVERTED-encoding stress lights (roots win)")]
        static void BuildInvertedStressLights()
        {
            var root = new GameObject("SPS Protocol Lights (inverted, 12 sockets)");
            Undo.RegisterCreatedObjectUndo(root, "Build SPS inverted stress lights");

            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI * 2f / 12f;
                var at = new Vector3(Mathf.Cos(angle) * 0.25f,
                                     0.1f + i * 0.04f,
                                     Mathf.Sin(angle) * 0.25f);
                var socket = new GameObject($"Socket {i:00}");
                socket.transform.SetParent(root.transform, false);
                socket.transform.localPosition = at;

                // Roots now outrange fronts, so they should hold the slots.
                MakeLight(socket.transform, "Root", (i % 2 == 0) ? 0.4906f : 0.4806f, Vector3.zero);
                MakeLight(socket.transform, "Front", 0.4106f, Vector3.forward * 0.01f);
            }

            Selection.activeGameObject = root;
            Debug.Log("[SpsSpike] Inverted encoding: roots at 0.49/0.48, fronts at 0.41. " +
                      "Compare against the normal stress rig — the slots should now hold " +
                      "roots (magenta/cyan) instead of filling with fronts.");
        }

        static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = assembly.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
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
