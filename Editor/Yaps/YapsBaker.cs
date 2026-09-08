// Bakes a plug mesh into the texture the deform reads. Format from
// SPS's documented layout, never its code. See docs/YAPS-CLEAN-ROOM.md.
//
// Each vertex is stored in the plug root's frame without its scale, in
// renderer units, placed through bone x bindpose. The active weight is
// the vertex's skin weight on the plug's bone chain, which feathers
// the base.
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
            // The mesh this bake describes. Apply names the generated material
            // after the pair: a bake is indexed by mesh-global vertex id, so two
            // meshes can never share one.
            public Renderer Renderer;
            // The root the bake ACTUALLY measured from, which is not always the one
            // it was handed. Anything asking which vertices or which slots are the
            // plug's has to ask the same question, or it answers about a wider
            // chain than the one that was baked.
            public Transform Root;
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
            bool objectFrame = false, bool flipAxis = false, Result shareFrameWith = null)
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

            // DOWN TO THE SHAFT, when the stated root is the hub above it. Here
            // because both builders arrive with a root somebody else chose, and
            // this is the one place they both pass through. Reported, never
            // silent: it moves where the bend begins and what the length means.
            //
            // A socket bakes in its own frame and has no shaft to find.
            if (!objectFrame)
            {
                var shaft = SuggestShaftRoot(renderer, plugRoot);
                if (shaft != null)
                {
                    report?.Converted(Category,
                        $"{plugRoot.name}: measured from \"{shaft.name}\" instead",
                        $"The plug's root was set on \"{plugRoot.name}\", which has more than one " +
                        $"chain of bones hanging off it, so everything on the others was being " +
                        $"measured as part of the shaft: the length spanned them and the bend began " +
                        $"behind them. \"{shaft.name}\" reaches more than twice as far as anything " +
                        $"else there, so it is the shaft. Move the plug onto the bone you want if " +
                        $"this is not it.");
                    plugRoot = shaft;
                }
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
                          ": tick Read/Write Enabled on the model importer";
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
                    out var activeWeights, out var placements, plugRoot, objectFrame, out failure))
            {
                return null;
            }

            // The bake is measured in the scene as it stands. A root turned or
            // scaled there bakes that turn and scale into every position, and the
            // shader assumes neither.
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

            // Frame measured from the mesh, not from the plug root. A plain mesh
            // bakes in its object's own origin and +Z instead: it has no bones for
            // the shader to recover a frame from.
            bool staticMesh = skin == null || skin.bones == null || skin.bones.Length == 0;
            Vector3 rootPos = plugRoot.position;
            Quaternion rootRot = plugRoot.rotation;
            if (staticMesh)
            {
                rootPos = renderer.transform.InverseTransformPoint(plugRoot.position);
                rootRot = Quaternion.Inverse(renderer.transform.rotation) * plugRoot.rotation;
            }
            Vector3 origin;
            Quaternion rotation;
            float axisDrift = 0f, originDrift = 0f;
            bool shared = shareFrameWith != null && !staticMesh;
            if (shared)
            {
                // A second mesh on the same plug takes the FIRST one's frame rather
                // than measuring its own. Otherwise a collar weighted to the same
                // bones finds its own axis, origin and length, and bends as a separate
                // plug. No drift to report: this frame was given, not measured.
                origin = shareFrameWith.Origin;
                rotation = shareFrameWith.Rotation;
            }
            else
            {
                MeasureFrame(worldPositions, activeWeights, rootPos, rootRot, flipAxis,
                    out origin, out rotation, out axisDrift, out originDrift);
            }
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

            // The shaft is the same shaft for every mesh on the plug, so the length
            // is the first one's too. Measured per mesh, every reach fraction would
            // mean a different distance on each renderer.
            if (shared)
            {
                length = shareFrameWith.Length;
            }

            if (active == 0)
            {
                failure = "no vertex on this renderer is weighted to the plug's bone chain, so " +
                          "there is nothing to bake";
                return null;
            }
            if (length <= 0.0001f && !objectFrame)
            {
                failure = "the plug measures no length along its own +Z: its root is probably " +
                          "pointing the wrong way";
                return null;
            }

            // The mesh reaching the vertex shader has blendshapes applied and
            // the bake is the rest pose; the deltas let the shader rebuild it.
            var shapes = CaptureShapes(mesh, skin, toPlug, activeWeights, placements, out var shapeNames,
                out var movingShapes, wantedShapes);

            var texture = WriteTexture(positions, normals, tangents, activeWeights, count, shapes);
            Directory.CreateDirectory(outputDir);
            // Named for the plug that wrote it, never numbered. A unique path is
            // the wrong answer here: the name it avoids belongs to the SAME plug
            // from the last conversion, so every reconvert left another ten
            // megabytes nobody ever read again. Overwriting the one this plug owns
            // is the point.
            //
            // The parent goes in the name because two plugs on one renderer are
            // usually called the same thing under different bones.
            string owner = plugRoot != null && plugRoot.parent != null
                ? plugRoot.parent.name + " " + plugRoot.name
                : plugRoot != null ? plugRoot.name : "plug";
            string path = outputDir + "/YAPS " + Sanitise(renderer.name) + " "
                          + Sanitise(owner) + " bake.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(texture, path);
            SettleForUpload(texture);

            // The deform throws vertices well outside the rest pose and Unity culls
            // on the mesh's own bounds, so a plug bending toward someone can vanish
            // mid-bend, precisely when it matters.
            ExtendBounds(renderer, mesh, length);

            bool drifted = axisDrift > 5f || originDrift > 0.01f;
            if (staticMesh && drifted && !objectFrame)
            {
                // A plain mesh bends around its object's origin along +Z, whatever the
                // vertices say. Said loudly, because the plug looks fine until a socket
                // engages it.
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
                Renderer = renderer,
                Root = plugRoot,
                Shapes = shapeNames,
                MovingShapes = movingShapes,
                Origin = origin,
                Rotation = rotation,
            };
        }

        // How much of this renderer belongs to that plug, without baking
        // anything. Nothing on a baked avatar says which renderer carries a
        // plug, so the answer is measured: the renderer with the most vertices
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
            return CountWeighted(skin, plugBones);
        }

        // Which child bone chain is the SHAFT, when the stated root sits above
        // it. Null when there is no clear answer, which is most of the time.
        //
        // A plug's root is inherited from wherever the original author put
        // their component, and that is routinely the hub ABOVE the shaft.
        // Everything else on that hub is then measured as part of the plug, so
        // the length spans it and the bend starts behind the base.
        //
        // Bone positions, never vertices. A skinned mesh's vertices are in bind
        // space, and the only question here is which chain goes furthest.
        //
        // Two guards, because a wrong root is worse than an inherited one.
        // There must be more than one chain to choose between, or the stated
        // root already IS the shaft's. And the winner has to reach twice as far
        // as the runner-up: two chains of similar length cannot be read.
        public static Transform SuggestShaftRoot(Renderer renderer, Transform statedRoot)
        {
            var skin = renderer as SkinnedMeshRenderer;
            if (skin == null || statedRoot == null || skin.bones == null || skin.bones.Length == 0)
            {
                return null;
            }

            Transform best = null;
            float bestReach = 0, nextReach = 0;
            int candidates = 0;
            for (int i = 0; i < statedRoot.childCount; i++)
            {
                var child = statedRoot.GetChild(i);
                if (CountVerticesUnder(renderer, child) == 0) continue;
                candidates++;
                float reach = 0;
                foreach (var t in child.GetComponentsInChildren<Transform>(true))
                {
                    reach = Mathf.Max(reach, Vector3.Distance(statedRoot.position, t.position));
                }
                if (reach > bestReach) { nextReach = bestReach; bestReach = reach; best = child; }
                else if (reach > nextReach) { nextReach = reach; }
            }
            if (candidates < 2 || best == null || bestReach < nextReach * 2f) return null;
            return best;
        }

        // Vertices weighted to any bone at or under one object, no climbing:
        // the caller decides which level is being asked about.
        public static int CountVerticesUnder(Renderer renderer, Transform level)
        {
            var skin = renderer as SkinnedMeshRenderer;
            if (skin == null || level == null || skin.sharedMesh == null
                || skin.bones == null || skin.bones.Length == 0)
            {
                return 0;
            }
            return CountWeighted(skin, BonesUnder(skin.bones, level));
        }

        static int CountWeighted(SkinnedMeshRenderer skin, HashSet<int> bones)
        {
            if (bones.Count == 0)
            {
                return 0;
            }
            int count = 0;
            foreach (var w in skin.sharedMesh.boneWeights)
            {
                if (WeightOnPlug(w, bones) > 0.001f)
                {
                    count++;
                }
            }
            return count;
        }

        // Applies the bake to a material, cloning it first so two renderers
        // sharing one material cannot overwrite each other's vertex counts.
        // Not hypothetical: real content ships a clothing material sharing a
        // renderer with a plug, its bake disagreeing with its own count.
        public static Material Apply(Result result, Material source, Shader patchedShader,
            string outputDir, bool skinned)
        {
            var clone = Generated(source, patchedShader,
                outputDir + "/" + Sanitise(source.name + " (YAPS)")
                + Tail(source, result.Renderer) + ".mat");
            Apply(result, clone, skinned);
            return clone;
        }

        // Which source material AND WHICH MESH this copy came from, in six
        // bytes.
        //
        // The path used to be the source's NAME alone, and two materials both
        // called "Material" is what an exporter writes when nobody renamed
        // anything. The second bake loaded the first one's asset by path,
        // overwrote it, and left a plug deforming against a mesh it is not.
        // Silent, because reusing an asset at a path is the intent when the
        // SAME material is baked twice.
        //
        // GUID plus local id, so it is stable across sessions and distinct for
        // two materials embedded in one FBX. An in-scene material has neither
        // and falls back to its instance id.
        //
        // THE MESH BELONGS IN THE KEY TOO. Three plug meshes sharing one
        // material is ordinary. Keyed on the material alone they resolved to
        // one asset, a material holds ONE bake, and the last one baked won.
        // Every one of them then reported the same length, which gave it away.
        //
        // The renderer's path under its avatar rather than its instance id, so
        // a rebuild lands on the same asset instead of leaving the old one.
        internal static string Tail(Material source, Renderer on)
        {
            string id = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                source, out string guid, out long local)
                ? guid + local
                : source.GetInstanceID().ToString();
            return " " + YapsShaderPatcher.Hash(id + "|" + PathOf(on));
        }

        static string PathOf(Renderer on)
        {
            if (on == null) return "";
            string path = Step(on.transform);
            for (var t = on.transform.parent; t != null; t = t.parent) path = Step(t) + "/" + path;
            return path;
        }

        // Unity lets two siblings carry the same name, and two renderers whose
        // paths match share a generated material, which is the mixed-up bake
        // this identity exists to stop. Only an ambiguous step is numbered.
        static string Step(Transform t)
        {
            var parent = t.parent;
            if (parent == null) return t.name;
            int seen = 0, mine = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name != t.name) continue;
                if (child == t) mine = seen;
                seen++;
            }
            return seen > 1 ? t.name + "#" + mine : t.name;
        }

        // The per-MESH half of a bake, onto a material that already exists.
        //
        // Split out because a plug spanning several renderers patches one
        // material per renderer and each needs its OWN bake: the texture is
        // indexed by mesh-global vertex id. The author's knobs are written
        // separately, from the component.
        public static void Apply(Result result, Material target, bool skinned)
        {
            if (result == null || target == null)
            {
                return;
            }
            target.SetTexture("_YAPS_Bake", result.Bake);
            target.SetFloat("_YAPS_VertexCount", result.VertexCount);
            target.SetFloat("_YAPS_Length", result.Length);
            target.SetFloat("_YAPS_BakeScale", 1f);   // the rest pose; a size animation drives these
            target.SetFloat("_YAPS_BakeGirth", 1f);
            target.SetFloat("_YAPS_FrameFromVertex", skinned ? 1f : 0f);
            target.SetFloat("_YAPS_ShapeCount", result.Shapes.Count);
            EditorUtility.SetDirty(target);
        }

        // The generated material for one source material, at a name that does
        // not move.
        //
        // GenerateUniqueAssetPath made a NEW file every time, so a plug removed
        // and baked again left "Fur_YAPS_ 1", "Fur_YAPS_ 2" behind it, one per
        // click, in a user's project.
        //
        // What is already at the path is never trusted, only its identity: the
        // values are re-derived from the source every time, so an asset left by
        // an older version cannot carry stale settings forward. The file keeps
        // its GUID, so anything referencing it stays pointed at it.
        public static Material Generated(Material source, Shader patched, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null && existing != source)
            {
                existing.CopyPropertiesFromMaterial(source);
                if (patched != null)
                {
                    existing.shader = patched;
                }
                EditorUtility.SetDirty(existing);
                return existing;
            }
            var made = new Material(source) { name = source.name + " (YAPS)" };
            if (patched != null)
            {
                made.shader = patched;
            }
            AssetDatabase.CreateAsset(made, path);
            return made;
        }

        // --- placing the vertices -------------------------------------

        static bool TryPlaceVertices(Mesh mesh, SkinnedMeshRenderer skin,
            out List<Vector3> positions, out List<Vector3> normals, out List<Vector3> tangents,
            out List<float> active, out List<Matrix4x4> placements, Transform plugRoot,
            bool socket, out string failure)
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
                // A plain mesh renderer: the shader reads its vertices in the object's
                // own frame and scales by the object itself, so the bake is the mesh as
                // modelled, untransformed.
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

            // Which bones count as "the plug": its root and everything beneath it,
            // so a plug with its own little chain is baked whole rather than only
            // at its base.
            var plugBones = BonesUnder(bones, plugRoot);
            if (plugBones.Count == 0)
            {
                // The plug object is very often hung off a bone rather than being one.
                // Climb to the first real bone above it and take that subtree instead
                // of baking nothing.
                Transform climbed = null;
                for (var above = plugRoot.parent; above != null && plugBones.Count == 0;
                     above = above.parent)
                {
                    plugBones = BonesUnder(bones, above);
                    climbed = above;
                }

                // The climb cannot tell when it has gone too far. Where the plug is
                // part of the body mesh and has no bone, the first level with any
                // weight is usually Hips, and Hips carries the skeleton: every body
                // vertex reads as plug and the avatar bakes as one shaft. Ten did.
                //
                // A shaft on its own renderer weighted only to Hips climbs to Hips
                // too and is right to, so the count of captured vertices cannot
                // separate them. What separates them is what came with it.
                // Sockets excepted. A socket on a body mesh is MEANT to climb to
                // the body's bone: it bakes shapes, and its weights only decide
                // which vertices exist, not which ones are a shaft.
                if (!socket && plugBones.Count > 0 && TookTheBody(skin, plugBones))
                {
                    failure = "the first bone above the plug object (\"" + climbed.name
                              + "\") carries this mesh's head or feet as well, so the bake could "
                              + "not tell the plug's vertices from the rest of the mesh and would "
                              + "have bent the whole avatar. Set the plug's Root Bone to the bone "
                              + "the shaft grows from, or to an empty under it, and bake again.";
                    return false;
                }
            }

            var weights = mesh.boneWeights;
            for (int i = 0; i < meshVertices.Length; i++)
            {
                var w = i < weights.Length ? weights[i] : default;
                // Skinning itself is bone x bindpose. Anything else places the vertex
                // where the modeller left it, not where the avatar wears it.
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

        // Origin and axis both from the mesh, since the object left behind
        // points anywhere. A rod's axis is where its points spread most, the
        // dominant eigenvector of their covariance. The base is the near end.
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

        // One entry per shape that moves the plug: delta position, normal and
        // tangent per vertex, in the plug frame. Shapes that leave it alone are
        // skipped, never stored as zeros.
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

            // Named shapes, in the order given: a socket's stages are the author's
            // choice, entry first. Names the mesh does not have are skipped.
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

            // Select by movement, emit in mesh order. A socket stages its
            // shapes by index, so the order carries meaning of its own.
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
                    // Directions, so rotation only: translating a delta would move the
                    // whole plug by the frame's origin. Turned first by whatever placed its
                    // vertex, then into the plug frame.
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

        // Whether a climbed level swallowed the body rather than the plug. The
        // converter refuses the same case earlier and more bluntly, by asking
        // whether the level IS a humanoid bone; that also turns away a shaft on
        // its own renderer weighted to Hips, which bakes correctly. This path
        // is the toolkit's, where the author is looking at the plug, so it asks
        // the narrower question.
        //
        // Humanoid extremities decide it, since no shaft sits above one, and
        // the question is asked of THIS renderer's captured bones rather than
        // the hierarchy: a head is under Hips on every avatar, but only a body
        // mesh is skinned to it. Nothing is claimed on a rig with no humanoid
        // mapping, where the bake behaves as it did before.
        static bool TookTheBody(SkinnedMeshRenderer skin, HashSet<int> captured)
        {
            var animator = skin.GetComponentInParent<Animator>();
            if (animator == null || !animator.isHuman) return false;

            var ends = new[]
            {
                HumanBodyBones.Head, HumanBodyBones.LeftFoot,
                HumanBodyBones.RightFoot, HumanBodyBones.LeftHand,
            };
            foreach (var end in ends)
            {
                var bone = animator.GetBoneTransform(end);
                if (bone == null) continue;
                for (int b = 0; b < skin.bones.Length; b++)
                {
                    if (skin.bones[b] == bone && captured.Contains(b)) return true;
                }
            }
            return false;
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

            // Shape blocks follow the base one, nine floats a vertex. The shader
            // finds block s at 1 + count*10 + s*count*9, which is why the vertex
            // count has to be on the material.
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

            // Point filtering and no mips are load-bearing, not tidiness: the
            // shader reassembles floats from raw bytes, so any interpolation
            // corrupts the values.
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

        // The uploader wants these flags and a code-made texture has no
        // importer to carry them. Written on the asset, as the CCK does.
        public static void SettleForUpload(Texture2D texture)
        {
            if (texture == null) return;
            var so = new SerializedObject(texture);
            var readable = so.FindProperty("m_IsReadable");
            var streaming = so.FindProperty("m_StreamingMipmaps");
            var priority = so.FindProperty("m_StreamingMipmapsPriority");
            if (readable != null) readable.boolValue = false;
            if (streaming != null) streaming.boolValue = true;
            if (priority != null) priority.intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(texture);
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
            // From the mesh's OWN bounds, recomputed, never from whatever a
            // previous bake left behind. This is a shared asset: expanding the
            // current value grows it again on every reconvert, and the reports
            // invite reconverting, so a session of tuning used to ratchet it.
            mesh.RecalculateBounds();
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
