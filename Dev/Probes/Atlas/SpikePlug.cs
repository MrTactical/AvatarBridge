// Spike 4: a plug and a socket, running on nothing but the screen atlas.
//
// Written for Joe. Dev only, pruned from public builds.
//
//   Tools > YAPS > Screen atlas: plug and socket
//
// Spikes 1 to 3 proved the transport a piece at a time: a named GrabPass
// survives a ChilloutVR upload, it carries a moving world position to about
// a tenth of a millimetre, and two objects that have never heard of each
// other can meet in a cell derived from where they both are. What none of
// them showed is the thing the transport exists for. This one bends.
//
// Nothing here is wired. The plug holds no reference to a socket, no
// contact pair is registered, no light is emitted, no animator parameter
// moves and nothing syncs. Drag a socket near the plug and the plug bends
// into it, because the socket drew two pixels and the plug read them.
//
// What to look for, in order:
//
//   grey plug            it found nothing. Either the atlas is not being
//                        drawn (is the grabber on screen?) or the row order
//                        is flipped: press "What is in the atlas".
//   bends toward one     the rendezvous works.
//   ROTATE a socket      the plug should swing to arrive along the socket's
//                        own axis, not just aim at its centre. That is the
//                        second pixel doing its job, and it is the thing the
//                        light protocol cannot do at all.
//   move the far socket  in closer, and the plug should switch to it.
//
// Deliberately NOT the shipping deform: a generated cylinder along local +Z
// bent along a cubic, so that anything wrong is obviously the transport
// rather than the bake.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

public class SpikePlug : EditorWindow
{
    [MenuItem("Tools/YAPS/Screen atlas: plug and socket")]
    static void Open() { GetWindow<SpikePlug>("Atlas plug"); }

    const string Root = "YAPS Atlas Plug Spike";
    const string PlugName = "Plug";
    const string WearPrefix = "YAPS Wear";
    const string Dir = "Assets/YapsSpike";
    const int OriginPx = 8;

    float _cellBase = 0.02f;
    // Four levels: cells from 2 cm to 1.28 m, plugs to about five metres.
    // The guess that this is all anyone needs gets its test in game before
    // six levels earn their extra rows of atlas.
    int _levels = 4;
    int _cellRadius = 2;   // cells out, NOT the plug radius above
    int _sockets = 3;
    // 64, not 16. Sockets clash by hashing to a slot another cell owns, and
    // the second home is the recovery, so what matters is clashing on BOTH:
    // 256 slots loses about one socket in seven at 120 sockets, which is
    // twenty people carrying six each, an ordinary instance. 4096 makes it
    // one in twelve hundred. Costs no reads and no visible dots, since the
    // clear writes alpha only and the dots follow the socket count. It costs
    // WIDTH, and whether a mirror is that wide is the open question.
    int _grid = 64;
    int _slotPx = 1;
    float _reach = 1.6f;
    float _length = 0.22f;
    float _radius = 0.022f;
    int _queueBase = 1000;
    int _debug = 0;
    int _forceRow = 0;
    string _result = "";

    // The level the plug will pick, and the cell size that comes with it.
    // Same arithmetic as the shader: coverage wants a cell of about L/2r and
    // the levels go up in fours.
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

