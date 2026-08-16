// What a DPS, TPS or SPS setup says about itself, and how each value
// becomes a YAPS one. One table, read by the converter and the toolkit
// alike, so an author's tuning survives whichever door it came in by.
//
// Read from the systems' own materials and property blocks, not memory:
//
//   Raliv DPS penetrator  _Length _Squeeze _SqueezeDist _BulgePower
//                         _BulgeOffset _EntranceStiffness _Curvature
//                         _ReCurvature _Wriggle _WriggleSpeed
//   Raliv DPS orifice     _EntryOpenDuration _Shape{1,2,3}Depth
//                         _Shape{1,2,3}Duration _BlendshapePower
//   Thry TPS              _TPS_PenetratorLength _TPS_Squeeze
//                         _TPS_SqueezeDistance _TPS_Buldge
//                         _TPS_BuldgeDistance _TPS_IdleSkrinkLength
//                         _TPS_IdleSkrinkWidth _TPS_PumpingSpeed
//                         _TPS_PumpingStrength _TPS_PumpingWidth
//                         _TPS_BezierSmoothness _TPS_BezierStart
//                         _TPS_SmoothStart _TPS_MinimumOrificeDistance
//                         _TPS_BufferedDepth _TPS_BufferedStrength
//   VRCFury SPS           _SPS_Length _SPS_Overrun
//
// The scales differ and the mapping says how. Where a knob has no
// counterpart it is listed as such rather than silently dropped, so the
// report can say "your TPS idle gravity did not come across" instead of
// the user discovering it.
//
// Nothing here touches a mesh or a shader. It reads floats off a material
// and returns floats for another. The measuring, length and frame from
// the mesh, shapes from the blendshapes, is YapsBaker's, and where the
// author's stated length and the measured one disagree, the mesh wins and
// the disagreement is reported.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsLegacyMap
    {
        public enum Origin { None, DPS, TPS, SPS, YAPS }

        public enum Part { Plug, Socket }

        // One carried value: where it came from, where it went, and what
        // was done to it on the way. Kept so the report can list them.
        public struct Carried
        {
            public string From;
            public string To;
            public float Value;
            public string Note;
        }

        // Which system authored this material, if any. Decided from the
        // properties the material actually HAS, not the shader's name , 
        // Poiyomi hosts TPS inside its own shader, and a locked Poiyomi
        // renames the shader per material.
        public static Origin Detect(Material material, out Part part)
        {
            part = Part.Plug;
            if (material == null)
            {
                return Origin.None;
            }
            if (material.HasProperty("_YAPS_Bake"))
            {
                part = material.HasProperty("_YAPS_SocketPower")
                       && material.GetFloat("_YAPS_SocketPower") > 0
                       && !(material.HasProperty("_YAPS_Enabled") && material.GetFloat("_YAPS_Enabled") > 0)
                    ? Part.Socket : Part.Plug;
                return Origin.YAPS;
            }
            if (material.HasProperty("_SPS_Bake"))
            {
                return Origin.SPS;
            }
            if (material.HasProperty("_TPS_PenetratorEnabled")
                && material.GetFloat("_TPS_PenetratorEnabled") > 0.5f)
            {
                return Origin.TPS;
            }
            if (material.HasProperty("_OrificeData") && material.HasProperty("_Shape1Depth"))
            {
                part = Part.Socket;
                return Origin.DPS;
            }
            if (material.HasProperty("_EntranceStiffness") && material.HasProperty("_ReCurvature"))
            {
                return Origin.DPS;
            }
            return Origin.None;
        }

        // Carries the authored values from `source` onto `target`, and
        // names the ones with no counterpart. The length and radius are
        // the bake's own: DPS states knobs in metres, YAPS in fractions.
        public static List<Carried> Carry(Material source, Material target,
            List<string> unmapped, float plugLength = 0f, float plugRadius = 0f)
        {
            var carried = new List<Carried>();
            unmapped = unmapped ?? new List<string>();
            var system = Detect(source, out var part);
            switch (system)
            {
                case Origin.DPS when part == Part.Plug:
                    CarryDpsPlug(source, target, carried, unmapped, plugLength, plugRadius); break;
                case Origin.DPS: CarryDpsSocket(source, target, carried, unmapped); break;
                case Origin.TPS: CarryTps(source, target, carried, unmapped); break;
                case Origin.SPS: CarrySps(source, target, carried, unmapped); break;
            }
            return carried;
        }

        // --- Raliv DPS ------------------------------------------------------
        // Read off RalivDPS_Functions.cginc, since two labels mislead:
        // _Squeeze is an absolute radius in metres, not a fraction, and
        // _BulgePower is a fraction of radius added.

        static void CarryDpsPlug(Material s, Material t, List<Carried> carried, List<string> unmapped,
            float plugLength, float plugRadius)
        {
            // Direct, same meaning, same units.
            Direct(s, t, "_Curvature", "_YAPS_Curvature", carried);
            Direct(s, t, "_ReCurvature", "_YAPS_ReCurvature", carried);
            Direct(s, t, "_EntranceStiffness", "_YAPS_EntranceStiffness", carried);
            Direct(s, t, "_Wriggle", "_YAPS_WriggleStrength", carried);
            Direct(s, t, "_WriggleSpeed", "_YAPS_WriggleSpeed", carried);

            // The plug's size, for the metre-to-fraction conversions. The
            // measured length wins; the author's _Length is the fallback
            // and the disagreement, if any, is noted.
            float stated = s.HasProperty("_Length") ? s.GetFloat("_Length") : 0f;
            float length = plugLength > 0.001f ? plugLength : stated;
            if (plugLength > 0.001f && stated > 0.001f
                && Mathf.Abs(plugLength - stated) / stated > 0.10f)
            {
                carried.Add(new Carried { From = "_Length", To = "(measured length used instead)",
                    Value = plugLength,
                    Note = $"the material said {stated:0.###} m, the mesh measures {plugLength:0.###} m" });
            }

            // Squeeze: a radius the shaft is clamped to. As a fraction it is
            // squeezed BY, that is 1 - clampRadius / plugRadius.
            if (s.HasProperty("_Squeeze") && plugRadius > 0.0001f)
            {
                float clampTo = Mathf.Max(s.GetFloat("_Squeeze"), 0f);
                Set(t, "_YAPS_Squeeze", Mathf.Clamp01(1f - clampTo / plugRadius),
                    "_Squeeze", carried, "a radius in metres the shaft is clamped to; converted to a fraction squeezed by");
            }
            if (s.HasProperty("_SqueezeDist") && length > 0.001f)
            {
                Set(t, "_YAPS_SqueezeDistance", Mathf.Clamp(s.GetFloat("_SqueezeDist") / length, 0.01f, 1f),
                    "_SqueezeDist", carried, "metres, divided by the plug's length");
            }
            // Bulge: a fraction of radius, directly.
            Direct(s, t, "_BulgePower", "_YAPS_Bulge", carried);
            if (s.HasProperty("_BulgeOffset") && length > 0.001f)
            {
                Set(t, "_YAPS_BulgeDistance", Mathf.Clamp(s.GetFloat("_BulgeOffset") / length, 0.01f, 1f),
                    "_BulgeOffset", carried, "metres, divided by the plug's length");
            }
            Note(s, "_OrificeChannel", unmapped);   // DPS channels: one lights protocol here
        }

        static void CarryDpsSocket(Material s, Material t, List<Carried> carried, List<string> unmapped)
        {
            // DPS depths are metres past the socket, ours fractions of a
            // plug a socket cannot know. Carried against a 0.3 m
            // reference, which keeps the order and the proportions.
            const float reference = 0.3f;
            var starts = new Vector4(0f, 0.25f, 0.5f, 0.75f);
            var fades = new Vector4(0.3f, 0.3f, 0.3f, 0.3f);
            if (s.HasProperty("_EntryOpenDuration"))
            {
                fades.x = Mathf.Clamp(s.GetFloat("_EntryOpenDuration") / reference, 0.01f, 1f);
                carried.Add(new Carried { From = "_EntryOpenDuration", To = "_YAPS_SocketShapeFade.x", Value = fades.x });
            }
            for (int i = 1; i <= 3; i++)
            {
                if (s.HasProperty($"_Shape{i}Depth"))
                {
                    float v = Mathf.Clamp(s.GetFloat($"_Shape{i}Depth") / reference, 0f, 1f);
                    starts[i] = v;
                    carried.Add(new Carried { From = $"_Shape{i}Depth", To = $"_YAPS_SocketShapeStart[{i}]", Value = v,
                        Note = "metres past the socket, against a 0.3 m reference plug" });
                }
                if (s.HasProperty($"_Shape{i}Duration"))
                {
                    float v = Mathf.Clamp(s.GetFloat($"_Shape{i}Duration") / reference, 0.01f, 1f);
                    fades[i] = v;
                    carried.Add(new Carried { From = $"_Shape{i}Duration", To = $"_YAPS_SocketShapeFade[{i}]", Value = v });
                }
            }
            t.SetVector("_YAPS_SocketShapeStart", starts);
            t.SetVector("_YAPS_SocketShapeFade", fades);
            if (s.HasProperty("_BlendshapePower"))
            {
                Set(t, "_YAPS_SocketPower", Mathf.Clamp01(s.GetFloat("_BlendshapePower")),
                    "_BlendshapePower", carried, "clamped to 1; DPS allowed overdrive");
            }
            Note(s, "_BlendshapeBadScaleFix", unmapped);   // scale is read live here
        }

        // --- Thry TPS -------------------------------------------------------

        static void CarryTps(Material s, Material t, List<Carried> carried, List<string> unmapped)
        {
            Direct(s, t, "_TPS_Squeeze", "_YAPS_Squeeze", carried);
            Direct(s, t, "_TPS_SqueezeDistance", "_YAPS_SqueezeDistance", carried);
            Direct(s, t, "_TPS_Buldge", "_YAPS_Bulge", carried);
            Direct(s, t, "_TPS_BuldgeDistance", "_YAPS_BulgeDistance", carried);
            Direct(s, t, "_TPS_IdleSkrinkLength", "_YAPS_IdleLength", carried);
            Direct(s, t, "_TPS_IdleSkrinkWidth", "_YAPS_IdleWidth", carried);
            Direct(s, t, "_TPS_PumpingSpeed", "_YAPS_PumpSpeed", carried);
            Direct(s, t, "_TPS_PumpingStrength", "_YAPS_PumpStrength", carried);
            Direct(s, t, "_TPS_PumpingWidth", "_YAPS_PumpWidth", carried);
            Direct(s, t, "_TPS_BezierSmoothness", "_YAPS_BezierSmoothness", carried);
            Direct(s, t, "_TPS_BezierStart", "_YAPS_BezierStart", carried);
            Direct(s, t, "_TPS_SmoothStart", "_YAPS_SmoothStart", carried);
            Direct(s, t, "_TPS_MinimumOrificeDistance", "_YAPS_MinimumSocketDistance", carried);
            // Buffered depth lives on the channel's smoothing layer, not the
            // material. Reported as carried-elsewhere so the builder can set
            // yapsSocketFollow from it.
            if (s.HasProperty("_TPS_BufferedStrength") && s.GetFloat("_TPS_BufferedStrength") > 0)
            {
                carried.Add(new Carried { From = "_TPS_BufferedDepth/Strength", To = "socket follow (channel smoothing)",
                    Value = s.HasProperty("_TPS_BufferedDepth") ? s.GetFloat("_TPS_BufferedDepth") : 0f,
                    Note = "not a material value: it becomes the channel's per-frame step" });
            }
            Note(s, "_TPS_IdleGravity", unmapped);
            Note(s, "_TPS_BuldgeFalloffDistance", unmapped);
            Note(s, "_TPS_TwoSidedRings", unmapped);
        }

        // --- VRCFury SPS ------------------------------------------------------

        static void CarrySps(Material s, Material t, List<Carried> carried, List<string> unmapped)
        {
            Direct(s, t, "_SPS_Overrun", "_YAPS_Overrun", carried);
            // Everything else SPS carries is either the bake (redone from
            // the mesh) or transport (identity, tags, the channel's job).
        }

        // --- helpers ---------------------------------------------------------

        static void Direct(Material s, Material t, string from, string to, List<Carried> carried)
        {
            if (!s.HasProperty(from) || !t.HasProperty(to))
            {
                return;
            }
            float v = s.GetFloat(from);
            t.SetFloat(to, v);
            carried.Add(new Carried { From = from, To = to, Value = v });
        }

        static void Set(Material t, string to, float value, string from, List<Carried> carried, string note)
        {
            if (!t.HasProperty(to))
            {
                return;
            }
            t.SetFloat(to, value);
            carried.Add(new Carried { From = from, To = to, Value = value, Note = note });
        }

        static void Note(Material s, string property, List<string> unmapped)
        {
            if (s.HasProperty(property))
            {
                unmapped.Add(property);
            }
        }
    }
}
#endif
