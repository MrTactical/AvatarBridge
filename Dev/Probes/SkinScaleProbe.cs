#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Does a skinned vertex carry the avatar's scale? unity_ObjectToWorld is identity on a skinned
    // plug and the frame recovery normalises, so a scaled avatar keeps its baked plug length. If
    // skinning leaves the normal and tangent unnormalised, their length is the scale, per vertex.
    // Hidden/SkinScaleCapture records what the vertex stage receives on the plug's slots. One
    // scale per process: the skinned buffer read back from C# in edit mode was stale.
    // Then the plug baked afresh at that scale, into a scratch folder removed after, for the
    // skin scale the bake records against the one the vertex stage saw.
    //
    // Run: -executeMethod AvatarBridge.Regression.SkinScaleProbe.Run [-ssAvatar <prefab path>] [-ssScale <s>]
    public static class SkinScaleProbe
    {
        const string DefaultAvatar = "Assets/AvatarBridgeOutput/Alexa/Alexa (ChilloutVR).prefab";
        const string Scratch = "Assets/__SkinScaleProbe";

        public static void Run()
        {
            void Log(string s) => Debug.Log("[SkinScale] " + s);
            string F(float x) => x.ToString("0.0000", CultureInfo.InvariantCulture);
            int code = 1;
            RenderTexture rt = null;
            ComputeBuffer buffer = null;
            try
            {
                string path = Arg("-ssAvatar") ?? DefaultAvatar;
                float scale = float.TryParse(Arg("-ssScale"), NumberStyles.Float, CultureInfo.InvariantCulture, out float s) ? s : 1f;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var shader = Shader.Find("Hidden/SkinScaleCapture");
                if (prefab == null) throw new Exception("no prefab at " + path);
                if (shader == null || !shader.isSupported) throw new Exception("Hidden/SkinScaleCapture did not compile");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var avatar = (GameObject) PrefabUtility.InstantiatePrefab(prefab);
                avatar.transform.position = Vector3.zero;
                avatar.transform.localScale = Vector3.one * scale;
                var skin = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.sharedMaterials.Any(IsPlug));
                if (skin == null) throw new Exception("no skinned plug on the avatar");
                for (var t = skin.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
                skin.updateWhenOffscreen = true;
                Log($"avatar {path}, root scale {scale}, plug {skin.name} lossy {F(skin.transform.lossyScale.x)}, " +
                    $"GPU skinning {PlayerSettings.gpuSkinning}, {SystemInfo.graphicsDeviceType}");

                var mesh = skin.sharedMesh;
                var mats = skin.sharedMaterials;
                var plugVerts = new HashSet<int>();
                for (int i = 0; i < mesh.subMeshCount && i < mats.Length; i++)
                    if (IsPlug(mats[i])) plugVerts.UnionWith(mesh.GetIndices(i));
                var ids = plugVerts.OrderBy(i => i).ToArray();
                var restNormal = mesh.normals;
                var restTangent = mesh.tangents;
                var restPos = mesh.vertices;
                Log($"{ids.Length} plug vertices of {mesh.vertexCount}; mesh normal {F(ids.Median(i => restNormal[i].magnitude))}, " +
                    $"tangent {F(ids.Median(i => ((Vector3) restTangent[i]).magnitude))} long");

                int n = mesh.vertexCount;
                var capture = new Material(shader);
                capture.SetFloat("_SkinScaleCount", n);
                skin.sharedMaterials = Enumerable.Repeat(capture, mats.Length).ToArray();
                buffer = new ComputeBuffer(2 * n, 16);
                buffer.SetData(new Vector4[2 * n]);

                var cam = new GameObject("__SkinScaleCam").AddComponent<Camera>();
                rt = new RenderTexture(64, 64, 24);
                cam.targetTexture = rt;
                var c = skin.bounds.center;
                cam.transform.position = c + new Vector3(0, 0, 3f * scale);
                cam.transform.LookAt(c);
                cam.farClipPlane = 100f;
                Graphics.ClearRandomWriteTargets();
                Graphics.SetRandomWriteTarget(1, buffer, false);
                cam.Render();
                Graphics.ClearRandomWriteTargets();
                var got = new Vector4[2 * n];
                buffer.GetData(got);

                int drawn = ids.Count(i => got[n + i].w > 0.5f);
                if (drawn == 0) throw new Exception("nothing captured: the plug was not drawn");
                var drawnIds = ids.Where(i => got[n + i].w > 0.5f).ToArray();
                var p0 = (Vector3) got[drawnIds[0]];
                float span = drawnIds.Max(i => ((Vector3) got[i] - p0).magnitude);
                Log($"drawn {drawnIds.Length}; plug span {F(span)} m (the bind mesh's {F(drawnIds.Max(i => (restPos[i] - restPos[drawnIds[0]]).magnitude))})");
                Log($"the vertex stage sees: normal {F(drawnIds.Median(i => got[i].w))} long " +
                    $"({F(drawnIds.Min(i => got[i].w))} to {F(drawnIds.Max(i => got[i].w))}), " +
                    $"tangent {F(drawnIds.Median(i => got[n + i].x))} long, ObjectToWorld scale {F(drawnIds.Median(i => got[n + i].y))}");

                var plug = avatar.GetComponentsInChildren<YapsPlug>(true).FirstOrDefault(p => p.Target == skin);
                var chainRoot = plug == null ? null : plug.rootBone != null ? plug.rootBone : plug.transform;
                if (chainRoot == null) throw new Exception("no YapsPlug on the plug's renderer");
                try
                {
                    var result = YapsBaker.Bake(skin, chainRoot, Scratch, new BridgeReport(), out string failure);
                    if (result == null) throw new Exception("bake failed: " + failure);
                    Log($"the bake records skin scale {F(result.SkinScale)}, length {F(result.Length)}");
                }
                finally
                {
                    AssetDatabase.DeleteAsset(Scratch);
                }
                code = 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[SkinScale] threw: " + e);
            }
            finally
            {
                if (rt != null) rt.Release();
                buffer?.Release();
            }
            Log(code == 0 ? "DONE" : "FAILED");
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static float Median(this IEnumerable<int> ids, Func<int, float> f)
        {
            var v = ids.Select(f).OrderBy(x => x).ToArray();
            return v.Length == 0 ? 0f : v[v.Length / 2];
        }

        static bool IsPlug(Material m)
        {
            if (m == null || m.shader == null) return false;
            if (m.shader.FindPropertyIndex("_YAPS_AnchorVertex") >= 0) return false;
            if (m.shader.FindPropertyIndex("_YAPS_VertexCount") < 0 || m.GetFloat("_YAPS_VertexCount") <= 0f) return false;
            return m.shader.FindPropertyIndex("_YAPS_SocketPower") < 0 || m.GetFloat("_YAPS_SocketPower") <= 0f;
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
#endif
