// A pair as soft tissue, solved once at the build so the shader only adds up answers. XPBD on
// tetrahedra: the chest voxelised from the body's own skin at rest, each cube five tetrahedra held
// to their rest volume, so flesh pressed one way has to go another; the edges soft. Behind the
// plane each part meets the body in, and wherever the skin carries no part, the tissue is held
// still. The two parts are separate bodies that press and slide on each other.
//
// A palm presses the pair eleven ways, each to two depths: per part from outside, from the
// partner's side, from the front, from above and from below, and both outsides at once. Every skin
// vertex's move at each is kept, smoothed over its neighbours, with how its normal turned. Summed
// in the shader from single presses, the moves came within a centimetre or two of the tissue
// solved under the same hands, where the squash that grew a part to keep its volume never could.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AvatarBridge
{
    sealed class YapsSoftBody
    {
        internal const int Ways = 11;
        // As shares of the part's reach toward its partner. A third depth at four fifths was not
        // reproducible: two orders of the same constraints differed by 4 cm there.
        internal static readonly float[] Depths = { 0.25f, 0.5f };
        // Cubes an eighth of the reach, six substeps a frame, eased in over 15 frames and held 30,
        // then 10 passes of smoothing: a second or so a press, within half a centimetre of cubes a
        // sixteenth with twelve substeps, which took minutes. Fewer substeps stiffened the edges.
        const float Compliance = 1e-4f;
        const int Substeps = 6, Frames = 15, Held = 30, Smoothing = 10;
        // The palm the shader reads another player's hand as, at a 1.7 m viewpoint: its radius,
        // and its length less that.
        internal const float PalmRadius = 0.053f, PalmLength = 0.134f;

        readonly float h;
        readonly Vector3 origin, X, Y, Z;
        readonly int nx, ny, nz;
        readonly Vector3[] x, x0, prev, vel;
        readonly float[] inv;
        readonly int[] sideOf;
        readonly bool[] surface;
        int[] edges, tets;
        float[] edgeRest, tetRest;
        // Corners of the two parts on one spot, where they touched at rest: kept from crossing in X.
        readonly int[] pairs;
        readonly Dictionary<int, int[]> cubes = new Dictionary<int, int[]>();
        // Each kind of constraint in batches that share no particle, so a batch runs on every core
        // at once and still solves as one pass in some order would. A cube's five tetrahedra share
        // corners, so they go together, one after another.
        readonly int[][] edgeBatches, cubeBatches;
        readonly int[] cubeTets;
        // The skin riding the tissue: per vertex its cube's corners and where in it it lies, or -1.
        int[] who, corner;
        Vector3[] within;

        // The pair's answers for this skin, world space, per way and depth (way * depths + depth):
        // each vertex's move, and its normal's change. Ways per part, the first then the second,
        // by where the palm is: outside, on the partner's side, in front, above, below; then both
        // outsides. Null when the skin gives no tissue.
        internal static (Vector3[][] moves, Vector3[][] turns, string log) Bake(Vector3[] at, int[] tris, List<int[]> welds, Vector2[] share,
            Vector3 c0, Vector3 c1, float reach, Vector3 up, Vector3 outward, System.Func<int, Vector3, float> extent)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var tissue = new YapsSoftBody(at, tris, share, c0, c1, reach, up, outward, reach / 8f, out string built);
            if (tissue.x.Length == 0) return (null, null, built);
            tissue.Embed(at);
            var across = tissue.X;
            Vector3[] outs = { -across, across }, centres = { c0, c1 };
            (Vector3 a, Vector3 b, float r, Vector3 by) Palm(int j, Vector3 dir)
            {
                // Upright on the sides and front, flat under or over the part.
                var axis = Mathf.Abs(Vector3.Dot(dir, up)) > 0.7f ? across : up;
                var touch = centres[j] + dir * (extent(j, dir) + PalmRadius);
                return (touch - axis * (0.5f * PalmLength), touch + axis * (0.5f * PalmLength), PalmRadius, -dir);
            }
            var presses = new List<(Vector3, Vector3, float, Vector3)[]>();
            for (int j = 0; j < 2; j++)
                foreach (var dir in new[] { outs[j], -outs[j], outward, up, -up })
                    presses.Add(new[] { Palm(j, dir) });
            presses.Add(new[] { Palm(0, outs[0]), Palm(1, outs[1]) });

            var depths = Depths.Select(d => d * reach).ToArray();
            var neighbours = Neighbours(at.Length, tris, welds);
            var rest = Normals(at, tris, welds);
            var moves = new Vector3[Ways * depths.Length][];
            var turns = new Vector3[moves.Length][];
            for (int w = 0; w < Ways; w++)
            {
                var fields = tissue.Respond(presses[w], depths);
                for (int k = 0; k < depths.Length; k++)
                {
                    var move = tissue.Smooth(tissue.SkinMoves(fields[k]), neighbours);
                    var now = Normals(at.Select((v, i) => v + move[i]).ToArray(), tris, welds);
                    moves[w * depths.Length + k] = move;
                    turns[w * depths.Length + k] = now.Select((n, i) => n - rest[i]).ToArray();
                }
            }
            return (moves, turns, $"{built}, {Ways} presses in {clock.Elapsed.TotalSeconds:0.0} s");
        }

        YapsSoftBody(Vector3[] bodyAt, int[] bodyTris, Vector2[] share, Vector3 c0, Vector3 c1, float reach, Vector3 up, Vector3 outward, float voxel,
            out string log)
        {
            h = voxel;
            var centres = new[] { c0, c1 };
            X = Vector3.ProjectOnPlane(c1 - c0, up).normalized;
            Y = up.normalized;
            Z = Vector3.Cross(X, Y);
            if (Vector3.Dot(Z, outward) < 0f) Z = -Z;
            var mid = 0.5f * (c0 + c1);
            float apart = Vector3.Dot(c1 - c0, X);
            float half = 0.5f * apart + 1.3f * reach, back = 1.3f * reach, front = 1.8f * reach, tall = 1.5f * reach;
            // Even across and centred, so the cubes split between the parts on the middle itself:
            // rounded up from one side, the split sat up to a cube over, and coarse cubes gave one
            // part a column of its partner's tissue.
            int across = Mathf.CeilToInt(half / h);
            nx = 2 * across;
            ny = Mathf.CeilToInt(2f * tall / h);
            nz = Mathf.CeilToInt((back + front) / h);
            origin = mid - X * (across * h) - Y * tall - Z * back;

            // Inside the skin, cube by cube: down each column of cube centres from the front,
            // counting the skin's faces in front of each. A piece turned inside out, as an outline
            // hull is, would count as a second skin: only the pieces facing out are counted.
            var solid = Outward(bodyAt, bodyTris, mid);
            var loc = bodyAt.Select(Local).ToArray();
            var bins = new List<int>[nx * ny];
            for (int t = 0; t < solid.Length; t += 3)
            {
                Vector3 a = loc[solid[t]], b = loc[solid[t + 1]], c = loc[solid[t + 2]];
                int i0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x) - 0.5f)), i1 = Mathf.Min(nx - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x, c.x) - 0.5f));
                int j0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y, c.y) - 0.5f)), j1 = Mathf.Min(ny - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y, c.y) - 0.5f));
                for (int i = i0; i <= i1; i++)
                    for (int j = j0; j <= j1; j++)
                        (bins[i * ny + j] ??= new List<int>()).Add(t);
            }
            var inside = new bool[nx * ny * nz];
            var crossings = new List<float>();
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    if (bins[i * ny + j] == null) continue;
                    // Off the exact centre, so a ray never runs down an edge two faces share.
                    float px = i + 0.5f + 1.3e-4f, py = j + 0.5f + 0.7e-4f;
                    crossings.Clear();
                    foreach (int t in bins[i * ny + j])
                    {
                        Vector3 a = loc[solid[t]], b = loc[solid[t + 1]], c = loc[solid[t + 2]];
                        float d = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
                        if (Mathf.Abs(d) < 1e-12f) continue;
                        float u = ((px - a.x) * (c.y - a.y) - (c.x - a.x) * (py - a.y)) / d;
                        float v = ((b.x - a.x) * (py - a.y) - (px - a.x) * (b.y - a.y)) / d;
                        if (u < 0f || v < 0f || u + v > 1f) continue;
                        crossings.Add(a.z + u * (b.z - a.z) + v * (c.z - a.z));
                    }
                    // Faces doubled on one spot, as a two-sided skin is, cross as one.
                    crossings.Sort();
                    for (int q = crossings.Count - 1; q > 0; q--)
                        if (crossings[q] - crossings[q - 1] < 0.05f) crossings.RemoveAt(q);
                    for (int k = 0; k < nz; k++)
                        inside[(i * ny + j) * nz + k] = crossings.Count(z => z > k + 0.5f) % 2 == 1;
                }

            // Each cube belongs to the part on its side of the middle; the corners on the middle
            // plane are doubled, one for each part.
            var id = new Dictionary<long, int>();
            var start = new List<Vector3>();
            var side = new List<int>();
            long Key(int s, int i, int j, int k) => (((long) s * (nx + 1) + i) * (ny + 1) + j) * (nz + 1) + k;
            int Corner(int s, int i, int j, int k)
            {
                long key = Key(s, i, j, k);
                if (id.TryGetValue(key, out int p)) return p;
                id[key] = p = start.Count;
                start.Add(origin + (X * i + Y * j + Z * k) * h);
                side.Add(s);
                return p;
            }
            // Five to a cube: one in the middle on four corners no two of which share an edge, and
            // one at each other corner, alternating cube to cube so neighbours' faces split alike.
            // Its mirror image is itself. Kuhn's six all lean on one long diagonal, so the tissue
            // gave one way more than its mirror, and the answers changed with which way was up.
            var tetList = new List<int>();
            var tetCube = new List<int>();
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                    {
                        if (!inside[(i * ny + j) * nz + k]) continue;
                        int s = i < nx / 2 ? 0 : 1;
                        var c = new int[8];
                        for (int b = 0; b < 8; b++) c[b] = Corner(s, i + (b & 1), j + ((b >> 1) & 1), k + ((b >> 2) & 1));
                        cubes[(i * ny + j) * nz + k] = c;
                        int parity = (i + j + k) & 1;
                        var middle = new List<int>();
                        for (int b = 0; b < 8; b++)
                        {
                            if ((Bits(b) & 1) == parity) { middle.Add(c[b]); continue; }
                            tetCube.Add(cubes.Count);
                            tetList.AddRange(new[] { c[b], c[b ^ 1], c[b ^ 2], c[b ^ 4] });
                        }
                        tetCube.Add(cubes.Count);
                        tetList.AddRange(middle);
                    }
            int n = start.Count;
            x = start.ToArray();
            x0 = (Vector3[]) x.Clone();
            prev = (Vector3[]) x.Clone();
            vel = new Vector3[n];
            inv = new float[n];
            sideOf = side.ToArray();
            tets = tetList.ToArray();
            tetRest = new float[tets.Length / 4];
            for (int t = 0; t < tetRest.Length; t++) tetRest[t] = Volume(t);
            var seenEdge = new HashSet<long>();
            var edgeList = new List<int>();
            for (int t = 0; t < tets.Length; t += 4)
                for (int a = 0; a < 4; a++)
                    for (int b = a + 1; b < 4; b++)
                    {
                        int p = Mathf.Min(tets[t + a], tets[t + b]), q = Mathf.Max(tets[t + a], tets[t + b]);
                        if (seenEdge.Add((long) p * n + q)) { edgeList.Add(p); edgeList.Add(q); }
                    }
            edges = edgeList.ToArray();
            edgeRest = new float[edges.Length / 2];
            for (int e = 0; e < edgeRest.Length; e++) edgeRest[e] = (x[edges[2 * e]] - x[edges[2 * e + 1]]).magnitude;
            // A corner inside the tissue is a corner of eight cubes.
            var around = new int[n];
            foreach (var c in cubes.Values)
                foreach (int p in c) around[p]++;
            surface = around.Select(d => d < 8).ToArray();

            // Held: behind the plane its part meets the body in, near skin that is no part's, or at
            // the box's walls.
            var planes = new (Vector3 n, Vector3 at)[2];
            for (int s = 0; s < 2; s++)
            {
                var on = share.Select(w => s == 0 ? w.x : w.y).ToArray();
                var (normal, above) = YapsSquash.Base(bodyAt, on, centres[s], s == 0 ? X : -X, Z, reach, reach);
                planes[s] = (normal, centres[s] - normal * above);
            }
            var grid = new Dictionary<Vector3Int, List<int>>();
            for (int v = 0; v < bodyAt.Length; v++)
            {
                var cell = Cell(bodyAt[v]);
                if (!grid.TryGetValue(cell, out var list)) grid[cell] = list = new List<int>();
                list.Add(v);
            }
            // Each particle's share of its part: from the skin within two cubes of it, and deeper in
            // from the nearest particle with skin near it, along the lattice. So the parts move and
            // the arms and belly, deep in the box, stay put.
            var part = new float[n];
            var reached = new bool[n];
            var queue = new Queue<int>();
            for (int p = 0; p < n; p++)
            {
                float sum = 0f, weight = 0f;
                foreach (int v in Near(grid, x[p]))
                {
                    float d2 = (bodyAt[v] - x[p]).sqrMagnitude;
                    if (d2 > 4f * h * h) continue;
                    float w = Mathf.Exp(-d2 / (h * h));
                    sum += w * (sideOf[p] == 0 ? share[v].x : share[v].y);
                    weight += w;
                }
                if (weight == 0f) continue;
                part[p] = sum / weight;
                reached[p] = true;
                queue.Enqueue(p);
            }
            var next = new List<int>[n];
            for (int e = 0; e < edges.Length; e += 2)
            {
                (next[edges[e]] ??= new List<int>()).Add(edges[e + 1]);
                (next[edges[e + 1]] ??= new List<int>()).Add(edges[e]);
            }
            while (queue.Count > 0)
            {
                int p = queue.Dequeue();
                foreach (int q in next[p])
                    if (!reached[q]) { reached[q] = true; part[q] = part[p]; queue.Enqueue(q); }
            }
            for (int p = 0; p < n; p++)
            {
                int s = sideOf[p];
                bool behind = Vector3.Dot(x[p] - planes[s].at, planes[s].n) < 0f;
                var l = Local(x[p]);
                bool wall = l.x < 1.01f || l.y < 1.01f || l.z < 1.01f || l.x > nx - 1.01f || l.y > ny - 1.01f || l.z > nz - 1.01f;
                inv[p] = behind || wall || part[p] < 0.05f ? 0f : 1f;
            }
            // Tissue held all round moves nothing: left out of the solve.
            var tetKeep = Enumerable.Range(0, tetRest.Length).Where(t => inv[tets[4 * t]] + inv[tets[4 * t + 1]] + inv[tets[4 * t + 2]] + inv[tets[4 * t + 3]] > 0f).ToArray();
            tets = tetKeep.SelectMany(t => new[] { tets[4 * t], tets[4 * t + 1], tets[4 * t + 2], tets[4 * t + 3] }).ToArray();
            tetRest = tetKeep.Select(t => tetRest[t]).ToArray();
            var groups = new List<int>();
            for (int k = 0; k < tetKeep.Length; k++)
                if (k == 0 || tetCube[tetKeep[k]] != tetCube[tetKeep[k - 1]]) groups.Add(k);
            groups.Add(tetKeep.Length);
            cubeTets = groups.ToArray();
            var corners = new int[8 * (cubeTets.Length - 1)];
            for (int g = 0; g + 1 < cubeTets.Length; g++)
            {
                var own = Enumerable.Range(4 * cubeTets[g], 4 * (cubeTets[g + 1] - cubeTets[g])).Select(i => tets[i]).Distinct().ToArray();
                for (int b = 0; b < 8; b++) corners[8 * g + b] = b < own.Length ? own[b] : -1;
            }
            cubeBatches = Batches(corners, 8, n);
            var edgeKeep = Enumerable.Range(0, edgeRest.Length).Where(e => inv[edges[2 * e]] + inv[edges[2 * e + 1]] > 0f).ToArray();
            edges = edgeKeep.SelectMany(e => new[] { edges[2 * e], edges[2 * e + 1] }).ToArray();
            edgeRest = edgeKeep.Select(e => edgeRest[e]).ToArray();
            edgeBatches = Batches(edges, 2, n);

            var pairList = new List<int>();
            long layer = (long) (nx + 1) * (ny + 1) * (nz + 1);
            foreach (var kv in id)
                if (kv.Key < layer && id.TryGetValue(kv.Key + layer, out int other)) { pairList.Add(kv.Value); pairList.Add(other); }
            pairs = pairList.ToArray();
            log = $"tissue {nx}x{ny}x{nz} cubes of {h * 100f:0.0} cm, {cubes.Count} inside, {n} particles, {inv.Count(w => w > 0f)} free";
        }

        // Greedy colouring: each item, its k particles in ids (negative for none), goes in the
        // first batch none of them is in yet. Past 64 batches an item runs alone.
        static int[][] Batches(int[] ids, int k, int n)
        {
            var used = new ulong[n];
            var lists = new List<List<int>>();
            for (int c = 0; c < ids.Length / k; c++)
            {
                ulong m = 0;
                for (int i = 0; i < k; i++) if (ids[k * c + i] >= 0) m |= used[ids[k * c + i]];
                if (m == ulong.MaxValue) { lists.Add(new List<int> { c }); continue; }
                int color = 0;
                while ((m >> color & 1UL) != 0) color++;
                for (int i = 0; i < k; i++) if (ids[k * c + i] >= 0) used[ids[k * c + i]] |= 1UL << color;
                while (lists.Count <= color) lists.Add(new List<int>());
                lists[color].Add(c);
            }
            return lists.Select(l => l.ToArray()).ToArray();
        }

        // A batch over every core; small ones on this thread.
        static void Each(int count, int[] items, System.Action<int> body)
        {
            if (count < 64)
            {
                for (int i = 0; i < count; i++) body(items == null ? i : items[i]);
                return;
            }
            // Small chunks: a cube is five tetrahedra, and chunked as a light item is, its batches ran
            // on two cores.
            int chunks = Mathf.Min(4 * System.Environment.ProcessorCount, count / 32);
            System.Threading.Tasks.Parallel.For(0, chunks, c =>
            {
                for (int i = count * c / chunks, end = count * (c + 1) / chunks; i < end; i++) body(items == null ? i : items[i]);
            });
        }

        static void Batched(int[][] batches, System.Action<int> body)
        {
            foreach (var b in batches) Each(b.Length, b, body);
        }

        // The skin's separate pieces, welded where a seam splits them, and the faces of those that
        // face out. Unity winds front faces clockwise, so a piece facing out sums negative.
        static int[] Outward(Vector3[] at, int[] tris, Vector3 mid)
        {
            var weld = new Dictionary<Vector3Int, int>();
            var parent = new int[at.Length];
            for (int v = 0; v < at.Length; v++)
            {
                var key = Vector3Int.RoundToInt(at[v] * 1e5f);
                if (!weld.TryGetValue(key, out int r)) weld[key] = r = v;
                parent[v] = r;
            }
            int Find(int v)
            {
                while (parent[v] != v) v = parent[v] = parent[parent[v]];
                return v;
            }
            for (int t = 0; t < tris.Length; t += 3)
                for (int q = 1; q < 3; q++)
                {
                    int a = Find(tris[t]), b = Find(tris[t + q]);
                    if (a != b) parent[a] = b;
                }
            var volume = new Dictionary<int, double>();
            for (int t = 0; t < tris.Length; t += 3)
            {
                int c = Find(tris[t]);
                Vector3 a = at[tris[t]] - mid, b = at[tris[t + 1]] - mid, d = at[tris[t + 2]] - mid;
                volume[c] = (volume.TryGetValue(c, out double v) ? v : 0) + Vector3.Dot(a, Vector3.Cross(b, d)) / 6.0;
            }
            return Enumerable.Range(0, tris.Length / 3).Where(t => volume[Find(tris[3 * t])] <= 0)
                .SelectMany(t => new[] { tris[3 * t], tris[3 * t + 1], tris[3 * t + 2] }).ToArray();
        }

        Vector3 Local(Vector3 p)
        {
            var d = p - origin;
            return new Vector3(Vector3.Dot(d, X), Vector3.Dot(d, Y), Vector3.Dot(d, Z)) / h;
        }

        Vector3Int Cell(Vector3 p) => Vector3Int.FloorToInt(p / (2f * h));

        IEnumerable<int> Near(Dictionary<Vector3Int, List<int>> grid, Vector3 p)
        {
            var cell = Cell(p);
            for (int a = -1; a <= 1; a++)
                for (int b = -1; b <= 1; b++)
                    for (int c = -1; c <= 1; c++)
                        if (grid.TryGetValue(cell + new Vector3Int(a, b, c), out var list))
                            foreach (int v in list) yield return v;
        }

        static int Bits(int b) => (b & 1) + ((b >> 1) & 1) + ((b >> 2) & 1);

        float Volume(int t)
        {
            Vector3 a = x[tets[4 * t]], b = x[tets[4 * t + 1]], c = x[tets[4 * t + 2]], d = x[tets[4 * t + 3]];
            return Vector3.Dot(Vector3.Cross(b - a, c - a), d - a) / 6f;
        }

        void Reset()
        {
            System.Array.Copy(x0, x, x.Length);
            System.Array.Clear(vel, 0, vel.Length);
        }

        // Each skin vertex rides the cube it lies in, or the nearest one of its own part within a
        // cube, at its nearest point in it: carried on past its corners, a vertex up to a cube
        // outside swung with every shear, and coarse cubes threw the skin into steps.
        void Embed(Vector3[] restAt)
        {
            who = new int[restAt.Length];
            var corners = new List<int>();
            var at = new List<Vector3>();
            for (int v = 0; v < restAt.Length; v++)
            {
                who[v] = -1;
                var l = Local(restAt[v]);
                int i = Mathf.FloorToInt(l.x), j = Mathf.FloorToInt(l.y), k = Mathf.FloorToInt(l.z);
                int[] cube = null;
                var from = Vector3Int.zero;
                bool left = l.x < nx / 2;
                float nearest = float.MaxValue;
                for (int a = -1; a <= 1; a++)
                    for (int b = -1; b <= 1; b++)
                        for (int c = -1; c <= 1; c++)
                        {
                            int ii = i + a, jj = j + b, kk = k + c;
                            if (ii < 0 || jj < 0 || kk < 0 || ii >= nx || jj >= ny || kk >= nz || ii < nx / 2 != left) continue;
                            if (!cubes.TryGetValue((ii * ny + jj) * nz + kk, out var cc)) continue;
                            float d = (new Vector3(ii + 0.5f, jj + 0.5f, kk + 0.5f) - l).sqrMagnitude - (a == 0 && b == 0 && c == 0 ? 10f : 0f);
                            if (d < nearest) { nearest = d; cube = cc; from = new Vector3Int(ii, jj, kk); }
                        }
                if (cube == null || cube.All(p => inv[p] == 0f)) continue;
                who[v] = at.Count;
                corners.AddRange(cube);
                at.Add(Vector3.Min(Vector3.one, Vector3.Max(Vector3.zero, l - from)));
            }
            corner = corners.ToArray();
            within = at.ToArray();
        }

        // One frame under these capsules, in substeps.
        void Step(IList<(Vector3 a, Vector3 b, float r)> hands)
        {
            float sdt = 1f / 60f / Substeps, alpha = Compliance / (sdt * sdt);
            int n = x.Length;
            FindContacts();
            for (int step = 0; step < Substeps; step++)
            {
                Each(n, null, p =>
                {
                    prev[p] = x[p];
                    if (inv[p] == 0f) return;
                    vel[p] *= 0.99f;
                    x[p] += vel[p] * sdt;
                });
                Each(pairs.Length / 2, null, q =>
                {
                    int a = pairs[2 * q], b = pairs[2 * q + 1];
                    float w = inv[a] + inv[b], gap = Vector3.Dot(x[b] - x[a], X);
                    if (w == 0f || gap >= 0f) return;
                    x[a] += X * (gap * inv[a] / w);
                    x[b] -= X * (gap * inv[b] / w);
                });
                Batched(contactBatches, Contact);
                foreach (var (ha, hb, hr) in hands)
                {
                    var seg = hb - ha;
                    float l2 = Mathf.Max(seg.sqrMagnitude, 1e-12f), keep = hr + 0.5f * h;
                    Each(n, null, p =>
                    {
                        if (inv[p] == 0f) return;
                        var at = ha + seg * Mathf.Clamp01(Vector3.Dot(x[p] - ha, seg) / l2);
                        var d = x[p] - at;
                        float dist = d.magnitude;
                        if (dist < keep) x[p] = at + (dist > 1e-6f ? d / dist : Z) * keep;
                    });
                }
                // In floats: Mono calls every Vector3 operator, and this runs most.
                Batched(edgeBatches, e =>
                {
                    int a = edges[2 * e], b = edges[2 * e + 1];
                    float wa = inv[a], wb = inv[b], w = wa + wb;
                    if (w == 0f) return;
                    var pa = x[a];
                    var pb = x[b];
                    float dx = pa.x - pb.x, dy = pa.y - pb.y, dz = pa.z - pb.z;
                    float len = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (len < 1e-9f) return;
                    float k = -(len - edgeRest[e]) / (w + alpha) / len, ka = k * wa, kb = k * wb;
                    x[a] = new Vector3(pa.x + dx * ka, pa.y + dy * ka, pa.z + dz * ka);
                    x[b] = new Vector3(pb.x - dx * kb, pb.y - dy * kb, pb.z - dz * kb);
                });
                // Contacts first and the volumes last, twice, so what a substep ends on keeps its
                // volume: the other way round, the hands squeezed a few percent out of the tissue.
                for (int pass = 0; pass < 2; pass++)
                    Batched(cubeBatches, g =>
                    {
                        for (int t = cubeTets[g]; t < cubeTets[g + 1]; t++) Hold(t);
                    });
                Each(n, null, p => vel[p] = (x[p] - prev[p]) / sdt);
            }
        }

        // In floats, as the edges.
        void Hold(int t)
        {
            int i0 = tets[4 * t], i1 = tets[4 * t + 1], i2 = tets[4 * t + 2], i3 = tets[4 * t + 3];
            float w0 = inv[i0], w1 = inv[i1], w2 = inv[i2], w3 = inv[i3];
            var p0 = x[i0];
            var p1 = x[i1];
            var p2 = x[i2];
            var p3 = x[i3];
            float ax = p1.x - p0.x, ay = p1.y - p0.y, az = p1.z - p0.z;
            float bx = p2.x - p0.x, by = p2.y - p0.y, bz = p2.z - p0.z;
            float cx = p3.x - p0.x, cy = p3.y - p0.y, cz = p3.z - p0.z;
            const float sixth = 1f / 6f;
            float g1x = (by * cz - bz * cy) * sixth, g1y = (bz * cx - bx * cz) * sixth, g1z = (bx * cy - by * cx) * sixth;
            float g2x = (cy * az - cz * ay) * sixth, g2y = (cz * ax - cx * az) * sixth, g2z = (cx * ay - cy * ax) * sixth;
            float g3x = (ay * bz - az * by) * sixth, g3y = (az * bx - ax * bz) * sixth, g3z = (ax * by - ay * bx) * sixth;
            float g0x = -(g1x + g2x + g3x), g0y = -(g1y + g2y + g3y), g0z = -(g1z + g2z + g3z);
            float w = w0 * (g0x * g0x + g0y * g0y + g0z * g0z) + w1 * (g1x * g1x + g1y * g1y + g1z * g1z)
                    + w2 * (g2x * g2x + g2y * g2y + g2z * g2z) + w3 * (g3x * g3x + g3y * g3y + g3z * g3z);
            if (w < 1e-20f) return;
            float s = -(g3x * cx + g3y * cy + g3z * cz - tetRest[t]) / w;
            float s0 = s * w0, s1 = s * w1, s2 = s * w2, s3 = s * w3;
            x[i0] = new Vector3(p0.x + g0x * s0, p0.y + g0y * s0, p0.z + g0z * s0);
            x[i1] = new Vector3(p1.x + g1x * s1, p1.y + g1y * s1, p1.z + g1z * s1);
            x[i2] = new Vector3(p2.x + g2x * s2, p2.y + g2y * s2, p2.z + g2z * s2);
            x[i3] = new Vector3(p3.x + g3x * s3, p3.y + g3y * s3, p3.z + g3z * s3);
        }

        // Outer particles kept most of a cube apart: the two parts where they meet away from the
        // middle plane, and a part against the body held still under it, so a part pushed down
        // lands on the ribs and belly instead of sinking through the fold into them. Within a part
        // only against held tissue, and never against what lay beside it at rest. The pairs near
        // enough to touch are found once a frame, out to a cube past touching, as nothing moves a
        // cube in a frame.
        readonly Dictionary<Vector3Int, List<int>> hash = new Dictionary<Vector3Int, List<int>>();
        int[] contacts = new int[0];
        int[][] contactBatches = new int[0][];

        void FindContacts()
        {
            // Cells as wide as the reach looked for, so only the 27 round a corner can hold one.
            float near = 1.8f * h, cellSize = near;
            foreach (var list in hash.Values) list.Clear();
            for (int p = 0; p < x.Length; p++)
            {
                if (!surface[p]) continue;
                var key = Vector3Int.FloorToInt(x[p] / cellSize);
                if (!hash.TryGetValue(key, out var list)) hash[key] = list = new List<int>();
                list.Add(p);
            }
            int chunks = 4 * System.Environment.ProcessorCount;
            var found = new List<int>[chunks];
            System.Threading.Tasks.Parallel.For(0, chunks, c =>
            {
                var mine = found[c] = new List<int>();
                for (int p = x.Length * c / chunks, end = x.Length * (c + 1) / chunks; p < end; p++)
                {
                    if (!surface[p] || inv[p] == 0f) continue;
                    var key = Vector3Int.FloorToInt(x[p] / cellSize);
                    for (int a = -1; a <= 1; a++)
                        for (int b = -1; b <= 1; b++)
                            for (int k = -1; k <= 1; k++)
                            {
                                if (!hash.TryGetValue(key + new Vector3Int(a, b, k), out var list)) continue;
                                foreach (int q in list)
                                {
                                    if (q == p) continue;
                                    if (sideOf[q] == sideOf[p] ? inv[q] > 0f || (x0[q] - x0[p]).sqrMagnitude < 2.25f * h * h : inv[q] > 0f && sideOf[p] == 1) continue;
                                    if ((x[q] - x[p]).sqrMagnitude >= near * near) continue;
                                    mine.Add(p);
                                    mine.Add(q);
                                }
                            }
                }
            });
            contacts = found.SelectMany(l => l).ToArray();
            contactBatches = Batches(contacts, 2, x.Length);
        }

        void Contact(int c)
        {
            int p = contacts[2 * c], q = contacts[2 * c + 1];
            float least = 0.8f * h;
            var d = x[q] - x[p];
            float d2 = d.sqrMagnitude, w = inv[p] + inv[q];
            if (d2 >= least * least || d2 < 1e-14f || w == 0f) return;
            float dist = Mathf.Sqrt(d2);
            var g = d / dist * (least - dist);
            x[p] -= g * (inv[p] / w);
            x[q] += g * (inv[q] / w);
        }

        // Each capsule eased in along its own way to each depth in turn, over Frames, held for
        // Held, every particle's move from rest there. Deeper in, flesh does not go on as it
        // started, so one depth scaled was not enough. Left at rest after.
        Vector3[][] Respond(IList<(Vector3 a, Vector3 b, float r, Vector3 by)> hand, float[] depths)
        {
            Reset();
            var moved = hand.Select(c => (c.a, c.b, c.r)).ToList();
            var fields = new Vector3[depths.Length][];
            float from = 0f;
            for (int k = 0; k < depths.Length; k++)
            {
                for (int f = 0; f < Frames + Held; f++)
                {
                    float d = Mathf.Lerp(from, depths[k], Mathf.SmoothStep(0f, 1f, Mathf.Min(f / (float) Frames, 1f)));
                    for (int i = 0; i < hand.Count; i++) moved[i] = (hand[i].a + hand[i].by * d, hand[i].b + hand[i].by * d, hand[i].r);
                    Step(moved);
                }
                from = depths[k];
                fields[k] = new Vector3[x.Length];
                for (int p = 0; p < x.Length; p++) fields[k][p] = x[p] - x0[p];
            }
            Reset();
            return fields;
        }

        // The skin's moves for these particle moves, zero where it rides nothing.
        Vector3[] SkinMoves(Vector3[] field)
        {
            var moves = new Vector3[who.Length];
            for (int v = 0; v < moves.Length; v++)
            {
                int k = who[v];
                if (k < 0) continue;
                var f = within[k];
                for (int b = 0; b < 8; b++)
                    moves[v] += field[corner[8 * k + b]] * (((b & 1) != 0 ? f.x : 1f - f.x) * ((b & 2) != 0 ? f.y : 1f - f.y) * ((b & 4) != 0 ? f.z : 1f - f.z));
            }
            return moves;
        }

        // Every vertex's neighbours across its faces, a seam's copies sharing theirs.
        static int[][] Neighbours(int n, int[] tris, List<int[]> welds)
        {
            var sets = new HashSet<int>[n];
            for (int v = 0; v < n; v++) sets[v] = new HashSet<int>();
            for (int t = 0; t < tris.Length; t += 3)
                for (int a = 0; a < 3; a++)
                    for (int b = 0; b < 3; b++)
                        if (a != b) sets[tris[t + a]].Add(tris[t + b]);
            foreach (var g in welds)
            {
                var all = new HashSet<int>(g.SelectMany(v => sets[v]));
                foreach (int v in g) sets[v] = new HashSet<int>(all.Where(u => u != v));
            }
            return sets.Select(s => s.ToArray()).ToArray();
        }

        // Averaged with its neighbours Smoothing times: on coarse cubes the skin beside held tissue
        // followed the cubes in steps, where solved tissue moves smoothly. Skin riding nothing
        // counts as still.
        Vector3[] Smooth(Vector3[] d, int[][] neighbours)
        {
            var next = new Vector3[d.Length];
            for (int pass = 0; pass < Smoothing; pass++)
            {
                for (int v = 0; v < d.Length; v++)
                {
                    if (who[v] < 0) { next[v] = d[v]; continue; }
                    var sum = d[v];
                    foreach (int u in neighbours[v]) sum += d[u];
                    next[v] = sum / (neighbours[v].Length + 1);
                }
                (d, next) = (next, d);
            }
            return d;
        }

        // Each vertex's normal from its faces, by area, a seam's copies sharing one. Only ever set
        // against the same at rest, so how the mesh's own normals were authored does not matter.
        // Unity winds front faces clockwise, so the cross the other way round points out.
        static Vector3[] Normals(Vector3[] at, int[] tris, List<int[]> welds)
        {
            var n = new Vector3[at.Length];
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                var f = Vector3.Cross(at[c] - at[a], at[b] - at[a]);
                n[a] += f;
                n[b] += f;
                n[c] += f;
            }
            foreach (var g in welds)
            {
                var sum = Vector3.zero;
                foreach (int v in g) sum += n[v];
                foreach (int v in g) n[v] = sum;
            }
            for (int v = 0; v < n.Length; v++) n[v] = n[v].normalized;
            return n;
        }
    }
}
#endif
