#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using ABI.CCK.Components;

namespace AvatarBridge
{
    // Reads avatar features straight off the meshes and rig, no VRC
    // descriptor involved. Fallbacks for the conversion path; Setup
    // mode relies on them entirely.
    public static class AvatarFeatureDetect
    {
        // ------------------------------------------- face tracking parameters ----

        static readonly HashSet<string> FtPlainNames = new HashSet<string>
        {
            "EyesY", "LeftEyeX", "RightEyeX",
            "LeftEyeLidExpandedSqueeze", "RightEyeLidExpandedSqueeze", "EyesDilation"
        };

        static readonly HashSet<string> FtGateNames = new HashSet<string>
        {
            "EyeTracking", "FaceTracking", "EyeTrackingActive", "LipTrackingActive",
            "FacialExpressionsDisabled"
        };

        public static bool IsFaceTrackingParameter(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Contains("/Smoothed/"))
            {
                return false;
            }
            if (name.Contains("v2/"))
            {
                return true;
            }
            string shortName = FaceTrackingShortName(name);
            return FtPlainNames.Contains(shortName) || FtGateNames.Contains(shortName);
        }

        public static bool IsFaceTrackingGate(string name)
        {
            return IsFaceTrackingParameter(name) && FtGateNames.Contains(FaceTrackingShortName(name));
        }

