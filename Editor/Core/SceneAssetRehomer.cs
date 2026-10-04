#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    // VRCFury bakes generated assets; meshes (SPS-deformed bodies, blendshape/mesh-optimizer
    // output, merged armatures), and materials/shaders (SPS-patched, poiyomi-locked); under
    // Packages/com.vrcfury.temp, and it deletes that folder on its next build. A converted
    // avatar that still points at those temp assets loses them:
    //   * a null sharedMesh renders nothing -> the avatar comes back **blank/invisible**;
    //   * a null material/shader renders **pink** and drops out of the scene.
    //
    // This copies every renderer mesh, material and (generated) shader that lives in the
    // volatile temp into the output folder and repoints the renderers, making the avatar
    // self-contained. Mesh-side sibling of AnimatorMerger.RehomeVolatileAssets (clips/masks).
    public static class SceneAssetRehomer
    {
        const string Category = "Assets";

        // One conversion's copies, so a temp object named from several places comes out as one
        // copy. Two gave "Body 1.mat" beside "Body.mat", and a swap restored an object the mesh
        // was not wearing.
        sealed class Rescued
        {
            internal readonly Dictionary<UnityEngine.Object, UnityEngine.Object> Copies =
                new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            // Source container path to its copy. Never carried over: the paths point into THIS
            // avatar's output folder, and reusing them on the next avatar would hand it the
            // previous one's pictures.
            internal readonly Dictionary<string, string> Containers = new Dictionary<string, string>();
        }

        static readonly ConditionalWeakTable<BridgeContext, Rescued> PerConversion =
            new ConditionalWeakTable<BridgeContext, Rescued>();

        static Rescued StateOf(BridgeContext ctx) => PerConversion.GetValue(ctx, _ => new Rescued());

        static bool IsVolatile(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return false;
            }
            // Shared with the animator rescue: covers Fury's own temp AND NDMF's __Generated,
            // where Fury bakes the moment Modular Avatar is installed. A mesh referenced from
            // either dies on the next play mode's bake; the avatar goes invisible rather than
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
                return; // everything is a permanent project asset: nothing to rescue
            }

            string dir = ctx.OutputDir.TrimEnd('/') + "/RehomedAssets";
            OutputAssetPaths.EnsureFolder(dir);

            foreach (var smr in skinned)
            {
                var m = (Mesh)Rehome(ctx, smr.sharedMesh);
                if (m != smr.sharedMesh) { smr.sharedMesh = m; EditorUtility.SetDirty(smr); }
            }
            foreach (var mf in filters)
            {
                var m = (Mesh)Rehome(ctx, mf.sharedMesh);
                if (m != mf.sharedMesh) { mf.sharedMesh = m; EditorUtility.SetDirty(mf); }
            }
            foreach (var r in renderers)
            {
                RehomeMaterials(ctx, r);
            }
            AssetDatabase.SaveAssets();

            int meshes = 0, materials = 0, shaders = 0, textures = 0;
            foreach (var pair in StateOf(ctx).Copies)
            {
                if (pair.Key == pair.Value) continue;   // left in place, nothing copied
                if (pair.Key is Mesh) meshes++;
                else if (pair.Key is Material) materials++;
                else if (pair.Key is Shader) shaders++;
                else if (pair.Key is Texture) textures++;
            }
            ctx.Report.Converted(Category,
                $"Re-homed {meshes} mesh(es), {materials} material(s), {shaders} shader(s), " +
                $"{textures} texture(s) out of temp",
                "VRCFury deletes Packages/com.vrcfury.temp on its next build, leaving the avatar invisible, " +
                "pink or missing textures. Saved to " + dir + ".");
        }

        // The four kinds a renderer wears, through the same map, so a rescue of swap-clip
        // references can reuse the renderers' copies instead of making its own. False for any
        // other kind, which the caller copies its own way.
        internal static bool TryRehome(BridgeContext ctx, UnityEngine.Object value, out UnityEngine.Object copy)
        {
            copy = value;
            if (!(value is Mesh || value is Material || value is Shader || value is Texture))
            {
                return false;
            }
            copy = Rehome(ctx, value);
            return true;
        }

        static UnityEngine.Object Rehome(BridgeContext ctx, UnityEngine.Object value)
        {
            if (!IsVolatile(value))
            {
                return value; // permanent (or null): leave it alone
            }
            var copies = StateOf(ctx).Copies;
            if (copies.TryGetValue(value, out var known))
            {
                return known;
            }
            string dir = ctx.OutputDir.TrimEnd('/') + "/RehomedAssets";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                OutputAssetPaths.EnsureFolder(dir);
            }
            UnityEngine.Object copy;
            switch (value)
            {
                case Mesh mesh: copy = CopyMesh(mesh, dir); break;
                case Material material: copy = CopyMaterial(ctx, material, dir); break;
                case Shader shader: copy = CopyShader(shader, dir); break;
                case Texture texture: copy = CopyTexture(ctx, texture, dir); break;
                default: return value;
            }
            copies[value] = copy;
            return copy;
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

        static Mesh CopyMesh(Mesh mesh, string dir)
        {
            var copy = UnityEngine.Object.Instantiate(mesh);
            copy.name = mesh.name;
            AssetDatabase.CreateAsset(copy, OutputAssetPaths.Claim($"{dir}/{OutputAssetPaths.SafeFileName(mesh.name)}.asset"));
            return copy;
        }

        static void RehomeMaterials(BridgeContext ctx, Renderer r)
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var rehomed = (Material)Rehome(ctx, mats[i]);
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

        static Material CopyMaterial(BridgeContext ctx, Material mat, string dir)
        {
            var copy = UnityEngine.Object.Instantiate(mat);
            copy.name = mat.name;
            // A generated (SPS/locked) shader lives in temp too; rescue it so the copy isn't pink.
            // Before the textures: assigning a shader can drop properties, and doing it after
            // would undo them.
            if (Rehome(ctx, copy.shader) is Shader shader && shader != copy.shader)
            {
                copy.shader = shader;
            }
            // And so do its TEXTURES, which for a long time they did not. Instantiate carries every
            // texture reference over verbatim, so a material rescued out of the doomed folder kept
            // pointing INTO it; the shader survived and the pictures did not. Reported from game
            // as particles turning from golden stars into plain white squares on every avatar that
            // had any: Fury packs baked textures as sub-assets of one "VRCFury Other.asset", that
            // folder is wiped on its next build, and a particle with no _MainTex draws as an
            // untextured quad. Nothing about it is particle-specific; particles are simply where
            // a missing texture is unmistakable rather than merely wrong.
            RehomeTextures(ctx, copy);
            AssetDatabase.CreateAsset(copy, OutputAssetPaths.Claim($"{dir}/{OutputAssetPaths.SafeFileName(mat.name)}.mat"));
            return copy;
        }

        static void RehomeTextures(BridgeContext ctx, Material mat)
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
                if (Rehome(ctx, texture) is Texture rescued && rescued != texture)
                {
                    mat.SetTexture(property, rescued);
                }
            }
        }

        // Fury packs its baked textures as sub-assets of one container, so the container is
        // copied once and each texture's counterpart found in the copy.
        static Texture CopyTexture(BridgeContext ctx, Texture texture, string dir)
        {
            string source = AssetDatabase.GetAssetPath(texture);
            var containers = StateOf(ctx).Containers;
            if (!containers.TryGetValue(source, out string copyPath))
            {
                string wanted = $"{dir}/{OutputAssetPaths.SafeFileName(Path.GetFileNameWithoutExtension(source))}" +
                                Path.GetExtension(source);
                string claimed = OutputAssetPaths.Claim(wanted);
                copyPath = AssetDatabase.CopyAsset(source, claimed) ? claimed : null;
                containers[source] = copyPath;
            }
            if (copyPath == null)
            {
                return texture;
            }
            return Counterpart(texture, source, copyPath) as Texture ?? texture;
        }

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

        // Only a shader that is a file of its own can be copied cleanly; one embedded in another
        // asset is left where it is.
        static Shader CopyShader(Shader shader, string dir)
        {
            if (!AssetDatabase.IsMainAsset(shader))
            {
                return shader;
            }
            string src = AssetDatabase.GetAssetPath(shader);
            string dst = OutputAssetPaths.Claim($"{dir}/{OutputAssetPaths.SafeFileName(shader.name)}{Path.GetExtension(src)}");
            return AssetDatabase.CopyAsset(src, dst)
                ? AssetDatabase.LoadAssetAtPath<Shader>(dst) ?? shader
                : shader;
        }
    }
}
#endif
