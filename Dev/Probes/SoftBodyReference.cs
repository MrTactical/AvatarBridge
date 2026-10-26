#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Collections.Generic;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A soft-body reference for the squash: the pair as real tissue, filmed beside the shader under
    // the same hands (docs/Unfinished.md item 25). Not shipped and not bound by what ChilloutVR runs.
    //
    // XPBD on tetrahedra: the chest voxelised from the body's own mesh at rest, each cube six
    // tetrahedra, every one held to its rest volume, so the flesh is incompressible and what a press
    // moves has to go somewhere; the edges soft. Behind the plane each part meets the body in, and
    // wherever the skin carries no part, the tissue is held still: the ribcage, the sternum, the arms.
    // The two parts are separate bodies that press and slide on each other. The hands are the
    // capsules the shader presses with. Clothing, found over the skin along the line from each
    // part's centre, keeps the surface it covers from stretching: it moves with the part, and what
    // is pressed goes out where it is open. No gravity: the rest pose already hangs.
    sealed class SoftBodyReference
    {
        public readonly string label;
        readonly float h, apart, reach;
        readonly Vector3 origin, mid, X, Y, Z;
        readonly Vector3[] centres;
        readonly int nx, ny, nz;
        readonly Vector3[] x, x0, prev, vel;
        readonly float[] inv;
        readonly int[] sideOf;
        readonly bool[] surface;
        readonly int[] edges;
        readonly float[] edgeRest;
        readonly int[] tets;
        readonly float[] tetRest;
        readonly float restVolume;
        // Corners of the two parts on one spot, where they touched at rest: kept from crossing in X.
        readonly int[] pairs;
        readonly Dictionary<int, int[]> cubes = new Dictionary<int, int[]>();
        // The cloth, its faces binned by cell, found over the tissue at rest; then the edges of the
        // tissue's surface it covers, which can bend and slide but never stretch, as fabric holds
        // what it covers. Held rigid where it lay at rest instead, a cup could not move with the
        // part it holds: squeezed together, the flesh was pressed out through the gaps.
        readonly Vector3[] clothAt;
        readonly int[] clothTris;
        readonly Dictionary<Vector3Int, List<int>> clothCells = new Dictionary<Vector3Int, List<int>>();
        readonly int[] covered;
        readonly float[] coveredRest;
        readonly List<Embedded> skins = new List<Embedded>();
        // Each kind of constraint in batches that share no particle, so a batch runs on every core
        // at once and still solves as one pass in some order would. A cube's six tetrahedra share
        // two corners, so they go together, one after another.
        readonly int[][] edgeBatches, coveredBatches, cubeBatches;
        readonly int[] cubeTets;

        sealed class Embedded
        {
            public Mesh mesh;
            public Vector3[] rest, now;
            public int[] who, corner;
            public Vector3[] at;
            public int[][] welds;
            // Clothing moves with the skin under it instead: its nearest vertex there, or none.
            public Embedded skin;
            public int[] follow;
        }

        public float Compliance = 1e-4f;
        public int Substeps = 12;
        public bool Threads = true;
        // Skin off its cube moves as the cube's nearest point does.
        public bool Clamp;
        readonly System.Diagnostics.Stopwatch[] clocks = Enumerable.Range(0, 7).Select(_ => new System.Diagnostics.Stopwatch()).ToArray();
        static readonly string[] ClockNames = { "finding contacts", "moving", "contacts", "cloth", "hands", "edges", "volumes" };
        public string Timing => string.Join(", ", ClockNames.Select((s, i) => $"{s} {clocks[i].Elapsed.TotalSeconds:0.0} s"));
        public double Seconds => clocks.Sum(c => c.Elapsed.TotalSeconds);

        // bodyAt: the skin at rest in world space, share each vertex's weight on the first part (x)
        // and the second (y); c0 and c1 the parts' follows; cloth the clothing over them at rest.
        public SoftBodyReference(string label, Vector3[] bodyAt, int[] bodyTris, Vector2[] share, Vector3 c0, Vector3 c1,
            float reach, Vector3 up, Vector3 forward, (Vector3[] at, int[] tris)? cloth, float voxel)
        {
            this.label = label;
            this.reach = reach;
            h = voxel;
            centres = new[] { c0, c1 };
            X = Vector3.ProjectOnPlane(c1 - c0, up).normalized;
            Y = up.normalized;
            Z = Vector3.Cross(X, Y);
            if (Vector3.Dot(Z, forward) < 0f) Z = -Z;
            mid = 0.5f * (c0 + c1);
            apart = Vector3.Dot(c1 - c0, X);
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
            var solid = Outward(bodyAt, bodyTris, mid, label);
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
            // A closed skin crosses every column an even number of times.
            int columns = 0, odd = 0, doubled = 0;
            var counts = new int[9];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    if (bins[i * ny + j] == null) continue;
                    columns++;
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
                    counts[Mathf.Min(crossings.Count, counts.Length - 1)]++;
                    // Faces doubled on one spot, as a two-sided skin is, cross as one.
                    crossings.Sort();
                    for (int q = crossings.Count - 1; q > 0; q--)
                        if (crossings[q] - crossings[q - 1] < 0.05f) { crossings.RemoveAt(q); doubled++; }
                    odd += crossings.Count % 2;
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
            // Kuhn's six: every order of the axes from one corner to the far one, so neighbouring
            // cubes' faces split alike.
            var orders = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
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
                        foreach (var o in orders)
                        {
                            int at = 0;
                            tetCube.Add(cubes.Count);
                            tetList.Add(c[at]);
                            for (int q = 0; q < 3; q++) { at |= 1 << o[q]; tetList.Add(c[at]); }
                        }
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
            restVolume = tetRest.Sum(Mathf.Abs);
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
            var degree = new int[n];
            foreach (int p in edges) degree[p]++;
            // A corner inside the tissue has fourteen neighbours: six along the axes, six across
            // the faces, two through the cube.
            surface = degree.Select(d => d < 14).ToArray();

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
            for (int s = 0; s < 2; s++)
            {
                var free = Enumerable.Range(0, n).Where(p => sideOf[p] == s && inv[p] > 0f).Select(p => x[p] - mid).ToList();
                string Span(Vector3 axis) => free.Count == 0 ? "-" : $"{100f * free.Min(d => Vector3.Dot(d, axis)):0} to {100f * free.Max(d => Vector3.Dot(d, axis)):0}";
                Debug.Log($"[SoftBody] {label} part {s}: meets the body {100f * Vector3.Dot(centres[s] - planes[s].at, planes[s].n):0.0} cm behind its centre, " +
                          $"{Vector3.Angle(planes[s].n, Z):0} degrees off forward; {share.Count(w => (s == 0 ? w.x : w.y) >= 0.05f)} skin vertices at 0.05 or more, " +
                          $"{share.Count(w => (s == 0 ? w.x : w.y) >= 0.5f)} at a half; {free.Count} free, across {Span(X)} cm, up {Span(Y)} cm, forward {Span(Z)} cm of the middle");
            }
            // Tissue held all round moves nothing: left out of the solve.
            var tetKeep = Enumerable.Range(0, tetRest.Length).Where(t => inv[tets[4 * t]] + inv[tets[4 * t + 1]] + inv[tets[4 * t + 2]] + inv[tets[4 * t + 3]] > 0f).ToArray();
            tets = tetKeep.SelectMany(t => new[] { tets[4 * t], tets[4 * t + 1], tets[4 * t + 2], tets[4 * t + 3] }).ToArray();
            tetRest = tetKeep.Select(t => tetRest[t]).ToArray();
            restVolume = tetRest.Sum(Mathf.Abs);
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

            var pairList = new List<int>();
            long layer = (long) (nx + 1) * (ny + 1) * (nz + 1);
            foreach (var kv in id)
                if (kv.Key < layer && id.TryGetValue(kv.Key + layer, out int other)) { pairList.Add(kv.Value); pairList.Add(other); }
            pairs = pairList.ToArray();

            var under = new bool[n];
            if (cloth.HasValue)
            {
                (clothAt, clothTris) = cloth.Value;
                for (int t = 0; t < clothTris.Length; t += 3)
                {
                    Vector3 a = clothAt[clothTris[t]], b = clothAt[clothTris[t + 1]], c = clothAt[clothTris[t + 2]];
                    Vector3Int lo = Cell(Vector3.Min(a, Vector3.Min(b, c))), hi = Cell(Vector3.Max(a, Vector3.Max(b, c)));
                    for (int i = lo.x; i <= hi.x; i++)
                        for (int j = lo.y; j <= hi.y; j++)
                            for (int k = lo.z; k <= hi.z; k++)
                            {
                                var cell = new Vector3Int(i, j, k);
                                if (!clothCells.TryGetValue(cell, out var list)) clothCells[cell] = list = new List<int>();
                                list.Add(t);
                            }
                }
                for (int p = 0; p < n; p++) under[p] = surface[p] && Under(p, out _, out _);
            }
            // Held ends too: the band on the ribs anchors the cups.
            var cov = Enumerable.Range(0, edgeRest.Length).Where(e => under[edges[2 * e]] && under[edges[2 * e + 1]]).ToArray();
            covered = cov.SelectMany(e => new[] { edges[2 * e], edges[2 * e + 1] }).ToArray();
            coveredRest = cov.Select(e => edgeRest[e]).ToArray();
            edgeBatches = Batches(edges, 2, n);
            coveredBatches = Batches(covered, 2, n);

            Debug.Log($"[SoftBody] {label}: {nx}x{ny}x{nz} cubes of {h * 100f:0.0} cm, {cubes.Count} inside, {odd} of {columns} columns crossed an odd number of times, {doubled} doubled crossings, columns by crossings {string.Join("/", counts)}, {n} particles, {inv.Count(w => w == 0f)} held, " +
                      $"{tetRest.Length} tetrahedra, {edgeRest.Length} edges, {pairs.Length / 2} touching, {under.Count(u => u)} under the cloth by {coveredRest.Length} edges, {restVolume * 1e6:0} cm3; " +
                      $"batches: {edgeBatches.Length} of edges, {cubeBatches.Length} of cubes, {coveredBatches.Length} of cloth");
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
        void Each(int count, int[] items, System.Action<int> body)
        {
            if (!Threads || count < 64)
            {
                for (int i = 0; i < count; i++) body(items == null ? i : items[i]);
                return;
            }
            // Small chunks: a cube is six tetrahedra, and chunked as a light item is, its batches ran
            // on two cores.
            int chunks = Mathf.Min(4 * System.Environment.ProcessorCount, count / 32);
            System.Threading.Tasks.Parallel.For(0, chunks, c =>
            {
                for (int i = count * c / chunks, end = count * (c + 1) / chunks; i < end; i++) body(items == null ? i : items[i]);
            });
        }

        void Batched(int[][] batches, System.Action<int> body)
        {
            foreach (var b in batches) Each(b.Length, b, body);
        }

        // The skin's separate pieces, welded where a seam splits them, and the faces of those that
        // face out. Unity winds front faces clockwise, so a piece facing out sums negative.
        static int[] Outward(Vector3[] at, int[] tris, Vector3 mid, string label)
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
            var faces = new Dictionary<int, int>();
            for (int t = 0; t < tris.Length; t += 3)
            {
                int c = Find(tris[t]);
                Vector3 a = at[tris[t]] - mid, b = at[tris[t + 1]] - mid, d = at[tris[t + 2]] - mid;
                volume[c] = (volume.TryGetValue(c, out double v) ? v : 0) + Vector3.Dot(a, Vector3.Cross(b, d)) / 6.0;
                faces[c] = (faces.TryGetValue(c, out int f) ? f : 0) + 1;
            }
            Debug.Log($"[SoftBody] {label}: {faces.Count} pieces of skin, the largest {string.Join(", ", faces.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Value} faces {-volume[kv.Key] * 1e6:0} cm3"))}");
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

        // The cloth face on the line from the particle's part centre through it, within two cubes,
        // and its normal out of the body.
        bool Under(int p, out Vector3 at, out Vector3 normal)
        {
            at = normal = default;
            var d = (x[p] - centres[sideOf[p]]).normalized;
            float best = 2f * h;
            var tried = new HashSet<int>();
            for (int s = -2; s <= 2; s++)
            {
                if (!clothCells.TryGetValue(Cell(x[p] + d * (s * h)), out var list)) continue;
                foreach (int t in list)
                {
                    if (!tried.Add(t)) continue;
                    Vector3 a = clothAt[clothTris[t]], e1 = clothAt[clothTris[t + 1]] - a, e2 = clothAt[clothTris[t + 2]] - a;
                    var pv = Vector3.Cross(d, e2);
                    float det = Vector3.Dot(e1, pv);
                    if (Mathf.Abs(det) < 1e-14f) continue;
                    var tv = x[p] - a;
                    float u = Vector3.Dot(tv, pv) / det;
                    if (u < 0f || u > 1f) continue;
                    var qv = Vector3.Cross(tv, e1);
                    float v = Vector3.Dot(d, qv) / det;
                    if (v < 0f || u + v > 1f) continue;
                    float along = Vector3.Dot(e2, qv) / det;
                    if (Mathf.Abs(along) >= best) continue;
                    best = Mathf.Abs(along);
                    at = x[p] + d * along;
                    normal = Vector3.Cross(e1, e2).normalized;
                    if (Vector3.Dot(normal, d) < 0f) normal = -normal;
                }
            }
            return best < 2f * h;
        }

        float Volume(int t)
        {
            Vector3 a = x[tets[4 * t]], b = x[tets[4 * t + 1]], c = x[tets[4 * t + 2]], d = x[tets[4 * t + 3]];
            return Vector3.Dot(Vector3.Cross(b - a, c - a), d - a) / 6f;
        }

        public float VolumeChange => Enumerable.Range(0, tetRest.Length).Sum(t => Mathf.Abs(Volume(t))) / restVolume - 1f;

        public void Reset()
        {
            System.Array.Copy(x0, x, x.Length);
            System.Array.Clear(vel, 0, vel.Length);
            foreach (var e in skins) Move(e);
        }

        // A mesh to draw with the tissue, its vertices at rest in world space: each rides the cube
        // it lies in, or the nearest one of its own part within a cube.
        public void Embed(Mesh mesh, Vector3[] restAt)
        {
            var e = new Embedded { mesh = mesh, rest = restAt, now = (Vector3[]) restAt.Clone(), who = new int[restAt.Length] };
            var corners = new List<int>();
            var at = new List<Vector3>();
            for (int v = 0; v < restAt.Length; v++)
            {
                e.who[v] = -1;
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
                e.who[v] = at.Count;
                corners.AddRange(cube);
                // Off the cube, its nearest point in it: carried on past its corners, a vertex up to a
                // cube outside swung with every shear, and coarse cubes threw the skin into steps.
                at.Add(Clamp ? Vector3.Min(Vector3.one, Vector3.Max(Vector3.zero, l - from)) : l - from);
            }
            e.corner = corners.ToArray();
            e.at = at.ToArray();
            e.welds = Welds(restAt);
            skins.Add(e);
            Debug.Log($"[SoftBody] {label}: {mesh.name} rides the tissue with {at.Count} of {restAt.Length} vertices");
        }

        // Vertices on one spot, as a mesh splits its seams: their normals averaged, so a seam
        // does not show as a crease.
        static int[][] Welds(Vector3[] at)
        {
            var weld = new Dictionary<Vector3Int, List<int>>();
            for (int v = 0; v < at.Length; v++)
            {
                var key = Vector3Int.RoundToInt(at[v] * 1e5f);
                if (!weld.TryGetValue(key, out var list)) weld[key] = list = new List<int>();
                list.Add(v);
            }
            return weld.Values.Where(g => g.Count > 1).Select(g => g.ToArray()).ToArray();
        }

        // Clothing to draw: each vertex moves as the nearest vertex of the first embedded skin within
        // three cubes does, as the squash moves a layer with the skin it lies on. Riding the tissue
        // itself, a layer standing off the skin crumpled.
        public void Follow(Mesh mesh, Vector3[] restAt)
        {
            var body = skins[0];
            var grid = new Dictionary<Vector3Int, List<int>>();
            for (int v = 0; v < body.rest.Length; v++)
            {
                if (body.who[v] < 0) continue;
                var cell = Cell(body.rest[v]);
                if (!grid.TryGetValue(cell, out var list)) grid[cell] = list = new List<int>();
                list.Add(v);
            }
            var e = new Embedded { mesh = mesh, rest = restAt, now = (Vector3[]) restAt.Clone(), skin = body, follow = new int[restAt.Length] };
            int count = 0;
            for (int v = 0; v < restAt.Length; v++)
            {
                e.follow[v] = -1;
                float best = 9f * h * h;
                foreach (int b in Near(grid, restAt[v]))
                {
                    float d2 = (body.rest[b] - restAt[v]).sqrMagnitude;
                    if (d2 < best) { best = d2; e.follow[v] = b; }
                }
                if (e.follow[v] >= 0) count++;
            }
            e.welds = Welds(restAt);
            skins.Add(e);
            Debug.Log($"[SoftBody] {label}: {mesh.name} follows the skin with {count} of {restAt.Length} vertices");
        }

        // One frame under these capsules, in substeps, then the embedded meshes.
        public void Step(IList<(Vector3 a, Vector3 b, float r)> hands, float dt = 1f / 60f, bool draw = true)
        {
            float sdt = dt / Substeps, alpha = Compliance / (sdt * sdt);
            int n = x.Length;
            clocks[0].Start();
            FindContacts();
            clocks[0].Stop();
            for (int step = 0; step < Substeps; step++)
            {
                clocks[1].Start();
                Each(n, null, p =>
                {
                    prev[p] = x[p];
                    if (inv[p] == 0f) return;
                    vel[p] *= 0.99f;
                    x[p] += vel[p] * sdt;
                });
                clocks[1].Stop();
                clocks[2].Start();
                Each(pairs.Length / 2, null, q =>
                {
                    int a = pairs[2 * q], b = pairs[2 * q + 1];
                    float w = inv[a] + inv[b], gap = Vector3.Dot(x[b] - x[a], X);
                    if (w == 0f || gap >= 0f) return;
                    x[a] += X * (gap * inv[a] / w);
                    x[b] -= X * (gap * inv[b] / w);
                });
                Batched(contactBatches, Contact);
                clocks[2].Stop();
                clocks[3].Start();
                Batched(coveredBatches, e =>
                {
                    int a = covered[2 * e], b = covered[2 * e + 1];
                    float w = inv[a] + inv[b];
                    var d = x[a] - x[b];
                    float len = d.magnitude;
                    if (w == 0f || len <= coveredRest[e]) return;
                    var g = d * ((len - coveredRest[e]) / (w * len));
                    x[a] -= g * inv[a];
                    x[b] += g * inv[b];
                });
                clocks[3].Stop();
                clocks[4].Start();
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
                clocks[4].Stop();
                clocks[5].Start();
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
                clocks[5].Stop();
                // Contacts first and the volumes last, twice, so what a substep ends on keeps its
                // volume: the other way round, the hands squeezed a few percent out of the tissue.
                clocks[6].Start();
                for (int pass = 0; pass < 2; pass++)
                    Batched(cubeBatches, g =>
                    {
                        for (int t = cubeTets[g]; t < cubeTets[g + 1]; t++) Hold(t);
                    });
                clocks[6].Stop();
                clocks[1].Start();
                Each(n, null, p => vel[p] = (x[p] - prev[p]) / sdt);
                clocks[1].Stop();
            }
            if (draw) foreach (var e in skins) Move(e);
        }

        // In floats: Mono calls every Vector3 operator, and this runs most.
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
            int chunks = Threads ? 4 * System.Environment.ProcessorCount : 1;
            var found = new List<int>[chunks];
            System.Action<int> search = c =>
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
            };
            if (chunks == 1) search(0);
            else System.Threading.Tasks.Parallel.For(0, chunks, search);
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

        void Move(Embedded e)
        {
            for (int v = 0; v < e.rest.Length; v++)
            {
                if (e.skin != null)
                {
                    int b = e.follow[v];
                    if (b >= 0) e.now[v] = e.rest[v] + e.skin.now[b] - e.skin.rest[b];
                    continue;
                }
                int k = e.who[v];
                if (k < 0) continue;
                var f = e.at[k];
                var sum = Vector3.zero;
                for (int b = 0; b < 8; b++)
                    sum += (x[e.corner[8 * k + b]] - x0[e.corner[8 * k + b]]) * (((b & 1) != 0 ? f.x : 1f - f.x) * ((b & 2) != 0 ? f.y : 1f - f.y) * ((b & 4) != 0 ? f.z : 1f - f.z));
                e.now[v] = e.rest[v] + sum;
            }
            if (Smooth > 0 && e.skin == null) SmoothSkin(e);
            if (Seam && e.skin == null) SeamSkin(e);
            if (PushOut != null && e.skin == null) PushSkin(e);
            e.mesh.vertices = e.now;
            e.mesh.RecalculateNormals();
            var normals = e.mesh.normals;
            foreach (var g in e.welds)
            {
                var sum = Vector3.zero;
                foreach (int v in g) sum += normals[v];
                foreach (int v in g) normals[v] = sum.normalized;
            }
            e.mesh.normals = normals;
            e.mesh.RecalculateBounds();
        }

        public Vector3[] Rest(int which) => skins[which].rest;
        public Vector3[] Now(int which) => skins[which].now;
        public Vector3[] Particles => x0;
        public bool[] Riding => skins[0].who.Select(k => k >= 0).ToArray();
        public int[][] SkinWelds => skins[0].welds;

        // The first embedded skin's moves for these particle moves, NaN where it rides nothing: so
        // two tissues of different cubes can be set side by side.
        public Vector3[] SkinMoves(Vector3[] field)
        {
            var e = skins[0];
            var moves = new Vector3[e.rest.Length];
            for (int v = 0; v < moves.Length; v++)
            {
                int k = e.who[v];
                if (k < 0) { moves[v] = new Vector3(float.NaN, 0f, 0f); continue; }
                var f = e.at[k];
                for (int b = 0; b < 8; b++)
                    moves[v] += field[e.corner[8 * k + b]] * (((b & 1) != 0 ? f.x : 1f - f.x) * ((b & 2) != 0 ? f.y : 1f - f.y) * ((b & 4) != 0 ? f.z : 1f - f.z));
            }
            return moves;
        }

        // The tissue's answers to a press, what a bake would store: each capsule eased in along its
        // own way to each depth in turn, over `frames`, held for `hold`, every particle's move from
        // rest there. Deeper in, flesh does not go on as it started, so one depth scaled was not
        // enough. With settle, the hold ends once the tissue moves less than that a frame. Left at rest
        // after.
        public Vector3[][] Respond(IList<(Vector3 a, Vector3 b, float r, Vector3 by)> hand, float[] depths, int frames, int hold, float settle = 0f)
        {
            Reset();
            var moved = new List<(Vector3 a, Vector3 b, float r)>();
            foreach (var c in hand) moved.Add((c.a, c.b, c.r));
            var fields = new Vector3[depths.Length][];
            float from = 0f;
            for (int k = 0; k < depths.Length; k++)
            {
                int f = 0;
                for (; f < frames + hold; f++)
                {
                    float d = Mathf.Lerp(from, depths[k], Mathf.SmoothStep(0f, 1f, Mathf.Min(f / (float) frames, 1f)));
                    for (int i = 0; i < hand.Count; i++) moved[i] = (hand[i].a + hand[i].by * d, hand[i].b + hand[i].by * d, hand[i].r);
                    Step(moved, draw: false);
                    if (settle > 0f && f >= frames && Settled(settle)) break;
                }
                from = depths[k];
                fields[k] = new Vector3[x.Length];
                for (int p = 0; p < x.Length; p++) fields[k][p] = x[p] - x0[p];
                Debug.Log($"[SoftBody] {label}: pressed {100f * depths[k]:0.0} cm in {f} frames, the tissue moves {100f * fields[k].Max(v => v.magnitude):0.0} cm at most, volume {100f * VolumeChange:+0.00;-0.00}%");
            }
            Reset();
            return fields;
        }

        // The free tissue's moves in the last frame, root mean square, under this: the fastest
        // alone never stilled, a few corners trembling at a contact.
        bool Settled(float perFrame)
        {
            double sum = 0;
            int count = 0;
            for (int p = 0; p < x.Length; p++)
            {
                if (inv[p] == 0f) continue;
                sum += vel[p].sqrMagnitude;
                count++;
            }
            return sum / System.Math.Max(count, 1) < perFrame * perFrame * 3600f;
        }

        // The pair kept apart as the shader's seam keeps them, vertex by vertex on the skin: answers
        // summed from single presses each push their part into its partner with nothing pushing
        // back, and the skins crossed in a crease. Where they overlap across the middle, both go
        // flat on the plane half way between their inner sides. Pressed flat in the tissue instead,
        // the skin riding the cubes showed them as steps.
        public bool Seam;
        public string SeamRead = "";

        // The skin's moves averaged with their neighbours' this many times, as a bake would smooth
        // what it stores: on coarse cubes the skin beside held tissue followed the cubes in steps,
        // where solved tissue moves smoothly. Averaging is linear, so the sum smoothed is the
        // answers smoothed. Skin riding nothing counts as still.
        public int Smooth;
        int[][] neighbours;

        // Skin inside these capsules put back on their surface, as the shader's push-out does
        // after the dent: smoothed, the answers lost the palm's imprint and the skin came through
        // the hand.
        public IList<(Vector3 a, Vector3 b, float r)> PushOut;

        void PushSkin(Embedded e)
        {
            foreach (var (a, b, r) in PushOut)
            {
                var seg = b - a;
                float l2 = Mathf.Max(seg.sqrMagnitude, 1e-12f);
                for (int v = 0; v < e.rest.Length; v++)
                {
                    if (e.who[v] < 0) continue;
                    var at = a + seg * Mathf.Clamp01(Vector3.Dot(e.now[v] - a, seg) / l2);
                    var d = e.now[v] - at;
                    float dist = d.magnitude;
                    if (dist < r && dist > 1e-6f) e.now[v] = at + d * (r / dist);
                }
            }
        }

        void SmoothSkin(Embedded e)
        {
            int n = e.rest.Length;
            if (neighbours == null)
            {
                var sets = new HashSet<int>[n];
                for (int v = 0; v < n; v++) sets[v] = new HashSet<int>();
                var tris = e.mesh.triangles;
                for (int t = 0; t < tris.Length; t += 3)
                    for (int a = 0; a < 3; a++)
                        for (int b = 0; b < 3; b++)
                            if (a != b) sets[tris[t + a]].Add(tris[t + b]);
                foreach (var g in e.welds)
                    foreach (int v in g)
                        foreach (int u in g)
                            if (u != v) sets[v].UnionWith(sets[u]);
                neighbours = sets.Select(x => x.ToArray()).ToArray();
            }
            var d = new Vector3[n];
            var next = new Vector3[n];
            for (int v = 0; v < n; v++) d[v] = e.now[v] - e.rest[v];
            for (int pass = 0; pass < Smooth; pass++)
            {
                for (int v = 0; v < n; v++)
                {
                    if (e.who[v] < 0) { next[v] = d[v]; continue; }
                    var sum = d[v];
                    foreach (int u in neighbours[v]) sum += d[u];
                    next[v] = sum / (neighbours[v].Length + 1);
                }
                (d, next) = (next, d);
            }
            for (int v = 0; v < n; v++) e.now[v] = e.rest[v] + d[v];
        }

        void SeamSkin(Embedded e)
        {
            float in0 = float.MinValue, in1 = float.MaxValue;
            for (int v = 0; v < e.rest.Length; v++)
            {
                if (e.who[v] < 0) continue;
                float d = Vector3.Dot(e.now[v] - mid, X);
                if (Vector3.Dot(e.rest[v] - mid, X) < 0f) in0 = Mathf.Max(in0, d);
                else in1 = Mathf.Min(in1, d);
            }
            SeamRead = $"inner sides {100f * in0:0.0} and {100f * in1:0.0} cm off the middle";
            if (in0 <= in1) return;
            float plane = 0.5f * (in0 + in1);
            int moved = 0;
            for (int v = 0; v < e.rest.Length; v++)
            {
                if (e.who[v] < 0) continue;
                float d = Vector3.Dot(e.now[v] - mid, X);
                if (Vector3.Dot(e.rest[v] - mid, X) < 0f ? d > plane : d < plane) { e.now[v] += X * (plane - d); moved++; }
            }
            SeamRead += $", {moved} vertices pressed flat";
        }

        // The tissue drawn at these particle positions, solving nothing.
        public void Show(Vector3[] particles)
        {
            System.Array.Copy(particles, x, x.Length);
            foreach (var e in skins) Move(e);
        }

        // Where a moved skin's volume went, in cm3, out positive: rows over, beside and under the
        // parts' centres, columns the middle between them, the parts, and outside them. Each face
        // sweeps its normal at the half-way shape along its mean move. Unity winds clockwise, so
        // the cross points in.
        public string Where(Vector3[] was, Vector3[] now, int[] tris)
        {
            var bins = new double[3, 3];
            double total = 0;
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                var move = (now[a] - was[a] + now[b] - was[b] + now[c] - was[c]) / 3f;
                if (move.sqrMagnitude < 1e-12f) continue;
                Vector3 ma = 0.5f * (was[a] + now[a]), mb = 0.5f * (was[b] + now[b]), mc = 0.5f * (was[c] + now[c]);
                double dv = -0.5 * Vector3.Dot(Vector3.Cross(mb - ma, mc - ma), move);
                var at = (was[a] + was[b] + was[c]) / 3f - mid;
                float across = Mathf.Abs(Vector3.Dot(at, X)), high = Vector3.Dot(at, Y);
                int col = across < 0.25f * apart ? 0 : across < 0.5f * apart + 0.6f * reach ? 1 : 2;
                int row = high > 0.35f * reach ? 0 : high > -0.35f * reach ? 1 : 2;
                bins[row, col] += dv;
                total += dv;
            }
            string Row(int r) => string.Join(" ", Enumerable.Range(0, 3).Select(c => (bins[r, c] * 1e6).ToString("+0;-0").PadLeft(5)));
            return $"over [{Row(0)}] beside [{Row(1)}] under [{Row(2)}] (middle, parts, outside), total {total * 1e6:+0;-0} cm3";
        }
    }
}
#endif
