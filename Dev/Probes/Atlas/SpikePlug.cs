// Spike 4: a plug and a socket, running on nothing but the screen atlas.
//
// Dev only, pruned from public builds.
//
//   Tools > Avatar Bridge > Dev > Atlas > Screen atlas: plug and socket
//
// Spikes 1-3 proved the transport (GrabPass survives upload, ~0.1 mm, cell
// rendezvous). This one bends, with nothing wired: no references, contacts,
// lights, parameters or sync.
//   grey plug            found nothing: grabber off screen, or rows flipped
//                        (press "What is in the atlas").
//   ROTATE a socket      the plug should arrive along its axis: the second
//                        pixel, which the light protocol cannot do.
//   move the far socket  closer, and the plug should switch to it.
// Not the shipping deform: a cylinder bent along a cubic, so faults are the transport's.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

public class SpikePlug : EditorWindow
{
    [MenuItem("Tools/Avatar Bridge/Dev/Atlas/Screen atlas: plug and socket")]
    static void Open() { GetWindow<SpikePlug>("Atlas plug"); }

    const string Root = "YAPS Atlas Plug Spike";
    const string PlugName = "Plug";
    const string WearPrefix = "YAPS Wear";
    const string Dir = "Assets/YapsSpike";
    const int OriginPx = 8;

    float _cellBase = 0.02f;
    // Four levels: cells 2 cm to 1.28 m, plugs to ~5 m. Test in game before adding rows.
    int _levels = 4;
    int _cellRadius = 2;   // cells out, NOT the plug radius above
    int _sockets = 3;
    // 64, not 16: a socket is lost only when both homes clash. At 120 sockets
    // 256 slots lose ~1 in 7, 4096 ~1 in 1200. Costs no reads, only width
    // (is a mirror that wide? open question).
    int _grid = 64;
    int _slotPx = 1;
    float _reach = 1.6f;
    float _length = 0.22f;
    float _radius = 0.022f;
    int _queueBase = 1000;
    int _debug = 0;
    int _forceRow = 0;
    string _result = "";

    // As the shader: cell ~ L/2r, levels go up in fours.
    float LevelSize(out int level)
    {
        float want = _length / Mathf.Max(2f * _cellRadius, 1f);
        level = Mathf.Clamp(Mathf.RoundToInt(Mathf.Log(want / Mathf.Max(_cellBase, 1e-6f), 2f) * 0.5f),
                            0, _levels - 1);
        return _cellBase * Mathf.Pow(4f, level);
    }

    float LevelSize() { int l; return LevelSize(out l); }

    void OnEnable() { EditorApplication.update += Tick; SceneView.duringSceneGui += OnScene; }
    void OnDisable() { EditorApplication.update -= Tick; SceneView.duringSceneGui -= OnScene; }

