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
            internal readonly Dictionary<(Mesh mesh, string path), List<(int, string, int, float)>> Animated =
                new Dictionary<(Mesh, string), List<(int, string, int, float)>>();
            internal readonly Dictionary<SkinnedMeshRenderer, Placement> Placed =
                new Dictionary<SkinnedMeshRenderer, Placement>();
            internal readonly Dictionary<(Mesh mesh, string path), Movers> Moving =
                new Dictionary<(Mesh, string), Movers>();
            internal readonly Dictionary<(Vector3 centre, float radius, bool shapes), Answer> Answers =
                new Dictionary<(Vector3, float, bool), Answer>();
        }

        // Zones that share a centre and a capture read the same vertices: a socket's receivers sit
        // on one point, most of them at the smallest capture. Reused only while every mesh is
        // placed and named as it was.
        sealed class Answer
        {
            internal List<Placement> Placed;
            internal List<string> Paths;
            internal float Push;
            internal Dictionary<string, float> PerShape;
        }

        // A mesh's sampled vertices placed in the world under one bone state, bucketed so a zone
        // reads only the cells around it. Every zone placed every vertex of every mesh again.
        sealed class Placement
        {
            readonly Vector3[] rest;
            readonly BoneWeight[] weights;
            readonly Matrix4x4[] toWorlds;
            readonly bool[] bound;
            internal readonly List<int> Index = new List<int>();   // sampled vertices with a dominant bone, ascending
            internal readonly List<Vector3> AtRest = new List<Vector3>();
            readonly Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            readonly float cell = 1f;
            readonly int nx = 1, ny = 1, nz = 1;
            readonly Dictionary<int, List<int>> cells = new Dictionary<int, List<int>>();
            // Not finite: no cell holds it and the distance test may still pass it.
            readonly List<int> strays = new List<int>();

            internal Placement(Vector3[] rest, BoneWeight[] weights, Matrix4x4[] toWorlds, bool[] bound)
            {
                this.rest = rest;
                this.weights = weights;
                this.toWorlds = toWorlds;
                this.bound = bound;
                int stride = Mathf.Max(1, rest.Length / SampleTarget);
                for (int i = 0; i < rest.Length; i += stride)
                {
                    var w = weights[i];
                    if (w.weight0 < MinBoneWeight || w.boneIndex0 < 0
                        || w.boneIndex0 >= toWorlds.Length || !bound[w.boneIndex0])
                    {
                        continue;
                    }
                    Vector3 at = toWorlds[w.boneIndex0].MultiplyPoint3x4(rest[i]);
                    Index.Add(i);
                    AtRest.Add(at);
                    if (Finite(at))
                    {
                        min = Vector3.Min(min, at);
                        max = Vector3.Max(max, at);
                    }
                }
                Vector3 size = max - min;
                float extent = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                bool grid = extent >= 0f && !float.IsInfinity(extent);
                if (grid)
                {
                    cell = Mathf.Max(extent / 64f, 1e-6f);
                    nx = Mathf.Min(Mathf.FloorToInt(size.x / cell), 64) + 1;
                    ny = Mathf.Min(Mathf.FloorToInt(size.y / cell), 64) + 1;
                    nz = Mathf.Min(Mathf.FloorToInt(size.z / cell), 64) + 1;
                }
                for (int j = 0; j < AtRest.Count; j++)
                {
                    var p = AtRest[j];
                    if (!grid || !Finite(p))
                    {
                        strays.Add(j);
                        continue;
                    }
                    int key = Axis(p.x, min.x, nx) + nx * (Axis(p.y, min.y, ny) + ny * Axis(p.z, min.z, nz));
                    if (!cells.TryGetValue(key, out var list))
                    {
                        cells[key] = list = new List<int>();
                    }
                    list.Add(j);
                }
            }

            // Compared exactly: reused only where placing again would give the same positions.
            internal bool Matches(Vector3[] rest, BoneWeight[] weights, Matrix4x4[] toWorlds, bool[] bound)
            {
                if (rest != this.rest || weights != this.weights || toWorlds.Length != this.toWorlds.Length)
                {
                    return false;
                }
                for (int b = 0; b < toWorlds.Length; b++)
                {
                    if (bound[b] != this.bound[b] || !toWorlds[b].Equals(this.toWorlds[b]))
                    {
                        return false;
                    }
                }
                return true;
            }

            // Subtract, divide and floor never reorder two values, so a point inside a range lands
            // in the range's cells.
            int Axis(float value, float from, int count) =>
                Mathf.FloorToInt(Mathf.Clamp((value - from) / cell, 0f, count - 1));

            // Positions in Index that may lie within radius of centre, ascending. The margin is for
            // rounding; the distance test after this decides.
            internal List<int> Near(Vector3 c, float radius)
            {
                float r = radius * 1.001f + 1e-5f * (1f + Mathf.Abs(c.x) + Mathf.Abs(c.y) + Mathf.Abs(c.z));
                if (!Finite(c) || !Finite(r))
                {
                    return All();
                }
                var found = new List<int>(strays);
                if (c.x + r >= min.x && c.x - r <= max.x && c.y + r >= min.y && c.y - r <= max.y
                    && c.z + r >= min.z && c.z - r <= max.z)
                {
                    int x0 = Axis(c.x - r, min.x, nx), x1 = Axis(c.x + r, min.x, nx);
                    int y0 = Axis(c.y - r, min.y, ny), y1 = Axis(c.y + r, min.y, ny);
                    int z0 = Axis(c.z - r, min.z, nz), z1 = Axis(c.z + r, min.z, nz);
                    if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1) > cells.Count)
                    {
                        return All();   // most of the mesh: reading every cell costs more than testing it
                    }
                    for (int z = z0; z <= z1; z++)
                    {
                        for (int y = y0; y <= y1; y++)
                        {
                            for (int x = x0; x <= x1; x++)
                            {
                                if (cells.TryGetValue(x + nx * (y + ny * z), out var list))
                                {
                                    found.AddRange(list);
                                }
                            }
                        }
                    }
                }
                found.Sort();
                return found;
            }

            List<int> All()
            {
                var all = new List<int>(Index.Count);
                for (int j = 0; j < Index.Count; j++)
                {
                    all.Add(j);
                }
                return all;
            }
        }

        // Per vertex, the animated shapes that move it, in shape order. A zone walks its own
        // samples instead of every shape's moved list, which was most of the pass.
        sealed class Movers
        {
            internal List<(int shape, string key, int frame, float scale)> Shapes;
            internal Vector3[][] Delta;   // per shape, null where unreadable
            internal int[] Start;         // per vertex, into Shape and At
            internal int[] Shape, At;
            internal List<float>[] Pushes;
        }

        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);

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
            // Only the pushes above zero are kept and sorted; the rest are counted.
            var deltas = new List<float>();
            int count = 0, broken = 0;
            var samples = perShape != null
                ? new List<(SkinnedMeshRenderer renderer, int index, Matrix4x4 toWorld, Vector3 direction)>()
                : null;
            var meshes = new List<(SkinnedMeshRenderer renderer, Placement place, Matrix4x4[] toWorlds,
                BoneWeight[] weights, Vector3[] grown)>();
            var placedNow = new List<Placement>();
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

                // Once per bone, not per vertex: the same multiply on the same
                // inputs, so the same bits, without a native getter per vertex.
                // Per call, not per conversion: a pass between two calls may
                // have moved a bone.
                int boneCount = Mathf.Min(bones.Length, binds.Length);
                var toWorlds = new Matrix4x4[boneCount];
                var placed = new bool[boneCount];
                for (int b = 0; b < boneCount; b++)
                {
                    if (bones[b] != null)
                    {
                        toWorlds[b] = bones[b].localToWorldMatrix * binds[b];
                        placed[b] = true;
                    }
                }

                if (!cache.Placed.TryGetValue(renderer, out var place)
                    || !place.Matches(rest, weights, toWorlds, placed))
                {
                    cache.Placed[renderer] = place = new Placement(rest, weights, toWorlds, placed);
                }
                meshes.Add((renderer, place, toWorlds, weights, grown));
                placedNow.Add(place);
            }

            List<string> paths = null;
            if (perShape != null)
            {
                paths = new List<string>(meshes.Count);
                foreach (var m in meshes)
                {
                    paths.Add(AnimationUtility.CalculateTransformPath(m.renderer.transform, ctx.Target.transform));
                }
            }
            var key = (worldCentre, captureRadius, perShape != null);
            if (cache.Answers.TryGetValue(key, out var answer) && answer.Placed.Count == placedNow.Count)
            {
                bool same = true;
                for (int m = 0; same && m < placedNow.Count; m++)
                {
                    same = answer.Placed[m] == placedNow[m] && (paths == null || answer.Paths[m] == paths[m]);
                }
                if (same)
                {
                    if (perShape != null)
                    {
                        foreach (var pair in answer.PerShape)
                        {
                            perShape[pair.Key] = pair.Value;
                        }
                    }
                    return answer.Push;
                }
            }
            float Remember(float push)
            {
                cache.Answers[key] = new Answer
                {
                    Placed = placedNow,
                    Paths = paths,
                    Push = push,
                    PerShape = perShape != null ? new Dictionary<string, float>(perShape, StringComparer.Ordinal) : null,
                };
                return push;
            }

            foreach (var (renderer, place, toWorlds, weights, grown) in meshes)
            {
                foreach (int j in place.Near(worldCentre, captureRadius))
                {
                    int i = place.Index[j];
                    Vector3 atRest = place.AtRest[j];
                    float restDistance = Vector3.Distance(atRest, worldCentre);
                    if (restDistance > captureRadius)
                    {
                        continue;
                    }
                    Matrix4x4 toWorld = toWorlds[weights[i].boneIndex0];
                    Vector3 atGrown = toWorld.MultiplyPoint3x4(grown[i]);
                    // Outward only: distance from the zone growing. A
                    // shape pulling the surface inward reads as zero,
                    // the way a shrinking slider costs the physics
                    // sizes nothing.
                    float delta = Mathf.Max(0f, Vector3.Distance(atGrown, worldCentre) - restDistance);
                    count++;
                    if (delta > 0f)
                    {
                        deltas.Add(delta);
                    }
                    else if (float.IsNaN(delta))
                    {
                        broken++;
                    }
                    if (samples != null)
                    {
                        Vector3 direction = restDistance > 1e-4f
                            ? (atRest - worldCentre) / restDistance
                            : (atGrown - atRest).normalized;
                        samples.Add((renderer, i, toWorld, direction));
                    }
                }
            }

            if (count < MinSamples)
            {
                return Remember(0f);
            }
            deltas.Sort();
            if (perShape != null)
            {
                AttributeShapes(ctx, samples, perShape);
            }
            // The far edge of the push, robust to a stray vertex. The
            // median would under-read a shape that only grows one side
            // of the body, which is what growth sliders do. A broken
            // vertex sorts below zero, as it did in the full sorted list.
            int at = Mathf.Clamp(Mathf.RoundToInt((count - 1) * 0.9f), 0, count - 1);
            return Remember(at < broken ? float.NaN : PercentileAboveZeros(deltas, count, 0.9f));
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
                var list = pair.Value;
                if (list.Count < MinSamples)
                {
                    continue;
                }
                var movers = MoversOf(ctx, cache, extents, pair.Key);
                foreach (var pushes in movers.Pushes)
                {
                    pushes.Clear();
                }
                foreach (int i in list)
                {
                    var sample = samples[i];
                    for (int e = movers.Start[sample.index]; e < movers.Start[sample.index + 1]; e++)
                    {
                        int k = movers.Shape[e];
                        Vector3 world = sample.toWorld.MultiplyVector(movers.Delta[k][movers.At[e]] * movers.Shapes[k].scale);
                        float one = Vector3.Dot(world, sample.direction);
                        if (one > 0f)
                        {
                            movers.Pushes[k].Add(one);
                        }
                    }
                }
                // Only the pushes above zero are kept and sorted: a vertex the shape does not move
                // pushes nothing, and those were most of every list.
                for (int k = 0; k < movers.Shapes.Count; k++)
                {
                    if (movers.Delta[k] == null)
                    {
                        continue;
                    }
                    var positives = movers.Pushes[k];
                    positives.Sort();
                    float push = PercentileAboveZeros(positives, list.Count, 0.9f);
                    if (push > 0.002f)
                    {
                        string key = movers.Shapes[k].key;
                        perShape[key] = perShape.TryGetValue(key, out float had) ? Mathf.Max(had, push) : push;
                    }
                }
            }
        }

        static Movers MoversOf(BridgeContext ctx, Cache cache, Dictionary<string, float> extents,
            SkinnedMeshRenderer renderer)
        {
            var mesh = renderer.sharedMesh;
            string path = AnimationUtility.CalculateTransformPath(renderer.transform, ctx.Target.transform);
            if (cache.Moving.TryGetValue((mesh, path), out var known))
            {
                return known;
            }
            var shapes = AnimatedShapes(cache, extents, mesh, path);
            int vertices = mesh.vertexCount;
            var movers = new Movers
            {
                Shapes = shapes,
                Delta = new Vector3[shapes.Count][],
                Pushes = new List<float>[shapes.Count],
                Start = new int[vertices + 1],
            };
            var moved = new int[shapes.Count][];
            Vector3[] scratch = null;
            for (int k = 0; k < shapes.Count; k++)
            {
                (moved[k], movers.Delta[k]) = LastFrame(cache, mesh, shapes[k].shape, shapes[k].frame, ref scratch);
                movers.Pushes[k] = new List<float>();
                if (moved[k] != null)
                {
                    foreach (int v in moved[k])
                    {
                        movers.Start[v + 1]++;
                    }
                }
            }
            for (int v = 0; v < vertices; v++)
            {
                movers.Start[v + 1] += movers.Start[v];
            }
            movers.Shape = new int[movers.Start[vertices]];
            movers.At = new int[movers.Start[vertices]];
            var next = (int[])movers.Start.Clone();
            for (int k = 0; k < shapes.Count; k++)
            {
                for (int at = 0; moved[k] != null && at < moved[k].Length; at++)
                {
                    int e = next[moved[k][at]]++;
                    movers.Shape[e] = k;
                    movers.At[e] = at;
                }
            }
            cache.Moving[(mesh, path)] = movers;
            return movers;
        }

        // A mesh's shapes the animator raises, with the frame read and the weight that maps it
        // to the reach, once per conversion and path: every zone asked the mesh again for each
        // name and frame through the native getters.
        static List<(int shape, string key, int frame, float scale)> AnimatedShapes(Cache cache,
            Dictionary<string, float> extents, Mesh mesh, string path)
        {
            if (cache.Animated.TryGetValue((mesh, path), out var known))
            {
                return known;
            }
            var shapes = new List<(int, string, int, float)>();
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
                shapes.Add((s, key, frames - 1, reachWeight / frameWeight));
            }
            cache.Animated[(mesh, path)] = shapes;
            return shapes;
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

        // Percentile of count values of which only the sorted ones above zero are listed.
        static float PercentileAboveZeros(List<float> sorted, int count, float p)
        {
            int at = Mathf.Clamp(Mathf.RoundToInt((count - 1) * p), 0, count - 1) - (count - sorted.Count);
            return at < 0 ? 0f : sorted[at];
        }

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
