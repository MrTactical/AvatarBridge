#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    // How far the animator's blendshapes can grow the mesh around a
    // point, as a ratio. The physics sizing asks the same question per
    // chain inside the MagicaCloth writer; contact zones need the
    // answer too, and this must compile without MagicaCloth2, so the
    // measurement lives here.
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

        // Per conversion and released with it. Every zone and chain asks
        // about the same meshes, and the deformed copies are the size of
        // every mesh on the avatar.
        sealed class Cache
        {
            internal Dictionary<string, float> Reach;
            internal readonly Dictionary<string, string> GroupParent =
                new Dictionary<string, string>(StringComparer.Ordinal);
            // By renderer, not path: two sibling meshes with one name share a
            // path, and the second was handed the first one's vertices.
            internal readonly Dictionary<(SkinnedMeshRenderer renderer, bool atReach), Vector3[]> Deformed =
                new Dictionary<(SkinnedMeshRenderer, bool), Vector3[]>();
            internal readonly Dictionary<Mesh, (BoneWeight[] weights, Matrix4x4[] binds)> Skin =
                new Dictionary<Mesh, (BoneWeight[], Matrix4x4[])>();
            internal readonly Dictionary<(Mesh mesh, int shape), (int[] index, Vector3[] delta)> LastFrame =
                new Dictionary<(Mesh, int), (int[], Vector3[])>();
        }

        static readonly ConditionalWeakTable<BridgeContext, Cache> Caches =
            new ConditionalWeakTable<BridgeContext, Cache>();

        static Cache CacheOf(BridgeContext ctx) => Caches.GetValue(ctx, _ => new Cache());

        // World metres the animator's blendshapes can push the surface
        // around a point outward, 0 when they cannot. A ratio of far
        // edges was tried first and read tiny: the vertices at the
        // capture boundary are often on neighbouring surface that does
        // not grow, so the ballooning vertices near the zone never
        // moved the reading. Displacement per vertex is the honest
        // measure of how far past the zone the body can get.
        internal static float Around(BridgeContext ctx, Vector3 worldCentre, float captureRadius)
            => Measure(ctx, worldCentre, captureRadius, null);

        // perShape: each animated shape's share of the push, keyed
        // "renderer path|shape name".
        internal static float Around(BridgeContext ctx, Vector3 worldCentre, float captureRadius,
            out Dictionary<string, float> perShape)
        {
            perShape = new Dictionary<string, float>(StringComparer.Ordinal);
            return Measure(ctx, worldCentre, captureRadius, perShape);
        }

        // perShape null: the caller wants the push alone, and attributing
        // it is most of the cost.
        static float Measure(BridgeContext ctx, Vector3 worldCentre, float captureRadius,
            Dictionary<string, float> perShape)
        {
            if (ctx?.Target == null)
            {
                return 0f;
            }
            if (Reach(ctx).Count == 0)
            {
                return 0f;   // nothing animated grows anything
            }

            var cache = CacheOf(ctx);
            var deltas = new List<float>();
            var samples = perShape != null
                ? new List<(SkinnedMeshRenderer renderer, int index, Matrix4x4 toWorld, Vector3 direction)>()
                : null;
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
                    if (!cache.Skin.TryGetValue(mesh, out var skin))
                    {
                        cache.Skin[mesh] = skin = (mesh.boneWeights, mesh.bindposes);
                    }
                    (weights, binds) = skin;
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
                    Matrix4x4 toWorld = bone.localToWorldMatrix * bind;
                    Vector3 atRest = toWorld.MultiplyPoint3x4(rest[i]);
                    float restDistance = Vector3.Distance(atRest, worldCentre);
                    if (restDistance > captureRadius)
                    {
                        continue;
                    }
                    Vector3 atGrown = toWorld.MultiplyPoint3x4(grown[i]);
                    // Outward only: distance from the zone growing. A
                    // shape pulling the surface inward reads as zero,
                    // the way a shrinking slider costs the physics
                    // sizes nothing.
                    deltas.Add(Mathf.Max(0f, Vector3.Distance(atGrown, worldCentre) - restDistance));
                    if (samples != null)
                    {
                        Vector3 direction = restDistance > 1e-4f
                            ? (atRest - worldCentre) / restDistance
                            : (atGrown - atRest).normalized;
                        samples.Add((renderer, i, toWorld, direction));
                    }
                }
            }

            if (deltas.Count < MinSamples)
            {
                return 0f;
            }
            deltas.Sort();
            if (perShape != null)
            {
                AttributeShapes(ctx, samples, perShape);
            }
            // The far edge of the push, robust to a stray vertex. The
            // median would under-read a shape that only grows one side
            // of the body, which is what growth sliders do.
            return Percentile(deltas, 0.9f);
        }

        // Which animated shape pushes the captured surface, and how far.
        static void AttributeShapes(BridgeContext ctx,
            List<(SkinnedMeshRenderer renderer, int index, Matrix4x4 toWorld, Vector3 direction)> samples,
            Dictionary<string, float> perShape)
        {
            var extents = Reach(ctx);
            var cache = CacheOf(ctx);
            var byRenderer = new Dictionary<SkinnedMeshRenderer, List<int>>();
            for (int i = 0; i < samples.Count; i++)
            {
                if (!byRenderer.TryGetValue(samples[i].renderer, out var list))
                {
                    byRenderer[samples[i].renderer] = list = new List<int>();
                }
                list.Add(i);
            }

            foreach (var pair in byRenderer)
            {
                var renderer = pair.Key;
                var mesh = renderer.sharedMesh;
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, ctx.Target.transform);
                Vector3[] scratch = null;
                for (int s = 0; s < mesh.blendShapeCount; s++)
                {
                    string key = path + "|" + mesh.GetBlendShapeName(s);
                    if (!extents.TryGetValue(key, out float reachWeight) || reachWeight < 0.01f)
                    {
                        continue;
                    }
                    int frames = mesh.GetBlendShapeFrameCount(s);
                    if (frames <= 0)
                    {
                        continue;
                    }
                    float frameWeight = mesh.GetBlendShapeFrameWeight(s, frames - 1);
                    if (frameWeight <= 0f)
                    {
                        continue;
                    }
                    var (moved, delta) = LastFrame(cache, mesh, s, frames - 1, ref scratch);
                    if (moved == null)
                    {
                        continue;
                    }
                    float scale = reachWeight / frameWeight;
                    var pushes = new List<float>(pair.Value.Count);
                    foreach (int i in pair.Value)
                    {
                        var sample = samples[i];
                        int at = Array.BinarySearch(moved, sample.index);
                        if (at < 0)
                        {
                            pushes.Add(0f);   // a vertex the shape does not move pushes nothing
                            continue;
                        }
                        Vector3 world = sample.toWorld.MultiplyVector(delta[at] * scale);
                        pushes.Add(Mathf.Max(0f, Vector3.Dot(world, sample.direction)));
                    }
                    if (pushes.Count < MinSamples)
                    {
                        continue;
                    }
                    pushes.Sort();
                    float push = Percentile(pushes, 0.9f);
                    if (push > 0.002f)
                    {
                        perShape[key] = perShape.TryGetValue(key, out float had) ? Mathf.Max(had, push) : push;
                    }
                }
            }
        }

        // One frame's moved vertices, read once per conversion. Every zone
        // asks about the same shapes, a full read copies the whole mesh,
        // and a shape moves only part of it. Index null: unreadable.
        static (int[] index, Vector3[] delta) LastFrame(Cache cache, Mesh mesh, int shape, int frame,
            ref Vector3[] scratch)
        {
            if (cache.LastFrame.TryGetValue((mesh, shape), out var known))
            {
                return known;
            }
            (int[], Vector3[]) result = (null, null);
            scratch = scratch ?? new Vector3[mesh.vertexCount];
            try
            {
                mesh.GetBlendShapeFrameVertices(shape, frame, scratch, null, null);
                var index = new List<int>();
                var delta = new List<Vector3>();
                for (int i = 0; i < scratch.Length; i++)
                {
                    // Exact, not Unity's ==, which calls a tiny delta zero.
                    var d = scratch[i];
                    if (d.x != 0f || d.y != 0f || d.z != 0f)
                    {
                        index.Add(i);
                        delta.Add(d);
                    }
                }
                result = (index.ToArray(), delta.ToArray());
            }
            catch
            {
                // cached as unreadable
            }
            cache.LastFrame[(mesh, shape)] = result;
            return result;
        }

        // The weight the animator can push one shape to, for mapping its
        // curves onto a zone's scale.
        internal static float ReachOf(BridgeContext ctx, string shapeKey)
            => Reach(ctx).TryGetValue(shapeKey, out float weight) ? weight : 0f;

        // Shapes raised together in the same clip move together: one
        // slider driving the body and its clothing is one group, not a
        // contribution split against itself. Keyed to the same "path|
        // shape" names as Reach.
        internal static string GroupOf(BridgeContext ctx, string shapeKey)
        {
            Reach(ctx);
            return Find(CacheOf(ctx).GroupParent, shapeKey);
        }

        static string Find(Dictionary<string, string> parents, string key)
        {
            if (!parents.TryGetValue(key, out var parent) || parent == key)
            {
                return key;
            }
            string root = Find(parents, parent);
            parents[key] = root;
            return root;
        }

        static void Union(Dictionary<string, string> parents, string a, string b)
        {
            string ra = Find(parents, a);
            string rb = Find(parents, b);
            if (ra != rb)
            {
                parents[rb] = ra;
            }
        }

        static float Percentile(List<float> sorted, float p)
            => sorted[Mathf.Clamp(Mathf.RoundToInt((sorted.Count - 1) * p), 0, sorted.Count - 1)];

        // Highest weight any selected controller's curves push each
        // blendshape to, keyed "renderer path|shape name".
        internal static Dictionary<string, float> Reach(BridgeContext ctx)
        {
            var cache = CacheOf(ctx);
            if (cache.Reach != null)
            {
                return cache.Reach;
            }
            var reach = cache.Reach = new Dictionary<string, float>(StringComparer.Ordinal);
            var groupParent = cache.GroupParent;
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
                    // Only shapes a clip meaningfully raises are grouped:
                    // VRCFury's rest-assert clips write every shape at 0
                    // and would union unrelated sliders into one blob.
                    var raised = new List<string>();
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
                        if (high > 0.5f)
                        {
                            raised.Add(name);
                        }
                    }
                    for (int i = 1; i < raised.Count; i++)
                    {
                        Union(groupParent, raised[0], raised[i]);
                    }
                }
            }
            return reach;
        }

        // The mesh as worn, or with every animated shape at the far end of
        // its curves. Once per renderer per conversion: every zone asks
        // about the same meshes.
        internal static Vector3[] Deformed(BridgeContext ctx, SkinnedMeshRenderer renderer, Mesh mesh,
            bool atReach = false)
        {
            var cache = CacheOf(ctx);
            if (cache.Deformed.TryGetValue((renderer, atReach), out var known))
            {
                return known;
            }

            // The path is still what an animation names, so it is what reach is keyed by.
            string path = AnimationUtility.CalculateTransformPath(renderer.transform, ctx.Target.transform);
            var extents = atReach ? Reach(ctx) : null;
            var vertices = mesh.vertices;
            int shapes = mesh.blendShapeCount;
            Vector3[] lower = null, upper = null;
            for (int s = 0; s < shapes; s++)
            {
                float weight = renderer.GetBlendShapeWeight(s);
                if (extents != null && extents.TryGetValue(path + "|" + mesh.GetBlendShapeName(s), out var high))
                {
                    weight = Mathf.Max(weight, high);
                }
                if (Mathf.Abs(weight) < 0.01f)
                {
                    continue;   // off, and reading its frames is the expensive part
                }
                if (lower == null)
                {
                    lower = new Vector3[vertices.Length];
                    upper = new Vector3[vertices.Length];
                }
                Apply(mesh, s, weight, vertices, lower, upper);
            }
            cache.Deformed[(renderer, atReach)] = vertices;
            return vertices;
        }

        static void Apply(Mesh mesh, int shape, float weight, Vector3[] into, Vector3[] lower, Vector3[] upper)
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
                mesh.GetBlendShapeFrameVertices(shape, 0, lower, null, null);
                for (int i = 0; i < into.Length; i++)
                {
                    into[i] += lower[i] * scale;
                }
                return;
            }
            // Between two authored frames, and past the last one along its
            // final step, the way a single frame already scales past 100.
            // Clamping here sized contacts for less than physics.
            float lowWeight = mesh.GetBlendShapeFrameWeight(shape, high - 1);
            float span = highWeight - lowWeight;
            float t = span > 0f ? (weight - lowWeight) / span : 0f;
            mesh.GetBlendShapeFrameVertices(shape, high - 1, lower, null, null);
            mesh.GetBlendShapeFrameVertices(shape, high, upper, null, null);
            for (int i = 0; i < into.Length; i++)
            {
                into[i] += Vector3.LerpUnclamped(lower[i], upper[i], t);
            }
        }
    }
}
#endif
