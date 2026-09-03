// One object on the avatar, carrying the named screen grab.
//
// Deliberately not spawned on the chance it gets used: a grab is a full
// screen copy per camera, and ChilloutVR renders mirrors into their own
// cameras. It goes on only when a patched plug shader declares that it
// reads the atlas, which is the caller's test to make.
//
// No VRChat types here on purpose, so the standalone toolkit can call it.
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarBridge
{
    public static class YapsAtlasGrab
    {
        public const string ObjectName = "YAPS Atlas Grab";
        const string ShaderName = "YAPS/Atlas Grab";

        // Unity culls on renderer bounds and a culled object never grabs.
        // The grab has to happen while the wearer is behind the camera,
        // because the plug reading it belongs to somebody else.
        const float BoundsSize = 1000f;

        public static GameObject Add(Transform root, string outputDir)
        {
            var shader = Shader.Find(ShaderName);
            if (root == null || shader == null)
            {
                return null;
            }
            var existing = root.Find(ObjectName);
            if (existing != null)
            {
                return existing.gameObject;
            }

            Directory.CreateDirectory(outputDir);
            var host = new GameObject(ObjectName);
            host.transform.SetParent(root, false);
            host.AddComponent<MeshFilter>().sharedMesh = GrabMesh(outputDir);

            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GrabMaterial(shader, outputDir);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return host;
        }

        static Mesh GrabMesh(string outputDir)
        {
            string path = outputDir + "/YAPS atlas grab.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                return existing;
            }

            // One triangle a millimetre across. The pass writes no colour and
            // no depth, so the draw exists only to make the grab run.
            var made = new Mesh { name = "YAPS Atlas Grab" };
            made.vertices = new[]
            {
                new Vector3(-0.0005f, 0f, 0f),
                new Vector3(0.0005f, 0f, 0f),
                new Vector3(0f, 0.001f, 0f)
            };
            made.triangles = new[] { 0, 1, 2 };
            // Last: assigning vertices recomputes bounds.
            made.bounds = new Bounds(Vector3.zero, Vector3.one * BoundsSize);
            AssetDatabase.CreateAsset(made, path);
            return made;
        }

        static Material GrabMaterial(Shader shader, string outputDir)
        {
            string path = outputDir + "/YAPS atlas grab.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = shader;
                return existing;
            }
            var made = new Material(shader) { name = "YAPS Atlas Grab" };
            AssetDatabase.CreateAsset(made, path);
            return made;
        }
    }
}
