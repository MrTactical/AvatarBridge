#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using ABI.CCK.Components;

namespace AvatarBridge
{
    // Detects VRChat face-tracking blendshapes (VRCFaceTracking / SRanipal or Unified
    // Expressions naming) and wires up ChilloutVR's native CVRFaceTracking component.
    //
    // VRChat face tracking is driven by an OSC app writing blendshape parameters into the
    // FX layer; that plumbing doesn't exist in CVR. But the *blendshapes themselves*
    // (JawOpen, MouthClosed, TongueOut, ...) carry over on the mesh, and CVR reads them
    // directly through CVRFaceTracking; no OSC, no per-shape animator layers. So if the
    // mesh has the shapes, the avatar can switch to native CVR face tracking for free.
    public static class FaceTrackingConverter
    {
        const string Category = "Face tracking";

        // Minimum distinct tracked shapes before a mesh counts as face-tracked.
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
            if (ctx.Settings.faceTrackingMode == FaceTrackingMode.None)
            {
                ctx.Report.Skipped(Category, "Kept the avatar's own face tracking (chosen)",
                    "Nothing was added or replaced. If the avatar already has a face tracking rig " +
                    "it is merged like the rest of its animator — see the entry naming the " +
                    "parameters that came through. If it has none, this is where one would go: " +
                    "convert again with \"CVR-VRCFT\" selected.");
                return;
            }

            if (ctx.Settings.faceTrackingMode == FaceTrackingMode.DragonSkyRunner)
            {
                // The avatar's existing FT rig is stripped and the bundled CVR-VRCFT animator
                // is injected + repathed in AnimatorMerger (FaceTrackingInjector); the native
                // CVRFaceTracking component is not added. Just report the bundle state here.
                if (!FaceTrackingPackages.IsInstalled())
                {
                    ctx.Report.Error(Category, "CVR-VRCFT face tracking selected, but its bundled assets are missing",
                        $"Reimport AvatarBridge so \"{FaceTrackingPackages.DisplayName}\" is present, then convert again.");
                }
                return;
            }

            if (ctx.Target.GetComponentInChildren<CVRFaceTracking>(true) != null)
            {
                ctx.Report.Converted(Category, "Avatar already has a CVRFaceTracking component; left as-is.");
                return;
            }

            bool detected = DetectShapes(ctx.Target,
                ctx.CvrAvatar != null ? ctx.CvrAvatar.bodyMesh : null,
                out var bestMesh, out bool bestIsUnified, out int bestScore);

            if (!detected)
            {
                // Says HOW CLOSE it got and on which mesh. "Nothing was detected" is where a
                // report like this stops being useful: an avatar that scored nothing has no
                // tracking shapes, and one that scored just under the bar has them under a naming
                // scheme this could not read; the same sentence for two different problems, and
                // no way to tell which from the outside.
                string near = bestMesh != null
                    ? $"The closest was \"{bestMesh.name}\", which matched {bestScore} of the " +
                      $"tracking shape names — {DetectionThreshold} are needed to be sure. "
                    : "No mesh on this avatar carries blendshapes that resemble tracking shapes. ";
                ctx.Report.Converted(Category, "No face-tracking blendshapes detected",
                    near + "Either the avatar isn't set up for face tracking, or its shapes use " +
                    "names this doesn't recognise. If you know it has them, the shape names on that " +
                    "mesh are the thing to check — and worth reporting, because a naming scheme " +
                    "that reads as nothing here is a gap worth closing.");
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
            int unsided = FillUnsidedShapes(faceTracking, bestMesh);

            int mapped = faceTracking.FaceBlendShapes.Count(s => !string.IsNullOrEmpty(s) && s != "-none-");
            EditorUtility.SetDirty(faceTracking);

            string mode = bestIsUnified ? "Unified Expressions" : "Legacy (SRanipal)";
            ctx.Report.Converted(Category,
                $"Native CVR face tracking set up on \"{bestMesh.name}\"",
                $"{mode} mode; {mapped} blendshapes mapped. Review them on the CVRFaceTracking " +
                "component." +
                (unsided > 0
                    ? $" {unsided} of those are shapes this avatar names WITHOUT a side — one " +
                      "\"EyeLookDown\" standing for the left and right slots both — which " +
                      "ChilloutVR's own matcher leaves empty because it looks for its full sided " +
                      "name. Both slots are given the same shape, which is how a rig like this is " +
                      "meant to be driven; expect symmetric movement on those, since there is only " +
                      "one shape to move."
                    : ""));
        }

