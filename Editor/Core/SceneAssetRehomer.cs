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
                RehomeMaterials(r, dir, matMap, shaderMap);
            }
            AssetDatabase.SaveAssets();

            ctx.Report.Converted(Category,
                $"Re-homed {meshMap.Count} mesh(es), {matMap.Count} material(s), {shaderMap.Count} shader(s) out of temp",
                "VRCFury generated these into Packages/com.vrcfury.temp, which it deletes on its next build — " +
                "without saved copies the avatar goes invisible (null mesh) or pink (null material/shader). " +
                "Saved to " + dir + ".");
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
            Dictionary<Material, Material> matMap, Dictionary<Shader, Shader> shaderMap)
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var rehomed = RehomeMaterial(mats[i], dir, matMap, shaderMap);
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
            Dictionary<Material, Material> matMap, Dictionary<Shader, Shader> shaderMap)
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
            AssetDatabase.CreateAsset(copy, OutputAssetPaths.Claim($"{dir}/{SafeName(mat.name)}.mat"));
            matMap[mat] = copy;
            return copy;
        }

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
