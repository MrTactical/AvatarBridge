// YAPS: the pipeline pass. Turns VRCFury's baked SPS rig into something
// ChilloutVR can run. Inspired by VRCFury's SPS, no SPS code is used.
// See docs/YAPS-CLEAN-ROOM.md.
//
// VRCFury leaves a "BakedSpsPlug" per plug and a "BakedSpsSocket" per
// socket, with contact senders and two protocol lights. Its screen-space
// atlas transport is VRChat-only and is deleted.
#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System;
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsConverter
    {
        const string Category = "YAPS";

        static readonly string[] AtlasJunk = { "SpsResolver", "SpsScreenMarker", "SpsAtlas" };

        public static void Run(BridgeContext ctx)
        {
            if (!ctx.Settings.convertYapsSystems)
            {
                return;
            }

            var plugRoots = Named(ctx, "BakedSpsPlug");
            var socketRoots = Named(ctx, "BakedSpsSocket");
            if (plugRoots.Count == 0 && socketRoots.Count == 0)
            {
                ReportLegacyContent(ctx);
                return;
            }

            RemoveAtlasJunk(ctx);
            // Sockets on this avatar: its own plugs check ownership first.
            float selfFlag = socketRoots.Count > 0 ? 1f : -1f;

            // The rebuild reads kind and channel off Fury's rig, then strips
            // it, so everything after here works on a bare socket.
            var rebuild = YapsSocketRebuilder.ReadAndStrip(ctx, socketRoots);
            CarryTags(ctx, rebuild);
            YapsSocketRebuilder.Wake(ctx, socketRoots);

            foreach (var plugRoot in plugRoots)
            {
                ConvertPlug(ctx, plugRoot);
            }
            foreach (var plug in ctx.YapsPlugs)
            {
                // Every material of the plug, never just the recorded one. A plug
                // spanning two materials with the flag on one reads the atlas across
                // half its mesh and the lights across the other, which looks right
                // until the lights go out.
                foreach (var material in plug.Materials)
                {
                    if (material == null) { continue; }
                    material.SetFloat("_YAPS_SelfTag", selfFlag);
                    // The atlas is a material flag so a plug can be built
                    // without it, and one switch decides for the whole system.
                    material.SetFloat("_YAPS_UseAtlas", YapsAtlas.Enabled ? 1f : 0f);
                }
            }

            ConvertSockets(ctx, socketRoots);
            YapsSocketRebuilder.Finish(ctx, rebuild);
            WireSocketToggles(ctx, socketRoots);
            YapsSocketRebuilder.Lighthouse(ctx);

            // After every plug material and socket writer exists: it wires
            // whatever renderer can hold the id.
            string owner = YapsOwner.Wire(ctx.Target, ctx.MergedController);
            if (owner != null)
            {
                ctx.Report.Converted(Category, "Plugs and sockets carry the wearer's owner id",
                    "A plug tells the wearer's sockets from anyone else's at any distance. One synced " +
                    "parameter, 32 bits. (" + owner + ")");
            }

            // One switch for the whole atlas, so the writers and the grab
            // cannot disagree about whether it is on.
            if (YapsAtlas.Enabled && YapsAtlas.AddClear(ctx.Target.transform) != null
                && YapsAtlas.AddGrab(ctx.Target.transform) != null)
            {
                ctx.Report.Converted(Category, "Added the screen surface plugs read each other through",
                    "Lets plugs find sockets through the screen, with no light slot, contact or sync bit. " +
                    "Invisible.");
            }

            if (socketRoots.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"Kept the depth reactions on {socketRoots.Count} socket(s)",
                    "Pointed at each rebuilt socket's depth trigger. Local, so only you see them, unless " +
                    "\"Show the avatar's OWN depth animations to other players\" is on (32 bits a socket).");
            }

            if (plugRoots.Count > 0 && ctx.YapsPlugs.Count == 0)
            {
                ctx.Report.Warning(Category, "No plug could be converted",
                    "The objects are here but nothing usable came out of them; the entries above " +
                    "say why for each. The avatar is otherwise unaffected.");
            }
        }

        // --- the plug --------------------------------------------------

        static void ConvertPlug(BridgeContext ctx, Transform plugRoot)
        {
            string where = ctx.PathInTarget(plugRoot);

            var renderer = FindPlugRenderer(ctx, plugRoot, out int plugVertices, out var chainLevel);
            if (renderer == null)
            {
                ctx.Report.Warning(Category, $"No mesh found for the plug at {where}",
                    "No skinned renderer on this avatar has vertices weighted to the plug's bones, " +
                    "so there is nothing to bend. A plug built as a separate unskinned object is " +
                    "not supported yet.");
                return;
            }

            // The plug's chain must be its own. When the first bones found above
            // the plug object are the wearer's, Hips or Spine or a leg, "the plug"
            // is the body. Length says nothing here: a hyper plug is longer than
            // its wearer and still has a chain of its own.
            string bodyBone = HumanoidBoneName(ctx, chainLevel);
            if (bodyBone != null)
            {
                ctx.Report.Warning(Category, $"The plug at {where} was left alone",
                    $"Its chain is the body's own ({bodyBone}), so it would bend the whole avatar. Put the " +
                    "SPS Plug on the plug's root bone and convert again.");
                return;
            }

            // A source built by the toolkit with its readout on wears the
            // readout's mesh; the bake wants the one underneath.
            YapsDebugOverlayBuilder.Restore(plugRoot.GetComponent<YapsPlug>());
            var result = YapsBaker.Bake(renderer, plugRoot, ctx.OutputDir + "/YAPS", ctx.Report,
                out string bakeFailure);
            if (result == null)
            {
                ctx.Report.Warning(Category, $"Could not bake the plug at {where}", bakeFailure);
                return;
            }

            // Every material slot the plug's triangles use, never just the biggest.
            // A tip modelled on its own material is a second slot, and leaving it
            // unpatched leaves it hanging in the air while the rest bends away.
            //
            // result.Root, not plugRoot: the bake may have descended to the shaft,
            // and asking a wider root patches materials the bake left out.
            var slots = MaterialSlotsOf(renderer, result.Root);
            var patchedSlots = new List<string>();
            var patchedMaterials = new List<Material>();
            var patchedSlotIndices = new List<int>();
            int primarySlot = -1;
            Material primaryMaterial = null;
            int skippedShadowPasses = 0;
            foreach (int slot in slots)
            {
                var slotMaterial = PatchPlugSlot(ctx, where, renderer, result.Root, slot, result,
                    out int skipped);
                if (slotMaterial == null)
                {
                    continue;
                }
                patchedSlots.Add($"{slot} (\"{slotMaterial.name}\")");
                patchedMaterials.Add(slotMaterial);
                patchedSlotIndices.Add(slot);
                if (primarySlot < 0)
                {
                    primarySlot = slot;
                    primaryMaterial = slotMaterial;
                    skippedShadowPasses = skipped;
                }
            }
            if (primarySlot < 0)
            {
                return;
            }

            ctx.YapsPlugs.Add(new BridgeContext.YapsPlug
            {
                Root = result.Root,
                Renderer = renderer,
                Material = primaryMaterial,
                MaterialSlot = primarySlot,
                Materials = patchedMaterials,
                MaterialSlots = patchedSlotIndices,
                Length = result.Length,
                Radius = result.Radius,
                Shapes = result.Shapes,
                MovingShapes = result.MovingShapes,
                ChainRoot = ChainRootOf(renderer as SkinnedMeshRenderer, chainLevel, result.Root),
                Origin = result.Origin,
                Rotation = result.Rotation,
            });

            // The same markers the toolkit builds for a native plug, on the measured
            // frame: DPS tracker light, TPS and SPS pointers. Fury's own rig goes
            // first, or its tracker doubles the fresh one and its pointers win the
            // announce's dedupe.
            YapsSocketRebuilder.StripPlugRig(plugRoot);
            YapsNativeBuilder.AnnouncePlug(plugRoot, result.Origin, result.Rotation,
                result.Length, result.Radius, tipLight: true, pointers: true);

            // The authoring component, read back off the patched material,
            // so a re-bake writes the same thing.
            YapsNativeBuilder.AdoptPlug(plugRoot, renderer, primarySlot, primaryMaterial, null);

            // The readout, if this conversion asked for one. Seeded onto the
            // component AdoptPlug just wrote, so a later Build in the toolkit
            // keeps it rather than silently taking it away again.
            var adopted = plugRoot.GetComponent<YapsPlug>();
            if (adopted != null)
            {
                adopted.debugOverlay = ctx.Settings.yapsDebugOverlay;
            }
            YapsDebugOverlayBuilder.Apply(plugRoot, renderer.name, ctx.Settings.yapsDebugOverlay,
                result, primaryMaterial, ctx.Report);

            ctx.Report.Converted(Category, $"Plug converted at {where}",
                $"\"{renderer.name}\" material{(patchedSlots.Count > 1 ? "s" : "")} " +
                $"{string.Join(" and ", patchedSlots)}, " +
                $"{plugVertices} vertices on the plug's bones, {result.Length:0.###} m long, on a private shader copy." +
                (skippedShadowPasses > 0 ? $" {skippedShadowPasses} shadow pass(es) cannot be patched and stay straight." : ""));
        }

        // One material slot of the plug's renderer: patch its shader,
        // apply the bake, carry the old system's settings. Null means the
        // slot could not take the deform, and it has said why.
        static Material PatchPlugSlot(BridgeContext ctx, string where, Renderer renderer,
            Transform plugRoot, int slot, YapsBaker.Result result, out int skippedShadowPasses)
        {
            skippedShadowPasses = 0;
            var materials = renderer.sharedMaterials;
            if (slot < 0 || slot >= materials.Length || materials[slot] == null)
            {
                ctx.Report.Warning(Category, $"No material to patch for the plug at {where}",
                    "The renderer's material list does not cover the plug's triangles.");
                return null;
            }

            // What the material already is. The old deform must not run beside
            // YAPS: TPS and SPS keep their shader with theirs switched off, DPS
            // moves to Simple Lit because Raliv's has no switch. Same rule the
            // toolkit applies to a native plug.
            var source = materials[slot];
            var legacy = YapsLegacyMap.Detect(source, out _);
            var patchSource = source;
            Shader shader = null;
            string refusal = null;
            if (legacy == YapsLegacyMap.Origin.DPS)
            {
                var plain = YapsNativeBuilder.OnSimpleLit(source, out string why);
                if (plain == null)
                {
                    refusal = why;
                }
                else
                {
                    shader = YapsShaderPatcher.Patch(plain, ctx.OutputDir + "/YAPS", ctx.Report,
                        out refusal, out skippedShadowPasses);
                    if (shader != null)
                    {
                        patchSource = plain;
                        ctx.Report.Approximated(Category, $"\"{source.name}\" wears YAPS Simple Lit now",
                            "A DPS shader has no switch for its own deform, so both would have bent the " +
                            "plug. Its colour, albedo, normal map, metallic and smoothness were carried " +
                            "over; the original material is untouched.");
                    }
                }
            }
            else
            {
                shader = YapsShaderPatcher.Patch(source, ctx.OutputDir + "/YAPS",
                    ctx.Report, out refusal, out skippedShadowPasses, allowSps: legacy == YapsLegacyMap.Origin.SPS);
            }

            // A shader that will not take the deform is not the end of it:
            // Simple Lit will, with the look carried over. The toolkit has
            // always done this; the converter used to give up instead.
            if (shader == null)
            {
                var plain = YapsNativeBuilder.OnSimpleLit(source, out string why);
                if (plain != null)
                {
                    var second = YapsShaderPatcher.Patch(plain, ctx.OutputDir + "/YAPS", ctx.Report,
                        out string plainRefusal, out skippedShadowPasses);
                    if (second != null)
                    {
                        shader = second;
                        patchSource = plain;
                        ctx.Report.Approximated(Category, $"\"{source.name}\" wears YAPS Simple Lit now",
                            $"Its shader could not take the deform ({refusal}). Colour, albedo, normal, metallic and " +
                            "smoothness carried over.");
                    }
                    else
                    {
                        refusal += "; and YAPS Simple Lit refused too: " + plainRefusal;
                    }
                }
                else
                {
                    refusal += "; and " + why;
                }
            }
            if (shader == null)
            {
                ctx.Report.Warning(Category, $"Could not add the deform to \"{source.name}\"",
                    $"{refusal}. The plug converts as an ordinary mesh: it will look right and " +
                    "simply will not bend.");
                return null;
            }

            // Read the author's values off the original material before the
            // patch repoints it; a Poiyomi material loses its TPS properties there.
            var patched = YapsBaker.Apply(result, patchSource, shader, ctx.OutputDir + "/YAPS",
                result.FromSkinnedMesh);
            var unmapped = new List<string>();
            var carried = YapsLegacyMap.Carry(source, patched, unmapped, result.Length, result.Radius);
            if (legacy != YapsLegacyMap.Origin.None && legacy != YapsLegacyMap.Origin.YAPS)
            {
                YapsNativeBuilder.SwitchOffLegacyDeform(patched, legacy);
            }
            // Every OTHER material the animator can put in this slot gets the same
            // bake. RepointSwappedMaterials below only knows the material the bake
            // replaced; an alternate look was never that, so it stayed deformless
            // and left the plug rigid whenever its toggle was on.
            if (ctx.MergedController != null)
            {
                Yaps.YapsSwapFollow.FollowVariants(renderer, slot, result,
                    ctx.OutputDir + "/YAPS", ctx.MergedController.animationClips, ctx.Report,
                    source, patchSource, patched);
            }
            if (carried.Count > 0)
            {
                var system = legacy;
                ctx.Report.Converted(Category,
                    $"Carried {carried.Count} {system} setting(s) onto the YAPS plug",
                    string.Join(", ", carried.ConvertAll(c => $"{c.From} → {c.To}")) +
                    (unmapped.Count > 0
                        ? $". No YAPS counterpart for: {string.Join(", ", unmapped)}"
                        : ""));
            }

            // The plug component's overrun choice wins over the material's,
            // since the component is what SPS's own tools edit.
            string plugObject = plugRoot.parent != null ? plugRoot.parent.name : null;
            bool overrun = plugObject != null
                           && YapsBakePrep.AuthoredOverrun.TryGetValue(plugObject, out bool authored)
                ? authored
                : true;
            patched.SetFloat("_YAPS_Overrun", overrun ? 1f : 0f);

            // Which sockets this plug will answer, in the author's own
            // words. SPS bakes them to hashes and a hash is not a word, so
            // they come off the component before the bake or not at all.
            if (plugObject != null && YapsBakePrep.AuthoredAnswers.TryGetValue(plugObject, out var answers))
            {
                patched.SetVector("_YAPS_TagInclude", YapsTags.Patterns(answers));
            }
            if (plugObject != null && YapsBakePrep.AuthoredRefuses.TryGetValue(plugObject, out var refuses))
            {
                patched.SetVector("_YAPS_TagExclude", YapsTags.Patterns(refuses));
            }
            // The rules the author aimed at their OWN sockets, kept by name for
            // the own-socket ticks, which are worked out once sockets exist.
            if (plugObject != null)
            {
                YapsBakePrep.AuthoredSelfAnswers.TryGetValue(plugObject, out var selfAnswers);
                YapsBakePrep.AuthoredSelfRefuses.TryGetValue(plugObject, out var selfRefuses);
                YapsOwner.KeepSelfRules(patched, selfAnswers, selfRefuses,
                    YapsBakePrep.AuthoredEntersOwnHips.Contains(plugObject));
            }
            ctx.YapsMaterialSwaps[(renderer, slot)] = (materials[slot], patched);
            materials[slot] = patched;
            renderer.sharedMaterials = materials;
            return patched;
        }

        // The socket half of the tag carry. The plug half sits in the
        // material patch, since that is where the plug's uniforms are
        // written; this one has to reach the rebuild, which happens later
        // and on the ChilloutVR side of the defines, so it travels in the
        // spec rather than by reading the dictionary from there.
        //
        // Keyed by the object the socket component sat on, which survives
        // as the parent of BakedSpsSocket.
        static void CarryTags(BridgeContext ctx, Dictionary<Transform, YapsSocketRebuilder.Spec> specs)
        {
            int tagged = 0;
            var words = new List<string>();
            foreach (var pair in specs)
            {
                var owner = pair.Key != null ? pair.Key.parent : null;
                if (owner == null) continue;
                if (!YapsBakePrep.AuthoredSocketTags.TryGetValue(owner.name, out var tags)) continue;
                pair.Value.Tags = new List<string>(tags);
                tagged++;
                foreach (string tag in tags) if (!words.Contains(tag)) words.Add(tag);
            }
            int plugs = YapsBakePrep.AuthoredAnswers.Count + YapsBakePrep.AuthoredRefuses.Count;
            if (tagged == 0 && plugs == 0) return;

            // The bake holds four on each list. Dropping the fifth quietly
            // changes which sockets the plug answers, and the author reads
            // their own list in the other tool and sees nothing wrong.
            int over = 0;
            foreach (var list in YapsBakePrep.AuthoredAnswers.Values) over += YapsTags.Dropped(list);
            foreach (var list in YapsBakePrep.AuthoredRefuses.Values) over += YapsTags.Dropped(list);
            if (over > 0)
            {
                ctx.Report.Warning(Category,
                    $"{over} tag(s) past the first {YapsTags.PlugSlots} on a list were not carried",
                    $"Only {YapsTags.PlugSlots} fit per list; sockets past that are treated as unlisted. Shorten "
                    + "the list, or share one word across those sockets.");
            }
            ctx.Report.Converted(Category,
                $"Carried the tags on {tagged} socket(s) and {plugs} plug rule list(s)",
                "Words as written: " + (words.Count == 0 ? "none on the sockets here" : string.Join(", ", words))
                + ". Tags match by fingerprint, so two names can rarely read as one.");
        }

        static Renderer FindPlugRenderer(BridgeContext ctx, Transform plugRoot, out int plugVertices,
            out Transform chainLevel)
        {
            Renderer best = null;
            plugVertices = 0;
            chainLevel = null;

            // VRCFury's own first rule: a renderer sitting on the plug's object is
            // the plug. A dedicated mesh object carrying the component has no bones
            // beneath it, and scoring by bone weight from there climbs to the hips
            // and elects the body.
            var owner = plugRoot.parent;
            if (owner != null && owner != ctx.Target.transform)
            {
                var onObject = owner.GetComponent<SkinnedMeshRenderer>();
                if (onObject != null && onObject.sharedMesh != null)
                {
                    plugVertices = onObject.sharedMesh.vertexCount;
                    // The level goes out with it, or this shortcut skips the
                    // body guard below: it returned before anything set one, so
                    // the guard asked about null, and null is not a bone. A
                    // component put on the object carrying the BODY's mesh took
                    // this path and the whole avatar was baked as the plug,
                    // silently, which is the failure the second rule exists for.
                    //
                    // The object the component sits on is the level here, and a
                    // dedicated plug mesh object is not a humanoid bone, so the
                    // guard passes it. One that IS a bone is the body's mesh
                    // sitting on the skeleton, which is the case to refuse.
                    chainLevel = owner;
                    return onObject;
                }
            }

            // VRCFury's second rule, and the one that matters: climb ONCE from the
            // plug object, and at the first level where any mesh has vertices on
            // that level's bones, take the mesh with most. Letting every mesh climb
            // on its own let the body reach the hips and win by sheer count over a
            // plug with its own armature. Ten avatars baked their whole body.
            var renderers = ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (var level = plugRoot; level != null && level != ctx.Target.transform.parent; level = level.parent)
            {
                foreach (var renderer in renderers)
                {
                    int count = YapsBaker.CountVerticesUnder(renderer, level);
                    if (count > plugVertices)
                    {
                        plugVertices = count;
                        best = renderer;
                    }
                }
                if (best != null)
                {
                    chainLevel = level;
                    return best;
                }
            }
            return best;
        }

        // The bone whose scale is the plug's scale: the level the mesh was
        // found at when that is one of the mesh's bones, else the topmost
        // bone of the mesh beneath it, else the plug object's first bone
        // ancestor. Null when the mesh has no bones there at all.
        static Transform ChainRootOf(SkinnedMeshRenderer skin, Transform level, Transform plugRoot)
        {
            if (skin == null || skin.bones == null)
            {
                return null;
            }
            var bones = new HashSet<Transform>(skin.bones.Where(b => b != null));
            if (level != null && bones.Contains(level))
            {
                return level;
            }
            if (level != null)
            {
                Transform top = null;
                foreach (var b in bones)
                {
                    if (!b.IsChildOf(level))
                    {
                        continue;
                    }
                    // Topmost: no other bone of the mesh above it under the level.
                    bool topmost = true;
                    for (var at = b.parent; at != null && at != level; at = at.parent)
                    {
                        if (bones.Contains(at)) { topmost = false; break; }
                    }
                    if (topmost) { top = b; break; }
                }
                if (top != null)
                {
                    return top;
                }
            }
            for (var at = plugRoot; at != null; at = at.parent)
            {
                if (bones.Contains(at))
                {
                    return at;
                }
            }
            return null;
        }

        // The humanoid bone an object IS, or null. The avatar root counts
        // too: a chain found there is every bone the avatar has.
        static string HumanoidBoneName(BridgeContext ctx, Transform level)
        {
            if (level == null)
            {
                return null;
            }
            if (level == ctx.Target.transform)
            {
                return "the avatar root";
            }
            var animator = ctx.TargetAnimator;
            if (animator == null || !animator.isHuman)
            {
                return null;
            }
            for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
            {
                if (animator.GetBoneTransform(bone) == level)
                {
                    return bone.ToString();
                }
            }
            return null;
        }

        // A submesh belongs to the plug if its triangles use plug vertices.
        // Names are the author's business and are routinely "Body".
        //
        // Every such submesh, biggest first: a tip modelled on its own material
        // is a second slot, and an unpatched slot stays rigid while the rest of
        // the plug bends around it.
        static List<int> MaterialSlotsOf(Renderer renderer, Transform plugRoot)
        {
            var found = new List<int>();
            var skin = renderer as SkinnedMeshRenderer;
            var mesh = skin != null ? skin.sharedMesh : null;
            var plugVertex = mesh != null ? PlugVertexMask(skin, plugRoot) : null;
            if (plugVertex == null)
            {
                found.Add(0);
                return found;
            }

            var hitsBySlot = new List<KeyValuePair<int, int>>();
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var indices = mesh.GetTriangles(sub);
                int hits = 0;
                for (int i = 0; i < indices.Length; i++)
                {
                    if (indices[i] < plugVertex.Length && plugVertex[indices[i]])
                    {
                        hits++;
                    }
                }
                if (hits > 0)
                {
                    hitsBySlot.Add(new KeyValuePair<int, int>(sub, hits));
                }
            }
            hitsBySlot.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (var pair in hitsBySlot)
            {
                found.Add(pair.Key);
            }
            if (found.Count == 0)
            {
                found.Add(0);
            }
            return found;
        }

        static int MaterialSlotOf(Renderer renderer, Transform plugRoot) =>
            MaterialSlotsOf(renderer, plugRoot)[0];

        static bool[] PlugVertexMask(SkinnedMeshRenderer skin, Transform plugRoot)
        {
            var mesh = skin.sharedMesh;
            var bones = skin.bones;
            if (mesh == null || bones == null || bones.Length == 0)
            {
                return null;
            }

            var plugBones = new HashSet<int>();
            for (int b = 0; b < bones.Length; b++)
            {
                if (bones[b] != null && bones[b].IsChildOf(plugRoot))
                {
                    plugBones.Add(b);
                }
            }
            if (plugBones.Count == 0)
            {
                for (var above = plugRoot.parent; above != null && plugBones.Count == 0;
                     above = above.parent)
                {
                    for (int b = 0; b < bones.Length; b++)
                    {
                        if (bones[b] != null && bones[b].IsChildOf(above))
                        {
                            plugBones.Add(b);
                        }
                    }
                }
            }
            if (plugBones.Count == 0)
            {
                return null;
            }

            var weights = mesh.boneWeights;
            var mask = new bool[mesh.vertexCount];
            for (int i = 0; i < mask.Length && i < weights.Length; i++)
            {
                var w = weights[i];
                mask[i] = (plugBones.Contains(w.boneIndex0) && w.weight0 > 0.5f)
                          || (plugBones.Contains(w.boneIndex1) && w.weight1 > 0.5f)
                          || (plugBones.Contains(w.boneIndex2) && w.weight2 > 0.5f)
                          || (plugBones.Contains(w.boneIndex3) && w.weight3 > 0.5f);
            }
            return mask;
        }

        // --- the sockets -----------------------------------------------

        // The author's menu entry, wired to the rebuilt socket. Fury's toggle
        // drove the deleted atlas, so pointing it at the socket object makes the
        // menu mean what it says without adding a second entry. A socket some
        // clip already switches is the author's to control and is left alone.
        static void WireSocketToggles(BridgeContext ctx, List<Transform> socketRoots)
        {
            // Only layers that can assert count as owning a path; Fury's
            // weight-zero exclusivity layers do not.
            var switchable = YapsSocketRebuilder.Switchable(ctx);

            int wired = 0;
            var unwired = new List<string>();
            foreach (var socket in socketRoots)
            {
                if (socket == null) continue;
                string path = ctx.PathInTarget(socket);
                if (switchable.Contains(path)) continue;
                string toggle = ToggleFor(ctx, socket);
                if (toggle != null && AddLightToggle(ctx, toggle, new List<string> { path }, startLit: true))
                {
                    wired++;
                }
                else
                {
                    unwired.Add(socket.name);
                }
            }
            if (wired > 0)
            {
                ctx.Report.Converted(Category, $"{wired} socket menu toggle(s) wired to their sockets",
                    "VRCFury's socket toggles drove its deleted atlas, so the menu looked right and " +
                    "did nothing. Each is now wired to its whole socket, lights, pointers and depth " +
                    "trigger together, so \"one socket at a time\" is the wearer's choice again.");
            }
            if (unwired.Count > 0)
            {
                ctx.Report.Approximated(Category,
                    $"{unwired.Count} socket(s) have no menu toggle and stay on",
                    string.Join(", ", unwired) + ". No menu entry matched them, so they are " +
                    "always active. The marker light budget still caps how many carry lights.");
            }
        }

        // The menu entry that toggles this socket. VRCFury's toggle drove
        // the deleted atlas and never touched the lights.
        static string ToggleFor(BridgeContext ctx, Transform socket)
        {
            var names = ctx.CvrAvatar.avatarSettings.settings
                .Select(e => e.machineName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            // The menu entry is named after the author's object, so walk up.
            for (var at = socket; at != null && at != ctx.Target.transform; at = at.parent)
            {
                string mine = Normalise(at.name);
                if (mine.Length < 3)
                {
                    continue;
                }
                // Longest match first, so a name beats its own prefix.
                string best = names
                    .Where(n => mine.StartsWith(Normalise(n), StringComparison.Ordinal)
                                && Normalise(n).Length >= 3)
                    .OrderByDescending(n => Normalise(n).Length)
                    .FirstOrDefault();
                if (best != null)
                {
                    return best;
                }
            }
            return null;
        }

        // "[VF958] Blowjob", "VF80_Blowjob" and "Handjob Left" must all meet
        // "HandjobLeft". VRCFury's numbering is noise; "Target" is object-only.
        static string Normalise(string name)
        {
            string clean = System.Text.RegularExpressions.Regex.Replace(name, @"\[VF\d+\]", "");
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"^VF\d+_", "");
            clean = clean.Replace("Target", "").Replace(" ", "").Replace("_", "");
            return clean;
        }

        // Holes first, then rings, then anything unlabelled.
        static int SocketRank(Transform socket)
        {
            int best = 3;
            foreach (var light in socket.GetComponentsInChildren<Light>(true))
            {
                int digit = Digit(light.range);
                if (digit == 1 || digit == 3) best = Mathf.Min(best, 0);
                else if (digit == 2 || digit == 4) best = Mathf.Min(best, 1);
            }
            return best;
        }

        // Two states, not a blend tree: m_IsActive is a switch.
        static bool AddLightToggle(BridgeContext ctx, string parameter, List<string> lightPaths, bool startLit)
        {
            var controller = ctx.MergedController;
            var declared = controller.parameters.FirstOrDefault(p => p.name == parameter);
            if (declared == null || lightPaths.Count == 0)
            {
                return false;
            }

            var on = ClipFor("YAPS lights on", lightPaths, 1f);
            var off = ClipFor("YAPS lights off", lightPaths, 0f);
            var machine = new UnityEditor.Animations.AnimatorStateMachine
            {
                name = parameter + " lights",
                hideFlags = HideFlags.HideInHierarchy,
            };

            string path = AssetDatabase.GetAssetPath(controller);
            if (!string.IsNullOrEmpty(path))
            {
                // Built in memory; it must join the asset or Unity drops it on save.
                AssetDatabase.AddObjectToAsset(machine, controller);
                AssetDatabase.AddObjectToAsset(on, controller);
                AssetDatabase.AddObjectToAsset(off, controller);
            }

            // Default On only while there is room. A mesh gets four vertex light
            // slots and a socket takes two, so an avatar that lights every socket
            // at once hands the plug four FRONT lights (range 0.453, the largest it
            // can see) and no roots, which is not a socket anything can enter.
            //
            // Past the cap they start dark and their own menu entry lights them,
            // which is what the entry is for. An avatar with one socket behaves
            // exactly as before.
            var onState = machine.AddState("On");
            onState.writeDefaultValues = false;
            onState.motion = on;
            var offState = machine.AddState("Off");
            offState.writeDefaultValues = false;
            offState.motion = off;
            machine.defaultState = startLit ? onState : offState;

            var toOn = offState.AddTransition(onState);
            var toOff = onState.AddTransition(offState);
            foreach (var t in new[] { toOn, toOff })
            {
                t.hasExitTime = false;
                t.duration = 0f;
            }
            if (declared.type == AnimatorControllerParameterType.Bool)
            {
                toOn.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, parameter);
                toOff.AddCondition(UnityEditor.Animations.AnimatorConditionMode.IfNot, 0f, parameter);
            }
            else
            {
                toOn.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Greater, 0.5f, parameter);
                toOff.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Less, 0.5f, parameter);
            }

            var layers = controller.layers.ToList();
            layers.Add(new UnityEditor.Animations.AnimatorControllerLayer
            {
                name = "YAPS " + parameter + " lights",
                defaultWeight = 1f,
                stateMachine = machine,
            });
            controller.layers = layers.ToArray();
            return true;
        }

        static AnimationClip ClipFor(string name, List<string> paths, float active)
        {
            var clip = new AnimationClip { name = name };
            foreach (string path in paths)
            {
                // The object, for every link in the chain; the Light
                // components are already enabled.
                clip.SetCurve(path, typeof(GameObject), "m_IsActive",
                    AnimationCurve.Constant(0f, 1f / 60f, active));
            }
            return clip;
        }

        static int Digit(float range) => Mathf.RoundToInt(range % 0.1f * 100f);

        // A socket nothing can find: no pointer carrying a socket tag and no
        // marker light. One with either is finished as far as a plug cares.
        //
        // Socket deform. A shape driven by both animator and shader applies
        // twice, so with no published depth (-1) the shader reads a light.
        static void ConvertSockets(BridgeContext ctx, List<Transform> socketRoots)
        {
            if (socketRoots.Count == 0)
            {
                return;
            }

            int deformed = 0, alreadyAnimated = 0, noShapes = 0, onBody = 0;
            var failures = new List<string>();

            foreach (var socketRoot in socketRoots)
            {
                var renderer = SocketRenderer(socketRoot);
                if (renderer == null || MeshOf(renderer) == null
                    || MeshOf(renderer).blendShapeCount == 0)
                {
                    // No shapes to open with, but still a socket to retune.
                    YapsNativeBuilder.AdoptSocket(socketRoot, renderer, null, null);
                    noShapes++;
                    continue;
                }

                // The socket shader measures depth from its mesh's own
                // origin, so only a mesh whose origin IS the socket opens
                // right. A body mesh keeps its contact-driven reactions.
                if (!YapsNativeBuilder.MeshIsTheSocket(renderer, socketRoot))
                {
                    YapsNativeBuilder.AdoptSocket(socketRoot, renderer, null, null);
                    onBody++;
                    continue;
                }

                bool animatorDrivesIt = AnimatorDrivesShapes(ctx, renderer);
                int slot = MaterialSlotOf(renderer, socketRoot);

                // Socket and plug on the same renderer and slot share the
                // material; the shader carries both deforms behind their own enables.
                var shared = ctx.YapsPlugs.FirstOrDefault(
                    p => p.Renderer == renderer && p.MaterialSlot == slot);

                Material material;
                List<string> bakedShapes;
                var already = renderer.sharedMaterials.Length > slot ? renderer.sharedMaterials[slot] : null;
                if (shared != null)
                {
                    material = shared.Material;
                    bakedShapes = shared.Shapes;
                    // _YAPS_Enabled stays 1: this material is a working plug too.
                }
                else if (already != null && already.HasProperty("_YAPS_Bake"))
                {
                    // A second socket on a mesh the first already baked:
                    // one material, one set of stages, shared.
                    material = already;
                    bakedShapes = null;
                }
                else
                {
                    var result = YapsBaker.Bake(renderer, socketRoot, ctx.OutputDir + "/YAPS",
                        null, out string failure, objectFrame: true);
                    if (result == null)
                    {
                        failures.Add($"{socketRoot.name}: {failure}");
                        continue;
                    }
                    bakedShapes = result.Shapes;

                    var materials = renderer.sharedMaterials;
                    if (slot < 0 || slot >= materials.Length || materials[slot] == null)
                    {
                        failures.Add($"{socketRoot.name}: its mesh has no material on the socket's slot");
                        continue;
                    }
                    var patched = YapsShaderPatcher.Patch(materials[slot], ctx.OutputDir + "/YAPS",
                        ctx.Report, out string refusal, out _);
                    if (patched == null)
                    {
                        failures.Add($"{socketRoot.name}: {refusal}");
                        continue;
                    }

                    material = YapsBaker.Apply(result, materials[slot], patched,
                        ctx.OutputDir + "/YAPS", renderer is SkinnedMeshRenderer);
                    // No plug on this mesh: the plug half stays asleep.
                    material.SetFloat("_YAPS_Enabled", 0f);

                    ctx.YapsMaterialSwaps[(renderer, slot)] = (materials[slot], material);
                    materials[slot] = material;
                    renderer.sharedMaterials = materials;
                }

                material.SetFloat("_YAPS_SocketPower", 1f);
                // -1, never 0. Zero is "a plug is here, not yet in"; -1 is
                // "nothing told me", which lets the shader fall back to lights.
                material.SetFloat("_YAPS_SocketDepth", -1f);
                // Self-exclusion earns its place only where a plug of this avatar's
                // rests on the socket, which is the crotch case it was written for. Out
                // on a hand it decides ownership by the nearest hip, and the nearest
                // hip can be somebody else's.
                bool ownPlugRests = ctx.YapsPlugs.Any(
                    p => Vector3.Distance(p.Origin, socketRoot.position) <= p.Length + 0.1f);
                // And the rest test is taken in the CONVERSION pose, which is one
                // pose out of all of them. A dedicated socket mesh on a limb that
                // happens to have the wearer's own plug near it at conversion had
                // exclusion switched on for the life of the upload, and thereafter
                // ownership answers with whichever hip is nearest that mesh: rest
                // the hand in a stranger's lap and the socket decides their plug is
                // its own wearer's and turns away the one plug it exists for.
                //
                // The toolkit bake has refused this case since 4.5.0 and the
                // converter did not, so the same avatar behaved differently
                // depending on which door it came through.
                bool undecidable = YapsNativeBuilder.MeshIsTheSocket(renderer, socketRoot)
                                   && YapsNativeBuilder.RidesAMovingLimb(socketRoot);
                material.SetFloat("_YAPS_SocketNoSelfExclude",
                    ownPlugRests && !undecidable ? 0f : 1f);

                // The authoring component, filled in from what was just built.
                YapsNativeBuilder.AdoptSocket(socketRoot, renderer, material, bakedShapes);

                deformed++;
                if (animatorDrivesIt)
                {
                    alreadyAnimated++;
                }
            }

            if (deformed > 0)
            {
                ctx.Report.Converted(Category,
                    $"{deformed} socket(s) can now deform around a plug",
                    "Blendshapes staged by depth in the socket's shader, so DPS plugs open them too. " +
                    (alreadyAnimated > 0 ? $"{alreadyAnimated} keep their contact as well." : ""));
            }
            if (onBody > 0)
            {
                ctx.Report.Approximated(Category,
                    $"{onBody} socket(s) keep their reactions on the animator",
                    "Their mesh is the body, so they keep their contact-driven shapes, made local.");
            }
            if (noShapes > 0)
            {
                ctx.Report.Converted(Category,
                    $"{noShapes} socket(s) have no blendshapes to deform",
                    "Nothing was changed for these. A socket deform reshapes the author's own " +
                    "blendshapes, so a socket built without any has nothing to open with; its " +
                    "contacts and marker lights work exactly as before.");
            }
            if (failures.Count > 0)
            {
                ctx.Report.Warning(Category,
                    $"{failures.Count} socket(s) could not be given a deform",
                    "Everything else about them is untouched and they still work as sockets: " +
                    "they simply will not reshape around a plug. " + string.Join("; ", failures));
            }
        }

        // The renderer wearing this socket's mesh, usually a parent: authors
        // hang the socket off the body they want reshaped.
        static Renderer SocketRenderer(Transform socketRoot)
        {
            var own = socketRoot.GetComponentInChildren<Renderer>(true);
            if (own != null && MeshOf(own) != null && MeshOf(own).blendShapeCount > 0)
            {
                return own;
            }
            for (var at = socketRoot.parent; at != null; at = at.parent)
            {
                var renderer = at.GetComponent<Renderer>();
                if (renderer != null && MeshOf(renderer) != null
                    && MeshOf(renderer).blendShapeCount > 0)
                {
                    return renderer;
                }
            }
            return null;
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

        // Informational only: the shader stands down on depth, not on this.
        static bool AnimatorDrivesShapes(BridgeContext ctx, Renderer renderer)
        {
            if (ctx.MergedController == null)
            {
                return false;
            }
            string path = ctx.PathInTarget(renderer.transform);
            foreach (var clip in ctx.MergedController.animationClips)
            {
                if (clip == null)
                {
                    continue;
                }
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.path == path
                        && binding.propertyName.StartsWith("blendShape.",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        static void ReportLegacyContent(BridgeContext ctx)
        {
            int lights = ctx.Target.GetComponentsInChildren<Light>(true)
                .Count(l => l != null && Digit(l.range) >= 1 && Digit(l.range) <= 6);

            var pointers = ctx.Target.GetComponentsInChildren<CVRPointer>(true)
                .Where(p => p != null && !string.IsNullOrEmpty(p.type))
                .Select(p => p.type)
                .ToList();
            int sockets = pointers.Count(t => t.StartsWith("TPS_Orf", StringComparison.OrdinalIgnoreCase)
                                           || t.StartsWith("SPSLL_Socket", StringComparison.OrdinalIgnoreCase));
            int plugs = pointers.Count(t => t.StartsWith("TPS_Pen", StringComparison.OrdinalIgnoreCase)
                                         || t.StartsWith("SPSLL_Pen", StringComparison.OrdinalIgnoreCase));

            if (lights == 0 && sockets == 0 && plugs == 0)
            {
                return;   // nothing penetration-shaped on this avatar at all
            }

            string found = string.Join(", ", new[]
            {
                lights > 0 ? $"{lights} marker light(s)" : null,
                sockets > 0 ? $"{sockets} socket contact(s)" : null,
                plugs > 0 ? $"{plugs} plug contact(s)" : null,
            }.Where(s => s != null));

            if (plugs > 0)
            {
                ctx.Report.Warning(Category,
                    "This avatar's penetrator could not be converted, but its sockets work",
                    $"Found {found}, and no SPS setup to build a plug from, so it will not bend. Set it up with " +
                    "SPS and convert again.");
            }
            else
            {
                ctx.Report.Converted(Category,
                    "Kept this avatar's existing penetration sockets",
                    $"Found {found} from DPS or TPS. Their lights and contacts come through as they were.");
            }
        }

        // --- the atlas's animation ----------------------------------------

        // RemoveAtlasJunk deletes the atlas objects early, before the merge.
        // The curves that animated them go late, once the clips are copies.
        public static void StripAtlasCurves(BridgeContext ctx)
        {
            if (!ctx.Settings.convertYapsSystems || ctx.MergedController == null)
            {
                return;
            }

            int removed = 0;
            var seen = new HashSet<AnimationClip>();
            foreach (var clip in ctx.MergedController.animationClips)
            {
                if (clip == null || !seen.Add(clip))
                {
                    continue;
                }
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (AddressesAtlas(binding.path))
                    {
                        AnimationUtility.SetEditorCurve(clip, binding, null);
                        removed++;
                    }
                }
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    if (AddressesAtlas(binding.path))
                    {
                        AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                        removed++;
                    }
                }
            }

            if (removed > 0)
            {
                ctx.Report.Converted(Category,
                    $"Removed {removed} animation curve(s) that drove the screen atlas",
                    "The socket toggles also wrote each socket's identity, tags and shape into the " +
                    "atlas marker's material, so VRChat's shader could read them back off the " +
                    "screen. The marker is gone and those writes went to nothing.");
            }
        }

        // A TOGGLE THAT SWAPS THE MATERIAL PUTS THE ORIGINAL BACK.
        //
        // The bake repoints a renderer's slot at the patched copy, and that
        // holds until the animator runs. An avatar that swaps that slot in an
        // animation, a skin picker or a variant toggle, hands the slot back to
        // the material the author baked from, which carries no deform and no
        // bake. The plug straightens the instant play mode starts, and the tool
        // reports it as never baked, because from the material there is nothing
        // to find.
        //
        // Scoped to the renderer AND slot that were actually repointed. The
        // original material is usually worn by other meshes as well, and they
        // have no bake of their own: handing them a plug's deform would bend
        // the wrong mesh.
        public static void RepointSwappedMaterials(BridgeContext ctx)
        {
            if (ctx.MergedController == null || ctx.YapsMaterialSwaps.Count == 0)
            {
                return;
            }

            // Keyed by the ORIGINAL material, never by the slot it sat in. A swap
            // clip is free to move a material between slots, and a mesh with two
            // baked materials has clips that do: one puts material B where the bake
            // found A. Matching on the slot alone left those keys pointing at the
            // unbaked original, so the part came back rigid the moment the toggle
            // played.
            //
            // Still scoped to this RENDERER. Every patched copy here was baked for
            // this mesh, so moving one between its own slots is safe.
            var bySlot = new Dictionary<string, Dictionary<int, (Material from, Material to)>>();
            var byMaterial = new Dictionary<string, Dictionary<Material, Material>>();
            foreach (var pair in ctx.YapsMaterialSwaps)
            {
                var renderer = pair.Key.renderer;
                if (renderer == null || pair.Value.from == null || pair.Value.to == null)
                {
                    continue;
                }
                string path = AnimationUtility.CalculateTransformPath(
                    renderer.transform, ctx.Target.transform);
                if (!bySlot.TryGetValue(path, out var slots))
                {
                    bySlot[path] = slots = new Dictionary<int, (Material, Material)>();
                    byMaterial[path] = new Dictionary<Material, Material>();
                }
                slots[pair.Key.slot] = pair.Value;

                // One original material baked twice on one mesh is ambiguous: two plug
                // regions sharing a material get a bake each, and a clip's key cannot
                // say which one it meant. Null marks it unanswerable, and the slot
                // route below stays the one that can answer.
                var mats = byMaterial[path];
                mats[pair.Value.from] = mats.TryGetValue(pair.Value.from, out var had)
                                        && had != pair.Value.to
                    ? null
                    : pair.Value.to;
            }

            int repointed = 0;
            var seen = new HashSet<AnimationClip>();
            foreach (var clip in ctx.MergedController.animationClips)
            {
                if (clip == null || !seen.Add(clip))
                {
                    continue;
                }
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    if (!bySlot.TryGetValue(binding.path, out var slots))
                    {
                        continue;
                    }
                    int slot = Yaps.YapsSwapFollow.SlotIndex(binding.propertyName);
                    if (slot < 0)
                    {
                        continue;
                    }
                    var swaps = byMaterial[binding.path];
                    var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                    if (keys == null)
                    {
                        continue;
                    }
                    bool touched = false;
                    for (int i = 0; i < keys.Length; i++)
                    {
                        var was = keys[i].value as Material;
                        if (was == null)
                        {
                            continue;
                        }
                        // The slot the bake touched answers first and exactly. The material map
                        // covers a clip that moves a material to another slot of the same mesh,
                        // and stands down where one original was baked more than once here.
                        Material to = null;
                        if (slots.TryGetValue(slot, out var swap) && swap.from == was)
                        {
                            to = swap.to;
                        }
                        else if (swaps.TryGetValue(was, out var wide))
                        {
                            to = wide;
                        }
                        if (to == null)
                        {
                            continue;
                        }
                        keys[i].value = to;
                        touched = true;
                        repointed++;
                    }
                    if (touched)
                    {
                        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
                    }
                }
            }

            if (repointed > 0)
            {
                ctx.Report.Converted(Category,
                    $"Pointed {repointed} material swap(s) at the baked material",
                    "Otherwise they would swap the unbaked material back in and the plug would straighten.");
            }
        }

        // "m_Materials.Array.data[3]" -> 3, anything else -> -1.
        static int MaterialSlotIndex(string propertyName)
        {
            const string prefix = "m_Materials.Array.data[";
            if (string.IsNullOrEmpty(propertyName) || !propertyName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return -1;
            }
            int close = propertyName.IndexOf(']', prefix.Length);
            if (close < 0)
            {
                return -1;
            }
            return int.TryParse(propertyName.Substring(prefix.Length, close - prefix.Length), out int slot)
                ? slot : -1;
        }

        static bool AddressesAtlas(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            // Any path segment that is an atlas object, or sits beneath one.
            foreach (string hint in AtlasJunk)
            {
                int at = path.IndexOf(hint, StringComparison.Ordinal);
                if (at < 0)
                {
                    continue;
                }
                // Segment boundary before it: start of path, or a slash.
                if (at > 0 && path[at - 1] != '/')
                {
                    continue;
                }
                return true;
            }
            return false;
        }

        // --- auto socket mode --------------------------------------------

        // VRCFury's auto mode switches on Current - Active > 0, no hysteresis.
        // Proximity is 0..1 over a 1 m sphere, so 0.05 is 5 cm of preference.
        const float AutoModeMargin = 0.05f;

        public static void SteadyAutoMode(BridgeContext ctx)
        {
            if (!ctx.Settings.convertYapsSystems || ctx.MergedController == null)
            {
                return;
            }

            int changed = 0;
            foreach (var layer in ctx.MergedController.layers)
            {
                if (layer.stateMachine == null)
                {
                    continue;
                }
                foreach (var child in layer.stateMachine.states)
                {
                    var state = child.state;
                    if (state == null)
                    {
                        continue;
                    }
                    foreach (var transition in state.transitions)
                    {
                        if (transition == null || transition.destinationState == null
                            || !transition.destinationState.name.StartsWith("Switch To ", StringComparison.Ordinal))
                        {
                            continue;
                        }
                        // The condition is VRCFury's "Current - Active" DBT
                        // subtraction; match on shape rather than on a Fury id.
                        var conditions = transition.conditions;
                        bool touched = false;
                        for (int i = 0; i < conditions.Length; i++)
                        {
                            var c = conditions[i];
                            if (c.mode == UnityEditor.Animations.AnimatorConditionMode.Greater
                                && Mathf.Approximately(c.threshold, 0f)
                                && c.parameter.Contains("AutoCurrentDist")
                                && c.parameter.Contains("AutoActiveDist"))
                            {
                                c.threshold = AutoModeMargin;
                                conditions[i] = c;
                                touched = true;
                            }
                        }
                        if (touched)
                        {
                            transition.conditions = conditions;
                            changed++;
                        }
                    }
                }
            }

            if (changed > 0)
            {
                ctx.Report.Converted(Category,
                    $"Auto socket mode switches only for a clearly nearer socket ({changed} transition(s))",
                    $"A socket must be about {AutoModeMargin * 100:0} cm nearer to take over, so close sockets " +
                    "no longer flicker.");
            }
        }

        // --- shape curves, atlas objects ---------------------------------

        // A vertex shader cannot read a blendshape weight or a bone's scale,
        // so each plug shape curve is mirrored onto the material, and the
        // chain root's scale curve onto the bake scale. Late, on owned copies.
        public static void MirrorShapeCurves(BridgeContext ctx)
        {
            if (!ctx.Settings.convertYapsSystems || ctx.YapsPlugs.Count == 0
                || ctx.MergedController == null)
            {
                return;
            }

            int written = 0, scaled = 0;
            var missed = new SortedSet<string>(StableSampleOrder.Instance);
            var clips = YapsCurveMirror.ClipsOf(ctx.MergedController).ToList();
            foreach (var plug in ctx.YapsPlugs)
            {
                string path = ctx.PathInTarget(plug.Renderer.transform);
                if (plug.Shapes.Count > 0)
                {
                    written += YapsCurveMirror.MirrorShapes(clips, path, plug.Renderer.GetType(),
                        plug.Shapes, plug.MovingShapes, missed);
                }
                // The chain root and its bone children: a size slider scales
                // one of them, and the shader takes that as the plug's scale.
                if (plug.ChainRoot != null)
                {
                    var bones = new Dictionary<string, Transform>();
                    void Bone(Transform t)
                    {
                        string bonePath = ctx.PathInTarget(t);
                        if (bonePath != null) bones[bonePath] = t;
                    }
                    Bone(plug.ChainRoot);
                    for (int i = 0; i < plug.ChainRoot.childCount; i++)
                    {
                        Bone(plug.ChainRoot.GetChild(i));
                    }
                    scaled += YapsCurveMirror.MirrorBoneScale(clips, bones, path, plug.Renderer.GetType(), plug.Rotation);
                }
            }

            if (written > 0)
            {
                ctx.Report.Converted(Category, $"Mirrored {written} blendshape curve(s) onto the plug",
                    "So the deform follows the plug's shapes, such as a size slider.");
            }
            if (scaled > 0)
            {
                ctx.Report.Converted(Category, $"Mirrored {scaled} bone scale curve(s) onto the plug",
                    "So a size or hyper toggle that scales the bone scales the bend too.");
            }
            if (missed.Count > 0)
            {
                ctx.Report.Warning(Category,
                    $"{missed.Count} animated plug blendshape(s) are not in the bake",
                    $"{string.Join(", ", missed.Take(8))}{(missed.Count > 8 ? ", …" : "")}: past the " +
                    $"{YapsBaker.MaxShapes} that move it most. The bend is slightly off while they are raised.");
            }
        }

        static void RemoveAtlasJunk(BridgeContext ctx)
        {
            var doomed = ctx.Target.GetComponentsInChildren<Transform>(true)
                .Where(t => t != null && t != ctx.Target.transform
                            && AtlasJunk.Any(hint => t.name.Contains(hint)))
                .OrderBy(Depth)
                .ToList();
            if (doomed.Count == 0)
            {
                return;
            }

            int removed = 0;
            foreach (var transform in doomed)
            {
                if (transform != null)
                {
                    UnityEngine.Object.DestroyImmediate(transform.gameObject);
                    removed++;
                }
            }
            ctx.Report.Converted(Category, $"Removed {removed} screen-atlas object(s)",
                "VRChat's version, which nothing here reads; left in, they draw into the view.");
        }

        static int Depth(Transform t)
        {
            int depth = 0;
            while (t.parent != null)
            {
                depth++;
                t = t.parent;
            }
            return depth;
        }

        // StartsWith, never Contains: the first-person exclusion object is
        // named "FPRExclusion_BakedSpsSocket" and is not a socket.
        //
        // Through Fury's ID prefix: ArmatureLink moves a socket to the head and
        // renames it "[VF724] BakedSpsSocket", and matching the raw name left
        // one of those looking like Fury's for a whole day.
        static List<Transform> Named(BridgeContext ctx, string needle) =>
            ctx.Target.GetComponentsInChildren<Transform>(true)
                .Where(t => t != null && YapsScanner.StripFuryId(t.name).StartsWith(needle, System.StringComparison.Ordinal))
                .ToList();
    }
}
#endif