        static int FillUnsidedShapes(CVRFaceTracking faceTracking, SkinnedMeshRenderer mesh)
        {
            var targets = faceTracking.CurrentShapeNames;
            var slots = faceTracking.FaceBlendShapes;
            if (targets == null || slots == null || slots.Length != targets.Length
                || mesh == null || mesh.sharedMesh == null)
            {
                return 0;
            }

            var shapes = new List<string>(mesh.sharedMesh.blendShapeCount);
            for (int i = 0; i < mesh.sharedMesh.blendShapeCount; i++)
            {
                shapes.Add(mesh.sharedMesh.GetBlendShapeName(i));
            }

            int filled = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                if (!string.IsNullOrEmpty(slots[i]) && slots[i] != "-none-")
                {
                    continue;   // ChilloutVR already matched this one
                }
                string target = Simplify(targets[i].ToLowerInvariant());
                string best = null;
                int bestLength = 0;
                foreach (var shape in shapes)
                {
                    string plain = Simplify(shape.ToLowerInvariant());
                    if (plain.Length <= bestLength || !IsSideVariant(target, plain))
                    {
                        continue;
                    }
                    best = shape;
                    bestLength = plain.Length;
                }
                if (best != null)
                {
                    slots[i] = best;
                    filled++;
                }
            }
            return filled;
        }

        internal static bool DetectShapes(GameObject root, SkinnedMeshRenderer named,
            out SkinnedMeshRenderer mesh, out bool unified, out int score)
        {
            var legacyShapes = ReadShapeList("LegacyShapeNames", LegacyFallback);
            var unifiedShapes = ReadShapeList("UnifiedShapeNames", UnifiedFallback);

            mesh = null;
            unified = false;
            score = 0;

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null || smr.sharedMesh.blendShapeCount == 0 || IsDebugMesh(smr))
                {
                    continue;
                }
                var shapeNames = ShapeNames(smr.sharedMesh);
                int legacyScore = CountMatches(shapeNames, legacyShapes);
                int unifiedScore = CountMatches(shapeNames, unifiedShapes);

                bool isUnified = unifiedScore >= legacyScore;
                int best = Mathf.Max(legacyScore, unifiedScore);
                if (best > score)
                {
                    score = best;
                    mesh = smr;
                    unified = isUnified;
                }
            }

            // The descriptor already named the face mesh, and it outranks any score: a scoring
            // contest is only needed when nothing authoritative is available. Applied when the
            // named mesh carries face-tracking shapes at all, so an avatar whose visemes and FT
            // live on different meshes still resolves by score.
            if (named != null && named != mesh && named.sharedMesh != null && !IsDebugMesh(named))
            {
                var namedShapes = ShapeNames(named.sharedMesh);
                int namedLegacy = CountMatches(namedShapes, legacyShapes);
                int namedUnified = CountMatches(namedShapes, unifiedShapes);
                if (Mathf.Max(namedLegacy, namedUnified) >= DetectionThreshold)
                {
                    mesh = named;
                    unified = namedUnified >= namedLegacy;
                    score = Mathf.Max(namedLegacy, namedUnified);
                }
            }

            return mesh != null && score >= DetectionThreshold;
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

        static int CountMatches(List<string> loweredShapeNames, string[] targets)
        {
            int count = 0;
            foreach (var target in targets)
            {
                string t = target.ToLowerInvariant();
                string tPlain = Simplify(t);
                bool hit = false;
                foreach (var shape in loweredShapeNames)
                {
                    string sPlain = Simplify(shape);
                    if (shape.Contains(t) || sPlain.Contains(tPlain) || IsSideVariant(tPlain, sPlain))
                    {
                        hit = true;
                        break;
                    }
                }
                if (hit)
                {
                    count++;
                }
            }
            return count;
        }

        static bool IsSideVariant(string targetPlain, string shapePlain)
        {
            if (shapePlain.Length < 4
                || !targetPlain.StartsWith(shapePlain, System.StringComparison.Ordinal))
            {
                return false;
            }
            string rest = targetPlain.Substring(shapePlain.Length);
            return System.Array.IndexOf(SideWords, rest) >= 0;
        }

        static readonly string[] SideWords =
        {
            "", "left", "right", "upper", "lower",
            "upperleft", "upperright", "lowerleft", "lowerright",
        };

        static string Simplify(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
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

        static bool IsDebugMesh(SkinnedMeshRenderer smr)
        {
            string name = smr.name.ToLowerInvariant();
            if (name.Contains("debug") || name.Contains("vf_ue") || name.Contains("ft_debug"))
            {
                return true;
            }
            // The panel is usually named innocuously and betrayed by its parents instead
            // (VRCFury - Face Tracking - UE Blendshapes/VF_UE_Debug/WorldObject/Window/FT_Debug).
            for (var t = smr.transform; t != null; t = t.parent)
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("debug") || n.Contains("vf_ue") || n.Contains("ft_debug")
                    || n.Contains("worldobject"))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
#endif
