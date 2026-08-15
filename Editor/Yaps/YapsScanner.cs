// Finds every penetration setup under a root, DPS, TPS, SPS or YAPS,
// and says what it found. Touches nothing.
//
// A socket is one object with a cluster of markers beneath it. Markers
// are gathered, each walked up to the object that owns the cluster, and
// grouped by that owner.
//
// How each system announces itself:
//   DPS plug     material with _EntranceStiffness and _ReCurvature; a
//                black ForceVertex tip light at range 0.49, intensity =
//                length, in a nested prefab.
//   DPS orifice  lights at 0.41 (hole) or 0.42 (ring) and 0.45 (normal).
//   TPS          Poiyomi material with _TPS_PenetratorEnabled; pointers
//                TPS_Orf_Root and TPS_Orf_Norm. No lights.
//   SPS          BakedSpsPlug and BakedSpsSocket objects, _SPS_Bake on the
//                material, lights 0.4106, 0.4206, 0.4506, pointers SPSLL_*.
//   YAPS         _YAPS_Bake on the material; YapsPlug and YapsSocket.
//
// A protocol light is near black with a range under 0.5; the second
// decimal of the range says what it is.
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

        // One plug or socket, as one object, with everything it carries.
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

        // The decoder, mirroring yaps_resolve.cginc.
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

            // Plugs first: a plug material, a YapsPlug, or a BakedSpsPlug object.
            foreach (var comp in root.GetComponentsInChildren<Yaps.YapsPlug>(true))
            {
                var f = NewPlug(comp.transform, comp.Target);
                f.IsYapsAlready = true;
                f.Origin = YapsLegacyMap.Origin.YAPS;
                f.StatedLength = comp.lengthOverride;
                // The component's renderer may already wear the material. Finish
                // claims the renderer, so read it here.
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
            // The plug object and its renderer are often different subtrees.
            // Pair a plug material with the nearest plug object under the avatar.
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
                        // Not above the renderer: the first unclaimed plug object.
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

            // Sockets: every marker, grouped by the owning object.
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
            // Skip markers under a plug already found.
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
            // YapsSocket components with nothing built still count.
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

        // The object that is the socket, walking up from a marker.
        static Transform SocketOwner(Transform marker, Transform root)
        {
            // Inclusive of the root; the scan target may be the socket.
            for (var at = marker; at != null; at = at.parent)
            {
                string n = at.name;
                if (n == "YAPS Socket" || n.StartsWith("BakedSpsSocket")) return at;
                if (at.GetComponent<Yaps.YapsSocket>() != null) return at;
                if (at == root) break;
            }
            // No named owner: the nearest ancestor holding a light and a pointer,
            // else the marker's parent.
            for (var at = marker.parent; at != null && at != root; at = at.parent)
            {
                bool light = at.GetComponentsInChildren<Light>(true).Any(IsProtocolLight);
                bool pointer = at.GetComponentsInChildren<CVRPointer>(true).Any(p => p != null && (IsSocketRootTag(p.type) || IsSocketFrontTag(p.type)));
                if (light && pointer) return at;
                // A DPS-only orifice: two lights, no pointers.
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

        // A name a user recognises. "[VF80] Pussy" gives "Pussy".
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

            // Readable by a family when announced in its terms.
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

            // Lights: DPS. TPS names: TPS. SPS names: SPS. YAPS reads all three.
            if (rootLight != null) f.ReadableBy |= Speaks.DPS;
            if (f.Pointers.Any(p => p.type.StartsWith("TPS_Orf_"))) f.ReadableBy |= Speaks.TPS;
            if (f.Pointers.Any(p => p.type.StartsWith("SPSLL_Socket_"))) f.ReadableBy |= Speaks.SPS;
            if (f.ReadableBy != Speaks.None) f.ReadableBy |= Speaks.YAPS;

            // Authored by, best guess.
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