    // An ignored socket is either outside the read box or past the chain's end; both look the same.
    void OnScene(SceneView sv)
    {
        var root = GameObject.Find(Root);
        var plug = root != null ? root.transform.Find(PlugName) : null;
        if (plug == null) return;

        Vector3 axis = plug.forward;
        Vector3 at0 = plug.position;
        float len = plug.TransformVector(Vector3.forward).magnitude;
        float cell = Mathf.Max(LevelSize(), 0.0001f);

        // (2r+1)^3 cells around the shaft midpoint's cell, so it moves in steps.
        Vector3 m = at0 + axis * (len * 0.5f);
        var c = new Vector3Int(Mathf.FloorToInt(m.x / cell),
                               Mathf.FloorToInt(m.y / cell),
                               Mathf.FloorToInt(m.z / cell));
        Vector3 centre = ((Vector3)c + Vector3.one * 0.5f) * cell;
        float span = (2 * _cellRadius + 1) * cell;
        Handles.color = new Color(0.35f, 0.75f, 1f, 0.9f);
        Handles.DrawWireCube(centre, Vector3.one * span);
        Handles.Label(centre + Vector3.up * (span * 0.5f + 0.02f),
                      "what the plug can read: " + span.ToString("F2") + " m");

        // Shader order: nearest the root first. A socket past the length is found but never reached.
        var socks = new List<Transform>();
        foreach (Transform t in root.transform)
            if (t.name.StartsWith("Socket")) socks.Add(t);
        socks.Sort((a, b) => Vector3.Distance(a.position, at0)
                     .CompareTo(Vector3.Distance(b.position, at0)));

        // Two sockets on one slot: the later draw wins, the loser is silently
        // dropped by the tag check, a deadzone one cell across (found as a gap
        // from y 1.26 to 1.44 at 18 cm cells). With octant buckets they only
        // fight in the same octant.
        var seen = new Dictionary<string, int>();
        foreach (var t in socks)
        {
            string k = Bucket(t.position, cell);
            seen[k] = seen.ContainsKey(k) ? seen[k] + 1 : 1;
        }
        var clash = new HashSet<Transform>();
        foreach (var t in socks)
            if (seen[Bucket(t.position, cell)] > 1) clash.Add(t);

        float arc = 0;
        Vector3 prev = at0;
        foreach (var t in socks)
        {
            Vector3 d = t.position - centre;
            float h = span * 0.5f;
            bool inBox = Mathf.Abs(d.x) <= h && Mathf.Abs(d.y) <= h && Mathf.Abs(d.z) <= h;
            arc += Vector3.Distance(t.position, prev);
            // A hole approached from behind is dropped, correctly; say so.
            bool hole = t.name.Length > 0 && ((t.name[t.name.Length - 1] - 'A') % 2) == 1;
            bool backwards = hole && Vector3.Dot(t.forward, t.position - at0) > 0;
            string why = backwards ? "HOLE approached from behind, skipped"
                       : clash.Contains(t) ? "same OCTANT as another, one is lost"
                       : !inBox ? "OUTSIDE the read box"
                       : arc > len ? "past the tip: arc " + arc.ToString("F2") + " of " + len.ToString("F2")
                       : "threaded at " + arc.ToString("F2") + " of " + len.ToString("F2");
            Handles.color = backwards ? new Color(0.8f, 0.4f, 0.8f)
                          : clash.Contains(t) ? Color.magenta
                          : !inBox ? Color.red
                          : arc > len ? new Color(1f, 0.7f, 0.2f) : Color.green;
            Handles.DrawLine(prev, t.position);
            Handles.Label(t.position + Vector3.up * 0.04f,
                          t.name + (hole ? " (hole)  " : " (ring)  ") + why);
            prev = t.position;
        }
    }
    void Tick() { if (EditorApplication.isPlaying) Repaint(); }

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Build, press Play, then drag and ROTATE the sockets in the scene view. "
            + "Nothing connects the plug to a socket: the socket draws two pixels and the plug "
            + "reads the twenty-seven cells around itself. Grey means it found nothing.",
            MessageType.Info);

        _cellBase = EditorGUILayout.Slider("Level 0 cell, metres", _cellBase, 0.005f, 0.2f);
        _levels = EditorGUILayout.IntSlider("Levels", _levels, 1, 8);
        EditorGUILayout.LabelField("  span",
            _cellBase.ToString("F3") + " m to "
            + (_cellBase * Mathf.Pow(4f, _levels - 1)).ToString("F1")
            + " m, four times a step. A socket publishes to every level and a plug reads one.");
        _cellRadius = EditorGUILayout.IntSlider("Neighbour radius, cells", _cellRadius, 0, 3);
        int cells = (2 * _cellRadius + 1) * (2 * _cellRadius + 1) * (2 * _cellRadius + 1);
        // From the neighbourhood: cell >= L/2r, and octants separate at half a
        // cell, so spacing L/(N+1) >= cell/2 gives N <= 4r-1.
        int ceiling = Mathf.Min(4 * _cellRadius - 1, 8);
        if (ceiling < 1) ceiling = 1;
        // 2r+1 assumes sockets exactly on boundaries at root and tip; real spacing gives 2r-1.
        int lvlNow; float cellNow = LevelSize(out lvlNow);
        EditorGUILayout.LabelField("  ceiling",
            ceiling + " sockets, on level " + lvlNow + " at " + cellNow.ToString("F3") + " m");
        EditorGUILayout.LabelField("  reads",
            cells + " headers a vertex, plus one per socket actually found");
        _sockets = EditorGUILayout.IntSlider("Sockets to build", _sockets, 1, ceiling);
        // Same-cell sockets share both homes, so double hashing cannot help.
        float spacing = _length / (_sockets + 1);
        if (spacing < cellNow * 0.5f)
            EditorGUILayout.HelpBox(
                "At " + _sockets + " sockets the spacing is " + spacing.ToString("F3")
                + " m, under the " + (cellNow * 0.5f).ToString("F3") + " m octant. Two sockets in "
                + "ONE octant is the last clash nothing here fixes. Raise the neighbour radius, "
                + "which picks a smaller level, or use fewer.", MessageType.Warning);
        _grid = EditorGUILayout.IntSlider("Cells across", _grid, 2, 64);
        EditorGUILayout.LabelField("  slots",
            (_grid * _grid) + "   (two sockets landing on one slot means one of them vanishes)");
        _slotPx = EditorGUILayout.IntSlider("Slot, pixels", _slotPx, 1, 8);
        EditorGUILayout.LabelField("  atlas",
            AtlasWidth() + " x " + AtlasHeight() + " px, " + Columns() + " columns"
            + "   (a cell is 17 slots: a header, then eight octants of position and facing)");
        // The rect must fit every camera (mirrors are smaller). Only grid 16
        // (280 px) passed in game, so stated, not guarded.
        EditorGUILayout.LabelField("  needs a camera",
            "at least " + AtlasWidth() + " x " + AtlasHeight() + " px, MIRRORS INCLUDED");

        // Scales with the plug: 10 m at radius 2 ~ 1 mm, 20 cm ~ 0.02 mm.
        EditorGUILayout.LabelField("  precision",
            (LevelSize() * 0.000488f * 1000f).ToString("F3") + " mm   (the cell times a half-float step)");
        EditorGUILayout.LabelField("  a read sees",
            "a " + (LevelSize() * (2 * _cellRadius + 1)).ToString("F2")
            + " m box around the shaft midpoint, and the plug spans "
            + _length.ToString("F2") + " m");

        EditorGUILayout.Space();
        _length = EditorGUILayout.Slider("Plug length, metres", _length, 0.05f, 30f);
        _radius = EditorGUILayout.Slider("Plug radius, metres", _radius, 0.002f, 4f);
        _reach = EditorGUILayout.Slider("Reach, in plug lengths", _reach, 0.5f, 4f);
        // Two ceilings that pull opposite ways, both worth seeing before
        // wondering why a socket is ignored.
        EditorGUILayout.LabelField("  sockets are included within",
            (_length * _reach * 1.6f).ToString("F2") + " m of the root, "
            + "and threaded over the plug's own " + _length.ToString("F2") + " m of chain");

        EditorGUILayout.Space();
        _queueBase = EditorGUILayout.IntField("Atlas queue base", _queueBase);
        EditorGUILayout.LabelField("  clear/sockets/grabber",
            _queueBase + " / " + (_queueBase + 1) + " / " + (_queueBase + 2)
            + "   (the plug draws at Geometry, after the grab)");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Diagnosis", EditorStyles.boldLabel);
        _debug = EditorGUILayout.Popup("Plug colours", _debug,
            new[] { "normal", "debug: red=cells claiming a socket, green=found, blue=row flipped" });
        _forceRow = EditorGUILayout.Popup("Atlas row order", _forceRow,
            new[] { "auto, from _TexelSize", "force top-down", "force bottom-up" });

        EditorGUILayout.Space();
        if (GUILayout.Button("Build the spike")) Build();
        using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
        {
            if (GUILayout.Button("Push the sliders to the live materials")) Push();
            if (GUILayout.Button("What is in the atlas?")) Inspect();
        }
        if (GUILayout.Button("Remove it")) Remove();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Wear it", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Builds the same rig on the selected CVR avatar, ready to upload. The plug grows "
            + "forward from the hips; sockets ride the hands and head, so in game a controller "
            + "IS a socket: move the hand, rotate the wrist. No toggles, no pickups, no scripts. "
            + "Press Play and see it bend BEFORE uploading.", MessageType.Info);
        if (GUILayout.Button("Build on the selected avatar")) WearBuild();
        if (GUILayout.Button("Remove it from the selected avatar")) WearRemove();

        if (!EditorApplication.isPlaying)
            EditorGUILayout.HelpBox("Press Play. The grab does not run otherwise.", MessageType.None);
        if (!string.IsNullOrEmpty(_result)) EditorGUILayout.HelpBox(_result, MessageType.None);
    }

    void Build()
    {
        Remove();
        var sockShader = Shader.Find("YAPS/Spike Socket");
        var plugShader = Shader.Find("YAPS/Spike Plug");
        // The shipped grab, so the spike keeps it honest.
        var readShader = Shader.Find("YAPS/Atlas Grab");
        var clearShader = Shader.Find("YAPS/Spike Clear");
        if (sockShader == null || plugShader == null || readShader == null || clearShader == null)
        {
            _result = "Missing a shader. Needs YAPS/Spike Socket, Plug, Reader and Clear.";
            return;
        }

        var root = new GameObject(Root);
        Undo.RegisterCreatedObjectUndo(root, "YAPS atlas plug spike");

        // Alpha 0 first, or the opaque screen reads as a socket in every cell.
        // Huge bounds so it is never frustum-culled.
        var clear = Quad(root, "Clear", 200f);
        var cm = Asset("Clear", clearShader);
        cm.renderQueue = _queueBase;
        clear.GetComponent<MeshRenderer>().sharedMaterial = cm;

        // Spaced off the cell size: hand placement put two in one cell.
        // The plug's shaft runs along world +X.
        Vector3 from = new Vector3(-0.18f, 1.15f, 0f);
        for (int i = 0; i < _sockets; i++)
        {
            float along = _length * (i + 1) / (_sockets + 1);
            var at = from + new Vector3(along, 0.01f * (i % 2), 0.02f * (i % 3));
            // Facing back down the shaft, or the cubic hairpins. Alternating
            // kinds: rotated 180, a ring flips to meet the plug, a hole drops out.
            Socket(root.transform, sockShader, "Socket " + (char)('A' + i), at,
                   Quaternion.Euler(0, 270 + (i - 1) * 10, (i - 1) * 8), i % 2);
        }

        // Never skinned: a skinned renderer gets an identity matrix, so it cannot find its root.
        var plug = new GameObject(PlugName);
        plug.transform.SetParent(root.transform, false);
        plug.transform.position = new Vector3(-0.18f, 1.15f, 0f);
        plug.transform.rotation = Quaternion.Euler(0, 90, 0);
        plug.AddComponent<MeshFilter>().sharedMesh = Shaft();
        var pm = Asset("Plug", plugShader);
        pm.SetFloat("_MeshLength", 1f);
        plug.AddComponent<MeshRenderer>().sharedMaterial = pm;
        plug.transform.localScale = Vector3.one * _length;

        // Huge bounds: a camera only grabs if a GrabPass renders in it.
        var grab = Quad(root, "Grabber", 200f);
        var gm = Asset("Grabber", readShader);
        gm.renderQueue = _queueBase + 2;
        grab.GetComponent<MeshRenderer>().sharedMaterial = gm;

        Push();
        Selection.activeGameObject = root;
        _result = "Built. Press Play, then drag and rotate the sockets.";
    }

    GameObject Quad(GameObject root, string name, float scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        go.transform.SetParent(root.transform, false);
        go.transform.localScale = Vector3.one * scale;
        DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    GameObject Socket(Transform parent, Shader shader, string name, Vector3 at, Quaternion rot, int kind)
    {
        // Draws in clip space, but needs a real renderer: the matrix is the payload.
        // Two quads: the second goes to the cell's other home, surviving a clash.
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        go.transform.rotation = rot;
        go.AddComponent<MeshFilter>().sharedMesh = LevelQuads();
        go.AddComponent<MeshRenderer>();
        var m = Asset(name.Replace(" ", ""), shader);
        m.renderQueue = _queueBase + 1;
        m.SetFloat("_Kind", kind);
        go.GetComponent<MeshRenderer>().sharedMaterial = m;

        // Ring and stub show the facing (+Z); the built-in cylinder runs along +Y, so turned 90.
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "Mouth";
        ring.transform.SetParent(go.transform, false);
        ring.transform.localRotation = Quaternion.Euler(90, 0, 0);
        ring.transform.localScale = new Vector3(0.13f, 0.012f, 0.13f);
        DestroyImmediate(ring.GetComponent<Collider>());
        ring.GetComponent<MeshRenderer>().sharedMaterial = kind == 1
            ? Colour("MouthHole", new Color(0.75f, 0.35f, 0.7f))
            : Colour("Mouth", new Color(0.35f, 0.6f, 0.85f));

        var stub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stub.name = "Axis";
        stub.transform.SetParent(go.transform, false);
        stub.transform.localRotation = Quaternion.Euler(90, 0, 0);
        stub.transform.localPosition = new Vector3(0, 0, 0.06f);
        stub.transform.localScale = new Vector3(0.02f, 0.06f, 0.02f);
        DestroyImmediate(stub.GetComponent<Collider>());
        stub.GetComponent<MeshRenderer>().sharedMaterial = Colour("Axis", new Color(0.9f, 0.75f, 0.25f));
        return go;
    }

    // Pushes every slider onto the live materials, so the atlas layout can be
    // changed without rebuilding and losing where the sockets were dragged to.
    void Push()
    {
        for (int i = 0; i < 8; i++)
        {
            var m = Load("Socket" + (char)('A' + i));
            if (m == null) continue;
            m.SetFloat("_Grid", _grid);
            m.SetFloat("_Cols", Columns());
            m.SetFloat("_SlotPx", _slotPx);
            m.SetFloat("_OriginPx", OriginPx);
            m.SetFloat("_CellSize", _cellBase);
            m.SetFloat("_Levels", _levels);
            m.SetFloat("_Kind", i % 2);
        }
        var p = Load("Plug");
        if (p != null)
        {
            p.SetFloat("_Grid", _grid);
            p.SetFloat("_Cols", Columns());
            p.SetFloat("_SlotPx", _slotPx);
            p.SetFloat("_OriginPx", OriginPx);
            p.SetFloat("_CellSize", _cellBase);
            p.SetFloat("_Levels", _levels);
            p.SetFloat("_Reach", _reach);
            p.SetFloat("_Radius", _cellRadius);
            p.SetFloat("_Debug", _debug);
            p.SetFloat("_ForceRow", _forceRow);
        }
        var c = Load("Clear");
        if (c != null)
        {
            // Room to spare: a missed pixel reads the opaque screen as occupied.
            c.SetFloat("_SpanPxX", AtlasWidth() + 4);
            c.SetFloat("_SpanPxY", AtlasHeight() + 4);
        }
    }

    // Columns != grid: at grid 64 one row was 1088 px, narrower cameras
    // clipped slots, and a socket hashing there vanished (bend dropping out
    // every few cm). A divisor of the grid, so levels hold whole rows; set on
    // both shaders from here.
    int Columns()
    {
        int total = _grid * _grid;
        float ideal = Mathf.Sqrt(total * _levels / 17f);
        int best = _grid;
        for (int c = 1; c <= _grid; c++)
            if (_grid % c == 0 && Mathf.Abs(c - ideal) < Mathf.Abs(best - ideal)) best = c;
        return Mathf.Max(best, 1);
    }

    int AtlasWidth() => OriginPx + Columns() * 17 * _slotPx;

    int AtlasHeight() => OriginPx + _levels * Mathf.Max(_grid * _grid / Columns(), 1) * _slotPx;

    // Generated: the built-in cylinder has two vertex rings and cannot bend.
    Mesh Shaft()
    {
        const int rings = 48, segs = 18;
        // Not cached: the radius is baked into the vertices.
        string path = Dir + "/SpikeShaft.asset";

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris = new List<int>();
        float r0 = _radius / Mathf.Max(_length, 0.0001f);

        for (int r = 0; r <= rings; r++)
        {
            float t = (float)r / rings;
            // Rounded over the last eighth, so the tip is a dome rather than
            // a flat disc. k is 0 along the shaft and 1 at the very tip.
            float k = Mathf.InverseLerp(0.88f, 1f, t);
            float shape = Mathf.Sqrt(Mathf.Max(0f, 1f - k * k));
            for (int s = 0; s < segs; s++)
            {
                float a = (float)s / segs * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                verts.Add(dir * (r0 * shape) + Vector3.forward * t);
                norms.Add((dir * shape + Vector3.forward * k).normalized);
            }
        }
        for (int r = 0; r < rings; r++)
        for (int s = 0; s < segs; s++)
        {
            int a = r * segs + s, b = r * segs + (s + 1) % segs;
            tris.Add(a); tris.Add(a + segs); tris.Add(b);
            tris.Add(b); tris.Add(a + segs); tris.Add(b + segs);
        }
        // A disc closing the base, so the tube is not open where it meets
        // whatever it is growing from.
        int centre = verts.Count;
        verts.Add(Vector3.zero); norms.Add(Vector3.back);
        for (int s = 0; s < segs; s++)
        {
            tris.Add(centre); tris.Add(s); tris.Add((s + 1) % segs);
        }

        var mesh = new Mesh { name = "YAPS Spike Shaft" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        // Generous bounds: the deform moves vertices metres away from where
        // Unity thinks they are, and a culled plug is an invisible plug.
        mesh.bounds = new Bounds(Vector3.forward * 0.5f, Vector3.one * 20f);
        System.IO.Directory.CreateDirectory(Dir);
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // One quad per (level, home), flagged by z. Huge bounds: Unity culls by
    // bounds, and a culled socket stops publishing.
    Mesh LevelQuads()
    {
        // Cached: recreating it per socket deletes the asset earlier sockets use.
        string path = Dir + "/SpikeSocketQuads.asset";
        int quads = _levels * 2;
        var have = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (have != null && have.vertexCount == quads * 4) return have;

        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int q = 0; q < quads; q++)
        {
            int b = verts.Count;
            verts.Add(new Vector3(-0.5f, -0.5f, q));
            verts.Add(new Vector3( 0.5f, -0.5f, q));
            verts.Add(new Vector3(-0.5f,  0.5f, q));
            verts.Add(new Vector3( 0.5f,  0.5f, q));
            tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
        }
        var mesh = new Mesh { name = "YAPS Spike Socket Quads" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 40f);
        System.IO.Directory.CreateDirectory(Dir);
        if (have != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static Material Asset(string name, Shader shader)
    {
        string path = Dir + "/Plug" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null || m.shader != shader) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        return m;
    }

    static Material Load(string name)
    {
        return AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Plug" + name + ".mat");
    }

    static Material Colour(string name, Color c)
    {
        string path = Dir + "/Plug" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Unlit/Color"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = c;
        return m;
    }

    // The shaders' arithmetic on the CPU: tells a grey plug from a wrong-row read.
    void Inspect()
    {
        var tex = Shader.GetGlobalTexture("_YAPS_Atlas") as RenderTexture;
        if (tex == null) { _result = "No atlas. Is the grabber on screen?"; return; }
        var root = GameObject.Find(Root);
        if (root == null) { _result = "Build it first."; return; }

        var shot = new Texture2D(tex.width, tex.height, TextureFormat.RGBAFloat, false);
        var was = RenderTexture.active;
        RenderTexture.active = tex;
        shot.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        shot.Apply();
        RenderTexture.active = was;

        var lines = new List<string> { "atlas " + tex.width + "x" + tex.height + " " + tex.format };
        foreach (Transform t in root.transform)
        {
            if (!t.name.StartsWith("Socket")) continue;
            Vector3 scaled = t.position / Mathf.Max(LevelSize(), 0.0001f);
            var c = new Vector3Int(Mathf.FloorToInt(scaled.x), Mathf.FloorToInt(scaled.y), Mathf.FloorToInt(scaled.z));
            int total = _grid * _grid;
            int idx = SpikeCell.Hash(c.x, c.y, c.z) % total; if (idx < 0) idx += total;
            int gx = idx % _grid, gy = idx / _grid;
            int px = OriginPx + gx * 2 * _slotPx + _slotPx / 2;
            int fromTop = OriginPx + gy * _slotPx + _slotPx / 2;

            // ReadPixels counts rows from the BOTTOM and the shaders write
            // from the top, so both are reported rather than assumed.
            var wantPos = new Vector3(scaled.x - c.x, scaled.y - c.y, scaled.z - c.z);
            Vector3 wantFwd = t.forward * 0.5f + Vector3.one * 0.5f;
            int up = Mathf.Clamp(fromTop, 0, tex.height - 1);
            int dn = Mathf.Clamp(tex.height - 1 - fromTop, 0, tex.height - 1);
            lines.Add(t.name + "  cell " + c + " -> grid (" + gx + "," + gy + ")  px " + px);
            lines.Add("   want pos " + wantPos.ToString("F4") + "   fwd " + wantFwd.ToString("F3"));
            lines.Add("   row " + up + "   pos " + Fmt(shot.GetPixel(px, up))
                      + "  fwd " + Fmt(shot.GetPixel(px + _slotPx, up)));
            lines.Add("   row " + dn + "   pos " + Fmt(shot.GetPixel(px, dn))
                      + "  fwd " + Fmt(shot.GetPixel(px + _slotPx, dn)));
        }
        // Did the clear run? An unused cell must read alpha 0, or the plug
        // locks onto the floor, which looks like a row-order fault.
        var used = new HashSet<int>();
        foreach (Transform t in root.transform)
        {
            if (!t.name.StartsWith("Socket")) continue;
            Vector3 sc = t.position / Mathf.Max(LevelSize(), 0.0001f);
            int i2 = SpikeCell.Hash(Mathf.FloorToInt(sc.x), Mathf.FloorToInt(sc.y), Mathf.FloorToInt(sc.z))
                     % (_grid * _grid);
            if (i2 < 0) i2 += _grid * _grid;
            used.Add(i2);
        }
        for (int i = 0; i < _grid * _grid; i++)
        {
            if (used.Contains(i)) continue;
            int gx = i % _grid, gy = i / _grid;
            int px = OriginPx + gx * 2 * _slotPx + _slotPx / 2;
            int fromTop = OriginPx + gy * _slotPx + _slotPx / 2;
            Color e = shot.GetPixel(px, Mathf.Clamp(fromTop, 0, tex.height - 1));
            lines.Add("EMPTY cell grid (" + gx + "," + gy + ")  alpha " + e.a.ToString("F2")
                      + (e.a > 0.5f ? "   <-- THE CLEAR IS NOT RUNNING" : "   (clear is working)"));
            break;
        }

        // Magenta does not say which shader failed or why.
        foreach (string n in new[] { "YAPS/Spike Clear", "YAPS/Spike Socket", "YAPS/Spike Plug" })
        {
            var sh = Shader.Find(n);
            if (sh == null) { lines.Add(n + "  MISSING"); continue; }
            bool bad = UnityEditor.ShaderUtil.ShaderHasError(sh);
            int msgs = UnityEditor.ShaderUtil.GetShaderMessageCount(sh);
            lines.Add(n + "  " + (bad ? "FAILED TO COMPILE" : "compiles") + ", " + msgs + " messages");
            if (msgs > 0)
                foreach (var msg in UnityEditor.ShaderUtil.GetShaderMessages(sh))
                    lines.Add("    " + msg.severity + ": " + msg.message.Trim()
                              + (msg.line > 0 ? "  (line " + msg.line + ")" : ""));
        }

        DestroyImmediate(shot);
        _result = string.Join(System.Environment.NewLine, lines);
        Debug.Log("[SpikePlug] " + _result);
    }

    // The shader's cell plus octant. Sharing this is the one clash nothing
    // fixes; a shared slot alone is what the second home and tag handle.
    string Bucket(Vector3 at, float cell)
    {
        Vector3 sc = at / cell;
        var c = new Vector3Int(Mathf.FloorToInt(sc.x), Mathf.FloorToInt(sc.y), Mathf.FloorToInt(sc.z));
        Vector3 f = sc - c;
        int oct = (f.x > 0.5f ? 4 : 0) + (f.y > 0.5f ? 2 : 0) + (f.z > 0.5f ? 1 : 0);
        return c + ":" + oct;
    }

    // Both homes of a cell, matching HashCell / HashCell2 in the shaders.
    void Slots(Vector3 at, float cell, out int first, out int second)
    {
        int cx = Mathf.FloorToInt(at.x / cell);
        int cy = Mathf.FloorToInt(at.y / cell);
        int cz = Mathf.FloorToInt(at.z / cell);
        int total = _grid * _grid;
        first = SpikeCell.Hash(cx, cy, cz) % total;
        if (first < 0) first += total;
        int step;
        unchecked
        {
            int h = cx * 12582917;
            h ^= cy * 3145739;
            h ^= cz * 6291469;
            step = h % Mathf.Max(total - 1, 1);
        }
        if (step < 0) step += Mathf.Max(total - 1, 1);
        second = (first + step + 1) % total;
    }

    static string Fmt(Color c)
    {
        return "(" + c.r.ToString("F3") + "," + c.g.ToString("F3") + "," + c.b.ToString("F3")
             + ") a=" + c.a.ToString("F1");
    }

    void Remove()
    {
        var go = GameObject.Find(Root);
        if (go != null) Undo.DestroyObjectImmediate(go);
        _result = "";
    }

    // Plain renderers only, so the upload needs no scripts, layers or menu.
    // Sockets on hands and head: in VR the controller is the gizmo, and a
    // wrist twist is the arrival-axis test.
    void WearBuild()
    {
        var picked = Selection.activeGameObject;
        var avatar = picked != null ? picked.GetComponentInParent<CVRAvatar>() : null;
        if (avatar == null) { _result = "Select the CVR avatar to wear it."; return; }
        var anim = avatar.GetComponent<Animator>();
        if (anim == null || !anim.isHuman)
        { _result = "The avatar needs a humanoid Animator: the sockets ride its hands."; return; }
        Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        Transform lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        Transform head = anim.GetBoneTransform(HumanBodyBones.Head);
        if (hips == null || lHand == null || rHand == null)
        { _result = "Hips or a hand is not mapped on this rig."; return; }

        var sockShader = Shader.Find("YAPS/Spike Socket");
        var plugShader = Shader.Find("YAPS/Spike Plug");
        // The shipped grab, so the spike keeps it honest.
        var readShader = Shader.Find("YAPS/Atlas Grab");
        var clearShader = Shader.Find("YAPS/Spike Clear");
        if (sockShader == null || plugShader == null || readShader == null || clearShader == null)
        { _result = "Missing a shader. Needs YAPS/Spike Socket, Plug, Reader and Clear."; return; }

        WearRemove();
        Transform root = avatar.transform;

        // A converted avatar has the shipped clear, grab and writers; spike
        // copies beside them would mask a broken one. Reader only then.
        bool shipped = root.Find("YAPS Atlas Clear") != null
                       || root.Find("YAPS Atlas Grab") != null;

        // Scale only beats frustum culling; countered so a scaled rig cannot shrink it.
        if (!shipped)
        {
            var rig = new GameObject(WearPrefix + " Atlas");
            Undo.RegisterCreatedObjectUndo(rig, "YAPS atlas wear rig");
            rig.transform.SetParent(root, false);
            var clear = Quad(rig, "Clear", 1f);
            clear.transform.localScale = Counter(rig.transform, 200f);
            var cm = Asset("Clear", clearShader);
            cm.renderQueue = _queueBase;
            clear.GetComponent<MeshRenderer>().sharedMaterial = cm;
            var grab = Quad(rig, "Grabber", 1f);
            grab.transform.localScale = Counter(rig.transform, 200f);
            var gm = Asset("Grabber", readShader);
            gm.renderQueue = _queueBase + 2;
            grab.GetComponent<MeshRenderer>().sharedMaterial = gm;
        }

        // Facing back, toward the plug: the hole only opens facing the approach.
        if (!shipped)
        {
            Quaternion facing = root.rotation * Quaternion.Euler(0, 180, 0);
            WearSocket(lHand, sockShader, 'A', 0, facing);
            WearSocket(rHand, sockShader, 'B', 1, facing);
            if (head != null) WearSocket(head, sockShader, 'C', 0, facing);
        }

        // From the hips, forward. Never skinned (identity matrix); a rigid child rides the bone.
        var plug = new GameObject(WearPrefix + " Plug");
        plug.transform.SetParent(hips, false);
        plug.transform.localScale = Counter(hips, _length);
        plug.transform.rotation = root.rotation;
        plug.transform.position = hips.position + root.forward * 0.05f;
        plug.AddComponent<MeshFilter>().sharedMesh = Shaft();
        var pm = Asset("Plug", plugShader);
        pm.SetFloat("_MeshLength", 1f);
        plug.AddComponent<MeshRenderer>().sharedMaterial = pm;

        Push();
        Selection.activeGameObject = avatar.gameObject;
        _result = shipped
            ? "Worn as a READER only. This avatar carries the shipped clear, grab and socket "
            + "writers, so the spike built none of its own: everything but the plug is shipped "
            + "code. Press Play and bring one of the avatar's OWN sockets near the plug at the "
            + "hips. A bend is the shipped writer working."
            : "Worn. Press Play HERE first: the plug grows forward from the hips and must "
            + "bend toward a hand brought near it, ring on the left, hole on the right, one "
            + "more on the head. If that holds, upload, then check the same three in game: "
            + "flat screen, VR, and a mirror.";
    }

    // A named holder per bone keeps the socket child on the scene spike's
    // names, so it lands on the SAME materials and Push reaches both rigs.
    void WearSocket(Transform bone, Shader shader, char letter, int kind, Quaternion facing)
    {
        var holder = new GameObject(WearPrefix + " " + bone.name);
        holder.transform.SetParent(bone, false);
        holder.transform.localScale = Counter(bone, 1f);
        Socket(holder.transform, shader, "Socket " + letter, bone.position, facing, kind);
    }

    // World size regardless of parent scale (a 0.01 armature shrinks it 100x).
    static Vector3 Counter(Transform parent, float world)
    {
        Vector3 s = parent.lossyScale;
        return new Vector3(world / Mathf.Max(Mathf.Abs(s.x), 1e-6f),
                           world / Mathf.Max(Mathf.Abs(s.y), 1e-6f),
                           world / Mathf.Max(Mathf.Abs(s.z), 1e-6f));
    }

    void WearRemove()
    {
        var picked = Selection.activeGameObject;
        var avatar = picked != null ? picked.GetComponentInParent<CVRAvatar>() : null;
        Transform scan = avatar != null ? avatar.transform
                       : picked != null ? picked.transform : null;
        if (scan == null) { _result = "Select the avatar it is worn on."; return; }
        var doomed = new List<GameObject>();
        foreach (var t in scan.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith(WearPrefix)) doomed.Add(t.gameObject);
        foreach (var go in doomed) if (go != null) Undo.DestroyObjectImmediate(go);
        _result = doomed.Count > 0 ? "Removed." : "Nothing worn there.";
    }
}
#endif
