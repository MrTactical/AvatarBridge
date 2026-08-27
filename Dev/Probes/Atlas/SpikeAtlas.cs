// Does a screen-space atlas work, and how much precision survives it?
//
// Written for Joe, in this project only. Not part of AvatarBridge.
//
//   Tools > YAPS > Screen atlas spike
//
// Builds two quads: one stamps a known value into a fixed corner of the
// screen, the other grabs the screen and samples that corner back. If the
// number comes back, the mechanism works and a socket could publish its
// world position this way: no light slots, no contact pairs, no sync bits,
// and nothing a viewer can switch off short of blocking the shader.
//
// Play Mode proves OUR half. Whether ChilloutVR's asset filter keeps a
// GrabPass through an upload is a separate question and only an upload can
// answer it.
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public class SpikeAtlas : EditorWindow
{
    [MenuItem("Tools/YAPS/Screen atlas spike")]
    static void Open() { GetWindow<SpikeAtlas>("Atlas spike"); }

    const string Root = "YAPS Atlas Spike";
    Color _value = new Color(0.25f, 0.50f, 0.75f, 1f);
    // Above the mic icon rather than the top corner, and small. A VR
    // headset's field of view is far narrower than the desktop window, so
    // the top-left of the render target is off the edge of what anyone
    // actually sees.
    Vector2 _corner = new Vector2(-0.95f, -0.45f);
    float _size = 0.02f;
    string _result = "";

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Build, then press Play. The left patch is what was stamped, the right is what came "
            + "back through the GrabPass. Same colour means the round trip works.", MessageType.Info);

        _value = EditorGUILayout.ColorField("Value to stamp", _value);
        _corner = EditorGUILayout.Vector2Field("Corner, clip space", _corner);
        _size = EditorGUILayout.Slider("Patch size", _size, 0.01f, 0.3f);

        if (GUILayout.Button("Build the spike")) Build();
        if (GUILayout.Button("Read what came back")) Read();
        if (GUILayout.Button("Attach it to the avatar, for an upload test")) Attach();
        if (GUILayout.Button("Remove it")) Remove();

        if (!string.IsNullOrEmpty(_result)) EditorGUILayout.HelpBox(_result, MessageType.None);
    }

    void Build()
    {
        Remove();
        var root = new GameObject(Root);
        Undo.RegisterCreatedObjectUndo(root, "YAPS atlas spike");
        Make(root, "Writer", "YAPS/Spike Writer");
        Make(root, "Reader", "YAPS/Spike Reader");
        Make(root, "Control", "YAPS/Spike Control");
        Selection.activeGameObject = root;
        _result = "Built. Press Play, then Read.";
    }

    void Make(GameObject root, string name, string shaderName)
    {
        var shader = Shader.Find(shaderName);
        if (shader == null) { _result = "MISSING shader: " + shaderName; return; }
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        go.transform.SetParent(root.transform, false);
        DestroyImmediate(go.GetComponent<Collider>());
        // SAVED AS AN ASSET, not made in the scene. A material created with
        // new Material() lives inside the scene object, and the shader it
        // points at is then a dependency of a scene object rather than of an
        // asset. The GameObjects reached the upload and the shader did not,
        // which is exactly what magenta means: Unity's error shader standing
        // in for one that is not in the bundle.
        const string dir = "Assets/YapsSpike";
        string path = dir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null || m.shader != shader)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetVector("_Corner", new Vector4(_corner.x, _corner.y, 0, 0));
        m.SetFloat("_Size", _size);
        if (m.HasProperty("_SpikeValue"))
        {
            // The control is deliberately a different colour. Set to the same
            // value as the writer it proved nothing: three identical patches
            // cannot show which one is which.
            m.SetVector("_SpikeValue",
                name == "Control" ? new Vector4(1f, 0.5f, 0f, 1f) : (Vector4) _value);
        }
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
    }

    // The named GrabPass publishes a global texture. Reading it back turns
    // "it looks right" into a number, which is the only way to see how much
    // precision a channel actually carries.
    void Read()
    {
        var tex = Shader.GetGlobalTexture("_YAPS_SpikeAtlas");
        if (tex == null)
        {
            _result = "No _YAPS_SpikeAtlas global texture. Either the GrabPass has not run yet "
                      + "(press Play and make sure both quads are on screen) or it was stripped.";
            return;
        }
        var rt = tex as RenderTexture;
        if (rt == null) { _result = "The atlas exists but is a " + tex.GetType().Name + ", not a RenderTexture."; return; }

        // Middle of the writer's patch, clip space to pixels.
        float u = (_corner.x + _size * 0.5f) * 0.5f + 0.5f;
        float v = (_corner.y - _size * 0.5f) * 0.5f + 0.5f;
        int px = Mathf.Clamp(Mathf.RoundToInt(u * rt.width), 0, rt.width - 1);
        int py = Mathf.Clamp(Mathf.RoundToInt(v * rt.height), 0, rt.height - 1);

        var was = RenderTexture.active;
        RenderTexture.active = rt;
        var shot = new Texture2D(1, 1, TextureFormat.RGBAFloat, false);
        shot.ReadPixels(new Rect(px, py, 1, 1), 0, 0);
        shot.Apply();
        RenderTexture.active = was;
        Color got = shot.GetPixel(0, 0);
        DestroyImmediate(shot);

        float err = Mathf.Max(Mathf.Abs(got.r - _value.r),
                    Mathf.Max(Mathf.Abs(got.g - _value.g), Mathf.Abs(got.b - _value.b)));
        _result = "atlas " + rt.width + "x" + rt.height + " " + rt.format
                  + "\nstamped  " + _value.r.ToString("F4") + ", " + _value.g.ToString("F4") + ", " + _value.b.ToString("F4")
                  + "\ncame back " + got.r.ToString("F4") + ", " + got.g.ToString("F4") + ", " + got.b.ToString("F4")
                  + "\nworst error " + err.ToString("F5")
                  + (err < 0.005f ? "   ROUND TRIP WORKS" : "   does not match, read the pixel maths");
        Debug.Log("[AtlasSpike] " + _result);
    }

    // The only question Play Mode cannot answer is whether ChilloutVR keeps
    // a GrabPass through an upload. Parent the spike under the avatar and it
    // ships with it: if the two patches still match in game, the transport
    // is real.
    //
    // The quads are scaled up enormously on purpose. The vertex shader
    // ignores their position entirely, so scale changes nothing about what is
    // drawn, but a renderer whose BOUNDS are huge is never frustum culled.
    // Otherwise the patch vanishes the moment the avatar leaves the view,
    // which is exactly when a socket most needs to be publishing.
    void Attach()
    {
        var root = GameObject.Find(Root);
        if (root == null) { _result = "Build it first."; return; }
        var avatar = Object.FindObjectOfType<ABI.CCK.Components.CVRAvatar>();
        if (avatar == null) { _result = "No CVRAvatar in the scene."; return; }

        Undo.SetTransformParent(root.transform, avatar.transform, "Attach atlas spike");
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root.transform) continue;
            // Big enough that frustum culling never drops it, small enough
            // that a FAILED shader is a patch rather than a wall: the error
            // shader ignores our vertex maths and draws the real geometry,
            // and at 100 units that filled the whole view with magenta.
            t.localScale = Vector3.one * 2f;
        }
        // OFF by default, behind a menu toggle. Two bright patches welded to
        // the corner of everyone's view is not something to ship by accident
        // on an avatar that gets worn.
        root.SetActive(false);
        string entry = MenuToggle(avatar, root);

        _result = "Attached under " + avatar.name + ", scaled so nothing culls it, and switched OFF "
                  + "behind a menu toggle called " + entry + ". "
                  + "Upload and turn the toggle on. Three patches, left to right: what was stamped, "
                  + "the VERDICT, and an orange control that contains no GrabPass. Green verdict "
                  + "means the value came back through the grab in game. Red means it did not. "
                  + "Magenta anywhere means that shader did not ship.";
        Debug.Log("[AtlasSpike] " + _result);
    }

    // The entry, the parameter and the layer, all written here.
    //
    // The CCK only generates a parameter and a layer for a settings entry
    // when someone presses Create Animator, and that regenerates the whole
    // controller from the base one, which throws away everything YAPS wrote
    // into it. Writing the three pieces directly avoids the regeneration and
    // the rebake that would have to follow it.
    static string MenuToggle(ABI.CCK.Components.CVRAvatar avatar, GameObject target)
    {
        const string Name = "Atlas Spike";
        if (avatar.avatarSettings == null) return "(no Advanced Settings on this avatar)";

        // The menu entry, with NO GameObject target: the layer below does the
        // switching, so there is nothing for the CCK to generate.
        var settings = avatar.avatarSettings.settings;
        settings.RemoveAll(e => e != null && e.machineName == Name);
        var entry = new ABI.CCK.Scripts.CVRAdvancedSettingsEntry
        {
            name = Name,
            machineName = Name,
            type = ABI.CCK.Scripts.CVRAdvancedSettingsEntry.SettingsType.Toggle,
        };
        entry.toggleSettings = new ABI.CCK.Scripts.CVRAdvancesAvatarSettingGameObjectToggle
        {
            defaultValue = false,
        };
        settings.Add(entry);
        EditorUtility.SetDirty(avatar);

        // The controller ChilloutVR uploads, not the Animator's own slot.
        RuntimeAnimatorController shipped =
            avatar.overrides != null ? avatar.overrides.runtimeAnimatorController : null;
        if (shipped == null) shipped = avatar.avatarSettings.baseController;
        var ac = shipped as AnimatorController;
        if (ac == null) return Name + " (no AnimatorController to write into)";

        string path = AnimationUtility.CalculateTransformPath(target.transform, avatar.transform);

        // Both directions as explicit clips. ChilloutVR does not restore
        // Write Defaults, so a state that says nothing leaves the object
        // wherever the last thing to touch it left it.
        var on = Clip(ac, Name + " on", path, 1f);
        var off = Clip(ac, Name + " off", path, 0f);

        if (!ac.parameters.Any(q => q.name == Name))
            ac.AddParameter(Name, AnimatorControllerParameterType.Bool);

        // Replace rather than stack, so re-running this is safe.
        foreach (var old in ac.layers.Where(l => l.name == Name).ToList())
            ac.RemoveLayer(System.Array.FindIndex(ac.layers, l => l.name == old.name));

        ac.AddLayer(Name);
        var layers = ac.layers;
        var layer = layers[layers.Length - 1];
        layer.defaultWeight = 1f;
        ac.layers = layers;

        // AddLayer embeds the state machine in the asset for us. Building one
        // by hand and assigning it serializes as a layer with no states.
        var sm = layer.stateMachine;
        var sOff = sm.AddState("Off");
        sOff.motion = off; sOff.writeDefaultValues = false;
        var sOn = sm.AddState("On");
        sOn.motion = on; sOn.writeDefaultValues = false;
        sm.defaultState = sOff;

        var toOn = sOff.AddTransition(sOn);
        toOn.hasExitTime = false; toOn.duration = 0f;
        toOn.AddCondition(AnimatorConditionMode.If, 0f, Name);
        var toOff = sOn.AddTransition(sOff);
        toOff.hasExitTime = false; toOff.duration = 0f;
        toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, Name);

        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        Debug.Log("[AtlasSpike] wired " + Name + " into " + AssetDatabase.GetAssetPath(ac)
                  + " as a bool parameter and a two state layer on " + path);
        return Name;
    }

    // A one frame constant, stored inside the controller so there are no
    // stray .anim files to lose.
    static AnimationClip Clip(AnimatorController ac, string name, string path, float value)
    {
        var clip = new AnimationClip { name = name };
        var binding = new EditorCurveBinding { path = path, type = typeof(GameObject), propertyName = "m_IsActive" };
        AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f / 60f, value));
        AssetDatabase.AddObjectToAsset(clip, ac);
        return clip;
    }

    void Remove()
    {
        var go = GameObject.Find(Root);
        if (go != null) Undo.DestroyObjectImmediate(go);
        _result = "";
    }
}
