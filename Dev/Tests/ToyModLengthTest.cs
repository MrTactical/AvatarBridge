#if CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Replays the toy mod's own measurement on a plug announced under a 100x
    // armature with a body mesh above it: climb from the tip light to the
    // first object with a mesh beneath, length is the furthest +Z vertex times
    // that object's forward scale, distance is from that object's position.
    public static class ToyModLengthTest
    {
        static int failures;

        [MenuItem("Tools/AvatarBridge Dev/Test: toy mod length")]
        public static void Run()
        {
            failures = 0;
            var generated = GeneratedFiles.Snapshot();
            var avatar = new GameObject("ToyLengthAvatar");
            try
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.transform.SetParent(avatar.transform, false);
                var armature = new GameObject("Armature").transform;
                armature.SetParent(avatar.transform, false);
                armature.localScale = Vector3.one * 100f;
                var plug = new GameObject("Plug").transform;
                plug.SetParent(armature, false);
                plug.localPosition = new Vector3(0f, 0.01f, 0f);
                var origin = plug.position;

                YapsNativeBuilder.AnnouncePlug(plug, origin, Quaternion.identity, 0.15f, 0.02f, true, true, YapsNativeBuilder.OutputRoot + "/ToyLengthTest");
                var light = plug.GetComponentsInChildren<Light>(true).FirstOrDefault();
                Check(light != null, "the plug has a tip light");
                if (light != null)
                {
                    var (meshObject, length) = AsTheModSees(light);
                    Check(meshObject != null && meshObject.name == "Length for toy mods", $"the mod finds the length mesh (found {meshObject?.name})");
                    Check(Mathf.Abs(length - 0.15f) < 0.001f, $"the mod reads 0.15 m (read {length:0.####})");
                    Check(meshObject != null && Vector3.Distance(meshObject.transform.position, origin) < 0.001f, "and measures from the base");
                }
                var mesh = plug.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).FirstOrDefault();
                string path = mesh != null ? AssetDatabase.GetAssetPath(mesh) : null;
                Check(!string.IsNullOrEmpty(path), "the mesh is a saved asset, so the upload keeps it");

                YapsNativeBuilder.AnnouncePlug(plug, origin, Quaternion.identity, 0.15f, 0.02f, true, true);
                Check(plug.GetComponentsInChildren<MeshFilter>(true).Length == 0, "switched off, the mesh goes");
                Check(string.IsNullOrEmpty(path) || AssetDatabase.LoadAssetAtPath<Mesh>(path) == null, "and so does its asset");
            }
            finally
            {
                Object.DestroyImmediate(avatar);
                GeneratedFiles.DeleteNew(generated);
            }
            Debug.Log(failures == 0 ? "[ToyModLengthTest] PASS" : $"[ToyModLengthTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static (GameObject, float) AsTheModSees(Light light)
        {
            GameObject root;
            for (root = light.gameObject; root != null && root.GetComponentInChildren<MeshRenderer>(true) == null
                 && root.GetComponentInChildren<SkinnedMeshRenderer>(true) == null; root = root.transform.parent?.gameObject) ;
            var renderer = root != null ? root.GetComponentInChildren<MeshRenderer>(true) : null;
            var mesh = renderer != null ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (mesh == null) return (null, 0f);
            float z = mesh.vertices.Max(v => v.z);
            return (renderer.gameObject, z * renderer.transform.TransformVector(Vector3.forward).magnitude);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[ToyModLengthTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