    // Draws the two ceilings in the scene view, because "why is that socket
    // ignored" has two completely different answers and they look identical
    // from outside: it is outside the box the plug can READ, or it is inside
    // the box and past the end of the CHAIN the plug can cover.
    void OnScene(SceneView sv)
    {
        var root = GameObject.Find(Root);
        var plug = root != null ? root.transform.Find(PlugName) : null;
        if (plug == null) return;

        Vector3 axis = plug.forward;
        Vector3 at0 = plug.position;
        float len = plug.TransformVector(Vector3.forward).magnitude;
        float cell = Mathf.Max(LevelSize(), 0.0001f);

        // The neighbourhood, exactly as the shader hashes it: a cube of
        // (2r+1) cells centred on the cell the shaft MIDPOINT falls in. Note
        // it is anchored to the cell, not to the plug, so it shifts in steps
        // as the plug moves and is never quite centred on it.
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

        // The chain, in the shader's own order: nearest to the root first,
        // then cumulative chord length. A socket sitting past the plug's own
        // length is found and simply never reached.
        var socks = new List<Transform>();
        foreach (Transform t in root.transform)
            if (t.name.StartsWith("Socket")) socks.Add(t);
        socks.Sort((a, b) => Vector3.Distance(a.position, at0)
                     .CompareTo(Vector3.Distance(b.position, at0)));

        // Which grid slot each socket hashes to. Two on one slot means the
        // second to draw overwrites the first and the loser is dropped by
        // the tag check, silently, for as long as it stays in that cell.
        // It presents as a DEADZONE exactly one cell across, which is how
        // this was found: socket B stopped working between y 1.26 and 1.44,
        // the bounds of a single cell at eighteen centimetres.
        // Buckets moved this. Two sockets in one CELL used to lose one of
        // them; now a cell holds eight octants, so they only fight when they
        // are in the same octant too. The old cell-only test cried wolf on
        // every socket once the cell grew larger than the spacing.
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
            // A hole approached through its back is dropped by the resolver,
            // which is correct and looks exactly like every other reason a
            // socket does not bend, so it gets said out loud.
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
        // The number Joe actually asked for, and it comes from the
        // NEIGHBOURHOOD rather than from the length of the list.
        // Buckets change the bound. Coverage still wants cell >= L/2r, but
        // an OCTANT separates sockets half a cell apart rather than a whole
        // one, so spacing L/(N+1) >= cell/2 gives N <= 4r-1 instead of 2r-1.
        int ceiling = Mathf.Min(4 * _cellRadius - 1, 8);
        if (ceiling < 1) ceiling = 1;
        // Two different ceilings, and the useful one is the smaller.
        // 2r+1 assumes sockets landing exactly on cell boundaries at the
        // very root and the very tip; with the cell fitted to the plug the
        // spacing that actually separates them gives 2r-1.
        int lvlNow; float cellNow = LevelSize(out lvlNow);
        EditorGUILayout.LabelField("  ceiling",
            ceiling + " sockets, on level " + lvlNow + " at " + cellNow.ToString("F3") + " m");
        EditorGUILayout.LabelField("  reads",
            cells + " headers a vertex, plus one per socket actually found");
        _sockets = EditorGUILayout.IntSlider("Sockets to build", _sockets, 1, ceiling);
        // Sockets in the SAME cell is the one clash double hashing cannot
        // help with: two homes are a property of the CELL, so two sockets in
        // one cell share both. Buckets are the fix and are not built.
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
            (OriginPx + _grid * 17 * _slotPx) + " x " + (OriginPx + _levels * _grid * _slotPx) + " px"
            + "   (a cell is 17 slots: a header, then eight octants of position and facing)");
        // The writer places pixels from _ScreenParams, so the rect has to FIT
        // every camera that carries the atlas. A mirror renders into its own
        // texture and is routinely smaller than the main view; the in-game
        // mirror test passed at grid 16, which is 280 px, and says nothing
        // about a wider one. Unverified, so it is stated rather than guarded:
        // inventing a threshold here would assert a mirror size nobody has
        // measured.
        EditorGUILayout.LabelField("  needs a camera",
            "at least " + (OriginPx + _grid * 17 * _slotPx) + " px wide, MIRRORS INCLUDED"
            + "   (untested above 280)");

        // Precision follows the cell, so it scales with the plug: a ten
        // metre plug at radius 2 resolves to about a millimetre, which is
        // proportionally the same as a twenty centimetre one at 0.02 mm.
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
        var readShader = Shader.Find("YAPS/Spike Reader");
        var clearShader = Shader.Find("YAPS/Spike Clear");
        if (sockShader == null || plugShader == null || readShader == null || clearShader == null)
        {
            _result = "Missing a shader. Needs YAPS/Spike Socket, Plug, Reader and Clear.";
            return;
        }

        var root = new GameObject(Root);
        Undo.RegisterCreatedObjectUndo(root, "YAPS atlas plug spike");

