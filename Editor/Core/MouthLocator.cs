#if CVR_CCK_EXISTS
using UnityEngine;

namespace AvatarBridge
{
    /// <summary>
    /// Finds where an avatar's mouth actually is, for ChilloutVR's voice position.
    ///
    /// This used to be the head bone, which sits at the base of the skull — so on anything with a
    /// muzzle the voice came out of the neck, and ChilloutVR builds its in-game mouth pointer from
    /// this value. On a flat human face the error is a couple of centimetres; on a snouted avatar
    /// it is most of a head.
    ///
    /// The good source is the avatar's own geometry. A viseme blendshape stores a per-vertex delta,
    /// so an open-mouth viseme like "aa" IS a map of which vertices form the mouth: take the ones
    /// that move most, average their positions, and the answer is measured rather than guessed —
    /// correct for any head shape, muzzle length or species, with no assumptions about proportion.
    ///
    /// Falls back to the jaw bone (right for jaw-flap avatars, which have no visemes to read, but
    /// it sits at the hinge rather than the lips) and then to the head bone, which is what every
    /// conversion did before. Never returns something worse than it used to.
    /// </summary>
    public static class MouthLocator
    {
        public enum Method
        {
            /// <summary>Measured from an open-mouth viseme's vertex deltas.</summary>
            VisemeShape,
            /// <summary>The humanoid jaw bone — the hinge, not the lips.</summary>
            JawBone,
            /// <summary>The head bone, at the base of the skull. The old behaviour.</summary>
            HeadBone,
            /// <summary>Nothing better was available.</summary>
            ViewPosition
        }

        /// <summary>
        /// VRChat's viseme array is a fixed order — sil PP FF TH DD kk CH SS nn RR aa E ih oh ou —
        /// so the shapes are chosen by INDEX, not by name, and any naming convention works. Ordered
        /// by how wide the mouth opens: a wide shape displaces more vertices and further, which
        /// makes the mouth stand out more sharply from the noise around it.
        /// </summary>
        static readonly int[] PreferredVisemes = { 10, 13, 14, 11, 12, 6, 4 }; // aa oh ou E ih CH DD

        /// <summary>
        /// A vertex counts as "mouth" if it moves at least this much of the largest delta. Low
        /// enough to take the whole lip region, high enough to exclude the cheek and jaw flex that
        /// most viseme shapes carry a little of.
        /// </summary>
        const float DeltaThreshold = 0.35f;

        /// <summary>
        /// Returns the mouth in avatar-local space, unscaled — the same space as
        /// <c>CVRAvatar.viewPosition</c>.
        /// </summary>
        public static Vector3 Locate(GameObject root, SkinnedMeshRenderer face, string[] visemeShapes,
            Animator animator, Vector3 viewPosition, out Method method, out string detail)
        {
            return Locate(root, face, visemeShapes, animator, viewPosition, out method, out detail, out _);
        }

        /// <summary>
        /// As above, additionally reporting a humanoid Jaw bone that was ignored for not being
        /// anywhere a jaw could be. See <see cref="JawIsBelievable"/> — the caller should say so,
        /// because it points at the avatar's rig rather than at anything the conversion did.
        /// </summary>
        public static Vector3 Locate(GameObject root, SkinnedMeshRenderer face, string[] visemeShapes,
            Animator animator, Vector3 viewPosition, out Method method, out string detail,
            out string rejectedJaw)
        {
            rejectedJaw = null;
            Transform head = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Head)
                : null;

            if (TryViseme(root, face, visemeShapes, head, viewPosition, out Vector3 mouth, out string shapeName))
            {
                method = Method.VisemeShape;
                detail = $"measured from the \"{shapeName}\" viseme";
                return mouth;
            }

            Transform jaw = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Jaw)
                : null;
            if (jaw != null)
            {
                if (JawIsBelievable(root, animator, jaw, out string why))
                {
                    method = Method.JawBone;
                    detail = $"from the jaw bone \"{jaw.name}\"";
                    return Local(root, jaw.position);
                }
                rejectedJaw = $"\"{jaw.name}\" ({why})";
            }

            if (head != null)
            {
                method = Method.HeadBone;
                detail = $"from the head bone \"{head.name}\"";
                return Local(root, head.position);
            }

