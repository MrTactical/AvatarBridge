using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    // What three soft-body routes cost per avatar wearing them, so a full instance can be
    // summed. Dent: the vertex math a patched body adds, per vertex per pass. Feedback: a
    // camera rendering one marker into a 1x1 texture plus the parser's async readback, in a
    // scene with enough renderers that culling is not free. Joints: PhysX rigidbodies with
    // configurable joints, five chains of three per avatar, stepped at 90 Hz.
    //
    // Run: -executeMethod AvatarBridge.Regression.SoftBodyPerf.Run -quit
    public static class SoftBodyPerf
    {
        static void Log(string s) => Debug.Log("[SoftBodyPerf] " + s);

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Log($"GPU {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}; CPU {SystemInfo.processorType}");
            try { Dent(); } catch (Exception e) { Log("Dent FAIL " + e); }
            try { Feedback(); } catch (Exception e) { Log("Feedback FAIL " + e); }
            try { Joints(); } catch (Exception e) { Log("Joints FAIL " + e); }
        }

        static void Dent()
        {
            var shader = Shader.Find("Hidden/SoftBodyDentPerf");
            if (shader == null || !shader.isSupported) { Log("Dent FAIL shader did not compile"); return; }
            var mat = new Material(shader);
            mat.SetVectorArray("_Points", new[]
            {
                new Vector4(0.1f, 0.5f, 0.5f, 0.2f), new Vector4(0.3f, 0.2f, 0.4f, 0.1f),
                new Vector4(0.6f, 0.7f, 0.1f, 0.3f), new Vector4(0.9f, 0.4f, 0.8f, 0.2f),
            });
            // Ranges of 1.05 m: tagged, so every slot does the full work.
            float atten = 25f / (1.05f * 1.05f);
            mat.SetVector("_Atten", new Vector4(atten, atten, atten, atten));
            var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            var cam = new GameObject("__DentCamera").AddComponent<Camera>();
            cam.targetTexture = target;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.cullingMask = 0;
            cam.enabled = false;
            var sync = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            const int vertices = 1 << 20, draws = 16;
            double Time(int pass)
            {
                var cb = new CommandBuffer();
                for (int i = 0; i < draws; i++) cb.DrawProcedural(Matrix4x4.identity, mat, pass, MeshTopology.Points, vertices);
                cam.AddCommandBuffer(CameraEvent.AfterEverything, cb);
                double best = double.MaxValue;
                for (int trial = 0; trial < 9; trial++)
                {
                    var watch = Stopwatch.StartNew();
                    cam.Render();
                    RenderTexture.active = target;
                    sync.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                    RenderTexture.active = null;
                    if (trial > 0) best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
                }
                cam.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
                cb.Release();
                return best * 1e6 / ((double) draws * vertices);
            }
            double floor = Time(1);
            double dent = Time(0) - floor;
            // A 60k-vertex body drawn in its forward pass and its shadow caster, in one view and a mirror.
            double perAvatar = dent * 60000 * 2 * 2 / 1000;
            Log($"Dent: floor {floor:0.000} ns a vertex, dent with four slots +{dent:0.000} ns a vertex; " +
                $"a 60k-vertex body, two passes, view plus mirror: {perAvatar:0.0} us GPU per avatar a frame");
            Object.DestroyImmediate(cam.gameObject);
            target.Release();
        }

        static void Feedback()
        {
            var filler = new GameObject("__Filler");
            var standard = new Material(Shader.Find("Standard"));
            for (int i = 0; i < 2000; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(c.GetComponent<Collider>());
                c.transform.SetParent(filler.transform);
                c.transform.position = new Vector3(i % 50 * 2f, 0f, i / 50 * 2f);
                c.GetComponent<Renderer>().sharedMaterial = standard;
            }
            var sun = new GameObject("__Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, 30f, 0f);

            // A normal view of the same scene, for scale.
            var mainRt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var main = new GameObject("__Main").AddComponent<Camera>();
            main.enabled = false;
            main.targetTexture = mainRt;
            main.transform.position = new Vector3(50f, 20f, -30f);
            main.transform.LookAt(new Vector3(50f, 0f, 40f));
            var sync = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            void Sync(RenderTexture rt) { RenderTexture.active = rt; sync.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); RenderTexture.active = null; }
            double mainMs = Best(() => { main.Render(); Sync(mainRt); });
            Log($"Feedback: one full 1080p view of the 2000-cube scene with soft shadows: {mainMs:0.000} ms");

            var unlit = new Material(Shader.Find("Unlit/Color"));
            foreach (int n in new[] { 1, 10, 40 })
            {
                var cams = new List<Camera>();
                var rts = new List<RenderTexture>();
                var made = new List<GameObject>();
                for (int i = 0; i < n; i++)
                {
                    var marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Object.DestroyImmediate(marker.GetComponent<Collider>());
                    marker.transform.position = new Vector3(i * 2f + 1f, 1f, 1f);
                    marker.transform.localScale = Vector3.one * 0.01f;
                    marker.GetComponent<Renderer>().sharedMaterial = unlit;
                    var rt = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    rt.Create();
                    var cam = new GameObject("__Feed").AddComponent<Camera>();
                    cam.enabled = false;
                    cam.orthographic = true;
                    cam.orthographicSize = 0.01f;
                    cam.nearClipPlane = 0.001f;
                    cam.farClipPlane = 0.02f;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.targetTexture = rt;
                    cam.transform.position = marker.transform.position - Vector3.forward * 0.01f;
                    cams.Add(cam); rts.Add(rt); made.Add(marker); made.Add(cam.gameObject);
                }
                double render = Best(() => { foreach (var c in cams) c.Render(); });
                double withGpu = Best(() => { foreach (var c in cams) c.Render(); Sync(rts[rts.Count - 1]); });
                double readback = Best(() =>
                {
                    foreach (var rt in rts) AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
                });
                AsyncGPUReadback.WaitAllRequests();
                Log($"Feedback x{n}: cameras {render:0.000} ms CPU ({render / n * 1000:0} us each), " +
                    $"with GPU finished {withGpu:0.000} ms, readback requests {readback * 1000 / n:0.0} us each");
                foreach (var g in made) Object.DestroyImmediate(g);
                foreach (var rt in rts) rt.Release();
            }
            Object.DestroyImmediate(filler);
            Object.DestroyImmediate(sun.gameObject);
            Object.DestroyImmediate(main.gameObject);
            mainRt.Release();
        }

        static double Best(Action a)
        {
            a();
            double best = double.MaxValue;
            for (int trial = 0; trial < 9; trial++)
            {
                var watch = Stopwatch.StartNew();
                a();
                best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        const int Chains = 5, Bones = 3;
        const float Step = 1f / 90f, BoneLength = 0.08f;

        static void Joints()
        {
            var was = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                foreach (int n in new[] { 1, 10, 40 })
                {
                    var root = new GameObject("__Joints");
                    var hips = new List<Rigidbody>();
                    var links = new List<(Rigidbody body, Rigidbody parent)>();
                    for (int a = 0; a < n; a++)
                    {
                        var h = new GameObject("Hips").AddComponent<Rigidbody>();
                        h.transform.SetParent(root.transform);
                        h.transform.position = new Vector3(a * 3f, 1f, 0f);
                        h.isKinematic = true;
                        var torso = h.gameObject.AddComponent<CapsuleCollider>();
                        torso.radius = 0.15f;
                        torso.height = 0.6f;
                        hips.Add(h);
                        for (int c = 0; c < Chains; c++)
                        {
                            var parent = h;
                            var dir = Quaternion.Euler(0f, c * 72f, 0f) * new Vector3(0f, -0.3f, 1f).normalized;
                            var pos = h.transform.position + dir * 0.18f;
                            for (int b = 0; b < Bones; b++)
                            {
                                pos += dir * BoneLength;
                                var go = new GameObject("Bone");
                                go.transform.SetParent(root.transform);
                                go.transform.position = pos;
                                var rb = go.AddComponent<Rigidbody>();
                                rb.mass = 0.1f;
                                rb.interpolation = RigidbodyInterpolation.Interpolate;
                                go.AddComponent<SphereCollider>().radius = 0.03f;
                                var j = go.AddComponent<ConfigurableJoint>();
                                j.connectedBody = parent;
                                j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Limited;
                                // Room to stretch or squash by half a bone, pulled back by a spring.
                                j.linearLimit = new SoftJointLimit { limit = BoneLength * 0.5f };
                                var pull = new JointDrive { positionSpring = 300f, positionDamper = 3f, maximumForce = float.MaxValue };
                                j.xDrive = j.yDrive = j.zDrive = pull;
                                j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Limited;
                                j.lowAngularXLimit = new SoftJointLimit { limit = -40f };
                                j.highAngularXLimit = new SoftJointLimit { limit = 40f };
                                j.angularYLimit = j.angularZLimit = new SoftJointLimit { limit = 40f };
                                j.rotationDriveMode = RotationDriveMode.Slerp;
                                j.slerpDrive = new JointDrive { positionSpring = 5f, positionDamper = 0.2f, maximumForce = float.MaxValue };
                                links.Add((rb, parent));
                                parent = rb;
                            }
                        }
                    }
                    var rest = links.Select(l => Vector3.Distance(l.body.position, l.parent.position)).ToArray();
                    var times = new List<double>();
                    float maxStretch = 0f, minSquash = float.MaxValue;
                    for (int s = 0; s < 990; s++)
                    {
                        float t = s * Step;
                        for (int a = 0; a < hips.Count; a++)
                        {
                            // A walk's bob and sway, then a jump every three seconds.
                            float jump = t % 3f < 0.5f ? Mathf.Sin(t % 3f / 0.5f * Mathf.PI) * 0.4f : 0f;
                            hips[a].MovePosition(new Vector3(a * 3f + Mathf.Sin(t * 6f) * 0.05f, 1f + Mathf.Abs(Mathf.Sin(t * 6f)) * 0.06f + jump, t * 1.2f));
                        }
                        var watch = Stopwatch.StartNew();
                        Physics.Simulate(Step);
                        if (s >= 90) times.Add(watch.Elapsed.TotalMilliseconds);
                        for (int i = 0; i < links.Count; i++)
                        {
                            float ratio = Vector3.Distance(links[i].body.position, links[i].parent.position) / rest[i];
                            maxStretch = Mathf.Max(maxStretch, ratio);
                            minSquash = Mathf.Min(minSquash, ratio);
                        }
                    }
                    times.Sort();
                    double mean = times.Average(), p99 = times[(int) (times.Count * 0.99)];
                    Log($"Joints x{n} ({links.Count} bodies): step mean {mean:0.000} ms, p99 {p99:0.000} ms " +
                        $"({mean / n * 1000:0.0} us an avatar); bone length ranged {minSquash:0.00} to {maxStretch:0.00} of rest");
                    Object.DestroyImmediate(root);
                }
            }
            finally
            {
                Physics.simulationMode = was;
            }
        }
    }
}
