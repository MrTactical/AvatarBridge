// Finds every penetration setup under a root — DPS, TPS, SPS, YAPS, or
// any mixture — and says what it found. Touches nothing.
//
// This is the first thing the YAPS toolkit does and the last thing that
// should ever surprise a user: "Pussy: hole, readable by DPS, TPS and SPS,
// 2 lights, 8 pointers, has an axis." Everything downstream — upgrade,
// verify, the overlay — starts from this list, so it errs toward finding
// and describing rather than deciding.
//
// ---------------------------------------------------------------------
// ONE SOCKET, MANY MARKERS — the model, and why the first draft was wrong
// ---------------------------------------------------------------------
//
// A socket is one OBJECT with a cluster of markers beneath it: DPS-style
// lights, TPS pointers, SPS pointers, sometimes all three — a converted
// avatar's sockets carry all three deliberately, so every plug family
// reads them. The first draft attributed by MARKER and reported Angela's
// twelve sockets thirty-seven times: once as TPS, once as DPS, once as
// SPS. It also looked for a socket's front pointer under the root
// pointer's PARENT, when the two sit under sibling objects, and so
// warned "no axis" on sockets that had one.
//
// So this walks the other way. Gather every marker under the root; for
// each, walk UP to the object that owns the whole cluster (the nearest
// ancestor holding every marker within reach that belongs to the same
// socket); group by that owner; then describe each cluster by everything
// it carries. A socket readable by three systems is one row saying so.
//
// ---------------------------------------------------------------------
// HOW EACH SYSTEM ANNOUNCES ITSELF, read from the systems' own shipped
// files on 2026-08-15, not from memory
// ---------------------------------------------------------------------
//
// Raliv DPS penetrator   material with _EntranceStiffness + _ReCurvature;
//                        a TIP light, near-black (0.004), ForceVertex, range
//                        0.49, intensity = plug length, in a NESTED prefab.
// Raliv DPS orifice      lights at 0.41 (hole) or 0.42 (ring), and 0.45 (the
//                        normal). Bulger tube: material with _OrificeData.
// Thry TPS               a Poiyomi material with _TPS_PenetratorEnabled = 1
//                        (plug); pointers TPS_Orf_Root / TPS_Orf_Norm
//                        (orifice). No lights of its own.
// VRCFury SPS            BakedSpsPlug / BakedSpsSocket objects, _SPS_Bake on
//                        the material, lights 0.4106 / 0.4206 / 0.4506,
//                        pointers SPSLL_*.
// YAPS                   _YAPS_Bake on the material; YapsPlug / YapsSocket
//                        components; "YAPS Plug" / "YAPS Socket" objects.
//
// A light is a PROTOCOL light when its colour is near black and its range
// is under 0.5; the second decimal of the range says what it is. Same
// decoder the shader uses, in C#.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsScanner
    {
        public enum Kind { Plug, Socket }

        [Flags]
        public enum Speaks { None = 0, DPS = 1, TPS = 2, SPS = 4, YAPS = 8 }

        // One thing found: a plug or a socket, as ONE object, with every
        // system that reads it and everything it carries.
        public class Found
        {
            public Kind Kind;
            public Transform Root;             // the object that IS the plug or socket
            public string Name;                // a human name for it, best effort
            public Speaks ReadableBy;          // which plug families can find this socket / which sockets this plug
            public YapsLegacyMap.Origin Origin;   // what system AUTHORED it, best guess
            public bool IsYapsAlready;         // carries _YAPS_Bake or a component
            public bool IsHole;                // sockets
            public bool HasAxis;               // sockets: a front/normal marker exists
            public Renderer Renderer;
            public Material Material;
            public int MaterialSlot = -1;
            public float StatedLength;         // plugs
            public List<Light> Lights = new List<Light>();
            public List<CVRPointer> Pointers = new List<CVRPointer>();
            public List<string> Notes = new List<string>();

            public string ReadableList()
            {
                var parts = new List<string>();
                if ((ReadableBy & Speaks.DPS) != 0) parts.Add("DPS");
                if ((ReadableBy & Speaks.TPS) != 0) parts.Add("TPS");
                if ((ReadableBy & Speaks.SPS) != 0) parts.Add("SPS");
                if ((ReadableBy & Speaks.YAPS) != 0) parts.Add("YAPS");
                return parts.Count == 0 ? "nothing" : string.Join(" · ", parts);
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
                    return "Nothing found: no plug or socket of any system under this object.";
                }
                int yaps = Plugs.Count(p => p.IsYapsAlready) + Sockets.Count(s => s.IsYapsAlready);
                int holes = Sockets.Count(s => s.IsHole);
                var s = new List<string>();
                if (Plugs.Count > 0) s.Add($"{Plugs.Count} plug{(Plugs.Count == 1 ? "" : "s")}");
                if (Sockets.Count > 0) s.Add($"{Sockets.Count} socket{(Sockets.Count == 1 ? "" : "s")} ({holes} hole{(holes == 1 ? "" : "s")}, {Sockets.Count - holes} ring{(Sockets.Count - holes == 1 ? "" : "s")})");
                string already = yaps == Total ? " — all already YAPS" : yaps > 0 ? $" — {yaps} already YAPS" : "";
                return "Found " + string.Join(" and ", s) + already + ".";
            }
        }

        // The decoder, in C#. Mirrors yaps_resolve.cginc.
        public static bool IsProtocolLight(Light light)
        {
            if (light == null || light.type != LightType.Point) return false;
            var c = light.color;
            if (c.r > 0.02f || c.g > 0.02f || c.b > 0.02f) return false;
            return light.range > 0.05f && light.range < 0.5f;
        }

        public static int LightDigit(Light light) => Mathf.RoundToInt(light.range % 0.1f * 100f);

        static bool IsSocketRootTag(string t) =>
            t == "TPS_Orf_Root" || t == "SPSLL_Socket_Root" || t == "SPSLL_Socket_Hole" || t == "SPSLL_Socket_Ring"
            || t.StartsWith("TPS_Orf_Root_") || t.StartsWith("SPSLL_Socket_Root_")
            || t.StartsWith("SPSLL_Socket_Hole_") || t.StartsWith("SPSLL_Socket_Ring_");
        static bool IsSocketFrontTag(string t) =>
            t == "TPS_Orf_Norm" || t == "SPSLL_Socket_Front"
            || t.StartsWith("TPS_Orf_Norm_") || t.StartsWith("SPSLL_Socket_Front_");
        static bool IsPlugTag(string t) => t.StartsWith("TPS_Pen_") || t.StartsWith("SPSLL_Pen_");

        // --- the scan ---------------------------------------------------

        public static Result Scan(GameObject root)
        {
            var result = new Result();
            if (root == null) return result;
            var owned = new HashSet<Transform>();

            // PLUGS first. A plug is a renderer whose material carries a
            // deform (YAPS, SPS, TPS, DPS), or a YapsPlug component, or a
            // BakedSpsPlug object. Its markers (tip light, pen pointers)
            // are gathered from the object that owns it.
            foreach (var comp in root.GetComponentsInChildren<Yaps.YapsPlug>(true))
            {
                var f = NewPlug(comp.transform, comp.Target);
                f.IsYapsAlready = true;
                f.Origin = YapsLegacyMap.Origin.YAPS;
                f.StatedLength = comp.lengthOverride;
                // The component names its renderer, and on a baked plug that
                // renderer already wears the material. Read it here: Finish
                // claims the renderer, so the material loop below never sees
                // it, and a converted plug — component on the marker object,
                // material on the body mesh — read "not baked yet" beside a
                // summary line that had just found its bake.
                var target = comp.Target;
                if (target != null)
                {
                    var mats = target.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (comp.materialSlot >= 0 && i != comp.materialSlot) continue;
                        var origin = YapsLegacyMap.Detect(mats[i], out var part);
                        if (origin == YapsLegacyMap.Origin.None || part != YapsLegacyMap.Part.Plug) continue;
                        f.Material = mats[i]; f.MaterialSlot = i;
                        f.Origin = origin;
                        f.IsYapsAlready = origin == YapsLegacyMap.Origin.YAPS;
                        if (f.StatedLength <= 0f) f.StatedLength = StatedLength(mats[i], origin);
                        break;
                    }
                }
                Finish(f, owned);
                if (f.Material == null) f.Notes.Add("not baked yet");
                result.Plugs.Add(f);
            }
            // The plug OBJECT and the plug's RENDERER are routinely different
            // subtrees: on a converted avatar the material sits on the body
            // mesh at the avatar root while "YAPS Plug" (VRCFury's
            // "BakedSpsPlug") hangs under the shaft's bone with the pointers
            // and lights beneath it. So a material that says plug is paired
            // with the nearest such object anywhere under the avatar, not
            // only above the renderer — otherwise the plug reads as having no
            // markers it plainly has.
            var plugObjects = root.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == "YAPS Plug" || t.name.StartsWith("BakedSpsPlug"))
                .Where(t => !owned.Contains(t)).ToList();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (owned.Contains(r.transform)) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var origin = YapsLegacyMap.Detect(mats[i], out var part);
                    if (origin == YapsLegacyMap.Origin.None || part != YapsLegacyMap.Part.Plug) continue;
                    var owner = PlugOwner(r.transform);
                    if (owner == r.transform && plugObjects.Count > 0)
                    {
                        // Not above the renderer: take the first unclaimed plug
                        // object. One plug per avatar is overwhelmingly the case;
                        // several are paired in order.
                        owner = plugObjects[0];
                        plugObjects.RemoveAt(0);
                    }
                    var f = NewPlug(owner, r);
                    f.Material = mats[i]; f.MaterialSlot = i;
                    f.Origin = origin;
                    f.IsYapsAlready = origin == YapsLegacyMap.Origin.YAPS;
                    f.StatedLength = StatedLength(mats[i], origin);
                    Finish(f, owned);
                    result.Plugs.Add(f);
                    break;
                }
            }

            // SOCKETS: every marker, grouped by the object that owns the
            // cluster.
            var markers = new List<(Transform host, Light light, CVRPointer pointer)>();
            foreach (var l in root.GetComponentsInChildren<Light>(true))
            {
                if (!IsProtocolLight(l)) continue;
                int d = LightDigit(l);
                if (d == 8 || d == 9) continue;   // a plug's tracker, not a socket
                markers.Add((l.transform, l, null));
            }
            foreach (var p in root.GetComponentsInChildren<CVRPointer>(true))
            {
                if (p == null || string.IsNullOrEmpty(p.type)) continue;
                if (IsSocketRootTag(p.type) || IsSocketFrontTag(p.type)) markers.Add((p.transform, null, p));
            }
            // Skip markers that belong to a plug we already own.
            markers.RemoveAll(m => result.Plugs.Any(pl => pl.Root != null && m.host.IsChildOf(pl.Root)));

            var clusters = new Dictionary<Transform, Found>();
            foreach (var m in markers)
            {
                var owner = SocketOwner(m.host, root.transform);
                if (!clusters.TryGetValue(owner, out var f))
                {
                    f = new Found { Kind = Kind.Socket, Root = owner, Name = SocketName(owner) };
                    clusters[owner] = f;
                }
                if (m.light != null) f.Lights.Add(m.light); else f.Pointers.Add(m.pointer);
            }
            // YapsSocket components with nothing built yet still count.
            foreach (var comp in root.GetComponentsInChildren<Yaps.YapsSocket>(true))
            {
                if (!clusters.TryGetValue(comp.transform, out var f))
                {
                    f = new Found { Kind = Kind.Socket, Root = comp.transform, Name = SocketName(comp.transform) };
                    clusters[comp.transform] = f;
                }
                f.IsYapsAlready = true;
                f.Renderer = comp.renderer;
                f.IsHole = comp.kind == Yaps.YapsSocket.SocketKind.Hole;
                if (f.Lights.Count == 0 && f.Pointers.Count == 0) f.Notes.Add("not built yet");
            }

            foreach (var f in clusters.Values)
            {
                Classify(f);
                result.Sockets.Add(f);
            }
            result.Sockets.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return result;
        }

        // --- ownership -----------------------------------------------------

        // The object that IS the socket: walk up from a marker until the
        // parent would take us past a sibling cluster or to the avatar root.
        // Named markers of the systems' own bakes are recognised outright —
        // VRCFury's "BakedSpsSocket" and our "YAPS Socket" — and DPS's
        // OrificeTracker/OrificeNormalTracker sit directly under the socket.
        static Transform SocketOwner(Transform marker, Transform root)
        {
            // Inclusive of the root: the scan target may BE the socket (a
            // user picks the prefab they just dropped), and stopping short
            // of it split one socket into its Lights and Pointers folders.
            for (var at = marker; at != null; at = at.parent)
            {
                string n = at.name;
                if (n == "YAPS Socket" || n.StartsWith("BakedSpsSocket")) return at;
                if (at.GetComponent<Yaps.YapsSocket>() != null) return at;
                if (at == root) break;
            }
            // No named owner. Take the nearest ancestor that contains at
            // least one light AND one pointer, or failing that the marker's
            // parent — a lone DPS orifice is its own object with the two
            // lights directly beneath.
            for (var at = marker.parent; at != null && at != root; at = at.parent)
            {
                bool light = at.GetComponentsInChildren<Light>(true).Any(IsProtocolLight);
                bool pointer = at.GetComponentsInChildren<CVRPointer>(true).Any(p => p != null && (IsSocketRootTag(p.type) || IsSocketFrontTag(p.type)));
                if (light && pointer) return at;
                // A DPS-only orifice: two lights, no pointers. Its owner is
                // the first ancestor holding both lights.
                var lights = at.GetComponentsInChildren<Light>(true).Where(IsProtocolLight).ToList();
                if (lights.Count >= 2 && lights.Any(l => LightDigit(l) == 1 || LightDigit(l) == 2)
                                      && lights.Any(l => LightDigit(l) == 5 || LightDigit(l) == 6)) return at;
            }
            return marker.parent != null ? marker.parent : marker;
        }

        static Transform PlugOwner(Transform renderer)
        {
            for (var at = renderer; at != null; at = at.parent)
            {
                if (at.name == "YAPS Plug" || at.name.StartsWith("BakedSpsPlug")) return at;
                if (at.GetComponent<Yaps.YapsPlug>() != null) return at;
            }
            return renderer;
        }

        // A name a user recognises: VRCFury's "[VF80] Pussy" gives "Pussy";
        // otherwise the owner's own name, or its parent's if the owner is
        // one of the systems' generic names.
        static string SocketName(Transform owner)
        {
            for (var at = owner; at != null; at = at.parent)
            {
                string n = at.name;
                if (n == "YAPS Socket" || n.StartsWith("BakedSpsSocket") || n == "Original Object"
                    || n == "WorldSpace" || n == "OneSpace" || n == "Lights" || n == "Senders") continue;
                var m = System.Text.RegularExpressions.Regex.Match(n, @"^\[VF\d+\]\s*(.+)$");
                return m.Success ? m.Groups[1].Value : n;
            }
            return owner.name;
        }

        // --- description ------------------------------------------------------

        static Found NewPlug(Transform owner, Renderer renderer)
        {
            var f = new Found { Kind = Kind.Plug, Root = owner, Renderer = renderer };
            f.Name = owner != null ? SocketName(owner) : (renderer != null ? renderer.name : "plug");
            return f;
        }

        static void Finish(Found f, HashSet<Transform> owned)
        {
            if (f.Root != null)
            {
                owned.Add(f.Root);
                foreach (var l in f.Root.GetComponentsInChildren<Light>(true))
                    if (IsProtocolLight(l) && (LightDigit(l) == 8 || LightDigit(l) == 9)) f.Lights.Add(l);
                foreach (var p in f.Root.GetComponentsInChildren<CVRPointer>(true))
                    if (p != null && !string.IsNullOrEmpty(p.type) && IsPlugTag(p.type)) f.Pointers.Add(p);
            }
            if (f.Renderer != null) owned.Add(f.Renderer.transform);

            // A plug is READABLE BY a socket family when it announces itself
            // in that family's terms.
            if (f.Lights.Any(l => LightDigit(l) == 9)) f.ReadableBy |= Speaks.DPS;
            if (f.Pointers.Any(p => p.type.StartsWith("TPS_Pen_"))) f.ReadableBy |= Speaks.TPS;
            if (f.Pointers.Any(p => p.type.StartsWith("SPSLL_Pen_"))) f.ReadableBy |= Speaks.SPS;
            if (f.IsYapsAlready) f.ReadableBy |= Speaks.YAPS;

            if (f.Material != null && !f.IsYapsAlready)
                f.Notes.Add($"{f.Origin} deform — upgrade carries its settings onto YAPS");
            if ((f.ReadableBy & Speaks.DPS) == 0) f.Notes.Add("no tip light: DPS sockets cannot see it");
            if ((f.ReadableBy & (Speaks.TPS | Speaks.SPS)) == 0) f.Notes.Add("no plug pointers: contact sockets cannot see it");
        }

        static void Classify(Found f)
        {
            // Kind, from whichever marker states it.
            var rootLight = f.Lights.FirstOrDefault(l => LightDigit(l) == 1 || LightDigit(l) == 2);
            var holePtr = f.Pointers.Any(p => p.type.StartsWith("SPSLL_Socket_Hole"));
            var ringPtr = f.Pointers.Any(p => p.type.StartsWith("SPSLL_Socket_Ring"));
            if (rootLight != null) f.IsHole = LightDigit(rootLight) == 1;
            else if (holePtr) f.IsHole = true;
            else if (ringPtr) f.IsHole = false;

            f.HasAxis = f.Lights.Any(l => LightDigit(l) == 5 || LightDigit(l) == 6)
                     || f.Pointers.Any(p => IsSocketFrontTag(p.type));

            // Readable by: lights → DPS (and any converted plug); TPS names →
            // TPS; SPS names → SPS. YAPS plugs read all three.
            if (rootLight != null) f.ReadableBy |= Speaks.DPS;
            if (f.Pointers.Any(p => p.type.StartsWith("TPS_Orf_"))) f.ReadableBy |= Speaks.TPS;
            if (f.Pointers.Any(p => p.type.StartsWith("SPSLL_Socket_"))) f.ReadableBy |= Speaks.SPS;
            if (f.ReadableBy != Speaks.None) f.ReadableBy |= Speaks.YAPS;

            // Authored by, best guess, for the report only.
            if (f.IsYapsAlready || (f.Root != null && f.Root.name == "YAPS Socket")) f.Origin = YapsLegacyMap.Origin.YAPS;
            else if (f.Root != null && f.Root.name.StartsWith("BakedSpsSocket")) f.Origin = YapsLegacyMap.Origin.SPS;
            else if (rootLight != null && f.Pointers.Count == 0) f.Origin = YapsLegacyMap.Origin.DPS;
            else if (f.Pointers.Any(p => p.type.StartsWith("TPS_Orf_")) && rootLight == null) f.Origin = YapsLegacyMap.Origin.TPS;
            else f.Origin = YapsLegacyMap.Origin.SPS;
            if (f.Root != null && f.Root.name == "YAPS Socket") f.IsYapsAlready = true;

            if (!f.HasAxis) f.Notes.Add("no axis — plugs will aim at it rather than thread it");
            if (rootLight == null && !f.IsYapsAlready) f.Notes.Add("no marker lights — DPS plugs and light-only plugs cannot see it");
        }

        static float StatedLength(Material m, YapsLegacyMap.Origin origin)
        {
            switch (origin)
            {
                case YapsLegacyMap.Origin.YAPS: return m.HasProperty("_YAPS_Length") ? m.GetFloat("_YAPS_Length") : 0f;
                case YapsLegacyMap.Origin.SPS: return m.HasProperty("_SPS_Length") ? m.GetFloat("_SPS_Length") : 0f;
                case YapsLegacyMap.Origin.TPS: return m.HasProperty("_TPS_PenetratorLength") ? m.GetFloat("_TPS_PenetratorLength") : 0f;
                case YapsLegacyMap.Origin.DPS: return m.HasProperty("_Length") ? m.GetFloat("_Length") : 0f;
                default: return 0f;
            }
        }
    }
}
#endif
