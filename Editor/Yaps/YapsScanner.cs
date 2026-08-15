// Finds every penetration setup under a root — DPS, TPS, SPS, or already
// YAPS — and says what it found. Touches nothing.
//
// This is the first thing the YAPS toolkit does and the last thing that
// should ever surprise a user: "Found: 1 DPS penetrator on Cock_Hyper
// (0.354 m), 2 DPS orifices, 0 TPS, 0 SPS." Everything downstream —
// upgrade, verify, the overlay — starts from this list, so it errs toward
// finding and describing rather than deciding. A thing that looks half
// like a socket is reported as such, with what is missing named.
//
// ---------------------------------------------------------------------
// HOW EACH SYSTEM ANNOUNCES ITSELF, read from the systems' own shipped
// files on 2026-08-15, not from memory
// ---------------------------------------------------------------------
//
// Raliv DPS penetrator
//   material on Raliv's penetrator shader (has _EntranceStiffness and
//   _ReCurvature), and a TIP light: black-ish (0.004, so "black" is a
//   threshold), ForceVertex, range 0.49, intensity = plug length. The tip
//   lives in a NESTED prefab (Includes/Tip.prefab) under the penetrator,
//   so the search walks the whole subtree.
// Raliv DPS orifice
//   two black ForceVertex lights: 0.41 (hole) or 0.42 (ring), and 0.45
//   (the normal), on objects named OrificeTracker / OrificeNormalTracker
//   in the shipped prefabs; a bulger tube has a material with _OrificeData
//   and _Shape1Depth.
// Thry TPS
//   a Poiyomi material with _TPS_PenetratorEnabled = 1 (plug), or contact
//   pointers tagged TPS_Orf_Root / TPS_Orf_Norm (orifice). No lights.
// VRCFury SPS
//   objects named BakedSpsPlug / BakedSpsSocket, _SPS_Bake on the
//   material, lights at 0.4106 / 0.4206 / 0.4506, pointers SPSLL_*.
// YAPS
//   _YAPS_Bake on the material, or a YapsPlug / YapsSocket component.
//
// A light is a PROTOCOL light when its colour is near black and its range
// is under 0.5; the second decimal of the range says what it is. Same
// decoder the shader uses, in C#.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsScanner
    {
        public enum Kind { Plug, Socket }

        // One thing found. Everything a user or an upgrader needs to know
        // about it, and nothing changed to learn it.
        public class Found
        {
            public Kind Kind;
            public YapsLegacyMap.Origin Origin;
            public Transform Root;            // the object that IS the plug or socket
            public Renderer Renderer;         // the mesh, if one was found
            public Material Material;         // its penetration material, if any
            public int MaterialSlot = -1;
            public bool IsHole;               // sockets: hole (true) or ring
            public float StatedLength;        // plugs: what the material says, 0 if it does not
            public List<Light> Lights = new List<Light>();
            public List<CVRPointer> Pointers = new List<CVRPointer>();
            public List<string> Notes = new List<string>();   // what is missing or odd

            public string Describe()
            {
                string what = Kind == Kind.Plug ? "penetrator" : (IsHole ? "hole" : "ring");
                string size = Kind == Kind.Plug && StatedLength > 0 ? $" ({StatedLength:0.###} m)" : "";
                string where = Root != null ? $" on \"{Root.name}\"" : "";
                string note = Notes.Count > 0 ? " — " + string.Join("; ", Notes) : "";
                return $"{Origin} {what}{size}{where}{note}";
            }
        }

        public class Result
        {
            public List<Found> Plugs = new List<Found>();
            public List<Found> Sockets = new List<Found>();
            public int Total => Plugs.Count + Sockets.Count;

            public string Summary()
            {
                if (Total == 0)
                {
                    return "Nothing found: no DPS, TPS, SPS or YAPS plug or socket under this object.";
                }
                var parts = new List<string>();
                foreach (var origin in new[] { YapsLegacyMap.Origin.YAPS, YapsLegacyMap.Origin.SPS,
                                               YapsLegacyMap.Origin.TPS, YapsLegacyMap.Origin.DPS })
                {
                    int p = Plugs.Count(f => f.Origin == origin);
                    int s = Sockets.Count(f => f.Origin == origin);
                    if (p + s == 0) continue;
                    parts.Add($"{origin}: {p} plug(s), {s} socket(s)");
                }
                return "Found " + string.Join("; ", parts) + ".";
            }
        }

        // The decoder, in C#. Mirrors yaps_resolve.cginc.
        public static bool IsProtocolLight(Light light)
        {
            if (light == null || light.type != LightType.Point) return false;
            var c = light.color;
            if (c.r > 0.02f || c.g > 0.02f || c.b > 0.02f) return false;   // real lighting
            return light.range > 0.05f && light.range < 0.5f;
        }

        public static int LightDigit(Light light) => Mathf.RoundToInt(light.range % 0.1f * 100f);

        public static Result Scan(GameObject root)
        {
            var result = new Result();
            if (root == null) return result;

            var claimed = new HashSet<Transform>();

            // 0. Authored YAPS, by component. First and unconditional: a
            //    freshly placed socket carries the same lights a DPS orifice
            //    does, and would otherwise be reported as one.
            foreach (var s in root.GetComponentsInChildren<Yaps.YapsSocket>(true))
            {
                var f = new Found { Kind = Kind.Socket, Origin = YapsLegacyMap.Origin.YAPS,
                    Root = s.transform, IsHole = s.kind == Yaps.YapsSocket.SocketKind.Hole,
                    Renderer = s.renderer };
                GatherAround(f, s.transform);
                if (f.Lights.Count == 0 && f.Pointers.Count == 0) f.Notes.Add("component only — not built yet");
                Add(result, f);
                claimed.Add(s.transform);
            }
            foreach (var p in root.GetComponentsInChildren<Yaps.YapsPlug>(true))
            {
                var f = new Found { Kind = Kind.Plug, Origin = YapsLegacyMap.Origin.YAPS,
                    Root = p.transform, Renderer = p.Target, StatedLength = p.lengthOverride };
                if (f.Renderer != null)
                {
                    var mats = f.Renderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] != null && mats[i].HasProperty("_YAPS_Bake")) { f.Material = mats[i]; f.MaterialSlot = i; break; }
                    }
                }
                GatherAround(f, p.transform);
                if (f.Material == null) f.Notes.Add("component only — not baked yet");
                Add(result, f);
                claimed.Add(p.transform);
                if (f.Renderer != null) claimed.Add(f.Renderer.transform);
            }

            // 1. Already YAPS, by material. Reported before the legacy
            //    systems so an upgrade run twice edits rather than re-detects.
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var origin = YapsLegacyMap.Detect(mats[i], out var part);
                    if (origin != YapsLegacyMap.Origin.YAPS) continue;
                    var f = new Found
                    {
                        Kind = part == YapsLegacyMap.Part.Plug ? Kind.Plug : Kind.Socket,
                        Origin = origin, Root = r.transform, Renderer = r,
                        Material = mats[i], MaterialSlot = i,
                        StatedLength = mats[i].HasProperty("_YAPS_Length") ? mats[i].GetFloat("_YAPS_Length") : 0f,
                    };
                    GatherAround(f, r.transform);
                    Add(result, f);
                    claimed.Add(r.transform);
                }
            }

            // 2. SPS, by the objects VRCFury's bake leaves behind. The bake
            //    names them, so this is the most reliable of the three.
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || claimed.Contains(t)) continue;
                bool plug = t.name.Contains("BakedSpsPlug");
                bool socket = t.name.Contains("BakedSpsSocket");
                if (!plug && !socket) continue;
                var f = new Found { Kind = plug ? Kind.Plug : Kind.Socket, Origin = YapsLegacyMap.Origin.SPS, Root = t };
                if (plug)
                {
                    FindRendererFor(f, t, "_SPS_Bake");
                    if (f.Material != null && f.Material.HasProperty("_SPS_Length"))
                    {
                        f.StatedLength = f.Material.GetFloat("_SPS_Length");
                    }
                }
                GatherAround(f, t);
                if (socket)
                {
                    // Hole or ring is in the light range VRCFury baked.
                    var rootLight = f.Lights.FirstOrDefault(l => LightDigit(l) == 1 || LightDigit(l) == 2);
                    f.IsHole = rootLight != null && LightDigit(rootLight) == 1;
                    if (rootLight == null) f.Notes.Add("no root light found (bake may be inactive)");
                }
                Add(result, f);
                claimed.Add(t);
            }

            // 3. TPS: a plug is a Poiyomi material with the flag on; an
            //    orifice is a pair of pointers and nothing else.
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (claimed.Contains(r.transform)) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (YapsLegacyMap.Detect(mats[i], out _) != YapsLegacyMap.Origin.TPS) continue;
                    var f = new Found
                    {
                        Kind = Kind.Plug, Origin = YapsLegacyMap.Origin.TPS, Root = r.transform,
                        Renderer = r, Material = mats[i], MaterialSlot = i,
                        StatedLength = mats[i].HasProperty("_TPS_PenetratorLength")
                            ? mats[i].GetFloat("_TPS_PenetratorLength") : 0f,
                    };
                    GatherAround(f, r.transform);
                    Add(result, f);
                    claimed.Add(r.transform);
                }
            }
            foreach (var p in root.GetComponentsInChildren<CVRPointer>(true))
            {
                if (p == null || p.type != "TPS_Orf_Root") continue;
                var host = p.transform.parent != null ? p.transform.parent : p.transform;
                if (claimed.Contains(host)) continue;
                var f = new Found { Kind = Kind.Socket, Origin = YapsLegacyMap.Origin.TPS, Root = host };
                GatherAround(f, host);
                if (!f.Pointers.Any(q => q.type == "TPS_Orf_Norm"))
                {
                    f.Notes.Add("no TPS_Orf_Norm — the socket has no axis, plugs will aim rather than thread");
                }
                Add(result, f);
                claimed.Add(host);
            }

            // 4. DPS: a plug is Raliv's material (plus a 0.49 tip light,
            //    usually in a nested prefab); an orifice is a 0.41/0.42 light
            //    with a 0.45 beside it, or a bulger tube's material.
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (claimed.Contains(r.transform)) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (YapsLegacyMap.Detect(mats[i], out var part) != YapsLegacyMap.Origin.DPS) continue;
                    var f = new Found
                    {
                        Kind = part == YapsLegacyMap.Part.Plug ? Kind.Plug : Kind.Socket,
                        Origin = YapsLegacyMap.Origin.DPS, Root = r.transform,
                        Renderer = r, Material = mats[i], MaterialSlot = i,
                        StatedLength = mats[i].HasProperty("_Length") ? mats[i].GetFloat("_Length") : 0f,
                    };
                    // The tip may sit above the renderer (the FBX is a
                    // sibling of the Tip prefab under the penetrator root).
                    var searchFrom = r.transform.parent != null ? r.transform.parent : r.transform;
                    GatherAround(f, searchFrom);
                    if (f.Kind == Kind.Plug && !f.Lights.Any(l => LightDigit(l) == 9))
                    {
                        f.Notes.Add("no 0.49 tip light — DPS orifices cannot see this plug");
                    }
                    Add(result, f);
                    claimed.Add(r.transform);
                }
            }
            // DPS orifices with no bulger material: lights alone.
            foreach (var l in root.GetComponentsInChildren<Light>(true))
            {
                if (!IsProtocolLight(l)) continue;
                int d = LightDigit(l);
                if (d != 1 && d != 2) continue;
                var host = l.transform.parent != null ? l.transform.parent : l.transform;
                if (claimed.Contains(host) || claimed.Contains(l.transform)) continue;
                // Already inside a found socket's subtree? Skip.
                if (result.Sockets.Any(s => s.Root != null && l.transform.IsChildOf(s.Root))) continue;
                var f = new Found { Kind = Kind.Socket, Origin = YapsLegacyMap.Origin.DPS, Root = host, IsHole = d == 1 };
                GatherAround(f, host);
                if (!f.Lights.Any(x => LightDigit(x) == 5 || LightDigit(x) == 6))
                {
                    f.Notes.Add("no normal light (0.45) — the socket has no axis");
                }
                Add(result, f);
                claimed.Add(host);
            }

            return result;
        }

        // Everything YAPS-relevant beneath (and, for lights, beside) a
        // transform: protocol lights, contact pointers.
        static void GatherAround(Found f, Transform at)
        {
            foreach (var l in at.GetComponentsInChildren<Light>(true))
            {
                if (IsProtocolLight(l)) f.Lights.Add(l);
            }
            foreach (var p in at.GetComponentsInChildren<CVRPointer>(true))
            {
                if (p != null && !string.IsNullOrEmpty(p.type)) f.Pointers.Add(p);
            }
        }

        // The renderer whose material carries `marker`, at or above the
        // object, or the first renderer beneath it.
        static void FindRendererFor(Found f, Transform t, string marker)
        {
            for (var at = t; at != null; at = at.parent)
            {
                var r = at.GetComponent<Renderer>();
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i].HasProperty(marker))
                    {
                        f.Renderer = r; f.Material = mats[i]; f.MaterialSlot = i;
                        return;
                    }
                }
            }
            var below = t.GetComponentInChildren<Renderer>(true);
            if (below != null)
            {
                f.Renderer = below;
                f.Notes.Add("no penetration material found; the mesh beneath is assumed");
            }
        }

        static void Add(Result result, Found f)
        {
            if (f.Kind == Kind.Plug) result.Plugs.Add(f); else result.Sockets.Add(f);
        }
    }
}
#endif
