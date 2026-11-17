#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
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
                    "Nothing added. An existing rig merges with the animator; to add one, convert with " +
                    "\"Native CVR Component\" or \"Unity Animator Blendtrees (DSR)\" selected.");
                return;
            }

            if (ctx.Settings.faceTrackingMode == FaceTrackingMode.DragonSkyRunner)
            {
                // The avatar's existing FT rig is stripped and the bundled CVR-VRCFT animator
                // is injected + repathed in AnimatorMerger (FaceTrackingInjector); the native
                // CVRFaceTracking component is not added. Just report the bundle state here.
                if (!FaceTrackingPackages.IsInstalled())
                {
                    ctx.Report.Error(Category, "\"Unity Animator Blendtrees (DSR)\" face tracking selected, but its bundled assets are missing",
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
                      $"tracking shape names: {DetectionThreshold} are needed to be sure. "
                    : "No mesh on this avatar carries blendshapes that resemble tracking shapes. ";
                string elsewhere = WhereTheShapesAre(ctx.Target, out _);
                if (elsewhere != null)
                {
                    ctx.Report.Warning(Category, "No face-tracking blendshapes detected", near + elsewhere);
                    return;
                }
                ctx.Report.Converted(Category, "No face-tracking blendshapes detected",
                    near + "None exist, or their names aren't recognised. If it has them, please report it.");
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
            int made = bestIsUnified ? MakeMissingShapes(ctx, faceTracking, bestMesh) : 0;
            int unsided = FillUnsidedShapes(faceTracking, bestMesh);

            int mapped = faceTracking.FaceBlendShapes.Count(s => !string.IsNullOrEmpty(s) && s != "-none-");
            EditorUtility.SetDirty(faceTracking);

            string mode = bestIsUnified ? "Unified Expressions" : "Legacy (SRanipal)";
            ctx.Report.Converted(Category,
                $"Native CVR face tracking set up on \"{bestMesh.name}\"",
                $"{mode} mode; {mapped} blendshapes mapped. Review them on the CVRFaceTracking " +
                "component." +
                (made > 0
                    ? $" {made} more were built from shapes the mesh has: a shape with no side split down the " +
                      "middle of the face, and ARKit's names blended into Unified Expressions' own."
                    : "") +
                (unsided > 0
                    ? $" {unsided} have no side, so both slots share one shape and move together."
                    : ""));
        }

        // Unified Expressions slots the CCK's matcher leaves empty, built on a copy of the mesh from
        // what it has. Recipes are tried in order, the first whose shapes all exist wins:
        // "A+B*k" sums shapes, "@L" / "@R" keeps one half of the face, ":w" is a frame's weight and
        // ";" starts another frame. {S} is Right or Left, {s} R or L, for the slot built.
        // Weights after VRCFaceTracking's own simple expressions. From Shiro Usagi's face tracking
        // fixer, with thanks.
        static readonly (string slot, string[] recipes)[] Recipes =
        {
            ("EyeWide{S}", new[] { "EyeWide@{s}" }),
            ("EyeSquint{S}", new[] { "EyeSquint@{s}" }),
            ("EyeDilation{S}", new[] { "EyeDilation@{s}", "PupilDilation@{s}" }),
            ("EyeConstrict{S}", new[] { "EyeConstrict@{s}", "PupilConstrict@{s}" }),
            ("BrowPinch{S}", new[] { "BrowDown{S}*0.25", "BrowDown@{s}*0.25" }),
            ("BrowLowerer{S}", new[] { "BrowDown{S}*0.75", "BrowDown@{s}*0.75" }),
            ("BrowInnerUp{S}", new[] { "BrowInnerUp@{s}" }),
            ("BrowOuterUp{S}", new[] { "BrowOuterUp@{s}" }),
            ("NoseSneer{S}", new[] { "NoseSneer@{s}" }),
            ("CheekSquint{S}", new[] { "CheekSquint@{s}" }),
            ("CheekPuff{S}", new[] { "CheekPuff@{s}" }),
            ("CheekSuck{S}", new[] { "CheekSuck@{s}" }),
            ("LipSuckUpper{S}", new[] { "LipSuckUpper@{s}", "MouthRollUpper@{s}" }),
            ("LipSuckLower{S}", new[] { "LipSuckLower@{s}", "MouthRollLower@{s}" }),
            ("LipFunnelUpper{S}", new[] { "LipFunnelUpper@{s}", "LipFunnel@{s}*0.5", "MouthFunnel@{s}*0.5" }),
            ("LipFunnelLower{S}", new[] { "LipFunnelLower@{s}", "LipFunnel@{s}*0.5", "MouthFunnel@{s}*0.5" }),
            ("LipPuckerUpper{S}", new[] { "LipPucker{S}*0.5", "LipPucker@{s}*0.5", "MouthPucker@{s}*0.5" }),
            ("LipPuckerLower{S}", new[] { "LipPucker{S}*0.5", "LipPucker@{s}*0.5", "MouthPucker@{s}*0.5" }),
            ("MouthUpperUp{S}", new[] { "MouthUpperUp@{s}" }),
            ("MouthLowerDown{S}", new[] { "MouthLowerDown@{s}" }),
            ("MouthUpper{S}", new[] { "Mouth{S}*0.5" }),
            ("MouthLower{S}", new[] { "Mouth{S}*0.5" }),
            ("MouthCornerPull{S}", new[] { "MouthSmile{S}*0.8", "MouthSmile@{s}*0.8" }),
            ("MouthCornerSlant{S}", new[] { "MouthSmile{S}*0.2", "MouthSmile@{s}*0.2" }),
            ("MouthDimple{S}", new[] { "MouthDimple@{s}" }),
            ("MouthFrown{S}", new[] { "MouthSad{S}", "MouthFrown@{s}", "MouthSad@{s}" }),
            ("MouthStretch{S}", new[] { "MouthStretch@{s}" }),
            ("MouthPress{S}", new[] { "MouthPress@{s}" }),
            ("MouthTightener{S}", new[] { "MouthTightener@{s}" }),
            ("MouthClosed", new[] { "MouthClose" }),
            ("MouthRaiserUpper", new[] { "MouthShrugUpper" }),
            ("MouthRaiserLower", new[] { "MouthShrugLower" }),
            // A two-step tongue: the first step alone half out, both all the way.
            ("TongueOut", new[] { "TongueOutStep1:50;TongueOutStep1+TongueOutStep2:100", "TongueOutStep1" }),
        };

        struct Term { public int shape, side; public float k; }

        static int MakeMissingShapes(BridgeContext ctx, CVRFaceTracking faceTracking, SkinnedMeshRenderer smr)
        {
            var targets = faceTracking.CurrentShapeNames;
            var slots = faceTracking.FaceBlendShapes;
            var src = smr.sharedMesh;
            if (targets == null || slots == null || slots.Length != targets.Length || src == null)
            {
                return 0;
            }
            var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < src.blendShapeCount; i++)
            {
                if (!names.ContainsKey(src.GetBlendShapeName(i))) names[src.GetBlendShapeName(i)] = i;
            }
            var recipes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var (slot, list) in Recipes)
            {
                if (!slot.Contains("{S}")) { recipes[slot] = list; continue; }
                foreach (var (S, side) in new[] { ("Right", "R"), ("Left", "L") })
                    recipes[slot.Replace("{S}", S)] = list.Select(r => r.Replace("{S}", S).Replace("{s}", side)).ToArray();
            }

            var plans = new List<(int slot, List<(float weight, List<Term> terms)> frames)>();
            for (int i = 0; i < targets.Length; i++)
            {
                if (!string.IsNullOrEmpty(slots[i]) && slots[i] != "-none-") continue;
                // ARKit's camelCase is the same name: the CCK's matcher is exact.
                if (names.TryGetValue(targets[i], out int same)) { slots[i] = src.GetBlendShapeName(same); continue; }
                if (!recipes.TryGetValue(targets[i], out var list)) continue;
                foreach (var recipe in list)
                {
                    var frames = Parse(recipe, names);
                    if (frames == null) continue;
                    var only = frames[0].terms[0];
                    if (frames.Count == 1 && frames[0].terms.Count == 1 && only.side == 0 && Mathf.Approximately(only.k, 1f) && Mathf.Approximately(frames[0].weight, 100f))
                        slots[i] = src.GetBlendShapeName(only.shape);
                    else
                        plans.Add((i, frames));
                    break;
                }
            }
            faceTracking.FaceBlendShapes = slots;
            if (plans.Count == 0) return 0;

            float[] right = RightOfMidline(ctx, smr);
            if (right == null) return 0;
            int n = src.vertexCount;
            var mesh = UnityEngine.Object.Instantiate(src);
            mesh.name = src.name;
            var deltas = new Dictionary<int, (Vector3[] v, Vector3[] nr, Vector3[] t)>();
            (Vector3[] v, Vector3[] nr, Vector3[] t) Delta(int shape)
            {
                if (deltas.TryGetValue(shape, out var d)) return d;
                int last = src.GetBlendShapeFrameCount(shape) - 1;
                d = (new Vector3[n], new Vector3[n], new Vector3[n]);
                src.GetBlendShapeFrameVertices(shape, last, d.v, d.nr, d.t);
                // Brought to a full-weight frame, so a shape authored at 50 is not halved.
                float w = src.GetBlendShapeFrameWeight(shape, last);
                if (w > 0f && !Mathf.Approximately(w, 100f))
                    for (int i = 0; i < n; i++) { d.v[i] *= 100f / w; d.nr[i] *= 100f / w; d.t[i] *= 100f / w; }
                return deltas[shape] = d;
            }
            foreach (var (slot, frames) in plans)
            {
                foreach (var (weight, terms) in frames)
                {
                    var v = new Vector3[n]; var nr = new Vector3[n]; var t = new Vector3[n];
                    foreach (var term in terms)
                    {
                        var d = Delta(term.shape);
                        for (int i = 0; i < n; i++)
                        {
                            float k = term.k * (term.side > 0 ? right[i] : term.side < 0 ? 1f - right[i] : 1f);
                            if (k == 0f) continue;
                            v[i] += d.v[i] * k; nr[i] += d.nr[i] * k; t[i] += d.t[i] * k;
                        }
                    }
                    mesh.AddBlendShapeFrame(targets[slot], weight, v, nr, t);
                }
                slots[slot] = targets[slot];
            }
            string dir = ctx.OutputDir.TrimEnd('/') + "/FaceTracking";
            OutputAssetPaths.EnsureFolder(dir);
            AssetDatabase.CreateAsset(mesh, OutputAssetPaths.Claim($"{dir}/{OutputAssetPaths.SafeFileName(src.name)}.asset"));
            smr.sharedMesh = mesh;
            faceTracking.FaceBlendShapes = slots;
            return plans.Count;
        }

        // Null when any shape a frame names is missing.
        static List<(float weight, List<Term> terms)> Parse(string recipe, Dictionary<string, int> names)
        {
            var frames = new List<(float, List<Term>)>();
            foreach (string text in recipe.Split(';'))
            {
                string body = text;
                float weight = 100f;
                int colon = text.LastIndexOf(':');
                if (colon >= 0)
                {
                    weight = float.Parse(text.Substring(colon + 1), System.Globalization.CultureInfo.InvariantCulture);
                    body = text.Substring(0, colon);
                }
                var terms = new List<Term>();
                foreach (string raw in body.Split('+'))
                {
                    string name = raw.Trim();
                    float k = 1f;
                    int side = 0;
                    int star = name.IndexOf('*');
                    if (star >= 0) { k = float.Parse(name.Substring(star + 1), System.Globalization.CultureInfo.InvariantCulture); name = name.Substring(0, star); }
                    int at = name.IndexOf('@');
                    if (at >= 0) { side = name.Substring(at + 1) == "R" ? 1 : -1; name = name.Substring(0, at); }
                    if (!names.TryGetValue(name, out int shape)) return null;
                    terms.Add(new Term { shape = shape, side = side, k = k });
                }
                frames.Add((weight, terms));
            }
            return frames;
        }

        // Per vertex, 1 on the avatar's right of the face's middle and 0 on its left, eased over
        // 6 mm across it, at the pose the clone stands in. Null without a humanoid head.
        static float[] RightOfMidline(BridgeContext ctx, SkinnedMeshRenderer smr)
        {
            var animator = ctx.Target.GetComponent<Animator>();
            var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head == null) return null;
            var l = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var r = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Vector3 across = l != null && r != null && (r.position - l.position).sqrMagnitude > 1e-8f
                ? (r.position - l.position).normalized : ctx.Target.transform.right;
            var baked = new Mesh();
            // Without the scale flag: with it, a body under a scaled parent came out scaled twice.
            smr.BakeMesh(baked);
            var v = baked.vertices;
            UnityEngine.Object.DestroyImmediate(baked);
            var t = smr.transform;
            const float half = 0.003f;
            var right = new float[v.Length];
            for (int i = 0; i < v.Length; i++)
                right[i] = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-half, half, Vector3.Dot(t.position + t.rotation * v[i] - head.position, across)));
            return right;
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

            var plains = ShapeNames(mesh.sharedMesh).plain;

            int filled = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                if (!string.IsNullOrEmpty(slots[i]) && slots[i] != "-none-")
                {
                    continue;   // ChilloutVR already matched this one
                }
                string target = Simplify(targets[i].ToLowerInvariant());
                int best = -1;
                int bestLength = 0;
                for (int s = 0; s < plains.Count; s++)
                {
                    string plain = plains[s];
                    if (plain.Length <= bestLength || !IsSideVariant(target, plain))
                    {
                        continue;
                    }
                    best = s;
                    bestLength = plain.Length;
                }
                if (best >= 0)
                {
                    slots[i] = mesh.sharedMesh.GetBlendShapeName(best);
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
                if (smr.sharedMesh == null || smr.sharedMesh.blendShapeCount == 0 || IsDebugMesh(smr, root.transform))
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
            if (named != null && named != mesh && named.sharedMesh != null && !IsDebugMesh(named, root.transform))
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

        // Lowered and simplified once per mesh: every target is compared against every shape.
        static (List<string> lowered, List<string> plain) ShapeNames(Mesh mesh)
        {
            var lowered = new List<string>(mesh.blendShapeCount);
            var plain = new List<string>(mesh.blendShapeCount);
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i).ToLowerInvariant();
                lowered.Add(name);
                plain.Add(Simplify(name));
            }
            return (lowered, plain);
        }

        static int CountMatches((List<string> lowered, List<string> plain) shapes, string[] targets)
        {
            int count = 0;
            foreach (var target in targets)
            {
                string t = target.ToLowerInvariant();
                string tPlain = Simplify(t);
                bool hit = false;
                for (int i = 0; i < shapes.lowered.Count; i++)
                {
                    string shape = shapes.lowered[i];
                    string sPlain = shapes.plain[i];
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
            if (rest.Length > 0 && System.Array.IndexOf(DirectionalTargets, targetPlain) >= 0)
            {
                return false;
            }
            return System.Array.IndexOf(SideWords, rest) >= 0;
        }

        static readonly string[] SideWords =
        {
            "", "left", "right", "upper", "lower",
            "upperleft", "upperright", "lowerleft", "lowerright",
        };

        // On these the direction IS the movement, not a side of one shape: a plain "Tongue"
        // standing in for TongueLeft sticks the tongue out when it should move sideways.
        static readonly string[] DirectionalTargets =
        {
            "jawleft", "jawright", "tongueleft", "tongueright",
            "mouthupperleft", "mouthupperright", "mouthlowerleft", "mouthlowerright",
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

        // Face-tracking add-ons recognised by their installer's type, under the name users know.
        // Each entry was read from the package itself.
        static readonly (string type, string name)[] AddOns =
        {
            ("Triturbo.FaceTrackingFramework.Installer.FaceTrackingInstaller", "Triturbo Face Tracking Framework"),
        };

        // Where an avatar without tracking shapes should have found them. An installer that works
        // on a copy leaves the original beside it, and the original is the one that gets
        // converted; an add-on installed and never run looks the same from inside the avatar.
        // Null when neither applies.
        internal static string WhereTheShapesAre(GameObject root, out GameObject copy)
        {
            copy = TrackedCopyOf(root);
            if (copy != null)
            {
                return $"\"{copy.name}\" in this scene wears the same mesh with tracking shapes added: " +
                       "if face tracking was installed, that is the avatar to convert.";
            }
            var addOn = AddOns.FirstOrDefault(a => AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
            {
                try { return assembly.GetType(a.type, false) != null; }
                catch { return false; }
            }));
            return addOn.name == null ? null
                : $"\"{addOn.name}\" is installed in this project, but this avatar does not wear its shapes: " +
                  "run its installer on the avatar, then convert the one it gives back.";
        }

        // A scene root wearing one of this avatar's meshes, matched by renderer name and vertex
        // count, with tracking shapes added. Roots only, as an installer's copy sits beside its
        // original; converted avatars are skipped, being outputs rather than sources.
        static GameObject TrackedCopyOf(GameObject root)
        {
            var own = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null)
                .Select(r => (r.name, r.sharedMesh.vertexCount))
                .ToList();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }
                foreach (var other in scene.GetRootGameObjects())
                {
                    if (other == root || other.GetComponent<CVRAvatar>() != null
                        || !DetectShapes(other, null, out var mesh, out _, out _))
                    {
                        continue;
                    }
                    if (own.Contains((mesh.name, mesh.sharedMesh.vertexCount)))
                    {
                        return other;
                    }
                }
            }
            return null;
        }

        // Internal so the face mesh finder can share it. The panel carries every tracking
        // shape, so it outscores the real face. It is usually named innocuously and betrayed by its parents
        // instead (VRCFury - Face Tracking - UE Blendshapes/VF_UE_Debug/WorldObject/Window/FT_Debug).
        // The walk stops below the avatar root: a scene parent named "Debug" is not the panel.
        internal static bool IsDebugMesh(SkinnedMeshRenderer smr, Transform root)
        {
            for (var t = smr.transform; t != null && t != root; t = t.parent)
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
