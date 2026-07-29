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
    /// <summary>
    /// Reads avatar features straight off the meshes and rig, with no VRChat descriptor
    /// involved. The conversion path uses these as fallbacks when the VRChat descriptor
    /// left something unset; Setup mode (no VRChat SDK at all) relies on them entirely.
    /// </summary>
    public static class AvatarFeatureDetect
    {
        // ------------------------------------------- face tracking parameters ----

        /// <summary>
        /// The eye/face parameters that carry no "v2/" anywhere in the name — the bundled
        /// CVR-VRCFT rig's own spelling.
        /// </summary>
        static readonly HashSet<string> FtPlainNames = new HashSet<string>
        {
            "EyesY", "LeftEyeX", "RightEyeX",
            "LeftEyeLidExpandedSqueeze", "RightEyeLidExpandedSqueeze", "EyesDilation"
        };

        /// <summary>The rig's master switches, whatever spelling it uses for them.</summary>
        static readonly HashSet<string> FtGateNames = new HashSet<string>
        {
            "EyeTracking", "FaceTracking", "EyeTrackingActive", "LipTrackingActive",
            "FacialExpressionsDisabled"
        };

        /// <summary>
        /// Is this a face-tracking parameter, whichever tool built the rig?
        ///
        /// Matching on a "v2/" PREFIX is not enough and was the first version's mistake: an
        /// OSCmooth rig names the same shape "#OSCm/Proxy/FT/v2/EyeLeftX", so a prefix test
        /// finds nothing at all on a perfectly good avatar and reports it as having no face
        /// tracking. The version marker can sit anywhere in the path.
        ///
        /// Smoothing chains are deliberately excluded. VRCFury and OSCmooth both generate
        /// per-parameter "…/Smoothed/Pass1" helpers; those are the smoother's own workings,
        /// driven FROM the proxy, and writing to them is overwritten on the next frame.
        /// </summary>
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

        /// <summary>The rig's own on/off switches — worth showing first, because with one of
        /// these at 0 every shape below it looks broken however hard it is driven.</summary>
        public static bool IsFaceTrackingGate(string name)
        {
            return IsFaceTrackingParameter(name) && FtGateNames.Contains(FaceTrackingShortName(name));
        }

        /// <summary>
        /// The readable tail of a parameter name: "#OSCm/Proxy/FT/v2/EyeLeftX" -> "EyeLeftX".
        /// The prefixes carry no meaning for someone reading fifty rows of them, and the
        /// grouping rules need the bare shape name to classify it.
        /// </summary>
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

        /// <summary>
        /// Finds blink shapes by name: separate left/right, or a single combined one.
        /// Deliberately matches "blink" only, never the Unified-Expressions EyeClosed*
        /// shapes — those belong to the face-tracking rig and must not be hijacked.
        /// </summary>
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

            // Prefer two shapes that are the same name but for the side token. A mesh often
            // carries more than one blink family — "! - Blink L"/"! - Blink R" alongside
            // "vrc.blink_left"/"vrc.blink_right" — and taking the first of each side
            // independently can pair the left eye of one family with the right of another.
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

        /// <summary>
        /// Of several both-eyes candidates, the one carrying the least decoration around the
        /// word itself. Meshes accumulate leftovers — one avatar had both "blink" and
        /// "blink_old", and taking whichever came first in mesh order wired the eyes to the
        /// dead one. Ties keep mesh order.
        /// </summary>
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

        /// <summary>
        /// A shape name with its side marker and all separators removed, so the two halves of
        /// a pair collapse to the same key while different families stay apart
        /// ("! - Blink L" -> "!blink", "vrc.blink_left" -> "vrcblink").
        /// </summary>
        static string SideStem(string name, string word, char letter)
        {
            string lower = name.ToLowerInvariant();
            lower = Regex.Replace(lower, word, "");
            lower = Regex.Replace(lower, $@"(^|[ _.\-]){letter}([ _.\-]|$)", "$1$2");
            return Regex.Replace(lower, @"[ _.\-]+", "");
        }

        /// <summary>
        /// A shape belongs to a side if it spells the word out, or carries the side letter
        /// as a standalone token ("Blink L", "blink_r", "L_Blink") — not just any l/r.
        /// </summary>
        public static bool IsSide(string lower, string word, char letter)
        {
            return lower.Contains(word)
                   || Regex.IsMatch(lower, $@"(^|[ _.\-]){letter}([ _.\-]|$)");
        }

        /// <summary>The blink-mode field/enum name varies across CCK versions; find it by its members.</summary>
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

        /// <summary>Viseme order shared by VRChat and ChilloutVR.</summary>
        public static readonly string[] VisemeOrder =
        {
            "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS",
            "nn", "RR", "aa", "E", "ih", "oh", "ou"
        };

        /// <summary>
        /// Matches the 15 visemes against a mesh's blendshapes. Only accepts the
        /// conventional spellings ("vrc.v_aa", "v_aa", "vis_aa", or a bare "aa") so short
        /// keys like "aa"/"E" can't collide with unrelated shape names.
        /// Returns null when fewer than half are found (i.e. this mesh isn't the face).
        /// </summary>
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

        /// <summary>
        /// The avatar's face mesh: one literally named "Body" (the near-universal
        /// convention), else the renderer with the most blendshapes — skipping VRCFury's
        /// face-tracking debug meshes, which carry a full shape set purely to visualise it.
        /// </summary>
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

        /// <summary>
        /// Estimates the first-person viewpoint the way VRChat's descriptor defaults do:
        /// between the eyes, nudged forward so it sits at the front of the eyes rather than
        /// inside the skull. Falls back to the head bone, then to a fraction of the
        /// avatar's bounds. Returned in avatar-local space (unscaled), which is what
        /// CVRAvatar.viewPosition holds — the scaler multiplies it back by root scale.
        /// </summary>
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
                // Not humanoid at all — use the visual bounds as a last resort.
                var bounds = CalculateBounds(root);
                world = new Vector3(bounds.center.x, bounds.max.y - bounds.size.y * 0.08f, bounds.center.z);
            }

            // Push forward toward the face so the camera isn't inside the head.
            world += root.transform.forward * 0.04f;
            return RootOffset(root, world);
        }

        // ---------------------------------------------- the CCK's own Auto placement ----

        static readonly string[] LeftEyeNameVariants = { "LeftEye", "Left_Eye", "EyeLeft", "Eye_Left" };
        static readonly string[] RightEyeNameVariants = { "RightEye", "Right_Eye", "EyeRight", "Eye_Right" };

        /// <summary>
        /// The CVRAvatar inspector's "Auto" button for View Position, replicated from the
        /// CCK's own editor (CCK_CVRAvatarEditor.AutoSetViewPosition is private, so it is
        /// mirrored here): the midpoint between the humanoid eye bones; a single eye is
        /// projected back onto the avatar's centreline; with no eye bones, name-matched eye
        /// children under Head; failing that, a head-bone offset scaled by the hips-to-head
        /// distance. Returned avatar-local like everything else here; false when the rig
        /// gives the CCK's chain nothing to work with.
        /// </summary>
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
                var namedLeft = FindChildByNameVariants(head, LeftEyeNameVariants);
                var namedRight = FindChildByNameVariants(head, RightEyeNameVariants);
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

        /// <summary>The Auto button for Voice Position: the jaw bone, else a small fixed
        /// offset in front of the head bone (CCK_CVRAvatarEditor.AutoSetVoicePosition,
        /// mirrored). Avatar-local; false without a humanoid jaw or head.</summary>
        public static bool CckAutoVoicePosition(GameObject root, Animator animator, out Vector3 localPosition)
        {
            localPosition = default;
            if (animator == null || !animator.isHuman)
            {
                return false;
            }
            var jaw = animator.GetBoneTransform(HumanBodyBones.Jaw);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            Vector3 world;
            if (jaw != null)
            {
                world = jaw.position;
            }
            else if (head != null)
            {
                // The CCK's numbers (5 mm up, 6 cm forward) are metres for a human-sized head.
                // Expressed as fractions of this avatar's own hips-to-head span they come out
                // the same on a 1.8 m biped and stay sane on anything else, without inheriting
                // a bone scale.
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                float span = hips != null ? Vector3.Distance(hips.position, head.position) : 0f;
                world = OffsetFromBone(head, span > 0.0001f
                    ? new Vector3(0f, 0.008f * span, 0.1f * span)
                    : new Vector3(0f, 0.005f, 0.06f));
            }
            else
            {
                return false;
            }
            localPosition = RootOffset(root, world);
            return true;
        }

        /// <summary>
        /// A point offset from a bone, in the bone's DIRECTIONS but in world-space metres.
        ///
        /// Not <c>bone.TransformPoint</c>, which is what the CCK uses and what put a tester's
        /// voice position 6 metres in front of their avatar. TransformPoint multiplies the offset
        /// by the bone's world SCALE, and plenty of rigs carry a scale on their bones —
        /// Second Life-derived skeletons routinely run at 100× — so a 6 cm nudge becomes 6 m.
        /// It is invisible on a rig whose bones are all scale 1, which is most of them, which is
        /// why it survived: the viewpoint (a midpoint between two eye bone POSITIONS) was fine on
        /// the same avatar, and only the offset-based fallback broke.
        ///
        /// Rotation carries the direction; the caller sizes the offset from the avatar itself.
        /// </summary>
        static Vector3 OffsetFromBone(Transform bone, Vector3 offsetInMetres)
        {
            // The AVATAR's orientation, not the bone's. A bone's local axes are whatever the
            // rigger felt like: on one robot avatar the head bone's +Z pointed at the sky, so
            // "6 cm forward" became "6 cm up" and the voice position sat above the eyes. The
            // avatar root is the one transform whose forward really is forward.
            var root = bone.root;
            return bone.position + root.rotation * offsetInMetres;
        }

        /// <summary>One eye only (cyclops rigs, asymmetric heads): the CCK removes the eye's
        /// offset along whichever root axis it sits furthest out on, landing the viewpoint
        /// back on the centreline.</summary>
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

        static Transform FindChildByNameVariants(Transform parent, string[] nameVariants)
        {
            foreach (string potentialName in nameVariants)
            {
                var child = parent.Find(potentialName);
                if (child != null)
                {
                    return child;
                }
                foreach (Transform candidate in parent)
                {
                    if (string.Equals(candidate.name, potentialName, StringComparison.OrdinalIgnoreCase))
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

        /// <summary>
        /// A world point expressed the way CVRAvatar stores viewPosition and voicePosition:
        /// the offset from the avatar root, **with the root's scale still in it**.
        ///
        /// This is not what InverseTransformPoint gives you, and the difference is invisible on
        /// the majority of avatars because their root scale is 1. On a root scaled to 1.4 the
        /// viewpoint landed at 1/1.4 of its correct height — around the collarbone rather than
        /// the eyes — and clicking the CCK's own Auto button "fixed" it, which is what finally
        /// gave the bug away.
        ///
        /// The CCK's contract is written down in its own inspector, and the scale it uses is
        /// **localScale**:
        ///
        ///     Vector3 scale = avatarTransform.localScale;
        ///     pos    = avatarTransform.TransformPoint(Scale(viewPosition, 1/scale));   // read
        ///     stored = Scale(avatarTransform.InverseTransformPoint(handle), scale);    // write
        ///
        /// localScale, NOT lossyScale — and the difference is invisible on an avatar sitting at
        /// the top of the hierarchy, where the two are equal. Parent that avatar under anything
        /// scaled and they diverge by the parent's factor, which throws the viewpoint metres
        /// away from the head. An earlier revision here used lossyScale and was confirmed
        /// correct on an unparented avatar, which proved nothing about the parented case.
        ///
        /// Matching localScale is also right for the game: an uploaded avatar is instantiated
        /// with no scaled ancestor, so localScale IS its world scale there.
        /// </summary>
        static Vector3 RootOffset(GameObject root, Vector3 world)
        {
            return Vector3.Scale(root.transform.InverseTransformPoint(world),
                                 root.transform.localScale);
        }

        /// <summary>
        /// The CCK's own round trip: what its inspector will draw for a stored position. Used to
        /// CHECK a computed placement instead of trusting the arithmetic that produced it —
        /// this contract has now been got wrong twice, in both directions.
        /// </summary>
        /// <summary>
        /// Checks the viewpoint and voice position where the user will actually see them: back
        /// through the CCK inspector's own arithmetic, measured against the head the avatar has.
        ///
        /// The space these are stored in has now been got wrong twice — once by dropping the
        /// scale entirely, once by using lossyScale where the CCK uses localScale — and both
        /// times the mistake was invisible on the avatars to hand, because an unparented root
        /// makes those two scales equal, and glaring to a tester whose avatar sat under
        /// something scaled. Arithmetic this easy to get wrong by inspection should be measured
        /// instead, so it is.
        ///
        /// The tolerance is deliberately loose: half a head-to-hips is not a rounding question,
        /// it means the value is in the wrong space entirely.
        /// </summary>
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

        /// <summary>Voice emitted from the head, like VRChat does. Avatar-local, unscaled.</summary>
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