        public static string FaceTrackingShortName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }
            string trimmed = name.TrimStart('#');
            int slash = trimmed.LastIndexOf('/');
            return slash >= 0 && slash < trimmed.Length - 1 ? trimmed.Substring(slash + 1) : trimmed;
        }

        // ------------------------------------------------------------------- blink ----

        public static void DetectBlinkShapes(Mesh mesh, out string left, out string right, out string combined)
        {
            left = right = combined = null;
            if (mesh == null)
            {
                return;
            }

            var lefts = new List<string>();
            var rights = new List<string>();
            var combineds = new List<string>();
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                string lower = name.ToLowerInvariant();
                if (!lower.Contains("blink"))
                {
                    continue;
                }
                if (IsSide(lower, "left", 'l'))
                {
                    lefts.Add(name);
                }
                else if (IsSide(lower, "right", 'r'))
                {
                    rights.Add(name);
                }
                else
                {
                    combineds.Add(name);
                }
            }
            combined = PlainestBlink(combineds);

            // Prefer two shapes equal but for the side token. A mesh
            // often carries several blink families, and independent
            // picks can pair eyes from different ones.
            foreach (string candidateLeft in lefts)
            {
                string stem = SideStem(candidateLeft, "left", 'l');
                string match = rights.FirstOrDefault(r => SideStem(r, "right", 'r') == stem);
                if (match != null)
                {
                    left = candidateLeft;
                    right = match;
                    return;
                }
            }

            left = lefts.FirstOrDefault();
            right = rights.FirstOrDefault();
        }

        static string PlainestBlink(List<string> names)
        {
            string best = null;
            int bestExtra = int.MaxValue;
            foreach (string name in names)
            {
                string core = Regex.Replace(name.ToLowerInvariant(), @"[ _.\-]", "");
                if (core.StartsWith("vrc"))
                {
                    core = core.Substring(3);
                }
                int extra = core.Length - "blink".Length;
                if (extra < bestExtra)
                {
                    bestExtra = extra;
                    best = name;
                }
            }
            return best;
        }

        static string SideStem(string name, string word, char letter)
        {
            string lower = name.ToLowerInvariant();
            lower = Regex.Replace(lower, word, "");
            lower = Regex.Replace(lower, $@"(^|[ _.\-]){letter}([ _.\-]|$)", "$1$2");
            return Regex.Replace(lower, @"[ _.\-]+", "");
        }

        public static bool IsSide(string lower, string word, char letter)
        {
            return lower.Contains(word)
                   || Regex.IsMatch(lower, $@"(^|[ _.\-]){letter}([ _.\-]|$)");
        }

        public static void SetBlinkMode(CVRAvatar cvrAvatar, string modeName)
        {
            foreach (var f in cvrAvatar.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!f.FieldType.IsEnum)
                {
                    continue;
                }
                var names = Enum.GetNames(f.FieldType);
                if (names.Contains("Separate") && names.Contains("Combined") && names.Contains(modeName))
                {
                    f.SetValue(cvrAvatar, Enum.Parse(f.FieldType, modeName));
                    return;
                }
            }
        }

        // ----------------------------------------------------------------- visemes ----

        public static readonly string[] VisemeOrder =
        {
            "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS",
            "nn", "RR", "aa", "E", "ih", "oh", "ou"
        };

        public static string[] DetectVisemes(Mesh mesh)
        {
            if (mesh == null)
            {
                return null;
            }
            var byLower = new System.Collections.Generic.Dictionary<string, string>();
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string n = mesh.GetBlendShapeName(i);
                string key = n.ToLowerInvariant();
                if (!byLower.ContainsKey(key))
                {
                    byLower[key] = n;
                }
            }

            var result = new string[VisemeOrder.Length];
            int found = 0;
            for (int i = 0; i < VisemeOrder.Length; i++)
            {
                string v = VisemeOrder[i].ToLowerInvariant();
                foreach (var candidate in new[] { "vrc.v_" + v, "vrc_v_" + v, "v_" + v, "vis_" + v, "viseme_" + v, v })
                {
                    if (byLower.TryGetValue(candidate, out var actual))
                    {
                        result[i] = actual;
                        found++;
                        break;
                    }
                }
            }
            return found >= VisemeOrder.Length / 2 ? result : null;
        }

        // --------------------------------------------------------------- face mesh ----

        public static SkinnedMeshRenderer FindFaceMesh(GameObject root)
        {
            var meshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in meshes)
            {
                if (smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0 &&
                    string.Equals(smr.name, "Body", StringComparison.OrdinalIgnoreCase))
                {
                    return smr;
                }
            }
            SkinnedMeshRenderer best = null;
            int bestCount = 0;
            foreach (var smr in meshes)
            {
                var m = smr.sharedMesh;
                if (m == null || m.blendShapeCount == 0 || IsDebugMesh(smr.name))
                {
                    continue;
                }
                if (m.blendShapeCount > bestCount)
                {
                    bestCount = m.blendShapeCount;
                    best = smr;
                }
            }
            return best;
        }

        static bool IsDebugMesh(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("debug") || n.Contains("vf_ue") || n.Contains("ft_debug");
        }

        // --------------------------------------------------------------- viewpoint ----

        public static Vector3 EstimateViewPosition(GameObject root, Animator animator)
        {
            Transform head = null, leftEye = null, rightEye = null;
            if (animator != null && animator.isHuman)
            {
                head = animator.GetBoneTransform(HumanBodyBones.Head);
                leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
                rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            }

            Vector3 world;
            if (leftEye != null && rightEye != null)
            {
                world = (leftEye.position + rightEye.position) * 0.5f;
            }
            else if (head != null)
            {
                // No eye bones: sit slightly above and ahead of the head bone.
                world = head.position + root.transform.up * 0.06f;
            }
            else
            {
                // Not humanoid at all; visual bounds as a last resort.
                var bounds = CalculateBounds(root);
                world = new Vector3(bounds.center.x, bounds.max.y - bounds.size.y * 0.08f, bounds.center.z);
            }

            // Push forward toward the face so the camera isn't inside the head.
            world += root.transform.forward * 0.04f;
            return RootOffset(root, world);
        }

        // ---------------------------------------------- the CCK's own Auto placement ----

        // Blender's ".L"/".R" suffix convention included. Unmapped eye
        // bones with those names otherwise fall through to the blind
        // fallback, and Blender exports that suffix by default.
        static readonly string[] LeftEyeNameVariants =
            { "LeftEye", "Left_Eye", "EyeLeft", "Eye_Left", "eye.L", "eye_L", "eyeL", "L_eye", "Eye.L.001" };
        static readonly string[] RightEyeNameVariants =
            { "RightEye", "Right_Eye", "EyeRight", "Eye_Right", "eye.R", "eye_R", "eyeR", "R_eye", "Eye.R.001" };

        public static bool CckAutoViewPosition(GameObject root, Animator animator, out Vector3 localPosition)
        {
            localPosition = default;
            if (animator == null || !animator.isHuman)
            {
                return false;
            }
            var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);

            Vector3 world;
            if (leftEye != null && rightEye != null)
            {
                world = (leftEye.position + rightEye.position) / 2f;
            }
            else if (leftEye != null || rightEye != null)
            {
                world = ProjectSingleEye(animator, leftEye != null ? leftEye : rightEye);
            }
            else if (head != null)
            {
                // Under the head first, then anywhere still near it.
                // Eye bones are not always children of the head bone;
                // rigs park them elsewhere whenever something else
                // drives the eyes. NearHeadByNameVariants widens the
                // net but only accepts bones within head distance.
                var namedLeft = FindChildByNameVariants(head, LeftEyeNameVariants);
                var namedRight = FindChildByNameVariants(head, RightEyeNameVariants);
                if (namedLeft == null || namedRight == null)
                {
                    namedLeft = namedLeft != null ? namedLeft : NearHeadByNameVariants(root, animator, head, LeftEyeNameVariants);
                    namedRight = namedRight != null ? namedRight : NearHeadByNameVariants(root, animator, head, RightEyeNameVariants);
                }
                if (namedLeft != null && namedRight != null)
                {
                    world = (namedLeft.position + namedRight.position) / 2f;
                }
                else if (namedLeft != null || namedRight != null)
                {
                    world = ProjectSingleEye(animator, namedLeft != null ? namedLeft : namedRight);
                }
                else
                {
                    var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                    if (hips == null)
                    {
                        return false;
                    }
                    float headBoneHeight = Vector3.Distance(hips.position, head.position);
                    world = OffsetFromBone(head, new Vector3(
                        0f, -0.1f * headBoneHeight, 0.1f * headBoneHeight));
                }
            }
            else
            {
                return false;
            }
            localPosition = RoundNearZero(RootOffset(root, world));
            return true;
        }

        public static bool ViewpointSitsAboveEyes(GameObject root, Animator animator,
            Vector3 localViewpoint, out float above, out float eyeSeparation)
        {
            above = 0f;
            eyeSeparation = 0f;
            if (root == null || animator == null || !animator.isHuman)
            {
                return false;
            }
            var left = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var right = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (left == null || right == null)
            {
                return false; // needs both, since the separation is the yardstick
            }
            eyeSeparation = Vector3.Distance(left.position, right.position);
            if (eyeSeparation < 0.0001f)
            {
                return false;
            }
            Vector3 eyes = RootOffset(root, (left.position + right.position) * 0.5f);
            above = Vector3.Dot(localViewpoint - eyes, Vector3.up);
            return above > eyeSeparation;
        }

        public static bool JawIsBelievable(GameObject root, Animator animator, Transform jaw, out string why)
        {
            why = null;
            if (root == null || animator == null || jaw == null)
            {
                return true;
            }
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null)
            {
                return true; // nothing to measure against; the mapping is all there is
            }

            var left = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var right = animator.GetBoneTransform(HumanBodyBones.RightEye);
            Vector3 up = root.transform.up;
            if (left != null || right != null)
            {
                Vector3 eyes = left != null && right != null ? (left.position + right.position) * 0.5f
                    : (left != null ? left.position : right.position);
                float above = Vector3.Dot(jaw.position - eyes, up);
                if (above > 0f)
                {
                    why = $"{above:0.##} m above the eyes — a jaw hinges below them";
                    return false;
                }
            }

            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            float reach = hips != null
                ? Mathf.Max(0.12f, Vector3.Distance(hips.position, head.position) * 0.25f)
                : 0.35f;
            float away = Vector3.Distance(jaw.position, head.position);
            if (away > reach)
            {
                why = $"{away:0.##} m from the head bone, past the {reach:0.##} m this rig's own " +
                      "proportions allow for a skull";
                return false;
            }
            return true;
        }

        public static bool CckAutoVoicePosition(GameObject root, Animator animator, out Vector3 localPosition)
        {
            localPosition = default;
            if (animator == null || !animator.isHuman)
            {
                return false;
            }
            var jaw = animator.GetBoneTransform(HumanBodyBones.Jaw);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            // A mapped Jaw is only used if it is somewhere a jaw could
            // be; the slot is optional and unvalidated. On failure
            // return false so the caller reaches MouthLocator.
            if (jaw != null && !JawIsBelievable(root, animator, jaw, out _))
            {
                return false;
            }
            // No jaw means only a head-bone guess. MouthLocator does
            // better whenever visemes exist: an open-mouth shape's
            // deltas are the mouth, measured. Hand the decision over.
            //
            // Found on an avatar with 15 viseme blendshapes whose voice still landed mid-face,
            // because this path answered first and never mentioned the visemes existed.
            if (jaw == null)
            {
                return false;
            }
            if (jaw == null)
            {
                return false;
            }
            localPosition = RootOffset(root, jaw.position);
            return true;
        }

        public static bool HeadVoiceOffset(GameObject root, Animator animator, out Vector3 localPosition)
        {
            localPosition = default;
            var head = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (root == null || head == null)
            {
                return false;
            }
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            float span = hips != null ? Vector3.Distance(hips.position, head.position) : 0f;
            localPosition = RootOffset(root, OffsetFromBone(head, span > 0.0001f
                ? new Vector3(0f, 0.008f * span, 0.1f * span)
                : new Vector3(0f, 0.005f, 0.06f)));
            return true;
        }

        public static bool DecoyRigAnchors(GameObject root, Animator animator,
            out Transform head, out Transform leftEye, out Transform rightEye)
        {
            head = null;
            leftEye = null;
            rightEye = null;
            if (root == null || animator == null || !animator.isHuman)
            {
                return false;
            }

            var humanoid = new HashSet<Transform>();
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone)
                {
                    continue;
                }
                var mapped = animator.GetBoneTransform(bone);
                if (mapped != null)
                {
                    humanoid.Add(mapped);
                }
            }

            var headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            var leftEyeBone = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var rightEyeBone = animator.GetBoneTransform(HumanBodyBones.RightEye);

            // The decoy test. A constraint reading the head is not
            // enough; ordinary rigs read the head for unrelated things.
            // What distinguishes a decoy is invisible bones: the map
            // points at stand-ins deforming no mesh. Being a decoy is a
            // property of the whole rig, so the whole rig is measured.
            // The threshold is loose; a true decoy scores zero and a
            // real humanoid clears a fifth on its fingers alone.
            if (headBone == null || humanoid.Count == 0)
            {
                return false;
            }
            var deforming = DeformingBones(root);
            int deformingHumanoid = 0;
            foreach (var bone in humanoid)
            {
                if (deforming.Contains(bone))
                {
                    deformingHumanoid++;
                }
            }
            if (deformingHumanoid > humanoid.Count * 0.2f)
            {
                return false;
            }

            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue;
                }
                var driven = ConstraintTarget(component);
                if (driven == null || humanoid.Contains(driven))
                {
                    continue;
                }
                foreach (var source in ConstraintSources(component))
                {
                    if (source == null)
                    {
                        continue;
                    }
                    if (source == headBone && head == null)
                    {
                        head = driven;
                    }
                    else if (source == leftEyeBone && leftEye == null)
                    {
                        leftEye = driven;
                    }
                    else if (source == rightEyeBone && rightEye == null)
                    {
                        rightEye = driven;
                    }
                }
            }
            // Both relayed eyes, or nothing. A puppet rig can read the
            // head as an input that swings the body; position and the
            // constraint type cannot tell that from a relay. Relayed
            // eyes can: an eye bone exists to aim eyeballs, so driving
            // from one means reproducing a face. A decoy mapping no eye
            // bones falls back to the authored viewpoint, the safe
            // direction to fail in.
            return leftEye != null && rightEye != null;
        }

        public static bool ExcludeVisibleHeadFromFirstPerson(Animator animator, Transform visibleHead)
        {
            if (visibleHead == null)
            {
                return false;
            }
            // An ordinary rig needs nothing: the client's own auto-add already targets this bone.
            var humanoidHead = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Head)
                : null;
            if (humanoidHead == visibleHead || visibleHead.GetComponent<FPRExclusion>() != null)
            {
                return false;
            }
            var exclusion = visibleHead.gameObject.AddComponent<FPRExclusion>();
            exclusion.target = visibleHead;
            exclusion.isShown = false;
            exclusion.shrinkToZero = true;
            return true;
        }

        public static void HumanoidDeformShare(GameObject root, Animator animator,
            out int mapped, out int deforming)
        {
            mapped = 0;
            deforming = 0;
            if (root == null || animator == null || !animator.isHuman)
            {
                return;
            }
            var deformingBones = DeformingBones(root);
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone)
                {
                    continue;
                }
                var t = animator.GetBoneTransform(bone);
                if (t == null)
                {
                    continue;
                }
                mapped++;
                if (deformingBones.Contains(t))
                {
                    deforming++;
                }
            }
        }

        public static bool RescueViewpointOffBody(GameObject root, Animator animator,
            Vector3 storedView, out Vector3 localPosition, out string detail)
        {
            localPosition = default;
            detail = null;
            if (root == null)
            {
                return false;
            }

            // Distance to the nearest deforming bone, not a bounding
            // box. A quadruped's box is enormous and containment
            // proves nothing. Deforming bones
            // are where the body genuinely is.
            var deforming = DeformingBones(root).Where(b => b != null).ToList();
            if (deforming.Count == 0)
            {
                return false;
            }

            // The same tolerance every other head check uses, so one rig can't be judged by two
            // different standards.
            var head = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            var hips = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            float tolerance = head != null && hips != null
                ? Mathf.Max(0.15f, Vector3.Distance(hips.position, head.position) * 0.5f)
                : 0.5f;

            float NearestDeforming(Vector3 point)
            {
                float best = float.MaxValue;
                foreach (var bone in deforming)
                {
                    best = Mathf.Min(best, Vector3.Distance(bone.position, point));
                }
                return best;
            }

            float currentOff = NearestDeforming(CckGizmoWorldPoint(root, storedView));
            if (currentOff <= tolerance)
            {
                return false;   // already on the body — nothing to rescue, and nothing to disturb
            }

            var candidates = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.IndexOf("eye", StringComparison.OrdinalIgnoreCase) >= 0
                    && NearestDeforming(t.position) <= tolerance)
                {
                    candidates.Add(t);
                }
            }
            if (candidates.Count == 0)
            {
                return false;
            }

            // Left/right pairs first: a pair straddling the centreline is a face, and its midpoint
            // is where the eyes are. Falling back to the centroid of whatever is left keeps a
            // single-marker rig working rather than refusing.
            var left = candidates.Where(t => t.position.x < root.transform.position.x).ToList();
            var right = candidates.Where(t => t.position.x > root.transform.position.x).ToList();
            Vector3 world;
            if (left.Count > 0 && right.Count > 0)
            {
                world = (Average(left) + Average(right)) / 2f;
                detail = $"midway between {left.Count} left and {right.Count} right eye marker(s), " +
                         $"e.g. \"{left[0].name}\" / \"{right[0].name}\"";
            }
            else
            {
                world = Average(candidates);
                detail = $"the average of {candidates.Count} eye marker(s) inside the body, " +
                         $"e.g. \"{candidates[0].name}\"";
            }

            localPosition = RoundNearZero(RootOffset(root, world));
            return true;
        }

        public static bool PointIsOffBody(GameObject root, Animator animator, Vector3 stored)
        {
            if (root == null)
            {
                return false;
            }
            var deforming = DeformingBones(root).Where(b => b != null).ToList();
            if (deforming.Count == 0)
            {
                return false;
            }
            var head = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            var hips = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            float tolerance = head != null && hips != null
                ? Mathf.Max(0.15f, Vector3.Distance(hips.position, head.position) * 0.5f)
                : 0.5f;

            var point = CckGizmoWorldPoint(root, stored);
            foreach (var bone in deforming)
            {
                if (Vector3.Distance(bone.position, point) <= tolerance)
                {
                    return false;
                }
            }
            return true;
        }

        static Vector3 Average(List<Transform> transforms)
        {
            var sum = Vector3.zero;
            foreach (var t in transforms)
            {
                sum += t.position;
            }
            return sum / transforms.Count;
        }

        static readonly string[] JawNameVariants =
            { "Jaw", "jaw", "LowerJaw", "Jaw_L", "Mouth", "mouth", "Chin", "Snout" };

        public static bool DecoyRigPlacement(GameObject root, Animator animator,
            out Vector3 viewLocal, out Vector3 voiceLocal, out Transform visibleHead, out string detail)
        {
            viewLocal = default;
            voiceLocal = default;
            visibleHead = null;
            detail = null;
            if (!DecoyRigAnchors(root, animator, out var head, out var leftEye, out var rightEye))
            {
                return false;
            }
            visibleHead = head;

            // The avatar's own size, so the fallback offsets below scale with it rather than
            // assuming a human head. Taken off the humanoid rig because that is the one chain
            // guaranteed to exist, and it is the right order of magnitude either way.
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            float span = hips != null && headBone != null
                ? Vector3.Distance(hips.position, headBone.position)
                : 0f;

            // DecoyRigAnchors guarantees both eyes; there is no head-only guess any more, because
            // guessing a face from one relayed bone is what put a taur's viewpoint at its hips.
            Vector3 viewWorld = (leftEye.position + rightEye.position) / 2f;
            string viewFrom = $"midway between \"{leftEye.name}\" and \"{rightEye.name}\"";

            string voiceFrom;
            Vector3 voiceWorld;
            var jaw = head != null ? FindChildByNameVariants(head, JawNameVariants) : null;
            if (jaw != null)
            {
                voiceWorld = jaw.position;
                voiceFrom = $"at \"{jaw.name}\"";
            }
            else if (head != null)
            {
                voiceWorld = OffsetFromBone(head, span > 0.0001f
                    ? new Vector3(0f, 0.008f * span, 0.1f * span)
                    : new Vector3(0f, 0.005f, 0.06f));
                voiceFrom = $"just in front of \"{head.name}\"";
            }
            else
            {
                voiceWorld = viewWorld;
                voiceFrom = "with the viewpoint (no visible head bone to work from)";
            }

            viewLocal = RoundNearZero(RootOffset(root, viewWorld));
            voiceLocal = RootOffset(root, voiceWorld);
            detail = $"View {viewFrom}; voice {voiceFrom}";
            return true;
        }

        static HashSet<Transform> DeformingBones(GameObject root)
        {
            var deforming = new HashSet<Transform>();
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = skin.sharedMesh;
                var bones = skin.bones;
                if (mesh == null || bones == null || bones.Length == 0)
                {
                    continue;
                }
                try
                {
                    var weights = mesh.GetAllBoneWeights();
                    for (int i = 0; i < weights.Length; i++)
                    {
                        var weight = weights[i];
                        if (weight.weight > 0f && weight.boneIndex >= 0 && weight.boneIndex < bones.Length
                            && bones[weight.boneIndex] != null)
                        {
                            deforming.Add(bones[weight.boneIndex]);
                        }
                    }
                }
                catch
                {
                    // Unreadable mesh: assume every listed bone deforms.
                    // That only makes the check more cautious.
                    foreach (var bone in bones)
                    {
                        if (bone != null)
                        {
                            deforming.Add(bone);
                        }
                    }
                }
            }
            return deforming;
        }

        static Transform ConstraintTarget(Component component)
        {
            if (component is UnityEngine.Animations.IConstraint)
            {
                return component.transform;
            }
            string typeName = component.GetType().Name;
            if (!typeName.StartsWith("VRC", StringComparison.Ordinal) ||
                !typeName.EndsWith("Constraint", StringComparison.Ordinal))
            {
                return null;
            }
            // Not "?? component.transform". An unassigned Transform is
            // Unity's fake null: overloaded == says null, ?? passes it.
            var target = Field<Transform>(component, "TargetTransform");
            return target != null ? target : component.transform;
        }

        static IEnumerable<Transform> ConstraintSources(Component component)
        {
            if (component is UnityEngine.Animations.IConstraint unity)
            {
                for (int i = 0; i < unity.sourceCount; i++)
                {
                    yield return unity.GetSource(i).sourceTransform;
                }
                yield break;
            }
            if (!(Field<object>(component, "Sources") is System.Collections.IEnumerable sources))
            {
                yield break;
            }
            foreach (var entry in sources)
            {
                if (entry == null)
                {
                    continue;
                }
                var transform = Field<Transform>(entry, "SourceTransform");
                if (transform != null)
                {
                    yield return transform;
                }
            }
        }

        static T Field<T>(object instance, string name)
        {
            if (instance == null)
            {
                return default;
            }
            var type = instance.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            object value = type.GetField(name, flags)?.GetValue(instance)
                           ?? type.GetProperty(name, flags)?.GetValue(instance);
            return value is T typed ? typed : default;
        }

        static Vector3 OffsetFromBone(Transform bone, Vector3 offsetInMetres)
        {
            // The AVATAR's orientation, not the bone's. A bone's local axes are whatever the
            // rigger felt like: on one robot avatar the head bone's +Z pointed at the sky, so
            // "6 cm forward" became "6 cm up" and the voice position sat above the eyes. The
            // avatar root is the one transform whose forward really is forward.
            var root = bone.root;
            return bone.position + root.rotation * offsetInMetres;
        }

        static Vector3 ProjectSingleEye(Animator animator, Transform singleEye)
        {
            var avatarRoot = animator.transform;
            Vector3 eyePosition = singleEye.position;
            Vector3 toEye = (eyePosition - avatarRoot.position).normalized;
            float dotForward = Vector3.Dot(toEye, avatarRoot.forward);
            float dotUp = Vector3.Dot(toEye, avatarRoot.up);
            float dotRight = Vector3.Dot(toEye, avatarRoot.right);
            if (Mathf.Abs(dotForward) > Mathf.Abs(dotUp) && Mathf.Abs(dotForward) > Mathf.Abs(dotRight))
            {
                return eyePosition - Vector3.Project(eyePosition - avatarRoot.position, avatarRoot.forward);
            }
            return Mathf.Abs(dotUp) > Mathf.Abs(dotRight)
                ? eyePosition - Vector3.Project(eyePosition - avatarRoot.position, avatarRoot.up)
                : eyePosition - Vector3.Project(eyePosition - avatarRoot.position, avatarRoot.right);
        }

        static Transform NearHeadByNameVariants(GameObject root, Animator animator, Transform head,
            string[] nameVariants)
        {
            if (root == null || head == null)
            {
                return null;
            }
            var hips = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Hips)
                : null;
            float tolerance = hips != null
                ? Mathf.Max(0.15f, Vector3.Distance(hips.position, head.position) * 0.5f)
                : 0.5f;

            Transform best = null;
            float bestDistance = float.MaxValue;
            foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
            {
                bool named = false;
                foreach (string variant in nameVariants)
                {
                    if (string.Equals(candidate.name, variant, StringComparison.OrdinalIgnoreCase))
                    {
                        named = true;
                        break;
                    }
                }
                if (!named)
                {
                    continue;
                }
                float distance = Vector3.Distance(candidate.position, head.position);
                if (distance <= tolerance && distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
            return best;
        }

        static Transform FindChildByNameVariants(Transform parent, string[] nameVariants)
        {
            foreach (string potentialName in nameVariants)
            {
                var child = parent.Find(potentialName);
                if (child != null)
                {
                    return child;
                }
                // Every descendant, not just direct children. Eye bones
                // routinely sit a few joints below the head. Safe to
                // widen: exact name match, never a substring.
                foreach (Transform candidate in parent.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate != parent
                        && string.Equals(candidate.name, potentialName, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }
            return null;
        }

        static Vector3 RoundNearZero(Vector3 position)
        {
            const float tolerance = 0.01f;
            return new Vector3(
                Mathf.Abs(position.x) < tolerance ? 0f : position.x,
                Mathf.Abs(position.y) < tolerance ? 0f : position.y,
                Mathf.Abs(position.z) < tolerance ? 0f : position.z);
        }

        static Vector3 RootOffset(GameObject root, Vector3 world)
        {
            return Vector3.Scale(root.transform.InverseTransformPoint(world),
                                 root.transform.localScale);
        }

        public static bool HeadDistance(GameObject root, Animator animator, Vector3 stored,
            out float distance, out float tolerance)
        {
            distance = 0f;
            tolerance = 0f;
            if (root == null || animator == null || !animator.isHuman)
            {
                return false;
            }
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null)
            {
                return false;
            }
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            tolerance = hips != null
                ? Mathf.Max(0.15f, Vector3.Distance(hips.position, head.position) * 0.5f)
                : 0.5f;
            distance = Vector3.Distance(CckGizmoWorldPoint(root, stored), head.position);
            return true;
        }

        public static void VerifyHeadPlacement(BridgeContext ctx, string category,
            Animator animator, Vector3 viewPosition, Vector3 voicePosition)
        {
            if (animator == null || !animator.isHuman)
            {
                return;
            }
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null)
            {
                return;
            }
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            float tolerance = hips != null
                ? Mathf.Max(0.15f, Vector3.Distance(hips.position, head.position) * 0.5f)
                : 0.5f;

            void Check(string what, Vector3 stored)
            {
                float distance = Vector3.Distance(
                    CckGizmoWorldPoint(ctx.Target, stored), head.position);
                if (distance <= tolerance)
                {
                    return;
                }
                ctx.Report.Warning(category,
                    $"{what} lands {distance:0.##} m from the head bone",
                    "Drawn where the CCK's own inspector draws it, that is far enough from the head " +
                    "to be wrong rather than merely unusual. The usual cause is the avatar sitting " +
                    "under a PARENT with a scale on it: ChilloutVR stores these positions against " +
                    "the avatar's own localScale, so a scaled ancestor moves them. Put the avatar at " +
                    "the top of the scene hierarchy (or clear the parent's scale) and convert again. " +
                    "Either way, check it before uploading — the CVRAvatar inspector's own Auto " +
                    "buttons place them exactly where this conversion aims to.");
            }

            Check("Viewpoint", viewPosition);
            Check("Voice position", voicePosition);
        }

        public static Vector3 CckGizmoWorldPoint(GameObject root, Vector3 stored)
        {
            var scale = root.transform.localScale;
            var inverse = new Vector3(
                Mathf.Approximately(scale.x, 0f) ? 0f : 1f / scale.x,
                Mathf.Approximately(scale.y, 0f) ? 0f : 1f / scale.y,
                Mathf.Approximately(scale.z, 0f) ? 0f : 1f / scale.z);
            return root.transform.TransformPoint(Vector3.Scale(stored, inverse));
        }

        public static Vector3 EstimateVoicePosition(GameObject root, Animator animator)
        {
            if (animator != null && animator.isHuman)
            {
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                {
                    return RootOffset(root, head.position);
                }
            }
            return EstimateViewPosition(root, animator);
        }

        static Bounds CalculateBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.one);
            }
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }
    }
}
#endif