        // Draws first and paints the atlas rect alpha 0, so an empty cell can
        // be told from an occupied one. Without it the grab returns the
        // opaque screen and every cell reads as a socket at the cell origin.
        // Scaled huge for the same reason as the grabber: it draws in clip
        // space and ignores its transform, but a one-metre quad at the
        // origin can still be frustum-culled, and a clear that does not draw
        // leaves every cell reading the opaque screen as occupied.
        var clear = Quad(root, "Clear", 200f);
        var cm = Asset("Clear", clearShader);
        cm.renderQueue = _queueBase;
        clear.GetComponent<MeshRenderer>().sharedMaterial = cm;

        // THREE, spaced off the CELL SIZE rather than hardcoded, because
        // the two are not independent. One cell holds one socket, so sockets
        // closer than a cell cannot both exist; and a plug only threads what
        // fits within its own length of chain. Placing them by hand is how
        // two of them ended up in one cell and only two of the three showed.
        //
        // The plug below is turned ninety degrees, so its shaft runs along
        // world +X from its root.
        Vector3 from = new Vector3(-0.18f, 1.15f, 0f);
        for (int i = 0; i < _sockets; i++)
        {
            float along = _length * (i + 1) / (_sockets + 1);
            var at = from + new Vector3(along, 0.01f * (i % 2), 0.02f * (i % 3));
            // Facing BACK down the shaft. A socket pointing the way the plug
            // travels is entered through its back, and the cubic ties itself
            // in a hairpin to arrive that way.
            // Alternating, so both behaviours are on screen at once. Rotate
            // one 180 degrees and watch the difference: a RING flips to meet
            // the plug and keeps working, a HOLE is being approached through
            // its back and drops out of the chain entirely.
            Socket(root.transform, sockShader, "Socket " + (char)('A' + i), at,
                   Quaternion.Euler(0, 270 + (i - 1) * 10, (i - 1) * 8), i % 2);
        }

        // The plug. An ORDINARY mesh, never skinned: Unity skins into world
        // space and hands a SkinnedMeshRenderer an identity matrix, so a
        // skinned plug could not work out where its own root is.
        var plug = new GameObject(PlugName);
        plug.transform.SetParent(root.transform, false);
        plug.transform.position = new Vector3(-0.18f, 1.15f, 0f);
        plug.transform.rotation = Quaternion.Euler(0, 90, 0);
        plug.AddComponent<MeshFilter>().sharedMesh = Shaft();
        var pm = Asset("Plug", plugShader);
        pm.SetFloat("_MeshLength", 1f);
        plug.AddComponent<MeshRenderer>().sharedMaterial = pm;
        plug.transform.localScale = Vector3.one * _length;

        // A DEDICATED grabber, scaled huge so no camera can cull it. A camera
        // only grabs if something carrying the GrabPass renders in it, and a
        // small object near the origin can simply be off screen.
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
        // The writer draws in CLIP space and ignores this object's transform,
        // so its own mesh is never seen. It still needs a real MeshRenderer
        // with a real matrix, because the matrix is the payload.
        //
        // TWO quads, not one: the shader sends the second to this cell's
        // other slot, so a socket that loses a slot clash is still readable
        // from its second home.
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

