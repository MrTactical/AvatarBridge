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

        // DPS's tracker: digit 9, intensity = length, at the base, with
        // VRCFury's trailing digits so C# protocol readers answer it alike.
        // No +0.003: see the socket ranges.
        public const float TrackerRange = 0.4906f;

        public class Outcome
        {
            public bool Ok;
            public string Message;
            public List<string> Notes = new List<string>();
            public Material Material;
            public float Length, Radius;
        }

        // A build writes the scene and files together, and leaving Play mode puts back only the
        // scene: the animations it repointed would then switch to copies the avatar no longer wears.
        public static bool Playing => EditorApplication.isPlayingOrWillChangePlaymode;
        public const string NotInPlayMode = "Leave Play mode first. Unity undoes a build's changes to the scene when Play mode ends, " +
                                            "but keeps the materials and animations it changed, so the avatar would come out half built.";

        // --- make this a plug ------------------------------------------------

        // Every readout comes off the mesh before the bake, so one that stops
        // short puts back the ones whose plugs still own their materials.
        public static Outcome Bake(YapsPlug plug)
        {
            if (Playing) return new Outcome { Message = NotInPlayMode };
            if (plug != null && plug.Target != null) YapsLock.Animate(plug.Target.transform);
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
            // The second copy off too: a bake over it measures a point.
            if (plug.dupFrom != null)
            {
                if (renderer is SkinnedMeshRenderer withCopy && YapsDuplicate.Is(withCopy.sharedMesh))
                {
                    withCopy.sharedMesh = plug.dupFrom;
                    YapsDebugOverlayBuilder.Replaced(withCopy, plug.dupFrom);
                }
                plug.dupFrom = null;
                EditorUtility.SetDirty(plug);
            }
            var filter = renderer.GetComponent<MeshFilter>();
            var unbounded = filter != null ? filter.sharedMesh : null;
            // Every clip that can run, not only the ones WireSize may write to:
            // the box has to hold whatever resizes the bone.
            var top = AvatarRoot(plug.transform);
            var animRoot = YapsSwapFollow.AnimationRootOf(top);
            var sizeBones = SizeBones(plug.rootBone, animRoot);
            // What ChilloutVR ships, not the Animator's slot.
            var cvr = top.GetComponentInParent<CVRAvatar>(true);
            RuntimeAnimatorController shipped = cvr == null ? null
                : cvr.overrides != null ? cvr.overrides : cvr.avatarSettings?.baseController;
            var rest = YapsRestPose.Apply(animRoot, shipped, sizeBones);
            if (rest == null)
            {
                o.Notes.Add("The size its slider starts at could not be read, so the bake takes the size the scene shows. " +
                            "If this plug has a size slider, check it at rest in game.");
            }
            else if (rest.Count > 0)
            {
                o.Notes.Add($"Set the plug to the size its slider starts at ({string.Join(", ", rest)}), so the bake and " +
                            "the length other sockets read match the plug as it loads. Undo puts it back; Bake again after.");
            }
            float size = YapsBaker.LargestSize(YapsSwapFollow.RunnableClips(top), sizeBones);
            var result = YapsBaker.Bake(renderer, chainRoot, dir, report, out string failure,
                flipAxis: plug.flipAxis, sizeScale: size, keepPixels: plug.twoAtOnce);
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
                // Not the plain-mesh branch: it would take every body material.
                int lost = plug.bakedSlots.Count(b => b != null && b.renderer == null);
                if (lost > 0)
                    o.Notes.Add($"{BridgeElements.Count(lost, "baked slot record names", "baked slot records name")} no mesh, or a mesh since deleted, so they " +
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
                alsoSlots = SlotsWeightedTo(skinned, result.Root, out slot, climb: true);
            }
            else if (slot < 0)
            {
                // A plain mesh bends whole, so every one of its materials
                // carries part of the plug.
                for (int i = 0; i < mats.Length; i++) alsoSlots.Add(i);
            }
            if (slot < 0) slot = 0;
            string splitKey = null;
            // Another shaft's bake in this slot: one material holds one bend, so
            // this plug moves to a slot of its own on a copy.
            if (renderer is SkinnedMeshRenderer shared && mats[slot] != null && mats[slot].HasProperty("_YAPS_Bake"))
            {
                var owner = OwnerOfSlot(plug, renderer, slot, mats[slot]);
                var mine = YapsSlotSplit.Mask(shared, result.Root);
                if (owner != null && !YapsSlotSplit.SameShaft(mine,
                        YapsSlotSplit.Mask(shared, owner.rootBone != null ? owner.rootBone : owner.transform)))
                {
                    // The pre-bake original, so this is a fresh bake: copying the
                    // other plug's material let the re-bake delete its texture.
                    var before = owner.bakedSlots.FirstOrDefault(b => b != null && b.slot == slot
                                     && Same(b.renderer, renderer, owner))?.was ?? owner.bakedFrom;
                    string copyPath = null;
                    if (before == null)
                    {
                        // Nothing recorded (baked in place): a copy with no bake,
                        // so the re-bake path cannot delete the other's texture.
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
                        // No materialSlot written: that author override stops
                        // baking every reached slot, and the next bake finds the
                        // new slot by its triangles anyway.
                        if (plug.converted)
                        {
                            // Its record follows its triangles, or the next
                            // re-bake starts from the slot it just left.
                            foreach (var b in plug.bakedSlots)
                                if (b != null && b.slot == slot && b.renderer == renderer) b.slot = added;
                            alsoSlots = RecordedSlots(plug, renderer);
                        }
                        else alsoSlots = SlotsWeightedTo(shared, result.Root, out _, climb: true);
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
            // On the mesh as split, before the patch: the copy's code is a
            // shader of its own.
            int copyStart = plug.twoAtOnce ? SecondCopy(plug, renderer, slot, alsoSlots, result, dir, o) : 0;

            // Legacy plugs upgrade in place: TPS, SPS and Poiyomi's DPS switch the
            // old deform off, Raliv's DPS (no switch) moves to Simple Lit. Values
            // are carried either way.
            var legacy = source.HasProperty("_YAPS_Bake") ? YapsLegacyMap.Origin.None
                : YapsLegacyMap.Detect(source, out _);
            bool simpleLit = legacy == YapsLegacyMap.Origin.DPS && !HasDpsSwitch(source);
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
                shader = simpleLit ? null
                    : YapsShaderPatcher.Patch(source, report, out refusal, out _, allowSps: legacy == YapsLegacyMap.Origin.SPS,
                        duplicate: copyStart > 0, roles: YapsShaderPatcher.Roles.Plug);
                if (shader == null)
                {
                    var plain = OnFallback(source, out string why);
                    if (plain == null) { o.Message = "could not patch the shader: " + refusal + "; and " + why; return o; }
                    string host = plain.shader.name.Replace('/', ' ');
                    shader = YapsShaderPatcher.Patch(plain, report, out string plainRefusal, out _, duplicate: copyStart > 0,
                        roles: YapsShaderPatcher.Roles.Plug);
                    if (shader == null) { o.Message = "could not patch the shader: " + refusal + $"; and {host} refused too: " + plainRefusal; return o; }
                    o.Notes.Add(simpleLit
                        ? "A DPS shader has no switch for its own deform, so the plug now wears YAPS Simple Lit " +
                          "with its colour, albedo, normal map, metallic and smoothness carried over. Its original " +
                          "material is untouched."
                        : plain.shader.name == StandardName
                        ? "Unity's Standard has no source to patch, so the plug now wears YAPS Standard, the same " +
                          "shader from Unity's published source, with every setting carried over. Its original " +
                          "material is untouched."
                        : $"\"{source.shader.name}\" could not be patched ({refusal}), so the plug now wears " +
                          "YAPS Simple Lit with its colour, albedo, normal map, metallic and smoothness carried " +
                          "over. Its original material is untouched. Put a shader with source on it (Poiyomi, " +
                          "for one) and re-bake if you need more than that.");
                    source = plain;
                }
            }
            if (shader == null) { o.Message = "could not patch the shader: " + refusal; return o; }

            // A generated material is never an original: Remove's fallback leaves
            // the clone on the source shader, and cloning it again buried the real
            // original deeper each round. Re-patch it in place.
            bool oursAlready = IsGenerated(source);
            if (oursAlready && !source.HasProperty("_YAPS_Bake"))
            {
                YapsShaderPatcher.Wear(source, shader);
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
                // Toggles assigning this slot must follow the bake, or the
                // animator hands back the unbaked material.
                YapsSwapFollow.Follow(renderer, slot, original, patched, report, clips);
            }
            else
            {
                // Re-bake: drop the replaced texture (12 MB per click otherwise),
                // and the shader when its patch is out of date.
                if (YapsShaderPatcher.IsStale(patched)) YapsShaderPatcher.Refresh(patched, report);
                var previous = patched.GetTexture("_YAPS_Bake");
                // THE SAME CALL A FRESH BAKE MAKES, never a hand-copied subset: one
                // missed _YAPS_BakeScale and _YAPS_BakeGirth, so a re-baked plug
                // kept the last clip's size and deformed differently.
                YapsBaker.Apply(result, patched, result.FromSkinnedMesh);
                string old = previous != null && previous != result.Bake
                    ? AssetDatabase.GetAssetPath(previous) : null;
                if (!string.IsNullOrEmpty(old) && old.StartsWith(OutputRoot + "/", System.StringComparison.Ordinal))
                {
                    AssetDatabase.DeleteAsset(old);
                }
            }
            // The copy decides which shader is current, so one added or dropped
            // on a re-bake leaves the patch behind it.
            patched.SetFloat("_YAPS_CopyStart", copyStart);
            if (YapsShaderPatcher.IsStale(patched)) YapsShaderPatcher.Refresh(patched, report);
            YapsShaderPatcher.Wear(patched, YapsShaderPatcher.WithRole(patched, YapsShaderPatcher.Roles.Plug, report) ?? patched.shader);
            if (plug.lengthOverride > 0) patched.SetFloat("_YAPS_Length", BakeLength(renderer, patched, plug.lengthOverride));
            patched.SetFloat("_YAPS_Enabled", 1f);
            WritePlugWide(plug, patched);
            // Past four, entries are dropped; the inspector list does not limit them.
            int over = YapsTags.Dropped(plug.answers) + YapsTags.Dropped(plug.refuses);
            if (over > 0)
            {
                o.Notes.Add($"{BridgeElements.Count(over, "tag", "tags")} past the first {YapsTags.PlugSlots} on each list were "
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
                o.Notes.Add($"Upgraded from {legacy}: {BridgeElements.Count(carried.Count, "setting", "settings")} carried" +
                            (unmapped.Count > 0 ? $"; no YAPS counterpart for {string.Join(", ", unmapped)}" : "") + ".");
                if (legacy == YapsLegacyMap.Origin.SPS && !original.HasProperty("_SPS_Enabled"))
                    o.Notes.Add(YapsLegacyMap.SpsPrebuilt);
            }
            WriteKnobs(plug, patched);
            EditorUtility.SetDirty(patched);
            o.Material = patched;

            // After the knobs, so the readout copies the values the plug
            // actually ended up with rather than the ones it started from.
            YapsDebugOverlayBuilder.Apply(plug, result, patched, report);

            // The other reached slots carry the SAME deform, values copied from the
            // primary (one bake serves every submesh), or they tear apart.
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
            // The report is not shown from here, so the notes are. A coarse
            // first mesh stops every mesh; a coarse second one stops alone.
            if (result.PortalSpan < 0f) o.Notes.Add(YapsBaker.CoarseNote);
            else
                foreach (var (r, baked) in meshes)
                    if (baked.PortalSpan < 0f) o.Notes.Add($"\"{r.name}\" stops at a portal entrance. {YapsBaker.CoarseExtraNote}");
            if (alsoMeshes > 0 && copyStart > 0)
                o.Notes.Add("The second copy is this mesh's alone, so the parts on the other meshes stay with the first.");
            if (alsoMeshes > 0)
            {
                o.Notes.Add($"{BridgeElements.Count(alsoMeshes, "other mesh on this avatar is weighted", "other meshes on this avatar are weighted")} to the plug's bone " +
                            $"chain, so they were baked into it too ({BridgeElements.Count(alsoMats, "material", "materials")}), sharing this " +
                            "plug's frame and length. Left out they would have stayed rigid while the rest bent.");
            }

            // Every OTHER material a toggle can put in that slot, or the plug goes
            // rigid while it is on. Last, so each copies its slot's final values.
            // Two plugs on one mesh each get animated values of their own, before
            // a variant copies the packs.
            foreach (var (meshRenderer, _) in meshes)
            {
                int dropped = YapsBaker.ShareRenderer(meshRenderer);
                if (dropped > 0)
                    o.Notes.Add($"\"{meshRenderer.name}\" carries two plugs, which share {YapsBaker.MaxShapes} shape slots: " +
                                $"{BridgeElements.Count(dropped, "shape", "shapes")} of the second do not bend it.");
            }
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
                string toggled = YapsToggles.EnsurePlugToggle(plug, avatarForToggle, patched, YapsToggles.MenuLabelFor(plug));
                if (toggled != null) o.Notes.Add(toggled);
                // A second row for whose sockets it answers, off by default.
                string own = YapsToggles.EnsureSelfToggle(plug, avatarForToggle, patched,
                    YapsToggles.MenuLabelFor(plug) + " own sockets");
                if (own != null) o.Notes.Add(own);
            }

            o.Ok = true;
            o.Message = $"Baked \"{renderer.name}\": {o.Length:0.###} m, {result.VertexCount} vertices, " +
                        $"{BridgeElements.Count(result.Shapes.Count, "shape", "shapes")}, material \"{patched.name}\".";
            o.Notes.Add(YapsPropBuilder.IsProp(AvatarRoot(plug.transform).gameObject)
                ? "This plug is on a prop; make it a prop again so the prop carries the new bake."
                : "This plug finds sockets through the screen atlas, and by their marker lights where the " +
                  "atlas cannot answer: a view too small to hold it, or content that only has lights.");
            if (result.FromSkinnedMesh) o.Notes.Add("Skinned mesh: frame recovered per vertex.");
            return o;
        }

        // The plug's second copy, its first vertex, or 0 with a note saying why
        // there is none.
        static int SecondCopy(YapsPlug plug, Renderer renderer, int slot, List<int> alsoSlots,
            YapsBaker.Result result, string dir, Outcome o)
        {
            string why;
            int start = -1;
            if (!(renderer is SkinnedMeshRenderer skin) || !result.FromSkinnedMesh)
                why = "it needs a skinned mesh";
            // Its copies sit past every vertex another plug's bake describes.
            else if (AvatarRoot(plug.transform).GetComponentsInChildren<YapsPlug>(true).Any(p => p != plug && p.Target == renderer))
                why = "another plug shares its mesh";
            // Copied from one slot, the copy would lack whatever the others hold.
            else if (alsoSlots.Count > 0)
                why = "its triangles span several materials";
            else
            {
                var under = skin.sharedMesh;
                start = YapsDuplicate.Add(skin, slot, result, dir, out why);
                if (start > 0)
                {
                    plug.dupFrom = under;
                    EditorUtility.SetDirty(plug);
                    o.Notes.Add($"Two at once: a second copy of the plug, {skin.sharedMesh.vertexCount - start} more vertices, " +
                                "drawn only while a second socket or a portal's exit is in reach.");
                    return start;
                }
            }
            o.Notes.Add($"Two at once is ticked but there is no second copy: {why}.");
            return 0;
        }

        // The switches every material of one plug shares. A carried mesh left
        // on its last bake's answers a socket the rest refuses, and tears there.
        static void WritePlugWide(YapsPlug plug, Material m)
        {
            // 1 when the avatar has its own sockets, so ownership is checked; -1
            // turns own-body exclusion off. Never hardcode -1: the wearer's own
            // sockets are always nearest, so the plug would never see anyone else's.
            var ownAvatar = plug.GetComponentInParent<CVRAvatar>(true);
            bool ownSockets = ownAvatar != null
                              && ownAvatar.GetComponentsInChildren<YapsSocket>(true).Length > 0;
            m.SetFloat("_YAPS_SelfTag", ownSockets ? 1f : -1f);

            // Only the atlas reads tags: a contact cannot see them.
            m.SetVector("_YAPS_TagInclude", YapsTags.Patterns(plug.answers));
            // Where the second plug on a shared mesh reads them.
            m.SetVector("_YAPS_TagInclude2", YapsTags.Patterns(plug.answers));
            m.SetVector("_YAPS_TagExclude", YapsTags.Patterns(plug.refuses));
            // Build adds the writers, clear and grab, so read the atlas too: with
            // the flag off a plug silently falls back to the lights.
            m.SetFloat("_YAPS_UseAtlas", 1f);
        }

        // Edits the user's clips, adding a curve beside each it mirrors, and
        // says so. Idempotent.
        static void WireSize(YapsPlug plug, List<(Renderer renderer, YapsBaker.Result result)> meshes, Outcome o)
        {
            var top = AvatarRoot(plug.transform);
            // NOT the Animator's slot: ChilloutVR uploads avatar.overrides, else
            // avatarSettings.baseController. The Animator's slot is a generated
            // override, often empty before a build, and mirrored nothing.
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
            YapsCurveMirror.Purged = false;
            var own = meshes.Where(m => m.renderer == plug.Target).Select(m => m.result).FirstOrDefault();
            if (own != null) { plug.bakedShapes = new List<string>(own.Shapes); EditorUtility.SetDirty(plug); }
            foreach (var (renderer, result) in meshes)
            {
                string rendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, animRoot);
                // This plug's lane on this mesh, from its own bake's material.
                var mine = renderer.sharedMaterials.FirstOrDefault(m => m != null && m.HasProperty("_YAPS_Bake")
                                                                        && m.GetTexture("_YAPS_Bake") == result.Bake);
                int lane = YapsBaker.LaneOf(mine);
                var baked = lane > 0 ? result.Shapes.Take(Mathf.RoundToInt(mine.GetFloat("_YAPS_ShapeCount"))).ToList() : result.Shapes;
                // The first plug on this mesh, whose old curves a pre-lane build may have overwritten.
                var first = lane > 0 && renderer == plug.Target
                    ? top.GetComponentsInChildren<YapsPlug>(true).FirstOrDefault(p => p != plug && p.Target == renderer && p.bakedShapes.Count > 0)
                    : null;
                if (baked.Count > 0)
                {
                    shapes += YapsCurveMirror.MirrorShapes(clips, rendererPath, renderer, baked,
                        result.MovingShapes, missed, lane, first?.bakedShapes);
                }
                // Somebody animating the component's own checkbox meant the
                // deform, so give them the deform.
                switched += YapsCurveMirror.MirrorEnabled(clips, plugPath, typeof(YapsPlug),
                    rendererPath, renderer, YapsBaker.SwitchName("_YAPS_Enabled", lane));
                scaled += YapsCurveMirror.MirrorBoneScale(clips, bones, rendererPath, renderer, result.Rotation, lane,
                    first != null && first.rootBone != plug.rootBone);
            }
            if (YapsCurveMirror.Purged)
                o.Notes.Add("An earlier build had written this plug's size curves under the other plug's name on this mesh; " +
                            "those are gone. Bake the other plug on this mesh again so its own are written back.");
            if (switched > 0)
            {
                o.Notes.Add($"{BridgeElements.Count(switched, "clip animates", "clips animate")} this component's own Enabled field, which does " +
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
                o.Notes.Add($"Wired the plug's size into {BridgeElements.Count(shapes + scaled, "curve", "curves")} of the avatar's own clips" +
                            (shapes > 0 ? $": {BridgeElements.Count(shapes, "shape curve", "shape curves")}" : "") +
                            (scaled > 0 ? $"{(shapes > 0 ? "," : ":")} {BridgeElements.Count(scaled, "bone scale curve", "bone scale curves")}" : "") +
                            ". A curve was added beside each, so a size slider or hyper toggle reaches the shader too.");
            }
            if (missed.Count > 0)
            {
                o.Notes.Add($"{BridgeElements.Count(missed.Count, "animated shape that moves the plug is", "animated shapes that move the plug are")} not in the bake " +
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

        // The old system's deform must not run beside YAPS. TPS has a switch on
        // the material. A finished SPS bake leaves none, so the SPS check is
        // for older bakes, and any keyword carrying the system's name goes too.
        public static void SwitchOffLegacyDeform(Material m, YapsLegacyMap.Origin legacy)
        {
            string flag = legacy == YapsLegacyMap.Origin.TPS ? "_TPS_PenetratorEnabled"
                        : legacy == YapsLegacyMap.Origin.SPS ? "_SPS_Enabled"
                        : legacy == YapsLegacyMap.Origin.DPS ? "_PenetratorEnabled" : null;
            if (flag != null && m.HasProperty(flag)) m.SetFloat(flag, 0f);
            string tag = legacy.ToString();
            foreach (var keyword in m.shaderKeywords)
            {
                if (keyword.IndexOf(tag, System.StringComparison.OrdinalIgnoreCase) >= 0) m.DisableKeyword(keyword);
            }
        }

        // Raliv's DPS shader has no switch for its deform, so its plug moves to Simple Lit.
        // Poiyomi's copy of it has one and keeps its own shader: off on the material, and the
        // patch turns off the copy a locked Poiyomi froze on.
        public static bool HasDpsSwitch(Material m) => m != null && m.HasProperty("_PenetratorEnabled");

        public const string StandardName = "YAPS/Standard";

        // The same material on Simple Lit, in memory. Property names match Standard's.
        public static Material OnSimpleLit(Material source, out string why)
        {
            var m = OnShader(source, SimpleLitName, "YapsSimpleLit.shader", out why);
            if (m != null) CarryEmission(source, m);
            return m;
        }

        // Poiyomi and lilToon default their emission colour to white and keep it behind a
        // switch, Poiyomi with a strength on top. Simple Lit has neither, so a plug moved onto
        // it glowed white all over.
        static void CarryEmission(Material source, Material m)
        {
            foreach (var flag in new[] { "_EnableEmission", "_UseEmission" })
            {
                if (source.HasProperty(flag) && source.GetFloat(flag) < 0.5f)
                {
                    m.SetColor("_EmissionColor", Color.black);
                    return;
                }
            }
            if (source.HasProperty("_EmissionStrength") && source.HasProperty("_EmissionColor"))
            {
                m.SetColor("_EmissionColor", source.GetColor("_EmissionColor") * source.GetFloat("_EmissionStrength"));
            }
        }

        // A shader the patcher refused, swapped for one it takes. Standard is built in with no
        // source on disk, so it goes to its own source, every map kept. The rest go to Simple Lit.
        public static Material OnFallback(Material source, out string why) =>
            source.shader != null && source.shader.name == "Standard"
                ? OnShader(source, StandardName, "YapsStandard.shader", out why)
                : OnSimpleLit(source, out why);

        static Material OnShader(Material source, string name, string file, out string why)
        {
            why = null;
            var shader = Shader.Find(name);
            if (shader == null)
            {
                why = $"{name.Replace('/', ' ')} is not in the project (Editor/Yaps/{file})";
                return null;
            }
            var m = new Material(source) { name = source.name };
            YapsShaderPatcher.Wear(m, shader);
            YapsBaker.StandsInFor.Add(m, source);
            return m;
        }

        // In world metres. The material holds bake units: metres for skinned,
        // the object's own units for a plain mesh.
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

        // Every YapsPlug wearing this material takes the values back, except
        // where another plug owns the slot (one panel rewrote the other's knobs).
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
            // Skinned: the measured frame. Plain: the mesh's origin and +Z, which
            // is not the plug object when the component sits above the mesh.
            bool skinned = result != null && result.FromSkinnedMesh;
            var mesh = plug.Target != null ? plug.Target.transform : plug.transform;
            AnnouncePlug(plug.transform, skinned ? result.Origin : mesh.position, skinned ? result.Rotation : mesh.rotation,
                length, radius, plug.emitTipLight, plug.emitPointers,
                plug.toyModLength ? OutputRoot + "/" + Sanitise(AvatarRoot(plug.transform).name) : null);
        }

        // Announces a plug to every socket family: tracker light at the base,
        // tip, root and width pointers. Null frame means the parent's transform.
        public static GameObject AnnouncePlug(Transform parent, Vector3? worldOrigin, Quaternion? worldRotation,
            float length, float radius, bool tipLight = true, bool pointers = true, string toyLengthDir = null)
        {
            var old = parent.Find(MarkersName);
            // Reused or deleted only when this plug alone draws it: a duplicated avatar shares it.
            var oldToy = old != null ? old.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh)
                .FirstOrDefault(x => x != null && AssetDatabase.GetAssetPath(x).StartsWith(OutputRoot + "/", System.StringComparison.Ordinal)
                                     && Object.FindObjectsOfType<MeshFilter>(true).Count(f => f.sharedMesh == x) == 1) : null;
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
                if (toyLengthDir != null) { ToyModLength(l.transform, length, toyLengthDir, oldToy); oldToy = null; }
            }
            if (oldToy != null) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(oldToy));
            if (pointers)
            {
                // Tip and root as separate points. Only names not already announced;
                // a second TPS_Pen_Penetrating would have a trigger reporting either.
                var have = new HashSet<string>(parent.GetComponentsInChildren<CVRPointer>(true)
                    .Where(p => p != null && p.transform != m && !p.transform.IsChildOf(m))
                    .Select(p => p.type));
                // Metres: a raw local offset under a 100x bone put the tip a
                // hundred lengths out.
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

        // Toy mods climb from the tip light to the first mesh and take its
        // furthest +Z vertex as the length; on an avatar that reaches a hat or
        // the body. This mesh under the light is found first. Inactive, no
        // material; a hundred vertices, since the mod warns about fewer.
        static void ToyModLength(Transform light, float length, string dir, Mesh reuse)
        {
            var go = new GameObject("Length for toy mods");
            go.transform.SetParent(light, false);
            go.SetActive(false);
            float z = length / Mathf.Max(go.transform.TransformVector(Vector3.forward).magnitude, 1e-6f);
            var mesh = reuse != null ? reuse : new Mesh();
            mesh.Clear();
            mesh.vertices = Enumerable.Range(0, 100).Select(i => new Vector3(0f, 0f, z * i / 99f)).ToArray();
            mesh.RecalculateBounds();
            if (reuse == null)
            {
                EnsureFolder(dir);
                mesh.name = light.parent.parent.name + " toy length";
                // Claimed, not made unique: a unique name left a copy per reconvert.
                AssetDatabase.CreateAsset(mesh, OutputAssetPaths.Claim($"{dir}/{Sanitise(mesh.name)}.asset"));
            }
            else EditorUtility.SetDirty(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().enabled = false;
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
        // Whether the mesh origin IS the socket: only then does the shader open
        // it, since depth measures from the origin. Skinned socket meshes are
        // unverified (identity object matrix, yaps_socket.cginc).
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

        // Does the wearer's own plug rest on this socket? That is why
        // self-exclusion exists: otherwise the socket reads always full.
        // Measured at rest because the shader only guesses by nearest hip,
        // wrong for a hand socket. From the tracker light, which the shader
        // measures from, never the plug's transform.
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

        // A socket's baked material, told from a plug's. Both carry _YAPS_Bake,
        // and Strength can be 0 on either, so the slot this socket recorded
        // counts first.
        static bool IsSocketMaterial(Material m, YapsSocket socket, Renderer renderer, int slot)
        {
            if (m == null || !m.HasProperty("_YAPS_Enabled") || m.GetFloat("_YAPS_Enabled") > 0f) return false;
            bool recorded = socket.bakedRenderer == renderer ? socket.bakedSlot == slot
                : socket.bakedRenderer == null && socket.bakedFrom != null;
            return recorded || m.HasProperty("_YAPS_SocketPower") && m.GetFloat("_YAPS_SocketPower") > 0f;
        }

        // The markers alone. The owner layer must still reach any writer added
        // or renamed: a portal writer it misses publishes for nobody.
        public static void RebuildMarkers(YapsSocket socket)
        {
            if (socket == null || Playing) return;
            YapsSocketBuilder.Build(socket);
            YapsOwner.Wire(socket.GetComponentInParent<CVRAvatar>(true));
        }

        // Everything Build does for one socket, shared by the window and the
        // inspector's button.
        public static List<string> BuildSocket(YapsSocket socket)
        {
            var lines = new List<string>();
            if (socket == null) return lines;
            if (Playing) { lines.Add("✗ " + NotInPlayMode); return lines; }
            Undo.RegisterFullObjectHierarchyUndo(socket.gameObject, "Build YAPS socket");
            string renamed = YapsToggles.RenameToLabel(socket, socket.GetComponentInParent<CVRAvatar>(true));
            if (renamed != null) lines.Add($"✓ {renamed}");
            YapsSocketBuilder.Build(socket);
            string capped = YapsSocketBuilder.LightCapNote(socket);
            if (capped != null) lines.Add($"✓ {YapsToggles.LabelFor(socket)}: {capped}");
            string portal = YapsSocketBuilder.PortalNote(socket);
            if (portal != null) lines.Add($"{portal.Substring(0, 2)}{YapsToggles.LabelFor(socket)}: {portal.Substring(2)}");
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
            if (Playing) return new Outcome { Message = NotInPlayMode };
            int before = YapsToggles.Edits;
            var o = Bake(plug);
            var avatar = plug != null ? plug.GetComponentInParent<CVRAvatar>(true) : null;
            string menu = YapsToggles.RefreshMenuAnimator(avatar, before);
            if (menu != null) o.Notes.Add(menu);
            // The contact channel too, as the window's Build does, or the two
            // buttons differ. It replaces its own wiring, so per plug is safe.
            if (avatar != null && o.Ok)
            {
                o.Notes.AddRange(YapsNativeChannel.Build(avatar));
            }
            return o;
        }

        // Which submesh the socket IS, not slot 0 (which patched the wrong part
        // and the shapes never moved). The submesh owning most of the shapes'
        // moved vertices wins. Only submeshes with a material: Unity allows
        // fewer materials than submeshes.
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

        // Keyed by the recorded renderer and slot: a socket whose mesh is set
        // to None no longer names it.
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
                // Swap keys still point at the baked material; left, a toggle
                // puts the deform back beside any reactions on the same shapes.
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
            if (renderer != null) YapsLock.Animate(renderer.transform);
            var stages = socket.shapes.Where(s => s != null && !string.IsNullOrEmpty(s.blendshape)).ToList();
            // An empty list is an instruction to tear down, or the layer,
            // parameter, contact and baked material are left behind.
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
            // Other meshes open through the animator (see MeshIsTheSocket).
            // Unbaked first, or an older shader bake drives the shapes twice.
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
            // A material carries ONE bake: on a plug's material (a ring on the
            // plug mesh) the plug would stop deforming. Drive by contact instead.
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
                // Refresh a stale SHADER too, or the rebuild keeps old deform code.
                // Values survive the swap; new properties arrive at defaults.
                if (YapsShaderPatcher.IsStale(material)) YapsShaderPatcher.Refresh(material, report);
                var previous = material.GetTexture("_YAPS_Bake");
                // The fresh bake's own call: copying fields by hand left stale ones.
                YapsBaker.Apply(result, material, result.FromSkinnedMesh);
                string old = previous != null && previous != result.Bake ? AssetDatabase.GetAssetPath(previous) : null;
                if (!string.IsNullOrEmpty(old) && old.StartsWith(OutputRoot + "/", System.StringComparison.Ordinal))
                    AssetDatabase.DeleteAsset(old);
            }
            else
            {
                var shader = YapsShaderPatcher.Patch(source, report, out string refusal, out _,
                    roles: YapsShaderPatcher.Roles.Socket);
                if (shader == null)
                {
                    var plain = OnFallback(source, out string why);
                    shader = plain != null ? YapsShaderPatcher.Patch(plain, report, out refusal, out _,
                        roles: YapsShaderPatcher.Roles.Socket) : null;
                    if (shader == null) return $"✗ {socket.name}: could not patch the shader: {refusal}";
                    source = plain;
                }
                material = YapsBaker.Apply(result, source, shader, dir, result.FromSkinnedMesh);
                material.SetFloat("_YAPS_Enabled", 0f);
                if (socket.bakedFrom == null) { socket.bakedFrom = mats[slot]; EditorUtility.SetDirty(socket); }
                RecordSocketSlot(socket, renderer, slot);
                var wasSocket = mats[slot];
                // The body sits outside the socket's undo: Ctrl+Z left the patched
                // material with nothing for Remove to restore.
                Undo.RecordObject(renderer, "Build YAPS socket");
                mats[slot] = material;
                renderer.sharedMaterials = mats;
                // The real slot, never 0: the repair is keyed by slot, and a
                // non-zero one was left unrepaired.
                YapsSwapFollow.Follow(renderer, slot, wasSocket, material, report);
            }

            // Stage k is baked shape k, not row k: the bake drops missing and
            // repeated names.
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
            return $"✓ {socket.name}: {BridgeElements.Count(result.Shapes.Count, "shape", "shapes")} staged on \"{renderer.name}\"";
        }

        // Whether this socket hangs off an arm or a leg. Ownership is the hip
        // nearest the mesh origin, so a hand in someone's lap answers with
        // THEIR hip and ignores their plug. No shader-side fix holds (a baked
        // offset is true in one pose; skinned meshes have no object matrix),
        // so exclusion is dropped here: the wearer's own plug opening the
        // socket is the lesser error.
        public static bool RidesAMovingLimb(Transform socket)
        {
            var avatar = socket != null ? socket.GetComponentInParent<CVRAvatar>(true) : null;
            var animator = avatar != null ? avatar.GetComponent<Animator>() : null;
            if (animator == null || !animator.isHuman) return false;

            // Not the head: it stays over the body, and a mouth socket wants
            // its wearer's plug excluded.
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

        // The YAPS window's Build, for the window and the converter alike, so a
        // reconvert matches the button. `only` limits it to those roots, for the
        // upgrade beside SPS, which must not rebuild what the SPS route made.
        public static List<string> BuildAvatar(GameObject target, out string headline, ICollection<Transform> only = null)
        {
            // Adopt first, so bare converted sockets and plugs get components.
            int adopted = 0;
            var scan = YapsScanner.Scan(target);
            foreach (var f in scan.Plugs.Concat(scan.Sockets))
            {
                if (f.Root == null || f.CarriedBy != null) continue;
                if (f.Root.GetComponent<YapsSocket>() != null || f.Root.GetComponent<YapsPlug>() != null) continue;
                Undo.RegisterFullObjectHierarchyUndo(f.Root.gameObject, "Adopt YAPS");
                Adopt(f);
                adopted++;
            }
            int plugsOk = 0, plugsTried = 0, socketsBuilt = 0;
            var lines = new List<string>();
            if (adopted > 0) lines.Add($"made {adopted} editable");
            int edits = YapsToggles.Edits;
            foreach (var p in target.GetComponentsInChildren<YapsPlug>(true))
            {
                if (only != null && !only.Contains(p.transform)) continue;
                plugsTried++;
                var o = Bake(p);
                if (o.Ok) plugsOk++;
                lines.Add((o.Ok ? "✓ " : "✗ ") + o.Message);
                lines.AddRange(o.Notes.Where(n => n.Contains("menu toggle") || n.Contains("Wired")));
            }
            // The plugs' toggles into the menu animator, once. Sockets do their own.
            string menu = YapsToggles.RefreshMenuAnimator(target.GetComponentInChildren<CVRAvatar>(true), edits);
            if (menu != null) lines.Add(menu);
            foreach (var s in target.GetComponentsInChildren<YapsSocket>(true))
            {
                if (only != null && !only.Contains(s.transform)) continue;
                socketsBuilt++;
                lines.AddRange(BuildSocket(s));
            }
            // The surface the writers publish to, or nothing grabs them.
            if (YapsAtlas.AddClear(target.transform) != null
                && YapsAtlas.AddGrab(target.transform) != null)
            {
                lines.Add("Added the screen surface plugs read each other through.");
            }

            // Last, and once: the channel reads the frames the bakes just
            // measured, and it replaces its own wiring rather than stacking.
            lines.AddRange(YapsNativeChannel.Build(target.GetComponentInChildren<CVRAvatar>(true)));
            headline = $"Built {plugsOk} of {plugsTried} plug{(plugsTried == 1 ? "" : "s")} and " +
                       $"{socketsBuilt} socket{(socketsBuilt == 1 ? "" : "s")}.";
            return lines;
        }

        // Puts the authoring component on a found plug or socket that has none.
        public static void Adopt(YapsScanner.Found f)
        {
            if (f.Root == null) return;
            // Never a mesh another plug carries: adopting it claims the mesh, the
            // carrier lets go, and Build recreates a component the user deleted.
            if (f.CarriedBy != null) return;
            if (f.Kind == YapsScanner.Kind.Socket)
            {
                // Shape names are not on the material, so the user fills the
                // rows. An empty list, not null: it still carries the power over.
                AdoptSocket(f.Root, f.Renderer, f.Material, new string[0]);
            }
            else
            {
                AdoptPlug(f.Root, f.Renderer, f.MaterialSlot, f.Material, null);
            }
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
                    var again = YapsShaderPatcher.Patch(was, report, out _, out _, roles: YapsShaderPatcher.Roles.Plug);
                    if (again != null)
                    {
                        YapsShaderPatcher.Wear(was, again);
                        CopyYapsProperties(primary, was);
                        EditorUtility.SetDirty(was);
                        done++;
                    }
                    continue;
                }
                if (was.HasProperty("_YAPS_Bake"))
                {
                    // Generated earlier: refresh, and do NOT record it as the original,
                    // or Remove "restores" a patched material.
                    target = was;
                    if (YapsShaderPatcher.IsStale(target)) YapsShaderPatcher.Refresh(target, report);
                    YapsShaderPatcher.Wear(target, YapsShaderPatcher.WithRole(target, YapsShaderPatcher.Roles.Plug, report) ?? target.shader);
                    CopyYapsProperties(primary, target);
                    EditorUtility.SetDirty(target);
                    done++;
                    continue;
                }
                {
                    var shader = YapsShaderPatcher.Patch(was, report, out string refusal, out _,
                        roles: YapsShaderPatcher.Roles.Plug);
                    if (shader == null)
                    {
                        // Say so: a silent skip is an unexplained tear.
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

        // Every _YAPS_ value, so submeshes bend alike. By name, never a list
        // kept here, which rots. missing: values `to` has no room for.
        public static void CopyYapsProperties(Material from, Material to, List<string> missing = null)
        {
            if (from == null || to == null) return;
            bool wasDuplicate = YapsShaderPatcher.Duplicate(to);
            var shader = from.shader;
            for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
            {
                string name = ShaderUtil.GetPropertyName(shader, i);
                if (!name.StartsWith("_YAPS_", System.StringComparison.Ordinal)) continue;
                // The patcher's markers carry their meaning in the description or
                // the shader default, so the material's value is nothing anyone reads.
                if (name == YapsShaderGUI.OriginalEditorProperty || name == YapsShaderPatcher.SourceShaderProperty
                    || name == YapsShaderPatcher.SourceGuidProperty || name == YapsShaderPatcher.RolesProperty) continue;
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
            // A swapped-in look of a plug with a second copy draws the copy too,
            // and only the copy's own shader hides it.
            if (YapsShaderPatcher.Duplicate(to) != wasDuplicate && YapsShaderPatcher.IsStale(to))
                YapsShaderPatcher.Refresh(to, null);
        }

        // By OutputRoot, which survives a shader revert; names and properties do not.
        static bool IsGenerated(Material m)
        {
            if (m == null) return false;
            string path = AssetDatabase.GetAssetPath(m);
            return !string.IsNullOrEmpty(path)
                   && path.StartsWith(OutputRoot + "/", System.StringComparison.Ordinal);
        }

        // Recorded as baked but no longer on a YAPS shader, so nothing bends.
        // Thry's "Unlock all materials" does this by guessing a plain shader.
        // Another bake patches it again.
        internal static List<Material> LostShader(YapsPlug plug)
        {
            var lost = new List<Material>();
            if (plug == null) return lost;
            foreach (var b in plug.bakedSlots)
            {
                if (b == null) continue;
                var r = b.renderer != null ? b.renderer : plug.Target;
                var mats = r != null ? r.sharedMaterials : null;
                if (mats == null || b.slot < 0 || b.slot >= mats.Length) continue;
                var m = mats[b.slot];
                if (m != null && m != b.was && IsGenerated(m) && !m.HasProperty("_YAPS_Bake")) lost.Add(m);
            }
            return lost.Distinct().ToList();
        }

        internal static Material LostShader(YapsSocket socket)
        {
            var r = socket != null ? socket.bakedRenderer : null;
            if (r == null || socket.bakedFrom == null) return null;
            var mats = r.sharedMaterials;
            var m = socket.bakedSlot >= 0 && socket.bakedSlot < mats.Length ? mats[socket.bakedSlot] : null;
            return m != null && m != socket.bakedFrom && IsGenerated(m) && !m.HasProperty("_YAPS_Bake") ? m : null;
        }

        // For the upload check, by material alone: a saved _YAPS_Enabled under
        // a shader without one is a patch taken off.
        internal static void ReportLostShaders(BridgeReport report, GameObject root)
        {
            var lost = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Where(m => m != null && !m.HasProperty("_YAPS_Enabled") && SavedFloat(m, "_YAPS_Enabled"))
                .Select(m => m.name).Distinct().ToList();
            if (lost.Count > 0)
                report.Warning("Upload", $"{BridgeElements.Count(lost.Count, "material has", "materials have")} lost the YAPS shader, so nothing on them bends",
                    $"{AvatarSurvey.Few(lost)}. " + LostShaderWhy + " Convert again for a converted avatar. Ignore this " +
                    "where YAPS was removed on purpose.");
        }

        static bool SavedFloat(Material m, string name)
        {
            var floats = new SerializedObject(m).FindProperty("m_SavedProperties.m_Floats");
            for (int i = 0; floats != null && i < floats.arraySize; i++)
                if (floats.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue == name) return true;
            return false;
        }

        internal const string LostShaderWhy =
            "Something put a plain shader back on it: Thry's Unlock all materials does this to materials " +
            "it did not lock itself. Bake again to patch it back.";

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

        // Every OTHER skinned mesh the chain reaches (collar, hair), or they
        // stay rigid while the rest bends. Each gets its OWN bake but shares
        // the primary's FRAME and LENGTH. Not when the author named a slot. A
        // converted plug takes its recorded meshes: its marker object has no
        // bones under it for the chain tests.
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
                // The converter's line: any weight would take a body touching the base.
                if (!converted && !YapsBaker.RidesPlug(skin, primary.Root != null ? primary.Root : plug.rootBone))
                {
                    report?.Skipped("YAPS", $"\"{skin.name}\" left out of the plug",
                        "It meets the plug only at the plug's root bone, so it is taken for the body " +
                        "the plug grows from and stays as it is. A part that should bend with the " +
                        "shaft needs weights on the shaft's own bones.");
                    continue;
                }

                // A mesh with its own plug is not this plug's: both baked one
                // material and hierarchy order picked the frame.
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

        // This renderer's own bake plus the knobs. Not MirrorToSlots: that
        // would hand over the primary's bake texture and vertex count.
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
                    : YapsShaderPatcher.Patch(was, report, out _, out _, roles: YapsShaderPatcher.Roles.Plug);
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
                    if (YapsShaderPatcher.IsStale(target)) YapsShaderPatcher.Refresh(target, report);
                    else YapsShaderPatcher.Wear(target, shader);
                    YapsShaderPatcher.Wear(target, YapsShaderPatcher.WithRole(target, YapsShaderPatcher.Roles.Plug, report) ?? target.shader);
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
                // On, or one switched off by hand stays rigid through rebuilds.
                target.SetFloat("_YAPS_Enabled", 1f);
                YapsBaker.MirrorSwitches(target);
                // The length override too, or each mesh ran its own envelope.
                if (plug.lengthOverride > 0) target.SetFloat("_YAPS_Length", BakeLength(skin, target, plug.lengthOverride));
                done++;
            }
            if (done > 0) skin.sharedMaterials = mats;
            return done;
        }

        // Every submesh the chain moves: any real weight counts, since one
        // moving vertex in an unpatched slot tears the seam.
        // heaviest: the most-weighted slot, -1 if none, from the same walk.
        // climb: the bake's own climb from a root no bone sits under (an empty
        // under a bone) to the first level that has one. Only where that bake
        // ran: from any other mesh, the bone above is usually the body's.
        public static List<int> SlotsWeightedTo(SkinnedMeshRenderer skin, Transform rootBone, out int heaviest, bool climb = false)
        {
            var slots = new List<int>();
            heaviest = -1;
            if (skin == null || skin.sharedMesh == null || skin.bones == null || rootBone == null) return slots;
            var mesh = skin.sharedMesh;
            var weights = mesh.boneWeights;
            if (weights == null || weights.Length == 0) return slots;
            var chain = new HashSet<int>();
            for (var level = rootBone; level != null && chain.Count == 0; level = climb ? level.parent : null)
                for (int i = 0; i < skin.bones.Length; i++)
                    if (skin.bones[i] != null && (skin.bones[i] == level || skin.bones[i].IsChildOf(level))) chain.Add(i);
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
        // meshPath: for a plug that outlives the scene; the default is
        // overwritten by every spawn.
        public static GameObject BuildTestPlug(Transform parent = null, bool select = true, float length = 0.25f,
            string meshPath = null, string name = "YAPS Test Plug")
        {
            length = Mathf.Clamp(length, 0.08f, 1.5f);
            float radius = Mathf.Clamp(0.028f * length / 0.25f, 0.015f, 0.07f);
            var root = new GameObject(name);
            if (parent != null) root.transform.SetParent(parent, false);
            else
            {
                var cam = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
                if (cam != null) root.transform.position = cam.transform.position + cam.transform.forward * 0.6f;
            }
            var mf = root.AddComponent<MeshFilter>();
            var mr = root.AddComponent<MeshRenderer>();
            var mesh = CapsuleMesh(length, radius);
            // One asset, overwritten: unique paths left a mesh per spawn.
            // By name: the bake folder follows the name too, so two test plugs named apart share nothing.
            if (string.IsNullOrEmpty(meshPath)) meshPath = OutputRoot + "/Test Plug/" + name + " Mesh.asset";
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
            // Wound so the OUTSIDE faces out: Unity's front is Cross(B - A, C - A).
            for (int ring = 0; ring < along; ring++)
                for (int a = 0; a < around; a++)
                {
                    int nx = (a + 1) % around, here = ring * around, up = (ring + 1) * around;
                    tri.Add(here + a); tri.Add(up + nx); tri.Add(up + a);
                    tri.Add(here + a); tri.Add(here + nx); tri.Add(up + nx);
                }
            // A base cap with its own vertices for a hard edge. The tip ring has radius 0.
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

        // The other plug whose bake this slot holds, including slots it
        // mirrored into, or a re-bake deletes the texture its primary shares.
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

        // The avatar or prop a part belongs to, else its top object. Not the
        // scene root: two avatars under one container would share everything.
        public static Transform AvatarRoot(Transform t)
        {
            if (t == null) return null;
            var avatar = t.GetComponentInParent<CVRAvatar>(true);
            if (avatar != null) return avatar.transform;
            var prop = t.GetComponentInParent<CVRSpawnable>(true);
            if (prop != null) return prop.transform;
            // Before conversion a VRChat avatar has neither, and the scene root of one
            // parented under a container is the container. Read by name, so the add-on
            // builds without the VRChat SDK.
            for (var at = t; at != null; at = at.parent)
            {
                foreach (var c in at.GetComponents<Component>())
                    if (c != null && c.GetType().Name == "VRCAvatarDescriptor") return at;
            }
            return t.root;
        }

        internal static string Sanitise(string s)
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
