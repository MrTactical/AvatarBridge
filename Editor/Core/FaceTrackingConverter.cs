#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using ABI.CCK.Components;

namespace AvatarBridge
{
    /// <summary>
    /// Detects VRChat face-tracking blendshapes (VRCFaceTracking / SRanipal or Unified
    /// Expressions naming) and wires up ChilloutVR's native CVRFaceTracking component.
    ///
    /// VRChat face tracking is driven by an OSC app writing blendshape parameters into the
    /// FX layer; that plumbing doesn't exist in CVR. But the *blendshapes themselves*
    /// (JawOpen, MouthClosed, TongueOut, …) carry over on the mesh, and CVR reads them
    /// directly through CVRFaceTracking — no OSC, no per-shape animator layers. So if the
    /// mesh has the shapes, we can switch the avatar to native CVR face tracking for free.
    /// </summary>
    public static class FaceTrackingConverter
    {
        const string Category = "Face tracking";

        // Minimum distinct tracked shapes on a mesh before we treat it as face-tracked.
        // Real setups match dozens; this only rules out avatars with a few stray matches.
        const int DetectionThreshold = 12;

        // Fallbacks if reflection can't read the CCK's own lists (field renamed, etc).
        static readonly string[] LegacyFallback =
        {
            "Jaw_Open", "Jaw_Left", "Jaw_Right", "Jaw_Forward", "Mouth_Ape_Shape",
            "Mouth_Smile_Right", "Mouth_Smile_Left", "Mouth_Sad_Right", "Mouth_Sad_Left",
            "Mouth_Pout", "Cheek_Puff_Right", "Cheek_Puff_Left", "Cheek_Suck",
            "Tongue_LongStep1", "Tongue_Up", "Tongue_Down", "Tongue_Left", "Tongue_Right"
        };
        static readonly string[] UnifiedFallback =
        {
            "JawOpen", "MouthClosed", "JawLeft", "JawRight", "JawForward",
            "CheekPuffLeft", "CheekPuffRight", "TongueOut", "BrowInnerUpLeft",
            "BrowInnerUpRight", "EyeWideLeft", "EyeWideRight", "EyeSquintLeft",
            "MouthUpperUpLeft", "MouthLowerDownLeft", "LipFunnelUpperLeft",
            "MouthCornerPullLeft", "MouthFrownLeft"
        };

        public static void Run(BridgeContext ctx)
        {
            if (!ctx.Settings.setupFaceTracking)
            {
                return;
            }
            if (ctx.Target.GetComponentInChildren<CVRFaceTracking>(true) != null)
            {
                ctx.Report.Converted(Category, "Avatar already has a CVRFaceTracking component; left as-is.");
                return;
            }

            var legacyShapes = ReadShapeList("LegacyShapeNames", LegacyFallback);
            var unifiedShapes = ReadShapeList("UnifiedShapeNames", UnifiedFallback);

            SkinnedMeshRenderer bestMesh = null;
            bool bestIsUnified = false;
            int bestScore = 0;

            foreach (var smr in ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null || smr.sharedMesh.blendShapeCount == 0)
                {
                    continue;
                }
                var shapeNames = ShapeNames(smr.sharedMesh);
                int legacyScore = CountMatches(shapeNames, legacyShapes);
                int unifiedScore = CountMatches(shapeNames, unifiedShapes);

                bool isUnified = unifiedScore >= legacyScore;
                int score = Mathf.Max(legacyScore, unifiedScore);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMesh = smr;
                    bestIsUnified = isUnified;
                }
            }

            if (bestMesh == null || bestScore < DetectionThreshold)
            {
                ctx.Report.Converted(Category, "No face-tracking blendshapes detected",
                    "Avatar isn't set up for face tracking, or uses an unrecognised shape naming scheme.");
                return;
            }

            var faceTracking = ctx.Target.AddComponent<CVRFaceTracking>();
            faceTracking.UseFacialTracking = true;
            faceTracking.FaceMesh = bestMesh;
            faceTracking.BlendShapeStrength = 100f;
            faceTracking.expressionsMode = bestIsUnified
                ? CVRFaceTracking.CVRFaceTrackingExpressionsMode.UnifiedExpressions
                : CVRFaceTracking.CVRFaceTrackingExpressionsMode.Legacy;

            // Let CVR's own matcher fill the per-shape slots, exactly like the CCK's
            // "Add Face Tracking" button does.
            faceTracking.GetBlendShapeNames();
            faceTracking.AutoSelectFaceTrackingShapes();

            int mapped = faceTracking.FaceBlendShapes.Count(s => !string.IsNullOrEmpty(s) && s != "-none-");
            EditorUtility.SetDirty(faceTracking);

            string mode = bestIsUnified ? "Unified Expressions" : "Legacy (SRanipal)";
            ctx.Report.Converted(Category,
                $"Native CVR face tracking set up on \"{bestMesh.name}\"",
                $"{mode} mode; {mapped} blendshapes auto-mapped. Review them on the CVRFaceTracking component.");
        }

        static List<string> ShapeNames(Mesh mesh)
        {
            var names = new List<string>(mesh.blendShapeCount);
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                names.Add(mesh.GetBlendShapeName(i).ToLowerInvariant());
            }
            return names;
        }

        /// <summary>
        /// Counts target shapes present on the mesh, mirroring CVR's own matcher:
        /// case-insensitive substring, also matching with underscores stripped.
        /// </summary>
        static int CountMatches(List<string> loweredShapeNames, string[] targets)
        {
            int count = 0;
            foreach (var target in targets)
            {
                string t = target.ToLowerInvariant();
                string tStripped = t.Replace("_", "");
                if (loweredShapeNames.Any(s => s.Contains(t) || s.Contains(tStripped)))
                {
                    count++;
                }
            }
            return count;
        }

        static string[] ReadShapeList(string fieldName, string[] fallback)
        {
            var field = typeof(CVRFaceTracking).GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Static);
            if (field != null && field.GetValue(null) is string[] list && list.Length > 0)
            {
                return list;
            }
            return fallback;
        }
    }
}
#endif
