// Spike 3: can a plug find a socket it was never told about?
//
// Written for Joe. Dev only, pruned from public builds.
//
//   Tools > YAPS > Screen atlas: rendezvous
//
// Two sockets and a plug, all draggable. Each socket publishes into a cell
// derived from its own world position. The plug derives ITS cell, reads its
// own and the neighbouring cells out of the grab, and reports what it found
// and how far off it was. Nobody is told anything.
//
// Four things this has to settle, and only the first is obvious:
//
//   does the rendezvous work at all
//   is the error about 0.24 mm rather than spike 2's 4 mm
//   does it survive a CELL BOUNDARY, where socket and plug are centimetres
//     apart in different cells
//   does the occupancy flag survive the grab, or does an empty cell read as
//     a socket sitting at the cell origin
//
// The neighbourhood is the real design question. Reading only your own cell
// misses a socket a centimetre the other side of a boundary; reading 3x3x3
// costs 27 taps. If 27 is too many the grid has to be coarser, which costs
// precision. This measures which radius is actually needed.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class SpikeCell : EditorWindow
{
    [MenuItem("Tools/YAPS/Screen atlas: rendezvous")]
    static void Open() { GetWindow<SpikeCell>("Atlas rendezvous"); }

    const string Root = "YAPS Atlas Rendezvous Spike";
    const string PlugName = "Plug (drag me)";

    float _cellSize = 0.5f;
    int _grid = 8;
    float _cellPixels = 0.02f;
    Vector2 _corner = new Vector2(-0.95f, 0.95f);
    int _radius = 1;
    bool _flipY = true;

    // Cost measurement. A named grab runs once per frame per NAME, so the
    // cost should scale with CAMERAS and not with sockets. Both halves are
    // worth measuring rather than assuming: extra cameras to see the per
    // camera slope, extra sockets to confirm the slope is flat.
    int _extraCameras = 0;
    int _extraSockets = 0;
    int _taps = 27;
    int _tapMeshes = 8;
    bool _tapsOnly = true;

    // WHERE IN THE FRAME the atlas is drawn, which decides whether anyone
    // can see it. Unity lets a material override its shader's queue, so this
    // needs no new shaders: clear at base, writers at base+1, grabber at
    // base+2, and the grab therefore happens before the scene paints over
    // the region. Background is 1000 and Geometry is 2000, so a base of 1000
    // puts the whole atlas before any opaque geometry.
    int _queueBase = 1000;
    string _queueNote = "";
    // Pixel-snapped mode: the atlas is placed in PIXELS rather than clip
    // units, so cells land on exact pixel boundaries and the readback can
    // hit their centres however small they are.
    bool _snap = true;
    int _cellPx = 2;
    const int OriginPx = 8;
    int _phase = -1, _frames;
    double _sum, _onMs, _offMs;
    const int Samples = 180;
    string _result = "";

    static MaterialPropertyBlock _blockCache;

    void OnEnable() { EditorApplication.update += Tick; }
    void OnDisable() { EditorApplication.update -= Tick; }
    void Tick()
    {
        if (!EditorApplication.isPlaying) { _phase = -1; return; }
        Repaint();
        if (_phase < 0) return;

        // Two windows of equal length, spike on then off, so anything else
        // going on in the scene lands in both.
        _sum += Time.unscaledDeltaTime * 1000.0;
        if (++_frames < Samples) return;

        double avg = _sum / _frames;
        _frames = 0; _sum = 0;
        var root = GameObject.Find(Root);
        if (_phase == 0)
        {
            _onMs = avg;
            // Toggling the whole root changes the SCENE, not the atlas: 40
            // extra spheres cost about 7.5 ms whether they read the atlas
            // sixty-four times or not at all, which is what the first run of
            // this measured and I read as a tap cost. Tap mode holds every
            // object in place and changes only the tap count, so the delta
            // is the taps and nothing else.
            if (_tapsOnly) SetTaps(0);
            else if (root != null) root.SetActive(false);
            _phase = 1;
        }
        else
        {
            _offMs = avg;
            if (_tapsOnly) SetTaps(_taps);
            else if (root != null) root.SetActive(true);
            _phase = -1;
            double d = _onMs - _offMs;
            _result = "with the atlas    " + _onMs.ToString("F3") + " ms"
                    + System.Environment.NewLine + "without it        " + _offMs.ToString("F3") + " ms"
                    + System.Environment.NewLine + "difference        " + d.ToString("F3") + " ms"
                    + "   (" + (_offMs > 0 ? (d / _offMs * 100.0) : 0).ToString("F1") + " per cent)"
                    + System.Environment.NewLine + "cameras " + (1 + _extraCameras)
                    + ", sockets " + (2 + _extraSockets)
                    + ", tap meshes " + _tapMeshes + " at " + _taps + " taps"
                    + " (" + (_tapMeshes * 515L * _taps).ToString("N0") + " reads a frame)"
                    + System.Environment.NewLine
                    + "A difference under the frame-to-frame noise means the grab is not the cost.";
            Debug.Log("[SpikeCell] " + _result);
        }
    }

    // Must match HashCell in YapsSpikeCell.shader exactly, wrap included.
    public static int Hash(int x, int y, int z)
    {
        unchecked
        {
            int h = x * 73856093;
            h ^= y * 19349663;
            h ^= z * 83492791;
            return h;
        }
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Build, press Play, drag the plug and the sockets. Nobody is told where anything is: "
            + "each side derives the same cell from the same world coordinates.", MessageType.Info);

        _cellSize = EditorGUILayout.Slider("Cell size, metres", _cellSize, 0.1f, 2f);
        _grid = EditorGUILayout.IntSlider("Cells across", _grid, 2, 16);
        // Down to a pixel or two. The payload is read from the cell CENTRE, so
        // one pixel is enough in principle; what breaks it is anything that
        // blends neighbours, MSAA or a resolve, and how far off centre the
        // readback lands when a cell is only a pixel wide.
        _cellPixels = EditorGUILayout.Slider("Cell on screen", _cellPixels, 0.002f, 0.06f);
        _snap = EditorGUILayout.ToggleLeft("Snap the atlas to whole pixels", _snap);
        if (_snap) _cellPx = EditorGUILayout.IntSlider("Cell, pixels", _cellPx, 1, 16);
        int shownPx = _snap ? _cellPx : Mathf.RoundToInt(_cellPixels * 0.5f * 1920f);
        EditorGUILayout.LabelField("  cell size",
            shownPx + " px   atlas " + (shownPx * _grid) + " px across"
            + (_snap ? "   (snapped)" : "   (clip space, 1920 wide)"));
        _radius = EditorGUILayout.IntSlider("Neighbour radius", _radius, 0, 2);
        // ReadPixels has y=0 at the BOTTOM; clip space +1 is the top. The
        // reader shader compensates through _TexelSize.y and this did not,
        // so the first run sampled the floor and every cell read as occupied
        // because the screen is opaque.
        using (new EditorGUI.DisabledScope(_snap))
            _flipY = EditorGUILayout.ToggleLeft(
                _snap ? "Flip Y (not used when snapped)" : "Flip Y when reading back", _flipY);
        if (GUILayout.Button("Find the patches (which orientation?)")) Locate();
        EditorGUILayout.LabelField("  taps per read", ((2 * _radius + 1) * (2 * _radius + 1) * (2 * _radius + 1)).ToString());

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Cost", EditorStyles.boldLabel);
        _extraCameras = EditorGUILayout.IntSlider("Extra cameras", _extraCameras, 0, 6);
        _extraSockets = EditorGUILayout.IntSlider("Extra sockets", _extraSockets, 0, 30);
        _taps = EditorGUILayout.IntSlider("Taps per vertex", _taps, 0, 64);
        _tapMeshes = EditorGUILayout.IntSlider("Tap meshes", _tapMeshes, 0, 40);
        EditorGUILayout.LabelField("  reads per frame",
            (_tapMeshes * 515L * _taps).ToString("N0") + "   (a sphere is about 515 verts)");
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Visibility", EditorStyles.boldLabel);
        _queueBase = EditorGUILayout.IntField("Atlas queue base", _queueBase);
        EditorGUILayout.LabelField("  clear/writers/grabber",
            _queueBase + " / " + (_queueBase + 1) + " / " + (_queueBase + 2)
            + "   (Background 1000, Geometry 2000)");
        if (GUILayout.Button("Apply the queue")) ApplyQueue();
        if (!string.IsNullOrEmpty(_queueNote)) EditorGUILayout.LabelField("  now", _queueNote);

        _tapsOnly = EditorGUILayout.ToggleLeft(
            "Measure the TAPS only (hold the scene still, change only the tap count)", _tapsOnly);
        using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || _phase >= 0))
        {
            if (GUILayout.Button(_phase >= 0
                    ? "measuring, phase " + (_phase + 1) + " of 2"
                    : "Measure the cost (" + (Samples * 2) + " frames)"))
            {
                _phase = 0; _frames = 0; _sum = 0;
                var r = GameObject.Find(Root);
                if (r != null) r.SetActive(true);
            }
        }

        if (GUILayout.Button("Build the spike")) Build();
        if (GUILayout.Button("Remove it")) Remove();

        EditorGUILayout.Space();
        if (EditorApplication.isPlaying) Measure();
        else EditorGUILayout.HelpBox("Press Play. The grab does not run otherwise.", MessageType.None);
        if (!string.IsNullOrEmpty(_result)) EditorGUILayout.HelpBox(_result, MessageType.None);
    }

    void Build()
    {
        Remove();
        var cell = Shader.Find("YAPS/Spike Cell");
        var reader = Shader.Find("YAPS/Spike Reader");
        if (cell == null || reader == null) { _result = "Missing YAPS/Spike Cell or YAPS/Spike Reader."; return; }

        var root = new GameObject(Root);
        Undo.RegisterCreatedObjectUndo(root, "YAPS atlas rendezvous spike");

        // Draws first and paints the whole atlas alpha 0, so an empty cell
        // can be told from an occupied one. Without it the grab returns the
        // opaque screen and every cell reads as a socket.
        var clearShader = Shader.Find("YAPS/Spike Clear");
        if (clearShader != null)
        {
            var clr = GameObject.CreatePrimitive(PrimitiveType.Quad);
            clr.name = "Clear";
            clr.transform.SetParent(root.transform, false);
            DestroyImmediate(clr.GetComponent<Collider>());
            var cm = Asset("Clear", clearShader);
            cm.SetVector("_Corner", new Vector4(_corner.x, _corner.y, 0, 0));
            cm.SetFloat("_CellPixels", _cellPixels);
            cm.SetFloat("_Grid", _grid);
            clr.GetComponent<MeshRenderer>().sharedMaterial = cm;
        }

        Socket(root, cell, "Socket A", new Vector3(0.2f, 1.1f, 0.3f));
        Socket(root, cell, "Socket B", new Vector3(-1.4f, 1.0f, 0.9f));

        var plug = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plug.name = PlugName;
        plug.transform.SetParent(root.transform, false);
        plug.transform.localScale = Vector3.one * 0.08f;
        plug.transform.position = new Vector3(0.35f, 1.15f, 0.25f);
        DestroyImmediate(plug.GetComponent<Collider>());
        plug.GetComponent<MeshRenderer>().sharedMaterial = Asset("PlugMarker", reader);

        // A DEDICATED grabber, scaled huge so no camera can cull it.
        //
        // A camera only grabs if something carrying the GrabPass renders in
        // it. The plug marker is a small cube near the origin, so an extra
        // camera looking from across the scene may simply not see it and
        // never grab at all — which is why the first per-camera measurement
        // was unverified. The shader writes clip space and ignores this
        // object's transform, so scale changes nothing except the bounds.
        var grab = GameObject.CreatePrimitive(PrimitiveType.Quad);
        grab.name = "Grabber";
        grab.transform.SetParent(root.transform, false);
        grab.transform.localScale = Vector3.one * 200f;
        DestroyImmediate(grab.GetComponent<Collider>());
        grab.GetComponent<MeshRenderer>().sharedMaterial = Asset("Grabber", reader);

        // Something must carry the GrabPass or nothing grabs. The reader
        // material already has one.
        for (int i = 0; i < _extraCameras; i++)
        {
            var cam = new GameObject("Extra camera " + i);
            cam.transform.SetParent(root.transform, false);
            cam.transform.position = new Vector3(2f + i, 1.5f, 2f);
            cam.transform.LookAt(new Vector3(0, 1.2f, 0));
            var c = cam.AddComponent<Camera>();
            c.depth = -10 - i;
            c.targetTexture = new RenderTexture(512, 512, 24);
        }
        // The tap load: the cost a plug's own vertex shader would pay.
        var tapShader = Shader.Find("YAPS/Spike Taps");
        if (tapShader != null && _tapMeshes > 0)
        {
            var tm = Asset("Taps", tapShader);
            tm.SetFloat("_Taps", _taps);
            for (int i = 0; i < _tapMeshes; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "Tap load " + i;
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(-3f + (i % 8) * 0.8f, 0.6f, -2f - (i / 8) * 0.8f);
                DestroyImmediate(go.GetComponent<Collider>());
                go.GetComponent<MeshRenderer>().sharedMaterial = tm;
            }
        }

        for (int i = 0; i < _extraSockets; i++)
        {
            Socket(root, cell, "Socket X" + i,
                new Vector3(-2f + i * 0.37f, 1.0f + (i % 3) * 0.4f, -1f + (i % 5) * 0.31f));
        }

        Selection.activeGameObject = plug;
        _result = "Built. Press Play, then drag the plug toward a socket.";
    }

    void Socket(GameObject root, Shader shader, string name, Vector3 at)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(root.transform, false);
        go.transform.localScale = Vector3.one * 0.09f;
        go.transform.position = at;
        DestroyImmediate(go.GetComponent<Collider>());
        var m = Asset(name.Replace(" ", ""), shader);
        m.SetVector("_Corner", new Vector4(_corner.x, _corner.y, 0, 0));
        m.SetFloat("_CellPixels", _cellPixels);
        m.SetFloat("_Grid", _grid);
        m.SetFloat("_CellSize", _cellSize);
        m.SetFloat("_CellPx", _snap ? _cellPx : 0);
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
    }

    // Saved as assets: a material made with new Material() in the scene does
    // not carry its shader into an upload, which cost two uploads on spike 1.
    static Material Asset(string name, Shader shader)
    {
        const string dir = "Assets/YapsSpike";
        string path = dir + "/Cell" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null || m.shader != shader) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        return m;
    }

    void Measure()
    {
        var root = GameObject.Find(Root);
        var plug = root != null ? root.transform.Find(PlugName) : null;
        if (plug == null) { _result = "Build it first."; return; }

        var tex = Shader.GetGlobalTexture("_YAPS_SpikeAtlas") as RenderTexture;
        if (tex == null) { _result = "No atlas yet. The plug marker has to be on screen."; return; }

        var shot = new Texture2D(tex.width, tex.height, TextureFormat.RGBAFloat, false);
        var was = RenderTexture.active;
        RenderTexture.active = tex;
        shot.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        shot.Apply();
        RenderTexture.active = was;

        Vector3 me = plug.position;
        Vector3 scaled = me / Mathf.Max(_cellSize, 0.0001f);
        var mine = new Vector3Int(Mathf.FloorToInt(scaled.x), Mathf.FloorToInt(scaled.y), Mathf.FloorToInt(scaled.z));

        var hits = new List<string>();
        Vector3 best = Vector3.zero; float bestDist = float.MaxValue; bool found = false;
        int total = _grid * _grid, taps = 0;

        for (int dx = -_radius; dx <= _radius; dx++)
        for (int dy = -_radius; dy <= _radius; dy++)
        for (int dz = -_radius; dz <= _radius; dz++)
        {
            var c = new Vector3Int(mine.x + dx, mine.y + dy, mine.z + dz);
            int idx = Hash(c.x, c.y, c.z) % total; if (idx < 0) idx += total;
            taps++;
            int gx = idx % _grid, gy = idx / _grid;
            // Cell centre in clip space, then to pixels.
            int px, up2, dn2;
            PixelOf(gx, gy, tex, out px, out up2, out dn2);
            // Snapped mode counts rows from the top already, so there is
            // nothing to flip. The toggle only applies to clip-space mode.
            int py = _snap ? up2 : (_flipY ? dn2 : up2);
            Color got = shot.GetPixel(px, py);
            // Alpha alone cannot be trusted: an empty cell holds whatever the
            // screen had there, and the screen is opaque. Require the flag
            // AND a payload that is not obviously scene colour.
            if (got.a < 0.5f) continue;
            Vector3 at = ((Vector3)c + new Vector3(got.r, got.g, got.b)) * _cellSize;
            float d = Vector3.Distance(at, me);
            if (d > _cellSize * (_radius + 1) * 2f) continue;   // a collision from far away
            hits.Add($"  cell {c}  ->  {at.ToString("F4")}   {d:F3} m away");
            if (d < bestDist) { bestDist = d; best = at; found = true; }
        }
        DestroyImmediate(shot);

        // What is actually nearest, so the decode can be scored against it.
        Transform nearest = null; float nd = float.MaxValue;
        foreach (Transform t in root.transform)
        {
            if (!t.name.StartsWith("Socket")) continue;
            float d = Vector3.Distance(t.position, me);
            if (d < nd) { nd = d; nearest = t; }
        }

        _result = "plug at    " + me.ToString("F4") + "   cell " + mine
                + "\ntaps       " + taps + " (radius " + _radius + ")"
                + "\nfound      " + (found ? best.ToString("F4") : "NOTHING")
                + (nearest != null
                    ? "\nnearest    " + nearest.name + " at " + nearest.position.ToString("F4")
                      + (found ? "\nerror      " + (Vector3.Distance(best, nearest.position) * 1000f).ToString("F2") + " mm" : "")
                    : "")
                + (hits.Count > 0 ? "\n" + string.Join("\n", hits) : "");
    }

    // Scans the whole grab for the two socket patches and says which
    // orientation finds them. Guessing at a flip is how the first run wasted
    // twenty-seven reads on the floor.
    void Locate()
    {
        var tex = Shader.GetGlobalTexture("_YAPS_SpikeAtlas") as RenderTexture;
        if (tex == null) { _result = "No atlas yet. Press Play with the plug on screen."; return; }
        var root = GameObject.Find(Root);
        if (root == null) { _result = "Build it first."; return; }

        var shot = new Texture2D(tex.width, tex.height, TextureFormat.RGBAFloat, false);
        var was = RenderTexture.active;
        RenderTexture.active = tex;
        shot.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        shot.Apply();
        RenderTexture.active = was;

        var lines = new List<string>();
        foreach (Transform t in root.transform)
        {
            if (!t.name.StartsWith("Socket")) continue;
            Vector3 scaled = t.position / Mathf.Max(_cellSize, 0.0001f);
            var c = new Vector3Int(Mathf.FloorToInt(scaled.x), Mathf.FloorToInt(scaled.y), Mathf.FloorToInt(scaled.z));
            int total = _grid * _grid;
            int idx = Hash(c.x, c.y, c.z) % total; if (idx < 0) idx += total;
            int gx = idx % _grid, gy = idx / _grid;
            int px, up, dn;
            PixelOf(gx, gy, tex, out px, out up, out dn);
            Vector3 want = new Vector3(scaled.x - c.x, scaled.y - c.y, scaled.z - c.z);
            Color a = shot.GetPixel(px, up), b = shot.GetPixel(px, dn);
            lines.Add(t.name + " cell " + c + " -> grid (" + gx + "," + gy + ")");
            lines.Add("    want      " + want.ToString("F4"));
            lines.Add("    y as-is   " + Fmt(a) + "   off by " + Off(a, want).ToString("F4"));
            lines.Add("    y flipped " + Fmt(b) + "   off by " + Off(b, want).ToString("F4"));
        }
        DestroyImmediate(shot);
        _result = "atlas " + tex.width + "x" + tex.height + " " + tex.format
                + System.Environment.NewLine + string.Join(System.Environment.NewLine, lines);
        Debug.Log("[SpikeCell] " + _result);
    }

    static string Fmt(Color c)
    {
        return "(" + c.r.ToString("F4") + ", " + c.g.ToString("F4") + ", " + c.b.ToString("F4") + ") a=" + c.a.ToString("F2");
    }

    static float Off(Color c, Vector3 want)
    {
        return Vector3.Distance(new Vector3(c.r, c.g, c.b), want);
    }

    // Drawing the atlas early and grabbing before the scene covers it is the
    // only way it can be invisible: any pixel it writes IS on screen, so the
    // scene has to paint over it afterwards. That works wherever opaque
    // geometry covers those pixels and fails against open sky, which is what
    // this is for finding out.
    void ApplyQueue()
    {
        Set("Clear", _queueBase);
        Set("SocketA", _queueBase + 1);
        Set("SocketB", _queueBase + 1);
        for (int i = 0; i < 40; i++) Set("SocketX" + i, _queueBase + 1);
        Set("Grabber", _queueBase + 2);
        Set("PlugMarker", _queueBase + 2);
        _queueNote = "clear " + Queue("Clear") + ", writers " + Queue("SocketA")
                   + ", grabber " + Queue("Grabber") + "  (asked for "
                   + _queueBase + "/" + (_queueBase + 1) + "/" + (_queueBase + 2) + ")";
        Debug.Log("[SpikeCell] queues now " + _queueNote);
    }

    // The same maths the shader does, so the readback hits the cell centre.
    void PixelOf(int gx, int gy, RenderTexture tex, out int px, out int up, out int dn)
    {
        if (_snap)
        {
            px = Mathf.Clamp(OriginPx + gx * _cellPx + _cellPx / 2, 0, tex.width - 1);
            int fromTop = OriginPx + gy * _cellPx + _cellPx / 2;
            dn = Mathf.Clamp(tex.height - 1 - fromTop, 0, tex.height - 1);
            up = Mathf.Clamp(fromTop, 0, tex.height - 1);
        }
        else
        {
            float clipX = _corner.x + gx * _cellPixels + _cellPixels * 0.5f;
            float clipY = _corner.y - gy * _cellPixels - _cellPixels * 0.5f;
            px = Mathf.Clamp(Mathf.RoundToInt((clipX * 0.5f + 0.5f) * tex.width), 0, tex.width - 1);
            up = Mathf.Clamp(Mathf.RoundToInt((clipY * 0.5f + 0.5f) * tex.height), 0, tex.height - 1);
            dn = Mathf.Clamp(Mathf.RoundToInt((1f - (clipY * 0.5f + 0.5f)) * tex.height), 0, tex.height - 1);
        }
    }

    static string Queue(string name)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/YapsSpike/Cell" + name + ".mat");
        return m == null ? "?" : m.renderQueue.ToString();
    }

    static void Set(string name, int queue)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/YapsSpike/Cell" + name + ".mat");
        if (m != null) { m.renderQueue = queue; EditorUtility.SetDirty(m); }
    }

    static void SetTaps(int n)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/YapsSpike/CellTaps.mat");
        if (m != null) m.SetFloat("_Taps", n);
    }

    void Remove()
    {
        var go = GameObject.Find(Root);
        if (go != null) Undo.DestroyObjectImmediate(go);
        _result = "";
    }
}
#endif
