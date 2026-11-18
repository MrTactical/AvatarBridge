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
            public float Radius;        // plug-local, the widest shaft vertex off the axis
            public bool FromSkinnedMesh;
            // A skinned shaft's normal length at bake, the skin's own scale then, which the
            // shader holds the live normal against. 0 on a plain mesh.
            public float SkinScale;
            // The mesh this bake describes. Apply names the generated material
            // after the pair: a bake is indexed by mesh-global vertex id, so two
            // meshes can never share one.
            public Renderer Renderer;
            // The root the bake ACTUALLY measured from, not always the one handed
            // in. Ask this one which vertices are the plug's, or the chain is wider.
            public Transform Root;
            public List<string> Shapes = new List<string>();
            // Each baked shape's weight on the renderer at bake time, 0..1.
            // The material starts there; an animation overrides it.
            public float[] ShapeWeights;
            public List<string> MovingShapes = new List<string>();   // every shape that moves the plug

            // The frame the deform works in, world space at bake time. Not
            // the plug object's transform; anything measuring the plug uses this.
            public Vector3 Origin;
            public Quaternion Rotation;

            // The lowest plug vertex wholly on one bone, -1 if none. The readout
            // skins like it, so it recovers the deform's own frame.
            public int AnchorVertex = -1;
            // The far end of the shaft, for the readout's live marker.
            public int TipVertex = -1;
            // The portal's band, _YAPS_PortalSpan: 0 for a socket, -1 for a
            // mesh too coarse to cut.
            public float PortalSpan;
            // The bake's floats as written, only when asked for: the second
            // copy extends them, and the saved texture is no longer readable.
            public float[] Pixels;
        }

        // Past this a band is a fifth of the plug: so much of it collapses
        // inside each body that the cut reads as the plug shrinking.
        public const float PortalSpanLimit = 0.2f;

        // A quarter more than the widest triangle, so none spans the cut.
        const float PortalSpanMargin = 1.25f;

        public const string CoarseNote = "This plug's triangles are too long along its shaft for a portal to cut " +
            "it, so it stops at a portal entrance as at any hole. Add loops along the shaft and bake again.";

        // A second mesh cuts at the first one's band or not at all, so one
        // coarser than the first stops while the rest of the plug goes through.
        public const string CoarseExtraNote = "This mesh is baked into the plug, and its triangles are longer along " +
            "the shaft than the plug's main mesh's, so the rest of the plug goes through a portal and this mesh " +
            "stops at the entrance. Add loops along its shaft and bake again.";

        // wantedShapes: a socket names its stages; a plug takes the shapes
        // that move it most. objectFrame: a socket's shader adds the shape
        // deltas in the mesh's own frame, so a socket bake stays in it.
        // sizeScale: the largest a size animation makes the plug (LargestSize),
        // so its culling box holds it at that size.
        public static Result Bake(Renderer renderer, Transform plugRoot, string outputDir,
            BridgeReport report, out string failure, IList<string> wantedShapes = null,
            bool objectFrame = false, bool flipAxis = false, Result shareFrameWith = null,
            float sizeScale = 1f, bool keepPixels = false)
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

            // DOWN TO THE SHAFT when the given root is the hub above it. Here
            // because both builders pass through. Reported: it moves where the
            // bend begins. A socket has no shaft to find.
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

            // Measured in the scene as it stands: a turned or scaled root bakes
            // that into every position, and the shader assumes neither.
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

            // Frame from the mesh, not the plug root. A plain mesh uses its
            // object's origin and +Z: no bones to recover a frame from.
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
                // A second mesh takes the FIRST one's frame, or a collar on the
                // same bones bends as a separate plug.
                origin = shareFrameWith.Origin;
                rotation = shareFrameWith.Rotation;
            }
            else
            {
                // The avatar's up where the mesh is measured: world for a skinned mesh,
                // the renderer's own space for a plain one.
                var avatar = renderer.GetComponentInParent<Animator>(true);
                Vector3 avatarUp = (avatar != null ? avatar.transform : renderer.transform.root).up;
                if (staticMesh) avatarUp = renderer.transform.InverseTransformDirection(avatarUp);
                MeasureFrame(worldPositions, activeWeights, rootPos, rootRot, flipAxis, avatarUp,
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
            var skinScales = new List<float>();

            // The readout's anchor. A blended vertex recovers a blended frame,
            // so the lowest one wholly on one bone, else the most wholly owned.
            var boneWeights = staticMesh ? null : mesh.boneWeights;
            int anchor = -1;
            float anchorScore = -1f;
            int tip = -1;
            float tipScore = -1f;

            for (int i = 0; i < count; i++)
            {
                positions[i] = toPlug.MultiplyPoint3x4(worldPositions[i]);
                normals[i] = toPlug.MultiplyVector(worldNormals[i]).normalized;
                tangents[i] = toPlug.MultiplyVector(worldTangents[i]).normalized;
                if (activeWeights[i] > 0.001f)
                {
                    active++;
                    length = Mathf.Max(length, positions[i].z);
                    // Shaft vertices only, as MeasureFrame takes them: a faint body
                    // weight gave a body-mesh plug a thigh's width.
                    if (positions[i].z >= 0f && activeWeights[i] > 0.5f)
                    {
                        radius = Mathf.Max(radius, positions[i].x * positions[i].x + positions[i].y * positions[i].y);
                        if (!staticMesh && !objectFrame) skinScales.Add(worldNormals[i].magnitude);
                    }

                    // Behind the base is not the shaft; the shader reads it inactive.
                    if (positions[i].z >= 0f)
                    {
                        float single = boneWeights != null && boneWeights.Length > i ? boneWeights[i].weight0 : 1f;
                        bool solid = activeWeights[i] > 0.999f && single > 0.999f;
                        float score = solid ? 1000f - positions[i].z : activeWeights[i] * single;
                        if (score > anchorScore)
                        {
                            anchorScore = score;
                            anchor = i;
                        }
                        // On a one-bone shaft the markers always coincide,
                        // which is true: that bone cannot bend.
                        float far = solid ? 1000f + positions[i].z : activeWeights[i] * single;
                        if (far > tipScore)
                        {
                            tipScore = far;
                            tip = i;
                        }
                    }
                }
            }

            // The first mesh's length for all, or reach fractions would mean a
            // different distance on each renderer.
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

            // A socket's material never deforms, so only a plug wants a band.
            float portalSpan = objectFrame ? 0f : PortalSpanOf(mesh, positions, activeWeights, length);
            if (portalSpan > PortalSpanLimit) portalSpan = -1f;
            // A second mesh takes the first one's band, or it would come out
            // of the exit shifted against it by twice the difference.
            if (shared) portalSpan = portalSpan >= 0f && portalSpan <= shareFrameWith.PortalSpan ? shareFrameWith.PortalSpan : -1f;
            if (portalSpan < 0f)
                report?.Approximated(Category, $"\"{renderer.name}\" stops at a portal entrance",
                    shared && shareFrameWith.PortalSpan >= 0f ? CoarseExtraNote : CoarseNote);

            // The mesh reaching the vertex shader has blendshapes applied and
            // the bake is the rest pose; the deltas let the shader rebuild it.
            var shapes = CaptureShapes(mesh, skin, toPlug, activeWeights, placements, out var shapeNames,
                out var movingShapes, wantedShapes);

            // Shape offsets are at bake size, vertices at live size; the skin
            // scale lets the shader carry them across.
            if (objectFrame && !staticMesh)
                for (int i = 0; i < count; i++)
                    if (shapes.Any(block => block[i * 3].sqrMagnitude > 1e-12f)) skinScales.Add(worldNormals[i].magnitude);

            // Held shapes too: the bent path rebuilds from the bake, so a part
            // hidden by a shape at 100 reappeared when the plug bent.
            float[] heldWeights = null;
            if (!objectFrame && skin != null)
            {
                int held = HoldShapes(mesh, skin, toPlug, placements, shapeNames,
                    positions, normals, tangents, out heldWeights);
                if (held > 0)
                {
                    report?.Converted(Category, $"{renderer.name}: kept {BridgeElements.Count(held, "blendshape", "blendshapes")} it holds",
                        "They sit at a fixed value on the renderer, so the plug keeps them while it " +
                        "bends instead of snapping back to the unshaped mesh.");
                }
            }

            var texture = WriteTexture(positions, normals, tangents, activeWeights, count, shapes, out var pixels);
            Directory.CreateDirectory(outputDir);
            // Named for the plug, never numbered: a unique path left ten MB per
            // reconvert. The parent is in the name because same-named meshes
            // wrote one file and the first plug lost its bake.
            string owner = plugRoot != null && plugRoot.parent != null
                ? plugRoot.parent.name + " " + plugRoot.name
                : plugRoot != null ? plugRoot.name : "plug";
            string path = outputDir + "/YAPS " + Sanitise(renderer.name) + " "
                          + Sanitise(owner) + " " + PlaceKey(renderer.transform, plugRoot) + " bake.asset";
            texture = SaveInPlace(texture, path);
            SettleForUpload(texture);

            // How far a bend or shape can carry a vertex, for a mesh wholly the
            // plug; zero otherwise (a body mesh keeps its box). Off-chain vertices
            // within a shaft length of the base (a ring, balls on the hips) count.
            float fit = 0f;
            if (!staticMesh && !objectFrame)
            {
                for (int i = 0; i < count; i++)
                {
                    var p = positions[i];
                    if (activeWeights[i] <= 0.001f && p.magnitude > length)
                    {
                        fit = 0f;
                        break;
                    }
                    float far = Mathf.Abs(p.z) + new Vector2(p.x, p.y).magnitude;
                    foreach (var block in shapes) far += block[i * 3].magnitude;
                    fit = Mathf.Max(fit, far);
                }
            }

            // Unity culls on rest bounds, so a bending plug could vanish mid-bend.
            ExtendBounds(renderer, mesh, length * sizeScale, fit * sizeScale, outputDir, origin, plugRoot, objectFrame);

            bool drifted = axisDrift > 5f || originDrift > 0.01f;
            if (staticMesh && drifted && !objectFrame)
            {
                // A plain mesh bends around its origin along +Z. Said loudly: it
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
                FromSkinnedMesh = !staticMesh,
                SkinScale = skinScales.Count > 0 ? skinScales.OrderBy(x => x).ElementAt(skinScales.Count / 2) : 0f,
                Renderer = renderer,
                Root = plugRoot,
                Shapes = shapeNames,
                ShapeWeights = heldWeights,
                MovingShapes = movingShapes,
                Origin = origin,
                Rotation = rotation,
                AnchorVertex = anchor,
                TipVertex = tip,
                PortalSpan = portalSpan,
                Pixels = keepPixels ? pixels : null,
            };
        }

        // The widest z-span of any triangle wholly on the shaft, over the
        // rest length, with the margin.
        static float PortalSpanOf(Mesh mesh, Vector3[] positions, List<float> activeWeights, float length)
        {
            if (length <= 0f) return -1f;
            var tris = mesh.triangles;
            float widest = 0f;
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if (a >= positions.Length || b >= positions.Length || c >= positions.Length) continue;
                if (activeWeights[a] <= 0.001f || activeWeights[b] <= 0.001f || activeWeights[c] <= 0.001f) continue;
                float lo = Mathf.Min(positions[a].z, Mathf.Min(positions[b].z, positions[c].z));
                float hi = Mathf.Max(positions[a].z, Mathf.Max(positions[b].z, positions[c].z));
                widest = Mathf.Max(widest, hi - lo);
            }
            return widest / length * PortalSpanMargin;
        }

        // Which child chain is the SHAFT when the inherited root is the hub
        // above it (else the length spans the hub and the bend starts behind
        // the base). Null when unclear, which is usual. Bone positions, never
        // bind-space vertices. A wrong root is worse than an inherited one, so:
        // more than one chain, and the winner reaching twice the runner-up.
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

        // Belongs to the plug, not merely meets it: the body stops at the root
        // bone, a ring or harness rides the shaft however little of it does.
        // Requiring MOST of the mesh left a harness rigid while the shaft bent
        // through it. "Past the root" means mostly held there: an auto-weighted
        // body can carry a trace of the second bone.
        public static bool RidesPlug(Renderer renderer, Transform root)
        {
            var skin = renderer as SkinnedMeshRenderer;
            if (skin == null || root == null || skin.sharedMesh == null
                || skin.bones == null || skin.bones.Length == 0)
            {
                return false;
            }
            var chain = BonesUnder(skin.bones, root);
            int on = CountWeighted(skin, chain);
            if (on == 0) return false;
            if (on * 2 > skin.sharedMesh.vertexCount) return true;
            chain.RemoveWhere(b => skin.bones[b] == root);
            if (chain.Count == 0) return false;
            foreach (var w in skin.sharedMesh.boneWeights)
            {
                if (WeightOnPlug(w, chain) > 0.5f) return true;
            }
            return false;
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

        // Clones first so renderers sharing a material (clothing beside a plug
        // ships that way) cannot overwrite each other's vertex counts.
        // key: for a slot split off another plug's, or the second bake loads
        // the first one's material by path and takes it over.
        public static Material Apply(Result result, Material source, Shader patchedShader,
            string outputDir, bool skinned, string key = null)
        {
            var clone = Generated(source, patchedShader,
                outputDir + "/" + Sanitise(source.name + " (YAPS)")
                + Tail(source, result.Renderer) + (key != null ? " " + key : "") + ".mat");
            Apply(result, clone, skinned);
            return clone;
        }

        // Which source material AND mesh this copy came from, in six bytes.
        // By name alone, two exporter-default "Material"s overwrote each other
        // silently. GUID plus local id: stable, and distinct inside one FBX
        // (in-scene materials fall back to instance id). The MESH too: three
        // plug meshes on one material resolved to one asset, which holds one
        // bake. The renderer's path from the scene root, so a rebuild lands on
        // the same asset and two copies of one avatar stay apart.
        internal static string Tail(Material source, Renderer on)
        {
            while (StandsInFor.TryGetValue(source, out var original)) source = original;
            string id = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                source, out string guid, out long local)
                ? guid + local
                : source.GetInstanceID().ToString();
            return " " + YapsShaderPatcher.Hash(id + "|" + PathOf(on));
        }

        // A stand-in made in memory on another shader, by the material it was made from. It has
        // no asset to name it, and its instance id named a new bake file on every fresh bake.
        internal static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Material, Material> StandsInFor =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Material, Material>();

        static string PathOf(Renderer on)
        {
            if (on == null) return "";
            string path = Step(on.transform);
            for (var t = on.transform.parent; t != null; t = t.parent) path = Step(t) + "/" + path;
            return path;
        }

        // Siblings can share a name, and matching paths would share a material.
        // Only an ambiguous step is numbered.
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

        // The per-MESH half of a bake: a plug spanning renderers needs one bake
        // each, indexed by mesh-global vertex id. Knobs are written separately.
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
            target.SetFloat("_YAPS_BakeSkinScale", skinned ? result.SkinScale : 0f);
            target.SetFloat("_YAPS_ShapeCount", result.Shapes.Count);
            target.SetFloat("_YAPS_PortalSpan", result.PortalSpan);
            if (result.ShapeWeights != null)
            {
                // Where this material's lane reads them; ShareRenderer settles the rest.
                int lane = LaneOf(target);
                var weights = new float[MaxShapes];
                for (int k = 0; k < Mathf.Min(MaxShapes, result.ShapeWeights.Length); k++)
                    weights[YapsCurveMirror.ShapeSlot(k, lane)] = result.ShapeWeights[k];
                WritePacks(target, weights);
            }
            EditorUtility.SetDirty(target);
        }

        // Two plugs on one renderer. Animation reaches every slot, so lane 1
        // reads shape slots from the top and the "2" scale, girth and tag
        // properties. A plug is identified by its bake texture; the lowest slot
        // is lane 0, so the answer is order-free. Every plug material gets the
        // same packs, since a clip animating one component takes the rest from
        // one material. Returns how many of the second plug's shapes did not fit.
        // ponytail: two lanes; a third plug shares lane 1, add a lane 2 if that ever matters.
        public static int ShareRenderer(Renderer renderer)
        {
            if (renderer == null) return 0;
            var mats = renderer.sharedMaterials;
            var plugs = new List<(Texture Bake, List<Material> Slots)>();
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || !m.HasProperty("_YAPS_Bake") || !m.HasProperty("_YAPS_PlugLane")) continue;
                if ((YapsShaderPatcher.RolesOf(m.shader) & YapsShaderPatcher.Roles.Plug) == 0) continue;
                var bake = m.GetTexture("_YAPS_Bake");
                if (bake == null) continue;
                int at = plugs.FindIndex(p => p.Bake == bake);
                if (at < 0) plugs.Add((bake, new List<Material> { m }));
                else if (!plugs[at].Slots.Contains(m)) plugs[at].Slots.Add(m);
            }
            foreach (var p in plugs)
                foreach (var m in p.Slots) MirrorSwitches(m);
            if (plugs.Count < 2)
            {
                // Alone again, after its partner went: back to the first plug's place.
                foreach (var p in plugs)
                    foreach (var m in p.Slots)
                        if (m.GetFloat("_YAPS_PlugLane") > 0.5f) Relane(m, 0);
                return 0;
            }

            var weights = new float[MaxShapes];
            int firstCount = 0, dropped = 0;
            for (int k = 0; k < plugs.Count; k++)
            {
                int lane = k == 0 ? 0 : 1;
                var first = plugs[k].Slots[0];
                var held = Held(first);
                int count = Mathf.RoundToInt(first.GetFloat("_YAPS_ShapeCount"));
                if (lane == 0) firstCount = count;
                else if (count > MaxShapes - firstCount)
                {
                    dropped += count - (MaxShapes - firstCount);
                    count = Mathf.Max(0, MaxShapes - firstCount);
                }
                for (int s = 0; s < count; s++) weights[YapsCurveMirror.ShapeSlot(s, lane)] = held[s];
                foreach (var m in plugs[k].Slots)
                {
                    m.SetFloat("_YAPS_PlugLane", lane);
                    m.SetFloat("_YAPS_ShapeCount", count);
                }
            }
            foreach (var p in plugs)
                foreach (var m in p.Slots)
                {
                    WritePacks(m, weights);
                    EditorUtility.SetDirty(m);
                }
            return dropped;
        }

        // A plug's own held weights in its own order, wherever its lane put them.
        static float[] Held(Material m)
        {
            var packs = Packs.Select(m.GetVector).ToArray();
            int lane = m.GetFloat("_YAPS_PlugLane") > 0.5f ? 1 : 0;
            var held = new float[MaxShapes];
            for (int s = 0; s < MaxShapes; s++)
            {
                int slot = YapsCurveMirror.ShapeSlot(s, lane);
                held[s] = packs[slot >> 2][slot & 3];
            }
            return held;
        }

        static void Relane(Material m, int lane)
        {
            var held = Held(m);
            var weights = new float[MaxShapes];
            for (int s = 0; s < MaxShapes; s++) weights[YapsCurveMirror.ShapeSlot(s, lane)] = held[s];
            m.SetFloat("_YAPS_PlugLane", lane);
            WritePacks(m, weights);
            EditorUtility.SetDirty(m);
        }

        static readonly string[] Packs = { "_YAPS_ShapeWeights", "_YAPS_ShapeWeights2", "_YAPS_ShapeWeights3", "_YAPS_ShapeWeights4" };

        static void WritePacks(Material m, float[] weights)
        {
            for (int p = 0; p < Packs.Length; p++)
                m.SetVector(Packs[p], new Vector4(weights[p * 4], weights[p * 4 + 1], weights[p * 4 + 2], weights[p * 4 + 3]));
        }

        // A plug's switches as the second plug reads them, from the values every builder and
        // the material panel write. Only animation tells the two apart.
        public static void MirrorSwitches(Material m)
        {
            foreach (string name in new[] { "_YAPS_Enabled", "_YAPS_SelfAllow" })
                if (m != null && m.HasProperty(name) && m.HasProperty(name + "2")) m.SetFloat(name + "2", m.GetFloat(name));
        }

        // The property a plug's own toggle animates on a mesh: the second plug has its own.
        public static string SwitchName(string name, int lane) => lane > 0 ? name + "2" : name;

        // The lane a plug's material was given; 0 for a lone plug and for any build before lanes.
        public static int LaneOf(Material m) =>
            m != null && m.HasProperty("_YAPS_PlugLane") && m.GetFloat("_YAPS_PlugLane") > 0.5f ? 1 : 0;

        // At a fixed path: a unique path left a new numbered copy per click.
        // Only the existing file's identity is kept (its GUID); values are
        // re-derived from the source so stale settings never carry forward.
        public static Material Generated(Material source, Shader patched, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // The values Poiyomi's lock left, when the patch came from its locked stand-in.
            var basis = YapsLock.Basis(source);
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null && existing != source)
            {
                // Source's shader first, so Wear really swaps: Wear skips a shader
                // already worn, leaving bake values outside the layout, and the
                // next refresh dropped them, bake texture included.
                existing.shader = basis.shader;
                existing.CopyPropertiesFromMaterial(basis);
                if (patched != null)
                {
                    YapsShaderPatcher.Wear(existing, patched);
                }
                YapsLock.Stamp(existing, source);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            var made = new Material(basis) { name = source.name + " (YAPS)" };
            if (patched != null)
            {
                YapsShaderPatcher.Wear(made, patched);
            }
            YapsLock.Stamp(made, source);
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
                // Plain mesh: the shader reads object space, so bake it untransformed.
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

            // The root and everything beneath, so a plug's own chain bakes whole.
            var plugBones = BonesUnder(bones, plugRoot);
            if (plugBones.Count == 0)
            {
                // The plug object often hangs off a bone: climb to the first real one.
                Transform climbed = null;
                for (var above = plugRoot.parent; above != null && plugBones.Count == 0;
                     above = above.parent)
                {
                    plugBones = BonesUnder(bones, above);
                    climbed = above;
                }

                // The climb can overshoot: a boneless plug in a body mesh climbs
                // to Hips and the whole avatar bakes as one shaft (ten did). A
                // shaft on its own renderer on Hips is right to, so what came
                // with it decides, not the count. Sockets are exempt: they bake
                // shapes, and weights only pick which vertices exist.
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
                // Bone x bindpose, or the vertex lands where the modeller left it.
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
            Vector3 avatarUp, out Vector3 origin, out Quaternion rotation, out float axisDrift, out float originDrift)
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

            // The avatar's up, not the bone's: a Blender bone's y runs along it,
            // so its roll decided and a roll-0 plug drooped sideways. The bone
            // decides only when the shaft points straight up or down.
            var up = avatarUp;
            if (up.sqrMagnitude < 1e-8f || Mathf.Abs(Vector3.Dot(up.normalized, axis)) > 0.99f)
            {
                up = authored * Vector3.up;
                if (Mathf.Abs(Vector3.Dot(up, axis)) > 0.99f)
                {
                    up = authored * Vector3.right;
                }
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
            var restN = mesh.normals;
            var restT = mesh.tangents;
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
                        block[i * 3 + 1] = DirectionDelta(toPlug, place, RestNormal(restN, i), deltaN[i]);
                        block[i * 3 + 2] = DirectionDelta(toPlug, place, RestTangent(restT, i), deltaT[i]);
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
                    // Directions: rotation only, or the delta moves by the frame's origin.
                    var place = i < placements.Count ? placements[i] : Matrix4x4.identity;
                    block[i * 3 + 0] = toPlug.MultiplyVector(place.MultiplyVector(deltaP[i]));
                    block[i * 3 + 1] = DirectionDelta(toPlug, place, RestNormal(restN, i), deltaN[i]);
                    block[i * 3 + 2] = DirectionDelta(toPlug, place, RestTangent(restT, i), deltaT[i]);
                }
                captured.Add(block);
                names.Add(mesh.GetBlendShapeName(index));
            }
            return captured;
        }

        // In the units the unit-length rest vector is stored in. Turned by the
        // matrices alone it carried their unit scale: at 0.07 a 90 degree turn
        // baked as 4, the recovered frame spun and held shapes tore the plug.
        static Vector3 DirectionDelta(Matrix4x4 toPlug, Matrix4x4 place, Vector3 rest, Vector3 delta)
        {
            float length = toPlug.MultiplyVector(place.MultiplyVector(rest)).magnitude;
            return length > 1e-8f ? toPlug.MultiplyVector(place.MultiplyVector(delta)) / length : Vector3.zero;
        }

        // The rest vectors the capture stored, with the same fallbacks.
        static Vector3 RestNormal(Vector3[] normals, int i) => i < normals.Length ? normals[i] : Vector3.forward;
        static Vector3 RestTangent(Vector4[] tangents, int i) => i < tangents.Length ? (Vector3) tangents[i] : Vector3.right;

        // Held weights, split by what the shader can see. A baked shape keeps
        // its weight on the material; any other held shape is folded into the
        // rest pose, the same turn as a shape block. Returns how many were held.
        // ponytail: last frame scaled by weight, exact only for one-frame shapes.
        static int HoldShapes(Mesh mesh, SkinnedMeshRenderer skin, Matrix4x4 toPlug,
            List<Matrix4x4> placements, List<string> baked,
            Vector3[] positions, Vector3[] normals, Vector3[] tangents, out float[] bakedWeights)
        {
            bakedWeights = new float[baked.Count];
            int held = 0;
            int count = positions.Length;
            Vector3[] dp = null, dn = null, dt = null, restN = null;
            Vector4[] restT = null;
            for (int s = 0; s < mesh.blendShapeCount; s++)
            {
                float w = skin.GetBlendShapeWeight(s) * 0.01f;
                if (Mathf.Abs(w) < 1e-4f)
                {
                    continue;
                }
                held++;
                int slot = baked.IndexOf(mesh.GetBlendShapeName(s));
                if (slot >= 0)
                {
                    bakedWeights[slot] = w;
                    continue;
                }
                dp ??= new Vector3[count];
                dn ??= new Vector3[count];
                dt ??= new Vector3[count];
                mesh.GetBlendShapeFrameVertices(s, mesh.GetBlendShapeFrameCount(s) - 1, dp, dn, dt);
                restN ??= mesh.normals;
                restT ??= mesh.tangents;
                for (int i = 0; i < count; i++)
                {
                    var place = i < placements.Count ? placements[i] : Matrix4x4.identity;
                    positions[i] += toPlug.MultiplyVector(place.MultiplyVector(dp[i])) * w;
                    normals[i] = (normals[i] + DirectionDelta(toPlug, place, RestNormal(restN, i), dn[i]) * w).normalized;
                    tangents[i] = (tangents[i] + DirectionDelta(toPlug, place, RestTangent(restT, i), dt[i]) * w).normalized;
                }
            }
            return held;
        }

        // Whether a climbed level swallowed the body. The converter refuses
        // any humanoid level, which also turns away a valid shaft on Hips; the
        // toolkit asks the narrower question: does THIS renderer skin to a
        // humanoid extremity (no shaft sits above one)? No humanoid mapping,
        // no claim.
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

        // Shared with YapsSlotSplit.Mask, so the bake and the slot split
        // agree on which vertices are the plug.
        internal static float WeightOnPlug(BoneWeight w, HashSet<int> plugBones)
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
            List<float> active, int count, List<Vector3[]> shapes, out float[] pixels)
        {
            int floats = 1 + count * FloatsPerVertex + shapes.Count * count * FloatsPerShapeVertex;
            int height = Mathf.Max(1, Mathf.CeilToInt((float) floats / TextureWidth));
            // One float per RGBA32 pixel, uploaded as raw bytes: red is the
            // least significant byte, as the shader reads it.
            pixels = new float[TextureWidth * height];

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

            return BakeTexture(pixels);
        }

        // Point filtering, no mips: floats are rebuilt from raw bytes.
        internal static Texture2D BakeTexture(float[] pixels)
        {
            var texture = new Texture2D(TextureWidth, pixels.Length / TextureWidth, TextureFormat.RGBA32, false, true)
            {
                name = "YAPS Bake",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
            texture.SetPixelData(pixels, 0);
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

        static void Write(float[] pixels, ref int at, float value) => pixels[at++] = value;

        // --- housekeeping ---------------------------------------------

        // length: how far a bend carries the shaft from the base. fit: the
        // same over every vertex and baked shape when the renderer is wholly
        // the plug, else 0. Both already at the largest animated size.
        static void ExtendBounds(Renderer renderer, Mesh mesh, float length, float fit, string outputDir,
            Vector3 origin, Transform plugRoot, bool socket)
        {
            if (renderer is SkinnedMeshRenderer skin)
            {
                // Never updateWhenOffscreen: it fits the CPU pose, misses the
                // shader's bend and discards localBounds. Sockets keep their box.
                if (socket || length <= 0f)
                {
                    return;
                }
                bool boned = skin.bones != null && skin.bones.Length > 0;
                var space = skin.rootBone != null ? skin.rootBone : skin.transform;
                var toLocal = boned ? space.worldToLocalMatrix
                    : space.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                var basePoint = boned ? origin : skin.transform.InverseTransformPoint(origin);
                Bounds Round(float r) => AvatarHygiene.TransformBounds(toLocal,
                    new Bounds(basePoint, Vector3.one * (r * 2f)));
                if (fit > 0f && CarriesBase(space, plugRoot))
                {
                    // Just that reach: vertex lights are chosen by bounds, and a
                    // wide box lets distant lights take the plug's four. Unverified
                    // in ChilloutVR. Tight only while the root bone carries the
                    // base; animation or physics moving the root is not covered.
                    skin.updateWhenOffscreen = false;
                    skin.localBounds = Round(Mathf.Max(fit, length));
                }
                else
                {
                    // A body mesh keeps its box, grown only where it misses the
                    // reach, which the converter's avatar-sized one seldom does.
                    var reach = Round(length);
                    var box = skin.localBounds;
                    if (box.Contains(reach.min) && box.Contains(reach.max))
                    {
                        return;
                    }
                    box.Encapsulate(reach);
                    skin.localBounds = box;
                }
                EditorUtility.SetDirty(skin);
                return;
            }
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || length <= 0f)
            {
                return;   // nothing to widen, so no copy either
            }
            // A model sub-asset loses the edit on restart and meanwhile widens
            // every user's box, so it, a built-in or a scene mesh is copied. The
            // toolkit's own .asset meshes keep the edit; other tools' are shared.
            string meshPath = AssetDatabase.GetAssetPath(mesh);
            bool ours = meshPath.StartsWith("Assets/YAPS/", StringComparison.Ordinal)
                        || meshPath.StartsWith(outputDir.TrimEnd('/') + "/", StringComparison.Ordinal);
            if (!(AssetDatabase.IsMainAsset(mesh) && ours
                  && meshPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)))
            {
                string path = outputDir + "/YAPS " + Sanitise(renderer.name) + " "
                              + PlaceKey(renderer.transform) + " bounds.asset";
                var copy = UnityEngine.Object.Instantiate(mesh);
                copy.name = mesh.name;
                copy = SaveInPlace(copy, path);
                filter.sharedMesh = copy;
                EditorUtility.SetDirty(filter);
                mesh = copy;
            }
            // From the mesh's own bounds, recomputed: growing the current value
            // ratcheted it on every reconvert.
            mesh.RecalculateBounds();
            var bounds = mesh.bounds;
            bounds.Expand(length * 2f);
            mesh.bounds = bounds;
            EditorUtility.SetDirty(mesh);
        }

        // Whether the root bone takes the plug's base wherever it goes: it is
        // the plug's root, or above it with no humanoid bone and no constraint
        // between. An Armature root bone above the Hips stays put while a
        // crouch carries the plug half a metre away.
        static bool CarriesBase(Transform space, Transform plugRoot)
        {
            var animator = plugRoot != null ? plugRoot.GetComponentInParent<Animator>(true) : null;
            var human = new HashSet<Transform>();
            if (animator != null && animator.isHuman)
            {
                for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++)
                {
                    var t = animator.GetBoneTransform(b);
                    if (t != null) human.Add(t);
                }
            }
            for (var t = plugRoot; t != space; t = t.parent)
            {
                if (t == null || human.Contains(t)
                    || t.GetComponent<UnityEngine.Animations.IConstraint>() != null)
                {
                    return false;
                }
            }
            return true;
        }

        // The largest any clip scales the plug, as a ratio to the size it
        // was baked at: a hyper toggle doubles the shaft, and a box sized at
        // rest then culls it. bones: each bone a size animation may scale,
        // by its path from the animator root, as YapsCurveMirror takes them.
        public static float LargestSize(IEnumerable<AnimationClip> clips, IDictionary<string, Transform> bones)
        {
            float largest = 1f;
            if (clips == null || bones == null || bones.Count == 0) return largest;
            foreach (var clip in clips)
            {
                if (clip == null) continue;
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (b.type != typeof(Transform)
                        || !b.propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal)
                        || !bones.TryGetValue(b.path, out var bone) || bone == null)
                    {
                        continue;
                    }
                    int axis = b.propertyName[b.propertyName.Length - 1] - 'x';
                    if (axis < 0 || axis > 2) continue;
                    float atBake = Mathf.Max(Mathf.Abs(bone.localScale[axis]), 1e-4f);
                    foreach (var key in AnimationUtility.GetEditorCurve(clip, b).keys)
                    {
                        largest = Mathf.Max(largest, Mathf.Abs(key.value) / atBake);
                    }
                }
            }
            return largest;
        }

        // Rewritten in place: recreating breaks the identity earlier
        // conversions point at.
        internal static T SaveInPlace<T>(T made, string path) where T : UnityEngine.Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(made, path);
                return made;
            }
            // CopySerialized carries the name too; keep the file's.
            made.name = existing.name;
            EditorUtility.CopySerialized(made, existing);
            UnityEngine.Object.DestroyImmediate(made);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssetIfDirty(existing);
            return existing;
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

        // Six hex digits for where these objects sit, stable across
        // conversions. By name, numbered only where siblings share one:
        // sibling indices moved on any reorder and left a bake behind.
        public static string PlaceKey(params Transform[] objects)
        {
            var at = new System.Text.StringBuilder();
            foreach (var o in objects)
            {
                for (var t = o; t != null && t.parent != null; t = t.parent) at.Append(Step(t)).Append('/');
                at.Append('|');
            }
            return Hash128.Compute(at.ToString()).ToString().Substring(0, 6);
        }
    }
}
#endif
