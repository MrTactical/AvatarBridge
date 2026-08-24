// YAPS: the pipeline pass. Turns VRCFury's baked SPS rig into
// something ChilloutVR can run. Inspired by VRCFury's SPS; no SPS
// code is used, see docs/YAPS-CLEAN-ROOM.md.
// VRCFury leaves a "BakedSpsPlug" per plug and a "BakedSpsSocket"
// per socket, with contact senders and two protocol lights. Its
// screen-space atlas transport is VRChat-only and is deleted.
#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System;
using System.Linq;
using ABI.CCK.Components;
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
            YapsSocketRebuilder.Wake(ctx, socketRoots);

            foreach (var plugRoot in plugRoots)
            {
                ConvertPlug(ctx, plugRoot);
            }
            foreach (var plug in ctx.YapsPlugs)
            {
                plug.Material.SetFloat("_YAPS_SelfTag", selfFlag);
            }

            ConvertSockets(ctx, socketRoots);
            YapsSocketRebuilder.Finish(ctx, rebuild);
            WireSocketToggles(ctx, socketRoots);
            YapsSocketRebuilder.Lighthouse(ctx);

            if (socketRoots.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"Kept the depth reactions on {socketRoots.Count} socket(s)",
                    "The animations a socket plays as a plug arrives — bulges, winces, whatever " +
                    "its author built — are kept and pointed at the rebuilt socket's own depth " +
                    "trigger. ChilloutVR runs that trigger on the wearer's machine alone, so by " +
                    "default the parameter is local: free against the sync budget, and the " +
                    "shapes play for you. \"Show the avatar's OWN depth animations to other " +
                    "players\" syncs it at 32 bits a socket so the room sees them too. A socket " +
                    "with its own mesh is different: its deform runs in its shader, which every " +
                    "client draws for itself, visible to everyone with nothing to sync.");
            }

            if (plugRoots.Count > 0 && ctx.YapsPlugs.Count == 0)
            {
                ctx.Report.Warning(Category, "No plug could be converted",
                    "The objects are here but nothing usable came out of them — the entries above " +
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

            // The plug's chain must be its own. When the first bones found
            // above the plug object are the wearer's, Hips or Spine or a
            // leg, "the plug" is the body: nothing beneath the plug object
            // told its vertices apart. Length says nothing here; a hyper
            // plug is longer than its wearer and has a chain of its own.
            string bodyBone = HumanoidBoneName(ctx, chainLevel);
            if (bodyBone != null)
            {
                ctx.Report.Warning(Category, $"The plug at {where} was left alone",
                    $"The first bones above the plug object belong to the body ({bodyBone}), so the " +
                    "bake could not tell the plug's vertices from the rest of the mesh and would have " +
                    "bent the whole avatar. Put the SPS Plug component on the plug's root bone, or on " +
                    "an empty under it, and convert again. Until then the plug keeps its mesh and " +
                    "does not bend.");
                return;
            }

            var result = YapsBaker.Bake(renderer, plugRoot, ctx.OutputDir + "/YAPS", ctx.Report,
                out string bakeFailure);
            if (result == null)
            {
                ctx.Report.Warning(Category, $"Could not bake the plug at {where}", bakeFailure);
                return;
            }

            // The plug's material is the slot its triangles use, not a name.
            int slot = MaterialSlotOf(renderer, plugRoot);
            var materials = renderer.sharedMaterials;
            if (slot < 0 || slot >= materials.Length || materials[slot] == null)
            {
                ctx.Report.Warning(Category, $"No material to patch for the plug at {where}",
                    "The renderer's material list does not cover the plug's triangles.");
                return;
            }

            // What the material already is. The old deform must not run
            // beside YAPS: TPS and SPS keep their shader with theirs
            // switched off, DPS moves to Simple Lit because Raliv's has no
            // switch. The same rule the toolkit applies to a native plug.
            var source = materials[slot];
            var legacy = YapsLegacyMap.Detect(source, out _);
            var patchSource = source;
            Shader shader = null;
            string refusal = null;
            int skippedShadowPasses = 0;
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
                            $"Its own shader could not take the deform ({refusal}), so the plug wears YAPS " +
                            "Simple Lit with its colour, albedo, normal map, metallic and smoothness carried " +
                            "over. The original material is untouched. Put a shader with source on the mesh " +
                            "(Poiyomi, for one) and reconvert for more than that.");
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
                return;
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
            materials[slot] = patched;
            renderer.sharedMaterials = materials;

            ctx.YapsPlugs.Add(new BridgeContext.YapsPlug
            {
                Root = plugRoot,
                Renderer = renderer,
                Material = patched,
                MaterialSlot = slot,
                Length = result.Length,
                Radius = result.Radius,
                Shapes = result.Shapes,
                MovingShapes = result.MovingShapes,
                ChainRoot = ChainRootOf(renderer as SkinnedMeshRenderer, chainLevel, plugRoot),
                Origin = result.Origin,
                Rotation = result.Rotation,
            });

            // The same markers the toolkit builds for a native plug, on the
            // measured frame: DPS tracker light, TPS and SPS pointers.
            // Fury's own rig goes first, or its tracker doubles the fresh
            // one and its pointers win the announce's dedupe.
            YapsSocketRebuilder.StripPlugRig(plugRoot);
            YapsNativeBuilder.AnnouncePlug(plugRoot, result.Origin, result.Rotation,
                result.Length, result.Radius, tipLight: true, pointers: true);

            // The authoring component, read back off the patched material,
            // so a re-bake writes the same thing.
            YapsNativeBuilder.AdoptPlug(plugRoot, renderer, slot, patched, null);

            ctx.Report.Converted(Category, $"Plug converted at {where}",
                $"\"{renderer.name}\" material {slot} (\"{materials[slot].name}\"), " +
                $"{plugVertices} vertices on the plug's bones, {result.Length:0.###} m long. " +
                "The mesh data it bends by is baked into a texture, and the deform is patched into " +
                "a private copy of the shader, so nothing else on the avatar is affected." +
                (skippedShadowPasses > 0
                    ? $" {skippedShadowPasses} shadow pass(es) were left undeformed — Unity's own " +
                      "shadow vertex function lives inside the engine and cannot be patched, and an " +
                      "unbent shadow is a far smaller loss than no deform at all."
                    : ""));
        }

        static Renderer FindPlugRenderer(BridgeContext ctx, Transform plugRoot, out int plugVertices,
            out Transform chainLevel)
        {
            Renderer best = null;
            plugVertices = 0;
            chainLevel = null;

            // VRCFury's own first rule: a renderer sitting on the plug's
            // object is the plug. A dedicated mesh object carrying the
            // component has no bones beneath it, and scoring by bone
            // weight from there climbs to the hips and elects the body.
            var owner = plugRoot.parent;
            if (owner != null && owner != ctx.Target.transform)
            {
                var onObject = owner.GetComponent<SkinnedMeshRenderer>();
                if (onObject != null && onObject.sharedMesh != null)
                {
                    plugVertices = onObject.sharedMesh.vertexCount;
                    return onObject;
                }
            }

            // VRCFury's second rule, and the one that matters: climb ONCE
            // from the plug object, and at the first level where any mesh
            // has vertices on that level's bones, take the mesh with most.
            // Letting every mesh climb on its own let the body climb to the
            // hips and win by sheer count over a plug with its own armature;
            // ten corpus avatars were baking their whole body as the plug.
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
        static int MaterialSlotOf(Renderer renderer, Transform plugRoot)
        {
            var skin = renderer as SkinnedMeshRenderer;
            var mesh = skin != null ? skin.sharedMesh : null;
            if (mesh == null)
            {
                return 0;
            }

            var plugVertex = PlugVertexMask(skin, plugRoot);
            if (plugVertex == null)
            {
                return 0;
            }

            int bestSlot = 0;
            int bestHits = 0;
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
                if (hits > bestHits)
                {
                    bestHits = hits;
                    bestSlot = sub;
                }
            }
            return bestSlot;
        }

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

        // The author's menu entry, wired to the rebuilt socket. Fury's
        // toggle drove the deleted atlas; pointing it at the socket object
        // makes the menu mean what it says without adding a second entry.
        // A socket some clip already switches is the author's to control
        // and is left alone.
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
                    "did nothing. Each is now wired to its whole socket — lights, pointers and depth " +
                    "trigger together — so \"one socket at a time\" is the wearer's choice again.");
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

            // Default On only while there is room. A mesh gets four vertex
            // light slots and a socket takes two, so an avatar that lights
            // every socket at once hands the plug four FRONT lights (range
            // 0.453, the largest it can see) and no roots, which is not a
            // socket anything can enter. Eleven sockets defaulting to lit is
            // twenty-two lights fighting over four places.
            //
            // Past the cap they start dark and their own menu entry lights
            // them, which is what the entry is for. An avatar with one socket
            // behaves exactly as before.
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
        // Socket deform. A shape driven by both animator and shader applies
        // twice, so with no published depth (-1) the shader reads a tracker light.
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

                    materials[slot] = material;
                    renderer.sharedMaterials = materials;
                }

                material.SetFloat("_YAPS_SocketPower", 1f);
                // -1, never 0. Zero is "a plug is here, not yet in"; -1 is
                // "nothing told me", which lets the shader fall back to lights.
                material.SetFloat("_YAPS_SocketDepth", -1f);
                // Self-exclusion earns its place only where a plug of this
                // avatar's rests on the socket, which is the crotch case it
                // was written for. Out on a hand it decides ownership by the
                // nearest hip, and the nearest hip can be somebody else's.
                bool ownPlugRests = ctx.YapsPlugs.Any(
                    p => Vector3.Distance(p.Origin, socketRoot.position) <= p.Length + 0.1f);
                material.SetFloat("_YAPS_SocketNoSelfExclude", ownPlugRests ? 0f : 1f);

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
                    $"Their blendshapes are baked and staged by depth in the socket's own shader, " +
                    "so they open around what arrives rather than sitting rigid. " +
                    (alreadyAnimated > 0
                        ? $"{alreadyAnimated} of them already play those shapes from a contact, and " +
                          "that is left exactly as it was — the shader only acts when nothing has " +
                          "told it a depth, which is precisely when the contact route is inert. "
                        : "") +
                    "What that buys is DPS content: Raliv's system is marker lights with no " +
                    "contacts anywhere in it, so a socket driven only by contacts does nothing at " +
                    "all against it, and most of the penetration content on ChilloutVR is exactly " +
                    "that. Shapes are staged in the order the author built them, entry first.");
            }
            if (onBody > 0)
            {
                ctx.Report.Approximated(Category,
                    $"{onBody} socket(s) keep their reactions on the animator",
                    "Their mesh is the body, and the socket-side shader deform measures depth from " +
                    "a mesh's own origin, which for a body is the avatar's root. So these sockets " +
                    "keep exactly what their author built: the shapes a contact drives, made local. " +
                    "A socket with a mesh of its own, origin at the entrance, gets the shader deform.");
            }
            if (noShapes > 0)
            {
                ctx.Report.Converted(Category,
                    $"{noShapes} socket(s) have no blendshapes to deform",
                    "Nothing was changed for these. A socket deform reshapes the author's own " +
                    "blendshapes, so a socket built without any has nothing to open with — its " +
                    "contacts and marker lights work exactly as before.");
            }
            if (failures.Count > 0)
            {
                ctx.Report.Warning(Category,
                    $"{failures.Count} socket(s) could not be given a deform",
                    "Everything else about them is untouched and they still work as sockets — " +
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
                    $"Found {found}, and no VRChat SPS setup. YAPS builds a plug from the objects " +
                    "VRChat's SPS bake leaves behind, and DPS and TPS predate all of that, so there " +
                    "is nothing here to build one from — the plug keeps a shader whose deform " +
                    "system does not exist in ChilloutVR, and it will not bend. Its SOCKETS are " +
                    "fine: their marker lights and contacts come through untouched, so other " +
                    "people's plugs can use them normally. To convert the plug too, set the avatar " +
                    "up with VRChat's SPS and convert again.");
            }
            else
            {
                ctx.Report.Converted(Category,
                    "Kept this avatar's existing penetration sockets",
                    $"Found {found} belonging to DPS or TPS rather than VRChat's SPS, so there was " +
                    "nothing for YAPS to build — but nothing was taken away either. The marker " +
                    "lights and contacts come through as they were, so plugs belonging to other " +
                    "players can use these sockets exactly as they did before. Leaving the YAPS " +
                    "setting ON is what keeps the contacts and the depth reactions that go with " +
                    "them; turning it off strips both.");
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
                    "VRChat's auto mode picks the socket nearest a plug and switches the moment " +
                    "another reads even fractionally closer, so two sockets a few centimetres " +
                    "apart flicker between each other with a plug in between. A socket now has " +
                    $"to be about {AutoModeMargin * 100:0} cm nearer than the active one before " +
                    "it takes over. Moving the plug from one socket to another still switches; " +
                    "sitting between them no longer does.");
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
                    var bones = new List<string> { ctx.PathInTarget(plug.ChainRoot) };
                    for (int i = 0; i < plug.ChainRoot.childCount; i++)
                    {
                        bones.Add(ctx.PathInTarget(plug.ChainRoot.GetChild(i)));
                    }
                    int along = YapsCurveMirror.AlongAxis(plug.ChainRoot, plug.Rotation);
                    scaled += YapsCurveMirror.MirrorBoneScale(clips, bones, path, plug.Renderer.GetType(), along);
                }
            }

            if (written > 0)
            {
                ctx.Report.Converted(Category, $"Mirrored {written} blendshape curve(s) onto the plug",
                    "A shader cannot read a blendshape weight, so every animation that moves one of " +
                    "the plug's own shapes now writes the same value onto its material as well. " +
                    "Without this the deform measures against a rest pose the mesh has already " +
                    "left, and a plug with a size slider bends as though it were still its " +
                    "original size.");
            }
            if (scaled > 0)
            {
                ctx.Report.Converted(Category, $"Mirrored {scaled} bone scale curve(s) onto the plug",
                    "The plug's root bone is scaled by an animation, a size or hyper toggle. The " +
                    "shader cannot see a bone's scale, so the same curve now drives the plug's bake " +
                    "scale on its material: at twice the size it reaches twice as far and bends as " +
                    "the bigger plug it is.");
            }
            if (missed.Count > 0)
            {
                ctx.Report.Warning(Category,
                    $"{missed.Count} animated plug blendshape(s) are not in the bake",
                    $"{string.Join(", ", missed.Take(8))}{(missed.Count > 8 ? ", …" : "")} — the bake " +
                    $"holds the {YapsBaker.MaxShapes} shapes that move the plug most, and these did " +
                    "not make the cut. They still change the mesh; the deform simply measures " +
                    "against the plug without them, so the bend is slightly off while they are " +
                    "raised. Bulge shapes on a SOCKET are unaffected by this — those are ordinary " +
                    "animation and have no limit.");
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
                "VRChat's version passes socket positions between avatars by drawing them into a " +
                "corner of the screen and reading them back. ChilloutVR publishes player positions " +
                "to shaders directly, so none of that machinery is needed here, and left in place " +
                "it would render marker quads into the view.");
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

        // StartsWith, not Contains: the first-person exclusion object is
        // named "FPRExclusion_BakedSpsSocket" and is not a socket.
        // Through Fury's ID prefix: ArmatureLink moves a socket to the head
        // and renames it "[VF724] BakedSpsSocket", and one of those stayed
        // Fury's for a whole day because this matched the raw name.
        static List<Transform> Named(BridgeContext ctx, string needle) =>
            ctx.Target.GetComponentsInChildren<Transform>(true)
                .Where(t => t != null && YapsScanner.StripFuryId(t.name).StartsWith(needle, System.StringComparison.Ordinal))
                .ToList();
    }
}
#endif
