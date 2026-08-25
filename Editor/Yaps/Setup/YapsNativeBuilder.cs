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

        // DPS's tracker: digit 9, intensity = length, at the base. Offset
        // the same way the socket ranges are, so a toy mod reading the
        // protocol in C# does not answer this plug either.
        public const float TrackerRange = 0.4930f;

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
            // Every slot the plug's vertices reach, not only the primary. An
            // explicit materialSlot is the author overriding that and wins.
            var alsoSlots = new List<int>();
            if (slot < 0 && renderer is SkinnedMeshRenderer skinned && plug.rootBone != null)
            {
                slot = SlotWeightedTo(skinned, plug.rootBone);
                alsoSlots = SlotsWeightedTo(skinned, plug.rootBone);
            }
            else if (slot < 0)
            {
                // A plain mesh bends whole, so every one of its materials
                // carries part of the plug.
                for (int i = 0; i < mats.Length; i++) alsoSlots.Add(i);
            }
            if (slot < 0) slot = 0;
            alsoSlots.Remove(slot);
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

            // A material THIS toolkit generated is never an original, even
            // when it looks like one. Remove's fallback puts the source
            // shader back on our clone and leaves it in the slot, so the next
            // bake sees no _YAPS_Bake and would clone it again — that is
            // where "Head _YAPS_ _YAPS_ 2" comes from, and every round buries
            // the real original one level deeper. Re-patch ours in place.
            bool oursAlready = IsGenerated(source);
            if (oursAlready && !source.HasProperty("_YAPS_Bake"))
            {
                source.shader = shader;
                EditorUtility.SetDirty(source);
            }
            bool fresh = !source.HasProperty("_YAPS_Bake") && !oursAlready;
            var patched = fresh
                ? YapsBaker.Apply(result, source, shader, dir, result.FromSkinnedMesh)
                : source;
            if (patched != source)
            {
                mats[slot] = patched;
                renderer.sharedMaterials = mats;
                // What the slot held, for Remove to put back.
                if (plug.bakedFrom == null) { plug.bakedFrom = original; EditorUtility.SetDirty(plug); }
                RecordBakedSlot(plug, slot, original);
            }
            else
            {
                // Re-bake: refresh everything the bake measured, and drop
                // the texture it replaces so a session of re-bakes does
                // not leave one twelve-megabyte asset per click behind.
                // The shader too, when this version emits something the
                // patch on it predates.
                if (YapsShaderPatcher.IsStale(patched)) YapsShaderPatcher.Refresh(patched, dir, report);
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

            // The rest of the slots the plug's vertices reach. They carry the
            // SAME deform: the bake is indexed by a mesh-global vertex id, so
            // one bake serves every submesh, and every _YAPS_ value is copied
            // from the primary rather than recomputed — a submesh bending on
            // its own curvature would tear against its neighbour just as
            // surely as one not bending at all.
            int mirrored = MirrorToSlots(plug, renderer, patched, alsoSlots, dir, report);
            if (mirrored > 0)
            {
                o.Notes.Add($"The plug's vertices span {mirrored + 1} of this mesh's materials, so the " +
                            "deform was baked into all of them. Baking only the primary one would leave " +
                            "the rest rigid and tear the mesh along the seam.");
            }

            // Announce: tip light for DPS, pointers for TPS and SPS.
            BuildMarkers(plug, result, o.Length, o.Radius);

            // The avatar's own animations that change the plug's size, shape
            // sliders and bone scale, now tell the material too.
            WireSize(plug, renderer, result, o);

            // A switch for the deform, unless the avatar already has one.
            var avatarForToggle = plug.GetComponentInParent<CVRAvatar>();
            if (avatarForToggle != null)
            {
                string toggled = YapsToggles.EnsurePlugToggle(plug, avatarForToggle, patched, YapsToggles.LabelFor(plug));
                if (toggled != null) o.Notes.Add(toggled);
            }

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

            // Somebody animating the component's own checkbox meant the
            // deform, so give them the deform.
            int switched = YapsCurveMirror.MirrorEnabled(clips,
                AnimationUtility.CalculateTransformPath(plug.transform, animator.transform), typeof(YapsPlug),
                rendererPath, renderer.GetType(), "_YAPS_Enabled");
            if (switched > 0)
            {
                o.Notes.Add($"{switched} clip(s) animate this component's own Enabled field, which does " +
                            "nothing in game: ChilloutVR strips the component. A matching curve on the " +
                            "material's _YAPS_Enabled was written beside each, so the animation now " +
                            "switches the deform the way it was meant to.");
            }

            int scaled = 0;
            var chainRoot = plug.rootBone;
            if (chainRoot != null)
            {
                // Each bone with its path: the mirror reads the scale it is
                // sitting at now, which is the pose the bake just measured.
                var bones = new Dictionary<string, Transform>();
                void Bone(Transform t)
                {
                    string p = AnimationUtility.CalculateTransformPath(t, animator.transform);
                    if (p != null) bones[p] = t;
                }
                Bone(chainRoot);
                for (int i = 0; i < chainRoot.childCount; i++) Bone(chainRoot.GetChild(i));
                scaled = YapsCurveMirror.MirrorBoneScale(clips, bones, rendererPath, renderer.GetType(), result.Rotation);
            }

            if (shapes + scaled + switched > 0)
            {
                AssetDatabase.SaveAssets();
            }
            if (shapes + scaled > 0)
            {
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

        // A baked plug's length in world metres. The material holds it in
        // the units the bake measured it in: metres already off a skinned
        // mesh, the object's own units off a plain one, which is why the
        // number on the material cannot be drawn in the scene as it stands.
        public static float WorldLength(Renderer renderer, Material m)
        {
            if (m == null || !m.HasProperty("_YAPS_Length")) return 0f;
            float length = m.GetFloat("_YAPS_Length");
            // A size animation stretches the shaft; the tip goes with it.
            if (m.HasProperty("_YAPS_BakeScale")) length *= Mathf.Max(m.GetFloat("_YAPS_BakeScale"), 0.01f);
            bool skinned = m.HasProperty("_YAPS_FrameFromVertex")
                ? m.GetFloat("_YAPS_FrameFromVertex") > 0.5f
                : renderer is SkinnedMeshRenderer s && s.bones != null && s.bones.Length > 0;
            if (!skinned && renderer != null) length *= Mathf.Abs(renderer.transform.lossyScale.z);
            return length;
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
                Add("Tip (for older sockets)", "TPS_Pen_Penetrating", new Vector3(0, 0, length));
                Add("Tip", "SPSLL_Pen_Penetrating", new Vector3(0, 0, length));
                Add("Root (for older sockets)", "TPS_Pen_Root", Vector3.zero);
                Add("Root", "SPSLL_Pen_Root", Vector3.zero);
                Add("Width", "TPS_Pen_Width", new Vector3(Mathf.Max(radius, 0.005f), 0, 0));
            }
            if (m.childCount == 0) { Object.DestroyImmediate(go); return null; }
            return go;
        }

        // The stage table on a socket material: sixteen starts and fades in
        // four float4s each. Missing entries read as the defaults.
        static readonly string[] StartProps = { "_YAPS_SocketShapeStart", "_YAPS_SocketShapeStart2", "_YAPS_SocketShapeStart3", "_YAPS_SocketShapeStart4" };
        static readonly string[] FadeProps = { "_YAPS_SocketShapeFade", "_YAPS_SocketShapeFade2", "_YAPS_SocketShapeFade3", "_YAPS_SocketShapeFade4" };

        public static void WriteStages(Material m, IList<(float start, float fade)> stages)
        {
            for (int pack = 0; pack < 4; pack++)
            {
                var starts = Vector4.zero;
                var fades = new Vector4(0.3f, 0.3f, 0.3f, 0.3f);
                for (int lane = 0; lane < 4; lane++)
                {
                    int i = pack * 4 + lane;
                    if (i < stages.Count)
                    {
                        starts[lane] = stages[i].start;
                        fades[lane] = Mathf.Max(0.01f, stages[i].fade);
                    }
                }
                m.SetVector(StartProps[pack], starts);
                m.SetVector(FadeProps[pack], fades);
            }
        }

        public static (float start, float fade) ReadStage(Material m, int i)
        {
            int pack = Mathf.Clamp(i / 4, 0, 3), lane = i & 3;
            var starts = m.HasProperty(StartProps[pack]) ? m.GetVector(StartProps[pack]) : Vector4.zero;
            var fades = m.HasProperty(FadeProps[pack]) ? m.GetVector(FadeProps[pack]) : new Vector4(0.3f, 0.3f, 0.3f, 0.3f);
            return (starts[lane], fades[lane]);
        }

        public static void WriteStage(Material m, int i, float start, float fade)
        {
            int pack = Mathf.Clamp(i / 4, 0, 3), lane = i & 3;
            var starts = m.HasProperty(StartProps[pack]) ? m.GetVector(StartProps[pack]) : Vector4.zero;
            var fades = m.HasProperty(FadeProps[pack]) ? m.GetVector(FadeProps[pack]) : new Vector4(0.3f, 0.3f, 0.3f, 0.3f);
            starts[lane] = start;
            fades[lane] = Mathf.Max(0.01f, fade);
            m.SetVector(StartProps[pack], starts);
            m.SetVector(FadeProps[pack], fades);
        }

        // --- the socket's shapes ------------------------------------------------
        //
        // Whether the mesh's own origin IS the socket, a dedicated socket
        // mesh as a rule. Depth no longer needs this, it measures from the
        // baked _YAPS_SocketOrigin; what still hangs on it is which
        // fallbacks a bake may take, since a body mesh must never have its
        // material swapped or another socket's bake replaced.
        public static bool MeshIsTheSocket(Renderer renderer, Transform socket)
        {
            if (renderer == null || socket == null) return false;
            return Vector3.Distance(renderer.transform.position, socket.position) < 0.03f;
        }

        // The route Build takes for this socket's shapes: the animator when
        // its reactions layer is already built or another socket holds the
        // mesh's one bake, the shader otherwise. The editors ask this too,
        // so what the inspector says is what Build does.
        public static bool ShapesByContact(YapsSocket socket)
        {
            if (socket == null || socket.renderer == null) return false;
            if (MeshIsTheSocket(socket.renderer, socket.transform)) return false;
            return YapsSocketReactions.Exists(socket) || AnotherSocketBaked(socket, socket.renderer);
        }

        // Does a plug of the wearer's own rest on top of this socket?
        //
        // This is the whole reason self-exclusion exists: an avatar
        // carrying both has its plug's tracker a hand's width from its
        // crotch socket, permanently within a plug length, and the socket
        // reads as always full. Measured here, in the rest pose, because
        // the shader can only guess at it — it decides ownership by whose
        // hip is nearest, which is right for a socket at the wearer's hip
        // and wrong for one out on a hand.
        //
        // The tracker light is the measurement, not the plug's transform:
        // it is the point the shader itself measures from, and it carries
        // the plug's length as its intensity.
        static bool OwnPlugRestsOn(YapsSocket socket)
        {
            var top = socket.transform.root;
            if (top == null) return false;
            foreach (var light in top.GetComponentsInChildren<Light>(true))
            {
                if (light == null || light.type != LightType.Point) continue;
                if (Mathf.Abs(light.range - TrackerRange) > 0.0005f) continue;
                float length = Mathf.Max(light.intensity, 0.01f);
                // Within its own length of the socket, plus a hand's width
                // of slack for a pose that is not quite the rest one.
                if (Vector3.Distance(light.transform.position, socket.transform.position) <= length + 0.1f)
                    return true;
            }
            return false;
        }

        // A different socket already baked into this renderer's material.
        // One material carries one bake and one origin, so the second
        // socket on a mesh takes the animator instead of replacing it.
        static bool AnotherSocketBaked(YapsSocket socket, Renderer renderer)
        {
            foreach (var s in socket.transform.root.GetComponentsInChildren<YapsSocket>(true))
            {
                if (s != socket && s.renderer == renderer && s.bakedFrom != null) return true;
            }
            return false;
        }

        // A socket's own baked material, told from a plug's: the socket bake
        // switches the deform off and writes a power, the plug bake does the
        // opposite. Both carry _YAPS_Bake, so the texture alone says nothing.
        static bool IsSocketMaterial(Material m)
        {
            return m != null
                   && m.HasProperty("_YAPS_SocketPower") && m.GetFloat("_YAPS_SocketPower") > 0f
                   && m.HasProperty("_YAPS_Enabled") && m.GetFloat("_YAPS_Enabled") <= 0f;
        }

        // Everything Build does for one socket: its markers, its shapes,
        // and a menu toggle when nothing switches it. What the window does
        // per socket, and what the inspector's own button does.
        public static List<string> BuildSocket(YapsSocket socket)
        {
            var lines = new List<string>();
            if (socket == null) return lines;
            Undo.RegisterFullObjectHierarchyUndo(socket.gameObject, "Build YAPS socket");
            string renamed = YapsToggles.RenameToLabel(socket, socket.GetComponentInParent<CVRAvatar>());
            if (renamed != null) lines.Add($"✓ {renamed}");
            YapsSocketBuilder.Build(socket);
            string capped = YapsSocketBuilder.LightCapNote(socket);
            if (capped != null) lines.Add($"✓ {YapsToggles.LabelFor(socket)}: {capped}");
            string shapes = BakeSocket(socket);
            if (shapes != null) lines.Add(shapes);
            var avatar = socket.GetComponentInParent<CVRAvatar>();
            int before = YapsToggles.Edits;
            string toggled = YapsToggles.EnsureObjectToggle(socket.gameObject, avatar, YapsToggles.LabelFor(socket));
            if (toggled != null) lines.Add(toggled);
            var animator = socket.GetComponentInParent<Animator>();
            var controller = (avatar != null && avatar.avatarSettings != null
                    ? BridgeContext.Underlying(avatar.avatarSettings.baseController) : null)
                ?? (animator != null ? BridgeContext.Underlying(animator.runtimeAnimatorController) : null);
            string menu = YapsToggles.RefreshMenuAnimator(avatar, before);
            if (menu != null) lines.Add(menu);
            // After the toggle layers: the lighthouse asserts the chosen
            // socket on, and a layer wins by coming later.
            string lighthouse = YapsLighthouse.Build(avatar, controller);
            if (lighthouse != null) lines.Add($"✓ {lighthouse}");
            return lines;
        }

        // A plug's bake, then the menu animator refreshed if its toggle
        // changed the entries. What the plug inspector's button does.
        public static Outcome BakeAndRefreshMenu(YapsPlug plug)
        {
            int before = YapsToggles.Edits;
            var o = Bake(plug);
            var avatar = plug != null ? plug.GetComponentInParent<CVRAvatar>() : null;
            string menu = YapsToggles.RefreshMenuAnimator(avatar, before);
            if (menu != null) o.Notes.Add(menu);
            // The contact channel, which the window's Build does after baking
            // and this door did not. The same plug came out differently
            // depending on which button was pressed, and the inspector's is
            // the one people reach for. The channel reads the frames the bake
            // just measured and replaces its own wiring rather than stacking,
            // so doing it per plug here is safe.
            if (avatar != null && o.Ok)
            {
                o.Notes.AddRange(YapsNativeChannel.Build(avatar));
            }
            return o;
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
            // A body mesh can open too, now that depth measures from the
            // baked socket origin rather than the mesh pivot. Two cases
            // still take the animator: a socket whose reactions layer is
            // already built, because bake and reactions drive the same
            // shapes and together apply them twice, and a second socket on
            // a mesh another one baked. Remove the built layer and build
            // again to move a socket across.
            bool own = MeshIsTheSocket(renderer, socket.transform);
            if (!own && YapsSocketReactions.Exists(socket))
                return YapsSocketReactions.Build(socket);
            if (!own && AnotherSocketBaked(socket, renderer))
            {
                string reacted = YapsSocketReactions.Build(socket);
                return (reacted ?? $"✓ {socket.name}") +
                       " (another socket baked this mesh, and a material holds one bake, so its " +
                       "shapes are driven by a depth contact instead)";
            }

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
            // A material carries ONE bake. If this mesh's material is a plug's,
            // baking the socket into it replaces the plug's vertex data with the
            // socket's and the plug stops deforming, which is what happens when
            // a ring is placed on the mesh the plug is part of. Drive the shapes
            // by contact instead: same reactions, nobody's bake overwritten.
            if (source.HasProperty("_YAPS_Bake") && !IsSocketMaterial(source))
            {
                string byContact = YapsSocketReactions.Build(socket);
                return (byContact ?? $"✓ {socket.name}") +
                       " (its mesh is a plug's, and a material holds one bake, so its shapes " +
                       "are driven by a depth contact rather than the socket shader)";
            }

            Material material;
            if (source.HasProperty("_YAPS_Bake"))
            {
                // Ours to refresh: a socket's own material, baked before.
                material = source;
                // Refresh the SHADER too when the tool has moved on since
                // this was patched. A material keeps its values across a
                // shader swap and a property the old code never had comes
                // in at its declared default, which the writes below then
                // set. Without this a rebuild refreshes the bake and leaves
                // the deform on whatever code shipped the day it was made.
                if (YapsShaderPatcher.IsStale(material)) YapsShaderPatcher.Refresh(material, dir, report);
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
                if (shader == null && !own)
                {
                    // Swapping a BODY to SimpleLit would repaint the whole
                    // avatar to rescue one socket. The animator route costs
                    // sync bits but changes nothing anyone can see.
                    string reacted = YapsSocketReactions.Build(socket);
                    return (reacted ?? $"✓ {socket.name}") +
                           $" (its mesh's shader could not be patched: {refusal})";
                }
                if (shader == null)
                {
                    var plain = OnSimpleLit(source, out string why);
                    shader = plain != null ? YapsShaderPatcher.Patch(plain, dir, report, out refusal, out _) : null;
                    if (shader == null) return $"✗ {socket.name}: could not patch the shader: {refusal}";
                    source = plain;
                }
                material = YapsBaker.Apply(result, source, shader, dir, result.FromSkinnedMesh);
                material.SetFloat("_YAPS_Enabled", 0f);
                if (socket.bakedFrom == null) { socket.bakedFrom = mats[0]; EditorUtility.SetDirty(socket); }
                mats[0] = material;
                renderer.sharedMaterials = mats;
            }

            WriteStages(material, stages.Select(s => (s.startsAt, s.fadeOver)).ToList());
            material.SetFloat("_YAPS_SocketPower", socket.shapePower);
            material.SetFloat("_YAPS_SocketDepth", -1f);
            // Where the socket sits in the mesh's own space. Depth measures
            // from here; ownership stays on the mesh origin. Near zero for a
            // dedicated socket mesh, the socket's real seat on a body.
            material.SetVector("_YAPS_SocketOrigin",
                renderer.transform.InverseTransformPoint(socket.transform.position));
            material.SetFloat("_YAPS_SocketNoSelfExclude", OwnPlugRestsOn(socket) ? 0f : 1f);
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
                comp.shapes.Clear();
                for (int i = 0; i < bakedShapes.Count && i < YapsBaker.MaxShapes; i++)
                {
                    var (start, fade) = ReadStage(material, i);
                    comp.shapes.Add(new YapsSocket.ShapeStage
                    {
                        blendshape = bakedShapes[i], startsAt = start, fadeOver = fade,
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
        // Patch each extra slot and give it the primary's deform exactly.
        // Returns how many were mirrored.
        static int MirrorToSlots(YapsPlug plug, Renderer renderer, Material primary,
            List<int> slots, string dir, BridgeReport report)
        {
            if (slots == null || slots.Count == 0) return 0;
            var mats = renderer.sharedMaterials;
            int done = 0;
            foreach (int i in slots)
            {
                if (i < 0 || i >= mats.Length || mats[i] == null || mats[i] == primary) continue;
                var was = mats[i];
                Material target;
                if (IsGenerated(was) && !was.HasProperty("_YAPS_Bake"))
                {
                    // Ours, with its shader reverted by a previous Remove.
                    // Re-patch in place rather than cloning a clone.
                    var again = YapsShaderPatcher.Patch(was, dir, report, out _, out _);
                    if (again != null)
                    {
                        was.shader = again;
                        CopyYapsProperties(primary, was);
                        EditorUtility.SetDirty(was);
                        done++;
                    }
                    continue;
                }
                if (was.HasProperty("_YAPS_Bake"))
                {
                    // Ours already, from an earlier bake: refresh it, and do
                    // NOT record it as the slot's original. It is not one —
                    // recording it makes Remove put a patched material back
                    // and call that a restore.
                    target = was;
                    if (YapsShaderPatcher.IsStale(target)) YapsShaderPatcher.Refresh(target, dir, report);
                    CopyYapsProperties(primary, target);
                    EditorUtility.SetDirty(target);
                    done++;
                    continue;
                }
                {
                    var shader = YapsShaderPatcher.Patch(was, dir, report, out string refusal, out _);
                    if (shader == null)
                    {
                        // Leave a material we cannot patch alone and say so:
                        // silently skipping it is what produces a tear nobody
                        // can account for.
                        report?.Warning("YAPS", $"\"{was.name}\" keeps its own shader",
                            $"The plug's vertices reach this material, but its shader could not be " +
                            $"patched ({refusal}), so that part of the mesh will not bend with the rest.");
                        continue;
                    }
                    target = new Material(was) { name = was.name + " (YAPS)", shader = shader };
                    AssetDatabase.CreateAsset(target, AssetDatabase.GenerateUniqueAssetPath(
                        dir + "/" + Sanitise(was.name) + "_YAPS_.mat"));
                    mats[i] = target;
                }
                CopyYapsProperties(primary, target);
                EditorUtility.SetDirty(target);
                RecordBakedSlot(plug, i, was);
                done++;
            }
            if (done > 0) renderer.sharedMaterials = mats;
            return done;
        }

        // Every _YAPS_ value from one material onto another, so two submeshes
        // of one mesh cannot disagree about how they bend.
        static void CopyYapsProperties(Material from, Material to)
        {
            var shader = from.shader;
            for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
            {
                string name = ShaderUtil.GetPropertyName(shader, i);
                if (!name.StartsWith("_YAPS_", System.StringComparison.Ordinal)) continue;
                if (!to.HasProperty(name)) continue;
                switch (ShaderUtil.GetPropertyType(shader, i))
                {
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        to.SetFloat(name, from.GetFloat(name)); break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        to.SetVector(name, from.GetVector(name)); break;
                    case ShaderUtil.ShaderPropertyType.Color:
                        to.SetColor(name, from.GetColor(name)); break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        to.SetTexture(name, from.GetTexture(name)); break;
                }
            }
        }

        // Did this toolkit make this material? Everything it makes lives
        // under OutputRoot, which survives a shader revert where a name or a
        // property does not.
        static bool IsGenerated(Material m)
        {
            if (m == null) return false;
            string path = AssetDatabase.GetAssetPath(m);
            return !string.IsNullOrEmpty(path)
                   && path.StartsWith(OutputRoot + "/", System.StringComparison.Ordinal);
        }

        // What a slot held before the bake, so Remove can put each one back.
        static void RecordBakedSlot(YapsPlug plug, int slot, Material was)
        {
            if (plug == null || was == null) return;
            var found = plug.bakedSlots.FirstOrDefault(b => b != null && b.slot == slot);
            if (found != null) return;                  // the first bake owns the record
            plug.bakedSlots.Add(new YapsPlug.BakedSlot { slot = slot, was = was });
            EditorUtility.SetDirty(plug);
        }

        // Every submesh the chain moves, not just the one it moves most.
        //
        // A plug's vertices can span several materials — a whole avatar baked
        // as one plug is the clear case — and a submesh left unpatched stays
        // rigid while its neighbours bend, so the mesh tears along the seam.
        // Any real weight counts: one moving vertex in a submesh is enough to
        // tear it, and an extra patched material is the cheaper mistake.
        public static List<int> SlotsWeightedTo(SkinnedMeshRenderer skin, Transform rootBone)
        {
            var slots = new List<int>();
            if (skin == null || skin.sharedMesh == null || skin.bones == null || rootBone == null) return slots;
            var mesh = skin.sharedMesh;
            var weights = mesh.boneWeights;
            if (weights == null || weights.Length == 0) return slots;
            var chain = new HashSet<int>();
            for (int i = 0; i < skin.bones.Length; i++)
                if (skin.bones[i] != null && (skin.bones[i] == rootBone || skin.bones[i].IsChildOf(rootBone))) chain.Add(i);
            if (chain.Count == 0) return slots;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                float total = 0f;
                var seen = new HashSet<int>();
                foreach (int v in mesh.GetTriangles(sub))
                {
                    if (v >= weights.Length || !seen.Add(v)) continue;
                    var w = weights[v];
                    if (chain.Contains(w.boneIndex0)) total += w.weight0;
                    if (chain.Contains(w.boneIndex1)) total += w.weight1;
                    if (chain.Contains(w.boneIndex2)) total += w.weight2;
                    if (chain.Contains(w.boneIndex3)) total += w.weight3;
                    if (total > 0.001f) break;
                }
                if (total > 0.001f) slots.Add(sub);
            }
            return slots;
        }

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
        public static GameObject BuildTestPlug(Transform parent = null, bool select = true, float length = 0.25f)
        {
            length = Mathf.Clamp(length, 0.08f, 1.5f);
            float radius = Mathf.Clamp(0.028f * length / 0.25f, 0.015f, 0.07f);
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
            // Wound so the OUTSIDE faces out. Unity's front face is
            // Cross(B - A, C - A); the first version of this had it the
            // other way, so backface culling threw away the surface you
            // were looking at and left the far inside showing through.
            for (int ring = 0; ring < along; ring++)
                for (int a = 0; a < around; a++)
                {
                    int nx = (a + 1) % around, here = ring * around, up = (ring + 1) * around;
                    tri.Add(here + a); tri.Add(up + nx); tri.Add(up + a);
                    tri.Add(here + a); tri.Add(here + nx); tri.Add(up + nx);
                }
            // A cap over the base, its own vertices so the edge stays hard.
            // The tip needs none: the last ring has radius 0. Without this
            // the mesh is a tube open at one end, and an open end reads as
            // a hole in the model from any angle that can see into it.
            int cap = v.Count;
            v.Add(Vector3.zero); n.Add(Vector3.back);
            for (int a = 0; a < around; a++)
            {
                float ang = a / (float) around * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * radius);
                n.Add(Vector3.back);
            }
            for (int a = 0; a < around; a++)
            {
                int nx = (a + 1) % around;
                tri.Add(cap); tri.Add(cap + 1 + nx); tri.Add(cap + 1 + a);
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
