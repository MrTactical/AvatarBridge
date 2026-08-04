#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    /// <summary>
    /// VRCFury bakes generated assets — meshes (SPS-deformed bodies, blendshape/mesh-optimizer
    /// output, merged armatures), and materials/shaders (SPS-patched, poiyomi-locked) — under
    /// Packages/com.vrcfury.temp, and it deletes that folder on its next build. A converted
    /// avatar that still points at those temp assets loses them:
    ///   * a null sharedMesh renders nothing → the avatar comes back **blank/invisible**;
    ///   * a null material/shader renders **pink** and drops out of the scene.
    ///
    /// This copies every renderer mesh, material and (generated) shader that lives in the
    /// volatile temp into the output folder and repoints the renderers, making the avatar
    /// self-contained. Mesh-side sibling of AnimatorMerger.RehomeVolatileAssets (clips/masks).
    /// </summary>
    public static class SceneAssetRehomer
    {
        const string Category = "Assets";

        static bool IsVolatile(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return false;
            }
            // Shared with the animator rescue: covers Fury's own temp AND NDMF's __Generated,
            // where Fury bakes the moment Modular Avatar is installed. A mesh referenced from
            // either dies on the next play mode's bake — the avatar goes invisible rather than
            // frozen, but by the same mechanism.
            return AnimatorMerger.IsDoomedGeneratedPath(AssetDatabase.GetAssetPath(obj));
        }

        public static void Run(BridgeContext ctx)
        {
            var skinned = ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var filters = ctx.Target.GetComponentsInChildren<MeshFilter>(true);
            var renderers = ctx.Target.GetComponentsInChildren<Renderer>(true);

            if (!AnyVolatile(skinned, filters, renderers))
            {
                return; // everything is a permanent project asset — nothing to rescue
            }

            string dir = ctx.OutputDir.TrimEnd('/') + "/RehomedAssets";
            EnsureFolder(dir);

            var meshMap = new Dictionary<Mesh, Mesh>();
            var matMap = new Dictionary<Material, Material>();
            var shaderMap = new Dictionary<Shader, Shader>();
            var textureMap = new Dictionary<Texture, Texture>();
            // Per conversion, never carried over: the paths in it point into THIS avatar's output
            // folder, and reusing them on the next avatar would hand it the previous one's pictures.
            containerCopies.Clear();

            foreach (var smr in skinned)
            {
                var m = RehomeMesh(smr.sharedMesh, dir, meshMap);
                if (m != smr.sharedMesh) { smr.sharedMesh = m; EditorUtility.SetDirty(smr); }
            }
            foreach (var mf in filters)
            {
                var m = RehomeMesh(mf.sharedMesh, dir, meshMap);
                if (m != mf.sharedMesh) { mf.sharedMesh = m; EditorUtility.SetDirty(mf); }
            }
            foreach (var r in renderers)
            {
                RehomeMaterials(r, dir, matMap, shaderMap, textureMap);
            }
            AssetDatabase.SaveAssets();

            ctx.Report.Converted(Category,
                $"Re-homed {meshMap.Count} mesh(es), {matMap.Count} material(s), {shaderMap.Count} shader(s), " +
                $"{textureMap.Count} texture(s) out of temp",
                "VRCFury generated these into Packages/com.vrcfury.temp, which it deletes on its next build — " +
                "without saved copies the avatar goes invisible (null mesh), pink (null material/shader) or " +
                "loses the pictures off its materials while everything else still looks right (null texture, " +
                "which is why particles came back as plain white squares). Saved to " + dir + ".");
        }

        static bool AnyVolatile(SkinnedMeshRenderer[] skinned, MeshFilter[] filters, Renderer[] renderers)
        {
            foreach (var smr in skinned) if (IsVolatile(smr.sharedMesh)) return true;
            foreach (var mf in filters) if (IsVolatile(mf.sharedMesh)) return true;
            foreach (var r in renderers)
            {
                foreach (var mat in r.sharedMaterials)
                {
                    if (IsVolatile(mat) || (mat != null && IsVolatile(mat.shader))) return true;
                }
            }
            return false;
        }

        static Mesh RehomeMesh(Mesh mesh, string dir, Dictionary<Mesh, Mesh> map)
        {
            if (!IsVolatile(mesh))
            {
                return mesh;
            }
            if (map.TryGetValue(mesh, out var existing))
            {
                return existing;
            }
            var copy = UnityEngine.Object.Instantiate(mesh);
            copy.name = mesh.name;
            AssetDatabase.CreateAsset(copy, OutputAssetPaths.Claim($"{dir}/{SafeName(mesh.name)}.asset"));
            map[mesh] = copy;
            return copy;
        }

        static void RehomeMaterials(Renderer r, string dir,
            Dictionary<Material, Material> matMap, Dictionary<Shader, Shader> shaderMap,
            Dictionary<Texture, Texture> textureMap)
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var rehomed = RehomeMaterial(mats[i], dir, matMap, shaderMap, textureMap);
                if (rehomed != mats[i])
                {
                    mats[i] = rehomed;
                    changed = true;
                }
            }
            if (changed)
            {
                r.sharedMaterials = mats;
                EditorUtility.SetDirty(r);
            }
        }

        static Material RehomeMaterial(Material mat, string dir,
            Dictionary<Material, Material> matMap, Dictionary<Shader, Shader> shaderMap,
            Dictionary<Texture, Texture> textureMap)
        {
            if (mat == null || !IsVolatile(mat))
            {
                return mat; // permanent (or null) — leave it alone
            }
            if (matMap.TryGetValue(mat, out var existing))
            {
                return existing;
            }
            var copy = UnityEngine.Object.Instantiate(mat);
            copy.name = mat.name;
            // A generated (SPS/locked) shader lives in temp too — rescue it so the copy isn't pink.
            if (IsVolatile(copy.shader))
            {
                copy.shader = RehomeShader(copy.shader, dir, shaderMap);
            }
            // And so do its TEXTURES, which for a long time they did not. Instantiate carries every
            // texture reference over verbatim, so a material rescued out of the doomed folder kept
            // pointing INTO it — the shader survived and the pictures did not. Reported from game
            // as particles turning from golden stars into plain white squares on every avatar that
            // had any: Fury packs baked textures as sub-assets of one "VRCFury Other.asset", that
            // folder is wiped on its next build, and a particle with no _MainTex draws as an
            // untextured quad. Nothing about it is particle-specific — particles are simply where
            // a missing texture is unmistakable rather than merely wrong.
            RehomeTextures(copy, dir, textureMap);
            AssetDatabase.CreateAsset(copy, OutputAssetPaths.Claim($"{dir}/{SafeName(mat.name)}.mat"));
            matMap[mat] = copy;
            return copy;
        }

        /// <summary>
        /// Repoints every texture the material reads at a copy that will still be there tomorrow.
        ///
        /// Only properties the SHADER declares are walked. A material remembers values for
        /// properties its current shader no longer has, and rescuing those would copy megabytes
        /// of textures nothing samples.
        /// </summary>
        static void RehomeTextures(Material mat, string dir, Dictionary<Texture, Texture> map)
        {
            var shader = mat.shader;
            if (shader == null)
            {
                return;
            }
            for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    continue;
                }
                string property = ShaderUtil.GetPropertyName(shader, i);
                var texture = mat.GetTexture(property);
                if (texture == null || !IsVolatile(texture))
                {
                    continue;
                }
                var rescued = RehomeTexture(texture, dir, map);
                if (rescued != null && rescued != texture)
                {
                    mat.SetTexture(property, rescued);
                }
            }
        }

        /// <summary>
        /// A baked texture is almost never a file of its own — it is one object inside a container
        /// asset holding dozens. So the CONTAINER is copied once, byte for byte, and each texture
        /// is matched to its counterpart inside the copy.
        ///
        /// Deliberately NOT Instantiate + CreateAsset, which is how the meshes above are rescued:
        /// a texture that is not marked readable has no pixels on the CPU to serialize, and that
        /// route would write a blank one — turning "the picture is missing" into "the picture is
        /// there and empty", which looks identical on screen and is harder to notice.
        /// </summary>
        static Texture RehomeTexture(Texture texture, string dir, Dictionary<Texture, Texture> map)
        {
            if (map.TryGetValue(texture, out var already))
            {
                return already;
            }
            string source = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(source))
            {
                map[texture] = texture;   // held only in memory; nothing on disk to copy
                return texture;
            }

            if (!containerCopies.TryGetValue(source, out string copyPath))
            {
                string wanted = $"{dir}/{SafeName(Path.GetFileNameWithoutExtension(source))}" +
                                Path.GetExtension(source);
                string claimed = OutputAssetPaths.Claim(wanted);
                copyPath = AssetDatabase.CopyAsset(source, claimed) ? claimed : null;
                containerCopies[source] = copyPath;
            }
            if (copyPath == null)
            {
                map[texture] = texture;
                return texture;
            }

            var counterpart = Counterpart(texture, source, copyPath) as Texture;
            map[texture] = counterpart ?? texture;
            return map[texture];
        }

        /// <summary>Source container path -> the copy of it, so one container is copied once.</summary>
        static readonly Dictionary<string, string> containerCopies = new Dictionary<string, string>();

        /// <summary>
        /// The same object, one file along. Matched by type and name, and where a container holds
        /// several that share both, by position among them — a byte copy preserves the order, so
        /// this is exact rather than a guess. Returns null when the shapes disagree, and the caller
        /// then leaves the reference alone rather than pointing it somewhere plausible and wrong.
        /// </summary>
        static UnityEngine.Object Counterpart(UnityEngine.Object original, string source, string copyPath)
        {
            int index = 0;
            foreach (var candidate in AssetDatabase.LoadAllAssetsAtPath(source))
            {
                if (candidate == original)
                {
                    break;
                }
                if (Matches(candidate, original))
                {
                    index++;
                }
            }
            foreach (var candidate in AssetDatabase.LoadAllAssetsAtPath(copyPath))
            {
                if (Matches(candidate, original) && index-- == 0)
                {
                    return candidate;
                }
            }
            return null;
        }

        static bool Matches(UnityEngine.Object candidate, UnityEngine.Object original) =>
            candidate != null && candidate.name == original.name
            && candidate.GetType() == original.GetType();

        static Shader RehomeShader(Shader shader, string dir, Dictionary<Shader, Shader> map)
        {
            if (!IsVolatile(shader))
            {
                return shader;
            }
            if (map.TryGetValue(shader, out var existing))
            {
                return existing;
            }
            string src = AssetDatabase.GetAssetPath(shader);
            // Only standalone .shader files can be copied cleanly; leave anything else as-is.
            if (!src.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
            {
                map[shader] = shader;
                return shader;
            }
            string dst = OutputAssetPaths.Claim($"{dir}/{SafeName(shader.name)}.shader");
            Shader copy = AssetDatabase.CopyAsset(src, dst)
                ? AssetDatabase.LoadAssetAtPath<Shader>(dst)
                : shader;
            map[shader] = copy;
            return copy;
        }

        static void EnsureFolder(string dir)
        {
            string abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", dir));
            Directory.CreateDirectory(abs);
            AssetDatabase.Refresh();
        }

        static string SafeName(string n)
        {
            if (string.IsNullOrEmpty(n))
            {
                return "Asset";
            }
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                n = n.Replace(c, '_');
            }
            // Shader names use '/' as category separators — not valid in file names.
            return n.Replace('/', '_');
        }
    }
}
#endif
