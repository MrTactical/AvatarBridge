#if CVR_CCK_EXISTS
using UnityEngine;

namespace AvatarBridge
{
    // Finds where an avatar's mouth actually is, for ChilloutVR's voice position.
    //
    // This used to be the head bone, which sits at the base of the skull; so on anything with a
    // muzzle the voice came out of the neck, and ChilloutVR builds its in-game mouth pointer from
    // this value. On a flat human face the error is a couple of centimetres; on a snouted avatar
    // it is most of a head.
    //
    // The good source is the avatar's own geometry. A viseme blendshape stores a per-vertex delta,
    // so an open-mouth viseme like "aa" IS a map of which vertices form the mouth: take the ones
    // that move most, average their positions, and the answer is measured rather than guessed .
    // correct for any head shape, muzzle length or species, with no assumptions about proportion.
    //
    // Falls back to the jaw bone (right for jaw-flap avatars, which have no visemes to read, but
    // it sits at the hinge rather than the lips) and then to the head bone, which is what every
    // conversion did before. Never returns something worse than it used to.
    public static class MouthLocator
    {
        public enum Method
        {
            VisemeShape,
            JawBone,
            HeadBone,
            ViewPosition
        }

        static readonly int[] PreferredVisemes = { 10, 13, 14, 11, 12, 6, 4 }; // aa oh ou E ih CH DD

        const float DeltaThreshold = 0.35f;

        public static Vector3 Locate(GameObject root, SkinnedMeshRenderer face, string[] visemeShapes,
            Animator animator, Vector3 viewPosition, out Method method, out string detail)
        {
            return Locate(root, face, visemeShapes, animator, viewPosition, out method, out detail, out _);
        }

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
                if (AvatarFeatureDetect.JawIsBelievable(root, animator, jaw, out string why))
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
                detail = $"just ahead of the head bone \"{head.name}\"";
                // The CCK's offset, not the bare bone position; the head bone sits at the base of
                // the skull, so its raw position puts the voice inside the head. Shared with
                // CckAutoVoicePosition so the two paths cannot drift apart.
                return AvatarFeatureDetect.HeadVoiceOffset(root, animator, out var ahead)
                    ? ahead
                    : Local(root, head.position);
            }

            method = Method.ViewPosition;
            detail = "no head bone or viseme to measure from";
            return viewPosition;
        }

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
            // the edge of the region; the centre of the motion rather than the centre of the box.
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

        static Vector3 Local(GameObject root, Vector3 world)
        {
            return Vector3.Scale(root.transform.InverseTransformPoint(world),
                                 root.transform.localScale);
        }
    }
}
#endif
