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
using UnityEditor;
using UnityEngine;

public class SpikePlug : EditorWindow
{
    [MenuItem("Tools/YAPS/Screen atlas: plug and socket")]
    static void Open() { GetWindow<SpikePlug>("Atlas plug"); }

    const string Root = "YAPS Atlas Plug Spike";
    const string PlugName = "Plug";
    const string Dir = "Assets/YapsSpike";
    const int OriginPx = 8;

    float _cellSize = 0.5f;
    int _grid = 8;
    int _slotPx = 1;
    float _reach = 1.6f;
    float _length = 0.22f;
    float _radius = 0.022f;
    int _queueBase = 1000;
    int _debug = 0;
    int _forceRow = 0;
    string _result = "";

    void OnEnable() { EditorApplication.update += Tick; }
    void OnDisable() { EditorApplication.update -= Tick; }
    void Tick() { if (EditorApplication.isPlaying) Repaint(); }

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Build, press Play, then drag and ROTATE the sockets in the scene view. "
            + "Nothing connects the plug to a socket: the socket draws two pixels and the plug "
            + "reads the twenty-seven cells around itself. Grey means it found nothing.",
            MessageType.Info);

        _cellSize = EditorGUILayout.Slider("Cell size, metres", _cellSize, 0.1f, 2f);
        _grid = EditorGUILayout.IntSlider("Cells across", _grid, 2, 16);
        _slotPx = EditorGUILayout.IntSlider("Slot, pixels", _slotPx, 1, 8);
        EditorGUILayout.LabelField("  atlas",
            (OriginPx + _grid * 2 * _slotPx) + " x " + (OriginPx + _grid * _slotPx) + " px"
            + "   (a cell is two slots: position, then facing)");

        EditorGUILayout.Space();
        _length = EditorGUILayout.Slider("Plug length, metres", _length, 0.05f, 0.6f);
        _radius = EditorGUILayout.Slider("Plug radius, metres", _radius, 0.005f, 0.08f);
        _reach = EditorGUILayout.Slider("Reach, in plug lengths", _reach, 0.5f, 4f);

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

        // Placed in DIFFERENT cells on purpose. One cell holds one socket:
        // the second to draw simply overwrites the first, which is a real
        // limit of the scheme rather than a bug in the spike.
        Socket(root, sockShader, "Socket A", new Vector3(0.0f, 1.05f, 0.30f), Quaternion.Euler(0, 180, 0));
        Socket(root, sockShader, "Socket B", new Vector3(0.30f, 1.36f, 0.78f), Quaternion.Euler(0, 250, 0));

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

    void Socket(GameObject root, Shader shader, string name, Vector3 at, Quaternion rot)
    {
        // The writer draws in CLIP space and ignores this object's transform,
        // so its own mesh is never seen. It still needs a real MeshRenderer
        // with a real matrix, because the matrix is the payload.
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        go.transform.SetParent(root.transform, false);
        go.transform.position = at;
        go.transform.rotation = rot;
        DestroyImmediate(go.GetComponent<Collider>());
        var m = Asset(name.Replace(" ", ""), shader);
        m.renderQueue = _queueBase + 1;
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
        ring.GetComponent<MeshRenderer>().sharedMaterial = Colour("Mouth", new Color(0.35f, 0.6f, 0.85f));

        var stub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stub.name = "Axis";
        stub.transform.SetParent(go.transform, false);
        stub.transform.localRotation = Quaternion.Euler(90, 0, 0);
        stub.transform.localPosition = new Vector3(0, 0, 0.06f);
        stub.transform.localScale = new Vector3(0.02f, 0.06f, 0.02f);
        DestroyImmediate(stub.GetComponent<Collider>());
        stub.GetComponent<MeshRenderer>().sharedMaterial = Colour("Axis", new Color(0.9f, 0.75f, 0.25f));
    }

    // Pushes every slider onto the live materials, so the atlas layout can be
    // changed without rebuilding and losing where the sockets were dragged to.
    void Push()
    {
        foreach (string n in new[] { "SocketA", "SocketB" })
        {
            var m = Load(n);
            if (m == null) continue;
            m.SetFloat("_Grid", _grid);
            m.SetFloat("_SlotPx", _slotPx);
            m.SetFloat("_OriginPx", OriginPx);
            m.SetFloat("_CellSize", _cellSize);
        }
        var p = Load("Plug");
        if (p != null)
        {
            p.SetFloat("_Grid", _grid);
            p.SetFloat("_SlotPx", _slotPx);
            p.SetFloat("_OriginPx", OriginPx);
            p.SetFloat("_CellSize", _cellSize);
            p.SetFloat("_Reach", _reach);
            p.SetFloat("_Debug", _debug);
            p.SetFloat("_ForceRow", _forceRow);
        }
        var c = Load("Clear");
        if (c != null)
        {
            // Covers the whole snapped rect with room to spare. Clearing a
            // few unused pixels costs nothing; missing one leaves a cell
            // reading the opaque screen, which reads as occupied.
            c.SetFloat("_SpanPxX", OriginPx + _grid * 2 * _slotPx + 4);
            c.SetFloat("_SpanPxY", OriginPx + _grid * _slotPx + 4);
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
            Vector3 scaled = t.position / Mathf.Max(_cellSize, 0.0001f);
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
            Vector3 sc = t.position / Mathf.Max(_cellSize, 0.0001f);
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

        var cs = Shader.Find("YAPS/Spike Clear");
        lines.Add("clear shader  " + (cs == null ? "MISSING"
                  : (UnityEditor.ShaderUtil.ShaderHasError(cs) ? "FAILED TO COMPILE" : "compiles")));

        DestroyImmediate(shot);
        _result = string.Join(System.Environment.NewLine, lines);
        Debug.Log("[SpikePlug] " + _result);
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
}
#endif
