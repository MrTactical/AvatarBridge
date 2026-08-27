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
    string _result = "";

    static MaterialPropertyBlock _blockCache;

    void OnEnable() { EditorApplication.update += Tick; }
    void OnDisable() { EditorApplication.update -= Tick; }
    void Tick() { if (EditorApplication.isPlaying) Repaint(); }

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
        _cellPixels = EditorGUILayout.Slider("Cell on screen", _cellPixels, 0.01f, 0.06f);
        _radius = EditorGUILayout.IntSlider("Neighbour radius", _radius, 0, 2);
        // ReadPixels has y=0 at the BOTTOM; clip space +1 is the top. The
        // reader shader compensates through _TexelSize.y and this did not,
        // so the first run sampled the floor and every cell read as occupied
        // because the screen is opaque.
        _flipY = EditorGUILayout.ToggleLeft("Flip Y when reading back", _flipY);
        if (GUILayout.Button("Find the patches (which orientation?)")) Locate();
        EditorGUILayout.LabelField("  taps per read", ((2 * _radius + 1) * (2 * _radius + 1) * (2 * _radius + 1)).ToString());

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

        // Something must carry the GrabPass or nothing grabs. The reader
        // material already has one.
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
            float clipX = _corner.x + gx * _cellPixels + _cellPixels * 0.5f;
            float clipY = _corner.y - gy * _cellPixels - _cellPixels * 0.5f;
            int px = Mathf.Clamp(Mathf.RoundToInt((clipX * 0.5f + 0.5f) * tex.width), 0, tex.width - 1);
            float ny = clipY * 0.5f + 0.5f;
            if (_flipY) ny = 1f - ny;
            int py = Mathf.Clamp(Mathf.RoundToInt(ny * tex.height), 0, tex.height - 1);
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
            float clipX = _corner.x + gx * _cellPixels + _cellPixels * 0.5f;
            float clipY = _corner.y - gy * _cellPixels - _cellPixels * 0.5f;
            int px = Mathf.Clamp(Mathf.RoundToInt((clipX * 0.5f + 0.5f) * tex.width), 0, tex.width - 1);
            int up = Mathf.Clamp(Mathf.RoundToInt((clipY * 0.5f + 0.5f) * tex.height), 0, tex.height - 1);
            int dn = Mathf.Clamp(Mathf.RoundToInt((1f - (clipY * 0.5f + 0.5f)) * tex.height), 0, tex.height - 1);
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

    void Remove()
    {
        var go = GameObject.Find(Root);
        if (go != null) Undo.DestroyObjectImmediate(go);
        _result = "";
    }
}
#endif
