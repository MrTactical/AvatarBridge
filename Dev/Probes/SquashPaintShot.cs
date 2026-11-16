// The squash painter's region overlay drawn over a real camera frame, depth and all, from a batch
// Unity, on top of everything and as the Scene view draws it, with the body it picked. A pose moved
// here is not re-skinned before the render, so a stale bake cannot be shown this way.
//
// Run: Unity.exe -batchmode -projectPath <p> -executeMethod AvatarBridge.Regression.SquashPaintShot.Run
//        -shotOut <dir> -shotPrefab <Assets/...prefab> -quit
#if CVR_CCK_EXISTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarBridge.Regression
{
    public static class SquashPaintShot
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const int Size = 800;

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        public static void Run()
        {
            string dir = Arg("-shotOut");
            Directory.CreateDirectory(dir);
            var log = new System.Text.StringBuilder();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                ShaderUtil.allowAsyncCompilation = false;
                var avatar = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Arg("-shotPrefab")));
                var painter = ScriptableObject.CreateInstance<YapsSquashPainter>();
                var t = typeof(YapsSquashPainter);
                t.GetMethod("Pick", Any).Invoke(painter, new object[] { avatar });
                t.GetMethod("FromNames", Any).Invoke(painter, null);
                var body = (SkinnedMeshRenderer)t.GetField("_body", Any).GetValue(painter);
                var anim = avatar.GetComponent<Animator>();
                var part = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
                var cam = new GameObject("shot") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
                cam.enabled = false;
                cam.fieldOfView = 35f;
                cam.nearClipPlane = 0.01f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 1f);
                var mid = part.position;
                cam.transform.position = mid + avatar.transform.forward * 1.4f;
                cam.transform.LookAt(mid);

                var baked = (Mesh)t.GetField("_baked", Any).GetValue(painter);
                var at = (Vector3[])t.GetField("_at", Any).GetValue(painter);
                var colours = (Color[])t.GetField("_colours", Any).GetValue(painter);
                var w = (float[][])t.GetField("_w", Any).GetValue(painter);
                log.AppendLine($"baked {baked?.vertexCount} verts, mesh colours {baked?.colors?.Length}, colours max alpha {colours?.Max(c => c.a)}, " +
                               $"weights max {string.Join("/", w.Select(x => x.Max().ToString("F2")))}");
                log.AppendLine($"body {body.name}: mesh {body.sharedMesh.name} {body.sharedMesh.vertexCount} verts, path {AssetDatabase.GetAssetPath(body.sharedMesh)}");
                foreach (var r in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null).OrderByDescending(r => r.sharedMesh.vertexCount).Take(6))
                    log.AppendLine($"  smr {r.name}: {r.sharedMesh.name} {r.sharedMesh.vertexCount}");
                var b0 = new Bounds(at[0], Vector3.zero);
                foreach (var v in at) b0.Encapsulate(v);
                log.AppendLine($"baked bounds {b0.center} size {b0.size}; renderer bounds {body.bounds.center} size {body.bounds.size}; lossyScale {body.transform.lossyScale}");
                log.AppendLine("always on top: " + Shoot(painter, cam, dir, "0-always", CompareFunction.Always));
                log.AppendLine("as painted: " + Shoot(painter, cam, dir, "1-painted"));
            }
            catch (Exception x)
            {
                log.AppendLine("ERROR " + x);
            }
            File.WriteAllText(Path.Combine(dir, "shots.txt"), log.ToString());
            Debug.Log("[SquashPaintShot]\n" + log);
            EditorApplication.Exit(0);
        }

        // In onPostRender the camera's matrices and its depth are still bound, as the Scene view's are.
        static string Shoot(object painter, Camera cam, string dir, string name, CompareFunction? only = null)
        {
            var t = painter.GetType();
            var draw = t.GetMethod("DrawRegions", Any);
            Camera.CameraCallback after = c =>
            {
                if (c != cam) return;
                if (only.HasValue) { draw.Invoke(painter, new object[] { only.Value, 1f }); return; }
                draw.Invoke(painter, new object[] { CompareFunction.Greater, 0.3f });
                draw.Invoke(painter, new object[] { CompareFunction.LessEqual, 1f });
            };
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            Camera.onPostRender += after;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            Camera.onPostRender -= after;
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            RenderTexture.active = was;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            var px = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            int red = px.Count(p => p.r > 150 && p.r > p.g + 60 && p.r > p.b + 60);
            int blue = px.Count(p => p.b > 150 && p.b > p.g + 60 && p.b > p.r + 60);
            return $"{red} strong red px, {blue} strong blue px";
        }
    }
}
#endif
