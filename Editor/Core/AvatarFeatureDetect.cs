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
        /// The visible head and eyes of a DECOY RIG — a quadruped (or any non-biped) whose
        /// humanoid map points at a hidden stand-in skeleton, with constraints relaying that
        /// skeleton onto the bones you can actually see.
        ///
        /// This has to be handled because ChilloutVR hangs BOTH markers off the humanoid Head
        /// bone. <c>AvatarHeadPoint.GetPointParent()</c> and <c>AvatarVoicePoint.GetPointParent()</c>
        /// each return <c>animator.GetBoneTransform(HumanBodyBones.Head)</c>; the client spawns a
        /// marker at the stored offset while the avatar is at rest, then re-parents it to that
        /// bone. So on a decoy rig both markers land on the STAND-IN, wherever the stand-in
        /// happens to be — which on the quadruped that produced this code was 0.57 m from the
        /// dragon's eyes, inside its skull. Look up and you see out; look down and the inside of
        /// the mouth fills the screen.
        ///
        /// Neither of the usual answers helps there: the CCK's Auto button reads the humanoid eye
        /// bones, which are part of the stand-in, and the author's VRChat viewpoint was placed for
        /// VRChat's own conventions against that same stand-in.
        ///
        /// The relay itself says where the visible bones are. A constraint whose SOURCE is the
        /// humanoid Head or an eye bone exists to drive that bone's visible counterpart, so the
        /// constrained transform is the answer:
        ///
        ///     Eye.L &lt;- RealEye.L      Eye.R &lt;- RealEye.R      Head &lt;- HeadHuman
        ///
        /// Only relays pointing AWAY from the humanoid rig count. A rig that constrains one
        /// humanoid bone to another is doing something else and is left alone.
        /// </summary>
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

            // THE test for a decoy rig, and the thing that stops this misfiring.
            //
            // "A constraint sourced from the humanoid head" is not enough on its own. Plenty of
            // ordinary rigs read the head to drive something unrelated — AnyTaur's flight system
            // has a "Rotation Constraint to Head - Y" feeding a contact sender — and the first
            // version of this happily decided the head's visible counterpart was a bone called
            // "HipsAgain", then aimed the viewpoint, the voice and the first-person exclusion at
            // it. Reading the head is not the same as reproducing it.
            //
            // What actually distinguishes a decoy is that its bones are INVISIBLE: the humanoid
            // map points at a stand-in that deforms no mesh, and the constraints exist to move
            // the bones that do.
            //
            // Testing that on the head bone ALONE was not enough, and this is the part worth
            // remembering. A taur base has a real humanoid torso — arms, hands, fingers, spine,
            // all skinning the mesh — but its humanoid Head happens to carry no weights of its
            // own, so a head-only check waved it straight through and the viewpoint went to
            // "HipsAgain" anyway. Being a decoy is a property of the WHOLE rig, so the whole rig
            // is what gets measured.
            //
            // The threshold is loose on purpose. A true decoy scores zero — every mapped bone is a
            // stand-in — while a real humanoid clears a fifth on its fingers alone. Nothing sits
            // near the line, so this only has to survive a rig with a few stray weights.
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
            // BOTH relayed eyes, or nothing. This is the gate that finally worked, after three
            // that didn't, and the reasoning behind it is the useful part.
            //
            // Every attempt to recognise a decoy by the RIG failed, because the taur base that
            // kept tripping this genuinely has one — a humanoid stand-in that deforms no mesh,
            // exactly like the dragon's. What it does NOT have is a relayed head. Its constraint
            // sourced from the humanoid head drives a hip clone, because that is its head-puppet
            // feature: the head is an INPUT that swings the body, not a bone being reproduced
            // somewhere visible.
            //
            // Position can't tell those apart — measured on both avatars, the dragon's real head
            // sits 0.46 m from its humanoid head and the taur's hip clone 0.50 m. Neither can the
            // constraint itself; they are the same component doing opposite jobs.
            //
            // Relayed EYES can. An eye bone exists to aim a pair of eyeballs, so a constraint
            // driving something from one is reproducing a face — there is no other reason to do
            // it. A puppet input never has them, and the taur's report said so outright: "this
            // rig relays no eye bones", right before it guessed from the hip clone anyway.
            //
            // The cost is a decoy rig that maps no eye bones no longer gets this treatment, and
            // falls back to the author's own viewpoint as it did before 2.92.0. That is the safe
            // direction to fail in: a viewpoint that is merely unhelpful beats one confidently
            // placed at the avatar's hips.
            return leftEye != null && rightEye != null;
        }

        /// <summary>
        /// Hides the VISIBLE head in first person on a decoy rig, by adding the CCK's own
        /// <c>FPRExclusion</c> to it.
        ///
        /// ChilloutVR does this for you, but it aims at the same wrong bone everything else does.
        /// <c>AvatarClone.AddExclusionToHeadIfNeeded()</c> reads
        /// <c>animator.GetBoneTransform(HumanBodyBones.Head)</c> and, if that GameObject has no
        /// exclusion of its own, adds one with <c>isShown = false</c>. On a decoy rig that bone is
        /// part of the stand-in skeleton and skins nothing, so the client dutifully hides nothing
        /// and you spend the session looking at the inside of your own head.
        ///
        /// The component only affects the first-person render clone — it shrinks the excluded
        /// hierarchy to zero for your camera, never for anyone else's — and it is on the CCK's
        /// avatar whitelist, so it survives upload. An exclusion the author placed themselves is
        /// left alone.
        /// </summary>
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

        static readonly string[] JawNameVariants =
            { "Jaw", "jaw", "LowerJaw", "Jaw_L", "Mouth", "mouth", "Chin", "Snout" };

        /// <summary>
        /// View and voice positions for a decoy rig, measured on the bones you can SEE.
        ///
        /// Same conventions as the CCK's own Auto buttons — eye midpoint for the view, jaw for
        /// the voice, a head-bone offset when a bone is missing — just aimed at the visible
        /// counterparts <see cref="DecoyRigAnchors"/> found instead of at the stand-in skeleton.
        /// Returns false on every ordinary rig, where there is no relay to follow.
        /// </summary>
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

        /// <summary>
        /// Bones that actually deform a mesh — ones with at least one vertex weighted to them.
        ///
        /// Not <c>SkinnedMeshRenderer.bones</c>, which lists the WHOLE skeleton regardless of
        /// whether a bone moves anything; an FBX exporter puts every bone in there. Only the
        /// weights prove it. Editor code can read them whatever the mesh's Read/Write setting says.
        /// </summary>
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
                    // Unreadable mesh: assume every listed bone deforms, which can only make this
                    // check MORE cautious — the decoy path stays off rather than firing wrongly.
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

        /// <summary>
        /// The transform a constraint drives, for Unity's own constraints and VRChat's alike,
        /// or null when the component is not a constraint at all.
        /// </summary>
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
            // NOT "?? component.transform": an unassigned Transform field comes back as Unity's
            // FAKE null — a live C# reference whose overloaded == reports null while ?? passes it
            // straight through. That distinction crashed every conversion in 2.88.0.
            var target = Field<Transform>(component, "TargetTransform");
            return target != null ? target : component.transform;
        }

        /// <summary>Every source transform a constraint reads, Unity's or VRChat's.</summary>
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

        /// <summary>Reads a public field or property by name, or default when it isn't there.</summary>
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
