// The jelly: a GPU soft body per squash pair, stepped by the CCK's blitters, drawn by the body
// shader's Jelly role. Built over a squash; a pair it refuses keeps the squash's dent.
//
// A particle lattice in a float render texture: position in the left half, last pass's in the
// right. Two blitters step it into a twin and back: each particle pulled to the rigid fit of its
// 3x3x3 neighbourhood, back face on the provider's anchor, hands pushing it aside. A third pass
// writes the far switch and animator outputs for the texture parser. Nothing baked or synced.
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
    internal static partial class YapsJelly
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
            public bool probes;
            // How far off the skin a collider pushes a probe, the least: spread too thin for it to cover
            // the skin, it grows to the gap.
            public float probeReach = 0.04f;
            // On whatever the squash component says.
            public bool twitch, slide;
            // The menu and animator wired.
            public bool wire = true;
        }

        // How the build tunes a part its bone carries, before the part's feel scales it. A hand moves
        // none of the whole: the cloth's colliders move the bones and the tissue only dents. At 0.525
        // a hand held deep leaned the whole 61 mm and drew the part out 1.3 times its length, and
        // on a part as small as the hand it read as a cone along the arm.
        internal const float CarriedHz = 2.5f, CarriedZeta = 1f, CarriedGive = 0f, CarriedLimit = 0.3f;
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
            // Not a part the bones carry: it reshaped the whole along the hand, the dent's job.
            mat.SetFloat("_Squeeze", mat.GetFloat("_MassCarried") > 0f ? 0f : 0.6f);
            // Flattened about its own middle, a squeezed part's inner side pulls off the cleavage;
            // the wholes close in to 0.55 of their distance at rest (was 0.85), so a pair squeezed
            // from the outsides presses into each other instead of parting top and bottom.
            mat.SetFloat("_PairKeep", 0.55f);
            // A part alone has no partner to meet.
            mat.SetFloat("_Pair", (s == null || s.meet) && mat.GetFloat("_Regions") > 1.5f ? 1f : 0f);
            // The squash's Grip slides: 2 cm of drag, then the grip goes with the fist.
            mat.SetFloat("_GripSlip", slide || s != null && s.gripSlides ? 0.02f : 0f);
            // Other players' hands, in or out: the tissue keeps a hand out of the skin, so a share has
            // nothing to scale.
            float hands = s == null || s.othersHands > 0f ? 1f : 0f;
            mat.SetVector("_JellyArrays", new Vector4(hands, hands, hands, hands));
            EditorUtility.SetDirty(mat);
        }

        // A worn part retuned from the component as its knobs move. Every avatar part is carried by
        // its cloth; only a probe's free mass (-jellyMass) is not, and it keeps its build values.
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
        internal const int Row = 64, ProbesPer = 24;
        // The most probes a region takes, and where their readout starts on
        // its side row: columns of their own past the particles'.
        internal const int ProbesMax = 64, Readout = 2 * Row;


        // Any earlier one on the avatar goes, only that pair's when named; how many. An instance is
        // named after its pair, Jelly_Spike and its pair; older builds left Jelly_Spike or Jelly Spike.
        internal static int Unworn(Transform avatar, string only = null)
        {
            // The colliders that pressed instead, back in their pair's cloth: their ends mark them and
            // name the pair, the breasts when they name none, as before any other pair.
            var ends = avatar.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Jelly collider ") && t.parent != null)
                // The whole name: as a piece of it, "Thighs" took the ends of "Thighs inner" too.
                .Where(t => only == null || string.Equals(t.name.Split(new[] { ' ' }, 5).ElementAtOrDefault(4) ?? "breast", only, StringComparison.OrdinalIgnoreCase)).ToList();
            var skeleton = Skeleton(avatar.GetComponentInChildren<Animator>());
            foreach (var pair in ends.GroupBy(t => t.name.Split(new[] { ' ' }, 5).ElementAtOrDefault(4)))
            {
                // By the bones its jelly rides, as put on, else by its name.
                var worn = avatar.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => pair.Key != null && t.name == WornName(pair.Key));
                var carrier = worn != null ? worn.GetComponentsInChildren<CVRMaterialDataProvider>(true).FirstOrDefault(q => q.transforms != null && q.transforms.Length > 0)?.transforms[0] : null;
                var part = carrier != null && !carrier.IsChildOf(worn) ? PartFrom(carrier, skeleton) : null;
                // Each collider back into the cloth it came out of, as its ends' marks list; a build
                // from before the marks lists none, and it goes into every cloth of its kind.
                foreach (var held in pair.GroupBy(t => t.parent))
                {
                    var listed = held.SelectMany(t => t.GetComponentsInChildren<Transform>(true)).Where(t => t.name.StartsWith(ListedMark))
                        .Select(t => ClothAt(avatar, t.name.Substring(ListedMark.Length))).Where(b => b != null).ToList();
                    PairCollide(avatar, part ?? (pair.Key != null ? PartOf(pair.Key) : Breast), held.Key.GetComponents<Component>().Where(IsCollider),
                        with: true, only: listed.Count > 0 ? listed : null);
                }
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
        internal static float MostCellsOverride;
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

        // One lattice per breast in one texture and pass pair, each swinging on its root and keeping
        // off the other. Fitted to the skin the squash gives it at least half of, 1 cm of room, back
        // face 0.5 cm into the chest; both the larger's size, BreastCells to MostCells cells, near
        // cubes. The squash materials gain the Jelly role and the squash leaves the pair to it.
        //
        // The first squash part whose name matches want, hung from its skin's trunk bone. Assets
        // in folder (folder_n for the nth part), step shader in shaders. Returns "On ..." when done.
        // Skin depth under each box face on a DepthGrid square: a hand is held by its depth past
        // the skin, not the face. A face touches only the outermost skin, 8 cm off where a poke came
        // in on a seat, so nothing dented; whole-cell maxima read 6 of those 8 cm short. Rows per
        // face, x y z, plus then minus, first region's six faces then the second's.
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
            // Filled as a new texture and copied over it, as the squash's textures are, and saved at
            // once: rewritten where it lay, a tester's map reached disk as uninitialised memory.
            string path = folder + "/Jelly_Depth.asset";
            var fresh = new Texture2D(G, 12 * G, TextureFormat.RFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            fresh.SetPixels(pixels);
            fresh.Apply(false);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null)
            {
                AssetDatabase.CreateAsset(fresh, path);
                tex = fresh;
            }
            else
            {
                EditorUtility.CopySerialized(fresh, tex);
                Object.DestroyImmediate(fresh);
            }
            // Flagged as the CCK's upload checks ask: left Read/Write enabled, it failed validation
            // and blocked the upload. Never streamed, unlike the squash's: the step's materials are on
            // no mesh, where the CCK warns of streaming.
            var flags = new SerializedObject(tex);
            flags.FindProperty("m_IsReadable").boolValue = false;
            flags.FindProperty("m_StreamingMipmaps").boolValue = false;
            flags.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(tex);
            AssetDatabase.SaveAssetIfDirty(tex);
            return tex;
        }

        internal static string PairOn(Transform picked, Regex want, string folder, string shaders, Options o, BridgeReport report, YapsSquash.SoftParts found = null)
        {
            var laps = new List<string>();
            var lapClock = System.Diagnostics.Stopwatch.StartNew();
            void Lap(string step) { laps.Add($"{step} {lapClock.ElapsedMilliseconds}"); lapClock.Restart(); }
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
            Lap("Measure");
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
            float aim = CellAim;
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
            // The pair's cloth swings the bones; the jelly is the flesh on them (dents, squash, a
            // viscous fill-back). Its mass only gives to a hand: stiff and fully damped, no swing on
            // top of the cloth's. Left free at 0.75 Hz it trailed every hop and drew the breast out
            // like taffy. Options.mass swaps the mass in for the cloth; noMass leaves it out.
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
            Lap("Core");
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
            bool clothed = PairCloth(root, part).Count > 0;
            var depthMap = DepthMap(sides, shares, at, body, chest, undo, los, his, into);
            Lap("Depth map");
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
            Lap("Made");
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
                // Probes: the client adds other players' hand colliders to every MagicaCloth
                // (CVRDynamicBoneManager.UpdateComponents; a capsule from the fingers if none), so a
                // spring cloth of loose points on the skin shows where a collider pushes. On the
                // chest, not the breast bone, or the breast's cloth takes each for a chain bone.
                // MagicaCloth only: DynamicBone probes saw nothing more and dented a quarter less.
                bool probing = o.probes;
                var spotsOf = new List<Vector3>[sides];
                float reach = o.probeReach;
                if (probing)
                {
                    // Over all the skin a hand reaches, all but where it meets the body, spread farthest
                    // first from its frontmost, every side as many, until none of it is further than a
                    // probe's reach from one or the room runs out. A collider anywhere on it is then in
                    // some probe's reach: on the front alone, twelve a breast left 11 cm between them,
                    // and a finger's collider, or a 3 cm ball, pressed 4 cm into a gap was never felt.
                    // ponytail: the room caps the probes, so on a large part the reach grows to the gap;
                    // past it a press sits nearer one probe than the next.
                    int room = ProbesMax;
                    var picks = new List<Vector3>[sides];
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
                    for (int side = 0; side < sides; side++) spotsOf[side] = picks[side].Take(probes).ToList();
                }
                AddSkin(made, provider, hangs, probing ? spotsOf : null, 0.5f * (n.z - 1) * spacing, "Put the breasts on the jelly", into, reach);
                if (probing && YapsSquash.SettingsOf(root)?.probeMarkers == true)
                    Markers(made, core.state, n, Mathf.RoundToInt(provider.materials[0].GetFloat("_JellyProbes")), into, shaders);
            }

            // The wearer's own arms, hands and thighs press the breasts as other players' hands do:
            // eight capsules bone to bone (provider transforms after the four anchors), each as thick
            // as the median skin distance round its bones. The client's arrays hold hands alone, so
            // without these the wearer's arm went through the breast.
            var ownBones = new[]
            {
                HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleDistal, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.RightMiddleDistal, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
            }.Select(b => animator.GetBoneTransform(b)).ToArray();
            var ownRadii = new float[8];
            var weights = body.sharedMesh.boneWeights;
            // Read once: body.bones copies the whole array on every call, and this ran per vertex.
            var bones = body.bones;
            Vector3 bodyAt = body.transform.position;
            Quaternion bodyTurn = body.transform.rotation;
            for (int c = 0; c < 8; c++)
            {
                int s0 = (c >> 2) * 6 + ((c & 3) < 3 ? (c & 3) : 4);
                Transform from = ownBones[s0], to = ownBones[s0 + 1];
                if (from == null || to == null) continue;
                var owns = bones.Select(b => b != null && b.IsChildOf(from) && !b.IsChildOf(to)).ToArray();
                Vector3 fp = from.position, seg = to.position - fp;
                // The skin whose strongest bone hangs from this one and not from the next (twist bones
                // and, for the hand, its fingers), round the middle of the bone alone: taken to the
                // ends it took in the shoulder and the hip, an upper arm 98 mm thick, a thigh 184.
                var near = new List<float>();
                for (int v = 0; v < Mathf.Min(at.Length, weights.Length); v++)
                {
                    int b = weights[v].boneIndex0;
                    if (b >= owns.Length || !owns[b]) continue;
                    var w = bodyAt + bodyTurn * at[v];
                    float along = Vector3.Dot(w - fp, seg) / Mathf.Max(seg.sqrMagnitude, 1e-9f);
                    if (along > 0.2f && along < 0.8f) near.Add(Vector3.Distance(w, fp + seg * along));
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
            // The avatar's own colliders on the breasts' cloth move into the tissue as limbs (two
            // capsule-end children each) and out of the cloth's lists: left in, a forearm threw the
            // cloth and tissue 577 mm. Other players' hands still move the bones. One within a cell
            // of the skin at rest stays in the cloth: it would dent for good (41 mm in the cleavage).
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
            var unlisted = PairCollide(root, part, pressing.Select(p => p.c), with: false);
            Lap("Collide");
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
                    if (end != "a") continue;
                    // Which cloth it came out of, so taking the jelly off puts it back there alone.
                    foreach (var b in unlisted.Where(u => u.c == pressing[i].c).Select(u => u.b))
                    {
                        string type = b.GetType().Name;
                        int nth = Array.IndexOf(b.GetComponents<Behaviour>().Where(x => x != null && x.GetType().Name == type).ToArray(), b);
                        var mark = new GameObject($"{ListedMark}{type} {nth} {AnimationUtility.CalculateTransformPath(b.transform, root)}");
                        Undo.RegisterCreatedObjectUndo(mark, "Put the breasts on the jelly");
                        mark.transform.SetParent(t, false);
                    }
                }
                colRadii[i] = pressing[i].r;
            }
            string collided = (pressing.Count > 0 ? $"{pressing.Count} of its colliders pressing instead of moving the bones ({string.Join(", ", pressing.Select(p => p.c.name))})" : "none of its colliders pressing") +
                              (touching.Count > 0 ? $", {touching.Count} left in the cloth as touching the breasts at rest ({string.Join(", ", touching)})" : "");
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
            Lap("Wire");

            var state = core.state;
            var left = new List<string>();
            int patched = 0;
            var jellied = Parts(root, names, dented);
            int reading = Reading(root, names);
            foreach (var m in mats)
            {
                var roles = RolesFor(m, reading, jellied);
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
                m.SetTexture(StateOf(pair), state);
                m.SetVector(LatticeOf(pair), new Vector4(n.x, n.y, n.z, spacing));
                m.SetVector("_YAPS_JellySlap", Slapped);
                Carried(m, root, names, dented);
                EditorUtility.SetDirty(m);
                patched++;
            }
            Lap("Materials");
            AssetDatabase.SaveAssets();
            if (Unsaved(made) is string lost)
                report?.Error(YapsSquash.Category, $"The jelly on {names[pair]} did not save",
                    $"Read back after saving, {lost}, so it would do nothing in game and can break a clone of the avatar. " +
                    "Convert again. If it happens again, send Diagnostics.md and the avatar's Jelly folder with the bug report.");
            Lap("Save");
            // Off with the mass; with the cloth carrying it, left as it was: switched on here, a
            // physics toggle off by default came on with it.
            var off = cloth ? restored : PairPhysics(root, part, on: false);
            Lap("Physics"); Debug.Log("[AvatarBridge] Jelly steps (ms): " + string.Join(", ", laps));
            string each = sides == 2 ? " each" : "", litres = string.Join(" and ", volume.Select(x => $"{1000f * x:0.0}")) + " litres";
            return $"On {root.name}: {names[pair]} on {(sides == 2 ? "two lattices" : "a lattice")} of {n.x}x{n.y}x{n.z} ({n.x * n.y * n.z} particles{each}, {100f * spacing:0.0} cm apart, " +
                   $"{100f * (n.x - 1) * spacing:0} x {100f * (n.y - 1) * spacing:0} x {100f * (n.z - 1) * spacing:0} cm) under {chest.name}; " +
                   (cloth ? $"{litres}, riding {string.Join(" and ", carriers.Select(x => x ?? "nothing"))}{(clothed ? ", which the cloth swings" : ", which no cloth swings")}, a hand denting it and never moving the whole; "
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

        static HashSet<string> Worns(Transform root)
        {
            var worn = new HashSet<string>(root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(Worn + " ")).Select(t => t.name.Substring(Worn.Length + 1)));
            if (Planned != null) worn.UnionWith(Planned);
            return worn;
        }

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
        static bool SquashIdle(Vector4 reach, Vector4 parts)
        {
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
            // A fresh conversion has nothing to read it off. The last build of this name is written
            // over, so a folder is not left behind each time, unless another avatar in the open
            // scenes wears it: a unique name every time left one numbered set per conversion.
            var others = Object.FindObjectsOfType<Renderer>(true).Where(r => !r.transform.IsChildOf(root))
                .SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToList();
            bool Worn(string dir) => others.Any(m => Enumerable.Range(0, YapsSquash.JellySlots).Any(s =>
                m.HasProperty(StateOf(s)) && m.GetTexture(StateOf(s)) is Texture t && t != null
                && Path.GetDirectoryName(AssetDatabase.GetAssetPath(t)).Replace('\\', '/') == dir + (s > 0 ? "_" + (s + 1) : "")));
            string mine = folder + "/" + YapsNativeBuilder.Sanitise(root.name) + " Jelly";
            for (int n = 1; ; n++)
            {
                string at = n == 1 ? mine : mine + " " + n;
                if (!Worn(at)) return at;
            }
        }

        // Every pair the squash found put on the jelly with a user's defaults, its assets in the
        // folder into; a pair it refuses keeps the squash. What was said, as put on or refused.
        internal static (List<string> on, List<string> refused) On(Transform root, string into, BridgeReport report)
        {
            var on = new List<string>();
            var refused = new List<string>();
            var found = YapsSquash.Measured.root == root && YapsSquash.Measured.parts != null ? YapsSquash.Measured.parts : YapsSquash.PartsOf(root, out _);
            var names = found?.parts.Select(x => x.name).ToList() ?? new List<string>();
            var dented = YapsSquash.SettingsOf(root)?.dented ?? new List<string>();
            // Each part changes what the materials read, so put on one at a time every material
            // was patched again for each, one import apiece: a minute and a half a part on an
            // avatar with many. Planned as all on, they are patched once, in one batch.
            Planned = new HashSet<string>(names.Take(YapsSquash.JellySlots));
            try
            {
                if (names.Count > 0) Prepatch(root, names, dented, report);
                foreach (var name in names)
                {
                    string said = PairOn(root, new Regex("^" + Regex.Escape(name) + "$", RegexOptions.IgnoreCase), into, Shaders, new Options { probes = YapsSquash.SettingsOf(root)?.otherColliders == true }, report, found);
                    Debug.Log("[YAPS] Jelly: " + said);
                    (said.StartsWith("On ") ? on : refused).Add(said);
                }
            }
            finally { Planned = null; }
            // A part it refused was planned as on: its materials go back to what is really worn.
            if (refused.Count > 0) Settle(root, names, dented, report);
            return (on, refused);
        }

        // Set while On puts every part on: the parts still to come count as worn.
        static HashSet<string> Planned;

        // What a squash material compiles in with these parts on the jelly. The squash only while it
        // still dents something: on every part the jelly has taken over, it costs a body's vertex
        // program 85% of its code to return early.
        static YapsShaderPatcher.Roles RolesFor(Material m, int reading, Vector4 jellied) =>
            RolesFor(YapsShaderPatcher.RolesOf(m.shader), m.GetVector("_YAPS_SquashReach"), reading, jellied);

        // From what a copy has and the squash's reach, before the copy exists: the squash patches
        // straight to these, so its shaders are not compiled twice.
        internal static YapsShaderPatcher.Roles RolesFor(YapsShaderPatcher.Roles have, Vector4 reach, int reading, Vector4 jellied)
        {
            var roles = have & ~YapsShaderPatcher.Roles.JellyUnread | YapsShaderPatcher.Roles.Jelly | YapsShaderPatcher.Roles.Squash
                | YapsShaderPatcher.JellyReading(reading);
            if (SquashIdle(reach, jellied)) roles &= ~YapsShaderPatcher.Roles.Squash;
            return roles;
        }

        static int Reading(Transform root, List<string> names)
        {
            var worn = Worns(root);
            return Enumerable.Range(0, YapsSquash.JellySlots).LastOrDefault(x => x < names.Count && worn.Contains(names[x])) + 1;
        }

        // Every squash material onto the shader it ends on, written and imported in one batch.
        static void Prepatch(Transform root, List<string> names, List<string> dented, BridgeReport report)
        {
            int reading = Reading(root, names);
            var jellied = Parts(root, names, dented);
            Rewear(YapsSquash.Materials(root).Select(m => (m, RolesFor(m, reading, jellied))), report, false);
        }

        static void Settle(Transform root, List<string> names, List<string> dented, BridgeReport report)
        {
            int reading = Reading(root, names);
            var jellied = Parts(root, names, dented);
            bool any = jellied != Vector4.zero;
            // None on at all: the squash alone, as before the jelly was tried.
            Rewear(YapsSquash.Materials(root).Where(m => m.HasProperty("_YAPS_JellyParts")).Select(m => (m, any ? RolesFor(m, reading, jellied)
                : YapsShaderPatcher.RolesOf(m.shader) & ~(YapsShaderPatcher.Roles.Jelly | YapsShaderPatcher.Roles.JellyUnread) | YapsShaderPatcher.Roles.Squash)),
                report, true);
            // Every one, rewritten or not: the planned parts were carried as worn.
            foreach (var m in YapsSquash.Materials(root).Where(m => m.HasProperty("_YAPS_JellyParts")))
            {
                Carried(m, root, names, dented);
                EditorUtility.SetDirty(m);
            }
        }

        // Each material onto its copy with these roles, imported in one batch (one at a time ran
        // every package's import hooks, ~50 s a copy). A copy that fails is left to its part's pass
        // to report. A plug's second copy is never batched: a stand-in from the original shader
        // cannot carry the value asking for it, so it is patched on its own once parts settle.
        static void Rewear(IEnumerable<(Material m, YapsShaderPatcher.Roles roles)> asked, BridgeReport report, bool settling)
        {
            bool Batched((Material m, YapsShaderPatcher.Roles) x) => YapsShaderPatcher.OriginalShaderOf(x.m) != null && !YapsShaderPatcher.Duplicate(x.m);
            var wanted = asked.Where(x => (settling || Batched(x))
                    && (YapsShaderPatcher.RolesOf(x.m.shader) != x.roles || YapsShaderPatcher.IsStale(x.m))).ToList();
            foreach (var group in wanted.Where(Batched).GroupBy(x => x.roles))
            {
                var stands = group.Select(x => new Material(YapsShaderPatcher.OriginalShaderOf(x.m)) { name = x.m.name }).ToList();
                try { YapsShaderPatcher.Prewrite(stands, group.Key); }
                finally { foreach (var stand in stands) Object.DestroyImmediate(stand); }
            }
            foreach (var (m, roles) in wanted)
            {
                var shader = YapsShaderPatcher.WithRoles(m, roles, report, out _);
                if (shader == null || ShaderUtil.ShaderHasError(shader)) continue;
                YapsShaderPatcher.Wear(m, shader);
                EditorUtility.SetDirty(m);
            }
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
        // finds the chains, by name and side, or on a bone naming the part with no side above them,
        // as one cloth rooted on a Boob_Root swings both: missed, the tissue rode the bone above the
        // swung one and its colliders kept their full size.
        internal static List<Behaviour> PairCloth(Transform root, Regex part)
        {
            bool Sided(Transform t) => t != null && part.IsMatch(t.name) && SideOf.IsMatch(t.name);
            var tops = root.GetComponentsInChildren<Transform>(true).Where(t => Sided(t) && !Sided(t.parent)).ToList();
            var chains = new HashSet<Transform>(tops.SelectMany(t => t.GetComponentsInChildren<Transform>(true)));
            bool Above(Transform t) => part.IsMatch(t.name) && tops.Any(c => c != t && c.IsChildOf(t));
            var found = new List<Behaviour>();
            foreach (var b in root.GetComponentsInChildren<Behaviour>(true))
            {
                string type = b != null ? b.GetType().Name : "";
                if (type != "MagicaCloth" && type != "DynamicBone") continue;
                if (RootsOf(b).Any(t => t != null && (chains.Contains(t) || Above(t)))) found.Add(b);
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
        // kind, only those in `only` when given; each collider taken out and the cloth it left.
        static List<(Component c, Behaviour b)> PairCollide(Transform root, Regex part, IEnumerable<Component> colliders, bool with,
            ICollection<Behaviour> only = null)
        {
            var set = colliders.ToList();
            var taken = new List<(Component c, Behaviour b)>();
            if (set.Count == 0) return taken;
            foreach (var b in PairCloth(root, part))
            {
                if (only != null && !only.Contains(b)) continue;
                bool magica = b.GetType().Name == "MagicaCloth";
                var so = new SerializedObject(b);
                var list = so.FindProperty(magica ? "serializeData.colliderCollisionConstraint.colliderList" : "m_Colliders");
                if (list == null || !list.isArray) continue;
                var have = Enumerable.Range(0, list.arraySize).Select(i => list.GetArrayElementAtIndex(i).objectReferenceValue as Component).ToList();
                var want = with ? have.Concat(set.Where(c => !have.Contains(c) && c.GetType().Name.StartsWith("Magica") == magica)).ToList() : have.Where(c => !set.Contains(c)).ToList();
                if (!with) taken.AddRange(have.Where(set.Contains).Select(c => (c, b)));
                if (want.Count == have.Count) continue;
                list.arraySize = want.Count;
                for (int i = 0; i < want.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = want[i];
                so.ApplyModifiedProperties();
            }
            return taken;
        }

        const string ListedMark = "Jelly listed in ";

        // "<type> <nth of its type there> <path>" back to the cloth or dynamic bone.
        static Behaviour ClothAt(Transform root, string mark)
        {
            var bits = mark.Split(new[] { ' ' }, 3);
            if (bits.Length < 3 || !int.TryParse(bits[1], out int nth)) return null;
            // By path, not Find: a slash in a name reads as missing to Find.
            var at = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => AnimationUtility.CalculateTransformPath(t, root) == bits[2]);
            return at != null ? at.GetComponents<Behaviour>().Where(x => x != null && x.GetType().Name == bits[0]).ElementAtOrDefault(nth) : null;
        }

        const string RadiusMark = "Jelly radius ";
        static string RadiusOf(string type) => type == "MagicaCloth" ? "serializeData.radius.value" : "m_Radius";

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

        // The bone carrying most of a side's skin (share per vertex), as the squash's follow:
        // off-skeleton bones below the chest if they carry a quarter or more, else the most
        // weighted bone, trunk included (a painted breast with no own bones rode the shoulder at 5%).
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
        internal static void AddSkin(GameObject made, CVRMaterialDataProvider provider, Transform[] hangs, List<Vector3>[] spots, float depth, string undo, string folder, float reach)
        {
            var probes = new List<Transform>[] { new List<Transform>(), new List<Transform>() };
#if AVATARBRIDGE_MAGICA
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
                        var w = spots[side][k];
                        var outward = (w - mid).normalized;
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
                // A sensor nobody needs to see: its gizmo covered the skin with spheres and axes.
                var cloth = net.GetComponent<MagicaCloth2.MagicaCloth>();
                cloth.GizmoSerializeData.clothDebugSettings.enable = false;
                net.SetActive(true);
                // The editor writes a cloth's start scale only while drawing its gizmo, now off;
                // without it a resized avatar's probes keep their reach in metres.
                MagicaClothKeepOut.Pin(cloth);
            }
#endif
            // Cloth-swung bones reach the step only through skinning: a zero-area helper mesh, four
            // points on each region's bone and four on its still copy. "[FPR]" keeps the first-person
            // copy off it, which stops skinning offscreen and froze the squash's.
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
            var skinMesh = FilledMesh(folder + "/Jelly_Skin.asset", mesh =>
            {
                mesh.name = "Jelly skin";
                mesh.vertices = points.ToArray();
                mesh.boneWeights = skinWeights.ToArray();
                mesh.bindposes = skinBones.Select(b => b.worldToLocalMatrix * skin.transform.localToWorldMatrix).ToArray();
                mesh.triangles = Enumerable.Range(0, 3 * points.Count).Select(i => i / 3).ToArray();
            });
            skin.sharedMesh = skinMesh;
            skin.bones = skinBones.ToArray();
            skin.sharedMaterial = Save(new Material(Shader.Find("Standard")), folder + "/Jelly_Skin.mat");
            skin.updateWhenOffscreen = true;
            skin.shadowCastingMode = ShadowCastingMode.Off;
            skin.receiveShadows = false;
            foreach (var mat in provider.materials)
            {
                mat.SetFloat("_JellySkinStride", skinMesh.GetVertexBufferStride(0));
                mat.SetFloat("_JellyProbes", probed);
                mat.SetFloat("_JellyProbeReach", reach);
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssetIfDirty(mat);
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
            var mesh = FilledMesh(folder + "/Jelly_Probe_Markers.asset", m =>
            {
                m.name = "Jelly probe markers";
                m.vertices = points.ToArray();
                m.SetUVs(1, at);
                m.boneWeights = weights.ToArray();
                m.bindposes = tips.Select(x => x.t.worldToLocalMatrix * markers.transform.localToWorldMatrix).ToArray();
                m.triangles = triangles.ToArray();
                m.RecalculateBounds();
            });
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
            var sideMat = Save(StepMaterial(step, n, 0, spacing, side: true), folder + "/Jelly_Step_Side.mat");
            // Only a probe's prop sleeps: worn, the avatar's own sway keeps it awake, and the test alone costs
            // a slow GPU a few percent. Set on the saved materials, which a rebuild keeps.
            foreach (var mat in new[] { stepMat, sideMat, stepMat2 })
            {
                if (sleeps) mat.EnableKeyword("JELLY_SLEEP");
                else mat.DisableKeyword("JELLY_SLEEP");
                // The rider's texels are read only by the parser's pass and the sleep: worn without
                // either, stepping them cost a particle's whole step on two texels a region.
                mat.SetFloat("_Rider", withRider || sleeps ? 1f : 0f);
                // A 144 Hz screen steps every other frame: the draw carries the jiggle on between, and
                // up to 90 Hz every frame still steps.
                mat.SetFloat("_Cap", Cap);
                // Past _Half every other frame steps at 90 Hz: the step after a skip takes the gap as
                // one predicted pass, so the skip saves a third, not the 6% two passes left.
                mat.SetFloat("_Mid", Mid);
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
            var fed = new List<Material> { stepMat, sideMat, stepMat2 };
            // Into the twin and back: a texture in and out the same would make the client render into
            // a temporary and copy it back, every pass. The side row's blit reads the state the first
            // pass's particles read and writes its row alone over theirs.
            foreach (var (mat, from, to) in new[] { (stepMat, state, twin), (sideMat, state, twin), (stepMat2, twin, state) })
            {
                var blitter = tissue.AddComponent<CVRBlitter>();
                steps.Add(blitter);
                blitter.originTexture = from;
                blitter.destinationTexture = to;
                blitter.blitMaterial = mat;
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

        // side: the first pass's side row, which looks for hands in reach; the third pass looks for
        // whatever wakes a rest.
        internal static Material StepMaterial(Shader step, Vector3Int n, int pass, float spacing, bool side = false)
        {
            var m = new Material(step);
            m.SetVector("_Lattice", new Vector4(n.x, n.y, n.z, spacing));
            m.SetFloat("_Pass", pass);
            if (side) m.EnableKeyword("JELLY_SCAN");
            if (pass == 2) m.EnableKeyword("JELLY_POINTS");
            return m;
        }

        // What of a jelly's own files came back empty or at its defaults, read from memory and from
        // disk, or null. One tester's build kept the skin mesh empty and the step materials at their
        // defaults, and nothing said so until the avatar broke in game.
        internal static string Unsaved(GameObject made)
        {
            var mesh = made.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name.StartsWith("Jelly skin"))?.sharedMesh;
            if (mesh != null && (mesh.vertexCount == 0 || OnDisk(mesh, @"^\s*m_VertexCount: 0\s*$")))
                return $"its skin mesh is empty ({AssetDatabase.GetAssetPath(mesh)})";
            foreach (var m in made.GetComponentsInChildren<CVRMaterialDataProvider>(true).SelectMany(q => q.materials ?? new Material[0]).Distinct())
                if (m != null && mesh != null && m.HasProperty("_JellySkinStride")
                    && (m.GetFloat("_JellySkinStride") == 0f || OnDisk(m, @"^\s*- _JellySkinStride: 0\s*$")))
                    return $"{m.name} is back at its defaults ({AssetDatabase.GetAssetPath(m)})";
            // One byte over and over is no depth map: a tester's was all 0xCD, and no hand pressed.
            foreach (var m in made.GetComponentsInChildren<CVRMaterialDataProvider>(true).SelectMany(q => q.materials ?? new Material[0]).Distinct())
                if (m != null && m.HasProperty("_JellyDepth") && m.GetTexture("_JellyDepth") is Texture2D depth
                    && OnDisk(depth, @"^\s*_typelessdata: ([0-9a-f]{2})\1*\s*$"))
                    return $"its depth map is blank ({AssetDatabase.GetAssetPath(depth)})";
            return null;
        }

        // Only text assets can be read back; a binary project is checked in memory alone.
        static bool OnDisk(Object asset, string pattern)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            return EditorSettings.serializationMode == SerializationMode.ForceText && File.Exists(path)
                   && Regex.IsMatch(File.ReadAllText(path), pattern, RegexOptions.Multiline);
        }

        // Kept in place when it exists (remade at the same path, scene copies read it Missing), and
        // on disk filled before this returns. Created first and filled after, a build that stopped
        // or went unsaved in between shipped the mesh empty, and the client's clone of the avatar
        // (Mini Me) failed on it.
        internal static Mesh FilledMesh(string path, System.Action<Mesh> fill)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool made = mesh == null;
            if (made) mesh = new Mesh();
            mesh.Clear();
            fill(mesh);
            if (made) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
            return mesh;
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

        internal const float Cap = 72;
        internal const float Mid = 45;
    }
}
#endif
