// Turns a YapsPlug into a plug and a YapsSocket's shapes into a bake:
// measure, bake, patch, write knobs, announce. Plain and skinned meshes,
// every slot and mesh the chain reaches, a slot of its own on a shared one.
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

        // DPS's tracker: digit 9, intensity = length, at the base. VRCFury's
        // trailing digits, so a mod reading the protocol in C# answers this
        // plug the way it answers any other. See the socket ranges for why the
        // +0.003 that used to sit here was dropped.
        public const float TrackerRange = 0.4906f;

        public class Outcome
        {
            public bool Ok;
            public string Message;
            public List<string> Notes = new List<string>();
            public Material Material;
            public float Length, Radius;
        }

        // --- make this a plug ------------------------------------------------

        // Every readout comes off the mesh before the bake, so one that stops
        // short puts back the ones whose plugs still own their materials.
        public static Outcome Bake(YapsPlug plug)
        {
            var o = BakeOnce(plug);
            if (!o.Ok && plug != null) YapsDebugOverlayBuilder.Build(plug.Target, null);
            return o;
        }

        static Outcome BakeOnce(YapsPlug plug)
        {
            var o = new Outcome();
            if (plug == null) { o.Message = "no plug"; return o; }
            var renderer = plug.Target;
            if (renderer == null) { o.Message = "the plug has no renderer"; return o; }

            string dir = OutputRoot + "/" + Sanitise(AvatarRoot(plug.transform).name);
            EnsureFolder(dir);

            var report = new BridgeReport();
            // The named root bone is the chain, else the plug object.
            var chainRoot = plug.rootBone != null ? plug.rootBone : plug.transform;
            // Every readout on the mesh off first, this plug's and any other's,
            // or their vertices bake as the plug's.
            YapsDebugOverlayBuilder.Restore(plug);
            YapsDebugOverlayBuilder.Restore(renderer);
            var filter = renderer.GetComponent<MeshFilter>();
            var unbounded = filter != null ? filter.sharedMesh : null;
            // Every clip that can run, not only the ones WireSize may write to:
            // the box has to hold whatever resizes the bone.
            var top = AvatarRoot(plug.transform);
            float size = YapsBaker.LargestSize(YapsSwapFollow.RunnableClips(top),
                SizeBones(plug.rootBone, YapsSwapFollow.AnimationRootOf(top)));
            var result = YapsBaker.Bake(renderer, chainRoot, dir, report, out string failure,
                flipAxis: plug.flipAxis, sizeScale: size);
            // A plain mesh moved onto the bounds copy; a re-bake keeps the copy and the record.
            if (filter != null && filter.sharedMesh != unbounded) { plug.boundsFrom = unbounded; EditorUtility.SetDirty(plug); }
            if (result == null) { o.Message = "could not bake: " + failure; return o; }
            // A plain mesh bakes in its own units; the markers and the
            // report want metres.
            float toMetres = result.FromSkinnedMesh ? 1f : Mathf.Abs(renderer.transform.lossyScale.z);
            o.Length = plug.lengthOverride > 0 ? plug.lengthOverride : result.Length * toMetres;
            o.Radius = result.Radius * toMetres;

            // The named slot; else the ones a conversion recorded; else, on a
            // skinned mesh, the slot the chain moves; else the first.
            var mats = renderer.sharedMaterials;
            if (mats == null || mats.Length == 0) { o.Message = "the renderer has no materials"; return o; }
            int slot = plug.materialSlot >= 0 && plug.materialSlot < mats.Length ? plug.materialSlot : -1;
            // Every slot the plug's vertices reach, not only the primary. An
            // explicit materialSlot is the author overriding that and wins.
            var alsoSlots = new List<int>();
            if (slot < 0 && plug.converted)
            {
                // The slots the conversion baked, primary first, never the
                // plain-mesh branch below: that would take every material on
                // the body.
                int lost = plug.bakedSlots.Count(b => b != null && b.renderer == null);
                if (lost > 0)
                    o.Notes.Add($"{lost} baked slot record(s) name no mesh, or a mesh since deleted, so they " +
                                "were not baked again.");
                alsoSlots = RecordedSlots(plug, renderer);
                if (alsoSlots.Count == 0)
                {
                    o.Message = "this converted plug has no record of the slots it was baked into; convert the avatar again";
                    return o;
                }
                slot = alsoSlots[0];
            }
            else if (slot < 0 && renderer is SkinnedMeshRenderer skinned && plug.rootBone != null)
            {
                // result.Root, not plug.rootBone: the bake may have descended
                // to the shaft, and the slots have to be the ones it baked.
                alsoSlots = SlotsWeightedTo(skinned, result.Root, out slot);
            }
            else if (slot < 0)
            {
                // A plain mesh bends whole, so every one of its materials
                // carries part of the plug.
                for (int i = 0; i < mats.Length; i++) alsoSlots.Add(i);
            }
            if (slot < 0) slot = 0;
            string splitKey = null;
            // Another plug's bake already in this slot, from another shaft. One
            // material holds one plug's bend, so rather than take it over and
            // leave that plug rigid, this one moves to a slot of its own, on a
            // copy of that material.
            if (renderer is SkinnedMeshRenderer shared && mats[slot] != null && mats[slot].HasProperty("_YAPS_Bake"))
            {
                var owner = OwnerOfSlot(plug, renderer, slot, mats[slot]);
                var mine = YapsSlotSplit.Mask(shared, result.Root);
                if (owner != null && !YapsSlotSplit.SameShaft(mine,
                        YapsSlotSplit.Mask(shared, owner.rootBone != null ? owner.rootBone : owner.transform)))
                {
                    // What the slot held before the other plug baked, so this
                    // bake is a fresh one with its own record for Remove. A
                    // copy of the other plug's material carried its bake, and
                    // the re-bake path deleted that as the texture it replaced.
                    var before = owner.bakedSlots.FirstOrDefault(b => b != null && b.slot == slot
                                     && Same(b.renderer, renderer, owner))?.was ?? owner.bakedFrom;
                    string copyPath = null;
                    if (before == null)
                    {
                        // Nothing recorded: the material was on a YAPS shader
                        // already and the other plug baked it in place. A copy
                        // with no bake yet, so the re-bake path has no texture
                        // of the other plug's to delete as the one it replaces.
                        before = new Material(mats[slot]) { name = mats[slot].name + " " + YapsBaker.PlaceKey(plug.transform) };
                        before.SetTexture("_YAPS_Bake", null);
                        copyPath = dir + "/" + Sanitise(before.name) + ".mat";
                        AssetDatabase.DeleteAsset(copyPath);
                        AssetDatabase.CreateAsset(before, copyPath);
                    }
                    var unsplit = shared.sharedMesh;
                    int added = YapsSlotSplit.Split(shared, result.Root, slot, mine, before, dir, out string why);
                    if (added < 0 && copyPath != null) AssetDatabase.DeleteAsset(copyPath);
                    if (added >= 0)
                    {
                        splitKey = YapsBaker.PlaceKey(plug.transform);
                        plug.splitFrom = unsplit;
                        EditorUtility.SetDirty(plug);
                        o.Notes.Add($"It shared slot {slot} with \"{owner.name}\", a plug on another shaft, and one " +
                                    $"material holds one plug's bend. Its triangles moved to slot {added} on a copy of " +
                                    "the mesh, so both bend.");
                        // No materialSlot written: that is the author's
                        // override and turns off baking into every slot the
                        // plug reaches. Its triangles now sit in the new
                        // slot, so the next bake finds it unaided. The slots
                        // it reaches changed with the mesh, and the one it
                        // left is the other plug's: mirroring into it would
                        // take that material over after all.
                        if (plug.converted)
                        {
                            // Its record follows its triangles, or the next
                            // re-bake starts from the slot it just left.
                            foreach (var b in plug.bakedSlots)
                                if (b != null && b.slot == slot && b.renderer == renderer) b.slot = added;
                            alsoSlots = RecordedSlots(plug, renderer);
                        }
                        else alsoSlots = SlotsWeightedTo(shared, result.Root);
                        alsoSlots.Remove(slot);
                        slot = added;
                        mats = renderer.sharedMaterials;
                    }
                    else
                    {
                        o.Notes.Add($"It shares slot {slot} with \"{owner.name}\", a plug on another shaft, and could " +
                                    $"not be given a slot of its own ({why}), so it takes the material over and that " +
                                    "plug will not bend.");
                    }
                }
            }
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

            // A material THIS toolkit generated is never an original, even when it
            // looks like one. Remove's fallback puts the source shader back on the
            // clone and leaves it in the slot, so the next bake sees no _YAPS_Bake
            // and clones it again, burying the real original one level deeper every
            // round. Re-patch it in place.
            bool oursAlready = IsGenerated(source);
            if (oursAlready && !source.HasProperty("_YAPS_Bake"))
            {
                source.shader = shader;
                EditorUtility.SetDirty(source);
            }
            bool fresh = !source.HasProperty("_YAPS_Bake") && !oursAlready;
            var patched = fresh
                ? YapsBaker.Apply(result, source, shader, dir, result.FromSkinnedMesh, splitKey)
                : source;
            // One walk for the swap repair here and the variants below. Nothing
            // between them adds a clip; Follow only rewrites keys.
            var clips = YapsSwapFollow.RunnableClips(renderer.transform);
            if (patched != source)
            {
                mats[slot] = patched;
                renderer.sharedMaterials = mats;
                // What the slot held, for Remove to put back.
                if (plug.bakedFrom == null) { plug.bakedFrom = original; EditorUtility.SetDirty(plug); }
                RecordBakedSlot(plug, renderer, slot, original);
                // Any toggle that assigns this slot has to follow the bake, or
                // it hands the slot back to the unbaked material the moment
                // the animator runs.
                YapsSwapFollow.Follow(renderer, slot, original, patched, report, clips);
            }
            else
            {
                // Re-bake: refresh everything the bake measured, and drop the texture
                // it replaces so a session of re-bakes does not leave a twelve-megabyte
                // asset per click behind. The shader too, when this version emits
                // something the patch on it predates.
                if (YapsShaderPatcher.IsStale(patched)) YapsShaderPatcher.Refresh(patched, dir, report);
                var previous = patched.GetTexture("_YAPS_Bake");
                // THE SAME CALL A FRESH BAKE MAKES, never a hand-copied subset of it.
                // This listed five of the seven fields and left out _YAPS_BakeScale and
                // _YAPS_BakeGirth, so a re-bake kept whatever those were sitting at
                // instead of returning them to the rest pose of 1, and on a plug whose
                // size is animated that is wherever the last clip left them. They scale
                // the baked positions, the length and the channel's offset, so a
                // re-baked plug deformed differently from a freshly baked one.
                //
                // Two doors to one job again. Apply is the door.
                YapsBaker.Apply(result, patched, result.FromSkinnedMesh);
                string old = previous != null && previous != result.Bake
                    ? AssetDatabase.GetAssetPath(previous) : null;
                if (!string.IsNullOrEmpty(old) && old.StartsWith(OutputRoot + "/", System.StringComparison.Ordinal))
                {
                    AssetDatabase.DeleteAsset(old);
                }
            }
            if (plug.lengthOverride > 0) patched.SetFloat("_YAPS_Length", BakeLength(renderer, patched, plug.lengthOverride));
            patched.SetFloat("_YAPS_Enabled", 1f);
            WritePlugWide(plug, patched);
            // Past four, entries are dropped. Saying so here because the
            // inspector list takes as many as anyone types and the bake was
            // the only thing that knew otherwise.
            int over = YapsTags.Dropped(plug.answers) + YapsTags.Dropped(plug.refuses);
            if (over > 0)
            {
                o.Notes.Add($"{over} tag(s) past the first {YapsTags.PlugSlots} on each list were "
                    + "not baked: that is all the plug has room for. Delete the ones you do not "
                    + "need, or put them on the socket instead, which has no limit.");
            }

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

            // After the knobs, so the readout copies the values the plug
            // actually ended up with rather than the ones it started from.
            YapsDebugOverlayBuilder.Apply(plug, result, patched, report);

            // The rest of the slots the plug's vertices reach. They carry the SAME
            // deform: the bake is indexed by a mesh-global vertex id, so one bake
            // serves every submesh, and every _YAPS_ value is copied from the
            // primary rather than recomputed. A submesh bending on its own
            // curvature would tear against its neighbour.
            int mirrored = MirrorToSlots(plug, renderer, patched, alsoSlots, dir, report);
            if (mirrored > 0)
            {
                o.Notes.Add($"The plug's vertices span {mirrored + 1} of this mesh's materials, so the " +
                            "deform was baked into all of them. Baking only the primary one would leave " +
                            "the rest rigid and tear the mesh along the seam.");
            }

            // And every OTHER mesh the same bones move. One plug, one frame,
            // a bake each.
            var meshes = new List<(Renderer, YapsBaker.Result)> { (renderer, result) };
            int alsoMats = MirrorToRenderers(plug, renderer, result, dir, report, out int alsoMeshes, meshes, size);
            if (alsoMeshes > 0)
            {
                o.Notes.Add($"{alsoMeshes} other mesh(es) on this avatar are weighted to the plug's bone " +
                            $"chain, so they were baked into it too ({alsoMats} material(s)), sharing this " +
                            "plug's frame and length. Left out they would have stayed rigid while the rest bent.");
            }

            // And every OTHER material a toggle can put in that slot. Follow only
            // repairs the swap that puts the bake source back; an alternate look
            // was never the source, so nothing above reaches it and the plug goes
            // rigid for as long as that toggle is on. After the knobs and every
            // slot are written: each variant copies its slot's finished values,
            // or it bends on the shader's defaults.
            YapsSwapFollow.FollowVariants(plug, renderer, slot, result, dir, clips, report,
                patched, original, source);

            // Announce: tip light for DPS, pointers for TPS and SPS.
            BuildMarkers(plug, result, o.Length, o.Radius);

            // The avatar's own animations that change the plug's size, shape
            // sliders and bone scale, now tell the material too.
            WireSize(plug, meshes, o);

            // A switch for the deform, unless the avatar already has one.
            var avatarForToggle = plug.GetComponentInParent<CVRAvatar>(true);
            if (avatarForToggle != null)
            {
                string toggled = YapsToggles.EnsurePlugToggle(plug, avatarForToggle, patched, YapsToggles.LabelFor(plug));
                if (toggled != null) o.Notes.Add(toggled);
                // And a second row for whose sockets it answers, where that
                // can mean anything. Off, so an avatar carrying both parts
                // behaves as it did before the atlas learned to see its own.
                string own = YapsToggles.EnsureSelfToggle(plug, avatarForToggle, patched,
                    YapsToggles.LabelFor(plug) + " own sockets");
                if (own != null) o.Notes.Add(own);
            }

            o.Ok = true;
            o.Message = $"Baked \"{renderer.name}\": {o.Length:0.###} m, {result.VertexCount} vertices, " +
                        $"{result.Shapes.Count} shape(s), material \"{patched.name}\".";
            o.Notes.Add(YapsPropBuilder.IsProp(AvatarRoot(plug.transform).gameObject)
                ? "This plug is on a prop; make it a prop again so the prop carries the new bake."
                : "This plug finds sockets through the screen atlas, and by their marker lights where the " +
                  "atlas cannot answer: a view too small to hold it, or content that only has lights.");
            if (result.FromSkinnedMesh) o.Notes.Add("Skinned mesh: frame recovered per vertex.");
            return o;
        }

        // The switches every material of one plug shares. A carried mesh left
        // on its last bake's answers a socket the rest refuses, and tears there.
        static void WritePlugWide(YapsPlug plug, Material m)
        {
            // 1 when this avatar wears sockets of its own, so the plug checks
            // ownership before answering a light, -1 when there is nothing to check
            // for. The toolkit hardcoded -1, which reads as "this avatar has no
            // sockets" and switches own-body exclusion off entirely:
            //
            //     if (_YAPS_SelfTag >= 0 && YapsSameBodyAs(...)) skip;
            //
            // The wearer's own sockets are permanently in reach and permanently
            // nearest, so a plug that does not skip them never looks at anybody
            // else's. The atlas skips them only past engagement onset now, so
            // the flag still decides whether ownership is asked about at all.
            var ownAvatar = plug.GetComponentInParent<CVRAvatar>(true);
            bool ownSockets = ownAvatar != null
                              && ownAvatar.GetComponentsInChildren<YapsSocket>(true).Length > 0;
            m.SetFloat("_YAPS_SelfTag", ownSockets ? 1f : -1f);

            // The tag sets, one pattern per component. Only the atlas reads
            // them: a contact cannot see what a socket is tagged, so the
            // channel tier answers without asking.
            m.SetVector("_YAPS_TagInclude", YapsTags.Patterns(plug.answers));
            m.SetVector("_YAPS_TagExclude", YapsTags.Patterns(plug.refuses));
            // Build adds the socket writers and the avatar's clear and grab, so a
            // toolkit avatar published to the atlas and then read none of it: the
            // flag was set on the convert path only, and a plug with it off falls
            // back to the lights without saying so.
            m.SetFloat("_YAPS_UseAtlas", 1f);
        }

        // Mirrors the avatar's own size animations onto the plug's material:
        // shape curves onto the shape weights, the root bone's scale onto
        // the bake scale. Edits the user's clips, adding a curve beside each
        // it mirrors, and says so. Idempotent: the same curve every time.
        static void WireSize(YapsPlug plug, List<(Renderer renderer, YapsBaker.Result result)> meshes, Outcome o)
        {
            var top = AvatarRoot(plug.transform);
            // NOT the Animator's own slot. ChilloutVR uploads what avatar.overrides
            // points at and falls back to avatarSettings.baseController. The
            // Animator's slot holds a generated override that is not what ships,
            // and on an avatar that has never been built it is often empty. Reading
            // only that slot found nothing at all and mirrored no shapes, so a plug
            // with a size slider bent against a rest pose its mesh had left.
            var animRoot = YapsSwapFollow.AnimationRootOf(top);
            if (animRoot == null) return;
            var clips = YapsSwapFollow.RunnableClips(top)
                .Where(YapsCurveMirror.UserOwned).ToList();
            if (clips.Count == 0) return;
            string plugPath = AnimationUtility.CalculateTransformPath(plug.transform, animRoot);

            // Each bone with its path: the mirror reads the scale it is
            // sitting at now, which is the pose the bake just measured.
            var bones = SizeBones(plug.rootBone, animRoot);

            // Every mesh the bake reached, or a resized or switched plug tears
            // where one mesh heard the animation and the other did not.
            var missed = new HashSet<string>();
            int shapes = 0, switched = 0, scaled = 0;
            foreach (var (renderer, result) in meshes)
            {
                string rendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, animRoot);
                if (result.Shapes.Count > 0)
                {
                    shapes += YapsCurveMirror.MirrorShapes(clips, rendererPath, renderer, result.Shapes,
                        result.MovingShapes, missed);
                }
                // Somebody animating the component's own checkbox meant the
                // deform, so give them the deform.
                switched += YapsCurveMirror.MirrorEnabled(clips, plugPath, typeof(YapsPlug),
                    rendererPath, renderer, "_YAPS_Enabled");
                scaled += YapsCurveMirror.MirrorBoneScale(clips, bones, rendererPath, renderer, result.Rotation);
            }
            if (switched > 0)
            {
                o.Notes.Add($"{switched} clip(s) animate this component's own Enabled field, which does " +
                            "nothing in game: ChilloutVR strips the component. A matching curve on the " +
                            "material's _YAPS_Enabled was written beside each, so the animation now " +
                            "switches the deform the way it was meant to.");
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

        // The chain root and its bone children by path: a size slider scales
        // one of them. The mirror reads it for the shader, the bake for its
        // culling box.
        static Dictionary<string, Transform> SizeBones(Transform chainRoot, Transform animRoot)
        {
            var bones = new Dictionary<string, Transform>();
            if (chainRoot == null || animRoot == null) return bones;
            void Bone(Transform t)
            {
                string p = AnimationUtility.CalculateTransformPath(t, animRoot);
                if (p != null) bones[p] = t;
            }
            Bone(chainRoot);
            for (int i = 0; i < chainRoot.childCount; i++) Bone(chainRoot.GetChild(i));
            return bones;
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

        // The other way: metres into the units the material holds, at rest.
        // The length override is metres, and written raw it gave a scaled
        // plain mesh an envelope off by its import scale.
        public static float BakeLength(Renderer renderer, Material m, float metres)
        {
            bool skinned = m != null && m.HasProperty("_YAPS_FrameFromVertex")
                ? m.GetFloat("_YAPS_FrameFromVertex") > 0.5f
                : renderer is SkinnedMeshRenderer s && s.bones != null && s.bones.Length > 0;
            float scale = !skinned && renderer != null ? Mathf.Abs(renderer.transform.lossyScale.z) : 1f;
            return metres / Mathf.Max(scale, 1e-6f);
        }

        // Every YapsPlug wearing this material takes the values back. After a
        // slot split two plugs share a mesh, and one's panel rewrote the
        // other's knobs, so a slot another plug owns is skipped. A slot nobody
        // recorded still syncs, as before.
        public static void SyncPlugsFrom(Material m)
        {
            if (m == null) return;
            var plugs = Object.FindObjectsOfType<YapsPlug>(true);
            foreach (var plug in plugs)
            {
                var r = plug.Target;
                if (r == null) continue;
                var mats = r.sharedMaterials;
                var slots = Enumerable.Range(0, mats.Length).Where(i => mats[i] == m).ToList();
                if (slots.Count == 0) continue;
                if (!slots.Any(i => OwnsSlot(plug, r, i, m))
                    && plugs.Any(p => p != plug && slots.Any(i => OwnsSlot(p, r, i, m)))) continue;
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
            m.SetFloat("_YAPS_BulgeFalloff", p.bulgeFalloff);
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
            // A skinned mesh's markers sit on the measured frame, a plain mesh's
            // on its own origin and +Z, where its bake bends from, which is not
            // the plug object when the component sits above the mesh.
            bool skinned = result != null && result.FromSkinnedMesh;
            var mesh = plug.Target != null ? plug.Target.transform : plug.transform;
            AnnouncePlug(plug.transform, skinned ? result.Origin : mesh.position, skinned ? result.Rotation : mesh.rotation,
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
                // Offsets are metres along the frame. The markers inherit the
                // parent's scale, so a raw local offset under a 100x armature
                // bone put the tip a hundred lengths out.
                void Add(string name, string type, Vector3 at)
                {
                    if (!have.Contains(type)) YapsSocketBuilder.Pointer(m, name, type, m.InverseTransformVector(m.rotation * at));
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
        // Whether the mesh's own origin IS the socket, a dedicated socket mesh
        // as a rule. Only that mesh opens in its shader: depth measures from
        // the mesh origin, and only a mesh whose origin is the socket measures
        // from the right place. A skinned socket mesh is unverified: it draws
        // with an identity object matrix too (yaps_socket.cginc).
        public static bool MeshIsTheSocket(Renderer renderer, Transform socket)
        {
            if (renderer == null || socket == null) return false;
            return Vector3.Distance(renderer.transform.position, socket.position) < 0.03f;
        }

        // The route Build takes for this socket's shapes: the shader on a mesh
        // of the socket's own, the animator on any other. The editors ask this
        // too, so what the inspector says is what Build does.
        public static bool ShapesByContact(YapsSocket socket)
        {
            if (socket == null || socket.renderer == null) return false;
            return !MeshIsTheSocket(socket.renderer, socket.transform);
        }

        // Does a plug of the wearer's own rest on top of this socket?
        //
        // This is the whole reason self-exclusion exists: an avatar carrying
        // both has its plug's tracker permanently within a plug length of its
        // own socket, and the socket reads as always full. Measured here, in
        // the rest pose, because the shader can only guess at it: it decides
        // ownership by whose hip is nearest, which is right for a socket at the
        // wearer's hip and wrong for one out on a hand.
        //
        // The tracker light is the measurement, never the plug's transform. It
        // is the point the shader measures from, and it carries the length.
        static bool OwnPlugRestsOn(YapsSocket socket)
        {
            var top = AvatarRoot(socket.transform);
            if (top == null) return false;
            foreach (var light in top.GetComponentsInChildren<Light>(true))
            {
                if (light == null || light.type != LightType.Point) continue;
                // 0.001, the protocol's own window, not an exact match: the
                // wearer's plug counts here whether YAPS baked it or DPS did.
                if (Mathf.Abs(light.range - TrackerRange) > 0.001f) continue;
                float length = Mathf.Max(light.intensity, 0.01f);
                // Within its own length of the socket, plus a hand's width
                // of slack for a pose that is not quite the rest one.
                if (Vector3.Distance(light.transform.position, socket.transform.position) <= length + 0.1f)
                    return true;
            }
            return false;
        }

        // A socket's own baked material, told from a plug's: the socket bake
        // switches the deform off, the plug bake switches it on. Both carry
        // _YAPS_Bake, so the texture alone says nothing. Power alone cannot
        // tell either: Strength can be 0, which sent a rebuild of the socket's
        // own material down the contact route, and a plug switched off in its
        // panel reads 0 too. So the slot this socket recorded counts first.
        static bool IsSocketMaterial(Material m, YapsSocket socket, Renderer renderer, int slot)
        {
            if (m == null || !m.HasProperty("_YAPS_Enabled") || m.GetFloat("_YAPS_Enabled") > 0f) return false;
            bool recorded = socket.bakedRenderer == renderer ? socket.bakedSlot == slot
                : socket.bakedRenderer == null && socket.bakedFrom != null;
            return recorded || m.HasProperty("_YAPS_SocketPower") && m.GetFloat("_YAPS_SocketPower") > 0f;
        }

        // Everything Build does for one socket: its markers, its shapes,
        // and a menu toggle when nothing switches it. What the window does
        // per socket, and what the inspector's own button does.
        public static List<string> BuildSocket(YapsSocket socket)
        {
            var lines = new List<string>();
            if (socket == null) return lines;
            Undo.RegisterFullObjectHierarchyUndo(socket.gameObject, "Build YAPS socket");
            string renamed = YapsToggles.RenameToLabel(socket, socket.GetComponentInParent<CVRAvatar>(true));
            if (renamed != null) lines.Add($"✓ {renamed}");
            YapsSocketBuilder.Build(socket);
            string capped = YapsSocketBuilder.LightCapNote(socket);
            if (capped != null) lines.Add($"✓ {YapsToggles.LabelFor(socket)}: {capped}");
            string shapes = BakeSocket(socket);
            if (shapes != null) lines.Add(shapes);
            string played = YapsSocketReactions.BuildAnimations(socket);
            if (played != null) lines.Add(played);
            var avatar = socket.GetComponentInParent<CVRAvatar>(true);
            int before = YapsToggles.Edits;
            string toggled = YapsToggles.EnsureObjectToggle(socket.gameObject, avatar, YapsToggles.LabelFor(socket));
            if (toggled != null) lines.Add(toggled);
            string menu = YapsToggles.RefreshMenuAnimator(avatar, before);
            if (menu != null) lines.Add(menu);
            // After the toggle layers: the lighthouse asserts the chosen
            // socket on, and a layer wins by coming later.
            string lighthouse = null;
            foreach (var controller in YapsOwner.Targets(avatar)) lighthouse = YapsLighthouse.Build(avatar, controller) ?? lighthouse;
            if (lighthouse != null) lines.Add($"✓ {lighthouse}");
            string owner = YapsOwner.Wire(avatar);
            if (owner != null) lines.Add($"✓ {owner}");
            string readout = null;
            foreach (var controller in YapsOwner.Targets(avatar)) readout = YapsDebugOverlayBuilder.Menu(avatar, controller) ?? readout;
            if (readout != null) lines.Add($"✓ {readout}");
            string shared = YapsOwner.SharedWarning(avatar);
            if (shared != null) lines.Add($"✗ {shared}");
            return lines;
        }

        // A plug's bake, then the menu animator refreshed if its toggle
        // changed the entries. What the plug inspector's button does.
        public static Outcome BakeAndRefreshMenu(YapsPlug plug)
        {
            int before = YapsToggles.Edits;
            var o = Bake(plug);
            var avatar = plug != null ? plug.GetComponentInParent<CVRAvatar>(true) : null;
            string menu = YapsToggles.RefreshMenuAnimator(avatar, before);
            if (menu != null) o.Notes.Add(menu);
            // The contact channel, which the window's Build does after baking and
            // this door did not. The same plug came out differently depending on
            // which button was pressed. The channel reads the frames the bake just
            // measured and replaces its own wiring rather than stacking, so doing
            // it per plug here is safe.
            if (avatar != null && o.Ok)
            {
                o.Notes.AddRange(YapsNativeChannel.Build(avatar));
            }
            return o;
        }

        // Which submesh the socket actually IS.
        //
        // It was always slot 0, so a socket modelled into a mesh carrying
        // several materials had the shader patched onto whichever part came
        // first, while the submesh holding the opening kept the shader it came
        // with. The shapes never moved and nothing said why.
        //
        // The blendshapes name it: a shape's deltas are non-zero on exactly the
        // vertices it moves, and those are the socket's own triangles. Biggest
        // share wins, and a mesh with one material keeps the old answer.
        // Only submeshes with a material: Unity allows fewer materials than
        // submeshes, and one past the end has nothing to patch.
        static int SocketSlot(SkinnedMeshRenderer skin, IEnumerable<string> shapes, int materials)
        {
            var mesh = skin != null ? skin.sharedMesh : null;
            int subs = mesh != null ? Mathf.Min(mesh.subMeshCount, materials) : 0;
            if (subs < 2) return 0;

            var moved = new bool[mesh.vertexCount];
            var delta = new Vector3[mesh.vertexCount];
            var spare = new Vector3[mesh.vertexCount];
            bool any = false;
            foreach (string name in shapes)
            {
                int index = mesh.GetBlendShapeIndex(name);
                if (index < 0) continue;
                for (int f = 0; f < mesh.GetBlendShapeFrameCount(index); f++)
                {
                    mesh.GetBlendShapeFrameVertices(index, f, delta, spare, null);
                    for (int v = 0; v < delta.Length; v++)
                        if (delta[v].sqrMagnitude > 1e-10f) { moved[v] = true; any = true; }
                }
            }
            if (!any) return 0;

            int best = 0, bestHits = 0;
            for (int sub = 0; sub < subs; sub++)
            {
                var indices = mesh.GetTriangles(sub);
                int hits = 0;
                for (int i = 0; i < indices.Length; i++)
                    if (indices[i] < moved.Length && moved[indices[i]]) hits++;
                if (hits > bestHits) { bestHits = hits; best = sub; }
            }
            return best;
        }

        // Where the socket's bake went, so Unbake and Remove find it again
        // after the mesh field changes.
        static void RecordSocketSlot(YapsSocket socket, Renderer renderer, int slot)
        {
            if (socket.bakedRenderer == renderer && socket.bakedSlot == slot) return;
            Undo.RecordObject(socket, "YAPS socket bake");
            socket.bakedRenderer = renderer;
            socket.bakedSlot = slot;
            EditorUtility.SetDirty(socket);
        }

        // The mesh back on the material the bake took it off. Keyed by the
        // renderer and slot the bake recorded, because a socket whose mesh is
        // set back to None no longer names the renderer it baked.
        static string Unbake(YapsSocket socket)
        {
            var renderer = socket.bakedRenderer;
            string said = null;
            var mats = renderer != null ? renderer.sharedMaterials : null;
            if (mats != null && socket.bakedFrom != null && socket.bakedSlot >= 0 && socket.bakedSlot < mats.Length
                && mats[socket.bakedSlot] != null && mats[socket.bakedSlot] != socket.bakedFrom
                && mats[socket.bakedSlot].HasProperty("_YAPS_Bake"))
            {
                Undo.RecordObject(renderer, "YAPS socket bake");
                var baked = mats[socket.bakedSlot];
                mats[socket.bakedSlot] = socket.bakedFrom;
                renderer.sharedMaterials = mats;
                // The bake pointed this slot's swap keys at its material. Left
                // there, a toggle puts the shader deform back on a socket that
                // no longer has it, beside any reactions layer on the same shapes.
                YapsSwapFollow.Follow(renderer, socket.bakedSlot, baked, socket.bakedFrom);
                said =$"\"{renderer.name}\" back on \"{socket.bakedFrom.name}\"";
            }
            if (socket.bakedFrom == null && socket.bakedRenderer == null) return said;
            Undo.RecordObject(socket, "YAPS socket bake");
            socket.bakedFrom = null;
            socket.bakedRenderer = null;
            socket.bakedSlot = -1;
            EditorUtility.SetDirty(socket);
            return said;
        }

        // Bakes the socket's chosen shapes into its mesh's material, staged
        // as the component says. Returns what happened, for the window.
        public static string BakeSocket(YapsSocket socket)
        {
            if (socket == null) return null;
            socket.builtBy = BridgeDefines.Version;
            var renderer = socket.renderer;
            var stages = socket.shapes.Where(s => s != null && !string.IsNullOrEmpty(s.blendshape)).ToList();
            // Nothing to open any more. Emptying the list is an instruction,
            // not nothing to do: the depth animations have always read it that
            // way, and this path left a layer, a synced parameter, a contact
            // and a baked material behind on every socket switched back.
            if (renderer == null || stages.Count == 0)
            {
                var undone = new List<string>();
                string put = Unbake(socket);
                if (put != null) undone.Add(put);
                string cleared = YapsSocketReactions.Clear(socket);
                if (cleared != null) undone.Add(cleared);
                return undone.Count > 0 ? $"✓ {socket.name}: nothing opens now, " + string.Join(", ", undone) : null;
            }
            if (renderer.sharedMesh == null || renderer.sharedMesh.blendShapeCount == 0)
                return $"✗ {socket.name}: its mesh has no blendshapes";
            // Any mesh but the socket's own opens through the animator, see
            // MeshIsTheSocket. Unbaked first: an earlier build put body-mesh
            // sockets on the shader, and that material would stay beside the
            // reactions, driving the same shapes twice.
            if (!MeshIsTheSocket(renderer, socket.transform))
            {
                string put = Unbake(socket);
                string reacted = YapsSocketReactions.Build(socket) ?? $"✓ {socket.name}";
                return put != null ? $"{reacted} ({put})" : reacted;
            }

            var mats = renderer.sharedMaterials;
            if (mats == null || mats.Length == 0) return $"✗ {socket.name}: its mesh has no material";
            int slot = SocketSlot(renderer, stages.Select(s => s.blendshape), mats.Length);
            if (mats[slot] == null) return $"✗ {socket.name}: its mesh has no material";
            string dir = OutputRoot + "/" + Sanitise(AvatarRoot(socket.transform).name);
            EnsureFolder(dir);
            var report = new BridgeReport();
            var wanted = stages.Select(s => s.blendshape).ToList();
            var result = YapsBaker.Bake(renderer, socket.transform, dir, report, out string failure,
                wanted, objectFrame: true);
            if (result == null) return $"✗ {socket.name}: could not bake: {failure}";
            if (result.Shapes.Count == 0) return $"✗ {socket.name}: none of the named shapes exist on \"{renderer.name}\"";

            var source = mats[slot];
            // A material carries ONE bake. If this mesh's material is a plug's,
            // baking the socket into it replaces the plug's vertex data with the
            // socket's and the plug stops deforming, which is what a ring placed on
            // the plug's own mesh does. Drive the shapes by contact instead: same
            // reactions, nobody's bake overwritten.
            if (source.HasProperty("_YAPS_Bake") && !IsSocketMaterial(source, socket, renderer, slot))
            {
                string byContact = YapsSocketReactions.Build(socket);
                return (byContact ?? $"✓ {socket.name}") +
                       " (its mesh is a plug's, and a material holds one bake, so its shapes " +
                       "are driven by a depth contact rather than the socket shader)";
            }

            Material material;
            if (source.HasProperty("_YAPS_Bake"))
            {
                // Generated here already: a socket's own material, baked before.
                material = source;
                // Where it sits, for a socket baked before this was recorded.
                RecordSocketSlot(socket, renderer, slot);
                // Refresh the SHADER too when the tool has moved on since this was
                // patched. A material keeps its values across a shader swap, and a
                // property the old code never had arrives at its declared default,
                // which the writes below then set. Without this a rebuild refreshes the
                // bake and leaves the deform on whatever code shipped that day.
                if (YapsShaderPatcher.IsStale(material)) YapsShaderPatcher.Refresh(material, dir, report);
                var previous = material.GetTexture("_YAPS_Bake");
                // The same call the fresh bake below makes, as the plug's re-bake
                // does. Three fields copied by hand left the length, the rest
                // scale and the shape weights at whatever the last bake wrote.
                YapsBaker.Apply(result, material, result.FromSkinnedMesh);
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
                if (socket.bakedFrom == null) { socket.bakedFrom = mats[slot]; EditorUtility.SetDirty(socket); }
                RecordSocketSlot(socket, renderer, slot);
                var wasSocket = mats[slot];
                // The body usually sits outside the socket's hierarchy, so its
                // undo did not cover this: Ctrl+Z cleared bakedFrom and left the
                // patched material on with nothing for Remove to put back.
                Undo.RecordObject(renderer, "Build YAPS socket");
                mats[slot] = material;
                renderer.sharedMaterials = mats;
                // The slot the bake actually went into, never 0. The repair is keyed by
                // renderer and slot, so a swap animation on a non-zero socket slot was
                // left unrepaired and put the unbaked material back the first time it
                // fired.
                YapsSwapFollow.Follow(renderer, slot, wasSocket, material, report);
            }

            // Stage k is baked shape k, not row k: the bake drops names the mesh
            // lacks and repeats, which shifted every later shape onto an earlier
            // row's range.
            WriteStages(material, result.Shapes
                .Select(n => stages.FirstOrDefault(s => s.blendshape == n))
                .Select(s => s != null ? (s.startsAt, s.fadeOver) : (0f, 0.3f)).ToList());
            material.SetFloat("_YAPS_SocketPower", socket.shapePower);
            material.SetFloat("_YAPS_SocketDepth", -1f);
            // Where the socket sits in the mesh's own space. Depth measures
            // from here; ownership stays on the mesh origin. Near zero, as
            // only the socket's own mesh gets this far.
            material.SetVector("_YAPS_SocketOrigin",
                renderer.transform.InverseTransformPoint(socket.transform.position));
            // Self-exclusion, unless the anchor cannot say whose socket this
            // is. See RidesAMovingLimb.
            bool undecidable = RidesAMovingLimb(socket.transform);
            material.SetFloat("_YAPS_SocketNoSelfExclude",
                OwnPlugRestsOn(socket) && !undecidable ? 0f : 1f);
            EditorUtility.SetDirty(material);
            return $"✓ {socket.name}: {result.Shapes.Count} shape(s) staged on \"{renderer.name}\"";
        }

        // Whether this socket hangs off an arm or a leg.
        //
        // It matters because ownership is decided by which player's hip is
        // nearest the mesh's own origin, and on a dedicated socket mesh that
        // origin IS the socket. A hand resting in somebody's lap answers with
        // THEIR hip, so the wearer's own plug test passes for a stranger's
        // plug and the socket ignores the one plug it exists for.
        //
        // Nothing the shader can see repairs it. An offset baked from the
        // socket to the wearer's hips is only true in the pose it was baked
        // in, and a hand leaves that pose immediately; a skinned mesh has no
        // usable object matrix at all.
        //
        // So the exclusion is dropped where it cannot be decided, which is the
        // direction the resolver already leans: a stranger's plug wrongly
        // ignored is no effect at all, where the wearer's own plug holding
        // their socket open is a visibly wrong one. On the body, where the
        // origin really is the wearer, nothing changes.
        public static bool RidesAMovingLimb(Transform socket)
        {
            var avatar = socket != null ? socket.GetComponentInParent<CVRAvatar>(true) : null;
            var animator = avatar != null ? avatar.GetComponent<Animator>() : null;
            if (animator == null || !animator.isHuman) return false;

            // The four roots that carry a socket away from the hips. The head
            // is deliberately not among them: it turns, but it stays over the
            // body, and a mouth socket wants its wearer's plug excluded.
            var limbs = new[]
            {
                HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
            };
            foreach (var bone in limbs)
            {
                var t = animator.GetBoneTransform(bone);
                if (t != null && socket.IsChildOf(t)) return true;
            }
            return false;
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

        // Patch each extra slot and give it the primary's deform exactly.
        // Returns how many were mirrored.
        static int MirrorToSlots(YapsPlug plug, Renderer renderer, Material primary,
            List<int> slots, string dir, BridgeReport report)
        {
            if (slots == null || slots.Count == 0) return 0;
            var mats = renderer.sharedMaterials;
            int done = 0;
            // Walked once for every slot: Follow rewrites keys, never the set of clips.
            List<AnimationClip> clips = null;
            foreach (int i in slots)
            {
                if (i < 0 || i >= mats.Length || mats[i] == null || mats[i] == primary) continue;
                var was = mats[i];
                Material target;
                if (IsGenerated(was) && !was.HasProperty("_YAPS_Bake"))
                {
                    // Generated here, with its shader reverted by a previous Remove.
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
                    // Generated already, from an earlier bake: refresh it, and do NOT record it
                    // as the slot's original. Recording it makes Remove put a patched
                    // material back and call that a restore.
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
                        // Leave a material that cannot be patched alone and say so:
                        // silently skipping it is what produces a tear nobody
                        // can account for.
                        report?.Warning("YAPS", $"\"{was.name}\" keeps its own shader",
                            $"The plug's vertices reach this material, but its shader could not be " +
                            $"patched ({refusal}), so that part of the mesh will not bend with the rest.");
                        continue;
                    }
                    target = YapsBaker.Generated(was, shader, dir + "/" + Sanitise(was.name) + "_YAPS_" + YapsBaker.Tail(was, renderer) + ".mat");
                    mats[i] = target;
                }
                CopyYapsProperties(primary, target);
                EditorUtility.SetDirty(target);
                RecordBakedSlot(plug, renderer, i, was);
                YapsSwapFollow.Follow(renderer, i, was, target, report,
                    clips ??= YapsSwapFollow.RunnableClips(renderer.transform));
                done++;
            }
            if (done > 0) renderer.sharedMaterials = mats;
            return done;
        }

        // Every _YAPS_ value from one material onto another, so two submeshes
        // of one mesh cannot disagree about how they bend, and the readout
        // shows what the plug really has. Copied by name, never from a list
        // kept here: a list rots the first time the patcher gains a property.
        // missing: the numbers `to` has no room for, for a caller that must
        // say so.
        public static void CopyYapsProperties(Material from, Material to, List<string> missing = null)
        {
            if (from == null || to == null) return;
            var shader = from.shader;
            for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
            {
                string name = ShaderUtil.GetPropertyName(shader, i);
                if (!name.StartsWith("_YAPS_", System.StringComparison.Ordinal)) continue;
                // The patcher's markers carry their meaning in the description,
                // so the value is nothing anyone reads.
                if (name == YapsShaderGUI.OriginalEditorProperty || name == YapsShaderPatcher.SourceShaderProperty) continue;
                var kind = ShaderUtil.GetPropertyType(shader, i);
                if (!to.HasProperty(name))
                {
                    if (kind == ShaderUtil.ShaderPropertyType.Float || kind == ShaderUtil.ShaderPropertyType.Range
                        || kind == ShaderUtil.ShaderPropertyType.Vector)
                        missing?.Add(name);
                    continue;
                }
                switch (kind)
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
        // Keyed on the renderer as well as the number: a plug spanning meshes
        // has a slot 0 on each of them, and they are not the same slot.
        static void RecordBakedSlot(YapsPlug plug, Renderer on, int slot, Material was)
        {
            if (plug == null || was == null) return;
            var found = plug.bakedSlots.FirstOrDefault(b => b != null && b.slot == slot && Same(b.renderer, on, plug));
            if (found != null) return;                  // the first bake owns the record
            plug.bakedSlots.Add(new YapsPlug.BakedSlot { slot = slot, was = was, renderer = on });
            EditorUtility.SetDirty(plug);
        }

        // A converted plug's slots on one mesh, in the order the converter
        // recorded them, which puts its primary first.
        static List<int> RecordedSlots(YapsPlug plug, Renderer on)
        {
            int count = on.sharedMaterials.Length;
            return plug.bakedSlots.Where(b => b != null && b.renderer == on && b.slot >= 0 && b.slot < count)
                .Select(b => b.slot).Distinct().ToList();
        }

        // A record with no renderer was written before a plug could span
        // them, and belonged to the plug's own.
        public static bool Same(Renderer recorded, Renderer asked, YapsPlug plug)
        {
            if (recorded == asked) return true;
            return recorded == null && plug != null && asked == plug.Target;
        }

        // Every OTHER skinned mesh the chain reaches.
        //
        // A plug rooted at the Armature IS the whole avatar, and an avatar is
        // rarely one renderer: a collar, a second body, hair, all weighted to
        // the same bones. Left out they keep their own shader and stay rigid
        // while everything around them bends, the same seam the material
        // mirroring closes, one level up.
        //
        // Each renderer gets its OWN bake, since the bake is indexed by
        // mesh-global vertex id and no two meshes share one. What they share is
        // the primary's FRAME and LENGTH, so they bend as one object.
        //
        // Only when the author has not named a material slot. Naming one says
        // "this mesh, this slot", and reaching onto other renderers would be
        // answering a question nobody asked.
        //
        // A converted plug takes the meshes and slots its conversion recorded,
        // baked from the same root the converter used. Its object is a marker
        // with no bones under it, so the chain tests below would find nothing.
        static int MirrorToRenderers(YapsPlug plug, Renderer primaryRenderer, YapsBaker.Result primary,
            string dir, BridgeReport report, out int meshes, List<(Renderer, YapsBaker.Result)> joined, float size)
        {
            meshes = 0;
            if (plug == null || plug.materialSlot >= 0) return 0;
            bool converted = plug.converted;
            if (!converted && plug.rootBone == null) return 0;
            if (primary == null || !primary.FromSkinnedMesh) return 0;
            var root = AvatarRoot(plug.transform);
            int done = 0;
            var skins = converted
                ? plug.bakedSlots.Where(b => b != null).Select(b => b.renderer as SkinnedMeshRenderer).Distinct()
                : root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var skin in skins)
            {
                if (skin == null || skin == primaryRenderer || skin.sharedMesh == null) continue;
                var slots = converted ? RecordedSlots(plug, skin) : SlotsWeightedTo(skin, plug.rootBone);
                if (slots.Count == 0) continue;
                // The converter's line too, from the shaft the bake settled on.
                // Any weight on the chain would take a body touching the base,
                // and bake all of it to bend a few vertices at the seam.
                if (!converted && !YapsBaker.RidesPlug(skin, primary.Root != null ? primary.Root : plug.rootBone))
                {
                    report?.Skipped("YAPS", $"\"{skin.name}\" left out of the plug",
                        "It meets the plug only at the plug's root bone, so it is taken for the body " +
                        "the plug grows from and stays as it is. A part that should bend with the " +
                        "shaft needs weights on the shaft's own bones.");
                    continue;
                }

                // A mesh with a plug of its own is not this plug's to take. Both would bake
                // the same material and whichever ran last would win, so which frame
                // the mesh wore depended on hierarchy order: it bent with the body
                // until the second bake, then snapped to its own. A component is the
                // author saying "this one is mine".
                var claimed = OwnerPlugOf(root, skin);
                if (claimed != null && claimed != plug)
                {
                    report?.Warning("YAPS", $"\"{skin.name}\" bends on its own",
                        "Its vertices are weighted to this plug's bone chain, but it has a plug of its " +
                        "own, so it keeps its own frame and length and will not move as one piece with " +
                        "this plug. Remove that plug to fold this mesh in.");
                    continue;
                }

                var result = YapsBaker.Bake(skin, converted ? primary.Root : plug.rootBone, dir, report, out string failure,
                    flipAxis: plug.flipAxis, shareFrameWith: primary, sizeScale: size);
                if (result == null)
                {
                    report?.Warning("YAPS", $"\"{skin.name}\" could not join the plug",
                        $"Its vertices are weighted to the plug's bone chain, but it could not be " +
                        $"baked ({failure}), so it will stay rigid while the rest bends.");
                    continue;
                }
                int patched = PatchExtra(plug, skin, result, slots, dir, report);
                if (patched > 0) { done += patched; meshes++; joined.Add((skin, result)); }
            }
            return done;
        }

        // The plug that has declared this renderer its own, if any. Used
        // both to keep one plug off another's mesh and, by the scanner, to
        // tell a carried mesh from a plug in its own right.
        public static YapsPlug OwnerPlugOf(Transform root, Renderer renderer)
        {
            if (root == null || renderer == null) return null;
            foreach (var other in root.GetComponentsInChildren<YapsPlug>(true))
            {
                if (other != null && other.Target == renderer) return other;
            }
            return null;
        }

        // One extra renderer's slots: this renderer's own bake, and the
        // component's knobs.
        //
        // Not MirrorToSlots, which copies the primary MATERIAL's values
        // wholesale. For another mesh that hands over the primary's bake
        // texture and vertex count, and the shader reads one mesh's vertices
        // out of another mesh's bake.
        static int PatchExtra(YapsPlug plug, SkinnedMeshRenderer skin, YapsBaker.Result result,
            List<int> slots, string dir, BridgeReport report)
        {
            var mats = skin.sharedMaterials;
            int done = 0;
            List<AnimationClip> clips = null;
            foreach (int i in slots)
            {
                if (i < 0 || i >= mats.Length || mats[i] == null) continue;
                var was = mats[i];
                bool ours = was.HasProperty("_YAPS_Bake") || IsGenerated(was);
                var shader = was.HasProperty("_YAPS_Bake")
                    ? was.shader
                    : YapsShaderPatcher.Patch(was, dir, report, out _, out _);
                if (shader == null)
                {
                    report?.Warning("YAPS", $"\"{was.name}\" keeps its own shader",
                        "The plug's vertices reach this material, but its shader could not be " +
                        "patched, so that part of the mesh will not bend with the rest.");
                    continue;
                }
                Material target;
                if (ours)
                {
                    target = was;
                    if (YapsShaderPatcher.IsStale(target)) YapsShaderPatcher.Refresh(target, dir, report);
                    else target.shader = shader;
                    YapsBaker.Apply(result, target, true);
                }
                else
                {
                    target = YapsBaker.Apply(result, was, shader, dir, true);
                    mats[i] = target;
                    RecordBakedSlot(plug, skin, i, was);
                    YapsSwapFollow.Follow(skin, i, was, target, report,
                        clips ??= YapsSwapFollow.RunnableClips(skin.transform));
                }
                WriteKnobs(plug, target);
                WritePlugWide(plug, target);
                // On, as the primary is. A copy switched off by hand stayed off
                // and rigid through every rebuild.
                target.SetFloat("_YAPS_Enabled", 1f);
                // The author's length override, which reached only the PRIMARY
                // materials. A carried mesh kept the measured length, so with an
                // override set the body ran one envelope and the collar another, every
                // threshold, taper and reach landing at a different depth per mesh.
                if (plug.lengthOverride > 0) target.SetFloat("_YAPS_Length", BakeLength(skin, target, plug.lengthOverride));
                done++;
            }
            if (done > 0) skin.sharedMaterials = mats;
            return done;
        }

        // Every submesh the chain moves, not just the one it moves most.
        //
        // A plug's vertices can span several materials, a whole avatar baked as
        // one plug being the clear case, and a submesh left unpatched stays
        // rigid while its neighbours bend, so the mesh tears along the seam.
        // Any real weight counts: one moving vertex is enough to tear it.
        //
        // heaviest: the slot whose triangles are weighted to the chain the
        // most, -1 when none is. One walk answers both, so the two cannot
        // disagree about which vertices count, and a bake reads the weights
        // once.
        public static List<int> SlotsWeightedTo(SkinnedMeshRenderer skin, Transform rootBone, out int heaviest)
        {
            var slots = new List<int>();
            heaviest = -1;
            if (skin == null || skin.sharedMesh == null || skin.bones == null || rootBone == null) return slots;
            var mesh = skin.sharedMesh;
            var weights = mesh.boneWeights;
            if (weights == null || weights.Length == 0) return slots;
            var chain = new HashSet<int>();
            for (int i = 0; i < skin.bones.Length; i++)
                if (skin.bones[i] != null && (skin.bones[i] == rootBone || skin.bones[i].IsChildOf(rootBone))) chain.Add(i);
            if (chain.Count == 0) return slots;
            float most = 0f;
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
                }
                if (total > 0.001f) slots.Add(sub);
                if (total > most) { most = total; heaviest = sub; }
            }
            return slots;
        }

        public static List<int> SlotsWeightedTo(SkinnedMeshRenderer skin, Transform rootBone) =>
            SlotsWeightedTo(skin, rootBone, out _);

        public static int SlotWeightedTo(SkinnedMeshRenderer skin, Transform rootBone)
        {
            SlotsWeightedTo(skin, rootBone, out int heaviest);
            return heaviest;
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
            p.bulgeFalloff = F("_YAPS_BulgeFalloff", 0f);
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
        // meshPath: where a plug that outlives the scene keeps its mesh. The
        // default is overwritten by every test plug, so a prefab saved around it
        // lost its mesh the next time one spawned.
        public static GameObject BuildTestPlug(Transform parent = null, bool select = true, float length = 0.25f,
            string meshPath = null)
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
            var mesh = CapsuleMesh(length, radius);
            // One asset, overwritten. GenerateUniqueAssetPath left a mesh
            // behind for every spawn, and the test plug is spawned to try
            // something and deleted straight after.
            if (string.IsNullOrEmpty(meshPath)) meshPath = OutputRoot + "/Test Plug/YAPS Test Plug Mesh.asset";
            EnsureFolder(Path.GetDirectoryName(meshPath).Replace('\\', '/'));
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);
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
            // Cross(B - A, C - A), and the first version had it the other way, so
            // backface culling threw away the surface you were looking at.
            for (int ring = 0; ring < along; ring++)
                for (int a = 0; a < around; a++)
                {
                    int nx = (a + 1) % around, here = ring * around, up = (ring + 1) * around;
                    tri.Add(here + a); tri.Add(up + nx); tri.Add(up + a);
                    tri.Add(here + a); tri.Add(here + nx); tri.Add(up + nx);
                }
            // A cap over the base, its own vertices so the edge stays hard. The tip
            // needs none: the last ring has radius 0. Without it the mesh is a tube
            // open at one end, which reads as a hole in the model.
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

        // The other plug whose bake this slot's material holds, if any. Its
        // baked-slot records count too: a slot it mirrored into holds a copy
        // that is neither its readout source nor its named slot, and missing
        // that let this plug re-bake the copy and delete the texture the other
        // plug's primary shares.
        static YapsPlug OwnerOfSlot(YapsPlug plug, Renderer renderer, int slot, Material material)
        {
            return AvatarRoot(plug.transform).GetComponentsInChildren<YapsPlug>(true).FirstOrDefault(p => p != plug
                && OwnsSlot(p, renderer, slot, material));
        }

        // Whose bake a slot holds. One rule for the bake, the inspector and the
        // material panel's write-back, or they disagree on which plug a slot is.
        public static bool OwnsSlot(YapsPlug p, Renderer r, int slot, Material m) =>
            p.Target == r && (p.readoutSource == m || p.materialSlot == slot)
            || p.bakedSlots.Any(b => b != null && b.slot == slot && Same(b.renderer, r, p));

        // The avatar or prop a part belongs to, else its top object. One answer
        // for every "which avatar" question: the scene root lets two avatars
        // parented under one container count each other's plugs, take each
        // other's meshes and share one output folder.
        public static Transform AvatarRoot(Transform t)
        {
            if (t == null) return null;
            var avatar = t.GetComponentInParent<CVRAvatar>(true);
            if (avatar != null) return avatar.transform;
            var prop = t.GetComponentInParent<CVRSpawnable>(true);
            return prop != null ? prop.transform : t.root;
        }

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