            method = Method.ViewPosition;
            detail = "no head bone or viseme to measure from";
            return viewPosition;
        }

        /// <summary>
        /// Whether the rig's Jaw bone is anywhere a jaw could be. Geometry decides; the name is
        /// never consulted.
        ///
        /// The humanoid Jaw slot is optional and unpoliced, and riggers fill it with whatever was
        /// nearest when they clicked. One avatar mapped Jaw to a bone called "fronthair1", 21 cm
        /// ABOVE the head bone and a centimetre above the viewpoint — so the voice came out of the
        /// top of the head, and the CVRAvatar gizmo drew it hovering over the hair. Trusting a
        /// mapped bone because it is mapped is how that ships.
        ///
        /// Two things are true of every real jaw and of nothing on top of a head:
        ///   - it is BELOW the eyes. Not level, not above: a jaw hinges under them. Eyes are the
        ///     reference rather than the head bone because the head bone sits at the base of the
        ///     skull, which a jaw is legitimately level with or slightly above.
        ///   - it is within a head's reach of the head bone. A quarter of hips-to-head is generous
        ///     for a skull and still rejects a bone out at the end of a hair strand or an ear.
        ///
        /// Failing either, the caller falls back to the head bone, which is where the voice sat
        /// before jaw support existed and is never grossly wrong.
        /// </summary>
        static bool JawIsBelievable(GameObject root, Animator animator, Transform jaw, out string why)
        {
            why = null;
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

        /// <summary>
        /// Says where the voice ended up and how it was found, with the numbers — because this is
        /// one of the few conversion results you can check by looking. ChilloutVR draws both
        /// gizmos in the scene view, so a mouth in the wrong place is obvious at a glance.
        ///
        /// Lives here rather than in the converter so both entry points can call it: the setup
        /// path runs without the VRChat SDK, where DescriptorConverter does not compile at all.
        /// </summary>
        public static void Report(BridgeContext ctx, string category, Vector3 voicePosition,
            Method method, string detail, string rejectedJaw = null)
        {
            string where = $"{voicePosition.y:0.000} m up, {voicePosition.z:0.000} m forward";

            if (!string.IsNullOrEmpty(rejectedJaw))
            {
                ctx.Report.Approximated(category, "The rig's \"jaw\" bone isn't on the jaw — ignored",
                    $"This avatar's humanoid Jaw slot points at {rejectedJaw}. The slot is optional " +
                    "and nothing checks it, so a rigger can map it to anything, and this one is " +
                    "somewhere no jaw can be. Left as it is — retargeting it could move geometry — " +
                    "but the voice was placed without it, because taking it at face value puts your " +
                    "voice wherever that bone happens to sit. Worth fixing in the model's Rig tab if " +
                    "you also want jaw-flap animation to work.");
            }

            if (method == Method.VisemeShape)
            {
                ctx.Report.Converted(category, "Voice position",
                    $"Placed at the mouth ({where}), {detail} — the vertices that shape moves ARE " +
                    "the mouth, so this is measured off your avatar rather than guessed. " +
                    "ChilloutVR builds its in-game mouth pointer from this, and draws it in the " +
                    "scene view next to the viewpoint if you want to check it.");
                return;
            }

            ctx.Report.Approximated(category, "Voice position",
                $"Placed {detail} ({where}). No open-mouth viseme could be measured, so this is the " +
                "nearest bone rather than the mouth itself — on a muzzled avatar that can be several " +
                "centimetres back. ChilloutVR draws it in the scene view beside the viewpoint; drag " +
                "it onto the lips if it looks wrong.");
        }

        static bool TryViseme(GameObject root, SkinnedMeshRenderer face, string[] visemeShapes,
            Transform head, Vector3 viewPosition, out Vector3 mouth, out string shapeName)
        {
            mouth = Vector3.zero;
            shapeName = null;

            var mesh = face != null ? face.sharedMesh : null;
            if (mesh == null || mesh.blendShapeCount == 0 || visemeShapes == null)
            {
                return false;
            }

            int shape = -1;
            foreach (int visemeIndex in PreferredVisemes)
            {
                if (visemeIndex >= visemeShapes.Length || string.IsNullOrEmpty(visemeShapes[visemeIndex]))
                {
                    continue;
                }
                int candidate = mesh.GetBlendShapeIndex(visemeShapes[visemeIndex]);
                if (candidate >= 0)
                {
                    shape = candidate;
                    shapeName = visemeShapes[visemeIndex];
                    break;
                }
            }
            if (shape < 0)
            {
                return false;
            }

            int frames = mesh.GetBlendShapeFrameCount(shape);
            if (frames <= 0)
            {
                return false;
            }
            var deltas = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(shape, frames - 1, deltas, null, null);

            float largest = 0f;
            for (int i = 0; i < deltas.Length; i++)
            {
                largest = Mathf.Max(largest, deltas[i].sqrMagnitude);
            }
            if (largest <= 1e-10f)
            {
                return false;   // the shape exists but moves nothing
            }
            float cutoff = largest * DeltaThreshold * DeltaThreshold;

            // Weighted by how far each vertex moves, so the middle of the lips pulls harder than
            // the edge of the region — the centre of the motion rather than the centre of the box.
            var toWorld = SkinTransform(face);
            Vector3 sum = Vector3.zero;
            float total = 0f;
            var vertices = mesh.vertices;
            for (int i = 0; i < deltas.Length && i < vertices.Length; i++)
            {
                if (deltas[i].sqrMagnitude < cutoff)
                {
                    continue;
                }
                float weight = deltas[i].magnitude;
                sum += toWorld(i, vertices[i]) * weight;
                total += weight;
            }
            if (total <= 0f)
            {
                return false;
            }

            Vector3 world = sum / total;
            Vector3 local = Local(root, world);

            // A wrong answer here is worse than the old one, so it has to pass a sanity check
            // before it is trusted: the mouth belongs near the head, and never above eye level.
            // If a mesh's bind poses are odd or a "viseme" shape drives something else entirely,
            // this catches it and the caller falls through to the jaw or head bone.
            if (head != null)
            {
                Vector3 headLocal = Local(root, head.position);
                float reach = Mathf.Max(0.12f, Mathf.Abs(viewPosition.y - headLocal.y) * 4f);
                if (Vector3.Distance(local, headLocal) > reach || local.y > viewPosition.y)
                {
                    return false;
                }
            }

            mouth = local;
            return true;
        }

        /// <summary>
        /// Builds a vertex-to-world mapping for the mesh as it is currently posed.
        ///
        /// `BakeMesh` would be the obvious tool and is deliberately not used: its output space is
        /// documented ambiguously enough that the result could not be reasoned about without
        /// running it, and this has to be right on avatars nobody here can open. Skinning at any
        /// pose is `boneMatrix * bindPose * vertex` for each influence, so evaluating that directly
        /// for the heaviest influence is both exact and provable on paper. Meshes with no bones
        /// fall back to the renderer's own transform.
        /// </summary>
        static System.Func<int, Vector3, Vector3> SkinTransform(SkinnedMeshRenderer face)
        {
            var mesh = face.sharedMesh;
            var bones = face.bones;
            var bindposes = mesh.bindposes;
            var weights = mesh.boneWeights;

            if (bones == null || bones.Length == 0 || bindposes == null || bindposes.Length == 0
                || weights == null || weights.Length != mesh.vertexCount)
            {
                var self = face.transform;
                return (index, vertex) => self.TransformPoint(vertex);
            }

            return (index, vertex) =>
            {
                int bone = weights[index].boneIndex0;
                if (bone < 0 || bone >= bones.Length || bone >= bindposes.Length || bones[bone] == null)
                {
                    return face.transform.TransformPoint(vertex);
                }
                Vector3 inBoneSpace = bindposes[bone].MultiplyPoint3x4(vertex);
                return bones[bone].localToWorldMatrix.MultiplyPoint3x4(inBoneSpace);
            };
        }

        /// <summary>
        /// World to the space CVRAvatar stores <c>viewPosition</c> and <c>voicePosition</c> in:
        /// the offset from the avatar root with the root's scale still in it.
        ///
        /// The scale is **localScale**, exactly as the CCK's own handle uses
        /// (`Scale(InverseTransformPoint(handle), avatarTransform.localScale)`). This line has
        /// been wrong twice: once with the multiply dropped entirely, once with lossyScale,
        /// which agrees with localScale only while the avatar has no scaled ancestor.
        /// </summary>
        static Vector3 Local(GameObject root, Vector3 world)
        {
            return Vector3.Scale(root.transform.InverseTransformPoint(world),
                                 root.transform.localScale);
        }
    }
}
#endif
