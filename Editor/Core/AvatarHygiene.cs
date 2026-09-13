// Two fixes any ChilloutVR avatar wants: audio sources clamped to sane
// spatial settings, and skinned mesh bounds sized to the avatar. The
// converter runs both; the toolkit runs them on any avatar.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    internal static class AvatarHygiene
    {
        internal static void SanitizeAudioSources(BridgeContext ctx)
        {
            int clamped = 0, flattened = 0;
            var notes = new List<string>();
            var flat = new List<string>();
            var nearby = new List<string>();
            foreach (var source in ctx.Target.GetComponentsInChildren<AudioSource>(true))
            {
                bool changed = false;

                // 2D audio is dropped, not merely unpositioned. CVR
                // decides whether to spatialize from the blend itself,
                // and a failing source is never handed to the
                // spatializer: perfect for the wearer, silent for
                // everyone else.
                if (source.spatialBlend < 1f)
                {
                    source.spatialBlend = 1f;
                    changed = true;
                    flattened++;
                    if (flat.Count < 6)
                    {
                        flat.Add(source.gameObject.name);
                    }
                }
                if (source.dopplerLevel != 0f)
                {
                    source.dopplerLevel = 0f;
                    changed = true;
                }
                if (source.minDistance < 0.3f)
                {
                    source.minDistance = 0.3f;
                    changed = true;
                }
                if (source.maxDistance > 40f)
                {
                    source.maxDistance = 40f;
                    changed = true;
                }
                if (source.maxDistance < source.minDistance)
                {
                    source.maxDistance = source.minDistance;
                    changed = true;
                }
                // Not changed; the author's reach is the author's call.
                // But a sound that dies inside arm's length presents as
                // a conversion fault, so it is named.
                if (source.maxDistance < 5f && nearby.Count < 6)
                {
                    nearby.Add($"{source.gameObject.name} ({source.maxDistance:0.#} m)");
                }

                if (changed)
                {
                    clamped++;
                    if (notes.Count < 6)
                    {
                        notes.Add(source.gameObject.name);
                    }
                    EditorUtility.SetDirty(source);
                }
            }

            if (flattened > 0)
            {
                ctx.Report.Converted("Audio",
                    $"{flattened} flat (2D) audio source(s) made positional",
                    $"On: {string.Join(", ", flat)}{(flattened > flat.Count ? ", …" : "")}: below fully 3D they can " +
                    "be silent for everyone else.");
            }

            if (nearby.Count > 0)
            {
                ctx.Report.Approximated("Audio",
                    $"{nearby.Count} audio source(s) stop carrying within a few metres",
                    $"{string.Join(", ", nearby)}: left as set. Raise Max Distance on any others should hear.");
            }
            if (clamped > 0)
            {
                ctx.Report.Approximated("Audio",
                    $"{clamped} audio source(s) clamped to VRChat's avatar audio limits",
                    $"On: {string.Join(", ", notes)}{(clamped > notes.Count ? ", …" : "")}: doppler 0, " +
                    "min distance at least 0.3 m, max at most 40 m, as VRChat enforced. Unclamped, min distance 0 " +
                    "can mute the whole game.");
            }
        }

        // VRChat never applies an avatar's root motion, so a source avatar
        // routinely ships with the flag left on and nothing ever played it.
        // Here the Animator does apply it, and a clip carrying root curves
        // turns or shoves the avatar itself, over whatever ChilloutVR's own
        // locomotion is doing. Reported as a spin on the spot after landing,
        // out of a VRChat proxy landing clip, and only in VR: a desktop
        // player's mouse writes an absolute yaw every frame, which hides the
        // drift, where VR only ever asks for a relative turn.
        internal static void StopRootMotion(BridgeContext ctx)
        {
            var animator = ctx.TargetAnimator;
            if (animator == null || !animator.applyRootMotion)
            {
                return;
            }
            animator.applyRootMotion = false;
            EditorUtility.SetDirty(animator);
            ctx.Report.Converted("Animator", "Apply Root Motion switched off",
                "The avatar's Animator had it on. VRChat ignores root motion on an avatar, so it did " +
                "nothing there and was probably never meant to; ChilloutVR does not ignore it, and an " +
                "animation that carries root movement can then turn or shove the avatar itself, on top " +
                "of the platform's own locomotion. ChilloutVR moves you, so nothing on an avatar needs " +
                "this. Turn it back on in the Animator if you know you want it.");
        }

        const float BoundsPaddingFraction = 0.3f;

        internal static void NormalizeSkinnedBounds(BridgeContext ctx)
        {
            float height = Mathf.Max(AvatarScalerInjector.MeasureHeight(ctx), 1.5f);

            if (!MeasureAvatarVolume(ctx.Target, out var envelope)
                // Shorter than half the avatar means the geometry gave
                // no usable answer. Decline rather than guess.
                || envelope.size.y < height * 0.5f)
            {
                ctx.Report.Warning("Meshes", "Bounding boxes left as the avatar had them",
                    "The avatar's own volume could not be measured from its meshes, so there was nothing " +
                    "trustworthy to size the culling boxes against. If meshes vanish at the edge of the " +
                    "screen in game, that is what this would have fixed; please report the avatar.");
                return;
            }

            envelope.Expand(height * BoundsPaddingFraction * 2f);   // Expand takes a diameter

            int changed = 0;
            foreach (var renderer in ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // localBounds is in the root bone's space, so the
                // world-metre envelope must be carried into that space
                // first. Bones ship at wildly different scales.
                var space = renderer.rootBone != null ? renderer.rootBone : renderer.transform;
                var wanted = ToLocalBounds(space, envelope);

                if (Approximately(renderer.localBounds, wanted))
                {
                    continue;
                }
                renderer.localBounds = wanted;
                EditorUtility.SetDirty(renderer);
                changed++;
            }

            if (changed > 0)
            {
                ctx.Report.Converted("Meshes",
                    $"{changed} skinned mesh bounding box(es) resized to the avatar: " +
                    $"{envelope.size.x:0.##} × {envelope.size.y:0.##} × {envelope.size.z:0.##} m",
                    $"The avatar's volume plus {height * BoundsPaddingFraction:0.##} m, so meshes stop " +
                    "vanishing at the screen's edge.");
            }
        }

        internal static bool MeasureAvatarVolume(GameObject root, out Bounds world)
        {
            // A local rather than the out parameter directly: C# won't let a local function
            // capture an out parameter.
            var measured = new Bounds();
            bool any = false;

            void Add(Vector3 point)
            {
                if (any)
                {
                    measured.Encapsulate(point);
                }
                else
                {
                    measured = new Bounds(point, Vector3.zero);
                    any = true;
                }
            }

            foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (var bone in skinned.bones)
                {
                    if (bone != null)
                    {
                        Add(bone.position);
                    }
                }
                // A skinned mesh with no bone list is rare but legal; its root bone still says
                // where it is, and without this such a mesh would contribute nothing at all.
                if (skinned.bones.Length == 0 && skinned.rootBone != null)
                {
                    Add(skinned.rootBone.position);
                }
            }

            world = measured;
            return any;
        }

        static Bounds TransformBounds(Matrix4x4 matrix, Bounds local)
        {
            var min = local.min;
            var max = local.max;
            var result = new Bounds(matrix.MultiplyPoint3x4(min), Vector3.zero);
            for (int i = 1; i < 8; i++)
            {
                result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z)));
            }
            return result;
        }

        static Bounds ToLocalBounds(Transform space, Bounds world) =>
            TransformBounds(space.worldToLocalMatrix, world);

        static bool Approximately(Bounds a, Bounds b) =>
            (a.center - b.center).sqrMagnitude < 1e-6f && (a.extents - b.extents).sqrMagnitude < 1e-6f;

    }
}
#endif
