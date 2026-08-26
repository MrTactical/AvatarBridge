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
            // Something to fix. An entry here turns the row amber.
            public List<string> Notes = new List<string>();

            // Working as designed, said out loud so it does not read as
            // damage. A row with only these stays green.
            public List<string> Expected = new List<string>();

            // The plug this mesh is PART OF, when it has no plug of its own.
            // A plug rooted high enough carries every mesh its bones move, and
            // each of those ends up wearing a patched material — which reads
            // as a plug in its own right and lists as a peer. It is not one:
            // it has no frame, no length and no settings of its own, it wears
            // the carrier's. Listing it as a peer invited exactly the mistake
            // that produced this field, a second plug component added to a
            // mesh already carried, which then re-baked it with its own frame
            // and broke the two apart.
            public Yaps.YapsPlug CarriedBy;

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
                    return "No penetration system on it yet: no DPS, TPS, SPS or YAPS plug or socket under this object. Its meshes and bones are what the buttons below turn into one.";
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
                        if (f.StatedLength <= 0f) f.StatedLength = StatedLength(mats[i], origin, target);
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
                    f.StatedLength = StatedLength(mats[i], origin, r);
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
            // Which plugs found by their MATERIAL are really another plug's
            // carried mesh. Only ever a mesh with no plug of its own: a
            // component is an author saying "this one is mine", and that is
            // answered as a conflict at bake, not quietly reparented here.
            foreach (var f in result.Plugs)
            {
                if (f.Root == null || f.Renderer == null) continue;
                if (f.Root.GetComponent<Yaps.YapsPlug>() != null) continue;
                foreach (var carrier in root.GetComponentsInChildren<Yaps.YapsPlug>(true))
                {
                    if (carrier == null || carrier.rootBone == null) continue;
                    if (carrier.Target == f.Renderer) continue;
                    if (!(f.Renderer is SkinnedMeshRenderer skin)) continue;
                    if (YapsNativeBuilder.SlotsWeightedTo(skin, carrier.rootBone).Count == 0) continue;
                    f.CarriedBy = carrier;
                    break;
                }
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
        // "[VF564] BakedSpsSocket" -> "BakedSpsSocket".
        public static string StripFuryId(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith("[VF", System.StringComparison.Ordinal))
            {
                return name;
            }
            int close = name.IndexOf(']');
            return close < 0 ? name : name.Substring(close + 1).TrimStart();
        }

        static Transform SocketOwner(Transform marker, Transform root)
        {
            // Inclusive of the root; the scan target may be the socket.
            for (var at = marker; at != null; at = at.parent)
            {
                // VRCFury stamps its own objects "[VF564] BakedSpsSocket",
                // so a StartsWith check misses every baked socket and the
                // heuristic below answers instead — a different ancestor for
                // the lights than for the pointers, which reads as two
                // sockets on one spot and invites deleting a working half.
                string n = StripFuryId(at.name);
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

            // Switched off is the difference between the preview and the
            // game. The preview reads transforms and bends a plug whatever
            // state anything is in; the game needs these components live.
            // A socket that previews perfectly and does nothing in game is
            // this, and it cost two people three evenings to find.
            // A socket switched off at its own root is the wearer using the
            // toggle, not a fault. Saying "nothing can find it" about a
            // socket somebody turned off on purpose sends them hunting a
            // bug they made themselves.
            bool socketItselfOff = f.Root != null && !f.Root.gameObject.activeInHierarchy;
            int darkPointers = f.Pointers.Count(p => p != null
                && (!p.enabled || !p.gameObject.activeInHierarchy));
            if (socketItselfOff)
            {
                f.Notes.Add("switched off — everything under it is dark until it is switched back on, " +
                            "which is what its menu toggle does in game");
            }
            else if (darkPointers > 0)
            {
                f.Notes.Add($"{darkPointers} of its pointers are switched off — nothing can find it in " +
                            "game, however well it previews");
            }
            int darkLights = f.Lights.Count(l => l != null
                && (!l.enabled || !l.gameObject.activeInHierarchy));
            if (darkLights > 0 && !socketItselfOff)
            {
                // Dark on purpose under the lighthouse: pairs wait on the
                // menu, one lit at a time. Only a socket dark with no
                // lighthouse to light it is a problem.
                var avatar = f.Root != null ? f.Root.GetComponentInParent<CVRAvatar>() : null;
                bool lighthouse = avatar != null && avatar.avatarSettings != null
                    && avatar.avatarSettings.settings != null
                    && avatar.avatarSettings.settings.Any(e => e != null && e.machineName == "YAPS/Lighthouse");
                if (lighthouse)
                {
                    f.Expected.Add("expected: its marker lights wait on the lighthouse, which lights one " +
                                   "socket at a time because a mesh has only four light slots. Old DPS toys " +
                                   "use the \"Marker lights\" menu to pick; everything else finds it by contact");
                }
                else
                {
                    f.Notes.Add($"{darkLights} of its marker lights are switched off — DPS plugs cannot see it");
                }
            }
            if (f.Root != null)
            {
                int deaf = f.Root.GetComponentsInChildren<CVRAdvancedAvatarSettingsTrigger>(true)
                    .Count(t => t != null && (!t.enabled || !t.gameObject.activeInHierarchy));
                if (deaf > 0 && !socketItselfOff)
                {
                    f.Notes.Add($"{deaf} of its receivers are switched off — it says where it is and " +
                                "never notices a plug arrive, which previews fine and does nothing in game");
                }
            }
        }

        static float StatedLength(Material m, YapsLegacyMap.Origin origin, Renderer renderer = null)
        {
            switch (origin)
            {
                // In metres, whatever units the bake measured it in.
                case YapsLegacyMap.Origin.YAPS: return YapsNativeBuilder.WorldLength(renderer, m);
                case YapsLegacyMap.Origin.SPS: return m.HasProperty("_SPS_Length") ? m.GetFloat("_SPS_Length") : 0f;
                case YapsLegacyMap.Origin.TPS: return m.HasProperty("_TPS_PenetratorLength") ? m.GetFloat("_TPS_PenetratorLength") : 0f;
                case YapsLegacyMap.Origin.DPS: return m.HasProperty("_Length") ? m.GetFloat("_Length") : 0f;
                default: return 0f;
            }
        }
    }
}
#endif
