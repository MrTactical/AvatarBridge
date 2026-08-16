// Bakes a plug mesh into the texture the deform reads. Format from
// SPS's documented layout, not its code; see docs/YAPS-CLEAN-ROOM.md.
// Each vertex is stored in the plug root's frame without its scale, in
// renderer units, placed through bone x bindpose. The active weight is
// the vertex's skin weight on the plug's bone chain; it feathers the base.
// Guarded on the CCK alone; nothing here touches a VRChat type.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsBaker
    {
        const string Category = "YAPS";
        const int TextureWidth = 8192;
        const int FloatsPerVertex = 10;
        const int FloatsPerShapeVertex = 9;   // delta position, normal, tangent

        // Sixteen, matching SPS. The weights ride four float4s.
        public const int MaxShapes = 16;

        public class Result
        {
            public Texture2D Bake;
            public int VertexCount;
            public float Length;        // plug-local, along +Z
            public float Radius;        // plug-local, the widest active vertex off the axis
            public float ActiveVertices;
            public bool FromSkinnedMesh;
            public List<string> Shapes = new List<string>();
            public List<string> MovingShapes = new List<string>();   // every shape that moves the plug

            // The frame the deform works in, world space at bake time. Not
            // the plug object's transform; anything measuring the plug uses this.
            public Vector3 Origin;
            public Quaternion Rotation;
        }

        // wantedShapes: a socket names its stages; a plug takes the shapes
        // that move it most. objectFrame: a socket's shader adds the shape
        // deltas in the mesh's own frame, so a socket bake stays in it.
        public static Result Bake(Renderer renderer, Transform plugRoot, string outputDir,
            BridgeReport report, out string failure, IList<string> wantedShapes = null,
            bool objectFrame = false, bool flipAxis = false)
        {
            failure = null;
            if (renderer == null || plugRoot == null)
            {
                failure = "no renderer or no plug root";
                return null;
            }

            var mesh = MeshOf(renderer);
            if (mesh == null)
            {
                failure = "the renderer has no mesh";
                return null;
            }
            // Read/Write Enabled gates the player, not the editor, so ask the
            // mesh rather than the flag.
            Vector3[] probe;
            try
            {
                probe = mesh.vertices;
            }
            catch (Exception e)
            {
                failure = $"\"{mesh.name}\" would not hand over its vertices ({e.GetType().Name}) " +
                          "— tick Read/Write Enabled on the model importer";
                return null;
            }
            if (probe == null || probe.Length == 0)
            {
                failure = $"\"{mesh.name}\" has no vertices to bake";
                return null;
            }

            var skin = renderer as SkinnedMeshRenderer;
            if (!TryPlaceVertices(mesh, skin,
                    out var worldPositions, out var worldNormals, out var worldTangents,
                    out var activeWeights, out var placements, plugRoot, out failure))
            {
                return null;
            }

            // The bake is measured in the scene as it stands. A root that
            // is turned or scaled there puts that turn and scale into
            // every baked position, and the shader assumes neither.
            var sceneRoot = renderer.transform.root;
            if (sceneRoot != null
                && (Quaternion.Angle(sceneRoot.rotation, Quaternion.identity) > 1f
                    || (sceneRoot.lossyScale - Vector3.one).magnitude > 0.01f))
            {
                report?.Warning(Category, $"\"{sceneRoot.name}\" is rotated or scaled in the scene",
                    "The plug bake measures the mesh where it stands, so that rotation or scale " +
                    "is baked in. Reset the root's rotation to zero and its scale to one before " +
                    "baking, then move it back afterwards.");
            }

            // The shaft direction and origin are measured from the mesh, not
            // taken from the plug root, whose rotation is whatever the author left.
            //
            // A skinned mesh is baked in that measured frame; the shader
            // recovers it per vertex from the bones. A plain mesh has no
            // bones to recover it from, so the shader takes the object's
            // own origin and +Z, and the markers sit there too. Its bake
            // is in that frame, mesh units, and the measurement only says
            // how far the mesh disagrees with it.
            bool staticMesh = skin == null || skin.bones == null || skin.bones.Length == 0;
            Vector3 rootPos = plugRoot.position;
            Quaternion rootRot = plugRoot.rotation;
            if (staticMesh)
            {
                rootPos = renderer.transform.InverseTransformPoint(plugRoot.position);
                rootRot = Quaternion.Inverse(renderer.transform.rotation) * plugRoot.rotation;
            }
            MeasureFrame(worldPositions, activeWeights, rootPos, rootRot, flipAxis,
                out var origin, out var rotation, out float axisDrift, out float originDrift);
            var toPlug = staticMesh || objectFrame ? Matrix4x4.identity
                : Matrix4x4.TRS(origin, rotation, Vector3.one).inverse;
            if (staticMesh)
            {
                origin = renderer.transform.TransformPoint(origin);
                rotation = renderer.transform.rotation * rotation;
            }

            int count = worldPositions.Count;
            var positions = new Vector3[count];
            var normals = new Vector3[count];
            var tangents = new Vector3[count];
            float length = 0f, radius = 0f;
            int active = 0;

            for (int i = 0; i < count; i++)
            {
                positions[i] = toPlug.MultiplyPoint3x4(worldPositions[i]);
                normals[i] = toPlug.MultiplyVector(worldNormals[i]).normalized;
                tangents[i] = toPlug.MultiplyVector(worldTangents[i]).normalized;
                if (activeWeights[i] > 0.001f)
                {
                    active++;
                    length = Mathf.Max(length, positions[i].z);
                    radius = Mathf.Max(radius, positions[i].x * positions[i].x + positions[i].y * positions[i].y);
                }
            }

            if (active == 0)
            {
                failure = "no vertex on this renderer is weighted to the plug's bone chain, so " +
                          "there is nothing to bake";
                return null;
            }
            if (length <= 0.0001f && !objectFrame)
            {
                failure = "the plug measures no length along its own +Z — its root is probably " +
                          "pointing the wrong way";
                return null;
            }

            // The mesh reaching the vertex shader has blendshapes applied and
            // the bake is the rest pose; the deltas let the shader rebuild it.
            var shapes = CaptureShapes(mesh, skin, toPlug, activeWeights, placements, out var shapeNames,
                out var movingShapes, wantedShapes);

            var texture = WriteTexture(positions, normals, tangents, activeWeights, count, shapes);
            Directory.CreateDirectory(outputDir);
            string path = AssetDatabase.GenerateUniqueAssetPath(
                outputDir + "/YAPS " + Sanitise(renderer.name) + " bake.asset");
            AssetDatabase.CreateAsset(texture, path);

            // The deform throws vertices well outside the rest pose, and
            // Unity culls on the mesh's own bounds, so a plug that bends
            // toward someone can vanish mid-bend precisely when it matters.
            ExtendBounds(renderer, mesh, length);

            bool drifted = axisDrift > 5f || originDrift > 0.01f;
            if (staticMesh && drifted && !objectFrame)
            {
                // A plain mesh bends around its object's origin along +Z,
                // whatever the vertices say. Said loudly, because the plug
                // looks fine until a socket engages it.
                report?.Warning(Category, $"\"{renderer.name}\" is not modelled along its object's +Z",
                    $"Its shaft measures {axisDrift:0} degrees off the object's +Z and its base sits " +
                    $"{originDrift * 100f:0.#} cm from the object's origin. A plain mesh bends around " +
                    "the object's origin along its +Z, so it will swing to that frame the moment a " +
                    "socket engages it. Set the mesh's pivot at the base and point +Z along the shaft " +
                    "(in Blender: origin to the base, shaft along +Y before export), then bake again.");
            }
            report?.Converted(Category, renderer.name,
                $"Baked {active} of {count} vertices, plug length {length:0.###} m" +
                (drifted && !staticMesh
                    ? $", measuring its own axis {axisDrift:0} degrees off the object's rotation " +
                      $"and its base {originDrift * 100f:0.#} cm from the object's position. Both " +
                      "come from where the vertices actually are, so a plug hanging off an object " +
                      "nobody aimed or placed still bakes correctly. "
                    : ". ") +
                (staticMesh
                    ? "Each vertex is stored in the object's own frame, base at the origin, shaft along +Z."
                    : "Each vertex is stored in the plug root's own frame, with its skin weight on " +
                      "the plug's bone chain as the blend weight, so the base feathers into the body " +
                      "instead of shearing off."));

            return new Result
            {
                Bake = texture,
                VertexCount = count,
                Length = length,
                Radius = Mathf.Sqrt(radius),
                ActiveVertices = active,
                FromSkinnedMesh = !staticMesh,
                Shapes = shapeNames,
                MovingShapes = movingShapes,
                Origin = origin,
                Rotation = rotation,
            };
        }

        // How much of this renderer belongs to that plug, without baking
        // anything. Nothing on a baked avatar says which renderer carries
        // a plug, VRCFury's component is gone by then and the flag that
        // would have left a bake texture behind is deliberately off, so
        // the answer is measured: the renderer with the most vertices
        // weighted to the plug's bone chain is the one wearing it.
        public static int CountPlugVertices(Renderer renderer, Transform plugRoot)
        {
            var skin = renderer as SkinnedMeshRenderer;
            if (skin == null || plugRoot == null || skin.sharedMesh == null
                || skin.bones == null || skin.bones.Length == 0)
            {
                return 0;
            }

            var plugBones = BonesUnder(skin.bones, plugRoot);
            if (plugBones.Count == 0)
            {
                for (var above = plugRoot.parent; above != null && plugBones.Count == 0;
                     above = above.parent)
                {
                    plugBones = BonesUnder(skin.bones, above);
                }
            }
            if (plugBones.Count == 0)
            {
                return 0;
            }

            int count = 0;
            foreach (var w in skin.sharedMesh.boneWeights)
            {
                if (WeightOnPlug(w, plugBones) > 0.001f)
                {
                    count++;
                }
            }
            return count;
        }

        // Applies the bake to a material, cloning it first so two renderers
        // sharing one material cannot overwrite each other's vertex counts.
        // That is not hypothetical: a real avatar in the corpus has a
        // clothing material sharing a renderer with a plug, and its bake
        // data disagreed with its own declared vertex count.
        public static Material Apply(Result result, Material source, Shader patchedShader,
            string outputDir, bool skinned)
        {
            var clone = new Material(source) { name = source.name + " (YAPS)" };
            if (patchedShader != null)
            {
                clone.shader = patchedShader;
            }
            clone.SetTexture("_YAPS_Bake", result.Bake);
            clone.SetFloat("_YAPS_VertexCount", result.VertexCount);
            clone.SetFloat("_YAPS_Length", result.Length);
            clone.SetFloat("_YAPS_BakeScale", 1f);   // baked in renderer units by construction
            clone.SetFloat("_YAPS_FrameFromVertex", skinned ? 1f : 0f);
            clone.SetFloat("_YAPS_ShapeCount", result.Shapes.Count);

            Directory.CreateDirectory(outputDir);
            AssetDatabase.CreateAsset(clone, AssetDatabase.GenerateUniqueAssetPath(
                outputDir + "/" + Sanitise(clone.name) + ".mat"));
            return clone;
        }

        // --- placing the vertices -------------------------------------

        static bool TryPlaceVertices(Mesh mesh, SkinnedMeshRenderer skin,
            out List<Vector3> positions, out List<Vector3> normals, out List<Vector3> tangents,
            out List<float> active, out List<Matrix4x4> placements, Transform plugRoot, out string failure)
        {
            failure = null;
            positions = new List<Vector3>();
            normals = new List<Vector3>();
            tangents = new List<Vector3>();
            active = new List<float>();
            // What placed each vertex. A blendshape delta is a mesh-space
            // direction and has to turn with its vertex.
            placements = new List<Matrix4x4>();

            var meshVertices = mesh.vertices;
            var meshNormals = mesh.normals;
            var meshTangents = mesh.tangents;

            if (skin == null || skin.bones == null || skin.bones.Length == 0)
            {
                // A plain mesh renderer: the shader reads its vertices in
                // the object's own frame and scales by the object itself,
                // so the bake is the mesh as modelled, untransformed.
                for (int i = 0; i < meshVertices.Length; i++)
                {
                    positions.Add(meshVertices[i]);
                    normals.Add(i < meshNormals.Length ? meshNormals[i] : Vector3.forward);
                    tangents.Add(i < meshTangents.Length
                        ? (Vector3) meshTangents[i] : Vector3.right);
                    active.Add(1f);
                    placements.Add(Matrix4x4.identity);
                }
                return true;
            }

            var bones = skin.bones;
            var bindposes = mesh.bindposes;
            if (bindposes == null || bindposes.Length == 0)
            {
                failure = "the skinned mesh has no bindposes";
                return false;
            }

            // Which bones count as "the plug": its root and everything
            // beneath it, so a plug with its own little chain of bones is
            // baked whole rather than only at its base.
            var plugBones = BonesUnder(bones, plugRoot);
            if (plugBones.Count == 0)
            {
                // The plug object is very often hung off a bone rather than
                // being one, VRCFury's own baked plug sits as a child of
                // the bone that carries it. Climb to the first real bone
                // above it and take that subtree instead of baking nothing.
                for (var above = plugRoot.parent; above != null && plugBones.Count == 0;
                     above = above.parent)
                {
                    plugBones = BonesUnder(bones, above);
                }
            }

            var weights = mesh.boneWeights;
            for (int i = 0; i < meshVertices.Length; i++)
            {
                var w = i < weights.Length ? weights[i] : default;
                // Skinning itself is bone × bindpose; anything else places
                // the vertex where the modeller left it, not where the
                // avatar wears it.
                Matrix4x4 place = Blend(bones, bindposes, w);
                positions.Add(place.MultiplyPoint3x4(meshVertices[i]));
                normals.Add(i < meshNormals.Length
                    ? place.MultiplyVector(meshNormals[i]) : Vector3.forward);
                tangents.Add(i < meshTangents.Length
                    ? place.MultiplyVector(meshTangents[i]) : Vector3.right);
                active.Add(WeightOnPlug(w, plugBones));
                placements.Add(place);
            }
            return true;
        }

        // Both the origin AND the axis come from the mesh, because neither
        // can be taken from the object VRCFury leaves behind once its own
        // SPS step is suppressed, measured on a real avatar, that object's
        // rotation pointed across the plug and its position sat a quarter
        // of a metre back up the body, so a plug 0.427 m long baked first at
        // 0.667 and then at 0.693.
        //
        // A plug is a rod, and a rod's own axis is the direction its points
        // are most spread along. That is the dominant eigenvector of their
        // covariance, found by power iteration; no transform is consulted at
        // all. The base is then simply the near end along that axis.
        static void MeasureFrame(List<Vector3> positions, List<float> active, Vector3 rootPosition, Quaternion rootRotation, bool flip,
            out Vector3 origin, out Quaternion rotation, out float axisDrift, out float originDrift)
        {
            var authored = rootRotation;
            origin = rootPosition;
            rotation = authored;
            axisDrift = 0f;
            originDrift = 0f;

            var mine = new List<Vector3>();
            for (int i = 0; i < positions.Count; i++)
            {
                if (active[i] > 0.5f)
                {
                    mine.Add(positions[i]);
                }
            }
            if (mine.Count < 8)
            {
                return;
            }

            var centre = Vector3.zero;
            foreach (var p in mine)
            {
                centre += p;
            }
            centre /= mine.Count;

            // Covariance, then power iteration for its dominant direction.
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in mine)
            {
                var d = p - centre;
                xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
                yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
            }
            var axis = authored * Vector3.forward;
            for (int i = 0; i < 32; i++)
            {
                var next = new Vector3(
                    xx * axis.x + xy * axis.y + xz * axis.z,
                    xy * axis.x + yy * axis.y + yz * axis.z,
                    xz * axis.x + yz * axis.y + zz * axis.z);
                if (next.sqrMagnitude < 1e-12f)
                {
                    return;
                }
                axis = next.normalized;
            }

            // An eigenvector has no sign. The tip is the end furthest from
            // where the plug attaches, so point away from the attachment.
            float alongRoot = Vector3.Dot(rootPosition - centre, axis);
            if (alongRoot > 0f)
            {
                axis = -axis;
            }
            if (flip)
            {
                axis = -axis;
            }

            float nearest = float.MaxValue;
            foreach (var p in mine)
            {
                nearest = Mathf.Min(nearest, Vector3.Dot(p - centre, axis));
            }

            var measured = centre + axis * nearest;
            axisDrift = Vector3.Angle(authored * Vector3.forward, axis);
            originDrift = Vector3.Distance(rootPosition, measured);
            origin = measured;

            var up = authored * Vector3.up;
            if (Mathf.Abs(Vector3.Dot(up, axis)) > 0.99f)
            {
                up = authored * Vector3.right;
            }
            rotation = Quaternion.LookRotation(axis, up);
        }

        // One entry per shape that actually moves the plug, holding a delta
        // position, normal and tangent per vertex, nine floats, in the
        // same plug frame as the base block.
        //
        // Shapes that leave the plug alone are skipped rather than stored
        // as zeros: an avatar can carry two hundred blendshapes and four
        // slots, so which four is the whole question, and "the ones that
        // move it most" is the only answer that survives contact with a
        // real face-tracking rig.
        static List<Vector3[]> CaptureShapes(Mesh mesh, SkinnedMeshRenderer skin, Matrix4x4 toPlug,
            List<float> active, List<Matrix4x4> placements, out List<string> names,
            out List<string> movers, IList<string> wanted = null)
        {
            names = new List<string>();
            movers = new List<string>();
            var captured = new List<Vector3[]>();
            if (mesh.blendShapeCount == 0)
            {
                return captured;
            }

            int count = mesh.vertexCount;
            var deltaP = new Vector3[count];
            var deltaN = new Vector3[count];
            var deltaT = new Vector3[count];
            var scored = new List<(float moved, int index)>();

            // Named shapes, in the order given: a socket's stages are
            // the author's choice, entry first, and nothing else is
            // stored. Names the mesh does not have are skipped.
            if (wanted != null)
            {
                var picked = new List<(float, int)>();
                foreach (string name in wanted)
                {
                    int index = string.IsNullOrEmpty(name) ? -1 : mesh.GetBlendShapeIndex(name);
                    if (index >= 0 && !picked.Any(p => p.Item2 == index) && picked.Count < MaxShapes)
                    {
                        picked.Add((1f, index));
                    }
                }
                foreach (var (_, index) in picked)
                {
                    int frames = mesh.GetBlendShapeFrameCount(index);
                    mesh.GetBlendShapeFrameVertices(index, frames - 1, deltaP, deltaN, deltaT);
                    var block = new Vector3[count * 3];
                    for (int i = 0; i < count; i++)
                    {
                        var place = i < placements.Count ? placements[i] : Matrix4x4.identity;
                        block[i * 3 + 0] = toPlug.MultiplyVector(place.MultiplyVector(deltaP[i]));
                        block[i * 3 + 1] = toPlug.MultiplyVector(place.MultiplyVector(deltaN[i]));
                        block[i * 3 + 2] = toPlug.MultiplyVector(place.MultiplyVector(deltaT[i]));
                    }
                    captured.Add(block);
                    names.Add(mesh.GetBlendShapeName(index));
                }
                return captured;
            }

            for (int s = 0; s < mesh.blendShapeCount; s++)
            {
                // The last frame is the shape at full weight, which is the
                // one a slider drives toward.
                int frames = mesh.GetBlendShapeFrameCount(s);
                mesh.GetBlendShapeFrameVertices(s, frames - 1, deltaP, deltaN, deltaT);
                float moved = 0f;
                for (int i = 0; i < count && i < active.Count; i++)
                {
                    if (active[i] > 0.5f)
                    {
                        moved += deltaP[i].sqrMagnitude;
                    }
                }
                if (moved > 1e-8f)
                {
                    scored.Add((moved, s));
                    movers.Add(mesh.GetBlendShapeName(s));
                }
            }

            // SELECT by movement, EMIT in mesh order. Two different
            // questions that were being answered with one sort.
            //
            // Which shapes to keep when more than sixteen exist is a
            // question about cost, and the biggest movers are the right
            // answer. What ORDER to write them in is a question about
            // meaning: a socket stages its shapes by INDEX, shape 0 is the
            // entry, shape 3 the deepest, so a socket whose entry shape
            // moved furthest would open at full depth and close as the plug
            // went in.
            //
            // Sorting once by movement answered the cost question and
            // silently gave a wrong answer to the meaning one. Doing both,
            // in that order, costs nothing and removes the hazard for good
            //, including for a socket sharing its mesh with a plug, where
            // one bake has to serve both ends and could not have been
            // ordered two ways.
            var chosen = scored
                .OrderByDescending(x => x.moved)
                .Take(MaxShapes)
                .OrderBy(x => x.index);

            foreach (var (_, index) in chosen)
            {
                int frames = mesh.GetBlendShapeFrameCount(index);
                mesh.GetBlendShapeFrameVertices(index, frames - 1, deltaP, deltaN, deltaT);
                var block = new Vector3[count * 3];
                for (int i = 0; i < count; i++)
                {
                    // Directions, so rotation only: a delta is a
                    // displacement, and translating one would move the
                    // whole plug by the frame's origin. Turned first by
                    // whatever placed its vertex, then into the plug frame.
                    var place = i < placements.Count ? placements[i] : Matrix4x4.identity;
                    block[i * 3 + 0] = toPlug.MultiplyVector(place.MultiplyVector(deltaP[i]));
                    block[i * 3 + 1] = toPlug.MultiplyVector(place.MultiplyVector(deltaN[i]));
                    block[i * 3 + 2] = toPlug.MultiplyVector(place.MultiplyVector(deltaT[i]));
                }
                captured.Add(block);
                names.Add(mesh.GetBlendShapeName(index));
            }
            return captured;
        }

        static HashSet<int> BonesUnder(Transform[] bones, Transform root)
        {
            var found = new HashSet<int>();
            for (int b = 0; b < bones.Length; b++)
            {
                if (bones[b] != null && bones[b].IsChildOf(root))
                {
                    found.Add(b);
                }
            }
            return found;
        }

        static Matrix4x4 Blend(Transform[] bones, Matrix4x4[] bindposes, BoneWeight w)
        {
            var result = new Matrix4x4();
            bool any = false;
            void Add(int index, float weight)
            {
                if (weight <= 0f || index < 0 || index >= bones.Length || index >= bindposes.Length
                    || bones[index] == null)
                {
                    return;
                }
                Matrix4x4 m = bones[index].localToWorldMatrix * bindposes[index];
                for (int e = 0; e < 16; e++)
                {
                    result[e] += m[e] * weight;
                }
                any = true;
            }
            Add(w.boneIndex0, w.weight0);
            Add(w.boneIndex1, w.weight1);
            Add(w.boneIndex2, w.weight2);
            Add(w.boneIndex3, w.weight3);
            return any ? result : Matrix4x4.identity;
        }

        static float WeightOnPlug(BoneWeight w, HashSet<int> plugBones)
        {
            float total = 0f;
            if (plugBones.Contains(w.boneIndex0)) total += w.weight0;
            if (plugBones.Contains(w.boneIndex1)) total += w.weight1;
            if (plugBones.Contains(w.boneIndex2)) total += w.weight2;
            if (plugBones.Contains(w.boneIndex3)) total += w.weight3;
            return Mathf.Clamp01(total);
        }

        // --- the texture ----------------------------------------------

        static Texture2D WriteTexture(Vector3[] positions, Vector3[] normals, Vector3[] tangents,
            List<float> active, int count, List<Vector3[]> shapes)
        {
            int floats = 1 + count * FloatsPerVertex + shapes.Count * count * FloatsPerShapeVertex;
            int height = Mathf.Max(1, Mathf.CeilToInt((float) floats / TextureWidth));
            var pixels = new Color32[TextureWidth * height];

            int at = 0;
            Write(pixels, ref at, 0f);   // header
            for (int i = 0; i < count; i++)
            {
                Write(pixels, ref at, positions[i].x);
                Write(pixels, ref at, positions[i].y);
                Write(pixels, ref at, positions[i].z);
                Write(pixels, ref at, normals[i].x);
                Write(pixels, ref at, normals[i].y);
                Write(pixels, ref at, normals[i].z);
                Write(pixels, ref at, tangents[i].x);
                Write(pixels, ref at, tangents[i].y);
                Write(pixels, ref at, tangents[i].z);
                Write(pixels, ref at, active[i]);
            }

            // Shape blocks follow the base one, nine floats a vertex. The
            // shader finds block s at 1 + count*10 + s*count*9, which is
            // why the vertex count has to be on the material.
            foreach (var block in shapes)
            {
                for (int i = 0; i < count; i++)
                {
                    for (int part = 0; part < 3; part++)
                    {
                        var v = block[i * 3 + part];
                        Write(pixels, ref at, v.x);
                        Write(pixels, ref at, v.y);
                        Write(pixels, ref at, v.z);
                    }
                }
            }

            // Point filtering and no mips are load-bearing, not tidiness:
            // the shader reads these pixels as raw bytes and reassembles
            // floats from them, so any interpolation corrupts the values.
            var texture = new Texture2D(TextureWidth, height, TextureFormat.RGBA32, false, true)
            {
                name = "YAPS Bake",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        static void Write(Color32[] pixels, ref int at, float value)
        {
            var bytes = BitConverter.GetBytes(value);
            pixels[at++] = new Color32(bytes[0], bytes[1], bytes[2], bytes[3]);
        }

        // --- housekeeping ---------------------------------------------

        static void ExtendBounds(Renderer renderer, Mesh mesh, float length)
        {
            if (renderer is SkinnedMeshRenderer skin)
            {
                // The cheapest correct answer for a skinned mesh, and it
                // also spares guessing how far a bend can travel.
                skin.updateWhenOffscreen = true;
                return;
            }
            var bounds = mesh.bounds;
            bounds.Expand(length * 2f);
            mesh.bounds = bounds;
        }

        static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skin)
            {
                return skin.sharedMesh;
            }
            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        static string Sanitise(string name)
            => new string(name.Select(c => char.IsLetterOrDigit(c) || c == ' ' ? c : '_').ToArray());
    }
}
#endif
