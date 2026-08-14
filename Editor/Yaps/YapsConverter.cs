// YAPS — the pipeline pass. Turns VRCFury's baked SPS rig into something
// ChilloutVR can actually run.
//
// Inspired by VRCFury's SPS, which invented this technique for VRChat.
// No SPS code is used; see Tools/SpsSpike/LICENSE-POSTURE.md.
//
// ---------------------------------------------------------------------
// WHAT ARRIVES HERE, AND WHAT LEAVES
// ---------------------------------------------------------------------
//
// VRCFury's bake leaves behind, per plug, a "BakedSpsPlug" object marking
// the plug's frame; per socket, a "BakedSpsSocket" carrying contact
// senders and a pair of protocol lights; and, threaded through both, the
// machinery of SPS's own transport — a screen-space atlas built from grab
// passes and marker renderers. That transport is VRChat-only and we do not
// port it, so the markers and the resolver are deleted outright. The
// objects that carry meaning survive.
//
// The plug renderer is then baked (YapsBaker), its shader patched
// (YapsShaderPatcher), and its material cloned and pointed at both.
//
// The socket lights are re-ranged. VRCFury authors fronts above roots, and
// Unity ranks vertex lights by range, so on an avatar with a dozen sockets
// every front evicts its own root and the plug is left with a direction
// and no origin. Ours go the other way round.
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

        // The two digits the legacy DPS protocol never claimed. It speaks
        // for 1 and 3 (hole), 2 and 4 (ring), 5 and 6 (front) and 8 and 9
        // (a plug's own tip), and a decoder only reads the second decimal.
        //
        // Nothing is stamped into the fourth decimal any more. That was an
        // owner digit, and it rested on precision nobody had measured: the
        // range arrives in the shader reconstructed from an attenuation
        // uniform, not read, and the fourth decimal does not survive it.
        // Ownership is decided from the player positions instead.
        const float RootRange = 0.4700f;
        const float FrontRange = 0.4000f;

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
            // A flag, not a tag: sockets on this avatar mean its own plugs
            // must check ownership before reaching for one.
            float selfFlag = socketRoots.Count > 0 ? 1f : -1f;
            ReRangeSocketLights(ctx, socketRoots);

            foreach (var plugRoot in plugRoots)
            {
                ConvertPlug(ctx, plugRoot);
            }
            foreach (var plug in ctx.YapsPlugs)
            {
                plug.Material.SetFloat("_YAPS_SelfTag", selfFlag);
            }

            ConvertSockets(ctx, socketRoots);

            if (socketRoots.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"Kept the depth reactions on {socketRoots.Count} socket(s)",
                    "The animations a socket plays as a plug arrives — bulges, winces, whatever " +
                    "its author built — are kept and made local rather than thrown away. In " +
                    "VRChat each of those costs a synced parameter, which is why keeping them " +
                    "the naive way took one avatar to its entire sync budget. In ChilloutVR a " +
                    "contact is computed by every client independently, so a local parameter " +
                    "reaches everyone and costs nothing at all. There is no limit on how many " +
                    "shapes a socket drives this way.");
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

            var renderer = FindPlugRenderer(ctx, plugRoot, out int plugVertices);
            if (renderer == null)
            {
                ctx.Report.Warning(Category, $"No mesh found for the plug at {where}",
                    "No skinned renderer on this avatar has vertices weighted to the plug's bones, " +
                    "so there is nothing to bend. A plug built as a separate unskinned object is " +
                    "not supported yet.");
                return;
            }

            var result = YapsBaker.Bake(renderer, plugRoot, ctx.OutputDir + "/YAPS", ctx.Report,
                out string bakeFailure);
            if (result == null)
            {
                ctx.Report.Warning(Category, $"Could not bake the plug at {where}", bakeFailure);
                return;
            }

            // Which material is the plug's is decided by which one the
            // plug's own triangles use, not by name: a body mesh routinely
            // carries a dozen materials and only one of them is the shaft.
            int slot = MaterialSlotOf(renderer, plugRoot);
            var materials = renderer.sharedMaterials;
            if (slot < 0 || slot >= materials.Length || materials[slot] == null)
            {
                ctx.Report.Warning(Category, $"No material to patch for the plug at {where}",
                    "The renderer's material list does not cover the plug's triangles.");
                return;
            }

            var shader = YapsShaderPatcher.Patch(materials[slot], ctx.OutputDir + "/YAPS",
                ctx.Report, out string refusal, out int skippedShadowPasses);
            if (shader == null)
            {
                ctx.Report.Warning(Category, $"Could not add the deform to \"{materials[slot].name}\"",
                    $"{refusal}. The plug converts as an ordinary mesh — it will look right and " +
                    "simply will not bend.");
                return;
            }

            var patched = YapsBaker.Apply(result, materials[slot], shader, ctx.OutputDir + "/YAPS",
                result.FromSkinnedMesh);

            // The author's own choice about whether the tip may travel past
            // the socket, read off the plug component before the bake
            // destroyed it. Defaulting silently to yes was overriding a
            // decision somebody had made.
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
                Shapes = result.Shapes,
                Origin = result.Origin,
                Rotation = result.Rotation,
            });

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

        static Renderer FindPlugRenderer(BridgeContext ctx, Transform plugRoot, out int plugVertices)
        {
            Renderer best = null;
            plugVertices = 0;
            foreach (var renderer in ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int count = YapsBaker.CountPlugVertices(renderer, plugRoot);
                if (count > plugVertices)
                {
                    plugVertices = count;
                    best = renderer;
                }
            }
            return best;
        }

        // A submesh belongs to the plug if its triangles use the plug's
        // vertices. Nothing else identifies it — names are the author's
        // business and are routinely "Body".
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

        static void ReRangeSocketLights(BridgeContext ctx, List<Transform> socketRoots)
        {

            // Four vertex slots, two lights per socket. A dozen sockets means
            // a plug never sees a PAIR — it sees four of whichever light
            // ranks higher, which is four roots with no fronts or four
            // fronts with no roots, and neither of those is a socket.
            //
            // Both halves of that were measured. Fronts above roots gave a
            // legacy plug direction with no origin; putting roots above them
            // gave origin with no direction, and the working approach angle
            // flipped to the opposite side. Unreliable either way, because
            // which four win changes with every small movement.
            //
            // So most sockets stop emitting light altogether. They still
            // work for a converted plug, which finds sockets by contact and
            // only refines with light — this is about leaving room for the
            // readers that have nothing else. Holes first, since they are
            // what a plug is usually looking for.
            // First, hand the problem to the menu the wearer already has.
            // Every socket whose toggle can be found gets its lights wired
            // to it, so "one socket at a time" finally means one socket LIT
            // at a time and the contention is theirs to control.
            // VRCFury bakes every BakedSpsSocket INACTIVE and relies on SPS's
            // own enable service to switch it on. That service is part of
            // the transport we delete, so on a converted avatar the sockets
            // — and the marker lights beneath them — never come on at all.
            //
            // This is why nothing found Angela's sockets through any amount
            // of re-encoding: the lights were switched off the whole time,
            // component enabled, object active, branch above them dead.
            // Walk up from each LIGHT, not from the socket. The socket is
            // switched off, but so is the "Lights" object beneath it, and
            // waking only the socket leaves the branch under it dark —
            // which is exactly what a first attempt did.
            int woken = 0;
            foreach (var socket in socketRoots)
            {
                foreach (var light in socket.GetComponentsInChildren<Light>(true))
                {
                    for (var at = light.transform; at != null && at != ctx.Target.transform;
                         at = at.parent)
                    {
                        if (!at.gameObject.activeSelf)
                        {
                            at.gameObject.SetActive(true);
                            woken++;
                        }
                    }
                    if (!light.enabled)
                    {
                        light.enabled = true;
                        woken++;
                    }
                }
            }

            int wired = 0;
            var unwired = new List<Transform>();
            foreach (var socket in socketRoots)
            {
                // Every object from the light up to the socket, not just
                // the light. VRCFury switches the whole branch off —
                // BakedSpsSocket, WorldSpace, Lights — and animating only
                // the leaf leaves it dark under a dead parent, which is
                // exactly what the first two attempts did. Setting them at
                // conversion is not enough either, since the animator can
                // restore the baked state; the layer has to assert the
                // whole chain.
                var paths = new List<string>();
                foreach (var light in socket.GetComponentsInChildren<Light>(true))
                {
                    if (Digit(light.range) < 1 || Digit(light.range) > 6)
                    {
                        continue;
                    }
                    for (var at = light.transform; at != null && at != ctx.Target.transform;
                         at = at.parent)
                    {
                        string path = ctx.PathInTarget(at);
                        if (!paths.Contains(path))
                        {
                            paths.Add(path);
                        }
                        if (at == socket)
                        {
                            break;   // stop at the socket; above it is the body
                        }
                    }
                }
                string toggle = ToggleFor(ctx, socket);
                if (toggle != null && paths.Count > 0 && AddLightToggle(ctx, toggle, paths))
                {
                    wired++;
                }
                else
                {
                    unwired.Add(socket);
                }
            }

            // Anything with no toggle to hang off still has to be capped,
            // or one unlabelled socket set puts us back where we started.
            var emitting = socketRoots
                .Where(s => !unwired.Contains(s))
                .Concat(unwired.OrderBy(SocketRank)
                    .Take(Mathf.Max(1, ctx.Settings.maxLightEmittingSockets)))
                .ToList();
            int darkened = 0;
            foreach (var socket in socketRoots)
            {
                if (emitting.Contains(socket))
                {
                    continue;
                }
                foreach (var light in socket.GetComponentsInChildren<Light>(true))
                {
                    if (Digit(light.range) >= 1 && Digit(light.range) <= 6)
                    {
                        UnityEngine.Object.DestroyImmediate(light);
                        darkened++;
                    }
                }
            }

            int roots = 0, fronts = 0, left = 0, legacy = 0;
            foreach (var socket in emitting)
            {
                foreach (var light in socket.GetComponentsInChildren<Light>(true))
                {
                    int digit = Digit(light.range);
                    // A twin alongside our own encoding does not work: four
                    // vertex slots cannot hold a legacy root, a legacy front
                    // AND our root, and ours outranks both, so the twins
                    // never get a slot except when movement reshuffles the
                    // ranking. Measured in game as a legacy plug that
                    // reacted only while walking backwards.
                    //
                    // So it is one encoding or the other, and legacy wins
                    // the lights. A YAPS plug has three ways to find a
                    // socket and lights are the middle one; a DPS plug has
                    // lights and nothing else. Spending the slots on the
                    // reader who has no alternative is the whole trade.
                    if (digit == 1 || digit == 2 || digit == 3 || digit == 4)
                    {
                        // Legacy mode leaves the range EXACTLY as VRCFury
                        // baked it — 0.4106, 0.4206, 0.4506, keeping the
                        // trailing digits and everything else about them.
                        // Rewriting to a rounded 0.4100 was an unexamined
                        // change: those trailing digits are part of what
                        // every other SPS avatar on the platform emits, and
                        // the test prop that worked carries them. Matching
                        // the ecosystem byte for byte beats reasoning about
                        // which parts of it matter.
                        if (!ctx.Settings.emitLegacySocketLights)
                        {
                            light.range = RootRange;
                        }
                        roots++;
                        legacy += ctx.Settings.emitLegacySocketLights ? 1 : 0;
                    }
                    else if (digit == 5 || digit == 6)
                    {
                        // 0.45, the value legacy has always used, and NOT
                        // the 0.35 that briefly lived here.
                        //
                        // The first decimal is free to OUR decoder, which
                        // reads the second — so 0.35 still says "front"
                        // while ranking below every root, which looked like
                        // a clean way to stop twelve sockets' fronts
                        // evicting their roots. It is not: a DPS decoder
                        // gates on a range window around 0.4 and never saw
                        // them at all. Its plugs were left with a root and
                        // no front, which is a position with no axis, so
                        // they fell back to a fixed direction and engaged
                        // from one side only. The green test prop at a plain
                        // 0.4506 was omnidirectional for that same plug the
                        // whole time, which is what gave it away.
                        //
                        // The eviction this was solving is gone regardless:
                        // socket lights follow their menu toggle now, so one
                        // socket lit is two lights against four slots.
                        if (!ctx.Settings.emitLegacySocketLights)
                        {
                            light.range = FrontRange;
                        }
                        fronts++;
                        legacy += ctx.Settings.emitLegacySocketLights ? 1 : 0;
                    }
                    else
                    {
                        left++;
                        continue;
                    }
                    // Belt and braces on what the protocol assumes: a
                    // socket light is a marker, not lighting, and a
                    // vertex light is what keeps it out of ChilloutVR's
                    // Advanced Safety light budget entirely.
                    //
                    // Black, but intensity stays at ONE. Black is what
                    // stops it lighting anything and what lets the decoder
                    // separate protocol lights from real ones. Zeroing the
                    // intensity instead makes Unity drop the light from the
                    // per-object list altogether — it contributes nothing,
                    // so it never occupies a slot, so the socket cannot be
                    // seen at all. Every socket on the avatar goes dark.
                    light.color = Color.black;
                    light.intensity = 1f;
                    light.bounceIntensity = 0f;
                    light.shadows = LightShadows.None;
                    light.renderMode = LightRenderMode.ForceVertex;
                }
            }

            if (roots + fronts == 0)
            {
                return;
            }
            ctx.Report.Converted(Category, $"Re-ranged {roots + fronts} socket marker light(s)" +
                    (woken > 0 ? $", and switched {woken} socket object(s) back on" : ""),
                (woken > 0
                    ? "VRCFury bakes every socket object INACTIVE and lets its own enable service " +
                      "switch them on; that service is part of the transport this tool deletes, so " +
                      "on a converted avatar the sockets never came on and their marker lights " +
                      "never emitted anything at all. They are switched on at conversion now, and " +
                      "the menu decides which stay lit from there. "
                    : "") +
                $"{roots} root, {fronts} front. A socket says where it is by the RANGE of a black " +
                "vertex light, and Unity gives the four light slots to the largest ranges it can " +
                "see. VRChat's ordering puts each socket's front above its own root, so on an " +
                "avatar with several sockets the slots fill with fronts — a direction with no " +
                "origin. Reversing the two makes roots win their slots." +
                (legacy > 0
                    ? " These sockets speak LEGACY, so every DPS plug already on ChilloutVR can " +
                      "see them, which is most of the content there is, and from any direction " +
                      "rather than only from the front, because they carry a root AND a front " +
                      "the way DPS expects. A root on its own is a position with no axis, and a " +
                      "plug reading one falls back to a fixed direction and engages from one " +
                      "side only."
                    : " These sockets use our own ordering, which wins the light slots cleanly " +
                      "but is unreadable to DPS content — they will be invisible to every plug " +
                      "except another converted one.") +
                (wired > 0
                    ? $" {wired} socket(s) had their lights wired to the menu entry that already " +
                      "turns them on and off, which until now did nothing to the lights at all — " +
                      "VRChat's socket menu selects which socket a screen atlas publishes, and " +
                      "that atlas is the part of its transport this tool deletes. So an avatar set " +
                      "to \"one socket at a time\" was still emitting every light it had. Now the " +
                      "menu means what it says, and four vertex light slots are enough for " +
                      "whatever you have switched on."
                    : "") +
                (darkened > 0
                    ? $" {darkened} marker light(s) on {socketRoots.Count - emitting.Count} other " +
                      "socket(s) were removed. Unity gives a mesh four vertex light slots and this " +
                      "protocol needs two per socket, so a dozen sockets means a plug sees four of " +
                      "whichever light ranks higher — four roots with no fronts, or four fronts " +
                      "with no roots, and neither of those is a socket. Measured in game as a " +
                      "legacy plug whose working approach angle flipped to the opposite side when " +
                      "the ranking changed, and was unreliable in both. Those sockets still work " +
                      "for a converted plug, which finds them by contact and only refines with " +
                      "light; what they lose is being findable by DPS content, which has nothing " +
                      "else. Raise the limit if you would rather have more of them lit and accept " +
                      "that none of them resolve cleanly."
                    : "") +
                (left > 0 ? $" {left} other marker light(s) left exactly as they were." : ""));
        }


        //
        // A socket cannot say both things with one light. Within the 0.4x
        // band legacy roots are digits 1 to 4 and fronts are 5 and 6, so a
        // front always outranks its own root on Unity's range-based slot
        // ranking — the eviction measured at twelve sockets, and structural,
        // since no arrangement of legacy digits puts a root above its front.
        // Our own ordering fixes that and is unreadable to legacy in
        // exchange, because 7 and 0 mean nothing to it.
        //
        // So the socket says both. Ours wins the slot for a YAPS plug; the
        // twin is there for the DPS content already on the platform, which
        // is most of it.

        // Wire a socket's marker lights to the menu entry that already turns
        // that socket on and off.
        //
        // The toggles exist and do nothing to the lights. VRCFury's socket
        // menu drives a parameter that told SPS's screen atlas which socket
        // to publish, and the atlas is the one part of its transport we
        // delete — so on a converted avatar every socket stays lit whatever
        // the menu says, and an avatar with "one socket at a time" still
        // emits two dozen lights. Nothing in the controller touches
        // m_IsActive on a socket at all.
        //
        // That matters because four vertex light slots cannot carry a dozen
        // sockets. Wiring the existing menu to the lights hands that problem
        // to the person best placed to solve it: the wearer, who already has
        // a control that says exactly what they want lit.
        static string ToggleFor(BridgeContext ctx, Transform socket)
        {
            var names = ctx.CvrAvatar.avatarSettings.settings
                .Select(e => e.machineName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            // Walk up from the socket: VRCFury names the object the author
            // made, and the menu entry is named after the same thing.
            for (var at = socket; at != null && at != ctx.Target.transform; at = at.parent)
            {
                string mine = Normalise(at.name);
                if (mine.Length < 3)
                {
                    continue;
                }
                // Longest match first, so "SteppiesLeft" wins over anything
                // that merely starts the same way.
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

        // "[VF958] Blowjob" and "VF80_Blowjob" and "Handjob Left" against
        // "HandjobLeft" all have to meet in the middle. Fury's own numbering
        // is noise, and "Target" is a word it adds to the object but not to
        // the menu.
        static string Normalise(string name)
        {
            string clean = System.Text.RegularExpressions.Regex.Replace(name, @"\[VF\d+\]", "");
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"^VF\d+_", "");
            clean = clean.Replace("Target", "").Replace(" ", "").Replace("_", "");
            return clean;
        }

        // Holes first, then rings, then anything unlabelled — so the sockets
        // that keep their lights are the ones a plug is most likely to be
        // looking for.
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

        // Two states and a parameter. Not a blend tree: m_IsActive is a
        // switch, and blending one halfway is meaningless.
        static bool AddLightToggle(BridgeContext ctx, string parameter, List<string> lightPaths)
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
                // Built in memory, so it has to become part of the asset or
                // Unity drops the lot on save and leaves an empty layer.
                AssetDatabase.AddObjectToAsset(machine, controller);
                AssetDatabase.AddObjectToAsset(on, controller);
                AssetDatabase.AddObjectToAsset(off, controller);
            }

            // Default ON, and that is deliberate. A layer that starts Off
            // and relies on a transition to switch the lights on fails
            // DARK: if the parameter never arrives, or arrives before the
            // layer is evaluated, or is driven some way this does not
            // expect, every socket on the avatar goes silent and the whole
            // feature looks broken. Starting On means the worst case is the
            // behaviour we had before any of this existed — all sockets
            // lit, contending for slots — which is degraded rather than
            // dead.
            var onState = machine.AddState("On");
            onState.writeDefaultValues = false;
            onState.motion = on;
            var offState = machine.AddState("Off");
            offState.writeDefaultValues = false;
            offState.motion = off;
            machine.defaultState = onState;

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

        // Enables the LIGHT COMPONENT, not the GameObject. Animating
        // m_IsActive churns the object every frame the curve is applied,
        // which re-registers the light with Unity and can leave it missing
        // from the per-object light lists a decoder reads — a socket that
        // resolves only while something is moving. Toggling the component
        // leaves the object alone.
        static AnimationClip ClipFor(string name, List<string> paths, float active)
        {
            var clip = new AnimationClip { name = name };
            foreach (string path in paths)
            {
                // The OBJECT, for every link in the chain. The Light
                // components are already enabled — that was never the
                // problem — and adding a component curve to the objects
                // above them would only bind to nothing.
                clip.SetCurve(path, typeof(GameObject), "m_IsActive",
                    AnimationCurve.Constant(0f, 1f / 60f, active));
            }
            return clip;
        }

        static int Digit(float range) => Mathf.RoundToInt(range % 0.1f * 100f);

        // DPS and TPS predate VRCFury entirely, so an avatar carrying them
        // has no BakedSps markers and this pass finds nothing to do. It
        // used to return in silence, which left the owner of a perfectly
        // ordinary older avatar unable to tell whether the feature had run,
        // was unsupported, or had broken.
        //
        // The answer is worth saying, because it is not "nothing happened":
        // their SOCKETS come through and work, and their PLUG does not.
        // Give a socket a deform of its own, so it opens around what
        // arrives instead of sitting rigid.
        //
        // THE RULE THAT DECIDES EVERYTHING HERE: a shape driven by BOTH the
        // animator and the shader applies TWICE. The animator sets the
        // blendshape weight, so the mesh reaches the vertex shader already
        // bulged, and the shader then adds the baked delta of the same
        // shape on top.
        //
        // They never have to fire together, though. A plug carrying
        // CONTACTS moves the author's own parameters, so the animator
        // handles it — with the author's curves, no shape limit, and the
        // winces and material swaps the shader cannot touch at all. A plug
        // with NO contacts, which is every piece of Raliv DPS content,
        // moves nothing, and the animator is inert exactly when the shader
        // should act.
        //
        // So the shader covers what the animator cannot reach and stands
        // down where it can, and the switch is simply whether we publish a
        // depth for it: left at -1, the shader falls back to reading a
        // plug's tracker light and never fights the animator.
        static void ConvertSockets(BridgeContext ctx, List<Transform> socketRoots)
        {
            if (socketRoots.Count == 0)
            {
                return;
            }

            int deformed = 0, alreadyAnimated = 0, noShapes = 0;
            var failures = new List<string>();

            foreach (var socketRoot in socketRoots)
            {
                var renderer = SocketRenderer(socketRoot);
                if (renderer == null || MeshOf(renderer) == null
                    || MeshOf(renderer).blendShapeCount == 0)
                {
                    noShapes++;
                    continue;
                }

                var mesh = MeshOf(renderer);
                bool animatorDrivesIt = AnimatorDrivesShapes(ctx, renderer);

                var result = YapsBaker.Bake(renderer, socketRoot, ctx.OutputDir + "/YAPS",
                    null, out string failure, shapesInMeshOrder: true);
                if (result == null)
                {
                    failures.Add($"{socketRoot.name}: {failure}");
                    continue;
                }

                int slot = MaterialSlotOf(renderer, socketRoot);
                var materials = renderer.sharedMaterials;
                var patched = YapsShaderPatcher.Patch(materials[slot], ctx.OutputDir + "/YAPS",
                    ctx.Report, out string refusal, out _);
                if (patched == null)
                {
                    failures.Add($"{socketRoot.name}: {refusal}");
                    continue;
                }

                var material = YapsBaker.Apply(result, materials[slot], patched,
                    ctx.OutputDir + "/YAPS", renderer is SkinnedMeshRenderer);
                material.SetFloat("_YAPS_SocketPower", 1f);
                // The plug half of this material must stay asleep. One
                // shader carries both ends and each guards on its own
                // enable; a socket that also thought it was a plug would
                // try to bend itself at the nearest socket.
                material.SetFloat("_YAPS_Enabled", 0f);
                // -1, never 0. Zero is "a plug is here and not yet in";
                // -1 is "nobody has told me anything", which is what lets
                // the shader fall back to lights.
                material.SetFloat("_YAPS_SocketDepth", -1f);

                materials[slot] = material;
                renderer.sharedMaterials = materials;

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

        // The renderer wearing this socket's mesh. A socket object is a
        // marker; the mesh it belongs to is usually a parent, since an
        // author hangs the socket off the body they want reshaped.
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

        // Whether anything in the controller already animates a blendshape
        // on this renderer. Only informational: the shader stands down on
        // depth rather than on this, so a wrong answer changes what the
        // report says and nothing else.
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

        // --- the transport we do not port ------------------------------

        // A vertex shader cannot read a blendshape weight, so every clip
        // that drives one of the plug's shapes gets a parallel curve
        // writing the same value onto the material.
        //
        // Registered as a clip-editing pass, after ownership settles: these
        // are the conversion's own copies, and editing a shared clip would
        // reach the source package.
        public static void MirrorShapeCurves(BridgeContext ctx)
        {
            if (!ctx.Settings.convertYapsSystems || ctx.YapsPlugs.Count == 0
                || ctx.MergedController == null)
            {
                return;
            }

            int written = 0;
            var missed = new SortedSet<string>(StableSampleOrder.Instance);
            foreach (var plug in ctx.YapsPlugs)
            {
                if (plug.Shapes.Count == 0)
                {
                    continue;
                }
                string path = ctx.PathInTarget(plug.Renderer.transform);
                foreach (var clip in ctx.MergedController.animationClips.Distinct())
                {
                    if (clip == null)
                    {
                        continue;
                    }
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.path != path
                            || !binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                        {
                            continue;
                        }
                        string shape = binding.propertyName.Substring("blendShape.".Length);
                        int slot = plug.Shapes.IndexOf(shape);
                        if (slot < 0)
                        {
                            missed.Add(shape);
                            continue;
                        }

                        // Unity animates a blendshape from 0 to 100 and the
                        // bake stores the shape at full, so the material
                        // wants the same curve scaled to 0..1.
                        var source = AnimationUtility.GetEditorCurve(clip, binding);
                        var scaled = new AnimationCurve();
                        foreach (var key in source.keys)
                        {
                            scaled.AddKey(new Keyframe(key.time, key.value * 0.01f,
                                key.inTangent * 0.01f, key.outTangent * 0.01f));
                        }
                        AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
                        {
                            path = path,
                            type = plug.Renderer.GetType(),
                            propertyName = "material." + WeightProperty(slot),
                        }, scaled);
                        written++;
                    }
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

        // Four float4s, sixteen shapes, matching SPS.
        static string WeightProperty(int slot)
        {
            string pack = slot < 4 ? "_YAPS_ShapeWeights"
                        : slot < 8 ? "_YAPS_ShapeWeights2"
                        : slot < 12 ? "_YAPS_ShapeWeights3"
                        : "_YAPS_ShapeWeights4";
            return pack + "." + "xyzw"[slot & 3];
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

        static List<Transform> Named(BridgeContext ctx, string needle) =>
            ctx.Target.GetComponentsInChildren<Transform>(true)
                .Where(t => t != null && t.name.Contains(needle))
                .ToList();
    }
}
#endif
