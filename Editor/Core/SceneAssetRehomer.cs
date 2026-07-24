#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    /// <summary>
    /// VRCFury bakes generated meshes (SPS-deformed bodies, blendshape/mesh-optimizer output,
    /// merged armatures…) as sub-assets under Packages/com.vrcfury.temp, and it deletes that
    /// folder on its next build. A converted avatar that still points at those temp meshes
    /// loses them — a SkinnedMeshRenderer with a null sharedMesh renders nothing, so the whole
    /// avatar comes back **blank/invisible** (and CVR's inspector throws in GetBlendshapeNames).
    ///
    /// This copies every renderer mesh that lives in the volatile temp into the output folder
    /// and repoints the renderers, making the avatar self-contained. It is the mesh-side
    /// sibling of AnimatorMerger.RehomeVolatileAssets (which does this for clips/masks).
    /// </summary>
    public static class SceneAssetRehomer
    {
        const string Category = "Meshes";

        static bool IsVolatile(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return false;
            }
            string path = AssetDatabase.GetAssetPath(obj);
            return !string.IsNullOrEmpty(path)
                   && path.Replace('\\', '/').StartsWith("Packages/com.vrcfury", StringComparison.OrdinalIgnoreCase);
        }

        public static void Run(BridgeContext ctx)
        {
            var skinned = ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var filters = ctx.Target.GetComponentsInChildren<MeshFilter>(true);

            bool any = false;
            foreach (var smr in skinned) if (IsVolatile(smr.sharedMesh)) { any = true; break; }
            if (!any) foreach (var mf in filters) if (IsVolatile(mf.sharedMesh)) { any = true; break; }
            if (!any)
            {
                return; // nothing baked into temp — meshes are permanent project assets
            }

            string dir = ctx.OutputDir.TrimEnd('/') + "/RehomedMeshes";
            EnsureFolder(dir);

            var map = new Dictionary<Mesh, Mesh>();
            foreach (var smr in skinned)
            {
                var rehomed = RehomeMesh(smr.sharedMesh, dir, map);
                if (rehomed != smr.sharedMesh)
                {
                    smr.sharedMesh = rehomed;
                    EditorUtility.SetDirty(smr);
                }
            }
            foreach (var mf in filters)
            {
                var rehomed = RehomeMesh(mf.sharedMesh, dir, map);
                if (rehomed != mf.sharedMesh)
                {
                    mf.sharedMesh = rehomed;
                    EditorUtility.SetDirty(mf);
                }
            }
            AssetDatabase.SaveAssets();

            ctx.Report.Converted(Category,
                $"Re-homed {map.Count} VRCFury-baked mesh(es) out of temp",
                "These were generated into Packages/com.vrcfury.temp, which Fury deletes on its next build — " +
                "without saved copies the avatar's renderers go null (invisible). Saved to " + dir + ".");
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
            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{SafeName(mesh.name)}.asset");
            AssetDatabase.CreateAsset(copy, path);
            map[mesh] = copy;
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
                return "Mesh";
            }
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                n = n.Replace(c, '_');
            }
            return n;
        }
    }
}
#endif
