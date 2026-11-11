// The jelly: a soft body for each pair the squash finds, stepped every frame on the GPU by the
// CCK's blitters and drawn by the body shader's Jelly role. Put on over a squash build, whose
// parts and shares it reads; a pair it refuses keeps the squash's own dent.
//
// A lattice of particles lives in a float render texture, each particle in its left half and where
// it was a pass before in its right. Two blitters step it into a twin and back, a pass each: every
// particle pulled toward where the rigid fit of its 3x3x3 neighbourhood puts it, the back face held
// on an anchor the material data provider hands in, the hands in reach pushing it aside. A third
// pass writes the far switch and what the jelly tells the animator into a strip the texture parser
// reads. Nothing is baked or synced; each client steps its own copy.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AvatarBridge
{
    internal static class YapsJelly
    {
        // Where every avatar's step shader lives: one copy, or each would compile every variant.
        internal const string Shaders = YapsNativeBuilder.OutputRoot + "/Jelly";

        // A build's choices, defaults as a user's build makes it.
        internal sealed class Options
        {
            // The fewest cells a part's lattice is cut into; a big part takes more, to MostCells.
            public float cells = BreastCells;
            // The jelly's own mass swings each part and the pair's cloth goes off; else the cloth carries it.
            public bool mass;
            // No mass at all: the lattices alone.
            public bool noMass;
            // How much of a held press moves the whole, and the whole's damping over critical; NaN keeps the default.
            public float give = float.NaN, zeta = float.NaN;
            // The plane the pair meet at.
            public bool plane = true;
            // MagicaCloth's colliders, other players' among them, felt as presses.
            public bool probes = true;
            // Twelve physics balls besides the probes, which the world's colliders push.
            public bool world = true;
            // How far off the skin a collider pushes a probe, the least: spread too thin for it to cover
            // the skin, it grows to the gap.
            public float probeReach = 0.04f;
            // On whatever the squash component says.
            public bool twitch, slide;
            // The menu and animator wired.
            public bool wire = true;
        }

        // How the build tunes a part its bone carries, before the part's feel scales it. Carried on
        // the cloth: the mass at 2.5 Hz, critically damped, a hand's press moving 0.525 of the whole,
        // held within 0.3 of its length any way, up and down too: a squeezed part goes where nothing
        // presses. 0.35 within 0.2 was tuned while a hand also shoved the cloth's bone; with the cloth
        // kept out of the touch it stopped a squeeze short. At 0.6 within 0.35 a squeeze from both
        // sides drove a pair together until their masses met, and the hands' push then took them
        // up toward the chin; held to 0.15 up and down, it rose 54 mm at 0.3 across, a hand's push
        // then turned by where the mass was (Lean). Leaned from its rest, it rises 16 mm and back.
        internal const float CarriedHz = 2.5f, CarriedZeta = 1f, CarriedGive = 0.525f, CarriedLimit = 0.3f;
        // How deep a hand presses before the skin holds it, metres, before Press depth scales it.
        // At 0.08 a squeeze from both sides stopped denting 4 cm short of hands 12 cm in.
        internal const float Deepest = 0.12f;

        // The presets' knobs in the Feel's order: firmness, settle, give, swing, spring back, taut,
        // press depth, slap. Breasts are what the build was tuned on; the rest are first guesses
        // from them, firmer and swinging less where the flesh sits tighter on the bone. Firm is
        // for skin close over muscle, an arm, a calf or a neck, which would otherwise feel like a breast.
        internal static readonly string[] FeelKnobs = { "firmness", "settle", "give", "swing", "springBack", "taut", "depth", "slap" };
        internal static readonly Dictionary<YapsSquashSettings.Preset, float[]> Presets = new Dictionary<YapsSquashSettings.Preset, float[]>
        {
            { YapsSquashSettings.Preset.Breasts, new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f } },
            { YapsSquashSettings.Preset.Butt, new[] { 1.25f, 1.2f, 0.85f, 0.75f, 1.2f, 1f, 1f, 1.25f } },
            { YapsSquashSettings.Preset.Thighs, new[] { 1.5f, 1.4f, 0.6f, 0.5f, 1.4f, 1.25f, 0.8f, 1f } },
            { YapsSquashSettings.Preset.Belly, new[] { 0.9f, 1.25f, 0.9f, 0.6f, 1f, 1f, 1.2f, 0.8f } },
            { YapsSquashSettings.Preset.Firm, new[] { 2f, 1.6f, 0.4f, 0.3f, 1.6f, 1.5f, 0.6f, 0.6f } },
        };

        internal static float[] KnobsOf(YapsSquashSettings.Feel f) => new[] { f.firmness, f.settle, f.give, f.swing, f.springBack, f.taut, f.depth, f.slap };

        // A part's preset from its name, else from the bone it rides: calves, thighs, then arms and
        // the neck (Firm), the chest, the spine, and anything else under the hips.
        internal static YapsSquashSettings.Preset Guess(string name, Transform rides, Animator animator)
        {
            if (Regex.IsMatch(name, "breast|boob|chest|pec", RegexOptions.IgnoreCase)) return YapsSquashSettings.Preset.Breasts;
            if (Regex.IsMatch(name, @"calf|calves|shin|lower[\W_]*leg|leg[\W_]*lower", RegexOptions.IgnoreCase)) return YapsSquashSettings.Preset.Firm;
            if (Regex.IsMatch(name, "thigh|leg", RegexOptions.IgnoreCase)) return YapsSquashSettings.Preset.Thighs;
            if (Regex.IsMatch(name, @"butt|glute|\bass\b|booty|\bbum|seat", RegexOptions.IgnoreCase)) return YapsSquashSettings.Preset.Butt;
            if (Regex.IsMatch(name, @"belly|stomach|tummy|\bgut|abdom", RegexOptions.IgnoreCase)) return YapsSquashSettings.Preset.Belly;
            if (Regex.IsMatch(name, @"\barm|upperarm|forearm|bicep|shoulder|neck", RegexOptions.IgnoreCase)) return YapsSquashSettings.Preset.Firm;
            if (rides == null || animator == null || !animator.isHuman) return YapsSquashSettings.Preset.Custom;
            bool Under(params HumanBodyBones[] bones) => bones.Select(animator.GetBoneTransform).Any(b => b != null && (rides == b || rides.IsChildOf(b)));
            if (Under(HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg)) return YapsSquashSettings.Preset.Firm;
            if (Under(HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg)) return YapsSquashSettings.Preset.Thighs;
            if (Under(HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.Neck)) return YapsSquashSettings.Preset.Firm;
            if (Under(HumanBodyBones.Chest, HumanBodyBones.UpperChest)) return YapsSquashSettings.Preset.Breasts;
            if (Under(HumanBodyBones.Spine)) return YapsSquashSettings.Preset.Belly;
            return Under(HumanBodyBones.Hips) ? YapsSquashSettings.Preset.Butt : YapsSquashSettings.Preset.Custom;
        }

        // The part's feel, a guessed preset's added the first time a build meets it.
        internal static YapsSquashSettings.Feel FeelOf(YapsSquashSettings s, string part, Transform rides, Animator animator)
        {
            var f = s?.feels.FirstOrDefault(q => q.part == part);
            if (f != null) return f;
            f = new YapsSquashSettings.Feel { part = part, preset = Guess(part, rides, animator) };
            if (Presets.TryGetValue(f.preset, out var k))
                (f.firmness, f.settle, f.give, f.swing, f.springBack, f.taut, f.depth, f.slap) = (k[0], k[1], k[2], k[3], k[4], k[5], k[6], k[7]);
            if (s == null) return f;
            Undo.RecordObject(s, "Put the breasts on the jelly");
            s.feels.Add(f);
            EditorUtility.SetDirty(s);
            return f;
        }

        // A part's feel on one of its step materials, each knob a share of what the build gives it:
        // the mass's rate, damping, give and limit as the build worked them out, the rest the tissue's.
        internal static void Tune(Material mat, YapsSquashSettings.Feel f, float hz, float zeta, float give, float limit)
        {
            // A little more pillow: the tissue's pull to its rest 2.4 a second, from 3.
            mat.SetFloat("_Hz", 2.4f * f.firmness);
            mat.SetFloat("_Stiff", Mathf.Clamp01(0.5f * f.firmness));
            // About 0.7 of critical at the tissue's own 3 Hz: a dent fills back, no wobble.
            mat.SetFloat("_Viscous", 25f * f.settle);
            mat.SetFloat("_Give", Mathf.Clamp(give * f.give, 0f, 0.95f));
            mat.SetFloat("_Limit", limit * f.swing);
            mat.SetFloat("_MassHz", hz * f.springBack);
            mat.SetFloat("_MassDamp", 2f * zeta * 2f * Mathf.PI * hz * f.springBack);
            mat.SetFloat("_Taut", f.taut);
            mat.SetFloat("_Deepest", Deepest * f.depth);
            mat.SetVector("_Slap", new Vector4(1.2f, 0.01f * f.slap, 0.03f * f.slap, 1.5f));
            EditorUtility.SetDirty(mat);
        }

        // The squash's own knobs that mean something to the tissue, live like the feel.
        internal static void Live(Material mat, YapsSquashSettings s, bool slide = false)
        {
            mat.SetFloat("_Hand", s != null ? s.handSize : 0.05f);
            // Solid: the homes take 0.7 of the whole's own squash at constant volume, so a squeeze
            // bulges it the other ways and it comes back when let go.
            mat.SetFloat("_Solid", 0.7f * (s != null ? s.volumeKept : 1f));
            // Squeeze: other players' palms flatten the whole at constant volume (Squeezed in the step).
            mat.SetFloat("_Squeeze", 0.6f);
            // Flattened about its own middle, a squeezed part's inner side pulls off the cleavage;
            // the wholes close in to 0.55 of their distance at rest (was 0.85), so a pair squeezed
            // from the outsides presses into each other instead of parting top and bottom.
            mat.SetFloat("_PairKeep", 0.55f);
            // A part alone has no partner to meet.
            mat.SetFloat("_Pair", (s == null || s.meet) && mat.GetFloat("_Regions") > 1.5f ? 1f : 0f);
            // The squash's Grip slides: 2 cm of drag, then the grip goes with the fist.
            mat.SetFloat("_GripSlip", slide || s != null && s.gripSlides ? 0.02f : 0f);
            // The stretch in motion, on a part a cloth swings: w 1, or x on from a build before w was kept.
            var motion = mat.GetVector("_Motion");
            bool swung = motion.w > 0f || motion.x > 0f;
            mat.SetVector("_Motion", swung && (s == null || s.stretchInMotion) ? YapsSquash.Stretch + new Vector4(0f, 0f, 0f, 1f) : new Vector4(0f, motion.y, motion.z, swung ? 1f : 0f));
            // Other players' hands, in or out: the tissue keeps a hand out of the skin, so a share has
            // nothing to scale.
            float hands = s == null || s.othersHands > 0f ? 1f : 0f;
            mat.SetVector("_JellyArrays", new Vector4(hands, hands, hands, hands));
            EditorUtility.SetDirty(mat);
        }

        // A worn part retuned from the component as its knobs move. A mass no cloth carries, a
        // probe's, keeps its own swing: ponytail, the live knobs know only the carried mass's values.
        internal static int Retune(Transform worn, YapsSquashSettings s)
        {
            string part = worn.name.Substring(Worn.Length + 1);
            var f = s.feels.FirstOrDefault(q => q.part == part) ?? new YapsSquashSettings.Feel();
            var mats = worn.GetComponentsInChildren<CVRMaterialDataProvider>(true).SelectMany(p => p.materials ?? new Material[0])
                .Where(m => m != null && m.HasProperty("_MassHz")).Distinct().ToList();
            foreach (var m in mats)
            {
                if (m.GetFloat("_MassCarried") > 0f) Tune(m, f, CarriedHz, CarriedZeta, CarriedGive, CarriedLimit);
                Live(m, s);
            }
            return mats.Count;
        }

        // Each pair's jelly named by its pair, so putting one on takes that one off alone.
        internal const string Worn = "Jelly_Spike";
        internal static string WornName(string pair) => $"{Worn} {pair}";
        internal const int Row = 64, Passes = 2, ProbesPer = 24;
        // The most probes a region takes, world balls among them, and where their readout starts on
        // its side row: columns of their own past the particles'.
        internal const int ProbesMax = 64, Readout = 2 * Row;


        // Any earlier one on the avatar goes, only that pair's when named; how many. An instance is
        // named after its pair, Jelly_Spike and its pair; older builds left Jelly_Spike or Jelly Spike.
        internal static int Unworn(Transform avatar, string only = null)
        {
            // The colliders that pressed instead, back in their pair's cloth: their ends mark them and
            // name the pair, the breasts when they name none, as before any other pair.
            var ends = avatar.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Jelly collider ") && t.parent != null)
                .Where(t => only == null || PartOf(only).IsMatch(t.name.Split(new[] { ' ' }, 5).ElementAtOrDefault(4) ?? "breast")).ToList();
            var skeleton = Skeleton(avatar.GetComponentInChildren<Animator>());
            foreach (var pair in ends.GroupBy(t => t.name.Split(new[] { ' ' }, 5).ElementAtOrDefault(4)))
            {
                // By the bones its jelly rides, as put on, else by its name.
                var worn = avatar.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => pair.Key != null && t.name == WornName(pair.Key));
                var carrier = worn != null ? worn.GetComponentsInChildren<CVRMaterialDataProvider>(true).FirstOrDefault(q => q.transforms != null && q.transforms.Length > 0)?.transforms[0] : null;
                var part = carrier != null && !carrier.IsChildOf(worn) ? PartFrom(carrier, skeleton) : null;
                PairCollide(avatar, part ?? (pair.Key != null ? PartOf(pair.Key) : Breast), pair.Select(t => t.parent).Distinct().SelectMany(t => t.GetComponents<Component>()).Where(IsCollider), with: true);
            }
            foreach (var end in ends) Undo.DestroyObjectImmediate(end.gameObject);
            var stale = avatar.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == Worn || t.name == "Jelly Spike" || (only == null ? t.name.StartsWith(Worn + " ") || t.name.StartsWith("Jelly Region") : t.name == WornName(only)))
                .Select(t => t.gameObject).ToList();
            // The cloth's balls back to their size, from the marks the jelly kept.
            foreach (var mark in stale.Where(o => o != null).SelectMany(o => o.GetComponentsInChildren<Transform>(true)).Where(t => t.name.StartsWith(RadiusMark)).ToList())
                Unshrink(avatar, mark.name);
            foreach (var old in stale)
                if (old != null) Undo.DestroyObjectImmediate(old);
            return stale.Count;
        }

        internal static readonly Regex Breast = new Regex("breast|boob", RegexOptions.IgnoreCase);
        internal static readonly Regex Other = new Regex("^(?!.*(breast|boob))", RegexOptions.IgnoreCase);

        // A pair's bones, cloth and colliders by the name the squash gives it.
        internal static Regex PartOf(string name) => new Regex(Regex.Escape(name), RegexOptions.IgnoreCase);
        internal const float BreastCells = 600f;
        // A part's cells by its box's volume at about CellAim apart, so a big part keeps fine cells:
        // at a fixed count a 41 cm box came out 4.6 cm a cell. Any box under about 4.8 litres stays at
        // BreastCells, as before. Capped, since every particle is a texel stepped each frame.
        internal const float CellAim = 0.02f, MostCells = 2400f;
        // For measuring: either used in place of its constant when above 0.
        internal static float CellAimOverride, MostCellsOverride;
        // A slap's ring as the draw spreads it: how much of the step's depth, speed (m/s),
        // wavelength (m), fade (s).
        internal static readonly Vector4 Slapped = new Vector4(1f, 1f, 0.1f, 0.4f);

        // A point off a box's corner in grid cells across and up, and in metres out.
        static Vector3 Cell(Vector3 p, Vector3 span, int grid) => new Vector3(p.x / span.x * grid, p.y / span.y * grid, p.z);

        internal static Transform AvatarOf(Transform picked)
        {
            var avatar = picked != null ? picked.GetComponentInParent<CVRAvatar>() : null;
            return avatar != null ? avatar.transform : picked;
        }

        // A lattice a breast, packed into one texture and one pair of passes: each swings on its root
        // as one piece and keeps off the other, two bodies. Each fitted to the skin the squash gives
        // that breast at least half of, a centimetre of room round it, its back face half a
        // centimetre into the chest wall; both the larger's size, cut by its volume (BreastCells to MostCells cells each), as
        // near cubes as the box allows. The body's squash materials gain the Jelly role, which moves
        // each vertex by the squash's own share of each breast, and the squash leaves the pair to it.
        // The cloth or dynamic bones swinging the pair are switched off, or they and the lattice
        // would both swing it.
        //
        // The first of the squash's parts whose name want matches, a pair or one side alone, hung
        // from the trunk bone its skin's bones hang from, in the slot its place among the parts gives
        // it (found, when given, the parts as YapsSquash.PartsOf reads them). Its assets in folder,
        // the nth part's in folder_n; the step shader in shaders. What was done, starting "On " when
        // it was.
        // How deep each region's skin lies under each face of its box, on a DepthGrid square over
        // the face: a hand is held by how far it is past the skin, not the face. A face touches the
        // skin at its outermost point only; where a poke came in on a seat it lay 8 cm off it, so the
        // whole of _Deepest went before the finger met the skin, and nothing dented. Each spot is the
        // outermost skin right over it, from the triangles: the outermost of a spot's whole cell, on a
        // round cheek, read 6 of those 8 cm short. Rows of a face each, x then y then z, plus then
        // minus, the first region's six faces then the second's.
        internal const int DepthGrid = 16;

        static Texture2D DepthMap(int sides, float[][] shares, Vector3[] at, SkinnedMeshRenderer body, Transform chest, Quaternion undo, Vector3[] los, Vector3[] his, string folder)
        {
            const int G = DepthGrid;
            var cells = Enumerable.Repeat(float.PositiveInfinity, 2 * 6 * G * G).ToArray();
            var binned = Enumerable.Repeat(float.PositiveInfinity, cells.Length).ToArray();
            var tris = body.sharedMesh.triangles;
            for (int side = 0; side < sides; side++)
            {
                Vector3 lo = los[side], hi = his[side];
                var half = 0.5f * (hi - lo);
                var origin = new Vector3(0.5f * (lo.x + hi.x), 0.5f * (lo.y + hi.y), lo.z);
                // On the region's anchor, from the middle of its box; null off the part.
                var on = new Vector3?[at.Length];
                for (int v = 0; v < at.Length && v < shares[side].Length; v++)
                    if (shares[side][v] >= 0.5f) on[v] = undo * (body.transform.position + body.transform.rotation * at[v] - chest.position) - origin - new Vector3(0f, 0f, half.z);
                // A face's spot coordinates and depth for a point on the anchor.
                Vector3 Spot(Vector3 l, int k, int s)
                {
                    int i = k == 0 ? 1 : 0, j = k == 2 ? 1 : 2;
                    return new Vector3((l[i] / half[i] + 1f) * 0.5f * (G - 1), (l[j] / half[j] + 1f) * 0.5f * (G - 1), half[k] - (s == 0 ? l[k] : -l[k]));
                }
                // Each vertex for its nearest spot, for a spot no triangle covers.
                for (int v = 0; v < on.Length; v++)
                    if (on[v] is Vector3 l)
                        for (int f = 0; f < 6; f++)
                        {
                            var q = Spot(l, f >> 1, f & 1);
                            int c = ((side * 6 + f) * G + Mathf.Clamp(Mathf.RoundToInt(q.y), 0, G - 1)) * G + Mathf.Clamp(Mathf.RoundToInt(q.x), 0, G - 1);
                            binned[c] = Mathf.Min(binned[c], Mathf.Max(q.z, 0f));
                        }
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    if (tris[t] >= on.Length || tris[t + 1] >= on.Length || tris[t + 2] >= on.Length) continue;
                    if (!(on[tris[t]] is Vector3 la) || !(on[tris[t + 1]] is Vector3 lb) || !(on[tris[t + 2]] is Vector3 lc)) continue;
                    for (int f = 0; f < 6; f++)
                    {
                        Vector3 A = Spot(la, f >> 1, f & 1), B = Spot(lb, f >> 1, f & 1), C = Spot(lc, f >> 1, f & 1);
                        float area = (B.x - A.x) * (C.y - A.y) - (C.x - A.x) * (B.y - A.y);
                        if (Mathf.Abs(area) < 1e-9f) continue;
                        int x0 = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(A.x, Mathf.Min(B.x, C.x)))), x1 = Mathf.Min(G - 1, Mathf.FloorToInt(Mathf.Max(A.x, Mathf.Max(B.x, C.x))));
                        int y0 = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(A.y, Mathf.Min(B.y, C.y)))), y1 = Mathf.Min(G - 1, Mathf.FloorToInt(Mathf.Max(A.y, Mathf.Max(B.y, C.y))));
                        for (int y = y0; y <= y1; y++)
                            for (int x = x0; x <= x1; x++)
                            {
                                float u = ((B.x - x) * (C.y - y) - (C.x - x) * (B.y - y)) / area, w = ((C.x - x) * (A.y - y) - (A.x - x) * (C.y - y)) / area;
                                if (u < -1e-4f || w < -1e-4f || u + w > 1f + 1e-4f) continue;
                                int c = ((side * 6 + f) * G + y) * G + x;
                                cells[c] = Mathf.Min(cells[c], Mathf.Max(u * A.z + w * B.z + (1f - u - w) * C.z, 0f));
                            }
                    }
                }
            }
            for (int c = 0; c < cells.Length; c++)
                if (float.IsPositiveInfinity(cells[c])) cells[c] = binned[c];
            // A spot of a face with no skin under it takes its shallowest neighbour's: held a little
            // early at a rim, never let through to tear it.
            for (int f = 0; f < 12; f++)
                for (int pass = 0; pass < 2 * G; pass++)
                {
                    bool empty = false;
                    var was = (float[]) cells.Clone();
                    for (int y = 0; y < G; y++)
                        for (int x = 0; x < G; x++)
                        {
                            int c = (f * G + y) * G + x;
                            if (!float.IsPositiveInfinity(was[c])) continue;
                            float near = float.PositiveInfinity;
                            if (x > 0) near = Mathf.Min(near, was[c - 1]);
                            if (x < G - 1) near = Mathf.Min(near, was[c + 1]);
                            if (y > 0) near = Mathf.Min(near, was[c - G]);
                            if (y < G - 1) near = Mathf.Min(near, was[c + G]);
                            cells[c] = near;
                            empty |= float.IsPositiveInfinity(near);
                        }
                    if (!empty) break;
                }
            var pixels = cells.Select(d => new Color(float.IsPositiveInfinity(d) ? 0f : d, 0f, 0f, 0f)).ToArray();
            // Kept in place when it exists: remade at the same path, the materials would read it Missing.
            string path = folder + "/Jelly_Depth.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            // Flagged as the CCK's upload checks ask: left Read/Write enabled, it failed validation
            // and blocked the upload. Readable again while it is rewritten. Never streamed, unlike
            // the squash's: the step's materials are on no mesh, where the CCK warns of streaming.
            void Flags(bool readable)
            {
                var flags = new SerializedObject(tex);
                flags.FindProperty("m_IsReadable").boolValue = readable;
                flags.FindProperty("m_StreamingMipmaps").boolValue = false;
                flags.ApplyModifiedPropertiesWithoutUndo();
            }
            if (tex == null)
            {
                tex = new Texture2D(G, 12 * G, TextureFormat.RFloat, false, true);
                AssetDatabase.CreateAsset(tex, path);
            }
            else
            {
                Flags(true);
                tex.Reinitialize(G, 12 * G, TextureFormat.RFloat, false);
            }
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels(pixels);
            tex.Apply(false);
            Flags(false);
            EditorUtility.SetDirty(tex);
            return tex;
        }

        internal static string PairOn(Transform picked, Regex want, string folder, string shaders, Options o, BridgeReport report, YapsSquash.SoftParts found = null)
        {
            var root = AvatarOf(picked);
            var animator = root != null ? root.GetComponentInChildren<Animator>() : null;
            if (animator == null || !animator.isHuman) return root == null ? "Select a humanoid avatar in the scene first." : $"{root.name} is not humanoid, so the jelly has no trunk to hang from.";
            var mats = YapsSquash.Materials(root);
            if (mats.Count == 0) return $"{root.name} has no YAPS squash built. The jelly moves each vertex by the squash's own part shares, so build the squash first.";
            // Painted regions first, as the squash takes them; else the pairs found by name or picked.
            string none = null;
            if (found == null) found = YapsSquash.PartsOf(root, out none);
            var names = found?.parts.Select(x => x.name).ToList();
            int pair = names != null ? names.FindIndex(want.IsMatch) : -1;
            if (pair < 0) return $"No such part in {root.name}'s squash: {(found == null ? none : "its parts are " + string.Join(", ", names))}.";
            var dented = YapsSquash.SettingsOf(root)?.dented ?? new List<string>();
            if (pair >= YapsSquash.JellySlots)
                return $"{names[pair]} is past the {YapsSquash.JellySlots} parts the jelly takes on one avatar, so it {(dented.Contains(names[pair]) ? "keeps the squash" : "stays as it is")}.";
            var body = found.body;
            // Each side's share of every vertex of the body, one side for a part alone.
            int sides = found.parts[pair].sides;
            var shares = Enumerable.Range(0, sides).Select(s => found.shares[body][2 * pair + s]).ToArray();
            string into = folder + (pair > 0 ? "_" + (pair + 1) : "");
            var skeleton = Skeleton(animator);
            var trunk = new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest }.Select(animator.GetBoneTransform).Where(t => t != null).ToList();
            var chest = Carrier(body, shares[0], root, skeleton);
            // The pair's cloth and colliders found by the bones carrying its skin: a painted pair's
            // name is the painter's ("Chest"), which names no bone.
            var part = PartFrom(chest, skeleton) ?? PartOf(names[pair]);
            while (chest != null && !trunk.Contains(chest)) chest = chest.parent;
            if (chest == null) return $"{root.name}'s {names[pair]} hangs from no bone of the trunk.";

            var baked = new Mesh();
            body.BakeMesh(baked);
            var at = baked.vertices;
            Object.DestroyImmediate(baked);
            var pairSkin = Enumerable.Range(0, Mathf.Min(at.Length, shares[0].Length))
                .Where(v => shares.Any(s => s[v] >= 0.5f))
                .Select(v => body.transform.position + body.transform.rotation * at[v]).ToArray();
            // A pair behind its bone, as a seat, faces back: the lattice's front is always out of the body.
            bool back = pairSkin.Length > 0 && Vector3.Dot(pairSkin.Aggregate(Vector3.zero, (sum, v) => sum + v) / pairSkin.Length - chest.position, root.forward) < 0f;
            var turn = back ? root.rotation * Quaternion.Euler(0f, 180f, 0f) : root.rotation;
            var undo = Quaternion.Inverse(turn);
            var los = new Vector3[sides];
            var his = new Vector3[sides];
            for (int side = 0; side < sides; side++)
            {
                var skin = new List<Vector3>();
                for (int v = 0; v < at.Length && v < shares[side].Length; v++)
                    if (shares[side][v] >= 0.5f)
                        skin.Add(undo * (body.transform.position + body.transform.rotation * at[v] - chest.position));
                if (skin.Count < 20) return $"Only {skin.Count} vertices of {body.name} belong to {(sides == 2 ? "one side of " : "")}{names[pair]}; too few to fit a lattice to.";
                Vector3 lo = skin[0], hi = skin[0];
                foreach (var x in skin)
                {
                    lo = Vector3.Min(lo, x);
                    hi = Vector3.Max(hi, x);
                }
                los[side] = lo - new Vector3(0.01f, 0.01f, 0.005f);
                his[side] = hi + new Vector3(0.01f, 0.01f, 0.01f);
            }
            var size = his[0] - los[0];
            for (int side = 1; side < sides; side++) size = Vector3.Max(size, his[side] - los[side]);
            float aim = CellAimOverride > 0f ? CellAimOverride : CellAim;
            float cap = Mathf.Max(MostCellsOverride > 0f ? MostCellsOverride : MostCells, o.cells);
            float cells = Mathf.Clamp(size.x * size.y * size.z / (aim * aim * aim), o.cells, cap);
            float cell = Mathf.Pow(size.x * size.y * size.z / cells, 1f / 3f);
            // Each breast's volume, blend shapes as set: from the plane of its base ring, where its own
            // skin ends on the body, out to its frontmost skin, over a grid of a hundredth of the box.
            // Its skin can be a shell, its inner faces turned in under its outer ones (a signed sum
            // over one avatar left a centimetre-thick shell, 1 litre), so only the frontmost surface over
            // each spot counts; from the chest bone's plane it took in the ribs (35 litres).
            var tris = body.sharedMesh.triangles;
            var volume = new float[sides];
            for (int side = 0; side < sides; side++)
            {
                var share = shares[side];
                var lo = los[side];
                var span = his[side] - lo;
                const int Grid = 100;
                var front = new float[Grid * Grid];
                // The ring: edges only one of the part's triangles has.
                var edges = new Dictionary<(int, int), int>();
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    if (a >= share.Length || b >= share.Length || c >= share.Length || share[a] < 0.5f || share[b] < 0.5f || share[c] < 0.5f) continue;
                    foreach (var (e0, e1) in new[] { (a, b), (b, c), (c, a) })
                    {
                        var key = e0 < e1 ? (e0, e1) : (e1, e0);
                        edges[key] = edges.TryGetValue(key, out int seen) ? seen + 1 : 1;
                    }
                }
                var ring = edges.Where(e => e.Value == 1).SelectMany(e => new[] { e.Key.Item1, e.Key.Item2 }).Distinct().ToList();
                float baseZ = ring.Count > 0 ? ring.Average(v => (undo * (body.transform.position + body.transform.rotation * at[v] - chest.position)).z) - lo.z : 0f;
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    if (a >= share.Length || b >= share.Length || c >= share.Length || share[a] < 0.5f || share[b] < 0.5f || share[c] < 0.5f) continue;
                    var A = Cell(undo * (body.transform.position + body.transform.rotation * at[a] - chest.position) - lo, span, Grid);
                    var B = Cell(undo * (body.transform.position + body.transform.rotation * at[b] - chest.position) - lo, span, Grid);
                    var C = Cell(undo * (body.transform.position + body.transform.rotation * at[c] - chest.position) - lo, span, Grid);
                    float area = (B.x - A.x) * (C.y - A.y) - (C.x - A.x) * (B.y - A.y);
                    if (Mathf.Abs(area) < 1e-9f) continue;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.x, Mathf.Min(B.x, C.x)))), x1 = Mathf.Min(Grid - 1, Mathf.CeilToInt(Mathf.Max(A.x, Mathf.Max(B.x, C.x))));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.y, Mathf.Min(B.y, C.y)))), y1 = Mathf.Min(Grid - 1, Mathf.CeilToInt(Mathf.Max(A.y, Mathf.Max(B.y, C.y))));
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            float px = x + 0.5f, py = y + 0.5f;
                            float u = ((B.x - px) * (C.y - py) - (C.x - px) * (B.y - py)) / area, v = ((C.x - px) * (A.y - py) - (A.x - px) * (C.y - py)) / area;
                            if (u < 0f || v < 0f || u + v > 1f) continue;
                            front[y * Grid + x] = Mathf.Max(front[y * Grid + x], u * A.z + v * B.z + (1f - u - v) * C.z);
                        }
                }
                volume[side] = front.Sum(z => Mathf.Max(z - baseZ, 0f)) * (span.x / Grid) * (span.y / Grid);
            }
            float kilos = 950f * volume.Average();
            // The same tissue at another size: about 2 Hz at half a kilogram, going as one over the
            // size, the cube root of the mass; damped at a quarter of critical; the heavier, the less
            // of a hand's push moves the whole. ponytail: one reference breast, not measured flesh;
            // the inspector's knobs take it from there.
            float hz = 2f * Mathf.Pow(0.5f / Mathf.Max(kilos, 1e-3f), 1f / 3f);
            // For now the pair's own cloth swings the breast bones and each region rides its bone, the
            // jelly the flesh on them: dents, the pair's squash, a viscous fill-back. Its own mass
            // is then the whole's give to a hand alone, carried on the bone: stiff and fully damped, so
            // a press moves the breast as one piece and it settles back, with no swing of its own on top
            // of the cloth's, as the squash moves it. The weight is the cloth's: left free at the breast's own 0.75 Hz, the mass trailed
            // every hop and drew the breast out like taffy. Options.mass puts the swinging mass back in
            // the cloth's place, the cloth off; noMass leaves the lattices alone, for telling the
            // mass's doing from theirs.
            bool cloth = !o.mass;
            if (cloth) hz = CarriedHz;
            if (o.noMass) hz = 0f;
            // How much of a held press moves the whole: a tenth, so a hand dents the flesh and the
            // body's own moves swing the mass, as the soft body does. From the tissue's stiffness
            // under a palm against the spring the breast hangs on, it was 0.4 to 0.56, and every
            // press shoved the whole breast: "far too volatile".
            // Carried on the cloth, CarriedGive.
            float give = cloth ? CarriedGive : 0.1f;
            // The whole's damping, 0.6 of critical: heavy flesh settles in about a swing, where at a
            // quarter one hit swung it back and forth for seconds. The options set
            // either, for tuning.
            if (!float.IsNaN(o.give)) give = o.give;
            float zeta = !float.IsNaN(o.zeta) ? o.zeta : cloth ? CarriedZeta : 0.6f;
            var n = new Vector3Int(Mathf.Max(3, Mathf.CeilToInt(size.x / cell) + 1), Mathf.Max(3, Mathf.CeilToInt(size.y / cell) + 1), Mathf.Max(3, Mathf.CeilToInt(size.z / cell) + 1));
            float spacing = Mathf.Max(size.x / (n.x - 1), size.y / (n.y - 1), size.z / (n.z - 1));

            var core = Core(into, shaders, n, spacing, regions: sides, sleeps: false, withRider: false, offFar: true, lift: 1f);
            if (core == null) return "The jelly did not build; the console says why.";
            float radius = (size.x + size.y + size.z) / 6f - 0.01f;
            // How far each breast's own skin reaches toward the other off its centre: where the two
            // actually meet, for the draw's last guard. The box's half width sits past the skin, 7 cm
            // into the other's box on one avatar.
            var skinReach = new float[2];
            var across = sides == 2 ? new Vector3(0.5f * (los[1].x + his[1].x - los[0].x - his[0].x), 0.5f * (los[1].y + his[1].y - los[0].y - his[0].y), los[1].z - los[0].z).normalized : Vector3.zero;
            for (int side = 0; side < sides && sides == 2; side++)
            {
                var c = new Vector3(0.5f * (los[side].x + his[side].x), 0.5f * (los[side].y + his[side].y), los[side].z + 0.5f * (n.z - 1) * spacing);
                var way = side == 0 ? across : -across;
                for (int v = 0; v < Mathf.Min(at.Length, shares[side].Length); v++)
                    if (shares[side][v] >= 0.5f)
                        skinReach[side] = Mathf.Max(skinReach[side], Vector3.Dot(undo * (body.transform.position + body.transform.rotation * at[v] - chest.position) - c, way));
            }
            // The squash's stretch in motion, as the conversion set it; none for a pair no cloth of its
            // own swings, as thighs: the bone it rides is the limb, and a seated thigh would read as
            // swung a quarter turn off where it hangs.
            var motion = mats[0].GetVector("_YAPS_SquashMotion");
            bool clothed = PairCloth(root, part).Count > 0;
            if (!clothed) motion.x = 0f;
            // w marks a part a cloth swings, for Live to turn the stretch back on.
            motion.w = clothed ? 1f : 0f;
            var depthMap = DepthMap(sides, shares, at, body, chest, undo, los, his, into);
            // The third pass too, which judges the same reach to wake a rest.
            foreach (var mat in core.provider.materials)
            {
                // The rates, give and limit with the part's feel, once the bone it rides is known.
                mat.SetFloat("_MassCarried", cloth ? 1f : 0f);
                mat.SetFloat("_MassRadius", radius);
                var apart = sides == 2 ? new Vector3(0.5f * (los[1].x + his[1].x - los[0].x - his[0].x), 0.5f * (los[1].y + his[1].y - los[0].y - his[0].y), los[1].z - los[0].z) : Vector3.zero;
                mat.SetVector("_PairOffset", apart);
                // Each one's half width across, and any gap between them as built: where the plane
                // they meet at starts to press.
                float wide0 = 0.5f * (his[0].x - los[0].x), wide1 = sides == 2 ? 0.5f * (his[1].x - los[1].x) : 0f;
                // Without the plane, for telling its doing from the rest's.
                bool plane = o.plane && sides == 2;
                mat.SetVector("_PairReach", plane ? new Vector4(wide0, wide1, Mathf.Max(apart.magnitude - wide0 - wide1, 0f), 0f) : Vector4.zero);
                mat.SetVector("_PairSkin", plane ? new Vector4(skinReach[0], skinReach[1], 0f, 0f) : Vector4.zero);
                // Each breast's skin on its region's frame, what the own capsules and hands are kept
                // in reach of: the lattice is the larger's box, past the smaller's skin.
                for (int side = 0; side < sides; side++)
                    mat.SetVector($"_JellyBox{side}", new Vector4(0.5f * (his[side].x - los[side].x), 0.5f * (his[side].y - los[side].y), his[side].z - los[side].z, 1f));
                mat.SetTexture("_JellyDepth", depthMap);
                mat.SetVector("_JellyOffsetAt0", Vector4.zero);
                mat.SetVector("_JellyOffsetAt1", Vector4.zero);
                mat.SetFloat("_JellySkinStride", 0f);
                // Flesh barely changes volume but gives easily in shear, so nearly all of each
                // neighbourhood's own squash is kept: half kept, it sprang back like rubber.
                mat.SetFloat("_Squish", 0.9f);
                mat.SetVector("_Motion", motion);
                EditorUtility.SetDirty(mat);
            }
            var restored = Restore(root, names[pair]);
            int stale = Unworn(root, names[pair]);
            var made = core.root;
            made.name = WornName(names[pair]);
            // A part alone gets a region of its own too, placed as a pair's are: the core's single
            // region is its root.
            if (sides == 1)
            {
                var lone = new GameObject("Jelly Region 0").transform;
                lone.SetParent(made.transform, false);
                core.provider.transforms = new[] { lone };
            }
            made.transform.SetParent(chest, false);
            made.transform.SetPositionAndRotation(chest.position, turn);
            made.transform.localScale = Vector3.one / Mathf.Max(chest.lossyScale.x, 1e-6f);
            for (int side = 0; side < sides; side++)
                made.transform.Find($"Jelly Region {side}").SetPositionAndRotation(chest.position + turn * new Vector3(0.5f * (los[side].x + his[side].x), 0.5f * (los[side].y + his[side].y), los[side].z), turn);
            Undo.RegisterCreatedObjectUndo(made, "Put the breasts on the jelly");
            // Each region rides the bone that carries most of its breast's skin, which the cloth
            // swings: the anchor handed the bone, the region's place on it in the step's materials.
            // The region stays out of the bone's tree, where the cloth would take it for a bone of
            // its own and bend the chain toward it.
            var carriers = new string[sides];
            var carrierBones = new Transform[sides];
            int probes = 0;
            string probed = "";
            Transform rides = chest;
            if (cloth)
            {
                var provider = made.GetComponentInChildren<CVRMaterialDataProvider>(true);
                Undo.RecordObject(provider, "Put the breasts on the jelly");
                for (int side = 0; side < sides; side++)
                {
                    var bone = Carrier(body, shares[side], chest, skeleton, clothed);
                    if (bone == null) continue;
                    var region = made.transform.Find($"Jelly Region {side}");
                    var placed = bone.InverseTransformPoint(region.position);
                    var turnOn = Quaternion.Inverse(bone.rotation) * region.rotation;
                    float sized = region.lossyScale.x / Mathf.Max(bone.lossyScale.x, 1e-9f);
                    var carried = bone.localToWorldMatrix * Matrix4x4.TRS(placed, turnOn, Vector3.one * sized);
                    float drift = 0f;
                    for (int e = 0; e < 16; e++) drift = Mathf.Max(drift, Mathf.Abs(carried[e] - region.localToWorldMatrix[e]));
                    if (drift > 1e-3f) Debug.LogWarning($"[Jelly] {bone.name} is scaled unevenly: the region's place on it is {drift:0.0000} off");
                    foreach (var mat in provider.materials)
                    {
                        mat.SetVector($"_JellyOffsetAt{side}", new Vector4(placed.x, placed.y, placed.z, sized));
                        mat.SetVector($"_JellyOffsetTurn{side}", new Vector4(turnOn.x, turnOn.y, turnOn.z, turnOn.w));
                        EditorUtility.SetDirty(mat);
                    }
                    provider.transforms[side] = bone;
                    carriers[side] = bone.name;
                    carrierBones[side] = bone;
                    if (side == 0) rides = bone;
                }
                // And each region held still where it hangs on the chest, for how far the cloth has
                // swung it: the step stretches it by that, as the squash does.
                var hangs = new Transform[sides];
                for (int side = 0; side < sides; side++)
                {
                    var region = made.transform.Find($"Jelly Region {side}");
                    hangs[side] = new GameObject($"Jelly Hang {side}").transform;
                    hangs[side].SetParent(region.parent, false);
                    hangs[side].localPosition = region.localPosition;
                    hangs[side].localRotation = region.localRotation;
                    hangs[side].localScale = region.localScale;
                    Undo.RegisterCreatedObjectUndo(hangs[side].gameObject, "Put the breasts on the jelly");
                }
                // A part alone fills the second region's slots with its own, as the helper reads both.
                provider.transforms = new[] { provider.transforms[0], provider.transforms[sides - 1], hangs[0], hangs[sides - 1] };
                // Probes: MagicaCloth's colliders, other players' among them, as presses. The
                // client adds every other player's colliders to every MagicaCloth an avatar carries
                // (CVRDynamicBoneManager.UpdateComponents), hands only unless the viewer chose more,
                // and a hand without one is given a capsule sized from its fingers. So a spring cloth
                // of loose points on each breast's skin at rest is pushed by them, and where a point
                // was pushed to says where a collider is. On the chest, not under the breast's bone:
                // there the breast's own cloth would take each for a bone of its chain.
                // MagicaCloth only: the same hand gets a DynamicBone collider too, so DynamicBone probes
                // saw nothing more, and dented a quarter less for the same press.
                // Feeling the world: twelve physics balls besides, spread over the front, which a wall or
                // a pane meets first. Spread over all the skin, farthest first, they sat on its rim.
                bool world = o.world, probing = o.probes || world;
                var spotsOf = new List<Vector3>[sides];
                float reach = o.probeReach;
                if (probing)
                {
                    // Over all the skin a hand reaches, all but where it meets the body, spread farthest
                    // first from its frontmost, every side as many, until none of it is further than a
                    // probe's reach from one or the room runs out. A collider anywhere on it is then in
                    // some probe's reach: on the front alone, twelve a breast left 11 cm between them,
                    // and a finger's collider, or a 3 cm ball, pressed 4 cm into a gap was never felt.
                    // ponytail: the room's 52 leave a 6 cm gap on a breast 38 cm across (117 for 4 cm),
                    // so the reach grows to the gap; past it a press sits nearer one probe than the next.
                    int room = ProbesMax - (world ? ProbesPer / 2 : 0);
                    var picks = new List<Vector3>[sides];
                    var balls = new List<Vector3>[sides];
                    var gaps = new List<float>[sides];
                    // Farthest first from the frontmost of from, up to count, the largest gap left after each.
                    List<Vector3> Spread(List<Vector3> from, Transform region, int count, List<float> left)
                    {
                        int first = Enumerable.Range(0, from.Count).OrderByDescending(i => region.InverseTransformPoint(from[i]).z).First();
                        var got = new List<Vector3> { from[first] };
                        var near = from.Select(w => (w - from[first]).magnitude).ToArray();
                        while (got.Count < Mathf.Min(count, from.Count))
                        {
                            int far = 0;
                            for (int i = 1; i < near.Length; i++) if (near[i] > near[far]) far = i;
                            left?.Add(near[far]);
                            got.Add(from[far]);
                            for (int i = 0; i < near.Length; i++) near[i] = Mathf.Min(near[i], (from[i] - from[far]).magnitude);
                        }
                        left?.Add(near.Max());
                        return got;
                    }
                    for (int side = 0; side < sides; side++)
                    {
                        var region = made.transform.Find($"Jelly Region {side}");
                        var skin = Enumerable.Range(0, Mathf.Min(at.Length, shares[side].Length)).Where(v => shares[side][v] >= 0.5f)
                            .Select(v => body.transform.position + body.transform.rotation * at[v]).ToList();
                        float deepest = skin.Max(w => region.InverseTransformPoint(w).z);
                        gaps[side] = new List<float>();
                        picks[side] = Spread(skin.Where(w => region.InverseTransformPoint(w).z >= 0.15f * deepest).ToList(), region, room, gaps[side]);
                        if (world) balls[side] = Spread(skin.Where(w => region.InverseTransformPoint(w).z >= 0.5f * deepest).ToList(), region, ProbesPer / 2, null);
                    }
                    // The gap n probes leave on a side is gaps[side][n - 1].
                    int most = picks.Min(q => q.Count);
                    probes = Enumerable.Range(1, most).FirstOrDefault(c => gaps.All(g => g[c - 1] <= o.probeReach));
                    if (probes == 0) probes = most;
                    reach = Mathf.Max(o.probeReach, gaps.Max(g => g[probes - 1]));
#if AVATARBRIDGE_MAGICA
                    // Without MagicaCloth AddSkin makes no probes, so nothing to report.
                    probed = $"MagicaCloth colliders felt through {probes} probes{(sides == 2 ? " each" : "")}, none of the skin further than {100f * reach:0.0} cm from one; ";
#endif
                    for (int side = 0; side < sides; side++)
                    {
                        spotsOf[side] = picks[side].Take(probes).ToList();
                        // The balls' places after the cloth's, which AddSkin reads as spots from the first.
                        if (world) spotsOf[side].AddRange(balls[side]);
                    }
                }
                AddSkin(made, provider, hangs, probing ? spotsOf : null, 0.5f * (n.z - 1) * spacing, "Put the breasts on the jelly", into, reach, world ? new[] { true, true } : null, probes);
                if (probing && YapsSquash.SettingsOf(root)?.probeMarkers == true)
                    Markers(made, core.state, n, Mathf.RoundToInt(provider.materials[0].GetFloat("_JellyProbes")), into, shaders);
            }

            // The wearer's own arms, hands and thighs press the breasts as other players' hands do:
            // eight capsules bone to bone, the bones after the four anchors in the provider's
            // transforms, each as thick as the body's skin round it (the median distance of the skin
            // its bones carry). Other players' hands come from the client's arrays; the wearer's arm
            // went through the breast in the film, as the arrays hold hands alone.
            var ownBones = new[]
            {
                HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleDistal, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.RightMiddleDistal, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
            }.Select(b => animator.GetBoneTransform(b)).ToArray();
            var ownRadii = new float[8];
            var weights = body.sharedMesh.boneWeights;
            for (int c = 0; c < 8; c++)
            {
                int s0 = (c >> 2) * 6 + ((c & 3) < 3 ? (c & 3) : 4);
                Transform from = ownBones[s0], to = ownBones[s0 + 1];
                if (from == null || to == null) continue;
                // The skin whose strongest bone hangs from this one and not from the next (twist bones
                // and, for the hand, its fingers), round the middle of the bone alone: taken to the
                // ends it took in the shoulder and the hip, an upper arm 98 mm thick, a thigh 184.
                var near = new List<float>();
                for (int v = 0; v < Mathf.Min(at.Length, weights.Length); v++)
                {
                    var bone = body.bones[weights[v].boneIndex0];
                    if (bone == null || !bone.IsChildOf(from) || bone.IsChildOf(to)) continue;
                    var w = body.transform.position + body.transform.rotation * at[v];
                    var seg = to.position - from.position;
                    float along = Vector3.Dot(w - from.position, seg) / Mathf.Max(seg.sqrMagnitude, 1e-9f);
                    if (along > 0.2f && along < 0.8f) near.Add(Vector3.Distance(w, from.position + seg * along));
                }
                // ponytail: a low share, as a capsule is round and a limb is not: the median left one avatar's
                // hand 56 mm thick and a thigh 236, pushing skin before they reached it. A limb a
                // little thin sinks in a touch first, which reads as soft.
                if (near.Count >= 8) ownRadii[c] = near.OrderBy(d => d).ElementAt(near.Count * 3 / 10);
            }
            // None reaching a particle of the pair's lattices as built, on the side it pushes them from:
            // there it would hold the tissue dented for good, as the thighs held a seat 18.7 mm in. Each
            // is thinned to clear them by half a centimetre, so it presses only past where it rests.
            var thinned = new List<string>();
            // Except the limb a region rides, which lies through it and moves with it: left out of
            // that region's step, plus one a region. Thinned to clear its own lattice, a thigh met the
            // other leg 27 mm wide.
            var rideOn = new int[2];
            var ridden = new List<string>();
            for (int side = 0; side < sides; side++)
                for (int c = 0; c < 8 && carrierBones[side] != null; c++)
                {
                    int s0 = (c >> 2) * 6 + ((c & 3) < 3 ? (c & 3) : 4);
                    if (ownBones[s0] == null || ownBones[s0 + 1] == null || !carrierBones[side].IsChildOf(ownBones[s0]) || carrierBones[side].IsChildOf(ownBones[s0 + 1])) continue;
                    rideOn[side] = c + 1;
                    ridden.Add(ownBones[s0].name);
                }
            // A region whose ridden bone runs through it, as a thigh's, holds a core round that bone
            // instead of its back face: the flesh rides its bone all round, and a held back face kept
            // the back of the thigh rigid however it was pressed. Four tenths of the limb's own
            // radius, at least enough that every layer along the bone holds a particle.
            var cores = new Vector4[4];
            var cored = new List<string>();
            for (int side = 0; side < sides; side++)
            {
                if (rideOn[side] == 0 || !(ownRadii[rideOn[side] - 1] > 0f)) continue;
                int c = rideOn[side] - 1, s0 = (c >> 2) * 6 + ((c & 3) < 3 ? (c & 3) : 4);
                var region = made.transform.Find($"Jelly Region {side}");
                Vector3 a = region.InverseTransformPoint(ownBones[s0].position), b = region.InverseTransformPoint(ownBones[s0 + 1].position);
                float zmax = (n.z - 1) * spacing, halfX = 0.5f * (n.x - 1) * spacing;
                var mid = new Vector3(0f, 0f, 0.5f * zmax);
                var foot = a + (b - a) * Mathf.Clamp01(Vector3.Dot(mid - a, b - a) / Mathf.Max((b - a).sqrMagnitude, 1e-12f));
                if (foot.z < 0.25f * zmax || foot.z > zmax || Mathf.Abs(foot.x) > halfX) continue;
                float rc = Mathf.Max(0.4f * ownRadii[c], 0.75f * spacing);
                cores[2 * side] = new Vector4(a.x, a.y, a.z, rc);
                cores[2 * side + 1] = new Vector4(b.x, b.y, b.z, 0f);
                int held = 0;
                for (int x = 0; x < n.x; x++)
                    for (int y = 0; y < n.y; y++)
                        for (int z = 0; z < n.z; z++)
                        {
                            var r = new Vector3((x - 0.5f * (n.x - 1)) * spacing, (y - 0.5f * (n.y - 1)) * spacing, z * spacing);
                            if (Vector3.Distance(r, a + (b - a) * Mathf.Clamp01(Vector3.Dot(r - a, b - a) / Mathf.Max((b - a).sqrMagnitude, 1e-12f))) < rc) held++;
                        }
                cored.Add($"{held} particles within {1000f * rc:0} mm of {ownBones[s0].name}");
            }
            for (int c = 0; c < 8; c++)
            {
                int s0 = (c >> 2) * 6 + ((c & 3) < 3 ? (c & 3) : 4);
                if (!(ownRadii[c] > 0f)) continue;
                Vector3 a = ownBones[s0].position, s = ownBones[s0 + 1].position - a;
                float was = ownRadii[c];
                for (int side = 0; side < sides; side++)
                {
                    if (rideOn[side] == c + 1) continue;
                    var region = made.transform.Find($"Jelly Region {side}");
                    var centre = region.TransformPoint(0f, 0f, 0.5f * (n.z - 1) * spacing);
                    for (int x = 0; x < n.x; x++)
                        for (int y = 0; y < n.y; y++)
                            for (int z = 0; z < n.z; z++)
                            {
                                var p = region.TransformPoint(new Vector3(x - 0.5f * (n.x - 1), y - 0.5f * (n.y - 1), z) * spacing);
                                var foot = a + s * Mathf.Clamp01(Vector3.Dot(p - a, s) / Mathf.Max(s.sqrMagnitude, 1e-12f));
                                if (Vector3.Dot(p - foot, centre - (a + 0.5f * s)) >= 0f) ownRadii[c] = Mathf.Min(ownRadii[c], Mathf.Max((p - foot).magnitude - 0.005f, 0f));
                            }
                }
                if (ownRadii[c] < was) thinned.Add($"{ownBones[s0].name} {1000f * was:0} to {1000f * ownRadii[c]:0} mm");
            }
            // The avatar's own MagicaCloth and DynamicBone colliders that its breasts' cloth collides
            // with press the breasts as its limbs do, and come out of that cloth's lists, so they dent
            // the skin and no longer move the bones: they deform the mesh. Each
            // is handed as two ends, a child at either end of its capsule, so it rides its bone as the
            // limbs do. Not one within a cell of the breasts' skin as built: it moves the lattice
            // round that skin at rest and would dent it for good (a sphere in the cleavage left the
            // tissue 41 mm off its rest), so it stays in the cloth, moving the bones as before.
            var pressing = new List<(Component c, Vector3 a, Vector3 b, float r)>();
            // The squash's Twitch: a transform to kick the mass, which takes the last
            // collider's anchors.
            var squashed = YapsSquash.SettingsOf(root);
            bool twitching = o.twitch || squashed != null && squashed.twitch;
            var touching = new List<string>();
            foreach (var c in PairColliders(root, part).OrderBy(c => (c.transform.position - chest.position).sqrMagnitude))
            {
                if (!Capsule(c, out var ca, out var cb, out float cr)) continue;
                if (pairSkin.Any(p => Seg(p, ca, cb) < cr + spacing)) touching.Add(c.name);
                // ponytail: eight, the nearest the chest first; more need anchors past the 32 handed.
                else if (pressing.Count < (twitching ? 7 : 8)) pressing.Add((c, ca, cb, cr));
            }
            PairCollide(root, part, pressing.Select(p => p.c), with: false);
            // And every other collider, other players' among them: the client adds theirs to every
            // cloth at runtime, past any list, and the cloth's balls, the source's size, moved the
            // bones under each hand the tissue was denting. One touch answered twice: a breast shoved
            // down and held, a squeeze shaking it. Shrunk, the tissue takes every touch and the cloth
            // only swings.
            var shrunk = PairShrink(root, part, made.transform);
            var colRadii = new float[8];
            var colEnds = new List<Transform>();
            for (int i = 0; i < pressing.Count; i++)
            {
                foreach (var (end, place) in new[] { ("a", pressing[i].a), ("b", pressing[i].b) })
                {
                    var t = new GameObject($"Jelly collider {i} {end} {names[pair]}").transform;
                    Undo.RegisterCreatedObjectUndo(t.gameObject, "Put the breasts on the jelly");
                    t.SetParent(pressing[i].c.transform, false);
                    t.position = place;
                    colEnds.Add(t);
                }
                colRadii[i] = pressing[i].r;
            }
            string collided = (pressing.Count > 0 ? $"{pressing.Count} of its colliders pressing instead of moving the bones ({string.Join(", ", pressing.Select(p => p.c.name))})" : "none of its colliders pressing") +
                              (touching.Count > 0 ? $", {touching.Count} left in the cloth as touching the breasts at rest ({string.Join(", ", touching)})" : "") +
                              (shrunk.Count > 0 ? $", the cloth's balls shrunk to {1000f * Shrunk:0} mm so the tissue takes every touch ({string.Join(", ", shrunk)})" : "");
            var anchors = made.GetComponentsInChildren<CVRMaterialDataProvider>(true).First(q => q.transforms != null && q.transforms.Length > 0);
            Undo.RecordObject(anchors, "Put the breasts on the jelly");
            var handed = anchors.transforms.ToList();
            // Without the cloth the hang slots are never read; the regions fill them.
            while (handed.Count < 4) handed.Add(handed[Mathf.Max(handed.Count - 2, 0)]);
            handed.AddRange(ownBones.Select(b => b != null ? b : chest));
            handed.AddRange(colEnds);
            Transform twitch = null;
            if (twitching)
            {
                twitch = new GameObject("Jelly Twitch").transform;
                Undo.RegisterCreatedObjectUndo(twitch.gameObject, "Put the breasts on the jelly");
                twitch.SetParent(handed[2], false);
                handed.Add(twitch);
            }
            anchors.transforms = handed.ToArray();
            foreach (var mat in anchors.materials)
            {
                mat.SetVector("_JellyOwn0", new Vector4(ownRadii[0], ownRadii[1], ownRadii[2], ownRadii[3]));
                mat.SetVector("_JellyOwn1", new Vector4(ownRadii[4], ownRadii[5], ownRadii[6], ownRadii[7]));
                mat.SetVector("_JellyCarrier", new Vector4(rideOn[0], rideOn[1], 0f, 0f));
                mat.SetVector("_JellyCoreA0", cores[0]);
                mat.SetVector("_JellyCoreB0", cores[1]);
                mat.SetVector("_JellyCoreA1", cores[2]);
                mat.SetVector("_JellyCoreB1", cores[3]);
                mat.SetVector("_JellyCol0", new Vector4(colRadii[0], colRadii[1], colRadii[2], colRadii[3]));
                mat.SetVector("_JellyCol1", new Vector4(colRadii[4], colRadii[5], colRadii[6], colRadii[7]));
                mat.SetFloat("_JellyTwitch", twitch != null ? handed.Count - 1 : 0);
                EditorUtility.SetDirty(mat);
            }
            // The part's own feel and the squash's knobs, as the inspector writes them live.
            var feel = FeelOf(squashed, names[pair], rides, animator);
            foreach (var mat in core.provider.materials)
            {
                Tune(mat, feel, hz, zeta, give, cloth ? CarriedLimit : 0.6f);
                Live(mat, squashed, o.slide);
            }
            // What the jelly tells the animator: the points strip's feedback through a driver the
            // parser fills.
            var parser = made.GetComponentInChildren<CVRTexturePropertyParser>(true);
            var driver = Undo.AddComponent<CVRAnimatorDriver>(parser.gameObject);
            var tells = TellsOf(names[pair]);
            for (int i = 0; i < tells.Length; i++)
            {
                driver.animators.Add(animator);
                driver.animatorParameters.Add(tells[i]);
                driver.animatorParameterType.Add(0);
                parser.tasks.Add(new CVRTexturePropertyParserTask
                {
                    x = 3 + i,
                    y = 0,
                    channel = CVRTexturePropertyParserTask.Channel.r,
                    minValue = 0f,
                    maxValue = 1f,
                    target = parser.gameObject,
                    component = driver,
                    propertyName = $"animatorParameter{i + 1:00}",
                    typeIndex = 0,
                });
            }
            string wired = !o.wire ? "the menu and animator left alone" : Wired(root.GetComponent<CVRAvatar>(), tells, squashed != null ? squashed.twitchSize : 0.02f, folder);

            var state = core.state;
            var left = new List<string>();
            int patched = 0;
            var jellied = Parts(root, names, dented);
            var worn = Worns(root);
            int reading = Enumerable.Range(0, YapsSquash.JellySlots).LastOrDefault(x => x < names.Count && worn.Contains(names[x])) + 1;
            foreach (var m in mats)
            {
                // The squash compiled in only while it still dents something: on every part it
                // carries the jelly has taken over, it costs a body's vertex program 85% of its
                // code to return early.
                var roles = YapsShaderPatcher.RolesOf(m.shader) & ~YapsShaderPatcher.Roles.JellyUnread | YapsShaderPatcher.Roles.Jelly | YapsShaderPatcher.Roles.Squash
                    | YapsShaderPatcher.JellyReading(reading);
                if (SquashIdle(m, jellied)) roles &= ~YapsShaderPatcher.Roles.Squash;
                var shader = YapsShaderPatcher.WithRoles(m, roles, report, out string why);
                if (shader == null || ShaderUtil.ShaderHasError(shader))
                {
                    // Unity's message runs on into advice; its first sentence says what happened.
                    if (why != null) why = why.Split(new[] { ". " }, 2, StringSplitOptions.None)[0];
                    left.Add(why == null ? m.name : $"{m.name}, as {why}");
                    if (why != null) report?.Warning(YapsSquash.Category, "A material is not on the jelly",
                        $"{m.name} keeps the squash alone for {names[pair]}: {why}. The next conversion tries it again.");
                    continue;
                }
                Undo.RecordObject(m, "Put the breasts on the jelly");
                YapsShaderPatcher.Wear(m, shader);
                // A skinned mesh is never instanced, so with no material asking, the build strips
                // the instancing copy of every variant.
                m.enableInstancing = false;
                m.SetTexture(StateOf(pair), state);
                m.SetVector(LatticeOf(pair), new Vector4(n.x, n.y, n.z, spacing));
                m.SetVector("_YAPS_JellySlap", Slapped);
                Carried(m, root, names, dented);
                EditorUtility.SetDirty(m);
                patched++;
            }
            AssetDatabase.SaveAssets();
            // Off with the mass; with the cloth carrying it, left as it was: switched on here, a
            // physics toggle off by default came on with it.
            var off = cloth ? restored : PairPhysics(root, part, on: false);
            string each = sides == 2 ? " each" : "", litres = string.Join(" and ", volume.Select(x => $"{1000f * x:0.0}")) + " litres";
            return $"On {root.name}: {names[pair]} on {(sides == 2 ? "two lattices" : "a lattice")} of {n.x}x{n.y}x{n.z} ({n.x * n.y * n.z} particles{each}, {100f * spacing:0.0} cm apart, " +
                   $"{100f * (n.x - 1) * spacing:0} x {100f * (n.y - 1) * spacing:0} x {100f * (n.z - 1) * spacing:0} cm) under {chest.name}; " +
                   (cloth ? $"{litres}, riding {string.Join(" and ", carriers.Select(x => x ?? "nothing"))}{(clothed ? ", which the cloth swings" : ", which no cloth swings")}, a hand moving the whole by {give:0.00} of its press; "
                       : $"{litres}, {kilos:0.0} kg{each}, so it swings at {hz:0.00} Hz and a hand moves the whole by {give:0.00} of its push; ") +
                   probed + $"{patched} of {mats.Count} squash materials read it{(left.Count > 0 ? " (left as they were: " + string.Join(", ", left) + ")" : "")}; " +
                   (cloth ? (off.Count > 0 ? "switched back on: " + string.Join(", ", off) : clothed ? "its cloth left on" : "no cloth on it")
                       : off.Count > 0 ? "switched off: " + string.Join(", ", off) : "no cloth or dynamic bones found on the pair, so if anything else swings it, switch that off") +
                   $"; {collided}; {(thinned.Count > 0 ? "thinned to clear it as built: " + string.Join(", ", thinned) : "none of the wearer's limbs reaching it as built")}{(ridden.Count > 0 ? $", each lattice left out of the limb it rides ({string.Join(", ", ridden)})" : "")}{(cored.Count > 0 ? $", held round its bone instead of its back face ({string.Join("; ", cored)})" : "")}; {wired}; {stale} earlier jelly taken off.";
        }

        // Slot s's state and lattice on the body's materials: the first part's, then the second's.
        internal static string StateOf(int s) => s == 0 ? "_YAPS_JellyState" : "_YAPS_JellyState" + (s + 1);
        internal static string LatticeOf(int s) => s == 0 ? "_YAPS_JellyLattice" : "_YAPS_JellyLattice" + (s + 1);

        // A slot's lattice kept only while its part's jelly is on; and each of the squash's own parts
        // the jelly carries named for the dent to leave, by its part and side as _YAPS_JellyOwn names
        // them (2 part + side, plus one).
        static void Carried(Material m, Transform root, List<string> names, List<string> dented)
        {
            var worn = Worns(root);
            for (int s = 0; s < YapsSquash.JellySlots; s++)
                if (s >= names.Count || !worn.Contains(names[s])) m.SetVector(LatticeOf(s), Vector4.zero);
            m.SetVector("_YAPS_JellyParts", Parts(root, names, dented));
        }

        static HashSet<string> Worns(Transform root) =>
            new HashSet<string>(root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(Worn + " ")).Select(t => t.name.Substring(Worn.Length + 1)));

        static Vector4 Parts(Transform root, List<string> names, List<string> dented)
        {
            var worn = Worns(root);
            var parts = Vector4.zero;
            for (int c = 0; c < 4 && c / 2 < dented.Count; c++)
            {
                int i = names.IndexOf(dented[c / 2]);
                if (i >= 0 && i < YapsSquash.JellySlots && worn.Contains(names[i])) parts[c] = 2 * i + c % 2 + 1;
            }
            return parts;
        }

        // Every part the squash built on m (a reach above 0) carried by the jelly, so its dent,
        // baked presses included, returns before doing anything (yaps_dent.cginc).
        static bool SquashIdle(Material m, Vector4 parts)
        {
            var reach = m.GetVector("_YAPS_SquashReach");
            for (int c = 0; c < 4; c++)
                if (reach[c] > 0f && !(parts[c] > 0f)) return false;
            return parts != Vector4.zero;
        }

        // The folder for an avatar's jelly beside the squash's in folder: the one its last jelly was
        // built in, read off its materials, so a rebuild writes over its own copies; else one of its
        // own, as two avatars of one name share the squash's folder. Read before the squash strips it.
        internal static string FolderFor(Transform root, string folder)
        {
            var mats = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).ToList();
            string Of(string name, string suffix) => mats.Where(m => m.HasProperty(name)).Select(m => m.GetTexture(name)).Where(t => t != null)
                .Select(t => Path.GetDirectoryName(AssetDatabase.GetAssetPath(t)).Replace('\\', '/')).Where(d => d.EndsWith(suffix))
                .Select(d => d.Substring(0, d.Length - suffix.Length)).FirstOrDefault();
            string found = Enumerable.Range(0, YapsSquash.JellySlots).Select(s => Of(StateOf(s), s > 0 ? "_" + (s + 1) : "")).FirstOrDefault(d => d != null);
            if (found != null) return found;
            // Unity names nothing under a folder that is not there yet: on a project's first build
            // the answer was empty, and the jelly's folder of "" threw.
            YapsNativeBuilder.EnsureFolderPublic(folder);
            return AssetDatabase.GenerateUniqueAssetPath(folder + "/" + YapsNativeBuilder.Sanitise(root.name) + " Jelly");
        }

        // Every pair the squash found put on the jelly with a user's defaults, its assets in the
        // folder into; a pair it refuses keeps the squash. What was said, as put on or refused.
        internal static (List<string> on, List<string> refused) On(Transform root, string into, BridgeReport report)
        {
            var on = new List<string>();
            var refused = new List<string>();
            var found = YapsSquash.PartsOf(root, out _);
            foreach (var (name, _) in found?.parts ?? new List<(string, int)>())
            {
                string said = PairOn(root, new Regex("^" + Regex.Escape(name) + "$", RegexOptions.IgnoreCase), into, Shaders, new Options(), report, found);
                Debug.Log("[YAPS] Jelly: " + said);
                (said.StartsWith("On ") ? on : refused).Add(said);
            }
            return (on, refused);
        }

        // Every part's jelly off, the squash materials back on the squash alone. What was done.
        internal static string Off(Transform picked)
        {
            var root = AvatarOf(picked);
            if (root == null) return "Select the avatar first.";
            int cleared = 0;
            foreach (var m in YapsSquash.Materials(root))
            {
                if (!m.HasProperty("_YAPS_JellyLattice") || m.GetVector("_YAPS_JellyParts") == Vector4.zero
                    && Enumerable.Range(0, YapsSquash.JellySlots).All(s => !m.HasProperty(LatticeOf(s)) || m.GetVector(LatticeOf(s)) == Vector4.zero)) continue;
                Undo.RecordObject(m, "Take the breasts off the jelly");
                for (int s = 0; s < YapsSquash.JellySlots; s++)
                    if (m.HasProperty(LatticeOf(s))) m.SetVector(LatticeOf(s), Vector4.zero);
                m.SetVector("_YAPS_JellyParts", Vector4.zero);
                // A shader that left the squash out to the jelly takes it back.
                var squash = YapsShaderPatcher.WithRole(m, YapsShaderPatcher.Roles.Squash, null);
                if (squash != null) YapsShaderPatcher.Wear(m, squash);
                EditorUtility.SetDirty(m);
                cleared++;
            }
            AssetDatabase.SaveAssets();
            var on = Restore(root, null);
            int stale = Unworn(root);
            string unwired = Wired(root.GetComponent<CVRAvatar>(), null, 0f, null);
            return $"Off {root.name}: {cleared} squash materials back on the squash alone, {stale} jelly taken off, {unwired}, " +
                   (on.Count > 0 ? "switched back on: " + string.Join(", ", on) : "no pair's cloth or dynamic bones were off");
        }

        // The cloth a jelly carrying its own mass switched off, back on, that pair's alone when named.
        // Only a mass build switches it off; anything else off was off already. What was switched.
        static List<string> Restore(Transform avatar, string only)
        {
            var said = new List<string>();
            foreach (var worn in avatar.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(Worn + " ") && (only == null || t.name == WornName(only))).ToList())
            {
                var anchors = worn.GetComponentsInChildren<CVRMaterialDataProvider>(true).FirstOrDefault(q => q.transforms != null && q.transforms.Length > 0);
                if (anchors != null && anchors.materials.Any(m => m != null && m.HasProperty("_MassCarried") && m.GetFloat("_MassCarried") < 0.5f))
                    said.AddRange(PairPhysics(avatar, PartOf(worn.name.Substring(Worn.Length + 1)), on: true));
            }
            return said;
        }

        // The parameters a pair's jelly tells, local so each viewer's own reading stands: never synced.
        static string[] TellsOf(string pair) => new[] { "Squeeze", "Grab", "Slap" }.Select(x => $"#Jelly/{pair}/{x}").ToArray();
        const string TwitchEntry = "Jelly twitch";

        // A pair's told parameters declared in every controller the avatar plays, or with none every
        // pair's taken out; and with any pair's twitch, a menu toggle whose every flip plays one kick
        // on each, size metres up and back, its clip in folder. The CCK's own toggle layer plays the
        // same clip on and off. What was done.
        static string Wired(CVRAvatar avatar, string[] tells, float size, string folder)
        {
            if (avatar == null) return "no CVRAvatar, so no menu or animator";
            var controllers = YapsAasAnimator.ControllersOf(avatar);
            foreach (var controller in controllers)
            {
                Undo.RegisterCompleteObjectUndo(controller, "Put the breasts on the jelly");
                foreach (var name in tells ?? new string[0])
                    if (!controller.parameters.Any(q => q.name == name)) controller.AddParameter(name, AnimatorControllerParameterType.Float);
                if (tells == null)
                    foreach (var had in controller.parameters.Where(q => q.name.StartsWith("#Jelly/")).ToList())
                        controller.RemoveParameter(had);
                EditorUtility.SetDirty(controller);
            }
            var settings = avatar.avatarSettings?.settings;
            var entry = settings?.FirstOrDefault(e => e != null && e.name == TwitchEntry);
            var twitches = avatar.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Jelly Twitch").ToList();
            string menu;
            if (twitches.Count == 0)
            {
                menu = entry == null ? "no twitch" : "the twitch toggle taken out";
                if (entry != null)
                {
                    Undo.RecordObject(avatar, "Take the breasts off the jelly");
                    settings.Remove(entry);
                    YapsAasAnimator.Unwire(avatar, entry.machineName);
                    EditorUtility.SetDirty(avatar);
                }
            }
            else
            {
                // Kept in place: remade at the same path, the layer's states read it Missing. The
                // toggle's own when it has one, as the second pair's folder is not the first's.
                var clip = entry?.toggleSettings?.animationClip;
                if (clip == null)
                {
                    string path = folder + "/Jelly_Twitch.anim";
                    clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (clip == null)
                    {
                        clip = new AnimationClip();
                        YapsNativeBuilder.EnsureFolderPublic(folder);
                        AssetDatabase.CreateAsset(clip, path);
                    }
                }
                clip.ClearCurves();
                foreach (var twitch in twitches)
                {
                    float up = size / Mathf.Max(Mathf.Abs(twitch.lossyScale.y), 1e-6f);
                    clip.SetCurve(AnimationUtility.CalculateTransformPath(twitch, avatar.transform), typeof(Transform), "m_LocalPosition.y",
                        new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.05f, up), new Keyframe(0.12f, up), new Keyframe(0.2f, 0f)));
                }
                EditorUtility.SetDirty(clip);
                if (entry == null)
                {
                    Undo.RecordObject(avatar, "Put the breasts on the jelly");
                    if (avatar.avatarSettings == null) avatar.avatarSettings = new CVRAdvancedAvatarSettings { settings = new List<CVRAdvancedSettingsEntry>(), initialized = true };
                    avatar.avatarUsesAdvancedSettings = true;
                    entry = new CVRAdvancedSettingsEntry
                    {
                        name = TwitchEntry,
                        machineName = "JellyTwitch",
                        type = CVRAdvancedSettingsEntry.SettingsType.Toggle,
                        toggleSettings = new CVRAdvancesAvatarSettingGameObjectToggle
                        {
                            usedType = CVRAdvancesAvatarSettingBase.ParameterType.Bool,
                            useAnimationClip = true,
                            animationClip = clip,
                            offAnimationClip = clip,
                        },
                    };
                    avatar.avatarSettings.settings.Add(entry);
                    EditorUtility.SetDirty(avatar);
                }
                menu = $"a \"{TwitchEntry}\" toggle kicking {100f * size:0.0} cm" + (YapsAasAnimator.Wire(avatar, entry) != null ? ", its layer written" : "");
            }
            menu += "; " + MarkersWired(avatar, folder);
            AssetDatabase.SaveAssets();
            return $"{menu}; {(tells != null ? string.Join(", ", tells) + " declared" : "every #Jelly/ parameter taken out")} in {controllers.Count} controller(s)";
        }

        internal const string MarkersEntry = "Jelly probes (debug)";

        // With any part's probe markers, a menu toggle showing them all, off by default; with none, the
        // toggle taken out. Its two clips in folder, kept in place.
        static string MarkersWired(CVRAvatar avatar, string folder)
        {
            var settings = avatar.avatarSettings?.settings;
            var entry = settings?.FirstOrDefault(e => e != null && e.name == MarkersEntry);
            var markers = avatar.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(MarkersName)).ToList();
            if (markers.Count == 0)
            {
                if (entry == null) return "no probe markers";
                Undo.RecordObject(avatar, "Take the breasts off the jelly");
                settings.Remove(entry);
                YapsAasAnimator.Unwire(avatar, entry.machineName);
                EditorUtility.SetDirty(avatar);
                return "the probe markers' toggle taken out";
            }
            AnimationClip Clip(AnimationClip had, string name, float on)
            {
                var clip = had != null ? had : AssetDatabase.LoadAssetAtPath<AnimationClip>(folder + name);
                if (clip == null)
                {
                    clip = new AnimationClip();
                    YapsNativeBuilder.EnsureFolderPublic(folder);
                    AssetDatabase.CreateAsset(clip, folder + name);
                }
                clip.ClearCurves();
                foreach (var m in markers)
                    clip.SetCurve(AnimationUtility.CalculateTransformPath(m, avatar.transform), typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 1f / 60f, on));
                EditorUtility.SetDirty(clip);
                return clip;
            }
            var shown = Clip(entry?.toggleSettings?.animationClip, "/Jelly_Probes_On.anim", 1f);
            var hidden = Clip(entry?.toggleSettings?.offAnimationClip, "/Jelly_Probes_Off.anim", 0f);
            if (entry == null)
            {
                Undo.RecordObject(avatar, "Put the breasts on the jelly");
                if (avatar.avatarSettings == null) avatar.avatarSettings = new CVRAdvancedAvatarSettings { settings = new List<CVRAdvancedSettingsEntry>(), initialized = true };
                avatar.avatarUsesAdvancedSettings = true;
                entry = new CVRAdvancedSettingsEntry
                {
                    name = MarkersEntry,
                    machineName = "JellyProbes",
                    type = CVRAdvancedSettingsEntry.SettingsType.Toggle,
                    toggleSettings = new CVRAdvancesAvatarSettingGameObjectToggle
                    {
                        usedType = CVRAdvancesAvatarSettingBase.ParameterType.Bool,
                        useAnimationClip = true,
                        animationClip = shown,
                        offAnimationClip = hidden,
                    },
                };
                avatar.avatarSettings.settings.Add(entry);
                EditorUtility.SetDirty(avatar);
            }
            return $"a \"{MarkersEntry}\" toggle showing {markers.Count} part(s)' probes" + (YapsAasAnimator.Wire(avatar, entry) != null ? ", its layer written" : "");
        }

        // The cloth and dynamic bones whose roots are on the pair's bone chains, switched on or off;
        // what was switched.
        static List<string> PairPhysics(Transform root, Regex part, bool on)
        {
            var said = new List<string>();
            foreach (var b in PairCloth(root, part))
            {
                if (b.enabled == on) continue;
                Undo.RecordObject(b, on ? "Take the breasts off the jelly" : "Put the breasts on the jelly");
                b.enabled = on;
                said.Add($"{b.GetType().Name} on {b.name}");
            }
            return said;
        }

        static readonly Regex SideOf = new Regex(@"left|right|[._ ]?[lr](?=$|[._ ])", RegexOptions.IgnoreCase);

        // The humanoid's own bones; none for anything else.
        static HashSet<Transform> Skeleton(Animator animator) => new HashSet<Transform>(animator != null && animator.isHuman
            ? Enumerable.Range(0, (int) HumanBodyBones.LastBone).Select(b => animator.GetBoneTransform((HumanBodyBones) b)).Where(t => t != null)
            : Enumerable.Empty<Transform>());

        // A pair's chain of bones as the name search names it: the topmost bone naming a side that
        // the carrier hangs from, its side taken out. Not the chain's top: a seat's hangs from a
        // ButtRoot that names no side. Null when the skeleton itself carries it.
        static Regex PartFrom(Transform carrier, ICollection<Transform> skeleton)
        {
            if (carrier == null || skeleton.Count == 0 || skeleton.Contains(carrier)) return null;
            var top = carrier;
            while (top.parent != null && !skeleton.Contains(top.parent) && SideOf.IsMatch(top.parent.name)) top = top.parent;
            string word = SideOf.Replace(top.name, "").Trim('.', '_', ' ', '-');
            return word.Length > 0 ? PartOf(word) : null;
        }

        // The cloth and dynamic bones whose roots are on the pair's bone chains, found as the squash
        // finds the chains, by name and side.
        internal static List<Behaviour> PairCloth(Transform root, Regex part)
        {
            bool Sided(Transform t) => t != null && part.IsMatch(t.name) && SideOf.IsMatch(t.name);
            var chains = new HashSet<Transform>(root.GetComponentsInChildren<Transform>(true)
                .Where(t => Sided(t) && !Sided(t.parent)).SelectMany(t => t.GetComponentsInChildren<Transform>(true)));
            var found = new List<Behaviour>();
            foreach (var b in root.GetComponentsInChildren<Behaviour>(true))
            {
                string type = b != null ? b.GetType().Name : "";
                if (type != "MagicaCloth" && type != "DynamicBone") continue;
                if (RootsOf(b).Any(t => t != null && chains.Contains(t))) found.Add(b);
            }
            return found;
        }

        static List<Transform> RootsOf(Behaviour b)
        {
            var so = new SerializedObject(b);
            var roots = new List<Transform>();
            foreach (var path in new[] { "serializeData.rootBones", "m_Roots" })
            {
                var list = so.FindProperty(path);
                if (list != null && list.isArray)
                    for (int i = 0; i < list.arraySize; i++) roots.Add(list.GetArrayElementAtIndex(i).objectReferenceValue as Transform);
            }
            if (so.FindProperty("m_Root")?.objectReferenceValue is Transform one) roots.Add(one);
            return roots;
        }

        static bool IsCollider(Component c) => c != null && (c.GetType().Name == "MagicaCapsuleCollider" || c.GetType().Name == "MagicaSphereCollider" || c.GetType().Name == "DynamicBoneCollider");

        // The colliders the pair's cloth and dynamic bones collide with, read off their lists.
        static List<Component> PairColliders(Transform root, Regex part)
        {
            var found = new List<Component>();
            foreach (var b in PairCloth(root, part))
            {
                var list = new SerializedObject(b).FindProperty(b.GetType().Name == "MagicaCloth" ? "serializeData.colliderCollisionConstraint.colliderList" : "m_Colliders");
                if (list == null || !list.isArray) continue;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue is Component c && IsCollider(c) && !found.Contains(c)) found.Add(c);
            }
            return found;
        }

        // The pair's cloth and dynamic bones made to collide with these or not, each with its own
        // kind. ponytail: put back into every cloth of the pair of its kind, not only the ones it was in.
        static void PairCollide(Transform root, Regex part, IEnumerable<Component> colliders, bool with)
        {
            var set = colliders.ToList();
            if (set.Count == 0) return;
            foreach (var b in PairCloth(root, part))
            {
                bool magica = b.GetType().Name == "MagicaCloth";
                var so = new SerializedObject(b);
                var list = so.FindProperty(magica ? "serializeData.colliderCollisionConstraint.colliderList" : "m_Colliders");
                if (list == null || !list.isArray) continue;
                var have = Enumerable.Range(0, list.arraySize).Select(i => list.GetArrayElementAtIndex(i).objectReferenceValue as Component).ToList();
                var want = with ? have.Concat(set.Where(c => !have.Contains(c) && c.GetType().Name.StartsWith("Magica") == magica)).ToList() : have.Where(c => !set.Contains(c)).ToList();
                if (want.Count == have.Count) continue;
                list.arraySize = want.Count;
                for (int i = 0; i < want.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = want[i];
                so.ApplyModifiedProperties();
            }
        }

        // A jellied part's cloth balls, MagicaCloth's least.
        internal const float Shrunk = 0.001f;
        const string RadiusMark = "Jelly radius ";
        static string RadiusOf(string type) => type == "MagicaCloth" ? "serializeData.radius.value" : "m_Radius";

        // The pair's cloth and dynamic bones' balls cut to Shrunk, each old size kept in a mark under
        // the jelly, "Jelly radius <size> <type> <nth of its type there> <path>"; what was cut. Not one
        // rooted inside another's chain: a piercing's cloth hanging off the part still feels a hand.
        static List<string> PairShrink(Transform root, Regex part, Transform under)
        {
            var said = new List<string>();
            var cloths = PairCloth(root, part).Select(b => (b, roots: RootsOf(b).Where(t => t != null).ToList())).ToList();
            foreach (var (b, roots) in cloths.Where(c => !c.roots.Any(t => cloths.Any(o => o.b != c.b && o.roots.Any(r => r != t && t.IsChildOf(r))))).ToList())
            {
                string type = b.GetType().Name;
                var so = new SerializedObject(b);
                var r = so.FindProperty(RadiusOf(type));
                if (r == null || r.floatValue <= Shrunk) continue;
                int nth = Array.IndexOf(b.GetComponents<Behaviour>().Where(x => x != null && x.GetType().Name == type).ToArray(), b);
                var mark = new GameObject($"{RadiusMark}{r.floatValue.ToString("R", CultureInfo.InvariantCulture)} {type} {nth} {AnimationUtility.CalculateTransformPath(b.transform, root)}");
                Undo.RegisterCreatedObjectUndo(mark, "Put the breasts on the jelly");
                mark.transform.SetParent(under, false);
                said.Add($"{b.name} from {1000f * r.floatValue:0} mm");
                r.floatValue = Shrunk;
                so.ApplyModifiedProperties();
            }
            return said;
        }

        // A mark's ball back to the size it keeps.
        static void Unshrink(Transform root, string mark)
        {
            var bits = mark.Substring(RadiusMark.Length).Split(new[] { ' ' }, 4);
            if (bits.Length < 4 || !float.TryParse(bits[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float size) || !int.TryParse(bits[2], out int nth)) return;
            // By path, not Find: a slash in a name reads as missing to Find.
            var at = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => AnimationUtility.CalculateTransformPath(t, root) == bits[3]);
            var b = at != null ? at.GetComponents<Behaviour>().Where(x => x != null && x.GetType().Name == bits[1]).ElementAtOrDefault(nth) : null;
            if (b == null) return;
            var so = new SerializedObject(b);
            var r = so.FindProperty(RadiusOf(bits[1]));
            if (r == null) return;
            r.floatValue = size;
            so.ApplyModifiedProperties();
        }

        // A collider as a capsule in the world, its ends and radius: MagicaCloth's spheres and
        // capsules, DynamicBone's outside ones. Planes and inside bounds are not a shape to press with.
        static bool Capsule(Component c, out Vector3 a, out Vector3 b, out float r)
        {
            a = b = Vector3.zero;
            r = 0f;
#if AVATARBRIDGE_MAGICA
            if (c is MagicaCloth2.MagicaSphereCollider sphere)
            {
                a = b = sphere.transform.TransformPoint(sphere.center);
                r = sphere.GetSize().x * Mathf.Abs(sphere.transform.lossyScale.x);
                return r > 0f;
            }
            if (c is MagicaCloth2.MagicaCapsuleCollider cap)
            {
                // As MagicaCloth draws it: centred, or from the centre back along its direction.
                var size = cap.GetSize();
                var dir = cap.GetLocalDir();
                float from = cap.alignedOnCenter ? 0.5f * size.z : 0f, to = cap.alignedOnCenter ? 0.5f * size.z : size.z - size.x;
                a = cap.transform.TransformPoint(cap.center + dir * Mathf.Max(from - size.x, 0f));
                b = cap.transform.TransformPoint(cap.center - dir * Mathf.Max(to - size.y, 0f));
                // ponytail: a cone as a cylinder of its larger end.
                r = Mathf.Max(size.x, size.y) * Mathf.Abs(cap.transform.lossyScale.x);
                return r > 0f;
            }
#endif
#if AVATARBRIDGE_DYNBONE
            if (c is DynamicBoneCollider db && db.m_Bound != DynamicBoneColliderBase.Bound.Inside)
            {
                var dir = db.m_Direction == DynamicBoneColliderBase.Direction.X ? Vector3.right : db.m_Direction == DynamicBoneColliderBase.Direction.Y ? Vector3.up : Vector3.forward;
                float half = Mathf.Max(0.5f * db.m_Height - db.m_Radius, 0f);
                a = db.transform.TransformPoint(db.m_Center - dir * half);
                b = db.transform.TransformPoint(db.m_Center + dir * half);
                r = db.m_Radius * Mathf.Abs(db.transform.lossyScale.x);
                return r > 0f;
            }
#endif
            return false;
        }

        static float Seg(Vector3 p, Vector3 a, Vector3 b)
        {
            var s = b - a;
            return Vector3.Distance(p, a + s * Mathf.Clamp01(Vector3.Dot(p - a, s) / Mathf.Max(s.sqrMagnitude, 1e-12f)));
        }

        // The bone carrying most of a side's skin, share its share of each vertex, as the squash's
        // follow rides it: of the bones
        // below the chest and off the skeleton, when they carry a quarter of it or more; else the
        // most weighted bone of all, the trunk itself when it carries most (a breast painted on a
        // body with no bones of its own rode the shoulder at 5%).
        static Transform Carrier(SkinnedMeshRenderer body, float[] share, Transform chest, ICollection<Transform> skeleton, bool clothed = true)
        {
            var weights = body.sharedMesh.boneWeights;
            var bones = body.bones;
            var sum = new float[bones.Length];
            for (int v = 0; v < weights.Length && v < share.Length; v++)
            {
                if (share[v] < 0.5f) continue;
                var w = weights[v];
                sum[w.boneIndex0] += w.weight0;
                sum[w.boneIndex1] += w.weight1;
                sum[w.boneIndex2] += w.weight2;
                sum[w.boneIndex3] += w.weight3;
            }
            // The most weighted one hanging from another of them, as the squash's follow: the cloth
            // holds a chain's root still, and the regions on it never swung. Else the most weighted.
            float total = sum.Sum();
            var carrying = new HashSet<Transform>(Enumerable.Range(0, bones.Length)
                .Where(b => bones[b] != null && sum[b] >= 0.03f * total && !chest.IsChildOf(bones[b]) && !skeleton.Contains(bones[b])).Select(b => bones[b]));
            Transform best = null, hung = null, heaviest = null;
            float most = 0f, mostHung = 0f, mostOfAll = 0f, loose = 0f;
            for (int b = 0; b < bones.Length; b++)
            {
                if (bones[b] == null) continue;
                if (sum[b] > mostOfAll) { mostOfAll = sum[b]; heaviest = bones[b]; }
                if (!carrying.Contains(bones[b])) continue;
                loose += sum[b];
                if (sum[b] > most) { most = sum[b]; best = bones[b]; }
                if (sum[b] > mostHung && carrying.Contains(bones[b].parent)) { mostHung = sum[b]; hung = bones[b]; }
            }
            if (loose < 0.25f * total) return heaviest;
            // With no cloth there is no still root to pass over: a thigh rides its leg, not the shin
            // hanging from it.
            return clothed ? hung ?? best : best;
        }

        // Region r's probes (spots[r], none for null) and the skinned helper the step reads the regions'
        // frames and the probes through: each region carried by provider.transforms[r], held still by
        // hangs[r], depth its lattice's middle off its anchor; a collider felt reach off the skin.
        // One hang for a part alone, its frame read for both. Its assets in folder.
        internal static void AddSkin(GameObject made, CVRMaterialDataProvider provider, Transform[] hangs, List<Vector3>[] spots, float depth, string undo, string folder, float reach, bool[] solid = null, int solidFrom = 0)
        {
            var probes = new List<Transform>[] { new List<Transform>(), new List<Transform>() };
#if AVATARBRIDGE_MAGICA
            // On a solid side, a ball the world's colliders push, sprung to its rest on the hang: the
            // step reads it as it reads a cloth probe, through a tip on the skin it carries. The ball
            // sits reach less its radius out, so a surface is felt from reach off the skin
            // as a collider is by the cloth's; one that size on the skin would shove its neighbours.
            // Frictionless, so a surface slides past rather than dragging it along.
            PhysicMaterial slick = null;
            Transform Solid(int side, int k, Vector3 w, Vector3 outward)
            {
                const float radius = 0.01f;
                slick = slick ?? Save(new PhysicMaterial("Jelly probe") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicMaterialCombine.Minimum, bounciness = 0f }, folder + "/Jelly_Probe.physicMaterial");
                var rest = new GameObject($"Jelly Probe Rest {side} {k}");
                rest.transform.SetParent(hangs[side], false);
                rest.transform.position = w + outward * (reach - radius);
                rest.AddComponent<Rigidbody>().isKinematic = true;
                var ball = new GameObject($"Jelly Probe Ball {side} {k}");
                ball.transform.SetParent(hangs[side], false);
                ball.transform.position = rest.transform.position;
                var body = ball.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.mass = 0.05f;
                body.constraints = RigidbodyConstraints.FreezeRotation;
                // Drawn where its speed says it is now: interpolated, it drew a physics step behind a
                // rest that moves every frame, and a body turning read as pushed.
                body.interpolation = RigidbodyInterpolation.Extrapolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var sphere = ball.AddComponent<SphereCollider>();
                sphere.radius = radius;
                sphere.sharedMaterial = slick;
                // 32 Hz and critical: at 7 Hz a body turning lagged each ball a centimetre behind its
                // rest, read as a push, and the breasts swung half again as far turning.
                var joint = ball.AddComponent<ConfigurableJoint>();
                joint.connectedBody = rest.GetComponent<Rigidbody>();
                joint.autoConfigureConnectedAnchor = false;
                joint.anchor = Vector3.zero;
                joint.connectedAnchor = Vector3.zero;
                joint.xDrive = joint.yDrive = joint.zDrive = new JointDrive { positionSpring = 2000f, positionDamper = 20f, maximumForce = float.MaxValue };
                var tip = new GameObject($"Jelly Probe Tip {side} {k}").transform;
                tip.SetParent(ball.transform, false);
                tip.position = w;
                return tip;
            }
            // Each probe a bone lying 10 cm along the skin to its spot, which a collider tips in: a
            // loose spring point takes every collider as soft and gave back 85% of each push past its
            // radius, 12 mm read for 30 in. Long enough to swing in by its reach and a deep press.
            List<Transform> Bones()
            {
                var bases = new List<Transform>();
                for (int side = 0; side < hangs.Length; side++)
                {
                    var region = made.transform.Find($"Jelly Region {side}");
                    var mid = region.TransformPoint(0f, 0f, depth);
                    for (int k = 0; k < spots[side].Count; k++)
                    {
                        // A ball sits on a cloth probe's spot, the best spread of the farthest-first picks:
                        // on the fill-ins after them, a wall's skin stood 14 mm past it on the prop.
                        bool ball = solid != null && solid[side] && k >= solidFrom;
                        var w = spots[side][ball ? k - solidFrom : k];
                        var outward = (w - mid).normalized;
                        if (ball)
                        {
                            probes[side].Add(Solid(side, k, w, outward));
                            continue;
                        }
                        var along = Vector3.Cross(outward, region.right);
                        if (along.sqrMagnitude < 0.01f) along = Vector3.Cross(outward, region.up);
                        along.Normalize();
                        var b = new GameObject($"Jelly Probe Base {side} {k}").transform;
                        b.SetParent(hangs[side], false);
                        b.position = w - along * Mathf.Max(0.1f, 2f * reach);
                        var tip = new GameObject($"Jelly Probe Tip {side} {k}").transform;
                        tip.SetParent(b, false);
                        tip.position = w;
                        bases.Add(b);
                        probes[side].Add(tip);
                    }
                }
                return bases;
            }
            var bases = spots != null ? Bones() : new List<Transform>();
            if (bases.Count > 0)
            {
                var net = new GameObject("Jelly Probes");
                net.transform.SetParent(made.transform, false);
                Undo.RegisterCreatedObjectUndo(net, undo);
                net.SetActive(false);
                var sd = net.AddComponent<MagicaCloth2.MagicaCloth>().SerializeData;
                sd.clothType = MagicaCloth2.ClothProcess.ClothType.BoneCloth;
                sd.rootBones = bases;
                // A collider is felt from reach off the skin: a palm between two probes reaches
                // one, and the push crosses the skin at speed, as a palm crosses the box.
                sd.radius = new MagicaCloth2.CurveSerializeData(reach);
                sd.gravity = 0f;
                // A sensor, not cloth: a world's wind zone reached every probe and read as presses,
                // which took the slots a hand needed.
                sd.wind.influence = 0f;
                // Back within a few frames once a collider lets go, and settled, not bouncing: at the
                // default damping it still read 2.6 mm a moment after, past the 2 mm a press takes,
                // and at 0.5 the 10 cm bone swung back out to 5 mm.
                sd.damping = new MagicaCloth2.CurveSerializeData(0.8f);
                sd.angleRestorationConstraint.useAngleRestoration = true;
                sd.angleRestorationConstraint.stiffness = new MagicaCloth2.CurveSerializeData(1f);
                // Every viewer's copy, seen or not: it has no renderer of its own to be culled by.
                sd.cullingSettings.cameraCullingMode = MagicaCloth2.CullingSettings.CameraCullingMode.Off;
                // Carried rigidly by the chest, or a hop would read as a press.
                sd.inertiaConstraint.worldInertia = 0f;
                sd.inertiaConstraint.localInertia = 0f;
                // Each where its collider puts it: the default 4 m/s cap would hold back a faster slap's.
                sd.inertiaConstraint.particleSpeedLimit = new MagicaCloth2.CheckSliderSerializeData(false, 4f);
                net.SetActive(true);
            }
#endif
            // The bones as the cloth swings them reach the step only through skinning, as the
            // squash's slots do: a helper mesh skinned four points to each region's bone and four to
            // its still copy. Its triangles have no area, so it draws nothing. "[FPR]" keeps the
            // client's first-person copy off it, which stops skinning the wearer's renderers
            // offscreen and froze the squash's.
            var skin = new GameObject("Jelly skin [FPR]").AddComponent<SkinnedMeshRenderer>();
            skin.transform.SetParent(made.transform, false);
            Undo.RegisterCreatedObjectUndo(skin.gameObject, undo);
            var skinBones = new List<Transform>();
            var points = new List<Vector3>();
            var skinWeights = new List<BoneWeight>();
            for (int side = 0; side < 2; side++)
                foreach (var on in new[] { provider.transforms[side], hangs[Mathf.Min(side, hangs.Length - 1)] })
                {
                    int b = skinBones.IndexOf(on);
                    if (b < 0) { b = skinBones.Count; skinBones.Add(on); }
                    foreach (var p in new[] { Vector3.zero, Vector3.right * 0.1f, Vector3.up * 0.1f, Vector3.forward * 0.1f })
                    {
                        points.Add(skin.transform.InverseTransformPoint(hangs[Mathf.Min(side, hangs.Length - 1)].TransformPoint(p)));
                        skinWeights.Add(new BoneWeight { boneIndex0 = b, weight0 = 1f });
                    }
                }
            // Then each probe twice, on itself and at its rest on the hang, from 16: a region's
            // pairs after the last's.
            int probed = hangs.Length > 1 ? Mathf.Min(probes[0].Count, probes[1].Count) : probes[0].Count;
            for (int side = 0; side < hangs.Length; side++)
                for (int k = 0; k < probed; k++)
                    foreach (var on in new[] { probes[side][k], hangs[side] })
                    {
                        int b = skinBones.IndexOf(on);
                        if (b < 0) { b = skinBones.Count; skinBones.Add(on); }
                        points.Add(skin.transform.InverseTransformPoint(probes[side][k].position));
                        skinWeights.Add(new BoneWeight { boneIndex0 = b, weight0 = 1f });
                    }
            // Kept in place when it exists: remade at the same path, scene copies read it Missing.
            var skinMesh = AssetDatabase.LoadAssetAtPath<Mesh>(folder + "/Jelly_Skin.asset");
            if (skinMesh == null)
            {
                skinMesh = new Mesh();
                AssetDatabase.CreateAsset(skinMesh, folder + "/Jelly_Skin.asset");
            }
            skinMesh.Clear();
            skinMesh.name = "Jelly skin";
            skinMesh.vertices = points.ToArray();
            skinMesh.boneWeights = skinWeights.ToArray();
            skinMesh.bindposes = skinBones.Select(b => b.worldToLocalMatrix * skin.transform.localToWorldMatrix).ToArray();
            skinMesh.triangles = Enumerable.Range(0, 3 * points.Count).Select(i => i / 3).ToArray();
            EditorUtility.SetDirty(skinMesh);
            skin.sharedMesh = skinMesh;
            skin.bones = skinBones.ToArray();
            skin.sharedMaterial = Save(new Material(Shader.Find("Standard")), folder + "/Jelly_Skin.mat");
            skin.updateWhenOffscreen = true;
            skin.shadowCastingMode = ShadowCastingMode.Off;
            skin.receiveShadows = false;
            // A world probe's face reaches half again as far as the probes sit apart, so a pane's
            // faces overlap and leave no gap for the skin to show through.
            float patch = 0f;
            for (int ps = 0; ps < hangs.Length; ps++)
                if (solid != null && solid[ps] && spots != null && spots[ps].Count > solidFrom + 1)
                {
                    var balls = spots[ps].Take(spots[ps].Count - solidFrom).ToList();
                    patch = Mathf.Max(patch, 1.5f * balls.Average(pa => balls.Where(pq => pq != pa).Min(pq => (pa - pq).magnitude)));
                }
            foreach (var mat in provider.materials)
            {
                mat.SetFloat("_JellySkinStride", skinMesh.GetVertexBufferStride(0));
                mat.SetFloat("_JellyProbes", probed);
                mat.SetFloat("_JellyProbeReach", reach);
                mat.SetFloat("_JellySolid", solid == null ? 0 : (solid[0] ? 1 : 0) + (solid[1] ? 2 : 0));
                mat.SetFloat("_JellySolidFrom", solidFrom);
                mat.SetFloat("_JellyProbePatch", patch);
                EditorUtility.SetDirty(mat);
            }
            // The client disposes the skinned buffer it fetched right after binding it, so the
            // squash read zeros; the previous-buffer mode binds a copy the provider owns.
            var skinned = provider.gameObject.AddComponent<CVRMaterialDataProvider>();
            Undo.RegisterCreatedObjectUndo(skinned, undo);
            skinned.materials = provider.materials.ToArray();
            skinned.dataMode = CVRMaterialDataProvider.DataMode.SkinnedMeshRendererPreviousVertexBuffer;
            skinned.smr = skin;
            skinned.shaderPropertyName = "_JellySkin";
            // After the anchors, before the passes.
            var renders = provider.GetComponent<CVRRenderController>();
            Undo.RecordObject(renders, undo);
            renders.cvrRenderComponents.Insert(renders.cvrRenderComponents.IndexOf(provider) + 1, skinned);
        }

        internal const string MarkersName = "Jelly Probe Markers";

        // A marker on each probe the step reads, for testing in game: skinned to its tip, so a
        // collider that moves the probe moves it, white turning red as far as the step read it pushed
        // in, a centimetre red. Off until the menu shows it. "[FPR]" for the skin's reason: the
        // first-person copy would freeze it.
        static void Markers(GameObject made, RenderTexture state, Vector3Int n, int probed, string folder, string shaders)
        {
            if (Write(shaders + "/JellyProbeMarker.shader", MarkerShader)) AssetDatabase.Refresh();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaders + "/JellyProbeMarker.shader");
            if (shader == null) return;
            var tips = made.GetComponentsInChildren<Transform>(true).Select(t => (t, at: t.name.StartsWith("Jelly Probe Tip ") ? t.name.Substring(16).Split(' ') : null))
                .Where(x => x.at != null && x.at.Length == 2 && int.Parse(x.at[1]) < probed).ToList();
            if (tips.Count == 0) return;
            var markers = new GameObject(MarkersName + " [FPR]").AddComponent<SkinnedMeshRenderer>();
            markers.transform.SetParent(made.transform, false);
            Undo.RegisterCreatedObjectUndo(markers.gameObject, "Put the breasts on the jelly");
            const float r = 0.006f;
            var corners = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            int[] faces = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
            var points = new List<Vector3>();
            var at = new List<Vector2>();
            var weights = new List<BoneWeight>();
            var triangles = new List<int>();
            int rows = Side(n);
            for (int i = 0; i < tips.Count; i++)
            {
                int side = int.Parse(tips[i].at[0]), k = int.Parse(tips[i].at[1]);
                foreach (int f in faces) triangles.Add(points.Count + f);
                foreach (var c in corners)
                {
                    points.Add(markers.transform.InverseTransformPoint(tips[i].t.position + c * r));
                    // The probe's readout texel, on its region's side row.
                    at.Add(new Vector2(Readout + k, side * (rows + 1) + rows));
                    weights.Add(new BoneWeight { boneIndex0 = i, weight0 = 1f });
                }
            }
            // Kept in place when it exists: remade at the same path, scene copies read it Missing.
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(folder + "/Jelly_Probe_Markers.asset");
            if (mesh == null)
            {
                mesh = new Mesh();
                AssetDatabase.CreateAsset(mesh, folder + "/Jelly_Probe_Markers.asset");
            }
            mesh.Clear();
            mesh.name = "Jelly probe markers";
            mesh.vertices = points.ToArray();
            mesh.SetUVs(1, at);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = tips.Select(x => x.t.worldToLocalMatrix * markers.transform.localToWorldMatrix).ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            markers.sharedMesh = mesh;
            markers.bones = tips.Select(x => x.t).ToArray();
            var mat = Save(new Material(shader), folder + "/Jelly_Probe_Markers.mat");
            mat.SetTexture("_MainTex", state);
            EditorUtility.SetDirty(mat);
            markers.sharedMaterial = mat;
            markers.updateWhenOffscreen = true;
            markers.shadowCastingMode = ShadowCastingMode.Off;
            markers.receiveShadows = false;
            markers.gameObject.SetActive(false);
        }

        internal const string MarkerShader = @"// The jelly's probe markers, for testing. Written by YAPS; edits here are overwritten.
