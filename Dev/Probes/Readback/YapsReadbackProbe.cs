using System;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Probe for the runtime tester: render a SKINNED strip, bent by a turned
    // bone, through a vertex shader that writes each vertex into a buffer,
    // and compare what comes back with Unity's own skinning (BakeMesh). A
    // plug is skinned, and skinning happens before the vertex stage, so a
    // static mesh would prove nothing about the case that matters.
    public static class YapsReadbackProbe
    {
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[YapsReadbackProbe] {(ok ? "ok  " : "FAIL")} {label}");
                return ok ? 0 : 1;
            }

            Debug.Log($"[YapsReadbackProbe] device {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceVersion}, " +
                      $"shader level {SystemInfo.graphicsShaderLevel}, compute {SystemInfo.supportsComputeShaders}");

            GameObject root = null, camGo = null;
            ComputeBuffer buffer = null;
            RenderTexture target = null;
            try
            {
                var shader = Shader.Find("Hidden/YapsReadbackProbe");
                fail += Check("the probe shader compiled and was found", shader != null && shader.isSupported);
                if (shader == null) throw new Exception("no shader");

                root = new GameObject("__YapsReadbackProbe");
                var b0 = new GameObject("B0").transform; b0.SetParent(root.transform, false);
                var b1 = new GameObject("B1").transform; b1.SetParent(b0, false);
                b1.localPosition = new Vector3(0, 0, 0.5f);

                const int n = 64;
                var vertices = new Vector3[n];
                var weights = new BoneWeight[n];
                for (int i = 0; i < n; i++)
                {
                    vertices[i] = new Vector3(i % 2 == 0 ? 0.05f : -0.05f, 0f, i / (float) (n - 1));
                    weights[i] = new BoneWeight { boneIndex0 = vertices[i].z < 0.5f ? 0 : 1, weight0 = 1f };
                }
                var mesh = new Mesh { name = "ProbeStrip", vertices = vertices, boneWeights = weights };
                var tris = new int[(n - 2) * 3];
                for (int t = 0; t < n - 2; t++) { tris[t * 3] = t; tris[t * 3 + 1] = t + 1; tris[t * 3 + 2] = t + 2; }
                mesh.triangles = tris;
                mesh.bindposes = new[] { b0.worldToLocalMatrix * root.transform.localToWorldMatrix,
                                         b1.worldToLocalMatrix * root.transform.localToWorldMatrix };
                mesh.RecalculateBounds();

                var skin = root.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = mesh;
                skin.bones = new[] { b0, b1 };
                skin.rootBone = b0;
                skin.updateWhenOffscreen = true;
                skin.sharedMaterial = new Material(shader);

                // Bend it after binding, so the skinning has work to do.
                b1.localRotation = Quaternion.Euler(40f, 0f, 0f);
                root.transform.position = new Vector3(0.3f, 1.1f, -0.2f);

                camGo = new GameObject("__ProbeCamera");
                var cam = camGo.AddComponent<Camera>();
                target = new RenderTexture(256, 256, 24);
                cam.targetTexture = target;
                cam.transform.position = new Vector3(0.3f, 1.3f, -2.5f);
                cam.transform.LookAt(root.transform.position + Vector3.forward * 0.4f);

                buffer = new ComputeBuffer(n, 16);
                var got = new Vector4[n];
                buffer.SetData(got);
                Graphics.ClearRandomWriteTargets();
                Graphics.SetRandomWriteTarget(1, buffer, false);
                cam.Render();
                Graphics.ClearRandomWriteTargets();
                buffer.GetData(got);

                var baked = new Mesh();
                skin.BakeMesh(baked, true);
                var want = baked.vertices;
                int written = 0;
                float worst = 0f;
                for (int i = 0; i < n; i++)
                {
                    if (got[i].w < 0.5f) continue;
                    written++;
                    worst = Mathf.Max(worst, Vector3.Distance(got[i], skin.transform.TransformPoint(want[i])));
                }
                Debug.Log($"[YapsReadbackProbe] {written}/{n} written, worst {worst * 1000f:0.000} mm, " +
                          $"tip {(Vector3) got[n - 1]} vs {skin.transform.TransformPoint(want[n - 1])}");
                fail += Check("every vertex came back", written == n);
                fail += Check("the skinned bend came back within 0.1 mm of Unity's own", written == n && worst < 1e-4f);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                fail++;
            }
            finally
            {
                buffer?.Release();
                if (target != null) target.Release();
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }

            Debug.Log(fail == 0
                ? "[YapsReadbackProbe] PASS: the vertex stage can hand the bend back."
                : $"[YapsReadbackProbe] FAIL: {fail} check(s).");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
