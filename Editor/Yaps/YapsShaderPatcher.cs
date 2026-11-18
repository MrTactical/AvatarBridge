// Puts the YAPS deform into an avatar's own body shader.
//
// Inspired by VRCFury's SPS. No SPS code is read or emitted here.
// See docs/YAPS-CLEAN-ROOM.md.
//
// Clones the source, adds properties, edits the vertex stage of EVERY pass (or the shadow
// stays straight), compiles, then repoints the material. Includes are inlined so the copy
// survives in a bundle. Struct members are found by semantic (vertex, pos, positionOS...).
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsShaderPatcher
    {
        const string Category = "YAPS";

        // Which deforms a patch compiles; each costs its compile in every pass (the plug's was
        // 10 s of a squash copy's 12). Jelly only by name, so All keeps its old meaning.
        // JellyUnread: how many of the four jelly slots go unread, a number in two bits.
        [Flags]
        public enum Roles { Plug = 1, Socket = 2, Squash = 4, All = Plug | Socket | Squash, Jelly = 8, JellyUnread = 48 }

        public static Roles JellyReading(int slots) => (Roles) ((4 - Mathf.Clamp(slots, 1, 4)) << 4);

        public const string RolesProperty = "_YAPS_Roles";

        // A patch made before roles, or for all of them, carries no such property.
        public static Roles RolesOf(Shader shader)
        {
            int i = shader != null ? shader.FindPropertyIndex(RolesProperty) : -1;
            return i < 0 ? Roles.All : (Roles) Mathf.RoundToInt(shader.GetPropertyDefaultFloatValue(i));
        }

        static string RolesDeclaration(Roles roles) => roles == Roles.All ? ""
            : $"        [HideInInspector] {RolesProperty} (\"YAPS deforms compiled in\", Float) = {(int) roles}\n";

        // One shared folder, names hashed from inputs, so a repeat patch reuses the compiled copy.
        // Include paths are compiled in, so a folder per output recompiled each, ~1 min a shader.
        public const string SharedFolder = "Assets/YAPS/Generated/Shaders";

        // Hashed from the emitted code, not a hand-bumped constant, which was forgotten and
        // left patched shaders silently stale.
        static string EmittedVersion(string yapsSource) => Hash(yapsSource + PropertyBlock + FogPerPixel + VertexLightPixelOnly);

        // Fog's vertex half on PC is the same in every mode, the clip depth. Passed always and
        // turned into fog per pixel always, every fog mode shares one vertex program and a no-fog
        // pixel program still lines up with it. Without this, a vertex program compiled once
        // would write no depth and the fog variants would read none.
        const string FogPerPixel = @"
#undef UNITY_FOG_COORDS
#define UNITY_FOG_COORDS(idx) UNITY_FOG_COORDS_PACKED(idx, float1)
#undef UNITY_TRANSFER_FOG
#define UNITY_TRANSFER_FOG(o,outpos) o.fogCoord.x = (outpos).z
#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
#undef UNITY_APPLY_FOG_COLOR
#define UNITY_APPLY_FOG_COLOR(coord,col,fogCol) UNITY_CALC_FOG_FACTOR((coord).x); UNITY_FOG_LERP_COLOR(col,fogCol,unityFogFactor)
#endif
";

        // Anything that works fog out by itself, so the three macros above are not the whole of
        // it: Unity's own Standard, particle and terrain code packs fog into other members.
        static readonly Regex OwnFog = new Regex(
            @"\bFOG_(LINEAR|EXP2?)\b|UNITY_TRANSFER_FOG_COMBINED|UNITY_EXTRACT_FOG|UNITY_CALC_FOG_FACTOR|unityFogFactor|unity_FogParams" +
            @"|#include\s+""(UnityStandardCore|UnityStandardParticles|TerrainSplatmapCommon|UnityBuiltin2xTreeLibrary)");

        static readonly Regex FogPragma = new Regex(@"#pragma\s+multi_compile_fog\b[^\n]*");
        static readonly Regex UnityCgInclude = new Regex(@"#include\s+""UnityCG\.cginc""[^\n]*\n");

        // Fog keywords out of every vertex program that only reaches fog through Unity's three
        // macros: a body pass compiled four times over, once per fog mode, for identical code.
        // A pass that does anything else with fog, or reaches UnityCG some other way, is left.
        static void FogInPixelOnly(List<ShaderSpiPatcher.SourceFile> unit)
        {
            var file = unit[0];
            string code = ShaderSpiPatcher.Uncommented(file.Text);
            var shared = Regex.Matches(code, @"(?:CG|HLSL)INCLUDE([\s\S]*?)END(?:CG|HLSL)").Cast<Match>().ToList();
            var edits = new SortedDictionary<int, (int cut, string put)>();
            foreach (Match block in Regex.Matches(code, @"(?:CG|HLSL)PROGRAM([\s\S]*?)END(?:CG|HLSL)"))
            {
                var body = block.Groups[1];
                var pragma = FogPragma.Match(code, body.Index, body.Length);
                if (!pragma.Success) continue;
                var include = UnityCgInclude.Match(code, body.Index, body.Length);
                if (!include.Success)
                    include = shared.Where(s => s.Index < block.Index).Select(s => UnityCgInclude.Match(code, s.Groups[1].Index, s.Groups[1].Length))
                        .LastOrDefault(m => m.Success) ?? include;
                if (!include.Success) continue;
                var reached = Reached(unit, code, body.Index, body.Index + body.Length).Skip(1).Select(f => ShaderSpiPatcher.Uncommented(f.Text));
                if (OwnFog.IsMatch(body.Value) || shared.Any(s => OwnFog.IsMatch(s.Groups[1].Value)) || reached.Any(OwnFog.IsMatch)) continue;
                edits[pragma.Index] = (pragma.Length, "#pragma multi_compile_fragment _ FOG_LINEAR FOG_EXP FOG_EXP2");
                edits[include.Index + include.Length] = (0, FogPerPixel);
            }
            foreach (var edit in edits.Reverse())
                file.Text = file.Text.Remove(edit.Key, edit.Value.cut).Insert(edit.Key, edit.Value.put);
        }

        static readonly Regex VertexLightPragma = new Regex(@"#pragma\s+multi_compile\s+_\s+VERTEXLIGHT_ON[ \t]*(?=\r?\n)");
        const string VertexLightPixelOnly = "#pragma multi_compile_fragment _ VERTEXLIGHT_ON";
        static readonly string[][] LightSets =
        {
            new[] { "DIRECTIONAL" }, new[] { "DIRECTIONAL", "SHADOWS_SCREEN" },
            new[] { "DIRECTIONAL", "LIGHTPROBE_SH" }, new[] { "DIRECTIONAL", "SHADOWS_SCREEN", "LIGHTPROBE_SH" },
        };

        // Older Poiyomi declares the vertex-light keyword for both stages and reads it per pixel
        // only, so every patched vertex program compiled twice over for the same bytes, a third
        // of a patch's first-upload compile. Pixel-only, as later Poiyomi declares it, where the
        // host's own vertex programs prove it: the same bytecode with the keyword and without.
        static void VertexLightInPixelOnly(List<ShaderSpiPatcher.SourceFile> unit, Material host)
        {
            var file = unit[0];
            if (!VertexLightPragma.IsMatch(file.Text) || !VertexIgnores(host, "VERTEXLIGHT_ON")) return;
            file.Text = VertexLightPragma.Replace(file.Text, VertexLightPixelOnly);
        }

        static bool VertexIgnores(Material host, string keyword)
        {
            int passes = 0;
            try
            {
                var data = ShaderUtil.GetShaderData(host.shader);
                for (int s = 0; s < data.SerializedSubshaderCount; s++)
                {
                    var sub = data.GetSerializedSubshader(s);
                    for (int p = 0; p < sub.PassCount; p++)
                    {
                        var pass = sub.GetPass(p);
                        if (pass.IsGrabPass || !pass.HasShaderStage(UnityEditor.Rendering.ShaderType.Vertex)
                            || !VertexLightPragma.IsMatch(pass.SourceCode + "\n")) continue;
                        foreach (var set in LightSets)
                        {
                            var words = set.Concat(host.shaderKeywords).Distinct().ToArray();
                            var off = pass.CompileVariant(UnityEditor.Rendering.ShaderType.Vertex, words,
                                UnityEditor.Rendering.ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
                            var on = pass.CompileVariant(UnityEditor.Rendering.ShaderType.Vertex, words.Append(keyword).ToArray(),
                                UnityEditor.Rendering.ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
                            if (!off.Success || !on.Success || off.ShaderData == null || off.ShaderData.Length == 0
                                || !off.ShaderData.SequenceEqual(on.ShaderData)) return false;
                        }
                        passes++;
                    }
                }
            }
            catch (Exception) { return false; }
            return passes > 0;
        }

        // What Properties{} needs. Distinct from the HLSL declarations in
        // yaps_props.cginc: Unity needs its own syntax here, and only
        // properties that must be animatable or inspectable belong.
        const string PropertyBlock = @"
        [Header(YAPS)]
        _YAPS_Bake (""YAPS baked data"", 2D) = ""black"" {}
        _YAPS_VertexCount (""YAPS vertex count"", Float) = 0
        _YAPS_Enabled (""YAPS enabled"", Range(0,1)) = 1
        _YAPS_Length (""YAPS plug length"", Float) = 1
        _YAPS_Overrun (""YAPS allow overrun"", Range(0,1)) = 1
        _YAPS_BakeScale (""YAPS bake scale"", Float) = 1
        _YAPS_BakeGirth (""YAPS bake girth"", Float) = 1
        [HideInInspector] _YAPS_PlugLane (""YAPS plug lane"", Float) = 0
        _YAPS_BakeScale2 (""YAPS bake scale, second plug"", Float) = 1
        _YAPS_BakeGirth2 (""YAPS bake girth, second plug"", Float) = 1
        _YAPS_Enabled2 (""YAPS enabled, second plug"", Range(0,1)) = 1
        _YAPS_SelfAllow2 (""YAPS own body allowed, second plug"", Range(0,1)) = 0
        _YAPS_BakeSkinScale (""YAPS bake skin scale"", Float) = 0
        _YAPS_PortalSpan (""YAPS portal band (-1 too coarse)"", Float) = 0
        _YAPS_CopyStart (""YAPS second copy's first vertex"", Float) = 0
        _YAPS_FrameFromVertex (""YAPS frame from vertex"", Range(0,1)) = 0
        _YAPS_SocketPos (""YAPS socket position"", Vector) = (0,0,0,0)
        _YAPS_SocketForward (""YAPS socket forward"", Vector) = (0,0,0,0)
        _YAPS_SocketUp (""YAPS socket up"", Vector) = (0,0,0,0)
        _YAPS_SocketFlags (""YAPS socket flags"", Vector) = (0,0,0,0)
        _YAPS_SocketFront (""YAPS socket front"", Vector) = (0,0,0,0)
        _YAPS_ChannelSpace (""YAPS channel space"", Range(0,1)) = 0
        _YAPS_ChannelOrigin (""YAPS channel origin"", Vector) = (0,0,0,0)
        _YAPS_ChannelForward (""YAPS channel forward"", Vector) = (0,0,0,0)
        _YAPS_ChannelUp (""YAPS channel up"", Vector) = (0,0,0,0)
        _YAPS_ChannelExtents (""YAPS channel extents"", Vector) = (1,1,1,0)
        _YAPS_SelfTag (""YAPS self tag"", Float) = -1
        _YAPS_SelfAllow (""YAPS own body allowed"", Range(0,1)) = 0
        _YAPS_Owner (""YAPS owner id"", Float) = 0
        _YAPS_SelfSockets (""YAPS own sockets it may enter"", Float) = 0
        _YAPS_SelfChosen (""YAPS own sockets past its tags"", Float) = 0
        _YAPS_PlugIndex (""YAPS plug number"", Float) = 0
        _YAPS_UseAtlas (""YAPS read the screen atlas"", Range(0,1)) = 0
        [Enum(Off,0,Resolved by,1,Gap to socket,2,Engagement,3,Socket facing,4,Atlas taps,5,Atlas target,6)] _YAPS_Debug (""YAPS view"", Float) = 0
        _YAPS_TaperStart (""YAPS hole taper start"", Range(0,1)) = 0.10
        _YAPS_TaperEnd (""YAPS hole taper end"", Range(0,1)) = 0.30
        _YAPS_IdleLength (""YAPS idle length"", Range(0.1,1)) = 1
        _YAPS_IdleWidth (""YAPS idle width"", Range(0.1,1)) = 1
        _YAPS_Squeeze (""YAPS squeeze"", Range(0,1)) = 0
        _YAPS_SqueezeDistance (""YAPS squeeze reach"", Range(0.01,1)) = 0.15
        _YAPS_Bulge (""YAPS bulge"", Range(0,1)) = 0
        _YAPS_BulgeDistance (""YAPS bulge reach"", Range(0.01,1)) = 0.2
        _YAPS_BulgeFalloff (""YAPS bulge falloff"", Range(0,0.5)) = 0
        _YAPS_PumpStrength (""YAPS pumping"", Range(0,0.5)) = 0
        _YAPS_PumpSpeed (""YAPS pumping speed"", Range(0,20)) = 6
        _YAPS_PumpWidth (""YAPS pumping width"", Range(0.05,1)) = 1
        _YAPS_WriggleStrength (""YAPS wriggle"", Range(0,0.5)) = 0
        _YAPS_WriggleSpeed (""YAPS wriggle speed"", Range(0,20)) = 2
        [Header(YAPS shape at rest)]
        _YAPS_Curvature (""YAPS curvature"", Range(-1.5,1.5)) = 0
        _YAPS_ReCurvature (""YAPS recurvature"", Range(-1.5,1.5)) = 0
        _YAPS_EntranceStiffness (""YAPS entrance stiffness"", Range(0,1)) = 0
        [Header(YAPS curve)]
        _YAPS_BezierSmoothness (""YAPS bezier smoothness"", Range(0.2,3)) = 1
        _YAPS_BezierStart (""YAPS straight before bend"", Range(0,0.8)) = 0
        _YAPS_SmoothStart (""YAPS ease into bend"", Range(0,0.5)) = 0
        _YAPS_MinimumSocketDistance (""YAPS minimum socket distance"", Range(0,1)) = 0
        [Header(YAPS tags)]
        _YAPS_TagInclude (""YAPS only sockets tagged"", Vector) = (0,0,0,0)
        _YAPS_TagInclude2 (""YAPS only sockets tagged, second plug"", Vector) = (0,0,0,0)
        _YAPS_TagExclude (""YAPS never sockets tagged"", Vector) = (0,0,0,0)
        _YAPS_ShapeCount (""YAPS shape count"", Float) = 0
        _YAPS_ShapeWeights (""YAPS shape weights 0-3"", Vector) = (0,0,0,0)
        _YAPS_ShapeWeights2 (""YAPS shape weights 4-7"", Vector) = (0,0,0,0)
        _YAPS_ShapeWeights3 (""YAPS shape weights 8-11"", Vector) = (0,0,0,0)
        _YAPS_ShapeWeights4 (""YAPS shape weights 12-15"", Vector) = (0,0,0,0)
        [Header(YAPS socket)]
        _YAPS_SocketPower (""YAPS socket power"", Range(0,1)) = 0
        _YAPS_SocketDepth (""YAPS socket depth (-1 none)"", Range(-1,1)) = -1
        _YAPS_SocketOrigin (""YAPS socket origin, mesh local"", Vector) = (0,0,0,0)
        _YAPS_SocketNoSelfExclude (""YAPS socket takes the wearer's own plug"", Range(0,1)) = 0
        _YAPS_SocketShapeStart (""YAPS socket shape starts 0-3"", Vector) = (0, 0.25, 0.5, 0.75)
        _YAPS_SocketShapeStart2 (""YAPS socket shape starts 4-7"", Vector) = (0, 0, 0, 0)
        _YAPS_SocketShapeStart3 (""YAPS socket shape starts 8-11"", Vector) = (0, 0, 0, 0)
        _YAPS_SocketShapeStart4 (""YAPS socket shape starts 12-15"", Vector) = (0, 0, 0, 0)
        _YAPS_SocketShapeFade (""YAPS socket shape fades 0-3"", Vector) = (0.3, 0.3, 0.3, 0.3)
        _YAPS_SocketShapeFade2 (""YAPS socket shape fades 4-7"", Vector) = (0.3, 0.3, 0.3, 0.3)
        _YAPS_SocketShapeFade3 (""YAPS socket shape fades 8-11"", Vector) = (0.3, 0.3, 0.3, 0.3)
        _YAPS_SocketShapeFade4 (""YAPS socket shape fades 12-15"", Vector) = (0.3, 0.3, 0.3, 0.3)
        [Header(YAPS dent)]
        _YAPS_DentPower (""YAPS soft-body dent"", Range(0,1)) = 0
        _YAPS_DentSpill (""YAPS squish spill, 1 keeps the volume"", Range(0,2)) = 1
        [NoScaleOffset] _YAPS_DentOwn (""YAPS dent part shares, per vertex"", 2D) = ""black"" {}
        [NoScaleOffset] _YAPS_DentSwing (""YAPS dent part swings, per vertex"", 2D) = ""white"" {}
        [NoScaleOffset] _YAPS_SquashSurface (""YAPS squash part surfaces, 32 rows a part"", 2D) = ""black"" {}
        _YAPS_SquashSurfaceOn (""YAPS squash: 1 where a part reads its own surface"", Vector) = (0,0,0,0)
        _YAPS_SquashReach (""YAPS squash reach per part toward its partner, follow units"", Vector) = (0,0,0,0)
        _YAPS_SquashAway (""YAPS squash reach per part away from its partner"", Vector) = (0,0,0,0)
        _YAPS_SquashOut (""YAPS squash reach per part out of the body"", Vector) = (0,0,0,0)
        _YAPS_SquashIn (""YAPS squash reach per part into the body"", Vector) = (0,0,0,0)
        _YAPS_SquashSideA (""YAPS squash reach per part across, one way"", Vector) = (0,0,0,0)
        _YAPS_SquashSideB (""YAPS squash reach per part across, the other way"", Vector) = (0,0,0,0)
        _YAPS_SquashHangToward (""YAPS squash hang point per part toward its partner, off its rest"", Vector) = (0,0,0,0)
        _YAPS_SquashHangOut (""YAPS squash hang point per part out of the body"", Vector) = (0,0,0,0)
        _YAPS_SquashHangAcross (""YAPS squash hang point per part across"", Vector) = (0,0,0,0)
        _YAPS_SquashPair (""YAPS squash pairs: start, first share; start, first share"", Vector) = (0,0.5,0,0.5)
        _YAPS_SquashShape (""YAPS squash: flattest, volume kept, widest, contact"", Vector) = (0.6,1,1.5,1)
        _YAPS_SquashPulse (""YAPS squash test pulse"", Float) = 0
        _YAPS_SquashPush (""YAPS squash: push gain, push ignored below, arm edge, arms on"", Vector) = (0,0.03,0.25,1)
        _YAPS_SquashLimbL (""YAPS squash left arm radii: upper, lower, hand"", Vector) = (0.2,0.18,0.25,0)
        _YAPS_SquashLimbR (""YAPS squash right arm radii: upper, lower, hand"", Vector) = (0.2,0.18,0.25,0)
        _YAPS_SquashHands (""YAPS squash hands: own gain, others radius, others gain, skin stride"", Vector) = (1,0.05,1,0)
        _YAPS_SquashTune (""YAPS squash: lying-back weight, strength, bend give, deepest dent"", Vector) = (0.25,1,0.35,0.35)
        _YAPS_SquashPlug (""YAPS squash plug press gain"", Range(0,2)) = 1
        [NoScaleOffset] _YAPS_JellyState (""YAPS live tissue state"", 2D) = ""black"" {}
        _YAPS_JellyLattice (""YAPS live tissue: particles across, up, deep; spacing"", Vector) = (0,0,0,0)
        [NoScaleOffset] _YAPS_JellyState2 (""YAPS live tissue state, the second part"", 2D) = ""black"" {}
        _YAPS_JellyLattice2 (""YAPS live tissue: the second part's lattice"", Vector) = (0,0,0,0)
        [NoScaleOffset] _YAPS_JellyState3 (""YAPS live tissue state, the third part"", 2D) = ""black"" {}
        _YAPS_JellyLattice3 (""YAPS live tissue: the third part's lattice"", Vector) = (0,0,0,0)
        [NoScaleOffset] _YAPS_JellyState4 (""YAPS live tissue state, the fourth part"", 2D) = ""black"" {}
        _YAPS_JellyLattice4 (""YAPS live tissue: the fourth part's lattice"", Vector) = (0,0,0,0)
        [NoScaleOffset] _YAPS_JellyOwn (""YAPS live tissue parts, per vertex"", 2D) = ""black"" {}
        _YAPS_JellyParts (""YAPS live tissue: the parts it carries"", Vector) = (0,0,0,0)
        _YAPS_JellySlap (""YAPS live tissue: a slap's ring, how much (0 none), speed, wavelength, fade"", Vector) = (0,1,0.1,0.4)
        [NoScaleOffset] _YAPS_SoftRow (""YAPS soft body rows, per vertex"", 2D) = ""black"" {}
        [NoScaleOffset] _YAPS_SoftTable (""YAPS soft body answers"", 2D) = ""black"" {}
        _YAPS_SoftDepth (""YAPS soft body depths per pair, 0 unbaked"", Vector) = (0,0,0,0)
        _YAPS_SoftOut (""YAPS soft body reach per part, outside"", Vector) = (0,0,0,0)
        _YAPS_SoftIn (""YAPS soft body reach per part, the partner's side"", Vector) = (0,0,0,0)
        _YAPS_SoftFront (""YAPS soft body reach per part, in front"", Vector) = (0,0,0,0)
        _YAPS_SoftSideA (""YAPS soft body reach per part, across one way"", Vector) = (0,0,0,0)
        _YAPS_SoftSideB (""YAPS soft body reach per part, across the other way"", Vector) = (0,0,0,0)
        _YAPS_SoftBelow (""YAPS soft body across toward the feet per pair, 1 or -1"", Vector) = (0,0,0,0)
";

        // The name a patch made now would carry, so a rebuild notices shader fixes.
        // Null means "leave it alone", not "stale".
        public static string CurrentNameFor(Material patchedOrOriginal)
        {
            if (patchedOrOriginal == null || patchedOrOriginal.shader == null) return null;
            var shader = patchedOrOriginal.shader;
            bool duplicate = Duplicate(patchedOrOriginal);
            if (NameCache.TryGetValue((shader, duplicate), out string cached)) return cached;
            // A patched material names its own source; an unpatched one is
            // its own source.
            var from = OriginalShaderOf(patchedOrOriginal) ?? shader;
            string sourcePath = ShaderSpiPatcher.SourcePathOf(from);
            if (string.IsNullOrEmpty(sourcePath)) return null;
            var unit = ShaderSpiPatcher.ReadUnit(sourcePath);
            if (unit.Count == 0) return null;
            string yaps = YapsSourceFor(duplicate, RolesOf(shader), out _);
            if (yaps == null) return null;
            return NameCache[(shader, duplicate)] = PatchedName(unit[0].Text, NameHash(sourcePath, unit, yaps));
        }

        // Valid until something imports; the material panel asks per GUI event and each
        // answer hashes the include closure. Failures are not cached.
        static readonly Dictionary<(Shader, bool), string> NameCache = new Dictionary<(Shader, bool), string>();

        sealed class NameCacheReset : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                NameCache.Clear();
                YapsFolder = null;
                YapsRead = null;
            }
        }

        // The source TEXT is in it, not only the path. Updating or re-locking a
        // shader rewrites the same file, so a name blind to that never went
        // stale, and a re-patch wrote over the copy other materials still used.
        static string NameHash(string sourcePath, List<ShaderSpiPatcher.SourceFile> unit, string yaps) =>
            Hash(sourcePath + EmittedVersion(yaps) + unit.Count + string.Concat(unit.Select(f => f.Text)));

        // False whenever it cannot tell. The original comes from the patch itself (its source
        // name rides in a hidden property's description): converted avatars never record it.
        public static bool IsStale(Material patched)
        {
            // Patched ones only. An unpatched material is its own source, so it
            // named the patch it would get and read as stale, and Refresh, with
            // no original to patch from, left it unpatched on every rebuild.
            return patched != null && patched.shader != null && SourceShaderOf(patched) != null
                   && CurrentNameFor(patched) is string want && patched.shader.name != want;
        }

        // The shader a patched material was made from, or null.
        public static Shader OriginalShaderOf(Material patched)
        {
            // By file first: two installs of one shader share its name, and Find picks either.
            if (patched?.shader != null && patched.shader.FindPropertyIndex(SourceGuidProperty) is int i && i >= 0
                && AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(patched.shader.GetPropertyDescription(i))) is Shader byFile)
                return byFile;
            string name = SourceShaderOf(patched);
            return string.IsNullOrEmpty(name) ? null : Shader.Find(name);
        }

        // Re-patch a material that has fallen behind, in place. Its values
        // survive the swap and a property the old code never had arrives at
        // the default its block declares.
        public static bool Refresh(Material patched, BridgeReport report)
        {
            var current = Repatch(patched, RolesOf(patched.shader), report, out _);
            if (current == null) return false;
            Wear(patched, current);
            return true;
        }

        // A swap that keeps the material's own render queue; the converter's copies use the same.
        // Instancing off: a skinned mesh is never instanced, and with no material asking, the
        // build strips the instancing copy of every variant.
        public static void Wear(Material m, Shader shader)
        {
            ShaderSpiPatcher.Wear(m, shader);
            m.enableInstancing = false;
        }

        // A shader with one more role: its own if compiled in and current, else the original
        // repatched with both (a plug on a squash-only copy never bent). Null if the original is gone.
        public static Shader WithRole(Material patched, Roles role, BridgeReport report) => WithRole(patched, role, report, out _);

        // With why it came back null, if it did.
        public static Shader WithRole(Material patched, Roles role, BridgeReport report, out string refusal)
        {
            refusal = null;
            var have = RolesOf(patched.shader);
            return (have & role) == role && !IsStale(patched) ? patched.shader : Repatch(patched, have | role, report, out refusal);
        }

        // Exactly these roles, any it has beyond them dropped too.
        public static Shader WithRoles(Material patched, Roles roles, BridgeReport report, out string refusal)
        {
            refusal = null;
            return RolesOf(patched.shader) == roles && !IsStale(patched) ? patched.shader : Repatch(patched, roles, report, out refusal);
        }

        static Shader Repatch(Material patched, Roles roles, BridgeReport report, out string refusal)
        {
            var original = OriginalShaderOf(patched);
            if (original == null)
            {
                refusal = "its original shader is gone from the project";
                return null;
            }
            var stand = new Material(original) { name = patched.name };
            try { return Patch(stand, report, out refusal, out _, allowSps: true, duplicate: Duplicate(patched), roles: roles); }
            finally { UnityEngine.Object.DestroyImmediate(stand); }
        }

        // Every patched material in the project: bakes only reach what is open, so props,
        // unopened scenes and imported packages never got shader fixes.
        public static string SweepProject()
        {
            var guids = AssetDatabase.FindAssets("t:Material");
            int found = 0, refreshed = 0, failed = 0;
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("YAPS",
                            $"Checking materials ({i + 1} of {guids.Length})",
                            (i + 1) / (float) Mathf.Max(guids.Length, 1))) break;
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    // The property is the test rather than the shader's name:
                    // a material patched by an older version answers this and
                    // may not have been named the way this one names them.
                    if (material == null || !material.HasProperty("_YAPS_Bake")) continue;
                    // A readout reads the bake and is not patched from anything,
                    // so it would count as a failure every time.
                    if (YapsMarks.IsReadoutMaterial(material)) continue;
                    found++;
                    if (!IsStale(material)) continue;
                    if (Refresh(material, null))
                    {
                        refreshed++;
                        EditorUtility.SetDirty(material);
                    }
                    else failed++;
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            if (refreshed > 0) AssetDatabase.SaveAssets();
            if (found == 0) return "No YAPS materials in this project.";
            if (refreshed == 0 && failed == 0) return $"{BridgeElements.Count(found, "YAPS material", "YAPS materials")}, all current.";
            string said = $"Refreshed {refreshed} of {BridgeElements.Count(found, "YAPS material", "YAPS materials")}.";
            // A failure is a material whose source shader is gone from the
            // project. Nothing can be rebuilt from a shader that is not
            // there, and saying so beats a count that quietly does not add up.
            return failed == 0 ? said
                : said + $" {failed} could not be: their original shader is no longer in the project.";
        }

        static readonly Regex FrozenPenetrator = new Regex(@"\(\s*[0-9.]+\s*/\*_PenetratorEnabled\*/\s*\)");

        // The tag every patched file carries, the shader and its includes alike.
        static readonly Regex GeneratedTag = new Regex(@"_YAPS_([0-9a-f]{8})(?:_\d+)?\.[A-Za-z0-9]+$");

        // Every patched shader nothing in the project uses, with its includes, to the trash.
        // Each YAPS version names its copies afresh, so the shared folder keeps every set before
        // it. A material or a scene naming the shader keeps it; so does one loaded and unsaved.
        // It reads the whole project, so it is a button, never part of a pass.
        public static string PruneUnused(Func<int, bool> confirm = null, string folder = SharedFolder)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return "Not while Unity is playing, compiling or importing. Try again once it is done.";
            AssetDatabase.Refresh();
            if (!AssetDatabase.IsValidFolder(folder)) return "No patched shaders in this project.";
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var guids = AssetDatabase.FindAssets("t:Material").Concat(AssetDatabase.FindAssets("t:Scene")).Distinct().ToArray();
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("YAPS", $"Checking what uses each shader ({i + 1} of {guids.Length})",
                            (i + 1) / (float) guids.Length)) return "Stopped. Nothing was removed.";
                    used.UnionWith(AssetDatabase.GetDependencies(AssetDatabase.GUIDToAssetPath(guids[i]), false));
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && m.shader != null) used.Add(AssetDatabase.GetAssetPath(m.shader));
            // A copy draws through its patched lender by name (UsePass), which no dependency lists:
            // left out, the lender went to the trash and every copy borrowing its passes stopped drawing.
            var lent = new Queue<string>(used.Where(p => p.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)));
            while (lent.Count > 0)
            {
                string path = lent.Dequeue();
                if (!path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
                foreach (Match use in Regex.Matches(File.ReadAllText(path), @"^[ \t]*UsePass\s+""([^""]+)/([^""/]+)""", RegexOptions.Multiline))
                {
                    var lender = Shader.Find(use.Groups[1].Value);
                    string at = lender != null ? AssetDatabase.GetAssetPath(lender) : null;
                    if (!string.IsNullOrEmpty(at) && used.Add(at)) lent.Enqueue(at);
                }
            }

            var groups = Directory.GetFiles(folder).Select(f => f.Replace('\\', '/'))
                .Select(f => (path: f, tag: GeneratedTag.Match(Path.GetFileName(f))))
                .Where(f => f.tag.Success).GroupBy(f => f.tag.Groups[1].Value, f => f.path).ToList();
            // A set goes whole or not at all: its includes belong to its shader alone.
            var unused = groups.Where(g => !g.Any(p => p.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) && used.Contains(p))).ToList();
            int shaders = unused.Sum(g => g.Count(p => p.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)));
            int kept = groups.Count - unused.Count;
            if (unused.Count == 0) return $"Nothing to remove: {BridgeElements.Count(kept, "patched shader is", "patched shaders are")} in use.";
            if (confirm != null && !confirm(shaders)) return "Nothing was removed.";
            var failed = new List<string>();
            AssetDatabase.MoveAssetsToTrash(unused.SelectMany(g => g).ToArray(), failed);
            return $"Moved {BridgeElements.Count(shaders, "unused patched shader", "unused patched shaders")} to the trash, " +
                   $"with {(shaders == 1 ? "its" : "their")} includes; {kept} in use kept." +
                   (failed.Count > 0 ? $" {BridgeElements.Count(failed.Count, "file", "files")} could not be moved: {string.Join(", ", failed.Take(3).Select(Path.GetFileName))}." : "");
        }

        // The name is not cosmetic. Before Thry 2.7x (see MarkLocked for after):
        //  - Poiyomi strips from the build any shader with Thry's two markers not named
        //    "Hidden/Locked/...", so the avatar uploads pink.
        //  - Auto-lock-on-upload treats that prefix as "already locked"; locked again, the
        //    animated _YAPS_ properties become constants and the deform freezes.
        // Hence either marker triggers it, and so does an already-locked source (locking
        // strips the markers): relocked, "float4 _YAPS_SocketPos;" becomes "float4 0.5;",
        // which fails only when Play mode compiles the shadow caster.
        static string PatchedName(string sourceText, string hash)
        {
            bool thry = sourceText != null
                        && (sourceText.Contains("shader_is_using_thry_editor")
                            || sourceText.Contains("ThryShaderOptimizerLockButton")
                            || Regex.IsMatch(sourceText, @"Shader\s+""Hidden/Locked/"));
            return (thry ? "Hidden/Locked/YAPS/" : "Hidden/YAPS/") + hash;
        }

        // Thry 2.7x reads the lock button's default (1 = locked) instead of the name. A patch of
        // an unlocked one kept 0, so upload relocked it into code that does not compile.
        static readonly Regex LockButtonDefault = new Regex(
            @"(\[ThryShaderOptimizerLockButton\][^\r\n]*\)\s*=\s*)0\b");

        static string MarkLocked(string text) => LockButtonDefault.Replace(text, "${1}1");

        // Writes many copies and imports in two batches (includes, then shaders): each lone
        // import runs every package's hooks, ~1 min in a large project, and a squash patches
        // twenty. Failed copies are left to Patch to rewrite and report.
        public static void Prewrite(IEnumerable<Material> materials, Roles roles = Roles.All)
        {
            var list = materials.Where(m => m != null).Distinct().ToList();
            if (list.Count == 0) return;
            YapsLock.Prepare(list);
            // A folder the AssetDatabase has not seen takes no imports into it.
            if (!AssetDatabase.IsValidFolder(SharedFolder))
            {
                Directory.CreateDirectory(SharedFolder);
                AssetDatabase.Refresh();
            }
            var written = Deferred = new List<string>();
            // Kept across calls in one pass: a caller writing two sets of roles makes two calls.
            if (Prewritten == null) Prewritten = new Dictionary<string, (string[], int, string, int, bool)>();
            try
            {
                foreach (var m in list) Patch(m, null, out _, out _, roles: roles);
            }
            finally
            {
                Deferred = null;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                foreach (bool shaders in new[] { false, true })
                {
                    AssetDatabase.StartAssetEditing();
                    try
                    {
                        foreach (string path in written.Distinct().Where(p => p.EndsWith(".shader") == shaders))
                        {
                            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                            Timing.Imported++;
                        }
                    }
                    finally { AssetDatabase.StopAssetEditing(); }
                }
                Timing.ImportMs += clock.ElapsedMilliseconds;
            }
        }

        // Set while Prewrite runs: files are written and listed here, never imported one by one.
        static List<string> Deferred;

        // From a Prewrite to the end of the pass: each patch's edited text, for the second Patch
        // of the same material in the slot loop. Keyed on everything the edits read, so it cannot
        // go stale, and dropped at the pass's end because a squash's copies run to megabytes.
        static Dictionary<string, (string[] Texts, int Passes, string Refusal, int Shadows, bool Frameless)> Prewritten;

        // Persistent memo of source to copy, skipping the text edits (10 to 20 s an avatar with many
        // Poiyomi materials). Keyed on edit inputs and tool build; changed or missing copies miss.
        // ponytail: the file only grows, one line a new copy; clear Library to reset it.
        static Dictionary<string, (string[] Paths, long[] Sizes, long[] Times, int Passes, int Shadows, bool Frameless)> Memo;
        static readonly string Build = "\0" + typeof(YapsShaderPatcher).Assembly.ManifestModule.ModuleVersionId;
        static string MemoFile => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "AvatarBridge", "yaps-patch-memo.tsv");

        static Shader Recall(string key, out int passes, out int shadows, out bool frameless)
        {
            passes = shadows = 0;
            frameless = false;
            if (Memo == null) LoadMemo();
            if (!Memo.TryGetValue(key, out var m)) return null;
            for (int i = 0; i < m.Paths.Length; i++)
            {
                var info = new FileInfo(m.Paths[i]);
                if (!info.Exists || info.Length != m.Sizes[i] || info.LastWriteTimeUtc.Ticks != m.Times[i]) return null;
            }
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(m.Paths[0]);
            if (shader == null || Broken(shader) != null) return null;
            passes = m.Passes;
            shadows = m.Shadows;
            frameless = m.Frameless;
            return shader;
        }

        static void Remember(string key, string[] paths, int passes, int shadows, bool frameless)
        {
            if (Memo == null) LoadMemo();
            var infos = paths.Select(p => new FileInfo(p)).ToArray();
            if (infos.Any(i => !i.Exists)) return;
            var entry = (paths, infos.Select(i => i.Length).ToArray(), infos.Select(i => i.LastWriteTimeUtc.Ticks).ToArray(), passes, shadows, frameless);
            Memo[key] = entry;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MemoFile));
                File.AppendAllText(MemoFile, string.Join("\t", new[] { key.Replace("\0", " "), passes.ToString(), shadows.ToString(), frameless ? "1" : "0" }
                    .Concat(paths.Select((p, i) => p + "\t" + entry.Item2[i] + "\t" + entry.Item3[i]))) + "\n");
            }
            catch (IOException) { }
        }

        static void LoadMemo()
        {
            Memo = new Dictionary<string, (string[], long[], long[], int, int, bool)>();
            try
            {
                if (!File.Exists(MemoFile)) return;
                foreach (string line in File.ReadAllLines(MemoFile))
                {
                    var f = line.Split('\t');
                    if (f.Length < 7 || (f.Length - 4) % 3 != 0) continue;
                    int n = (f.Length - 4) / 3;
                    var paths = new string[n];
                    var sizes = new long[n];
                    var times = new long[n];
                    bool ok = int.TryParse(f[1], out int passes) & int.TryParse(f[2], out int shadows);
                    for (int i = 0; i < n; i++)
                    {
                        paths[i] = f[4 + 3 * i];
                        ok &= long.TryParse(f[5 + 3 * i], out sizes[i]) & long.TryParse(f[6 + 3 * i], out times[i]);
                    }
                    if (ok) Memo[f[0].Replace(" ", "\0")] = (paths, sizes, times, passes, shadows, f[3] == "1");
                }
            }
            catch (IOException) { }
        }

        static string TransformKey(string shaderName, string yaps, List<ShaderSpiPatcher.SourceFile> unit) =>
            Hash(shaderName + "\0" + yaps + string.Concat(unit.Select(f => "\0" + f.OriginalPath + "\0" + f.Text.Length + "\0" + f.Text)), 32);

        // --- what it costs, for the log only --------------------------

        // Never the report: its lines must not move between two runs of the same avatar.
        sealed class Timings
        {
            public long ErrorMs, MessagesMs, ImportMs, CompileMs;
            public int ErrorCalls, MessagesCalls, Imported, Patches, Reused, Remembered, Loads, Compiled;
            public bool Any => Patches + Loads + Imported > 0;

            public override string ToString() =>
                $"compile {CompileMs} ({Compiled} copies), error check {ErrorMs} ({ErrorCalls} calls), messages {MessagesMs} ({MessagesCalls} calls), " +
                $"prewrite import {ImportMs} ({Imported} files), patches {Patches} ({Reused} from Prewrite, {Remembered} remembered), " +
                $"YAPS source loads {Loads}";
        }

        static Timings Timing = new Timings();

        // Set by the release check that times a patched body compiled fresh; null otherwise.
        internal static string FreshCompileNonce;

        // The conversion the counts belong to. A Patch reporting to another one starts afresh.
        static BridgeReport TimedFor;

        static void CountPatch(BridgeReport report)
        {
            if (report != null && report != TimedFor)
            {
                TimedFor = report;
                Timing = new Timings();
                Prewritten = null;
            }
            Timing.Patches++;
        }

        // Logs the patcher's work since the last call, then starts over and drops what Prewrite
        // kept. A pass that cannot log its own is logged by the next one that can, so work left
        // by anything but `report`'s conversion is dropped unlogged. No `pass`, no line.
        public static void EndPass(string pass, BridgeReport report)
        {
            if (pass != null && report == TimedFor && Timing.Any)
                Debug.Log($"[AvatarBridge] YAPS shader timing (ms): {pass}: {Timing}");
            TimedFor = report;
            Timing = new Timings();
            Prewritten = null;
        }

        static bool HasError(Shader shader)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try { return ShaderUtil.ShaderHasError(shader); }
            finally
            {
                Timing.ErrorMs += clock.ElapsedMilliseconds;
                Timing.ErrorCalls++;
            }
        }

        static ShaderMessage[] Messages(Shader shader)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try { return ShaderUtil.GetShaderMessages(shader); }
            finally
            {
                Timing.MessagesMs += clock.ElapsedMilliseconds;
                Timing.MessagesCalls++;
            }
        }

        // duplicate: the plug's second copy is drawn, which the material says
        // by itself once its _YAPS_CopyStart is set.
        public static Shader Patch(Material material, BridgeReport report,
            out string refusal, out int skippedShadowPasses, bool allowSps = false, bool duplicate = false,
            Roles roles = Roles.All)
        {
            refusal = null;
            skippedShadowPasses = 0;
            CountPatch(report);
            if (material == null || material.shader == null)
            {
                refusal = "the material has no shader";
                return null;
            }
            // An unlocked Poiyomi is patched from its locked stand-in. Prewrite prepares a batch
            // in one lock; a lone patch prepares its own.
            if (Deferred == null) YapsLock.Prepare(new[] { material });
            var asked = material;
            duplicate |= Duplicate(asked);
            material = YapsLock.Basis(material);
            YapsLock.WarnIfUnlocked(asked, material, report);

            string sourcePath = ShaderSpiPatcher.SourcePathOf(material.shader);
            if (string.IsNullOrEmpty(sourcePath))
            {
                // A shader its importer makes from another file (ORL's .orlshader) has a path
                // in the project, which a built-in never does.
                string assetPath = AssetDatabase.GetAssetPath(material.shader);
                refusal = assetPath.StartsWith("Assets/") || assetPath.StartsWith("Packages/")
                    ? $"\"{material.shader.name}\" is made on import from \"{Path.GetFileName(assetPath)}\", " +
                      "which is not shader source, so there is nothing to patch"
                    : $"\"{material.shader.name}\" has no source file on disk, so there is " +
                      "nothing to patch: it is one of Unity's built-ins or lives inside a " +
                      "compiled package";
                return null;
            }

            var unit = ShaderSpiPatcher.ReadUnit(sourcePath);
            if (unit.Count == 0)
            {
                refusal = "the shader source could not be read";
                return null;
            }

            var shaderFile = unit[0];
            if (shaderFile.Text.Contains("_YAPS_Bake"))
            {
                refusal = "it already carries YAPS";
                return null;
            }
            if (shaderFile.Text.Contains("_SPS_Bake") && !allowSps)
            {
                // Two deform systems moving the same vertices would fight, and the
                // result would be neither. The converter suppresses SPS at bake time,
                // so a shader that still has SPS in it means that step did not happen.
                refusal = "it already carries VRChat's SPS, and two deform systems on the same " +
                          "vertices would fight: suppress SPS at bake time so the plain shader " +
                          "comes through instead";
                return null;
            }
            if (Regex.IsMatch(shaderFile.Text, @"#pragma\s+surface"))
            {
                refusal = "it is a surface shader, so Unity generates the vertex stage and " +
                          "there is no vertex function of its own to wrap";
                return null;
            }
            // A pass borrowed whole keeps the other shader's vertex stage, so the plug drew
            // straight with no word (lilToon draws this way). The lender is patched too and the
            // copy borrows from that. A lender of only a caster, lightmap or deferred pass is left
            // alone: none is the plug the player sees, and the client draws forward.
            var lent = new Dictionary<string, string>();
            int lentPasses = 0;
            foreach (var use in Regex.Matches(shaderFile.Text, @"^[ \t]*UsePass\s+""([^""]+)/([^""/]+)""", RegexOptions.Multiline)
                         .Cast<Match>().GroupBy(m => m.Groups[1].Value))
            {
                if (use.All(m => Regex.IsMatch(m.Groups[2].Value, @"^(SHADOW_?CASTER|META|DEFERRED)$", RegexOptions.IgnoreCase))) continue;
                var lender = Shader.Find(use.Key);
                string why = null;
                Shader lenderPatched = null;
                if (lender != null)
                {
                    var lenderMaterial = new Material(lender);
                    lenderPatched = Patch(lenderMaterial, null, out why, out _, allowSps, duplicate, roles);
                    UnityEngine.Object.DestroyImmediate(lenderMaterial);
                }
                if (lenderPatched == null)
                {
                    // A batch write has not compiled the lender yet; the patch after it does.
                    if (Deferred != null && lender != null) return null;
                    refusal = $"it borrows its passes from \"{use.Key}\", which cannot be patched: " +
                              (lender == null ? "it is not in the project" : why);
                    return null;
                }
                lent[use.Key] = lenderPatched.name;
                lentPasses += use.Count();
            }

            string yaps = YapsSourceFor(duplicate, roles, out string loadFailure);
            if (yaps == null)
            {
                refusal = loadFailure;
                return null;
            }
            // A line more moves every #line, so Unity's shader cache cannot answer the compile.
            if (FreshCompileNonce != null) yaps = "// " + FreshCompileNonce + "\n" + yaps;
            // Before any edit below: the name is taken from the source as it is on disk.
            string hash = NameHash(sourcePath, unit, yaps);

            // A squash patches each material in Prewrite and again in its slot loop, and the
            // edits below come out the same both times.
            string key = Prewritten == null ? null : TransformKey(material.shader.name, yaps, unit);
            int patchedPasses = 0;
            string blockRefusal = null;
            bool frameless = false;
            // A borrowed pass's copy is named from its lender's, which the key does not read. The
            // source's GUID too: the copy records it, and two installs can hold the same text.
            string memoKey = lent.Count == 0
                ? TransformKey(material.shader.name, yaps, unit) + "\0" + AssetDatabase.AssetPathToGUID(sourcePath) + Build : null;
            var remembered = memoKey != null ? Recall(memoKey, out patchedPasses, out skippedShadowPasses, out frameless) : null;
            if (remembered != null)
            {
                Timing.Remembered++;
            }
            else if (key != null && Prewritten.TryGetValue(key, out var done))
            {
                Timing.Reused++;
                if (done.Texts != null)
                    for (int i = 0; i < unit.Count; i++) unit[i].Text = done.Texts[i];
                patchedPasses = done.Passes;
                blockRefusal = done.Refusal;
                skippedShadowPasses = done.Shadows;
                frameless = done.Frameless;
            }
            else
            {
                // Properties first: the block is in the shader file, and later
                // edits work on offsets that this would otherwise shift.
                var properties = Regex.Match(shaderFile.Text, @"Properties\s*\{");
                if (!properties.Success)
                {
                    refusal = "it has no Properties block to add the YAPS properties to";
                    return null;
                }
                shaderFile.Text = shaderFile.Text.Insert(properties.Index + properties.Length,
                    PropertyBlock + RolesDeclaration(roles));
                foreach (var pair in lent)
                    shaderFile.Text = Regex.Replace(shaderFile.Text, $@"(UsePass\s+""){Regex.Escape(pair.Key)}/",
                        m => m.Groups[1].Value + pair.Value + "/");
                // lilToon declares the vertex inputs the deform reads only when a pass asks.
                if (unit.Any(f => f.Text.Contains("LIL_REQUIRE_APP_VERTEXID")))
                    shaderFile.Text = Regex.Replace(shaderFile.Text, @"^[ \t]*(HLSLPROGRAM|CGPROGRAM)[ \t]*(?=\r?$)",
                        m => m.Value + "\n#define LIL_REQUIRE_APP_NORMAL\n#define LIL_REQUIRE_APP_TANGENT\n#define LIL_REQUIRE_APP_VERTEXID",
                        RegexOptions.Multiline);

                // Locking Poiyomi writes its DPS penetrator's switch into the code, on. The copy
                // bends the plug the YAPS way, so DPS's goes off there; the source keeps it.
                foreach (var file in unit) file.Text = FrozenPenetrator.Replace(file.Text, "(0 /*_PenetratorEnabled*/)");
                patchedPasses = PatchProgramBlocks(unit, yaps, out blockRefusal,
                    out skippedShadowPasses, out frameless);
                bool whole = (patchedPasses > 0 || lent.Count > 0) && blockRefusal == null;
                if (whole)
                {
                    FogInPixelOnly(unit);
                    VertexLightInPixelOnly(unit, material);
                    string newName = PatchedName(shaderFile.Text, hash);
                    shaderFile.Text = MarkLocked(ShaderSpiPatcher.Rename(shaderFile.Text, newName));
                    InstallShaderGui(shaderFile, material.shader.name,
                        AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material.shader)));
                }
                if (key != null)
                {
                    Prewritten[key] = (whole ? unit.Select(f => f.Text).ToArray() : null,
                        patchedPasses, blockRefusal, skippedShadowPasses, frameless);
                }
            }
            if (patchedPasses == 0 && lent.Count == 0)
            {
                refusal = blockRefusal ?? "no pass in it declares a vertex stage";
                return null;
            }
            if (blockRefusal != null)
            {
                // Half a patched shader is worse than none: the passes that took the
                // deform would bend a mesh the others left straight.
                refusal = blockRefusal;
                return null;
            }

            string compileError = null;
            var patched = remembered ?? WriteAndVerify(unit, sourcePath, SharedFolder, hash, out compileError);
            if (patched != null && (compileError = Compiled(patched, asked)) != null) patched = null;
            if (patched != null && remembered == null && memoKey != null)
                Remember(memoKey, unit.Select(f => SharedFolder + "/" + f.OutputName).ToArray(), patchedPasses, skippedShadowPasses, frameless);
            if (patched == null)
            {
                refusal = "the patched shader did not compile: " + compileError;
                return null;
            }

            report?.Converted(Category, asked.name,
                $"Deform patched into \"{asked.shader.name}\"" +
                (asked != material ? ", locked through Poiyomi first," : "") +
                $" across {BridgeElements.Count(patchedPasses + lentPasses, "pass", "passes")}. " +
                $"The original shader is untouched; a copy carrying YAPS was written to {SharedFolder}, " +
                "shared by everything patched from the same shader, and the material repointed at it."
                + (frameless
                    ? " Its vertex input takes no normal or no tangent from the mesh, and a skinned " +
                      "plug finds which way it points from those, so a skinned plug on this shader " +
                      "stays straight. A plug that is its own mesh object bends as normal. YAPS " +
                      "Simple Lit carries both."
                    : "")
                + (skippedShadowPasses > 0
                    ? $" {BridgeElements.Count(skippedShadowPasses, "shadow pass takes its", "shadow passes take their")} vertex stage from outside " +
                      "this shader and Unity's standard caster, so there is nothing of the avatar's to " +
                      "edit there: the shadow will not follow the bend. Cosmetic, and the " +
                      "alternative was no deform at all."
                    : ""));
            return patched;
        }

        // --- the material panel -----------------------------------------

        // YapsShaderGUI on top, the shader's own editor underneath. Its
        // class name and the source shader's name ride in the description
        // of a hidden property: the only place a string can travel.
        public const string SourceShaderProperty = "_YAPS_SourceShader";
        public const string SourceGuidProperty = "_YAPS_SourceShaderGuid";

        public static string SourceShaderOf(Material material)
        {
            if (material == null || material.shader == null) return null;
            int index = material.shader.FindPropertyIndex(SourceShaderProperty);
            if (index < 0) return null;
            string name = material.shader.GetPropertyDescription(index);
            return string.IsNullOrEmpty(name) ? null : name;
        }

        static void InstallShaderGui(ShaderSpiPatcher.SourceFile shaderFile, string sourceShaderName, string sourceGuid)
        {
            string original = null;
            var existing = Regex.Match(shaderFile.Text, @"^\s*CustomEditor\s+""([^""]+)""\s*$",
                RegexOptions.Multiline);
            if (existing.Success)
            {
                original = existing.Groups[1].Value;
                shaderFile.Text = shaderFile.Text.Remove(existing.Index, existing.Length);
            }

            // The marker properties, into the Properties block beside the YAPS ones.
            var properties = Regex.Match(shaderFile.Text, @"Properties\s*\{");
            if (properties.Success)
            {
                string marker = "\n        [HideInInspector] " + YapsShaderGUI.OriginalEditorProperty
                                + " (\"" + (original ?? "") + "\", Float) = 0"
                                + "\n        [HideInInspector] " + SourceShaderProperty
                                + " (\"" + (sourceShaderName ?? "").Replace("\"", "") + "\", Float) = 0"
                                + "\n        [HideInInspector] " + SourceGuidProperty
                                + " (\"" + (sourceGuid ?? "") + "\", Float) = 0";
                shaderFile.Text = shaderFile.Text.Insert(properties.Index + properties.Length, marker);
            }

            // CustomEditor sits at shader scope, after the SubShaders. The
            // last closing brace of the file is the shader's own.
            int close = shaderFile.Text.LastIndexOf('}');
            if (close > 0)
            {
                shaderFile.Text = shaderFile.Text.Insert(close,
                    "    CustomEditor \"AvatarBridge.YapsShaderGUI\"\n");
            }
        }

        // --- the vertex wrapper ---------------------------------------

        // The vertex function is edited in place, never wrapped. Poiyomi
        // declares its signature across a preprocessor conditional, which no
        // wrapper can reproduce. The parameter is a local copy anyway.
        static int PatchProgramBlocks(List<ShaderSpiPatcher.SourceFile> unit, string yaps,
            out string refusal, out int skippedShadowPasses, out bool frameless)
        {
            refusal = null;
            skippedShadowPasses = 0;
            frameless = false;
            var shaderFile = unit[0];
            AddStandardCaster(shaderFile);
            int patched = 0;
            // Whether each include function patched so far lacked a normal or tangent.
            var alreadyInjected = new Dictionary<string, bool>(StringComparer.Ordinal);

            // Scanned with comments blanked: Poiyomi's lock comments out a pass it turns off,
            // and an entry define went in after its CGPROGRAM, outside any program. Edits go in
            // last block first, so the offsets read here hold for every block still to come.
            string code = ShaderSpiPatcher.Uncommented(shaderFile.Text);
            var blocks = Regex.Matches(code, @"(CGPROGRAM|HLSLPROGRAM)")
                .Cast<Match>().Reverse().ToList();

            foreach (var block in blocks)
            {
                int start = block.Index + block.Length;
                var end = Regex.Match(code.Substring(start), @"(ENDCG|ENDHLSL)");
                if (!end.Success)
                {
                    continue;
                }

                var vertPragma = Regex.Match(
                    code.Substring(start, end.Index), @"#pragma\s+vertex\s+(\w+)");
                if (!vertPragma.Success)
                {
                    continue;   // fragment-only, or a pass we need not touch
                }

                // Lightmap baking runs its own geometry and shows the player nothing.
                // Deforming there is pointless and can upset the bake. A "Never" pass is
                // never drawn (lilToon keeps one to hold UV channels), and Unity's upgrader
                // reads its patched "struct v2f vert(...) {" as a struct and drops it on DX11.
                if (LooksLikeMetaPass(shaderFile.Text, block.Index) || PassTagIs(shaderFile.Text, block.Index, "Never"))
                {
                    continue;
                }
                // A locked Poiyomi outline at width 0 clips every pixel it draws. Bent, it only cost
                // each viewer's driver a full build of the bend the first time the plug drew.
                // The raw text: the lock leaves the width as a comment, which the blanked copy drops.
                if (OutlineAlwaysClipped(shaderFile.Text.Substring(start, end.Index), Reached(unit, code, start, start + end.Index)))
                {
                    continue;
                }

                string vertName = vertPragma.Groups[1].Value;
                if (!PatchOneVertexFunction(Reached(unit, code, start, start + end.Index), yaps, vertName,
                        start, start + end.Index, alreadyInjected, out string why, out bool missingFrame))
                {
                    // A shadow caster is allowed to be out of reach. Unity's own
                    // vertShadowCaster lives in its CGIncludes, not in the shader's source
                    // unit, so it is wrapped instead; any other is left. The shadow is
                    // cosmetic, the deform is the feature.
                    if (IsShadowCasterPass(shaderFile.Text, block.Index))
                    {
                        if (vertName == "vertShadowCaster"
                            && unit.Any(f => f.Text.Contains("UnityStandardShadow.cginc"))
                            && WrapUnityShadowCaster(shaderFile, yaps, start, start + end.Index))
                        {
                            patched++;
                            continue;
                        }
                        skippedShadowPasses++;
                        continue;
                    }
                    refusal = why;
                    return patched;
                }
                // Asked of the passes people see: casters routinely leave the tangent out.
                if (missingFrame && !IsShadowCasterPass(shaderFile.Text, block.Index))
                {
                    frameless = true;
                }
                // Blocks run last first, so this shifts none still to come.
                shaderFile.Text = shaderFile.Text.Insert(start, $"\n#define {EntryDefine(vertName)}\n");
                patched++;
            }

            return patched;
        }

        static string EntryDefine(string vertName) => "YAPS_ENTRY_" + vertName;

        const string VertexIdParameter = "yapsVertexId";

        // The shader and the includes one program reaches: its own, and every CGINCLUDE
        // block's, which is prepended to it. Passes keep their includes apart, and the first
        // match across all of them was the outline pass's vert, patched once for four passes
        // while the other three drew the plug straight.
        static List<ShaderSpiPatcher.SourceFile> Reached(List<ShaderSpiPatcher.SourceFile> unit,
            string code, int blockStart, int blockEnd)
        {
            var byPath = unit.ToDictionary(f => Path.GetFullPath(f.OriginalPath), StringComparer.OrdinalIgnoreCase);
            var scope = new List<ShaderSpiPatcher.SourceFile> { unit[0] };
            void Walk(string text, string path)
            {
                string folder = Path.GetDirectoryName(path) ?? ".";
                foreach (Match m in ShaderSpiPatcher.IncludeDirective.Matches(text))
                {
                    string found = ShaderSpiPatcher.ResolveInclude(folder, m.Groups[2].Value);
                    if (found != null && byPath.TryGetValue(Path.GetFullPath(found), out var file) && !scope.Contains(file))
                    {
                        scope.Add(file);
                        Walk(ShaderSpiPatcher.Uncommented(file.Text), file.OriginalPath);
                    }
                }
            }
            foreach (Match shared in Regex.Matches(code, @"(?:CG|HLSL)INCLUDE([\s\S]*?)END(?:CG|HLSL)"))
            {
                Walk(shared.Groups[1].Value, unit[0].OriginalPath);
            }
            Walk(code.Substring(blockStart, blockEnd - blockStart), unit[0].OriginalPath);
            return scope;
        }

        // The lock writes the width in as a literal: clip(mask * (0.0 /*_LineWidth*/) - 0.001).
        // Older locks write it bare, clip(OutlineMask * 0.0 - 0.001), and Poiyomi Pro as float(0).
        static readonly Regex ZeroWidthOutlineClip = new Regex(
            @"clip\s*\([^;]*\(\s*0(?:\.0*)?\s*/\*\s*_LineWidth\s*\*/\s*\)\s*-\s*0?\.0*1\s*\)"
            + @"|clip\s*\(\s*OutlineMask\s*\*\s*(?:0(?:\.0*)?|float\s*\(\s*0(?:\.0*)?\s*\))\s*-\s*0?\.0*1\s*\)");

        static bool OutlineAlwaysClipped(string block, List<ShaderSpiPatcher.SourceFile> reached)
            => ZeroWidthOutlineClip.IsMatch(block)
               || reached.Skip(1).Any(f => ZeroWidthOutlineClip.IsMatch(f.Text));

        static bool PatchOneVertexFunction(List<ShaderSpiPatcher.SourceFile> unit, string yaps,
            string vertName, int blockStart, int blockEnd, Dictionary<string, bool> alreadyInjected,
            out string refusal, out bool missingFrame)
        {
            refusal = null;
            missingFrame = false;
            var pattern = new Regex($@"(\w+)\s+{Regex.Escape(vertName)}\s*\(");

            // This program block first: a flattened shader repeats the vertex function per
            // pass, and a file-wide search patched the first copy repeatedly at stale offsets
            // (reported as an undeclared 'endif').
            ShaderSpiPatcher.SourceFile file = unit[0];
            Match head = Match.Empty;
            if (blockStart >= 0 && blockEnd <= file.Text.Length)
            {
                var inBlock = pattern.Match(ShaderSpiPatcher.Uncommented(file.Text), blockStart, blockEnd - blockStart);
                if (inBlock.Success)
                {
                    head = inBlock;
                }
            }

            bool tessellated = blockStart >= 0 && blockEnd <= unit[0].Text.Length && Regex.IsMatch(
                ShaderSpiPatcher.Uncommented(unit[0].Text).Substring(blockStart, blockEnd - blockStart), @"#pragma\s+hull\b");
            if (head.Success)
            {
                return PatchDefinition(unit, yaps, vertName, file, head, blockStart, tessellated,
                    out refusal, out missingFrame);
            }

            // Not in the block, so it lives in an include. Those are shared textually by every
            // pass, so each is patched exactly once. An #if can choose between two files that
            // each define it, and only the first found was patched: passes reading the other
            // drew the plug straight.
            var files = unit.Skip(1).Where(f => Definitions(f, pattern).Count > 0).ToList();
            if (files.Count == 0)
            {
                file = ShaderSpiPatcher.FindIn(unit.Skip(1).ToList(), pattern.ToString(), out head);
                if (file == null)
                {
                    refusal = $"the vertex function \"{vertName}\" could not be found in the " +
                              "shader or its includes";
                    return false;
                }
                files.Add(file);
            }
            foreach (var include in files)
            {
                string injectedKey = include.OriginalPath + "::" + vertName;
                if (alreadyInjected.TryGetValue(injectedKey, out bool done))
                {
                    missingFrame |= done;
                    continue;   // already done for another pass
                }
                // Every definition in the file. One file can keep a vert per pass behind its own
                // #if, and only the first was patched. Read here, not above: patching one file
                // can edit another.
                var heads = Definitions(include, pattern);
                if (heads.Count == 0) heads.Add(head);
                bool fileFrameless = false;
                foreach (var definition in heads)
                {
                    if (!PatchDefinition(unit, yaps, vertName, include, definition, blockStart, tessellated,
                            out refusal, out bool frameless))
                    {
                        return false;
                    }
                    fileFrameless |= frameless;
                }
                // Marked only once injected. Passes run last first, so a shadow caster that
                // fails here, and is skipped as cosmetic, otherwise left every later pass
                // "already done" with nothing in it: a plug drawn straight, no refusal.
                alreadyInjected[injectedKey] = fileFrameless;
                missingFrame |= fileFrameless;
            }
            return true;
        }

        // Last first, so patching one leaves the offsets of those before it.
        static List<Match> Definitions(ShaderSpiPatcher.SourceFile file, Regex pattern)
        {
            string code = ShaderSpiPatcher.Uncommented(file.Text);
            return pattern.Matches(code).Cast<Match>().Where(m => IsDefinition(code, m.Index)).Reverse().ToList();
        }

        static readonly Regex BodyFollows = new Regex(@"\G\s*(?::\s*\w+\s*)?\{");

        // A head followed by a body, not a call or a declaration.
        static bool IsDefinition(string code, int at)
        {
            int close = MatchBracket(code, code.IndexOf('(', at), '(', ')');
            return close >= 0 && BodyFollows.IsMatch(code, close + 1);
        }

        static bool PatchDefinition(List<ShaderSpiPatcher.SourceFile> unit, string yaps, string vertName,
            ShaderSpiPatcher.SourceFile file, Match head, int blockStart, bool tessellated, out string refusal, out bool missingFrame)
        {
            refusal = null;
            missingFrame = false;
            int parenOpen = file.Text.IndexOf('(', head.Index);
            int parenClose = MatchBracket(file.Text, parenOpen, '(', ')');
            if (parenClose < 0)
            {
                refusal = $"the parameter list of \"{vertName}\" is not closed";
                return false;
            }

            int braceOpen = file.Text.IndexOf('{', parenClose);
            if (braceOpen < 0)
            {
                refusal = $"\"{vertName}\" looks like a declaration without a body";
                return false;
            }

            string parameters = file.Text.Substring(parenOpen + 1, parenClose - parenOpen - 1);

            // Strip preprocessor lines before reading identifiers. Poiyomi's
            // parameter list ENDS with "#endif", so the last identifier of the raw
            // text named the parameter "endif" and emitted `endif.vertex.xyz`.
            string cleaned = Regex.Replace(parameters, @"^[ \t]*#.*$", "", RegexOptions.Multiline);

            var identifiers = Regex.Matches(cleaned, @"[A-Za-z_]\w*")
                .Cast<Match>().Select(m => m.Value).ToList();
            if (identifiers.Count < 2)
            {
                refusal = $"\"{vertName}\" takes no vertex input this patcher can recognise";
                return false;
            }

            // The parameter's own name is the last identifier; everything
            // before it is candidate type names, preprocessor symbols and
            // qualifiers. Try each as a struct until one carries POSITION.
            string parameterName = identifiers[identifiers.Count - 1];
            Dictionary<string, string> members = null;
            foreach (string candidate in identifiers.Take(identifiers.Count - 1).Distinct())
            {
                var found = ReadStructMembers(unit, candidate, file, file == unit[0] ? blockStart : 0, head.Index);
                if (found != null && found.ContainsKey("POSITION"))
                {
                    members = found;
                    break;
                }
            }
            if (members == null)
            {
                refusal = $"no vertex input struct with a POSITION member could be found for " +
                          $"\"{vertName}\"";
                return false;
            }

            members.TryGetValue("POSITION", out string positionField);
            members.TryGetValue("NORMAL", out string normalField);
            members.TryGetValue("TANGENT", out string tangentField);
            missingFrame = normalField == null || tangentField == null;
            members.TryGetValue("SV_VERTEXID", out string vertexIdField);
            // Poiyomi Pro's tessellated shaders give the same field TEXCOORD6 there, behind an #if
            // this reader does not follow, so the deform read a mesh UV channel and every vertex
            // took one vertex's squash share and plug frame. A tessellated entry is a real vertex
            // stage, so it takes its own.
            if (tessellated && vertexIdField != null && members.ContainsKey(TwiceKey + vertexIdField))
            {
                vertexIdField = null;
            }

            // An input with no SV_VertexID: the function takes one as a parameter instead, the
            // struct left alone, since other stages may build it.
            bool idParameter = string.IsNullOrEmpty(vertexIdField);
            string idExpression = idParameter ? VertexIdParameter : parameterName + "." + vertexIdField;

            var body = new StringBuilder();
            body.AppendLine();
            body.AppendLine("    // --- YAPS ---");
            // Only where this function is the pass's own vertex stage. lilToon's tessellated
            // passes deform in vertTess, then the domain calls the vert other passes patched,
            // and the plug bent twice and left the screen.
            body.AppendLine($"#ifdef {EntryDefine(vertName)}");
            // Runs before the host's setup, and instanced variants read unity_ObjectToWorld via
            // the instance id (0 until set), so every batched copy deformed in the first's frame.
            // Repeated only when the host calls it on this parameter outside any #if; harmless twice.
            int bodyClose = MatchBracket(file.Text, braceOpen, '{', '}');
            string hostBody = bodyClose > braceOpen
                ? file.Text.Substring(braceOpen, bodyClose - braceOpen) : "";
            // A commented-out call is no evidence: its struct member is often
            // commented out too, and v.instanceID then fails the instanced
            // variants only, which the import-time error check never compiles.
            hostBody = Regex.Replace(hostBody, @"//[^\n]*|/\*.*?\*/", "", RegexOptions.Singleline);
            var hostSetup = Regex.Match(hostBody,
                $@"\bUNITY_SETUP_INSTANCE_ID\s*\(\s*{Regex.Escape(parameterName)}\s*\)");
            if (hostSetup.Success)
            {
                string before = hostBody.Substring(0, hostSetup.Index);
                int depth = Regex.Matches(before, @"^[ \t]*#[ \t]*if", RegexOptions.Multiline).Count
                            - Regex.Matches(before, @"^[ \t]*#[ \t]*endif", RegexOptions.Multiline).Count;
                if (depth == 0)
                {
                    body.AppendLine($"    UNITY_SETUP_INSTANCE_ID({parameterName});");
                }
            }
            body.AppendLine($"    float3 yapsPosition = {parameterName}.{positionField}.xyz;");
            // Zero when the host has none. A constant stand-in read as a real
            // direction, and a skinned plug recovered a different wrong frame
            // per vertex from it; zero makes the deform leave the vertex alone.
            body.AppendLine(normalField != null
                ? $"    float3 yapsNormal = {parameterName}.{normalField}.xyz;"
                : NoNormal);
            body.AppendLine(tangentField != null
                ? $"    float3 yapsTangent = {parameterName}.{tangentField}.xyz;"
                : NoTangent);
            // Both ends, always: each returns at once unless its own enable is set, so the
            // material's properties decide. One body, not two to keep in step; role defines
            // only decide which calls compile.
            body.Append(DeformCalls(idExpression));
            body.AppendLine($"    {parameterName}.{positionField}.xyz = yapsPosition;");
            if (normalField != null)
            {
                body.AppendLine($"    {parameterName}.{normalField}.xyz = yapsNormal;");
            }
            if (tangentField != null)
            {
                body.AppendLine($"    {parameterName}.{tangentField}.xyz = yapsTangent;");
            }
            body.AppendLine("#endif");
            body.AppendLine("    // --- end YAPS ---");

            // Body first, then the includes above the function, so the insertion
            // offsets stay valid. The YAPS source goes immediately before the
            // function rather than at the top of the block: by there the shader's
            // own includes have run, so UnityCG's matrices and light arrays exist.
            file.Text = file.Text.Insert(braceOpen + 1, body.ToString());
            // Only where it is the pass's entry, and with a default: a domain calling it passes one
            // argument, and is compiled in the plain passes too when it shares their include.
            if (idParameter)
            {
                file.Text = file.Text.Insert(parenClose,
                    $"\n#ifdef {EntryDefine(vertName)}\n    , uint {VertexIdParameter} : SV_VertexID = 0\n#endif\n");
            }
            // At the start of the line, not at the return type: lilToon writes "struct v2f
            // vert(...)", and a word in front of the type was left in front of the source.
            file.Text = file.Text.Insert(file.Text.LastIndexOf('\n', head.Index) + 1, "\n" + yaps + "\n");
            return true;
        }

        // The one list of deforms, for the patched vertex functions and the shadow wrapper alike.
        static string DeformCalls(string id) =>
            $"#ifndef YAPS_NO_PLUG\n    YapsDeform(yapsPosition, yapsNormal, yapsTangent, {id});\n#endif\n" +
            $"#ifndef YAPS_NO_SOCKET\n    YapsSocketDeform(yapsPosition, yapsNormal, yapsTangent, {id});\n#endif\n" +
            $"#ifndef YAPS_NO_SQUASH\n    YapsDentDeform(yapsPosition, yapsNormal, yapsTangent, {id});\n#endif\n" +
            $"#ifndef YAPS_NO_JELLY\n    YapsJellyDeform(yapsPosition, yapsNormal, yapsTangent, {id});\n#endif\n";

        // Unity's own caster: a wrapper with its exact signature deforms the input, then calls
        // it. The signature is read from the editor's own copy, the one the shader compiles
        // against: 2022.3 returns the position in a struct where older versions had an out
        // parameter. No tangent, so a skinned plug's shadow stays straight; the dent needs none.
        static bool WrapUnityShadowCaster(ShaderSpiPatcher.SourceFile file, string yaps,
            int blockStart, int blockEnd)
        {
            string include = Path.Combine(EditorApplication.applicationContentsPath, "CGIncludes",
                "UnityStandardShadow.cginc");
            if (!File.Exists(include))
            {
                return false;
            }
            var head = Regex.Match(File.ReadAllText(include),
                @"(\w+)\s+vertShadowCaster\s*\(\s*VertexInput\s+v\b([^)]*)\)");
            var pragma = new Regex(@"#pragma\s+vertex\s+vertShadowCaster\b")
                .Match(file.Text, blockStart, blockEnd - blockStart);
            if (!head.Success || !pragma.Success)
            {
                return false;
            }
            string returns = head.Groups[1].Value, parameters = head.Groups[2].Value;
            // Each parameter passed on by name, under the same #ifdefs as its declaration.
            string arguments = Regex.Replace(parameters, @",\s*out\s+\w+\s+(\w+)(\s*:\s*\w+)?", ", $1");
            var wrapper = new StringBuilder();
            wrapper.AppendLine();
            wrapper.AppendLine(yaps);
            wrapper.AppendLine($"{returns} yapsVertShadowCaster (VertexInput v, uint yapsVertexId : SV_VertexID{parameters})");
            wrapper.AppendLine("{");
            wrapper.AppendLine("    UNITY_SETUP_INSTANCE_ID(v);");
            wrapper.AppendLine("    float3 yapsPosition = v.vertex.xyz;");
            wrapper.AppendLine("    float3 yapsNormal = v.normal;");
            // Unity's caster input has no tangent. Zero, as NoTangent writes it.
            wrapper.AppendLine("    float3 yapsTangent = 0;");
            wrapper.Append(DeformCalls("yapsVertexId"));
            wrapper.AppendLine("    v.vertex.xyz = yapsPosition;");
            wrapper.AppendLine("    v.normal = yapsNormal;");
            wrapper.AppendLine($"    {(returns == "void" ? "" : "return ")}vertShadowCaster(v{arguments});");
            wrapper.AppendLine("}");
            // The end first, so the pragma's offset still holds.
            file.Text = file.Text.Insert(blockEnd, wrapper.ToString());
            file.Text = file.Text.Remove(pragma.Index, pragma.Length)
                .Insert(pragma.Index, "#pragma vertex yapsVertShadowCaster");
            return true;
        }

        // A Fallback caster knows nothing of the deform, so Unity's caster goes into each
        // SubShader for WrapUnityShadowCaster. Not when the Fallback cuts alpha (cut parts would
        // shadow solid), beside a shared include block (duplicate declarations), or for surface
        // shaders (only the shadow would bend).
        internal static bool AddStandardCaster(ShaderSpiPatcher.SourceFile file)
        {
            string text = file.Text;
            var fallback = Regex.Match(text, @"\bFallBack\s+""([^""]+)""", RegexOptions.IgnoreCase);
            if (!fallback.Success
                || Regex.IsMatch(fallback.Groups[1].Value, "Cutout|Transparent|Fade|Alpha|Particle", RegexOptions.IgnoreCase)
                || Regex.IsMatch(text, @"""LightMode""\s*=\s*""ShadowCaster""", RegexOptions.IgnoreCase)
                || Regex.IsMatch(text, @"\bUsePass\b", RegexOptions.IgnoreCase)
                || Regex.IsMatch(text, @"\b(CGINCLUDE|HLSLINCLUDE|HLSLPROGRAM)\b")
                || Regex.IsMatch(text, @"#pragma\s+surface\b"))
            {
                return false;
            }
            bool added = false;
            foreach (var sub in Regex.Matches(text, @"\bSubShader\s*\{").Cast<Match>().Reverse())
            {
                int close = MatchBracket(text, sub.Index + sub.Length - 1, '{', '}');
                if (close < 0) continue;
                text = text.Insert(close, StandardCaster);
                added = true;
            }
            file.Text = text;
            return added;
        }

        const string StandardCaster = @"
        Pass
        {
            Name ""ShadowCaster""
            Tags { ""LightMode"" = ""ShadowCaster"" }
            ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma target 4.5
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            #pragma vertex vertShadowCaster
            #pragma fragment fragShadowCaster
            #include ""UnityStandardShadow.cginc""
            ENDCG
        }
    ";

        const string NoNormal ="    float3 yapsNormal = float3(0,0,0);";
        const string NoTangent = "    float3 yapsTangent = float3(0,0,0);";

        static int MatchBracket(string text, int open, char opening, char closing)
        {
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == opening) depth++;
                else if (text[i] == closing && --depth == 0) return i;
            }
            return -1;
        }

        // Members keyed by SEMANTIC, because names are anybody's guess but
        // semantics are the contract.
        // Not a semantic: marks a field the struct declares more than once.
        const string TwiceKey = "twice:";

        static Dictionary<string, string> ReadStructMembers(
            List<ShaderSpiPatcher.SourceFile> unit, string structName,
            ShaderSpiPatcher.SourceFile near = null, int from = 0, int before = 0, int depth = 0)
        {
            // The optional ": Base" matters. A struct can inherit, and then
            // POSITION lives in the parent rather than here, reading only
            // the body reports a vertex input with no vertex in it.
            string pattern = $@"struct\s+{Regex.Escape(structName)}\s*(?::\s*(\w+)\s*)?\{{";
            // The declaration the function sees first: the last one above it in its own
            // program or include. Each pass compiles alone, so two passes may declare
            // the same name differently, and the first in the file was another pass's.
            var file = near;
            var declaration = near == null ? Match.Empty
                : Regex.Matches(near.Text.Substring(0, before), pattern).Cast<Match>().LastOrDefault(m => m.Index >= from) ?? Match.Empty;
            if (!declaration.Success)
            {
                file = ShaderSpiPatcher.FindIn(unit.Skip(1).ToList(), pattern, out declaration)
                    ?? ShaderSpiPatcher.FindIn(unit, pattern, out declaration);
            }
            if (file == null)
            {
                return null;
            }

            int open = declaration.Index + declaration.Length;
            int close = file.Text.IndexOf('}', open);
            if (close < 0)
            {
                return null;
            }

            var members = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string body = file.Text.Substring(open, close - open);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(body, @"\w+\s+(\w+)\s*:\s*(\w+)\s*;"))
            {
                string semantic = m.Groups[2].Value.ToUpperInvariant();
                if (!members.ContainsKey(semantic))
                {
                    members[semantic] = m.Groups[1].Value;
                }
                // A field declared twice sits behind #if branches with different semantics.
                if (!seen.Add(m.Groups[1].Value))
                {
                    members[TwiceKey + m.Groups[1].Value] = m.Groups[1].Value;
                }
            }

            string baseName = declaration.Groups[1].Success ? declaration.Groups[1].Value : null;
            if (!string.IsNullOrEmpty(baseName) && !string.Equals(baseName, structName,
                    StringComparison.Ordinal) && depth < 4)
            {
                var inherited = ReadStructMembers(unit, baseName, depth: depth + 1);
                if (inherited != null)
                {
                    foreach (var pair in inherited)
                    {
                        if (!members.ContainsKey(pair.Key))
                        {
                            members[pair.Key] = pair.Value;
                        }
                    }
                }
            }
            return members;
        }

        static bool IsShadowCasterPass(string text, int programIndex)
            => PassTagIs(text, programIndex, "ShadowCaster");

        static bool LooksLikeMetaPass(string text, int programIndex)
            => PassTagIs(text, programIndex, "Meta");

        // Scoped from the previous ENDCG, not a backward search for "Pass": inlined names
        // like CGI_PoiPassShadow contain it and read the wrong tag.
        static bool PassTagIs(string text, int programIndex, string lightMode)
        {
            int from = 0;
            foreach (Match end in Regex.Matches(text, @"\b(ENDCG|ENDHLSL)\b"))
            {
                if (end.Index >= programIndex)
                {
                    break;
                }
                from = end.Index + end.Length;
            }

            string head = text.Substring(from, programIndex - from);
            return Regex.IsMatch(head, $@"""LightMode""\s*=\s*""{Regex.Escape(lightMode)}""",
                RegexOptions.IgnoreCase);
        }

        // --- the YAPS source, inlined ---------------------------------

        // The folder, kept until anything imports, moves or is deleted, as FindAssets searches the
        // whole project. The files are read on every call and joined again only when they changed.
        static string YapsFolder;
        static string[] YapsRead;
        static string YapsJoined;

        // A plug drawing its second copy, by the value its material holds.
        public static bool Duplicate(Material m) =>
            m != null && m.HasProperty("_YAPS_CopyStart") && m.GetFloat("_YAPS_CopyStart") > 0;

        // The copy's code costs every vertex registers (52 temps to 59 under
        // fxc) whether or not a copy is there, so only a material that draws
        // one gets it, as a shader of its own beside the plain one.
        static string YapsSourceFor(bool duplicate, Roles roles, out string failure)
        {
            string yaps = LoadYapsSource(out failure);
            if (yaps == null) return null;
            // In the text, so the name hashes them and each set of roles is a shader of its own.
            if ((roles & Roles.Plug) == 0) yaps = "#define YAPS_NO_PLUG\n" + yaps;
            if ((roles & Roles.Socket) == 0) yaps = "#define YAPS_NO_SOCKET\n" + yaps;
            if ((roles & Roles.Squash) == 0) yaps = "#define YAPS_NO_SQUASH\n" + yaps;
            if ((roles & Roles.Jelly) == 0) yaps = "#define YAPS_NO_JELLY\n" + yaps;
            else if ((roles & Roles.JellyUnread) != 0) yaps = $"#define YAPS_JELLY_SLOTS {4 - ((int) (roles & Roles.JellyUnread) >> 4)}\n" + yaps;
            return duplicate ? "#define YAPS_DUPLICATE\n" + yaps : yaps;
        }

        static string LoadYapsSource(out string failure)
        {
            failure = null;
            Timing.Loads++;
            string folder = YapsFolder != null && File.Exists(YapsFolder + "/yaps_deform.cginc")
                ? YapsFolder : YapsFolder = FindYapsFolder();
            if (folder == null)
            {
                failure = "the YAPS shader includes could not be found in the project";
                return null;
            }

            // Dependency order: inlined, the include guards cannot reorder. The socket
            // include always comes, so one body serves both ends.
            string[] names =
            {
                "yaps_props.cginc", "yaps_atlas.cginc", "yaps_resolve.cginc", "yaps_deform.cginc",
                "yaps_socket.cginc", "yaps_dent.cginc", "yaps_jelly.cginc",
            };
            var read = new string[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                string path = folder + "/" + names[i];
                if (!File.Exists(path))
                {
                    failure = $"\"{names[i]}\" is missing from the YAPS includes";
                    return null;
                }
                read[i] = File.ReadAllText(path);
            }
            if (YapsRead != null && read.SequenceEqual(YapsRead)) return YapsJoined;
            var sb = new StringBuilder();
            foreach (string text in read)
            {
                // Strip the includes of each other: everything is being
                // concatenated, so those lines would look for files that
                // will not exist beside the patched shader.
                sb.AppendLine(Regex.Replace(text, @"#include\s+""yaps_\w+\.cginc""\s*", ""));
            }
            YapsRead = read;
            return YapsJoined = sb.ToString();
        }

        static string FindYapsFolder()
        {
            foreach (string guid in AssetDatabase.FindAssets("yaps_deform"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("yaps_deform.cginc", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetDirectoryName(path)?.Replace('\\', '/');
                }
            }
            return null;
        }

        // --- writing and verifying ------------------------------------

        static Shader WriteAndVerify(List<ShaderSpiPatcher.SourceFile> unit, string sourcePath,
            string dir, string hash, out string error)
        {
            error = null;
            bool known = AssetDatabase.IsValidFolder(dir);
            Directory.CreateDirectory(dir);

            // The hash names the file as well as the shader. Two locked Poiyomi
            // materials both come from a "Poiyomi Toon.shader" of their own, and
            // one output name would have the second write over the first.
            var shaderFile = unit[0];
            string tag = "_YAPS_" + hash.Substring(0, Math.Min(8, hash.Length));
            shaderFile.OutputName = Path.GetFileNameWithoutExtension(sourcePath) + tag + ".shader";

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { shaderFile.OutputName };
            foreach (var file in unit.Skip(1))
            {
                string stem = Path.GetFileNameWithoutExtension(file.OriginalPath) + tag;
                string extension = Path.GetExtension(file.OriginalPath);
                string name = stem + extension;
                for (int n = 2; !used.Add(name); n++)
                {
                    name = stem + "_" + n + extension;
                }
                file.OutputName = name;
            }

            // Resolve each include against the file it appears in, never the
            // spelling it was first seen under: one file is often referred to two
            // different ways.
            var byPath = new Dictionary<string, ShaderSpiPatcher.SourceFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in unit)
            {
                byPath[Path.GetFullPath(file.OriginalPath)] = file;
            }
            foreach (var file in unit)
            {
                string folder = Path.GetDirectoryName(file.OriginalPath) ?? ".";
                file.Text = ShaderSpiPatcher.IncludeDirective.Replace(file.Text, m =>
                {
                    string candidate = ShaderSpiPatcher.ResolveInclude(folder, m.Groups[2].Value);
                    if (candidate != null
                        && byPath.TryGetValue(Path.GetFullPath(candidate), out var target)
                        && target != file)
                    {
                        return m.Groups[1].Value + target.OutputName + m.Groups[3].Value;
                    }
                    return m.Value;
                });
                // One line ending throughout, whichever the source used. The inlined
                // YAPS source may not match, and Unity warns about a mixed file on
                // every import.
                file.Text = file.Text.Replace("\r\n", "\n");
                if (file.Crlf)
                {
                    file.Text = file.Text.Replace("\n", "\r\n");
                }
            }

            // Every slot, extra and socket on one source arrives here with the
            // same text. Rewriting a file bumps its time, and that alone
            // reimports and recompiles it, so an identical, working copy is
            // used as it is. Anything less goes the whole way below.
            string shaderPath = dir + "/" + shaderFile.OutputName;
            if (unit.All(f =>
                {
                    string path = dir + "/" + f.OutputName;
                    return File.Exists(path) && File.ReadAllText(path) == f.Text;
                })
                && AssetDatabase.LoadAssetAtPath<Shader>(shaderPath) is Shader existing
                && Broken(existing) == null)
            {
                return existing;
            }

            var written = new List<string>();
            var created = new List<string>();
            foreach (var file in unit)
            {
                string path = dir + "/" + file.OutputName;
                // A file already here has this name and may be in use. A
                // failure below removes only what this call created.
                if (!File.Exists(path)) created.Add(path);
                File.WriteAllText(path, file.Text);
                written.Add(path);
            }
            if (Deferred != null)
            {
                Deferred.AddRange(written);
                return null;
            }

            // Includes first: importing the shader is what compiles it, and
            // it fails on includes not yet in the AssetDatabase.
            foreach (string path in written.Skip(1))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            // Only for a folder made on disk just now, which the AssetDatabase has not seen. A
            // refresh rescans the whole project, and once per shader it cost about a minute each
            // in a large one.
            if (!known) AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);

            var result = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (result == null)
            {
                error = "Unity did not import it as a shader at all";
                CleanUp(created, shaderPath);
                return null;
            }
            error = Broken(result);
            if (error != null)
            {
                // Keep the failing source so the error can be read against
                // real line numbers.
                File.WriteAllText(shaderPath + ".failed.txt", shaderFile.Text);
                CleanUp(created, null);
                return null;
            }
            return result;
        }

        // Why the copy would not draw on `like`, compiled as wearing would (every pass, its
        // variant). Import compiles too little: a copy that then timed out drew garbage. Free on reuse.
        static string Compiled(Shader shader, Material like)
        {
            var stand = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, shaderKeywords = like.shaderKeywords };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                for (int pass = 0; pass < stand.passCount; pass++) ShaderUtil.CompilePass(stand, pass, true);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stand);
                Timing.CompileMs += clock.ElapsedMilliseconds;
                Timing.Compiled++;
            }
            return Broken(shader);
        }

        // Why the copy would not draw, or null. A pass Unity's upgrader drops for DX11 is only a
        // warning, "not supported on this GPU", so the error check alone passed a copy that drew
        // nothing. Compiling every pass here was tried for squash and pulled: it changed which
        // shaders were refused, and the import already compiles the default variant.
        static string Broken(Shader shader)
        {
            bool error = HasError(shader);
            if (!error && shader.isSupported) return null;
            var messages = Messages(shader)
                .OrderBy(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error ? 0 : 1).Take(3).ToArray();
            string said = messages.Length > 0
                ? string.Join("; ", messages.Select(m => $"line {m.line}: {m.message}"))
                : "no message";
            return error ? said : $"Unity cannot draw it on this GPU ({said})";
        }

        static void CleanUp(List<string> written, string keep)
        {
            foreach (string path in written)
            {
                if (path != keep)
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }
        }

        internal static string Hash(string input, int bytes = 6)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(digest, 0, bytes).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
#endif