Shader ""Hidden/YAPS/Jelly Probe Marker""
{
    Properties { _MainTex (""The jelly's state"", 2D) = ""black"" {} }
    SubShader
    {
        // Over everything, so a probe the body hides still shows.
        Tags { ""Queue"" = ""Overlay"" ""RenderType"" = ""Overlay"" ""IgnoreProjector"" = ""True"" }
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include ""UnityCG.cginc""
            Texture2D<float4> _MainTex;
            struct appdata { float4 vertex : POSITION; float2 at : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; fixed4 colour : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                // How far the step read this probe pushed in, metres.
                float pushed = _MainTex.Load(int3((int2) v.at, 0)).w;
                o.colour = lerp(fixed4(1, 1, 1, 1), fixed4(1, 0, 0, 1), saturate(pushed / 0.01));
                return o;
            }
            fixed4 frag(v2f i) : SV_Target { return i.colour; }
            ENDCG
        }
    }
}
";

        // One jelly's textures and passes, its scene objects unsaved: an anchor a region on a root,
        // two steps into a twin and back, and with a rider or the far switch a third pass and the
        // parser reading it. Null when the step shader has errors.
        internal sealed class Built
        {
            public GameObject root, tissue;
            public List<Transform> anchors;
            public CVRMaterialDataProvider provider;
            public CVRRenderController controller;
            public RenderTexture state;
            public CVRTexturePropertyParser parser;
            public Shader step;
        }

        internal static Built Core(string folder, string shaders, Vector3Int n, float spacing, int regions, bool sleeps, bool withRider, bool offFar, float lift)
        {
            YapsNativeBuilder.EnsureFolderPublic(folder);
            YapsNativeBuilder.EnsureFolderPublic(shaders);
            // Written only when changed: each import compiles every variant again.
            if (Write(shaders + "/JellyCommon.cginc", CommonInclude) | Write(shaders + "/JellyStep.shader", StepShader)) AssetDatabase.Refresh();
            var step = AssetDatabase.LoadAssetAtPath<Shader>(shaders + "/JellyStep.shader");
            if (step == null || ShaderUtil.ShaderHasError(step))
            {
                Debug.Log("[Jelly] BUILD ABORT: the step shader did not import or has errors: " + (step == null ? "missing" : string.Join(" | ", ShaderUtil.GetShaderMessages(step)
                    .Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).Select(m => $"{m.message} line {m.line}"))));
                return null;
            }

            var state = Save(State(n, regions), folder + "/Jelly_State.renderTexture");
            var twin = Save(State(n, regions), folder + "/Jelly_State_B.renderTexture");
            var stepMat = Save(StepMaterial(step, n, 0, spacing), folder + "/Jelly_Step.mat");
            var stepMat2 = Save(StepMaterial(step, n, 1, spacing), folder + "/Jelly_Step_2.mat");
            // Only a prop sleeps: worn, the avatar's own sway keeps it awake, and the test alone costs
            // a slow GPU a few percent. Set on the saved materials, which a rebuild keeps.
            foreach (var mat in new[] { stepMat, stepMat2 })
            {
                if (sleeps) mat.EnableKeyword("JELLY_SLEEP");
                else mat.DisableKeyword("JELLY_SLEEP");
                // The rider's texels are read only by the parser's pass and the sleep: worn without
                // either, stepping them cost a particle's whole step on two texels a region.
                mat.SetFloat("_Rider", withRider || sleeps ? 1f : 0f);
                mat.SetFloat("_Lift", lift);
                mat.SetFloat("_Regions", regions);
                EditorUtility.SetDirty(mat);
            }
            var root = new GameObject("Jelly Spike");
            // Each region an anchor of its own. Packed, they sit side by side to be moved; alone, the
            // root is the anchor.
            var anchors = new List<Transform> { root.transform };
            if (regions > 1)
            {
                anchors.Clear();
                for (int r = 0; r < regions; r++)
                {
                    var anchor = new GameObject($"Jelly Region {r}").transform;
                    anchor.SetParent(root.transform, false);
                    anchor.localPosition = new Vector3((r - 0.5f * (regions - 1)) * ((n.x - 1) * spacing + 0.1f), 0f, 0f);
                    anchors.Add(anchor);
                }
            }
            var tissue = new GameObject("Tissue");
            tissue.transform.SetParent(root.transform, false);
            var provider = tissue.AddComponent<CVRMaterialDataProvider>();
            provider.dataMode = CVRMaterialDataProvider.DataMode.TransformLocalToWorld;
            provider.transforms = anchors.ToArray();
            provider.shaderPropertyName = "_JellyAnchor";
            var controller = tissue.AddComponent<CVRRenderController>();
            // The anchor first, then the passes, in the order the client runs them.
            controller.cvrRenderComponents.Add(provider);
            var steps = new List<CVRBlitter>();
            var fed = new List<Material> { stepMat, stepMat2 };
            for (int i = 0; i < Passes; i++)
            {
                var blitter = tissue.AddComponent<CVRBlitter>();
                steps.Add(blitter);
                // Into the twin and back: a texture in and out the same would make the client
                // render into a temporary and copy it back, every pass.
                blitter.originTexture = i == 0 ? state : twin;
                blitter.destinationTexture = i == 0 ? twin : state;
                blitter.blitMaterial = i == 0 ? stepMat : stepMat2;
                blitter.destinationTextures = new RenderTexture[0];
                controller.cvrRenderComponents.Add(blitter);
            }
            // Last, the rider's texels and the far switch out of the stepped state, for the parser.
            CVRTexturePropertyParser parser = null;
            if (withRider || offFar)
            {
                var where = Save(Strip(6), folder + "/Jelly_Points.renderTexture");
                var pointsMat = StepMaterial(step, n, 2, spacing);
                pointsMat.SetFloat("_Regions", regions);
                pointsMat = Save(pointsMat, folder + "/Jelly_Points.mat");
                fed.Add(pointsMat);
                var pointer = tissue.AddComponent<CVRBlitter>();
                pointer.originTexture = state;
                pointer.destinationTexture = where;
                pointer.blitMaterial = pointsMat;
                pointer.destinationTextures = new RenderTexture[0];
                controller.cvrRenderComponents.Add(pointer);
                parser = tissue.AddComponent<CVRTexturePropertyParser>();
                parser.textureType = CVRTexturePropertyParser.TextureType.LocalTexture;
                parser.texture = where;
                // The far switch: the client binds any public property, so the third texel turns both
                // steps' blitters off (a bool, set at 0.5 and up) far from the viewer, and the pass
                // that writes it, which stays on, turns them back on.
                if (offFar)
                    foreach (var blitter in steps)
                        parser.tasks.Add(new CVRTexturePropertyParserTask
                        {
                            x = 2,
                            y = 0,
                            channel = CVRTexturePropertyParserTask.Channel.r,
                            minValue = 0f,
                            maxValue = 1f,
                            target = tissue,
                            component = blitter,
                            propertyName = "enabled",
                            typeIndex = 2,
                        });
            }
            provider.materials = fed.ToArray();
            return new Built { root = root, tissue = tissue, anchors = anchors, provider = provider, controller = controller, state = state, parser = parser, step = step };
        }

        static bool Write(string path, string text)
        {
            if (File.Exists(path) && File.ReadAllText(path) == text) return false;
            File.WriteAllText(path, text);
            return true;
        }


        // The row after the lattice's: the hands in reach, the rider and the far switch. Its rows
        // padded so each region is a whole number of eight: a GPU runs pixels in blocks of rows,
        // and a block with the side row and particles in it ran both their work one after the other.
        internal static int Side(Vector3Int n) => ((n.x * n.y * n.z + Row - 1) / Row + 8) / 8 * 8 - 1;

        internal static RenderTexture State(Vector3Int n, int regions = 1)
        {
            // Float, so positions keep their digits; point and no mips, so a texel is only ever itself.
            return new RenderTexture(Readout + ProbesMax, (Side(n) + 1) * regions, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
            };
        }

        // A row of texels for a pass's few answers.
        internal static RenderTexture Strip(int width) => new RenderTexture(width, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false,
        };

        internal static Material StepMaterial(Shader step, Vector3Int n, int pass, float spacing)
        {
            var m = new Material(step);
            m.SetVector("_Lattice", new Vector4(n.x, n.y, n.z, spacing));
            m.SetFloat("_Pass", pass);
            // The first pass looks for the hands in reach, the third for whatever wakes a rest.
            if (pass == 0) m.EnableKeyword("JELLY_SCAN");
            if (pass == 2) m.EnableKeyword("JELLY_POINTS");
            // The second pass leaves the first's side row out, which it never runs.
            if (pass == 1) m.EnableKeyword("JELLY_LATER");
            return m;
        }

        // Kept where it already exists, its values brought up to date in place: an asset deleted and
        // made again at the same path in one session leaves the scene's jelly pointing at a dead
        // object (Missing), and copying a whole new object over it does the same.
        internal static T Save<T>(T asset, string path) where T : Object
        {
            var old = AssetDatabase.LoadAssetAtPath<T>(path);
            if (old == null)
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(asset, path);
                return AssetDatabase.LoadAssetAtPath<T>(path);
            }
            if (old is Material m && asset is Material fresh)
            {
                m.shader = fresh.shader;
                m.CopyPropertiesFromMaterial(fresh);
                EditorUtility.SetDirty(m);
            }
            if (old is RenderTexture rt && asset is RenderTexture want && (rt.width != want.width || rt.height != want.height || rt.format != want.format))
            {
                rt.Release();
                rt.width = want.width;
                rt.height = want.height;
                rt.format = want.format;
                EditorUtility.SetDirty(rt);
            }
            // A surface rebuilt in place, so instances keep pointing at it: its uvs name the region.
            if (old is Mesh keep && asset is Mesh made)
            {
                keep.Clear();
                keep.vertices = made.vertices;
                keep.uv = made.uv;
                keep.uv2 = made.uv2;
                keep.triangles = made.triangles;
                keep.bounds = made.bounds;
                EditorUtility.SetDirty(keep);
            }
            return old;
        }

        internal const string StepShader = @"// The jelly's step. Written by YAPS; edits here are overwritten.
