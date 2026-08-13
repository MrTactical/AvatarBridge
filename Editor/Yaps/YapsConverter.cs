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
using System.Linq;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsConverter
    {
        const string Category = "YAPS";

        // The two digits the legacy DPS protocol never claimed. It speaks
        // for 1 and 3 (hole), 2 and 4 (ring), 5 and 6 (front) and 8 and 9
        // (a plug's own tip), and a decoder only reads the second decimal.
        // The second decimal is the protocol digit. The FOURTH is who owns
        // the socket — see _YAPS_SelfTag — and the converter picks it from
        // the avatar's name so reconverting the same avatar keeps it.
        const float RootRange = 0.4700f;
        const float FrontRange = 0.4000f;

        static float SelfTag(BridgeContext ctx)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in ctx.Target.name)
                {
                    hash = hash * 31 + c;
                }
                return Mathf.Abs(hash) % 10;
            }
        }

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
                return;
            }

            RemoveAtlasJunk(ctx);
            float selfTag = SelfTag(ctx);
            ReRangeSocketLights(ctx, socketRoots, selfTag);

            foreach (var plugRoot in plugRoots)
            {
                ConvertPlug(ctx, plugRoot);
            }
            foreach (var plug in ctx.YapsPlugs)
            {
                plug.Material.SetFloat("_YAPS_SelfTag", selfTag);
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

        static void ReRangeSocketLights(BridgeContext ctx, List<Transform> socketRoots, float selfTag)
        {
            float owner = selfTag * 0.0001f;
            int roots = 0, fronts = 0, left = 0;
            foreach (var socket in socketRoots)
            {
                foreach (var light in socket.GetComponentsInChildren<Light>(true))
                {
                    int digit = Digit(light.range);
                    if (digit == 1 || digit == 2 || digit == 3 || digit == 4)
                    {
                        light.range = RootRange + owner;
                        roots++;
                    }
                    else if (digit == 5 || digit == 6)
                    {
                        light.range = FrontRange + owner;
                        fronts++;
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
            ctx.Report.Converted(Category, $"Re-ranged {roots + fronts} socket marker light(s)",
                $"{roots} root, {fronts} front. A socket says where it is by the RANGE of a black " +
                "vertex light, and Unity gives the four light slots to the largest ranges it can " +
                "see. VRChat's ordering puts each socket's front above its own root, so on an " +
                "avatar with several sockets the slots fill with fronts — a direction with no " +
                "origin. Reversing the two makes roots win their slots." +
                (left > 0 ? $" {left} other marker light(s) left exactly as they were." : ""));
        }

        static int Digit(float range) => Mathf.RoundToInt(range % 0.1f * 100f);

        // --- the transport we do not port ------------------------------

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
                    Object.DestroyImmediate(transform.gameObject);
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
