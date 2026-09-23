// The in-game readout for a plug that will not behave.
//
// Twelve cells painted by our own shader, saying who resolved the socket,
// whether the plug is bending, what the atlas saw and what state the plug's
// own bake and frame are in, plus two markers saying whether anything has
// moved its bones.
//
// The plug's own debug view answers in LENGTH and straightens the plug to
// do it, so the bend and the reason for the bend can never be read at the
// same time. This leaves the plug alone and reports beside it.
//
// DRAWN BY THE PLUG'S OWN RENDERER, as three extra quads in one submesh
// with one material slot. The first version was a separate
// object beside the plug, and a separate renderer cannot see two things
// the plug sees. The contact channel and the enabled flag arrive as
// animated material properties, which land in the plug renderer's
// property block and nowhere else; and the plug's frame is recovered
// from its own skinned vertices. So that readout resolved from a bone
// matrix with no channel, and reported nobody while the plug was bent
// round a socket. A second opinion is not a readout.
//
// Sharing the renderer shares the block. The strip and the rest marker
// are skinned exactly like one plug vertex, the anchor, so they land where
// it lands and the shader recovers the frame the deform recovers, from the
// same bake entry. The live marker is skinned like a vertex at the far end
// of the shaft instead, and that pair is the only thing here that can see
// the BONES move: one vertex can only be measured in world space, where an
// avatar turning round moves every number, and two a shaft apart cannot be
// fooled that way. The plug's own material never draws any of them: they
// sit past _YAPS_VertexCount, in a submesh it does not own.
//
// Built on every plug of an avatar, hidden, unless the plug's In-game
// readout tick is off. One synced menu toggle shows them all, so a helper
// sees the plug as the wearer's own client resolves it. A plug on a prop
// has no menu to show it, and gets none. Plugs sharing a mesh get one
// submesh each, built together.
//
// Every socket of an avatar gets one too, a quad of its own beside its
// atlas writer (YAPS/Socket Readout), on the same toggle, with the same
// tick on the socket.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using AvatarBridge.Yaps;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarBridge
{
    public static class YapsDebugOverlayBuilder
    {
        const string Category = "YAPS penetration system";
        // The object the first version built beside the plug. Still
        // removed, so an avatar built with it does not keep a dead quad.
        public const string ObjectName = "YAPS Debug Overlay";
        const string ShaderName = "YAPS/Debug Overlay";
        const string Folder = "Assets/YAPS/Generated";
        const string MeshSuffix = " readout";
        const int Corners = 4;
        // The strip, the rest marker and the live marker.
        const int Quads = 3;

        // The menu switch that shows every readout on the avatar.
        public const string Parameter = "YAPS/Readout";
        const string MenuLabel = "YAPS readout";
        const string LayerName = "YAPS readout";
        const string Property = "_YAPS_ReadoutOn";
        const string SocketShaderName = "YAPS/Socket Readout";
        public const string SocketReadoutName = "Socket Readout";
        const string SharedFolder = "Assets/YAPS";

        // The toolkit's entry: this plug's readout recorded, then every
        // readout on its mesh built again.
        public static void Apply(YapsPlug plug, YapsBaker.Result result, Material patched,
            BridgeReport report)
        {
            if (plug == null)
            {
                return;
            }
            Record(plug, result, patched, plug.name, report);
            Build(plug.readoutRenderer, report);
        }

        // What a plug's readout is built from, kept on the plug so another
        // plug baked into the same mesh can build it again without baking
        // this one. Builds nothing: Build does, once every plug on the mesh
        // is measured. Built one plug at a time, the next plug measured the
        // last one's readout as part of itself and patched its slot into a
        // copy of the plug, so a mesh with three plugs drew the plug three
        // times and only the last readout worked.
        public static void Record(YapsPlug record, YapsBaker.Result result, Material patched,
            string label, BridgeReport report)
        {
            if (record == null)
            {
                // Without somewhere to record the mesh this replaces, a later
                // build could not put it back, and the readout would outlive
                // the tick that asked for it.
                report?.Warning(Category, label,
                    "There is no plug component to remember the mesh the readout replaces, so none " +
                    "was built. The plug itself is unaffected.");
                return;
            }
            // The first version's object beside the plug.
            Transform parent = result != null && result.Root != null ? result.Root : record.transform;
            var old = parent.Find(ObjectName);
            if (old != null)
            {
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            }

            record.readoutRenderer = result != null ? result.Renderer : null;
            // The mesh the bake measured, which every record on the renderer
            // keeps: the plug that built the last readout can be deleted, and
            // the mesh has to come back all the same.
            var measured = MeshOf(record.readoutRenderer);
            if (measured != null && !measured.name.EndsWith(MeshSuffix, StringComparison.Ordinal))
            {
                record.readoutReplaced = measured;
            }
            record.readoutSource = patched;
            record.readoutAnchor = result != null ? result.AnchorVertex : -1;
            record.readoutTip = result != null ? result.TipVertex : -1;
            EditorUtility.SetDirty(record);
            if (record.readoutRenderer == null)
            {
                return;
            }
            // A plug baked into this material before this one no longer owns
            // it: the bake it would read is gone.
            foreach (var other in Records(record.readoutRenderer))
            {
                if (other == record || other.readoutSource != patched)
                {
                    continue;
                }
                other.readoutSource = null;
                EditorUtility.SetDirty(other);
            }
            if (record.readout && record.readoutAnchor < 0)
            {
                report?.Warning(Category, label,
                    "The bake has no vertex for the readout to hang from, so none was built. The " +
                    "plug itself is unaffected.");
            }
        }

        // Every readout one mesh carries, built together on the mesh it had
        // before any of them: a submesh and a material slot for each plug
        // that asks for one and still owns its material. A plug on a prop
        // has no menu to show it, and gets none. Safe to run again.
        public static void Build(Renderer renderer, BridgeReport report)
        {
            if (renderer == null)
            {
                return;
            }
            Restore(renderer);
            var mats = renderer.sharedMaterials.ToList();
            var wanted = Records(renderer)
                .Where(p => p.readout && p.readoutSource != null && p.readoutAnchor >= 0
                            && mats.Contains(p.readoutSource)
                            && p.GetComponentInParent<CVRAvatar>(true) != null)
                .ToList();
            if (wanted.Count == 0)
            {
                return;
            }

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                report?.Warning(Category, renderer.name,
                    "The readout's shader could not be found, so none was built. The plug itself " +
                    "is unaffected.");
                return;
            }
            var source = MeshOf(renderer);
            if (source == null)
            {
                report?.Warning(Category, renderer.name,
                    "The plug's renderer has no mesh, so no readout was built.");
                return;
            }
            if (source.name.EndsWith(MeshSuffix, StringComparison.Ordinal))
            {
                // Built on, it would stack a second set of readout vertices
                // on the first.
                report?.Warning(Category, renderer.name,
                    "The renderer still wears an earlier readout's mesh and the mesh under it could not " +
                    "be found, so no readout was built. Put the original mesh back on the renderer and bake again.");
                return;
            }

            var mesh = source;
            var built = new List<YapsPlug>();
            foreach (var plug in wanted)
            {
                var next = WithReadout(mesh, plug.readoutAnchor, plug.readoutTip, out string why);
                if (next == null)
                {
                    report?.Warning(Category, plug.name,
                        "The readout could not be added to \"" + source.name + "\": " + why +
                        ". The plug itself is unaffected.");
                    continue;
                }
                if (mesh != source)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
                mesh = next;
                built.Add(plug);
            }
            if (built.Count == 0)
            {
                return;
            }
            mesh.name = source.name + MeshSuffix;

            string dir = DirOf(built[0].readoutSource.GetTexture("_YAPS_Bake") as Texture2D) ?? Folder;
            Directory.CreateDirectory(dir);
            // Named for where the renderer sits as well: two renderers can share
            // a mesh, or a mesh name, and one would delete the other's.
            string key = Safe(renderer.name) + " " + YapsBaker.PlaceKey(renderer.transform);
            string meshPath = dir + "/YAPS " + key + MeshSuffix + ".asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var labels = new HashSet<string>();
            foreach (var plug in built)
            {
                // A material each, even for plugs of the same name: each
                // holds its own plug's anchor and bake. Every converted plug
                // is called BakedSpsPlug, so the renderer's key goes first.
                string label = key + " " + plug.name;
                for (int n = 2; !labels.Add(label); n++)
                {
                    label = key + " " + plug.name + " " + n;
                }
                var material = MaterialFor(shader, dir, label);
                Copy(plug.readoutSource, material, plug.name, report);
                material.SetFloat("_YAPS_AnchorVertex", plug.readoutAnchor);
                EditorUtility.SetDirty(material);
                mats.Add(material);
                plug.readoutReplaced = source;
                EditorUtility.SetDirty(plug);

                report?.Converted(Category, plug.name,
                    "A readout was added to this plug, hidden until the avatar's \"" + MenuLabel + "\" " +
                    "menu toggle shows it. Twelve cells in two rows, and two " +
                    "markers out on the plug itself. Top row, left to right: who resolved the " +
                    "socket, whether it is bending, how far away the socket is, what the screen " +
                    "atlas read, whether the atlas is on the camera drawing this view, and whether " +
                    "this plug asks for the atlas at all. Bottom row: whether the plug recovered " +
                    "its own frame, whether the readout's vertex is inside the bake, whether the " +
                    "bake read anything, whether it will take its own wearer's sockets, how many " +
                    "sockets are in the chain, and whether a socket was refused by this plug's " +
                    "tags. Grey or black is nothing, red is a fault, green is working. The two markers are the pair that " +
                    "answers a different question: the white one sits where the tip would be if " +
                    "nothing had moved the plug's BONES, the magenta one sits where the tip " +
                    "actually is. Together means the bones are where the bake left them and any " +
                    "bend you can see is the shader's; apart means something else is moving them, " +
                    "cloth or an animation or a constraint, and no cell above can tell you that. " +
                    "The toggle syncs, so everyone who can see the plug sees the readout while it is " +
                    "on, each drawn from what their own game resolves.");
            }
            SetMesh(renderer, mesh);
            renderer.sharedMaterials = mats.ToArray();
            EditorUtility.SetDirty(renderer);
        }

        // Every readout off one mesh, whichever plug built it: the mesh
        // they were built on back, and every readout slot dropped. Runs
        // before every bake as well as every build: a bake taken over a
        // readout counts its vertices as the plug's, and patches its slot
        // as a plug material.
        public static void Restore(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }
            var records = Records(renderer);
            var current = MeshOf(renderer);
            // Never a readout mesh itself: one built before readouts were
            // built together could have replaced another's.
            var original = records.Select(p => p.readoutReplaced)
                .FirstOrDefault(m => m != null && !m.name.EndsWith(MeshSuffix, StringComparison.Ordinal));
            if (original != null && current != null
                && current.name.EndsWith(MeshSuffix, StringComparison.Ordinal))
            {
                SetMesh(renderer, original);
            }
            var mats = renderer.sharedMaterials.ToList();
            if (mats.RemoveAll(IsReadout) > 0)
            {
                renderer.sharedMaterials = mats.ToArray();
            }
            EditorUtility.SetDirty(renderer);
        }

        public static void Restore(YapsPlug plug)
        {
            if (plug != null)
            {
                Restore(plug.readoutRenderer);
            }
        }

        // A plug taken out: every readout off its mesh and its own record
        // cleared. The caller builds the others again once the plug's
        // materials are back, or they copy a bake that is about to go.
        public static void Drop(YapsPlug plug)
        {
            if (plug == null || plug.readoutRenderer == null)
            {
                return;
            }
            var renderer = plug.readoutRenderer;
            Restore(renderer);
            plug.readoutRenderer = null;
            plug.readoutReplaced = null;
            plug.readoutSource = null;
            EditorUtility.SetDirty(plug);
        }

        // The mesh under every readout on this renderer, after a plug's
        // triangles were split out onto a copy of it.
        public static void Replaced(Renderer renderer, Mesh mesh)
        {
            foreach (var plug in Records(renderer))
            {
                plug.readoutReplaced = mesh;
                EditorUtility.SetDirty(plug);
            }
        }

        // The plugs that recorded a readout on this renderer.
        static List<YapsPlug> Records(Renderer renderer)
        {
            return renderer.transform.root.GetComponentsInChildren<YapsPlug>(true)
                .Where(p => p != null && p.readoutRenderer == renderer)
                .ToList();
        }

        // By shader, and by name when the shader is gone: a project that
        // lost the shader shows the slot magenta and still has to shed it.
        public static bool IsReadout(Material m)
        {
            if (m == null)
            {
                return false;
            }
            if (YapsMarks.IsReadoutMaterial(m))
            {
                return true;
            }
            return m.name.StartsWith(ObjectName, StringComparison.Ordinal);
        }

        // The plug's mesh with three more quads, every vertex a copy of one
        // real plug vertex: position, normal, tangent, every uv but the
        // first, bone weights and every blendshape delta. Skinned and shaped
        // the same, they arrive in the vertex shader where that vertex
        // arrives. The first uv carries the corner and WHICH quad instead;
        // the shader billboards from there, so where they rest is irrelevant.
        //
        // Quad 0 and quad 1 copy the base anchor, so both recover the plug's
        // frame from the same bake entry the deform uses. Quad 2 copies a
        // vertex at the far end of the shaft, and copies nothing else from
        // it: it is only ever asked where it arrives. That is the point of
        // it. Everything a shader can measure from ONE vertex is in world
        // space, so an avatar that turned round moves every number; two
        // points a shaft apart do not have that problem, and the gap between
        // them is the only thing here that can see the BONES move.
        static Mesh WithReadout(Mesh source, int anchor, int tip, out string why)
        {
            why = null;
            int n = source.vertexCount;
            if (anchor < 0 || anchor >= n)
            {
                why = "the anchor vertex is not on this mesh";
                return null;
            }
            if (tip < 0 || tip >= n)
            {
                // Not fatal: the strip and the rest marker still mean what
                // they mean. Only the pair reading is lost.
                tip = anchor;
            }
            Vector3[] vertices;
            try
            {
                vertices = source.vertices;
            }
            catch (Exception e)
            {
                why = "it would not hand over its vertices (" + e.GetType().Name +
                      "): tick Read/Write Enabled on the model importer";
                return null;
            }

            // Which real vertex each added vertex copies, in order.
            var from = new int[Quads * Corners];
            for (int q = 0; q < Quads; q++)
            {
                for (int c = 0; c < Corners; c++)
                {
                    from[q * Corners + c] = q == 2 ? tip : anchor;
                }
            }

            var mesh = new Mesh { name = source.name + MeshSuffix, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = Extend(vertices, from);
            mesh.normals = Extend(source.normals, from);
            mesh.tangents = Extend(source.tangents, from);
            mesh.colors = Extend(source.colors, from);

            var uv = new List<Vector4>();
            for (int channel = 0; channel < 8; channel++)
            {
                uv.Clear();
                source.GetUVs(channel, uv);
                if (uv.Count == 0 && channel > 0)
                {
                    continue;
                }
                while (uv.Count < n)
                {
                    uv.Add(Vector4.zero);
                }
                if (channel == 0)
                {
                    // z is the quad. Nothing else separates three quads whose
                    // vertices are otherwise the same numbers.
                    for (int q = 0; q < Quads; q++)
                    {
                        uv.Add(new Vector4(0f, 0f, q, 0f));
                        uv.Add(new Vector4(1f, 0f, q, 0f));
                        uv.Add(new Vector4(0f, 1f, q, 0f));
                        uv.Add(new Vector4(1f, 1f, q, 0f));
                    }
                }
                else
                {
                    for (int i = 0; i < from.Length; i++)
                    {
                        uv.Add(uv[from[i]]);
                    }
                }
                mesh.SetUVs(channel, uv);
            }

            // The full weight list, not the four-per-vertex view: a mesh
            // imported with more than four influences keeps them.
            var perVertex = source.GetBonesPerVertex();
            if (perVertex.Length == n)
            {
                var weights = source.GetAllBoneWeights();
                var starts = new int[n];
                int running = 0;
                for (int i = 0; i < n; i++)
                {
                    starts[i] = running;
                    running += perVertex[i];
                }
                int added = 0;
                for (int i = 0; i < from.Length; i++)
                {
                    added += perVertex[from[i]];
                }
                var perVertexOut = new NativeArray<byte>(n + from.Length, Allocator.Temp);
                var weightsOut = new NativeArray<BoneWeight1>(weights.Length + added, Allocator.Temp);
                try
                {
                    NativeArray<byte>.Copy(perVertex, perVertexOut, n);
                    NativeArray<BoneWeight1>.Copy(weights, weightsOut, weights.Length);
                    int at = weights.Length;
                    for (int i = 0; i < from.Length; i++)
                    {
                        int count = perVertex[from[i]];
                        perVertexOut[n + i] = (byte) count;
                        for (int k = 0; k < count; k++)
                        {
                            weightsOut[at++] = weights[starts[from[i]] + k];
                        }
                    }
                    mesh.SetBoneWeights(perVertexOut, weightsOut);
                }
                finally
                {
                    perVertexOut.Dispose();
                    weightsOut.Dispose();
                }
                mesh.bindposes = source.bindposes;
            }

            mesh.subMeshCount = source.subMeshCount + 1;
            for (int s = 0; s < source.subMeshCount; s++)
            {
                mesh.SetIndices(source.GetIndices(s), source.GetTopology(s), s);
            }
            // One submesh for all three, so the readout still costs the
            // renderer a single extra material slot.
            var triangles = new List<int>();
            for (int q = 0; q < Quads; q++)
            {
                int b = n + q * Corners;
                triangles.AddRange(new[] { b, b + 2, b + 1, b + 2, b + 3, b + 1 });
            }
            mesh.SetTriangles(triangles, source.subMeshCount);

            var dv = new Vector3[n];
            var dn = new Vector3[n];
            var dt = new Vector3[n];
            for (int s = 0; s < source.blendShapeCount; s++)
            {
                string name = source.GetBlendShapeName(s);
                int frames = source.GetBlendShapeFrameCount(s);
                for (int f = 0; f < frames; f++)
                {
                    source.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    mesh.AddBlendShapeFrame(name, source.GetBlendShapeFrameWeight(s, f),
                        Extend(dv, from), Extend(dn, from), Extend(dt, from));
                }
            }

            mesh.bounds = source.bounds;
            return mesh;
        }

        // An empty channel stays empty: Unity wants every channel either
        // absent or exactly the vertex count long.
        static T[] Extend<T>(T[] source, int[] from)
        {
            if (source == null || source.Length == 0)
            {
                return source;
            }
            var to = new T[source.Length + from.Length];
            Array.Copy(source, to, source.Length);
            for (int i = 0; i < from.Length; i++)
            {
                to[source.Length + i] = source[from[i]];
            }
            return to;
        }

        static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skin)
            {
                return skin.sharedMesh;
            }
            var filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            return filter != null ? filter.sharedMesh : null;
        }

        static void SetMesh(Renderer renderer, Mesh mesh)
        {
            if (renderer is SkinnedMeshRenderer skin)
            {
                skin.sharedMesh = mesh;
                return;
            }
            var filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            if (filter != null)
            {
                filter.sharedMesh = mesh;
            }
        }

        // Beside the bake, which both builders already keep per avatar.
        static string DirOf(Texture2D bake)
        {
            string path = bake != null ? AssetDatabase.GetAssetPath(bake) : null;
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            return Path.GetDirectoryName(path)?.Replace('\\', '/');
        }

        // EVERY _YAPS_ VALUE THE PLUG HAS, copied by name rather than by a
        // list kept here. A hand-written list rots the first time a property
        // is added to the patcher's block, and the cell it feeds then reads
        // zero without saying so. Anything the overlay shader cannot hold is
        // reported, so the drift is loud.
        //
        // The animated ones arrive through the renderer's block at runtime
        // regardless; this is for the values that only live on the material.
        static void Copy(Material from, Material to, string label, BridgeReport report)
        {
            if (from == null || to == null)
            {
                return;
            }
            var missing = new List<string>();
            int count = ShaderUtil.GetPropertyCount(from.shader);
            for (int i = 0; i < count; i++)
            {
                string name = ShaderUtil.GetPropertyName(from.shader, i);
                if (!name.StartsWith("_YAPS_", StringComparison.Ordinal))
                {
                    continue;
                }
                // The patcher's markers, naming the shader it started from.
                // Nothing a cell reads.
                if (name == YapsShaderGUI.OriginalEditorProperty || name == YapsShaderPatcher.SourceShaderProperty)
                {
                    continue;
                }
                var kind = ShaderUtil.GetPropertyType(from.shader, i);
                if (!to.HasProperty(name))
                {
                    if (kind == ShaderUtil.ShaderPropertyType.Float
                        || kind == ShaderUtil.ShaderPropertyType.Range
                        || kind == ShaderUtil.ShaderPropertyType.Vector)
                    {
                        missing.Add(name);
                    }
                    continue;
                }
                switch (kind)
                {
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        to.SetFloat(name, from.GetFloat(name));
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        to.SetVector(name, from.GetVector(name));
                        break;
                    case ShaderUtil.ShaderPropertyType.Color:
                        to.SetColor(name, from.GetColor(name));
                        break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        to.SetTexture(name, from.GetTexture(name));
                        break;
                }
            }
            EditorUtility.SetDirty(to);
            if (missing.Count == 0)
            {
                return;
            }
            report?.Warning(Category, label,
                $"The readout could not carry {missing.Count} of the plug's setting(s): " +
                string.Join(", ", missing) + ". Cells that depend on them read as nothing rather " +
                "than as their real value. The readout's shader needs the same property added.");
        }

        // One synced toggle for every readout on the avatar, off by default.
        // Taken out with the last readout. Safe to run again.
        public static string Menu(CVRAvatar avatar, AnimatorController controller)
        {
            if (avatar == null || controller == null)
            {
                return null;
            }
            // Every renderer drawing a readout, plugs' and sockets' alike.
            var targets = avatar.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.sharedMaterials.Any(IsReadout))
                .Select(r => (AnimationUtility.CalculateTransformPath(r.transform, avatar.transform), r))
                .ToList();
            bool had = RemoveLayer(controller);
            var settings = avatar.avatarSettings != null ? avatar.avatarSettings.settings : null;
            var ours = settings?.FirstOrDefault(e => e != null && e.machineName == Parameter);
            if (targets.Count == 0)
            {
                // The parameter too. It syncs whether the menu names it or
                // not, so it would keep its bit with nothing left to show.
                int param = Array.FindIndex(controller.parameters, p => p.name == Parameter);
                if (param >= 0 && !YapsRemover.ParameterUsed(controller, Parameter))
                {
                    controller.RemoveParameter(param);
                    had = true;
                }
                if (ours != null)
                {
                    Undo.RecordObject(avatar, "YAPS readout");
                    settings.Remove(ours);
                    EditorUtility.SetDirty(avatar);
                    had = true;
                }
                return had ? "readout toggle removed: nothing carries a readout" : null;
            }

            if (avatar.avatarSettings == null)
            {
                avatar.avatarSettings = new CVRAdvancedAvatarSettings
                {
                    settings = new List<CVRAdvancedSettingsEntry>(),
                    initialized = true,
                };
            }
            avatar.avatarUsesAdvancedSettings = true;
            Undo.RecordObject(avatar, "YAPS readout");
            if (ours == null)
            {
                ours = new CVRAdvancedSettingsEntry { machineName = Parameter };
                avatar.avatarSettings.settings.Add(ours);
            }
            ours.name = MenuLabel;
            ours.type = CVRAdvancedSettingsEntry.SettingsType.Toggle;
            // No clips of its own: the layer below plays them, as the tag
            // chooser's does.
            ours.toggleSettings = new CVRAdvancesAvatarSettingGameObjectToggle
            {
                defaultValue = false,
                usedType = CVRAdvancesAvatarSettingBase.ParameterType.Bool,
            };
            EditorUtility.SetDirty(avatar);

            if (!controller.parameters.Any(p => p.name == Parameter))
            {
                controller.AddParameter(Parameter, AnimatorControllerParameterType.Bool);
            }
            string dir = YapsNativeBuilder.OutputRoot + "/" + Safe(avatar.name);
            YapsNativeBuilder.EnsureFolderPublic(dir);
            var machine = new AnimatorStateMachine { name = LayerName, hideFlags = HideFlags.HideInHierarchy };
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(controller)))
            {
                AssetDatabase.AddObjectToAsset(machine, controller);
            }
            // Both states write the value: ChilloutVR restores nothing when a
            // state is left, so an Off that wrote nothing would stay on.
            for (int on = 0; on < 2; on++)
            {
                var state = machine.AddState(on == 1 ? "Shown" : "Hidden");
                state.writeDefaultValues = false;
                state.motion = YapsToggles.Clip(targets, Property, on,
                    dir + "/" + MenuLabel + (on == 1 ? " on" : " off") + ".anim");
                if (on == 0)
                {
                    machine.defaultState = state;
                }
                var to = machine.AddAnyStateTransition(state);
                to.hasExitTime = false;
                to.duration = 0f;
                to.canTransitionToSelf = false;
                to.AddCondition(on == 1 ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, Parameter);
            }
            var layers = controller.layers.ToList();
            layers.Add(new AnimatorControllerLayer { name = LayerName, defaultWeight = 1f, stateMachine = machine });
            controller.layers = layers.ToArray();
            EditorUtility.SetDirty(controller);
            return $"readout toggle \"{MenuLabel}\" for {targets.Count} readout(s), off by default, synced (1 bit)";
        }

        // A socket's readout, beside its atlas writer under the same parent,
        // so a rebuild or a remove of the socket takes it with the writer.
        // Positions all zero and the corners in UV0, like the writer: with
        // custom shaders off the stand-in shader draws nothing.
        public static GameObject AddSocketReadout(Transform parent)
        {
            var shader = Shader.Find(SocketShaderName);
            if (parent == null || shader == null)
            {
                return null;
            }
            var host = new GameObject(SocketReadoutName);
            host.transform.SetParent(parent, false);
            host.AddComponent<MeshFilter>().sharedMesh = SocketQuad();
            var renderer = host.AddComponent<MeshRenderer>();
            string path = SharedFolder + "/YAPS Socket Readout.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Directory.CreateDirectory(SharedFolder);
                material = new Material(shader) { name = "YAPS Socket Readout" };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            renderer.sharedMaterial = material;
            YapsAtlas.Quiet(renderer);
            return host;
        }

        static Mesh SocketQuad()
        {
            string path = SharedFolder + "/YAPS Socket Readout Quad.asset";
            var have = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (have != null)
            {
                return have;
            }
            var mesh = new Mesh { name = "YAPS Socket Readout Quad" };
            mesh.SetVertices(new List<Vector3> { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            mesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
            // Round the socket, not the strip: the vertex lights Unity hands a
            // renderer are chosen by its bounds, and these should be the
            // lights a socket there gets.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 0.5f);
            Directory.CreateDirectory(SharedFolder);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static bool RemoveLayer(AnimatorController controller)
        {
            bool removed = false;
            for (int i = controller.layers.Length - 1; i >= 0; i--)
            {
                if (controller.layers[i].name != LayerName)
                {
                    continue;
                }
                controller.RemoveLayer(i);
                removed = true;
            }
            return removed;
        }

        static Material MaterialFor(Shader shader, string dir, string label)
        {
            string path = dir + "/" + ObjectName + " " + Safe(label) + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = shader;
                return existing;
            }
            var made = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(made, path);
            return made;
        }

        static string Safe(string name)
        {
            foreach (char bad in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(bad, '_');
            }
            return name;
        }
    }
}
#endif