        // Something to look at, and something that shows which way the socket
        // FACES. A ring around the parent's local +Z, plus a stub along it:
        // the built-in cylinder runs along its own +Y, so the child is turned
        // ninety degrees to line the two up.
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
            // Covers the whole snapped rect with room to spare. Clearing a
            // few unused pixels costs nothing; missing one leaves a cell
            // reading the opaque screen, which reads as occupied.
            c.SetFloat("_SpanPxX", OriginPx + _grid * 17 * _slotPx + 4);
            c.SetFloat("_SpanPxY", OriginPx + _levels * _grid * _slotPx + 4);
        }
    }

    // A cylinder along local +Z with a rounded tip, generated because the
    // built-in cylinder has two rings of vertices and therefore cannot bend
    // at all. Radius and length are baked at one unit and scaled by the
    // transform, so the same mesh serves any size.
    Mesh Shaft()
    {
        const int rings = 48, segs = 18;
        // Regenerated every build rather than cached: the radius is baked
        // into the vertices, so a cached mesh would silently ignore the
        // slider that appears to control it.
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

    // A unit quad per (level, home), flagged by z. The socket shader reads
    // that flag to decide which level it is publishing to and which of the
    // cell's two homes it is filling.
    //
    // The bounds are deliberately huge. The shader ignores the transform and
    // writes clip space, but Unity still culls by BOUNDS, and a socket
    // culled out of frame stops publishing entirely.
    Mesh LevelQuads()
    {
        // Cached, unlike the shaft: this mesh has no parameters, and
        // recreating it per socket would delete the asset the previous two
        // are pointing at.
        // One quad per (level, home). The socket shader reads z to work out
        // which level it is publishing to and which of that cell's two homes
        // it is filling, and both passes place the same quads differently.
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

    // The same arithmetic the shaders do, done on the CPU, so a grey plug can
    // be told apart from a plug that read the wrong row. Guessing at a flip
    // is how spike 3 spent a run reading the floor.
    void Inspect()
    {
        var tex = Shader.GetGlobalTexture("_YAPS_SpikeAtlas") as RenderTexture;
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
        // Did the CLEAR run? A cell no socket hashes to has to read alpha 0.
        // If it reads 1 the atlas is holding the opaque screen, every one of
        // the plug's twenty-seven cells claims a socket, and it locks onto
        // whichever piece of floor decodes nearest. That is indistinguishable
        // from a row-order problem by eye, which is why it is measured.
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

        // Every shader, not just the clear. A magenta mesh says one of them
        // failed and says nothing about which or why, and reading the error
        // beats reading the source.
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

    // The shader's cell-to-slot arithmetic, so the scene view and the GPU
    // agree about which sockets are fighting over a pixel.
    // Cell plus octant, which is what a socket actually occupies. Two
    // sockets sharing this are the last clash nothing fixes; sharing only the
    // cell is fine, and sharing only a grid slot is what the second home and
    // the tag are for.
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

    // The spike, worn. Everything travels as plain renderers on shared
    // materials, so the upload needs no scripts, no animator layers and no
    // menu entries; the CCK packs what the renderers reference. Sockets ride
    // the hands and the head because in VR that makes the controller the
    // socket gizmo: moving a hand is moving a socket, rotating the wrist is
    // the arrival-axis test, and no toggles are needed at all.
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
        var readShader = Shader.Find("YAPS/Spike Reader");
        var clearShader = Shader.Find("YAPS/Spike Clear");
        if (sockShader == null || plugShader == null || readShader == null || clearShader == null)
        { _result = "Missing a shader. Needs YAPS/Spike Socket, Plug, Reader and Clear."; return; }

        WearRemove();
        Transform root = avatar.transform;

        // Clear and grabber under the avatar root. Both draw in clip space
        // and ignore where they sit; the scale only exists to beat frustum
        // culling, and it is countered so a scaled rig cannot shrink it back
        // into cullable range.
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

        // Facing the wearer's BACK, which is toward the oncoming plug. The
        // ring would flip to meet it anyway; the hole on the right hand only
        // opens facing the approach, so both start correct and a twist of
        // the wrist shows the difference between the kinds live.
        Quaternion facing = root.rotation * Quaternion.Euler(0, 180, 0);
        WearSocket(lHand, sockShader, 'A', 0, facing);
        WearSocket(rHand, sockShader, 'B', 1, facing);
        if (head != null) WearSocket(head, sockShader, 'C', 0, facing);

        // From the hips, along the avatar's forward. Never skinned: Unity
        // skins into world space and hands the shader an identity matrix, so
        // a skinned plug cannot find its own root. Parented to the bone it
        // rides animation fine as a rigid child.
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
        _result = "Worn. Press Play HERE first: the plug grows forward from the hips and must "
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

    // World-size a child no matter what the rig scaled its parent to. Import
    // scale hides here: a 0.01 armature would shrink a hand socket's gizmo
    // to a hundredth, and a scaled clear quad back into cullable range.
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
