#if CVR_CCK_EXISTS && VRC_SDK_VRCSDK3
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    // Native face tracking's built shapes on each avatar a list names, without converting it:
    // the slots the CCK fills, what the old sideless fill added, and what the build adds now.
    // Every built left or right half is checked: its move lies on its own side of the face, and
    // the two halves of one source add back up to it. Nothing is saved; the meshes go to a scratch
    // folder deleted at the end.
    //   -executeMethod AvatarBridge.Regression.FaceTrackingShapesProbe.Run -ftScenes <file> -ftOut <file>
    public static class FaceTrackingShapesProbe
    {
        const string Scratch = "Assets/AvatarBridge_FTProbe";

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string k) { int i = Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            var scenes = File.ReadAllLines(Arg("-ftScenes")).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
            var lines = new List<string>();
            int bad = 0;
            try
            {
                foreach (var path in scenes)
                {
                    try { bad += One(path, lines); }
                    catch (Exception e) { lines.Add($"{path}: EXCEPTION {e.GetType().Name} {e.Message}"); bad++; }
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset(Scratch);
                lines.Add(bad == 0 ? "[FTShapes] PASS" : $"[FTShapes] FAIL ({bad})");
                File.WriteAllLines(Arg("-ftOut"), lines);
                Debug.Log(string.Join("\n", lines));
                EditorApplication.Exit(0);
            }
        }

        static int One(string scenePath, List<string> lines)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var descriptor = scene.GetRootGameObjects().Select(r => r.GetComponentInChildren<VRCAvatarDescriptor>(true)).FirstOrDefault(d => d != null);
            string name = Path.GetFileNameWithoutExtension(scenePath);
            if (descriptor == null) { lines.Add($"{name}: no avatar"); return 0; }
            var root = descriptor.gameObject;
            root.SetActive(true);
            var face = descriptor.VisemeSkinnedMesh;
            if (!FaceTrackingConverter.DetectShapes(root, face, out var smr, out bool unified, out int score))
            {
                lines.Add($"{name}: no tracking shapes (best {score})");
                return 0;
            }

            // Before: the CCK's matcher and the sideless fill, as the last release did.
            var old = root.AddComponent<CVRFaceTracking>();
            Setup(old, smr, unified);
            int cck = Mapped(old);
            int unsidedOld = FaceTrackingConverter.FillUnsidedShapes(old, smr);
            int before = Mapped(old);
            UnityEngine.Object.DestroyImmediate(old);

            // Now: the build first, then the sideless fill on whatever is left.
            var src = smr.sharedMesh;
            var ft = root.AddComponent<CVRFaceTracking>();
            Setup(ft, smr, unified);
            AssetDatabase.CreateFolder("Assets", "AvatarBridge_FTProbe");
            var ctx = new BridgeContext { Target = root, OutputDir = Scratch, Report = new BridgeReport(), Settings = new BridgeSettings() };
            OutputAssetPaths.Reset();
            var t0 = DateTime.Now;
            int made = unified ? FaceTrackingConverter.MakeMissingShapes(ctx, ft, smr) : 0;
            double ms = (DateTime.Now - t0).TotalMilliseconds;
            int unsidedNew = FaceTrackingConverter.FillUnsidedShapes(ft, smr);
            int after = Mapped(ft);

            int problems = made > 0 ? Check(ft, src, smr, root, lines, name) : 0;
            lines.Add($"{name}: {(unified ? "UE" : "legacy")} on \"{smr.name}\" ({src.vertexCount} verts): CCK {cck}, before {before} (+{unsidedOld} sideless), " +
                      $"now {after} (+{made} built in {ms:0} ms, +{unsidedNew} sideless){(problems > 0 ? $", {problems} PROBLEMS" : "")}");
            smr.sharedMesh = src;
            UnityEngine.Object.DestroyImmediate(ft);
            AssetDatabase.DeleteAsset(Scratch);
            return problems;
        }

        static void Setup(CVRFaceTracking ft, SkinnedMeshRenderer smr, bool unified)
        {
            ft.UseFacialTracking = true;
            ft.FaceMesh = smr;
            ft.expressionsMode = unified ? CVRFaceTracking.CVRFaceTrackingExpressionsMode.UnifiedExpressions : CVRFaceTracking.CVRFaceTrackingExpressionsMode.Legacy;
            ft.GetBlendShapeNames();
            ft.AutoSelectFaceTrackingShapes();
        }

        static int Mapped(CVRFaceTracking ft) => ft.FaceBlendShapes.Count(s => !string.IsNullOrEmpty(s) && s != "-none-");

        // Each built shape not in the source mesh: no bad numbers; a Left or Right one moves on its
        // own side of the head (its weighted centre past the midline toward its side); and a Left
        // and Right pair built from one source sum to the same, so neither half is lost.
        static int Check(CVRFaceTracking ft, Mesh src, SkinnedMeshRenderer smr, GameObject root, List<string> lines, string name)
        {
            var mesh = smr.sharedMesh;
            var animator = root.GetComponent<Animator>();
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var across = (animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position - animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position).normalized;
            var baked = new Mesh();
            smr.BakeMesh(baked);
            var at = baked.vertices;
            UnityEngine.Object.DestroyImmediate(baked);
            var t = smr.transform;
            var x = at.Select(v => Vector3.Dot(t.position + t.rotation * v - head.position, across)).ToArray();
            int n = mesh.vertexCount, problems = 0;
            var built = ft.FaceBlendShapes.Where(s => !string.IsNullOrEmpty(s) && src.GetBlendShapeIndex(s) < 0).Distinct().ToList();
            var sums = new Dictionary<string, float>();
            var report = new List<string>();
            foreach (var s in built)
            {
                int i = mesh.GetBlendShapeIndex(s), last = mesh.GetBlendShapeFrameCount(i) - 1;
                var d = new Vector3[n];
                mesh.GetBlendShapeFrameVertices(i, last, d, null, null);
                float total = 0f, side = 0f, push = 0f;
                bool nan = false;
                for (int v = 0; v < n; v++)
                {
                    float m = d[v].magnitude;
                    nan |= float.IsNaN(m) || float.IsInfinity(m);
                    total += m;
                    side += m * x[v];
                    push += Vector3.Dot(t.rotation * d[v], across);
                }
                sums[s] = total;
                float centre = total > 0 ? side / total * 1000f : 0f;
                // A whole shape that moves the mouth sideways lies on the middle; its move points to its side.
                int sign = s.EndsWith("Left") ? -1 : s.EndsWith("Right") ? 1 : 0;
                bool onSide = centre * sign > -1f || push * sign > 0f;
                // An empty shape only copies an empty source; reported, not counted.
                bool wrong = nan || total > 0f && !onSide;
                if (wrong) problems++;
                report.Add($"{s} {centre:+0.0;-0.0} mm{(total <= 0f ? " EMPTY" : "")}{(wrong ? " WRONG" : "")}");
            }
            foreach (var s in built.Where(b => b.EndsWith("Left")))
            {
                string r = s.Substring(0, s.Length - 4) + "Right";
                if (!sums.TryGetValue(r, out float rs) || sums[s] <= 0f) continue;
                float ratio = sums[s] / rs;
                if (ratio < 0.5f || ratio > 2f) { problems++; report.Add($"{s}/{r} unequal {ratio:0.00}"); }
            }
            lines.Add($"  {name} built: {string.Join(", ", report)}");
            return problems;
        }
    }
}
#endif
