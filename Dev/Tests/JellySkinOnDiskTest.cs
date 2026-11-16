#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // The jelly's helper meshes reach disk filled. Created first and filled after, a build that
    // stopped in between left the asset empty, and the client could not clone the avatar.
    public static class JellySkinOnDiskTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Jelly helper meshes reach disk filled")]
        public static void Run()
        {
            failures = 0;
            var generated = GeneratedFiles.Snapshot();
            try
            {
                string folder = YapsNativeBuilder.OutputRoot + "/JellySkinOnDiskTest";
                YapsNativeBuilder.EnsureFolderPublic(folder);
                string path = folder + "/Jelly_Skin.asset";
                var points = Enumerable.Range(0, 16).Select(i => new Vector3(i, 0, 0)).ToArray();

                // A build that throws while filling leaves nothing behind, never an empty mesh.
                try { YapsJelly.FilledMesh(path, m => throw new System.InvalidOperationException("stopped")); }
                catch (System.InvalidOperationException) { }
                Check(!File.Exists(path), "a fill that throws writes no asset");

                YapsJelly.FilledMesh(path, m => { m.vertices = points; m.triangles = Enumerable.Range(0, 48).Select(i => i / 3).ToArray(); });
                Check(File.ReadAllText(path).Contains("m_VertexCount: 16"), "a new mesh is on disk with its 16 vertices when it returns");

                var kept = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                var again = YapsJelly.FilledMesh(path, m => { m.vertices = points.Take(8).ToArray(); m.triangles = Enumerable.Range(0, 24).Select(i => i / 3).ToArray(); });
                Check(ReferenceEquals(kept, again), "an existing mesh is kept in place");
                Check(File.ReadAllText(path).Contains("m_VertexCount: 8"), "a refill is on disk when it returns");
            }
            finally
            {
                GeneratedFiles.DeleteNew(generated);
            }
            Debug.Log(failures == 0 ? "[JellySkinOnDiskTest] PASS" : $"[JellySkinOnDiskTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[JellySkinOnDiskTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
