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
            return root.transform.InverseTransformPoint(world);
        }

        /// <summary>Voice emitted from the head, like VRChat does. Avatar-local, unscaled.</summary>
        public static Vector3 EstimateVoicePosition(GameObject root, Animator animator)
        {
            if (animator != null && animator.isHuman)
            {
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                {
                    return root.transform.InverseTransformPoint(head.position);
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
