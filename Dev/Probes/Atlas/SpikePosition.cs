// Spike 2: does a MOVING world position survive the atlas?
//
// Written for Joe, in this project only. Not part of AvatarBridge.
//
//   Tools > YAPS > Screen atlas: position round trip
//
// Spike 1 proved a named GrabPass survives a ChilloutVR upload and returns
// what another object rendered. It round-tripped a constant, so it said
// nothing about carrying a real position. This drops a marker you can drag
// around, publishes its own world position from its own matrix, reads the
// grab back and reports the error in MILLIMETRES.
//
// The encoding here is deliberately the naive one, normalised across a fixed
// box around the world origin, because the point is to measure where it
// falls down. A half float carries about three decimal digits of relative
// precision, so the error should grow with distance from the origin. If it
// does, that is the argument for encoding an offset within a spatial-hash
// cell instead, where the payload is never more than half a cell wide.
using UnityEditor;
using UnityEngine;

public class SpikePosition : EditorWindow
{
    [MenuItem("Tools/YAPS/Screen atlas: position round trip")]
    static void Open() { GetWindow<SpikePosition>("Atlas position"); }

    const string Root = "YAPS Atlas Position Spike";
    const string MarkerName = "Marker (drag me)";
    float _extent = 8f;
    Vector2 _corner = new Vector2(-0.95f, -0.45f);
    float _size = 0.02f;
    string _result = "";

    // Created on first use: a ScriptableObject field initializer runs during
    // construction, where Unity forbids making native objects.
    static MaterialPropertyBlock _blockCache;
    static MaterialPropertyBlock Block
    {
        get { return _blockCache ?? (_blockCache = new MaterialPropertyBlock()); }
    }

    void OnEnable() { EditorApplication.update += Tick; }
    void OnDisable() { EditorApplication.update -= Tick; }
    void Tick() { if (EditorApplication.isPlaying) Repaint(); }

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Build, press Play, then drag the marker around the scene. The error is measured "
            + "against the marker's real position, so it should stay small near the world origin "
            + "and grow as you move away — that growth is the argument for encoding within a "
            + "spatial-hash cell rather than across the whole world.", MessageType.Info);

        _extent = EditorGUILayout.Slider("Box half-size, metres", _extent, 1f, 64f);
        _corner = EditorGUILayout.Vector2Field("Corner, clip space", _corner);
        _size = EditorGUILayout.Slider("Patch size", _size, 0.01f, 0.2f);

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
        var shader = Shader.Find("YAPS/Spike Position");
        var readerShader = Shader.Find("YAPS/Spike Reader");
        if (shader == null || readerShader == null)
        {
            _result = "Missing shader. Needs YAPS/Spike Position and YAPS/Spike Reader.";
            return;
        }

        var root = new GameObject(Root);
        Undo.RegisterCreatedObjectUndo(root, "YAPS atlas position spike");

        // The marker. An ORDINARY mesh, never skinned: Unity skins into world
        // space and hands a SkinnedMeshRenderer an identity matrix, so a
        // skinned marker could not publish where it is.
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = MarkerName;
        marker.transform.SetParent(root.transform, false);
        marker.transform.localScale = Vector3.one * 0.1f;
        marker.transform.position = new Vector3(0.3f, 1.2f, 0.4f);
        DestroyImmediate(marker.GetComponent<Collider>());
        var m = Asset("Position", shader);
        m.SetVector("_Corner", new Vector4(_corner.x, _corner.y, 0, 0));
        m.SetFloat("_Size", _size);
        m.SetFloat("_Extent", _extent);
        marker.GetComponent<MeshRenderer>().sharedMaterial = m;

        // Something has to carry the GrabPass, or nothing grabs.
        var reader = GameObject.CreatePrimitive(PrimitiveType.Quad);
        reader.name = "Grabber";
        reader.transform.SetParent(root.transform, false);
        DestroyImmediate(reader.GetComponent<Collider>());
        var r = Asset("PositionReader", readerShader);
        r.SetVector("_Corner", new Vector4(_corner.x, _corner.y, 0, 0));
        r.SetFloat("_Size", _size);
        reader.GetComponent<MeshRenderer>().sharedMaterial = r;

        Selection.activeGameObject = marker;
        _result = "Built. Press Play, then drag the marker in the scene view.";
    }

    // Saved as an asset, not made in the scene: a material created with
    // new Material() does not carry its shader into an upload, which cost
    // two uploads to learn on spike 1.
    static Material Asset(string name, Shader shader)
    {
        const string dir = "Assets/YapsSpike";
        string path = dir + "/Spike" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null || m.shader != shader)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        return m;
    }

    void Measure()
    {
        var root = GameObject.Find(Root);
        var marker = root != null ? root.transform.Find(MarkerName) : null;
        if (marker == null) { _result = "Build it first."; return; }

        var tex = Shader.GetGlobalTexture("_YAPS_SpikeAtlas") as RenderTexture;
        if (tex == null)
        {
            _result = "No atlas yet. The grabber has to be on screen for the GrabPass to run.";
            return;
        }

        float u = (_corner.x + _size * 0.5f) * 0.5f + 0.5f;
        float v = (_corner.y - _size * 0.5f) * 0.5f + 0.5f;
        int px = Mathf.Clamp(Mathf.RoundToInt(u * tex.width), 0, tex.width - 1);
        int py = Mathf.Clamp(Mathf.RoundToInt(v * tex.height), 0, tex.height - 1);

        var was = RenderTexture.active;
        RenderTexture.active = tex;
        var shot = new Texture2D(1, 1, TextureFormat.RGBAFloat, false);
        shot.ReadPixels(new Rect(px, py, 1, 1), 0, 0);
        shot.Apply();
        RenderTexture.active = was;
        Color got = shot.GetPixel(0, 0);
        DestroyImmediate(shot);

        Vector3 decoded = new Vector3(got.r - 0.5f, got.g - 0.5f, got.b - 0.5f) * (2f * _extent);
        Vector3 truth = marker.position;
        float err = Vector3.Distance(decoded, truth);

        _result = "marker at   " + truth.ToString("F4")
                + "\ndecoded to  " + decoded.ToString("F4")
                + "\nerror       " + (err * 1000f).ToString("F2") + " mm"
                + "   (" + truth.magnitude.ToString("F1") + " m from the origin)"
                + "\nbox         " + (_extent * 2f).ToString("F0") + " m across, "
                + tex.format;
    }

    void Remove()
    {
        var go = GameObject.Find(Root);
        if (go != null) Undo.DestroyObjectImmediate(go);
        _result = "";
    }
}
