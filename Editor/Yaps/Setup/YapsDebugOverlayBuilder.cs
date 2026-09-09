// The in-game readout for a plug that will not behave.
//
// A quad beside the plug, painted by our own shader, saying who resolved
// the socket and what the atlas saw. The plug's own debug view answers in
// LENGTH and straightens the plug to do it, so the bend and the reason
// for the bend can never be read at the same time. This leaves the plug
// alone and reports beside it.
//
// Built only when the plug asks for it, and the scanner refuses an upload
// while one exists.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.IO;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarBridge
{
    public static class YapsDebugOverlayBuilder
    {
        const string Category = "YAPS penetration system";
        public const string ObjectName = "YAPS Debug Overlay";
        const string ShaderName = "YAPS/Debug Overlay";
        const string Folder = "Assets/YAPS/Generated";

        // The toolkit's entry: the plug component owns the tickbox.
        public static void Apply(YapsPlug plug, YapsBaker.Result result, Material patched,
            BridgeReport report)
        {
            if (plug == null)
            {
                return;
            }
            Apply(plug.transform, plug.name, plug.debugOverlay, result, patched, report);
        }

        // Build, refresh or remove. Safe to run again: the object is found by
        // name and replaced.
        //
        // TAKES THE ROOT AND THE FLAG RATHER THAN THE COMPONENT, because the
        // converter never has one. A converted avatar's plug is baked straight
        // from YapsBaker with no YapsPlug anywhere, so a version of this that
        // needed the component would have built readouts for the toolkit alone
        // and left every converted plug, which is most of them, with nothing.
        public static void Apply(Transform plugRoot, string label, bool wanted,
            YapsBaker.Result result, Material patched, BridgeReport report)
        {
            if (plugRoot == null)
            {
                return;
            }
            Transform parent = result != null && result.Root != null ? result.Root : plugRoot;
            var existing = parent.Find(ObjectName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }
            if (!wanted)
            {
                return;
            }

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                report?.Warning(Category, label,
                    "The debug overlay was asked for and its shader could not be found, so no " +
                    "readout was built. The plug itself is unaffected.");
                return;
            }

            var host = new GameObject(ObjectName);
            host.transform.SetParent(parent, false);
            // The frame the BAKE measured, not the bone's own axes. A bone
            // points wherever the rigger pointed it, and a readout resolving
            // from a different frame than the deform is a readout that lies
            // about the deform.
            if (result != null)
            {
                host.transform.SetPositionAndRotation(result.Origin, result.Rotation);
            }
            host.transform.localScale = Vector3.one * LocalScaleFor(parent, result, label, report);

            host.AddComponent<MeshFilter>().sharedMesh = Quad();
            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialFor(shader, label);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            Copy(patched, renderer.sharedMaterial, label, report);

            report?.Converted(Category, label,
                "A debug readout was built beside this plug. Six cells, left to right: who " +
                "resolved the socket, whether it engaged, how far away it is, what the screen " +
                "atlas read, whether the atlas is on the camera drawing this view, and whether " +
                "this plug asks for the atlas at all. Grey or black is nothing, red is a fault, " +
                "and cyan, amber or green in the first cell is the contact channel, a marker " +
                "light or the screen atlas answering. It is visible to everyone who can see the " +
                "plug, and an upload is refused while it exists: untick it when you are done.");
        }

        // The plug's world scale, so the readout's ranges are the plug's.
        //
        // The deform takes its scale from the RENDERER's matrix and its frame
        // from the bone, and those are two different transforms. The quad
        // hangs off the bone, so its own scale has to be corrected back to
        // the renderer's or every distance in the readout is wrong by the
        // ratio between them.
        static float LocalScaleFor(Transform parent, YapsBaker.Result result, string label,
            BridgeReport report)
        {
            if (result == null || result.Renderer == null || parent == null)
            {
                return 1f;
            }
            Vector3 parentScale = parent.lossyScale;
            float want = result.Renderer.transform.lossyScale.z;
            float have = parentScale.z;
            if (Mathf.Abs(parentScale.x - parentScale.z) > 1e-4f
                || Mathf.Abs(parentScale.y - parentScale.z) > 1e-4f)
            {
                report?.Warning(Category, label,
                    "The bone this plug's readout hangs from is scaled unevenly, so the distances " +
                    "the readout shows are approximate. Which tier answered is still exact; the " +
                    "gap bar is the only cell affected.");
            }
            return Mathf.Abs(have) > 1e-6f ? want / have : 1f;
        }

        // EVERY _YAPS_ VALUE THE PLUG HAS, copied by name rather than by a
        // list kept here. A hand-written list rots the first time a property
        // is added to the patcher's block, and the cell it feeds then reads
        // zero without saying so. Anything the overlay shader cannot hold is
        // reported, so the drift is loud.
        static void Copy(Material from, Material to, string label, BridgeReport report)
        {
            if (from == null || to == null)
            {
                return;
            }
            var missing = new List<string>();
            int count = ShaderUtil.GetPropertyCount(from.shader);
            for (int i = 0; i < count; i++)
            {
                string name = ShaderUtil.GetPropertyName(from.shader, i);
                if (!name.StartsWith("_YAPS_", System.StringComparison.Ordinal))
                {
                    continue;
                }
                var kind = ShaderUtil.GetPropertyType(from.shader, i);
                if (!to.HasProperty(name))
                {
                    // The bake texture and the knobs that only shape the mesh
                    // are not read by the readout, so their absence is normal.
                    if (kind == ShaderUtil.ShaderPropertyType.Float
                        || kind == ShaderUtil.ShaderPropertyType.Range
                        || kind == ShaderUtil.ShaderPropertyType.Vector)
                    {
                        missing.Add(name);
                    }
                    continue;
                }
                switch (kind)
                {
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        to.SetFloat(name, from.GetFloat(name));
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        to.SetVector(name, from.GetVector(name));
                        break;
                    case ShaderUtil.ShaderPropertyType.Color:
                        to.SetColor(name, from.GetColor(name));
                        break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        to.SetTexture(name, from.GetTexture(name));
                        break;
                }
            }
            EditorUtility.SetDirty(to);
            if (missing.Count == 0)
            {
                return;
            }
            report?.Warning(Category, label,
                $"The debug readout could not carry {missing.Count} of the plug's setting(s): " +
                string.Join(", ", missing) + ". Cells that depend on them read as nothing rather " +
                "than as their real value. The readout's shader needs the same property added.");
        }

        // A unit quad. Its vertices are never seen where they are put: the
        // shader billboards them in view space, so only the UVs matter.
        //
        // SAVED AS AN ASSET, and that is the whole reason this is not two
        // lines. A mesh built in memory and handed to a MeshFilter draws
        // perfectly in the editor and serialises to nothing, so the readout
        // appeared in the Scene view and was absent in game, which reads as
        // the shader failing rather than the mesh never arriving.
        static Mesh Quad()
        {
            string path = Folder + "/YAPS Debug Overlay Quad.asset";
            var have = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (have != null && have.vertexCount == 4)
            {
                return have;
            }

            var mesh = new Mesh { name = "YAPS Debug Overlay Quad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f),
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            // Never culled by distance or by facing: the quad is drawn where
            // the view matrix puts it, not where these bounds say it is.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
            Directory.CreateDirectory(Folder);
            if (have != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Material MaterialFor(Shader shader, string label)
        {
            string path = Folder + "/YAPS Debug Overlay " + Safe(label) + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = shader;
                return existing;
            }
            Directory.CreateDirectory(Folder);
            var made = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(made, path);
            return made;
        }

        static string Safe(string name)
        {
            foreach (char bad in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(bad, '_');
            }
            return name;
        }
    }
}
#endif
