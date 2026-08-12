#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    // How far the animator's blendshapes can grow the mesh around a
    // point, as a ratio. The physics sizing asks the same question per
    // chain inside the MagicaCloth writer; contact zones need the
    // answer too, and this must compile without MagicaCloth2, so the
    // measurement lives here on its own.
    //
    // Two readings of every skinned mesh: the pose as saved, and the
    // pose with every blendshape any selected controller animates
    // pushed as far as its curves reach. Vertices are placed through
    // the bind pose and the dominant bone's current matrix, the same
    // trick the physics measurement uses, so the answer is in world
    // units and does not move when the avatar does.
    static class MeshGrowth
    {
        const int SampleTarget = 200000;
        const int MinSamples = 12;
        const float MinBoneWeight = 0.2f;
        const float MaxGrowth = 3f;

        static BridgeContext owner;
        static Dictionary<string, float> reach;
        static Dictionary<string, Vector3[]> deformed;

        internal static float Around(BridgeContext ctx, Vector3 worldCentre, float captureRadius)
        {
            if (ctx?.Target == null)
            {
                return 1f;
            }
            if (Reach(ctx).Count == 0)
            {
                return 1f;   // nothing animated grows anything
            }

            var restDistances = new List<float>();
            var reachDistances = new List<float>();
            foreach (var renderer in ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }
                Vector3[] rest, grown;
                BoneWeight[] weights;
                Matrix4x4[] binds;
                try
                {
                    rest = Deformed(ctx, renderer, mesh, atReach: false);
                    grown = Deformed(ctx, renderer, mesh, atReach: true);
                    weights = mesh.boneWeights;
                    binds = mesh.bindposes;
                }
                catch
                {
                    continue;   // unreadable mesh; nothing to measure
                }
                var bones = renderer.bones;
                if (bones == null || rest.Length == 0 || weights.Length != rest.Length)
                {
                    continue;
                }

                int stride = Mathf.Max(1, rest.Length / SampleTarget);
                for (int i = 0; i < rest.Length; i += stride)
                {
                    var w = weights[i];
                    if (w.weight0 < MinBoneWeight || w.boneIndex0 < 0
                        || w.boneIndex0 >= bones.Length || w.boneIndex0 >= binds.Length)
                    {
                        continue;
                    }
                    var bone = bones[w.boneIndex0];
                    if (bone == null)
                    {
                        continue;
                    }
                    var bind = binds[w.boneIndex0];
                    Vector3 atRest = bone.localToWorldMatrix.MultiplyPoint3x4(bind.MultiplyPoint3x4(rest[i]));
                    float restDistance = Vector3.Distance(atRest, worldCentre);
                    if (restDistance > captureRadius)
                    {
                        continue;
                    }
                    Vector3 atGrown = bone.localToWorldMatrix.MultiplyPoint3x4(bind.MultiplyPoint3x4(grown[i]));
                    restDistances.Add(restDistance);
                    reachDistances.Add(Vector3.Distance(atGrown, worldCentre));
                }
            }

            if (restDistances.Count < MinSamples)
            {
                return 1f;
            }
            restDistances.Sort();
            reachDistances.Sort();
            // The far edge of each reading, robust to a stray vertex. A
            // ratio of medians would under-read a shape that only grows
            // the outside of the body, which is what growth sliders do.
            float restEdge = Percentile(restDistances, 0.9f);
            float grownEdge = Percentile(reachDistances, 0.9f);
            if (restEdge <= 1e-5f)
            {
                return 1f;
            }
            return Mathf.Clamp(grownEdge / restEdge, 1f, MaxGrowth);
        }

        static float Percentile(List<float> sorted, float p)
            => sorted[Mathf.Clamp(Mathf.RoundToInt((sorted.Count - 1) * p), 0, sorted.Count - 1)];

        // Highest weight any selected controller's curves push each
        // blendshape to, keyed "renderer path|shape name".
        static Dictionary<string, float> Reach(BridgeContext ctx)
        {
            if (ReferenceEquals(owner, ctx) && reach != null)
            {
                return reach;
            }
            owner = ctx;
            deformed = new Dictionary<string, Vector3[]>(StringComparer.Ordinal);
            reach = new Dictionary<string, float>(StringComparer.Ordinal);
            var seen = new HashSet<AnimationClip>();
            foreach (var entry in AnimatorMerger.GetSelectedVrcControllers(ctx))
            {
                if (entry.controller == null)
                {
                    continue;
                }
                foreach (var clip in entry.controller.animationClips)
                {
                    if (clip == null || !seen.Add(clip))
                    {
                        continue;
                    }
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type != typeof(SkinnedMeshRenderer)
                            || !binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                        {
                            continue;
                        }
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        if (curve == null || curve.keys.Length == 0)
                        {
                            continue;
                        }
                        float high = curve.keys[0].value;
                        foreach (var key in curve.keys)
                        {
                            high = Mathf.Max(high, key.value);
                        }
                        string name = binding.path + "|" + binding.propertyName.Substring("blendShape.".Length);
                        reach[name] = reach.TryGetValue(name, out var had) ? Mathf.Max(had, high) : high;
                    }
                }
            }
            return reach;
        }

        static Vector3[] Deformed(BridgeContext ctx, SkinnedMeshRenderer renderer, Mesh mesh, bool atReach)
        {
            string path = AnimationUtility.CalculateTransformPath(renderer.transform, ctx.Target.transform);
            string key = (atReach ? "max|" : "saved|") + path;
            if (deformed.TryGetValue(key, out var known))
            {
                return known;
            }

            var extents = atReach ? Reach(ctx) : null;
            var vertices = mesh.vertices;
            int shapes = mesh.blendShapeCount;
            Vector3[] scratch = null;
            for (int s = 0; s < shapes; s++)
            {
                float weight = renderer.GetBlendShapeWeight(s);
                if (extents != null && extents.TryGetValue(path + "|" + mesh.GetBlendShapeName(s), out var high))
                {
                    weight = Mathf.Max(weight, high);
                }
                if (Mathf.Abs(weight) < 0.01f)
                {
                    continue;
                }
                scratch = scratch ?? new Vector3[vertices.Length];
                Apply(mesh, s, weight, vertices, scratch);
            }
            deformed[key] = vertices;
            return vertices;
        }

        static void Apply(Mesh mesh, int shape, float weight, Vector3[] into, Vector3[] scratch)
        {
            int frames = mesh.GetBlendShapeFrameCount(shape);
            if (frames <= 0)
            {
                return;
            }
            int high = frames - 1;
            for (int f = 0; f < frames; f++)
            {
                if (mesh.GetBlendShapeFrameWeight(shape, f) >= weight)
                {
                    high = f;
                    break;
                }
            }
            float highWeight = mesh.GetBlendShapeFrameWeight(shape, high);
            if (high == 0)
            {
                float scale = highWeight > 0f ? weight / highWeight : 0f;
                mesh.GetBlendShapeFrameVertices(shape, 0, scratch, null, null);
                for (int i = 0; i < into.Length; i++)
                {
                    into[i] += scratch[i] * scale;
                }
                return;
            }
            // Between two authored frames: the lower frame fully, plus
            // the slice of the step up to the requested weight.
            float lowWeight = mesh.GetBlendShapeFrameWeight(shape, high - 1);
            float span = highWeight - lowWeight;
            float t = span > 0f ? Mathf.Clamp01((weight - lowWeight) / span) : 0f;
            mesh.GetBlendShapeFrameVertices(shape, high - 1, scratch, null, null);
            for (int i = 0; i < into.Length; i++)
            {
                into[i] += scratch[i] * (1f - t);
            }
            mesh.GetBlendShapeFrameVertices(shape, high, scratch, null, null);
            for (int i = 0; i < into.Length; i++)
            {
                into[i] += scratch[i] * t;
            }
        }
    }
}
#endif
