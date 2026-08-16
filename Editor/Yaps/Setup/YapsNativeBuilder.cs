// Turns a YapsPlug into a plug: measure, bake, patch, write knobs,
// announce. Static meshes and markers today.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsNativeBuilder
    {
        public const string OutputRoot = "Assets/YAPS/Generated";
        const string MarkersName = "YAPS Markers";

        // DPS's tracker: range 0.49, intensity = length, at the base.
        public const float TrackerRange = 0.49f;

        public class Outcome
        {
            public bool Ok;
            public string Message;
            public List<string> Notes = new List<string>();
            public Material Material;
            public float Length, Radius;
        }

        // --- make this a plug ------------------------------------------------

        public static Outcome Bake(YapsPlug plug)
        {
            var o = new Outcome();
            if (plug == null) { o.Message = "no plug"; return o; }
            var renderer = plug.Target;
            if (renderer == null) { o.Message = "the plug has no renderer"; return o; }

            string dir = OutputRoot + "/" + Sanitise(TopName(plug.transform));
            EnsureFolder(dir);

            var report = new BridgeReport();
            // The named root bone is the chain, else the plug object.
            var chainRoot = plug.rootBone != null ? plug.rootBone : plug.transform;
            var result = YapsBaker.Bake(renderer, chainRoot, dir, report, out string failure, flipAxis: plug.flipAxis);
            if (result == null) { o.Message = "could not bake: " + failure; return o; }
            // A plain mesh bakes in its own units; the markers and the
            // report want metres.
            float toMetres = result.FromSkinnedMesh ? 1f : Mathf.Abs(renderer.transform.lossyScale.z);
            o.Length = plug.lengthOverride > 0 ? plug.lengthOverride : result.Length * toMetres;
            o.Radius = result.Radius * toMetres;

            // The named slot; else, on a skinned mesh, the slot the chain
            // moves; else the first.
            var mats = renderer.sharedMaterials;
            if (mats == null || mats.Length == 0) { o.Message = "the renderer has no materials"; return o; }
            int slot = plug.materialSlot >= 0 && plug.materialSlot < mats.Length ? plug.materialSlot : -1;
            if (slot < 0 && renderer is SkinnedMeshRenderer skinned && plug.rootBone != null)
            {
                slot = SlotWeightedTo(skinned, plug.rootBone);
            }
            if (slot < 0) slot = 0;
            var source = mats[slot];
            if (source == null) { o.Message = $"material slot {slot} is empty"; return o; }

            // What the material already is. A legacy plug is upgraded in
            // place: TPS and SPS keep their shader with the old deform
            // switched off, DPS moves to Simple Lit because its deform has
            // no switch. The author's values are carried either way.
            var legacy = source.HasProperty("_YAPS_Bake") ? YapsLegacyMap.Origin.None
                : YapsLegacyMap.Detect(source, out _);
            var original = source;

            // Patch its own shader; keep one already patched. When it refuses,
            // fall back to Simple Lit.
            Shader shader;
            string refusal = null;
            if (source.HasProperty("_YAPS_Bake"))
            {
                shader = source.shader;
            }
            else
            {
                shader = legacy == YapsLegacyMap.Origin.DPS ? null
                    : YapsShaderPatcher.Patch(source, dir, report, out refusal, out _, allowSps: legacy == YapsLegacyMap.Origin.SPS);
                if (shader == null)
                {
                    var plain = OnSimpleLit(source, out string why);
                    if (plain == null) { o.Message = "could not patch the shader: " + refusal + "; and " + why; return o; }
                    shader = YapsShaderPatcher.Patch(plain, dir, report, out string plainRefusal, out _);
                    if (shader == null) { o.Message = "could not patch the shader: " + refusal + "; and YAPS Simple Lit refused too: " + plainRefusal; return o; }
                    o.Notes.Add(legacy == YapsLegacyMap.Origin.DPS
                        ? "A DPS shader has no switch for its own deform, so the plug now wears YAPS Simple Lit " +
                          "with its colour, albedo, normal map, metallic and smoothness carried over. Its original " +
                          "material is untouched."
                        : $"\"{source.shader.name}\" could not be patched ({refusal}), so the plug now wears " +
                          "YAPS Simple Lit with its colour, albedo, normal map, metallic and smoothness carried " +
                          "over. Its original material is untouched. Put a shader with source on it (Poiyomi, " +
                          "for one) and re-bake if you need more than that.");
                    source = plain;
                }
            }
            if (shader == null) { o.Message = "could not patch the shader: " + refusal; return o; }

            bool fresh = !source.HasProperty("_YAPS_Bake");
            var patched = fresh
                ? YapsBaker.Apply(result, source, shader, dir, result.FromSkinnedMesh)
                : source;
            if (patched != source)
            {
                mats[slot] = patched;
                renderer.sharedMaterials = mats;
            }
            else
            {
                // Re-bake: refresh everything the bake measured, and drop
                // the texture it replaces so a session of re-bakes does
                // not leave one twelve-megabyte asset per click behind.
                var previous = patched.GetTexture("_YAPS_Bake");
                patched.SetTexture("_YAPS_Bake", result.Bake);
                patched.SetFloat("_YAPS_VertexCount", result.VertexCount);
                patched.SetFloat("_YAPS_ShapeCount", result.Shapes.Count);
                patched.SetFloat("_YAPS_Length", result.Length);
                patched.SetFloat("_YAPS_FrameFromVertex", result.FromSkinnedMesh ? 1f : 0f);
                string old = previous != null && previous != result.Bake
                    ? AssetDatabase.GetAssetPath(previous) : null;
                if (!string.IsNullOrEmpty(old) && old.StartsWith(OutputRoot + "/", System.StringComparison.Ordinal))
                {
                    AssetDatabase.DeleteAsset(old);
                }
            }
            if (plug.lengthOverride > 0) patched.SetFloat("_YAPS_Length", plug.lengthOverride);
            patched.SetFloat("_YAPS_Enabled", 1f);
            patched.SetFloat("_YAPS_SelfTag", -1f);   // native: self-exclusion is by body, no tag

            // A fresh bake of a legacy plug: carry the author's values onto
            // the YAPS knobs, switch the old deform off, and let the
            // component take the carried values as its own.
            if (fresh && legacy != YapsLegacyMap.Origin.None && legacy != YapsLegacyMap.Origin.YAPS)
            {
                var unmapped = new List<string>();
                var carried = YapsLegacyMap.Carry(original, patched, unmapped, o.Length, o.Radius);
                SwitchOffLegacyDeform(patched, legacy);
                ReadKnobs(plug, patched);
                EditorUtility.SetDirty(plug);
                o.Notes.Add($"Upgraded from {legacy}: {carried.Count} setting(s) carried" +
                            (unmapped.Count > 0 ? $"; no YAPS counterpart for {string.Join(", ", unmapped)}" : "") + ".");
            }
            WriteKnobs(plug, patched);
            EditorUtility.SetDirty(patched);
            o.Material = patched;

            // Announce: tip light for DPS, pointers for TPS and SPS.
            BuildMarkers(plug, result, o.Length, o.Radius);

            // The avatar's own animations that change the plug's size, shape
            // sliders and bone scale, now tell the material too.
            WireSize(plug, renderer, result, o);

            o.Ok = true;
            o.Message = $"Baked \"{renderer.name}\": {o.Length:0.###} m, {result.VertexCount} vertices, " +
                        $"{result.Shapes.Count} shape(s), material \"{patched.name}\".";
            o.Notes.Add(YapsPropBuilder.IsProp(TopOf(plug.transform).gameObject)
                ? "This plug is on a prop; make it a prop again to rebuild its contact channel for the new bake."
                : "On an avatar this plug reads sockets by their marker lights only; on a prop, Make this a prop adds the synced contact channel.");
            if (result.FromSkinnedMesh) o.Notes.Add("Skinned mesh: frame recovered per vertex.");
            return o;
        }

        // Mirrors the avatar's own size animations onto the plug's material:
        // shape curves onto the shape weights, the root bone's scale onto
        // the bake scale. Edits the user's clips, adding a curve beside each
        // it mirrors, and says so. Idempotent: the same curve every time.
        static void WireSize(YapsPlug plug, Renderer renderer, YapsBaker.Result result, Outcome o)
        {
            var top = TopOf(plug.transform);
            var animator = top.GetComponentInParent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null) return;
            var clips = YapsCurveMirror.ClipsOf(animator.runtimeAnimatorController)
                .Where(YapsCurveMirror.UserOwned).ToList();
            if (clips.Count == 0) return;
            string rendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, animator.transform);

            var missed = new HashSet<string>();
            int shapes = result.Shapes.Count > 0
                ? YapsCurveMirror.MirrorShapes(clips, rendererPath, renderer.GetType(), result.Shapes,
                    result.MovingShapes, missed)
                : 0;

            int scaled = 0;
            var chainRoot = plug.rootBone;
            if (chainRoot != null)
            {
                var bones = new List<string> { AnimationUtility.CalculateTransformPath(chainRoot, animator.transform) };
                for (int i = 0; i < chainRoot.childCount; i++)
                    bones.Add(AnimationUtility.CalculateTransformPath(chainRoot.GetChild(i), animator.transform));
                scaled = YapsCurveMirror.MirrorBoneScale(clips, bones, rendererPath, renderer.GetType());
            }

            if (shapes + scaled > 0)
            {
                AssetDatabase.SaveAssets();
                o.Notes.Add($"Wired the plug's size into {shapes + scaled} of the avatar's own clip(s)" +
                            (shapes > 0 ? $": {shapes} shape curve(s)" : "") +
                            (scaled > 0 ? $"{(shapes > 0 ? "," : ":")} {scaled} bone scale curve(s)" : "") +
                            ". A curve was added beside each, so a size slider or hyper toggle reaches the shader too.");
            }
            if (missed.Count > 0)
            {
                o.Notes.Add($"{missed.Count} animated shape(s) that move the plug are not in the bake " +
                            $"({string.Join(", ", missed.Take(6))}); the bake holds the {YapsBaker.MaxShapes} that move it most.");
            }
        }

        public const string SimpleLitName = "YAPS/Simple Lit";

        // The old system's deform must not run beside YAPS. TPS and SPS have
        // a switch; both are turned off, and any keyword carrying the
        // system's name goes with it.
        public static void SwitchOffLegacyDeform(Material m, YapsLegacyMap.Origin legacy)
        {
            string flag = legacy == YapsLegacyMap.Origin.TPS ? "_TPS_PenetratorEnabled"
                        : legacy == YapsLegacyMap.Origin.SPS ? "_SPS_Enabled" : null;
            if (flag != null && m.HasProperty(flag)) m.SetFloat(flag, 0f);
            string tag = legacy.ToString();
            foreach (var keyword in m.shaderKeywords)
            {
                if (keyword.IndexOf(tag, System.StringComparison.OrdinalIgnoreCase) >= 0) m.DisableKeyword(keyword);
            }
        }

        // The same material on Simple Lit, in memory. Property names match Standard's.
        public static Material OnSimpleLit(Material source, out string why)
        {
            why = null;
            var shader = Shader.Find(SimpleLitName);
            if (shader == null)
            {
                why = "YAPS Simple Lit is not in the project (Editor/Yaps/YapsSimpleLit.shader)";
                return null;
            }
            var m = new Material(source) { name = source.name };
            m.shader = shader;
            return m;
        }

        // Every YapsPlug wearing this material takes the values back.
        public static void SyncPlugsFrom(Material m)
        {
            if (m == null) return;
            foreach (var plug in Object.FindObjectsOfType<YapsPlug>())
            {
                var r = plug.Target;
                if (r == null || !r.sharedMaterials.Contains(m)) continue;
                // Only a real change dirties the scene.
                string before = JsonUtility.ToJson(plug);
                Undo.RecordObject(plug, "YAPS plug knobs");
                ReadKnobs(plug, m);
                if (JsonUtility.ToJson(plug) != before) EditorUtility.SetDirty(plug);
            }
        }

        public static void WriteKnobs(YapsPlug p, Material m)
        {
            m.SetFloat("_YAPS_Overrun", p.overrun ? 1f : 0f);
            m.SetFloat("_YAPS_TaperStart", p.taperStart);
            m.SetFloat("_YAPS_TaperEnd", p.taperEnd);
            m.SetFloat("_YAPS_Curvature", p.curvature);
            m.SetFloat("_YAPS_ReCurvature", p.recurvature);
            m.SetFloat("_YAPS_EntranceStiffness", p.entranceStiffness);
            m.SetFloat("_YAPS_Squeeze", p.squeeze);
            m.SetFloat("_YAPS_SqueezeDistance", p.squeezeReach);
            m.SetFloat("_YAPS_Bulge", p.bulge);
            m.SetFloat("_YAPS_BulgeDistance", p.bulgeReach);
            m.SetFloat("_YAPS_IdleLength", p.idleLength);
            m.SetFloat("_YAPS_IdleWidth", p.idleWidth);
            m.SetFloat("_YAPS_WriggleStrength", p.wriggle);
            m.SetFloat("_YAPS_WriggleSpeed", p.wriggleSpeed);
            m.SetFloat("_YAPS_PumpStrength", p.pumping);
            m.SetFloat("_YAPS_PumpSpeed", p.pumpingSpeed);
            m.SetFloat("_YAPS_PumpWidth", p.pumpingWidth);
            m.SetFloat("_YAPS_BezierSmoothness", p.bezierSmoothness);
            m.SetFloat("_YAPS_BezierStart", p.straightBeforeBend);
            m.SetFloat("_YAPS_SmoothStart", p.easeIntoBend);
            m.SetFloat("_YAPS_MinimumSocketDistance", p.minimumSocketDistance);
        }

        static void BuildMarkers(YapsPlug plug, YapsBaker.Result result, float length, float radius)
        {
            // A skinned mesh's markers sit on the measured frame.
            bool skinned = result != null && result.FromSkinnedMesh;
            AnnouncePlug(plug.transform, skinned ? result.Origin : (Vector3?) null, skinned ? result.Rotation : (Quaternion?) null,
                length, radius, plug.emitTipLight, plug.emitPointers);
        }

        // Announces a plug to every socket family: tracker light at the base,
        // tip, root and width pointers. Null frame means the parent's transform.
        public static GameObject AnnouncePlug(Transform parent, Vector3? worldOrigin, Quaternion? worldRotation,
            float length, float radius, bool tipLight = true, bool pointers = true)
        {
            var old = parent.Find(MarkersName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject(MarkersName);
            go.transform.SetParent(parent, false);
            if (worldOrigin.HasValue && worldRotation.HasValue
                && (worldRotation.Value.x != 0f || worldRotation.Value.y != 0f
                    || worldRotation.Value.z != 0f || worldRotation.Value.w != 0f))
            {
                go.transform.SetPositionAndRotation(worldOrigin.Value, worldRotation.Value);
            }
            var m = go.transform;

            if (tipLight)
            {
                // Raliv reads intensity as the length and measures from the base.
                var l = YapsSocketBuilder.MarkerLight(m, "DPS Tracker", TrackerRange, Vector3.zero);
                l.intensity = Mathf.Max(length, 0.01f);
            }
            if (pointers)
            {
                // Tip and root as separate points. Only names not already announced;
                // a second TPS_Pen_Penetrating would have a trigger reporting either.
                var have = new HashSet<string>(parent.GetComponentsInChildren<CVRPointer>(true)
                    .Where(p => p != null && p.transform != m && !p.transform.IsChildOf(m))
                    .Select(p => p.type));
                void Add(string name, string type, Vector3 at)
                {
                    if (!have.Contains(type)) YapsSocketBuilder.Pointer(m, name, type, at);
                }
                Add("Tip", "TPS_Pen_Penetrating", new Vector3(0, 0, length));
                Add("Tip (SPS)", "SPSLL_Pen_Penetrating", new Vector3(0, 0, length));
                Add("Root", "TPS_Pen_Root", Vector3.zero);
                Add("Root (SPS)", "SPSLL_Pen_Root", Vector3.zero);
                Add("Width", "TPS_Pen_Width", new Vector3(Mathf.Max(radius, 0.005f), 0, 0));
            }
            if (m.childCount == 0) { Object.DestroyImmediate(go); return null; }
            return go;
        }

        // --- the socket's shapes ------------------------------------------------
        //
        // The socket shader measures depth from its mesh's own origin, so
        // only a mesh whose origin is the socket can open right. A body
        // mesh keeps whatever the animator does with its shapes.
        public static bool MeshIsTheSocket(Renderer renderer, Transform socket)
        {
            if (renderer == null || socket == null) return false;
            return Vector3.Distance(renderer.transform.position, socket.position) < 0.03f;
        }

        // Bakes the socket's chosen shapes into its mesh's material, staged
        // as the component says. Returns what happened, for the window.
        public static string BakeSocket(YapsSocket socket)
        {
            if (socket == null) return null;
            var renderer = socket.renderer;
            var stages = socket.shapes.Where(s => s != null && !string.IsNullOrEmpty(s.blendshape)).ToList();
            if (renderer == null || stages.Count == 0) return null;
            if (renderer.sharedMesh == null || renderer.sharedMesh.blendShapeCount == 0)
                return $"✗ {socket.name}: its mesh has no blendshapes";
            if (!MeshIsTheSocket(renderer, socket.transform))
                return $"✗ {socket.name}: the shapes need a mesh whose origin is this socket. " +
                       $"\"{renderer.name}\" sits {Vector3.Distance(renderer.transform.position, socket.transform.position):0.##} m away; " +
                       "a body mesh cannot open this way, only a mesh of the socket's own.";

            var mats = renderer.sharedMaterials;
            if (mats == null || mats.Length == 0 || mats[0] == null) return $"✗ {socket.name}: its mesh has no material";
            string dir = OutputRoot + "/" + Sanitise(TopName(socket.transform));
            EnsureFolder(dir);
            var report = new BridgeReport();
            var wanted = stages.Select(s => s.blendshape).ToList();
            var result = YapsBaker.Bake(renderer, socket.transform, dir, report, out string failure,
                wanted, objectFrame: true);
            if (result == null) return $"✗ {socket.name}: could not bake: {failure}";
            if (result.Shapes.Count == 0) return $"✗ {socket.name}: none of the named shapes exist on \"{renderer.name}\"";

            var source = mats[0];
            Material material;
            if (source.HasProperty("_YAPS_Bake"))
            {
                // Already YAPS, ours to refresh; a plug's material keeps its plug half.
                material = source;
                var previous = material.GetTexture("_YAPS_Bake");
                material.SetTexture("_YAPS_Bake", result.Bake);
                material.SetFloat("_YAPS_VertexCount", result.VertexCount);
                material.SetFloat("_YAPS_ShapeCount", result.Shapes.Count);
                string old = previous != null && previous != result.Bake ? AssetDatabase.GetAssetPath(previous) : null;
                if (!string.IsNullOrEmpty(old) && old.StartsWith(OutputRoot + "/", System.StringComparison.Ordinal))
                    AssetDatabase.DeleteAsset(old);
            }
            else
            {
                var shader = YapsShaderPatcher.Patch(source, dir, report, out string refusal, out _);
                if (shader == null)
                {
                    var plain = OnSimpleLit(source, out string why);
                    shader = plain != null ? YapsShaderPatcher.Patch(plain, dir, report, out refusal, out _) : null;
                    if (shader == null) return $"✗ {socket.name}: could not patch the shader: {refusal}";
                    source = plain;
                }
                material = YapsBaker.Apply(result, source, shader, dir, result.FromSkinnedMesh);
                material.SetFloat("_YAPS_Enabled", 0f);
                mats[0] = material;
                renderer.sharedMaterials = mats;
            }

            var starts = Vector4.zero;
            var fades = new Vector4(0.3f, 0.3f, 0.3f, 0.3f);
            for (int i = 0; i < stages.Count && i < 4; i++)
            {
                starts[i] = stages[i].startsAt;
                fades[i] = Mathf.Max(0.01f, stages[i].fadeOver);
            }
            material.SetVector("_YAPS_SocketShapeStart", starts);
            material.SetVector("_YAPS_SocketShapeFade", fades);
            material.SetFloat("_YAPS_SocketPower", socket.shapePower);
            material.SetFloat("_YAPS_SocketDepth", -1f);
            EditorUtility.SetDirty(material);
            return $"✓ {socket.name}: {result.Shapes.Count} shape(s) staged on \"{renderer.name}\"";
        }

        // --- adoption --------------------------------------------------------
        //
        // Puts the authoring component on a converted socket or plug, filled
        // from what was built. An existing component is left alone.

        public static YapsSocket AdoptSocket(Transform socketRoot, Renderer renderer, Material material,
            IList<string> bakedShapes)
        {
            if (socketRoot == null) return null;
            var comp = socketRoot.GetComponent<YapsSocket>();
            if (comp != null) return comp;
            comp = socketRoot.gameObject.AddComponent<YapsSocket>();

            // Kind from the root light's digit, or the SPS pointer name.
            bool hole = false;
            foreach (var l in socketRoot.GetComponentsInChildren<Light>(true))
            {
                if (!YapsScanner.IsProtocolLight(l)) continue;
                int d = YapsScanner.LightDigit(l);
                if (d == 1) { hole = true; break; }
                if (d == 2) { hole = false; break; }
            }
            foreach (var p in socketRoot.GetComponentsInChildren<CVRPointer>(true))
            {
                if (p != null && p.type != null && p.type.StartsWith("SPSLL_Socket_Hole")) { hole = true; break; }
            }
            comp.kind = hole ? YapsSocket.SocketKind.Hole : YapsSocket.SocketKind.Ring;
            comp.renderer = material != null ? renderer as SkinnedMeshRenderer : null;
            comp.emitLights = socketRoot.GetComponentsInChildren<Light>(true).Any(YapsScanner.IsProtocolLight);

            // Shape rows from the bake, staged as the material says.
            if (bakedShapes != null && material != null && material.HasProperty("_YAPS_SocketShapeStart"))
            {
                var starts = material.GetVector("_YAPS_SocketShapeStart");
                var fades = material.GetVector("_YAPS_SocketShapeFade");
                comp.shapes.Clear();
                for (int i = 0; i < bakedShapes.Count && i < 4; i++)
                {
                    comp.shapes.Add(new YapsSocket.ShapeStage
                    {
                        blendshape = bakedShapes[i], startsAt = starts[i], fadeOver = fades[i],
                    });
                }
                if (material.HasProperty("_YAPS_SocketPower")) comp.shapePower = material.GetFloat("_YAPS_SocketPower");
            }
            return comp;
        }

        public static YapsPlug AdoptPlug(Transform plugRoot, Renderer renderer, int slot, Material material,
            Transform rootBone, float lengthOverride = 0f)
        {
            if (plugRoot == null) return null;
            var comp = plugRoot.GetComponent<YapsPlug>();
            if (comp != null) return comp;
            comp = plugRoot.gameObject.AddComponent<YapsPlug>();
            comp.renderer = renderer;
            comp.materialSlot = slot;
            // Guessed only when the plug object is the renderer itself; a marker
            // object under a bone already names the chain by where it sits.
            comp.rootBone = rootBone != null ? rootBone
                : (renderer != null && plugRoot == renderer.transform ? GuessRootBone(renderer as SkinnedMeshRenderer, slot) : null);
            comp.lengthOverride = lengthOverride;
            if (material != null) ReadKnobs(comp, material);
            comp.emitTipLight = plugRoot.GetComponentsInChildren<Light>(true)
                .Any(l => YapsScanner.IsProtocolLight(l) && (YapsScanner.LightDigit(l) == 8 || YapsScanner.LightDigit(l) == 9));
            comp.emitPointers = plugRoot.GetComponentsInChildren<CVRPointer>(true)
                .Any(p => p != null && p.type != null && (p.type.StartsWith("TPS_Pen_") || p.type.StartsWith("SPSLL_Pen_")));
            return comp;
        }

        // The bone a skinned plug grows from, from the mesh: the bone that
        // carries the most weight in the plug's material slot, climbed to
        // the top of its weighted chain. Null when nothing decides it.
        public static Transform GuessRootBone(SkinnedMeshRenderer skin, int slot)
        {
            if (skin == null || skin.sharedMesh == null || skin.bones == null || slot < 0) return null;
            var mesh = skin.sharedMesh;
            if (slot >= mesh.subMeshCount) return null;
            var weights = mesh.boneWeights;
            if (weights == null || weights.Length == 0) return null;
            var total = new float[skin.bones.Length];
            var seen = new HashSet<int>();
            foreach (int v in mesh.GetTriangles(slot))
            {
                if (v >= weights.Length || !seen.Add(v)) continue;
                var w = weights[v];
                void Add(int b, float f) { if (b >= 0 && b < total.Length) total[b] += f; }
                Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1);
                Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
            }
            int best = -1;
            for (int i = 0; i < total.Length; i++) if (total[i] > 0f && (best < 0 || total[i] > total[best])) best = i;
            if (best < 0 || skin.bones[best] == null) return null;
            // Climb while the parent is also a weighted bone of this slot.
            var bone = skin.bones[best];
            for (var up = bone.parent; up != null; up = up.parent)
            {
                int i = System.Array.IndexOf(skin.bones, up);
                if (i < 0 || total[i] <= 0f) break;
                bone = up;
            }
            return bone;
        }

        // The material slot whose triangles are weighted to this bone and
        // its children the most. -1 when none is.
        public static int SlotWeightedTo(SkinnedMeshRenderer skin, Transform rootBone)
        {
            if (skin == null || skin.sharedMesh == null || skin.bones == null || rootBone == null) return -1;
            var mesh = skin.sharedMesh;
            var weights = mesh.boneWeights;
            if (weights == null || weights.Length == 0) return -1;
            var chain = new HashSet<int>();
            for (int i = 0; i < skin.bones.Length; i++)
                if (skin.bones[i] != null && (skin.bones[i] == rootBone || skin.bones[i].IsChildOf(rootBone))) chain.Add(i);
            if (chain.Count == 0) return -1;
            int best = -1; float bestWeight = 0f;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                float total = 0f;
                var seen = new HashSet<int>();
                foreach (int v in mesh.GetTriangles(s))
                {
                    if (v >= weights.Length || !seen.Add(v)) continue;
                    var w = weights[v];
                    if (chain.Contains(w.boneIndex0)) total += w.weight0;
                    if (chain.Contains(w.boneIndex1)) total += w.weight1;
                    if (chain.Contains(w.boneIndex2)) total += w.weight2;
                    if (chain.Contains(w.boneIndex3)) total += w.weight3;
                }
                if (total > bestWeight) { bestWeight = total; best = s; }
            }
            return best;
        }

        // The mirror of WriteKnobs.
        public static void ReadKnobs(YapsPlug p, Material m)
        {
            float F(string n, float d) => m.HasProperty(n) ? m.GetFloat(n) : d;
            p.overrun = F("_YAPS_Overrun", 1f) > 0.5f;
            p.taperStart = F("_YAPS_TaperStart", 0.10f);
            p.taperEnd = F("_YAPS_TaperEnd", 0.30f);
            p.curvature = F("_YAPS_Curvature", 0f);
            p.recurvature = F("_YAPS_ReCurvature", 0f);
            p.entranceStiffness = F("_YAPS_EntranceStiffness", 0f);
            p.squeeze = F("_YAPS_Squeeze", 0f);
            p.squeezeReach = F("_YAPS_SqueezeDistance", 0.15f);
            p.bulge = F("_YAPS_Bulge", 0f);
            p.bulgeReach = F("_YAPS_BulgeDistance", 0.2f);
            p.idleLength = F("_YAPS_IdleLength", 1f);
            p.idleWidth = F("_YAPS_IdleWidth", 1f);
            p.wriggle = F("_YAPS_WriggleStrength", 0f);
            p.wriggleSpeed = F("_YAPS_WriggleSpeed", 2f);
            p.pumping = F("_YAPS_PumpStrength", 0f);
            p.pumpingSpeed = F("_YAPS_PumpSpeed", 6f);
            p.pumpingWidth = F("_YAPS_PumpWidth", 1f);
            p.bezierSmoothness = F("_YAPS_BezierSmoothness", 1f);
            p.straightBeforeBend = F("_YAPS_BezierStart", 0f);
            p.easeIntoBend = F("_YAPS_SmoothStart", 0f);
            p.minimumSocketDistance = F("_YAPS_MinimumSocketDistance", 0f);
        }

        // --- the test plug ---------------------------------------------------

        // A capsule with a YapsPlug, on Simple Lit, baked through the normal path.
        public static GameObject BuildTestPlug(Transform parent = null, bool select = true)
        {
            const float length = 0.25f, radius = 0.028f;
            var root = new GameObject("YAPS Test Plug");
            if (parent != null) root.transform.SetParent(parent, false);
            else
            {
                var cam = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
                if (cam != null) root.transform.position = cam.transform.position + cam.transform.forward * 0.6f;
            }
            var mf = root.AddComponent<MeshFilter>();
            var mr = root.AddComponent<MeshRenderer>();
            EnsureFolder(OutputRoot + "/Test Plug");
            var mesh = CapsuleMesh(length, radius);
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(OutputRoot + "/Test Plug/YAPS Test Plug Mesh.asset"));
            mf.sharedMesh = mesh;
            var shader = Shader.Find(SimpleLitName) ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "YAPS Test Plug", color = new Color(0.85f, 0.55f, 0.65f) };
            mr.sharedMaterial = mat;

            var plug = root.AddComponent<YapsPlug>();
            plug.renderer = mr;
            var o = Bake(plug);
            Debug.Log("[YAPS] " + o.Message + (o.Notes.Count > 0 ? "\n  " + string.Join("\n  ", o.Notes) : ""));
            if (!o.Ok) Debug.LogError("[YAPS] test plug failed to bake: " + o.Message);
            Undo.RegisterCreatedObjectUndo(root, "YAPS test plug");
            if (select) Selection.activeGameObject = root;
            return root;
        }

        static Mesh CapsuleMesh(float length, float radius)
        {
            const int around = 20, along = 28;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            for (int ring = 0; ring <= along; ring++)
            {
                float t = ring / (float) along;
                float z = t * length;
                float r = t < 0.8f ? radius : radius * Mathf.Cos((t - 0.8f) / 0.2f * Mathf.PI * 0.5f);
                for (int a = 0; a < around; a++)
                {
                    float ang = a / (float) around * Mathf.PI * 2f;
                    var off = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
                    v.Add(off * r + new Vector3(0, 0, z)); n.Add(off);
                }
            }
            for (int ring = 0; ring < along; ring++)
                for (int a = 0; a < around; a++)
                {
                    int nx = (a + 1) % around, here = ring * around, up = (ring + 1) * around;
                    tri.Add(here + a); tri.Add(up + a); tri.Add(up + nx);
                    tri.Add(here + a); tri.Add(up + nx); tri.Add(here + nx);
                }
            var mesh = new Mesh { name = "YAPS Test Plug Mesh" };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(tri, 0);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }

        // --- helpers ------------------------------------------------------------

        static Transform TopOf(Transform t)
        {
            var top = t; while (top.parent != null) top = top.parent;
            return top;
        }

        static string TopName(Transform t) => TopOf(t).name;

        static string Sanitise(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }

        public static void EnsureFolderPublic(string path) => EnsureFolder(path);

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