//
// The frame's two passes, from one texture into its twin and back. Left half: where each particle
// is, w the _Time.y of the last pass (negative while no anchor arrives). Right half: where it was a
// pass before, w the h that pass took, so a velocity is only ever read off the two. The row after
// the lattice: the hands in reach (texels 0 to 15), the rider (16, 17), the far switch and
// whether it sleeps (18), how long it has stood still (19), where the anchor was the last time it
// stepped (20 to 22), the rider a frame before (23, 24), the step held with the smoothed frame
// time (25), the anchor the second pass stepped on (26 to 29: its axes with its scale, then its
// place, w 1), for a body's own shader to find the lattice by (yaps_jelly.cginc), and the region's
// mass, as one piece (30, its centre, w the _Time.y it stepped; 31, where it was, w that step), and
// whether any probe reads pushed (87), and the anchor the first pass stepped on, for the second's
// particles (88 to 90, its rows).
// Below 72 fps each pass takes a step. Above it the first only works out where each particle is
// headed and the second fits, so each neighbour is read once. The first pass also looks for the
// hands, so the second pushes against where they are this frame; below 72 fps the first pass's own
// step pushes against where they were the frame before.
Shader ""Hidden/AvatarBridge/JellyStep""
{
    Properties
    {
        _MainTex (""Last pass's state"", 2D) = ""black"" {}
        _Lattice (""Particles across, up and deep; spacing in metres"", Vector) = (16, 12, 16, 0.0133)
        _Stiff (""Pull toward the neighbourhood's shape over a 180th of a second"", Range(0, 1)) = 0.5
        _Hz (""Wobble a second, toward its rest on the chest"", Float) = 3
        _Damp (""Damping a second"", Float) = 4
        _Viscous (""Damping a second of what the tissue does apart from its home's own move: as _Damp a jelly, far more flesh"", Float) = 4
        _Hand (""Hand radius at a 1.6 m viewpoint"", Float) = 0.05
        _Far (""Hangs rigid past this many metres from the viewer's head"", Float) = 4
        _Behind (""Hangs rigid more than this many degrees off where the viewer's camera looks, 0 never"", Float) = 100
        _Near (""Never hangs rigid for being out of view within this many metres of the viewer's camera"", Float) = 2
        _Half (""Steps at _Rate.y past this many metres from the viewer's head, short of _Far; 0 never"", Float) = 2
        // Every frame: the client reads the switch back late and one read at a time, so at 45 a
        // second and 230 fps the steps came in bursts, the draw stuttered and it never slept.
        _Rate (""Steps a second near the viewer's head (x) and past _Half (y), the draw carrying the jiggle on between; 0 every frame"", Vector) = (0, 0, 0, 0)
        _Pass (""Which of the frame's passes this is: 2 copies the rider out"", Float) = 0
        _Slap (""Slaps: metres a second to fire, depth per m/s, deepest, life in seconds"", Vector) = (1.2, 0.01, 0.03, 1.5)
        _Landing (""A landing or a yank rings the tissue past this change of its speed in a step, metres a second"", Float) = 1
        _FixedDt (""Fixed frame time, 0 for the real one"", Float) = 0
        _Stress (""The cost meter's load: steps only in odd phases"", Float) = 0
        _Rider (""Steps the rider's texels, for the parser and the sleep"", Float) = 1
        _Sleep (""Rests after standing still this many seconds, untouched and unmoved; 0 never"", Float) = 0.5
        _Rest (""Worn: stops the steps after lying still this many seconds with no hand near; 0 never"", Float) = 0.5
        _Regions (""How many regions the state packs"", Float) = 1
        _Lift (""1 lifts its rest by the sag gravity gives, along the anchor's up: modelled tissue hangs already; a free mass is lifted always"", Float) = 0
        _Gravity (""Share of gravity the tissue feels"", Float) = 1
        _MassHz (""The whole region as one piece, swinging on its root: wobbles a second, 0 off"", Float) = 0
        _MassCarried (""1: the mass rides the anchor's own move, a bone its cloth swings, and only hands move it on it"", Float) = 0
        _MassDamp (""Its damping a second"", Float) = 4
        _Give (""Share of how far a hand reaches into it that moves the whole; the particles dent the rest"", Range(0, 1)) = 0.5
        _MassRadius (""Its radius against hands, in the anchor's metres"", Float) = 0.1
        _Limit (""Furthest its centre swings off its rest, as a share of its length from the root"", Float) = 0.6
        _Taut (""How much a loaded region's swing stiffens and damps: fully strained, the spring 1 + _Taut times as stiff, the damping 1 + 3 _Taut times"", Float) = 1
        _JellyTwitch (""Which handed transform the animator moves off the first hang to kick the mass; 0 none"", Float) = 0
        _Motion (""Stretch in motion, the squash's own: x per reach of swing, y swing ignored below this share of the reach, z longest stretch; x 0 none"", Vector) = (0, 0, 1, 0)
        _Pair (""1: regions 0 and 1 are a pair, each keeping off the other"", Float) = 0
        _PairKeep (""Nearest the pair's centres come, as a share of their distance at rest"", Float) = 0.85
        _Squish (""Share of each neighbourhood's own squash and stretch it keeps, at constant volume; 0 rigid"", Range(0, 1)) = 0
        _PairOffset (""The second region's place in the first's frame at rest, both turned alike"", Vector) = (0, 0, 0, 0)
        _PairReach (""Each region's half width toward the other at rest, and the gap between them then; x 0 no plane"", Vector) = (0, 0, 0, 0)
        _PairSkin (""Each region's skin's reach toward the other off its centre at rest; x 0 none"", Vector) = (0, 0, 0, 0)
        _JellyOwn0 (""The wearer's own capsules' radii: the left upper arm, forearm, hand and thigh; 0 none"", Vector) = (0, 0, 0, 0)
        _JellyOwn1 (""The same for the right"", Vector) = (0, 0, 0, 0)
        _JellyCarrier (""Which of those eight each region rides, plus one, left out of its own: x the first region's, y the second's; 0 none"", Vector) = (0, 0, 0, 0)
        _JellyCoreA0 (""The first region's held core, one end of its bone on the anchor, w its radius; w 0 holds the back face"", Vector) = (0, 0, 0, 0)
        _JellyCoreB0 (""Its other end"", Vector) = (0, 0, 0, 0)
        _JellyCoreA1 (""The same for the second region"", Vector) = (0, 0, 0, 0)
        _JellyCoreB1 (""Its other end"", Vector) = (0, 0, 0, 0)
        _JellyCol0 (""The avatar's own colliders' radii, those pressing; 0 none"", Vector) = (0, 0, 0, 0)
        _JellyCol1 (""The next four"", Vector) = (0, 0, 0, 0)
        _JellyBox0 (""The first region's skin on its anchor: half width, half height, depth, w 1 with its depth map; 0 the lattice's box"", Vector) = (0, 0, 0, 0)
        _JellyBox1 (""The same for the second"", Vector) = (0, 0, 0, 0)
        _JellyDepth (""How deep each region's skin lies under each face of its box, YapsJelly.DepthGrid square a face"", 2D) = ""black"" {}
        _JellyOffsetAt0 (""The first region's place on the bone it rides, in the bone's units, w its scale on it; w 0 rides none"", Vector) = (0, 0, 0, 0)
        _JellyOffsetTurn0 (""The first region's turn on that bone"", Vector) = (0, 0, 0, 1)
        _JellyOffsetAt1 (""The second region's place on its bone"", Vector) = (0, 0, 0, 0)
        _JellyOffsetTurn1 (""The second region's turn on its bone"", Vector) = (0, 0, 0, 1)
        _JellySkinStride (""The skinned helper's vertex stride, 0 for none: the bones as the cloth swings them"", Float) = 0
        _JellyProbes (""MagicaCloth probes a region has in the skinned helper, after its frames, 0 none"", Float) = 0
        _JellyProbeReach (""How far off the skin a collider pushes a probe, the probes' radius, metres"", Float) = 0
        _JellySolid (""Regions whose probes are balls the world's solids push, a bit each: felt as flat faces, not hands"", Float) = 0
        _JellySolidFrom (""In those regions, the first probe that is a ball; the ones before it the cloth's"", Float) = 0
        _JellyProbePatch (""How far across a world probe's face reaches, metres"", Float) = 0
        _Deepest (""How deep a hand presses before the skin holds it, metres; past the tissue's middle it lets go"", Float) = 0.12
        _GripSlip (""How far a gripped spot drags behind a fist stroking along the skin before it slides with it, metres; 0 it never slides"", Float) = 0
        _JellyArrays (""A component a region: 0 leaves the player arrays' hands out, its probes alone"", Vector) = (1, 1, 1, 1)
        _Floor (""A board under the tissue on its anchor: x 1 for one, y its height there in metres"", Vector) = (0, 0, 0, 0)
        _Solid (""Share of the whole's own squash its homes take at constant volume, so squeezed it bulges where nothing holds it; 0 none"", Range(0, 0.95)) = 0
        _Squeeze (""Share of other players' palms' reach into the whole it flattens along their line at constant volume; 0 none"", Range(0, 1)) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 4.5
            // A blit into the state, never an eye: the stereo copies tripled the upload's compile.
            #pragma skip_variants UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON STEREO_CUBEMAP_RENDER_ON
            // shader_feature: an upload builds only the variants its passes use. multi_compile built
            // all eight, each at the compiler's time limit.
            // Only the first pass looks for hands, so only it carries every player's positions.
            #pragma shader_feature_local __ JELLY_SCAN
            // Only a prop sleeps; worn, it is compiled out.
            #pragma shader_feature_local __ JELLY_SLEEP
            // The third pass, which wakes a resting jelly, reads the players' hands too.
            #pragma shader_feature_local __ JELLY_POINTS
            // The second pass, compiled without the first's side row.
            #pragma shader_feature_local __ JELLY_LATER
            #include ""UnityCG.cginc""
            #include ""JellyCommon.cginc""

            float _Stiff, _Hz, _Hand, _Far, _Behind, _Near, _Half, _Pass, _FixedDt, _Stress, _Sleep, _Lift, _Rider, _Rest, _Regions;
            float _MassHz, _MassCarried, _MassDamp, _Give, _MassRadius, _Limit, _Pair, _PairKeep, _Squish, _Viscous, _Solid, _Squeeze, _Taut, _JellyTwitch;
            float4 _Slap, _Rate;
            float _Landing;
            // The frame's time, for how fast the homes move.
            static float FrameDt;
            // The anchor snapped to a new pose this frame: the tissue rides the jump.
            static bool Snapped = false;
            float4 _Motion, _PairOffset, _PairReach, _PairSkin, _JellyOwn0, _JellyOwn1, _JellyCol0, _JellyCol1, _JellyBox0, _JellyBox1, _JellyOffsetAt0, _JellyOffsetTurn0, _JellyOffsetAt1, _JellyOffsetTurn1;
            // A helper mesh skinned four points to each region's bone and four to where it hangs on
            // the chest, as the squash's slots, bound as the provider's copy of its skinned buffer.
            ByteAddressBuffer _JellySkin;
            Texture2D<float> _JellyDepth;
            float _JellySkinStride, _JellyProbes, _JellyProbeReach, _Deepest, _GripSlip, _JellySolid, _JellySolidFrom, _JellyProbePatch;
            float4 _JellyArrays, _JellyCarrier, _JellyCoreA0, _JellyCoreB0, _JellyCoreA1, _JellyCoreB1;
            float4 _Floor;
            // The most probes a region has, YapsJelly.ProbesMax, and their readout's first column on
            // the side row, YapsJelly.Readout: columns of their own past the particles'.
            #define PROBES 64
            #define READOUT 128
            // The depth map's spots along a face, YapsJelly.DepthGrid.
            #define DEPTH_GRID 16
            // A world probe's ball radius, as the builder's Solid() makes it.
            #define BALL 0.01

            // The inverse of a turn, an even scale and a place.
            float4x4 Unrigid(float4x4 m)
            {
                float3x3 r = transpose((float3x3) m) / dot(m._m00_m10_m20, m._m00_m10_m20);
                float3 t = -mul(r, m._m03_m13_m23);
                return float4x4(float4(r[0], t.x), float4(r[1], t.y), float4(r[2], t.z), float4(0, 0, 0, 1));
            }

            // A frame through skinning, from four points at base: its place, then a tenth along each
            // of its axes, in the helper's own space (skinned with no root bone, not the world's).
            // false where the buffer reads empty or spoilt.
            bool Skinned(uint base, out float4x4 f)
            {
                f = 0;
                uint stride = (uint) _JellySkinStride;
                float3 p[4];
                [unroll] for (uint i = 0; i < 4; i++)
                {
                    uint3 raw = _JellySkin.Load3((base + i) * stride);
                    // NaN and infinity by their bits: the compiler folds a float test for NaN away.
                    if (any((raw & 0x7f800000) == 0x7f800000)) return false;
                    p[i] = asfloat(raw);
                }
                float3 x = (p[1] - p[0]) * 10, y = (p[2] - p[0]) * 10, z = (p[3] - p[0]) * 10;
                if (!(dot(x, x) > 1e-8)) return false;
                f = float4x4(x.x, y.x, z.x, p[0].x, x.y, y.y, z.y, p[0].y, x.z, y.z, z.z, p[0].z, 0, 0, 0, 1);
                return true;
            }

            // Region r's anchor. One riding a bone its cloth swings is handed the bone, and its place
            // on it is here: parented under the bone, it joined the cloth's chain as a bone of its own.
            // The bone as the cloth swings it comes only through skinning: the provider reads bones
            // in a script LateUpdate, after the cloth put them back to rest and before it writes them,
            // so every region it handed stayed still on the chest. The region's frame on its bone
            // against the same frame where it hangs, both as last skinned, is the swing, a frame late,
            // put on the chest as it is now, so the body's own move is not late.
            float4x4 AnchorOf(uint r)
            {
                float4x4 a = _JellyAnchor[min(r, 3)];
                float4 at = r == 0 ? _JellyOffsetAt0 : _JellyOffsetAt1, q = r == 0 ? _JellyOffsetTurn0 : _JellyOffsetTurn1;
                if (r > 1 || !(at.w > 0)) return a;
                float3x3 t = Turn(q) * at.w;
                a = mul(a, float4x4(float4(t[0], at.x), float4(t[1], at.y), float4(t[2], at.z), float4(0, 0, 0, 1)));
                // Both in the helper's space, which their difference does not see. Kept only at its own
                // scale and within half a metre: the squash's skinned slots reached remote copies
                // metres off.
                float4x4 on, still;
                [branch] if (_JellySkinStride > 0 && Skinned(8 * r, on) && Skinned(8 * r + 4, still))
                {
                    float4x4 swung = mul(Unrigid(still), on);
                    if (abs(length(swung._m00_m10_m20) - 1) < 0.2 && length(swung._m03_m13_m23) < 0.5)
                        return mul(_JellyAnchor[2 + r], swung);
                }
                return a;
            }

            // The twitch: a transform the avatar's animator moves off the first hang, kicking both
            // regions' mass as far as it moved. 0 at rest.
            float3 Twitch() { return _JellyTwitch > 0 ? _JellyAnchor[(uint) _JellyTwitch]._m03_m13_m23 - _JellyAnchor[2]._m03_m13_m23 : 0; }

            // Whether region r has world balls, and the first of its probes that is one: the ones
            // before it are the cloth's, PROBES for a region of cloth alone.
            bool SolidRegion(uint r) { return (((uint) round(_JellySolid) >> r) & 1) != 0; }
            uint WorldFrom(uint r) { return SolidRegion(r) ? (uint) _JellySolidFrom : PROBES; }

            // Whether any of a side's probes read pushed last frame: the side row's 87, which the
            // first pass works out beside the readout, so a particle reads one texel, not every probe.
            bool Pushed(uint side) { return _MainTex.Load(int3(87, side, 0)).x > 0.5; }

            // MagicaCloth's probes on region r's hang, as the build put them: back takes the skinned
            // helper's space to the world through the hang as last skinned, put on the hang as it
            // is now. false where there are none or the hang reads spoilt.
            bool Probing(uint r, out float4x4 back)
            {
                float4x4 still = 0;
                bool ok = false;
                back = 0;
                // Branched: an avatar with no probes would still take the hang's sixteen reads.
                [branch] if (_JellyProbes > 0 && _JellySkinStride > 0)
                {
                    ok = Skinned(8 * r + 4, still);
                    back = ok ? mul(_JellyAnchor[2 + r], Unrigid(still)) : 0;
                }
                return ok;
            }

            // Probe k of region r's: where it was pushed to and which way, and how far, 0 for a spoilt
            // read. Each is two points in the helper from 16, itself then its rest on the hang.
            float Probe(uint r, uint k, float4x4 back, out float3 at, out float3 way, bool sided = true)
            {
                uint stride = (uint) _JellySkinStride, i0 = 16 + 2 * (r * min((uint) _JellyProbes, (uint) PROBES) + k);
                uint3 rp = _JellySkin.Load3(i0 * stride), rr = _JellySkin.Load3((i0 + 1) * stride);
                float3 d = mul((float3x3) back, asfloat(rp) - asfloat(rr));
                float gone = length(d);
                at = mul(back, float4(asfloat(rp), 1)).xyz;
                way = gone > 1e-6 ? d / gone : 0;
                // A ball the world pushes is only ever pushed in: one out past its rest is caught on
                // a solid's far side, as a thin pane passed over it leaves one, and read as a press.
                // A select, not a branch: per probe and unrolled, the branches timed the compiler out.
                float3 mid = mul(_JellyAnchor[2 + r], float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1)).xyz;
                if (sided && k >= WorldFrom(r) && !(dot(way, mid - (at - d)) > 0)) gone = 0;
                return any((rp & 0x7f800000) == 0x7f800000) || any((rr & 0x7f800000) == 0x7f800000) || !(gone < 2 * _JellyProbeReach + 0.1) ? 0 : gone;
            }

            // A collider the probes felt: its ball and radius, where on the skin it pressed (its probes'
            // rests weighted by their pushes), how far its leading probe was pushed (0 for none), and
            // which probe that was.
            struct Press { float3 ball; float radius; float3 rest; float deep; uint k; };

            // Probe k of region r's push as Felt reads it, 0 under 2 mm, its tip and its rest. Pushed
            // past most of a reach is swept along by a sliding collider, round its side and out past
            // it, not pressed: one read 12 cm for a ball 3 cm in and rebuilt it 5 cm too deep in a
            // frame. Held to 0.8 of a reach, a press reads at most about 3 cm deep. Solving from where
            // the tips sit instead jumped up to 8 cm a frame as roots flipped.
            float Push(uint r, uint k, float4x4 back, float reach, out float3 tip, out float3 rest)
            {
                float3 way;
                float g = Probe(r, k, back, tip, way);
                rest = tip - way * g;
                return g > 0.002 ? min(g, 0.8 * reach) : 0;
            }

            // The sizes a felt collider is tried at, a fingertip's to a hand's.
            static const float Sizes[8] = { 0.005, 0.0075, 0.01, 0.015, 0.02, 0.03, 0.04, 0.05 };
            #define MEMBERS 8
            #define FREES 16

            // Region r's probes as at most two colliders, so a slot and a ring are each a collider's,
            // not a probe's: within two hands, two probes 8 cm apart took two slots, and the one a
            // collider met slantwise swings along its bone twice as fast as it came, a slow press
            // read as a slap. The furthest pushed leads, and the furthest pushed whose rest is past
            // 1.25 reaches of its rest leads a second; each takes the pushed probes resting within
            // 1.2 reaches of its own. ponytail: two colliders a region; a third reads as one of those.
            // Each one's place over the rests of its probes, weighted by their pushes, a reach less
            // its leader's push out along the skin: a probe pushed g had the collider's skin, a probe
            // radius out from it, come g past its rest. By the tips, which a collider sweeps round it,
            // two balls 12 cm apart shared the probes and each slot sat 3 to 5 cm off its ball; by
            // the rests, 1.5 cm. Put out along one probe's normal, a ball over a face's middle sat 4 cm
            // too far out to dent at all.
            // Its size from the probes round it: each it pushed has its tip a collider radius and a
            // probe radius from the collider's middle, each it left alone at least that far from its
            // rest. Tried at each of Sizes, the middle settled on the pushed tips, the size that fits
            // them best keeping clear of the rest. Every collider was a hand's 5 cm ball before, a
            // finger's dent as wide as a palm's; modelled offline, fingers came out 10 to 15 mm, 15 and
            // 30 mm balls their own size, palms 40. A thin collider's depth between probes is not
            // there to read: a probe on the skin slides round its side, so the depth is the leader's.
            void Felt(uint r, uint upto, float4x4 back, out Press p[2])
            {
                // Out of the body from the middle of the lattice at rest, through each probe's rest.
                float3 mid = mul(_JellyAnchor[2 + r], float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1)).xyz;
                float reach = _Hand + _JellyProbeReach;
                uint n = min(min((uint) _JellyProbes, upto), (uint) PROBES);
                p[0] = (Press) 0;
                p[1] = (Press) 0;
                float best = 0, next = 0;
                float3 from = 0, from2 = 0;
                uint k0 = 0, k1 = 0;
                [loop] for (uint k = 0; k < n; k++)
                {
                    float3 tip, rest;
                    float g = Push(r, k, back, reach, tip, rest);
                    bool more = g > best;
                    from = more ? rest : from;
                    k0 = more ? k : k0;
                    best = max(best, g);
                }
                // Untouched, as nearly always, none of this is looked at: the slaps run it every frame.
                [branch] if (best > 0)
                {
                    [loop] for (uint k = 0; k < n; k++)
                    {
                        float3 tip, rest;
                        float g = Push(r, k, back, reach, tip, rest);
                        bool far = g > next && distance(rest, from) >= 1.25 * reach;
                        from2 = far ? rest : from2;
                        k1 = far ? k : k1;
                        next = far ? g : next;
                    }
                    [loop] for (uint i = 0; i < 2; i++)
                    {
                        float lg = i == 0 ? best : next;
                        float3 lead = i == 0 ? from : from2;
                        [branch] if (lg > 0)
                        {
                            // ponytail: the first MEMBERS pushed in probe order and FREES untouched,
                            // enough for a hand's; a bigger press sizes from those.
                            float3 tips[MEMBERS], frees[FREES];
                            float pull[MEMBERS];
                            uint held = 0, open = 0;
                            float w = 0;
                            float3 rest = 0;
                            [loop] for (uint k = 0; k < n; k++)
                            {
                                float3 tip, kr;
                                float g = Push(r, k, back, reach, tip, kr);
                                float apart = distance(kr, lead);
                                [branch] if (g > 0 && apart < 1.2 * reach)
                                {
                                    w += g;
                                    rest += kr * g;
                                    [branch] if (held < MEMBERS) { tips[held] = tip; pull[held] = g * g; held++; }
                                }
                                else if (g == 0 && apart < 2 * reach && open < FREES) { frees[open] = kr; open++; }
                            }
                            rest /= max(w, 1e-6);
                            float3 outward = normalize(rest - mid), centre = 0;
                            float sum = 0;
                            [loop] for (uint q = 0; q < held; q++) { centre += tips[q] * pull[q]; sum += pull[q]; }
                            centre /= max(sum, 1e-12);
                            float size = _Hand, least = 1e9;
                            [loop] for (uint s = 0; s < 8; s++)
                            {
                                float tried = min(Sizes[s], _Hand), d = tried + _JellyProbeReach;
                                float3 c = centre + outward * d;
                                [loop] for (uint it = 0; it < 6; it++)
                                {
                                    float3 to = 0;
                                    [loop] for (uint q = 0; q < held; q++)
                                    {
                                        float3 v = c - tips[q];
                                        to += (tips[q] + v * (d / max(length(v), 1e-6))) * pull[q];
                                    }
                                    c = to / max(sum, 1e-12);
                                }
                                float miss = 0, into = 0, worst = 0;
                                [loop] for (uint q = 0; q < held; q++) { float e = distance(c, tips[q]) - d; miss += e * e; }
                                [loop] for (uint q = 0; q < open; q++) { float h = max(0, d - distance(c, frees[q])); into += h * h; worst = max(worst, h); }
                                float cost = miss / max(held, 1) + 4 * into / max(open, 1) + 4 * worst * worst;
                                size = cost < least ? tried : size;
                                least = min(least, cost);
                            }
                            p[i].ball = rest + outward * (size + _JellyProbeReach - lg);
                            p[i].radius = size;
                            p[i].rest = rest;
                            p[i].deep = lg;
                            p[i].k = i == 0 ? k0 : k1;
                        }
                    }
                }
            }

            // Worked out once a pixel at the top of the step, as each is read many times over: through
            // skinning each is sixteen reads, and inlined at every caller the anchors were a fifth of
            // the step. Its own region's, Mine; the other's, Theirs, only where it is read. MineOf
            // stays its region where Region changes to pick a region's box.
            static float4x4 Mine, Theirs;
            static uint Region, MineOf;
            float4x4 Anchor(uint r) { return r == MineOf ? Mine : Theirs; }
            // Its own region's probes' frame, as Probing gives it: once a pixel too.
            static float4x4 Back;
            static bool Probed;

            // A world point in an anchor's rest frame, metres from it along its axes.
            float3 Into(float4x4 a, float3 x)
            {
                float3 d = x - a._m03_m13_m23, ax = a._m00_m10_m20;
                return float3(dot(d, ax), dot(d, a._m01_m11_m21), dot(d, a._m02_m12_m22)) / dot(ax, ax);
            }

            // A kept hand from its slot's texels a (wrist, radius) and b (an open hand's tip, 10 plus the
            // gesture, and 10 a millimetre the skin holds it back), as the squash reads one
            // (yaps_dent.cginc): the palm from a to pb, ending at the knuckles as it closes to a fist,
            // and a finger out, thin, from fa to fb of radius rf, 0
            // for none. A pointing finger read as a whole open hand shoved the breast away from it.
            void Hand(float4 a, float4 b, out float3 pb, out float3 fa, out float3 fb, out float rf)
            {
                float3 seg = b.xyz - a.xyz;
                float len = length(seg), g = fmod(b.w - 10, 10), r = a.w;
                pb = a.xyz;
                fa = fb = a.xyz;
                rf = 0;
                if (len < 1e-6) return;
                float3 dir = seg / len;
                float tall = (len + r) / 0.11, fist = g > 1.5 ? 1 : saturate(g);
                pb = a.xyz + dir * max(lerp(0.11, 0.06, fist) * tall - r, 0);
                rf = g > 2.5 ? (g > 4.5 && g < 5.5 ? 0.35 : 0.2) * r : 0;
                fa = a.xyz + dir * (0.06 * tall);
                fb = a.xyz + dir * max(0.11 * tall - rf, 0.06 * tall);
            }

            // Whether a slot's hand is a closed fist: its gesture's 0 to 1 past three quarters.
            bool Fist(float4 b) { float g = fmod(b.w - 10, 10); return b.w > 0.5 && g > 0.75 && g < 1.5; }

            // Where a fist meets what it closes on: its knuckles' far side.
            float3 Knuckles(float4 a, float4 b)
            {
                float3 pb, fa, fb;
                float rf;
                Hand(a, b, pb, fa, fb, rf);
                float3 seg = b.xyz - a.xyz;
                return pb + (dot(seg, seg) > 1e-12 ? normalize(seg) : 0) * a.w;
            }

            // Slot e's grip as last frame left it (texel 112 + e): where on the anchor its fist closed
            // on the skin, in at; and how far the hand has pulled that since, on the anchor, capped at
            // half the tissue's depth, w 1; raw, the pull uncapped. 0 with no grip or its hand gone.
            // A grip's spot on the anchor once its fist has moved to k: across the skin, the way back
            // up the hand from its tip, it trails the fist by _GripSlip at most, so a fist stroking
            // along the skin carries the grip with it and the skin drags behind; out of the skin it
            // stays, so pulled out too far it still lets go. 0, the squash settings' Grip slides off,
            // it stays where the fist closed. The way out from the lattice's middle tilted on a flat
            // front, so a stroke read half as a pull and slid half as far.
            float3 Slid(float3 at, float3 k, float3 up)
            {
                up = dot(up, up) > 1e-12 ? normalize(up) : at - float3(0, 0, 0.5 * (Count().z - 1) * _Lattice.w);
                up = dot(up, up) > 1e-12 ? normalize(up) : float3(0, 0, 1);
                float3 d = k - at, across = d - up * dot(d, up);
                float l = length(across);
                return _GripSlip > 0 && l > _GripSlip ? at + across * (1 - _GripSlip / l) : at;
            }

            float4 Pulled(uint e, float4x4 m, uint side, out float3 at, out float raw)
            {
                float4 g = _MainTex.Load(int3(112 + e, side, 0)), a = _MainTex.Load(int3(2 * e, side, 0)), b = _MainTex.Load(int3(2 * e + 1, side, 0));
                at = g.xyz;
                raw = 0;
                if (!(g.w > 0 && b.w > 0.5)) return 0;
                float3 d = Into(m, Knuckles(a, b)) - g.xyz;
                raw = length(d);
                return float4(d * min(1, 0.5 * (Count().z - 1) * _Lattice.w / max(raw, 1e-6)), 1);
            }

            // next on ahead of a capsule from a to b of radius r toward centre, the way a hand
            // presses: pushed out of the hand's nearest side, a particle a little off its axis went
            // round it and the hand passed between them, 35 mm of tissue moved for 130 mm in. One
            // behind the hand's line is left be, as a hand pushes and never pulls. Pushed on through,
            // a particle its spring had flung past the line came out the far side and was flung back,
            // a deep press at 60 frames a second shaking 10 cm a frame; pushed back out behind, the
            // ones a withdrawing hand had passed were dragged out with it, 11 cm past their rest.
            float3 Plow(float3 next, float3 a, float3 b, float r, float3 centre)
            {
                float3 seg = b - a, in_ = centre - (a + 0.5 * seg);
                float3 off = next - (a + seg * saturate(dot(next - a, seg) / max(dot(seg, seg), 1e-9)));
                float d2 = dot(off, off);
                if (d2 >= r * r || !(dot(in_, in_) > 1e-8)) return next;
                float3 way = normalize(in_);
                float along = dot(off, way);
                if (along < 0) return next;
                return next + way * (sqrt(max(along * along - d2 + r * r, 0)) - along);
            }

            // next out of a capsule the nearest way, as the wearer's own body pushes. Plowed toward
            // the centre like a hand, a particle whose rest lay behind a held arm slid round it and
            // was thrown back over the top, 50 mm a frame, round and round. One behind the line from
            // the limb to centre is left be, as hands do: pushed out either way, an arm moving off
            // dragged the tissue it had passed with it, and the breast stuck to it.
            float3 Out(float3 next, float3 a, float3 b, float r, float3 centre)
            {
                float3 seg = b - a;
                float3 off = next - (a + seg * saturate(dot(next - a, seg) / max(dot(seg, seg), 1e-9)));
                float d2 = dot(off, off);
                if (d2 >= r * r || d2 < 1e-12 || dot(off, centre - (a + 0.5 * seg)) < 0) return next;
                return next + off * (r * rsqrt(d2) - 1);
            }

            // The face of the tissue a hand came in by, a local axis, and the tissue's half thickness
            // across it: the face the wrist is over while it is outside; inside, for another player's
            // hand, the one it lies least deep past, of those not facing away from its arm (its
            // player's chest) with its deeper end a radius r short of the middle; else the face a
            // point 30 cm back along the arm is over. That point alone read a player's palm flat on a
            // breast's side, the arm from the front, as in by the front and held it 18 cm out in
            // front; the least deep alone read a hand pushed straight in past the middle as in by a
            // side, held it 15 cm out to it and tore 330 particles. The wearer's own hands keep the
            // point, the wearer's elbow being where the hand came from: by the least deep, a hand
            // pushed in from the front was held there and swung the whole 2 cm back and 2 cm out.
            float3 Facing(float3 wrist, float3 tip, float r, bool other, float4x4 m, float scale, out float3 mid, out float across)
            {
                float4 box = Region == 0 ? _JellyBox0 : _JellyBox1;
                float3 half = box.z > 0 ? float3(box.xy, 0.5 * box.z) : 0.5 * (float3) (Count() - 1) * _Lattice.w;
                mid = float3(0, 0, half.z);
                float3 wl = Into(m, wrist), tl = Into(m, tip), q = (wl - mid) / half, least = 1e9, way = 1;
                if (max(abs(q.x), max(abs(q.y), abs(q.z))) < 1 && dot(wl - tl, wl - tl) > 1e-12)
                {
                    // How far the deeper end lies past each face, a face more than 60 degrees off
                    // the arm left out.
                    float3 arm = normalize(wl - tl);
                    float3 inPlus = arm < -0.5 ? 1e9 : half - (min(wl, tl) - mid);
                    float3 inMinus = arm > 0.5 ? 1e9 : half + (max(wl, tl) - mid);
                    least = min(inPlus, inMinus);
                    way = inPlus <= inMinus ? 1 : -1;
                    least = other && least <= half - r / scale ? least : 1e9;
                    q = (Into(m, wrist + normalize(wrist - tip) * 0.3 * scale) - mid) / half;
                }
                float3 face = min(least.x, min(least.y, least.z)) < 1e8
                    ? (least.x <= least.y && least.x <= least.z ? float3(way.x, 0, 0) : least.y <= least.z ? float3(0, way.y, 0) : float3(0, 0, way.z))
                    : (abs(q.x) >= abs(q.y) && abs(q.x) >= abs(q.z) ? float3(sign(q.x), 0, 0) : abs(q.y) >= abs(q.z) ? float3(0, sign(q.y), 0) : float3(0, 0, sign(q.z)));
                across = dot(abs(face), half);
                return face;
            }

            // How deep the skin lies under face at l, a point on the anchor as Into gives it: the
            // region's depth map between its spots. 0 with no map, the face then taken for the skin.
            float Skin(float3 face, float3 l)
            {
                float4 box = Region == 0 ? _JellyBox0 : _JellyBox1;
                if (!(box.w > 0.5) || !(box.z > 0)) return 0;
                float3 half = float3(box.xy, 0.5 * box.z), q = (l - float3(0, 0, half.z)) / half;
                uint k = abs(face.x) > 0.5 ? 0 : abs(face.y) > 0.5 ? 1 : 2;
                float2 uv = k == 0 ? q.yz : k == 1 ? q.xz : q.xy;
                float2 g = saturate(0.5 * uv + 0.5) * (DEPTH_GRID - 1);
                int2 g0 = min((int2) g, DEPTH_GRID - 2);
                float2 t = g - g0;
                int row = (int) ((Region * 6 + 2 * k + (face.x + face.y + face.z < 0 ? 1 : 0)) * DEPTH_GRID) + g0.y;
                float a = _JellyDepth.Load(int3(g0.x, row, 0)), b = _JellyDepth.Load(int3(g0.x + 1, row, 0));
                float c = _JellyDepth.Load(int3(g0.x, row + 1, 0)), d = _JellyDepth.Load(int3(g0.x + 1, row + 1, 0));
                return lerp(lerp(a, b, t.x), lerp(c, d, t.x), t.y);
            }

            // A hand past _Deepest into the skin, or a third of the tissue, is held there, as skin
            // would stop a real one; in VR it goes on through, and once its tip is past the tissue's
            // middle it lets go over its radius, as a body a hand has passed into springs back.
            // Followed in, a hand 11 cm in dragged 44 particles over 10 cm and one 17 cm in 120, the
            // tissue torn, and again as it was pulled back out. hold is how far the skin keeps it
            // short of where it is, which the whole still feels (Unheld): skin that stops a hand is
            // pushed along by it, and with the mass held too a squeeze no longer met in the middle.
            // The face is judged with the hand at from: for another player's, where the same hand was
            // held a step before, just past the face it came in by however deep it goes. Judged where
            // it was, a finger pushed in deep from behind read as in by the top once past the middle,
            // was held 8.6 cm up and dented the skin above it, none under it. The tip has its own
            // radius rTip, a finger's when one is out: by the palm's, a finger 2.6 cm into a seat read
            // as at its 7.9 cm and was held there, the rest of a poke sinking into the skin.
            // Both depths are past the skin, which lies under the face by the region's depth map: past
            // the face, a poke on a seat used its whole 7.9 cm before the finger met the skin 8 cm under
            // it, and dented nothing. The middle it lets go at, and a third of the tissue, are the box's.
            void Held(inout float3 wrist, inout float3 tip, float r, float rTip, bool other, float3 fromWrist, float3 fromTip, float4x4 m, float scale, out float hold)
            {
                float3 mid;
                float across;
                float3 face = Facing(fromWrist, fromTip, r, other, m, scale, mid, across);
                hold = 0;
                if (!any(face) || !(r > 0)) return;
                float3 lw = Into(m, wrist), lt = Into(m, tip);
                float pastW = across - dot(lw - mid, face), pastT = across - dot(lt - mid, face), skin = Skin(face, lt);
                float inW = (pastW - Skin(face, lw)) * scale, inT = (pastT - skin) * scale;
                float deep = max(inW + r, inT + rTip);
                float most = min(_Deepest * scale, 2 * max(across - skin, 0) * scale / 3), go = saturate((max(pastW, pastT) - across) * scale / r);
                float back = max(deep - most, 0) + most * go;
                // Never more than it is held back: short of skin under the middle, deep is below 0.
                hold = clamp(back - deep * go, 0, back);
                float3 out_ = normalize(mul((float3x3) m, face));
                wrist += out_ * back;
                tip += out_ * back;
            }

            // A held hand back where it is, less only its letting go: what moves the whole.
            void Unheld(inout float3 wrist, inout float3 tip, float r, bool other, float hold, float4x4 m, float scale)
            {
                float3 mid;
                float across;
                float3 face = Facing(wrist, tip, r, other, m, scale, mid, across);
                float3 in_ = any(face) ? normalize(mul((float3x3) m, face)) * hold : 0;
                wrist -= in_;
                tip -= in_;
            }

            // Which particles ride the anchor: the back face, on the chest wall; on a limb, those within
            // the core's radius of its bone, as a thigh's flesh rides its bone all round.
            bool Pinned(int3 l)
            {
                if (!Cored) return l.z == 0;
                float4 a = Region == 0 ? _JellyCoreA0 : _JellyCoreA1, b = Region == 0 ? _JellyCoreB0 : _JellyCoreB1;
                float3 r = Rest(l), s = b.xyz - a.xyz;
                return distance(r, a.xyz + s * saturate(dot(r - a.xyz, s) / max(dot(s, s), 1e-12))) < a.w;
            }

            // The wearer's own capsule c of the sixteen, end to end after the four anchors: the left
            // upper arm, forearm, hand and thigh, then the right's, bone to bone; then the avatar's
            // colliders, each between its two ends. r 0 for none.
            void Own(uint c, float scale, out float3 a, out float3 b, out float r)
            {
                uint s = c < 8 ? 4 + (c >> 2) * 6 + ((c & 3) < 3 ? (c & 3) : 4) : 2 * c;
                a = _JellyAnchor[s]._m03_m13_m23;
                b = _JellyAnchor[s + 1]._m03_m13_m23;
                r = (c < 4 ? _JellyOwn0[c & 3] : c < 8 ? _JellyOwn1[c & 3] : c < 12 ? _JellyCol0[c & 3] : _JellyCol1[c & 3]) * scale;
                // The limb a region rides lies through it and moves with it, so it presses only the other.
                if (c + 1 == (uint) round(Region == 0 ? _JellyCarrier.x : _JellyCarrier.y)) r = 0;
            }

            // How much of the pair's plane holds, from the turn between the two frames, the partner's
            // as its row last wrote it. Built side by side and turned alike, the plane is where they
            // press; a limb turned across the other, as a thigh crossed over its partner some 55 to 75
            // degrees off it, lay past the plane with nothing there to meet and was pressed into a
            // blade. All of it to 40 degrees, none from 55; knees pressed together turn 31.
            float Meet(float4x4 m, uint ps)
            {
                float4 x = _MainTex.Load(int3(26, ps, 0)), y = _MainTex.Load(int3(27, ps, 0)), z = _MainTex.Load(int3(28, ps, 0));
                if (!(x.w > 0.5)) return 1;
                float t = dot(normalize(m._m00_m10_m20), normalize(x.xyz)) + dot(normalize(m._m01_m11_m21), normalize(y.xyz)) + dot(normalize(m._m02_m12_m22), normalize(z.xyz));
                return smoothstep(0.5736, 0.7660, 0.5 * (t - 1));
            }

            // How far a capsule from a to b of radius r leans the mass's spring target: c of its
            // overlap with the mass at rest, centred on p, out of the hand, fading to nothing within a
            // quarter of the reach of a hand lying through the centre, which pushes no way more than
            // another.
            float3 Lean(float3 p, float3 a, float3 b, float r, float radius, float c)
            {
                float3 seg = b - a;
                float3 off = p - (a + seg * saturate(dot(p - a, seg) / max(dot(seg, seg), 1e-9)));
                float d = length(off);
                return d < r + radius ? off * (c * (r + radius - d) / max(d, 0.25 * (r + radius))) : 0;
            }

            // How far a capsule from a to b of radius r is off the skin: off its box on the anchor (the
            // lattice's, where none is set), less its radius and however far the mass has swung off its
            // rest. Steps + 1 points along the capsule, none more than half a step off any point of it.
            float Gap(float3 a, float3 b, float r, float4x4 m, float scale, uint side, int steps = 8)
            {
                float4 box = Region == 0 ? _JellyBox0 : _JellyBox1;
                float3 half = box.z > 0 ? float3(box.xy, 0.5 * box.z) : 0.5 * (float3) (Count() - 1) * _Lattice.w, mid = float3(0, 0, half.z);
                float4 mass = _MainTex.Load(int3(30, side, 0));
                float swung = mass.w > 0 ? distance(mass.xyz, mul(m, float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1)).xyz) : 0;
                // Into is affine, so the ends taken in once and the points between found there: a point
                // at a time, its 128 hands' worth timed the compiler out.
                float3 ia = Into(m, a) - mid, ib = Into(m, b) - mid;
                float most = 1e9;
                [unroll] for (int k = 0; k <= steps; k++) most = min(most, length(max(abs(lerp(ia, ib, k / (float) steps)) - half, 0)));
                return most * scale - r - swung - distance(a, b) / (2 * steps);
            }

            // Whether it can reach the skin: within 8 cm, for what a press bulges out of the box. A sphere
            // round the lattice took in the thighs below it, so every particle and breast vertex tested
            // them with the arms down.
            bool InReach(float3 a, float3 b, float r, float4x4 m, float scale, uint side, int steps = 8) { return Gap(a, b, r, m, scale, side, steps) <= 0.08 * scale; }

            // How far the wearer's capsules in reach have moved against the anchor since the state at
            // side was stepped: their ends as it kept them (texels 42 to 57 and 64 to 79) on its anchor
            // then (26 to 29), against where they are now on m. Fresh, the reach is judged again and
            // one newly in it counts as a metre; otherwise those in reach then, which the count, run
            // every frame, can afford.
            float Shifted(float4x4 m, float scale, uint side, bool fresh)
            {
                float4 x0 = _MainTex.Load(int3(26, side, 0)), y0 = _MainTex.Load(int3(27, side, 0)), z0 = _MainTex.Load(int3(28, side, 0)), o0 = _MainTex.Load(int3(29, side, 0));
                float4x4 last = float4x4(x0.x, y0.x, z0.x, o0.x, x0.y, y0.y, z0.y, o0.y, x0.z, y0.z, z0.z, o0.z, 0, 0, 0, 1);
                uint was = (uint) _MainTex.Load(int3(41, side, 0)).x;
                float most = 0;
                [loop] for (uint c = 0; c < 16; c++)
                {
                    float3 a, b;
                    float r;
                    Own(c, scale, a, b, r);
                    bool kept = (was & (1u << c)) != 0;
                    [branch] if (r > 0 && (fresh ? InReach(a, b, r, m, scale, side) : kept))
                    {
                        uint t = c < 8 ? 42 + 2 * c : 48 + 2 * c;
                        float3 ka = _MainTex.Load(int3(t, side, 0)).xyz, kb = _MainTex.Load(int3(t + 1, side, 0)).xyz;
                        float moved = max(length(Into(m, a) - Into(last, ka)), length(Into(m, b) - Into(last, kb))) * scale;
                        most = max(most, kept ? moved : 1);
                    }
                }
                return most;
            }

#if defined(JELLY_SCAN) || defined(JELLY_POINTS)
            // The client's first is the local player's head, then the others': heads press as hands do.
            float4 _CVR_PlayerHeadPositions[64];
#else
            // The client's first is the local player's head; only it is read.
            float4 _CVR_PlayerHeadPositions[1];
#endif

            // The client's rendering buffer: where the local player's camera is and how it is turned,
            // a frame late. Unbound, as in the editor or on a client without it, it reads zeros and
            // nothing counts as out of view.
            struct CVRRenderingData
            {
                int renderingCamera;
                int renderingMirroredView;
                int renderingCullingMask;
                float3 renderingCameraPos;
                float3 renderingPlayerCameraPos;
                float4 renderingPlayerCameraRot;
                float3 padding;
            };
            StructuredBuffer<CVRRenderingData> _CVRRenderingBuffer;

            // Well behind the viewer: past _Near of the camera and more than _Behind degrees off where
            // it looks, less the angle size takes up there. Waking comes back through the parser's
            // readback a few frames late, so it takes ten degrees more to go than to come back.
            bool Unseen(float3 at, float size, bool was)
            {
                if (!(_Behind > 0)) return false;
                CVRRenderingData d = _CVRRenderingBuffer[0];
                float4 q = d.renderingPlayerCameraRot;
                if (abs(dot(q, q) - 1) > 0.01) return false;
                float3 look = float3(2 * (q.x * q.z + q.w * q.y), 2 * (q.y * q.z - q.w * q.x), 1 - 2 * (q.x * q.x + q.y * q.y));
                float3 to = at - d.renderingPlayerCameraPos;
                float gone = length(to);
                if (!(gone > _Near)) return false;
                float off = degrees(acos(clamp(dot(look, to / gone), -1, 1))) - degrees(asin(saturate(size / gone)));
                return off > _Behind + (was ? 0 : 10);
            }

            // Far from the viewer's head or well behind their camera, judged on the first region for
            // every region alike, half a metre's give on the distance so it does not flicker.
            bool Away(bool was)
            {
                float4x4 a = Anchor(0);
                float3 head = _CVR_PlayerHeadPositions[0].xyz, at = a._m03_m13_m23;
                return dot(head, head) > 1e-6 && distance(head, at) > _Far - (was ? 0.5 : 0)
                    || Unseen(at, 2 * length(float3(Count()) * _Lattice.w) * length(a._m00_m10_m20), was);
            }

            // Past _Half of the viewer's head the parser runs the steps at _Rate.y, slower still
            // where the jiggle's own rate does not show.
            bool Halved()
            {
                float3 head = _CVR_PlayerHeadPositions[0].xyz;
                return _Half > 0 && dot(head, head) > 1e-6 && distance(head, Anchor(0)._m03_m13_m23) > _Half;
            }

#if defined(JELLY_SCAN) || defined(JELLY_POINTS)
            // The first 64 of the client's 255, all the scan reads: the whole arrays made every first
            // pass upload 16 kB of constants.
            float4 _CVR_PlayerLeftHandPositions[64];
            float4 _CVR_PlayerRightHandPositions[64];
            float4 _CVR_PlayerChestPositions[64];
            float4 _CVR_PlayerRootPositions[64];
            float4 CVRGlobalParams1;

            // Player who's head as a ball: the client gives the head bone, at the skull's base, so its
            // middle is put up from it, away from their chest. w 0 with no head.
            float4 HeadOf(int who, float tall)
            {
                float3 h = _CVR_PlayerHeadPositions[who].xyz, c = _CVR_PlayerChestPositions[who].xyz;
                // An avatar with no chest bone mapped hands a zero there.
                float3 up = h - (dot(c, c) > 1e-6 ? c : _CVR_PlayerRootPositions[who].xyz);
                if (!(dot(h, h) >= 1e-6)) return 0;
                return float4(h + (dot(up, up) > 1e-6 ? normalize(up) : 0) * (0.08 * tall / 1.6), 0.1 * tall / 1.6);
            }

            // Whether player who is the wearer, both hands where the own hand capsules start: the
            // wearer is in the arrays too, on every machine, and their hands already press as the own
            // capsules, rimless. Counted again, a hand of their own pressed twice.
            bool Wearer(int who)
            {
                return distance(_CVR_PlayerLeftHandPositions[who].xyz, _JellyAnchor[6]._m03_m13_m23) < 0.05
                    && distance(_CVR_PlayerRightHandPositions[who].xyz, _JellyAnchor[12]._m03_m13_m23) < 0.05;
            }

            // Hand slot texel x of the eight, two texels each, (wrist, radius) then (an open hand's
            // tip, 10 plus the gesture), an empty one 0. Each looks through every player's hands and keeps the one it is due, so a
            // pass reads eight however many players there are.
            float4 Scan(int x, float4x4 m, float scale, uint side, bool felt, Press presses[2])
            {
                int slot = x >> 1;
                int3 n = Count();
                // A sphere round the lattice at rest, with room for it to bulge.
                float3 centre = mul(m, float4(0, 0, 0.5 * (n.z - 1) * _Lattice.w, 1)).xyz;
                float reach = (0.5 * length(float3(n - 1)) * _Lattice.w + 0.05) * scale;
                int players = (Region == 0 ? _JellyArrays.x : _JellyArrays.y) > 0.5 ? min((int) round(CVRGlobalParams1.y), 64) : 0;
                int found = 0;
                float4 kept = 0, kw = 0, kt = 0;
                // Every player's hands as the squash reads them: from the wrist, away from that
                // player's chest, a ninth of their viewpoint height. Rolled: written out for all 64,
                // the hands were a fifth of a step every player's driver took seconds to build the
                // first time it saw one. Shader Filtering cuts a rolled loop short, but player 0 is
                // the local player, so a viewer's own hands always count (on the wearer's machine it is
                // the wearer, skipped as Wearer). Most players are passed over on where they stand,
                // since a hand is never further from its player's feet than half as much again as
                // their height.
                [loop] for (int who = 0; who < players; who++)
                {
                    float4 root = _CVR_PlayerRootPositions[who];
                    float tall = root.w > 0.1 ? root.w : 1.6;
                    float3 to = centre - root.xyz;
                    float around = reach + 1.5 * tall + 0.5;
                    [branch] if (dot(to, to) < around * around && !Wearer(who))
                    {
                        float r = _Hand * tall / 1.6;
                        float3 chest = _CVR_PlayerChestPositions[who].xyz;
                        [unroll] for (int hand = 0; hand < 2; hand++)
                        {
                            float4 held = hand == 0 ? _CVR_PlayerLeftHandPositions[who] : _CVR_PlayerRightHandPositions[who];
                            float3 wrist = held.xyz;
                            float3 arm = wrist - chest;
                            float3 tip = wrist + (dot(arm, arm) > 1e-6 ? normalize(arm) : 0) * max(0.11 * tall - r, 0);
                            // ponytail: eight hands at most, the first in player order; a crowd round one
                            // jelly needs more slots.
                            // Twice the radius, the skin's rim the draw eases out round a hand. Three
                            // points: nine, when this was written out 128 times, timed the compiler out.
                            bool hit = dot(wrist, wrist) >= 1e-6 && InReach(wrist, tip, 2 * r, m, scale, side, 2);
                            kw = hit && found == slot ? float4(wrist, r) : kw;
                            kt = hit && found == slot ? float4(tip, 10 + clamp(held.w, 0, 9.9)) : kt;
                            found += hit;
                        }
                    }
                }
                // Then the heads, after every hand: slots go by order, and a grip lives in its slot, so
                // a head coming into reach among the hands moved a later hand to another slot and broke
                // its grip. Each is kept as an open hand of no length, a ball, which never grips.
                [loop] for (int who = 0; who < players; who++)
                {
                    float4 root = _CVR_PlayerRootPositions[who];
                    float tall = root.w > 0.1 ? root.w : 1.6;
                    float3 to = centre - root.xyz;
                    float around = reach + tall + 0.5;
                    [branch] if (dot(to, to) < around * around && !Wearer(who))
                    {
                        float4 head = HeadOf(who, tall);
                        bool hit = head.w > 0 && InReach(head.xyz, head.xyz, 2 * head.w, m, scale, side, 1);
                        kw = hit && found == slot ? head : kw;
                        kt = hit && found == slot ? float4(head.xyz, 10) : kt;
                        found += hit;
                    }
                }
                // Held once, for the hand this slot keeps: inside the loop it was 128 copies, too many to unroll.
                [branch] if (kt.w > 0.5)
                {
                    float3 w = kw.xyz, t = kt.xyz, pb, fa, fb;
                    float rf, hold;
                    // Held by as far as the hand reaches as drawn, a fist's knuckles or a finger out:
                    // by the open fingertips, a fist closing on the skin sat 3 cm off it, and gripped
                    // only if the frame it closed on caught it still in.
                    Hand(kw, kt, pb, fa, fb, rf);
                    float3 reach = rf > 0 ? fb : pb, was = w;
                    // The slot's hand a step before, if it is this one: within its hold and a step's
                    // move of where it was held.
                    float4 pw = _MainTex.Load(int3(2 * slot, side, 0)), pt = _MainTex.Load(int3(2 * slot + 1, side, 0));
                    bool same = pt.w > 0.5 && distance(w, pw.xyz) < floor((pt.w - 10) / 10) / 1000 + 0.15 * scale;
                    float3 fw = same ? pw.xyz : w;
                    Held(w, reach, kw.w, rf > 0 ? rf : kw.w, true, fw, fw + reach - w, m, scale, hold);
                    t += w - was;
                    // How far it is held back rides on the gesture, 10 a millimetre: 10 + gesture + 10 k.
                    kept = (x & 1) == 0 ? float4(w, kw.w) : float4(t, kt.w + 10 * round(1000 * hold));
                }
                // Then where MagicaCloth's colliders pushed the probes, each a hand's ball whose side
                // is the point it pushed: pushed out its nearest way, a probe lies on the collider's
                // skin, the collider on past it the way it came from. Its push is on the hang, as
                // last skinned, put on the hang as it is now. Felt before the dispatch.
                [branch] if (felt)
                {
                    // Felt leaves two pressing at most, taken in their leaders' probe order so two
                    // presses alike never swap slots frame to frame. Every probe tested in turn
                    // timed the compiler out.
                    uint first = presses[1].deep > 0 && presses[1].k < presses[0].k ? 1 : 0;
                    [loop] for (uint j = 0; j < 2; j++)
                    {
                        uint i = j ^ first;
                        bool hit = false;
                        float3 led = presses[i].ball;
                        float rad = presses[i].radius;
                        [branch] if (presses[i].deep > 0)
                        {
                            // A point, so its two ends do.
                            hit = InReach(led, led, rad, m, scale, side, 1);
                            // A collider on a hand the arrays gave last frame is that hand, which has
                            // its slot: the client puts a capsule on every player's hands. The ball
                            // sits up to a reach out from the collider, the way its probe was pushed.
                            [loop] for (uint e = 0; e < 8; e++)
                            {
                                float4 ha = _MainTex.Load(int3(2 * e, side, 0)), hb = _MainTex.Load(int3(2 * e + 1, side, 0));
                                float3 seg = hb.xyz - ha.xyz;
                                float3 p = ha.xyz + seg * saturate(dot(led - ha.xyz, seg) / max(dot(seg, seg), 1e-12));
                                hit = hit && !(hb.w > 0.5 && dot(seg, seg) > 1e-12 && distance(led, p) < ha.w + rad + _JellyProbeReach);
                            }
                        }
                        kept = hit && found == slot ? ((x & 1) == 0 ? float4(led, rad) : float4(led, 10)) : kept;
                        found += hit;
                    }
                }
                return kept;
            }
#endif

#ifdef JELLY_POINTS
            // What the avatar's animator is told, 3 to 5, through the parser and an animator driver:
            // the most loaded region's strain, 1 while any fist grips, and the last slap's depth
            // against the deepest, fading over its life. 0 while the step is off.
            float4 Feedback(uint x)
            {
                uint rows = Rows(), regions = (uint) round(_Regions);
                float felt = 0;
                // ponytail: two regions, a pair; a second pair needs four here.
                [unroll] for (uint r = 0; r < 2; r++)
                [branch] if (r < regions)
                {
                    uint side = r * (rows + 1) + rows;
                    if (!(abs(_Time.y - _MainTex.Load(int3(25, side, 0)).z) < 0.1)) continue;
                    if (x == 3) felt = max(felt, _MainTex.Load(int3(86, side, 0)).x);
                    else if (x == 4) { [unroll] for (uint e = 0; e < 8; e++) if (_MainTex.Load(int3(112 + e, side, 0)).w > 0) felt = 1; }
                    else [unroll] for (uint k = 0; k < 2; k++)
                    {
                        float age = _Time.y - _MainTex.Load(int3(80 + 2 * k, side, 0)).w;
                        felt = max(felt, saturate(_MainTex.Load(int3(81 + 2 * k, side, 0)).w / _Slap.z) * saturate(1 - age / _Slap.w));
                    }
                }
                return float4(felt, 0, 0, 1);
            }

            // How far point p is off region r's box on its anchor m, in metres.
            float BoxGap(float3 p, float4x4 m, float scale, uint r)
            {
                float4 box = r == 0 ? _JellyBox0 : _JellyBox1;
                float3 half = box.z > 0 ? float3(box.xy, 0.5 * box.z) : 0.5 * (float3) (Count() - 1) * _Lattice.w, mid = float3(0, 0, half.z);
                return length(max(abs(Into(m, p) - mid) - half, 0)) * scale;
            }

            // Every region resting by its own count, and nothing changed since its last step: the
            // anchor within 1 cm and 3 degrees of where it was, nothing of the wearer's in reach moved
            // a centimetre against it or come into reach, and no player's hand where the scan would
            // keep it, its wrist, middle or tip within 8 cm of either box. The steps' blitters are off
            // meanwhile, so this pass alone wakes them. The players walked as the scan walks them.
            bool Resting()
            {
                uint rows = Rows(), regions = (uint) round(_Regions);
                int players = min((int) round(CVRGlobalParams1.y), 64);
                bool resting = regions > 0;
                float4x4 on[2];
                float scales[2];
                float3 mid = 0;
                // ponytail: two regions, a pair; a second pair needs four here.
                [unroll] for (uint r = 0; r < 2; r++)
                {
                    // The second's here, past the far switch: built for the parser's pass, a far prop cost a fifth more.
                    float4x4 m = r == 0 || regions < 2 ? Mine : AnchorOf(1);
                    float scale = length(m._m00_m10_m20);
                    on[r] = m;
                    scales[r] = scale;
                    mid += 0.5 * m._m03_m13_m23;
                    [branch] if (r < regions)
                    {
                        uint side = r * (rows + 1) + rows;
                        float4 x0 = _MainTex.Load(int3(26, side, 0)), z0 = _MainTex.Load(int3(28, side, 0)), o0 = _MainTex.Load(int3(29, side, 0));
                        Region = r;
                        resting = resting && _MainTex.Load(int3(18, side, 0)).y > 0.5 && scale > 1e-6 && distance(m._m03_m13_m23, o0.xyz) < 0.01 * scale
                            && dot(normalize(m._m00_m10_m20), normalize(x0.xyz)) > 0.9986 && dot(normalize(m._m02_m12_m22), normalize(z0.xyz)) > 0.9986
                            && Shifted(m, scale, side, true) < 0.01 * scale;
                    }
                }
                float reach = (length(float3(Count() - 1)) * _Lattice.w + 0.3) * scales[0];
                bool near = false;
                // Rolled, as the scan's: player 0, the local player, always counts (the wearer, on
                // their own machine, skipped as Wearer).
                [loop] for (int who = 0; who < players; who++)
                {
                    float4 root = _CVR_PlayerRootPositions[who];
                    float tall = root.w > 0.1 ? root.w : 1.6;
                    float3 to = mid - root.xyz;
                    float around = reach + 1.5 * tall + 0.5;
                    [branch] if (dot(to, to) < around * around && !Wearer(who))
                    {
                        float hr = _Hand * tall / 1.6;
                        float3 chest = _CVR_PlayerChestPositions[who].xyz;
                        [unroll] for (int hand = 0; hand < 2; hand++)
                        {
                            float3 wrist = (hand == 0 ? _CVR_PlayerLeftHandPositions[who] : _CVR_PlayerRightHandPositions[who]).xyz, arm = wrist - chest;
                            float3 tip = wrist + (dot(arm, arm) > 1e-6 ? normalize(arm) : 0) * max(0.11 * tall - hr, 0);
                            [unroll] for (uint r = 0; r < 2; r++)
                            {
                                float gap = min(min(BoxGap(wrist, on[r], scales[r], r), BoxGap(0.5 * (wrist + tip), on[r], scales[r], r)), BoxGap(tip, on[r], scales[r], r));
                                near = near || dot(wrist, wrist) >= 1e-6 && gap < 2 * hr + 0.08 * scales[r];
                            }
                        }
                        float4 head = HeadOf(who, tall);
                        [unroll] for (uint q = 0; q < 2; q++)
                            near = near || head.w > 0 && BoxGap(head.xyz, on[q], scales[q], q) < 2 * head.w + 0.08 * scales[q];
                    }
                }
                // Or a probe pushed, a collider's press.
                [loop] for (uint q = 0; q < 2; q++)
                {
                    float4x4 back;
                    [branch] if (q < regions)
                    [branch] if (Probing(q, back))
                    {
                        [loop] for (uint k = 0; k < (uint) _JellyProbes && k < PROBES; k++)
                        {
                            float3 at, way;
                            near = near || Probe(q, k, back, at, way, false) > 0.002;
                        }
                    }
                }
                return resting && !near;
            }
#endif

            // The whole's own squash the homes take, from texels 32 to 34, a column each: the identity
            // when there is none.
            float3x3 Shape(uint side)
            {
                float4 x = _MainTex.Load(int3(32, side, 0)), y = _MainTex.Load(int3(33, side, 0)), z = _MainTex.Load(int3(34, side, 0));
                if (!(_Solid > 0) || !(x.w > 0)) return float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
                return transpose(float3x3(x.xyz, y.xyz, z.xyz));
            }

            // Particle l's place on the anchor's rest frame with the whole's squash taken: none at the
            // back face, on the chest wall, all of it at the front.
            float3 Shaped(int3 l, float3x3 shape)
            {
                float3 r = Rest(l);
                return r + (float) l.z / max(Count().z - 1, 1) * (mul(shape, r) - r);
            }

            // How that stretches the rest round l: its gradient there.
            float3x3 ShapedAt(int3 l, float3x3 shape)
            {
                float zmax = max(Count().z - 1, 1);
                float3x3 grow = shape - float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
                float3 g = mul(grow, Rest(l)) / (zmax * _Lattice.w);
                return float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1) + (float) l.z / zmax * grow + float3x3(0, 0, g.x, 0, 0, g.y, 0, 0, g.z);
            }

            // The region's mass, as one piece: its centre, the lattice's at rest, swings on the root,
            // the anchor's place. Each particle's home turns with it, more the nearer the front, so the
            // back face stays on the chest wall, and follows its centre in and out at constant volume.
            // Then the squash's own stretch in motion (yaps_dent.cginc), as real flesh's: swung
            // away from where it hangs it stretches, toward it it squashes, by _Motion.x per reach of
            // swing past _Motion.y of the reach, within _Motion.z, at constant volume, about the side
            // it hangs from. Off, or not stepped this tenth of a second, home is the rest on the anchor
            // alone. swing is the turn the particle's home took.
            // How far region r's bone has carried its centre off where it hangs on the chest, as the
            // squash's swing is its follow off its rest: the provider's third and fourth transforms
            // hold each region still on its parent. 0 for a region riding no bone.
            float3 Hung(uint r)
            {
                float4 at = r == 0 ? _JellyOffsetAt0 : _JellyOffsetAt1;
                if (r > 1 || !(at.w > 0)) return 0;
                float4x4 h = _JellyAnchor[2 + r];
                if (!(dot(h._m00_m10_m20, h._m00_m10_m20) > 1e-12)) return 0;
                float4 c = float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1);
                return mul(Anchor(r), c).xyz - mul(h, c).xyz;
            }

            float3 HomeOn(int3 l, float4x4 m, float4 tip, bool swings, float3x3 shape, float3 hung, out float4 swing)
            {
                float3 local = mul((float3x3) m, Shaped(l, shape));
                float3 root = m._m03_m13_m23;
                swing = float4(0, 0, 0, 1);
                if (!swings) return root + local;
                int3 n = Count();
                float3 rest = mul((float3x3) m, float3(0, 0, 0.5 * (n.z - 1) * _Lattice.w)), now = tip.xyz - root;
                float len0 = max(length(rest), 1e-6), len = max(length(now), 1e-6);
                float3 a = rest / len0, b = now / len;
                float w = (float) l.z / max(n.z - 1, 1);
                swing = normalize(lerp(float4(0, 0, 0, 1), normalize(float4(cross(a, b), 1 + dot(a, b))), w));
                float s = lerp(1, len / len0, w), along = dot(local, a);
                local = a * (along * s) + (local - a * along) * rsqrt(s);
                float3 at = root + mul(Turn(swing), local);
                // The cloth's swing of the bone, as the squash's stretch reads it, and the mass's lag on
                // it.
                float3 sw = hung + tip.xyz - root - rest;
                float reach = _MassRadius * length(m._m00_m10_m20), sl = length(sw), off = sl - _Motion.y * reach;
                [branch] if (_Motion.x > 0 && off > 0)
                {
                    float3 u = sw / sl, up = normalize(m._m01_m11_m21);
                    float st = lerp(1, clamp(1 + _Motion.x * off / reach * -dot(u, up), 1 / _Motion.z, _Motion.z), w);
                    float3 hangAt = tip.xyz + up * (0.5 * (n.y - 1) * _Lattice.w * length(m._m01_m11_m21));
                    float3 r0 = at - hangAt;
                    float by = dot(r0, u);
                    at = hangAt + u * (by * st) + (r0 - u * by) * rsqrt(st);
                }
                return at;
            }

            // The solid, once a frame in the first pass, column k of the squash the homes take: the best
            // linear map from a 4 x 4 x 4 of the particles' rest places to where they are on the anchor,
            // its turn taken out and its volume made the rest's, _Solid of it, the rest the rest. Flesh
            // barely changes volume, so squeezed one way it bulges the others, most where nothing holds
            // it, and let go it comes back. A pressure in its place swelled the whole evenly, into the
            // squeezing hands too, as a balloon would. w is the volume the map had, kept for the trace.
            // The map is of the particles, which sit on the homes, so it holds the homes' own squash:
            // taking _Solid of it outright, a 3 cm poke flattened the whole part to 0.87 of its depth and
            // kept sinking while held. Weighted _Solid / (1 + _Solid), the homes end near _Solid of what
            // the hands do: about 0.9 of it at 0.7, as the homes' squash ramps in with depth and the fit
            // sees part of it.
            // Other players' palms squeezing the whole flatten it along their line at constant volume,
            // so it grows up and forward. Fitted from the particles alone, a hard squeeze read as a
            // hand-shaped hole that averaged to almost no squash, and the skin closed round the
            // hands. Worked out fresh from where the hands are each frame, so nothing builds up;
            // half at most. The finger is left out, so a poke still dents rather than flattens.
            float3x3 Squeezed(float4x4 m, float scale, uint side)
            {
                float3x3 id = float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
                if (!(_Squeeze > 0) || !(_MainTex.Load(int3(1, side, 0)).w > 0.5)) return id;
                float radius = _MassRadius * scale;
                float3 rest = mul(m, float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1)).xyz, at = Into(m, rest);
                float3x3 s = 0;
                [loop] for (int e = 0; e < 8; e++)
                {
                    float4 a = _MainTex.Load(int3(2 * e, side, 0)), b = _MainTex.Load(int3(2 * e + 1, side, 0));
                    if (!(b.w > 0.5)) continue;
                    if (_MainTex.Load(int3(112 + e, side, 0)).w > 0) continue;
                    float3 ua = a.xyz, ub = b.xyz;
                    Unheld(ua, ub, a.w, true, floor((b.w - 10) / 10) / 1000, m, scale);
                    a.xyz = ua;
                    b.xyz = ub;
                    float3 pb, fa, fb;
                    float rf;
                    Hand(a, b, pb, fa, fb, rf);
                    float3 leaned = Lean(rest, a.xyz, pb, a.w, radius, 1);
                    float l = length(leaned);
                    if (!(l > 1e-6)) continue;
                    float3 n = normalize(Into(m, rest + leaned) - at);
                    s += l / (2 * radius) * float3x3(n.x * n, n.y * n, n.z * n);
                }
                float t = _Squeeze * (s[0][0] + s[1][1] + s[2][2]);
                if (!(t > 1e-6)) return id;
                float3x3 h = id - s * (_Squeeze * min(1, 0.5 / t));
                float v = determinant(h);
                return v > 0.1 ? h * pow(v, -1.0 / 3.0) : id;
            }

            float4 Bulk(int k, float4x4 m, float scale, bool distant, uint side)
            {
                float4 none = float4(k == 0, k == 1, k == 2, 1);
                if (distant) return none;
                // Snapped, the particles still lie where the jump left them: last frame's squash kept.
                if (Snapped) return _MainTex.Load(int3(32 + k, side, 0));
                float3x3 hands = Squeezed(m, scale, side);
                float4 squeezed = float4(transpose(hands)[k], 1);
                int3 n = Count();
                // Each particle against its home with no squash: fitted to the rest alone, the mass's
                // swing and stretch, which turn the front more than the back, read as a shear.
                float4 tip = _MainTex.Load(int3(30, side, 0)), unused;
                bool swings = _MassHz > 0 && tip.w > 0 && abs(_Time.y - tip.w) < 0.1;
                float3 hung = Hung(Region);
                float3 pc = 0, qc = 0, qq = 0, c0 = 0, c1 = 0, c2 = 0;
                // Summed as each is loaded, not held, so the pass keeps few registers. Rolled, and
                // divided by as many as it took: Shader Filtering cuts it short, to the back layers.
                float taken = 0;
                [loop] for (int i = 0; i < 64; i++)
                {
                    int3 l = int3(round(float3(i % 4, (i / 4) % 4, i / 16) * (n - 1) / 3.0));
                    float3 q = Rest(l), p = Into(m, _MainTex.Load(Texel((uint) Index(l), 0)).xyz)
                        + q - Into(m, HomeOn(l, m, tip, swings, hands, hung, unused));
                    pc += p;
                    qc += q;
                    qq += q * q;
                    c0 += p * q.x;
                    c1 += p * q.y;
                    c2 += p * q.z;
                    taken++;
                }
                pc /= max(taken, 1);
                qc /= max(taken, 1);
                // Over a box of rest places the rest's own spread is diagonal, an axis a column.
                float3 spread = qq - taken * qc * qc;
                if (!all(spread > 1e-12)) return squeezed;
                c0 = (c0 - taken * pc * qc.x) / spread.x;
                c1 = (c1 - taken * pc * qc.y) / spread.y;
                c2 = (c2 - taken * pc * qc.z) / spread.z;
                float4 q = float4(0, 0, 0, 1);
                [unroll] for (int it = 0; it < 4; it++)
                {
                    float3x3 t = Turn(q);
                    float3 t0 = float3(t[0][0], t[1][0], t[2][0]), t1 = float3(t[0][1], t[1][1], t[2][1]), t2 = float3(t[0][2], t[1][2], t[2][2]);
                    float3 spin = (cross(t0, c0) + cross(t1, c1) + cross(t2, c2)) / (abs(dot(t0, c0) + dot(t1, c1) + dot(t2, c2)) + 1e-12);
                    float w = length(spin);
                    if (w > 1e-9) q = normalize(Mul(float4(spin / w * sin(0.5 * w), cos(0.5 * w)), q));
                }
                float3x3 a = mul(transpose(Turn(q)), transpose(float3x3(c0, c1, c2)));
                a = 0.5 * (a + transpose(a));
                float volume = determinant(a);
                if (!(volume > 0.3 && volume < 3)) return squeezed;
                float share = _Solid / (1 + _Solid);
                float3x3 shape = mul(hands, (1 - share) * float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1) + share * a * pow(volume, -1.0 / 3.0));
                return float4(transpose(shape)[k], volume);
            }

            // A point as last frame's anchor held it, put where this frame's holds it.
            float3 Carried(float3 x, float4x4 m, uint side)
            {
                float4 x0 = _MainTex.Load(int3(26, side, 0)), y0 = _MainTex.Load(int3(27, side, 0)), z0 = _MainTex.Load(int3(28, side, 0)), o0 = _MainTex.Load(int3(29, side, 0));
                if (!(o0.w > 0.5)) return x;
                float4x4 last = float4x4(x0.x, y0.x, z0.x, o0.x, x0.y, y0.y, z0.y, o0.y, x0.z, y0.z, z0.z, o0.z, 0, 0, 0, 1);
                return mul(m, float4(Into(last, x), 1)).xyz;
            }

            float3 Home(int3 l, float4x4 m, uint side, out float4 swing)
            {
                float4 tip = _MainTex.Load(int3(30, side, 0));
                // Snapped, the first pass still has last frame's mass: on the jump with the anchor.
                [branch] if (Snapped && _Pass < 0.5) tip.xyz = Carried(tip.xyz, m, side);
                // Stamped and recent: a cleared texel reads as stepped at time 0, and at a scene's first
                // frame that passed, swinging every home toward the world's origin.
                return HomeOn(l, m, tip, _MassHz > 0 && tip.w > 0 && abs(_Time.y - tip.w) < 0.1, Shape(side), Hung(Region), swing);
            }

            // How fast particle l's home moves: from last frame's anchor and mass (texels 26 to 29 and
            // 31) to this one's, over the frame. 0 before there was a last frame.
            // ponytail: the cloth's swing is this frame's for both, so the stretch's own rate is
            // missing; keep last frame's hang in the side row if the stretch lags.
            float3 HomeSpeed(int3 l, uint side, float3 home)
            {
                float4 x = _MainTex.Load(int3(26, side, 0)), y = _MainTex.Load(int3(27, side, 0)), z = _MainTex.Load(int3(28, side, 0));
                float4 o = _MainTex.Load(int3(29, side, 0)), tip = _MainTex.Load(int3(30, side, 0)), was = _MainTex.Load(int3(31, side, 0));
                if (!(o.w > 0.5) || !(FrameDt > 0)) return 0;
                float4x4 last = float4x4(x.x, y.x, z.x, o.x, x.y, y.y, z.y, o.y, x.z, y.z, z.z, o.z, 0, 0, 0, 1);
                // Carried, 31 holds last frame's mass moved on with the anchor: put back on last frame's.
                if (_MassCarried > 0) was.xyz = mul(last, float4(Into(Anchor(Region), was.xyz), 1)).xyz;
                float4 unused;
                return (home - HomeOn(l, last, was, _MassHz > 0 && tip.w > 0 && abs(_Time.y - tip.w) < 0.1, Shape(side), Hung(Region), unused)) / FrameDt;
            }

            // Where a particle is headed from s, where it was was (w that step's time): its home's own
            // move kept, damped by _Damp; what it does apart from that damped by _Viscous. Flesh is
            // viscous, so a dent fills back without ringing while the mass's swing carries the whole:
            // damped by _Damp alone, the lattice rang at 3 Hz after every press, a jelly.
            float3 Ahead(float4 s, float4 was, int3 l, uint side, float3 home, float h)
            {
                float3 v = (s.xyz - was.xyz) / max(was.w, 1e-4);
                // Snapped, the jump is no speed of the home's: the particle was carried over it.
                float3 vh = Snapped ? v : HomeSpeed(l, side, home);
                return s.xyz + (vh * exp(-_Damp * was.w) + (v - vh) * exp(-_Viscous * was.w)) * h;
            }

            // The mass's step, once a frame by the whole frame time, in the first pass: texel 30 its
            // centre (w the _Time.y it stepped), 31 where it was (w that step's time). Verlet under
            // gravity; a spring of _MassHz toward its rest, held up by the sag gravity gives when the
            // tissue was modelled hanging; pushed out of the hands in reach by _Give of how far they
            // reach into it, the particles left to dent the rest; kept off its partner's centre, each
            // taking half; its swing held within _Limit of its length, in and out within a tenth.
            // The hands are the slots the last frame's scan kept.
            float4 Mass(int x, float4x4 m, float scale, float dt, bool distant, uint side, uint region, uint partnerSide)
            {
                float4 tip = _MainTex.Load(int3(30, side, 0)), was = _MainTex.Load(int3(31, side, 0));
                // Carried, both are moved on with the anchor since last frame, so only the hands move
                // the mass on it: lagging a bone its cloth swings, it swung the whole a second time.
                [branch] if (_MassCarried > 0 || Snapped)
                {
                    float4 x0 = _MainTex.Load(int3(26, side, 0)), y0 = _MainTex.Load(int3(27, side, 0)), z0 = _MainTex.Load(int3(28, side, 0)), o0 = _MainTex.Load(int3(29, side, 0));
                    if (o0.w > 0.5)
                    {
                        float4x4 last = float4x4(x0.x, y0.x, z0.x, o0.x, x0.y, y0.y, z0.y, o0.y, x0.z, y0.z, z0.z, o0.z, 0, 0, 0, 1);
                        tip.xyz = mul(m, float4(Into(last, tip.xyz), 1)).xyz;
                        was.xyz = mul(m, float4(Into(last, was.xyz), 1)).xyz;
                    }
                }
                float3 root = m._m03_m13_m23;
                float zc = 0.5 * (Count().z - 1) * _Lattice.w;
                float3 rest = mul(m, float4(0, 0, zc, 1)).xyz;
                bool fresh = distant || !(tip.w > 0 && abs(_Time.y - tip.w) < 0.1) || !(was.w > 0 && was.w < 0.1) || !(distance(tip.xyz, rest) < 1);
                if (x == 31) return float4(fresh ? rest : tip.xyz, dt);
                if (fresh) return x == 86 ? 0 : float4(rest, _Time.y);
                // Strained, as last frame's 86 has it, flesh goes taut: pulled out by a fist or
                // pressed and squeezed, it swings stiffer and settles sooner, from moving, shaking or
                // a slap alike.
                float4 strained = _MainTex.Load(int3(86, side, 0));
                float taut = strained.w > 0 && abs(_Time.y - strained.w) < 0.1 ? _Taut * strained.x : 0;
                // Carried, the cloth gives the weight its sag, so the mass takes none of its own.
                float3 next = tip.xyz + (tip.xyz - was.xyz) * (exp(-_MassDamp * (1 + 3 * taut) * dt) * dt / max(was.w, 1e-4)) + (_MassCarried > 0 ? 0 : Gravity * dt * dt);
                float spring = 6.2831853 * _MassHz * dt;
                // How hard it is loaded: each push and pull on it by its size, so a squeeze from both
                // sides, which leaves the whole where it was, loads it twice over.
                float load = 0;
                // The hands move where the spring holds it, a force, not the mass itself: pushed out
                // a share of the way each frame, it left any hand within a few frames and the
                // particles nothing to dent. Each takes give of its overlap with the mass at rest,
                // where it settles. Taken where the mass was, at give / (1 - give), it settled the
                // same but fed back: past a give of a half two hands squeezing buzzed at 37 frames a
                // second or fewer, and fingertips level with a centre tipped one back 25 mm.
                float radius = _MassRadius * scale;
                float3 pushed = 0;
                [branch] if (_MainTex.Load(int3(1, side, 0)).w > 0.5)
                {
                    [loop] for (int e = 0; e < 8; e++)
                    {
                        float4 a = _MainTex.Load(int3(2 * e, side, 0)), b = _MainTex.Load(int3(2 * e + 1, side, 0));
                        if (!(b.w > 0.5)) continue;
                        if (_MainTex.Load(int3(112 + e, side, 0)).w > 0) continue;
                        float3 ua = a.xyz, ub = b.xyz;
                        Unheld(ua, ub, a.w, true, floor((b.w - 10) / 10) / 1000, m, scale);
                        a.xyz = ua;
                        b.xyz = ub;
                        // A finger out leans by as much less as it is thinner, as the tissue's stiffness
                        // under a contact goes with its radius.
                        float3 pb, fa, fb;
                        float rf, c = min(_Give, 0.95);
                        Hand(a, b, pb, fa, fb, rf);
                        float3 leaned = Lean(rest, a.xyz, pb, a.w, radius, c) + (rf > 0 ? Lean(rest, fa, fb, rf, radius, c * rf / a.w) : 0);
                        pushed += leaned;
                        load += length(leaned);
                    }
                    // Each grip pulls the whole by the share of a press it takes, the skin it holds
                    // following the hand the rest of the way.
                    [loop] for (uint gi = 0; gi < 8; gi++)
                    {
                        float3 gat;
                        float graw;
                        float4 d = Pulled(gi, m, side, gat, graw);
                        float3 pull = mul((float3x3) m, d.xyz) * (d.w * min(_Give, 0.95));
                        pushed += pull;
                        load += length(pull);
                    }
                }
                // The wearer's own hands too; their arms and thighs only dent it, or an arm resting
                // against its side would shove the whole away.
                uint own = (uint) _MainTex.Load(int3(41, side, 0)).x & 0x44u;
                [branch] if (own != 0)
                {
                    float c = min(_Give, 0.95);
                    [loop] for (uint k = 2; k < 8; k += 4)
                    {
                        [branch] if (own & (1u << k))
                        {
                            float3 a, b;
                            float r;
                            Own(k, scale, a, b, r);
                            float hold;
                            Held(a, b, r, r, false, a, b, m, scale, hold);
                            Unheld(a, b, r, false, hold, m, scale);
                            float3 leaned = Lean(rest, a, b, r, radius, c);
                            pushed += leaned;
                            load += length(leaned);
                        }
                    }
                }
                // A world solid's faces too, by the deepest, as a hand leans: a pane slapped on
                // shoves the whole and it swings after. The faces alone pressed the front flat and
                // nothing followed.
                [branch] if (SolidRegion(region) && Probed)
                [branch] if (Pushed(side))
                {
                    float c = min(_Give, 0.95), deepest = 0;
                    float3 along = 0, smid = mul(_JellyAnchor[2 + region], float4(0, 0, zc, 1)).xyz;
                    [loop] for (uint fk = WorldFrom(region); fk < (uint) _JellyProbes && fk < PROBES; fk++)
                    {
                        float3 fat, fway;
                        float fg = Probe(region, fk, Back, fat, fway);
                        [branch] if (fg > 0.02)
                        {
                            float3 face = fat + normalize(fat - fway * fg - smid) * (_JellyProbeReach - BALL) - fway * BALL;
                            float over = radius - dot(rest - face, fway);
                            if (over > deepest) { deepest = over; along = fway; }
                        }
                    }
                    pushed += along * (c * deepest);
                    load += c * deepest;
                }
                // Strained half way at a load a tenth of its length, wholly well past it.
                if (x == 86) return float4(load / (load + 0.1 * zc * scale), load, 0, _Time.y);
                // A twitch kicks it as a hand would, never loading it.
                pushed += Twitch();
                spring *= 1 + taut;
                // Free, lifted by its own sag whatever _Lift says of the tissue: a prop's tissue sags
                // onto its plate, and a mass let sag dragged the whole down after it. Taken at the taut
                // spring: lifted for the slack one, a press sprang the whole 10 cm up.
                float3 lift = !(_MassCarried > 0) ? normalize(m._m01_m11_m21) * 9.81 * _Gravity * (1 - spring * spring) / (39.478418 * _MassHz * _MassHz * (1 + taut) * (1 + taut)) : 0;
                next = lerp(next, rest + lift + pushed, min(spring * spring, 1));
                [branch] if (_Pair > 0)
                {
                    float4 other = _MainTex.Load(int3(30, partnerSide, 0));
                    // As far apart as they sat as built: from where the anchors are now, two breasts
                    // the cloth swung together read as resting that close.
                    // Kept apart across the seam they rested at and free along it: kept apart along
                    // the line between their centres, a squeeze at their middles sheared them past
                    // each other, one up and one down, as two balls pressed together slip.
                    float keep = _PairKeep * length(_PairOffset.xyz) * scale;
                    float3 seam = normalize(mul((float3x3) m, _PairOffset.xyz));
                    float s = dot(next - other.xyz, seam), side_ = s < 0 ? -1 : 1;
                    if (other.w > 0 && abs(_Time.y - other.w) < 0.1 && abs(s) < keep) next += seam * (0.5 * (side_ * keep - s));
                }
                float len0 = zc * scale;
                float3 off = next - root;
                float len = max(length(off), 1e-6);
                next = root + off * (clamp(len, 0.9 * len0, 1.1 * len0) / len);
                float3 swung = next - rest;
                float most = _Limit * len0;
                if (dot(swung, swung) > most * most) next = rest + swung * (most / length(swung));
                return float4(next, _Time.y);
            }

            // Where particle p, at l, goes this pass, and its neighbourhood's turn: toward the rigid
            // fit of its neighbourhood, toward its rest on the anchor, and out of any hand in reach.
            // With predicted, where each particle is headed is the first pass's; otherwise it is
            // worked out here from both halves.
            float3 Moved(uint p, int3 l, float4x4 m, float scale, float h, float3 home, bool predicted, bool distant, uint side, float4 swing, out float4 q)
            {
                q = Mul(swing, Quat((float3x3) m / scale));
                // The back face rides the anchor, or on a limb the core round its bone.
                if (Pinned(l) || distant) return home;
                float4 s = _MainTex.Load(Texel(p, 0));
                float3 ahead, base;
                float kh = 0;
                if (predicted)
                {
                    // The first pass's mark: 0 for a particle it put back at rest.
                    if (!(s.w > 0.5)) return home;
                    ahead = base = s.xyz;
                }
                else
                {
                    float4 was = _MainTex.Load(Texel(p, 1));
                    // Carried over a snap once, by the first pass; the second steps on from there.
                    [branch] if (Snapped && _Pass < 0.5) { s.xyz = Carried(s.xyz, m, side); was.xyz = Carried(was.xyz, m, side); }
                    if (Fresh(s, was, home)) return home;
                    // Every right-half w is the h of the same pass, so this holds for the neighbours too.
                    kh = exp(-_Damp * was.w) / max(was.w, 1e-4) * h;
                    base = Ahead(s, was, l, side, home, h);
                    ahead = base + Gravity * h * h;
                }
                float3 c, rc;
                float4 lin;
                // The rest round l as the homes have it, the whole's squash taken. Held to the plain
                // rest while the homes moved, each particle was pulled two ways, and the lattice's sides
                // flickered a frame apart under a held press.
                float3x3 sw = ShapedAt(l, Shape(side));
                q = Fit(p, l, ahead, base, kh, m, h, home, q, predicted, sw, c, rc, lin);
                // Rigid, or keeping _Squish of its neighbourhood's own squash and stretch at constant
                // volume, so flesh pressed in one way bulges out the others.
                float3 rigid = mul(Turn(q), mul(sw, Rest(l) - rc) * scale);
                float3 goal = c + (lin.w > 0 ? lerp(rigid, lin.xyz, _Squish) : rigid);
                // _Stiff is the pull over a 180th of a second, so the tissue feels the same
                // whether a frame takes one pass or two.
                float3 next = lerp(ahead, goal, 1 - pow(1 - _Stiff, h * 180));
                // And the whole of it toward its rest on the anchor, as a spring of _Hz: the
                // neighbourhoods alone keep the shape near each particle but let a block 16
                // across fold under its own weight, 42 cm at the front.
                float spring = 6.2831853 * _Hz * h;
                // Upright, gravity hangs the tissue g (1 - spring^2) / w^2 below where this pull holds
                // it; tissue modelled on a body hangs already, so it is held as far above. Held round
                // a limb's bone, against the world's up: a seated thigh lifted along its bone sat
                // 32 mm off its modelled shape, toward the hip and down. Firm flesh on a limb keeps
                // its shape however the limb lies and still swings with it.
                float3 lift = 0;
                if (_Lift > 0) lift = _Lift * (Cored ? float3(0, 1, 0) : normalize(m._m01_m11_m21)) * 9.81 * _Gravity * (1 - spring * spring) / (39.478418 * _Hz * _Hz);
                // The pair meet at a plane between them, moving and tilting with them, with no friction,
                // so they slide along it and never stick: they push together.
                // Each home past it is eased back behind it in order, the face it meets flat, and the
                // whole grows across by the volume that takes, so what is pressed flat goes up, down and
                // forward, never into nothing. Read from the
                // masses, where each breast is as a whole; only closer than they sat as built do they
                // press, each taking half. Before, each read the other's homes through its body's
                // distance field: the lens between them was pressed to half and lost, and a hard push
                // passed through.
                float3 held = home, pn = 0;
                float pd = 0, meet = 0;
                bool plane = false;
                [branch] if (_Pair > 0 && _PairReach.x > 0)
                {
                    uint rows = side - Base, other = Region ^ 1, ps = other * (rows + 1) + rows;
                    float zc = 0.5 * (Count().z - 1) * _Lattice.w;
                    float4 ta = _MainTex.Load(int3(30, side, 0)), tb = _MainTex.Load(int3(30, ps, 0));
                    float3 ca = ta.w > 0 ? ta.xyz : mul(m, float4(0, 0, zc, 1)).xyz;
                    float3 cb = tb.xyz;
                    // Its own copy: a particle never has the other's anchor to hand.
                    [branch] if (!(tb.w > 0)) cb = mul(AnchorOf(other), float4(0, 0, zc, 1)).xyz;
                    float dist = distance(ca, cb), wa = (other == 1 ? _PairReach.x : _PairReach.y) * scale;
                    float e = 0.5 * max((length(_PairOffset.xyz) - _PairReach.z) * scale - dist, 0);
                    meet = e > 0 ? Meet(m, ps) : 0;
                    [branch] if (e > 0 && dist > 1e-4 && meet > 0)
                    {
                        plane = true;
                        pn = (cb - ca) / dist;
                        pd = dot(ca, pn) + wa - e;
                        // Across, the cap past the plane of a ball the breast's half width given back,
                        // none at the chest wall.
                        float cap = e * e * (3 * wa - e) / (4 * wa * wa * wa), w = (float) l.z / max(Count().z - 1, 1);
                        float3 from = held - ca;
                        held += (from - pn * dot(from, pn)) * ((rsqrt(max(1 - cap, 0.5)) - 1) * w);
                        // Behind the plane by a soft minimum with it, so layers keep their order and the
                        // face is flat to a twelfth of the half width; eased in as they meet.
                        float a = dot(held, pn) - pd, soft = 0.08 * wa;
                        float eased = min(a, 0) - soft * log(1 + exp(-abs(a) / soft));
                        held += pn * ((eased - a) * saturate(e / soft));
                        held = lerp(home, held, meet);
                    }
                }
                next = lerp(next, held + lift, min(spring * spring, 1));
                // The hands found in reach, each from the wrist to its tip with a radius. Slots fill in
                // order, so an empty first one means there are none. Rolled, as every loop here over
                // slots, capsules and probes: Shader Filtering cuts them short, the first slots first.
                // Each goes on ahead of the palm and any finger out, toward the region's centre.
                [branch] if (_MainTex.Load(int3(1, side, 0)).w > 0.5)
                {
                    float4 mass = _MainTex.Load(int3(30, side, 0));
                    float3 centre = mass.w > 0 ? mass.xyz : mul(m, float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1)).xyz;
                    [loop] for (int e = 0; e < 8; e++)
                    {
                        float4 a = _MainTex.Load(int3(2 * e, side, 0)), b = _MainTex.Load(int3(2 * e + 1, side, 0));
                        if (!(b.w > 0.5)) continue;
                        // A gripping hand carries the skin below rather than pushing it.
                        if (_MainTex.Load(int3(112 + e, side, 0)).w > 0) continue;
                        float3 pb, fa, fb;
                        float rf;
                        Hand(a, b, pb, fa, fb, rf);
                        next = Plow(next, a.xyz, pb, a.w, centre);
                        if (rf > 0) next = Plow(next, fa, fb, rf, centre);
                    }
                    // Each grip takes the skin it closed on along with the hand: all of it at the
                    // grip, none a hand's breadth off, the neighbourhoods' fit bending what lies
                    // between. Let go, the tissue's own spring brings it back.
                    [loop] for (uint gi = 0; gi < 8; gi++)
                    {
                        float3 gat;
                        float graw;
                        float4 d = Pulled(gi, m, side, gat, graw);
                        [branch] if (d.w > 0)
                        {
                            float w = smoothstep(2 * _MainTex.Load(int3(2 * gi, side, 0)).w / scale, 0, distance(Rest(l), gat));
                            // Its rest on the anchor carried by the pull, so the skin under the fist
                            // sits at the fist: on its home, the whole's lean after the hand counted twice.
                            next = lerp(next, lerp(home, mul(m, float4(Rest(l) + d.xyz, 1)).xyz, w), w);
                        }
                    }
                }
                // The wearer's own arms, hands and thighs in reach. None in reach, one read.
                uint own = (uint) _MainTex.Load(int3(41, side, 0)).x;
                [branch] if (own != 0)
                {
                    float4 mass = _MainTex.Load(int3(30, side, 0));
                    float3 centre = mass.w > 0 ? mass.xyz : mul(m, float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1)).xyz;
                    [loop] for (uint c = 0; c < 16; c++)
                    {
                        [branch] if (own & (1u << c))
                        {
                            float3 a, b;
                            float r;
                            Own(c, scale, a, b, r);
                            float hold;
                            if (c == 2 || c == 6) Held(a, b, r, r, false, a, b, m, scale, hold);
                            next = Out(next, a, b, r, centre);
                        }
                    }
                }
                // A particle the hands drive past the plane goes half the way back to its home's side of
                // it each pass, each to its own home's depth, so the layers keep their order: put on the
                // plane itself, a slab of the lattice lay flat there, skin and what is worn over it on
                // one plane, and the two drew through each other in patches.
                [branch] if (plane)
                {
                    // Never further back than its own home, eased by how hard they press: sent onto
                    // the plane outright, the edge moved 13.6 mm with the pair resting as built.
                    float a = dot(next, pn) - pd, hb = dot(held, pn) - pd;
                    if (a > max(hb, 0)) next -= pn * (0.5 * (a - hb) * meet);
                }
                // A world solid, felt through the probes it pushed: each a flat face where its ball
                // met it, facing the way it pushed and reaching _JellyProbePatch across, and the skin
                // in front of one put back on it. A pane pushes them all alike and flattens the whole
                // front at once; a ball dropped on it pushes a few and dents only there. Rebuilt as
                // two balls, as a hand is, a pane pressed two dents and the rest showed through it.
                // The face is where the ball's side touched, its centre out from the tip along the
                // skin's own outward way as the build set it: offset along the push instead, a ball
                // lagging sideways in a turn made a sideways wall that threw the tissue 59 cm. And only
                // past 2 cm: a frontal solid reaches the skin at 4, and a turn's lag is millimetres.
                [branch] if (SolidRegion(Region) && Probed)
                [branch] if (Pushed(side))
                {
                    float3 smid = mul(_JellyAnchor[2 + Region], float4(0, 0, 0.5 * (Count().z - 1) * _Lattice.w, 1)).xyz;
                    [loop] for (uint fk = WorldFrom(Region); fk < (uint) _JellyProbes && fk < PROBES; fk++)
                    {
                        float3 fat, fway;
                        float fg = Probe(Region, fk, Back, fat, fway);
                        [branch] if (fg > 0.02)
                        {
                            float3 fout = normalize(fat - fway * fg - smid);
                            float3 fo = next - (fat + fout * (_JellyProbeReach - BALL) - fway * BALL);
                            float fa = dot(fo, fway), across = length(fo - fway * fa);
                            if (fa < 0) next -= fway * (fa * (1 - smoothstep(0.5 * _JellyProbePatch, _JellyProbePatch, across)));
                        }
                    }
                }
                // A board under it on its anchor, which it rests on: dropped, it sagged through.
                [branch] if (_Floor.x > 0)
                {
                    float3 lp = Into(m, next);
                    if (lp.y < _Floor.y) next = mul(m, float4(lp.x, _Floor.y, lp.z, 1)).xyz;
                }
                return next;
            }

            // The texture parser's two texels, each a range between its task's bounds: where the
            // front centre is in the anchor's own space, x, y and z each from -0.5 to 0.5; and how its
            // neighbourhood has turned against the anchor, as Unity's Euler angles, each from -180 to
            // 180. From the same step the front centre's own texel takes, at and q.
            float4 Rider(int x, float4x4 m, float scale, float3 at, float4 q)
            {
                float3x3 r = (float3x3) m / scale;
                if (x == 16) return float4(saturate(mul(transpose(r), at - m._m03_m13_m23) / scale + 0.5), 1);
                float3x3 t = mul(transpose(r), Turn(q));
                // Unity turns by z, then x, then y: t = Ry Rx Rz.
                float3 euler = degrees(float3(asin(clamp(-t[1][2], -1, 1)), atan2(t[0][2], t[2][2]), atan2(t[1][0], t[1][1])));
                return float4(saturate(euler / 360 + 0.5), 1);
            }

            // The anchor's move since it last stepped, at the lattice's far corner, about 0.2 of the
            // anchor's units out.
            float AnchorMoved(float4x4 m, uint side)
            {
                float3 x = _MainTex.Load(int3(20, side, 0)).xyz, y = _MainTex.Load(int3(21, side, 0)).xyz, at = _MainTex.Load(int3(22, side, 0)).xyz;
                return distance(at, m._m03_m13_m23) + 0.2 * (distance(x, m._m00_m10_m20) + distance(y, m._m01_m11_m21));
            }

            // How long the tissue has stood still, in summed frame times so it holds at any frame
            // rate: the rider's texels a frame apart, the front centre under 2 mm/s and its
            // neighbourhood's turn under a degree a second, and the anchor under 2 mm/s. Read off
            // the second pass's own row, since a stillness test in the first, beside the hand scan,
            // slowed every step a fast GPU took near other players.
            float4 Still(float4 here, float4x4 m, float scale, float dt, bool distant, uint side)
            {
                float3 front = _MainTex.Load(int3(16, side, 0)).xyz - _MainTex.Load(int3(23, side, 0)).xyz;
                float3 turn = _MainTex.Load(int3(17, side, 0)).xyz - _MainTex.Load(int3(24, side, 0)).xyz;
                bool still = !distant && AnchorMoved(m, side) < 0.002 * dt && length(front) * scale < 0.002 * dt && length(turn) * 360 < dt;
                return float4(still ? here.x + dt : 0, 0, 0, 1);
            }

            // How long the region has lain still with no hand near, in x, counted at _Time.y in y: no
            // hand kept, nothing of the wearer's in reach moving 2 cm a second against the anchor
            // (texel 37, the first pass's), and nine particles of the front moving under 1 cm a second
            // against it. Still, not at rest, so an arm resting on it rests with its dent. Counted
            // afresh after any gap, as when the parser switched the steps off, so a wake needs _Rest
            // of quiet before it rests again. Predicted, the first pass's places carry a step of
            // gravity the fit takes back, 1.2 mm a frame at 90: taken off here.
            float4 Settling(float4 here, float4x4 m, float scale, float dt, bool distant, uint side, float3 fall)
            {
                float4 x0 = _MainTex.Load(int3(26, side, 0)), y0 = _MainTex.Load(int3(27, side, 0)), z0 = _MainTex.Load(int3(28, side, 0)), o0 = _MainTex.Load(int3(29, side, 0));
                float4x4 last = float4x4(x0.x, y0.x, z0.x, o0.x, x0.y, y0.y, z0.y, o0.y, x0.z, y0.z, z0.z, o0.z, 0, 0, 0, 1);
                bool quiet = !distant && o0.w > 0.5 && !(_MainTex.Load(int3(1, side, 0)).w > 0.5) && !Pushed(side) && _MainTex.Load(int3(37, side, 0)).x < 0.02 * scale * dt;
                int3 n = Count();
                [unroll] for (int k = 0; k < 9; k++)
                {
                    uint p = (uint) Index(int3((k % 3) * (n.x - 1) / 2, (k / 3) * (n.y - 1) / 2, n.z - 1));
                    float3 now = _MainTex.Load(Texel(p, 0)).xyz - fall, was = _MainTex.Load(Texel(p, 1)).xyz;
                    quiet = quiet && length(Into(m, now) - Into(last, was)) * scale < 0.01 * scale * dt;
                }
                bool counting = here.y > 0 && _Time.y - here.y < 0.1;
                return float4(quiet ? (counting ? here.x : 0) + dt : 0, _Time.y, 0, 1);
            }

            // Whether both passes may leave the tissue be next frame: still long enough, no hand in
            // reach this frame, and the anchor within 0.1 mm of where it last stepped. Decided here,
            // a frame ahead, since a pass cannot read its own row.
            bool Sleepy(float4x4 m, bool distant, uint side)
            {
                // Any slot, a hand or a probe's ball: it was w == 1, from before a slot's w carried
                // 10 and the gesture, so a sleeping prop woke for no hand at all. Or a world solid on
                // its probes, which takes no slot: pressed and still, it slept through the press.
                bool hand = _MainTex.Load(int3(1, side, 0)).w > 0.5 || Pushed(side);
                return _Sleep > 0 && !(_Stress > 0) && !distant && !hand && AnchorMoved(m, side) < 1e-4 && _MainTex.Load(int3(19, side, 0)).x >= _Sleep;
            }

            // The slaps, texels 80 to 84, which the draw spreads over the skin as rings on top of the
            // lattice's own give. A slap is a hand reaching the skin faster than
            // _Slap.x metres a second, judged on the skin's box taken as an ellipsoid: 84 keeps the
            // nearest palm's distance off it from last frame, so a hand already resting in fires
            // nothing, nor does the first frame after a gap. Each lands as a ring for the draw (80 and
            // 81, 82 and 83: where and when, then its depth in w, _Slap.y a metre a second up to
            // _Slap.z), in the slot with less left of its own, never over a live stronger one. The
            // hands are the slots the scan wrote last frame, less the probes', which ring below.
            // How fast the anchor moved since the state was last stepped, in metres a second; 0 with
            // no anchor kept.
            float3 AnchorVelocity(float4x4 m, float dt, uint side)
            {
                float4 at = _MainTex.Load(int3(29, side, 0));
                return at.w > 0 ? (m._m03_m13_m23 - at.xyz) / max(dt, 1e-4) : 0;
            }

            float4 Slap(int x, float4x4 m, float scale, float dt, bool distant, uint side, bool felt, Press presses[2])
            {
                float4 box = Region == 0 ? _JellyBox0 : _JellyBox1;
                float3 half = box.z > 0 ? float3(box.xy, 0.5 * box.z) : 0.5 * (float3) (Count() - 1) * _Lattice.w, mid = float3(0, 0, half.z);
                float near = 1e9;
                float3 at = 0;
                [branch] if (!distant && _MainTex.Load(int3(1, side, 0)).w > 0.5)
                {
                    float3 centre = mul(m, float4(mid, 1)).xyz;
                    [loop] for (int e = 0; e < 8; e++)
                    {
                        float4 a = _MainTex.Load(int3(2 * e, side, 0)), b = _MainTex.Load(int3(2 * e + 1, side, 0));
                        // A probe's ball has no length. Taken as a palm, its first frame read as the
                        // nearest palm closing from wherever another one was.
                        if (!(b.w > 0.5) || all(b.xyz == a.xyz)) continue;
                        float3 pb, fa, fb;
                        float rf;
                        Hand(a, b, pb, fa, fb, rf);
                        float3 seg = pb - a.xyz, p = a.xyz + seg * saturate(dot(centre - a.xyz, seg) / max(dot(seg, seg), 1e-9));
                        float3 q = (Into(m, p) - mid) / half;
                        float lq = max(length(q), 1e-6), d = (lq - 1) * min(half.x, min(half.y, half.z)) * scale - a.w;
                        if (d < near)
                        {
                            near = d;
                            at = mul(m, float4(mid + q / lq * half, 1)).xyz;
                        }
                    }
                }
                if (x == 84) return float4(near, 0, 0, _Time.y);
                float4 keep = _MainTex.Load(int3(x, side, 0)), last = _MainTex.Load(int3(84, side, 0));
                float4 r0 = _MainTex.Load(int3(80, side, 0)), s0 = _MainTex.Load(int3(81, side, 0));
                float4 r1 = _MainTex.Load(int3(82, side, 0)), s1 = _MainTex.Load(int3(83, side, 0));
                bool timed = last.w > 0 && _Time.y - last.w < 0.1;
                float speed = (last.x - near) / max(dt, 1e-4);
                bool fired = timed && last.x > 0 && last.x < 0.5 && near <= 0 && speed > _Slap.x;
                // A collider only the probes know of: a probe's push crossing its reach, the collider
                // reaching the skin, timed over the frame as a palm's crossing of the box is. Measured
                // from the skin itself, the slap's end, a film's slap read a third as fast. Last
                // frame's push is the readout's. A ring under 0.2 s old within two reaches
                // is the same collider's: a hand carrying one as well rang through the arrays a few
                // frames before the probes, and as a palm goes in the probe furthest in changes and
                // crosses faster. Such a one deepens that ring and never adds a second.
                int into = -1;
                // Felt before the dispatch, while timed.
                [branch] if (!fired && felt)
                {
                    // The probes read a slap about a third as fast as it came, so they fire at 0.4 of
                    // a hand's speed: a ball slapped in at 3 m/s read 1.05.
                    speed = 0.4 * _Slap.x;
                    float skin = max(_JellyProbeReach, 0.002);
                    [loop] for (uint i = 0; i < 2; i++)
                    [branch] if (presses[i].deep >= skin)
                    {
                        float gone = presses[i].deep, was = _MainTex.Load(int3(READOUT + presses[i].k, side, 0)).w;
                        float rate = (gone - was) / max(dt, 1e-4);
                        float3 rest = presses[i].rest;
                        float apart = 2 * (_Hand + _JellyProbeReach);
                        int same = _Time.y - r0.w < 0.2 && distance(r0.xyz, rest) < apart ? 0 : _Time.y - r1.w < 0.2 && distance(r1.xyz, rest) < apart ? 1 : -1;
                        float had = same == 0 ? s0.w : same == 1 ? s1.w : 0;
                        [flatten] if (was >= 0 && was < skin && gone >= skin && rate > speed && min(_Slap.y * rate, _Slap.z) > had)
                        {
                            speed = rate;
                            at = rest;
                            fired = true;
                            into = same;
                        }
                    }
                }
                // The tissue's own impact: its anchor stopping short, dropped onto something or
                // landing, rings it from the face its own weight carries on into, as a slap into that
                // face would; so does a yank, the tissue lagging into the face it leaves by. Only the
                // slaps that hit it rang it, never it hitting anything. Fired by more than
                // _Landing's change of speed in a frame, at that speed; a frame faster than 8 m/s is a
                // teleport, not a fall. 85 keeps the anchor's last velocity.
                [branch] if (!fired && timed && !distant)
                {
                    float4 was = _MainTex.Load(int3(85, side, 0));
                    float3 v = AnchorVelocity(m, dt, side);
                    float lost = length(was.xyz - v);
                    [branch] if (!Snapped && was.w > 0 && _Time.y - was.w < 0.1 && length(was.xyz) < 8 && lost > _Landing)
                    {
                        float3 q = Into(m, m._m03_m13_m23 + was.xyz - v) / half;
                        at = mul(m, float4(mid + q / max(length(q), 1e-6) * half, 1)).xyz;
                        speed = lost;
                        fired = true;
                    }
                }
                // A slap that drives one of a pair into the other, their skins within a cell, rings
                // both from where they meet, out from between them, as fast as it drove that way: a
                // ring either row started on the last step from its far side. Their masses close a
                // tenth as fast as the hands that squeeze them, so never struck hard enough to ring.
                [branch] if (!fired && timed && !distant && _Pair > 0 && _PairSkin.x > 0)
                {
                    uint ps = (Region ^ 1) * (side - Base + 1) + side - Base;
                    float4 ta = _MainTex.Load(int3(30, side, 0)), tb = _MainTex.Load(int3(30, ps, 0));
                    float self = (Region == 0 ? _PairSkin.x : _PairSkin.y) * scale, other = (Region == 0 ? _PairSkin.y : _PairSkin.x) * scale;
                    float dist = distance(ta.xyz, tb.xyz);
                    [branch] if (ta.w > 0 && tb.w > 0 && dist > 1e-4 && dist - self - other < _Lattice.w * scale)
                    {
                        float3 u = (tb.xyz - ta.xyz) / dist;
                        float drove = 0;
                        [unroll] for (uint k = 0; k < 4; k++)
                        {
                            uint row = k < 2 ? side : ps;
                            float4 hit = _MainTex.Load(int3(80 + 2 * (k & 1), row, 0));
                            float way = dot(normalize((k < 2 ? ta.xyz : tb.xyz) - hit.xyz), k < 2 ? u : -u);
                            if (hit.w == last.w) drove = max(drove, _MainTex.Load(int3(81 + 2 * (k & 1), row, 0)).w / _Slap.y * way);
                        }
                        [branch] if (drove > _Slap.x)
                        {
                            at = ta.xyz + (tb.xyz - ta.xyz) * (self / (self + other));
                            speed = drove;
                            fired = true;
                        }
                    }
                }
                // A grip let go, or slipped: the skin it held springs back from as far as it was
                // pulled, at the speed the tissue's own spring gives that far out, ringing from where
                // it was gripped. 120 on keep each one's last pull. A pull is held to half the
                // tissue's depth, so on a thin soft part even the furthest pull came back slower than
                // a slap's line and never rang: three quarters of that furthest rings it.
                [branch] if (!fired && timed && !distant)
                {
                    float least = min(_Slap.x, 0.75 * 0.5 * (Count().z - 1) * _Lattice.w * scale * 6.2831853 * _Hz);
                    [loop] for (uint gi = 0; gi < 8; gi++)
                    {
                        float3 gat;
                        float graw;
                        Pulled(gi, m, side, gat, graw);
                        float4 hb = _MainTex.Load(int3(2 * gi + 1, side, 0)), pull = _MainTex.Load(int3(120 + gi, side, 0));
                        bool gripped = _MainTex.Load(int3(112 + gi, side, 0)).w > 0;
                        float back = length(pull.xyz) * scale * 6.2831853 * _Hz;
                        [flatten] if (gripped && (!Fist(hb) || graw > (Count().z - 1) * _Lattice.w) && back > max(fired ? speed : 0, least))
                        {
                            at = mul(m, float4(gat, 1)).xyz;
                            speed = back;
                            fired = true;
                        }
                    }
                }
                if (!fired) return keep;
                // Taut, it rings less.
                float4 strained = _MainTex.Load(int3(86, side, 0));
                float depth = min(_Slap.y * speed, _Slap.z) / (1 + (strained.w > 0 && abs(_Time.y - strained.w) < 0.1 ? _Taut * strained.x : 0));
                float left0 = s0.w > 0 ? s0.w * saturate(1 - (_Time.y - r0.w) / _Slap.w) : 0;
                float left1 = s1.w > 0 ? s1.w * saturate(1 - (_Time.y - r1.w) / _Slap.w) : 0;
                int slot = into >= 0 ? into : left0 <= left1 ? 0 : 1;
                if (into < 0 && min(left0, left1) > depth || (x - 80) / 2 != slot) return keep;
                return (x & 1) == 0 ? float4(at, _Time.y) : float4(0, 0, 0, depth);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                int2 px = int2(i.pos.xy);
                int3 n = Count();
                uint count = n.x * n.y * n.z, side = Rows();
                // The cost meter's load rests in even phases, as cheap as a pass can be.
                if (_Pass < 1.5 && _Stress > 0 && fmod(floor(_Time.y / Window), 2) < 0.5) return _MainTex.Load(int3(px, 0));
                // Packed regions share the passes, the scan's player arrays and the draw's texture:
                // each is a block of rows with its own anchor, and from here on side is its own side
                // row. The third pass draws one row, so reads the first region's.
                // In floats: a division by a value known only at run time is emulated in integers.
                uint region = (uint) floor((px.y + 0.5) / (side + 1));
                Base = region * (side + 1);
                side += Base;
                // The rows padding the region out, before any work.
                if ((uint) px.y != side && (uint) px.x % Row + ((uint) px.y - Base) * Row >= count) return 0;
                // The probes' readout columns hold nothing on the particles' rows.
                if ((uint) px.x >= 2 * Row && (uint) px.y != side) return 0;
                Region = region;
                MineOf = region;
                Cored = (region == 0 ? _JellyCoreA0.w : _JellyCoreA1.w) > 0;
#ifdef JELLY_LATER
                // The second pass's particles take the anchor the first pass's side row worked out this
                // frame (88 to 90, its rows), and leave before anything else where they only copy:
                // far, asleep, or a predicted step's right half. The clock is the side row's own test.
                [branch] if ((uint) px.y != side)
                {
                    float4 sw = _MainTex.Load(int3(18, side, 0)), ck = _MainTex.Load(int3(25, side, 0));
                    if (sw.x > 0.5) discard;
#ifdef JELLY_SLEEP
                    [branch] if (sw.y > 0.5)
                    {
                        float4 still = _MainTex.Load(int3(px, 0));
                        return (uint) px.x < Row ? float4(still.xyz, _Time.y) : still;
                    }
#endif
                    float dt1 = min((_FixedDt > 0 ? _FixedDt : max(ck.x, 1.0 / 240)) * max(ck.w, 1), 1.0 / 30);
                    if ((uint) px.x >= Row && !(dt1 > 1.0 / 72) && !(_Stress > 0))
                        return sw.w > 0.5 ? _MainTex.Load(int3(px, 0)) : 0;
                    Mine = float4x4(_MainTex.Load(int3(88, side, 0)), _MainTex.Load(int3(89, side, 0)), _MainTex.Load(int3(90, side, 0)), float4(0, 0, 0, 1));
                }
                else Mine = AnchorOf(region);
#else
                Mine = AnchorOf(region);
#endif
                // The other region's only where it is read: the far switch measures from the first's,
                // and the side row's pair plane. Built for every pixel, far-off steps cost half again.
                Theirs = Mine;
                [branch] if (_Regions > 1.5 && (region == 1 && _Pass < 0.5 || (uint) px.y == side)) Theirs = AnchorOf(region ^ 1);
                // A third pass, where there is a rider, copies its two texels into a texture of their
                // own: the texture parser reads back a whole texture, and the whole state costs it
                // ten times two texels. Its third texel says whether the steps should run at all: the
                // parser switches their blitters off far from the viewer or well behind them, and on
                // again once near and in view. Off, the state keeps its last far switch, so the same
                // rule brings them back.
                if (_Pass > 1.5)
                {
                    if (px.x < 2) return _MainTex.Load(int3(16 + px.x, side, 0));
#ifdef JELLY_POINTS
                    [branch] if (px.x >= 3) return Feedback(px.x);
#endif
                    bool off = Away(_MainTex.Load(int3(18, side, 0)).x > 0.5);
#ifdef JELLY_POINTS
                    [branch] if (!off && _Rest > 0) off = Resting();
#endif
                    // On for one frame's time in each step's, by the clock, not by frames, so the
                    // parser's few frames of lag only shift which. At or under the rate, every frame.
                    [branch] if (!off)
                    {
                        float rate = Halved() ? _Rate.y : _Rate.x, frame = _MainTex.Load(int3(25, side, 0)).x;
                        off = rate > 0 && frame > 0 && frac(_Time.y * rate) >= frame * rate;
                    }
                    return float4(off ? 0 : 1, 0, 0, 1);
                }
#ifdef JELLY_POINTS
                // A points material only ever runs the block above: the step left out of it, the
                // compiler took twenty minutes over code it never runs.
                return 0;
#else
                float4 here = _MainTex.Load(int3(px, 0));
                float4x4 m = Mine;
                float scale = length(m._m00_m10_m20);
                bool anchored = scale > 1e-6;
                // The step is held while the frame time, smoothed, stays within 15% of it: where the
                // tissue rests moves with the step, so stepping each uneven frame by its own time
                // kept a settled prop trembling, never asleep. The first pass decides; the second
                // reads its answer.
                float4 clock = _MainTex.Load(int3(25, side, 0));
                float real = clamp(unity_DeltaTime.x, 1.0 / 240, 1.0 / 30), smooth = clock.y > 0 ? lerp(clock.y, real, 0.05) : real;
                float held = _Pass > 0.5 ? max(clock.x, 1.0 / 240) : clock.x > 0 && abs(smooth - clock.x) < 0.15 * clock.x ? clock.x : smooth;
                // Skipped by the rate, the step takes the frames it missed, counted against this
                // frame's own time so a slow frame is not taken twice. A gap past a tenth of a second
                // is a wake and steps one frame, as before. The first pass counts; the second reads its count.
                // Not asked whether it is halved: && runs both sides, and that alone cost every step 10%.
                float frames = clock.w;
                [branch] if (_Pass < 0.5)
                {
                    float f = clock.z > 0 ? round((_Time.y - clock.z) / real) : 1;
                    frames = f >= 2 && f * real < 0.1 ? f : 1;
                }
                frames = max(frames, 1);
                float dt = min((_FixedDt > 0 ? _FixedDt : held) * frames, 1.0 / 30);
                FrameDt = dt;
                // An anchor whose speed changes by more than 8 m/s in a frame has snapped to a new pose:
                // an emote, sitting, a limb's tracking coming back. No limb moves so and a fall stops
                // slower, as the landing's own 8 m/s says. Left behind by the jump, a part on an arm
                // sat 0.5 m off its rest for a tenth of a second; now it and its mass ride it. The first
                // pass decides and signs 85 with it; the second reads its answer.
                float4 sped = _MainTex.Load(int3(85, side, 0));
                Snapped = _Pass < 0.5 ? anchored && sped.w != 0 && _Time.y - abs(sped.w) < 0.1 && length(AnchorVelocity(m, dt, side) - sped.xyz) > 8 : sped.w < 0;
                int passes = dt > 1.0 / 72 ? 2 : 1;
                float h = dt / passes;
                // The meter's load always takes a whole step, the cost it is there to show.
                bool predicted = passes == 1 && !(_Stress > 0);
                // Far from the viewer's head or well behind them the tissue hangs rigid. The first pass
                // decides and the second reads its answer. No head, as without a humanoid avatar, counts
                // as near.
                float4 switches = _MainTex.Load(int3(18, side, 0));
                bool wasDistant = switches.x > 0.5, distant = wasDistant;
                if (_Pass < 0.5) distant = !(_Stress > 0) && Away(wasDistant);
                // Asleep: settled, untouched, on an anchor that has not moved. The second pass decided
                // it last frame, so both passes take the same way; a moved anchor or a hand wakes it
                // the frame after.
#ifdef JELLY_SLEEP
                bool asleep = switches.y > 0.5 && !distant;
#else
                bool asleep = false;
#endif
                // The probes' frame only where it is read: the side row, and an awake step of a region
                // the world pushes.
                Probed = false;
                [branch] if ((uint) px.y == side || SolidRegion(region) && !distant && !asleep && Pushed(side)) Probed = Probing(region, Back);

                bool rider = false;
                if ((uint) px.y == side)
                {
                    if (!anchored) return 0;
                    // The second pass never runs this, so its variant is compiled without it.
#ifndef JELLY_LATER
                    if (_Pass < 0.5)
                    {
                        if (px.x == 18) return float4(distant, asleep, 0, 1);
                        if (px.x == 25) return float4(held, smooth, _Time.y, frames);
                        // The probes as Felt has them, for the scan's texels and the slaps': one copy of
                        // it between them, as a copy each took the compiler past its time limit. The
                        // scan's only once a cloth probe was pushed last frame, as the readout
                        // has it: a collider is felt a reach off the skin, so the frame this starts late
                        // by is still short of it, and at rest this is most of the step's cost. World
                        // probes press as flat faces in the step, never as hands, so the scan takes the
                        // cloth's alone. The slaps' while their last frame was timed. Each test
                        // branched: `&&` works out both sides.
                        Press presses[2];
                        presses[0] = (Press) 0;
                        presses[1] = (Press) 0;
                        bool feel = false, felt = false;
                        uint upto = PROBES;
#ifdef JELLY_SCAN
                        [branch] if (px.x < 16 && !distant)
                        {
                            upto = min((uint) _JellyProbes, WorldFrom(Region));
                            [loop] for (uint k = 0; k < upto && k < PROBES && !feel; k++) feel = _MainTex.Load(int3(READOUT + k, side, 0)).w > 0.001;
                        }
#endif
                        [branch] if (px.x >= 80 && px.x < 84 && !distant)
                        {
                            float4 last = _MainTex.Load(int3(84, side, 0));
                            feel = last.w > 0 && _Time.y - last.w < 0.1;
                        }
                        [branch] if (feel && Probed)
                        {
                            Felt(Region, upto, Back, presses);
                            felt = true;
                        }
#ifdef JELLY_SCAN
                        [branch] if (px.x < 16)
                        {
                            if (distant) return 0;
                            return Scan(px.x, m, scale, side, felt, presses);
                        }
#endif
                        [branch] if (px.x >= 32 && px.x <= 34 && _Solid > 0) return Bulk(px.x - 32, m, scale, distant, side);
                        // How far the wearer's capsules in reach moved against the anchor since last frame,
                        // for the rest's count.
                        [branch] if (px.x == 37) return float4(Shifted(m, scale, side, false), 0, 0, 1);
                        // A readout for the verify: the plane's distances as the step reads them.
                        [branch] if (px.x == 40)
                        {
                            float zc = 0.5 * (Count().z - 1) * _Lattice.w;
                            uint ps = (region ^ 1) * (side - Base + 1) + side - Base;
                            float4 ta = _MainTex.Load(int3(30, side, 0)), tb = _MainTex.Load(int3(30, ps, 0));
                            float3 ca = ta.w > 0 ? ta.xyz : mul(m, float4(0, 0, zc, 1)).xyz;
                            float3 cb = tb.xyz;
                            [branch] if (!(tb.w > 0)) cb = mul(Anchor(region ^ 1), float4(0, 0, zc, 1)).xyz;
                            return float4(distance(ca, cb), length(_PairOffset.xyz) * scale, _PairReach.z * scale, scale);
                        }
                        // Each of the wearer's own capsules' ends for the draw, as the hands' slots are,
                        // and on its far end whether it is in reach: judged here side by side, the
                        // second pass gathers them into 41. One texel judging all sixteen in turn was a
                        // seventh of the step.
                        [branch] if ((px.x >= 42 && px.x <= 57) || (px.x >= 64 && px.x <= 79))
                        {
                            float3 a, b;
                            float r;
                            uint c = px.x < 64 ? (uint) (px.x - 42) >> 1 : 8 + ((uint) (px.x - 64) >> 1);
                            Own(c, scale, a, b, r);
                            bool reach = false;
                            [branch] if ((px.x & 1) && r > 0) reach = InReach(a, b, r, m, scale, side);
                            float hold;
                            [branch] if (c == 2 || c == 6) Held(a, b, r, r, false, a, b, m, scale, hold);
                            return (px.x & 1) == 0 ? float4(a, r) : float4(b, reach);
                        }
                        // The draw's last guard: the plane midway between the two skins where they reach
                        // toward each other, moved on by half however far they overlapped as built, and
                        // the give it eases skin past it into.
                        [branch] if (px.x == 58 || px.x == 59)
                        {
                            if (!(_Pair > 0 && _PairSkin.x > 0) || distant) return 0;
                            float zc = 0.5 * (Count().z - 1) * _Lattice.w;
                            uint ps = (region ^ 1) * (side - Base + 1) + side - Base;
                            float4 ta = _MainTex.Load(int3(30, side, 0)), tb = _MainTex.Load(int3(30, ps, 0));
                            float3 ca = ta.w > 0 ? ta.xyz : mul(m, float4(0, 0, zc, 1)).xyz;
                            float3 cb = tb.xyz;
                            [branch] if (!(tb.w > 0)) cb = mul(Anchor(region ^ 1), float4(0, 0, zc, 1)).xyz;
                            float dist = distance(ca, cb);
                            if (!(dist > 1e-4)) return 0;
                            float self = (region == 0 ? _PairSkin.x : _PairSkin.y) * scale, partner = (region == 0 ? _PairSkin.y : _PairSkin.x) * scale;
                            float overlap = max(self + partner - length(_PairOffset.xyz) * scale, 0);
                            float3 u = (cb - ca) / dist;
                            float meet = Meet(m, ps);
                            if (!(meet > 0)) return 0;
                            return px.x == 58 ? float4(u, dot(ca, u) + 0.5 * (self + dist - partner) + 0.5 * overlap) : float4(0.004 * scale, meet, 0, 1);
                        }
                        [branch] if ((px.x == 30 || px.x == 31 || px.x == 86) && _MassHz > 0)
                        {
                            uint side0 = side - Base;
                            return Mass(px.x, m, scale, dt, distant, side, region, (region ^ 1) * (side0 + 1) + side0);
                        }
                        if (px.x >= 80 && px.x <= 84) return Slap(px.x, m, scale, dt, distant, side, felt, presses);
                        if (px.x == 85) return float4(AnchorVelocity(m, dt, side), Snapped ? -_Time.y : _Time.y);
                        // The anchor's rows, for the second pass's particles.
                        if (px.x >= 88 && px.x <= 90) return px.x == 88 ? m[0] : px.x == 89 ? m[1] : m[2];
                        // Each hand slot's grip (112 to 119), as Pulled reads it: a fist closed on the
                        // skin grips it where it closed, or where Slid takes it, until it opens or has
                        // pulled twice as far as the skin follows; a fist closed off the skin, or that
                        // came in closed, never grips, as a punch is no grab. Its pull (120 to 127) kept
                        // for the ring when it lets go.
                        [branch] if (px.x >= 112 && px.x < READOUT)
                        {
                            uint e = ((uint) px.x - 112) & 7;
                            float4 a = _MainTex.Load(int3(2 * e, side, 0)), b = _MainTex.Load(int3(2 * e + 1, side, 0)), was = _MainTex.Load(int3(112 + e, side, 0));
                            float3 at;
                            float raw;
                            float4 d = Pulled(e, m, side, at, raw);
                            if (px.x >= 120) return float4(d.xyz, d.w > 0 ? _Time.y : 0);
                            if (distant || !Fist(b)) return 0;
                            if (was.w > 0) return raw > (Count().z - 1) * _Lattice.w ? float4(0, 0, 0, -1) : float4(Slid(was.xyz, Into(m, Knuckles(a, b)), Into(m, a.xyz) - Into(m, b.xyz)), was.w);
                            float3 pb, fa, fb;
                            float rf;
                            Hand(a, b, pb, fa, fb, rf);
                            return was.w == 0 && Gap(a.xyz, pb, a.w, m, scale, side) <= 0.01 * scale ? float4(Into(m, Knuckles(a, b)), _Time.y) : float4(0, 0, 0, -1);
                        }
                        // Whether any probe below reads pushed, for Pushed.
                        [branch] if (px.x == 87)
                        {
                            bool any = false;
                            [loop] for (uint k = 0; k < (uint) _JellyProbes && k < PROBES && !any; k++)
                            {
                                float3 at, way;
                                any = (Probed ? Probe(Region, k, Back, at, way) : -1) > 0.002;
                            }
                            return float4(any, 0, 0, 1);
                        }
                        // Each probe as the scan reads it, where it is and how far pushed: the slaps' last
                        // frame, and a readout for the verify.
                        [branch] if (px.x >= READOUT && px.x < READOUT + PROBES)
                        {
                            float3 at, way;
                            float gone = Probed ? Probe(Region, px.x - READOUT, Back, at, way) : -1;
                            return float4(at, gone);
                        }
                        return here;
                    }
#endif
                    if (px.x >= 26 && px.x <= 29)
                        return float4(px.x == 26 ? m._m00_m10_m20 : px.x == 27 ? m._m01_m11_m21 : px.x == 28 ? m._m02_m12_m22 : m._m03_m13_m23, 1);
                    // Which of the wearer's own capsules are in reach, a bit each, from the first pass's
                    // judgements. Read a frame late by the first pass, so the reach has room.
                    [branch] if (px.x == 41)
                    {
                        uint bits = 0;
                        [unroll] for (uint c = 0; c < 16; c++) bits |= _MainTex.Load(int3(c < 8 ? 43 + 2 * c : 49 + 2 * c, side, 0)).w > 0.5 ? 1u << c : 0;
                        return float4(distant ? 0 : bits, 0, 0, 1);
                    }
#ifdef JELLY_SLEEP
                    if (px.x == 18) return float4(distant, Sleepy(m, distant, side), 0, 1);
                    if (asleep) return here;
                    if (px.x == 19) return Still(here, m, scale, dt, distant, side);
                    if (px.x >= 20 && px.x <= 22) return float4(px.x == 20 ? m._m00_m10_m20 : px.x == 21 ? m._m01_m11_m21 : m._m03_m13_m23, 1);
                    if (px.x == 23 || px.x == 24) return _MainTex.Load(int3(px.x - 7, side, 0));
#else
                    // Worn, resting: the third pass then switches the steps off and the draw keeps the
                    // state as it lies. Only after a count stepped without a gap.
                    if (px.x == 18)
                    {
                        float4 rest = _MainTex.Load(int3(35, side, 0));
                        return float4(distant, _Rest > 0 && !distant && rest.y > 0 && _Time.y - rest.y < 0.1 && rest.x >= _Rest, 0, 1);
                    }
                    if (px.x == 35) return Settling(here, m, scale, dt, distant, side, predicted ? Gravity * h * h : 0);
#endif
                    // The rider takes the particles' own path below, so the step is written out once.
                    rider = (px.x == 16 || px.x == 17) && _Rider > 0;
                    if (!rider) return here;
                }
                int3 l;
                uint p;
                float3 home;
                float4 swing = float4(0, 0, 0, 1), q;
                [branch] if (rider)
                {
                    // The front centre, homed on the anchor alone.
                    int3 n = Count();
                    l = int3(n.x / 2, n.y / 2, n.z - 1);
                    p = (uint) Index(l);
                    home = mul(m, float4(Rest(l), 1)).xyz;
                }
                else
                {
                    // Far off, both passes leave the state be, as cheap as a pass can be: the surface
                    // draws the rest shape off the far switch, and a stale state starts again at rest.
                    // The second discards, writing nothing. Compiled into its variant alone: a discard
                    // anywhere in the first, which carries the hand scan, slowed a fast GPU's near steps,
                    // even one it never reached.
                    [branch] if (distant)
                    {
#ifndef JELLY_SCAN
                        if (_Pass > 0.5) discard;
#endif
                        return here;
                    }
                    // Asleep, the same, but stamped, so it reads as stepping and wakes where it lay.
                    [branch] if (asleep) return _Pass > 0.5 && (uint) px.x < Row ? float4(here.xyz, _Time.y) : here;
                    p = (uint) px.x % Row + ((uint) px.y - Base) * Row;
                    if (p >= count) return 0;
                    int half_ = (uint) px.x >= Row;
                    l = Place(p);
                    // No anchor yet: nothing to hang from. Marked, so the surface can say so.
                    if (!anchored) return half_ == 0 ? float4(0, 0, 0, -1) : 0;
                    // The fit's right half was settled in the first pass: no home to work out.
                    if (predicted && _Pass > 0.5 && half_ == 1) return here;
                    home = Home(l, m, side, swing);
                    if (predicted && _Pass < 0.5)
                    {
                        // Where each particle is headed, for the second pass to fit, w 0 for one put back
                        // at rest; the right half already takes where it was.
                        float4 s = _MainTex.Load(Texel(p, 0)), was = _MainTex.Load(Texel(p, 1));
                        [branch] if (Snapped) { s.xyz = Carried(s.xyz, m, side); was.xyz = Carried(was.xyz, m, side); }
                        bool rest = Fresh(s, was, home);
                        if (half_ == 1) return float4(rest ? home : s.xyz, h);
                        if (rest) return float4(home, 0);
                        if (Pinned(l)) return float4(home, 1);
                        return float4(Ahead(s, was, l, side, home, h) + Gravity * h * h, 1);
                    }
                    // A whole step's right half only moves along, so half the texture does next to no work.
                    if (!predicted && half_ == 1)
                    {
                        float4 s = _MainTex.Load(Texel(p, 0));
                        [branch] if (Snapped && _Pass < 0.5) s.xyz = Carried(s.xyz, m, side);
                        return float4(Fresh(s, here, home) ? home : s.xyz, h);
                    }
                }
                // The fit from the first pass's predictions, or a whole step. One call: each call
                // writes the whole step out again.
                float3 at = Moved(p, l, m, scale, h, home, predicted, distant, side, swing, q);
                [branch] if (rider) return Rider(px.x, m, scale, at, q);
                return float4(at, _Time.y);
#endif
            }
            ENDCG
        }
    }
    Fallback Off
}
";

        internal const string CommonInclude = @"// The jelly's lattice, shared by its passes. Written by YAPS; edits here are overwritten.
#ifndef JELLY_COMMON
#define JELLY_COMMON

Texture2D<float4> _MainTex;
float4 _Lattice;
// One anchor a region. Packed, each region is a block of rows of the state, its side row last.
// After the four, the wearer's own bones (YapsJelly's ownBones).
float4x4 _JellyAnchor[32];
float _Damp;
// The first row of this pixel's region, set once a pixel and added to every lattice texel.
static uint Base = 0;

static const uint Row = 64;
static const float Window = 0.5;
// This pixel's region holds a core round its bone instead of its back face.
static bool Cored = false;
// The step's share of gravity: 1 hangs like flesh, 0 keeps the modelled shape however the body lies.
float _Gravity;
#define Gravity float3(0, -9.81 * _Gravity, 0)
// The rigid fit's rotation steps. Each starts from the anchor's turn, which a neighbourhood's own
// is never far from, so three land where six did.
static const int Turns = 3;

int3 Count() { return int3(_Lattice.xyz); }
// A region's lattice rows, padded so with its side row it is a whole number of eight: a block of
// rows the GPU runs together never holds both a side row's work and particles'.
uint Rows() { int3 n = Count(); return ((uint) (n.x * n.y * n.z + Row - 1) / Row + 8) / 8 * 8 - 1; }
int Index(int3 l) { int3 n = Count(); return (l.z * n.y + l.y) * n.x + l.x; }
// In floats, where a division is cheap and exact enough for any lattice a texture holds; integer
// division by a value only known at run time is emulated, a few dozen instructions.
int3 Place(uint p)
{
    int3 n = Count();
    float row = floor((p + 0.5) / n.x), slab = floor((row + 0.5) / n.y);
    return int3(p - row * n.x, row - slab * n.y, slab);
}
// Unsigned, so a neighbour's index wrapped below 0 lands outside the texture, where a load reads 0.
int3 Texel(uint p, int half_) { return int3(p % Row + half_ * Row, p / Row + Base, 0); }
float3 Rest(int3 l) { int3 n = Count(); return float3(l.x - 0.5 * (n.x - 1), l.y - 0.5 * (n.y - 1), l.z) * _Lattice.w; }

// A fresh texture, a new copy of the avatar, garbage, a teleport, or a state left still a tenth
// of a second, far off or switched off: start at rest.
bool Fresh(float4 s, float4 was, float3 home)
{
    return !(was.w > 0 && was.w < 0.1) || !(abs(s.x) + abs(s.y) + abs(s.z) + abs(was.x) + abs(was.y) + abs(was.z) < 1e6)
        || !(distance(s.xyz, home) < 1) || !(_Time.y - s.w < 0.1);
}

float4 Mul(float4 a, float4 b) { return float4(a.w * b.xyz + b.w * a.xyz + cross(a.xyz, b.xyz), a.w * b.w - dot(a.xyz, b.xyz)); }

float3x3 Turn(float4 q)
{
    float x = q.x, y = q.y, z = q.z, w = q.w;
    return float3x3(1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w),
                    2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w),
                    2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y));
}

// Of the anchor alone, so every lane takes the same way.
float4 Quat(float3x3 m)
{
    float t = m[0][0] + m[1][1] + m[2][2];
    float4 q;
    [branch] if (t > 0) { float s = sqrt(t + 1) * 2; q = float4((m[2][1] - m[1][2]) / s, (m[0][2] - m[2][0]) / s, (m[1][0] - m[0][1]) / s, 0.25 * s); }
    else
    {
        [branch] if (m[0][0] > m[1][1] && m[0][0] > m[2][2]) { float s = sqrt(1 + m[0][0] - m[1][1] - m[2][2]) * 2; q = float4(0.25 * s, (m[0][1] + m[1][0]) / s, (m[0][2] + m[2][0]) / s, (m[2][1] - m[1][2]) / s); }
        else
        {
            [branch] if (m[1][1] > m[2][2]) { float s = sqrt(1 + m[1][1] - m[0][0] - m[2][2]) * 2; q = float4((m[0][1] + m[1][0]) / s, 0.25 * s, (m[1][2] + m[2][1]) / s, (m[0][2] - m[2][0]) / s); }
            else { float s = sqrt(1 + m[2][2] - m[0][0] - m[1][1]) * 2; q = float4((m[0][2] + m[2][0]) / s, (m[1][2] + m[2][1]) / s, 0.25 * s, (m[1][0] - m[0][1]) / s); }
        }
    }
    return normalize(q);
}

// The best rigid fit of l's 3x3x3 neighbourhood, from where each of them is headed: its turn
// (Mueller and others, 2016, started from q, the anchor's), its centre c and its rest centre rc.
// Each neighbour is read once, at an offset the whole pass shares, and taken against where l is
// headed, so the sums keep their digits; one out of the lattice counts 0. With predicted, where
// each is headed is the first pass's, its texel; otherwise it is worked out here, with gravity,
// the same for all, left out of both sides of each difference. Behind a particle one in from the
// back face are the anchor's own places, since the back face rides it.
float4 Fit(uint p, int3 l, float3 ahead, float3 base, float kh, float4x4 m, float h, float3 home, float4 q, bool predicted, float3x3 sw, out float3 c, out float3 rc, out float4 lin)
{
    int3 n = Count();
    bool3 lo = l > 0, hi = l < n - 1;
    float s = _Lattice.w;
    float3 mx = m._m00_m10_m20 * s, my = m._m01_m11_m21 * s;
    float3 behind = home - mul((float3x3) m, mul(sw, float3(0, 0, s))) - Gravity * h * h;
    int sy = n.x, sz = n.x * n.y;
    // The differences summed, and summed signed by each axis's offset.
    float3 sd = 0, ax = 0, ay = 0, az = 0;
    [unroll] for (int dz = -1; dz <= 1; dz++)
    [unroll] for (int dy = -1; dy <= 1; dy++)
    [unroll] for (int dx = -1; dx <= 1; dx++)
    {
        // l itself adds nothing.
        if (dx != 0 || dy != 0 || dz != 0)
        {
            bool ok = (dx < 0 ? lo.x : dx > 0 ? hi.x : true) && (dy < 0 ? lo.y : dy > 0 ? hi.y : true) && (dz < 0 ? lo.z : dz > 0 ? hi.z : true);
            uint o = p + (uint) (dx + dy * sy + dz * sz);
            // Predicted, where each is headed is the first pass's, with no velocity to read. One
            // body for both: the step's single Moved call carries predicted as it comes.
            float3 x = _MainTex.Load(Texel(o, 0)).xyz, w = x;
            [branch] if (!predicted) w = _MainTex.Load(Texel(o, 1)).xyz;
            float3 d = predicted ? x - ahead : x + (x - w) * kh - base;
            if (!predicted && dz < 0 && !Cored) d = l.z == 1 ? behind + dx * mx + dy * my - base : d;
            d = ok ? d : 0;
            sd += d;
            if (dx != 0) ax += dx * d;
            if (dy != 0) ay += dy * d;
            if (dz != 0) az += dz * d;
        }
    }
    // How many neighbours l has along each axis, and what their offsets sum to along it.
    float3 cnt = 1 + (float3) lo + (float3) hi, bal = (float3) hi - (float3) lo;
    float k = cnt.x * cnt.y * cnt.z;
    float3 sr = bal * float3(cnt.y * cnt.z, cnt.x * cnt.z, cnt.x * cnt.y) * s;
    c = ahead + sd / k;
    rc = Rest(l) + sr / k;
    // The spread of where they are headed against their rest places, about both centres, a column
    // for each rest axis.
    float3 a0 = ax * s - sd * (sr.x / k), a1 = ay * s - sd * (sr.y / k), a2 = az * s - sd * (sr.z / k);
    // Its own linear fit, A = Apq Aqq^-1, with its volume kept (Mueller and others, 2005): over a box
    // of offsets Aqq is diagonal, each axis's spread about the rest centre. Scaled to the anchor's
    // own scale cubed, l's place on it, and the volume kept the one the homes' stretch sw gives;
    // w 0 where it has folded or has no spread along an axis.
    float3 side2 = float3(cnt.y * cnt.z, cnt.x * cnt.z, cnt.x * cnt.y);
    float3 spread = s * s * ((float3) lo + (float3) hi - bal * bal * side2 / k) * side2;
    lin = 0;
    if (all(spread > 1e-12))
    {
        float3 c0 = a0 / spread.x, c1 = a1 / spread.y, c2 = a2 / spread.z;
        float sc = length(m._m00_m10_m20), vol = dot(c0, cross(c1, c2)) / (sc * sc * sc);
        float3 r = Rest(l) - rc;
        if (vol > 0.05) lin = float4((c0 * r.x + c1 * r.y + c2 * r.z) * pow(vol / determinant(sw), -1.0 / 3.0), 1);
    }
    [unroll] for (int it = 0; it < Turns; it++)
    {
        float3x3 t = Turn(q);
        float3 t0 = float3(t[0][0], t[1][0], t[2][0]), t1 = float3(t[0][1], t[1][1], t[2][1]), t2 = float3(t[0][2], t[1][2], t[2][2]);
        float3 spin = (cross(t0, a0) + cross(t1, a1) + cross(t2, a2)) / (abs(dot(t0, a0) + dot(t1, a1) + dot(t2, a2)) + 1e-12);
        float w = length(spin);
        if (w > 1e-9) q = normalize(Mul(float4(spin / w * sin(0.5 * w), cos(0.5 * w)), q));
    }
    return q;
}

#endif
";
    }
}
#endif
